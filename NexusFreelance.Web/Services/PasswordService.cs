using System;
using System.Security.Cryptography;

namespace NexusFreelance.Web.Services
{
    public static class PasswordService
    {
        private const int Iterations = 600000;

        public static bool NeedsRehash(string hash) { return hash == null || !hash.StartsWith(Iterations + ":", StringComparison.Ordinal); }

        public static string Hash(string password)
        {
            var salt = new byte[16];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(salt);
            using (var pbkdf2 = new Rfc2898DeriveBytes(password, salt, Iterations, HashAlgorithmName.SHA256))
                return Iterations + ":" + Convert.ToBase64String(salt) + ":" + Convert.ToBase64String(pbkdf2.GetBytes(32));
        }

        public static bool Verify(string password, string stored)
        {
            if (string.IsNullOrWhiteSpace(stored) || password == null) return false;
            var parts = stored.Split(':');
            if (parts.Length != 3 || !int.TryParse(parts[0], out var iterations) || (iterations < 100000 || iterations > 2000000)) return false;
            try
            {
                var salt = Convert.FromBase64String(parts[1]);
                var expected = Convert.FromBase64String(parts[2]);
                if (salt.Length != 16 || expected.Length != 32) return false;
                using (var pbkdf2 = new Rfc2898DeriveBytes(password, salt, iterations, HashAlgorithmName.SHA256))
                {
                    var actual = pbkdf2.GetBytes(expected.Length);
                    var difference = 0;
                    for (var i = 0; i < expected.Length; i++) difference |= expected[i] ^ actual[i];
                    return difference == 0;
                }
            }
            catch (FormatException) { return false; }
        }
    }
}
