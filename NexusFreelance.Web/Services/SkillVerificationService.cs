using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Linq;
using NexusFreelance.Web.Data;
using NexusFreelance.Web.Models;
using NexusFreelance.Web.ViewModels;

namespace NexusFreelance.Web.Services
{
    public sealed class SkillVerificationService
    {
        private readonly AppDbContext db;
        public const int PassPercent = 80;
        public static bool HasPassed(int correct, int total) => total > 0 && correct >= 0 && correct <= total && (long)correct * 100 > (long)total * PassPercent;
        public const int MaxAttemptsPerDay = 3;
        public SkillVerificationService(AppDbContext db) { this.db = db; }

        public SkillCenterPage LoadCenter(int freelancerId)
        {
            return new SkillCenterPage
            {
                Tracks = db.SkillTracks.Where(t=>t.IsActive!=false && db.SkillExamQuestions.Count(q=>q.SkillTrackId==t.Id)>=5).OrderBy(t => t.Title).ToList(),
                Badges = db.EarnedSkillBadges.Include(b => b.SkillTrack).Include(b => b.EvidenceAttempt)
                    .Where(b => b.UserAccountId == freelancerId && !b.RevokedAtUtc.HasValue)
                    .OrderByDescending(b => b.IssuedAtUtc).ToList(),
                RevokedBadges = db.EarnedSkillBadges.Include(b => b.SkillTrack)
                    .Where(b => b.UserAccountId == freelancerId && b.RevokedAtUtc.HasValue)
                    .OrderByDescending(b => b.RevokedAtUtc).ToList(),
                RecentAttempts = db.SkillExamAttempts.Include(a => a.SkillTrack)
                    .Where(a => a.UserAccountId == freelancerId).OrderByDescending(a => a.SubmittedAtUtc)
                    .Take(20).ToList()
            };
        }

        public SkillExamPage LoadExam(int trackId, int freelancerId)
        {
            var track = db.SkillTracks.SingleOrDefault(t => t.Id == trackId && t.IsActive != false);
            if (track == null) return null;
            bool earned = db.EarnedSkillBadges.Any(b => b.UserAccountId == freelancerId &&
                b.SkillTrackId == trackId && !b.RevokedAtUtc.HasValue);
            DateTime start = DateTime.UtcNow.AddHours(-24);
            int attempts = db.SkillExamAttempts.Count(a => a.UserAccountId == freelancerId && a.SkillTrackId == trackId && a.SubmittedAtUtc >= start);
            var questions = (earned || attempts >= MaxAttemptsPerDay)
                ? new List<PublicExamQuestion>()
                : db.SkillExamQuestions.Where(q => q.SkillTrackId == trackId).OrderBy(q => q.Ordinal)
                    .Select(q => new PublicExamQuestion
                    {
                        Id = q.Id, Ordinal = q.Ordinal, Prompt = q.Prompt,
                        ChoiceA = q.ChoiceA, ChoiceB = q.ChoiceB, ChoiceC = q.ChoiceC, ChoiceD = q.ChoiceD
                    }).ToList();
            // A newly created catalog item cannot be taken until Admin has
            // supplied the minimum complete bank accepted by Submit().
            if (questions.Count < 5) questions.Clear();
            SkillExamSession session = null;
            if (questions.Count >= 5)
            {
                // A database-backed sitting prevents refreshing the page from
                // resetting the 20-minute deadline. Serialize starts per user.
                using (var tx = db.Database.BeginTransaction(IsolationLevel.Serializable))
                {
                    db.Database.SqlQuery<int>(
                        "SELECT [Id] FROM [dbo].[Users] WITH (UPDLOCK, HOLDLOCK) WHERE [Id]=@p0", freelancerId).Single();
                    var now = DateTime.UtcNow;
                    session = db.SkillExamSessions.SingleOrDefault(s =>
                        s.UserAccountId == freelancerId && s.SkillTrackId == trackId);
                    if (session == null)
                    {
                        session = new SkillExamSession { UserAccountId = freelancerId, SkillTrackId = trackId };
                        db.SkillExamSessions.Add(session);
                    }
                    if (session.StartedAtUtc == default(DateTime) || session.CompletedAtUtc.HasValue ||
                        session.ExpiresAtUtc <= now)
                    {
                        session.StartedAtUtc = now;
                        session.ExpiresAtUtc = now.AddSeconds(ExamTicketService.DurationSeconds);
                        session.CompletedAtUtc = null;
                    }
                    db.SaveChanges();
                    tx.Commit();
                }
            }
            return new SkillExamPage
            {
                SkillTrackId = track.Id, Title = track.Title, Description = track.Description,
                EarnedBadge = earned, RemainingAttempts = Math.Max(0, MaxAttemptsPerDay - attempts),
                Questions = questions,
                ExamDurationSeconds = session == null ? 0 : Math.Max(0,
                    (int)Math.Ceiling((session.ExpiresAtUtc - DateTime.UtcNow).TotalSeconds)),
                ExamTicket = session == null ? null : ExamTicketService.Issue(freelancerId, trackId, session.StartedAtUtc)
            };
        }

