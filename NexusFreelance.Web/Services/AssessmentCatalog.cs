using System;
using System.Collections.Generic;
using System.Linq;

namespace NexusFreelance.Web.Services
{
    public sealed class AssessmentResource
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Provider { get; set; }
        public string Industry { get; set; }
        public string Level { get; set; }
        public string Summary { get; set; }
        public string SourceUrl { get; set; }
        public string AssessmentUrl { get; set; }
        public string CredentialName { get; set; }
        public string Note { get; set; }
    }

    // Metadata transcribed from the linked providers, reviewed 2026-10-16.
    // Practice questions and scores stay with the provider; no scraped exam bank.
    public static class AssessmentCatalog
    {
        public const string ReviewedOn = "2026-10-16";
        public static IReadOnlyList<AssessmentResource> All { get; } = Array.AsReadOnly(new[] {
            new AssessmentResource {
                Id="azure-fundamentals", Title="Azure Fundamentals · AZ-900", Provider="Microsoft Learn", Industry="Công nghệ thông tin", Level="Nền tảng",
                Summary="Cloud, các dịch vụ Azure và quản trị hạ tầng. Phù hợp Backend Developer, DevOps và người mới làm cloud.",
                SourceUrl="https://learn.microsoft.com/en-us/credentials/certifications/azure-fundamentals/",
                AssessmentUrl="https://learn.microsoft.com/en-us/credentials/certifications/azure-fundamentals/practice/assessment?assessment-type=practice&assessmentId=23&practice-assessment-type=certification",
                CredentialName="Microsoft Certified: Azure Fundamentals", Note="Bài luyện tập không cấp chứng chỉ. Kỳ thi chứng nhận được đăng ký riêng trên Microsoft Learn."
            },
            new AssessmentResource {
                Id="azure-data-fundamentals", Title="Azure Data Fundamentals · DP-900", Provider="Microsoft Learn", Industry="Dữ liệu & AI", Level="Nền tảng",
                Summary="Dữ liệu quan hệ, phi quan hệ và các loại workload trên Azure. Gợi ý cho Data Engineer và người học SQL.",
                SourceUrl="https://learn.microsoft.com/en-us/credentials/certifications/azure-data-fundamentals/",
                AssessmentUrl="https://learn.microsoft.com/en-us/credentials/certifications/azure-data-fundamentals/practice/assessment?assessment-type=practice&assessmentId=24&practice-assessment-type=certification",
                CredentialName="Microsoft Certified: Azure Data Fundamentals", Note="Làm bài luyện tập trên Microsoft Learn; chỉ gửi minh chứng chứng chỉ sau khi được Microsoft cấp."
            },
            new AssessmentResource {
                Id="power-bi-data-analyst", Title="Power BI Data Analyst · PL-300", Provider="Microsoft Learn", Industry="Dữ liệu & AI", Level="Trung cấp",
                Summary="Chuẩn bị và mô hình hóa dữ liệu, phân tích trực quan và quản lý Power BI. Gợi ý cho Data Analyst.",
                SourceUrl="https://learn.microsoft.com/en-us/credentials/certifications/data-analyst-associate/",
                AssessmentUrl="https://learn.microsoft.com/en-us/credentials/certifications/data-analyst-associate/#practice-for-the-exam",
                CredentialName="Microsoft Certified: Power BI Data Analyst Associate", Note="Trang nguồn có Practice Assessment, hướng dẫn thi và gia hạn; điều kiện hiện hành do Microsoft công bố."
            },
            new AssessmentResource {
                Id="aws-cloud-practitioner", Title="AWS Certified Cloud Practitioner · CLF-C02", Provider="AWS", Industry="Công nghệ thông tin", Level="Nền tảng",
                Summary="Kiến thức tổng quan cloud AWS, dịch vụ và thuật ngữ. Trang nguồn dẫn tới kế hoạch ôn thi trên AWS Skill Builder.",
                SourceUrl="https://aws.amazon.com/certification/certified-cloud-practitioner/",
                AssessmentUrl="https://aws.amazon.com/certification/certified-cloud-practitioner/",
                CredentialName="AWS Certified Cloud Practitioner", Note="Xem Official Practice Question Set và Exam Prep trên nguồn AWS. Tài khoản, ngôn ngữ và phí tùy chương trình."
            },
            new AssessmentResource {
                Id="scrum-open", Title="Scrum Open Assessments", Provider="Scrum.org", Industry="Kinh doanh & tư vấn", Level="Tự đánh giá",
                Summary="Đánh giá mở về Scrum, vai trò và thực hành phát triển sản phẩm. Phù hợp Project Manager, Product Owner và Developer.",
                SourceUrl="https://www.scrum.org/open-assessments", AssessmentUrl="https://www.scrum.org/open-assessments",
                Note="Open Assessment là bài luyện tập, không cấp badge/chứng chỉ. Chỉ gửi chứng chỉ Professional nếu bạn đã thi và được cấp riêng."
            },
            new AssessmentResource {
                Id="hubspot-digital-marketing", Title="Digital Marketing Certification", Provider="HubSpot Academy", Industry="Marketing & truyền thông", Level="Nền tảng",
                Summary="Khóa học và đánh giá về chiến lược digital marketing, SEO, nội dung, mạng xã hội và email.",
                SourceUrl="https://academy.hubspot.com/courses/digital-marketing", AssessmentUrl="https://academy.hubspot.com/courses/digital-marketing",
                CredentialName="HubSpot Academy Digital Marketing Certification", Note="Học và hoàn thành yêu cầu trên HubSpot Academy. Gửi liên kết chứng chỉ đã được cấp để quản trị viên xác minh."
            }
        });
        public static AssessmentResource Find(string id) => All.SingleOrDefault(x => x.Id == id);
    }
}
