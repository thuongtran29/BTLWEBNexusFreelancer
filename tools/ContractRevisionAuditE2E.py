import argparse,json,re
from pathlib import Path
from datetime import datetime,timedelta,timezone
import urllib3
from PlatformJourneyE2E import Journey

def run(j):
 c=j.login('client.minhan','Client');w=j.login('linh.backend','Freelancer');other=j.login('client.thuha','Client');a=j.login('admin','Admin')
 p=j.post(c,'/Projects/Create',{'Title':j.tag+' xác nhận phiên bản','Description':'Phạm vi API quản lý đặt lịch, kiểm thử đầu vào và tài liệu bàn giao rõ ràng.','ProjectType':'SOLO','Industry':'Công nghệ thông tin','Budget':1000000},form='/Projects/Create')
 pid=int(re.search(r'/Projects/Manage/(\d+)',p.url).group(1))
 j.post(c,'/Projects/AddRequirement',{'ProjectId':pid,'Title':'Backend Developer','RequiredCount':1},form=f'/Projects/Manage/{pid}');rid=j.sql(f'SELECT Id FROM dbo.RoleRequirements WHERE ProjectId={pid}')
 j.sql(f'UPDATE dbo.Projects SET DeadlineUtc=DATEADD(day,-1,GETUTCDATE()) WHERE Id={pid}')
 j.post(c,f'/Projects/Publish/{pid}',form=f'/Projects/Manage/{pid}')
 j.check(j.sql(f'SELECT Status FROM dbo.Projects WHERE Id={pid}')=='Draft','Past-dated draft cannot be published')
 future=(datetime.now(timezone.utc)+timedelta(days=30)).strftime('%Y-%m-%d')
 for session,label in [(w,'Freelancer'),(other,'Other Client'),(a,'Admin')]:
  p=j.post(session,f'/Projects/UpdateDeadline/{pid}',{'deadline':future},form='/Dashboard' if label!='Admin' else '/Admin')
  j.check(p.status_code==403 and j.sql(f'SELECT CASE WHEN DeadlineUtc<GETUTCDATE() THEN 1 ELSE 0 END FROM dbo.Projects WHERE Id={pid}')=='1',label+' cannot extend a project it does not own')
 j.post(c,f'/Projects/UpdateDeadline/{pid}',{'deadline':'not-a-date'},form=f'/Projects/Manage/{pid}')
 j.check(j.sql(f'SELECT CASE WHEN DeadlineUtc<GETUTCDATE() THEN 1 ELSE 0 END FROM dbo.Projects WHERE Id={pid}')=='1','Malformed deadline retains original value')
 today=datetime.now(timezone.utc).strftime('%Y-%m-%d')
 j.post(c,f'/Projects/UpdateDeadline/{pid}',{'deadline':today},form=f'/Projects/Manage/{pid}');j.post(c,f'/Projects/Publish/{pid}',form=f'/Projects/Manage/{pid}')
 j.post(w,'/Proposals/Apply',{'ProjectId':pid,'RoleRequirementId':rid,'Pitch':'Bàn giao đủ API và kiểm thử đã thống nhất.','QuotedPrice':500000,'DeliveryDays':7},form=f'/Projects/Details/{pid}')
 prop=j.sql(f'SELECT Id FROM dbo.Proposals WHERE ProjectId={pid}')
 j.check(bool(prop),'Deadline includes all of the selected calendar day')
 j.post(c,f'/Projects/Accept/{pid}',{'proposalId':prop},form=f'/Projects/Manage/{pid}')
 cid=j.sql(f'SELECT Id FROM dbo.Contracts WHERE ProjectId={pid}');wid=j.sql(f'SELECT Id FROM dbo.Workspaces WHERE ContractId={cid}')
 v1,h1=j.sql(f"SELECT CAST(Id AS varchar)+'|'+Hash FROM dbo.ContractVersions WHERE ContractId={cid}").split('|')
 j.post(w,f'/Contracts/Confirm/{cid}',{'versionId':v1,'hash':h1},form=f'/Contracts/Details/{cid}')
 plan=json.dumps([{'Title':'API và kiểm thử','Amount':500000,'DueDay':10,'AcceptanceCriteria':'Đủ API đặt lịch, validation và tài liệu kiểm thử.'}],ensure_ascii=False)
 j.post(c,f'/Contracts/Revise/{cid}',{'scope':'API đặt lịch bổ sung kiểm tra trùng ngày hẹn, kiểm thử nhập sai và hướng dẫn vận hành.','milestonePlan':plan,'deliveryDays':10},form=f'/Contracts/Details/{cid}')
 v2,h2=j.sql(f"SELECT TOP 1 CAST(Id AS varchar)+'|'+Hash FROM dbo.ContractVersions WHERE ContractId={cid} ORDER BY VersionNumber DESC").split('|')
 j.check(v1!=v2 and h1!=h2,'Revision creates a distinct version and hash')
 j.check(j.sql(f'SELECT COUNT(*) FROM dbo.ContractAcceptances WHERE ContractVersionId={v1}')=='1' and j.sql(f'SELECT COUNT(*) FROM dbo.ContractAcceptances WHERE ContractVersionId={v2}')=='0','Revision preserves old acceptance without carrying it forward')
 j.post(c,f'/Contracts/Confirm/{cid}',{'versionId':v1,'hash':h1},form=f'/Contracts/Details/{cid}')
 j.check(j.sql(f'SELECT COUNT(*) FROM dbo.ContractAcceptances WHERE ContractVersionId={v2}')=='0','Stale confirmation cannot sign revised terms')
 j.post(c,f'/Contracts/Confirm/{cid}',{'versionId':v2,'hash':h2},form=f'/Contracts/Details/{cid}')
 j.check(j.sql(f'SELECT Status FROM dbo.Contracts WHERE Id={cid}')=='AwaitingSignatures','One signature on revised terms does not enable funding')
 for _ in range(2):j.post(w,f'/Contracts/Confirm/{cid}',{'versionId':v2,'hash':h2},form=f'/Contracts/Details/{cid}')
 j.check(j.sql(f'SELECT Status FROM dbo.Contracts WHERE Id={cid}')=='AwaitingEscrow' and j.sql(f'SELECT COUNT(*) FROM dbo.ContractAcceptances WHERE ContractVersionId={v2}')=='2','Both parties must sign revised terms; retry does not duplicate acceptance')
 j.post(c,f'/Projects/UpdateDeadline/{pid}',{'deadline':future},form=f'/Projects/Manage/{pid}')
 j.check(j.sql(f'SELECT Hash FROM dbo.ContractVersions WHERE Id={v2}')==h2,'Project date update preserves signed contract hash')
 before=j.sql(f'SELECT COUNT(*) FROM dbo.ActivityLogs WHERE ProjectId={pid}')
 j.post(c,f'/Projects/UpdateDeadline/{pid}',{'deadline':future},form=f'/Projects/Manage/{pid}')
 j.check(j.sql(f'SELECT COUNT(*) FROM dbo.ActivityLogs WHERE ProjectId={pid}')==before,'Repeated same deadline produces no duplicate business activity')
 j.post(w,f'/Contracts/Decline/{cid}',{'reason':'Thời hạn mới chưa phù hợp để triển khai.'},form=f'/Contracts/Details/{cid}')
 j.check(j.post(w,'/Workspace/SendMessage',{'WorkspaceId':wid,'Body':'Đã kết thúc hợp đồng này.'},form=f'/Workspace/Index/{wid}').status_code==409,'Declined contract discussion stays read only')
 j.post(c,f'/Projects/ChangeStatus/{pid}',{'operation':'Cancel'},form=f'/Projects/Manage/{pid}')
 before=j.sql(f'SELECT CONVERT(varchar,DeadlineUtc,126) FROM dbo.Projects WHERE Id={pid}')
 p=j.post(c,f'/Projects/UpdateDeadline/{pid}',{'deadline':today},form=f'/Projects/Manage/{pid}')
 j.check(p.status_code==409 and j.sql(f'SELECT CONVERT(varchar,DeadlineUtc,126) FROM dbo.Projects WHERE Id={pid}')==before,'Canceled project cannot be reopened by changing its date')
 return {'checks':j.checks,'projectId':pid,'contractId':cid,'workspaceId':wid}
if __name__=='__main__':
 p=argparse.ArgumentParser();p.add_argument('--base',required=True);p.add_argument('--database',required=True);p.add_argument('--result',required=True);a=p.parse_args();urllib3.disable_warnings();r=run(Journey(a.base,a.database));Path(a.result).write_text(json.dumps(r,ensure_ascii=False,indent=2),encoding='utf-8')

