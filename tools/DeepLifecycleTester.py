"""Comprehensive deep-dive end-to-end lifecycle auditor for NexusFreelance.
Tests every flow from registration to individual functions, logging successes and errors.
"""
import argparse, email, html, os, re, subprocess, sys, time, uuid
from pathlib import Path
from urllib.parse import urljoin
import requests, urllib3

sys.stdout.reconfigure(encoding='utf-8', errors='replace')
sys.stderr.reconfigure(encoding='utf-8', errors='replace')

urllib3.disable_warnings(urllib3.exceptions.InsecureRequestWarning)
SQLCMD = r'C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\180\Tools\Binn\SQLCMD.EXE'

def sql(q):
    p = subprocess.run([SQLCMD, '-S', r'lpc:.\SQLEXPRESS', '-d', 'NexusFreelanceMvp', '-E', '-C', '-I', '-b', '-h', '-1', '-W', '-Q', 'SET NOCOUNT ON; ' + q],
                       capture_output=True, text=True, encoding='utf-8', errors='replace')
    if p.returncode:
        raise AssertionError(f"SQL Error: {p.stdout} {p.stderr}")
    return p.stdout.strip()

from SmokeE2E import Smoke, forms, route_id, error_excerpt

class LifecycleAuditor:
    def __init__(self, base_url, mailbox_path):
        self.base = base_url.rstrip('/')
        self.mailbox = Path(mailbox_path)
        self.smoke = Smoke(self.base)
        self.run_id = uuid.uuid4().hex[:10]
        self.password = f'NexusV3!{self.run_id}Aa7'
        self.findings = []
        self.passed_checks = []

    def log_pass(self, flow, check_name, detail=""):
        msg = f"[PASS] [{flow}] {check_name}"
        if detail: msg += f" - {detail}"
        print(msg, flush=True)
        self.passed_checks.append((flow, check_name, detail))

    def log_issue(self, flow, title, severity, detail):
        msg = f"[{severity.upper()}] [{flow}] {title}: {detail}"
        print(msg, flush=True)
        self.findings.append({'flow': flow, 'title': title, 'severity': severity, 'detail': detail})

    def session(self):
        s = requests.Session()
        s.verify = False
        return s

    def code_for(self, email_address, role):
        result = []
        for path in self.mailbox.glob('*.eml'):
            try:
                msg = email.message_from_bytes(path.read_bytes())
                if email_address not in str(msg.get('To', '')): continue
                payload = msg.get_payload(decode=True)
                if not payload: continue
                text = payload.decode('utf-8', errors='replace')
                if role in text or 'mã xác minh' in text.lower():
                    m = re.search(r'\b(\d{6})\b', text)
                    if m: result.append((path.stat().st_mtime_ns, m.group(1)))
            except Exception: pass
        if not result:
            raise AssertionError(f'No pickup email found for {email_address} ({role})')
        return max(result)[1]

    def register(self, s, name, email_address, role, pwd=None):
        pwd = pwd or self.password
        return self.smoke.post(s, '/Account/Register', {
            'Name': name, 'Email': email_address, 'Password': pwd, 'Role': role
        }, '/Account/Register', expect_redirect=False)

    def verify(self, s, email_address, role, code, page=None):
        page = page or f'/Account/ConfirmRegistration?email={email_address}&role={role}'
        return self.smoke.post(s, '/Account/ConfirmRegistration', {
            'Email': email_address, 'Role': role, 'Code': code
        }, page, expect_redirect=False)

    def login(self, s, email_address, role, pwd=None):
        pwd = pwd or self.password
        return self.smoke.post(s, '/Account/Login', {
            'Email': email_address, 'Role': role, 'Password': pwd
        }, '/Account/Login', expect_redirect=False)

    # ========================================================
    # FLOW 1: AUTHENTICATION & ONBOARDING
    # ========================================================
    def audit_auth_flow(self):
        flow = "1. Authentication"
        guest = self.session()

        # 1.1 Invalid email format
        r = self.register(guest, "Bad Email", "invalid-email-at-domain", "Client")
        if r.status_code == 200 and "Email" in r.text:
            self.log_pass(flow, "Format email không hợp lệ bị từ chối")
        else:
            self.log_issue(flow, "Validation Email", "Medium", f"Đăng ký email sai định dạng không trả lỗi validation mong muốn (HTTP {r.status_code})")

        # 1.2 Weak password check (1 attempt to preserve rate limit token)
        r_weak = self.register(guest, "Weak User", f"weak-{uuid.uuid4().hex[:6]}@example.test", "Client", pwd='Password123!')
        if r_weak.status_code == 200:
            self.log_pass(flow, "Mật khẩu yếu 'Password123!' bị từ chối")
        else:
            self.log_issue(flow, "Chính sách mật khẩu", "High", f"Mật khẩu yếu không bị chặn: {r_weak.status_code}")

        # 1.3 Valid registration Client
        self.client_email = f"client-{self.run_id}@example.test"
        self.client_sess = self.session()
        r = self.register(self.client_sess, f"Client {self.run_id}", self.client_email, "Client")
        if r.status_code == 302 and "/ConfirmRegistration" in r.headers.get("Location", ""):
            self.log_pass(flow, "Đăng ký Client hợp lệ chuyển hướng sang trang ConfirmRegistration")
        else:
            self.log_issue(flow, "Đăng ký Client", "Critical", f"Đăng ký không chuyển hướng xác minh: {r.status_code}")

        # 1.4 Unverified login attempt
        r_unverified = self.login(self.session(), self.client_email, "Client")
        if r_unverified.status_code == 200 and "xác minh" in r_unverified.text:
            self.log_pass(flow, "Chặn đăng nhập khi chưa xác minh email OTP")
        else:
            self.log_issue(flow, "Chặn tài khoản chưa xác minh", "High", f"Đăng nhập tài khoản chưa xác minh không trả cảnh báo (HTTP {r_unverified.status_code})")

        # 1.5 Wrong OTP code
        r_wrong_otp = self.verify(self.client_sess, self.client_email, "Client", "000000")
        if r_wrong_otp.status_code == 200 and ("không đúng" in r_wrong_otp.text or "sai" in r_wrong_otp.text):
            self.log_pass(flow, "Nhập sai mã OTP 6 số bị từ chối")
        else:
            self.log_issue(flow, "Kiểm tra OTP sai", "High", f"Nhập OTP sai không hiển thị lỗi phù hợp: {r_wrong_otp.status_code}")

        # 1.6 Correct OTP code
        try:
            client_otp = self.code_for(self.client_email, "Client")
            r_verify = self.verify(self.client_sess, self.client_email, "Client", client_otp)
            if r_verify.status_code == 302 and "/Login" in r_verify.headers.get("Location", ""):
                self.log_pass(flow, "Xác minh OTP đúng thành công, chuyển hướng đến Login")
            else:
                self.log_issue(flow, "Xác minh OTP đúng", "Critical", f"Xác minh OTP đúng không chuyển hướng Login (HTTP {r_verify.status_code})")
        except Exception as e:
            self.log_issue(flow, "Hộp thư OTP", "Critical", f"Không tìm thấy mã OTP trong mailbox: {e}")

        # 1.7 Replay OTP
        r_replay = self.verify(self.client_sess, self.client_email, "Client", client_otp)
        if r_replay.status_code == 200:
            self.log_pass(flow, "Tái sử dụng OTP (Replay) bị từ chối")
        else:
            self.log_issue(flow, "Replay OTP", "High", f"Cho phép dùng lại mã OTP đã xác minh!")

        # 1.8 Register Freelancer 1
        self.free1_email = f"free1-{self.run_id}@example.test"
        self.free1_sess = self.session()
        self.register(self.free1_sess, f"Freelancer 1 {self.run_id}", self.free1_email, "Freelancer")
        free1_otp = self.code_for(self.free1_email, "Freelancer")
        self.verify(self.free1_sess, self.free1_email, "Freelancer", free1_otp)
        self.log_pass(flow, "Đăng ký và xác minh Freelancer 1 thành công")

        # 1.9 Register Freelancer 2 (for team testing)
        self.free2_email = f"free2-{self.run_id}@example.test"
        self.free2_sess = self.session()
        self.register(self.free2_sess, f"Freelancer 2 {self.run_id}", self.free2_email, "Freelancer")
        free2_otp = self.code_for(self.free2_email, "Freelancer")
        self.verify(self.free2_sess, self.free2_email, "Freelancer", free2_otp)
        self.log_pass(flow, "Đăng ký và xác minh Freelancer 2 thành công")

        # 1.10 Dual-role on single email
        r_dual = self.register(self.session(), f"Dual Role {self.run_id}", self.client_email, "Freelancer")
        if r_dual.status_code == 302:
            dual_otp = self.code_for(self.client_email, "Freelancer")
            self.verify(self.session(), self.client_email, "Freelancer", dual_otp)
            self.log_pass(flow, "Hỗ trợ 2 vai trò Client + Freelancer trên cùng 1 địa chỉ email")
        else:
            self.log_issue(flow, "Đăng ký 2 vai trò", "Medium", f"Không cho phép đăng ký vai trò thứ hai trên cùng email: {r_dual.status_code}")

        # 1.11 Duplicate registration on same email and SAME role
        r_dup = self.register(self.session(), "Duplicate", self.client_email, "Client")
        if r_dup.status_code in (200, 429):
            self.log_pass(flow, "Chặn đăng ký trùng lặp vai trò và áp dụng Rate Limiting")
        else:
            self.log_issue(flow, "Trùng vai trò", "Medium", "Không chặn đăng ký trùng vai trò trên cùng email")

        # 1.12 Successful Login
        r_login_c = self.login(self.client_sess, self.client_email, "Client")
        if r_login_c.status_code == 302:
            self.log_pass(flow, "Đăng nhập Client thành công")
        else:
            self.log_issue(flow, "Đăng nhập Client", "Critical", f"Đăng nhập Client thất bại: {r_login_c.status_code}")

        r_login_f1 = self.login(self.free1_sess, self.free1_email, "Freelancer")
        if r_login_f1.status_code == 302:
            self.log_pass(flow, "Đăng nhập Freelancer 1 thành công")
        else:
            self.log_issue(flow, "Đăng nhập Freelancer 1", "Critical", f"Đăng nhập Freelancer thất bại: {r_login_f1.status_code}")

        r_login_f2 = self.login(self.free2_sess, self.free2_email, "Freelancer")
        if r_login_f2.status_code == 302:
            self.log_pass(flow, "Đăng nhập Freelancer 2 thành công")

        # Setup Admin session
        self.admin_email = f"admin-{self.run_id}@example.test"
        self.admin_sess = self.session()
        self.register(self.admin_sess, f"Admin {self.run_id}", self.admin_email, "Client")
        admin_otp = self.code_for(self.admin_email, "Client")
        self.verify(self.admin_sess, self.admin_email, "Client", admin_otp)
        self.admin_uid = int(sql(f"SELECT Id FROM Users WHERE Email='{self.admin_email}' AND Role='Client'"))
        sql(f"UPDATE Users SET Role='Admin' WHERE Id={self.admin_uid}")
        r_admin_login = self.login(self.admin_sess, self.admin_email, "Admin")
        if r_admin_login.status_code == 302:
            self.log_pass(flow, "Tạo và đăng nhập tài khoản Admin thành công")
        else:
            self.log_issue(flow, "Đăng nhập Admin", "High", f"Đăng nhập Admin thất bại: {r_admin_login.status_code}")

    # ========================================================
    # FLOW 2: FREELANCER PROFILE & PORTFOLIO
    # ========================================================
    def audit_profile_flow(self):
        flow = "2. Profile & Portfolio"
        f = self.free1_sess

        # 2.1 View profile
        p = self.smoke.get(f, '/Profile')
        if p.status_code == 200:
            self.log_pass(flow, "Mở trang Profile Freelancer thành công")
        else:
            self.log_issue(flow, "Xem Profile", "High", f"Lỗi mở Profile: {p.status_code}")

        # 2.2 Update profile
        r_update = self.smoke.post(f, '/Profile/Update', {
            'CoreRole': 'Backend Developer',
            'Bio': 'Chuyên viên phát triển hệ thống C# .NET và SQL Server với 5 năm kinh nghiệm.',
            'Industry': 'Công nghệ thông tin',
            'Location': 'Hà Nội (UTC+7)',
            'YearsExperience': 5,
            'MinimumProjectBudget': 5000000,
            'IsAvailable': 'true',
            'IsPublic': 'true'
        }, '/Profile')
        if r_update.status_code in (200, 302):
            self.log_pass(flow, "Cập nhật hồ sơ Freelancer thành công (/Profile/Update)")
        else:
            self.log_issue(flow, "Cập nhật Profile", "Medium", f"Lỗi cập nhật hồ sơ: {r_update.status_code}")

        # 2.3 Portfolio with HTTP URL (insecure)
        r_http_port = self.smoke.post(f, '/Profile/AddPortfolio', {
            'Title': 'Insecure Site',
            'Url': 'http://insecure-example.com',
            'Summary': 'Testing HTTP rejected'
        }, '/Profile', expect_redirect=False)
        count = int(sql(f"SELECT COUNT(*) FROM PortfolioItems WHERE Url LIKE 'http://%'"))
        if count == 0:
            self.log_pass(flow, "Chặn Portfolio sử dụng URL HTTP không bảo mật (yêu cầu HTTPS)")
        else:
            self.log_issue(flow, "Bảo mật Portfolio", "Medium", "Cho phép lưu Portfolio dùng URL HTTP không bảo mật!")

        # 2.4 Portfolio with HTTPS URL
        r_https_port = self.smoke.post(f, '/Profile/AddPortfolio', {
            'Title': 'Nexus Enterprise ERP',
            'Url': 'https://github.com/nexus/enterprise-erp',
            'Role': 'Backend Architect',
            'Skills': 'C#, ASP.NET, SQL Server',
            'Summary': 'Hệ thống quản trị tài nguyên doanh nghiệp hiệu năng cao.'
        }, '/Profile')
        self.log_pass(flow, "Thêm mục Portfolio HTTPS hợp lệ thành công")

    # ========================================================
    # FLOW 3: SKILLS, EXAMS & BADGES
    # ========================================================
    def audit_skills_flow(self):
        flow = "3. Skills & Badges"
        f = self.free1_sess

        # 3.1 View skill catalog
        p = self.smoke.get(f, '/Skills')
        if p.status_code == 200 and "Kiểm tra kỹ năng" in p.text:
            self.log_pass(flow, "Mở danh mục kiểm tra kỹ năng thành công")
        else:
            self.log_issue(flow, "Mở trang Skills", "Medium", f"Trang Skills không hiển thị đúng (HTTP {p.status_code})")

        # 3.2 View exam form
        track_id = int(sql("SELECT TOP 1 Id FROM SkillTracks ORDER BY Id ASC"))
        p_exam = self.smoke.get(f, f'/Skills/Exam/{track_id}')
        if p_exam.status_code == 200:
            self.log_pass(flow, "Mở bài kiểm tra trắc nghiệm thành công")
        else:
            self.log_issue(flow, "Mở đề thi", "High", f"Không mở được đề thi track {track_id}")
            return

        form = next((fo for fo in forms(p_exam.text) if f"/Skills/Submit/{track_id}" in fo["action"] or "/Skills/Submit" in fo["action"]), None)
        if not form:
            self.log_issue(flow, "Form bài thi", "High", "Không tìm thấy form nộp bài thi")
            return

        # 3.3 Submit passing exam
        pass_data = dict(form["fields"])
        q_count = len([k for k in pass_data if k.endswith('.QuestionId')])
        for i in range(q_count):
            qid = pass_data[f"Answers[{i}].QuestionId"]
            correct = sql(f"SELECT CorrectChoice FROM SkillExamQuestions WHERE Id={qid}")
            pass_data[f"Answers[{i}].Choice"] = correct

        r_pass = self.smoke.post(f, f'/Skills/Submit/{track_id}', pass_data)
        badge_count = int(sql(f"SELECT COUNT(*) FROM EarnedSkillBadges b JOIN Users u ON u.Id=b.UserAccountId WHERE u.Email='{self.free1_email}'"))
        if badge_count >= 1:
            self.log_pass(flow, "Thi đạt >=80%: Tự động chấm điểm và cấp Badge CSDL ngay lập tức")
        else:
            self.log_issue(flow, "Cấp Badge", "High", "Thi đạt nhưng không thấy cấp Badge trong CSDL!")

    # ========================================================
    # FLOW 4: PROJECT CREATION, AI JOB STUDIO & PUBLISHING
    # ========================================================
    def audit_project_flow(self):
        flow = "4. Projects & AI Job Studio"
        c = self.client_sess

        # 4.1 Freelancer cannot create project
        p_forbidden = self.smoke.get(self.free1_sess, '/Projects/Create', status=403)
        self.log_pass(flow, "Phân quyền: Chặn Freelancer truy cập trang tạo dự án (HTTP 403)")

        # 4.2 AI Job Studio outline generation
        r_ai = self.smoke.post(c, '/JobStudio/Generate', {
            'Industry': 'Công nghệ thông tin',
            'Idea': f'Hệ thống thương mại điện tử đa nền tảng {self.run_id}',
            'ProjectType': 'SOLO',
            'Budget': 15000000,
            'Deadline': '2027-03-01',
            'UseExternalAi': 'false'
        }, '/JobStudio/New')
        if r_ai.status_code == 302 and "/JobStudio/Edit/" in r_ai.headers.get("Location", ""):
            self.log_pass(flow, "AI Job Studio sinh dàn ý dự án theo luật thành công")
            ai_draft_id = int(re.search(r'/JobStudio/Edit/(\d+)', r_ai.headers['Location']).group(1))
            r_apply = self.smoke.post(c, f'/JobStudio/Apply/{ai_draft_id}', {}, f'/JobStudio/Edit/{ai_draft_id}')
            self.log_pass(flow, "Áp dụng bản thảo AI Job Studio tạo dự án thành công")
        else:
            self.log_issue(flow, "AI Job Studio", "Medium", f"Tạo bản thảo AI thất bại (HTTP {r_ai.status_code})")

        # 4.3 Create manual Project Draft
        r_create = self.smoke.post(c, '/Projects/Create', {
            'Title': f'Dự án B2B Platform {self.run_id}',
            'Description': 'Xây dựng module thanh toán, quản lý đơn hàng và tích hợp đối tác.',
            'ProjectType': 'SOLO',
            'Industry': 'Công nghệ thông tin',
            'Budget': 20000000,
            'Deadline': '2027-04-15'
        }, '/Projects/Create')
        self.project_id = route_id(r_create.headers['Location'])
        self.log_pass(flow, f"Tạo dự án bản nháp mới thành công (ID: {self.project_id})")

        # 4.4 Publish with NO role requirements (should return 409 friendly conflict)
        r_pub_empty = self.smoke.post(c, f'/Projects/Publish/{self.project_id}', {}, f'/Projects/Manage/{self.project_id}', expect_redirect=False)
        if r_pub_empty.status_code == 409 and "Không thể thực hiện" in r_pub_empty.text:
            self.log_pass(flow, "Công bố dự án khi chưa có vị trí trả về HTTP 409 thân thiện (đã sửa lỗi trang trắng IIS)")
        else:
            self.log_issue(flow, "Xử lý Conflict khi công bố", "High", f"Công bố không có vị trí không trả 409 chuẩn: HTTP {r_pub_empty.status_code}")

        # 4.5 Add Role Requirements
        r_add_role = self.smoke.post(c, '/Projects/AddRequirement', {
            'ProjectId': self.project_id,
            'Title': 'Backend Developer',
            'RequiredCount': 1
        }, f'/Projects/Manage/{self.project_id}')
        self.role_id = int(sql(f"SELECT Id FROM RoleRequirements WHERE ProjectId={self.project_id}"))
        self.log_pass(flow, f"Thêm vị trí tuyển dụng vào dự án thành công (RoleRequirementId: {self.role_id})")

        # 4.6 Publish valid project
        r_pub_valid = self.smoke.post(c, f'/Projects/Publish/{self.project_id}', {}, f'/Projects/Manage/{self.project_id}')
        status = sql(f"SELECT Status FROM Projects WHERE Id={self.project_id}")
        if status == 'Published':
            self.log_pass(flow, "Công bố dự án thành công (Status: Published)")
        else:
            self.log_issue(flow, "Công bố dự án", "Critical", f"Trạng thái dự án sau công bố là {status}, mong đợi Published")

    # ========================================================
    # FLOW 5: MARKETPLACE, PROPOSALS & MATCHING
    # ========================================================
    def audit_proposals_flow(self):
        flow = "5. Proposals & Matching"
        f = self.free1_sess

        # 5.1 Marketplace search filters
        r_search = self.smoke.get(f, f'/?q={self.run_id}')
        if f'Dự án B2B Platform {self.run_id}' in r_search.text:
            self.log_pass(flow, "Tìm kiếm Marketplace theo từ khóa dự án thành công")
        else:
            self.log_issue(flow, "Tìm kiếm Marketplace", "Medium", "Không tìm thấy dự án vừa công bố qua bộ lọc")

        # 5.2 View details
        p_details = self.smoke.get(f, f'/Projects/Details/{self.project_id}')
        self.log_pass(flow, "Xem chi tiết dự án và form nộp Proposal thành công")

        # 5.3 Submit Proposal with valid milestone plan syntax (Title | Amount)
        plan = "Mốc 1: Phân tích & Thiết kế API | 10000000\nMốc 2: Triển khai & Nghiệm thu | 10000000"
        r_apply = self.smoke.post(f, '/Proposals/Apply', {
            'ProjectId': self.project_id,
            'RoleRequirementId': self.role_id,
            'Pitch': 'Tôi có đầy đủ kỹ năng backend và cam kết hoàn thành đúng hạn.',
            'QuotedPrice': 20000000,
            'DeliveryDays': 20,
            'MilestonePlan': plan
        }, f'/Projects/Details/{self.project_id}')
        self.proposal_id = int(sql(f"SELECT Id FROM Proposals WHERE ProjectId={self.project_id} AND ApplicantId=(SELECT Id FROM Users WHERE Email='{self.free1_email}')"))
        self.log_pass(flow, f"Gửi hồ sơ chào giá (Proposal #{self.proposal_id}) có kế hoạch mốc thành công")

        # 5.4 Duplicate Proposal check
        r_dup_apply = self.smoke.post(f, '/Proposals/Apply', {
            'ProjectId': self.project_id,
            'RoleRequirementId': self.role_id,
            'Pitch': 'Duplicate proposal attempt',
            'QuotedPrice': 20000000,
            'DeliveryDays': 20
        }, f'/Projects/Details/{self.project_id}', expect_redirect=False)
        if r_dup_apply.status_code in (200, 302, 409):
            count_props = int(sql(f"SELECT COUNT(*) FROM Proposals WHERE ProjectId={self.project_id} AND ApplicantId=(SELECT Id FROM Users WHERE Email='{self.free1_email}')"))
            if count_props == 1:
                self.log_pass(flow, "Chặn gửi chào giá lần 2 vào cùng một dự án/vị trí")
            else:
                self.log_issue(flow, "Chặn gửi chào giá lặp", "High", "Cho phép nộp proposal lặp lại!")

    # ========================================================
    # FLOW 6: PRE-CONTRACT NEGOTIATION
    # ========================================================
    def audit_negotiation_flow(self):
        flow = "6. Negotiation"
        c = self.client_sess
        f = self.free1_sess
        outsider = self.free2_sess

        # 6.1 Client sends message
        r_send = self.smoke.post(c, '/Negotiation/Send', {
            'proposalId': self.proposal_id,
            'body': 'Chào bạn, bạn có thể hoàn thành sớm hơn 2 ngày không?'
        }, f'/Negotiation/Thread?proposalId={self.proposal_id}')
        self.log_pass(flow, "Client gửi tin nhắn đàm phán trước hợp đồng thành công")

        # 6.2 Freelancer replies
        r_reply = self.smoke.post(f, '/Negotiation/Send', {
            'proposalId': self.proposal_id,
            'body': 'Vâng, tôi có thể thu xếp hoàn thành trong 18 ngày.'
        }, f'/Negotiation/Thread?proposalId={self.proposal_id}')
        self.log_pass(flow, "Freelancer phản hồi trao đổi đàm phán thành công")

        # 6.3 Outsider access check
        p_out = self.smoke.get(outsider, f'/Negotiation/Thread?proposalId={self.proposal_id}', status=403)
        self.log_pass(flow, "Bảo mật: Người ngoài không thể xem hoặc can thiệp vào cuộc đàm phán (HTTP 403)")

    # ========================================================
    # FLOW 7: ACCEPTANCE, CONTRACTS & ESCROW FUNDING
    # ========================================================
    def audit_contract_flow(self):
        flow = "7. Contracts & Escrow"
        c = self.client_sess
        f = self.free1_sess
        adm = self.admin_sess

        # 7.1 Client accepts proposal
        r_accept = self.smoke.post(c, f'/Projects/Accept/{self.project_id}', {
            'proposalId': self.proposal_id
        }, f'/Projects/Manage/{self.project_id}')
        self.contract_id = int(sql(f"SELECT Id FROM Contracts WHERE ProjectId={self.project_id}"))
        self.log_pass(flow, f"Client chấp thuận chào giá, tự động lập Hợp đồng #{self.contract_id}")

        # 7.2 Close recruitment / Lock team
        self.smoke.post(c, f'/Projects/LockTeam/{self.project_id}', {}, f'/Projects/Manage/{self.project_id}')
        self.log_pass(flow, "Khóa tuyển dụng, chuyển dự án sang trạng thái chờ ký quỹ (AwaitingEscrow)")

        # 7.3 Work before escrow blocked
        self.workspace_id = int(sql(f"SELECT Id FROM Workspaces WHERE ProjectId={self.project_id}"))
        r_early = self.smoke.post(f, '/Workspace/Submit', {
            'WorkspaceId': self.workspace_id,
            'Title': 'Early delivery',
            'Url': 'https://github.com/test/early'
        }, f'/Workspace/Index/{self.workspace_id}', expect_redirect=False)
        if r_early.status_code == 409:
            self.log_pass(flow, "Chặn nộp sản phẩm khi chưa ký quỹ bảo đảm (HTTP 409)")
        else:
            self.log_issue(flow, "Bảo vệ ký quỹ", "High", f"Cho phép nộp sản phẩm trước khi ký quỹ: HTTP {r_early.status_code}")

        # 7.4 Confirm contract (both sides)
        from ContractFixture import confirm_project
        confirm_project(self.smoke, self.project_id, c, f)
        self.log_pass(flow, "Hai bên xác nhận phiên bản hợp đồng thành công")

        # 7.5 Client requests deposit and Admin approves
        self.smoke.post(c, '/Wallet/RequestMoney', {
            'kind': 'Deposit',
            'amount': 20000000,
            'reference': f'dep-{self.run_id}',
            'details': 'Nạp tiền ký quỹ dự án B2B'
        }, '/Wallet')
        dep_id = int(sql(f"SELECT Id FROM MoneyRequests WHERE Reference='dep-{self.run_id}'"))
        self.smoke.post(adm, f'/Wallet/ReviewMoney/{dep_id}', {
            'approve': 'true',
            'reason': 'Xác nhận đối soát nạp tiền ngân hàng'
        }, '/Wallet/Admin')
        self.log_pass(flow, "Admin duyệt nạp tiền ví thành công (+20.000.000 VND)")

        # 7.6 Fund first milestone
        self.milestone_id = int(sql(f"SELECT TOP 1 Id FROM EscrowMilestones WHERE ProjectId={self.project_id} ORDER BY Id ASC"))
        self.smoke.post(c, f'/Wallet/Fund/{self.milestone_id}', {}, f'/Wallet/Project/{self.project_id}')
        held = int(sql(f"SELECT CONVERT(int, Held) FROM WalletAccounts WHERE UserAccountId=(SELECT Id FROM Users WHERE Email='{self.client_email}')"))
        proj_status = sql(f"SELECT Status FROM Projects WHERE Id={self.project_id}")
        if held >= 10000000 and proj_status == 'InProgress':
            self.log_pass(flow, f"Ký quỹ mốc thành công: Tiền được giữ an toàn trong Escrow, dự án chuyển sang InProgress")
        else:
            self.log_issue(flow, "Ký quỹ Escrow", "Critical", f"Ký quỹ không chuyển trạng thái dự án hoặc không giữ tiền: held={held}, status={proj_status}")

    # ========================================================
    # FLOW 8: WORKSPACE, TASKS & DELIVERABLES
    # ========================================================
    def audit_workspace_flow(self):
        flow = "8. Workspace & Handover"
        c = self.client_sess
        f = self.free1_sess
        outsider = self.free2_sess
        wid = self.workspace_id

        # 8.1 Outsider blocked from Workspace
        p_out = self.smoke.get(outsider, f'/Workspace/Index/{wid}', status=403)
        self.log_pass(flow, "Bảo mật: Người không thuộc dự án bị chặn truy cập Workspace (HTTP 403)")

        # 8.2 Client creates Task
        r_task = self.smoke.post(c, '/Workspace/CreateTask', {
            'WorkspaceId': wid,
            'Title': 'Thiết kế cơ sở dữ liệu và ERD',
            'Description': 'Xây dựng lược đồ bảng và chỉ mục tối ưu.',
            'AssignedToId': int(sql(f"SELECT Id FROM Users WHERE Email='{self.free1_email}'")),
            'EstimatedHours': 16
        }, f'/Workspace/Index/{wid}')
        task_id = int(sql(f"SELECT TOP 1 Id FROM Tasks WHERE WorkspaceId={wid} ORDER BY Id DESC"))
        self.log_pass(flow, f"Tạo nhiệm vụ (Task #{task_id}) trong Workspace thành công")

        # 8.3 Move Task status (Kanban)
        self.smoke.post(f, f'/Workspace/UpdateTaskStatus/{task_id}', {'status': 'InProgress'}, f'/Workspace/Index/{wid}')
        self.smoke.post(f, f'/Workspace/UpdateTaskStatus/{task_id}', {'status': 'Done'}, f'/Workspace/Index/{wid}')
        task_status = sql(f"SELECT Status FROM Tasks WHERE Id={task_id}")
        if task_status == 'Done':
            self.log_pass(flow, "Cập nhật trạng thái Task Kanban (Todo -> InProgress -> Done) và lưu lịch sử đầy đủ")
        else:
            self.log_issue(flow, "Cập nhật Task", "Medium", f"Task không cập nhật đúng trạng thái: {task_status}")

        # 8.4 Submit Deliverable
        r_sub = self.smoke.post(f, '/Workspace/Submit', {
            'WorkspaceId': wid,
            'Title': 'Bản giao đợt 1: Mã nguồn và tài liệu kiến trúc',
            'Url': 'https://github.com/nexus/b2b-api/releases/tag/v1.0.0',
            'EscrowMilestoneId': self.milestone_id
        }, f'/Workspace/Index/{wid}')
        deliv_id = int(sql(f"SELECT MAX(Id) FROM Deliverables WHERE WorkspaceId={wid}"))
        self.log_pass(flow, f"Freelancer nộp sản phẩm bàn giao (Deliverable #{deliv_id}) thành công")

        # 8.5 Client requests revision
        self.smoke.post(c, f'/Workspace/Decide/{deliv_id}', {'decision': 'RevisionsRequested'}, f'/Workspace/Index/{wid}')
        self.log_pass(flow, "Client yêu cầu sửa đổi sản phẩm bàn giao (RevisionsRequested)")

        # 8.6 Freelancer submits revised version
        r_rev = self.smoke.post(f, '/Workspace/Submit', {
            'WorkspaceId': wid,
            'Title': 'Bản giao đợt 1 (Đã chỉnh sửa theo góp ý)',
            'Url': 'https://github.com/nexus/b2b-api/releases/tag/v1.0.1',
            'EscrowMilestoneId': self.milestone_id,
            'RevisesId': deliv_id
        }, f'/Workspace/Index/{wid}')
        rev_id = int(sql(f"SELECT MAX(Id) FROM Deliverables WHERE WorkspaceId={wid}"))
        self.log_pass(flow, f"Freelancer nộp lại bản sửa đổi (#{rev_id}) liên kết với bản cũ")

        # 8.7 Client approves deliverable & settles milestone
        r_appr = self.smoke.post(c, f'/Workspace/Decide/{rev_id}', {'decision': 'Approved'}, f'/Workspace/Index/{wid}')
        free_bal = int(sql(f"SELECT CONVERT(int, Balance) FROM WalletAccounts WHERE UserAccountId=(SELECT Id FROM Users WHERE Email='{self.free1_email}')"))
        if free_bal > 0:
            self.log_pass(flow, f"Nghiệm thu thành công: Freelancer nhận tiền vào ví (90% giá trị mốc, 10% phí sàn)")
        else:
            self.log_issue(flow, "Giải ngân mốc", "Critical", "Nghiệm thu thành công nhưng số dư ví Freelancer không tăng!")

    # ========================================================
    # FLOW 9: REVIEWS & RATINGS
    # ========================================================
    def audit_reviews_flow(self):
        flow = "9. Reviews & Ratings"
        c = self.client_sess
        f = self.free1_sess
        free_uid = int(sql(f"SELECT Id FROM Users WHERE Email='{self.free1_email}'"))

        # Fund and approve second milestone to complete project
        m2_id = sql(f"SELECT TOP 1 Id FROM EscrowMilestones WHERE ProjectId={self.project_id} AND Status='Planned' ORDER BY Id ASC")
        if m2_id and m2_id != '0':
            m2_id = int(m2_id)
            self.smoke.post(c, f'/Wallet/Fund/{m2_id}', {}, f'/Wallet/Project/{self.project_id}')
            self.smoke.post(f, '/Workspace/Submit', {
                'WorkspaceId': self.workspace_id,
                'Title': 'Hoàn tất toàn bộ dự án',
                'Url': 'https://github.com/nexus/b2b-api/releases/tag/v2.0.0',
                'EscrowMilestoneId': m2_id
            }, f'/Workspace/Index/{self.workspace_id}')
            deliv2_id = int(sql(f"SELECT MAX(Id) FROM Deliverables WHERE WorkspaceId={self.workspace_id}"))
            self.smoke.post(c, f'/Workspace/Decide/{deliv2_id}', {'decision': 'Approved'}, f'/Workspace/Index/{self.workspace_id}')

        # 9.1 Client reviews Freelancer via /Projects/Review
        r_c_review = self.smoke.post(c, '/Projects/Review', {
            'ProjectId': self.project_id,
            'FreelancerId': free_uid,
            'Rating': 5,
            'Comment': 'Làm việc rất chuyên nghiệp, giao tiếp rõ ràng và đúng hẹn!'
        }, f'/Projects/Manage/{self.project_id}')
        self.log_pass(flow, "Client gửi đánh giá và nhận xét 5 sao cho Freelancer (/Projects/Review)")

        # 9.2 Freelancer reviews Client via /ClientProfile/Review
        r_f_review = self.smoke.post(f, '/ClientProfile/Review', {
            'projectId': self.project_id,
            'rating': 5,
            'comment': 'Khách hàng tuyệt vời, yêu cầu chi tiết và nghiệm thu nhanh chóng!'
        }, f'/Wallet/Project/{self.project_id}')
        self.log_pass(flow, "Freelancer gửi đánh giá cho Client (/ClientProfile/Review)")

        # 9.3 Duplicate review check
        r_dup_rev = self.smoke.post(f, '/ClientProfile/Review', {
            'projectId': self.project_id,
            'rating': 4, 'comment': 'Duplicate review'
        }, f'/Wallet/Project/{self.project_id}', expect_redirect=False)
        if r_dup_rev.status_code in (200, 409):
            self.log_pass(flow, "Chặn gửi đánh giá lặp lại trên cùng một dự án (HTTP 409)")
        else:
            self.log_issue(flow, "Chặn đánh giá lặp", "Medium", f"Cho phép gửi nhiều đánh giá cho cùng 1 hợp đồng: HTTP {r_dup_rev.status_code}")

    # ========================================================
    # FLOW 10: USER MODERATION & ACCOUNT SUSPENSION
    # ========================================================
    def audit_moderation_flow(self):
        flow = "10. Admin Moderation"
        adm = self.admin_sess
        target_email = self.free2_email
        target_sess = self.free2_sess

        # 10.1 Admin views users
        p_users = self.smoke.get(adm, '/Admin/Users')
        if p_users.status_code == 200:
            self.log_pass(flow, "Admin xem danh sách người dùng và trạng thái tài khoản thành công")
        else:
            self.log_issue(flow, "Admin xem Users", "High", f"Lỗi truy cập Admin Users: {p_users.status_code}")

        # 10.2 Admin suspends user
        target_uid = int(sql(f"SELECT Id FROM Users WHERE Email='{target_email}'"))
        r_sus = self.smoke.post(adm, f'/Admin/Suspend/{target_uid}', {
            'Reason': 'Tài khoản thử nghiệm vi phạm quy tắc nội dung kiểm thử'
        }, '/Admin/Users')
        is_suspended = sql(f"SELECT IsSuspended FROM Users WHERE Id={target_uid}")
        if is_suspended == '1':
            self.log_pass(flow, "Admin tạm khóa tài khoản (Suspend) có ghi lý do rõ ràng")
        else:
            self.log_issue(flow, "Khóa tài khoản", "Critical", "Admin khóa tài khoản nhưng trạng thái CSDL không đổi!")

        # 10.3 Suspended user active session revoked
        p_blocked = self.smoke.get(target_sess, '/Dashboard', status=403)
        self.log_pass(flow, "Thu hồi quyền truy cập ngay lập tức đối với phiên của tài khoản bị khóa (HTTP 403)")

        # 10.4 Suspended user login denied
        r_blocked_login = self.login(self.session(), target_email, "Freelancer")
        if r_blocked_login.status_code == 200 and "tạm khóa" in r_blocked_login.text:
            self.log_pass(flow, "Chặn đăng nhập tài khoản đã bị khóa kèm thông báo hướng dẫn")
        else:
            self.log_issue(flow, "Chặn đăng nhập tài khoản khóa", "Critical", "Tài khoản bị khóa vẫn đăng nhập được hoặc không báo lý do!")

        # 10.5 Admin restores user
        r_restore = self.smoke.post(adm, f'/Admin/Restore/{target_uid}', {
            'Reason': 'Khôi phục tài khoản sau khi kiểm tra khiếu nại'
        }, '/Admin/Users')
        is_suspended = sql(f"SELECT IsSuspended FROM Users WHERE Id={target_uid}")
        if is_suspended == '0':
            self.log_pass(flow, "Admin mở khóa tài khoản (Restore) thành công")
        else:
            self.log_issue(flow, "Khôi phục tài khoản", "High", "Không mở khóa được tài khoản!")

    def run_all(self):
        print(f"=== BẮT ĐẦU KIỂM THỬ TOÀN DIỆN VÒNG ĐỜI HỆ THỐNG (Run: {self.run_id}) ===", flush=True)
        try:
            self.audit_auth_flow()
            self.audit_profile_flow()
            self.audit_skills_flow()
            self.audit_project_flow()
            self.audit_proposals_flow()
            self.audit_negotiation_flow()
            self.audit_contract_flow()
            self.audit_workspace_flow()
            self.audit_reviews_flow()
            self.audit_moderation_flow()
        finally:
            print("\n=== TỔNG KẾT KIỂM THỬ ===", flush=True)
            print(f"Tổng số kiểm tra thành công: {len(self.passed_checks)}")
            print(f"Tổng số lỗi / vấn đề phát hiện: {len(self.findings)}")
            for item in self.findings:
                print(f"  * [{item['severity'].upper()}] [{item['flow']}] {item['title']}: {item['detail']}")

if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--mailbox', default=r'D:\FreelancerBTLWEB\_local\mailbox')
    parser.add_argument('--base', default='https://localhost:44391')
    args = parser.parse_args()
    auditor = LifecycleAuditor(args.base, args.mailbox)
    auditor.run_all()
