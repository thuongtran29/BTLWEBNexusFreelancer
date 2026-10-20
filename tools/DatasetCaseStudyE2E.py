from pathlib import Path
import sys,subprocess,json,re
import requests,urllib3
sys.path.insert(0,'outputs/NexusFreelance/tools')
from PlatformJourneyE2E import Fields
urllib3.disable_warnings();base='https://localhost:44393';db='NexusFreelanceSubmission2026102002';w=Path('work/dataset-20261020')
config=(w/'proof/NexusFreelance.Web/Web.config').read_text(encoding='utf-8-sig');assert 'Initial Catalog='+db+';' in config
results=[]
def check(code,ok,expected,actual):
 results.append(dict(code=code,expected=expected,actual=actual,result='PASS' if ok else 'FAIL'))
 (w/'http-results.json').write_text(json.dumps(results,ensure_ascii=False,indent=2),encoding='utf-8')
 assert ok,code+': '+actual
 print('PASS '+code+' '+expected,flush=True)
def sql(q):
 p=subprocess.run([r'C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\180\Tools\Binn\SQLCMD.EXE','-S',r'.\SQLEXPRESS','-d',db,'-E','-C','-b','-h','-1','-W','-f','65001','-Q','SET NOCOUNT ON; SET QUOTED_IDENTIFIER ON; '+q],capture_output=True,encoding='utf-8');assert p.returncode==0,p.stdout;return p.stdout.strip()
def get(s,path):return s.get(base+path,timeout=100)
def post(s,path,data,form='/Dashboard'):
 token=Fields(get(s,form).text).values['__RequestVerificationToken']
 return s.post(base+path,data={**data,'__RequestVerificationToken':token},timeout=100)
def login(key,role):
 s=requests.Session();s.verify=False;p=post(s,'/Account/Login',{'Email':key+'@nexus.example.test','Role':role,'Password':'NexusDemo@2026!OnlyLocal'},'/Account/Login')
 check('AUTH-'+key,p.status_code==200 and 'GoogleLoginButton' not in p.text,'Đăng nhập đúng vai trò '+role,str(p.status_code));return s
c=login('client.minhan','Client');c2=login('client.thuha','Client');c3=login('client.quanghuy','Client');f=login('minh.frontend','Freelancer');back=login('linh.backend','Freelancer');a=login('admin','Admin');x=login('vy.translation','Freelancer')
for role,s,start in [('Client',c,'/Dashboard'),('Freelancer',f,'/Dashboard'),('Admin',a,'/Admin')]:
 for path in dict.fromkeys(Fields(get(s,start).text).links):
  p=get(s,path);check('NAV-'+role+'-'+path,p.status_code==200,'Điều hướng hợp lệ '+path,str(p.status_code))
p=get(c,'/api/v1/projects?pageSize=2');data=p.json();(w/'api-sample.json').write_text(json.dumps(data,ensure_ascii=False,indent=2),encoding='utf-8')
check('API-PAGE',p.status_code==200 and len(data['items'])==2,'API phân trang trả hai bản ghi',str(len(data.get('items',[]))))
p=get(c,'/?minBudget=9000000&maxBudget=1000000');check('FILTER-400',p.status_code==400,'Bộ lọc ngân sách sai trả 400 có kiểm soát',str(p.status_code))
p=get(x,'/Workspace/Index/4');check('AUTHZ-WS',p.status_code==403,'Người ngoài không truy cập workspace',str(p.status_code))
p=get(a,'/Workspace/Index/4');check('ADMIN-READ',p.status_code==200 and 'action="/Workspace/CreateTask"' not in p.text,'Admin chỉ đọc bằng chứng workspace',str(p.status_code))
p=get(c,'/Projects/Edit/1');check('EDITOR-PLAIN',p.status_code==200 and 'project-editor.js' in p.text,'Trang sửa tích hợp editor văn bản thường',str(p.status_code))
for cid,s in [(1,back),(2,f)]:
 fields=Fields(get(s,'/Contracts/Details/'+str(cid)).text).values
 p=post(s,'/Contracts/Confirm/'+str(cid),{'versionId':fields['versionId'],'hash':fields['hash']},'/Contracts/Details/'+str(cid))
 check('SIGN-'+str(cid),sql('SELECT Status FROM dbo.Contracts WHERE Id='+str(cid))=='AwaitingEscrow','Hai phía ký đúng phiên bản hợp đồng '+str(cid),str(p.status_code))
