"""Reset/verify HTTP + SQL tests using hashed token fixtures; no SMTP delivery.

--run-writes creates a throwaway account and token rows in NexusFreelanceMvp.
This checks consuming emailed tokens, expiry, replay and session invalidation;
it does not validate a real mail server or the optional registration email gate.
"""
import argparse
import hashlib
import secrets
import uuid

import requests
import urllib3

from ConcurrencyE2E import sql_scalar
from SmokeE2E import Smoke


def equal(actual, expected):
    assert actual == expected, (actual, expected)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--run-writes", action="store_true")
    if not parser.parse_args().run_writes:
        parser.error("Creates NexusFreelanceMvp token fixtures; add --run-writes")
    urllib3.disable_warnings(urllib3.exceptions.InsecureRequestWarning)
    smoke = Smoke("https://localhost:44391")
    owner, guest = requests.Session(), requests.Session()
    owner.verify = guest.verify = False
    email = "recovery-" + uuid.uuid4().hex[:12] + "@example.invalid"
    old_password = secrets.token_urlsafe(22) + "A1!"
    new_password = secrets.token_urlsafe(22) + "B2!"
    smoke.post(owner, "/Account/Register", {"Name": "Recovery fixture", "Email": email,
               "Password": old_password, "Role": "Client"}, "/Account/Register")
    uid = sql_scalar(f"SELECT Id FROM dbo.Users WHERE Email=N'{email}'")

    def token(table, expired=False):
        assert table in ("PasswordResetTokens", "EmailVerificationTokens")
        raw = secrets.token_urlsafe(32)
        digest = hashlib.sha256(raw.encode("ascii")).hexdigest().upper()
        expiry_minutes = -1 if expired else 30
        sql_scalar(f"INSERT dbo.{table}(UserAccountId,TokenHash,CreatedAtUtc,ExpiresAtUtc) "
                   f"VALUES({uid},'{digest}',DATEADD(hour,-1,SYSUTCDATETIME()),"
                   f"DATEADD(minute,{expiry_minutes},SYSUTCDATETIME())); SELECT CONVERT(int,SCOPE_IDENTITY());")
        return raw

    expired = token("PasswordResetTokens", expired=True)
    expired_page = smoke.get(guest, "/Account/ResetPassword?token=" + expired)
    smoke.check("Expired reset token cannot open reset form", lambda: equal('name="Password"' in expired_page.text, False))
    verify_expired = token("EmailVerificationTokens", expired=True)
    expired_page = smoke.get(guest, "/Account/VerifyEmail?token=" + verify_expired)
    smoke.check("Expired email token cannot open verification form", lambda: equal('name="Token"' in expired_page.text, False))

    verify = token("EmailVerificationTokens")
    verify_path = "/Account/VerifyEmail?token=" + verify
    csrf = guest.post(smoke.base + "/Account/VerifyEmail", data={"Token": verify}, allow_redirects=False, timeout=35)
    smoke.check("Email verification requires CSRF", lambda: equal(csrf.status_code, 400))
    smoke.post(guest, "/Account/VerifyEmail", {"Token": verify}, verify_path)
    smoke.check("Email verification persisted", lambda: equal(sql_scalar(
        f"SELECT COUNT(*) FROM dbo.Users WHERE Id={uid} AND EmailVerifiedAtUtc IS NOT NULL"), 1))
    used = smoke.get(guest, verify_path)
    smoke.check("Email verification cannot be replayed", lambda: equal('name="Token"' in used.text, False))

    reset = token("PasswordResetTokens")
    token("PasswordResetTokens")  # A second link must also be invalidated after reset.
    reset_path = "/Account/ResetPassword?token=" + reset
    payload = {"Token": reset, "Password": new_password, "ConfirmPassword": new_password}
    csrf = guest.post(smoke.base + "/Account/ResetPassword", data=payload, allow_redirects=False, timeout=35)
    smoke.check("Password reset requires CSRF", lambda: equal(csrf.status_code, 400))
    mismatch = smoke.post(guest, "/Account/ResetPassword", {**payload, "ConfirmPassword": "wrong-value"},
                          reset_path, expect_redirect=False)
    smoke.check("Password confirmation mismatch is rejected", lambda: equal(mismatch.status_code, 200))
    smoke.check("Invalid reset leaves password revision unchanged", lambda: equal(sql_scalar(
        f"SELECT COUNT(*) FROM dbo.Users WHERE Id={uid} AND PasswordChangedAtUtc IS NULL"), 1))
    smoke.post(guest, "/Account/ResetPassword", payload, reset_path)
    smoke.check("Password change revision persisted", lambda: equal(sql_scalar(
        f"SELECT COUNT(*) FROM dbo.Users WHERE Id={uid} AND PasswordChangedAtUtc IS NOT NULL"), 1))
    smoke.check("All outstanding reset links consumed", lambda: equal(sql_scalar(
        f"SELECT COUNT(*) FROM dbo.PasswordResetTokens WHERE UserAccountId={uid} AND ConsumedAtUtc IS NULL"), 0))
    stale = owner.get(smoke.base + "/Projects/Mine", allow_redirects=False, timeout=35)
    smoke.check("Reset revokes previous authenticated session", lambda: equal(
        (stale.status_code, ".ASPXAUTH" in owner.cookies), (403, False)))
    used = smoke.get(guest, reset_path)
    smoke.check("Reset link cannot be replayed", lambda: equal('name="Password"' in used.text, False))
    invalid_login = smoke.post(guest, "/Account/Login", {"Email": email, "Password": old_password},
                               "/Account/Login", expect_redirect=False)
    smoke.check("Old password no longer authenticates", lambda: equal(invalid_login.status_code, 200))
    valid_login = smoke.post(guest, "/Account/Login", {"Email": email, "Password": new_password}, "/Account/Login")
    smoke.check("New password authenticates", lambda: equal(valid_login.status_code, 302))
    print(f"ACCOUNT_RECOVERY_E2E_PASSED={len(smoke.rows)} SMTP_TESTED=False")


if __name__ == "__main__":
    main()
