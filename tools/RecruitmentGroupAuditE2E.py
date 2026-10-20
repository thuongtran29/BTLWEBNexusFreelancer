"""Pending recruitment branches on the explicitly isolated audit instance only."""
import argparse,json,re
from pathlib import Path
import urllib3
from PlatformJourneyE2E import Journey

def run(j):
 c=j.login('client.minhan','Client');l=j.login('minh.frontend','Freelancer');w=j.login('linh.backend','Freelancer');x=j.login('vy.translation','Freelancer')
 def project(kind):
  p=j.post(c,'/Projects/Create',{'Title':j.tag+' '+kind,'Description':'Xây giao diện và API quản lý đặt lịch, có phân công và tiêu chí bàn giao.','ProjectType':kind,'Industry':'Công nghệ thông tin','Budget':2000000},form='/Projects/Create');pid=int(re.search(r'/Projects/Manage/(\d+)',p.url).group(1))
  for role in (['Frontend Developer','Backend Developer'] if kind=='TEAM' else ['Backend Developer']):j.post(c,'/Projects/AddRequirement',{'ProjectId':pid,'Title':role,'RequiredCount':1},form=f'/Projects/Manage/{pid}')
  j.post(c,f'/Projects/Publish/{pid}',form=f'/Projects/Manage/{pid}');return pid,j.sql(f'SELECT Id FROM dbo.RoleRequirements WHERE ProjectId={pid} ORDER BY Id').splitlines()
 pid,roles=project('SOLO')
 j.post(w,'/Proposals/Apply',{'ProjectId':pid,'RoleRequirementId':roles[0],'Pitch':'Triển khai đủ API với kiểm thử và tài liệu.','QuotedPrice':500000,'DeliveryDays':7},form=f'/Projects/Details/{pid}');prop=j.sql(f'SELECT Id FROM dbo.Proposals WHERE ProjectId={pid}')
 j.post(c,f'/Projects/Shortlist/{pid}',{'proposalId':prop},form=f'/Projects/Manage/{pid}')
 j.check(j.sql(f'SELECT Status FROM dbo.Proposals WHERE Id={prop}')=='Shortlisted','Client can shortlist a pending solo application')
 p=j.post(x,f'/Proposals/Withdraw/{prop}',form='/Proposals/Mine');j.check(p.status_code==404,'Another freelancer cannot withdraw an application')
 j.post(w,f'/Proposals/Withdraw/{prop}',form='/Proposals/Mine')
 j.check(j.sql(f'SELECT Status FROM dbo.Proposals WHERE Id={prop}')=='Withdrawn','Applicant can withdraw a shortlisted proposal')
 j.post(c,f'/Projects/Accept/{pid}',{'proposalId':prop},form=f'/Projects/Manage/{pid}')
 j.check(j.sql(f'SELECT COUNT(*) FROM dbo.Contracts WHERE ProjectId={pid}')=='0','Withdrawn proposal cannot be accepted into a contract')
 fields={'Name':j.tag+' vòng đời nhóm','Description':'Nhóm web frontend backend phân công rõ và bàn giao theo mốc.','Industry':'Công nghệ thông tin','WorkStyle':'Remote','TargetSize':3,'MinimumBudget':500000,'Specialties':'Web, API','Collaboration':'Cập nhật việc mỗi ngày','IsPublic':'true'}
 p=j.post(l,'/Groups/Create',fields,form='/Groups');gid=int(re.search(r'/Groups/Details/(\d+)',p.url).group(1))
 j.post(l,'/Groups/Invite',{'GroupId':gid,'Email':'linh.backend@nexus.example.test'},form=f'/Groups/Details/{gid}');j.post(w,f'/Groups/Respond/{gid}',{'accept':'true'},form='/Groups')
 def bid():
  pid,roles=project('TEAM');data={'FreelancerGroupId':gid,'ProjectId':pid,'Pitch':'Nhóm phân công giao diện và API, kiểm thử và bàn giao đầy đủ.','DeliveryDays':10}
  for i,(uid,rid) in enumerate(zip([7,8],roles)):data.update({f'Candidates[{i}].Selected':'true',f'Candidates[{i}].UserAccountId':uid,f'Candidates[{i}].RoleRequirementId':rid,f'Candidates[{i}].QuotedPrice':500000})
  j.post(l,'/GroupBids/Create',data,form=f'/GroupBids/Create?groupId={gid}&projectId={pid}');return pid,j.sql(f'SELECT Id FROM dbo.GroupBids WHERE ProjectId={pid}')
 gp,b=bid();proposal=j.sql(f'SELECT TOP 1 Id FROM dbo.Proposals WHERE GroupBidId={b}')
 j.post(c,f'/Projects/Shortlist/{gp}',{'proposalId':proposal},form=f'/Projects/Manage/{gp}')
 j.check(j.sql(f"SELECT COUNT(*) FROM dbo.Proposals WHERE GroupBidId={b} AND Status='Shortlisted'")=='2','Shortlisting group application updates its entire roster')
 j.check(j.post(w,f'/GroupBids/Withdraw/{b}',form='/Groups').status_code==403,'Only group leader can withdraw a collective bid')
 j.post(l,f'/GroupBids/Withdraw/{b}',form=f'/Groups/Details/{gid}')
 j.check(j.sql(f'SELECT Status FROM dbo.GroupBids WHERE Id={b}')=='Withdrawn' and j.sql(f"SELECT COUNT(*) FROM dbo.Proposals WHERE GroupBidId={b} AND Status='Withdrawn'")=='2','Leader withdrawal closes group bid and each member proposal')
 gp,b=bid();j.post(c,f'/Projects/ChangeStatus/{gp}',{'operation':'Cancel'},form=f'/Projects/Manage/{gp}')
 j.check(j.sql(f'SELECT Status FROM dbo.GroupBids WHERE Id={b}')=='Rejected' and j.sql(f"SELECT COUNT(*) FROM dbo.Proposals WHERE GroupBidId={b} AND Status='Rejected'")=='2','Project cancellation closes pending collective bid atomically')
 j.check(j.sql(f"SELECT COUNT(DISTINCT RecipientId) FROM dbo.Notifications WHERE ProjectId={gp} AND RecipientId IN (7,8) AND Title LIKE N'%hủy%'")=='2','Canceled collective application notifies every affected member')
 j.check(j.get(w,f'/Projects/Details/{gp}').status_code==200,'Group member retains canceled application history')
 gp,b=bid();j.post(c,f'/GroupBids/Decide/{b}',{'accept':'true'},form=f'/Projects/Manage/{gp}')
 j.post(w,f'/Groups/Leave/{gid}',form='/Groups')
 j.check(j.sql(f'SELECT Status FROM dbo.FreelancerGroupMembers WHERE FreelancerGroupId={gid} AND UserAccountId=8')=='Active','Contracted member cannot leave a group before contractual closure')
 j.post(l,f'/Groups/Remove/{gid}',{'memberId':8},form=f'/Groups/Details/{gid}')
 j.check(j.sql(f'SELECT Status FROM dbo.FreelancerGroupMembers WHERE FreelancerGroupId={gid} AND UserAccountId=8')=='Active','Leader cannot remove a contracted member to bypass signatures')
 return {'checks':j.checks,'groupId':gid,'projectId':gp,'contractId':j.sql(f'SELECT Id FROM dbo.Contracts WHERE ProjectId={gp}')}
if __name__=='__main__':
 p=argparse.ArgumentParser();p.add_argument('--base',required=True);p.add_argument('--database',required=True);p.add_argument('--result',required=True);a=p.parse_args();urllib3.disable_warnings();r=run(Journey(a.base,a.database));Path(a.result).write_text(json.dumps(r,ensure_ascii=False,indent=2),encoding='utf-8')
