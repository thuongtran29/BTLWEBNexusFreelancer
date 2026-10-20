"""Localhost regression; creates one disposable user and removes only its rows."""
import concurrent.futures
import hashlib
import secrets
import subprocess
import uuid
from html.parser import HTMLParser
from html import unescape

import requests
import urllib3

BASE = "https://localhost:44391"
SQLCMD = r"C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\180\Tools\Binn\SQLCMD.EXE"
checks = 0


class Fields(HTMLParser):
    def __init__(self, html):
        super().__init__()
        self.token = None
        self.role = None
        self.feed(html)

    def handle_starttag(self, tag, attrs):
        attrs = dict(attrs)
        if tag == "input" and attrs.get("name") == "__RequestVerificationToken" and self.token is None:
            self.token = attrs.get("value")
        if tag == "option" and "selected" in attrs:
            self.role = attrs.get("value")


def sql(query):
    result = subprocess.run([SQLCMD, "-S", r".\SQLEXPRESS", "-d", "NexusFreelanceMvp", "-E", "-C", "-b", "-h", "-1", "-W", "-Q", "SET NOCOUNT ON; SET QUOTED_IDENTIFIER ON; " + query], capture_output=True, text=True, timeout=35)
    if result.returncode:
        raise AssertionError("SQL fixture failed: " + result.stdout.strip() + result.stderr.strip())
    return result.stdout.strip()


def session():
    s = requests.Session()
    s.verify = False
    return s


def get(s, path):
    return s.get(BASE + path, timeout=45)


def post(s, path, data, form_path=None, follow=False):
    page = get(s, form_path or path)
    token = Fields(page.text).token
    assert token, "Missing CSRF field on " + (form_path or path)
    return s.post(BASE + path, data={**data, "__RequestVerificationToken": token}, allow_redirects=follow, timeout=45)


def check(ok, label):
    global checks
    assert ok, label
    checks += 1
    print("PASS " + label, flush=True)


