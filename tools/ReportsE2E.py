"""Project abuse report HTTP and SQL integration tests, on the local development DB.

Run explicitly with --run-writes after SQL Server and IIS Express are running.
Creates disposable users and projects. Admin privileges are reverted in finally.
"""

import argparse
import html
import re
import shutil
import subprocess
import uuid
from concurrent.futures import ThreadPoolExecutor
from threading import Barrier

import requests

from AdminE2E import PASSWORD, Forms, form_for, power_shell, request

CHECK_COUNT = 0


def check(label, operation):
    global CHECK_COUNT
    result = operation()
    CHECK_COUNT += 1
    print("PASS " + label, flush=True)
    return result


def register(role, email, name=None):
    session = requests.Session()
    form = form_for(request(session, "GET", "/Account/Register").text, "/Account/Register")
    response = request(session, "POST", form["action"], data={
        **form["fields"], "Name": name or ("Report Test " + role), "Email": email,
        "Password": PASSWORD, "Role": role,
    })
    assert "Đăng xuất" in html.unescape(response.text)
    return session


def all_forms(markup):
    parser = Forms()
    parser.feed(markup)
    return parser.forms


def csrf(session, page, action=None):
    markup = request(session, "GET", page).text
    forms = all_forms(markup)
    forms = [f for f in forms if action in f["action"]] if action else forms
    tokens = [f["fields"].get("__RequestVerificationToken") for f in forms]
    return next(token for token in tokens if token)


def submit(session, project_id, marker, category="Fraud", status=302, with_csrf=True):
    data = {"ProjectId": project_id, "Category": category, "Details": marker}
    if with_csrf:
        data["__RequestVerificationToken"] = csrf(
            session, f"/Projects/Details/{project_id}", "/Reports/Submit")
    return request(session, "POST", "/Reports/Submit", status,
                   allow_redirects=False, data=data)


def create_project(owner, suffix, title):
    fields = {
        "Title": title + " " + suffix,
        "Description": "Project created to verify report moderation invariants.",
        "ProjectType": "SOLO", "Budget": "1000000",
        "__RequestVerificationToken": csrf(owner, "/Projects/Create", "/Projects/Create"),
    }
    result = request(owner, "POST", "/Projects/Create", 302,
                     data=fields, allow_redirects=False)
    match = re.search(r"/Manage/(\d+)", result.headers["Location"])
    assert match, result.headers["Location"]
    project_id = int(match.group(1))
    manage = f"/Projects/Manage/{project_id}"
    token = csrf(owner, manage, "/Projects/AddRequirement")
    request(owner, "POST", "/Projects/AddRequirement", 302,
            data={"ProjectId": project_id, "Title": "Backend Developer", "RequiredCount": 1,
                  "__RequestVerificationToken": token}, allow_redirects=False)
    return project_id, manage


def publish(owner, project_id, manage):
    request(owner, "POST", f"/Projects/Publish/{project_id}", 302,
            data={"__RequestVerificationToken": csrf(owner, manage,
                                                       f"/Projects/Publish/{project_id}")},
            allow_redirects=False)


def moderation_form(admin, marker):
    markup = request(admin, "GET", "/Admin/Reports?status=Pending").text
    for fragment in markup.split('<article class="report-row">')[1:]:
        if marker not in html.unescape(fragment.split("</article>", 1)[0]):
            continue
        form = form_for(fragment.split("</article>", 1)[0], "/Admin/ReviewReport")
        assert int(form["fields"]["Id"]) > 0
        return form
    raise AssertionError("Report absent from administrator's Pending list: " + marker)


def review(admin, form, decision, explanation, expected=302):
    return request(admin, "POST", "/Admin/ReviewReport", expected,
                   data={**form["fields"], "Decision": decision,
                         "Resolution": explanation}, allow_redirects=False)


