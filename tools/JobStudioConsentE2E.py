"""Local Job Studio consent and persisted origin checks; never requests external AI.

--run-writes creates throwaway fixtures in NexusFreelanceMvp. The forged-consent
case runs only when the server does not offer external AI on the form.
"""
import argparse
import re
import uuid

import requests
import urllib3

from ConcurrencyE2E import sql_scalar
from SmokeE2E import Smoke, route_id


def equal(actual, expected):
    assert actual == expected, (actual, expected)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--run-writes", action="store_true")
    if not parser.parse_args().run_writes:
        parser.error("Creates NexusFreelanceMvp test fixtures; add --run-writes")
    urllib3.disable_warnings(urllib3.exceptions.InsecureRequestWarning)
    smoke = Smoke("https://localhost:44391")
    owner, outsider, freelancer = (requests.Session() for _ in range(3))
    guest = requests.Session()
    guest.verify = False
    response = guest.get(smoke.base + "/JobStudio/New", allow_redirects=False, timeout=35)
    smoke.check("Guest must authenticate", lambda: equal(response.status_code, 302))
    suffix = uuid.uuid4().hex[:12]
    for session, label, role in ((owner, "owner", "Client"), (outsider, "outsider", "Client"),
                                  (freelancer, "worker", "Freelancer")):
        session.verify = False
        smoke.post(session, "/Account/Register", {
            "Name": "Consent " + label, "Email": f"consent-{label}-{suffix}@example.invalid",
            "Password": "ConsentFixtureStrong!123", "Role": role
        }, "/Account/Register")
    smoke.check("Freelancer cannot use Client Job Studio", lambda: smoke.get(
        freelancer, "/JobStudio/New", status=403))
    page = smoke.get(owner, "/JobStudio/New").text
    ai_available = 'name="UseExternalAi"' in page
    idea = {"Idea": "Local consent regression " + suffix, "ProjectType": "SOLO", "Budget": "3000000"}
    no_csrf = owner.post(smoke.base + "/JobStudio/Generate", data=idea, allow_redirects=False, timeout=35)
    smoke.check("Generate requires CSRF", lambda: equal(no_csrf.status_code, 400))

    def generate(extra):
        result = smoke.post(owner, "/JobStudio/Generate", {**idea, **extra}, "/JobStudio/New")
        did = int(re.search(r"/JobStudio/Edit/(\d+)", result.headers["Location"]).group(1))
        count = sql_scalar(f"SELECT COUNT(*) FROM dbo.AIJobDraftVersions WHERE AIJobDraftId={did} "
                           "AND Generator='RuleBasedDemo-v2'")
        equal(count, 1)
        return did

    draft = smoke.check("Omitted consent persists rule-based origin", lambda: generate({}))
    smoke.check("Other Client cannot open private draft", lambda: smoke.get(
        outsider, f"/JobStudio/Edit/{draft}", status=404))
    if not ai_available:
        smoke.check("Forged consent with disabled provider still persists rule-based origin",
                    lambda: generate({"UseExternalAi": "true"}))
    applied = smoke.post(owner, f"/JobStudio/Apply/{draft}", {}, f"/JobStudio/Edit/{draft}")
    pid = route_id(applied.headers["Location"])
    smoke.check("Apply creates unpublished Project", lambda: equal(sql_scalar(
        f"SELECT COUNT(*) FROM dbo.Projects WHERE Id={pid} AND Status='Draft'"), 1))
    smoke.post(owner, f"/JobStudio/Apply/{draft}", {}, f"/JobStudio/Edit/{draft}")
    smoke.check("Repeat Apply keeps one project link", lambda: equal(sql_scalar(
        f"SELECT COUNT(*) FROM dbo.AIJobDrafts WHERE Id={draft} AND AppliedProjectId={pid}"), 1))
    print(f"JOB_STUDIO_CONSENT_E2E_PASSED={len(smoke.rows)} EXTERNAL_AI_UI={ai_available}")


if __name__ == "__main__":
    main()
