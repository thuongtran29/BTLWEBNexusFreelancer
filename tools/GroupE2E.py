"""Freelancer Group invitation/permission E2E on local IIS Express and NexusFreelanceMvp only.

Requires --run-writes; creates disposable uniquely named user and group records.
"""
import argparse
import html
import re
import uuid

from FeatureE2E import BASE, find_form, forms, get, register, send


def check(value, reason):
    if not value:
        raise AssertionError(reason)
    print("PASS " + reason)


def respond_form(markup, decision):
    wanted = "accept=True" if decision else "accept=False"
    candidates = [f for f in forms(markup) if "/Groups/Respond" in f["action"]
                  and wanted.lower() in html.unescape(f["action"]).lower()]
    if len(candidates) != 1:
        raise AssertionError("Expected unique response form: " + wanted)
    return candidates[0]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--run-writes", action="store_true")
    args = parser.parse_args()
    if not args.run_writes:
        parser.error("This test writes to isolated NexusFreelanceMvp; pass --run-writes")

    suffix = uuid.uuid4().hex[:12]
    leader = register("Freelancer", "group-leader-" + suffix)
    invited = register("Freelancer", "group-member-" + suffix)
    outsider = register("Freelancer", "group-outsider-" + suffix)
    client = register("Client", "group-client-" + suffix)
    check(get(client, "/Groups/Index", status=403).status_code == 403,
          "Client cannot access Freelancer Groups")

    create_page = get(leader, "/Groups/Index").text
    form = find_form(create_page, "/Groups/Create")
    name = "Group test " + suffix
    result = send(leader, form, Name=name, Description="Backend and testing specialists")
    match = re.search(r"/Groups/Details/(\d+)", result.url)
    check(match is not None, "Leader creates group with own active membership")
    group_id = match.group(1)

    check(get(outsider, "/Groups/Details/" + group_id, status=404).status_code == 404,
          "Outsider cannot read private group membership")
    invite_form = find_form(result.text, "/Groups/Invite")
    invite_email = "feature-freelancer-group-member-" + suffix + "@example.invalid"
    result = send(leader, invite_form, GroupId=group_id, Email=invite_email)
    check("Chờ phản hồi" in result.text, "Leader invites existing Freelancer")

    invite_list = get(invited, "/Groups/Index").text
    check(name in invite_list, "Invitation visible to intended recipient")
    check(name not in get(outsider, "/Groups/Index").text,
          "Invitation hidden from unrelated Freelancer")

    accepted = send(invited, respond_form(invite_list, True))
    check(name in accepted.text and not any("Respond" in f["action"] for f in forms(accepted.text)),
          "Recipient accepts invitation once")
    active_page = get(invited, "/Groups/Details/" + group_id)
    check(active_page.status_code == 200 and "Backend and testing" in active_page.text,
          "Accepted member accesses group")

    # Only the leader may remove members. Get member id from leader's generated button URL.
    leader_page = get(leader, "/Groups/Details/" + group_id).text
    member_match = re.search(r"/Groups/Remove/" + group_id + r"\?memberId=(\d+)", html.unescape(leader_page))
    check(member_match is not None, "Leader sees membership moderation control")
    member_id = member_match.group(1)
    csrf = find_form(get(outsider, "/Groups/Index").text, "/Groups/Create")["fields"]
    forbidden = outsider.post(BASE + "/Groups/Remove/" + group_id + "?memberId=" + member_id,
                             data=csrf, verify=False, timeout=25, allow_redirects=False)
    check(forbidden.status_code == 403, "Outsider cannot remove group member")

    own_page = get(invited, "/Groups/Details/" + group_id).text
    left = send(invited, find_form(own_page, "/Groups/Leave"))
    check(name not in left.text and get(invited, "/Groups/Details/" + group_id, status=404).status_code == 404,
          "Member leaves group and loses private access")

    leader_page = get(leader, "/Groups/Details/" + group_id).text
    reissued = send(leader, find_form(leader_page, "/Groups/Invite"), GroupId=group_id, Email=invite_email)
    check("Chờ phản hồi" in reissued.text, "Leader can re-invite former member")
    declined = send(invited, respond_form(get(invited, "/Groups/Index").text, False))
    check(name not in declined.text, "Recipient may decline invitation")

    send(leader, find_form(get(leader, "/Groups/Details/" + group_id).text, "/Groups/Invite"),
         GroupId=group_id, Email=invite_email)
    send(invited, respond_form(get(invited, "/Groups/Index").text, True))
    leader_page = get(leader, "/Groups/Details/" + group_id).text
    removed = send(leader, find_form(leader_page, "/Groups/Remove"))
    revoked = invited.get(BASE + "/Groups/Details/" + group_id, verify=False, timeout=25)
    revoked.encoding = "utf-8"
    check(revoked.status_code == 404 and "Removed freelancer #" in removed.text,
          "Leader removes member and revokes group access: " +
          "status=" + str(revoked.status_code) + ", event=" +
          str("Removed freelancer #" in removed.text))
    print("GROUP_E2E_PASSED=14")


if __name__ == "__main__":
    main()
