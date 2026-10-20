"""Isolated group-proposal HTTP and SQL E2E; run with --run-writes on test DB only."""
import argparse
import html
import re
import subprocess
import uuid

from FeatureE2E import BASE, find_form, forms, get, register, send
from GroupE2E import respond_form

SQLCMD = r"C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\180\Tools\Binn\SQLCMD.EXE"
PASSED = 0


def check(ok, description):
    global PASSED
    if not ok:
        raise AssertionError(description)
    PASSED += 1
    print("PASS " + description, flush=True)


def count(statement):
    command = [SQLCMD, "-S", r".\SQLEXPRESS", "-d", "NexusFreelanceMvp", "-E", "-C",
               "-h", "-1", "-W", "-Q", "SET NOCOUNT ON; SELECT COUNT(*) FROM " + statement]
    output = subprocess.run(command, capture_output=True, text=True, timeout=30, check=True)
    numbers = re.findall(r"(?m)^\s*(\d+)\s*$", output.stdout)
    if len(numbers) != 1:
        raise AssertionError("Unexpected SQL result: " + output.stdout)
    return int(numbers[0])


def new_project(client, suffix, index):
    created = send(client, find_form(get(client, "/Projects/Create").text, "/Projects/Create"),
                   Title="Group bid project " + suffix + " #" + str(index),
                   Description="Testing complete team acceptance with transactional assignments.",
                   ProjectType="TEAM", Budget="30000000")
    match = re.search(r"/Projects/Manage/(\d+)", created.url)
    check(match is not None, "Create team project " + str(index))
    project_id = int(match.group(1))
    for title in ("Backend Developer", "Frontend Developer"):
        manage = get(client, "/Projects/Manage/" + str(project_id)).text
        send(client, find_form(manage, "/Projects/AddRequirement"),
             ProjectId=str(project_id), Title=title, RequiredCount="1")
    send(client, find_form(get(client, "/Projects/Manage/" + str(project_id)).text,
                           "/Projects/Publish"))
    return project_id


def submit_bid(leader, group_id, project_id, outsider=None):
    project_page = get(leader, "/Projects/Details/" + str(project_id)).text
    roles = [int(f["fields"]["RoleRequirementId"]) for f in forms(project_page)
             if "/Proposals/Apply" in f["action"]]
    check(len(roles) == 2, "Project has two distinct roles")
    path = "/GroupBids/Create?groupId=%d&projectId=%d" % (group_id, project_id)
    if outsider is not None:
        check(get(outsider, path, 404).status_code == 404,
              "Only group leader may prepare a group bid")
    page = get(leader, path).text
    form = find_form(page, "/GroupBids/Create")
    members = sorted({int(value) for field, value in form["fields"].items()
                      if re.fullmatch(r"Candidates\[\d+\]\.UserAccountId", field)})
    check(len(members) == 2, "Both active members available for role assignment")
    payload = dict(form["fields"])
    for index in range(len(members)):
        payload["Candidates[%d].Selected" % index] = "true"
        payload["Candidates[%d].RoleRequirementId" % index] = str(roles[index])
        payload["Candidates[%d].QuotedPrice" % index] = "5000000"
    payload["Pitch"] = "Team proposal with two individually assigned specialists"
    missing_token = dict(payload)
    missing_token.pop("__RequestVerificationToken", None)
    csrf = leader.post(BASE + form["action"], data=missing_token,
                       verify=False, timeout=25, allow_redirects=False)
    check(csrf.status_code == 400, "Group bid requires anti-CSRF token (HTTP %d)" % csrf.status_code)
    response = leader.post(BASE + form["action"], data=payload,
                           verify=False, timeout=30, allow_redirects=True)
    response.encoding = "utf-8"
    check(response.status_code == 200 and "Đã gửi Proposal nhóm" in html.unescape(response.text),
          "Leader submits bid with two Matching snapshots")
    duplicate = leader.post(BASE + form["action"], data=payload,
                            verify=False, timeout=25, allow_redirects=False)
    check(duplicate.status_code == 409, "Duplicate group bid cannot create duplicate proposals")
    manage = get_client_manage(project_id)
    decision = [f for f in forms(manage) if "/GroupBids/Decide/" in f["action"]]
    pending = [int(value) for value in re.findall(r"/GroupBids/Withdraw/(\d+)",
                                                     html.unescape(response.text))]
    check(bool(pending), "Leader sees submitted group bid identifier")
    bid_id = max(pending)
    decision = [f for f in decision if "/GroupBids/Decide/" + str(bid_id) in f["action"]]
    check(len(decision) == 2, "Client sees actions for entire group bid")
    check(count("dbo.Proposals WHERE GroupBidId=" + str(bid_id)) == 2,
          "Bid persists two member proposals")
    check(count("dbo.MatchingResults WHERE ProposalId IN "
                "(SELECT Id FROM dbo.Proposals WHERE GroupBidId=" + str(bid_id) + ")") == 2,
          "Each member has independent matching snapshot")
    return bid_id, decision


