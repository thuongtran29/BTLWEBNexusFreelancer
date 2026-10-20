"""Business branch regression on the explicitly isolated audit site only. No real PSP/email."""
import argparse,json,re
from pathlib import Path
from html import unescape
from datetime import datetime,timedelta,timezone
import urllib3
from PlatformJourneyE2E import Journey

def run(j):
    failures=[]
    def verify(ok,label):
        if ok:j.check(True,label)
        else:failures.append(label);print('FAIL '+label,flush=True)
    client=j.login('client.minhan','Client');worker=j.login('linh.backend','Freelancer');peer=j.login('minh.frontend','Freelancer');outside=j.login('vy.translation','Freelancer');admin=j.login('admin','Admin')
    def project(kind='SOLO'):
        p=j.post(client,'/Projects/Create',{'Title':j.tag+' '+kind,'Description':'Xây website quản lý đặt lịch và hồ sơ, có API, giao diện và kiểm thử bàn giao.','Industry':'Công nghệ thông tin','ProjectType':kind,'Budget':1000000},form='/Projects/Create')
        pid=int(re.search(r'/Projects/Manage/(\d+)',p.url).group(1))
        j.post(client,'/Projects/AddRequirement',{'ProjectId':pid,'Title':'Backend Developer','RequiredCount':2 if kind=='TEAM' else 1},form=f'/Projects/Manage/{pid}')
        rid=int(j.sql(f'SELECT Id FROM dbo.RoleRequirements WHERE ProjectId={pid}'))
        j.post(client,f'/Projects/Publish/{pid}',form=f'/Projects/Manage/{pid}')
        return pid,rid
    def apply(s,pid,rid):return j.post(s,'/Proposals/Apply',{'ProjectId':pid,'RoleRequirementId':rid,'Pitch':'Triển khai đúng phạm vi, kiểm thử đầy đủ và bàn giao tài liệu.','QuotedPrice':500000,'DeliveryDays':7},form=f'/Projects/Details/{pid}')
    pid,rid=project();apply(worker,pid,rid)
    j.post(client,f'/Projects/ChangeStatus/{pid}',{'operation':'Pause'},form=f'/Projects/Manage/{pid}')
    verify(j.get(worker,f'/Projects/Details/{pid}').status_code==200,'Applicant can read own project history while recruitment is paused')
    verify(j.get(outside,f'/Projects/Details/{pid}').status_code==404,'Unrelated freelancer cannot inspect paused project')
    j.post(client,f'/Projects/ChangeStatus/{pid}',{'operation':'Resume'},form=f'/Projects/Manage/{pid}')
    apply(peer,pid,rid)
    j.post(client,f'/Projects/ChangeStatus/{pid}',{'operation':'Cancel'},form=f'/Projects/Manage/{pid}')
    verify(j.sql(f"SELECT COUNT(*) FROM dbo.Proposals WHERE ProjectId={pid} AND Status IN ('Pending','Shortlisted')")=='0','Cancel project closes outstanding applications')
    verify(j.sql(f"SELECT COUNT(DISTINCT RecipientId) FROM dbo.Notifications WHERE ProjectId={pid} AND RecipientId IN (7,8) AND Title LIKE N'%hủy%'")=='2','Cancel project notifies each affected applicant')
    verify(j.get(worker,f'/Projects/Details/{pid}').status_code==200,'Applicant can inspect canceled project without 404 dead end')
    verify(j.get(outside,f'/Projects/Details/{pid}').status_code==404,'Canceled project stays private to unrelated users')
    expired,er=project();j.sql(f'UPDATE dbo.Projects SET DeadlineUtc=DATEADD(day,-1,GETUTCDATE()) WHERE Id={expired}')
    apply(worker,expired,er)
    verify(j.sql(f'SELECT COUNT(*) FROM dbo.Proposals WHERE ProjectId={expired}')=='0','Direct POST cannot apply to an expired project')
    verify('action="/Proposals/Apply"' not in j.get(worker,f'/Projects/Details/{expired}').text,'Expired project has no misleading application form')
    team,tr=project('TEAM');j.sql(f'UPDATE dbo.Projects SET DeadlineUtc=DATEADD(day,-1,GETUTCDATE()) WHERE Id={team}')
    verify(j.get(peer,f'/GroupBids/Create?groupId=1&projectId={team}').status_code!=200,'Expired project cannot open group application form')
    future=(datetime.now(timezone.utc)+timedelta(days=30)).strftime('%Y-%m-%d')
    p=j.post(client,f'/Projects/UpdateDeadline/{expired}',{'deadline':future},form=f'/Projects/Manage/{expired}')
    verify(p.status_code==200 and j.sql(f'SELECT CASE WHEN DeadlineUtc>GETUTCDATE() THEN 1 ELSE 0 END FROM dbo.Projects WHERE Id={expired}')=='1','Owner can extend expected project date without editing signed contract terms')
    # Contract preparation, communication, rejection and counterpart notification.
    cp,cr=project();apply(worker,cp,cr);proposal=j.sql(f'SELECT Id FROM dbo.Proposals WHERE ProjectId={cp}')
    j.post(client,f'/Projects/Accept/{cp}',{'proposalId':proposal},form=f'/Projects/Manage/{cp}')
    cid=int(j.sql(f'SELECT Id FROM dbo.Contracts WHERE ProjectId={cp}'));wid=int(j.sql(f'SELECT Id FROM dbo.Workspaces WHERE ContractId={cid}'))
    before=j.sql(f'SELECT COUNT(*) FROM dbo.WorkspaceMessages WHERE WorkspaceId={wid}')
    p=j.post(worker,'/Workspace/SendMessage',{'WorkspaceId':wid,'Body':'Cần làm rõ phạm vi trước khi xác nhận điều khoản.'},form=f'/Workspace/Index/{wid}')
    verify(p.status_code==200 and int(j.sql(f'SELECT COUNT(*) FROM dbo.WorkspaceMessages WHERE WorkspaceId={wid}'))==int(before)+1,'Parties can clarify terms in their workspace before signing')
    before=j.sql(f"SELECT COUNT(*) FROM dbo.Notifications WHERE RecipientId=4 AND TargetPath='/Contracts/Details/{cid}'")
    j.post(worker,f'/Contracts/Decline/{cid}',{'reason':'Phạm vi chưa phù hợp với năng lực và thời gian của tôi.'},form=f'/Contracts/Details/{cid}')
    verify(j.sql(f'SELECT Status FROM dbo.Contracts WHERE Id={cid}')=='Declined','Freelancer can decline unfunded contract and free the vacancy')
    verify(int(j.sql(f"SELECT COUNT(*) FROM dbo.Notifications WHERE RecipientId=4 AND TargetPath='/Contracts/Details/{cid}'"))>int(before),'Contract decline notifies the other party')
    # Work -> meeting -> dispute. Financial mutations are only in this audit DB.
    ap,ar=project();apply(worker,ap,ar);prop=j.sql(f'SELECT Id FROM dbo.Proposals WHERE ProjectId={ap}')
    j.post(client,f'/Projects/Accept/{ap}',{'proposalId':prop},form=f'/Projects/Manage/{ap}')
    ac=int(j.sql(f'SELECT Id FROM dbo.Contracts WHERE ProjectId={ap}'));aw=int(j.sql(f'SELECT Id FROM dbo.Workspaces WHERE ContractId={ac}'))
    version,digest=j.sql(f"SELECT CAST(Id AS varchar)+'|'+Hash FROM dbo.ContractVersions WHERE ContractId={ac}").split('|')
    for s in [client,worker]:j.post(s,f'/Contracts/Confirm/{ac}',{'versionId':version,'hash':digest},form=f'/Contracts/Details/{ac}')
    milestone=j.sql(f'SELECT Id FROM dbo.EscrowMilestones WHERE ContractId={ac}');j.post(client,'/Wallet/Fund/'+milestone,form=f'/Contracts/Details/{ac}')
    meet_before=j.sql(f"SELECT COUNT(*) FROM dbo.Notifications WHERE RecipientId=4 AND TargetPath='/Workspace/Index/{aw}'")
    p=j.post(worker,'/Workspace/ScheduleMeeting',{'WorkspaceId':aw,'Title':'Làm rõ bản mẫu và tiêu chí nghiệm thu','StartsAtUtc':(datetime.now(timezone.utc)+timedelta(days=1)).strftime('%Y-%m-%dT%H:%M'),'Url':'https://example.com/meeting'},form=f'/Workspace/Index/{aw}')
    verify(p.status_code==200 and int(j.sql(f"SELECT COUNT(*) FROM dbo.Notifications WHERE RecipientId=4 AND TargetPath='/Workspace/Index/{aw}'"))>int(meet_before),'Freelancer-scheduled meeting notifies its Client')
    j.post(worker,f'/Wallet/Dispute/{ap}',{'contractId':ac,'reason':'Yêu cầu hỗ trợ đối chiếu phạm vi và bằng chứng nghiệm thu của hợp đồng.'},form=f'/Contracts/Details/{ac}')
    p=j.post(client,'/Workspace/SendMessage',{'WorkspaceId':aw,'Body':'Bổ sung giải trình để quản trị có thể đối chiếu trong tranh chấp.'},form=f'/Workspace/Index/{aw}')
    verify(p.status_code==200 and j.sql(f"SELECT COUNT(*) FROM dbo.WorkspaceMessages WHERE WorkspaceId={aw} AND AuthorId=4")=='1','Disputed workspace keeps an evidence discussion channel open')
    verify(j.post(worker,'/Workspace/CreateTask',{'WorkspaceId':aw,'Title':'Không được sửa công việc khi tranh chấp','AssigneeId':8,'Priority':'Normal'},form=f'/Workspace/Index/{aw}').status_code==409,'Dispute still freezes task creation')
    verify(j.post(outside,'/Workspace/SendMessage',{'WorkspaceId':aw,'Body':'Không được xâm nhập'},form='/Dashboard').status_code==403,'Communication exception never grants access to outsiders')
    result={'checks':j.checks,'failures':failures,'fixtures':{'cancelProject':pid,'expiredProject':expired,'declinedContract':cid,'disputedContract':ac,'workspace':aw}}
    print('ROLE_BRANCH_CHECKS='+str(len(j.checks))+' FAILURES='+str(len(failures)),flush=True)
    return result

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--base',required=True);p.add_argument('--database',required=True);p.add_argument('--result',required=True);a=p.parse_args();urllib3.disable_warnings();r=run(Journey(a.base,a.database));Path(a.result).write_text(json.dumps(r,ensure_ascii=False,indent=2),encoding='utf-8');raise SystemExit(1 if r['failures'] else 0)
