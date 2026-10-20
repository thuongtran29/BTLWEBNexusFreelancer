"""Repeatable local MVC5 HTTP smoke test (uses throwaway users/projects).

Run: python tools/SmokeE2E.py --run-writes
Requires: pip install requests
Requires: IIS Express on https://localhost:44391 and NexusFreelanceMvp test DB.
"""

import argparse
from datetime import datetime, timedelta, timezone
import html
import re
import subprocess
import sys
import uuid
from html.parser import HTMLParser
from urllib.parse import urljoin, urlsplit, parse_qs

import requests
import urllib3


class FormParser(HTMLParser):
    def __init__(self):
        super().__init__()
        self.forms = []
        self.active = None

    def handle_starttag(self, tag, raw_attrs):
        attrs = dict(raw_attrs)
        if tag == "form":
            self.active = {"action": attrs.get("action", ""), "method": attrs.get("method", "get"), "fields": {}}
            self.forms.append(self.active)
        elif self.active is not None and tag == "input" and attrs.get("name"):
            self.active["fields"][attrs["name"]] = attrs.get("value", "")

    def handle_endtag(self, tag):
        if tag == "form":
            self.active = None


def forms(markup):
    parser = FormParser()
    parser.feed(markup)
    return parser.forms


def error_excerpt(response):
    title = re.search(r"<title[^>]*>(.*?)</title>", response.text, re.I | re.S)
    summary = re.search(r'<div[^>]*class="(?:error-list|alert danger)[^"]*"[^>]*>(.*?)</div>', response.text, re.I | re.S)
    snippet = summary.group(1) if summary else (title.group(1) if title else response.text[:300])
    return re.sub(r"\s+", " ", re.sub(r"<[^>]*>", " ", html.unescape(snippet)))[:350]


class Smoke:
    def __init__(self, base):
        self.base = base.rstrip("/")
        self.rows = []

    def get(self, sess, path, status=200):
        r = sess.get(urljoin(self.base + "/", path.lstrip("/")), timeout=35)
        # IIS Express omits the charset on some local text/html responses.
        # Razor output is UTF-8; decoding explicitly preserves Vietnamese assertions.
        r.encoding = "utf-8"
        if r.status_code != status:
            raise AssertionError(f"GET {path}: HTTP {r.status_code}, expected {status}; {error_excerpt(r)}")
        return r

    def post(self, sess, path, fields, form_page=None, expect_redirect=True):
        if form_page:
            r = self.get(sess, form_page)
            tokens = [f["fields"].get("__RequestVerificationToken") for f in forms(r.text)]
            token = next((x for x in tokens if x), None)
            if not token:
                raise AssertionError(f"No anti-forgery token on {form_page}")
            fields = {"__RequestVerificationToken": token, **fields}
        r = sess.post(urljoin(self.base + "/", path.lstrip("/")), data=fields,
                      timeout=35, allow_redirects=False)
        if expect_redirect and r.status_code not in (301, 302, 303):
            raise AssertionError(f"POST {path}: HTTP {r.status_code}, expected redirect; {error_excerpt(r)}")
        return r

    def check(self, label, callback):
        result = callback()
        self.rows.append(label)
        print(f"PASS {label}", flush=True)
        return result


def route_id(location):
    m = re.search(r"/Manage/(\d+)", location or "", re.I)
    if not m:
        q = parse_qs(urlsplit(location or "").query)
        if "id" in q:
            return int(q["id"][0])
        raise AssertionError(f"Project id not found in redirect: {location}")
    return int(m.group(1))


