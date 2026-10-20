"""HTTP E2E for Admin skill catalog/question bank on the development DB only.

Run after IIS Express starts at https://localhost:44391.
Creates a uniquely named sample skill and five questions; these test DB rows persist.
Temporary Admin privilege is reset in finally.
"""
import argparse
import html
import uuid
from urllib.parse import quote

import requests

from AdminE2E import PASSWORD, Forms, form_for, post_form, power_shell, request


# Answers belong to the newly created E2E question bank; no answer key is
# read from the public exam page (which intentionally never exposes it).
ANSWER_KEY = ("A", "B", "C", "D", "A")


def forms(page):
    parser = Forms()
    parser.feed(page)
    return parser.forms


def exam_answers(form):
    fields = form["fields"]
    assert len([key for key in fields if key.endswith(".QuestionId")]) == len(ANSWER_KEY)
    answers = {}
    for index, correct_choice in enumerate(ANSWER_KEY):
        assert f"Answers[{index}].QuestionId" in fields
        answers[f"Answers[{index}].Choice"] = correct_choice
    return answers


def own_badge_form(session, email, status):
    page = request(session, "GET", f"/Admin/Badges?status={status}&q={quote(email)}").text
    revoke_forms = [f for f in forms(page) if "/Admin/RevokeBadge" in f["action"]]
    if status == "Active":
        assert len(revoke_forms) == 1, (status, email, len(revoke_forms))
    else:
        assert not revoke_forms, (status, email, len(revoke_forms))
    return page, revoke_forms[0] if revoke_forms else None


