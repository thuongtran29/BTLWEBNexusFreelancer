using System;
using System.Collections.Generic;
using System.Linq;
using NexusFreelance.Web.Models;
using NexusFreelance.Web.Services;
using NexusFreelance.Web.ViewModels;
class TestKnowledgePositions {
 static int count=0;static void Check(bool ok,string name){if(!ok)throw new Exception(name);count++;Console.WriteLine("PASS "+name);}
 static void Denied(Action action,string name){bool denied=false;try{action();}catch(InvalidOperationException){denied=true;}Check(denied,name);}
 static int Main(){try {
 var f=new KnowledgePublishForm {Title="Kiểm tra lịch hẹn khi triển khai dịch vụ",Summary="Kiểm tra đầu vào và trạng thái lịch hẹn trong quá trình tiếp nhận yêu cầu.",Practices="Xác nhận thời gian còn trống và xử lý yêu cầu đổi hoặc hủy lịch rõ ràng.",AcceptanceChecklist="Tái hiện trường hợp trùng lịch, dữ liệu thiếu và thao tác đồng thời để nghiệm thu.",SkillTags="API, đặt lịch",ConfirmGeneric=true};
 SharedKnowledgeService.Validate(f);Check(true,"Generic knowledge passes explicit sharing validation");f.ConfirmGeneric=false;Denied(()=>SharedKnowledgeService.Validate(f),"Sharing requires explicit owner confirmation");f.ConfirmGeneric=true;
 foreach(var marker in new[]{"owner@example.test","https://private.example.test","290820061111","password=secret"}){var old=f.Practices;f.Practices=old+" "+marker;Denied(()=>SharedKnowledgeService.Validate(f),"Private marker excluded: "+marker.Split('@')[0].Split(':')[0]);f.Practices=old;}
 var tracks=new[]{new SkillTrack{Id=1,Title="Backend C# / ASP.NET",MatchKeywords="backend asp.net c#"},new SkillTrack{Id=2,Title="Frontend HTML / CSS / JavaScript",MatchKeywords="frontend javascript"},new SkillTrack{Id=3,Title="SQL / Database",MatchKeywords="sql database"},new SkillTrack{Id=4,Title="Thiết kế trải nghiệm & giao diện",MatchKeywords="ui ux research"}};
 var backend=new RoleRequirement{Id=1,Title="Backend Developer",RequiredCount=1};var front=new RoleRequirement{Id=2,Title="Frontend Developer",RequiredCount=1};var global=new List<ProjectSkillRequirement>();
 var bs=RoleSkillResolver.ForRole(backend,tracks,global);var fs=RoleSkillResolver.ForRole(front,tracks,global);
 Check(bs.Select(x=>x.SkillTrackId).SequenceEqual(new[]{1}),"Backend role does not require unrelated frontend or database tracks");
 Check(fs.Select(x=>x.SkillTrackId).SequenceEqual(new[]{2}),"Frontend role resolves separately from backend");
 Check(RoleSkillResolver.ForRole(new RoleRequirement{Title="UI/UX Designer"},tracks,global).Single().SkillTrackId==4,"UI/UX role resolves to design evidence");
 Check(RoleSkillResolver.ForRole(new RoleRequirement{Title="Unknown custom role"},tracks,global).Count==0,"Unknown roles do not fabricate skill requirements");
 var positions=new List<PositionRecommendation>{new PositionRecommendation{RoleId=1,Remaining=1,Skills=bs},new PositionRecommendation{RoleId=2,Remaining=1,Skills=fs}};
 var members=new Dictionary<int,List<VerifiedSkillRow>>{{7,new List<VerifiedSkillRow>{new VerifiedSkillRow{SkillId=1,Level=3},new VerifiedSkillRow{SkillId=2,Level=3}}}};
 Check(PositionMatchingService.AssignSlots(positions,members).Count==1,"One multi-skilled member covers at most one hiring slot");
 members.Add(8,new List<VerifiedSkillRow>{new VerifiedSkillRow{SkillId=1,Level=3}});
 var assigned=PositionMatchingService.AssignSlots(positions,members);Check(assigned.Count==2 && assigned[7]==2 && assigned[8]==1,"Maximum assignment preserves scarce roles rather than greedy double counting");
 var preferred=PositionMatchingService.AssignSlots(new List<PositionRecommendation>{positions[0]},members,new[]{8});Check(preferred.Count==1 && preferred.ContainsKey(8),"Active member gets priority over pending invitation for a shared slot");
 bs[0].MinimumLevel=4;Check(PositionMatchingService.AssignSlots(positions,members).Count==1,"Group coverage respects the required proficiency level");
 var profile=new FreelancerProfile{UserAccountId=8,User=new UserAccount{Name="Synthetic candidate"},CoreRole="Backend Developer",Industry="Công nghệ thông tin"};
 var project=new Project{Industry="Công nghệ thông tin"};var evidence=members[8];
 Check(PositionMatchingService.Score(backend,project,bs,profile,evidence,evidence).Score==0,"Missing skill level earns no misleading position score");
 Check(PositionMatchingService.Score(front,project,fs,profile,evidence,evidence).Covered==0,"Backend-only candidate does not match frontend vacancy");
 var longIdea=new JobIdeaForm{Idea=new string('a',1500),AdditionalInstructions=new string('b',600),Industry="Công nghệ thông tin",ProjectType="TEAM",Budget=1000000};var draft=new RuleBasedJobDraftGenerator().Generate(longIdea);
 Check(draft.Description.Length+draft.Objectives.Length+draft.Scope.Length+draft.UserStories.Length+draft.RequiredSkills.Length+draft.Deliverables.Length+draft.Milestones.Length<=3600,"Long brief remains inside project description budget");
 Console.WriteLine("KNOWLEDGE_POSITION_POLICY_CHECKS="+count);return 0;
 }catch(Exception e){Console.WriteLine("FAIL "+e.Message);return 1;}}
}
