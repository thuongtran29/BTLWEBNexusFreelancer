using System;
using System.Data;
using System.Linq;
using System.Security.Cryptography;
using NexusFreelance.Web.Data;
using NexusFreelance.Web.Models;

namespace NexusFreelance.Web.Services
{
    public sealed class EmailCodeService
    {
        private readonly AppDbContext db;
        public EmailCodeService(AppDbContext db) { this.db = db; }
        public bool Issue(UserAccount user)
        {
            string code;
            // Rejection sampling removes modulo bias for six decimal digits.
            using (var rng = RandomNumberGenerator.Create())
            { var bytes = new byte[4]; uint n; do { rng.GetBytes(bytes); n = BitConverter.ToUInt32(bytes, 0); } while (n >= 4294000000u); code = (n % 1000000).ToString("D6"); }
            var salt = PasswordRecoveryService.NewToken();
            var now = DateTime.UtcNow;
            var record = new RegistrationCode { UserAccountId = user.Id, Salt = salt,
                CodeHash = PasswordRecoveryService.HashToken(salt + code), CreatedAtUtc = now, ExpiresAtUtc = now.AddMinutes(10) };
            using (var tx = db.Database.BeginTransaction(IsolationLevel.ReadCommitted))
            {
                db.Database.SqlQuery<int>("SELECT Id FROM dbo.Users WITH(UPDLOCK,HOLDLOCK) WHERE Id=@p0", user.Id).Single();
                foreach (var old in db.RegistrationCodes.Where(c => c.UserAccountId == user.Id && !c.ConsumedAtUtc.HasValue)) old.ConsumedAtUtc = now;
                db.RegistrationCodes.Add(record); db.SaveChanges(); tx.Commit();
            }
            try { PasswordRecoveryService.SendCode(user.Email, code, user.Role); return true; }
            catch (Exception ex) when (ex is System.Net.Mail.SmtpException || ex is InvalidOperationException || ex is ArgumentException || ex is FormatException || ex is System.IO.IOException || ex is UnauthorizedAccessException)
            { record.ConsumedAtUtc = DateTime.UtcNow; db.SaveChanges(); System.Diagnostics.Trace.TraceWarning("Registration code delivery failed."); return false; }
        }

        public bool Verify(string email, string role, string code)
        {
            if (string.IsNullOrWhiteSpace(email) || (role != "Client" && role != "Freelancer") ||
                code == null || code.Length != 6 || code.Any(c => c < '0' || c > '9')) return false;
            using (var tx = db.Database.BeginTransaction(IsolationLevel.ReadCommitted))
            {
                var user = db.Users.SqlQuery("SELECT * FROM dbo.Users WITH(UPDLOCK,HOLDLOCK) WHERE Email=@p0 AND Role=@p1", email, role).SingleOrDefault();
                if (user == null || user.IsSuspended || user.EmailVerifiedAtUtc.HasValue) return false;
                var record = db.RegistrationCodes.Where(c => c.UserAccountId == user.Id && !c.ConsumedAtUtc.HasValue).OrderByDescending(c => c.Id).FirstOrDefault();
                if (record == null || record.ExpiresAtUtc <= DateTime.UtcNow || record.FailedAttempts >= 5) return false;
                var expected = record.CodeHash; var actual = PasswordRecoveryService.HashToken(record.Salt + code);
                var diff = 0; for (var i = 0; i < expected.Length; i++) diff |= expected[i] ^ actual[i];
                if (diff != 0) { record.FailedAttempts++; db.SaveChanges(); tx.Commit(); return false; }
                var now = DateTime.UtcNow;
                user.EmailVerifiedAtUtc = now;
                // The user lock serializes every browser/worker. Consume all outstanding
                // challenges, including legacy rows, in the same atomic confirmation.
                foreach (var pending in db.RegistrationCodes.Where(c => c.UserAccountId == user.Id && !c.ConsumedAtUtc.HasValue))
                    pending.ConsumedAtUtc = now;
                db.SaveChanges(); tx.Commit(); return true;
            }
        }
    }
}