def sql_verify(project_id, concurrency_project_id, owner_email, client_email,
               freelancer_email, admin_email):
    executable = shutil.which("sqlcmd") or (
        r"C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\180\Tools\Binn\SQLCMD.EXE")
    quoted = [value.replace("'", "''") for value in (
        owner_email, client_email, freelancer_email, admin_email)]
    owner, client, freelancer, admin = quoted
    query = f"""
SET NOCOUNT ON;
DECLARE @p int={int(project_id)}, @race int={int(concurrency_project_id)};
DECLARE @owner int=(SELECT Id FROM dbo.Users WHERE Email=N'{owner}');
DECLARE @client int=(SELECT Id FROM dbo.Users WHERE Email=N'{client}');
DECLARE @freelancer int=(SELECT Id FROM dbo.Users WHERE Email=N'{freelancer}');
DECLARE @admin int=(SELECT Id FROM dbo.Users WHERE Email=N'{admin}');
IF @owner IS NULL OR @client IS NULL OR @freelancer IS NULL OR @admin IS NULL
    THROW 51040, 'Users missing', 1;
IF (SELECT COUNT(*) FROM dbo.ProjectAbuseReports
    WHERE ProjectId=@p AND ((ReporterId=@client AND Status='Confirmed')
                         OR (ReporterId=@freelancer AND Status='Dismissed'))
    AND ReviewerId=@admin AND ReviewedAtUtc IS NOT NULL
    AND LEN(Resolution)>=5)<>2
    THROW 51041, 'Moderation rows missing or invalid', 1;
IF (SELECT COUNT(*) FROM dbo.ProjectAbuseReports
    WHERE ProjectId=@race AND ReporterId=@freelancer AND Status='Pending')<>1
    THROW 51042, 'Concurrent submissions created duplicate pending rows', 1;
IF (SELECT COUNT(*) FROM dbo.ProjectAbuseReports
    WHERE ProjectId=@p AND ReporterId=@client AND Status='Pending'
      AND Category='Other')<>1
    THROW 51045, 'New pending report after moderation missing or duplicated', 1;
IF (SELECT COUNT(*) FROM dbo.AdminAuditLogs
    WHERE Action='ReviewProjectReport' AND ActorId=@admin AND TargetUserId=@owner
      AND Reason IN ('Confirmed abuse report E2E','Dismissed abuse report E2E'))<>2
    THROW 51043, 'Moderation audit missing', 1;
IF (SELECT COUNT(*) FROM dbo.Notifications
    WHERE RecipientId IN (@client,@freelancer) AND TargetPath='/Reports/Mine'
    AND Title LIKE N'Báo cáo dự án%')<2
    THROW 51044, 'Reporter notifications missing', 1;
SELECT 'REPORTS_SQL_VALIDATED';
"""
    proc = subprocess.run([
        executable, "-S", r".\SQLEXPRESS", "-d", "NexusFreelanceMvp",
        "-E", "-C", "-b", "-l", "10", "-Q", query,
    ], capture_output=True, text=True, timeout=55)
    if proc.returncode or "REPORTS_SQL_VALIDATED" not in proc.stdout:
        raise AssertionError("SQL persistence verification failed: " +
                             (proc.stdout + proc.stderr)[-1300:])


