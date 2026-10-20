"""Read-only HTTP smoke tests for anonymous public talent pages.

Uses only GET requests. No accounts or database rows are created.
"""

import argparse
import html
import re
import ssl
from urllib.error import HTTPError
from urllib.parse import quote, urlsplit
from urllib.request import urlopen


def run(base_url):
    base_url = base_url.rstrip("/")
    if urlsplit(base_url).hostname not in ("localhost", "127.0.0.1"):
        raise ValueError("TalentE2E must target a local development instance")

    context = ssl._create_unverified_context()  # Local IIS Express development certificate.
    passed = 0

    def check(condition, description):
        nonlocal passed
        if not condition:
            raise AssertionError(description)
        passed += 1
        print("PASS", description)

    def get(path):
        try:
            with urlopen(base_url + path, context=context, timeout=20) as response:
                return response.status, response.read().decode("utf-8", "replace")
        except HTTPError as error:
            return error.code, error.read().decode("utf-8", "replace")

    def ids_for(page, kind):
        target = re.compile(r"/Talent/" + kind + r"/(\d+)", re.IGNORECASE)
        return set(target.findall(html.unescape(page)))

    status, freelancers = get("/Talent")
    check(status == 200, "Anonymous freelancer directory is available")
    check("talent-query" in freelancers, "Freelancer directory offers search")

    status, groups = get("/Talent?kind=group")
    check(status == 200, "Anonymous group directory is available")
    check("Nhóm chuyên môn" in groups, "Group tab is rendered")

    status, _ = get("/Talent?q=" + "x" * 101)
    check(status == 400, "Query longer than 100 characters is rejected")

    for kind in ("Freelancer", "Group"):
        status, _ = get("/Talent/" + kind + "/99999999")
        check(status == 404, kind + " unknown profile returns 404")

    status, _ = get("/Talent?page=2147483647")
    check(status == 200, "Freelancer page overflow is bounded")
    status, _ = get("/Talent?kind=group&page=-5")
    check(status == 200, "Negative group page is bounded")

    payload = '<img src=x onerror=alert(1)>'
    status, searched = get("/Talent?q=" + quote(payload))
    check(status == 400 or (status == 200 and payload not in searched),
          "Markup input is rejected or rendered safely without injection")

    for kind, listing in (("Freelancer", freelancers), ("Group", groups)):
        profile_ids = ids_for(listing, kind)
        check(len(profile_ids) <= 12, kind + " directory respects page size")
        for profile_id in list(sorted(profile_ids, key=int))[:3]:
            status, detail = get("/Talent/" + kind + "/" + profile_id)
            check(status == 200, kind + " existing profile " + profile_id + " renders")
            check("mailto:" not in detail.lower(),
                  kind + " profile " + profile_id + " does not link a private email")

    print("TALENT_E2E_PASSED", passed)


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--base-url", default="https://localhost:44391")
    run(parser.parse_args().base_url)
