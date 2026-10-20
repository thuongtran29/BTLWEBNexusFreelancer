using System;
using System.Data.Entity;
using System.Linq;
using NexusFreelance.Web.Data;
using NexusFreelance.Web.Services;
class TestStaffingFreshness {
 static int n;static void Check(bool ok,string text){if(!ok)throw new Exception(text);n++;Console.WriteLine("PASS "+text);}
 static int Main(string[] args){try{
 Database.SetInitializer<AppDbContext>(null);int group=int.Parse(args[0]),project=int.Parse(args[1]),plan=int.Parse(args[2]);
 using(var a=new AppDbContext())using(var b=new AppDbContext()){
 if(a.Database.Connection.Database!="NexusFreelanceAudit20261019")throw new Exception("Audit DB only");
 var svc=new GroupRecruitmentService(a);var page=svc.Build(group,project,true);var backend=page.Positions.Single(p=>p.Title.Contains("Backend"));
 Check(page.MemberCount==1 && backend.FilledCount==0 && backend.AwaitingCount==1 && backend.Remaining==0,"Pending invitation reserves one role but is never counted as joined");
 Check(page.PendingInvitations.Any(m=>m.UserId==8),"Leader can inspect and cancel pending invitations");
 bool denied=false;try{svc.CancelInvitation(plan,14,8);}catch(UnauthorizedAccessException){denied=true;}Check(denied,"Nonleader cannot cancel another group invitation");
 svc.CancelInvitation(plan,7,8);page=svc.Build(group,project,true);backend=page.Positions.Single(p=>p.Title.Contains("Backend"));Check(backend.Remaining==1 && backend.AwaitingCount==0 && page.MemberCount==1,"Cancelled invitation reopens role without changing active membership");
 Check(page.CanRecruit && page.IsOpen,"Cancelled invitation restores an open eligible recruitment baseline");
 b.Database.ExecuteSqlCommand("UPDATE dbo.GroupProjectRecruitments SET Status='Closed' WHERE Id=@p0",plan);
 Check(svc.Build(group,project).Recruitment.Status=="Closed" && !svc.Build(group,project).IsOpen,"Preloaded context sees a concurrently closed recruitment");
 b.Database.ExecuteSqlCommand("UPDATE dbo.GroupProjectRecruitments SET Status='Open' WHERE Id=@p0",plan);
 var deadline=b.Projects.AsNoTracking().Single(p=>p.Id==project).DeadlineUtc;
 try{b.Database.ExecuteSqlCommand("UPDATE dbo.Projects SET DeadlineUtc=@p0 WHERE Id=@p1",DateTime.UtcNow.AddDays(-2),project);Check(!svc.Build(group,project).CanRecruit,"Preloaded context sees expired deadline before inviting");}
 finally{b.Database.ExecuteSqlCommand("UPDATE dbo.Projects SET DeadlineUtc=@p0 WHERE Id=@p1",(object)deadline??DBNull.Value,project);}
 var size=b.FreelancerGroups.AsNoTracking().Single(g=>g.Id==group).TargetSize;
 try{b.Database.ExecuteSqlCommand("UPDATE dbo.FreelancerGroups SET TargetSize=1 WHERE Id=@p0",group);Check(!svc.Build(group,project).CanRecruit,"Preloaded context sees capacity change before inviting");}
 finally{b.Database.ExecuteSqlCommand("UPDATE dbo.FreelancerGroups SET TargetSize=@p0 WHERE Id=@p1",(object)size??DBNull.Value,group);}
 denied=false;try{svc.CancelInvitation(plan,7,7);}catch(InvalidOperationException){denied=true;}Check(denied,"Cancellation cannot remove an active group member");
 }
 Console.WriteLine("STAFFING_FRESHNESS_CHECKS="+n);return 0;
 }catch(Exception e){Console.WriteLine("FAIL "+e);return 1;}}
}