def verify_sql_state(project_id, client_email, freelancer_email):
    """Independently verify persisted E2E invariants in the dedicated MVP database.

    SQLCMD -b makes any THROW or query error fail the entire E2E run. Generated
    test emails and integer project IDs scope every check to this run.
    """
    import shutil

    sqlcmd = shutil.which("sqlcmd")
    if not sqlcmd:
        sqlcmd = r"C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\180\Tools\Binn\SQLCMD.EXE"
    pid = int(project_id)
    ce = client_email.replace("'", "''")
    fe = freelancer_email.replace("'", "''")
    query = f"""
SET NOCOUNT ON;
DECLARE @project int = {pid};
DECLARE @client int = (SELECT Id FROM dbo.Users WHERE Email=N'{ce}');
DECLARE @freelancer int = (SELECT Id FROM dbo.Users WHERE Email=N'{fe}');
IF @client IS NULL OR @freelancer IS NULL THROW 51001, 'E2E users missing', 1;
IF (SELECT COUNT(*) FROM dbo.Projects WHERE Id=@project AND ClientId=@client AND Status='Completed') <> 1
    THROW 51002, 'Project state or owner mismatch', 1;
IF (SELECT COUNT(*) FROM dbo.Proposals WHERE ProjectId=@project AND ApplicantId=@freelancer AND Status='Accepted') <> 1
    THROW 51003, 'Accepted proposal mismatch', 1;
IF (SELECT COUNT(*) FROM dbo.MatchingResults m JOIN dbo.Proposals p ON p.Id=m.ProposalId
    WHERE p.ProjectId=@project AND p.ApplicantId=@freelancer AND m.TotalPoints =
        m.CoreRolePoints+m.BadgePoints+m.PortfolioPoints+m.AvailabilityPoints+m.ReliabilityPoints+m.BudgetPoints
        AND m.TotalPoints BETWEEN 0 AND 100) <> 1
    THROW 51004, 'Matching snapshot missing or invalid', 1;
IF (SELECT COUNT(*) FROM dbo.RoleAssignments WHERE ProjectId=@project AND FreelancerId=@freelancer) <> 1
    THROW 51005, 'Role assignment mismatch', 1;
IF (SELECT COUNT(*) FROM dbo.TeamMembers tm JOIN dbo.ProjectTeams t ON t.Id=tm.ProjectTeamId
    WHERE t.ProjectId=@project AND t.Locked=1 AND tm.UserAccountId=@freelancer) <> 1
    THROW 51006, 'Team membership or locked status mismatch', 1;
IF (SELECT COUNT(*) FROM dbo.Workspaces WHERE ProjectId=@project) <> 1
    THROW 51007, 'Workspace missing', 1;
IF (SELECT COUNT(*) FROM dbo.Tasks t JOIN dbo.Workspaces w ON w.Id=t.WorkspaceId
    WHERE w.ProjectId=@project AND t.Title LIKE 'Implement backend endpoints %'
    AND t.Priority='High' AND t.DueAtUtc IS NOT NULL AND t.Status='Doing'
    AND t.AssigneeId=@freelancer) <> 1
    THROW 51023, 'Task priority, due date, assignee or state missing', 1;
IF (SELECT COUNT(*) FROM dbo.TaskHistories h JOIN dbo.Tasks t ON t.Id=h.WorkTaskId
    JOIN dbo.Workspaces w ON w.Id=t.WorkspaceId WHERE w.ProjectId=@project) <> 2
    THROW 51024, 'Expected exactly two task history events', 1;
IF NOT EXISTS (SELECT 1 FROM dbo.TaskHistories h JOIN dbo.Tasks t ON t.Id=h.WorkTaskId
    JOIN dbo.Workspaces w ON w.Id=t.WorkspaceId WHERE w.ProjectId=@project
    AND h.EventType='Created' AND h.ActorId=@client AND h.OldStatus IS NULL AND h.NewStatus='ToDo')
    OR NOT EXISTS (SELECT 1 FROM dbo.TaskHistories h JOIN dbo.Tasks t ON t.Id=h.WorkTaskId
    JOIN dbo.Workspaces w ON w.Id=t.WorkspaceId WHERE w.ProjectId=@project
    AND h.EventType='StatusChanged' AND h.ActorId=@freelancer AND h.OldStatus='ToDo' AND h.NewStatus='Doing')
    THROW 51025, 'Task history actor or transition mismatch', 1;
IF (SELECT COUNT(*) FROM dbo.ProjectMilestones m JOIN dbo.Workspaces w ON w.Id=m.WorkspaceId
    WHERE w.ProjectId=@project AND m.Title LIKE 'E2E Milestone %' AND m.Status='Completed'
    AND m.CompletedAtUtc IS NOT NULL AND m.CompletedAtUtc>=m.CreatedAtUtc AND m.DueAtUtc>m.CreatedAtUtc) <> 1
    THROW 51020, 'Milestone state, timing or ownership mismatch', 1;
IF (SELECT COUNT(*) FROM dbo.WorkspaceMeetings mt JOIN dbo.Workspaces w ON w.Id=mt.WorkspaceId
    WHERE w.ProjectId=@project AND mt.Title LIKE 'E2E Meeting %'
    AND mt.Url='https://meet.google.com/abc-defg-hij' AND mt.StartsAtUtc>mt.CreatedAtUtc) <> 1
    THROW 51021, 'Meeting URL, timing or ownership mismatch', 1;
IF (SELECT COUNT(*) FROM dbo.Notifications n WHERE n.RecipientId=@freelancer
    AND n.Title LIKE N'Lịch họp mới: E2E Meeting %'
    AND n.TargetPath='/Workspace/Index/' + CONVERT(varchar(20), (SELECT Id FROM dbo.Workspaces WHERE ProjectId=@project))) <> 1
    THROW 51022, 'Meeting recipient notification missing', 1;
IF (SELECT COUNT(*) FROM dbo.Deliverables d JOIN dbo.Workspaces w ON w.Id=d.WorkspaceId
    WHERE w.ProjectId=@project) <> 2
    THROW 51008, 'Deliverable revisions count mismatch', 1;
IF (SELECT COUNT(*) FROM dbo.Deliverables old JOIN dbo.Deliverables newer ON newer.RevisesId=old.Id
    JOIN dbo.Workspaces w ON w.Id=old.WorkspaceId
    WHERE w.ProjectId=@project AND old.Status='RevisionsRequested' AND newer.Status='Approved'
        AND newer.SubmittedById=old.SubmittedById AND newer.WorkspaceId=old.WorkspaceId) <> 1
    THROW 51009, 'Deliverable revision linkage mismatch', 1;
IF (SELECT COUNT(*) FROM dbo.Reviews WHERE ProjectId=@project AND ClientId=@client
    AND FreelancerId=@freelancer AND Rating=5) <> 1
    THROW 51010, 'Review mismatch', 1;
IF (SELECT COUNT(*) FROM dbo.ContributionRecords c JOIN dbo.Reviews r ON r.Id=c.ReviewId
    WHERE c.ProjectId=@project AND c.UserAccountId=@freelancer AND r.ProjectId=@project
        AND c.Method='ReviewSnapshot-v1' AND c.EvidenceScore =
            CASE WHEN c.DoneTasks*10+c.ApprovedDeliverables*20+c.ClientRating*10 > 100
                 THEN 100 ELSE c.DoneTasks*10+c.ApprovedDeliverables*20+c.ClientRating*10 END) <> 1
    THROW 51011, 'Contribution snapshot mismatch', 1;
IF (SELECT COUNT(*) FROM dbo.XPTransactions x JOIN dbo.ContributionRecords c ON c.Id=x.ContributionRecordId
    WHERE c.ProjectId=@project AND x.UserAccountId=@freelancer AND x.Points=c.EvidenceScore) <> 1
    THROW 51012, 'XP transaction mismatch', 1;
IF (SELECT COUNT(*) FROM dbo.SkillDNAHistories s JOIN dbo.ContributionRecords c ON c.Id=s.ContributionRecordId
    WHERE c.ProjectId=@project AND s.UserAccountId=@freelancer AND s.EvidenceScore=c.EvidenceScore) <> 1
    THROW 51013, 'Skill DNA snapshot mismatch', 1;
IF (SELECT COUNT(*) FROM dbo.ProjectMemories WHERE ProjectId=@project) <> 2
    OR (SELECT COUNT(*) FROM dbo.ProjectMemories WHERE ProjectId=@project AND Version=1 AND Source='RuleBasedDemo-v1') <> 1
    OR (SELECT COUNT(*) FROM dbo.ProjectMemories WHERE ProjectId=@project AND Version=2 AND Source='Manual-v1') <> 1
    THROW 51014, 'Project Memory versions mismatch', 1;
IF (SELECT COUNT(*) FROM dbo.SkillExamAttempts WHERE UserAccountId=@freelancer AND SkillTrackId=1
    AND QuestionCount=5 AND CorrectCount=5 AND Passed=1) <> 1
    OR (SELECT COUNT(*) FROM dbo.SkillExamAttempts WHERE UserAccountId=@freelancer AND SkillTrackId=2
    AND Passed=0) <> 3
    THROW 51015, 'Skill exam attempts or quota mismatch', 1;
IF (SELECT COUNT(*) FROM dbo.SkillExamAnswers a JOIN dbo.SkillExamAttempts e ON e.Id=a.SkillExamAttemptId
    WHERE e.UserAccountId=@freelancer) <> 20
    THROW 51016, 'Saved exam answer count mismatch', 1;
IF EXISTS (SELECT 1 FROM dbo.SkillExamAttempts WHERE UserAccountId=@freelancer AND SkillTrackId=3)
    THROW 51019, 'Tampered exam created an attempt', 1;
IF (SELECT COUNT(*) FROM dbo.EarnedSkillBadges WHERE UserAccountId=@freelancer AND SkillTrackId=1) <> 1
    OR (SELECT COUNT(*) FROM dbo.EarnedSkillBadges WHERE UserAccountId=@freelancer AND SkillTrackId=2) <> 0
    THROW 51017, 'Earned badges mismatch', 1;
IF (SELECT COUNT(*) FROM dbo.ActivityLogs WHERE ProjectId=@project) < 8
    THROW 51018, 'Activity audit trail incomplete', 1;
SELECT 'SQL_VALIDATED';
"""
    proc = subprocess.run([sqlcmd, "-S", r".\SQLEXPRESS", "-d", "NexusFreelanceMvp",
                           "-E", "-C", "-b", "-l", "10", "-h", "-1", "-W", "-Q", query],
                          capture_output=True, text=True, timeout=60)
    if proc.returncode != 0 or "SQL_VALIDATED" not in proc.stdout:
        raise AssertionError("Independent SQL verification failed: " + (proc.stderr + proc.stdout)[-1200:])


