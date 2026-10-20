using System;
using System.Collections.Generic;
using System.Linq;
using NexusFreelance.Web.Data;
using NexusFreelance.Web.ViewModels;

namespace NexusFreelance.Web.Services
{
    // Shared by HTML forms and REST. Annotation validation runs at both boundaries.
    public static class ProjectInputPolicy
    {
        public static IEnumerable<KeyValuePair<string, string>> Errors(ProjectForm form, AppDbContext db, bool informationOnly)
        {
            if (form == null) { yield return Error("", "Thiếu dữ liệu dự án."); yield break; }
            form.SkillIds = form.SkillIds ?? new int[0];
            if (form.Budget != decimal.Truncate(form.Budget)) yield return Error("Budget", "Ngân sách VND phải là số nguyên.");
            if (string.IsNullOrWhiteSpace(form.Title)) yield return Error("Title", "Vui lòng nhập tên dự án.");
            if (string.IsNullOrWhiteSpace(form.Description)) yield return Error("Description", "Vui lòng nhập mô tả dự án.");
            if (!informationOnly && (form.SkillIds.Length > 12 || form.SkillIds.Distinct().Count() != form.SkillIds.Length ||
                db.SkillTracks.Count(t => form.SkillIds.Contains(t.Id) && t.IsActive != false) != form.SkillIds.Length))
                yield return Error("SkillIds", "Chọn tối đa 12 kỹ năng đang hoạt động.");
            if (!IndustryCatalog.IsValid(form.Industry)) yield return Error("Industry", "Nhóm ngành không hợp lệ.");
            if (form.ProjectType != "SOLO" && form.ProjectType != "TEAM" && form.ProjectType != "ENTERPRISE")
                yield return Error("ProjectType", "Loại dự án không hợp lệ.");
            if (!informationOnly && form.Deadline.HasValue &&
                (form.Deadline.Value.Date < DateTime.UtcNow.Date || form.Deadline.Value.Date > DateTime.UtcNow.Date.AddYears(5)))
                yield return Error("Deadline", "Hạn phải từ hôm nay đến tối đa 5 năm tới.");
        }
        private static KeyValuePair<string, string> Error(string key, string message) => new KeyValuePair<string, string>(key, message);
    }
}