def register(role, suffix, name=None):
    session = requests.Session()
    register_form = form_for(request(session, "GET", "/Account/Register").text,
                             "/Account/Register")
    response = post_form(session, register_form, Name=name or "Skill E2E " + role,
                         Email=f"skills-{role.lower()}-{suffix}@example.invalid",
                         Password=PASSWORD, Role=role)
    assert "Đăng xuất" in html.unescape(response.text)
    return session, f"skills-{role.lower()}-{suffix}@example.invalid"


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--run-writes", action="store_true")
    args = parser.parse_args()
    if not args.run_writes:
        parser.error("This suite writes test database rows; supply --run-writes")

    suffix = uuid.uuid4().hex[:10]
    code = "E2E_" + suffix.upper()
    title = "Admin Skills E2E " + suffix
    guest = requests.Session()
    request(guest, "GET", "/Admin/Skills", 302, allow_redirects=False)
    regular, _ = register("Client", suffix)
    request(regular, "GET", "/Admin/Skills", 403)
    freelancer, freelancer_email = register("Freelancer", suffix)
    request(freelancer, "GET", "/Admin/Skills", 403)
    request(freelancer, "GET", "/Admin/Questions/1", 403)
    print("PASS catalog and questions deny guest/non-admin")

    admin, admin_email = register("Client", "admin-" + suffix, name="Admin E2E")
    elevated = False
    try:
        power_shell("BootstrapAdmin.ps1", admin_email)
        elevated = True
        catalog = request(admin, "GET", "/Admin/Skills").text
        create = form_for(catalog, "/Admin/CreateSkill")
        assert "__RequestVerificationToken" in create["fields"]
        request(admin, "POST", "/Admin/CreateSkill", 400,
                data={"Code": code, "Title": title, "Description": "No CSRF"})
        user_page = request(regular, "GET", "/Projects/Mine").text
        client_csrf = form_for(user_page, "/Account/Logout")["fields"]["__RequestVerificationToken"]
        request(regular, "POST", "/Admin/CreateSkill", 403,
                allow_redirects=False,
                data={"__RequestVerificationToken": client_csrf, "Code": code,
                      "Title": title, "Description": "Invalid user", "MatchKeywords": "e2e"})
        invalid = post_form(admin, create, 400, Code="!!", Title=title,
                            Description="Invalid code", MatchKeywords="e2e")
        assert invalid.status_code == 400
        print("PASS Admin authorization, CSRF and form validation")

        created = post_form(admin, create, Code=code, Title=title,
                            Description="Skill catalogue test", MatchKeywords="admin e2e test")
        assert title in created.text
        catalog = request(admin, "GET", "/Admin/Skills").text
        update = next(f for f in forms(catalog) if "/Admin/UpdateSkill" in f["action"]
                      and f["fields"].get("Title") == title)
        track_id = int(update["fields"]["Id"])
        dupe = post_form(admin, create, 409, Code=code, Title=title,
                         Description="Duplicate code", MatchKeywords="e2e")
        assert dupe.status_code == 409
        edited_title = title + " Edited"
        changed = post_form(admin, update, Title=edited_title,
                            Description="Updated metadata", MatchKeywords="updated e2e keywords")
        assert edited_title in changed.text
        print("PASS create track, unique code, edit metadata")

        question_page = request(admin, "GET", f"/Admin/Questions/{track_id}").text
        assert "ngân hàng" in question_page.lower()
        first_form = next(f for f in forms(question_page) if "/Admin/SaveQuestion" in f["action"]
                          and f["fields"].get("Id") == "0")
        for ordinal in range(1, 6):
            question_page = request(admin, "GET", f"/Admin/Questions/{track_id}").text
            new = next(f for f in forms(question_page) if "/Admin/SaveQuestion" in f["action"]
                       and f["fields"].get("Id") == "0")
            result = post_form(admin, new, Ordinal=str(ordinal),
                               Prompt=f"E2E unique question {ordinal} of {suffix}",
                               ChoiceA="No", ChoiceB="Yes", ChoiceC="Maybe",
                               ChoiceD="Never", CorrectChoice=ANSWER_KEY[ordinal - 1])
            assert "E2E unique question" in result.text
        question_page = request(admin, "GET", f"/Admin/Questions/{track_id}").text
        parsed = forms(question_page)
        original = next(f for f in parsed if "/Admin/SaveQuestion" in f["action"]
                        and f["fields"].get("Id") not in (None, "0"))
        conflicting = post_form(admin, first_form, 409, Ordinal="1",
                                Prompt="Duplicate ordinal", ChoiceA="No", ChoiceB="Yes",
                                ChoiceC="Maybe", ChoiceD="Never", CorrectChoice="B")
        assert conflicting.status_code == 409
        saved = post_form(admin, original, Prompt="Updated question before examination",
                          ChoiceA="No", ChoiceB="Yes", ChoiceC="Maybe",
                          ChoiceD="Never", CorrectChoice=ANSWER_KEY[0])
        assert "Updated question before examination" in saved.text
        print("PASS 5 questions, unique ordinal and editable unused question")

        exam = request(freelancer, "GET", f"/Skills/Exam/{track_id}").text
        assert "CorrectChoice" not in exam
        exam_form = next(f for f in forms(exam) if "/Skills/Submit/" in f["action"])
        answers = exam_answers(exam_form)
        submitted = request(freelancer, "POST", exam_form["action"], 302,
                            allow_redirects=False, data={**exam_form["fields"], **answers})
        assert "/Skills/Result/" in submitted.headers.get("Location", "")
        badge_page = request(freelancer, "GET", "/Skills/Index").text
        assert edited_title in badge_page
        assert "/Skills/Result/" in badge_page
        locked = request(admin, "GET", f"/Admin/Questions/{track_id}").text
        assert "khóa chỉnh sửa" in locked
        blocked = post_form(admin, original, 409, Prompt="Tampered after exam",
                            ChoiceA="No", ChoiceB="Yes", ChoiceC="Maybe",
                            ChoiceD="Never", CorrectChoice="A")
        assert blocked.status_code == 409
        assert "Updated question before examination" in request(admin, "GET", f"/Admin/Questions/{track_id}").text
        print("PASS server-side grading, earned badge and immutable used question")

        badge_admin_page, revoke = own_badge_form(admin, freelancer_email, "Active")
        assert edited_title in badge_admin_page
        assert freelancer_email in badge_admin_page
        badge_id = revoke["fields"]["BadgeId"]
        request(admin, "POST", "/Admin/RevokeBadge", 400,
                data={"BadgeId": badge_id, "Reason": "Missing CSRF"}, allow_redirects=False)
        freelancer_token = form_for(request(freelancer, "GET", "/Skills/Index").text,
                                    "/Account/Logout")["fields"]["__RequestVerificationToken"]
        request(freelancer, "POST", "/Admin/RevokeBadge", 403,
                data={**revoke["fields"],
                      "__RequestVerificationToken": freelancer_token,
                      "Reason": "Forbidden user"}, allow_redirects=False)
        request(admin, "POST", "/Admin/RevokeBadge", 400,
                data={**revoke["fields"], "Reason": "bad"}, allow_redirects=False)
        print("PASS revoke requires Admin, CSRF and a reason")

        revoked = request(admin, "POST", "/Admin/RevokeBadge", 302,
                          data={**revoke["fields"], "Reason": "Test assessment invalidated"},
                          allow_redirects=False)
        assert "/Admin/Badges" in revoked.headers["Location"]
        request(admin, "POST", "/Admin/RevokeBadge", 409,
                data={**revoke["fields"], "Reason": "Duplicate revocation"},
                allow_redirects=False)
        revoked_admin, _ = own_badge_form(admin, freelancer_email, "Revoked")
        assert edited_title in revoked_admin and "Test assessment invalidated" in revoked_admin
        revoked_center = request(freelancer, "GET", "/Skills/Index").text
        assert "Test assessment invalidated" in revoked_center
        assert "Badge đã thu hồi" in revoked_center
        print("PASS revoke persisted, replay rejected and Freelancer informed")

        repeat_exam = request(freelancer, "GET", f"/Skills/Exam/{track_id}").text
        repeat_form = next(f for f in forms(repeat_exam) if "/Skills/Submit/" in f["action"])
        repeat_answers = exam_answers(repeat_form)
        request(freelancer, "POST", repeat_form["action"], 302,
                data={**repeat_form["fields"], **repeat_answers}, allow_redirects=False)
        reissued_center = request(freelancer, "GET", "/Skills/Index").text
        assert edited_title in reissued_center and "Badge đã thu hồi" not in reissued_center
        reissued_admin, reissued = own_badge_form(admin, freelancer_email, "Active")
        assert edited_title in reissued_admin
        assert reissued["fields"]["BadgeId"] == badge_id
        print("PASS passing a new exam reissues same badge without duplicating it")

        audit = request(admin, "GET", "/Admin/Audit").text
        assert "CreateExamQuestion" in audit and "UpdateExamQuestion" in audit
        assert "CreateSkillTrack" in audit and "UpdateSkillTrack" in audit
        assert "RevokeBadge" in audit
        print("PASS catalog edits recorded in SQL-backed audit")
        print("ADMIN_SKILLS_E2E_PASSED=9")
    finally:
        if elevated:
            power_shell("ResetTestAdmin.ps1", admin_email)


if __name__ == "__main__":
    main()
