"""Local OTP, withdrawal, signed milestone metadata and simulated settlement regression.
Creates uniquely labelled test records and retains their financial audit history.
Only the explicitly authorized Admin fixture #838 is temporarily promoted.
No PSP, Google, SMTP or banking requests are made.
"""
import argparse,email,html,json,re,uuid
from pathlib import Path
from urllib.parse import urlsplit
import requests,urllib3
from SmokeE2E import Smoke,forms,route_id
from V3FlowE2E import sql

p=argparse.ArgumentParser();p.add_argument('--run-writes',action='store_true');p.add_argument('--mailbox',type=Path,required=True);p.add_argument('--result',type=Path,required=True);a=p.parse_args()
assert a.run_writes and a.mailbox.is_absolute() and a.mailbox.is_dir()
assert sql('SELECT DB_NAME()')=='NexusFreelanceMvp'
urllib3.disable_warnings();t=Smoke('https://localhost:44391');run=uuid.uuid4().hex[:12];address=f'repair-{run}@example.test';password='NexusFlow!'+run+'Aa7';checks=0
def check(ok,label):
 global checks
 assert ok,label
 checks+=1;print('PASS '+label,flush=True)
def actor():
 s=requests.Session();s.verify=False;s.trust_env=False;return s
def post(s,path,data,page=None,ok=True):return t.post(s,path,data,page,ok)
def login(s,mail,role,pwd=password):return post(s,'/Account/Login',{'Email':mail,'Role':role,'Password':pwd},'/Account/Login')
def latest(role):
 found=[]
 for path in a.mailbox.glob('*.eml'):
  message=email.message_from_bytes(path.read_bytes())
  if address.lower() not in str(message.get('To','')).lower():continue
  for part in message.walk():
   if part.get_content_type()!='text/plain':continue
   body=(part.get_payload(decode=True) or b'').decode(part.get_content_charset() or 'utf-8')
   match=re.search(r'vai trò '+role+r':\s*([0-9]{6})',body)
   if match:found.append((path.stat().st_mtime_ns,match.group(1)))
 assert found,'Owned fixture code not delivered to local pickup'
 return max(found)[1]
client,dual,worker,other,admin=[actor() for _ in range(5)]
check('data-mail-delivery="pickup"' in t.get(client,'/Account/Register').text,'test delivery is explicitly local pickup')
for s,role in [(client,'Client'),(dual,'Freelancer')]:
 result=post(s,'/Account/Register',{'Name':'Kiểm thử luồng '+run,'Email':address,'Password':password,'Role':role},'/Account/Register')
 check('/ConfirmRegistration' in result.headers['Location'],'registration enters OTP screen '+role)
check(sql(f"SELECT COUNT(*) FROM Users WHERE Email='{address}'")=='2','same email has two independent role records')
blocked=post(client,'/Account/Login',{'Email':address,'Role':'Client','Password':password},'/Account/Login',False)
check(blocked.status_code==302 and '/ConfirmRegistration' in blocked.headers.get('Location',''),'unverified login returns to OTP screen')
check(client.get(t.base+'/Dashboard',allow_redirects=False,timeout=30).status_code==302,'unverified login creates no authenticated session')
old=latest('Client');confirm='/Account/ConfirmRegistration?email='+address+'&role=Client'
bad=post(client,'/Account/ConfirmRegistration',{'Email':address,'Role':'Client','Code':'111111' if old!='111111' else '222222'},confirm,False)
check(bad.status_code==200,'wrong OTP is rejected')
post(client,'/Account/ResendVerification',{'Email':address,'Role':'Client'},'/Account/ResendVerification')
new=latest('Client')
check(sql(f"SELECT COUNT(*) FROM RegistrationCodes c JOIN Users u ON u.Id=c.UserAccountId WHERE u.Email='{address}' AND u.Role='Client' AND c.ConsumedAtUtc IS NULL")=='1','resend uses the same OTP flow and invalidates prior codes')
if old!=new:
 stale=post(client,'/Account/ConfirmRegistration',{'Email':address,'Role':'Client','Code':old},confirm,False);check(stale.status_code==200,'superseded OTP cannot verify')
