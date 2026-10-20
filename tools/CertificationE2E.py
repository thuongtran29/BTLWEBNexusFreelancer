"""External certificate submission/moderation on NexusFreelanceMvp development DB.

Requires --run-writes. No remote certificate URL is fetched. Temporary Admin
privileges are removed in finally; certificate fixtures persist for inspection.
"""
import argparse
import datetime as dt
import html
import uuid

from AdminE2E import form_for, power_shell, request
from AdminSkillsE2E import forms, register
from ConcurrencyE2E import sql_scalar


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--run-writes", action="store_true")
    if not parser.parse_args().run_writes:
        parser.error("Creates NexusFreelanceMvp certificate fixtures; add --run-writes")
    suffix = uuid.uuid4().hex[:12]
    worker, email = register("Freelancer", "cert-" + suffix)
    form = form_for(request(worker, "GET", "/Skills/Certifications").text, "/Skills/SubmitCertification")
    today = dt.date.today()
    uid = sql_scalar(f"SELECT Id FROM dbo.Users WHERE Email=N'{email}'")
    passed = 0

    def check(condition, label):
        nonlocal passed
        assert condition, label
        passed += 1
        print("PASS " + label, flush=True)

    def submit(label, issued, expires=""):
        name = "Certificate " + label + " " + suffix
        payload = {**form["fields"], "Name": name, "Issuer": "Fixture issuer",
                   "EvidenceUrl": "https://example.invalid/certificate", "IssuedOn": issued,
                   "ExpiresOn": expires}
        request(worker, "POST", form["action"], 302, data=payload, allow_redirects=False)
        cid = sql_scalar(f"SELECT Id FROM dbo.ExternalCertifications WHERE UserAccountId={uid} AND Name=N'{name}'")
        check(cid > 0, "Submit " + label + " certificate")
        return cid, name

    current, current_name = submit("issued-today", today.isoformat())
    expiring, expiring_name = submit("expires-today", (today - dt.timedelta(days=2)).isoformat(), today.isoformat())
    expired, expired_name = submit("expired", (today - dt.timedelta(days=3)).isoformat(),
                                   (today - dt.timedelta(days=1)).isoformat())
    rejected, rejected_name = submit("rejected", today.isoformat())
    request(worker, "POST", form["action"], 400, data={"Name": "Missing CSRF"}, allow_redirects=False)
    check(True, "Submission requires CSRF")
    profile = form_for(request(worker, "GET", "/Profile/Index").text, "/Profile/Update")
    request(worker, "POST", profile["action"], 302, allow_redirects=False,
            data={**profile["fields"], "CoreRole": "Backend Developer", "Bio": "Certificate fixture", "IsPublic": "true"})
    public = request(worker, "GET", f"/Talent/Freelancer/{uid}").text
    check(current_name not in public, "Pending certificate stays private")
    request(worker, "GET", "/Admin/Certifications", 403)
    check(True, "Freelancer cannot moderate certificates")

    admin, admin_email = register("Client", "cert-admin-" + suffix, name="Admin E2E")
    elevated = False
    try:
        power_shell("BootstrapAdmin.ps1", admin_email)
        elevated = True
        page = request(admin, "GET", "/Admin/Certifications").text

        def review(cid, decision):
            selected = next(f for f in forms(page) if "/Admin/ReviewCertification" in f["action"]
                            and f["fields"].get("Id") == str(cid))
            payload = {**selected["fields"], "Decision": decision, "Reason": "Fixture evidence reviewed"}
            request(admin, "POST", selected["action"], 302, data=payload, allow_redirects=False)
            request(admin, "POST", selected["action"], 409, data=payload, allow_redirects=False)
            check(True, decision + " persists once, replay is rejected")

        review(current, "Verified")
        review(expiring, "Verified")
        review(expired, "Verified")
        review(rejected, "Rejected")
        public = request(worker, "GET", f"/Talent/Freelancer/{uid}").text
        check(current_name in public and expiring_name in public, "Verified certificates remain public through expiry day")
        check(expired_name not in public and rejected_name not in public, "Expired and rejected certificates stay out of public profile")
        own = html.unescape(request(worker, "GET", "/Skills/Certifications").text)
        check("Đã hết hạn" in own and "Bị từ chối" in own, "Owner sees expiry and moderation states")
        check(sql_scalar(f"SELECT COUNT(*) FROM dbo.AdminAuditLogs WHERE TargetUserId={uid} "
                         "AND Action IN ('VerifyCertification','RejectCertification')") == 4,
              "All four review decisions have SQL audit records")
        print(f"CERTIFICATION_E2E_PASSED={passed}")
    finally:
        if elevated:
            power_shell("ResetTestAdmin.ps1", admin_email)


if __name__ == "__main__":
    main()
