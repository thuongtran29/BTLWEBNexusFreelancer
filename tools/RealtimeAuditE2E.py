"""Realtime transport, resync and role ACLs. Never writes to the delivered live DB."""
import argparse
import concurrent.futures
import json
import re
import time
import uuid
from datetime import datetime, timezone, timedelta
from html import unescape
from pathlib import Path
import requests
import urllib3
from PlatformJourneyE2E import Journey, Fields


def event(response, predicate=lambda data: True):
    start = time.monotonic()
    if not hasattr(response, '_audit_lines'):
        response._audit_lines = response.iter_lines(chunk_size=1, decode_unicode=True)
    for line in response._audit_lines:
        if time.monotonic() - start > 12: raise AssertionError('Stream event deadline exceeded')
        if line and line.startswith('data: '):
            data = json.loads(line[6:])
            if predicate(data): return data
    raise AssertionError('Stream ended without expected data')


def run(j):
    c=j.login('client.quanghuy','Client');f=j.login('linh.backend','Freelancer');a=j.login('admin','Admin');o=j.login('vy.translation','Freelancer')
    guest=requests.Session();guest.verify=False
    opened=j.post(c,'/Messages/Contact',{'freelancerId':8},form='/Talent/Freelancer/8')
    cid=int(re.search(r'/Messages/Thread/(\d+)',opened.url).group(1));direct=f'?conversationId={cid}'
    wid=int(j.sql("SELECT TOP 1 w.Id FROM Workspaces w JOIN Contracts c ON c.Id=w.ContractId JOIN Projects p ON p.Id=w.ProjectId WHERE p.ClientId=6 AND c.CounterpartyId=8 AND c.Status='Active' ORDER BY w.Id DESC"));workspace=f'?workspaceId={wid}'
    def get(s,path):return j.get(s,'/Realtime/'+path)
    def send(s,path,body,form,extra=None):
        token=Fields(j.get(s,form).text).values['__RequestVerificationToken']
        return s.post(j.base+path,data={'body':body,'__RequestVerificationToken':token,**(extra or {})},headers={'X-Requested-With':'XMLHttpRequest'},timeout=15)
    for endpoint in ['State','Messages'+direct,'Stream'+direct]:
        r=get(guest,endpoint);j.check(r.status_code==401 and r.headers['Content-Type'].startswith('application/json'),'Guest gets JSON 401: '+endpoint.split('?')[0])
    for s,uid,label in [(c,6,'Client'),(f,8,'Freelancer'),(a,3,'Admin')]:
        r=get(s,'State');data=r.json()
        j.check(r.status_code==200 and data['accountId']==uid and data['unreadCount']==int(j.sql(f'SELECT COUNT(*) FROM Notifications WHERE RecipientId={uid} AND ReadAtUtc IS NULL')),label+' sees only own unread total')
        j.check('body' not in data and 'items' not in data,label+' global state does not disclose message bodies')
    for scope in [direct,workspace]:
        for endpoint in ['State','Messages','Stream']:
            j.check(get(o,endpoint+scope).status_code==404,'Outsider denied '+endpoint+scope.split('=')[0])
    for endpoint in ['State','Messages','Stream']:
        j.check(get(a,endpoint+direct).status_code==403,'Admin cannot read private direct '+endpoint)
    j.check(get(a,'State'+workspace).status_code==200 and not get(a,'State'+workspace).json()['canSend'],'Admin workspace stream is read-only')
    j.check(get(a,'Messages'+workspace).status_code==200,'Admin may read workspace evidence messages')
    for path in ['State?conversationId=-1','State?conversationId=bad','State?conversationId=1&workspaceId=1','Messages'+direct+'&after=-1','Messages']:
        j.check(get(c,path).status_code==400,'Malformed realtime input controlled: '+path)
    before=get(c,'State'+direct).json()
    stream=c.get(j.base+'/Realtime/Stream'+direct,stream=True,timeout=(15,15))
    try:
        started=time.monotonic();first=event(stream)
        j.check(stream.status_code==200 and 'text/event-stream' in stream.headers['Content-Type'] and time.monotonic()-started<3,'SSE sends initial event immediately without buffering')
        j.check('no-store' in stream.headers.get('Cache-Control',''),'Stream is not cacheable')
        text='Realtime private '+j.tag+' <img src=x onerror="window.auditXss=true">'
        response=send(f,f'/Messages/Send/{cid}',text,f'/Messages/Thread/{cid}')
        j.check(response.status_code==200 and response.json()['sent'],'Freelancer sends plaintext through AJAX')
        changed=event(stream,lambda d:d.get('latestMessageId',0)>first['latestMessageId'])
        j.check(changed['unreadCount']>before['unreadCount'],'Client receives pushed unread/message update without reloading')
    finally:stream.close()
    messages=get(c,'Messages'+direct+'&after='+str(before['latestMessageId'])).json()
    j.check(any(m['body']==text and m['authorId']==8 for m in messages['items']),'Delta contains correct author, literal markup and UTC time')
    count=j.sql(f'SELECT COUNT(*) FROM DirectMessages WHERE ConversationId={cid}')
    get(c,'Messages'+direct);get(c,'Messages'+direct)
    j.check(j.sql(f'SELECT COUNT(*) FROM DirectMessages WHERE ConversationId={cid}')==count,'Reconnect/sync does not insert duplicate messages')
    r=send(c,f'/Messages/Send/{cid}','  ',f'/Messages/Thread/{cid}')
    j.check(r.status_code==422 and j.sql(f'SELECT COUNT(*) FROM DirectMessages WHERE ConversationId={cid}')==count,'Invalid AJAX text rejected without insert')
    r=c.post(j.base+f'/Messages/Send/{cid}',data={'body':'bad csrf'},headers={'X-Requested-With':'XMLHttpRequest'},timeout=15)
    j.check(r.status_code==400,'AJAX send still requires CSRF')
    # Same browser cookie can POST while its stream is open; no session lock starvation.
    stream=c.get(j.base+'/Realtime/Stream'+direct,stream=True,timeout=(15,15))
    try:
        event(stream);start=time.monotonic();r=send(c,f'/Messages/Send/{cid}','Same-session send '+j.tag,f'/Messages/Thread/{cid}')
        j.check(r.status_code==200 and time.monotonic()-start<8,'SSE does not block same-session AJAX send')
    finally:stream.close()
    before=get(c,'State'+workspace).json()
    stream=c.get(j.base+'/Realtime/Stream'+workspace,stream=True,timeout=(15,15))
    try:
        first=event(stream);r=send(f,'/Workspace/SendMessage','Workspace push '+j.tag,f'/Workspace/Index/{wid}',{'WorkspaceId':wid})
        j.check(r.status_code==200 and r.json()['sent'],'Freelancer sends workspace chat by AJAX')
        changed=event(stream,lambda d:d.get('latestMessageId',0)>first['latestMessageId'])
        j.check(changed['latestMessageId']>before['latestMessageId'],'Workspace participant receives pushed channel update')
    finally:stream.close()
    r=send(a,'/Workspace/SendMessage','Admin cannot write',f'/Workspace/Index/{wid}',{'WorkspaceId':wid})
    j.check(r.status_code==403,'Admin cannot write via realtime-enhanced workspace form')
    # More than one page missed offline: SQL fixture, not bypassing HTTP send quota.
    checkpoint=get(c,'State'+direct).json()['latestMessageId']
    j.sql(f"BEGIN TRAN; SELECT Id FROM DirectConversations WITH(UPDLOCK,HOLDLOCK) WHERE Id={cid}; DECLARE @i int=0; WHILE @i<61 BEGIN INSERT DirectMessages(ConversationId,SenderId,Body,CreatedAtUtc) VALUES({cid},8,N'Audit offline backlog '+CONVERT(nvarchar(10),@i),GETUTCDATE()); SET @i+=1; END COMMIT;")
    ids=[];after=checkpoint
    while True:
        data=get(c,'Messages'+direct+'&after='+str(after)).json();j.check(len(data['items'])<=50,'Delta page bounded at 50');ids.extend(m['id'] for m in data['items']);after=data['nextCursor']
        if not data['hasMore']:break
    j.check(len(ids)==61 and len(set(ids))==61 and ids==sorted(ids),'Offline backlog resumes across pages without gaps or duplicates')
    j.check(len(get(c,'Messages'+direct+'&after=0').json()['items'])==50,'Initial sync returns latest 50 rather than unbounded history')
    history=j.get(c,f'/Messages/Thread/{cid}?before={after}')
    j.check('data-history="true"' in history.text and 'data-live-latest' in history.text,'Historical page remains anchored with a latest-message link')
    # Disposable ordinary account proves open stream revocation. No Admin privilege changes.
    tag=uuid.uuid4().hex;email='rt-revoke-'+tag+'@example.test'
    uid=int(j.sql(f"INSERT Users(Name,Email,PasswordHash,Role,JoinedAtUtc,IsSuspended,EmailVerifiedAtUtc) SELECT N'Realtime disposable',N'{email}',PasswordHash,'Freelancer',GETUTCDATE(),0,GETUTCDATE() FROM Users WHERE Id=8; SELECT CAST(SCOPE_IDENTITY() AS int);"))
    try:
        temp=requests.Session();temp.verify=False
        p=j.get(temp,'/Account/Login');token=Fields(p.text).values['__RequestVerificationToken']
        r=temp.post(j.base+'/Account/Login',data={'Email':email,'Password':j.credentials['Password'],'Role':'Freelancer','__RequestVerificationToken':token},timeout=30)
        j.check(r.status_code==200 and 'GoogleLoginButton' not in r.text,'Disposable normal account logs in')
        stream=temp.get(j.base+'/Realtime/Stream',stream=True,timeout=(15,15))
        try:
            event(stream);j.sql(f'UPDATE Users SET IsSuspended=1 WHERE Id={uid}')
            denied=event(stream,lambda d:d.get('status')==401)
            j.check(denied['status']==401,'An already-open stream revokes a suspended account')
        finally:stream.close()
        j.sql(f'UPDATE Users SET IsSuspended=0 WHERE Id={uid}')
        p=j.get(temp,'/Account/Login');token=Fields(p.text).values['__RequestVerificationToken']
        temp.post(j.base+'/Account/Login',data={'Email':email,'Password':j.credentials['Password'],'Role':'Freelancer','__RequestVerificationToken':token},timeout=30)
        stream=temp.get(j.base+'/Realtime/Stream',stream=True,timeout=(15,15))
        try:
            event(stream);j.sql(f'UPDATE Users SET PasswordChangedAtUtc=GETUTCDATE() WHERE Id={uid}')
            denied=event(stream,lambda d:d.get('status')==401)
            j.check(denied['status']==401,'Password revision change revokes an already-open stream')
        finally:stream.close()
    finally:
        j.sql(f'DELETE FROM UserAccessLogs WHERE UserAccountId={uid}; DELETE FROM Users WHERE Id={uid};')
    return {'checks':j.checks,'conversationId':cid,'workspaceId':wid,'xssText':text,
            'cooldownUntilUtc':(datetime.now(timezone.utc)+timedelta(seconds=65)).isoformat()}


if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--base',required=True);p.add_argument('--database',required=True);p.add_argument('--result',required=True);args=p.parse_args();urllib3.disable_warnings()
    result=run(Journey(args.base,args.database));Path(args.result).write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8');print('REALTIME_HTTP_CHECKS='+str(len(result['checks'])))
