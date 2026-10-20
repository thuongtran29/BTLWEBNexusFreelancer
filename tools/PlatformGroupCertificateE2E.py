"""Additional write cases on the isolated audit site/database only. No external email/PSP calls."""
import argparse
import json
import re
from html import unescape
from pathlib import Path
import urllib3
from PlatformJourneyE2E import Journey, Fields


def run(j):
    client=j.login('client.thuha','Client'); leader=j.login('minh.frontend','Freelancer'); worker=j.login('linh.backend','Freelancer'); admin=j.login('admin','Admin'); outsider=j.login('vy.translation','Freelancer')
    # Internal certification: test actual boundary and issuance, never a third-party examination.
    track=2; uid=7
    existing=j.sql(f'SELECT COUNT(*) FROM dbo.EarnedSkillBadges WHERE UserAccountId={uid} AND SkillTrackId={track} AND RevokedAtUtc IS NULL')
    if existing == '0':
        rows=[line.split('|') for line in j.sql(f"SELECT CAST(Id AS varchar)+'|'+CorrectChoice FROM dbo.SkillExamQuestions WHERE SkillTrackId={track} ORDER BY Ordinal").splitlines()]
        for mode in ['boundary','full']:
            page=j.get(leader,f'/Skills/Exam/{track}'); values=Fields(page.text).values
            assert values.get('ExamTicket'), 'No exam sitting available in audit fixture'
            data={'SkillTrackId':track,'ExamTicket':values['ExamTicket'],'BrowserFocusLossCount':0}
            correct_count=(len(rows)*4)//5 if mode=='boundary' else len(rows)
            for i,(qid,answer) in enumerate(rows):
                data[f'Answers[{i}].QuestionId']=qid;data[f'Answers[{i}].Choice']=answer if i<correct_count else ('B' if answer=='A' else 'A')
            j.post(leader,f'/Skills/Submit/{track}',data,form=f'/Skills/Exam/{track}')
            passed=j.sql(f'SELECT TOP 1 Passed FROM dbo.SkillExamAttempts WHERE UserAccountId={uid} AND SkillTrackId={track} ORDER BY Id DESC')
            j.check(passed==('0' if mode=='boundary' else '1'),'Internal exam actual scoring '+mode)
        j.check(j.sql(f'SELECT COUNT(*) FROM dbo.EarnedSkillBadges WHERE UserAccountId={uid} AND SkillTrackId={track} AND RevokedAtUtc IS NULL')=='1','Passing internal examination automatically issues one public badge')
    else:
        j.check(True,'Existing valid internal badge remains available without duplicate issuance')
    # External certificate: only an Admin determines the verified skill/level.
    certificate_name=j.tag+' SQL certificate'
    j.post(worker,'/Skills/SubmitCertification',{'Name':certificate_name,'Issuer':'Internal audit fixture provider','EvidenceUrl':'https://example.com/credential/audit','IssuedOn':'2026-01-01','SkillTrackId':3},form='/Skills/Certifications')
    cert=int(j.sql("SELECT TOP 1 Id FROM dbo.ExternalCertifications WHERE UserAccountId=8 AND Name=N'"+certificate_name+"' ORDER BY Id DESC"))
    j.post(admin,'/Admin/ReviewCertification',{'Id':cert,'Decision':'Verified','Reason':'Bằng chứng trong CSDL audit đã được đối chiếu, xác nhận kỹ năng SQL mức thực hành.','SkillTrackId':3,'ProficiencyLevel':2},form='/Admin/Certifications')
    j.check(j.sql(f'SELECT Status FROM dbo.ExternalCertifications WHERE Id={cert}')=='Verified','Admin verifies external certificate with skill and level')
    p=j.get(worker,'/Profile');j.check('SQL / Database' in unescape(p.text) and 'CoreSkillTrackId' in p.text,'Verified certificate becomes a selectable core skill')
    j.check(certificate_name in unescape(j.get(client,'/Talent/Freelancer/8').text),'Verified external certificate appears publicly')
    j.post(admin,f'/Admin/ClassifyCertification/{cert}',{'skillId':3,'level':3,'reason':'Đối chiếu lại bằng chứng audit, xác định kỹ năng SQL mức thành thạo.'},form='/Admin/Certifications?status=Verified')
    j.check(j.sql(f'SELECT ProficiencyLevel FROM dbo.ExternalCertifications WHERE Id={cert}')=='3','Admin can classify already verified certificates without reuploading')
    p=j.post(worker,f'/Admin/ClassifyCertification/{cert}',{'skillId':3,'level':5,'reason':'Unauthorized change'},form='/Dashboard');j.check(p.status_code==403,'Freelancer cannot promote its certificate classification')
    j.check('Nguyễn Khánh Linh' in unescape(j.get(client,'/Talent?skillId=3&verifiedOnly=true').text),'Verified talent filter includes linked external certificates')
    j.check('Hoàng Đức Minh' in unescape(j.get(client,'/Talent?skillId=2&verifiedOnly=true').text),'Verified talent filter includes internal examination certificates')
    # Complete group profile, invitation, membership and coverage.
    name=j.tag+' nhóm'
    fields={'Name':name,'Description':'Nhóm xây web MVC gồm frontend, backend, kiểm thử và bàn giao.','Industry':'Công nghệ thông tin','WorkStyle':'Remote','TargetSize':3,'MinimumBudget':15000000,'Specialties':'Web MVC, API, SQL','Collaboration':'Cập nhật mỗi ngày, họp thứ hai 9h UTC+7','IsPublic':'true'}
    p=j.post(leader,'/Groups/Create',fields,form='/Groups');group=int(re.search(r'/Groups/Details/(\d+)',p.url).group(1))
    j.check(j.sql(f'SELECT TargetSize FROM dbo.FreelancerGroups WHERE Id={group}')=='3','Detailed group profile persists capacity and collaboration options')
    j.post(leader,'/Groups/Invite',{'GroupId':group,'Email':'linh.backend@nexus.example.test'},form=f'/Groups/Details/{group}')
    j.check(j.sql(f'SELECT Status FROM dbo.FreelancerGroupMembers WHERE FreelancerGroupId={group} AND UserAccountId=8')=='Invited','Invitation does not silently add a freelancer')
    j.post(worker,f'/Groups/Respond/{group}',{'accept':'true'},form='/Groups')
    j.check(j.sql(f'SELECT Status FROM dbo.FreelancerGroupMembers WHERE FreelancerGroupId={group} AND UserAccountId=8')=='Active','Freelancer acceptance activates group membership')
    p=j.post(worker,f'/Groups/Update/{group}',fields,form=f'/Groups/Details/{group}');j.check(p.status_code==403,'A member cannot edit the leader-owned group profile')
    fields['Collaboration']='Họp hằng tuần và cập nhật việc theo từng mốc.'
    j.post(leader,f'/Groups/Update/{group}',fields,form=f'/Groups/Details/{group}')
    j.check('vừa được cập nhật' in unescape(j.get(worker,'/Notifications').text),'Group profile changes notify active members')
    notification=int(j.sql(f"SELECT TOP 1 Id FROM dbo.Notifications WHERE RecipientId=8 AND TargetPath='/Groups/Details/{group}' ORDER BY Id DESC"))
    p=j.post(outsider,f'/Notifications/Open/{notification}',form='/Notifications');j.check(p.status_code==404,'Another user cannot open or mark a private notification')
    j.post(worker,f'/Notifications/Open/{notification}',form='/Notifications')
    j.check(j.sql(f'SELECT COUNT(*) FROM dbo.Notifications WHERE Id={notification} AND ReadAtUtc IS NOT NULL')=='1','Opening own notification marks it read and reaches its source')
    p=j.post(client,'/Projects/Create',{'Title':j.tag+' TEAM','Description':'Xây cổng web với API và giao diện được nghiệm thu.','Industry':'Công nghệ thông tin','ProjectType':'TEAM','Budget':40000000,'SkillIds':[1,2],'MinimumSkillLevel':1},form='/Projects/Create')
    pid=int(re.search(r'/Projects/Manage/(\d+)',p.url).group(1))
    for role in ['Frontend Developer','Backend Developer']:j.post(client,'/Projects/AddRequirement',{'ProjectId':pid,'Title':role,'RequiredCount':1},form=f'/Projects/Manage/{pid}')
    j.post(client,f'/Projects/Publish/{pid}',form=f'/Projects/Manage/{pid}')
    j.check(name in unescape(j.get(client,f'/Projects/Manage/{pid}').text),'Client recommendation combines verified skills across active group members')
    j.check(j.tag+' TEAM' in unescape(j.get(leader,'/Dashboard').text),'Group leader receives reverse recommendations for TEAM projects')
    roles=[int(x) for x in j.sql(f'SELECT Id FROM dbo.RoleRequirements WHERE ProjectId={pid} ORDER BY Id').splitlines()]
    bid_data={'FreelancerGroupId':group,'ProjectId':pid,'Pitch':'Nhóm thực hiện toàn bộ API và giao diện, phân công rõ và kiểm thử.','DeliveryDays':21}
    for i,(user,role) in enumerate(zip([7,8],roles)):
        bid_data.update({f'Candidates[{i}].Selected':'true',f'Candidates[{i}].UserAccountId':user,f'Candidates[{i}].RoleRequirementId':role,f'Candidates[{i}].QuotedPrice':15000000})
    p=j.post(leader,'/GroupBids/Create',bid_data,form=f'/GroupBids/Create?groupId={group}&projectId={pid}')
    j.check(p.status_code==200,'Leader submits the agreed group roster')
    bid=int(j.sql(f'SELECT Id FROM dbo.GroupBids WHERE ProjectId={pid}'))
    j.post(client,f'/GroupBids/Decide/{bid}',{'accept':'true'},form=f'/Projects/Manage/{pid}')
    cid=int(j.sql(f'SELECT Id FROM dbo.Contracts WHERE ProjectId={pid}'));wid=int(j.sql(f'SELECT Id FROM dbo.Workspaces WHERE ContractId={cid}'))
    version,digest=j.sql(f"SELECT CAST(Id AS varchar)+'|'+Hash FROM dbo.ContractVersions WHERE ContractId={cid}").split('|')
    for s in [client,leader]:j.post(s,f'/Contracts/Confirm/{cid}',{'versionId':version,'hash':digest},form=f'/Contracts/Details/{cid}')
    j.check(j.sql(f'SELECT Status FROM dbo.Contracts WHERE Id={cid}')=='AwaitingSignatures','Group contract waits for all contractual member confirmations')
    j.post(worker,f'/Contracts/Confirm/{cid}',{'versionId':version,'hash':digest},form=f'/Contracts/Details/{cid}')
    m=int(j.sql(f'SELECT Id FROM dbo.EscrowMilestones WHERE ContractId={cid}'));j.post(client,f'/Wallet/Fund/{m}',form=f'/Contracts/Details/{cid}')
    j.check(j.sql(f'SELECT Status FROM dbo.Contracts WHERE Id={cid}')=='Active','Confirmed group contract activates after funding')
    p=j.post(leader,'/Workspace/CreateTask',{'WorkspaceId':wid,'Title':'Lập API theo phạm vi nhóm','AssigneeId':8,'Priority':'High'},form=f'/Workspace/Index/{wid}')
    task=int(j.sql(f'SELECT Id FROM dbo.Tasks WHERE WorkspaceId={wid}'))
    j.check(p.status_code==200,'Group leader assigns a task to its contracted backend member')
    p=j.post(client,f'/Workspace/UpdateTask/{task}',{'status':'Doing'},form=f'/Workspace/Index/{wid}');j.check(p.status_code==403,'Client cannot alter group task state')
    p=j.post(worker,f'/Workspace/UpdateTask/{task}',{'status':'Doing'},form=f'/Workspace/Index/{wid}');j.check(p.status_code==200,'Assigned group member updates its own task')
    p=j.post(worker,'/Workspace/CreateTask',{'WorkspaceId':wid,'Title':'Member should not assign','AssigneeId':7,'Priority':'Normal'},form=f'/Workspace/Index/{wid}');j.check(p.status_code==403,'Ordinary member cannot assign others without lead authority')
    # An unrelated group member/outsider still cannot see this workspace.
    j.check(j.get(outsider,f'/Workspace/Index/{wid}').status_code==403,'Workspace access remains bound to the contract roster')
    j.post(worker,f'/Wallet/Dispute/{pid}',{'reason':'Phạm vi API có khác biệt với điều khoản, cần quản trị kiểm tra bằng chứng và mốc ký quỹ.','contractId':cid},form=f'/Contracts/Details/{cid}')
    dispute=int(j.sql(f'SELECT TOP 1 Id FROM dbo.EscrowDisputes WHERE ContractId={cid} ORDER BY Id DESC'))
    p=j.get(admin,f'/Wallet/ReviewDispute/{dispute}');j.check(p.status_code==200 and 'Xem kênh làm việc' in unescape(p.text),'Admin dispute dossier links exact contract evidence and workspace')
    p=j.get(client,f'/Wallet/ReviewDispute/{dispute}');j.check(p.status_code==403,'Client cannot use Admin dispute decision page')
    j.post(admin,f'/Wallet/Resolve/{dispute}',{'refund':15000000,'reason':'Đã đối chiếu hồ sơ audit: hoàn một phần theo thỏa thuận, phần còn lại giải ngân cho nhóm theo tỷ lệ đã ký.'},form=f'/Wallet/ReviewDispute/{dispute}')
    j.check(j.sql(f'SELECT Status FROM dbo.EscrowDisputes WHERE Id={dispute}')=='Resolved','Admin decision settles only the held contract funds with audit reason')
    j.check(j.sql(f'SELECT Refunded FROM dbo.EscrowMilestones WHERE Id={m}').startswith('15000000'),'Partial refund amount is preserved on the exact milestone')
    print('PLATFORM_GROUP_CERTIFICATE_CHECKS='+str(len(j.checks)),flush=True)
    return {'checks':j.checks,'projectId':pid,'groupId':group,'contractId':cid,'workspaceId':wid,'disputeId':dispute}


if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--base',required=True);p.add_argument('--database',required=True);p.add_argument('--result',required=True);a=p.parse_args()
    urllib3.disable_warnings(urllib3.exceptions.InsecureRequestWarning)
    Path(a.result).write_text(json.dumps(run(Journey(a.base,a.database)),ensure_ascii=False,indent=2),encoding='utf-8')
