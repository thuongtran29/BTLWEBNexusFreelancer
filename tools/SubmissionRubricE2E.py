"""Rubric additions: HTTP tests against the isolated 44392 audit site only."""
import sys,json,io
from pathlib import Path
import argparse
parser=argparse.ArgumentParser(description='Writes only to an isolated audit mirror of the local demo dataset.')
parser.add_argument('--audit-web-root',required=True)
args=parser.parse_args()
audit=Path(args.audit_web_root).resolve()
config=(audit/'Web.config').read_text(encoding='utf-8-sig')
if 'Initial Catalog=NexusFreelanceAudit20261019;' not in config:raise RuntimeError('Expected isolated audit database connection')
if audit == Path('D:/FreelancerBTLWEB') or Path('D:/FreelancerBTLWEB') in audit.parents:raise RuntimeError('Never run on the delivered live web root')
sys.path.insert(0,str(Path(__file__).resolve().parent))
from PlatformJourneyE2E import Journey,Fields
import requests,urllib3
import struct,zlib
urllib3.disable_warnings()
result_path=Path(__file__).resolve().parent/'_runtime/submission-http-results.json'
result_path.parent.mkdir(exist_ok=True)
j=Journey('https://localhost:44392','NexusFreelanceAudit20261019')
guest=requests.Session();guest.verify=False
r=j.get(guest,'/api/v1/projects?pageSize=2');d=r.json();j.check(r.status_code==200 and len(d['items'])<=2 and d['pageSize']==2,'REST public projects paginate with explicit DTO')
j.check(all(set(x)<= {'id','title','industry','type','budgetVnd','deadlineUtc'} for x in d['items']),'API excludes private client and authentication data')
if d['items']:
 title=d['items'][0]['title'];r=guest.get(j.base+'/api/v1/projects',params={'q':title},timeout=60);j.check(r.json()['total']>=1 and all(title in x['title'] for x in r.json()['items']),'REST title filter executes on SQL')
j.check(j.get(guest,'/api/v1/projects?pageSize=51').status_code==400,'REST rejects excessive page size')
j.check(j.get(guest,'/api/v1/projects?page=-1').status_code==400,'REST rejects negative page')
j.check(j.get(guest,'/api/v1/notifications').status_code==401,'Private REST returns 401 without login redirect')
client=j.login('client.minhan','Client'); worker=j.login('linh.backend','Freelancer')
assert j.database.startswith('NexusFreelanceAudit')
id=j.sql("INSERT dbo.Notifications(RecipientId,Title,TargetPath,CreatedAtUtc) OUTPUT INSERTED.Id VALUES (4,N'Kiểm thử AJAX rubric','/Dashboard',GETUTCDATE());")
try:
 p=j.get(client,'/Notifications'); token=Fields(p.text).values['__RequestVerificationToken']
 r=client.patch(j.base+'/api/v1/notifications/'+id,data={'read':'true'},verify=False);j.check(r.status_code==400,'PATCH requires anti-forgery token')
 r=worker.patch(j.base+'/api/v1/notifications/'+id,data={'read':'true'},headers={'X-CSRF-Token':Fields(j.get(worker,'/Profile').text).values['__RequestVerificationToken']});j.check(r.status_code==404,'PATCH rejects another recipient without disclosing data')
 r=client.patch(j.base+'/api/v1/notifications/'+id,data={'read':'true'},headers={'X-CSRF-Token':token});j.check(r.status_code==200 and r.json()['read'],'PATCH marks own notification read')
 r=client.patch(j.base+'/api/v1/notifications/'+id,data={'read':'true'},headers={'X-CSRF-Token':token});j.check(r.status_code==200,'PATCH read is idempotent')
 j.check(j.sql('SELECT COUNT(*) FROM dbo.Notifications WHERE Id='+id+' AND ReadAtUtc IS NOT NULL')=='1','AJAX status persists in SQL')
