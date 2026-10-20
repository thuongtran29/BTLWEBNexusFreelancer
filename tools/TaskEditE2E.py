"""Task-edit HTTP + SQL regression on the disposable NexusFreelanceMvp database.

Requires local IIS Express and SQL Server. Creates uniquely named test records;
run with --run-writes only against the development database.
"""
import argparse
import datetime as dt
import re
import uuid

import requests
import urllib3

from ConcurrencyE2E import sql_scalar
from SmokeE2E import Smoke, forms, route_id


BASE = "https://localhost:44391"


def matching_form(markup, part):
    found = [form for form in forms(markup) if part in form["action"]]
    if len(found) != 1:
        raise AssertionError("Expected exactly one form for " + part)
    return found[0]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--run-writes", action="store_true")
    args = parser.parse_args()
    if not args.run_writes:
        parser.error("Writes disposable NexusFreelanceMvp DB; add --run-writes")

    urllib3.disable_warnings(urllib3.exceptions.InsecureRequestWarning)
    smoke = Smoke(BASE)
    suffix = uuid.uuid4().hex[:12]
    client, worker, outsider = (requests.Session() for _ in range(3))
    for session in (client, worker, outsider):
        session.verify = False

    for session, role, label in ((client, "Client", "owner"),
                                 (worker, "Freelancer", "worker"),
                                 (outsider, "Freelancer", "outsider")):
        smoke.post(session, "/Account/Register", {
            "Name": "Task " + label, "Email": f"task-{label}-{suffix}@example.invalid",
            "Password": "TaskEditE2EStrong!123", "Role": role
        }, "/Account/Register")
        assert ".ASPXAUTH" in session.cookies

    created = smoke.post(client, "/Projects/Create", {
        "Title": "Task edit regression " + suffix,
        "Description": "Verify old/new snapshots and task permission boundaries.",
        "ProjectType": "SOLO", "Budget": "2000000"
    }, "/Projects/Create")
    project_id = route_id(created.headers.get("Location"))
    manage = f"/Projects/Manage/{project_id}"
    smoke.post(client, "/Projects/AddRequirement", {
        "ProjectId": project_id, "Title": "Backend contributor", "RequiredCount": "1"
    }, manage)
    smoke.post(client, f"/Projects/Publish/{project_id}", {}, manage)
    details = f"/Projects/Details/{project_id}"
    apply = matching_form(smoke.get(worker, details).text, "/Proposals/Apply")
    smoke.post(worker, "/Proposals/Apply", {
        "ProjectId": project_id,
        "RoleRequirementId": apply["fields"]["RoleRequirementId"],
        "QuotedPrice": "1000000", "Pitch": "Task edit verification contributor"
    }, details)
    accept = matching_form(smoke.get(client, manage).text, "/Projects/Accept/")
    smoke.post(client, accept["action"], {}, manage)
    smoke.post(client, f"/Projects/LockTeam/{project_id}", {}, manage)
    workspace_markup = smoke.get(client, manage).text
    workspace_id = int(re.search(r"/Workspace/Index/(\d+)", workspace_markup).group(1))
    workspace = f"/Workspace/Index/{workspace_id}"

    # The member ID is rendered by the server in the task assignment selector.
    markup = smoke.get(client, workspace).text
    match = re.search(r'<select[^>]+id="AssigneeId"[^>]*>\s*<option value="(\d+)"', markup)
    if not match:
        raise AssertionError("Could not find the team's assignment dropdown")
    member_id = int(match.group(1))
    original = "Task before edit " + suffix
    smoke.post(client, "/Workspace/CreateTask", {
        "WorkspaceId": workspace_id, "Title": original,
        "AssigneeId": member_id, "Priority": "Normal"
    }, workspace)
    edit = matching_form(smoke.get(client, workspace).text, "/Workspace/EditTask")
    task_id = int(edit["fields"]["Id"])
    assert original in smoke.get(worker, workspace).text
    smoke.check("Member can see task", lambda: smoke.get(worker, workspace))
    smoke.check("Outsider cannot read workspace", lambda:
                smoke.get(outsider, workspace, status=403))

    def post_edit(session, form_page, payload, expected):
        response = smoke.post(session, "/Workspace/EditTask", {
            "Id": task_id, "AssigneeId": member_id,
            "Title": original, "Priority": "Normal", **payload
        }, form_page, expect_redirect=False)
        if response.status_code != expected:
            raise AssertionError(f"Edit HTTP={response.status_code}, expected={expected}")
        return response

    smoke.check("Freelancer cannot edit task", lambda:
                post_edit(worker, workspace, {"Title": "Illegal edit"}, 403))
    smoke.check("Outsider cannot edit task", lambda:
                post_edit(outsider, "/Groups/Index", {"Title": "Illegal edit"}, 403))
    smoke.check("Missing anti-forgery token rejected", lambda:
                assert_status(client.post(BASE + "/Workspace/EditTask", data={
                    "Id": task_id, "Title": "Invalid request", "AssigneeId": member_id,
                    "Priority": "Normal"
                }, allow_redirects=False, timeout=35), 400))
    invalid = post_edit(client, workspace, {"Priority": "Urgent"}, 302)
    assert "Task" in smoke.get(client, workspace).text
    smoke.check("Invalid priority does not change task", lambda:
                assert_value(sql_scalar(f"SELECT COUNT(*) FROM dbo.Tasks WHERE Id={task_id} "
                                        "AND Priority='Normal'"), 1))

    changed = "Task after edit " + suffix
    due = (dt.datetime.now(dt.timezone.utc) + dt.timedelta(days=7)).strftime("%Y-%m-%dT%H:%M")
    smoke.check("Owner updates title, priority and UTC due date", lambda:
                post_edit(client, workspace, {"Title": changed, "Priority": "High",
                                              "DueAtUtc": due}, 302))
    smoke.check("Owner no-op edit does not create another history", lambda:
                post_edit(client, workspace, {"Title": changed, "Priority": "High",
                                              "DueAtUtc": due}, 302))
    smoke.check("Task fields persist in SQL", lambda: assert_value(sql_scalar(
        f"SELECT COUNT(*) FROM dbo.Tasks WHERE Id={task_id} "
        "AND Priority='High' AND DueAtUtc IS NOT NULL "
        f"AND Title=N'{changed}'"), 1))
    smoke.check("One complete before/after Edited event is persisted", lambda:
                assert_value(sql_scalar(
                    "SELECT COUNT(*) FROM dbo.TaskHistories "
                    f"WHERE WorkTaskId={task_id} AND EventType='Edited' "
                    f"AND OldTitle=N'{original}' AND NewTitle=N'{changed}' "
                    "AND OldPriority='Normal' AND NewPriority='High' "
                    "AND OldDueAtUtc IS NULL AND NewDueAtUtc IS NOT NULL "
                    f"AND OldAssigneeId={member_id} AND NewAssigneeId={member_id} "
                    "AND OldStatus='ToDo' AND NewStatus='ToDo'"), 1))
    assert changed in smoke.get(worker, workspace).text
    print(f"TASK_EDIT_E2E_PASSED={len(smoke.rows)} PROJECT={project_id} TASK={task_id}")


def assert_status(response, status):
    if response.status_code != status:
        raise AssertionError(f"Unexpected HTTP {response.status_code}; expected {status}")


def assert_value(actual, expected):
    if actual != expected:
        raise AssertionError(f"SQL value {actual}; expected {expected}")


if __name__ == "__main__":
    main()
