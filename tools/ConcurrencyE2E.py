"""Isolated, concurrent HTTP/SQL regression test for NexusFreelanceMvp.

Usage: python tools/ConcurrencyE2E.py --run-writes
Creates dedicated throwaway users and a project in NexusFreelanceMvp only.
No cleanup is performed, preserving evidence for SQL inspection.
"""

import argparse
import concurrent.futures
import re
import subprocess
import uuid
from pathlib import Path

import requests
import urllib3

from SmokeE2E import Smoke, forms, route_id


SQLCMD = Path(r"C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\180\Tools\Binn\SQLCMD.EXE")


def sql_scalar(query):
    result = subprocess.run(
        [str(SQLCMD), "-S", r".\SQLEXPRESS", "-d", "NexusFreelanceMvp",
         "-E", "-C", "-b", "-W", "-h", "-1", "-Q", "SET NOCOUNT ON; " + query],
        capture_output=True, text=True, timeout=35,
    )
    if result.returncode:
        raise AssertionError(f"SQL check failed: {result.stderr.strip()} ({query})")
    values = [line.strip() for line in result.stdout.splitlines() if line.strip()]
    if not values:
        raise AssertionError("No SQL scalar returned: " + query)
    return int(values[0])


def assert_eq(label, actual, expected):
    if actual != expected:
        raise AssertionError(f"{label}: actual={actual}, expected={expected}")
    print(f"PASS {label} ({actual})", flush=True)


def session():
    s = requests.Session()
    s.verify = False
    return s


def clone_auth(existing):
    clone = session()
    clone.cookies.update(existing.cookies)
    return clone


def csrf_post(smoke, sess, path, payload, form_page):
    markup = smoke.get(sess, form_page).text
    available = [f for f in forms(markup) if f["fields"].get("__RequestVerificationToken")]
    matching = [f for f in available if path.split("?")[0] in f["action"]]
    form = (matching or available)[-1]
    token = form["fields"]["__RequestVerificationToken"]
    return smoke.post(sess, path, {"__RequestVerificationToken": token, **payload}, expect_redirect=False)


def parallel_two(func_a, func_b):
    with concurrent.futures.ThreadPoolExecutor(max_workers=2) as pool:
        futures = [pool.submit(func_a), pool.submit(func_b)]
        return [f.result(timeout=50) for f in futures]


