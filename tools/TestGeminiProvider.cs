using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using NexusFreelance.Web.Services;
using NexusFreelance.Web.ViewModels;
class GeminiChunkedFixture:HttpContent {
 readonly byte[] bytes;
 public GeminiChunkedFixture(string s){bytes=Encoding.UTF8.GetBytes(s);}
 protected override bool TryComputeLength(out long length){length=0;return false;}
 protected override Task<Stream> CreateContentReadStreamAsync(){return Task.FromResult<Stream>(new MemoryStream(bytes));}
 protected override Task SerializeToStreamAsync(Stream stream,TransportContext ctx){return stream.WriteAsync(bytes,0,bytes.Length);}
}
class GeminiFixtureHandler:HttpMessageHandler {
 public HttpStatusCode[] StatusSequence;
 public int Calls;public string Body,RequestBody,Secret;public HttpStatusCode Status=HttpStatusCode.OK;public bool Timeout,UnknownLength;
 protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req,CancellationToken ct){
  Calls++;if(Timeout)throw new TaskCanceledException("fixture-secret");
  if(req.RequestUri.AbsoluteUri!="https://generativelanguage.googleapis.com/v1beta/openai/chat/completions")throw new Exception("Wrong Gemini host");
  if(req.Headers.Authorization.Scheme!="Bearer" || req.Headers.Authorization.Parameter!=Secret)throw new Exception("Missing header auth");
  if(req.RequestUri.Query.Length!=0)throw new Exception("Key in URL");
  RequestBody=req.Content.ReadAsStringAsync().GetAwaiter().GetResult();return Task.FromResult(new HttpResponseMessage(StatusSequence==null?Status:StatusSequence[Math.Min(Calls-1,StatusSequence.Length-1)]){Content=UnknownLength?(HttpContent)new GeminiChunkedFixture(Body??""):new StringContent(Body??"",Encoding.UTF8,"application/json")});
 }
}
class TestGeminiProvider {
 static int passed;static JavaScriptSerializer json=new JavaScriptSerializer();static string[] names={"NEXUS_AI_PROVIDER","NEXUS_AI_ENABLED","NEXUS_OPENAI_API_KEY","NEXUS_OPENAI_MODEL","NEXUS_GEMINI_API_KEY","NEXUS_GEMINI_MODEL"};
 static void Check(bool ok,string name){if(!ok)throw new Exception(name);passed++;Console.WriteLine("PASS "+name);}
 static JobEditForm Draft()=>new JobEditForm{Title="Thiết kế nhận diện tiệm bánh",Description="Thiết kế logo và tài liệu hướng dẫn dùng trên bao bì.",Objectives="Logo rõ khi in một màu, mở được tệp vector.",Scope="Trong phạm vi: logo và bao bì. Ngoài: website.",UserStories="Chủ tiệm cần tệp dễ chỉnh sửa để in hộp bánh.",RequiredSkills="Graphic Designer: thiết kế nhận diện và kiểm tra in.",Deliverables="Tệp vector và mẫu in được khách hàng duyệt.",Milestones="1. Duyệt phác thảo. 2. Kiểm tra mẫu in và bàn giao.",Roles=new List<JobRoleForm>{new JobRoleForm{Title="Graphic Designer",RequiredCount=1}}};
 static JobIdeaForm Brief(bool consent=true)=>new JobIdeaForm{Idea="Thiết kế bộ nhận diện cho tiệm bánh trên hộp giấy.",Industry="Thiết kế & sáng tạo",ProjectType="SOLO",Budget=5000000,UseExternalAi=consent,AdditionalInstructions="Kiểm tra logo khi in một màu",KnowledgeContext="Bài học đã được phép chia sẻ: kiểm tra nét mảnh trên giấy.",GenerationAngle="Ưu tiên khả năng bàn giao"};
 static string Wrap(JobEditForm d,string finish="stop")=>json.Serialize(new{choices=new[]{new{finish_reason=finish,message=new{content=json.Serialize(d)}}}});
 static IJobDraftGenerator Generator(GeminiFixtureHandler h,HttpClient c){var t=typeof(JobStudioService).Assembly.GetType("NexusFreelance.Web.Services.GeminiJobDraftGenerator");return (IJobDraftGenerator)Activator.CreateInstance(t,new object[]{h.Secret,"gemini-3.7-flash",new RuleBasedJobDraftGenerator(),c});}
 static JobEditForm Suggest(JobIdeaForm b,IJobDraftGenerator g){try{return (JobEditForm)typeof(JobStudioService).GetMethod("GenerateSuggestion",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{b,g});}catch(TargetInvocationException e){throw e.InnerException;}}
 static void Denied(Action a,string text){bool no=false;try{a();}catch(InvalidOperationException e){no=true;Check(!e.Message.Contains("fixture-secret"),"Provider detail is not exposed: "+text);}Check(no,text);}
 static int Main(){var previous=names.ToDictionary(k=>k,Environment.GetEnvironmentVariable);try{
 foreach(var n in names)Environment.SetEnvironmentVariable(n,"");
 Environment.SetEnvironmentVariable("NEXUS_AI_ENABLED","true");Environment.SetEnvironmentVariable("NEXUS_AI_PROVIDER","Gemini");Environment.SetEnvironmentVariable("NEXUS_GEMINI_API_KEY","fixture-secret-gemini-key-only");Environment.SetEnvironmentVariable("NEXUS_GEMINI_MODEL","gemini-3.7-flash");
 Check(JobDraftGeneratorFactory.IsExternalAiConfigured,"Gemini-only configuration is recognized");
 Check(JobDraftGeneratorFactory.Create(true).GetType().Name=="GeminiJobDraftGenerator","Factory chooses Gemini without OpenAI key");
 Denied(()=>JobDraftGeneratorFactory.Create(true,"OpenAI"),"Provider switch requires renewed consent");
 Check(JobDraftGeneratorFactory.Create(false) is RuleBasedJobDraftGenerator,"No consent always stays local");
 var h=new GeminiFixtureHandler{Secret="fixture-secret-gemini-key-only",Body=Wrap(Draft())};using(var c=new HttpClient(h)){
  var g=Generator(h,c);Denied(()=>Suggest(Brief(false),g),"Gemini cannot send without consent");Check(h.Calls==0,"Consent is checked before HTTP");
  var b=Brief();var d=Suggest(b,g);Check(d.Title==Draft().Title && g.GeneratorName=="Gemini-gemini-3.7-flash","Actual Gemini provenance and JD preserved");
  var req=(Dictionary<string,object>)json.DeserializeObject(h.RequestBody);var messages=(object[])req["messages"];Check((string)((Dictionary<string,object>)messages[0])["content"]==JobPrompt.System,"Hidden system prompt is unchanged");
  Check(h.RequestBody.Contains(b.Idea)&&h.RequestBody.Contains(b.KnowledgeContext)&&h.RequestBody.Contains(b.AdditionalInstructions),"Same permitted brief and knowledge sent to Gemini");
  Check(!h.RequestBody.Contains(h.Secret)&&!req.ContainsKey("store"),"No key or unsupported store flag in Gemini payload");
  Check((string)req["reasoning_effort"]=="low","Gemini interactive request bounds thinking latency");
  var fmt=(Dictionary<string,object>)req["response_format"];var schema=(Dictionary<string,object>)fmt["json_schema"];Check((string)fmt["type"]=="json_schema" && (bool)schema["strict"],"Gemini uses structured JSON schema");
  var contract=(Dictionary<string,object>)schema["schema"];var props=(Dictionary<string,object>)contract["properties"];Check((int)((Dictionary<string,object>)props["Description"])["maxLength"]==600 && (int)((Dictionary<string,object>)props["Roles"])["maxItems"]==1,"Gemini schema enforces concise fields and SOLO hiring boundary");
 }
 foreach(var status in new[]{HttpStatusCode.Unauthorized,HttpStatusCode.Forbidden,HttpStatusCode.NotFound,(HttpStatusCode)429,HttpStatusCode.InternalServerError}){
  var bad=new GeminiFixtureHandler{Secret="fixture-secret",Status=status,Body="fixture-secret private provider response"};using(var c=new HttpClient(bad)){Denied(()=>Suggest(Brief(),Generator(bad,c)),"HTTP "+(int)status+" never stores fake AI");Check(bad.Calls==1,"No automatic retry or provider switch for "+(int)status);}}
 foreach(var status in new[]{HttpStatusCode.BadGateway,HttpStatusCode.ServiceUnavailable,HttpStatusCode.GatewayTimeout}){
  var transient=new GeminiFixtureHandler{Secret="fixture-secret",Body=Wrap(Draft()),StatusSequence=new[]{status,HttpStatusCode.OK}};
  using(var c=new HttpClient(transient)){var g=Generator(transient,c);Check(Suggest(Brief(),g).Title==Draft().Title && transient.Calls==2,"Transient "+(int)status+" recovers on the same provider");}
 }
 var unavailable=new GeminiFixtureHandler{Secret="fixture-secret",Status=HttpStatusCode.ServiceUnavailable,Body="fixture-secret"};
 using(var c=new HttpClient(unavailable)){var g=Generator(unavailable,c);Denied(()=>Suggest(Brief(),g),"Persistent 503 never saves fallback AI");Check(unavailable.Calls==3,"503 retries stop after three total attempts");Check(((IExternalJobDraftGenerator)g).FailureMessage.Contains("503"),"Persistent 503 has an actionable overload message");}
 foreach(var body in new[]{"invalid-json","{}",Wrap(Draft(),"length"),new string('x',60001),"{\"choices\":[{\"finish_reason\":\"stop\",\"message\":{\"refusal\":\"fixture-secret\",\"content\":\"{}\"}}]}"}){
  var bad=new GeminiFixtureHandler{Secret="fixture-secret",Body=body};using(var c=new HttpClient(bad))Denied(()=>Suggest(Brief(),Generator(bad,c)),"Invalid or refused JSON rejected");}
 var chunked=new GeminiFixtureHandler{Secret="fixture-secret",UnknownLength=true,Body=Wrap(Draft())};using(var c=new HttpClient(chunked))Check(Suggest(Brief(),Generator(chunked,c)).Title==Draft().Title,"Valid chunked provider response is read without Content-Length");
 chunked=new GeminiFixtureHandler{Secret="fixture-secret",UnknownLength=true,Body=new string('x',60001)};using(var c=new HttpClient(chunked))Denied(()=>Suggest(Brief(),Generator(chunked,c)),"Chunked response cannot bypass size bound");
 var invalid=Draft();invalid.Roles.Add(new JobRoleForm{Title="Other",RequiredCount=1});var hh=new GeminiFixtureHandler{Secret="fixture-secret",Body=Wrap(invalid)};using(var c=new HttpClient(hh))Denied(()=>Suggest(Brief(),Generator(hh,c)),"SOLO cannot get multiple roles");
 hh=new GeminiFixtureHandler{Secret="fixture-secret",Timeout=true};using(var c=new HttpClient(hh))Denied(()=>Suggest(Brief(),Generator(hh,c)),"Timeout gives safe error without fake AI");
 Environment.SetEnvironmentVariable("NEXUS_GEMINI_API_KEY","");Environment.SetEnvironmentVariable("NEXUS_OPENAI_API_KEY","fixture-openai-key-only");Environment.SetEnvironmentVariable("NEXUS_OPENAI_MODEL","fixture-model");Check(!JobDraftGeneratorFactory.IsExternalAiConfigured,"Missing Gemini key does not silently select paid OpenAI");
 Environment.SetEnvironmentVariable("NEXUS_AI_PROVIDER","Unknown");Check(!JobDraftGeneratorFactory.IsExternalAiConfigured,"Unknown provider fails closed");
 Environment.SetEnvironmentVariable("NEXUS_AI_PROVIDER","");Check(JobDraftGeneratorFactory.Create(true) is OpenAiJobDraftGenerator,"Legacy OpenAI configuration remains compatible");
 Console.WriteLine("GEMINI_PROVIDER_CHECKS="+passed+"; zero external calls");return 0;
 }catch(Exception e){Console.Error.WriteLine("FAIL "+e.Message);return 1;}finally{foreach(var n in names)Environment.SetEnvironmentVariable(n,previous[n]);}}
}

