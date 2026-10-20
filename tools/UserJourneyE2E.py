"""Role-scoped work guidance regression on localhost, using existing test accounts.
Run with --fixture-run HEX12; add --run-writes to verify draft creation and isolation.
The write case removes only its own unchanged draft in finally; it never resets the DB.
"""
import argparse
import html
import re
import uuid
import requests
import urllib3
from SmokeE2E import Smoke, route_id, forms
from V3FlowE2E import sql

p=argparse.ArgumentParser()
p.add_argument('--fixture-run',required=True)
p.add_argument('--run-writes',action='store_true')
a=p.parse_args()
assert re.fullmatch('[a-f0-9]{12}',a.fixture_run)
urllib3.disable_warnings(urllib3.exceptions.InsecureRequestWarning)
test=Smoke('https://localhost:44391')
password='NexusV3!'+a.fixture_run+'Aa7'
def login(email,role):
    s=requests.Session();s.verify=False
    test.post(s,'/Account/Login',{'Email':email,'Role':role,'Password':password},'/Account/Login')
    return s
client_email=f'v3-{a.fixture_run}@example.test'
client=login(client_email,'Client')
worker=login(f'v3-worker-{a.fixture_run}@example.test','Freelancer')
outsider=login(f'v3-admin-{a.fixture_run}@example.test','Client')
def markup(session,path='/Dashboard'):
    return html.unescape(test.get(session,path).text)
def count(body,key):
    m=re.search(r'data-work-count="'+key+r'"[^>]*>\s*(\d+)',body)
    assert m, f'Missing visible work count: {key}'
    return int(m.group(1))
page=markup(client)
assert 'data-journey-role="Client"' in page, 'Dashboard must explain the Client work journey'
assert 'Việc cần làm tiếp' in page and 'Từng bước để bắt đầu' in page
cid=int(sql(f"SELECT Id FROM Users WHERE Email='{client_email}' AND Role='Client'"))
expected=int(sql(f"SELECT COUNT(*) FROM Projects WHERE ClientId={cid} AND Status='Draft'"))
assert count(page,'drafts')==expected
worker_page=markup(worker)
assert 'data-journey-role="Freelancer"' in worker_page
assert 'data-next-action="fund"' not in worker_page, 'Freelancer cannot receive the Client funding action'
assert 'Hoàn thiện hồ sơ' in worker_page and 'Tìm dự án' in worker_page
for stage in ('confirm','fund','active','review','issues','closed'):
    body=markup(client,'/Contracts?stage='+stage)
    assert 'data-contract-stage="'+stage+'"' in body
assert client.get(test.base+'/Contracts?stage=forged',timeout=25).status_code==400
assert 'Ký quỹ' in markup(client,'/Wallet') and 'Số dư khả dụng' in markup(client,'/Wallet')
profile=markup(worker,'/Profile')
assert re.search(r'<select[^>]*id="PortfolioProject"',profile), 'Select completed projects by title rather than entering database IDs'
evidence=markup(worker,'/SkillEvidence')
assert 'Mã kết quả bài kiểm tra' not in evidence and 'Mã Portfolio của bạn' not in evidence
assert 'Khai báo kỹ năng' in evidence and 'Chọn bằng chứng' in evidence
print('PASS role guidance, real counts, contract filters and money explanations')