for s,role,code in [(client,'Client',new),(dual,'Freelancer',latest('Freelancer'))]:
 result=post(s,'/Account/ConfirmRegistration',{'Email':address,'Role':role,'Code':code},'/Account/ConfirmRegistration?email='+address+'&role='+role)
 check('/Login' in result.headers['Location'],'correct OTP verifies '+role)
replay=post(client,'/Account/ConfirmRegistration',{'Email':address,'Role':'Client','Code':new},confirm,False);check(replay.status_code==200,'OTP replay cannot verify twice')
login(client,address,'Client');login(dual,address,'Freelancer')
fixture_password='NexusV3!f1ec4d5c91f3Aa7'
login(worker,'v3-worker-f1ec4d5c91f3@example.test','Freelancer',fixture_password)
login(other,'v3-f1ec4d5c91f3@example.test','Freelancer',fixture_password)
cid=int(sql(f"SELECT Id FROM Users WHERE Email='{address}' AND Role='Client'"));wid_user=839
def project(label,roles):
 result=post(client,'/Projects/Create',{'Title':'QA luồng '+run+' '+label,'Description':'Dự án kiểm thử localhost, tiền mô phỏng, lưu lịch sử và kiểm tra nghiệm thu theo hợp đồng. Không chuyển tiền ngân hàng.','Industry':'Công nghệ thông tin','ProjectType':'ENTERPRISE','Budget':'1500000'},'/Projects/Create')
 pid=route_id(result.headers['Location']);manage=f'/Projects/Manage/{pid}'
 for role in roles:post(client,'/Projects/AddRequirement',{'ProjectId':pid,'Title':role,'RequiredCount':1},manage)
 post(client,f'/Projects/Publish/{pid}',{},manage)
 return pid,[int(x) for x in sql(f'SELECT Id FROM RoleRequirements WHERE ProjectId={pid} ORDER BY Id').splitlines()]
def apply(s,pid,rid,price,plan=''):
 return post(s,'/Proposals/Apply',{'ProjectId':pid,'RoleRequirementId':rid,'Pitch':'Bàn giao mã nguồn, hướng dẫn chạy và minh chứng kiểm thử theo tiêu chí đã thống nhất.','QuotedPrice':price,'DeliveryDays':14,'MilestonePlan':plan},f'/Projects/Details/{pid}')
withdraw_pid,roles=project('rút đề xuất',['Backend Developer'])
apply(worker,withdraw_pid,roles[0],1000000)
proposal=int(sql(f'SELECT Id FROM Proposals WHERE ProjectId={withdraw_pid}'))
check(worker.post(t.base+f'/Proposals/Withdraw/{proposal}',data={},allow_redirects=False,timeout=30).status_code==400,'withdrawal requires CSRF')
check(post(other,f'/Proposals/Withdraw/{proposal}',{},'/Proposals/Mine',False).status_code==404,'another freelancer cannot withdraw the proposal')
post(worker,f'/Proposals/Withdraw/{proposal}',{},'/Proposals/Mine')
check(sql(f'SELECT Status FROM Proposals WHERE Id={proposal}')=='Withdrawn','withdrawal persists state and keeps history')
check(post(worker,f'/Proposals/Withdraw/{proposal}',{},'/Proposals/Mine',False).status_code==409,'repeated withdrawal is a controlled conflict')
apply(worker,withdraw_pid,roles[0],1000000)
check(sql(f'SELECT COUNT(*) FROM Proposals WHERE ProjectId={withdraw_pid}')=='1','withdrawal does not allow duplicate application history')
pid,roles=project('hợp đồng độc lập',['Backend Developer','Frontend Developer'])
plan=[{'Title':'Bản mẫu API','Amount':400000,'DueDay':5,'AcceptanceCriteria':'API chạy đúng bộ kiểm thử đã được hai phía thống nhất.'},{'Title':'Bàn giao mã nguồn','Amount':600000,'DueDay':14,'AcceptanceCriteria':'Có mã nguồn, tài liệu và hướng dẫn triển khai đầy đủ.'}]
apply(worker,pid,roles[0],1000000,json.dumps(plan,ensure_ascii=False));apply(other,pid,roles[1],500000)
proposals=[int(x) for x in sql(f'SELECT Id FROM Proposals WHERE ProjectId={pid} ORDER BY Id').splitlines()]
for proposal in proposals:post(client,f'/Projects/Accept/{pid}',{'proposalId':proposal},f'/Projects/Manage/{pid}')
contracts=[int(x) for x in sql(f'SELECT Id FROM Contracts WHERE ProjectId={pid} ORDER BY Id').splitlines()];check(len(contracts)==2,'one project has two independent contracts')
ca,cb=contracts
for contract,person in [(ca,worker),(cb,other)]:
 for s in (person,client):
  page=t.get(s,f'/Contracts/Details/{contract}');form=[f for f in forms(page.text) if '/Contracts/Confirm/' in f['action']][0];post(s,form['action'],form['fields'])
