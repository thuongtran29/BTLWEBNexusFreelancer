using System.Linq;
using NexusFreelance.Web.Data;
using NexusFreelance.Web.Models;

namespace NexusFreelance.Web.Services
{
    // Bootstrap the introductory question bank once. Admin-managed question edits
    // must survive future application restarts without reintroducing old ordinals.
    public static class SkillExamSeeder
    {
        private static void AddTrack(AppDbContext db, string code, string title, string description,
            string keywords, string[][] questions)
        {
            var track = db.SkillTracks.SingleOrDefault(t => t.Code == code);
            if (track == null)
            {
                track = new SkillTrack { Code = code, Title = title, Description = description, MatchKeywords = keywords };
                db.SkillTracks.Add(track);
                db.SaveChanges();
            }
            if (db.SkillExamQuestions.Any(q => q.SkillTrackId == track.Id)) return;
            for (var i = 0; i < questions.Length; i++)
            {
                int ordinal = i + 1;
                var item = questions[i];
                db.SkillExamQuestions.Add(new SkillExamQuestion
                {
                    SkillTrackId = track.Id, Ordinal = ordinal, Prompt = item[0],
                    ChoiceA = item[1], ChoiceB = item[2], ChoiceC = item[3], ChoiceD = item[4],
                    CorrectChoice = item[5]
                });
            }
            db.SaveChanges();
        }

        public static void Seed(AppDbContext db)
        {
            AddTrack(db, "BACKEND_DOTNET", "Backend C# / ASP.NET", "Bài kiểm tra nhập môn backend, HTTP và bảo mật.",
                "backend asp.net dotnet c# server api", new[]
            {
                new[] { "HTTP GET thường được thiết kế để làm gì?", "Xóa dữ liệu", "Đọc tài nguyên mà không chủ ý thay đổi trạng thái", "Đổi mật khẩu", "Tạo tài khoản", "B" },
                new[] { "Cách phổ biến để tránh SQL injection là gì?", "Ghép trực tiếp chuỗi SQL", "Ẩn trang đăng nhập", "Sử dụng câu lệnh có tham số", "Chỉ kiểm tra ở JavaScript", "C" },
                new[] { "Từ khóa await trong C# thường có tác dụng gì?", "Chặn toàn bộ máy chủ", "Đợi bất đồng bộ một tác vụ hỗ trợ await", "Tự động tạo thread mới", "Tắt exception", "B" },
                new[] { "Controller trong ASP.NET MVC chủ yếu làm gì?", "Chứa mật khẩu người dùng", "Lưu hình ảnh trên ổ đĩa", "Nhận request và điều phối xử lý", "Thay thế database", "C" },
                new[] { "Mã HTTP 403 biểu thị điều gì?", "Không tìm thấy", "Máy chủ không phản hồi", "Yêu cầu thành công", "Máy chủ từ chối quyền truy cập", "D" }
            });

            AddTrack(db, "FRONTEND_WEB", "Frontend HTML / CSS / JavaScript", "Bài kiểm tra nhập môn giao diện web.",
                "frontend html css javascript js react web interface", new[]
            {
                new[] { "Phương thức JavaScript nào gắn listener sự kiện vào phần tử?", "addEventListener", "addHandlerToPage", "createElement", "fetch", "A" },
                new[] { "Khai báo const trong JavaScript có đặc điểm nào?", "Không thể đọc", "Không thể gán lại biến đó", "Luôn là object bất biến sâu", "Chỉ dùng trong vòng lặp", "B" },
                new[] { "Thuộc tính CSS nào bật Grid Layout?", "position: fixed", "flex-wrap: wrap", "display: grid", "float: left", "C" },
                new[] { "Khi hiển thị chuỗi do người dùng nhập, cách nào giúp tránh chèn HTML ngoài ý muốn?", "Gán innerHTML trực tiếp", "Dùng eval", "Nối chuỗi vào onclick", "Gán textContent", "D" },
                new[] { "Trong Flexbox, justify-content căn chỉnh theo hướng nào?", "Trục chính", "Trục phụ", "Luôn theo chiều dọc", "Chỉ phần tử cuối", "A" }
            });

            AddTrack(db, "DATABASE_SQL", "SQL / Database", "Bài kiểm tra nhập môn dữ liệu quan hệ và SQL.",
                "sql database data mysql sqlserver mssql query", new[]
            {
                new[] { "Một PRIMARY KEY có đặc điểm nào?", "Có thể trùng nhiều dòng", "Định danh duy nhất và không NULL", "Luôn là văn bản", "Bắt buộc là khóa ngoại", "B" },
                new[] { "INNER JOIN trả về những dòng nào?", "Mọi dòng bảng trái", "Mọi dòng cả hai bảng", "Những dòng khớp điều kiện JOIN", "Chỉ những dòng không khớp", "C" },
                new[] { "Lệnh nào xác nhận các thay đổi trong transaction SQL?", "ROLLBACK", "DELETE", "DROP", "COMMIT", "D" },
                new[] { "Index trong cơ sở dữ liệu thường có đánh đổi nào?", "Có thể tăng tốc truy vấn nhưng thêm chi phí ghi", "Làm mọi truy vấn chậm đi", "Tự xóa dữ liệu trùng", "Thay thế PRIMARY KEY", "A" },
                new[] { "Câu lệnh nào nhóm bản ghi trước khi tính SUM theo từng nhóm?", "ORDER BY", "GROUP BY", "DISTINCT ON", "TOP", "B" }
            });
        }
    }
}
