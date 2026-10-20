"""Local HTTP privacy regression suite; writes only to disposable NexusFreelanceMvp.

Requires running IIS Express at https://localhost:44391 and --run-writes.
Creates fresh Freelancer users and a Group. Does not clean up the test fixtures.
"""
import argparse
import re
import uuid
from urllib.parse import quote

import requests

from FeatureE2E import BASE, find_form, get, register, send
from GroupE2E import respond_form

CHECK_COUNT = 0


def check(condition, message):
    global CHECK_COUNT
    if not condition:
        raise AssertionError(message)
    CHECK_COUNT += 1
    print("PASS " + message)


def update_profile(session, core_role, is_public):
    page = get(session, "/Profile/Index").text
    result = send(session, find_form(page, "/Profile/Update"),
                  CoreRole=core_role, Bio="Privacy E2E profile",
                  IsAvailable="true", IsPublic=str(is_public).lower())
    match = re.search(r"/Talent/Freelancer/(\d+)", result.text)
    return int(match.group(1)) if match else None


def directory(kind, query):
    return get(requests.Session(), "/Talent/Index?kind=" + kind + "&q=" + quote(query)).text


def listing_contains(kind, query, path):
    # Search echoes query in the input field even when the results are empty.
    # Check a rendered result link rather than the untrusted query string.
    return 'href="' + path + '"' in directory(kind, query)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--run-writes", action="store_true")
    args = parser.parse_args()
    if not args.run_writes:
        parser.error("Writes test fixtures to NexusFreelanceMvp; pass --run-writes")

    suffix = uuid.uuid4().hex[:12]
    leader_role = "PrivacyLeader" + suffix
    member_role = "PrivacyMember" + suffix
    group_name = "Privacy Group " + suffix
    leader = register("Freelancer", "privacy-leader-" + suffix)
    member = register("Freelancer", "privacy-member-" + suffix)
    outsider = register("Freelancer", "privacy-outsider-" + suffix)
    guest = requests.Session()

    leader_id = update_profile(leader, leader_role, True)
    member_id = update_profile(member, member_role, True)
    check(leader_id is not None and member_id is not None,
          "Freelancer profiles publish their own public URLs")
    check(listing_contains("freelancer", leader_role,
                           "/Talent/Freelancer/" + str(leader_id)) and
          listing_contains("freelancer", member_role,
                           "/Talent/Freelancer/" + str(member_id)),
          "Public profiles appear in search")

    create = find_form(get(leader, "/Groups/Index").text, "/Groups/Create")
    result = send(leader, create, Name=group_name, Description="Privacy permissions test",
                  IsPublic="true")
    match = re.search(r"/Groups/Details/(\d+)", result.url)
    check(match is not None, "Leader creates a public group")
    group_id = match.group(1)
    group_url = "/Talent/Group/" + group_id

    check(listing_contains("group", group_name, group_url) and
          group_name in get(guest, group_url).text,
          "Public group appears in search and has public details")
    invite = find_form(get(leader, "/Groups/Details/" + group_id).text, "/Groups/Invite")
    invite_email = "feature-freelancer-privacy-member-" + suffix + "@example.invalid"
    send(leader, invite, GroupId=group_id, Email=invite_email)
    send(member, respond_form(get(member, "/Groups/Index").text, True))
    public_group = get(guest, group_url).text
    check(member_role in public_group and "2 thành viên" in public_group,
          "Active public member is counted and shown")

    update_profile(member, member_role, False)
    check(get(guest, "/Talent/Freelancer/" + str(member_id), status=404).status_code == 404 and
          not listing_contains("freelancer", member_role,
                               "/Talent/Freelancer/" + str(member_id)),
          "Private Freelancer profile disappears from directory and direct URL")
    public_group = get(guest, group_url).text
    check(member_role not in public_group and "1 thành viên" in public_group,
          "Private member is omitted from public group details and count")
    check(not listing_contains("freelancer", member_role,
                               "/Talent/Freelancer/" + str(member_id)),
          "Public search does not leak opted-out skills")

    group_page = get(leader, "/Groups/Details/" + group_id).text
    check("Xem hồ sơ nhóm công khai" in group_page,
          "Leader sees valid public-group link")
    visibility_form = find_form(group_page, "/Groups/UpdateVisibility/")
    unauthorized = member.post(BASE + visibility_form["action"],
                               data={"isPublic": "false",
                                     "__RequestVerificationToken":
                                         find_form(get(member, "/Profile/Index").text,
                                                   "/Profile/Update")["fields"]["__RequestVerificationToken"]},
                               verify=False, timeout=25, allow_redirects=False)
    check(unauthorized.status_code == 403,
          "Member cannot change group visibility with a valid own CSRF token")
    check(group_name in get(guest, group_url).text,
          "Unauthorized request did not change group state")

    missing_token = leader.post(BASE + visibility_form["action"],
                                data={"isPublic": "false"}, verify=False, timeout=25,
                                allow_redirects=False)
    check(missing_token.status_code >= 400 and
          group_name in get(guest, group_url).text,
          "Group visibility form rejects missing CSRF token")

    send(leader, visibility_form, isPublic="false")
    check(not listing_contains("group", group_name, group_url) and
          get(guest, group_url, status=404).status_code == 404,
          "Hidden group is excluded from directory and direct URL")
    check(group_name in get(member, "/Groups/Details/" + group_id).text,
          "Private group remains accessible to active members")
    check(get(outsider, "/Groups/Details/" + group_id, status=404).status_code == 404,
          "Outsider cannot view private group membership")

    group_page = get(leader, "/Groups/Details/" + group_id).text
    send(leader, find_form(group_page, "/Groups/UpdateVisibility/"), isPublic="true")
    check(group_name in get(guest, group_url).text,
          "Leader can republish group")
    update_profile(leader, leader_role, False)
    check(not listing_contains("group", group_name, group_url) and
          get(guest, group_url, status=404).status_code == 404,
          "Leader profile opt-out also hides entire group")
    check("Xem hồ sơ nhóm công khai" not in
          get(leader, "/Groups/Details/" + group_id).text,
          "Private Leader does not see a broken public-group link")
    update_profile(leader, leader_role, True)
    update_profile(member, member_role, True)
    check(listing_contains("group", group_name, group_url) and
          member_role in get(guest, group_url).text and
          "Xem hồ sơ nhóm công khai" in get(leader, "/Groups/Details/" + group_id).text,
          "Restored profile visibility restores group link and member listing")
    print("TALENT_PRIVACY_E2E_PASSED=%d GROUP_ID=%s" % (CHECK_COUNT, group_id))


if __name__ == "__main__":
    main()
