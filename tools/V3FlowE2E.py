"""V3 HTTP+SQL acceptance tests. Uses the explicitly configured local pickup mailbox.
Run against local development only: python tools/V3FlowE2E.py --run-writes --mailbox ABS_PATH
Never reads or prints production credentials or email codes.
"""
import argparse, email, html, re, subprocess, uuid
from pathlib import Path
from urllib.parse import urljoin
import requests, urllib3
from SmokeE2E import Smoke, forms, route_id, error_excerpt

SQLCMD=r'C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\180\Tools\Binn\SQLCMD.EXE'
def sql(q):
    p=subprocess.run([SQLCMD,'-S',r'lpc:.\SQLEXPRESS','-d','NexusFreelanceMvp','-E','-C','-I','-b','-h','-1','-W','-Q','SET NOCOUNT ON; '+q],capture_output=True,text=True,encoding='utf-8',errors='replace')
    if p.returncode: raise AssertionError(p.stdout+p.stderr)
    return p.stdout.strip()
def assert_eq(a,b):
    if a!=b: raise AssertionError(f'{a!r} != {b!r}')
def main():
    p=argparse.ArgumentParser();p.add_argument('--run-writes',action='store_true');p.add_argument('--mailbox',type=Path,required=True);p.add_argument('--base',default='https://localhost:44391');a=p.parse_args()
    if not a.run_writes: p.error('--run-writes is required')
    if not a.mailbox.is_absolute(): p.error('absolute mailbox required')
    urllib3.disable_warnings(); smoke=Smoke(a.base); run=uuid.uuid4().hex[:12]; password='NexusV3!'+run+'Aa7'; email_address=f'v3-{run}@example.test'
    def session():
        s=requests.Session();s.verify=False;return s
    def check(label,fn): return smoke.check(label,fn)
    def get(s,path,status=200):return smoke.get(s,path,status)
    def post(s,path,data,page):return smoke.post(s,path,data,page)
    def code_for(address,role):
        result=[]
        for path in a.mailbox.glob('*.eml'):
            msg=email.message_from_bytes(path.read_bytes())
            if address not in str(msg.get('To','')): continue
            payload=msg.get_payload(decode=True).decode('utf-8')
            if role in payload:
                m=re.search(r'\b(\d{6})\b',payload)
                if m:result.append((path.stat().st_mtime_ns,m.group(1)))
        if not result: raise AssertionError('No matching pickup email')
        return max(result)[1]
    def register(role,address=email_address):
        s=session();r=post(s,'/Account/Register',{'Name':'V3 '+role+' '+run,'Email':address,'Password':password,'Role':role},'/Account/Register')
        assert '/ConfirmRegistration' in r.headers['Location']
        return s,r.headers['Location']
    def verify(s,page,address,role,code):return post(s,'/Account/ConfirmRegistration',{'Email':address,'Role':role,'Code':code},page)
    def login(s,address,role):return post(s,'/Account/Login',{'Email':address,'Role':role,'Password':password},'/Account/Login')
    guest=session()
    weak=smoke.post(guest,'/Account/Register',{'Name':'Weak','Email':f'weak-{run}@example.test','Password':'Password123!','Role':'Client'},'/Account/Register',False)
    check('weak common password rejected',lambda:assert_eq(weak.status_code,200))
    check('weak account not persisted',lambda:assert_eq(sql(f"SELECT COUNT(*) FROM Users WHERE Email='weak-{run}@example.test'"),'0'))
    client,cpage=register('Client'); freelancer,fpage=register('Freelancer')
    check('same email two role records',lambda:assert_eq(sql(f"SELECT COUNT(*) FROM Users WHERE Email='{email_address}'"),'2'))
    blocked=smoke.post(client,'/Account/Login',{'Email':email_address,'Role':'Client','Password':password},'/Account/Login',False)
    check('unverified login enters OTP screen',lambda:assert_eq(blocked.status_code==302 and '/ConfirmRegistration' in blocked.headers.get('Location',''),True))
    check('unverified login creates no authenticated session',lambda:assert_eq(client.get(a.base+'/Dashboard',allow_redirects=False,timeout=25).status_code,302))
    client_code=code_for(email_address,'Client');free_code=code_for(email_address,'Freelancer')
    bad=smoke.post(client,'/Account/ConfirmRegistration',{'Email':email_address,'Role':'Client','Code':'999999' if client_code!='999999' else '111111'},cpage,False)
    check('incorrect code denied',lambda:assert_eq(bad.status_code,200))
    check('incorrect code attempts persisted',lambda:assert_eq(sql(f"SELECT MAX(c.FailedAttempts) FROM RegistrationCodes c JOIN Users u ON u.Id=c.UserAccountId WHERE u.Email='{email_address}' AND u.Role='Client'"),'1'))
    verify(client,cpage,email_address,'Client',client_code);verify(freelancer,fpage,email_address,'Freelancer',free_code)
    replay=smoke.post(client,'/Account/ConfirmRegistration',{'Email':email_address,'Role':'Client','Code':client_code},cpage,False)
    check('code replay denied',lambda:assert_eq(replay.status_code,200))
    login(client,email_address,'Client');login(freelancer,email_address,'Freelancer')
    check('client scope denied freelancer profile',lambda:get(client,'/Profile',403))
    check('freelancer scope denied create project',lambda:get(freelancer,'/Projects/Create',403))
    check('client dashboard and budget',lambda:get(client,'/Dashboard'))
    check('freelancer profile and role suggestions',lambda:assert_eq('role-suggestions' in get(freelancer,'/Profile').text,True))
    check('upgraded password storage',lambda:assert_eq(sql(f"SELECT COUNT(*) FROM Users WHERE Email='{email_address}' AND PasswordHash LIKE '600000:%'"),'2'))
    csrf=client.post(a.base+'/Wallet/RequestMoney',data={'kind':'Deposit','amount':10000,'reference':run,'details':'no csrf'},allow_redirects=False)
    check('wallet requires CSRF',lambda:assert_eq(csrf.status_code,400))
    check('google forged callback denied',lambda:get(guest,'/Account/GoogleCallback?state=forged&code=forged',400))
    check('google admin role denied',lambda:assert_eq(smoke.post(guest,'/Account/GoogleLogin',{'role':'Admin'},'/Account/Login',False).status_code,400))
    admin,apage=register('Client',f'v3-admin-{run}@example.test');verify(admin,apage,f'v3-admin-{run}@example.test','Client',code_for(f'v3-admin-{run}@example.test','Client'))
    admin_id=int(sql(f"SELECT Id FROM Users WHERE Email='v3-admin-{run}@example.test'"))
    sql(f"UPDATE Users SET Role='Admin' WHERE Id={admin_id}")
    try:
        login(admin,f'v3-admin-{run}@example.test','Admin')
        client_id=int(sql(f"SELECT Id FROM Users WHERE Email='{email_address}' AND Role='Client'"));free_id=int(sql(f"SELECT Id FROM Users WHERE Email='{email_address}' AND Role='Freelancer'"))
        # Preserve shared-email login checks above; contracting uses a different person.
        worker_email=f'v3-worker-{run}@example.test'
        freelancer,worker_page=register('Freelancer',worker_email)
        verify(freelancer,worker_page,worker_email,'Freelancer',code_for(worker_email,'Freelancer'))
        login(freelancer,worker_email,'Freelancer')
        free_id=int(sql(f"SELECT Id FROM Users WHERE Email='{worker_email}' AND Role='Freelancer'"))
        # Project creation, publishing, solo quote and acceptance are actual HTTP actions.
        pr=post(client,'/Projects/Create',{'Title':'V3 '+run,'Description':'API implementation and acceptance documentation for a real workflow','ProjectType':'SOLO','Industry':'Công nghệ thông tin','Budget':10000,'Deadline':'2027-01-15'},'/Projects/Create');pid=route_id(pr.headers['Location']);assert pid
        post(client,'/Projects/AddRequirement',{'ProjectId':pid,'Title':'Backend Developer','RequiredCount':1},f'/Projects/Manage/{pid}')
        post(client,f'/Projects/Publish/{pid}',{},f'/Projects/Manage/{pid}')
        role_id=int(sql(f'SELECT Id FROM RoleRequirements WHERE ProjectId={pid}'))
        post(freelancer,'/Proposals/Apply',{'ProjectId':pid,'RoleRequirementId':role_id,'Pitch':'I will deliver documented MVC API, SQL tests and deployment guide.','QuotedPrice':10000},f'/Projects/Details/{pid}')
        proposal=int(sql(f'SELECT Id FROM Proposals WHERE ProjectId={pid} AND ApplicantId={free_id}'))
        post(client,f'/Projects/Accept/{pid}',{'proposalId':proposal},f'/Projects/Manage/{pid}')
        post(client,f'/Projects/LockTeam/{pid}',{},f'/Projects/Manage/{pid}')
        check('contract waits for escrow',lambda:assert_eq(sql(f'SELECT Status FROM Projects WHERE Id={pid}'),'AwaitingEscrow'))
        wid=int(sql(f'SELECT Id FROM Workspaces WHERE ProjectId={pid}'))
        before=smoke.post(freelancer,'/Workspace/Submit',{'WorkspaceId':wid,'Title':'Early','Url':'https://example.test/early'},f'/Workspace/Index/{wid}',False)
        check('work before escrow denied',lambda:assert_eq(before.status_code,409))
        from ContractFixture import confirm_project
        confirm_project(smoke,pid,client,freelancer)
        mid=int(sql(f'SELECT Id FROM EscrowMilestones WHERE ProjectId={pid}'))
        post(client,f'/Wallet/Fund/{mid}',{},f'/Wallet/Project/{pid}')
        check('insufficient funds no hold',lambda:assert_eq(sql(f'SELECT Status FROM EscrowMilestones WHERE Id={mid}'),'Planned'))
        post(client,'/Wallet/RequestMoney',{'kind':'Deposit','amount':10000,'reference':'v3-deposit-'+run,'details':'Isolated test transfer; no actual bank payment'},'/Wallet')
        req=int(sql(f"SELECT Id FROM MoneyRequests WHERE Reference='v3-deposit-{run}'"))
        check('deposit unapproved no balance',lambda:assert_eq(sql(f'SELECT CONVERT(int,Balance) FROM WalletAccounts WHERE UserAccountId={client_id}'),'0'))
        post(admin,f'/Wallet/ReviewMoney/{req}',{'approve':'true','reason':'Verified test fixture only, no actual money'},'/Wallet/Admin')
        post(admin,f'/Wallet/ReviewMoney/{req}',{'approve':'true','reason':'Replay must not duplicate balance'},'/Wallet/Admin')
        check('deposit approved once',lambda:assert_eq(sql(f'SELECT CONVERT(int,Balance) FROM WalletAccounts WHERE UserAccountId={client_id}'),'10000'))
        post(client,f'/Wallet/Fund/{mid}',{},f'/Wallet/Project/{pid}');post(client,f'/Wallet/Fund/{mid}',{},f'/Wallet/Project/{pid}')
        check('fund moves balance to held once',lambda:assert_eq(sql(f'SELECT CONVERT(varchar(30),CONVERT(int,Balance))+\':\'+CONVERT(varchar(30),CONVERT(int,Held)) FROM WalletAccounts WHERE UserAccountId={client_id}'),'0:10000'))
        check('fund starts contract',lambda:assert_eq(sql(f'SELECT Status FROM Projects WHERE Id={pid}'),'InProgress'))
        submit={'WorkspaceId':wid,'Title':'API documented delivery','Url':'https://example.test/delivery','EscrowMilestoneId':mid}
        post(freelancer,'/Workspace/Submit',submit,f'/Workspace/Index/{wid}')
        did=int(sql(f'SELECT MAX(Id) FROM Deliverables WHERE WorkspaceId={wid}'))
        post(client,f'/Workspace/Decide/{did}',{'decision':'RevisionsRequested','feedback':'Bổ sung kiểm thử đầu vào và tài liệu bàn giao đúng phạm vi đã thống nhất.'},f'/Workspace/Index/{wid}')
        check('revision holds money',lambda:assert_eq(sql(f'SELECT CONVERT(int,Held) FROM WalletAccounts WHERE UserAccountId={client_id}'),'10000'))
        submit['RevisesId']=did;submit['Title']='Revised API delivery';post(freelancer,'/Workspace/Submit',submit,f'/Workspace/Index/{wid}')
        final=int(sql(f'SELECT MAX(Id) FROM Deliverables WHERE WorkspaceId={wid}'))
        post(client,f'/Workspace/Decide/{final}',{'decision':'Approved'},f'/Workspace/Index/{wid}')
        check('final approval completes project',lambda:assert_eq(sql(f'SELECT Status FROM Projects WHERE Id={pid}'),'Completed'))
        check('90 percent payout',lambda:assert_eq(sql(f'SELECT CONVERT(int,Balance) FROM WalletAccounts WHERE UserAccountId={free_id}'),'9000'))
        check('ten percent fee',lambda:assert_eq(sql(f'SELECT CONVERT(int,Fee) FROM EscrowMilestones WHERE Id={mid}'),'1000'))
        check('escrow exhausted',lambda:assert_eq(sql(f'SELECT CONVERT(int,Held) FROM WalletAccounts WHERE UserAccountId={client_id}'),'0'))
        again=smoke.post(client,f'/Workspace/Decide/{final}',{'decision':'Approved'},f'/Workspace/Index/{wid}',False)
        check('approval replay denied',lambda:assert_eq(again.status_code,409))
        post(freelancer,'/Wallet/RequestMoney',{'kind':'Withdrawal','amount':8000,'reference':'v3-withdraw-'+run,'details':'TEST BANK 123456 V3 freelancer fixture'},'/Wallet')
        post(freelancer,'/Wallet/RequestMoney',{'kind':'Withdrawal','amount':2000,'reference':'v3-overdraw-'+run,'details':'TEST overlapping pending withdrawal'},'/Wallet')
        check('pending withdrawal prevents overspend',lambda:assert_eq(sql(f"SELECT COUNT(*) FROM MoneyRequests WHERE Reference='v3-overdraw-{run}'"),'0'))
        wr=int(sql(f"SELECT Id FROM MoneyRequests WHERE Reference='v3-withdraw-{run}'"))
        post(admin,f'/Wallet/ReviewMoney/{wr}',{'approve':'true','reason':'Test payout confirmed no actual bank transfer'},'/Wallet/Admin')
        check('withdrawal deducted on approval',lambda:assert_eq(sql(f'SELECT CONVERT(int,Balance) FROM WalletAccounts WHERE UserAccountId={free_id}'),'1000'))
        outsider,op=register('Freelancer',f'v3-outsider-{run}@example.test');verify(outsider,op,f'v3-outsider-{run}@example.test','Freelancer',code_for(f'v3-outsider-{run}@example.test','Freelancer'));login(outsider,f'v3-outsider-{run}@example.test','Freelancer')
        check('escrow privacy outsider denied',lambda:get(outsider,f'/Wallet/Project/{pid}',403))
        check('wallet administration role denied',lambda:get(freelancer,'/Wallet/Admin',403))
        check('marketplace invalid range denied',lambda:get(guest,'/?minBudget=200&maxBudget=100',400))
        check('marketplace invalid sort denied',lambda:get(guest,'/?sort=unsafe',400))
        check('ledger reconciles wallet balances',lambda:assert_eq(sql('SELECT COUNT(*) FROM WalletAccounts w WHERE w.Balance<>COALESCE((SELECT SUM(e.BalanceDelta) FROM WalletEntries e WHERE e.UserAccountId=w.UserAccountId),0) OR w.Held<>COALESCE((SELECT SUM(e.HeldDelta) FROM WalletEntries e WHERE e.UserAccountId=w.UserAccountId),0)'),'0'))
        print(f'V3_FLOW_E2E_PASSED checks={len(smoke.rows)} run={run}',flush=True)
    finally:
        sql(f"UPDATE Users SET Role='Client' WHERE Id={admin_id}")
if __name__=='__main__':main()
