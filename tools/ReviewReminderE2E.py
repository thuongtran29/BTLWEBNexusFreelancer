"""Local-only regression: overdue reminder races, revisions and dispute freeze.
Uses disposable V3FlowE2E identities and explicitly approved write fixtures.
"""
import argparse
import re
import uuid
from concurrent.futures import ThreadPoolExecutor
import requests
import urllib3
from SmokeE2E import Smoke, forms, route_id
from V3FlowE2E import sql, assert_eq
from ContractFixture import confirm_project, dispute_contract


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--run-writes', action='store_true')
    parser.add_argument('--fixture-run', required=True)
    args = parser.parse_args()
    if not args.run_writes or not re.fullmatch('[a-f0-9]{12}', args.fixture_run):
        parser.error('Explicit local fixture run and write flag required')
    urllib3.disable_warnings()
    base = 'https://localhost:44391'
    smoke = Smoke(base)
    run = uuid.uuid4().hex[:10]
    address = f'v3-{args.fixture_run}@example.test'
    admin_address = f'v3-admin-{args.fixture_run}@example.test'
    password = 'NexusV3!' + args.fixture_run + 'Aa7'
    admin_id = int(sql(f"SELECT Id FROM Users WHERE Email='{admin_address}'"))
    def session():
        s = requests.Session(); s.verify = False
        return s
    def post(s, path, fields, page): return smoke.post(s, path, fields, page)
    def login(email, role):
        s = session()
        post(s, '/Account/Login', {'Email':email, 'Role':role, 'Password':password}, '/Account/Login')
        return s
    def check(label, actual, expected): smoke.check(label, lambda: assert_eq(actual, expected))
    sql(f"UPDATE Users SET Role='Admin' WHERE Id={admin_id}")
    try:
        client, freelancer, admin = login(address,'Client'), login(f'v3-worker-{args.fixture_run}@example.test','Freelancer'), login(admin_address,'Admin')
        for resource, expected in [('azure-fundamentals',200), ('forged',404), ('scrum-open',404)]:
            response = freelancer.get(base+'/Skills/Certifications', params={'resource':resource}, timeout=30)
            check('certificate prefill: '+resource, response.status_code, expected)
            if expected == 200:
                fields = next(f['fields'] for f in forms(response.text) if f['action'].endswith('/SubmitCertification'))
                check('credential name prefilled from source', fields['Name'], 'Microsoft Certified: Azure Fundamentals')
                check('credential issuer prefilled from source', fields['Issuer'], 'Microsoft Learn')
        check('client cannot submit freelancer certificate', client.get(base+'/Skills/Certifications?resource=azure-fundamentals',timeout=30).status_code,403)
        center = smoke.get(freelancer,'/Skills').text
        check('historical E2E catalog hidden', 'Admin Skills E2E' in center or 'Timer test' in center, False)
        r = post(client, '/Projects/Create', {'Title':'Review reminder '+run,'Description':'Disposable API acceptance scope and documented delivery','ProjectType':'SOLO','Industry':'Công nghệ thông tin','Budget':1000,'Deadline':'2027-02-01'}, '/Projects/Create')
        pid = route_id(r.headers['Location'])
        manage = f'/Projects/Manage/{pid}'
        post(client,'/Projects/AddRequirement',{'ProjectId':pid,'Title':'Backend Developer','RequiredCount':1},manage)
        post(client,f'/Projects/Publish/{pid}',{},manage)
        rid = int(sql(f'SELECT Id FROM RoleRequirements WHERE ProjectId={pid}'))
        post(freelancer,'/Proposals/Apply',{'ProjectId':pid,'RoleRequirementId':rid,'Pitch':'API implementation and test handover','QuotedPrice':1000},f'/Projects/Details/{pid}')
        proposal = int(sql(f'SELECT Id FROM Proposals WHERE ProjectId={pid}'))
        post(client,f'/Projects/Accept/{pid}',{'proposalId':proposal},manage)
        post(client,f'/Projects/LockTeam/{pid}',{},manage)
        confirm_project(smoke,pid,client,freelancer)
        post(client,'/Wallet/RequestMoney',{'kind':'Deposit','amount':1000,'reference':'reminder-'+run,'details':'Disposable local test, no real bank transaction'},'/Wallet')
        money = int(sql(f"SELECT Id FROM MoneyRequests WHERE Reference='reminder-{run}'"))
        post(admin,f'/Wallet/ReviewMoney/{money}',{'approve':'true','reason':'Local test amount only'},'/Wallet/Admin')
        mid = int(sql(f'SELECT Id FROM EscrowMilestones WHERE ProjectId={pid}'))
        wid = int(sql(f'SELECT Id FROM Workspaces WHERE ProjectId={pid}'))
        wallet, workspace = f'/Wallet/Project/{pid}', f'/Workspace/Index/{wid}'
        post(client,f'/Wallet/Fund/{mid}',{},wallet)
        def submit(previous=None):
            fields = {'WorkspaceId':wid,'EscrowMilestoneId':mid,'Title':'API handover','Url':'https://example.test/reminder'}
            if previous: fields['RevisesId'] = previous
            post(freelancer,'/Workspace/Submit',fields,workspace)
            return int(sql(f'SELECT MAX(Id) FROM Deliverables WHERE WorkspaceId={wid}'))
        did = submit()
        post(admin,'/Wallet/ProcessDue',{},'/Wallet/Admin')
        check('fresh handover not reminded',sql(f'SELECT COUNT(*) FROM EscrowMilestones WHERE Id={mid} AND ReviewRemindedAtUtc IS NOT NULL'),'0')
        sql(f'UPDATE EscrowMilestones SET SubmittedAtUtc=DATEADD(day,-8,GETUTCDATE()) WHERE Id={mid}')
        token = next(f['fields']['__RequestVerificationToken'] for f in forms(smoke.get(admin,'/Wallet/Admin').text) if '__RequestVerificationToken' in f['fields'])
        def send(_):
            s = session(); s.cookies.update(admin.cookies)
            return s.post(base+'/Wallet/ProcessDue',data={'__RequestVerificationToken':token},allow_redirects=False,timeout=40).status_code
        with ThreadPoolExecutor(max_workers=2) as pool: statuses = list(pool.map(send,range(2)))
        check('concurrent reminder requests controlled', statuses, [302,302])
        def reminders(): return sql(f'SELECT COUNT(*) FROM MilestoneReviewReminders WHERE EscrowMilestoneId={mid}')
        check('concurrent reminder recorded once', reminders(),'1')
        check('reminder never approves handover',sql(f'SELECT Status FROM Deliverables WHERE Id={did}'),'Submitted')
        check('reminder never settles funds',sql(f"SELECT COUNT(*) FROM WalletEntries WHERE EscrowMilestoneId={mid} AND Kind='EscrowSettle'"),'0')
        post(client,f'/Workspace/Decide/{did}',{'decision':'RevisionsRequested','feedback':'Bổ sung kiểm thử đầu vào và tài liệu bàn giao đúng phạm vi đã thống nhất.'},workspace)
        did = submit(did)
        check('revision starts a new reminder window',sql(f'SELECT COUNT(*) FROM EscrowMilestones WHERE Id={mid} AND ReviewRemindedAtUtc IS NULL'),'1')
        sql(f'UPDATE EscrowMilestones SET SubmittedAtUtc=DATEADD(day,-8,GETUTCDATE()) WHERE Id={mid}')
        post(admin,'/Wallet/ProcessDue',{},'/Wallet/Admin')
        check('second overdue revision gets its own reminder',reminders(),'2')
        post(client,f'/Workspace/Decide/{did}',{'decision':'RevisionsRequested','feedback':'Bổ sung kiểm thử đầu vào và tài liệu bàn giao đúng phạm vi đã thống nhất.'},workspace)
        did = submit(did)
        sql(f'UPDATE EscrowMilestones SET SubmittedAtUtc=DATEADD(day,-8,GETUTCDATE()) WHERE Id={mid}')
        post(client,f'/Wallet/Dispute/{pid}',{'contractId':dispute_contract(pid),'reason':'Acceptance scope needs administrator investigation'},wallet)
        post(admin,'/Wallet/ProcessDue',{},'/Wallet/Admin')
        check('pending dispute stops reminders',reminders(),'2')
        check('disputed funds stay held',sql(f'SELECT Status FROM EscrowMilestones WHERE Id={mid}'),'Submitted')
        check('immutable delivery history retained',sql(f'SELECT COUNT(*) FROM Deliverables WHERE WorkspaceId={wid}'),'3')
        print(f'REVIEW_REMINDER_E2E_PASSED checks={len(smoke.rows)} run={run}',flush=True)
    finally:
        sql(f"UPDATE Users SET Role='Client' WHERE Id={admin_id}")


if __name__ == '__main__': main()