        // Grade strictly from the server-side answer key. Serialize submissions per user to
        // enforce the attempt quota and prevent duplicate badges across concurrent requests.
        public SkillExamAttempt Submit(int freelancerId, int trackId, ExamSubmissionForm form)
        {
            if (form == null || form.SkillTrackId != trackId || form.Answers == null)
                throw new InvalidOperationException("Dữ liệu bài thi không hợp lệ.");
            // Validate the signed ticket before any scoring or database writes.
            var startedAt = ExamTicketService.Validate(form.ExamTicket, freelancerId, trackId);
            if (form.BrowserFocusLossCount < 0 || form.BrowserFocusLossCount > 999)
                throw new InvalidOperationException("Dữ liệu phiên thi không hợp lệ.");
            using (var transaction = db.Database.BeginTransaction(IsolationLevel.Serializable))
            {
                db.Database.SqlQuery<int>("SELECT [Id] FROM [dbo].[Users] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = @p0", freelancerId).Single();
                if (!db.SkillTracks.Any(t => t.Id == trackId && t.IsActive != false)) throw new InvalidOperationException("Bài thi không tồn tại.");
                var session = db.SkillExamSessions.SingleOrDefault(s =>
                    s.UserAccountId == freelancerId && s.SkillTrackId == trackId);
                if (session == null || session.CompletedAtUtc.HasValue ||
                    session.StartedAtUtc != startedAt || session.ExpiresAtUtc < DateTime.UtcNow)
                    throw new InvalidOperationException("Phiên thi đã kết thúc hoặc thay đổi. Vui lòng mở lại bài thi.");
                var existingBadge = db.EarnedSkillBadges.SingleOrDefault(b =>
                    b.UserAccountId == freelancerId && b.SkillTrackId == trackId);
                if (existingBadge != null && !existingBadge.RevokedAtUtc.HasValue)
                    throw new InvalidOperationException("Bạn đã hoàn thành và nhận Badge cho kỹ năng này.");
                DateTime now = DateTime.UtcNow;
                if (now > session.ExpiresAtUtc)
                    throw new InvalidOperationException("Đã hết 20 phút làm bài. Vui lòng mở lại bài thi.");
                DateTime start = now.AddHours(-24);
                if (db.SkillExamAttempts.Count(a => a.UserAccountId == freelancerId && a.SkillTrackId == trackId && a.SubmittedAtUtc >= start) >= MaxAttemptsPerDay)
                    throw new InvalidOperationException("Đã đạt giới hạn 3 lần thi trong 24 giờ. Hãy quay lại sau.");

                var questions = db.SkillExamQuestions.Where(q => q.SkillTrackId == trackId).ToList();
                // Exactly the full current bank, with no foreign/duplicate question IDs.
                if (questions.Count < 5 || form.Answers.Count != questions.Count ||
                    form.Answers.Any(a => a == null) ||
                    form.Answers.Select(a => a.QuestionId).Distinct().Count() != questions.Count)
                    throw new InvalidOperationException("Bài thi phải có một câu trả lời cho từng câu hỏi.");

                var normalized = new Dictionary<int, string>();
                foreach (var answer in form.Answers)
                {
                    string value = (answer.Choice ?? "").Trim().ToUpperInvariant();
                    if (value != "A" && value != "B" && value != "C" && value != "D")
                        throw new InvalidOperationException("Vui lòng chọn một đáp án A, B, C hoặc D cho mỗi câu.");
                    normalized.Add(answer.QuestionId, value);
                }
                if (questions.Any(q => !normalized.ContainsKey(q.Id)))
                    throw new InvalidOperationException("Mã câu hỏi không thuộc bài thi này.");

                var attempt = new SkillExamAttempt
                {
                    UserAccountId = freelancerId, SkillTrackId = trackId,
                    QuestionCount = questions.Count, SubmittedAtUtc = now,
                    StartedAtUtc = startedAt,
                    DurationSeconds = Math.Max(0, (int)(now - startedAt).TotalSeconds),
                    BrowserFocusLossCount = form.BrowserFocusLossCount
                };
                foreach (var question in questions)
                {
                    bool correct = normalized[question.Id] == question.CorrectChoice;
                    if (correct) attempt.CorrectCount++;
                    attempt.Answers.Add(new SkillExamAnswer
                    {
                        SkillExamQuestionId = question.Id, SelectedChoice = normalized[question.Id], IsCorrect = correct
                    });
                }
                attempt.Percent = (int)Math.Round(100.0 * attempt.CorrectCount / attempt.QuestionCount, MidpointRounding.AwayFromZero);
                attempt.Passed = HasPassed(attempt.CorrectCount, attempt.QuestionCount);
                db.SkillExamAttempts.Add(attempt);
                session.CompletedAtUtc = now;
                db.SaveChanges();
                if (attempt.Passed)
                {
                    if (existingBadge != null)
                    {
                        // A revoked badge is reissued only after a new passing exam.
                        // Historical attempts and the earlier admin audit remain intact.
                        existingBadge.EvidenceAttemptId = attempt.Id;
                        existingBadge.IssuedAtUtc = now;
                        existingBadge.RevokedAtUtc = null;
                        existingBadge.RevokedById = null;
                        existingBadge.RevocationReason = null;
                    }
                    else
                    {
                        db.EarnedSkillBadges.Add(new EarnedSkillBadge
                        {
                            UserAccountId = freelancerId, SkillTrackId = trackId,
                            EvidenceAttemptId = attempt.Id, IssuedAtUtc = now
                        });
                    }
                    db.SaveChanges();
                }
                transaction.Commit();
                return attempt;
            }
        }

        public SkillExamAttempt FindOwnAttempt(int attemptId, int freelancerId)
        {
            return db.SkillExamAttempts.Include(a => a.SkillTrack)
                .SingleOrDefault(a => a.Id == attemptId && a.UserAccountId == freelancerId);
        }
    }
}
