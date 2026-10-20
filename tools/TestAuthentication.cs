using System;
using System.Security.Cryptography;
using NexusFreelance.Web.Services;
using NexusFreelance.Web.ViewModels;
public static class TestAuthentication
{
    private static int count;
    private static void Check(bool value, string label) { if (!value) throw new Exception(label); count++; Console.WriteLine("PASS " + label); }
    public static int Main()
    {
        var policy = new StrongPasswordAttribute();
        Check(!policy.IsValid("Ab!23456789"), "eleven character password rejected");
        Check(!policy.IsValid("Strongpassword123!"), "common password pattern rejected");
        Check(!policy.IsValid("alllowercase123!"), "missing uppercase rejected");
        Check(policy.IsValid("UniqueNexus!2026-Aa"), "strong password accepted");
        var encoded = PasswordService.Hash("UniqueNexus!2026-Aa");
        Check(encoded.StartsWith("600000:"), "new hash uses recommended PBKDF2 work factor");
        Check(PasswordService.Verify("UniqueNexus!2026-Aa", encoded), "correct password verifies");
        Check(!PasswordService.Verify("wrong password", encoded), "wrong password rejected");
        var salt = new byte[16]; using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(salt);
        string legacy; using (var derive = new Rfc2898DeriveBytes("LegacyNexus!2026", salt, 100000, HashAlgorithmName.SHA256))
            legacy = "100000:" + Convert.ToBase64String(salt) + ":" + Convert.ToBase64String(derive.GetBytes(32));
        Check(PasswordService.Verify("LegacyNexus!2026", legacy) && PasswordService.NeedsRehash(legacy), "legacy passwords remain usable and request upgrade");
        Check(!PasswordService.Verify("x", "2147483647:AA==:AA=="), "unbounded stored hash work factor rejected");
        Check(!PasswordService.Verify("x", "600000:invalid:invalid"), "malformed stored hash rejected");
        Console.WriteLine("AUTHENTICATION_TESTS_PASSED checks=" + count); return 0;
    }
}