def run():
    suffix = uuid.uuid4().hex[:10]
    emails = [f"report-{role}-{suffix}@example.invalid" for role in
              ("owner", "client", "freelancer", "admin")]
    owner = register("Client", emails[0])
    client = register("Client", emails[1])
    freelancer = register("Freelancer", emails[2])
    admin = register("Client", emails[3], name="Admin E2E")
    elevated = False
    try:
        check("Anonymous Mine requires authentication", lambda:
              request(requests.Session(), "GET", "/Reports/Mine", 302,
                      allow_redirects=False))
        check("Client cannot open admin reports", lambda:
              request(client, "GET", "/Admin/Reports", 403))
        project, manage = create_project(owner, suffix, "Moderation review")
        check("Draft project cannot be reported", lambda:
              request(freelancer, "POST", "/Reports/Submit", 409,
                      data={"ProjectId": project, "Category": "Fraud",
                            "Details": "Draft report is invalid and must be rejected.",
                            "__RequestVerificationToken": csrf(freelancer, "/")},
                      allow_redirects=False))
        publish(owner, project, manage)
        check("Anonymous cannot submit report", lambda:
              request(requests.Session(), "POST", "/Reports/Submit", 302,
                      allow_redirects=False, data={"ProjectId": project}))
        check("Missing CSRF rejected", lambda:
              submit(client, project, "Missing CSRF should be rejected by server.",
                     status=400, with_csrf=False))
        check("Invalid category rejected", lambda:
              submit(client, project, "Invalid category must be rejected by server.",
                     category="Incorrect", status=400))
        check("Short details rejected", lambda:
              submit(client, project, "too short", status=400))
        check("Project owner cannot report own project", lambda:
              request(owner, "POST", "/Reports/Submit", 403, allow_redirects=False,
                      data={"ProjectId": project, "Category": "Fraud",
                            "Details": "Owner must not report their own project.",
                            "__RequestVerificationToken": csrf(owner, manage)}))
        marker_client = "Client report " + suffix + " with credible detailed evidence."
        marker_freelancer = "Freelancer report " + suffix + " with detailed evidence."
        check("Client submits a report", lambda:
              submit(client, project, marker_client))
        check("Freelancer submits a report", lambda:
              submit(freelancer, project, marker_freelancer, category="Spam"))
        check("Duplicate pending report returns conflict", lambda:
              submit(client, project, marker_client, status=409))
        client_mine = request(client, "GET", "/Reports/Mine").text
        freelancer_mine = request(freelancer, "GET", "/Reports/Mine").text
        assert marker_client in client_mine and marker_freelancer not in client_mine
        assert marker_freelancer in freelancer_mine and marker_client not in freelancer_mine
        print("PASS Reporter ownership on Mine listing", flush=True)
        power_shell("BootstrapAdmin.ps1", emails[3])
        elevated = True
        admin_reports = request(admin, "GET", "/Admin/Reports?status=Pending").text
        assert marker_client in admin_reports and marker_freelancer in admin_reports
        print("PASS Admin can inspect pending reports", flush=True)
        check("Admin cannot submit user report", lambda:
              request(admin, "POST", "/Reports/Submit", 403, allow_redirects=False,
                      data={"ProjectId": project, "Category": "Fraud",
                            "Details": "Admin should not submit user reports.",
                            "__RequestVerificationToken": csrf(admin, "/Admin/Reports")}))
        form_client = moderation_form(admin, marker_client)
        check("Client cannot moderate reports", lambda:
              request(client, "POST", "/Admin/ReviewReport", 403,
                      allow_redirects=False, data={**form_client["fields"],
                                                   "Decision": "Confirmed",
                                                   "Resolution": "Unauthorized moderation",
                                                   "__RequestVerificationToken": csrf(client, "/Reports/Mine")}))
        check("Missing admin CSRF rejected", lambda:
              request(admin, "POST", "/Admin/ReviewReport", 400,
                      allow_redirects=False, data={"Id": form_client["fields"]["Id"],
                                                   "Decision": "Confirmed",
                                                   "Resolution": "CSRF not provided"}))
        check("Invalid admin resolution rejected", lambda:
              review(admin, form_client, "Confirmed", "bad", expected=400))
        check("Invalid admin decision rejected", lambda:
              review(admin, form_client, "Invalid", "Unrecognized decision", expected=400))
        check("Admin confirms report", lambda:
              review(admin, form_client, "Confirmed", "Confirmed abuse report E2E"))
        check("Admin rejects replay", lambda:
              review(admin, form_client, "Confirmed", "Repeated decision", expected=409))
        form_freelancer = moderation_form(admin, marker_freelancer)
        check("Admin dismisses second report", lambda:
              review(admin, form_freelancer, "Dismissed", "Dismissed abuse report E2E"))
        assert "Confirmed" in request(client, "GET", "/Reports/Mine").text
        assert "Dismissed" in request(freelancer, "GET", "/Reports/Mine").text
        print("PASS Reporters receive moderation outcomes", flush=True)

        # Closing an old Pending report permits a new report from the same user,
        # while the filtered index still protects the new Pending record.
        reopened = "Client reopened report " + suffix + " with new supporting evidence."
        check("New report allowed after earlier decision", lambda:
              submit(client, project, reopened, category="Other"))
        check("Reopened Pending report cannot be duplicated", lambda:
              submit(client, project, reopened, status=409))

        race_project, race_manage = create_project(owner, suffix, "Concurrent report")
        publish(owner, race_project, race_manage)
        sessions = [requests.Session(), requests.Session()]
        for session in sessions:
            session.cookies.update(freelancer.cookies)
        tokens = [csrf(s, f"/Projects/Details/{race_project}", "/Reports/Submit")
                  for s in sessions]
        barrier = Barrier(2)

        # A race has nondeterministic winners: inspect both statuses independently.
        def concurrent_submits():
            def post(index):
                barrier.wait(timeout=20)
                response = sessions[index].post(
                    "https://localhost:44391/Reports/Submit", verify=False,
                    allow_redirects=False, timeout=40, data={
                        "ProjectId": race_project, "Category": "Spam",
                        "Details": "Concurrent submission " + suffix + " long enough.",
                        "__RequestVerificationToken": tokens[index],
                    })
                return response.status_code

            with ThreadPoolExecutor(max_workers=2) as pool:
                statuses = list(pool.map(post, range(2)))
            assert sorted(statuses) == [302, 409], statuses

        check("Concurrent duplicate submissions serialize (302 + 409)", concurrent_submits)
        check("Persisted SQL reports, audit and notifications", lambda:
              sql_verify(project, race_project, *emails))
        print(f"REPORTS_E2E_PASSED={CHECK_COUNT}", flush=True)
    finally:
        if elevated:
            power_shell("ResetTestAdmin.ps1", emails[3])


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--run-writes", action="store_true")
    args = parser.parse_args()
    if not args.run_writes:
        parser.error("Writes NexusFreelanceMvp development SQL DB; add --run-writes")
    run()
