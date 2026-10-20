"""Timed skill-exam HTTP E2E. Writes isolated NexusFreelanceMvp test records.

Run after IIS Express starts: python tools/SkillExamTimerE2E.py --run-writes
"""
import argparse
import html
import re
import subprocess
import time
import uuid
from pathlib import Path

from AdminE2E import form_for, post_form, request
from AdminSkillsE2E import ANSWER_KEY, exam_answers, forms, power_shell, register


def exam_form(session, track_id):
    page = request(session, "GET", f"/Skills/Exam/{track_id}").text
    assert "CorrectChoice" not in page
    form = next(f for f in forms(page) if "/Skills/Submit/" in f["action"])
    assert form["fields"].get("ExamTicket")
    assert form["fields"].get("BrowserFocusLossCount") == "0"
    assert 'id="exam-clock"' in page
    match = re.search(r"Date\.now\(\) \+ (\d+) \* 1000", page)
    assert match, "Server-issued remaining time is missing"
    return form, int(match.group(1))


def rejected(session, form, **overrides):
    response = request(session, "POST", form["action"], 302,
                       data={**form["fields"], **exam_answers(form), **overrides},
                       allow_redirects=False)
    assert "/Skills/Exam/" in response.headers.get("Location", "")


def make_track(admin, code, title):
    catalog = request(admin, "GET", "/Admin/Skills").text
    created = post_form(admin, form_for(catalog, "/Admin/CreateSkill"),
                        Code=code, Title=title, Description="Timed E2E sample",
                        MatchKeywords="timer e2e")
    assert title in created.text
    catalog = request(admin, "GET", "/Admin/Skills").text
    track_id = int(next(f["fields"]["Id"] for f in forms(catalog)
                        if "/Admin/UpdateSkill" in f["action"]
                        and f["fields"].get("Title") == title))
    for ordinal, correct in enumerate(ANSWER_KEY, start=1):
        page = request(admin, "GET", f"/Admin/Questions/{track_id}").text
        new = next(f for f in forms(page) if "/Admin/SaveQuestion" in f["action"]
                   and f["fields"].get("Id") == "0")
        post_form(admin, new, Ordinal=str(ordinal),
                  Prompt=f"Timed test question {ordinal} for {code}",
                  ChoiceA="A", ChoiceB="B", ChoiceC="C", ChoiceD="D",
                  CorrectChoice=correct)
    return track_id


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--run-writes", action="store_true")
    args = parser.parse_args()
    if not args.run_writes:
        parser.error("Writes to NexusFreelanceMvp; supply --run-writes")

    suffix = uuid.uuid4().hex[:10]
    freelancer, _ = register("Freelancer", "timer-" + suffix)
    other, _ = register("Freelancer", "timer-other-" + suffix)
    client, _ = register("Client", "timer-client-" + suffix)
    admin, admin_email = register("Client", "admin-timer-" + suffix, name="Admin E2E")
    elevated = False
    try:
        power_shell("BootstrapAdmin.ps1", admin_email)
        elevated = True
        track_id = make_track(admin, "TIMER_" + suffix.upper(), "Timer test " + suffix)

        request(client, "GET", f"/Skills/Exam/{track_id}", 403)
        form, remaining = exam_form(freelancer, track_id)
        assert 1190 <= remaining <= 1200, remaining
        time.sleep(2)
        refreshed, after_refresh = exam_form(freelancer, track_id)
        assert 0 < after_refresh < remaining, (remaining, after_refresh)
        print("PASS timed exam, protected form, 20-minute persistent deadline, role checks")

        rejected(freelancer, refreshed, ExamTicket="tampered-ticket")
        rejected(freelancer, refreshed, BrowserFocusLossCount="-1")
        rejected(freelancer, refreshed, BrowserFocusLossCount="1000")
        other_form, _ = exam_form(other, track_id)
        rejected(other, other_form, ExamTicket=form["fields"]["ExamTicket"])
        request(freelancer, "POST", refreshed["action"], 400,
                data={**refreshed["fields"], **exam_answers(refreshed),
                      "SkillTrackId": str(track_id + 1000000)},
                allow_redirects=False)
        print("PASS bad ticket, invalid focus count, foreign user and track binding rejected")

        successful = request(freelancer, "POST", refreshed["action"], 302,
                             data={**refreshed["fields"], **exam_answers(refreshed),
                                   "BrowserFocusLossCount": "2"}, allow_redirects=False)
        location = successful.headers.get("Location", "")
        assert "/Skills/Result/" in location, location
        result = html.unescape(request(freelancer, "GET", location).text)
        assert "Thời gian làm bài:" in result and "2 (chỉ mang tính tham khảo)" in result
        assert "100%" in result
        rejected(freelancer, refreshed)
        center = request(freelancer, "GET", "/Skills/Index").text
        assert center.count("/Skills/Result/") >= 1
        print("PASS graded score, persisted elapsed/focus data and replay prevention")

        # Expire the other user's sitting in the isolated E2E database.
        time.sleep(1)
        subprocess.run(["powershell.exe", "-NoProfile", "-ExecutionPolicy", "Bypass",
                        "-File", str(Path(__file__).with_name("ExpireTestExamSession.ps1")),
                        "-Email", "skills-freelancer-timer-other-" + suffix + "@example.invalid",
                        "-TrackId", str(track_id)], check=True, timeout=45)
        rejected(other, other_form)
        new_form, renewed = exam_form(other, track_id)
        assert renewed >= 1190
        rejected(other, other_form)
        assert new_form["fields"]["ExamTicket"] != other_form["fields"]["ExamTicket"]
        print("PASS expired sitting rejected and stale ticket invalid after restart")
        print("SKILL_EXAM_TIMER_E2E_PASSED=4")
    finally:
        if elevated:
            power_shell("ResetTestAdmin.ps1", admin_email)


if __name__ == "__main__":
    main()
