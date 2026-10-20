using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Web.Script.Serialization;
using NexusFreelance.Web.Models;
using NexusFreelance.Web.Services;

// Match the MVC5 host's .NET 4.8 URI escaping semantics rather than CLR 4.0 quirks.
[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8")]

// Offline boundary tests: no database, IIS, real credentials, or provider transport.
// Vectors generated independently with Python hashlib/hmac + urllib.parse.quote_plus
// (safe='', then '~' -> '%7E'). Never generate expected signatures using VnPayCodec.
// API field order: https://sandbox.vnpayment.vn/apis/docs/truy-van-hoan-tien/querydr&refund.html
// querydr response has 15 fields (including absent promotion slots); refund has 13.
public static class TestVnPayGateway
{
    private const string Secret = "local-only-vnpay-vector-secret-2026";
    private const string Canonical = "vnp_Amount=1234500&vnp_OrderInfo=Thanh+to%C3%A1n+%2B+%7E%21%2A%27%28%29+%26%2F%3D%3F&vnp_TxnRef=TXN001";
    private const string CanonicalHash = "f996c26494161b510ecbe08162a0ad5883a9b2508fe44544414e9d5109ecde0d20f576f10013b36591994c49d662fa6fa96c2bdc51f2765497e4078d8609a653";
    private const string CheckoutQuery = "vnp_Amount=1234500&vnp_Command=pay&vnp_CreateDate=20261016093000&vnp_CurrCode=VND&vnp_ExpireDate=20261016100000&vnp_IpAddr=127.0.0.1&vnp_Locale=vn&vnp_OrderInfo=Bao+dam+hop+dong+7+moc+9&vnp_OrderType=other&vnp_ReturnUrl=https%3A%2F%2Fmerchant.example.test%2FPayments%2FReturn&vnp_TmnCode=TEST0001&vnp_TxnRef=TXN001&vnp_Version=2.1.0";
    private const string CheckoutHash = "7068101bc9ca3c31c4a51f8a165ba0e61baf719ed7d98284b60dc3f42f26a664c0626e2228d568703d983c9a033ff350eaa6c2253ae6edf6caa307f1437800de";
    private const string QueryHash = "fc4b316534e30fb0a8bcb113597bde9ed886994dc3f67e60aac4454f1da49cfa1fbc7274264f550182279cfe474115b6c9e0aebb271e862a2b0646e36587fc3d";
    private const string PromotionHash = "8e869e70a43694f4c8802ac0a33283c29a9aef9cc46369bb18599b75655726cc150657c647cfa9e62313636d121f1f29fe31e8c79a70f9a3f2dfa07ec0f37bdd";
    private const string FullHash = "6bbc730260d256a2b3c12415a9729cafc9a675d2481cf0e85a51e8037bfbe09f60809ac9f45ec0a612761f7742810d2891c59a0a7660a01d89b21e9974b7e246";
    private const string PartialHash = "a6a7f7adee0a964085c68c455452564f67e39d966765c1692db7833968f4abcd354cfc3bfb766e2300fae66f80e8f0ab94ce67c710d9663dee37ddaba153c09c";
    private static VnPaySettings settings;
    private static void Check(bool condition, string label)
    { if (!condition) throw new Exception(label); }
    private static void Reject(Action action, string label)
    {
        try { action(); } catch (InvalidOperationException) { return; }
        throw new Exception(label);
    }
    private static T Bounded<T>(Task<T> task)
    {
        Check(Task.WhenAny(task, Task.Delay(2500)).GetAwaiter().GetResult() == task,
            "gateway operation exceeded 2.5 second offline test bound");
        return task.GetAwaiter().GetResult();
    }
    private static string IndependentHmac(string text)
    {
        using (var hmac = new HMACSHA512(Encoding.UTF8.GetBytes(Secret)))
            return BitConverter.ToString(hmac.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "").ToLowerInvariant();
    }
    private static PaymentIntent Payment()
    {
        return new PaymentIntent { Id=1, ContractId=7, EscrowMilestoneId=9, Amount=12345m,
            TxnRef="TXN001", MerchantCode="TEST0001", Environment="sandbox", Currency="VND",
            TransactionDate="20261016093000", ProviderTransactionNo="9988", Status="Paid",
            ExpiresAtUtc=new DateTime(2026,10,16,3,0,0,DateTimeKind.Utc) };
    }
    private static PspRefund Refund(bool partial)
    {
        return new PspRefund { Id=2, PaymentIntentId=1, Payment=Payment(), RequestId="RF001",
            Amount=partial ? 2345m : 12345m, RequestedById=42, Status="Unknown", Reason="Offline test" };
    }
    private static Dictionary<string,string> Response(string command, bool partial, bool promotion)
    {
        var fields = new Dictionary<string,string> {
            {"vnp_ResponseId","RESP001"},{"vnp_Command",command},{"vnp_ResponseCode","00"},
            {"vnp_Message","OK"},{"vnp_TmnCode","TEST0001"},{"vnp_TxnRef","TXN001"},
            {"vnp_Amount",partial ? "234500" : "1234500"},{"vnp_BankCode","NCB"},
            {"vnp_PayDate","20261016093500"},{"vnp_TransactionNo","9988"},
            {"vnp_TransactionType",command=="querydr" ? "01" : partial ? "03" : "02"},
            {"vnp_TransactionStatus","00"},{"vnp_OrderInfo","Order 7"},
            {"vnp_SecureHash",command=="querydr" ? (promotion ? PromotionHash : QueryHash) : (partial ? PartialHash : FullHash)} };
        if (promotion) { fields.Add("vnp_PromotionCode","PROMO7"); fields.Add("vnp_PromotionAmount","10000"); }
        return fields;
    }
    private static void CodecVectors()
    {
        var fields = new Dictionary<string,string> { {"vnp_TxnRef","TXN001"},
            {"vnp_OrderInfo","Thanh toán + ~!*'() &/=?"},{"vnp_Amount","1234500"},
            {"vnp_Empty",""},{"vnp_Null",null},{"vnp_SecureHash","ignored"},{"vnp_SecureHashType","SHA512"} };
        Check(IndependentHmac(Canonical)==CanonicalHash,"independent HMAC oracle matches literal vector");
        Check(VnPayCodec.Canonical(fields)==Canonical,"RFC1738 UTF8, punctuation, ordinal sorting and empty exclusions");
        Check(VnPayCodec.Hmac(Secret,Canonical)==CanonicalHash,"canonical HMAC literal vector");
        Check(VnPayCodec.Checkout(Payment(),settings,"127.0.0.1")==
            "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html?"+CheckoutQuery+"&vnp_SecureHash="+CheckoutHash,
            "checkout exact independently signed URL and VND scaling");
        var parsed=VnPayCodec.ParseQuery(HttpUtility.ParseQueryString(Canonical+"&vnp_SecureHash="+CanonicalHash+"&ignored=value"));
        Check(!parsed.ContainsKey("ignored") && parsed["vnp_OrderInfo"]==fields["vnp_OrderInfo"],"query decoded exactly once");
        Check(VnPayCodec.VerifyQuery(parsed,settings),"literal signed query verifies");
        parsed["vnp_SecureHash"]=CanonicalHash.ToUpperInvariant();
        Check(VnPayCodec.VerifyQuery(parsed,settings),"uppercase hex signature verifies");
        parsed["vnp_Amount"]="1234501";
        Check(!VnPayCodec.VerifyQuery(parsed,settings),"amount tamper rejected");
        foreach (var signature in new[] { "", CanonicalHash.Substring(1), new string('z',128), new string('0',128) })
            Check(!VnPayCodec.EqualSignature(CanonicalHash,signature),"malformed or incorrect signature rejected");
        foreach (var query in new[] { "vnp_Amount=1&vnp_Amount=2", "vnp_OrderInfo=a%0Ab", "vnp_bad-key=x", "vnp_OrderInfo="+new string('x',601) })
            Reject(delegate { VnPayCodec.ParseQuery(HttpUtility.ParseQueryString(query)); },"unsafe query accepted");
    }
    private static void ApiVectors()
    {
        foreach (var fields in new[] { Response("querydr",false,false), Response("querydr",false,true), Response("refund",false,false), Response("refund",true,false) })
        {
            Check(VnPayCodec.VerifyApiResponse(fields,settings),"command-specific literal API signature");
            foreach (var key in fields.Keys.Where(k=>k!="vnp_SecureHash").ToArray())
            {
                var changed=new Dictionary<string,string>(fields); changed[key]+="x";
                Check(!VnPayCodec.VerifyApiResponse(changed,settings),"signed API field tamper rejected: "+key);
            }
        }
        var legacy=Response("querydr",false,false);
        legacy["vnp_SecureHash"]=IndependentHmac("RESP001|querydr|00|OK|TEST0001|TXN001|1234500|NCB|20261016093500|9988|01|00|Order 7");
        Check(!VnPayCodec.VerifyApiResponse(legacy,settings),"querydr missing trailing promotion slots rejected");
        var refund=Response("refund",false,false);
        refund["vnp_SecureHash"]=IndependentHmac("RESP001|refund|00|OK|TEST0001|TXN001|1234500|NCB|20261016093500|9988|02|00|Order 7||");
        Check(!VnPayCodec.VerifyApiResponse(refund,settings),"refund must not use querydr promotion slots");
    }
    private static void VerifyRequest(HttpRequestMessage request, Dictionary<string,string> fields, PspRefund refund)
    {
        Check(request.Method==HttpMethod.Post && request.RequestUri.AbsoluteUri==
            "https://sandbox.vnpayment.vn/merchant_webapi/api/transaction","sandbox JSON POST destination");
        Check(request.Content.Headers.ContentType.MediaType=="application/json","JSON request content type");
        Check(fields.Count==(refund==null ? 11 : 14),"complete request field set");
        Check(fields["vnp_Version"]=="2.1.0" && fields["vnp_Command"]==(refund==null ? "querydr" : "refund"),"version and command");
        Check(fields["vnp_TmnCode"]=="TEST0001" && fields["vnp_TxnRef"]=="TXN001" &&
            fields["vnp_TransactionDate"]=="20261016093000" && fields["vnp_TransactionNo"]=="9988","original transaction identity");
        DateTime created;
        Check(DateTime.TryParseExact(fields["vnp_CreateDate"],"yyyyMMddHHmmss",CultureInfo.InvariantCulture,
            DateTimeStyles.None,out created) && Math.Abs((created-DateTime.UtcNow.AddHours(7)).TotalSeconds)<10,"request Vietnam timestamp");
        Check(fields["vnp_IpAddr"]=="127.0.0.1" && fields["vnp_OrderInfo"].Length>0,"request IP and order info");
        Check(refund==null ? Regex.IsMatch(fields["vnp_RequestId"],"^[A-Za-z0-9]{1,32}$") : fields["vnp_RequestId"]=="RF001","request identity");
        string data;
        if (refund==null)
            data=string.Join("|",new[] { fields["vnp_RequestId"],"2.1.0","querydr","TEST0001","TXN001",
                "20261016093000",fields["vnp_CreateDate"],"127.0.0.1",fields["vnp_OrderInfo"] });
        else
        {
            var partial=refund.Amount<refund.Payment.Amount;
            Check(fields["vnp_TransactionType"]==(partial ? "03" : "02") &&
                fields["vnp_Amount"]==(partial ? "234500" : "1234500") && fields["vnp_CreateBy"]=="admin42","full/partial refund amount and initiator");
            data=string.Join("|",new[] { "RF001","2.1.0","refund","TEST0001",partial ? "03" : "02","TXN001",
                partial ? "234500" : "1234500","9988","20261016093000","admin42",fields["vnp_CreateDate"],"127.0.0.1",fields["vnp_OrderInfo"] });
        }
        Check(fields["vnp_SecureHash"]==IndependentHmac(data),"request HMAC official field order");
    }
    private sealed class FakeTransport : HttpMessageHandler
    {
        public Func<HttpRequestMessage,HttpResponseMessage> Reply;
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { Calls++; return Task.FromResult(Reply(request)); }
    }
    private static void GatewayRoundTrip(string command, bool partial, bool promotion, bool tamper)
    {
        var refund=command=="refund" ? Refund(partial) : null;
        using (var transport=new FakeTransport())
        {
            transport.Reply=delegate(HttpRequestMessage request) {
                var sent=new JavaScriptSerializer().Deserialize<Dictionary<string,string>>(Bounded(request.Content.ReadAsStringAsync()));
                VerifyRequest(request,sent,refund);
                var fields=Response(command,partial,promotion);
                if (tamper) fields["vnp_Amount"]="1";
                return new HttpResponseMessage(HttpStatusCode.OK) { Content=new StringContent(new JavaScriptSerializer().Serialize(fields),Encoding.UTF8,"application/json") };
            };
            var gateway=new VnPayGateway(settings,transport);
            if (tamper) Reject(delegate { Bounded(gateway.Send(Payment(),refund,"127.0.0.1")); },"gateway returned tampered response");
            else Check(Bounded(gateway.Send(Payment(),refund,"127.0.0.1"))["vnp_SecureHash"]==Response(command,partial,promotion)["vnp_SecureHash"],"authenticated API response returned unchanged");
            Check(transport.Calls==1,"fake transport handled exactly one call");
        }
    }
    // A deterministic stalled-body boundary probe; this does NOT claim to measure the
    // production 15s deadline. It requires a cancellable token, then simulates an I/O
    // cancellation after 100ms and checks that the exception reaches the caller.
    private sealed class CancellationProbe : MemoryStream
    {
        public bool ReadStarted;
        public bool ReceivedCancellableToken;
        private readonly TaskCompletionSource<bool> release=new TaskCompletionSource<bool>();
        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token)
        {
            ReadStarted=true; ReceivedCancellableToken=token.CanBeCanceled;
            Check(token.CanBeCanceled,"response body ReadAsync must receive a cancellable token");
            await Task.WhenAny(Task.Delay(100),release.Task).ConfigureAwait(false);
            throw new OperationCanceledException("Synthetic body cancellation",token);
        }
        public void Release() { release.TrySetResult(true); }
    }
    private static void BodyCancellation()
    {
        using (var probe=new CancellationProbe())
        using (var transport=new FakeTransport())
        {
            transport.Reply=delegate { return new HttpResponseMessage(HttpStatusCode.OK) { Content=new StreamContent(probe) }; };
            Task<Dictionary<string,string>> operation=null;
            try
            {
                operation=new VnPayGateway(settings,transport).Send(Payment(),Refund(false),"127.0.0.1");
                var cancelled=false;
                try { Bounded(operation); } catch (OperationCanceledException) { cancelled=true; }
                Check(probe.ReadStarted && probe.ReceivedCancellableToken && cancelled,"stalled body cancellation propagated within 2.5s");
            }
            finally
            {
                probe.Release();
                if (operation!=null && !operation.IsCompleted)
                { try { Bounded(operation); } catch (OperationCanceledException) { } }
            }
        }
    }
    private static void Classification()
    {
        // This boundary assumes the caller already authenticated HMAC. Mutating the
        // business fields below intentionally tests classification, not verification.
        foreach (var command in new[] { "refund","querydr" })
        foreach (var partial in new[] { false,true })
        {
            var refund=Refund(partial);
            var valid=Response("refund",partial,false); valid["vnp_Command"]=command; valid["vnp_TransactionNo"]="112233";
            Check(VnPayRefundOutcome.Classify(refund,valid,command)=="Succeeded","matching completed refund succeeds");
            var unknowns=new Dictionary<string,string[]> {
                {"vnp_Command",new[] { "pay" }},{"vnp_TmnCode",new[] { "OTHER001" }},
                {"vnp_TxnRef",new[] { "OTHERREF" }},{"vnp_TransactionType",new[] { "01",partial ? "02" : "03" }},
                {"vnp_Amount",new[] { "1","-1","1234.5","garbage" }},{"vnp_ResponseCode",new[] { "94","99" }},
                {"vnp_TransactionStatus",new[] { "05","06" }},
                {"vnp_TransactionNo",new[] { "", "0", "00000", "9988", "abc", "1234567890123456" }} };
            foreach (var pair in unknowns)
            foreach (var value in pair.Value)
            {
                var changed=new Dictionary<string,string>(valid); changed[pair.Key]=value;
                Check(VnPayRefundOutcome.Classify(refund,changed,command)=="Unknown","uncertain refund stays Unknown: "+pair.Key);
            }
            foreach (var status in new[] { "02","09" })
            {
                var failed=new Dictionary<string,string>(valid); failed["vnp_TransactionStatus"]=status;
                Check(VnPayRefundOutcome.Classify(refund,failed,command)=="Failed","terminal refund failure classified");
                failed["vnp_ResponseCode"]="99";
                Check(VnPayRefundOutcome.Classify(refund,failed,command)==(command=="querydr" ? "Unknown" : "Failed"),"querydr failure requires API success");
                failed["vnp_ResponseCode"]="94";
                Check(VnPayRefundOutcome.Classify(refund,failed,command)=="Unknown","duplicate refund result never terminal");
            }
        }
    }
    public static int Main(string[] args)
    {
        var cases=new Dictionary<string,Action> {
            {"codec",CodecVectors},{"api-vectors",ApiVectors},
            {"query",delegate { GatewayRoundTrip("querydr",false,false,false); }},
            {"query-promotion",delegate { GatewayRoundTrip("querydr",false,true,false); }},
            {"refund-full",delegate { GatewayRoundTrip("refund",false,false,false); }},
            {"refund-partial",delegate { GatewayRoundTrip("refund",true,false,false); }},
            {"tamper",delegate { GatewayRoundTrip("querydr",false,false,true); GatewayRoundTrip("refund",false,false,true); }},
            {"cancellation",BodyCancellation},{"classification",Classification} };
        var selected=args.Length==0 ? "all" : args[0];
        if (args.Length>1 || (selected!="all" && !cases.ContainsKey(selected)))
        { Console.Error.WriteLine("Usage: TestVnPayGateway.exe [all|"+string.Join("|",cases.Keys)+"]"); return 2; }
        var synthetic=new Dictionary<string,string> { {"NEXUS_VNPAY_ENABLED","true"},{"NEXUS_VNPAY_MODE","sandbox"},
            {"NEXUS_VNPAY_TMN_CODE","TEST0001"},{"NEXUS_VNPAY_HASH_SECRET",Secret},
            {"NEXUS_PUBLIC_BASE_URL","https://merchant.example.test"} };
        var saved=synthetic.Keys.ToDictionary(key=>key,key=>Environment.GetEnvironmentVariable(key));
        var failures=0; var passed=0;
        try
        {
            foreach (var pair in synthetic) Environment.SetEnvironmentVariable(pair.Key,pair.Value);
            settings=VnPaySettings.Load();
            foreach (var pair in cases.Where(pair=>selected=="all" || pair.Key==selected))
            {
                try { pair.Value(); passed++; Console.WriteLine("PASS "+pair.Key); }
                catch (Exception error)
                {
                    failures++;
                    // Do not print response bodies, configuration, or exception dumps.
                    Console.Error.WriteLine("FAIL "+pair.Key+": "+error.GetType().Name+": "+error.Message.Replace(Secret,"[redacted]"));
                }
            }
        }
        finally { foreach (var pair in saved) Environment.SetEnvironmentVariable(pair.Key,pair.Value); }
        Console.WriteLine("VNPAY_GATEWAY_TESTS passed="+passed+" failed="+failures);
        return failures==0 ? 0 : 1;
    }
}