def run(base):
    urllib3.disable_warnings(urllib3.exceptions.InsecureRequestWarning)
    smoke = Smoke(base)
    client, first, second = session(), session(), session()
    suffix = uuid.uuid4().hex[:12]
    password = "NexusRace!2026pass"

    def register(s, role, name):
        email = f"race-{name}-{suffix}@example.invalid"
        smoke.post(s, "/Account/Register", {"Name": name, "Email": email,
                   "Password": password, "Role": role}, "/Account/Register")
        if ".ASPXAUTH" not in s.cookies:
            raise AssertionError("Register/login failed for " + name)
        return email

    email_client = register(client, "Client", "client")
    email_first = register(first, "Freelancer", "first")
    email_second = register(second, "Freelancer", "second")
    print("PASS Three independent authenticated sessions", flush=True)

    created = smoke.post(client, "/Projects/Create", {
        "Title": "Concurrent vacancy " + suffix, "Description": "Two simultaneous applicants",
        "ProjectType": "SOLO", "Budget": "20000000"}, "/Projects/Create")
    project = route_id(created.headers.get("Location"))
    manage = f"/Projects/Manage/{project}"
    smoke.post(client, "/Projects/AddRequirement", {
        "ProjectId": project, "Title": "Backend Developer", "RequiredCount": "1"}, manage)
    smoke.post(client, f"/Projects/Publish/{project}", {}, manage)
    details = f"/Projects/Details/{project}"
    proposal_ids = []
    for s in (first, second):
        application = next(f for f in forms(smoke.get(s, details).text)
                           if "/Proposals/Apply" in f["action"])
        smoke.post(s, application["action"], {
            "ProjectId": project, "RoleRequirementId": application["fields"]["RoleRequirementId"],
            "QuotedPrice": "12000000", "Pitch": "Concurrent candidate"}, details)
    for f in forms(smoke.get(client, manage).text):
        match = re.search(r"proposalId=(\d+)", f["action"])
        if "/Projects/Accept/" in f["action"] and match:
            proposal_ids.append(int(match.group(1)))
    if len(proposal_ids) != 2:
        raise AssertionError(f"Expected 2 proposal actions, got {proposal_ids}")
    c1, c2 = clone_auth(client), clone_auth(client)
    urls = [f"/Projects/Accept/{project}?proposalId={p}" for p in proposal_ids]
    races = parallel_two(
        lambda: csrf_post(smoke, c1, urls[0], {}, manage),
        lambda: csrf_post(smoke, c2, urls[1], {}, manage),
    )
    print("OBSERVATION concurrent Accept HTTP=" + str([r.status_code for r in races]), flush=True)
    assert_eq("One accepted proposal despite last-seat race", sql_scalar(
        f"SELECT COUNT(*) FROM dbo.Proposals WHERE ProjectId={project} AND Status='Accepted'"), 1)
    assert_eq("One assignment despite last-seat race", sql_scalar(
        f"SELECT COUNT(*) FROM dbo.RoleAssignments WHERE ProjectId={project}"), 1)
    assert_eq("One team membership despite last-seat race", sql_scalar(
        "SELECT COUNT(*) FROM dbo.TeamMembers tm JOIN dbo.ProjectTeams pt ON pt.Id=tm.ProjectTeamId "
        f"WHERE pt.ProjectId={project}"), 1)

    winner_id = sql_scalar(f"SELECT FreelancerId FROM dbo.RoleAssignments WHERE ProjectId={project}")
    first_id = sql_scalar("SELECT Id FROM dbo.Users WHERE Email=N'" + email_first + "'")
    winner = first if first_id == winner_id else second
    smoke.post(client, f"/Projects/LockTeam/{project}", {}, manage)
    workspace_markup = smoke.get(client, manage).text
    workspace_id = int(re.search(r"/Workspace/Index/(\d+)", workspace_markup).group(1))
    workspace_page = f"/Workspace/Index/{workspace_id}"

    # A pending second deliverable and a project completion must never commit together.
    smoke.post(winner, "/Workspace/Submit", {"WorkspaceId": workspace_id,
        "Title": "Initial handoff", "Url": "https://example.com/accepted"}, workspace_page)
    approval = next(f for f in forms(smoke.get(client, workspace_page).text)
                    if "/Workspace/Decide" in f["action"] and "Approved" in f["action"])
    smoke.post(client, approval["action"], {}, workspace_page)
    w2, c3 = clone_auth(winner), clone_auth(client)
    results = parallel_two(
        lambda: csrf_post(smoke, w2, "/Workspace/Submit", {
            "WorkspaceId": workspace_id, "Title": "Racing handoff",
            "Url": "https://example.com/racing"}, workspace_page),
        lambda: csrf_post(smoke, c3, f"/Projects/Complete/{project}", {}, manage),
    )
    print("OBSERVATION Submit/Complete HTTP=" + str([r.status_code for r in results]), flush=True)
    impossible = sql_scalar(
        "SELECT COUNT(*) FROM dbo.Projects p WHERE p.Id=" + str(project) +
        " AND p.Status='Completed' AND EXISTS (SELECT 1 FROM dbo.Deliverables d "
        "JOIN dbo.Workspaces w ON w.Id=d.WorkspaceId WHERE w.ProjectId=p.Id "
        "AND d.Status<>'Approved' AND NOT EXISTS (SELECT 1 FROM dbo.Deliverables d2 WHERE d2.RevisesId=d.Id))")
    assert_eq("Complete/Submit race preserves acceptance invariant", impossible, 0)

    # If Submit won, approve it before completing; if Complete won, reject late submissions.
    state = sql_scalar(f"SELECT CASE WHEN Status='Completed' THEN 1 ELSE 0 END FROM dbo.Projects WHERE Id={project}")
    if not state:
        pages = forms(smoke.get(client, workspace_page).text)
        for approval in pages:
            if "/Workspace/Decide" in approval["action"] and "Approved" in approval["action"]:
                smoke.post(client, approval["action"], {}, workspace_page)
        smoke.post(client, f"/Projects/Complete/{project}", {}, manage)
    assert_eq("Project eventually completes", sql_scalar(
        f"SELECT COUNT(*) FROM dbo.Projects WHERE Id={project} AND Status='Completed'"), 1)

    # Review transaction also creates exactly one contribution/XP/SkillDNA snapshot.
    c4, c5 = clone_auth(client), clone_auth(client)
    review = {"ProjectId": project, "FreelancerId": winner_id, "Rating": "5",
              "Comment": "Concurrent review"}
    reviews = parallel_two(
        lambda: csrf_post(smoke, c4, "/Projects/Review", review, manage),
        lambda: csrf_post(smoke, c5, "/Projects/Review", review, manage),
    )
    print("OBSERVATION concurrent Review HTTP=" + str([r.status_code for r in reviews]), flush=True)
    for table, join in (
        ("Reviews", "ProjectId"), ("ContributionRecords", "ProjectId"),
        ("XPTransactions", "ContributionRecordId IN (SELECT Id FROM dbo.ContributionRecords WHERE ProjectId"),
        ("SkillDNAHistories", "ContributionRecordId IN (SELECT Id FROM dbo.ContributionRecords WHERE ProjectId"),
    ):
        predicate = f"{join}={project}" if join == "ProjectId" else f"{join}={project})"
        assert_eq(f"Only one {table} row", sql_scalar(f"SELECT COUNT(*) FROM dbo.{table} WHERE {predicate}"), 1)

    memory_page = f"/ProjectMemory/Index?projectId={project}"
    generate = next(f for f in forms(smoke.get(client, memory_page).text)
                    if "/ProjectMemory/Generate" in f["action"])
    smoke.post(client, generate["action"], {}, memory_page)
    memory_markup = smoke.get(client, memory_page).text
    save = next(f for f in forms(memory_markup) if "/ProjectMemory/Save" in f["action"])
    expected = int(save["fields"]["ExpectedVersion"])
    payload = {"ProjectId": project, "ExpectedVersion": expected,
        "ExecutiveSummary": "Race-tested memory " + suffix,
        "TechStack": "MVC5 SQL Server", "ChallengesAndSolutions": "Two writers",
        "LessonsLearned": "Version concurrency", "ReusableAssets": "Internal only",
        "PortfolioDraft": "Private"}
    m1, m2 = clone_auth(client), clone_auth(client)
    mem_results = parallel_two(
        lambda: csrf_post(smoke, m1, "/ProjectMemory/Save", payload, memory_page),
        lambda: csrf_post(smoke, m2, "/ProjectMemory/Save", payload, memory_page),
    )
    print("OBSERVATION concurrent Memory Save HTTP=" + str([r.status_code for r in mem_results]), flush=True)
    assert_eq("Exactly one optimistic-version Memory update", sql_scalar(
        f"SELECT COUNT(*) FROM dbo.ProjectMemories WHERE ProjectId={project}"), expected + 1)

    # Two concurrent exam requests compete for the single remaining attempt (3 per 24h).
    loser = second if first_id == winner_id else first
    loser_email = email_second if first_id == winner_id else email_first
    loser_id = sql_scalar("SELECT Id FROM dbo.Users WHERE Email=N'" + loser_email + "'")
    quiz_path = "/Skills/Exam/2"

    def exam_payload(s):
        target = next(f for f in forms(smoke.get(s, quiz_path).text)
                      if "/Skills/Submit/2" in f["action"])
        fields = {k: v for k, v in target["fields"].items()
                  if k != "__RequestVerificationToken"}
        for key in list(fields):
            if key.endswith(".QuestionId"):
                fields[key.replace(".QuestionId", ".Choice")] = "A"
        if len([k for k in fields if k.endswith(".Choice")]) != 5:
            raise AssertionError("Expected five seeded exam questions")
        return fields

    for _ in range(2):
        # Each submitted sitting consumes its signed ticket. Open a fresh
        # sitting for every setup attempt; reusing a completed ticket is replay.
        result = csrf_post(smoke, loser, "/Skills/Submit/2", exam_payload(loser), quiz_path)
        if result.status_code != 302 or "/Skills/Result/" not in result.headers.get("Location", ""):
            raise AssertionError("Exam setup did not persist a failing attempt")
    assert_eq("Exam setup records two attempts", sql_scalar(
        f"SELECT COUNT(*) FROM dbo.SkillExamAttempts WHERE UserAccountId={loser_id} AND SkillTrackId=2"), 2)
    quiz = exam_payload(loser)
    s1, s2 = clone_auth(loser), clone_auth(loser)
    exams = parallel_two(
        lambda: csrf_post(smoke, s1, "/Skills/Submit/2", quiz, quiz_path),
        lambda: csrf_post(smoke, s2, "/Skills/Submit/2", quiz, quiz_path),
    )
    print("OBSERVATION concurrent Exam Submit HTTP=" + str([r.status_code for r in exams]), flush=True)
    assert_eq("Only one final exam attempt wins quota race", sql_scalar(
        f"SELECT COUNT(*) FROM dbo.SkillExamAttempts WHERE UserAccountId={loser_id} AND SkillTrackId=2"), 3)
    assert_eq("Failed attempts do not award badges", sql_scalar(
        f"SELECT COUNT(*) FROM dbo.EarnedSkillBadges WHERE UserAccountId={loser_id} AND SkillTrackId=2"), 0)

    # Job Studio must accept just one revision of a particular version and apply once.
    generated = smoke.post(client, "/JobStudio/Generate", {
        "Idea": "Create a booking website with backend APIs and frontend dashboard",
        "ProjectType": "SOLO", "Budget": "50000000"}, "/JobStudio/New")
    target = generated.headers.get("Location", "")
    draft_match = re.search(r"/JobStudio/Edit/(\d+)", target)
    if not draft_match:
        raise AssertionError("Job Studio generator failed: " + target)
    draft_id = int(draft_match.group(1))
    edit_page = f"/JobStudio/Edit/{draft_id}"
    edit_form = next(f for f in forms(smoke.get(client, edit_page).text)
                     if "/JobStudio/Edit" in f["action"])
    expected_version_id = int(edit_form["fields"]["ExpectedVersionId"])
    fields = {
        "Id": draft_id, "ExpectedVersionId": expected_version_id,
        "Title": "Concurrent job idea " + suffix,
        "Description": "Full stack web project summary",
        "Objectives": "Deliver stable booking application",
        "Scope": "Client and server modules",
        "UserStories": "As a user I can book online",
        "RequiredSkills": "MVC 5, SQL Server",
        "Deliverables": "Source and documentation",
        "Milestones": "MVP and handoff",
        "Roles[0].Title": "Backend Developer", "Roles[0].RequiredCount": "1",
    }
    j1, j2 = clone_auth(client), clone_auth(client)
    edits = parallel_two(
        lambda: csrf_post(smoke, j1, "/JobStudio/Edit", fields, edit_page),
        lambda: csrf_post(smoke, j2, "/JobStudio/Edit", fields, edit_page),
    )
    print("OBSERVATION concurrent Job Edit HTTP=" + str([r.status_code for r in edits]), flush=True)
    assert_eq("One Job Studio revision from same version", sql_scalar(
        f"SELECT COUNT(*) FROM dbo.AIJobDraftVersions WHERE AIJobDraftId={draft_id}"), 2)

    client_id = sql_scalar("SELECT Id FROM dbo.Users WHERE Email=N'" + email_client + "'")
    before_projects = sql_scalar(f"SELECT COUNT(*) FROM dbo.Projects WHERE ClientId={client_id}")
    a1, a2 = clone_auth(client), clone_auth(client)
    applies = parallel_two(
        lambda: csrf_post(smoke, a1, f"/JobStudio/Apply/{draft_id}", {}, edit_page),
        lambda: csrf_post(smoke, a2, f"/JobStudio/Apply/{draft_id}", {}, edit_page),
    )
    print("OBSERVATION concurrent Job Apply HTTP=" + str([r.status_code for r in applies]), flush=True)
    assert_eq("One project from simultaneous Apply", sql_scalar(
        f"SELECT COUNT(*) FROM dbo.Projects WHERE ClientId={client_id}"), before_projects + 1)
    assert_eq("Job Draft links to exactly one Project", sql_scalar(
        f"SELECT COUNT(*) FROM dbo.AIJobDrafts d JOIN dbo.Projects p ON p.Id=d.AppliedProjectId "
        f"WHERE d.Id={draft_id} AND p.ClientId={client_id}"), 1)
    assert_eq("Applied Job Studio project keeps role requirements", sql_scalar(
        f"SELECT CASE WHEN COUNT(*) > 0 THEN 1 ELSE 0 END FROM dbo.RoleRequirements "
        f"WHERE ProjectId=(SELECT AppliedProjectId FROM dbo.AIJobDrafts WHERE Id={draft_id})"), 1)

    # Accept and Withdraw compete on the same pending proposal. Exactly one
    # transition can commit and an accepted proposal must have an assignment.
    race_project = route_id(smoke.post(client, "/Projects/Create", {
        "Title": "Accept withdraw race " + suffix,
        "Description": "Ensure one proposal transition wins",
        "ProjectType": "SOLO", "Budget": "20000000"}, "/Projects/Create").headers.get("Location"))
    race_manage = f"/Projects/Manage/{race_project}"
    smoke.post(client, "/Projects/AddRequirement", {
        "ProjectId": race_project, "Title": "Backend Developer", "RequiredCount": "1"}, race_manage)
    smoke.post(client, f"/Projects/Publish/{race_project}", {}, race_manage)
    race_details = f"/Projects/Details/{race_project}"
    apply = next(f for f in forms(smoke.get(first, race_details).text)
                 if "/Proposals/Apply" in f["action"])
    smoke.post(first, apply["action"], {
        "ProjectId": race_project, "RoleRequirementId": apply["fields"]["RoleRequirementId"],
        "QuotedPrice": "12000000", "Pitch": "Acceptance withdrawal race"}, race_details)
    accept_action = next(f["action"] for f in forms(smoke.get(client, race_manage).text)
                         if "/Projects/Accept/" in f["action"])
    race_proposal = int(re.search(r"proposalId=(\d+)", accept_action).group(1))
    withdraw_action = next(f["action"] for f in forms(smoke.get(first, "/Proposals/Mine").text)
                           if "/Proposals/Withdraw" in f["action"] and
                           re.search(r"/Withdraw/" + str(race_proposal) + r"(?:\?|$)", f["action"]))
    race_accept, race_withdraw = parallel_two(
        lambda: csrf_post(smoke, clone_auth(client), accept_action, {}, race_manage),
        lambda: csrf_post(smoke, clone_auth(first), withdraw_action, {}, "/Proposals/Mine"),
    )
    print("OBSERVATION concurrent Accept/Withdraw HTTP=" +
          str([race_accept.status_code, race_withdraw.status_code]), flush=True)
    if race_accept.status_code >= 500 or race_withdraw.status_code >= 500:
        raise AssertionError("Accept/Withdraw race generated an HTTP server error")
    assert_eq("Proposal is accepted or withdrawn once", sql_scalar(
        f"SELECT COUNT(*) FROM dbo.Proposals WHERE Id={race_proposal} "
        "AND Status IN ('Accepted','Withdrawn')"), 1)
    assert_eq("Accept/Withdraw assignment consistent with proposal", sql_scalar(
        "SELECT COUNT(*) FROM dbo.Proposals p WHERE p.Id=" + str(race_proposal) +
        " AND ((p.Status='Accepted' AND EXISTS (SELECT 1 FROM dbo.RoleAssignments a WHERE a.ProposalId=p.Id)) "
        "OR (p.Status='Withdrawn' AND NOT EXISTS (SELECT 1 FROM dbo.RoleAssignments a WHERE a.ProposalId=p.Id)))"), 1)
    assert_eq("At most one assignment after Accept/Withdraw", sql_scalar(
        "SELECT CASE WHEN COUNT(*) <= 1 THEN 1 ELSE 0 END FROM dbo.RoleAssignments "
        f"WHERE ProposalId={race_proposal}"), 1)

    print("OBSERVATION project_id=" + str(project), flush=True)
    print("OBSERVATION client_email=" + email_client, flush=True)
    print("OBSERVATION freelancer_emails=" + email_first + "," + email_second, flush=True)
    print("CONCURRENCY_PASSED=19", flush=True)


if __name__ == "__main__":
    p = argparse.ArgumentParser()
    p.add_argument("--base", default="https://localhost:44391")
    p.add_argument("--run-writes", action="store_true")
    args = p.parse_args()
    if not args.run_writes:
        p.error("Explicit --run-writes required: this test creates data in NexusFreelanceMvp")
    run(args.base)
