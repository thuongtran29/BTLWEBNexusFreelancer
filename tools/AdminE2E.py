"""One-shot isolated Admin HTTP + SQL E2E on NexusFreelanceMvp only.

Registers a disposable Admin candidate, bootstraps the first Admin via SQL,
tests security/moderation/audit, and removes the temporary Admin privilege.
Run with --run-writes; requires live IIS Express localhost:44391.
"""
import argparse
import html
from html.parser import HTMLParser
from pathlib import Path
import secrets
import subprocess
import uuid

import requests
import urllib3

urllib3.disable_warnings(urllib3.exceptions.InsecureRequestWarning)
BASE = "https://localhost:44391"
PASSWORD = secrets.token_urlsafe(24) + "A1!"


class Forms(HTMLParser):
    def __init__(self):
        super().__init__()
        self.forms = []
        self.current = None

    def handle_starttag(self, tag, raw_attrs):
        attrs = dict(raw_attrs)
        if tag == "form":
            self.current = {"action": attrs.get("action", ""), "fields": {}}
            self.forms.append(self.current)
        elif tag == "input" and self.current is not None and attrs.get("name"):
            self.current["fields"][attrs["name"]] = attrs.get("value", "")

    def handle_endtag(self, tag):
        if tag == "form":
            self.current = None


def request(session, method, path, status=200, **kwargs):
    response = session.request(method, BASE + path, verify=False, timeout=40, **kwargs)
    response.encoding = "utf-8"
    assert response.status_code == status, (
        f"{method} {path} returned {response.status_code}, expected {status}: "
        + html.unescape(response.text[:200]))
    return response


def form_for(page, action):
    parser = Forms()
    parser.feed(page)
    matching = [f for f in parser.forms if action in f["action"]]
    assert len(matching) == 1, (action, [f["action"] for f in parser.forms])
    return matching[0]


def post_form(session, form, status=200, **fields):
    action = form["action"]
    assert action.startswith("/") and not action.startswith("//")
    data = {**form["fields"], **fields}
    return request(session, "POST", action, status, data=data)


def register(name, email):
    client = requests.Session()
    form = form_for(request(client, "GET", "/Account/Register").text, "/Account/Register")
    result = post_form(client, form, Name=name, Email=email,
                       Password=PASSWORD, Role="Client")
    assert "Đăng xuất" in html.unescape(result.text)
    return client


def power_shell(script, email):
    subprocess.run(["powershell.exe", "-NoProfile", "-ExecutionPolicy", "Bypass",
                    "-File", str(Path(__file__).with_name(script)), "-Email", email],
                   check=True, timeout=45)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--run-writes", action="store_true")
    args = parser.parse_args()
    if not args.run_writes:
        parser.error("This E2E mutates NexusFreelanceMvp; supply --run-writes")

    suffix = uuid.uuid4().hex[:12]
    admin_email = f"admin-e2e-{suffix}@example.invalid"
    user_email = f"suspend-e2e-{suffix}@example.invalid"
    guest = requests.Session()
    request(guest, "GET", "/Admin", 302, allow_redirects=False)
    admin_session = register("Admin E2E", admin_email)
    user_session = register("Suspension Target E2E", user_email)
    request(user_session, "GET", "/Admin", 403)
    print("PASS Guest redirected; ordinary Client denied")

    elevated = False
    try:
        power_shell("BootstrapAdmin.ps1", admin_email)
        elevated = True
        dashboard = request(admin_session, "GET", "/Admin").text
        assert "Quản trị hệ thống" in dashboard
        assert 'href="/Admin/Users"' in dashboard
        assert 'href="/Admin/Audit"' in dashboard
        users = request(admin_session, "GET", "/Admin/Users?q=" + suffix).text
        assert user_email in users
        print("PASS Admin dashboard, filtered users")

        form = form_for(users, "/Admin/ChangeStatus") if users.count('/Admin/ChangeStatus') == 1 else None
        if form is None:
            parser = Forms()
            parser.feed(users)
            form = next(f for f in parser.forms if "/Admin/ChangeStatus" in f["action"]
                        and f["fields"].get("Suspend") == "true")
        missing_csrf = request(admin_session, "POST", "/Admin/ChangeStatus", 400,
                               data={"Id": form["fields"]["Id"], "Suspend": "true", "Reason": "E2E missing CSRF"})
        assert missing_csrf.status_code == 400
        own_page = request(user_session, "GET", "/Projects/Mine").text
        own_token = form_for(own_page, "/Account/Logout")["fields"]["__RequestVerificationToken"]
        request(user_session, "POST", "/Admin/ChangeStatus", 403,
                data={**form["fields"], "__RequestVerificationToken": own_token,
                      "Reason": "Cross-user request"},
                allow_redirects=False)
        print("PASS Admin mutation requires role and CSRF")

        user_id = form["fields"]["Id"]
        post_form(admin_session, form, Id=user_id, Suspend="true", Reason="Suspend E2E test")
        request(user_session, "GET", "/Projects/Mine", 403, allow_redirects=False)
        repeat = post_form(admin_session, form, 409, Id=user_id,
                           Suspend="true", Reason="Repeat E2E request")
        assert repeat.status_code == 409
        print("PASS Suspension revokes existing session; replay rejected")

        fresh_session = requests.Session()
        login_form = form_for(request(fresh_session, "GET", "/Account/Login").text,
                              "/Account/Login")
        denied_login = post_form(fresh_session, login_form,
                                 Email=user_email, Password=PASSWORD)
        assert "tạm khóa" in html.unescape(denied_login.text)
        print("PASS Suspended account cannot log in")

        users_after = request(admin_session, "GET", "/Admin/Users?q=" + suffix).text
        parser = Forms()
        parser.feed(users_after)
        restore = next(f for f in parser.forms if "/Admin/ChangeStatus" in f["action"]
                       and f["fields"].get("Id") == user_id
                       and f["fields"].get("Suspend") == "false")
        post_form(admin_session, restore, Reason="Restore E2E test")
        fresh_session = requests.Session()
        login = form_for(request(fresh_session, "GET", "/Account/Login").text, "/Account/Login")
        logged_in = post_form(fresh_session, login, Email=user_email, Password=PASSWORD)
        assert "Đăng xuất" in html.unescape(logged_in.text)
        audit = request(admin_session, "GET", "/Admin/Audit").text
        assert "SuspendUser" in audit and "RestoreUser" in audit
        assert "Suspend E2E test" in audit and "Restore E2E test" in audit
        print("PASS Restore, re-login, persisted audit (2 events)")
        print("ADMIN_E2E_PASSED=7")
    finally:
        if elevated:
            power_shell("ResetTestAdmin.ps1", admin_email)


if __name__ == "__main__":
    main()
