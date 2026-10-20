using System;
using System.Collections.Concurrent;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using System.Web.Mvc;
using System.Web.Script.Serialization;
using System.Web.SessionState;
using NexusFreelance.Web.Services;

namespace NexusFreelance.Web.Controllers
{
    // Streaming must never hold ASP.NET's exclusive per-session lock.
    [SessionState(SessionStateBehavior.Disabled)]
    public sealed class RealtimeController : BaseController
    {
        private static readonly ConcurrentDictionary<int, int> Streams = new ConcurrentDictionary<int, int>();
        private static readonly object StreamGate = new object();
        private static int totalStreams;
        private ActionResult Error(int status)
        {
            Response.StatusCode = status; Response.TrySkipIisCustomErrors = true; Response.SuppressFormsAuthenticationRedirect = true;
            Response.Cache.SetNoStore(); return Json(new { error = "Realtime unavailable", status }, JsonRequestBehavior.AllowGet);
        }
        protected override void OnActionExecuting(ActionExecutingContext context)
        {
            base.OnActionExecuting(context);
            Response.Cache.SetNoStore(); Response.SuppressFormsAuthenticationRedirect = true;
            if (context.Result != null && Response.StatusCode >= 400) context.Result = Error(Response.StatusCode);
        }
        [HttpGet]
        public ActionResult State(int? conversationId, int? workspaceId)
        {
            try { using (var reader = new RealtimeService(Request, conversationId, workspaceId))
                return Json(reader.State(), JsonRequestBehavior.AllowGet); }
            catch (RealtimeAccessException ex) { return Error(ex.Status); }
        }
        [HttpGet]
        public ActionResult Messages(int after = 0, int? conversationId = null, int? workspaceId = null)
        {
            try { using (var reader = new RealtimeService(Request, conversationId, workspaceId))
                return Json(reader.Messages(after), JsonRequestBehavior.AllowGet); }
            catch (RealtimeAccessException ex) { return Error(ex.Status); }
        }
        [HttpGet]
        public async Task<ActionResult> Stream(int? conversationId, int? workspaceId)
        {
            // Authenticate/check scope before writing any stream bytes.
            int userId;
            try { using (var reader = new RealtimeService(Request, conversationId, workspaceId)) { reader.State(); userId = AuthSessionService.AccountId(Request).Value; } }
            catch (RealtimeAccessException ex) { return Error(ex.Status); }
            lock (StreamGate)
            {
                int count; Streams.TryGetValue(userId, out count);
                if (count >= 4 || totalStreams >= 64) { Response.AppendHeader("Retry-After", "15"); return Error(429); }
                Streams[userId] = count + 1; totalStreams++;
            }
            var serializer = new JavaScriptSerializer(); string previous = null;
            try
            {
                Response.ContentType = "text/event-stream"; Response.ContentEncoding = Encoding.UTF8;
                Response.BufferOutput = false; Response.Cache.SetNoStore();
                Response.AppendHeader("X-Accel-Buffering", "no"); Response.AppendHeader("Content-Encoding", "identity");
                Response.AppendHeader("X-Content-Type-Options", "nosniff");
                var until = DateTime.UtcNow.AddSeconds(24);
                var authorized = true;
                while (DateTime.UtcNow < until && Response.IsClientConnected)
                {
                    try
                    {
                        string current;
                        // A fresh context revalidates suspension, password revision and membership each tick.
                        using (var reader = new RealtimeService(Request, conversationId, workspaceId)) current = serializer.Serialize(reader.State());
                        if (current != previous) { Response.Write("event: state\ndata: " + current + "\n\n"); previous = current; }
                        else Response.Write(": heartbeat\n\n");
                    }
                    catch (RealtimeAccessException ex) { authorized = false; Response.Write("event: access\ndata: {\"status\":" + ex.Status + "}\n\n"); Response.Flush(); break; }
                    Response.Flush();
                    await Task.Delay(2000, Response.ClientDisconnectedToken);
                }
                if (authorized && Response.IsClientConnected) { Response.Write("event: rotate\ndata: {}\n\n"); Response.Flush(); }
            }
            catch (OperationCanceledException) { }
            catch (HttpException) { }
            catch (System.IO.IOException) { }
            catch (Exception ex) { System.Diagnostics.Trace.TraceWarning("Realtime stream interrupted: {0}", ex.GetType().Name); }
            finally
            {
                lock (StreamGate) { int count; if (Streams.TryGetValue(userId, out count) && count > 1) Streams[userId] = count - 1; else { int ignored; Streams.TryRemove(userId, out ignored); } totalStreams--; }
            }
            return new EmptyResult();
        }
    }
}
