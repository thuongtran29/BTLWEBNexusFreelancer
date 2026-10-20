"""Write tests ONLY on the explicitly named isolated audit database/site, never the live site.

python tools/PlatformJourneyE2E.py --base https://localhost:44392 --database NexusFreelanceAudit20261019
Uses the local encrypted demo credential catalog in memory; prints no credentials.
"""
import argparse
import ctypes
import ctypes.wintypes as wt
import json
import re
import subprocess
import uuid
from html import unescape
from html.parser import HTMLParser
from pathlib import Path
import requests
import urllib3


class Blob(ctypes.Structure):
    _fields_ = [('size', wt.DWORD), ('data', ctypes.POINTER(ctypes.c_ubyte))]


def catalog():
    raw = Path(r'D:\FreelancerBTLWEB\_local\demo-accounts.dat').read_bytes()
    entropy = b'NexusFreelance.demo-accounts.v1'
    first, second = ctypes.create_string_buffer(raw), ctypes.create_string_buffer(entropy)
    source = Blob(len(raw), ctypes.cast(first, ctypes.POINTER(ctypes.c_ubyte)))
    salt = Blob(len(entropy), ctypes.cast(second, ctypes.POINTER(ctypes.c_ubyte)))
    output = Blob()
    if not ctypes.windll.crypt32.CryptUnprotectData(ctypes.byref(source), None, ctypes.byref(salt), None, None, 0, ctypes.byref(output)):
        raise RuntimeError('Cannot read local test catalog.')
    try:
        return json.loads(ctypes.string_at(output.data, output.size).decode('utf-8-sig'))
    finally:
        ctypes.memset(output.data, 0, output.size)
        ctypes.windll.kernel32.LocalFree(output.data)


class Fields(HTMLParser):
    def __init__(self, text):
        super().__init__(); self.values = {}; self.links = []; self.feed(text)

    def handle_starttag(self, tag, attrs):
        a = dict(attrs)
        if tag == 'input' and a.get('name'): self.values.setdefault(a['name'], a.get('value', ''))
        if tag == 'a' and a.get('data-nav-key'): self.links.append(a.get('href'))


