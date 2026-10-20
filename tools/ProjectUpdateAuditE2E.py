"""Published/active project editing and participant notifications; isolated audit only."""
import argparse,json,re
from pathlib import Path
from html import unescape
import urllib3
from PlatformJourneyE2E import Journey,Fields


def run(j):
 c=j.login('client.quanghuy','Client');back=j.login('linh.backend','Freelancer');front=j.login('minh.frontend','Freelancer');other=j.login('client.thuha','Client');outside=j.login('an.design','Freelancer')
 data={'Title':j.tag+' công bố','Description':'Mô tả ban đầu, ghi rõ phạm vi và sản phẩm cần bàn giao.','Industry':'Công nghệ thông tin','ProjectType':'ENTERPRISE','Budget':5000000,'MinimumSkillLevel':1}
 p=j.post(c,'/Projects/Create',data,form='/Projects/Create');pid=int(re.search(r'/Projects/Manage/(\d+)',p.url).group(1));edit=f'/Projects/Edit/{pid}';manage=f'/Projects/Manage/{pid}'
 for role in ['Backend Developer','Frontend Developer']:j.post(c,'/Projects/AddRequirement',{'ProjectId':pid,'Title':role,'RequiredCount':1},form=manage)
 roles=[int(x) for x in j.sql(f'SELECT Id FROM dbo.RoleRequirements WHERE ProjectId={pid} ORDER BY Id').splitlines()]
 j.post(c,f'/Projects/Publish/{pid}',form=manage)
 j.post(back,'/Proposals/Apply',{'ProjectId':pid,'RoleRequirementId':roles[0],'QuotedPrice':1000000,'DeliveryDays':7,'Pitch':'Triển khai backend đúng phạm vi và bàn giao kiểm thử.'},form=f'/Projects/Details/{pid}')
 page=j.get(c,edit);j.check(page.status_code==200,'Owner can open the editor after publishing')
 j.check(Fields(page.text).values['Budget'].isdigit(),'Existing VND budget renders as an integer acceptable to native number input')
 def version():return Fields(j.get(c,edit).text).values['EditVersion']
 def notices():return int(j.sql(f"SELECT COUNT(*) FROM dbo.Notifications WHERE ProjectId={pid} AND Title LIKE N'Dự án vừa cập nhật:%'"))
 initial=version();data.update({'Title':j.tag+' đã cập nhật','Description':'Nội dung thường, không cần định dạng.\nDòng thứ hai giữ nguyên dấu * và ký tự tiếng Việt.','Budget':6000000,'EditVersion':initial})
 before=notices();page=j.post(c,edit,data,form=edit)
 j.check(page.status_code==200 and j.sql(f'SELECT Budget FROM dbo.Projects WHERE Id={pid}')=='6000000.00','Published project accepts normal text and recruitment budget change')
 j.check(notices()==before+1,'Pending applicant gets exactly one project-update notification')
 j.check(j.sql(f"SELECT COUNT(*) FROM dbo.Notifications WHERE ProjectId={pid} AND Title LIKE N'Dự án vừa cập nhật:%' AND ActorId=6 AND RecipientId=8 AND Source=N'Dự án' AND TargetPath='/Projects/Details/{pid}'")=='1','Notification identifies editor, project and valid destination')
 j.check(j.sql(f"SELECT COUNT(*) FROM dbo.Notifications WHERE ProjectId={pid} AND Title LIKE N'Dự án vừa cập nhật:%' AND RecipientId IN (6,13)")=='0','Owner and unrelated freelancer do not receive the private update event')
 old={**data,'Title':'Stale editor must not overwrite the latest title','EditVersion':initial}
 page=j.post(c,edit,old,form=edit)
 j.check(page.status_code==409 and 'đã thay đổi' in unescape(page.text),'Stale edit is rejected with recovery guidance')
 j.check(j.sql(f'SELECT Title FROM dbo.Projects WHERE Id={pid}')==data['Title'] and notices()==before+1,'Stale write changes neither data nor notifications')
 data['EditVersion']=version();j.post(c,edit,data,form=edit)
 j.check(notices()==before+1,'Saving unchanged values sends no duplicate notification')
 j.check(j.post(c,edit,{**data,'EditVersion':''},form=edit).status_code==409,'Published edit requires an explicit current version')
 j.check(j.post(c,edit,{**data,'EditVersion':version(),'ProjectType':'SOLO'},form=edit).status_code==409,'Published project cannot change its application mode')
 j.check(j.get(other,edit).status_code==403 and j.get(back,edit).status_code==403,'Only project owner may open edit form')
 j.check(j.post(back,edit,data,form='/Dashboard').status_code==403,'Freelancer cannot submit a project edit directly')
 invalid={**data,'EditVersion':version(),'Description':' '};page=j.post(c,edit,invalid,form=edit)
 j.check(page.status_code==200 and j.sql(f'SELECT Description FROM dbo.Projects WHERE Id={pid}')==data['Description'],'Empty description retains editor and does not save')
 j.post(c,f'/Projects/ChangeStatus/{pid}',{'operation':'Pause'},form=manage)
 data.update({'EditVersion':version(),'Description':data['Description']+'\nThông tin được bổ sung trong lúc tạm dừng tuyển.'});j.post(c,edit,data,form=edit)
 j.check(j.sql(f'SELECT Description FROM dbo.Projects WHERE Id={pid}')==data['Description'],'Owner can update a paused published project')
 j.post(c,f'/Projects/ChangeStatus/{pid}',{'operation':'Resume'},form=manage)
 j.post(front,'/Proposals/Apply',{'ProjectId':pid,'RoleRequirementId':roles[1],'QuotedPrice':1000000,'DeliveryDays':7,'Pitch':'Triển khai giao diện có kiểm thử và hướng dẫn bàn giao.'},form=f'/Projects/Details/{pid}')
 prop=int(j.sql(f'SELECT Id FROM dbo.Proposals WHERE ProjectId={pid} AND ApplicantId=7'));j.post(c,f'/Projects/Accept/{pid}',{'proposalId':prop},form=manage)
 digest=j.sql(f'SELECT Hash FROM dbo.ContractVersions WHERE ContractId IN(SELECT Id FROM dbo.Contracts WHERE ProjectId={pid}) ORDER BY Id')
 count=notices();data.update({'EditVersion':version(),'Description':data['Description']+'\nCập nhật đầu mối và hướng dẫn trao đổi trong dự án.'});j.post(c,edit,data,form=edit)
 j.check(notices()==count+2,'Both pending applicant and contracted worker receive the update once')
 j.check(j.sql(f'SELECT Hash FROM dbo.ContractVersions WHERE ContractId IN(SELECT Id FROM dbo.Contracts WHERE ProjectId={pid}) ORDER BY Id')==digest,'Public information edit preserves immutable contract snapshot')
 tampered={**data,'EditVersion':version(),'Budget':7000000};page=j.post(c,edit,tampered,form=edit)
 j.check(page.status_code==409 and j.sql(f'SELECT Budget FROM dbo.Projects WHERE Id={pid}')=='6000000.00','Existing contract locks commercial project fields on the server')
 from datetime import datetime,timedelta,timezone
 future=(datetime.now(timezone.utc)+timedelta(days=30)).strftime('%Y-%m-%d');count=notices()
 j.post(c,f'/Projects/UpdateDeadline/{pid}',{'deadline':future},form=manage)
 j.check(notices()==count+2,'Separate deadline update informs both applicant and contractual participant')
 count=notices();j.post(c,f'/Projects/UpdateSkills/{pid}',{'skillIds':[1],'minimumSkillLevel':1},form=manage)
 j.check(notices()==count+2,'Separate recruitment-skill update informs each related person once')
 count=notices();j.post(c,f'/Projects/UpdateSkills/{pid}',{'skillIds':[1],'minimumSkillLevel':1},form=manage)
 j.check(notices()==count,'Unchanged recruitment skills do not create duplicate updates')
 data.update({'Deadline':future,'SkillIds':[1]})
 prop=int(j.sql(f'SELECT Id FROM dbo.Proposals WHERE ProjectId={pid} AND ApplicantId=8'));j.post(c,f'/Projects/Accept/{pid}',{'proposalId':prop},form=manage)
 contracts=[int(x) for x in j.sql(f'SELECT Id FROM dbo.Contracts WHERE ProjectId={pid} ORDER BY Id').splitlines()]
 j.post(c,f'/Projects/LockTeam/{pid}',form=manage)
 for cid in contracts:
  v,h,uid=j.sql(f"SELECT CONCAT(v.Id,'|',v.Hash,'|',c.CounterpartyId) FROM dbo.ContractVersions v JOIN dbo.Contracts c ON c.Id=v.ContractId WHERE c.Id={cid}").split('|')
  for s in [c,front if uid=='7' else back]:j.post(s,f'/Contracts/Confirm/{cid}',{'versionId':v,'hash':h},form=f'/Contracts/Details/{cid}')
  mid=j.sql(f'SELECT Id FROM dbo.EscrowMilestones WHERE ContractId={cid}');j.post(c,f'/Wallet/Fund/{mid}',form=f'/Contracts/Details/{cid}')
 j.check(j.sql(f'SELECT Status FROM dbo.Projects WHERE Id={pid}')=='InProgress','Fixture reaches actual active project through signatures and escrow')
 count=notices();data.update({'EditVersion':version(),'Title':j.tag+' đang triển khai'});j.post(c,edit,data,form=edit)
 j.check(notices()==count+2 and j.sql(f'SELECT Title FROM dbo.Projects WHERE Id={pid}')==data['Title'],'Active project information edit notifies each contracted member once')
 j.check('data-markdown-content' not in j.get(outside,f'/Projects/Details/{pid}').text,'Project detail displays description as plain encoded text')
 j.post(back,f'/Wallet/Dispute/{pid}',{'contractId':contracts[-1],'reason':'Fixture kiểm tra việc khóa sửa nội dung dự án khi đang tranh chấp.'},form=f'/Contracts/Details/{contracts[-1]}')
 j.check(j.get(c,edit).status_code==409,'Disputed project cannot rewrite its displayed evidence')
 print('PROJECT_UPDATE_AUDIT_CHECKS='+str(len(j.checks)),flush=True)
 return {'checks':j.checks,'projectId':pid,'contracts':contracts,'description':data['Description']}


if __name__=='__main__':
 p=argparse.ArgumentParser();p.add_argument('--base',required=True);p.add_argument('--database',required=True);p.add_argument('--result',required=True);a=p.parse_args();urllib3.disable_warnings()
 Path(a.result).write_text(json.dumps(run(Journey(a.base,a.database)),ensure_ascii=False,indent=2),encoding='utf-8')
