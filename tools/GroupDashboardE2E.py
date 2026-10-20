"""Group dashboard permissions and task progress on local disposable NexusFreelanceMvp.

Run with --run-writes only after starting IIS Express and the development SQL Server.
Creates unique user, group, proposal, project, workspace and task records.
"""
import argparse
import html
import re
import uuid

import GroupBidE2E as bid_tools
from FeatureE2E import find_form, get, register, send
from GroupE2E import respond_form


def check(condition, message):
    if not condition:
        raise AssertionError(message)
    print("PASS " + message, flush=True)


def project_section(markup):
    """Only inspect the assigned-project section, excluding the group bid list."""
    start = markup.index('id="group-progress-title"')
    end = markup.index('<section class="panel section-small"><h2>Proposal nhóm', start)
    return html.unescape(markup[start:end])


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--run-writes", action="store_true")
    args = parser.parse_args()
    if not args.run_writes:
        parser.error("Explicit --run-writes required for NexusFreelanceMvp test records")

    suffix = uuid.uuid4().hex[:10]
    client = register("Client", "dashboard-client-" + suffix)
    leader = register("Freelancer", "dashboard-leader-" + suffix)
    member = register("Freelancer", "dashboard-member-" + suffix)
    spectator = register("Freelancer", "dashboard-spectator-" + suffix)
    outsider = register("Freelancer", "dashboard-outsider-" + suffix)
    bid_tools.CLIENT = client

    created = send(leader, find_form(get(leader, "/Groups/Index").text, "/Groups/Create"),
                   Name="Dashboard group " + suffix, Description="Group dashboard isolation test")
    group_id = int(re.search(r"/Groups/Details/(\d+)", created.url).group(1))
    group_path = "/Groups/Details/%d" % group_id
    check(get(outsider, group_path, 404).status_code == 404,
          "Unrelated freelancer cannot view group dashboard")
    check(get(client, group_path, 403).status_code == 403,
          "Client cannot view group dashboard")

    def invite(session_label):
        email = "feature-freelancer-dashboard-%s-%s@example.invalid" % (session_label, suffix)
        send(leader, find_form(get(leader, group_path).text, "/Groups/Invite"),
             GroupId=str(group_id), Email=email)

    invite("member")
    invite("spectator")
    check(get(member, group_path, 404).status_code == 404,
          "Pending invitation cannot access dashboard")
    check(get(spectator, group_path, 404).status_code == 404,
          "Second pending invitation cannot access dashboard")
    send(member, respond_form(get(member, "/Groups/Index").text, True))
    check("Chưa có dự án nhóm nào" in get(member, group_path).text,
          "Active member with no workspace sees empty state")

    project_id = bid_tools.new_project(client, suffix, 1)
    bid_id, decisions = bid_tools.submit_bid(leader, group_id, project_id)
    before_accept = project_section(get(leader, group_path).text)
    check("Chưa có dự án nhóm nào" in before_accept,
          "Pending group proposal is excluded from project dashboard")
    accept = next(f for f in decisions if "accept=True" in html.unescape(f["action"]))
    send(client, accept)
    send(client, find_form(get(client, "/Projects/Manage/%d" % project_id).text,
                           "/Projects/LockTeam/"))

    lead_progress = project_section(get(leader, group_path).text)
    member_progress = project_section(get(member, group_path).text)
    check("Group bid project " + suffix in lead_progress and "0/0 Task" in lead_progress,
          "Leader sees accepted project with zero tasks")
    check("Group bid project " + suffix in member_progress,
          "Assigned active member sees accepted project")
    check("Tạo tri thức dự án" not in lead_progress and "ProjectMemory/Index" not in lead_progress,
          "Incomplete project does not show invalid Project Memory link")
    workspace_match = re.search(r"/Workspace/Index/(\d+)", lead_progress)
    check(workspace_match is not None, "Dashboard links to authorized Workspace")
    workspace_id = int(workspace_match.group(1))

    send(spectator, respond_form(get(spectator, "/Groups/Index").text, True))
    inactive_assignment = project_section(get(spectator, group_path).text)
    check("Chưa có dự án nhóm nào" in inactive_assignment and
          "/Workspace/Index/" not in inactive_assignment,
          "Active group member without Team assignment cannot see project dashboard")
    check(get(spectator, "/Workspace/Index/%d" % workspace_id, 403).status_code == 403,
          "Active group membership alone cannot open Workspace")

    workspace = get(client, "/Workspace/Index/%d" % workspace_id).text
    assigned = re.search(r'<select id="AssigneeId" name="AssigneeId">\s*<option value="(\d+)"', workspace)
    check(assigned is not None, "Workspace provides assignable team member")
    send(client, find_form(workspace, "/Workspace/CreateTask"), WorkspaceId=str(workspace_id),
         Title="Dashboard progress task " + suffix,
         AssigneeId=assigned.group(1), Priority="Normal")
    check("0/1 Task" in project_section(get(leader, group_path).text),
          "Dashboard updates total task count")
    for status in ("Doing", "Review", "Done"):
        workspace = get(client, "/Workspace/Index/%d" % workspace_id).text
        send(client, find_form(workspace, "/Workspace/UpdateTask"), status=status)
    check("1/1 Task" in project_section(get(leader, group_path).text),
          "Dashboard updates completed task count")
    check("1/1 Task" in project_section(get(member, group_path).text),
          "Completed task count is consistent for all assigned group members")
    check("Chưa có dự án nhóm nào" in project_section(get(spectator, group_path).text),
          "Team exclusion remains enforced after task updates")
    print("GROUP_DASHBOARD_E2E_PASSED group=%d bid=%d workspace=%d" %
          (group_id, bid_id, workspace_id), flush=True)


if __name__ == "__main__":
    main()
