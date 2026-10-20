"""Workspace binary file HTTP and SQL regression for NexusFreelanceMvp only.

Requires IIS Express at https://localhost:44391 and SQL Express .\\SQLEXPRESS.
Creates unique development records and retains them for inspection.
Run: python tools/WorkspaceFilesE2E.py --run-writes
"""

import argparse
import hashlib
import re
import uuid

import requests
import urllib3

from ConcurrencyE2E import sql_scalar
from SmokeE2E import Smoke, forms, route_id


BASE = "https://localhost:44391"
UPLOAD = "/WorkspaceFiles/Upload"


def exactly_one_form(markup, action_fragment, revision=None):
    found = [f for f in forms(markup)
             if action_fragment in f["action"] and
             (revision is None or ("FileId" in f["fields"]) == revision)]
    if len(found) != 1:
        raise AssertionError(f"Expected one {action_fragment} form (revision={revision}), found {len(found)}")
    return found[0]


def status(response, expected, context):
    if response.status_code != expected:
        raise AssertionError(f"{context}: HTTP {response.status_code}, expected {expected}: {response.text[:250]}")
    return response


def post_file(session, fields, filename, content, expected, context):
    response = session.post(BASE + UPLOAD, data=fields,
                            files={"upload": (filename, content, "application/octet-stream")},
                            timeout=45, allow_redirects=False)
    return status(response, expected, context)


def token_form(smoke, session, path, action_fragment=UPLOAD, revision=None):
    return exactly_one_form(smoke.get(session, path).text, action_fragment, revision)


def verify_download(session, version_id, expected_bytes, expected_name):
    response = session.get(BASE + f"/WorkspaceFiles/Download/{version_id}", timeout=35,
                           allow_redirects=False)
    status(response, 200, "Download")
    assert response.content == expected_bytes, "Downloaded bytes differ from original"
    assert response.headers.get("X-Content-Type-Options") == "nosniff"
    assert "attachment" in response.headers.get("Content-Disposition", "").lower()
    assert expected_name in response.headers.get("Content-Disposition", "")
    assert "no-store" in response.headers.get("Cache-Control", "").lower()