def main():
    urllib3.disable_warnings(urllib3.exceptions.InsecureRequestWarning)
    uid = None
    email = "http-recovery-" + uuid.uuid4().hex + "@example.test"
    try:
        s = session()
        p = get(s, "/Account/ForgotPassword?role=Freelancer")
        check(p.status_code == 200 and 'data-recovery-form="true"' in unescape(p.text) and "recovery-form.js" in unescape(p.text), "recovery form loads processing feedback")
        selected = Fields(p.text).role
        check(selected == "Freelancer", "requested role is selected")
        p = post(s, "/Account/ForgotPassword", {"Email": "not-an-email", "Role": "Freelancer"})
        selected = Fields(p.text).role
        check(p.status_code == 200 and "chưa đúng định dạng" in unescape(p.text) and selected == "Freelancer", "invalid email has Vietnamese feedback and retains role")
        p = post(s, "/Account/ForgotPassword", {"Email": email})
        check(p.status_code == 200 and "chọn đúng vai trò" in unescape(p.text), "missing role rejected")
        for path in ["/Account/ForgotPassword", "/Account/ResetPassword", "/Account/ConfirmRegistration", "/Account/ResendRegistrationCode", "/Account/ResendVerification", "/Account/VerifyEmail"]:
            p = s.post(BASE + path, data={}, timeout=45)
            check(p.status_code == 200 and "Yêu cầu vừa gửi chưa được thực hiện" in unescape(p.text), "stale or missing CSRF safely recovers: " + path + " HTTP=" + str(p.status_code))
        p = post(s, "/Account/ForgotPassword", {"Email": "<script>alert(1)</script>", "Role": "Client"})
        check(p.status_code == 400 and "Chưa thể thực hiện yêu cầu" in unescape(p.text) and "Server Error" not in unescape(p.text), "hostile request input rejected with actionable HTTP 400")
        for path in ["/Account/ConfirmRegistration", "/Contracts/Details/0"]:
            p = get(s, path)
            # Guest contract request may go to Login; malformed registration must remain rejected.
            check((p.status_code == 400 and "Chưa thể thực hiện yêu cầu" in unescape(p.text)) if path.startswith("/Account") else p.status_code in (200,403,404), "malformed or unauthorized route has a controlled response: " + path)
        for attempt in range(4):
            p = post(s, "/Account/ForgotPassword", {"Email": email, "Role": "Client"})
            check((p.status_code == 200 and "Đã tiếp nhận yêu cầu khôi phục" in unescape(p.text)) if attempt < 3 else (p.status_code == 429 and "quá nhiều yêu cầu" in unescape(p.text)), "recovery acknowledgement / rate limit attempt " + str(attempt + 1))

        uid = int(sql("INSERT Users(Name,Email,PasswordHash,Role,JoinedAtUtc,IsSuspended) VALUES(N'Disposable HTTP recovery fixture',N'" + email + "','disabled-fixture-hash','Client',SYSUTCDATETIME(),0); SELECT CONVERT(int,SCOPE_IDENTITY());"))
        code = "".join(secrets.choice("0123456789") for _ in range(6))
        salt = secrets.token_urlsafe(32)
        digest = hashlib.sha256((salt + code).encode("ascii")).hexdigest().upper()
        sql(f"INSERT RegistrationCodes(UserAccountId,Salt,CodeHash,CreatedAtUtc,ExpiresAtUtc,FailedAttempts) VALUES({uid},'{salt}','{digest}',SYSUTCDATETIME(),DATEADD(minute,10,SYSUTCDATETIME()),0);")
        confirm = "/Account/ConfirmRegistration?email=" + email + "&role=Client"
        a, b = session(), session()
        # Obtain each browser's independent anti-forgery token before racing the POSTs.
        tokens = []
        for browser in [a, b]:
            page = get(browser, confirm)
            tokens.append(Fields(page.text).token)

        def confirm_once(pair):
            browser, token = pair
            return browser.post(BASE + "/Account/ConfirmRegistration", data={"Email": email, "Role": "Client", "Code": code, "__RequestVerificationToken": token}, allow_redirects=False, timeout=45)

        with concurrent.futures.ThreadPoolExecutor(max_workers=2) as pool:
            results = list(pool.map(confirm_once, zip([a,b], tokens)))
        check(sorted(r.status_code for r in results) == [200,302], "two browser sessions: exactly one OTP confirmation succeeds")
        rejected = next(r for r in results if r.status_code == 200)
        check("đã dùng" in unescape(rejected.text), "second browser sees explicit used/invalid-code feedback")
        p = post(session(), "/Account/ConfirmRegistration", {"Email": email,"Role":"Client","Code":code}, confirm)
        check(p.status_code == 200 and "đã dùng" in unescape(p.text), "third browser cannot reuse consumed OTP")
        check(sql(f"SELECT COUNT(*) FROM RegistrationCodes WHERE UserAccountId={uid} AND ConsumedAtUtc IS NULL;") == "0", "no active registration codes remain")

        raw = secrets.token_urlsafe(32)
        digest = hashlib.sha256(raw.encode("ascii")).hexdigest().upper()
        sql(f"INSERT PasswordResetTokens(UserAccountId,TokenHash,CreatedAtUtc,ExpiresAtUtc) VALUES({uid},'{digest}',SYSUTCDATETIME(),DATEADD(minute,30,SYSUTCDATETIME()));")
        reset = "/Account/ResetPassword?token=" + raw
        p = post(s, "/Account/ResetPassword", {"Token":raw,"Password":"weak","ConfirmPassword":"weak"}, reset)
        check(p.status_code == 200 and 'name="Password"' in unescape(p.text) and "validation-summary-errors" in unescape(p.text), "weak reset password rejected with validation feedback")
        password = secrets.token_urlsafe(24) + "Aa1!"
        p = post(s, "/Account/ResetPassword", {"Token":raw,"Password":password,"ConfirmPassword":password}, reset)
        check(p.status_code == 302 and "/Account/Login" in p.headers.get("Location",""), "valid reset completes for disposable account")
        p = get(session(), reset)
        check(p.status_code == 200 and 'name="Password"' not in unescape(p.text), "used reset link cannot be replayed")
        p = post(s, "/Account/Login", {"Email":email,"Password":password,"Role":"Client"})
        check(p.status_code == 302 and "/Dashboard" in p.headers.get("Location",""), "new password authenticates after reset")
        check(sql(f"SELECT COUNT(*) FROM PasswordResetTokens WHERE UserAccountId={uid} AND ConsumedAtUtc IS NULL;") == "0", "reset consumes outstanding reset tokens")
        print("RECOVERY_HTTP_CHECKS=" + str(checks), flush=True)
    finally:
        if uid is not None:
            sql(f"DELETE r FROM PasswordResetTokens r JOIN Users u ON u.Id=r.UserAccountId WHERE u.Id={uid} AND u.Email='{email}'; DELETE r FROM RegistrationCodes r JOIN Users u ON u.Id=r.UserAccountId WHERE u.Id={uid} AND u.Email='{email}'; DELETE FROM Users WHERE Id={uid} AND Email='{email}';")
        print("Disposable HTTP fixtures cleaned; existing users/passwords unchanged.", flush=True)


if __name__ == "__main__":
    main()