class Journey:
    def __init__(self, base, database):
        assert base == 'https://localhost:44392' and re.fullmatch(r'NexusFreelanceAudit[0-9]{8}', database), 'Only the isolated audit site/database is allowed.'
        self.base, self.database = base, database
        self.checks = []; self.credentials = catalog(); self.tag = 'Nghiệm thử nền tảng ' + uuid.uuid4().hex[:8]

    def sql(self, query):
        result = subprocess.run([r'C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\180\Tools\Binn\SQLCMD.EXE', '-S', r'.\SQLEXPRESS', '-d', self.database, '-E', '-C', '-b', '-h', '-1', '-W', '-f', '65001', '-Q', 'SET NOCOUNT ON; SET QUOTED_IDENTIFIER ON; ' + query], capture_output=True, encoding='utf-8', timeout=45)
        if result.returncode: raise AssertionError('Audit SQL failed: ' + result.stdout[:400])
        return result.stdout.strip()

    def check(self, condition, label):
        if not condition: raise AssertionError(label)
        self.checks.append(label); print('PASS ' + label, flush=True)

    def get(self, session, path):
        page = session.get(self.base + path, timeout=100)
        if page.status_code >= 500: raise AssertionError('Server error at ' + path + ': ' + re.sub('<[^>]+>', ' ', page.text)[-1700:])
        return page

    def post(self, session, path, data=None, form='/Dashboard', follow=True):
        fields = Fields(self.get(session, form).text).values
        token = fields.get('__RequestVerificationToken')
        assert token, 'Missing CSRF on ' + form
        page = session.post(self.base + path, data={**(data or {}), '__RequestVerificationToken': token}, timeout=100, allow_redirects=follow)
        if page.status_code >= 500: raise AssertionError('POST server error at ' + path + ': ' + re.sub('<[^>]+>', ' ', page.text)[-1700:])
        return page

    def login(self, key, role):
        s = requests.Session(); s.verify = False
        p = self.post(s, '/Account/Login', {'Email': key + '@nexus.example.test', 'Password': self.credentials['Password'], 'Role': role}, form='/Account/Login')
        self.check(p.status_code == 200 and 'GoogleLoginButton' not in p.text, role + ' login')
        return s

    def run(self):
        client = self.login('client.minhan', 'Client'); worker = self.login('linh.backend', 'Freelancer'); admin = self.login('admin', 'Admin'); outsider = self.login('vy.translation', 'Freelancer')
        pending = self.sql("SELECT TOP 1 r.Id FROM dbo.SkillEvidenceRequests r JOIN dbo.SkillClaims c ON c.Id=r.SkillClaimId WHERE c.UserAccountId=8 AND c.SkillTrackId=1 AND r.Status='Pending' ORDER BY r.Id DESC")
        if pending:
            self.post(admin, '/SkillEvidence/Review/' + pending, {'approve': 'true', 'reason': 'Xác minh dữ liệu minh chứng trong CSDL audit riêng, phục vụ kiểm thử ghép nối.'}, form='/SkillEvidence/Admin')
        for role, session, start in [('Client', client, '/Dashboard'), ('Freelancer', worker, '/Dashboard'), ('Admin', admin, '/Admin')]:
            for path in Fields(self.get(session, start).text).links:
                p = self.get(session, path); self.check(p.status_code == 200, role + ' nav ' + path)
        p = self.get(admin, '/Workspace/Index/8')
        self.check(p.status_code == 200 and 'Chế độ kiểm tra' in unescape(p.text) and 'action="/Workspace/CreateTask"' not in p.text, 'Admin workspace audit is read-only; former IIS 403 is resolved')
        p = self.get(outsider, '/Workspace/Index/8')
        self.check(p.status_code == 403 and 'Trang này cần quyền' in unescape(p.text) and 'Detailed Error' not in p.text, 'Unrelated freelancer remains forbidden with application guidance')
        for path in ['/Account/ForgotPassword', '/Account/ResendVerification', '/Talent/Freelancer/8', '/Contribution', '/Skills/Certifications']:
            self.check(self.get(worker, path).status_code == 200, 'Account/profile route ' + path)
        p = self.post(client, '/Projects/Create', {'Title': self.tag, 'Description': 'Xây API ASP.NET MVC quản lý yêu cầu, có kiểm thử và bàn giao mã nguồn.', 'Industry': 'Công nghệ thông tin', 'ProjectType': 'SOLO', 'Budget': 20000000, 'SkillIds': [1], 'MinimumSkillLevel': 1}, form='/Projects/Create')
        match = re.search(r'/Projects/Manage/(\d+)', p.url); assert match, 'Project did not save'
        pid = int(match.group(1)); self.check('smart-matching' in p.text and 'id="project-applicants"' not in p.text, 'Draft management shows matching without redundant application panels')
        self.check(self.sql(f'SELECT COUNT(*) FROM dbo.ProjectSkillRequirements WHERE ProjectId={pid} AND SkillTrackId=1') == '1', 'Project core skill persisted')
        self.check('Nguyễn Khánh Linh' in unescape(p.text), 'Client gets verified backend freelancer suggestion')
        self.post(client, '/Projects/AddRequirement', {'ProjectId': pid, 'Title': 'Backend Developer', 'RequiredCount': 1}, form=f'/Projects/Manage/{pid}')
        rid = int(self.sql(f'SELECT Id FROM dbo.RoleRequirements WHERE ProjectId={pid}'))
        self.post(client, f'/Projects/Publish/{pid}', form=f'/Projects/Manage/{pid}')
        self.check(self.tag in unescape(self.get(worker, '/Dashboard').text), 'Reverse recommendation shows published project for matching freelancer')
        self.post(worker, '/Proposals/Apply', {'ProjectId': pid, 'RoleRequirementId': rid, 'Pitch': 'Triển khai API và kiểm thử đúng phạm vi, có hướng dẫn vận hành.', 'QuotedPrice': 18000000, 'DeliveryDays': 14}, form=f'/Projects/Details/{pid}')
        proposal = int(self.sql(f'SELECT Id FROM dbo.Proposals WHERE ProjectId={pid}'))
        p = self.get(client, '/Projects/Mine')
        self.check('cập nhật chưa đọc' in unescape(p.text), 'Project list shows new project-specific activity')
        self.check(self.sql(f'SELECT COUNT(*) FROM dbo.Notifications WHERE ProjectId={pid} AND ActorId=8') == '1', 'Notification records source project and sender')
        self.post(client, f'/Projects/Accept/{pid}', {'proposalId': proposal}, form=f'/Projects/Manage/{pid}')
        cid = int(self.sql(f'SELECT Id FROM dbo.Contracts WHERE ProjectId={pid}'))
        version, digest = self.sql(f'SELECT CAST(Id AS varchar)+\'|\'+Hash FROM dbo.ContractVersions WHERE ContractId={cid}').split('|')
        for s in [client, worker]:
            self.post(s, f'/Contracts/Confirm/{cid}', {'versionId': version, 'hash': digest}, form=f'/Contracts/Details/{cid}')
        self.check(self.sql(f'SELECT Status FROM dbo.Contracts WHERE Id={cid}') == 'AwaitingEscrow', 'Contract awaits escrow only after both confirmations')
        milestone = int(self.sql(f'SELECT Id FROM dbo.EscrowMilestones WHERE ContractId={cid}'))
        self.post(client, f'/Wallet/Fund/{milestone}', form=f'/Contracts/Details/{cid}')
        self.check(self.sql(f'SELECT Status FROM dbo.Contracts WHERE Id={cid}') == 'Active', 'Wallet funding activates the confirmed contract')
        wid = int(self.sql(f'SELECT Id FROM dbo.Workspaces WHERE ContractId={cid}'))
        p = self.post(client, '/Workspace/CreateTask', {'WorkspaceId': wid, 'Title': 'Client must not assign', 'AssigneeId': 8, 'Priority': 'Normal'}, form=f'/Workspace/Index/{wid}')
        self.check(p.status_code == 403 and self.sql(f'SELECT COUNT(*) FROM dbo.Tasks WHERE WorkspaceId={wid}') == '0', 'Client cannot create internal tasks')
        p = self.post(worker, '/Workspace/CreateTask', {'WorkspaceId': wid, 'Title': 'Triển khai API', 'AssigneeId': 8, 'Priority': 'High'}, form=f'/Workspace/Index/{wid}')
        task = int(self.sql(f'SELECT Id FROM dbo.Tasks WHERE WorkspaceId={wid}'))
        self.check(p.status_code == 200, 'Solo delivery owner creates a task')
        for status in ['Doing', 'Review', 'Done']:
            p = self.post(worker, f'/Workspace/UpdateTask/{task}', {'status': status}, form=f'/Workspace/Index/{wid}')
            self.check(p.status_code == 200 and self.sql(f'SELECT Status FROM dbo.Tasks WHERE Id={task}') == status, 'Task transition ' + status)
        self.post(worker, '/Workspace/Submit', {'WorkspaceId': wid, 'Title': 'Bàn giao API hoàn chỉnh', 'Url': 'https://example.com/delivery', 'EscrowMilestoneId': milestone}, form=f'/Workspace/Index/{wid}')
        did = int(self.sql(f'SELECT Id FROM dbo.Deliverables WHERE WorkspaceId={wid}'))
        self.post(client, f'/Workspace/Decide/{did}', {'decision': 'Approved'}, form=f'/Workspace/Index/{wid}')
        self.check(self.sql(f'SELECT Status FROM dbo.EscrowMilestones WHERE Id={milestone}') == 'Released', 'Client acceptance releases the exact funded milestone')
        self.check(self.sql(f'SELECT Status FROM dbo.Contracts WHERE Id={cid}') == 'Completed', 'Accepted deliverable completes its own contract')
        before = self.sql(f'SELECT COUNT(*) FROM dbo.WalletEntries WHERE EscrowMilestoneId={milestone}')
        self.post(client, f'/Workspace/Decide/{did}', {'decision': 'Approved'}, form=f'/Workspace/Index/{wid}')
        self.check(self.sql(f'SELECT COUNT(*) FROM dbo.WalletEntries WHERE EscrowMilestoneId={milestone}') == before, 'Replayed acceptance cannot settle twice')
        # Direct contact permissions and sender details.
        p = self.post(client, '/Messages/Contact', {'freelancerId': 8}, form='/Talent/Freelancer/8')
        thread = int(re.search(r'/Messages/Thread/(\d+)', p.url).group(1))
        self.post(client, f'/Messages/Send/{thread}', {'body': 'Cần trao đổi dự án API và lịch bàn giao.'}, form=f'/Messages/Thread/{thread}')
        self.check('Cần trao đổi dự án API' in unescape(self.get(worker, f'/Messages/Thread/{thread}').text), 'Public profile contact opens a private Client–Freelancer conversation')
        self.check(self.get(outsider, f'/Messages/Thread/{thread}').status_code == 404, 'Other freelancers cannot read private conversation')
        p = self.post(worker, '/Messages/Contact', {'freelancerId': 8}, form='/Dashboard')
        self.check(p.status_code == 403, 'Freelancer cannot impersonate a Client contact')
        # Bad input and stale CSRF never write business data.
        for path in ['/Projects/Create', '/Workspace/CreateTask', '/Messages/Send/' + str(thread), '/Wallet/Topup']:
            p = client.post(self.base + path, data={}, timeout=45)
            self.check(p.status_code == 400 and 'biểu mẫu' in unescape(p.text).lower(), 'Invalid CSRF blocked with helpful 400 ' + path)
        p = self.post(client, '/Wallet/Topup', {'amount': -1}, form='/Wallet')
        self.check(p.status_code == 200 and 'Số tiền nạp' in unescape(p.text), 'Invalid top-up is explained without crediting wallet')
        # Seed group #1 contract remains private to its contractual roster.
        lead = self.login('minh.frontend', 'Freelancer')
        p = self.post(lead, '/Workspace/CreateTask', {'WorkspaceId': 4, 'Title': self.tag + ' nhóm', 'AssigneeId': 8, 'Priority': 'Normal'}, form='/Workspace/Index/4')
        self.check(p.status_code == 200 and self.sql("SELECT COUNT(*) FROM dbo.Tasks WHERE WorkspaceId=4 AND Title LIKE N'"+self.tag+"%'") == '1', 'Team leader assigns work inside the group contract')
        self.check(self.get(outsider, '/Workspace/Index/4').status_code == 403, 'Unrelated freelancer cannot enter group workspace')
        p = self.post(client, '/JobStudio/Generate', {'Idea': 'Xây trang quản lý lịch hẹn cho doanh nghiệp nhỏ, có thông báo và báo cáo.', 'ProjectType': 'SOLO', 'Budget': 3000000, 'Industry': 'Công nghệ thông tin', 'UseProjectKnowledge': 'true', 'UseExternalAi': 'false'}, form='/JobStudio/New')
        draft = int(re.search(r'/JobStudio/Edit/(\d+)', p.url).group(1))
        self.check(self.sql(f"SELECT PromptVersion FROM dbo.AIJobDraftVersions WHERE AIJobDraftId={draft}") == 'JD-Knowledge-v4', 'JD records prompt version with its generated draft')
        p = self.post(client, f'/JobStudio/Delete/{draft}', form='/JobStudio')
        self.check(self.sql(f'SELECT IsDeleted FROM dbo.AIJobDrafts WHERE Id={draft}') == '1' and self.get(client, f'/JobStudio/Edit/{draft}').status_code == 404, 'Unused JD draft deletion hides it and revokes edit access')
        print('PLATFORM_HTTP_CHECKS=' + str(len(self.checks)), flush=True)
        return {'checks': self.checks, 'projectId': pid, 'contractId': cid, 'workspaceId': wid, 'database': self.database, 'site': self.base}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(); parser.add_argument('--base', required=True); parser.add_argument('--database', required=True); parser.add_argument('--result', required=True); args = parser.parse_args()
    urllib3.disable_warnings(urllib3.exceptions.InsecureRequestWarning)
    result = Journey(args.base, args.database).run()
    Path(args.result).write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')
