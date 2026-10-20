using System;
using NexusFreelance.Web.Models;
using NexusFreelance.Web.ViewModels;
namespace NexusFreelance.Web.Services
{
    public sealed class PageBackLink { public string Path {get;set;} public string Label {get;set;} }
    public static class PageNavigation
    {
        private static PageBackLink Link(string path,string label)=>new PageBackLink{Path=path,Label=label};
        public static PageBackLink Back(string controller,string action,string role,object model,int? id=null)
        {
            controller=(controller??"").ToLowerInvariant();action=(action??"").ToLowerInvariant();
            if((controller=="home" && action=="index") || (controller=="dashboard" && action=="index") || (controller=="admin" && action=="index") || (controller=="account" && action=="login"))return null;
            var contract=model as ContractDetailsPage;
            if(contract!=null)return Link("~/Contracts?projectId="+contract.Contract.ProjectId,"Hợp đồng của dự án");
            var workspace=model as Workspace;
            if(workspace!=null)return workspace.ContractId.HasValue?Link("~/Contracts/Details/"+workspace.ContractId,"Điều khoản & thanh toán"):Link("~/Projects/Details/"+workspace.ProjectId,"Chi tiết dự án");
            var bid=model as GroupBidPage;
            if(bid!=null)return Link("~/Projects/Details/"+bid.Project.Id,"Dự án đang ứng tuyển");
            if(controller=="projects"){
                if(action=="edit" && id>0)return Link("~/Projects/Manage/"+id,"Quản lý dự án");
                return role=="Client"?Link("~/Projects/Mine","Dự án & ứng viên"):Link("~/","Tìm dự án");
            }
            if(controller=="admin")return Link("~/Admin","Việc quản trị cần làm");
            if(controller=="proposals" && action!="mine")return Link("~/Proposals/Mine","Theo dõi ứng tuyển");
            if(controller=="contracts" && action!="index")return Link("~/Contracts","Hợp đồng & bàn giao");
            if(controller=="groups" && action!="index")return Link("~/Groups","Nhóm cộng tác");
            if(controller=="messages" && action!="index")return Link("~/Messages","Hội thoại & liên hệ");
            if(controller=="profile" && action!="index")return Link("~/Profile","Hồ sơ & sản phẩm đã làm");
            if(controller=="skills" && action!="index")return Link("~/Skills","Bài kiểm tra & chứng chỉ");
            if(controller=="negotiation")return role=="Client"?Link("~/Projects/Mine","Dự án & ứng viên"):Link("~/Proposals/Mine","Theo dõi ứng tuyển");
            if(controller=="account" && action!="security")return Link("~/Account/Login","Đăng nhập");
            if(controller=="payments")return Link("~/Contracts","Hợp đồng & bàn giao");
            if(controller=="talent" && action!="index")return Link("~/Talent","Chuyên gia & nhóm");
            if(controller=="jobstudio")return Link("~/Projects/Create","Tạo dự án");
            return role=="Admin"?Link("~/Admin","Việc quản trị cần làm"):string.IsNullOrEmpty(role)?Link("~/","Trang chủ"):Link("~/Dashboard","Việc cần làm");
        }
    }
}
