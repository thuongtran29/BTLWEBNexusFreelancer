using System;
using System.Linq;

namespace NexusFreelance.Web.Services
{
    public static class IndustryCatalog
    {
        public static readonly string[] All = { "Công nghệ thông tin", "Thiết kế & sáng tạo", "Marketing & truyền thông", "Nội dung & biên dịch", "Kinh doanh & tư vấn", "Dữ liệu & AI", "Kỹ thuật & kiến trúc", "Khác" };
        public static bool IsValid(string value) => string.IsNullOrWhiteSpace(value) || All.Contains(value, StringComparer.Ordinal);
        public static string Normalize(string value) => string.IsNullOrWhiteSpace(value) ? All[0] : value;
        public static string[] SuggestedRoles(string industry)
        {
            switch (industry) {
                case "Thiết kế & sáng tạo": return new[] { "UI/UX Designer", "Graphic Designer", "Creative Reviewer" };
                case "Marketing & truyền thông": return new[] { "Digital Marketer", "Content Strategist", "Marketing Analyst" };
                case "Nội dung & biên dịch": return new[] { "Content Writer", "Translator", "Editor" };
                case "Kinh doanh & tư vấn": return new[] { "Business Analyst", "Business Consultant", "Project Manager" };
                case "Dữ liệu & AI": return new[] { "Data Analyst", "Data Engineer", "AI / ML Engineer" };
                case "Kỹ thuật & kiến trúc": return new[] { "Architect", "CAD Designer", "Technical Reviewer" };
                default: return new[] { "Full-stack Developer", "UI/UX Designer", "QA / Tester" };
            }
        }
        public static readonly string[] Roles = { "Frontend Developer", "Backend Developer", "Full-stack Developer", "Mobile Developer", "UI/UX Designer", "Graphic Designer", "QA / Tester", "DevOps Engineer", "Data Analyst", "Data Scientist", "AI / ML Engineer", "Project Manager", "Business Analyst", "Content Writer", "SEO Specialist", "Digital Marketer", "Video Editor", "Translator", "Architect" };
    }
}
