using System.Linq;
using NexusFreelance.Web.Models;

namespace NexusFreelance.Web.Data
{
    // Career taxonomy for self-declared skills/evidence, not an exam bank or credential.
    internal static class SkillCatalogSeed
    {
        internal static void Apply(AppDbContext db)
        {
            var rows=new[] {
                new[]{"NF_UX_DESIGN","Thiết kế & sáng tạo","Thiết kế trải nghiệm & giao diện","ui ux figma prototype research","Nghiên cứu nhu cầu, thiết kế luồng sử dụng, giao diện và bản mẫu có thể kiểm thử."},
                new[]{"NF_GRAPHIC_DESIGN","Thiết kế & sáng tạo","Thiết kế đồ họa","graphic branding illustration photoshop","Thiết kế nhận diện, ấn phẩm và hình ảnh theo yêu cầu truyền thông."},
                new[]{"NF_DIGITAL_MARKETING","Marketing & truyền thông","Tiếp thị số","digital marketing campaign ads analytics","Lập kế hoạch chiến dịch, triển khai kênh và theo dõi kết quả tiếp thị."},
                new[]{"NF_SEO","Marketing & truyền thông","Tối ưu tìm kiếm (SEO)","seo search content audit","Nghiên cứu từ khóa, kiểm tra website và cải thiện nội dung cho tìm kiếm."},
                new[]{"NF_CONTENT_WRITING","Nội dung & biên dịch","Viết & biên tập nội dung","content writing editing copywriting","Viết nội dung rõ ràng, đúng đối tượng và biên tập theo yêu cầu bàn giao."},
                new[]{"NF_TRANSLATION","Nội dung & biên dịch","Biên dịch & hiệu đính","translation localization proofreading","Dịch, hiệu đính và thống nhất thuật ngữ phù hợp ngữ cảnh sử dụng."},
                new[]{"NF_BUSINESS_ANALYSIS","Kinh doanh & tư vấn","Phân tích nghiệp vụ","business analysis requirements process","Làm rõ nhu cầu, mô hình hóa quy trình và viết yêu cầu có tiêu chí nghiệm thu."},
                new[]{"NF_PROJECT_MANAGEMENT","Kinh doanh & tư vấn","Quản lý dự án","project management planning risk coordination","Lập kế hoạch, phối hợp các bên và theo dõi phạm vi, tiến độ, rủi ro."},
                new[]{"NF_DATA_ANALYSIS","Dữ liệu & AI","Phân tích dữ liệu","data analysis sql dashboard visualization","Làm sạch dữ liệu, phân tích và trình bày kết quả bằng báo cáo hoặc dashboard."},
                new[]{"NF_MACHINE_LEARNING","Dữ liệu & AI","Ứng dụng học máy","machine learning python model evaluation","Chuẩn bị dữ liệu, đánh giá mô hình và xây bản thử nghiệm có thước đo rõ ràng."},
                new[]{"NF_CAD","Kỹ thuật & kiến trúc","Bản vẽ kỹ thuật CAD","cad drafting technical drawing","Triển khai bản vẽ, lớp thông tin và hồ sơ bàn giao theo yêu cầu kỹ thuật."},
                new[]{"NF_BIM","Kỹ thuật & kiến trúc","Mô hình thông tin công trình (BIM)","bim revit model coordination","Xây dựng mô hình, phối hợp bộ môn và chuẩn bị hồ sơ theo phạm vi được giao."},
                new[]{"NF_RESEARCH","Khác","Nghiên cứu & tổng hợp thông tin","research synthesis documentation","Tìm nguồn, kiểm tra thông tin và tổng hợp tài liệu có dẫn nguồn rõ ràng."},
                new[]{"NF_OPERATIONS","Khác","Điều phối công việc","operations coordination scheduling","Sắp xếp đầu việc, lịch trình và theo dõi các bên hoàn thành cam kết."}
            };
            var existing=db.SkillTracks.Select(t=>t.Code).ToList();
            foreach(var row in rows)
                if(!existing.Contains(row[0])) db.SkillTracks.Add(new SkillTrack{Code=row[0],Industry=row[1],Title=row[2],MatchKeywords=row[3],Description=row[4],IsActive=true});
            db.SaveChanges();
        }
    }
}