finally:j.sql('DELETE dbo.Notifications WHERE Id='+id)
# Only audit App_Data can be modified by this test.
avatar=audit/'App_Data/ProfileImages/8.jpg'
before=avatar.read_bytes() if avatar.exists() else None
client_avatar=audit/'App_Data/ProfileImages/4.jpg'
client_before=client_avatar.read_bytes() if client_avatar.exists() else None
old_public=j.sql('SELECT COALESCE(CAST(IsPublic AS varchar),\'null\') FROM dbo.FreelancerProfiles WHERE UserAccountId=8')
p=j.get(worker,'/Profile');token=Fields(p.text).values['__RequestVerificationToken']
try:
 chunk=lambda kind,data:struct.pack('>I',len(data))+kind+data+struct.pack('>I',zlib.crc32(kind+data)&0xffffffff)
 raw=io.BytesIO(b'\x89PNG\r\n\x1a\n'+chunk(b'IHDR',struct.pack('>IIBBBBB',1,1,8,2,0,0,0))+chunk(b'IDAT',zlib.compress(b'\x00\x1e\x64\x78'))+chunk(b'IEND',b''))
 r=worker.post(j.base+'/ProfileImage/Upload',data={'__RequestVerificationToken':token},files={'image':('demo.png',raw.getvalue(),'image/png')});j.check(r.status_code==200 and avatar.exists(),'PNG upload stores normalized avatar outside public executable paths')
 uploaded=j.get(guest,'/ProfileImage/Show/8');j.check(uploaded.status_code==200 and uploaded.headers.get('Content-Type')=='image/jpeg' and uploaded.content[:2]==b'\xff\xd8','Public profile image is re-encoded JPEG')
 valid=avatar.read_bytes()
 for name,content in [('fake.png',b'<script>alert(1)</script>'),('payload.svg',b'<svg/>'),('large.jpg',b'x'*(2*1024*1024+1))]:
  r=worker.post(j.base+'/ProfileImage/Upload',data={'__RequestVerificationToken':token},files={'image':(name,content,'image/png')});j.check(r.status_code==200 and avatar.read_bytes()==valid,'Image validation preserves previous avatar for '+name)
 r=client.post(j.base+'/ProfileImage/Upload',data={'__RequestVerificationToken':Fields(j.get(client,'/Dashboard').text).values['__RequestVerificationToken'],'returnUrl':'https://example.com'},files={'image':('a.png',raw.getvalue(),'image/png')});j.check(r.status_code==200 and client_avatar.exists() and avatar.read_bytes()==valid,'Client updates only own avatar and external return URL is ignored')
 j.check(j.get(guest,'/ProfileImage/Show/4').status_code==404,'Client avatar is not disclosed to anonymous visitors')
 r=worker.post(j.base+'/ProfileImage/Upload',files={'image':('a.png',raw.getvalue(),'image/png')},allow_redirects=False);j.check(r.status_code in (302,400),'Avatar upload rejects missing CSRF')
 j.sql('UPDATE dbo.FreelancerProfiles SET IsPublic=0 WHERE UserAccountId=8')
 j.check(j.get(guest,'/ProfileImage/Show/8').status_code==404,'Hidden profile image is not public')
 j.check(j.get(worker,'/ProfileImage/Show/8').status_code==200,'Owner may view own hidden avatar')
finally:
 j.sql('UPDATE dbo.FreelancerProfiles SET IsPublic='+old_public+' WHERE UserAccountId=8')
 if before is not None:avatar.write_bytes(before)
 elif avatar.exists():avatar.unlink()
 if client_before is not None:client_avatar.write_bytes(client_before)
 elif client_avatar.exists():client_avatar.unlink()
p=j.get(client,'/Projects/Create');j.check('project-editor.js' in p.text and 'easymde.min.js' in p.text and 'jquery-3.7.1.min.js' in p.text,'Project form includes local frontend dependencies and editor')
result_path.write_text(json.dumps({'checks':j.checks},ensure_ascii=False,indent=2),encoding='utf-8')
print('SUBMISSION_HTTP_CHECKS='+str(len(j.checks)))