pid=None
title='Journey fixture '+uuid.uuid4().hex[:12]
try:
    if a.run_writes:
        result=test.post(client,'/Projects/Create',{'Title':title,'Description':'Owned draft for user journey and data isolation regression.','Industry':'Công nghệ thông tin','ProjectType':'SOLO','Budget':'12000000'},'/Projects/Create')
        pid=route_id(result.headers.get('Location'))
        own=markup(client)
        assert count(own,'drafts')==expected+1 and 'data-next-action="drafts"' in own
        assert title in own and title not in markup(outsider), 'Draft must remain scoped to its owner'
        assert title not in markup(worker), 'Private draft must not leak to freelancers'
        print('PASS created draft changes next action; two other accounts cannot see it')
        owner_confirm=count(own,'confirm')
        worker_confirm=count(markup(worker),'confirm')
        manage=f'/Projects/Manage/{pid}'
        test.post(client,'/Projects/AddRequirement',{'ProjectId':pid,'Title':'Backend Developer','RequiredCount':1},manage)
        test.post(client,f'/Projects/Publish/{pid}',{},manage)
        role_id=int(sql(f'SELECT Id FROM RoleRequirements WHERE ProjectId={pid}'))
        test.post(worker,'/Proposals/Apply',{'ProjectId':pid,'RoleRequirementId':role_id,'Pitch':'A reproducible implementation, documented delivery and testable acceptance criteria.','QuotedPrice':'1000000','DeliveryDays':14},f'/Projects/Details/{pid}')
        proposal=int(sql(f'SELECT Id FROM Proposals WHERE ProjectId={pid}'))
        test.post(client,f'/Projects/Accept/{pid}',{'proposalId':proposal},manage)
        contract=int(sql(f'SELECT Id FROM Contracts WHERE ProjectId={pid}'))
        assert count(markup(client),'confirm')==owner_confirm+1
        assert count(markup(worker),'confirm')==worker_confirm+1
        assert title not in markup(outsider,f'/Contracts?projectId={pid}'), 'Contract filters must preserve visibility'
        for actor in (worker,client):
            page=test.get(actor,f'/Contracts/Details/{contract}')
            consent=[f for f in forms(page.text) if '/Contracts/Confirm/' in f['action']]
            assert len(consent)==1
            test.post(actor,consent[0]['action'],consent[0]['fields'])
            if actor is worker:
                assert count(markup(worker),'confirm')==worker_confirm, 'Already-signed current version must leave the personal signing queue'
                assert title not in markup(worker,'/Contracts?stage=confirm')
                assert count(markup(client),'confirm')==owner_confirm+1
        assert sql(f'SELECT Status FROM Contracts WHERE Id={contract}')=='AwaitingEscrow'
        assert title in markup(client,'/Contracts?stage=fund')
        assert 'data-next-action="fund"' in markup(client)
        assert 'data-next-action="fund"' not in markup(worker)
        print('PASS live signing queues, current signer exclusion, funding next-step and contract privacy')
finally:
    if pid:
        sql(f"""SET XACT_ABORT ON; BEGIN TRAN;
IF NOT EXISTS(SELECT 1 FROM Projects WHERE Id={pid} AND ClientId={cid} AND Status IN('Draft','Published','AwaitingEscrow') AND Title=N'{title}') THROW 51741,'Fixture state changed; preserve data',1;
IF EXISTS(SELECT 1 FROM EscrowMilestones WHERE ProjectId={pid} AND Status<>'Planned') OR EXISTS(SELECT 1 FROM PaymentIntents WHERE ContractId IN(SELECT Id FROM Contracts WHERE ProjectId={pid})) THROW 51742,'Fixture has money activity; preserve data',1;
IF EXISTS(SELECT 1 FROM Tasks WHERE WorkspaceId IN(SELECT Id FROM Workspaces WHERE ProjectId={pid})) THROW 51743,'Fixture has new work; preserve data',1;
DELETE Notifications WHERE RecipientId IN(SELECT Id FROM Users WHERE Email IN('{client_email}','v3-worker-{a.fixture_run}@example.test')) AND (TargetPath='/Projects/Manage/{pid}' OR TargetPath='/Projects/Details/{pid}' OR TargetPath IN(SELECT '/Contracts/Details/'+CONVERT(varchar(20),Id) FROM Contracts WHERE ProjectId={pid}));
DELETE ContractAcceptances WHERE ContractVersionId IN(SELECT v.Id FROM ContractVersions v JOIN Contracts c ON c.Id=v.ContractId WHERE c.ProjectId={pid});
DELETE ContractMembers WHERE ContractId IN(SELECT Id FROM Contracts WHERE ProjectId={pid});
DELETE ContractVersions WHERE ContractId IN(SELECT Id FROM Contracts WHERE ProjectId={pid});
DELETE Workspaces WHERE ProjectId={pid}; DELETE EscrowShares WHERE ProjectId={pid}; DELETE EscrowMilestones WHERE ProjectId={pid}; DELETE Contracts WHERE ProjectId={pid};
DELETE TeamMembers WHERE ProjectTeamId IN(SELECT Id FROM ProjectTeams WHERE ProjectId={pid}); DELETE RoleAssignments WHERE ProjectId={pid}; DELETE ProjectTeams WHERE ProjectId={pid};
DELETE MatchingResults WHERE ProposalId IN(SELECT Id FROM Proposals WHERE ProjectId={pid}); DELETE Proposals WHERE ProjectId={pid}; DELETE RoleRequirements WHERE ProjectId={pid};
DELETE ActivityLogs WHERE ProjectId={pid}; DELETE Projects WHERE Id={pid} AND ClientId={cid}; COMMIT;""")
        print('PASS removed only the owned project/contract fixture; no money moved')