def get_client_manage(project_id):
    return get(CLIENT, "/Projects/Manage/" + str(project_id)).text


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--run-writes", action="store_true")
    args = parser.parse_args()
    if not args.run_writes:
        parser.error("Explicit --run-writes required for NexusFreelanceMvp test records")
    suffix = uuid.uuid4().hex[:10]
    global CLIENT
    CLIENT = register("Client", "bid-client-" + suffix)
    leader = register("Freelancer", "bid-leader-" + suffix)
    member = register("Freelancer", "bid-member-" + suffix)
    outsider = register("Freelancer", "bid-outsider-" + suffix)
    group_page = get(leader, "/Groups/Index").text
    created = send(leader, find_form(group_page, "/Groups/Create"),
                   Name="Group bid specialists " + suffix,
                   Description="Two specialists joining for a project")
    group_id = int(re.search(r"/Groups/Details/(\d+)", created.url).group(1))
    member_email = "feature-freelancer-bid-member-" + suffix + "@example.invalid"
    send(leader, find_form(created.text, "/Groups/Invite"),
         GroupId=str(group_id), Email=member_email)
    send(member, respond_form(get(member, "/Groups/Index").text, True))
    check(get(member, "/Groups/Details/" + str(group_id)).status_code == 200,
          "Invited freelancer accepts group membership")

    accepted_project = new_project(CLIENT, suffix, 1)
    bid, decision = submit_bid(leader, group_id, accepted_project, outsider)
    accept_form = next(f for f in decision if "accept=True" in html.unescape(f["action"]))
    outsider_token = find_form(get(outsider, "/Groups/Index").text, "/Groups/Create")
    rejected_ownership = outsider.post(BASE + accept_form["action"],
                                       data=outsider_token["fields"], verify=False,
                                       timeout=25, allow_redirects=False)
    check(rejected_ownership.status_code == 403, "Freelancer cannot accept a group bid")
    send(CLIENT, accept_form)
    check(count("dbo.GroupBids WHERE Id=%d AND Status='Accepted'" % bid) == 1,
          "Group bid accepted")
    check(count("dbo.Proposals WHERE GroupBidId=%d AND Status='Accepted'" % bid) == 2,
          "Both proposals accepted atomically")
    check(count("dbo.RoleAssignments WHERE ProposalId IN "
                "(SELECT Id FROM dbo.Proposals WHERE GroupBidId=%d)" % bid) == 2,
          "Both role assignments created")
    check(count("dbo.TeamMembers WHERE RoleAssignmentId IN "
                "(SELECT Id FROM dbo.RoleAssignments WHERE ProposalId IN "
                "(SELECT Id FROM dbo.Proposals WHERE GroupBidId=%d))" % bid) == 2,
          "Both team members recorded")
    stale = CLIENT.post(BASE + accept_form["action"],
                        data=accept_form["fields"], verify=False, timeout=25, allow_redirects=False)
    check(stale.status_code == 409, "Second accept cannot process an accepted group")
    locked = send(CLIENT, find_form(get_client_manage(accepted_project), "/Projects/LockTeam/"))
    check("InProgress" in locked.text, "Client locks complete group team into Workspace")

    rejected_project = new_project(CLIENT, suffix, 2)
    rejected_bid, decision = submit_bid(leader, group_id, rejected_project)
    reject_form = next(f for f in decision if "accept=False" in html.unescape(f["action"]))
    send(CLIENT, reject_form)
    check(count("dbo.GroupBids WHERE Id=%d AND Status='Rejected'" % rejected_bid) == 1
          and count("dbo.Proposals WHERE GroupBidId=%d AND Status='Rejected'" % rejected_bid) == 2,
          "Reject applies to all proposals")

    withdrawn_project = new_project(CLIENT, suffix, 3)
    withdrawn_bid, _ = submit_bid(leader, group_id, withdrawn_project)
    group = get(leader, "/Groups/Details/" + str(group_id)).text
    withdraw_form = next(f for f in forms(group)
                         if "/GroupBids/Withdraw/" + str(withdrawn_bid) in f["action"])
    send(leader, withdraw_form)
    check(count("dbo.GroupBids WHERE Id=%d AND Status='Withdrawn'" % withdrawn_bid) == 1
          and count("dbo.Proposals WHERE GroupBidId=%d AND Status='Withdrawn'" % withdrawn_bid) == 2,
          "Withdraw applies to all proposals")

    stale_project = new_project(CLIENT, suffix, 4)
    stale_bid, decision = submit_bid(leader, group_id, stale_project)
    stale_accept = next(f for f in decision if "accept=True" in html.unescape(f["action"]))
    send(member, find_form(get(member, "/Groups/Details/" + str(group_id)).text,
                           "/Groups/Leave"))
    stale_response = CLIENT.post(BASE + stale_accept["action"],
                                 data=stale_accept["fields"], verify=False,
                                 timeout=25, allow_redirects=False)
    check(stale_response.status_code == 409,
          "Client cannot accept an old group bid after member departure")
    check(count("dbo.GroupBids WHERE Id=%d AND Status='Pending'" % stale_bid) == 1,
          "Rejected stale acceptance does not transition group bid")
    check(count("dbo.RoleAssignments WHERE ProjectId=%d" % stale_project) == 0,
          "Rejected stale acceptance creates no role assignments")
    check(count("dbo.Proposals WHERE GroupBidId=%d AND Status='Pending'" % stale_bid) == 2,
          "Rejected stale acceptance preserves both child proposals")
    print("GROUP_BID_E2E_PASSED=%d BIDS=%s,%s,%s" %
          (PASSED, bid, rejected_bid, withdrawn_bid), flush=True)


if __name__ == "__main__":
    main()
