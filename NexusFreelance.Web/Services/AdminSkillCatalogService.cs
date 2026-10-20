using System;
using System.Data;
using System.Data.Entity;
using System.Linq;
using NexusFreelance.Web.Data;
using NexusFreelance.Web.Models;
using NexusFreelance.Web.ViewModels;

namespace NexusFreelance.Web.Services
{
    // Catalog operations share the same transaction as their audit record.
    // Published answers become immutable once a learner has submitted them.
    public sealed class AdminSkillCatalogService
    {
        private readonly AppDbContext db;
        public AdminSkillCatalogService(AppDbContext context) { db = context; }

        private void Audit(int actorId, string action, string description)
        {
            db.AdminAuditLogs.Add(new AdminAuditLog
            {
                ActorId = actorId,
                // Catalog changes have no target user; the actor is the audit anchor.
                TargetUserId = actorId,
                Action = action,
                Reason = description,
                CreatedAtUtc = DateTime.UtcNow
            });
        }

        public void CreateTrack(int actorId, AdminCreateSkillForm form)
        {
            var code = form.Code.Trim().ToUpperInvariant();
            using (var tx = db.Database.BeginTransaction(IsolationLevel.Serializable))
            {
                if (db.SkillTracks.Any(t => t.Code == code))
                    throw new InvalidOperationException("Mã kỹ năng đã tồn tại.");
                var track = new SkillTrack
                {
                    Code = code, Industry = IndustryCatalog.Normalize(form.Industry), IsActive = form.IsActive,
                    Title = form.Title.Trim(),
                    Description = form.Description.Trim(),
                    MatchKeywords = form.MatchKeywords.Trim()
                };
                db.SkillTracks.Add(track);
                db.SaveChanges();
                Audit(actorId, "CreateSkillTrack", "SkillTrack #" + track.Id + " (" + code + ")");
                db.SaveChanges();
                tx.Commit();
            }
        }

        public bool UpdateTrack(int actorId, AdminUpdateSkillForm form)
        {
            using (var tx = db.Database.BeginTransaction(IsolationLevel.Serializable))
            {
                var track = db.SkillTracks.SingleOrDefault(t => t.Id == form.Id);
                if (track == null) return false;
                track.Industry = IndustryCatalog.Normalize(form.Industry); track.IsActive = form.IsActive;
                track.Title = form.Title.Trim();
                track.Description = form.Description.Trim();
                track.MatchKeywords = form.MatchKeywords.Trim();
                Audit(actorId, "UpdateSkillTrack", "SkillTrack #" + track.Id + " (" + track.Code + ")");
                db.SaveChanges();
                tx.Commit();
                return true;
            }
        }

        public bool SaveQuestion(int actorId, AdminSaveQuestionForm form)
        {
            using (var tx = db.Database.BeginTransaction(IsolationLevel.Serializable))
            {
                if (!db.SkillTracks.Any(t => t.Id == form.SkillTrackId)) return false;
                SkillExamQuestion question;
                if (form.Id == 0)
                {
                    question = new SkillExamQuestion { SkillTrackId = form.SkillTrackId };
                    db.SkillExamQuestions.Add(question);
                }
                else
                {
                    question = db.SkillExamQuestions.SingleOrDefault(q => q.Id == form.Id && q.SkillTrackId == form.SkillTrackId);
                    if (question == null) return false;
                    if (db.SkillExamAnswers.Any(a => a.SkillExamQuestionId == form.Id))
                        throw new InvalidOperationException("Câu hỏi đã được sử dụng trong bài thi; không thể chỉnh sửa.");
                }
                if (db.SkillExamQuestions.Any(q => q.SkillTrackId == form.SkillTrackId &&
                                                     q.Ordinal == form.Ordinal && q.Id != form.Id))
                    throw new InvalidOperationException("Thứ tự câu hỏi đã tồn tại trong kỹ năng này.");
                question.Ordinal = form.Ordinal;
                question.Prompt = form.Prompt.Trim();
                question.ChoiceA = form.ChoiceA.Trim();
                question.ChoiceB = form.ChoiceB.Trim();
                question.ChoiceC = form.ChoiceC.Trim();
                question.ChoiceD = form.ChoiceD.Trim();
                question.CorrectChoice = form.CorrectChoice.Trim().ToUpperInvariant();
                db.SaveChanges();
                Audit(actorId, form.Id == 0 ? "CreateExamQuestion" : "UpdateExamQuestion",
                    "Question #" + question.Id + ", SkillTrack #" + form.SkillTrackId);
                db.SaveChanges();
                tx.Commit();
                return true;
            }
        }
    }
}
