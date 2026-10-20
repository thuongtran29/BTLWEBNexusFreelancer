using System;
using NexusFreelance.Web.Services;
class TestMilestonePlan
{
    static int checks;
    static void Check(bool ok,string label){if(!ok)throw new Exception(label);checks++;Console.WriteLine("PASS "+label);}
    static void Reject(string value,decimal total,string label){try{PaymentPlan.Parse(value,total,1,14);throw new Exception("Accepted "+label);}catch(InvalidOperationException){Check(true,label);}}
    static int Main(){
        var legacy=PaymentPlan.Parse("Phân tích | 400000\nBàn giao | 600000",1000000,1,14);
        Check(legacy.Count==2 && legacy[0].Amount==400000,"legacy two-column plan remains readable");
        var json="[{\"Title\":\"Bản mẫu API\",\"Amount\":400000,\"DueDay\":5,\"AcceptanceCriteria\":\"API chạy đúng bộ kiểm thử đã thống nhất\"},{\"Title\":\"Bàn giao mã nguồn\",\"Amount\":600000,\"DueDay\":14,\"AcceptanceCriteria\":\"Có mã nguồn và hướng dẫn triển khai đầy đủ\"}]";
        var plan=PaymentPlan.Parse(json,1000000,1,14);
        Check(plan.Count==2 && plan[0].DueDay==5 && plan[1].AcceptanceCriteria.Contains("mã nguồn"),"structured SOLO or TEAM plan keeps deadline and acceptance criteria");
        Check(PaymentPlan.Parse(null,1000000,1,14).Count==1,"blank plan creates a whole-scope milestone");
        Reject(json,900000,"sum mismatch rejected");
        Reject(json.Replace("\"DueDay\":14","\"DueDay\":15"),1000000,"deadline cannot exceed agreement duration");
        Reject(json.Replace("\"DueDay\":5","\"DueDay\":0"),1000000,"zero deadline rejected");
        Reject(json.Replace("\"DueDay\":5","\"DueDay\":14").Replace("\"DueDay\":14,\"AcceptanceCriteria\":\"Có","\"DueDay\":5,\"AcceptanceCriteria\":\"Có"),1000000,"milestone deadlines remain ordered");
        Reject(json.Replace("400000","400000.5"),1000000.5m,"fractional VND rejected");
        Reject(json.Replace("400000","400000.5").Replace("600000","599999.5"),1000000,"fractional milestone amounts rejected even when their sum is an integer");
        Reject("[]",1000000,"empty structured plan rejected");
        Reject("[null]",1000000,"null milestone rejected");
        Reject(json.Replace("Bản mẫu API","AB"),1000000,"short title rejected");
        Reject(json.Replace("API chạy đúng bộ kiểm thử đã thống nhất",""),1000000,"structured plan requires acceptance criteria");
        Reject(json.Replace("\"DueDay\":5","\"DueDay\":5.6"),1000000,"fractional JSON day is a controlled validation error");
        Reject(json.Replace("\"DueDay\":5","\"DueDay\":2147483648"),1000000,"oversized JSON day is a controlled validation error");
        Check(PaymentPlan.Preview(json.Replace("\"DueDay\":5","\"DueDay\":5.6")).Count==0,"preview safely handles malformed numeric fields");
        Check(PaymentPlan.Preview(json.Replace("\"DueDay\":5","\"DueDay\":2147483648")).Count==0,"preview safely handles oversized numeric fields");
        Console.WriteLine("MILESTONE_PLAN_CHECKS="+checks);return 0;
    }
}
