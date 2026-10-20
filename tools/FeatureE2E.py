"""Isolated HTTP E2E for new proposal rejection and SQL-backed notifications.

Run only against localhost IIS Express and the disposable NexusFreelanceMvp database.
Creates Client/Freelancer/project/proposal test rows. Usage: --run-writes
"""
import argparse
import html
import html.parser
import re
import uuid

import requests
import urllib3

urllib3.disable_warnings(urllib3.exceptions.InsecureRequestWarning)
BASE = "https://localhost:44391"


class FormReader(html.parser.HTMLParser):
    def __init__(self):
        super().__init__()
        self.forms = []
        self.active = None

    def handle_starttag(self, tag, attributes):
        attrs = dict(attributes)
        if tag == "form":
            self.active = {"action": attrs.get("action", ""), "fields": {}}
        elif tag == "input" and self.active is not None and attrs.get("name"):
            self.active["fields"][attrs["name"]] = attrs.get("value", "")

    def handle_endtag(self, tag):
        if tag == "form" and self.active is not None:
            self.forms.append(self.active)
            self.active = None


def forms(markup):
    parser = FormReader()
    parser.feed(markup)
    return parser.forms


def find_form(markup, action_part):
    choices = [f for f in forms(markup) if action_part in f["action"]]
    if len(choices) != 1:
        raise AssertionError("Expected one form for %s, got %s" %
                             (action_part, [f["action"] for f in choices]))
    return choices[0]


def get(session, path, status=200):
    result = session.get(BASE + path, verify=False, timeout=25)
    result.encoding = "utf-8"
    if result.status_code != status:
        raise AssertionError("GET %s HTTP %s != %s" % (path, result.status_code, status))
    return result


def send(session, f, **values):
    data = dict(f["fields"])
    data.update(values)
    action = f["action"]
    if not action.startswith("/") or action.startswith("//"):
        raise AssertionError("Untrusted form path: " + action)
    result = session.post(BASE + action, data=data, verify=False, timeout=25)
    result.encoding = "utf-8"
    if result.status_code != 200:
        raise AssertionError("POST %s HTTP %s" % (action, result.status_code))
    return result


def register(role, suffix):
    session = requests.Session()
    form = find_form(get(session, "/Account/Register").text, "/Account/Register")
    email = "feature-%s-%s@example.invalid" % (role.lower(), suffix)
    result = send(session, form, Name="Feature Test " + role,
                  Email=email, Password="FeatureStrongPass!123", Role=role)
    if "Đăng xuất" not in html.unescape(result.text):
        raise AssertionError("Registration did not authenticate " + email)
    print("PASS Register " + role)
    return session


