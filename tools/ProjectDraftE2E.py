"""Calendar-date and draft-edit HTTP/SQL regression on NexusFreelanceMvp only.

Requires local IIS Express and SQL Server. --run-writes creates isolated fixtures.
"""
import argparse
import datetime as dt
import uuid

import requests
import urllib3

from ConcurrencyE2E import sql_scalar
from SmokeE2E import Smoke, forms, route_id


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--run-writes", action="store_true")
    if not parser.parse_args().run_writes:
        parser.error("Creates NexusFreelanceMvp development fixtures; add --run-writes")
    urllib3.disable_warnings(urllib3.exceptions.InsecureRequestWarning)
    smoke = Smoke("https://localhost:44391")
    suffix = uuid.uuid4().hex[:12]
    owner, outsider = requests.Session(), requests.Session()
    for session, label in ((owner, "owner"), (outsider, "outsider")):
        session.verify = False
        smoke.post(session, "/Account/Register", {
            "Name": "Draft " + label, "Email": f"draft-{label}-{suffix}@example.invalid",
            "Password": "DraftRegressionStrong!123", "Role": "Client"
        }, "/Account/Register")
    deadline = (dt.date.today() + dt.timedelta(days=7)).isoformat()
    values = {"Title": "Enterprise draft " + suffix, "Description": "Calendar deadline and draft-edit regression.",
              "ProjectType": "ENTERPRISE", "Budget": "25000000", "Deadline": deadline}
    result = smoke.post(owner, "/Projects/Create", values, "/Projects/Create")
    pid = route_id(result.headers.get("Location"))
    edit = f"/Projects/Edit/{pid}"
    manage = f"/Projects/Manage/{pid}"

    def check_deadline(expected):
        markup = smoke.get(owner, edit).text
        form = next(f for f in forms(markup) if "/Projects/Edit/" in f["action"])
        assert form["fields"]["Deadline"] == expected, (form["fields"]["Deadline"], expected)
        stored = sql_scalar(f"SELECT CONVERT(varchar(8), DeadlineUtc, 112) FROM dbo.Projects WHERE Id={pid}")
        assert stored == int(expected.replace("-", "")), (stored, expected)

    smoke.check("Created calendar deadline preserved in SQL and edit form", lambda: check_deadline(deadline))
    smoke.check("Enterprise type retained", lambda: _equal(sql_scalar(
        f"SELECT COUNT(*) FROM dbo.Projects WHERE Id={pid} AND ProjectType='ENTERPRISE'"), 1))
    smoke.check("Outsider cannot edit draft", lambda: smoke.get(outsider, edit, status=403))
    csrf = owner.post(smoke.base + edit, data=values, allow_redirects=False, timeout=35)
    smoke.check("Edit rejects missing CSRF", lambda: _equal(csrf.status_code, 400))
    invalid = smoke.post(owner, edit, {**values, "Title": ""}, edit, expect_redirect=False)
    smoke.check("Invalid edit stays on same form", lambda: _equal(invalid.status_code, 200))
    assert any("/Projects/Edit/" in f["action"] for f in forms(invalid.text))
    smoke.check("Invalid edit leaves stored title intact", lambda: _equal(sql_scalar(
        f"SELECT COUNT(*) FROM dbo.Projects WHERE Id={pid} AND Title=N'Enterprise draft {suffix}'"), 1))
    new_deadline = (dt.date.today() + dt.timedelta(days=9)).isoformat()
    smoke.post(owner, edit, {**values, "Deadline": new_deadline}, edit)
    smoke.check("Edited calendar deadline preserved", lambda: check_deadline(new_deadline))
    smoke.post(owner, "/Projects/AddRequirement", {
        "ProjectId": pid, "Title": "Enterprise contributor", "RequiredCount": "2"
    }, manage)
    smoke.post(owner, f"/Projects/Publish/{pid}", {}, manage)
    details = smoke.get(owner, f"/Projects/Details/{pid}").text
    display = dt.date.fromisoformat(new_deadline).strftime("%d/%m/%Y")
    smoke.check("Published details show selected calendar day", lambda: _equal(display in details, True))
    smoke.check("Published draft cannot reopen edit form", lambda: smoke.get(owner, edit, status=409))
    print(f"PROJECT_DRAFT_E2E_PASSED={len(smoke.rows)} PROJECT_ID={pid}")


def _equal(actual, expected):
    assert actual == expected, (actual, expected)


if __name__ == "__main__":
    main()