check(sql(f"SELECT COUNT(*) FROM Contracts WHERE ProjectId={pid} AND Status='AwaitingEscrow'")=='2','both contracts require complete two-party confirmation')
milestones=[int(x) for x in sql(f'SELECT Id FROM EscrowMilestones WHERE ContractId={ca} ORDER BY Id').splitlines()]
check(sql(f'SELECT DueDay FROM EscrowMilestones WHERE Id={milestones[0]}')=='5','milestone deadline persists')
check(sql(f"SELECT COUNT(*) FROM EscrowMilestones WHERE ContractId={ca} AND LEN(AcceptanceCriteria)>10")=='2','acceptance criteria persist on both milestones')
check('Tiêu chí nghiệm thu' in html.unescape(t.get(client,f'/Contracts/Details/{ca}').text),'contract shows agreed milestone criteria')
workspace=int(sql(f'SELECT Id FROM Workspaces WHERE ContractId={ca}'))
early=post(worker,'/Workspace/Submit',{'WorkspaceId':workspace,'Title':'Too early','Url':'https://example.test/early','EscrowMilestoneId':milestones[0]},f'/Workspace/Index/{workspace}',False)
check(early.status_code==409,'delivery before funding is denied')
promoted=False
try:
 sql("SET XACT_ABORT ON; BEGIN TRAN; UPDATE Users SET Role='Admin' WHERE Id=838 AND Email='v3-admin-f1ec4d5c91f3@example.test' AND Role='Client' AND IsSuspended=0; IF @@ROWCOUNT<>1 THROW 51770,'Exact authorized fixture unavailable',1; COMMIT;");promoted=True
 login(admin,'v3-admin-f1ec4d5c91f3@example.test','Admin',fixture_password)
 post(client,'/Wallet/RequestMoney',{'kind':'Deposit','amount':1000000,'reference':'repair-'+run,'details':'Kiểm thử tiền mô phỏng localhost, không chuyển tiền thật.'},'/Wallet')
 request=int(sql(f"SELECT Id FROM MoneyRequests WHERE Reference='repair-{run}'"));post(admin,f'/Wallet/ReviewMoney/{request}',{'approve':'true','reason':'Chỉ duyệt khoản tiền mô phỏng cho fixture riêng của lần kiểm thử.'},'/Wallet/Admin')
 post(admin,f'/Wallet/ReviewMoney/{request}',{'approve':'true','reason':'Kiểm tra chống duyệt lặp.'},'/Wallet/Admin')
 check(sql(f'SELECT CONVERT(int,Balance) FROM WalletAccounts WHERE UserAccountId={cid}')=='1000000','deposit approval cannot credit twice')
finally:
 if promoted:sql("UPDATE Users SET Role='Client' WHERE Id=838 AND Email='v3-admin-f1ec4d5c91f3@example.test' AND Role='Admin';")
