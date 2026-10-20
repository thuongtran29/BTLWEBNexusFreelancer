"""HTTP integration test for login lockout. Writes throwaway accounts to test DB.

Run: python tools/LoginThrottleE2E.py --base https://localhost:44391 --run-writes
"""
import argparse
import uuid

import requests
import urllib3

from SmokeE2E import forms


urllib3.disable_warnings(urllib3.exceptions.InsecureRequestWarning)
PASSWORD = "LocalThrottleE2E_ValidPass!123"


def submit(session, base, path, fields):
    response = session.get(base + path, verify=False, timeout=30)
    assert response.status_code == 200, (path, response.status_code)
    form = next(f for f in forms(response.text) if path in f["action"])
    payload = {**form["fields"], **fields}
    return session.post(base + form["action"], data=payload, verify=False,
                        allow_redirects=False, timeout=30)


def register(base, email):
    response = submit(requests.Session(), base, "/Account/Register",
                      {"Email": email, "Name": "Throttle E2E", "Role": "Client",
                       "Password": PASSWORD})
    assert response.status_code == 302, ("registration", response.status_code)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--base", default="https://localhost:44391")
    parser.add_argument("--run-writes", action="store_true")
    args = parser.parse_args()
    if not args.run_writes:
        parser.error("Creates throwaway accounts in the test database; pass --run-writes")
    base = args.base.rstrip("/")
    suffix = uuid.uuid4().hex[:12]
    target_email = f"lockout-{suffix}@example.invalid"
    other_email = f"lockout-other-{suffix}@example.invalid"
    register(base, target_email)
    register(base, other_email)
    print("PASS register isolated login test accounts", flush=True)

    attacker = requests.Session()
    for attempt in range(5):
        response = submit(attacker, base, "/Account/Login",
                          {"Email": target_email, "Password": "IncorrectPassword_123!"})
        assert response.status_code == 200, (attempt, response.status_code)
    print("PASS five invalid logins are rejected", flush=True)

    blocked = submit(attacker, base, "/Account/Login",
                     {"Email": target_email, "Password": PASSWORD})
    assert blocked.status_code == 429, blocked.status_code
    assert 1 <= int(blocked.headers.get("Retry-After", "0")) <= 900
    blocked.encoding = "utf-8"
    assert '<form' in blocked.text and '__RequestVerificationToken' in blocked.text
    print("PASS correct password is throttled with HTTP 429 and Retry-After", flush=True)

    visitor = requests.Session()
    success = submit(visitor, base, "/Account/Login",
                     {"Email": other_email, "Password": PASSWORD})
    assert success.status_code == 302, success.status_code
    print("PASS unrelated valid account can still sign in", flush=True)
    print("LOGIN_THROTTLE_E2E_PASSED=4", flush=True)


if __name__ == "__main__":
    main()