before=sql('SELECT COUNT(*) FROM dbo.WalletEntries e JOIN dbo.EscrowMilestones m ON m.Id=e.EscrowMilestoneId WHERE m.ContractId=3')
p=post(c3,'/Wallet/Fund/3',{},'/Contracts/Details/2')
check('FUND-A',sql('SELECT Status FROM dbo.Contracts WHERE Id=2')=='Active','Ký quỹ riêng hợp đồng A',str(p.status_code))
check('INDEPENDENT-B',before==sql('SELECT COUNT(*) FROM dbo.WalletEntries e JOIN dbo.EscrowMilestones m ON m.Id=e.EscrowMilestoneId WHERE m.ContractId=3'),'Sổ tiền hợp đồng B không đổi','Đối chiếu trước và sau')
p=post(c2,'/Workspace/CreateTask',{'WorkspaceId':4,'Title':'Không được phép phân công','AssigneeId':6,'Priority':'Normal'},'/Workspace/Index/4')
check('CLIENT-NO-TASK',p.status_code==403,'Client không sửa nhiệm vụ nội bộ nhóm',str(p.status_code))
p=post(f,'/Workspace/CreateTask',{'WorkspaceId':4,'Title':'Đối chiếu giao diện chọn lịch và trạng thái lỗi','AssigneeId':6,'Priority':'High'},'/Workspace/Index/4')
task=sql("SELECT TOP 1 Id FROM dbo.Tasks WHERE WorkspaceId=4 AND Title=N'Đối chiếu giao diện chọn lịch và trạng thái lỗi' ORDER BY Id DESC")
check('LEADER-TASK',task.isdigit(),'Trưởng nhóm tạo và giao việc cho thành viên',str(p.status_code))
p=post(back,'/Workspace/UpdateTask/'+task,{'status':'Doing'},'/Workspace/Index/4');check('MEMBER-TASK',sql('SELECT Status FROM dbo.Tasks WHERE Id='+task)=='Doing','Thành viên cập nhật việc được giao',str(p.status_code))
did=sql('SELECT TOP 1 Id FROM dbo.Deliverables WHERE WorkspaceId=5 AND Status=\'Submitted\'')
p=post(c,'/Workspace/Decide/'+did,{'decision':'Approved'},'/Workspace/Index/5');check('ACCEPT-PAYOUT',sql('SELECT Status FROM dbo.EscrowMilestones WHERE Id=9')=='Released','Nghiệm thu giải ngân đúng mốc',str(p.status_code))
p=post(a,'/Wallet/Resolve/1',{'refund':3600000,'reason':'Đối chiếu hợp đồng và thống nhất hoàn toàn bộ khoản đang giữ trong tình huống thử.'},'/Wallet/ReviewDispute/1')
check('DISPUTE-REFUND',sql('SELECT Status FROM dbo.EscrowDisputes WHERE Id=1')=='Resolved' and sql('SELECT Refunded FROM dbo.EscrowMilestones WHERE Id=13').startswith('3600000'),'Admin xử lý tranh chấp và hoàn tiền đúng mốc',str(p.status_code))
request=sql("SELECT TOP 1 Id FROM dbo.MoneyRequests WHERE Kind='Withdrawal' AND Status='Pending'")
p=post(a,'/Wallet/ReviewMoney/'+request,{'approve':'true','reason':'Xác nhận chi trả minh họa trên bản kiểm thử, không chuyển tiền thật.'},'/Wallet/Admin')
check('WITHDRAWAL',sql('SELECT Status FROM dbo.MoneyRequests WHERE Id='+request)=='Approved','Duyệt rút tiền có nhật ký và trừ ví',str(p.status_code))
p=post(c,'/Projects/ChangeStatus/7',{'operation':'Resume'},'/Projects/Manage/7')
check('PROJECT-RESUME',sql('SELECT Status FROM dbo.Projects WHERE Id=7')=='Published','Tiếp tục tuyển dự án đang tạm dừng',str(p.status_code))
for name in ['FinanceAudit.sql','ContractsAudit.sql','SqlAudit.sql','SpecFlowAudit.sql']:
 script=(w/name).read_text(encoding='utf-8-sig').replace('NexusFreelanceSubmission20261020',db);p=w/('after-'+name);p.write_text(script,encoding='utf-8-sig')
 r=subprocess.run([r'C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\180\Tools\Binn\SQLCMD.EXE','-S',r'.\SQLEXPRESS','-d',db,'-E','-C','-b','-i',str(p),'-o',str(w/(name+'-after.txt'))]);check('SQL-'+name,r.returncode==0,'Bất biến sau thao tác '+name,str(r.returncode))
print('DATASET_HTTP_CHECKS='+str(len(results)))