check(sql('SELECT Role FROM Users WHERE Id=838')=='Client','temporary authorized Admin privilege restored')
post(client,'/Workspace/CreateTask',{'WorkspaceId':workspace,'Title':'Kiểm tra và bàn giao API','AssigneeId':wid_user,'Priority':'Normal'},f'/Workspace/Index/{workspace}',False)
# A task before funding remains blocked. Finance and delivery are driven through actual HTTP actions.
for number,mid in enumerate(milestones):
 post(client,f'/Wallet/Fund/{mid}',{},f'/Contracts/Details/{ca}')
 check(sql(f'SELECT Status FROM EscrowMilestones WHERE Id={mid}')=='Funded','fund milestone '+str(number+1))
 check(sql(f'SELECT Status FROM Contracts WHERE Id={cb}')=='AwaitingEscrow','funding one contract leaves the other unchanged')
 post(worker,'/Workspace/Submit',{'WorkspaceId':workspace,'Title':'Bản bàn giao '+str(number+1),'Url':'https://example.test/repair/'+run+'/'+str(number),'EscrowMilestoneId':mid},f'/Workspace/Index/{workspace}')
 did=int(sql(f'SELECT TOP(1) Id FROM Deliverables WHERE EscrowMilestoneId={mid} ORDER BY Id DESC'))
 if number==0:
  post(client,f'/Workspace/Decide/{did}',{'decision':'RevisionsRequested','feedback':'Bổ sung kiểm thử đầu vào và tài liệu bàn giao đúng phạm vi đã thống nhất.'},f'/Workspace/Index/{workspace}')
  check(sql(f'SELECT Status FROM EscrowMilestones WHERE Id={mid}')=='RevisionsRequested','client can request a revision without release')
  post(worker,'/Workspace/Submit',{'WorkspaceId':workspace,'Title':'Bản sửa theo phản hồi','Url':'https://example.test/repair/'+run+'/revision','EscrowMilestoneId':mid,'RevisesId':did},f'/Workspace/Index/{workspace}')
  did=int(sql(f'SELECT TOP(1) Id FROM Deliverables WHERE EscrowMilestoneId={mid} ORDER BY Id DESC'))
 post(client,f'/Workspace/Decide/{did}',{'decision':'Approved'},f'/Workspace/Index/{workspace}')
 check(sql(f'SELECT Status FROM EscrowMilestones WHERE Id={mid}')=='Released','acceptance settles the exact milestone')
 repeated=post(client,f'/Workspace/Decide/{did}',{'decision':'Approved'},f'/Workspace/Index/{workspace}',False)
 check(repeated.status_code==409,'acceptance replay is denied')
check(sql(f'SELECT Status FROM Contracts WHERE Id={ca}')=='Completed','all milestones settle the first contract')
check(sql(f'SELECT Status FROM Contracts WHERE Id={cb}')=='AwaitingEscrow','second contract remains independent after first completes')
check(sql(f'SELECT CONVERT(int,Held) FROM WalletAccounts WHERE UserAccountId={cid}')=='0','no money remains held after final acceptance')
check(sql(f'SELECT CONVERT(int,SUM(Fee)) FROM EscrowMilestones WHERE ContractId={ca}')=='100000','fee conservation matches agreed ten percent')
post(client,f'/Projects/Complete/{pid}',{},f'/Projects/Manage/{pid}')
check(sql(f'SELECT Status FROM Projects WHERE Id={pid}')!='Completed','project cannot finish while another contract is pending')
check(sql('SELECT Role FROM Users WHERE Id=838')=='Client','Admin remains revoked at end')
a.result.parent.mkdir(parents=True,exist_ok=True);a.result.write_text(json.dumps({'run':run,'email':address,'clientId':cid,'projectId':pid,'withdrawalProjectId':withdraw_pid,'contracts':contracts,'workspace':workspace,'milestones':milestones,'checks':checks},ensure_ascii=False),encoding='utf-8')
print('WORKFLOW_REPAIR_E2E checks='+str(checks)+' PASS',flush=True)
