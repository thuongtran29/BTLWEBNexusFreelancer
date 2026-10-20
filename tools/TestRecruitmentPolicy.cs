using System;
using NexusFreelance.Web.Services;
class TestRecruitmentPolicy
{
 static int Main() {
  var now=new DateTime(2026,10,19,23,59,59,DateTimeKind.Utc);
  var cases=new[]{
   new {Due=(DateTime?)null,Expected=true,Name="Unspecified date remains open"},
   new {Due=(DateTime?)now.Date.AddDays(-1),Expected=false,Name="Past day is closed"},
   new {Due=(DateTime?)now.Date,Expected=true,Name="Last day stays open until midnight"},
   new {Due=(DateTime?)now.Date.AddDays(1),Expected=true,Name="Future day is open"}};
  foreach(var c in cases) {
   if(RecruitmentPolicy.DateAllowsApplications(c.Due,now)!=c.Expected) {Console.WriteLine("FAIL "+c.Name);return 1;}
   Console.WriteLine("PASS "+c.Name);
  }
  if(RecruitmentPolicy.DateAllowsApplications(now.Date,now.Date.AddDays(1)))return 1;
  Console.WriteLine("PASS Following day closes recruitment");return 0;
 }
}