def run(base):
    urllib3.disable_warnings(urllib3.exceptions.InsecureRequestWarning)
    smoke = Smoke(base)
    client = requests.Session()
    freelancer = requests.Session()
    client.verify = False
    freelancer.verify = False
    suffix = uuid.uuid4().hex[:10]
    password = "NexusTest!2026pass"
    email_client = f"e2e-client-{suffix}@example.invalid"
    email_freelancer = f"e2e-freelancer-{suffix}@example.invalid"

    smoke.check("GET home with EF6 SQL query", lambda: smoke.get(client, "/"))
    def verify_utf8():
        response = smoke.get(client, "/")
        rendered = response.content.decode("utf-8", errors="strict")
        if "Khám phá dự án" not in rendered or "Đội ngũ phù hợp" not in rendered:
            raise AssertionError("Vietnamese text corrupted in actual HTTP response")
        return response

    smoke.check("UTF-8 Vietnamese text survives HTTP response", verify_utf8)

    def reject_anonymous():
        anonymous = requests.Session()
        anonymous.verify = False
        response = anonymous.get(smoke.base + "/Projects/Mine", timeout=35, allow_redirects=False)
        if response.status_code != 302 or "/Account/Login" not in response.headers.get("Location", ""):
            raise AssertionError(f"Anonymous protected page: {response.status_code} {response.headers.get('Location')}")

    smoke.check("Anonymous user cannot access protected pages", reject_anonymous)
    smoke.check("GET account registration", lambda: smoke.get(client, "/Account/Register"))

    def register(session, name, email, role):
        r = smoke.post(session, "/Account/Register", {
            "Name": name, "Email": email, "Password": password, "Role": role
        }, "/Account/Register")
        assert ".ASPXAUTH" in session.cookies, "No forms-auth cookie after registration"
        return r

    smoke.check("Register Client (anti-CSRF, EF6 write, auth cookie)",
                lambda: register(client, "E2E Client", email_client, "Client"))

    def reject_missing_csrf():
        response = smoke.post(client, "/Projects/Create", {
            "Title": "Missing CSRF " + suffix, "Description": "Must be rejected",
            "ProjectType": "SOLO", "Budget": "10000"
        }, expect_redirect=False)
        if response.status_code not in (400, 403, 500):
            raise AssertionError(f"POST without CSRF token was not rejected: {response.status_code}")

    smoke.check("Project create rejects missing anti-CSRF token", reject_missing_csrf)
    smoke.check("Client ownership page", lambda: smoke.get(client, "/Projects/Mine"))

    def create_project():
        r = smoke.post(client, "/Projects/Create", {
            "Title": "E2E MVC5 Backend Project " + suffix,
            "Description": "Automated integration test of the full NexusFreelance workflow.",
            "ProjectType": "SOLO", "Budget": "20000000",
        }, "/Projects/Create")
        return route_id(r.headers.get("Location"))

    project_id = smoke.check("Create draft project", create_project)
    manage = f"/Projects/Manage/{project_id}"
    smoke.check("GET draft management", lambda: smoke.get(client, manage))
    smoke.check("Add required Backend Developer role", lambda: smoke.post(client,
                "/Projects/AddRequirement", {"ProjectId": project_id,
                "Title": "Backend Developer", "RequiredCount": "1"}, manage))
    smoke.check("Publish project", lambda: smoke.post(client, f"/Projects/Publish/{project_id}", {}, manage))
    details = f"/Projects/Details/{project_id}"

    def pause_and_resume():
        smoke.post(client, f"/Projects/ChangeStatus/{project_id}", {"operation": "Pause"}, manage)
        paused = smoke.get(client, manage).text
        if "Paused" not in paused:
            raise AssertionError("Project did not transition to Paused")
        smoke.get(freelancer, details, status=404)
        smoke.get(client, details)
        smoke.post(client, f"/Projects/ChangeStatus/{project_id}", {"operation": "Resume"}, manage)
        if "Published" not in smoke.get(client, manage).text:
            raise AssertionError("Paused project did not resume")
    smoke.check("Pause hides marketplace details; Client resumes publishing", pause_and_resume)

    def cancel_draft():
        other = create_project()
        other_manage = f"/Projects/Manage/{other}"
        smoke.post(client, f"/Projects/ChangeStatus/{other}", {"operation": "Cancel"}, other_manage)
        if "Cancelled" not in smoke.get(client, other_manage).text:
            raise AssertionError("Draft project was not cancelled")
        smoke.get(freelancer, f"/Projects/Details/{other}", status=404)
        rejected = smoke.post(client, f"/Projects/Publish/{other}", {}, other_manage, expect_redirect=False)
        if rejected.status_code != 409:
            raise AssertionError(f"Cancelled project republished: {rejected.status_code}")
    smoke.check("Cancel draft is final, private, and cannot be published", cancel_draft)

    smoke.check("Published project is public", lambda: smoke.get(freelancer, details))

    smoke.check("Register Freelancer (independent auth session)",
                lambda: register(freelancer, "E2E Freelancer", email_freelancer, "Freelancer"))
    smoke.check("GET freelancer profile", lambda: smoke.get(freelancer, "/Profile/Index"))
    smoke.check("Update freelancer core role", lambda: smoke.post(freelancer,
                "/Profile/Update", {"CoreRole": "Backend Developer", "Bio": "Integration test",
                                    "IsAvailable": "true"}, "/Profile/Index"))

    def withdraw_pending_proposal():
        withdraw_project = create_project()
        withdraw_manage = f"/Projects/Manage/{withdraw_project}"
        smoke.post(client, "/Projects/AddRequirement", {"ProjectId": withdraw_project,
                   "Title": "Backend Developer", "RequiredCount": "1"}, withdraw_manage)
        smoke.post(client, f"/Projects/Publish/{withdraw_project}", {}, withdraw_manage)
        withdraw_details = f"/Projects/Details/{withdraw_project}"
        apply_form = next(f for f in forms(smoke.get(freelancer, withdraw_details).text)
                          if "/Proposals/Apply" in f["action"])
        smoke.post(freelancer, apply_form["action"], {"ProjectId": withdraw_project,
                   "RoleRequirementId": apply_form["fields"]["RoleRequirementId"],
                   "Pitch": "Temporary application to be withdrawn", "QuotedPrice": "1000000"}, withdraw_details)
        mine = "/Proposals/Mine"
        withdraw_form = next(f for f in forms(smoke.get(freelancer, mine).text)
                             if "/Proposals/Withdraw/" in f["action"])
        # A Client must not withdraw somebody else's proposal.
        forbidden = smoke.post(client, withdraw_form["action"], {}, withdraw_manage,
                               expect_redirect=False)
        if forbidden.status_code != 403:
            raise AssertionError(f"Client withdrew Freelancer proposal: HTTP {forbidden.status_code}")
        smoke.post(freelancer, withdraw_form["action"], {}, mine)
        markup = smoke.get(client, withdraw_manage).text
        if "Withdrawn" not in markup or "Temporary application to be withdrawn" not in markup:
            raise AssertionError("Withdrawn status not visible to Client")
        if "Withdrawn" not in smoke.get(freelancer, mine).text:
            raise AssertionError("Withdrawn status not visible in Freelancer history")
        proposal_id = int(re.search(r"/Proposals/Withdraw/(\d+)", withdraw_form["action"]).group(1))
        stale = smoke.post(freelancer, withdraw_form["action"], {}, mine, expect_redirect=False)
        if stale.status_code != 409:
            raise AssertionError(f"Second withdrawal should conflict: {stale.status_code}")
        rejected = smoke.post(client, f"/Projects/Accept/{withdraw_project}",
                              {"proposalId": proposal_id}, withdraw_manage)
        if rejected.status_code != 302:
            raise AssertionError(f"Unexpected stale acceptance response: HTTP {rejected.status_code}")
        after_rejection = html.unescape(smoke.get(client, withdraw_manage).text)
        if "Withdrawn" not in after_rejection or "Proposal không hợp lệ hoặc đã xử lý" not in after_rejection:
            raise AssertionError("Withdrawn proposal was not explicitly rejected by Client acceptance: "
                                 + after_rejection[after_rejection.find('alert danger'):][:320])
        # Status and lack of assignment are checked in persisted SQL as well as HTML.
        import shutil
        sqlcmd = shutil.which("sqlcmd") or r"C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\180\Tools\Binn\SQLCMD.EXE"
        query = ("SET NOCOUNT ON; IF (SELECT COUNT(*) FROM dbo.Proposals WHERE Id="
                 + str(proposal_id) + " AND ProjectId=" + str(withdraw_project)
                 + " AND Status='Withdrawn') <> 1 OR EXISTS "
                 + "(SELECT 1 FROM dbo.RoleAssignments WHERE ProposalId=" + str(proposal_id)
                 + ") THROW 51031, 'Withdrawn proposal accepted or assigned', 1; "
                 + "SELECT 'WITHDRAWAL_SQL_VALIDATED';")
        persisted = subprocess.run([sqlcmd, "-S", r".\SQLEXPRESS", "-d", "NexusFreelanceMvp",
                                    "-E", "-C", "-b", "-Q", query],
                                   capture_output=True, text=True, timeout=35)
        if persisted.returncode or "WITHDRAWAL_SQL_VALIDATED" not in persisted.stdout:
            raise AssertionError("Withdrawal SQL invariant failed: " + (persisted.stderr + persisted.stdout)[-800:])
        return rejected

    smoke.check("Withdraw pending proposal; reject forbidden, duplicate and stale acceptance", withdraw_pending_proposal)

    for label, session, page in (("DETAILS", freelancer, details), ("MANAGE", client, manage)):
        parsed = forms(smoke.get(session, page).text)
        for form in parsed:
            print("FORM", label, form["method"], form["action"],
                  ",".join(k for k in form["fields"] if k != "__RequestVerificationToken"), flush=True)

    detail_forms = forms(smoke.get(freelancer, details).text)
    application = next(f for f in detail_forms if "/Proposals/Apply" in f["action"])
    role_id = int(application["fields"]["RoleRequirementId"])
    smoke.check("Apply to published role", lambda: smoke.post(freelancer,
                application["action"], {"ProjectId": project_id,
                "RoleRequirementId": role_id, "Pitch": "I am an experienced Backend Developer for this role.",
                "QuotedPrice": "15000000"}, details))
    manage_forms = forms(smoke.get(client, manage).text)
    for form in manage_forms:
        print("FORM AFTER APPLY MANAGE", form["action"],
              ",".join(k for k in form["fields"] if k != "__RequestVerificationToken"), flush=True)

    acceptance = next(f for f in manage_forms if "/Projects/Accept/" in f["action"])
    smoke.check("Client accepts proposal", lambda: smoke.post(client,
                acceptance["action"], {}, manage))
    lock_form = next(f for f in forms(smoke.get(client, manage).text)
                     if "/Projects/LockTeam/" in f["action"])
    smoke.check("Lock complete team and open workspace", lambda: smoke.post(client,
                lock_form["action"], {}, manage))
    management_html = smoke.get(client, manage).text
    workspace_links = re.findall(r'href="([^"]*Workspace/Index/\d+[^"]*)"', management_html)
    if len(workspace_links) != 1:
        raise AssertionError(f"Expected one workspace link after team lock, got: {workspace_links}")
    workspace_page = html.unescape(workspace_links[0])
    workspace_id = int(re.search(r"/Workspace/Index/(\d+)", workspace_page).group(1))
    smoke.check("Client workspace access", lambda: smoke.get(client, workspace_page))
    smoke.check("Freelancer workspace access", lambda: smoke.get(freelancer, workspace_page))

    outsider = requests.Session()
    outsider.verify = False
    smoke.check("Register unrelated Freelancer for ownership checks", lambda: register(
        outsider, "E2E Outsider", f"e2e-outsider-{suffix}@example.invalid", "Freelancer"))
    smoke.check("Unrelated Freelancer cannot read team workspace", lambda: smoke.get(
        outsider, workspace_page, status=403))

    future_utc = (datetime.now(timezone.utc) + timedelta(days=7)).strftime("%Y-%m-%dT%H:%M")
    milestone_title = "E2E Milestone " + suffix
    meeting_title = "E2E Meeting " + suffix

    smoke.check("Client creates workspace milestone", lambda: smoke.post(
        client, "/Workspace/CreateMilestone", {"WorkspaceId": workspace_id,
        "Title": milestone_title, "DueAtUtc": future_utc}, workspace_page))
    member_html = smoke.get(freelancer, workspace_page).text
    if milestone_title not in member_html:
        raise AssertionError("Workspace milestone not visible to team member")
    completed_form = next(f for f in forms(smoke.get(client, workspace_page).text)
                          if "/Workspace/CompleteMilestone/" in f["action"])

    def forbid_member_milestone_mutations():
        for action, payload in (
            ("/Workspace/CreateMilestone", {"WorkspaceId": workspace_id,
                                            "Title": "Forbidden milestone", "DueAtUtc": future_utc}),
            (completed_form["action"], {}),
            ("/Workspace/ScheduleMeeting", {"WorkspaceId": workspace_id,
                                           "Title": "Forbidden meeting", "Url": "https://example.com", "StartsAtUtc": future_utc}),
        ):
            response = smoke.post(freelancer, action, payload, workspace_page, expect_redirect=False)
            if response.status_code != 403:
                raise AssertionError(f"Freelancer modified owner-only action {action}: HTTP {response.status_code}")
    smoke.check("Freelancer cannot manage milestones or meetings", forbid_member_milestone_mutations)

    smoke.check("Client completes milestone", lambda: smoke.post(
        client, completed_form["action"], {}, workspace_page))
    def reject_duplicate_milestone_completion():
        response = smoke.post(client, completed_form["action"], {}, workspace_page, expect_redirect=False)
        if response.status_code != 409:
            raise AssertionError(f"Duplicate completion accepted: HTTP {response.status_code}")
    smoke.check("Milestone cannot be completed twice", reject_duplicate_milestone_completion)

    smoke.check("Client schedules HTTPS team meeting", lambda: smoke.post(
        client, "/Workspace/ScheduleMeeting", {"WorkspaceId": workspace_id,
        "Title": meeting_title, "Url": "https://meet.google.com/abc-defg-hij",
        "StartsAtUtc": future_utc}, workspace_page))
    if meeting_title not in smoke.get(freelancer, workspace_page).text:
        raise AssertionError("Workspace meeting not visible to team member")
    def verify_meeting_notification():
        notification_page = smoke.get(freelancer, "/Notifications/Index").text
        if "Lịch họp mới: " + meeting_title not in notification_page:
            raise AssertionError("Freelancer did not receive new meeting notification")
    smoke.check("Team member receives meeting notification", verify_meeting_notification)

    task_client_markup = smoke.get(client, workspace_page).text
    assigned = re.search(r'<select[^>]+name="AssigneeId"[^>]*>\s*<option value="(\d+)"', task_client_markup)
    if not assigned:
        raise AssertionError("Cannot find task assignee dropdown")
    task_assignee_id = assigned.group(1)

    def reject_invalid_task(priority, due_date, label):
        title = f"Rejected {label} {suffix}"
        response = smoke.post(client, "/Workspace/CreateTask", {
            "WorkspaceId": workspace_id, "Title": title, "AssigneeId": task_assignee_id,
            "Priority": priority, "DueAtUtc": due_date}, workspace_page)
        if response.status_code != 302 or title in smoke.get(client, workspace_page).text:
            raise AssertionError(f"Invalid task {label} was accepted")

    smoke.check("Invalid task priority is rejected", lambda: reject_invalid_task(
        "Urgent", future_utc, "priority"))
    smoke.check("Past task deadline is rejected", lambda: reject_invalid_task(
        "High", "2000-01-01T00:00", "deadline"))

    def create_task():
        markup = smoke.get(client, workspace_page).text
        form = next(f for f in forms(markup) if "/Workspace/CreateTask" in f["action"])
        return smoke.post(client, form["action"], {
            "WorkspaceId": workspace_id, "Title": "Implement backend endpoints " + suffix,
            "AssigneeId": task_assignee_id, "Priority": "High", "DueAtUtc": future_utc}, workspace_page)

    smoke.check("Client creates assigned Kanban task", create_task)
    task_markup = smoke.get(freelancer, workspace_page).text
    if "Implement backend endpoints " + suffix not in task_markup:
        raise AssertionError("Task not present in freelancer workspace")
    smoke.check("Assigned task displays priority, due date and creation history", lambda:
                ("Ưu tiên: High" in html.unescape(task_markup) and
                 "Hạn:" in html.unescape(task_markup) and "Tạo: ToDo" in html.unescape(task_markup))
                 or (_ for _ in ()).throw(AssertionError("Task metadata or creation history not visible")))
    update = next(f for f in forms(task_markup) if "/Workspace/UpdateTask" in f["action"])
    def reject_outsider_task_update():
        response = smoke.post(outsider, update["action"], {
            "id": update["fields"]["id"], "status": "Doing"}, "/Profile/Index", expect_redirect=False)
        if response.status_code != 403:
            raise AssertionError(f"Outsider changed task status: HTTP {response.status_code}")
    smoke.check("Outsider cannot modify assigned Kanban task", reject_outsider_task_update)
    smoke.check("Freelancer advances Kanban status", lambda: smoke.post(freelancer,
                update["action"], {"id": update["fields"]["id"], "status": "Doing"}, workspace_page))
    def reject_duplicate_task_transition():
        response = smoke.post(freelancer, update["action"], {
            "id": update["fields"]["id"], "status": "Doing"}, workspace_page, expect_redirect=False)
        if response.status_code != 400:
            raise AssertionError(f"Duplicate Kanban transition allowed: HTTP {response.status_code}")
    smoke.check("Duplicate Kanban transition is rejected", reject_duplicate_task_transition)
    def validate_task_history():
        page = html.unescape(smoke.get(client, workspace_page).text)
        if "ToDo → Doing" not in page:
            raise AssertionError("Kanban status change missing from task history")
    smoke.check("Client sees actor-attributed Kanban change history", validate_task_history)

    smoke.check("Client sends workspace chat", lambda: smoke.post(client,
                "/Workspace/SendMessage", {"WorkspaceId": workspace_id,
                "Body": "Please submit first version."}, workspace_page))
    if "Please submit first version." not in smoke.get(freelancer, workspace_page).text:
        raise AssertionError("Workspace chat message not visible to team member")
    smoke.check("Freelancer submits deliverable", lambda: smoke.post(freelancer,
                "/Workspace/Submit", {"WorkspaceId": workspace_id,
                "Title": "Backend version 1", "Url": "https://example.com/e2e-v1"}, workspace_page))
    delivered_markup = smoke.get(client, workspace_page).text
    if "Backend version 1" not in delivered_markup:
        raise AssertionError("Deliverable not present in client workspace")
    decision_forms = forms(delivered_markup)
    revision_form = next(f for f in decision_forms
                         if "/Workspace/Decide" in f["action"] and "RevisionsRequested" in f["action"])
    smoke.check("Client requests revision", lambda: smoke.post(client,
                revision_form["action"], {"feedback":"Bổ sung kiểm thử đầu vào và tài liệu bàn giao đúng phạm vi đã thống nhất."}, workspace_page))
    revised_markup = smoke.get(freelancer, workspace_page).text
    revision_submission = next(f for f in forms(revised_markup)
                               if "/Workspace/Submit" in f["action"] and f["fields"].get("RevisesId"))
    smoke.check("Freelancer resubmits revision", lambda: smoke.post(freelancer,
                revision_submission["action"], {"WorkspaceId": workspace_id,
                "RevisesId": revision_submission["fields"]["RevisesId"],
                "Title": "Backend version 2", "Url": "https://example.com/e2e-v2"}, workspace_page))
    approved_markup = smoke.get(client, workspace_page).text
    approve_form = next(f for f in forms(approved_markup)
                        if "/Workspace/Decide" in f["action"] and "Approved" in f["action"])
    smoke.check("Client approves latest deliverable", lambda: smoke.post(client,
                approve_form["action"], {}, workspace_page))

    manage_forms = forms(smoke.get(client, manage).text)
    complete_form = next(f for f in manage_forms if "/Projects/Complete/" in f["action"])
    smoke.check("Complete project after acceptance", lambda: smoke.post(client,
                complete_form["action"], {}, manage))
    after_completion = smoke.get(client, manage).text
    print("FORM AFTER COMPLETE", [(f["action"], [k for k in f["fields"] if k != "__RequestVerificationToken"])
                                  for f in forms(after_completion)], flush=True)

    review_form = next(f for f in forms(after_completion) if "/Projects/Review" in f["action"])
    smoke.check("Client reviews completed freelancer", lambda: smoke.post(client,
                review_form["action"], {"ProjectId": project_id,
                "FreelancerId": review_form["fields"]["FreelancerId"],
                "Rating": "5", "Comment": "Successful automated E2E acceptance."}, manage))

    def reject_duplicate_review():
        response = smoke.post(client, review_form["action"], {
            "ProjectId": project_id, "FreelancerId": review_form["fields"]["FreelancerId"],
            "Rating": "5", "Comment": "This duplicate must not create XP."}, manage)
        if not response.headers.get("Location", "").startswith("/Projects/Manage"):
            raise AssertionError("Duplicate review did not return to management")

    smoke.check("Duplicate review cannot award contribution and XP twice", reject_duplicate_review)

    def reject_post_completion_writes():
        for action, payload in (
            ("/Workspace/SendMessage", {"WorkspaceId": workspace_id, "Body": "Must be rejected"}),
            ("/Workspace/Submit", {"WorkspaceId": workspace_id,
                                   "Title": "Late upload", "Url": "https://example.com/late"}),
            ("/Workspace/CreateMilestone", {"WorkspaceId": workspace_id,
                "Title": "Late milestone", "DueAtUtc": future_utc}),
            ("/Workspace/ScheduleMeeting", {"WorkspaceId": workspace_id,
                "Title": "Late meeting", "Url": "https://meet.google.com/abc-defg-hij", "StartsAtUtc": future_utc}),
            (completed_form["action"], {}),
        ):
            session = freelancer if "Submit" in action else client
            response = smoke.post(session, action, payload, workspace_page, expect_redirect=False)
            if response.status_code != 409:
                raise AssertionError(f"Completed project accepted {action}: HTTP {response.status_code}")

    smoke.check("Completed workspace rejects chat, deliverable, milestone and meeting writes", reject_post_completion_writes)
    contribution_page = smoke.check("Freelancer contribution and XP dashboard",
                                    lambda: smoke.get(freelancer, "/Contribution/Index"))
    if not re.search(r'XP|Điểm|đóng góp', contribution_page.text, re.I):
        raise AssertionError("Contribution dashboard did not render expected information")

    final_workspace = smoke.get(client, workspace_page).text
    memory_links = re.findall(r'href="([^"]*ProjectMemory[^"]*)"', final_workspace)
    if len(memory_links) != 1:
        raise AssertionError(f"Expected Project Memory link after completion, got {memory_links}")
    memory_page = html.unescape(memory_links[0])
    client_memory = smoke.check("Client can open Project Memory", lambda: smoke.get(client, memory_page))
    smoke.check("Freelancer can read Project Memory", lambda: smoke.get(freelancer, memory_page))
    print("FORM MEMORY", [(f["action"], [k for k in f["fields"] if k != "__RequestVerificationToken"])
                          for f in forms(client_memory.text)], flush=True)
    memory_generate = next(f for f in forms(client_memory.text)
                           if "/ProjectMemory/Generate" in f["action"])
    smoke.check("Generate project memory from completed work", lambda: smoke.post(client,
                memory_generate["action"], {}, memory_page))
    generated_memory = smoke.get(client, memory_page).text
    print("FORM GENERATED MEMORY", [(f["action"], [k for k in f["fields"] if k != "__RequestVerificationToken"])
                                    for f in forms(generated_memory)], flush=True)
    print("OBSERVATION memory_textareas", re.findall(r'<textarea[^>]*name="([^"]+)"', generated_memory), flush=True)

    memory_save_form = next(f for f in forms(generated_memory)
                            if "/ProjectMemory/Save" in f["action"])
    if memory_save_form["fields"].get("ExpectedVersion") != "1":
        raise AssertionError("Expected initial Project Memory version 1")
    memory_fields = {
        "ProjectId": project_id, "ExpectedVersion": "1",
        "ExecutiveSummary": "Reviewed E2E memory for " + suffix,
        "TechStack": "ASP.NET MVC 5, EF6, SQL Server",
        "ChallengesAndSolutions": "Verified project workflow",
        "LessonsLearned": "Integration checks are valuable",
        "ReusableAssets": "Approved deliverable link (private)",
        "PortfolioDraft": "Internal draft only",
    }
    smoke.check("Client saves Project Memory version 2", lambda: smoke.post(client,
                memory_save_form["action"], memory_fields, memory_page))

    def check_memory_versions():
        latest = smoke.get(client, memory_page).text
        if ("Reviewed E2E memory for " + suffix not in latest or
                "version=2" not in latest or "Manual-v1" not in latest):
            raise AssertionError("Saved memory version 2 is not visible")
        v1 = smoke.get(client, memory_page + "&version=1" if "?" in memory_page
                       else memory_page + "?version=1").text
        if "RuleBasedDemo-v1" not in v1 or "Reviewed E2E memory for " + suffix in v1:
            raise AssertionError("Version 1 was overwritten instead of retained")
        visible_to_member = smoke.get(freelancer, memory_page).text
        if "Reviewed E2E memory for " + suffix not in visible_to_member:
            raise AssertionError("Team member cannot read updated memory")
        return latest

    smoke.check("Project Memory preserves versions and member access", check_memory_versions)

    def reject_stale_version():
        smoke.post(client, memory_save_form["action"], memory_fields, memory_page)
        current = smoke.get(client, memory_page).text
        if "version=3" in current or "version=2" not in current:
            raise AssertionError("Stale optimistic-lock version did not reject the edit")

    smoke.check("Project Memory rejects stale ExpectedVersion", reject_stale_version)

    def reject_freelancer_memory_write():
        forbidden = smoke.post(freelancer, memory_save_form["action"], memory_fields,
                               "/Profile/Index", expect_redirect=False)
        if forbidden.status_code != 403:
            raise AssertionError(f"Expected 403 for member memory save, got {forbidden.status_code}")

    smoke.check("Project Memory forbids non-client writes", reject_freelancer_memory_write)

    def reject_third_party_access():
        stranger = requests.Session()
        stranger.verify = False
        register(stranger, "E2E Outsider Client", f"e2e-outsider-client-{suffix}@example.invalid", "Client")
        for page in (manage, workspace_page, memory_page):
            response = stranger.get(urljoin(smoke.base + "/", page.lstrip("/")), timeout=35)
            if response.status_code != 403:
                raise AssertionError(f"Unrelated Client accessed {page}: HTTP {response.status_code}")
        response = smoke.post(stranger, memory_save_form["action"], memory_fields,
                              "/Projects/Mine", expect_redirect=False)
        if response.status_code != 403:
            raise AssertionError(f"Unrelated Client saved private memory: HTTP {response.status_code}")

    smoke.check("Third-party Client cannot access or edit private project data", reject_third_party_access)

    skills_index = smoke.check("Freelancer skill verification catalogue",
                               lambda: smoke.get(freelancer, "/Skills/Index"))
    print("OBSERVATION skill_links", re.findall(r'href="([^"]*Skills/[^"]*)"', skills_index.text), flush=True)
    exam = smoke.get(freelancer, "/Skills/Exam/1")
    print("FORM EXAM", [(f["action"], [k for k in f["fields"] if k != "__RequestVerificationToken"])
                        for f in forms(exam.text)], flush=True)

    def submit_exam(track_id, answers):
        path = f"/Skills/Exam/{track_id}"
        exam_forms = forms(smoke.get(freelancer, path).text)
        target = next(f for f in exam_forms if f"/Skills/Submit/{track_id}" in f["action"])
        payload = {key: value for key, value in target["fields"].items()
                   if key != "__RequestVerificationToken"}
        question_keys = sorted((key for key in payload if key.endswith(".QuestionId")),
                               key=lambda key: int(re.search(r"\[(\d+)\]", key).group(1)))
        if len(question_keys) != len(answers):
            raise AssertionError(f"Exam {track_id}: expected {len(answers)} questions; got {len(question_keys)}")
        for key, answer in zip(question_keys, answers):
            payload[key.replace(".QuestionId", ".Choice")] = answer
        response = smoke.post(freelancer, target["action"], payload, path)
        return payload, response

    def reject_exam_tampering():
        # The third track is deliberately left unused by the successful workflow.
        path = "/Skills/Exam/3"
        exam_form = next(f for f in forms(smoke.get(freelancer, path).text)
                         if "/Skills/Submit/3" in f["action"])
        payload = {k: v for k, v in exam_form["fields"].items()
                   if k != "__RequestVerificationToken"}
        for key in list(payload):
            if key.endswith(".QuestionId"):
                payload[key.replace(".QuestionId", ".Choice")] = "A"
        first_choice = "Answers[0].Choice"
        payload[first_choice] = "Z"
        rejected = smoke.post(freelancer, exam_form["action"], payload, path)
        if "/Skills/Exam/3" not in rejected.headers.get("Location", ""):
            raise AssertionError("Invalid choice was accepted")
        payload[first_choice] = "A"
        payload["Answers[0].QuestionId"] = passed_payload["Answers[0].QuestionId"]
        rejected = smoke.post(freelancer, exam_form["action"], payload, path)
        if "/Skills/Exam/3" not in rejected.headers.get("Location", ""):
            raise AssertionError("Foreign track question was accepted")

    # The valid backend attempt provides a known foreign question ID.

    passed_payload, passed_response = smoke.check("Submit valid exam with server grading",
                    lambda: submit_exam(1, ["B", "C", "B", "C", "D"]))
    smoke.check("Skill exam rejects invalid choices and foreign question IDs", reject_exam_tampering)

    def check_badge():
        result_url = passed_response.headers.get("Location", "")
        result = smoke.get(freelancer, result_url).text
        if "100%" not in result or "5/5" not in result or "EXAM RESULT" not in result:
            raise AssertionError("Expected 100% passed exam result")
        index = smoke.get(freelancer, "/Skills/Index").text
        if "Skills/Result/" not in index:
            raise AssertionError("Earned badge not displayed in skill catalogue")
        if "Answers[0].QuestionId" in smoke.get(freelancer, "/Skills/Exam/1").text:
            raise AssertionError("Passed exam is still offered for new submission")

    smoke.check("Exam 100% result and one earned badge", check_badge)

    def duplicate_exam():
        before = smoke.get(freelancer, "/Skills/Index").text.count("Skills/Result/")
        r = smoke.post(freelancer, "/Skills/Submit/1", passed_payload,
                       "/Skills/Index")
        if "Skills/Exam/1" not in r.headers.get("Location", ""):
            raise AssertionError("Duplicate badge submit should be rejected")
        after = smoke.get(freelancer, "/Skills/Index").text.count("Skills/Result/")
        if after != before:
            raise AssertionError("Duplicate exam created another attempt or badge")

    smoke.check("Reject repeat exam after earning badge", duplicate_exam)

    def check_exam_quota():
        for i in range(3):
            payload, attempt = submit_exam(2, ["A"] * 5)
            result_text = smoke.get(freelancer, attempt.headers.get("Location", "")).text
            if "40%" not in result_text:
                raise AssertionError(f"Expected failed attempt #{i + 1}")
        attempt = smoke.post(freelancer, "/Skills/Submit/2", payload, "/Skills/Index")
        if "Skills/Exam/2" not in attempt.headers.get("Location", ""):
            raise AssertionError("Exam attempt over daily quota was accepted")
        exhausted = smoke.get(freelancer, "/Skills/Exam/2").text
        if "Answers[0].QuestionId" in exhausted or "/Skills/Submit/2" in exhausted:
            raise AssertionError("Daily exam quota not reflected in page")

    smoke.check("Skill exam enforces three-attempt quota per 24 hours", check_exam_quota)

    def check_ownership():
        wrong = freelancer.get(urljoin(smoke.base + "/", manage.lstrip("/")), timeout=35)
        if wrong.status_code != 403:
            raise AssertionError(f"Freelancer Manage owner-only: expected 403, got {wrong.status_code}")
        return wrong

    smoke.check("Reject cross-role project management", check_ownership)

    def test_login_again():
        logout_form = next(f for f in forms(smoke.get(freelancer, "/").text)
                           if "/Account/Logout" in f["action"])
        smoke.post(freelancer, logout_form["action"], {}, "/")
        if ".ASPXAUTH" in freelancer.cookies:
            raise AssertionError("Logout did not clear auth cookie")
        r = smoke.post(freelancer, "/Account/Login", {"Email": email_freelancer,
                       "Password": password}, "/Account/Login")
        if ".ASPXAUTH" not in freelancer.cookies:
            raise AssertionError("Login did not issue forms-auth cookie")
        return r

    smoke.check("Logout and login with stored PBKDF2 hash", test_login_again)

    smoke.check("Independent SQL: project, matching, team, revisions, review, contribution, XP, memory and badges",
                lambda: verify_sql_state(project_id, email_client, email_freelancer))

    print("OBSERVATION project_id=" + str(project_id), flush=True)
    print("OBSERVATION client_email=" + email_client, flush=True)
    print("OBSERVATION freelancer_email=" + email_freelancer, flush=True)
    print(f"PASSED={len(smoke.rows)}", flush=True)


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--base", default="https://localhost:44391")
    parser.add_argument("--run-writes", action="store_true")
    args = parser.parse_args()
    if not args.run_writes:
        parser.error("Pass --run-writes to create throwaway test records in NexusFreelanceMvp")
    try:
        run(args.base)
    except Exception as ex:
        print(f"FAIL {type(ex).__name__}: {ex}", file=sys.stderr, flush=True)
        sys.exit(1)
