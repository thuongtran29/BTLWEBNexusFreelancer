"""Team rounding/races, disputes, expiry, overdue reminders, profiles and search.
Run after V3FlowE2E using its printed --fixture-run. Local development DB only.
All money and bank references below are disposable test fixtures.
"""
import argparse, hashlib, re, uuid
from concurrent.futures import ThreadPoolExecutor
from urllib.parse import quote
import requests, urllib3
from SmokeE2E import Smoke, route_id, forms
from V3FlowE2E import sql, assert_eq
from ContractFixture import confirm_project, dispute_contract

def main():
    p=argparse.ArgumentParser();p.add_argument('--run-writes',action='store_true');p.add_argument('--fixture-run',required=True);a=p.parse_args()
    if not a.run_writes or not re.fullmatch('[a-f0-9]{12}',a.fixture_run):p.error('explicit write flag and valid run required')
    urllib3.disable_warnings();base='https://localhost:44391';smoke=Smoke(base);run=uuid.uuid4().hex[:10];fixture=a.fixture_run;pwd='NexusV3!'+fixture+'Aa7'
    def sess():s=requests.Session();s.verify=False;return s
    def post(s,path,fields,page):return smoke.post(s,path,fields,page)
    def get(s,path,status=200):return smoke.get(s,path,status)
    def check(label,fn):return smoke.check(label,fn)
    def login(address,role):
        s=sess();post(s,'/Account/Login',{'Email':address,'Role':role,'Password':pwd},'/Account/Login');return s
    address=f'v3-{fixture}@example.test';cid=int(sql(f"SELECT Id FROM Users WHERE Email='{address}' AND Role='Client'"));fid=int(sql(f"SELECT Id FROM Users WHERE Email='v3-worker-{fixture}@example.test' AND Role='Freelancer'"))
    aid=int(sql(f"SELECT Id FROM Users WHERE Email='v3-admin-{fixture}@example.test'"));sql(f"UPDATE Users SET Role='Admin' WHERE Id={aid}")
    try:
        client=login(address,'Client');leader=login(f'v3-worker-{fixture}@example.test','Freelancer');admin=login(f'v3-admin-{fixture}@example.test','Admin');guest=sess()
        def clone(suffix,verified=True):
            em=f'v3-extra-{run}-{suffix}@example.test';dt='GETUTCDATE()' if verified else 'NULL'
            uid=int(sql(f"INSERT Users(Name,Email,Role,PasswordHash,JoinedAtUtc,IsSuspended,EmailVerifiedAtUtc) SELECT 'V3 extra {suffix}','{em}','Freelancer',PasswordHash,GETUTCDATE(),0,{dt} FROM Users WHERE Id={fid}; SELECT SCOPE_IDENTITY();"))
            return em,uid
        bmail,bid=clone('b');cmail,tid=clone('c');memberb=login(bmail,'Freelancer');memberc=login(cmail,'Freelancer')
        post(leader,'/Profile/Update',{'CoreRole':'Backend Developer','Bio':'I build APIs, document integration boundaries and test SQL queries.','Industry':'Công nghệ thông tin','Location':'TP HCM UTC+7','YearsExperience':3,'MinimumProjectBudget':10000,'IsAvailable':'true','IsPublic':'true'},'/Profile')
        check('detailed profile persists',lambda:assert_eq(sql(f'SELECT YearsExperience FROM FreelancerProfiles WHERE UserAccountId={fid}'),'3'))
        post(client,'/ClientProfile',{'ClientType':'Business','CompanyName':'V3 Example Studio','Location':'Ha Noi UTC+7','About':'We develop scheduling products and review milestones weekly.','Website':'https://example.test'},'/ClientProfile')
        check('client business profile persists',lambda:assert_eq(sql(f"SELECT COUNT(*) FROM ClientProfiles WHERE UserAccountId={cid} AND CompanyName='V3 Example Studio'"),'1'))
        def project(title,kind,budget=10001,industry='Công nghệ thông tin',deadline='2027-02-01',count=1):
            r=post(client,'/Projects/Create',{'Title':title,'Description':'V3 detailed scope API integration, documentation and acceptance tests.','ProjectType':kind,'Industry':industry,'Budget':budget,'Deadline':deadline},'/Projects/Create');pid=route_id(r.headers['Location'])
            post(client,'/Projects/AddRequirement',{'ProjectId':pid,'Title':'Backend Developer','RequiredCount':count},f'/Projects/Manage/{pid}');post(client,f'/Projects/Publish/{pid}',{},f'/Projects/Manage/{pid}')
            rid=int(sql(f'SELECT Id FROM RoleRequirements WHERE ProjectId={pid}'));return pid,rid
        group=post(leader,'/Groups/Create',{'Name':'V3 team '+run,'Description':'Backend team for API and SQL delivery'},'/Groups');gid=int(re.search(r'/Details/(\d+)',group.headers['Location']).group(1))
        for em,person in [(bmail,memberb),(cmail,memberc)]:
            post(leader,'/Groups/Invite',{'GroupId':gid,'Email':em},f'/Groups/Details/{gid}');post(person,f'/Groups/Respond/{gid}',{'accept':'true'},'/Groups')
        pid,rid=project('Team '+run,'TEAM',count=3)
        forbidden=smoke.post(leader,'/Proposals/Apply',{'ProjectId':pid,'RoleRequirementId':rid,'Pitch':'Must be group','QuotedPrice':10001},f'/Projects/Details/{pid}',False)
        check('team blocks individual bids',lambda:assert_eq(forbidden.status_code,409))
        payload={'ProjectId':pid,'FreelancerGroupId':gid,'Pitch':'Three-member API delivery and review team'}
        for i,(uid,price) in enumerate([(fid,3333),(bid,3333),(tid,3335)]):payload.update({f'Candidates[{i}].Selected':'true',f'Candidates[{i}].UserAccountId':uid,f'Candidates[{i}].RoleRequirementId':rid,f'Candidates[{i}].QuotedPrice':price})
        post(leader,'/GroupBids/Create',payload,f'/GroupBids/Create?groupId={gid}&projectId={pid}')
        gb=int(sql(f'SELECT Id FROM GroupBids WHERE ProjectId={pid}'));post(client,f'/GroupBids/Decide/{gb}',{'accept':'true'},f'/Projects/Manage/{pid}');post(client,f'/Projects/LockTeam/{pid}',{},f'/Projects/Manage/{pid}')
        check('team contract snapshot has all shares',lambda:assert_eq(sql(f'SELECT COUNT(*) FROM EscrowShares WHERE ProjectId={pid}'),'3'))
        confirm_project(smoke,pid,client,leader,memberb,memberc)
        post(memberb,f'/Groups/Leave/{gid}',{},f'/Groups/Details/{gid}')
        check('active contract blocks member leaving',lambda:assert_eq(sql(f'SELECT Status FROM FreelancerGroupMembers WHERE FreelancerGroupId={gid} AND UserAccountId={bid}'),'Active'))
        def deposit(amount,ref):
            post(client,'/Wallet/RequestMoney',{'kind':'Deposit','amount':amount,'reference':ref,'details':'Disposable test deposit no real bank transaction'},'/Wallet');r=int(sql(f"SELECT Id FROM MoneyRequests WHERE Reference='{ref}'"));post(admin,f'/Wallet/ReviewMoney/{r}',{'approve':'true','reason':'Test fixture verification only'},'/Wallet/Admin')
        def parallel_post(s,path,fields,page):
            markup=get(s,page).text;token=next(f['fields']['__RequestVerificationToken'] for f in forms(markup) if '__RequestVerificationToken' in f['fields'])
            def send(_):
                ss=sess();ss.cookies.update(s.cookies);return ss.post(base+path,data={'__RequestVerificationToken':token,**fields},allow_redirects=False,timeout=60).status_code
            with ThreadPoolExecutor(max_workers=2) as pool:return list(pool.map(send,range(2)))
        deposit(10001,'v3-team-'+run);mid=int(sql(f'SELECT Id FROM EscrowMilestones WHERE ProjectId={pid}'))
        statuses=parallel_post(client,f'/Wallet/Fund/{mid}',{},f'/Wallet/Project/{pid}')
        check('concurrent hold returns controlled responses',lambda:assert_eq(all(x==302 for x in statuses),True))
        check('concurrent hold exactly one journal',lambda:assert_eq(sql(f"SELECT COUNT(*) FROM WalletEntries WHERE Reference='hold:{mid}'"),'1'))
        wid=int(sql(f'SELECT Id FROM Workspaces WHERE ProjectId={pid}'));post(leader,'/Workspace/Submit',{'WorkspaceId':wid,'EscrowMilestoneId':mid,'Title':'Three member deliverable','Url':'https://example.test/team'},f'/Workspace/Index/{wid}');did=int(sql(f'SELECT Id FROM Deliverables WHERE WorkspaceId={wid}'))
        statuses=parallel_post(client,f'/Workspace/Decide/{did}',{'decision':'Approved'},f'/Workspace/Index/{wid}')
        check('concurrent approval exactly one succeeds',lambda:assert_eq(sorted(statuses),[302,409]))
        check('team payouts conserve after rounding',lambda:assert_eq(sql(f"SELECT CONVERT(int,SUM(Amount)) FROM WalletEntries WHERE EscrowMilestoneId={mid} AND Kind='Payout'"),'9001'))
        check('rounding remainder goes to final member',lambda:assert_eq(sql(f"SELECT CONVERT(int,Amount) FROM WalletEntries WHERE EscrowMilestoneId={mid} AND Kind='Payout' AND UserAccountId={tid}"),'3003'))
        check('team journal includes three payouts',lambda:assert_eq(sql(f"SELECT COUNT(*) FROM WalletEntries WHERE EscrowMilestoneId={mid} AND Kind='Payout'"),'3'))
        def solo_contract(label):
            pp,rr=project(label+' '+run,'SOLO',budget=1000);post(leader,'/Proposals/Apply',{'ProjectId':pp,'RoleRequirementId':rr,'Pitch':'Solo detailed API implementation','QuotedPrice':1000},f'/Projects/Details/{pp}');pr=int(sql(f'SELECT Id FROM Proposals WHERE ProjectId={pp}'));post(client,f'/Projects/Accept/{pp}',{'proposalId':pr},f'/Projects/Manage/{pp}');post(client,f'/Projects/LockTeam/{pp}',{},f'/Projects/Manage/{pp}');confirm_project(smoke,pp,client,leader);deposit(1000,'v3-'+label+'-'+run);mm=int(sql(f'SELECT Id FROM EscrowMilestones WHERE ProjectId={pp}'));post(client,f'/Wallet/Fund/{mm}',{},f'/Wallet/Project/{pp}');ww=int(sql(f'SELECT Id FROM Workspaces WHERE ProjectId={pp}'));post(leader,'/Workspace/Submit',{'WorkspaceId':ww,'EscrowMilestoneId':mm,'Title':'API handover','Url':'https://example.test/deliverable'},f'/Workspace/Index/{ww}');dd=int(sql(f'SELECT Id FROM Deliverables WHERE WorkspaceId={ww}'));return pp,mm,ww,dd
        dp,dm,dw,dd=solo_contract('dispute');post(client,f'/Wallet/Dispute/{dp}',{'contractId':dispute_contract(dp),'reason':'Scope and acceptance criteria require administrator investigation'},f'/Wallet/Project/{dp}')
        blocked=smoke.post(client,f'/Workspace/Decide/{dd}',{'decision':'Approved'},f'/Workspace/Index/{dw}',False)
        check('dispute freezes approval',lambda:assert_eq(blocked.status_code,409))
        dispute=int(sql(f'SELECT Id FROM EscrowDisputes WHERE ProjectId={dp}'));post(admin,f'/Wallet/Resolve/{dispute}',{'refund':400,'reason':'Test partial settlement: customer refunded 400, remaining paid after fee'},'/Wallet/Admin')
        check('partial refund reconciles gross',lambda:assert_eq(sql(f"SELECT CONVERT(int,SUM(Amount)) FROM WalletEntries WHERE EscrowMilestoneId={dm} AND Kind IN ('Refund','Payout','PlatformFee')"),'1000'))
        check('fee charged only on non-refunded portion',lambda:assert_eq(sql(f'SELECT CONVERT(int,Fee) FROM EscrowMilestones WHERE Id={dm}'),'60'))
        ap,am,aw,ad=solo_contract('automatic');sql(f'UPDATE EscrowMilestones SET SubmittedAtUtc=DATEADD(day,-8,GETUTCDATE()) WHERE Id={am}');post(admin,'/Wallet/ProcessDue',{},'/Wallet/Admin')
        check('overdue escrow stays submitted',lambda:assert_eq(sql(f'SELECT Status FROM EscrowMilestones WHERE Id={am}'),'Submitted'))
        check('overdue handover awaits client decision',lambda:assert_eq(sql(f'SELECT Status FROM Deliverables WHERE Id={ad}'),'Submitted'))
        check('overdue funds remain held',lambda:assert_eq(sql(f'SELECT CONVERT(int,SUM(HeldDelta)) FROM WalletEntries WHERE EscrowMilestoneId={am}'),'1000'))
        check('reminder does not create financial entries',lambda:assert_eq(sql(f"SELECT COUNT(*) FROM WalletEntries WHERE EscrowMilestoneId={am} AND Kind IN ('Payout','PlatformFee','EscrowSettle')"),'0'))
        post(admin,'/Wallet/ProcessDue',{},'/Wallet/Admin')
        check('overdue reminder is idempotent',lambda:assert_eq(sql(f'SELECT COUNT(*) FROM MilestoneReviewReminders WHERE EscrowMilestoneId={am}'),'1'))
        check('client notified exactly once',lambda:assert_eq(sql(f"SELECT COUNT(*) FROM Notifications WHERE RecipientId={cid} AND TargetPath='/Wallet/Project/{ap}' AND Title LIKE N'%#{am}%'"),'1'))
        post(client,f'/Workspace/Decide/{ad}',{'decision':'Approved'},f'/Workspace/Index/{aw}')
        check('explicit approval settles overdue milestone',lambda:assert_eq(sql(f'SELECT Status FROM EscrowMilestones WHERE Id={am}'),'Released'))
        post(leader,'/ClientProfile/Review',{'projectId':ap,'rating':5,'comment':'Clear scope, prompt communication and funded milestones.'},f'/Wallet/Project/{ap}')
        check('freelancer can review completed client',lambda:assert_eq(sql(f'SELECT COUNT(*) FROM ClientReviews WHERE ProjectId={ap}'),'1'))
        post(leader,'/Profile/AddPortfolio',{'Title':'Verified participation '+run,'Summary':'Delivered documented API endpoints with tested SQL queries.','Url':'https://example.test/portfolio','Role':'Backend Developer','Skills':'MVC5, SQL Server','Outcome':'Documented delivery approved by client','ProjectId':ap},'/Profile')
        check('portfolio links actual completed participation',lambda:assert_eq(sql(f'SELECT COUNT(*) FROM PortfolioItems WHERE ProjectId={ap} AND FreelancerId={fid}'),'1'))
        post(memberb,'/Profile/AddPortfolio',{'Title':'Invalid participation '+run,'Summary':'Cannot claim participation in another project.','Url':'https://example.test/fake','ProjectId':ap},'/Profile')
        check('portfolio cannot claim outsider project',lambda:assert_eq(sql(f'SELECT COUNT(*) FROM PortfolioItems WHERE ProjectId={ap} AND FreelancerId={bid}'),'0'))
        # Boundary checks use a seeded hashed OTP, without claiming additional SMTP delivery.
        otpmail,otpuid=clone('otp',False);salt='A'*43;digest=hashlib.sha256((salt+'123456').encode('ascii')).hexdigest().upper();sql(f"INSERT RegistrationCodes(UserAccountId,Salt,CodeHash,CreatedAtUtc,ExpiresAtUtc,FailedAttempts) VALUES({otpuid},'{salt}','{digest}',GETUTCDATE(),DATEADD(minute,10,GETUTCDATE()),0)")
        otp=sess();page='/Account/ConfirmRegistration?email='+quote(otpmail)+'&role=Freelancer'
        for _ in range(6):smoke.post(otp,'/Account/ConfirmRegistration',{'Email':otpmail,'Role':'Freelancer','Code':'000000'},page,False)
        smoke.post(otp,'/Account/ConfirmRegistration',{'Email':otpmail,'Role':'Freelancer','Code':'123456'},page,False)
        check('OTP locks after five wrong attempts',lambda:assert_eq(sql(f'SELECT FailedAttempts FROM RegistrationCodes WHERE UserAccountId={otpuid}'),'5'))
        check('OTP locked correct code still denied',lambda:assert_eq(sql(f'SELECT COUNT(*) FROM Users WHERE Id={otpuid} AND EmailVerifiedAtUtc IS NOT NULL'),'0'))
        sql(f'UPDATE RegistrationCodes SET FailedAttempts=0,CreatedAtUtc=DATEADD(day,-1,GETUTCDATE()),ExpiresAtUtc=DATEADD(hour,-1,GETUTCDATE()) WHERE UserAccountId={otpuid}')
        smoke.post(otp,'/Account/ConfirmRegistration',{'Email':otpmail,'Role':'Freelancer','Code':'123456'},page,False)
        check('expired OTP denied',lambda:assert_eq(sql(f'SELECT COUNT(*) FROM Users WHERE Id={otpuid} AND EmailVerifiedAtUtc IS NOT NULL'),'0'))
        # Real published fixtures exercise combined filtering and alphabetical ordering.
        pa,_=project('Alpha filter-'+run,'SOLO',budget=100,deadline='2027-01-10');pz,_=project('Zeta filter-'+run,'SOLO',budget=200,deadline='2027-01-20');other,_=project('Other filter-'+run,'TEAM',budget=300,industry='Thiết kế & sáng tạo',count=2)
        page=get(guest,'/?q=filter-'+run+'&type=SOLO&minBudget=100&maxBudget=200&sort=az&deadlineFrom=2027-01-01&deadlineTo=2027-01-31')
        check('combined search filters exclude team',lambda:assert_eq('Other filter-'+run in page.text,False))
        check('alphabetical sort order',lambda:assert_eq(page.text.index('Alpha filter-'+run)<page.text.index('Zeta filter-'+run),True))
        page=get(guest,'/?q=filter-'+run+'&industry='+quote('Thiết kế & sáng tạo')+'&type=TEAM')
        check('industry filter isolates matching team job',lambda:assert_eq('Other filter-'+run in page.text and 'Alpha filter-'+run not in page.text,True))
        print(f'V3_ADDITIONAL_E2E_PASSED checks={len(smoke.rows)} run={run}')
    finally:sql(f"UPDATE Users SET Role='Client' WHERE Id={aid}")
if __name__=='__main__':main()