def sql_version(file_id, version_id, number, content, uploader_email):
    digest = hashlib.sha256(content).hexdigest().upper()
    email = uploader_email.replace("'", "''")
    query = ("SELECT COUNT(*) FROM dbo.WorkspaceFileVersions v "
             "JOIN dbo.Users u ON u.Id=v.UploadedById "
             f"WHERE v.Id={version_id} AND v.WorkspaceFileId={file_id} "
             f"AND v.VersionNumber={number} AND v.ByteLength={len(content)} "
             f"AND v.Sha256='{digest}' AND u.Email='{email}' "
             f"AND v.Data=0x{content.hex()}")
    assert sql_scalar(query) == 1, f"SQL content, hash, owner or version mismatch: {number}"


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--run-writes", action="store_true")
    parser.add_argument("--exercise-pagination", action="store_true",
                        help="Verify latest revisions on older workspace history pages")
    args = parser.parse_args()
    if not args.run_writes:
        parser.error("Creates records in NexusFreelanceMvp; supply --run-writes")
    assert sql_scalar("SELECT CASE WHEN DB_NAME()='NexusFreelanceMvp' THEN 1 ELSE 0 END") == 1
    urllib3.disable_warnings(urllib3.exceptions.InsecureRequestWarning)

    smoke = Smoke(BASE)
    suffix = uuid.uuid4().hex[:12]
    client, worker, outsider, guest = (requests.Session() for _ in range(4))
    for session in (client, worker, outsider, guest):
        session.verify = False

    emails = {}
    for session, role, label in ((client, "Client", "client"),
                                 (worker, "Freelancer", "worker"),
                                 (outsider, "Freelancer", "outsider")):
        email = f"files-{label}-{suffix}@example.invalid"
        emails[label] = email
        smoke.post(session, "/Account/Register", {
            "Name": "Files " + label, "Email": email,
            "Password": "FilesE2EStrong!123", "Role": role
        }, "/Account/Register")
        assert ".ASPXAUTH" in session.cookies

    project = smoke.post(client, "/Projects/Create", {
        "Title": "File regression " + suffix,
        "Description": "Verify file bytes, versions, CSRF and team access.",
        "ProjectType": "SOLO", "Budget": "1000000"
    }, "/Projects/Create")
    project_id = route_id(project.headers.get("Location"))
    manage = f"/Projects/Manage/{project_id}"
    smoke.post(client, "/Projects/AddRequirement", {
        "ProjectId": project_id, "Title": "Backend contributor", "RequiredCount": "1"
    }, manage)
    smoke.post(client, f"/Projects/Publish/{project_id}", {}, manage)
    details = f"/Projects/Details/{project_id}"
    proposal = exactly_one_form(smoke.get(worker, details).text, "/Proposals/Apply")
    smoke.post(worker, "/Proposals/Apply", {
        "ProjectId": project_id,
        "RoleRequirementId": proposal["fields"]["RoleRequirementId"],
        "QuotedPrice": "900000", "Pitch": "Binary file regression member"
    }, details)
    accept = exactly_one_form(smoke.get(client, manage).text, "/Projects/Accept/")
    smoke.post(client, accept["action"], {}, manage)
    smoke.post(client, f"/Projects/LockTeam/{project_id}", {}, manage)
    match = re.search(r"/Workspace/Index/(\d+)", smoke.get(client, manage).text)
    assert match, "Locked project has no workspace link"
    workspace_id = int(match.group(1))
    workspace = f"/Workspace/Index/{workspace_id}"

    smoke.check("Unrelated freelancer cannot inspect workspace", lambda:
                smoke.get(outsider, workspace, status=403))
    new_file = token_form(smoke, client, workspace, UPLOAD, revision=False)
    file_a = ("NexusFreelance workspace v1 " + suffix + "\n").encode("utf-8")
    name_a = "test-source.txt"

    smoke.check("Missing CSRF prevents file upload", lambda:
                post_file(client, {"WorkspaceId": workspace_id}, name_a, file_a,
                          400, "Missing CSRF"))
    smoke.check("Guest cannot upload", lambda:
                post_file(guest, {"WorkspaceId": workspace_id}, name_a, file_a,
                          302, "Guest upload"))
    outsider_token = token_form(smoke, outsider, "/Groups/Index", "/Account/Logout")
    smoke.check("Unrelated member cannot upload", lambda:
                post_file(outsider, {"WorkspaceId": workspace_id,
                                    "__RequestVerificationToken": outsider_token["fields"]["__RequestVerificationToken"]},
                          name_a, file_a, 403, "Outsider upload"))

    fields = new_file["fields"]
    smoke.check("Reject forbidden file extension", lambda:
                post_file(client, fields, "payload.exe", file_a, 400, "Bad extension"))
    smoke.check("Reject empty file", lambda:
                post_file(client, fields, "empty.txt", b"", 400, "Empty file"))
    smoke.check("Reject file exceeding 8 MiB", lambda:
                post_file(client, fields, "large.txt", b"x" * (8 * 1024 * 1024 + 1),
                          400, "Oversized file"))
    assert sql_scalar(f"SELECT COUNT(*) FROM dbo.WorkspaceFiles WHERE WorkspaceId={workspace_id}") == 0

    smoke.check("Client uploads first immutable version", lambda:
                post_file(client, fields, name_a, file_a, 302, "Initial upload"))
    updated = smoke.get(client, workspace).text
    link = re.search(r"/WorkspaceFiles/Download/(\d+)", updated)
    assert link, "Uploaded version is missing from workspace"
    version_a = int(link.group(1))
    revision = exactly_one_form(updated, UPLOAD, revision=True)
    file_id = int(revision["fields"]["FileId"])
    sql_version(file_id, version_a, 1, file_a, emails["client"])
    smoke.check("Client downloads first version unchanged", lambda:
                verify_download(client, version_a, file_a, name_a))
    smoke.check("Assigned freelancer can download version", lambda:
                verify_download(worker, version_a, file_a, name_a))
    smoke.check("Outsider cannot download", lambda:
                status(outsider.get(BASE + f"/WorkspaceFiles/Download/{version_a}",
                                    timeout=35, allow_redirects=False), 403, "Outsider download"))
    smoke.check("Guest cannot download", lambda:
                status(guest.get(BASE + f"/WorkspaceFiles/Download/{version_a}",
                                  timeout=35, allow_redirects=False), 302, "Guest download"))

    file_b = ("NexusFreelance version two " + suffix + "\n").encode("utf-8")
    fields_revision = revision["fields"]
    smoke.check("Reject revision with unknown file id", lambda:
                post_file(client, {**fields_revision, "FileId": 2147483647},
                          "revision.txt", file_b, 403, "Unknown revision id"))
    worker_revision = token_form(smoke, worker, workspace, UPLOAD, revision=True)
    smoke.check("Assigned freelancer uploads version two", lambda:
                post_file(worker, worker_revision["fields"], "revision.txt", file_b,
                          302, "Revision upload"))
    latest = smoke.get(client, workspace).text
    links = [int(value) for value in re.findall(r"/WorkspaceFiles/Download/(\d+)", latest)]
    assert len(links) >= 2 and version_a in links, "Revision history lost version one"
    version_b = next(value for value in links if value != version_a)
    assert version_b > version_a
    sql_version(file_id, version_b, 2, file_b, emails["worker"])
    smoke.check("Both versions retain exact original bytes", lambda:
                (verify_download(client, version_a, file_a, name_a),
                 verify_download(worker, version_b, file_b, "revision.txt")))
    assert sql_scalar(f"SELECT COUNT(*) FROM dbo.WorkspaceFileVersions WHERE WorkspaceFileId={file_id}") == 2
    if args.exercise_pagination:
        for index in range(30):
            post_file(client, fields, f"filler-{index:02d}.txt",
                      f"Pagination filler {index} {suffix}".encode("utf-8"),
                      302, f"Pagination filler {index}")
        first_page = smoke.get(client, workspace).text
        assert "beforeFileVersionId=" in first_page, "Missing workspace file history pagination"
        oldest_filler_id = sql_scalar(
            "SELECT MIN(v.Id) FROM dbo.WorkspaceFileVersions v "
            "JOIN dbo.WorkspaceFiles f ON f.Id=v.WorkspaceFileId "
            f"WHERE f.WorkspaceId={workspace_id} AND v.Id>{version_b}")
        assert oldest_filler_id is not None and oldest_filler_id > version_b
        older_page = f"{workspace}?beforeFileVersionId={oldest_filler_id}"
        older_markup = smoke.get(client, older_page).text
        older_revision_forms = [f for f in forms(older_markup)
                                if UPLOAD in f["action"] and
                                f["fields"].get("FileId") == str(file_id)]
        assert len(older_revision_forms) == 1, (
            "Latest file version on older page must still offer revision")
        file_c = ("NexusFreelance version three " + suffix + "\n").encode("utf-8")
        smoke.check("Latest revision on older history page can be uploaded", lambda:
                    post_file(client, older_revision_forms[0]["fields"],
                              "third.txt", file_c, 302, "Version three"))
        version_c = sql_scalar(
            f"SELECT MAX(Id) FROM dbo.WorkspaceFileVersions WHERE WorkspaceFileId={file_id}")
        sql_version(file_id, version_c, 3, file_c, emails["client"])
        smoke.check("Version three retains exact bytes", lambda:
                    verify_download(worker, version_c, file_c, "third.txt"))
        superseded_markup = smoke.get(client, older_page).text
        assert not any(UPLOAD in f["action"] and f["fields"].get("FileId") == str(file_id)
                       for f in forms(superseded_markup)), (
            "Superseded version must not offer a revision form")
    print(f"WORKSPACE_FILES_E2E_PASSED={len(smoke.rows)} PROJECT={project_id} WORKSPACE={workspace_id} FILE={file_id}")


if __name__ == "__main__":
    main()
