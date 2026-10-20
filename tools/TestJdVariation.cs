using System;
using System.Linq;
using NexusFreelance.Web.Services;
using NexusFreelance.Web.ViewModels;
class TestJdVariation {
 static int fails=0,checks=0;
 static void Check(bool ok,string label){checks++;Console.WriteLine((ok?"PASS ":"FAIL ")+label);if(!ok)fails++;}
 static int Main(){
 var g=new RuleBasedJobDraftGenerator();
 var vet=new JobIdeaForm {Industry="Công nghệ thông tin",ProjectType="TEAM",Budget=1000000,Idea="Website phòng khám thú y có đặt lịch khám, hồ sơ thú cưng và lịch hẹn của bác sĩ."};
 var shop=new JobIdeaForm {Industry="Công nghệ thông tin",ProjectType="TEAM",Budget=1000000,Idea="Website cửa hàng bán sản phẩm có giỏ hàng, tồn kho và theo dõi đơn hàng."};
 var a=g.Generate(vet);var b=g.Generate(shop);var c=g.Generate(vet);
 Check(a.Objectives!=b.Objectives && a.Deliverables!=b.Deliverables,"Different projects have different goals and acceptance deliverables");
 Check(a.Description!=c.Description || a.Milestones!=c.Milestones,"Repeated brief produces a different useful drafting angle");
 Check(a.UserStories.Contains("lịch") && b.UserStories.Contains("đơn"),"User stories reflect concrete project workflows");
 var translation=g.Generate(new JobIdeaForm {Industry="Nội dung & biên dịch",ProjectType="SOLO",Budget=1000000,Idea="Dịch hướng dẫn sản phẩm từ tiếng Anh sang tiếng Việt, giữ thống nhất thuật ngữ."});
 Check(translation.Roles.Count==1 && translation.Roles[0].Title=="Translator","Solo translation selects the actual professional role");
 Check(!translation.Deliverables.Contains("Mã nguồn"),"Non-software deliverables do not demand source code");
 Console.WriteLine("JD_VARIATION_CHECKS="+checks+" FAILURES="+fails);return fails==0?0:1;
 }
}
