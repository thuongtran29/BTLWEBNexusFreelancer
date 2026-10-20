"""JD, shared generic knowledge, position matching and group staffing. Isolated audit only."""
import argparse,json,re
from pathlib import Path
from html import unescape
import urllib3
from PlatformJourneyE2E import Journey,Fields

def run(j):
 c=j.login('client.minhan','Client');other=j.login('client.thuha','Client');w=j.login('linh.backend','Freelancer');l=j.login('minh.frontend','Freelancer');x=j.login('vy.translation','Freelancer');admin=j.login('admin','Admin')
 def latest_draft():return int(j.sql('SELECT TOP 1 Id FROM dbo.AIJobDrafts WHERE ClientId=4 ORDER BY Id DESC'))
 def generate(idea,shared=True):
  p=j.post(c,'/JobStudio/Generate',{'Idea':idea+' · '+j.tag,'Industry':'Công nghệ thông tin','ProjectType':'TEAM','Budget':40000000,'UseExternalAi':'false','UseProjectKnowledge':'false','UseSharedKnowledge':str(shared).lower()},form='/JobStudio/New')
  assert '/JobStudio/Edit/' in p.url,'JD generation did not reach editor: '+unescape(re.sub('<[^>]+>',' ',p.text))[-700:]
  return latest_draft(),p
 # Completed source fixture intentionally has private source/title; publish form starts empty.
 marker='PRIVATE-SOURCE-'+j.tag.split()[-1]
 p=j.post(c,'/Projects/Create',{'Title':marker,'Description':'Nội dung riêng '+marker+' cho lịch khám và đặt lịch, không được tự sao chép vào thư viện.','Industry':'Công nghệ thông tin','ProjectType':'SOLO','Budget':1000000},form='/Projects/Create');source=int(re.search(r'/Projects/Manage/(\d+)',p.url).group(1))
 j.sql(f"UPDATE dbo.Projects SET Status='Completed' WHERE Id={source}")
 j.post(c,'/ProjectMemory/Generate',{'projectId':source},form=f'/Projects/Manage/{source}')
 j.check(marker in unescape(j.get(c,'/Knowledge/Completed').text),'Client can inspect own completed projects and knowledge state')
 j.check(marker not in unescape(j.get(other,'/Knowledge/Completed').text),'Another Client completed-project list excludes private source')
 j.check(j.get(w,'/Knowledge/Completed').status_code==403,'Freelancer cannot browse Client private completed-project list')
 p=j.get(c,f'/Knowledge/Share?projectId={source}');j.check(Fields(p.text).values.get('Title')=='','Sharing form never auto-copies private project title into generic fields')
 generic={'ProjectId':source,'ExpectedVersion':0,'Title':'Bài học kiểm tra lịch hẹn '+j.tag.split()[-1],'Summary':'Kiểm tra luồng đặt lịch và xác nhận thời gian còn trống trước khi tiếp nhận lịch hẹn.','Practices':'Đối chiếu trùng lịch trước khi nhận yêu cầu và ghi rõ trạng thái khi người dùng đổi lịch.','AcceptanceChecklist':'Thử tạo hai lịch trùng, thay đổi thời gian và hủy lịch để kiểm tra trạng thái nhất quán.','SkillTags':'đặt lịch, API, kiểm thử'}
 j.post(c,'/Knowledge/Share',generic,form=f'/Knowledge/Share?projectId={source}')
 j.check(j.sql(f'SELECT COUNT(*) FROM dbo.SharedKnowledgeEntries WHERE ProjectId={source}')=='0','Generic sharing requires explicit confirmation')
 bad={**generic,'ConfirmGeneric':'true','Practices':generic['Practices']+' owner@example.test'}
 j.post(c,'/Knowledge/Share',bad,form=f'/Knowledge/Share?projectId={source}')
 j.check(j.sql(f'SELECT COUNT(*) FROM dbo.SharedKnowledgeEntries WHERE ProjectId={source}')=='0','Sharing rejects email/contact markers before publication')
 p=j.post(c,'/Knowledge/Share',{**generic,'ConfirmGeneric':'true'},form=f'/Knowledge/Share?projectId={source}');kid=int(re.search(r'/Knowledge/Details/(\d+)',p.url).group(1))
 text=unescape(j.get(w,f'/Knowledge/Details/{kid}').text)
 j.check(generic['Practices'] in text and marker not in text and 'owner@example.test' not in text,'Shared article exposes curated lessons without private project source')
 j.check(j.get(other,f'/Knowledge/Share?projectId={source}').status_code==403,'Another Client cannot author knowledge for someone else project')
 j.check(j.get(admin,'/Knowledge/Completed').status_code==200,'Admin can inspect completed-project inventory while private memory permissions remain separate')
 idea='Website phòng khám thú y có đặt lịch khám, hồ sơ thú cưng, xác nhận lịch hẹn và kiểm tra trùng lịch.'
 did,p=generate(idea)
 refs=j.sql(f'SELECT KnowledgeReferences FROM dbo.AIJobDraftVersions WHERE AIJobDraftId={did}')
 j.check(f'#{kid}' in refs and 'Memory #' not in refs,'JD records selected shared sources and respects private-memory opt-out')
 j.check('Đối chiếu trùng lịch' in unescape(p.text),'Local JD applies an actual relevant shared practice')
 j.check('data-role-match=' in p.text and 'Backend Developer' in p.text and 'Frontend Developer' in p.text,'JD editor automatically previews candidates by each hiring position')
 did2,p2=generate(idea)
 descriptions=j.sql(f'SELECT Description FROM dbo.AIJobDraftVersions WHERE AIJobDraftId IN ({did},{did2}) ORDER BY Id')
 a=j.sql(f'SELECT Description FROM dbo.AIJobDraftVersions WHERE AIJobDraftId={did}');b=j.sql(f'SELECT Description FROM dbo.AIJobDraftVersions WHERE AIJobDraftId={did2}')
 j.check(a!=b,'Repeated same brief stores a genuinely different drafting angle')
 shop,ps=generate('Website bán sản phẩm có giỏ hàng, cập nhật tồn kho và theo dõi trạng thái đơn hàng.',False)
 j.check(j.sql(f'SELECT Objectives FROM dbo.AIJobDraftVersions WHERE AIJobDraftId={shop}')!=j.sql(f'SELECT Objectives FROM dbo.AIJobDraftVersions WHERE AIJobDraftId={did}'),'Different project needs produce different goals and workflow')
 p=j.post(c,f'/JobStudio/Apply/{did}',form=f'/JobStudio/Edit/{did}');project=int(re.search(r'/Projects/Manage/(\d+)',p.url).group(1))
 j.check(j.sql(f'SELECT COUNT(*) FROM dbo.ProjectSkillRequirements WHERE ProjectId={project}')=='2','Applying JD automatically maps professional positions to catalog skills')
 roles=j.sql(f"SELECT CAST(Id AS varchar)+'|'+Title FROM dbo.RoleRequirements WHERE ProjectId={project} ORDER BY Id").splitlines();backend=int(next(v.split('|')[0] for v in roles if 'Backend' in v));front=int(next(v.split('|')[0] for v in roles if 'Frontend' in v))
 j.check(f'data-role-match="{backend}"' in p.text and f'data-role-match="{front}"' in p.text,'Created project retains separate position recommendation panels')
 j.post(c,f'/Projects/Publish/{project}',form=f'/Projects/Manage/{project}')
 j.check(j.sql(f'SELECT CASE WHEN PublishedAtUtc IS NOT NULL THEN 1 ELSE 0 END FROM dbo.Projects WHERE Id={project}')=='1','Project publication records an actual publication timestamp')
 home=j.get(w,'/');j.check('Đăng lúc' in unescape(home.text) and 'project-posted' in home.text,'Project discovery displays labelled publication time')
 def new_group():
  p=j.post(l,'/Groups/Create',{'Name':j.tag+' staffing','Description':'Nhóm giao diện bổ sung backend theo nhu cầu đặt lịch của dự án.','Industry':'Công nghệ thông tin','WorkStyle':'Remote','TargetSize':3,'MinimumBudget':1000000,'Specialties':'Web MVC, API','Collaboration':'Phân công theo vị trí, xác nhận thành viên trước khi gửi chào giá.','IsPublic':'true'},form='/Groups');return int(re.search(r'/Groups/Details/(\d+)',p.url).group(1))
 gid=new_group();manage=f'/GroupRecruitment/Manage?groupId={gid}&projectId={project}'
 p=j.get(l,manage);j.check(p.status_code==200 and 'Nguyễn Khánh Linh' in unescape(p.text),'Leader automatically sees a verified backend candidate for the missing position')
 j.check(j.get(w,manage).status_code==403,'Ordinary freelancer cannot inspect leader private staffing dashboard')
 j.post(l,'/GroupRecruitment/Open',{'groupId':gid,'projectId':project},form=manage)
 recruitment=int(j.sql(f'SELECT Id FROM dbo.GroupProjectRecruitments WHERE FreelancerGroupId={gid} AND ProjectId={project}'))
 text=unescape(j.get(w,f'/Projects/Details/{project}').text);name=j.sql(f'SELECT Name FROM dbo.FreelancerGroups WHERE Id={gid}')
 j.check(name in text and 'Đề nghị tham gia cho vị trí này' in text,'Solo freelancer viewing TEAM project sees matching groups missing its verified role')
 j.check(name not in unescape(j.get(x,f'/Projects/Details/{project}').text),'Unmatched freelancer is not shown misleading group opportunity')
 pitch='Tôi có minh chứng backend phù hợp và có thể triển khai API lịch hẹn theo yêu cầu đã nêu.'
 j.post(w,f'/GroupRecruitment/ApplyToGroup/{recruitment}',{'roleId':backend,'pitch':pitch},form=f'/Projects/Details/{project}')
 req=int(j.sql(f'SELECT Id FROM dbo.GroupJoinRequests WHERE GroupProjectRecruitmentId={recruitment} AND FreelancerId=8'))
 j.check(j.sql(f'SELECT Status FROM dbo.GroupJoinRequests WHERE Id={req}')=='Pending' and j.sql(f"SELECT COUNT(*) FROM dbo.FreelancerGroupMembers WHERE FreelancerGroupId={gid} AND UserAccountId=8 AND Status='Active'")=='0','Requesting a role never silently joins a group')
 j.check(j.sql(f"SELECT COUNT(*) FROM dbo.Notifications WHERE RecipientId=7 AND ProjectId={project} AND TargetPath LIKE '/GroupRecruitment/Manage%'")=='1','Join request notifies the actual leader with project source')
 j.post(w,f'/GroupRecruitment/ApplyToGroup/{recruitment}',{'roleId':backend,'pitch':pitch},form=f'/Projects/Details/{project}')
 j.check(j.sql(f'SELECT COUNT(*) FROM dbo.GroupJoinRequests WHERE GroupProjectRecruitmentId={recruitment} AND FreelancerId=8')=='1','Repeated join request cannot duplicate pending request')
 j.check(j.post(x,f'/GroupRecruitment/Decide/{req}',{'approve':'true'},form='/Groups').status_code==403,'Outsider cannot approve a group request')
 j.post(l,f'/GroupRecruitment/Decide/{req}',{'approve':'true'},form=manage)
 j.check(j.sql(f'SELECT Status FROM dbo.GroupJoinRequests WHERE Id={req}')=='Invited' and j.sql(f'SELECT Status FROM dbo.FreelancerGroupMembers WHERE FreelancerGroupId={gid} AND UserAccountId=8')=='Invited','Leader approval creates invitation while preserving final freelancer consent')
 j.post(w,f'/Groups/Respond/{gid}',{'accept':'true'},form='/Groups')
 j.check(j.sql(f'SELECT Status FROM dbo.FreelancerGroupMembers WHERE FreelancerGroupId={gid} AND UserAccountId=8')=='Active','Freelancer invitation acceptance completes membership')
 j.check(j.sql(f'SELECT COUNT(*) FROM dbo.ContractMembers m JOIN dbo.Contracts c ON c.Id=m.ContractId WHERE c.ProjectId={project}')=='0','New group membership does not create or rewrite contractual membership')
 p=j.get(l,manage);j.check('Thiếu 0' in unescape(p.text),'Staffing coverage recomputes after consented membership change')
 j.check(name not in unescape(j.get(w,f'/Projects/Details/{project}').text),'Filled or already-joined group is removed from freelancer opportunities')
 j.post(l,f'/GroupRecruitment/Close/{recruitment}',form=manage)
 j.check(j.sql(f'SELECT Status FROM dbo.GroupProjectRecruitments WHERE Id={recruitment}')=='Closed','Leader can close advertised staffing need')
 gid2=new_group();manage2=f'/GroupRecruitment/Manage?groupId={gid2}&projectId={project}';j.post(l,'/GroupRecruitment/Open',{'groupId':gid2,'projectId':project},form=manage2)
 rec2=int(j.sql(f'SELECT Id FROM dbo.GroupProjectRecruitments WHERE FreelancerGroupId={gid2} AND ProjectId={project}'))
 j.post(l,f'/GroupRecruitment/Invite/{rec2}',{'userId':8,'roleId':backend},form=manage2)
 j.check(j.sql(f'SELECT Status FROM dbo.FreelancerGroupMembers WHERE FreelancerGroupId={gid2} AND UserAccountId=8')=='Invited','Leader can invite directly from position-specific suggestions')
 j.post(c,'/Knowledge/Share',{**generic,'ConfirmGeneric':'true'},form=f'/Knowledge/Share?projectId={source}')
 j.check(j.sql(f'SELECT COUNT(*) FROM dbo.SharedKnowledgeEntries WHERE ProjectId={source}')=='1','Stale knowledge editor cannot overwrite a published version')
 updated={**generic,'ExpectedVersion':1,'ConfirmGeneric':'true','Practices':'Kiểm tra dữ liệu đầu vào, trùng lịch và thay đổi trạng thái trước khi hoàn tất nghiệm thu.'}
 p=j.post(c,'/Knowledge/Share',updated,form=f'/Knowledge/Share?projectId={source}');kid2=int(re.search(r'/Knowledge/Details/(\d+)',p.url).group(1))
 j.check(kid!=kid2 and j.get(w,f'/Knowledge/Details/{kid}').status_code==404,'New shared version preserves old record and removes it from active retrieval')
 j.post(c,f'/Knowledge/Withdraw/{kid2}',{'reason':'Ngừng chia sẻ để rà soát nội dung chung.'},form=f'/Knowledge/Details/{kid2}')
 j.check(j.get(w,f'/Knowledge/Details/{kid2}').status_code==404,'Withdrawn knowledge cannot be browsed as an active source')
 after,p=generate(idea)
 j.check(f'#{kid2}' not in j.sql(f'SELECT COALESCE(KnowledgeReferences,\'\') FROM dbo.AIJobDraftVersions WHERE AIJobDraftId={after}'),'Withdrawn knowledge is excluded from future JD generation')
 result={'checks':j.checks,'projectId':project,'sourceProjectId':source,'draftId':did2,'groupId':gid,'directInviteGroupId':gid2,'recruitmentId':recruitment,'directRecruitmentId':rec2,'knowledgeId':kid2,'backendRoleId':backend,'frontRoleId':front}
 print('KNOWLEDGE_STAFFING_HTTP_CHECKS='+str(len(j.checks)),flush=True);return result
if __name__=='__main__':
 p=argparse.ArgumentParser();p.add_argument('--base',required=True);p.add_argument('--database',required=True);p.add_argument('--result',required=True);a=p.parse_args();urllib3.disable_warnings();j=Journey(a.base,a.database);r=run(j);Path(a.result).write_text(json.dumps(r,ensure_ascii=False,indent=2),encoding='utf-8')
