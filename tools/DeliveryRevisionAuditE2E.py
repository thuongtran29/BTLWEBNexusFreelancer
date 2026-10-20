"""Revision round trip on the isolated audit database, never live money or external providers."""
import argparse,json,re
from pathlib import Path
from html import unescape
import urllib3
from PlatformJourneyE2E import Journey

def run(j):
    failures=[]
    def verify(ok,label):
        if ok:j.check(True,label)
        else:failures.append(label);print('FAIL '+label,flush=True)
    client=j.login('client.minhan','Client');worker=j.login('linh.backend','Freelancer');outside=j.login('vy.translation','Freelancer')
    p=j.post(client,'/Projects/Create',{'Title':j.tag+' bản sửa','Description':'Xây API đặt lịch với kiểm thử, chia hai mốc độc lập và nghiệm thu từng phần.','ProjectType':'SOLO','Industry':'Công nghệ thông tin','Budget':1000000},form='/Projects/Create')
    pid=int(re.search(r'/Projects/Manage/(\d+)',p.url).group(1));j.post(client,'/Projects/AddRequirement',{'ProjectId':pid,'Title':'Backend Developer','RequiredCount':1},form=f'/Projects/Manage/{pid}');rid=j.sql(f'SELECT Id FROM dbo.RoleRequirements WHERE ProjectId={pid}');j.post(client,f'/Projects/Publish/{pid}',form=f'/Projects/Manage/{pid}')
    plan=json.dumps([{'Title':'API bản đầu','Amount':250000,'DueDay':3,'AcceptanceCriteria':'Kiểm tra được API và trường hợp nhập sai.'},{'Title':'API hoàn thiện','Amount':250000,'DueDay':7,'AcceptanceCriteria':'Tài liệu và kiểm thử đầy đủ theo yêu cầu.'}],ensure_ascii=False)
    j.post(worker,'/Proposals/Apply',{'ProjectId':pid,'RoleRequirementId':rid,'Pitch':'Bàn giao API hai mốc có kiểm thử và tài liệu.','QuotedPrice':500000,'DeliveryDays':7,'MilestonePlan':plan},form=f'/Projects/Details/{pid}')
    proposal=j.sql(f'SELECT Id FROM dbo.Proposals WHERE ProjectId={pid}');j.post(client,f'/Projects/Accept/{pid}',{'proposalId':proposal},form=f'/Projects/Manage/{pid}')
    cid=j.sql(f'SELECT Id FROM dbo.Contracts WHERE ProjectId={pid}');wid=j.sql(f'SELECT Id FROM dbo.Workspaces WHERE ContractId={cid}');version,digest=j.sql(f"SELECT CAST(Id AS varchar)+'|'+Hash FROM dbo.ContractVersions WHERE ContractId={cid}").split('|')
    for s in [client,worker]:j.post(s,f'/Contracts/Confirm/{cid}',{'versionId':version,'hash':digest},form=f'/Contracts/Details/{cid}')
    milestones=j.sql(f'SELECT Id FROM dbo.EscrowMilestones WHERE ContractId={cid} ORDER BY Id').splitlines();m1,m2=milestones
    for m in milestones:j.post(client,'/Wallet/Fund/'+m,form=f'/Contracts/Details/{cid}')
    j.post(worker,'/Workspace/Submit',{'WorkspaceId':wid,'Title':'API bản đầu','Url':'https://example.com/v1','EscrowMilestoneId':m1},form=f'/Workspace/Index/{wid}')
    did=j.sql(f'SELECT Id FROM dbo.Deliverables WHERE WorkspaceId={wid}');ledger=j.sql(f'SELECT COUNT(*) FROM dbo.WalletEntries WHERE EscrowMilestoneId IN ({m1},{m2})')
    p=j.post(client,f'/Workspace/Decide/{did}',{'decision':'RevisionsRequested'},form=f'/Workspace/Index/{wid}')
    unchanged=j.sql(f'SELECT Status FROM dbo.Deliverables WHERE Id={did}')=='Submitted'
    verify(unchanged,'Revision request requires actionable feedback and cannot change status without it')
    if not unchanged:j.sql(f"UPDATE dbo.Deliverables SET Status='Submitted' WHERE Id={did};UPDATE dbo.EscrowMilestones SET Status='Submitted' WHERE Id={m1}")
    feedback='Bổ sung kiểm tra đầu vào ngày hẹn, trả thông báo rõ khi dữ liệu không hợp lệ.'
    j.post(client,f'/Workspace/Decide/{did}',{'decision':'RevisionsRequested','feedback':feedback},form=f'/Workspace/Index/{wid}')
    verify(j.sql(f'SELECT Status FROM dbo.Deliverables WHERE Id={did}')=='RevisionsRequested','Client can request revision with detailed feedback')
    verify(feedback in unescape(j.get(worker,f'/Workspace/Index/{wid}').text),'Freelancer sees exact revision feedback in workspace')
    verify('name="RevisesId"' in j.get(worker,f'/Workspace/Index/{wid}').text,'Solo owner gets explicit submit-revision form with historical link')
    j.post(worker,'/Workspace/Submit',{'WorkspaceId':wid,'Title':'Mốc hai bản đầu','Url':'https://example.com/second','EscrowMilestoneId':m2},form=f'/Workspace/Index/{wid}')
    second=j.sql(f'SELECT Id FROM dbo.Deliverables WHERE WorkspaceId={wid} AND EscrowMilestoneId={m2}')
    j.post(client,f'/Workspace/Decide/{second}',{'decision':'RevisionsRequested','feedback':'Bổ sung tài liệu đầy đủ cho API của mốc hai.'},form=f'/Workspace/Index/{wid}')
    p=j.post(worker,'/Workspace/Submit',{'WorkspaceId':wid,'Title':'Chuyển mốc trái phép','Url':'https://example.com/wrong','EscrowMilestoneId':m2,'RevisesId':did},form=f'/Workspace/Index/{wid}')
    wrong=j.sql(f'SELECT COUNT(*) FROM dbo.Deliverables WHERE RevisesId={did} AND EscrowMilestoneId={m2}')
    verify(wrong=='0' and p.status_code in (400,409),'Revision cannot switch to another funded milestone')
    if wrong!='0':j.sql(f"DELETE dbo.Deliverables WHERE RevisesId={did} AND EscrowMilestoneId={m2};UPDATE dbo.EscrowMilestones SET Status='RevisionsRequested' WHERE Id={m2}")
    j.post(worker,'/Workspace/Submit',{'WorkspaceId':wid,'Title':'API bản sửa sau phản hồi','Url':'https://example.com/v2','EscrowMilestoneId':m1,'RevisesId':did},form=f'/Workspace/Index/{wid}')
    latest=j.sql(f'SELECT Id FROM dbo.Deliverables WHERE RevisesId={did}')
    verify(bool(latest),'Correct revision links to prior deliverable in same milestone')
    p=j.post(client,f'/Workspace/Decide/{did}',{'decision':'Approved'},form=f'/Workspace/Index/{wid}');verify(p.status_code==409,'Superseded deliverable cannot be accepted for payment')
    verify(j.sql(f'SELECT COUNT(*) FROM dbo.WalletEntries WHERE EscrowMilestoneId IN ({m1},{m2})')==ledger,'Requesting and resubmitting revision never releases money')
    if latest:
        j.post(client,f'/Workspace/Decide/{latest}',{'decision':'Approved'},form=f'/Workspace/Index/{wid}')
        verify(j.sql(f'SELECT Status FROM dbo.EscrowMilestones WHERE Id={m1}')=='Released' and j.sql(f'SELECT Status FROM dbo.EscrowMilestones WHERE Id={m2}')=='RevisionsRequested','Acceptance releases revised milestone only, next milestone remains held')
    verify(j.post(outside,'/Workspace/Submit',{'WorkspaceId':wid,'Title':'Unauthorized','Url':'https://example.com/no','EscrowMilestoneId':m2},form='/Dashboard').status_code==403,'Unrelated account cannot submit a deliverable')
    result={'checks':j.checks,'failures':failures,'projectId':pid,'contractId':cid,'workspaceId':wid};print('REVISION_AUDIT_FAILURES='+str(len(failures)),flush=True);return result
if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--base',required=True);p.add_argument('--database',required=True);p.add_argument('--result',required=True);a=p.parse_args();urllib3.disable_warnings();r=run(Journey(a.base,a.database));Path(a.result).write_text(json.dumps(r,ensure_ascii=False,indent=2),encoding='utf-8');raise SystemExit(1 if r['failures'] else 0)