def main():
    argp = argparse.ArgumentParser()
    argp.add_argument("--run-writes", action="store_true")
    args = argp.parse_args()
    if not args.run_writes:
        argp.error("This test writes to NexusFreelanceMvp; pass --run-writes")

    guest = requests.Session()
    anon = guest.get(BASE + "/Notifications", verify=False, timeout=25, allow_redirects=False)
    if anon.status_code != 302 or "Login" not in anon.headers.get("Location", ""):
        raise AssertionError("Anonymous Notifications endpoint exposed: " + str(anon.status_code))
    print("PASS Anonymous notifications require login")

    suffix = uuid.uuid4().hex[:12]
    client = register("Client", suffix)
    freelancer = register("Freelancer", suffix)
    other = register("Freelancer", suffix + "b")

    create = find_form(get(client, "/Projects/Create").text, "/Projects/Create")
    response = send(client, create, Title="Notification verification " + suffix,
                    Description="Testing rejection and private notification workflows.",
                    ProjectType="SOLO", Budget="12500000")
    match = re.search(r"/Projects/Manage/(\d+)", response.url)
    if not match:
        raise AssertionError("Create did not redirect to management: " + response.url)
    project_id = int(match.group(1))

    manage = get(client, "/Projects/Manage/" + str(project_id)).text
    requirement = find_form(manage, "/Projects/AddRequirement")
    send(client, requirement, ProjectId=str(project_id), Title="Backend Developer", RequiredCount="1")
    manage = get(client, "/Projects/Manage/" + str(project_id)).text
    send(client, find_form(manage, "/Projects/Publish"))
    print("PASS Client creates and publishes project %d" % project_id)

    details = get(freelancer, "/Projects/Details/" + str(project_id)).text
    apply = find_form(details, "/Proposals/Apply")
    applied = send(freelancer, apply, Pitch="Feature E2E application", QuotedPrice="7000000")
    if "Đã gửi Proposal" not in html.unescape(applied.text):
        raise AssertionError("Apply did not succeed: URL=" + applied.url +
                             " messages=" + str(re.findall(r'<div[^>]*(?:alert|error)[^>]*>.*?</div>',
                                                         applied.text, re.S)[:4]) +
                             " fields=" + str(apply["fields"]))
    inbox = get(client, "/Notifications").text
    if "ứng viên mới" not in html.unescape(inbox):
        raise AssertionError("Client missing proposal notification; inbox=" +
                             re.sub(r"\s+", " ", inbox)[:600])
    if "ứng viên mới" in html.unescape(get(other, "/Notifications").text):
        raise AssertionError("Notification leaked across users")
    print("PASS Client proposal notification is private")

    manage = get(client, "/Projects/Manage/" + str(project_id)).text
    rejection = find_form(manage, "/Projects/Reject/")
    proposal_id = int(re.search(r"proposalId=(\d+)", rejection["action"]).group(1))
    send(client, rejection)
    manage = get(client, "/Projects/Manage/" + str(project_id)).text
    if "Rejected" not in manage:
        raise AssertionError("Proposal status not visible as Rejected")
    mine = get(freelancer, "/Proposals/Mine").text
    if "Rejected" not in mine:
        raise AssertionError("Freelancer proposal rejection invisible")
    freelancer_inbox = get(freelancer, "/Notifications").text
    if "bị từ chối" not in html.unescape(freelancer_inbox):
        raise AssertionError("Freelancer did not receive rejection notification")
    print("PASS Reject proposal %d; both users see status and notification" % proposal_id)

    mark_form = find_form(freelancer_inbox, "/Notifications/MarkRead/")
    mark_id = int(re.search(r"/MarkRead/(\d+)", mark_form["action"]).group(1))
    unauthorized = client.post(BASE + mark_form["action"], data=mark_form["fields"],
                               verify=False, timeout=25, allow_redirects=False)
    if unauthorized.status_code == 200 or unauthorized.status_code == 302:
        raise AssertionError("Cross-user read action succeeded")
    send(freelancer, mark_form)
    after = get(freelancer, "/Notifications").text
    if "Đã đọc" not in html.unescape(after):
        raise AssertionError("Read status not visible")
    print("PASS Only recipient can mark notification %d read" % mark_id)

    # A processed proposal must never be accepted afterwards.
    stale_action = "/Projects/Accept/%d?proposalId=%d" % (project_id, proposal_id)
    manage = get(client, "/Projects/Manage/" + str(project_id)).text
    token_form = find_form(manage, "/Projects/LockTeam/")
    stale = client.post(BASE + stale_action,
                        data={"__RequestVerificationToken": token_form["fields"]["__RequestVerificationToken"]},
                        verify=False, timeout=25)
    stale.encoding = "utf-8"
    if stale.status_code != 200 or "Proposal không hợp lệ hoặc đã xử lý" not in html.unescape(stale.text):
        raise AssertionError("Stale acceptance not rejected")
    print("PASS Rejected proposal cannot later be accepted")
    print("FEATURE_E2E_PASSED=8 PROJECT_ID=%d PROPOSAL_ID=%d" % (project_id, proposal_id))


if __name__ == "__main__":
    main()
