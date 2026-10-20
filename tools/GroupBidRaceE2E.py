"""Concurrent group-bid acceptance and withdrawal checks, isolated test DB only."""
import argparse
import concurrent.futures
import html
import re
import uuid

from FeatureE2E import BASE, find_form, forms, get, register, send
from GroupE2E import respond_form
import GroupBidE2E as gb


def check(value, reason):
    if not value:
        raise AssertionError(reason)
    print("PASS " + reason, flush=True)


def make_group(suffix, name):
    leader = register("Freelancer", name + "-leader-" + suffix)
    member = register("Freelancer", name + "-member-" + suffix)
    page = get(leader, "/Groups/Index").text
    result = send(leader, find_form(page, "/Groups/Create"),
                  Name="Race group " + name + " " + suffix,
                  Description="Concurrency bid verification")
    group_id = int(re.search(r"/Groups/Details/(\d+)", result.url).group(1))
    send(leader, find_form(result.text, "/Groups/Invite"),
         GroupId=str(group_id),
         Email="feature-freelancer-%s-member-%s@example.invalid" % (name, suffix))
    send(member, respond_form(get(member, "/Groups/Index").text, True))
    return leader, member, group_id


def decision_form(client, project_id, bid_id):
    markup = get(client, "/Projects/Manage/" + str(project_id)).text
    return next(f for f in forms(markup)
                if "/GroupBids/Decide/" + str(bid_id) in f["action"]
                and "accept=True" in html.unescape(f["action"]))


def race(a, b):
    with concurrent.futures.ThreadPoolExecutor(max_workers=2) as executor:
        fa = executor.submit(a)
        fb = executor.submit(b)
        return fa.result(timeout=55), fb.result(timeout=55)


def request(session, form):
    response = session.post(BASE + form["action"], data=form["fields"],
                            verify=False, allow_redirects=False, timeout=40)
    return response.status_code


def accepted(project_id):
    return gb.count("dbo.GroupBids WHERE ProjectId=%d AND Status='Accepted'" % project_id)


def assignments(project_id):
    return gb.count("dbo.RoleAssignments WHERE ProjectId=%d" % project_id)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--run-writes", action="store_true")
    args = parser.parse_args()
    if not args.run_writes:
        parser.error("Writes test records: --run-writes required for NexusFreelanceMvp")

    suffix = uuid.uuid4().hex[:10]
    client = register("Client", "race-bids-client-" + suffix)
    gb.CLIENT = client
    leader_a, member_a, group_a = make_group(suffix, "a")
    leader_b, _, group_b = make_group(suffix, "b")

    project = gb.new_project(client, suffix, 1)
    bid_a, _ = gb.submit_bid(leader_a, group_a, project)
    bid_b, _ = gb.submit_bid(leader_b, group_b, project)
    accept_a = decision_form(client, project, bid_a)
    accept_b = decision_form(client, project, bid_b)
    status_a, status_b = race(lambda: request(client, accept_a),
                              lambda: request(client, accept_b))
    print("OBSERVATION group competition HTTP=%s,%s" % (status_a, status_b), flush=True)
    check(status_a < 500 and status_b < 500, "Competing bid requests avoid server errors")
    check(accepted(project) == 1, "Exactly one complete group wins the two seats")
    check(assignments(project) == 2, "Competing groups cannot overfill positions")
    check(gb.count("dbo.Proposals WHERE ProjectId=%d AND Status='Accepted'" % project) == 2,
          "Winning group accepts two proposals atomically")
    check(gb.count("dbo.TeamMembers tm JOIN dbo.ProjectTeams pt ON tm.ProjectTeamId=pt.Id "
                   "WHERE pt.ProjectId=%d" % project) == 2,
          "Exactly two team members follow winning group")

    second_project = gb.new_project(client, suffix, 2)
    pending, _ = gb.submit_bid(leader_a, group_a, second_project)
    accept = decision_form(client, second_project, pending)
    group_page = get(leader_a, "/Groups/Details/" + str(group_a)).text
    withdraw = next(f for f in forms(group_page)
                    if "/GroupBids/Withdraw/" + str(pending) in f["action"])
    client_result, leader_result = race(lambda: request(client, accept),
                                        lambda: request(leader_a, withdraw))
    print("OBSERVATION group accept/withdraw HTTP=%s,%s" %
          (client_result, leader_result), flush=True)
    check(client_result < 500 and leader_result < 500,
          "Accept/withdraw collision avoids server errors")
    check(gb.count("dbo.GroupBids WHERE Id=%d AND Status IN ('Accepted','Withdrawn')" % pending) == 1,
          "Pending group transitions exactly once")
    check(assignments(second_project) == 2 * accepted(second_project),
          "Withdrawn bid creates no assignments; accepted bid creates both")
    check(gb.count("dbo.Proposals WHERE GroupBidId=%d AND Status=(SELECT Status "
                   "FROM dbo.GroupBids WHERE Id=%d)" % (pending, pending)) == 2,
          "Child proposal states match the group decision")
    print("GROUP_BID_RACE_PASSED=9 projects=%d,%d" %
          (project, second_project), flush=True)


if __name__ == "__main__":
    main()
