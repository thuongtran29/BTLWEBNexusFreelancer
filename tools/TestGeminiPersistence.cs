using System;
using System.Data.Entity;
using System.Linq;
using System.Net.Http;
using System.Web.Script.Serialization;
using NexusFreelance.Web.Data;
using NexusFreelance.Web.Models;
using NexusFreelance.Web.Services;
using NexusFreelance.Web.ViewModels;
class TestGeminiPersistence {
 static int n;static void Check(bool ok,string s){if(!ok)throw new Exception(s);n++;Console.WriteLine("PASS "+s);}
 static int Main(){try{Database.SetInitializer<AppDbContext>(null);using(var db=new AppDbContext()){
 if(db.Database.Connection.Database!="NexusFreelanceAudit20261019")throw new Exception("Audit database only");var tag=Guid.NewGuid().ToString("N").Substring(0,8);
 db.ExternalCertifications.Add(new ExternalCertification{UserAccountId=13,SkillTrackId=5,ProficiencyLevel=1,Name="AUDIT-FIXTURE design evidence "+tag,Issuer="Synthetic audit fixture",EvidenceUrl="https://example.com/audit-design",IssuedOn=DateTime.UtcNow.Date,Status="Verified",ReviewedById=3,SubmittedAtUtc=DateTime.UtcNow,ReviewedAtUtc=DateTime.UtcNow});db.SaveChanges();
 var source=new Project{ClientId=4,Title="PRIVATE-GEMINI-SOURCE-"+tag,Description="Private project description fixture",Industry="Thiết kế & sáng tạo",ProjectType="SOLO",Budget=8000000,Status="Completed",CreatedAtUtc=DateTime.UtcNow};db.Projects.Add(source);db.SaveChanges();
 db.ProjectMemories.Add(new ProjectMemory{ProjectId=source.Id,EditorId=4,Version=1,Source="ClientEdited",ExecutiveSummary="PRIVATE-MEMORY-"+tag,TechStack="Vector",LessonsLearned="OWN-LESSON-"+tag,CreatedAtUtc=DateTime.UtcNow});db.SaveChanges();
 var article=new KnowledgePublishForm{ProjectId=source.Id,ExpectedVersion=0,Title="Bài học in logo trên hộp giấy",Summary="Dùng cho logo tiệm bánh trên hộp giấy và túi, cần kiểm tra độ rõ trước khi chốt.",Practices="Kiểm tra logo khi in một màu và kích thước nhỏ trước khi bàn giao tệp vector.",AcceptanceChecklist="Đối chiếu bản in, kiểm tra nét mảnh và mở thử tệp vector trong công cụ chỉnh sửa.",SkillTags="logo, hộp giấy",ConfirmGeneric=true};var kid=new SharedKnowledgeService(db).Publish(4,article);
 var form=new JobIdeaForm{Idea="Thiết kế logo tiệm bánh trên hộp giấy "+tag,Industry="Thiết kế & sáng tạo",ProjectType="SOLO",Budget=8000000,UseExternalAi=true,UseProjectKnowledge=true,UseSharedKnowledge=true};JobPrompt.AttachKnowledge(db,4,form);
 Check(form.KnowledgeContext.Contains("PRIVATE-MEMORY-"+tag)&&form.KnowledgeContext.Contains(article.Practices),"Same private opt-in and shared library retrieved for Gemini");
 var other=new JobIdeaForm{Idea=form.Idea,Industry=form.Industry,UseProjectKnowledge=true,UseSharedKnowledge=true};JobPrompt.AttachKnowledge(db,5,other);Check(!other.KnowledgeContext.Contains("PRIVATE-MEMORY-"+tag)&&other.KnowledgeContext.Contains(article.Practices),"Other Client gets shared lesson but never private memory");
 var off=new JobIdeaForm{Idea=form.Idea,Industry=form.Industry,UseProjectKnowledge=false,UseSharedKnowledge=false,KnowledgeContext="forged context"};JobPrompt.AttachKnowledge(db,4,off);Check(string.IsNullOrWhiteSpace(off.KnowledgeContext)&&off.KnowledgeReferences=="","Opt-out clears forged provider context");
 var draft=new JobEditForm{Title="Nhận diện tiệm bánh "+tag,Description="Thiết kế logo và bộ tài sản in hộp giấy.",Objectives="Logo rõ khi in một màu và kích thước nhỏ.",Scope="Trong: logo, bao bì. Ngoài: website.",UserStories="Chủ tiệm cần logo mở được và in rõ trên túi giấy.",RequiredSkills="Graphic Designer: thiết kế vector và kiểm tra bản in.",Deliverables="Tệp vector và hướng dẫn sử dụng có thể chỉnh sửa.",Milestones="1. Duyệt phác thảo. 2. Duyệt bản in và bàn giao.",Roles=new System.Collections.Generic.List<JobRoleForm>{new JobRoleForm{Title="Graphic Designer",RequiredCount=1}}};
 var json=new JavaScriptSerializer();var handler=new GeminiFixtureHandler{Secret="fixture-secret",Body=json.Serialize(new{choices=new[]{new{finish_reason="stop",message=new{content=json.Serialize(draft)}}}})};
 using(var client=new HttpClient(handler)){
 var provider=new GeminiJobDraftGenerator(handler.Secret,"gemini-3.7-flash",new RuleBasedJobDraftGenerator(),client);var service=new JobStudioService(db,provider);var saved=service.Generate(form,4);var version=db.AIJobDraftVersions.Single(v=>v.AIJobDraftId==saved.Id);
 Check(version.Generator=="Gemini-gemini-3.7-flash"&&version.ExternalAiRequested,"Persisted origin is actual Gemini and consented");
 Check(version.PromptVersion==JobPrompt.Version&&version.KnowledgeReferences.Contains("Tri thức chung #"+kid)&&version.KnowledgeReferences.Contains("Memory #"),"Version retains unchanged prompt and both permitted source references");
 Check(handler.RequestBody.Contains(article.Practices)&&handler.RequestBody.Contains("OWN-LESSON-"+tag),"HTTP payload includes only retrieved authorized knowledge");
 var pid=service.ApplyToProject(saved.Id,4);Check(service.ApplyToProject(saved.Id,4)==pid,"Gemini draft Apply remains idempotent");
 var p=db.Projects.Include(x=>x.Client).Include(x=>x.Requirements).Single(x=>x.Id==pid);Check(p.Status=="Draft"&&p.Requirements.Count==1,"Gemini draft maps to unpublished project and SOLO hiring role");
 Check(db.ProjectSkillRequirements.Any(s=>s.ProjectId==pid&&s.SkillTrackId==5),"Gemini position maps to existing graphic design skill");
 var matches=new PositionMatchingService(db).ForProject(p);Check(matches.Count==1&&matches[0].Title=="Graphic Designer"&&matches[0].People.Any(),"Position recommendations continue after Gemini Apply");
 bool denied=false;try{service.ApplyToProject(saved.Id,5);}catch(InvalidOperationException){denied=true;}Check(denied,"Another Client cannot apply the Gemini draft");
 var failed=new GeminiFixtureHandler{Secret="fixture-secret",Status=(System.Net.HttpStatusCode)429,Body="fixture-secret"};using(var failClient=new HttpClient(failed)){var total=db.AIJobDrafts.Count();try{new JobStudioService(db,new GeminiJobDraftGenerator(failed.Secret,"gemini-3.7-flash",new RuleBasedJobDraftGenerator(),failClient)).Generate(form,4);}catch(InvalidOperationException){}Check(db.AIJobDrafts.Count()==total,"Quota failure does not persist a fallback draft or project");}
 Console.WriteLine("GEMINI_PERSISTENCE_FIXTURE draft="+saved.Id+" project="+pid+" knowledge="+kid);
 }
 }Console.WriteLine("GEMINI_PERSISTENCE_CHECKS="+n);return 0;}catch(Exception e){Console.WriteLine("FAIL "+e);return 1;}}
}
