namespace NexusFreelance.Web.Services
{
    public static class WorkLabels
    {
        public static string ProjectType(string value)
        {
            switch(value) { case "SOLO":return "Cá nhân";case "TEAM":return "Nhóm";case "ENTERPRISE":return "Cá nhân hoặc nhóm";default:return value; }
        }
        public static string Proposal(string value)
        {
            switch(value) { case "Pending":return "Chờ khách hàng xem";case "Shortlisted":return "Đang được cân nhắc";case "Accepted":return "Đã được chọn";case "Rejected":return "Chưa được chọn";case "Withdrawn":return "Đã rút đề xuất";default:return value; }
        }
        public static string Task(string value)
        {
            switch(value) { case "ToDo":return "Chưa bắt đầu";case "Doing":return "Đang thực hiện";case "Review":return "Chờ kiểm tra";case "Done":return "Đã hoàn thành";default:return value; }
        }
        public static string Priority(string value)
        {
            switch(value) { case "High":return "Ưu tiên cao";case "Low":return "Ưu tiên thấp";case "Normal":case null:return "Ưu tiên thường";default:return value; }
        }
        public static string Evidence(string value)
        {
            switch(value) { case "Pending":return "Đang chờ xét duyệt";case "Approved":return "Đã được duyệt";case "Rejected":return "Cần bổ sung theo lý do từ chối";case "Expired":return "Đã hết hiệu lực";case "Cancelled":return "Đã hủy";default:return value; }
        }
    }
}
