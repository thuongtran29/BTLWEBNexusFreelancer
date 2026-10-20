"""Upload rejection must not persist a file; role ACLs still apply. Audit only."""
import argparse
import base64
import io
import json
from html import unescape
import zipfile
from pathlib import Path
import urllib3
from PlatformJourneyE2E import Journey, Fields


def run(j):
    client = j.login('client.quanghuy', 'Client'); worker = j.login('linh.backend', 'Freelancer')
    admin = j.login('admin', 'Admin'); outsider = j.login('vy.translation', 'Freelancer')
    wid = int(j.sql("SELECT TOP 1 w.Id FROM Workspaces w JOIN Contracts c ON c.Id=w.ContractId JOIN Projects p ON p.Id=w.ProjectId WHERE p.ClientId=6 AND c.CounterpartyId=8 AND c.Status='Active' ORDER BY w.Id DESC"))
    form = f'/Workspace/Index/{wid}'
    token = Fields(j.get(client, form).text).values['__RequestVerificationToken']
    def count(): return j.sql(f'SELECT COUNT(*) FROM WorkspaceFileVersions v JOIN WorkspaceFiles f ON f.Id=v.WorkspaceFileId WHERE f.WorkspaceId={wid}')
    def upload(session, name, data, csrf=token):
        return session.post(j.base+'/WorkspaceFiles/Upload', data={'WorkspaceId':wid,'__RequestVerificationToken':csrf},
                            files={'upload':(name,data,'application/octet-stream')},timeout=70)
    m=io.BytesIO()
    with zipfile.ZipFile(m,'w') as z:z.writestr('../outside.txt','path traversal')
    for name, data in [('spoof.png',b'not image'),('spoof.pdf',b'not pdf'),('fake.docx',m.getvalue()),('escape.zip',m.getvalue()),('binary.txt',b'abc\x00binary')]:
        before=count();r=upload(client,name,data)
        j.check(r.status_code==200 and 'Tệp không hợp lệ' in unescape(r.text) and before==count(),name+' rejected with guidance and no stored version')
    r=upload(client,'nghiem-thu.txt','Hướng dẫn bàn giao UTF-8, kiểm thử nội dung.'.encode())
    j.check(r.status_code==200 and 'Đã lưu tệp' in unescape(r.text),'Valid text accepted from participant')
    version=int(j.sql(f'SELECT TOP 1 v.Id FROM WorkspaceFileVersions v JOIN WorkspaceFiles f ON f.Id=v.WorkspaceFileId WHERE f.WorkspaceId={wid} ORDER BY v.Id DESC'))
    j.check(j.sql(f'SELECT ContentType FROM WorkspaceFileVersions WHERE Id={version}')=='text/plain','Server assigns detected MIME, ignores browser MIME')
    for s,label in [(worker,'Assigned freelancer'),(admin,'Admin evidence reviewer')]:
        page=j.get(s,form)
        j.check(f'/WorkspaceFiles/Download/{version}' in page.text,label+' sees the actual evidence download button')
        r=j.get(s,f'/WorkspaceFiles/Download/{version}')
        j.check(r.status_code==200 and r.content=='Hướng dẫn bàn giao UTF-8, kiểm thử nội dung.'.encode() and r.headers.get('X-Content-Type-Options')=='nosniff',label+' reads private attachment')
    j.check(j.get(outsider,f'/WorkspaceFiles/Download/{version}').status_code==403,'Unrelated freelancer cannot download')
    before=count();t=Fields(j.get(admin,form).text).values['__RequestVerificationToken'];r=upload(admin,'admin.txt',b'blocked',t)
    j.check(r.status_code==403 and count()==before,'Admin cannot become participant by uploading')
    return {'checks':j.checks,'workspaceId':wid}


if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--base',required=True);p.add_argument('--database',required=True);p.add_argument('--result',required=True);a=p.parse_args();urllib3.disable_warnings()
    result=run(Journey(a.base,a.database));Path(a.result).write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8');print('UPLOAD_CHECKS='+str(len(result['checks'])))
