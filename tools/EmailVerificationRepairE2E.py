"""Local pickup-only OTP lockout, role isolation and resend regression; cleans its own auth-only users."""
import argparse,email,re,uuid
from pathlib import Path
import requests,urllib3
from SmokeE2E import Smoke
from V3FlowE2E import sql
p=argparse.ArgumentParser();p.add_argument('--mailbox',type=Path,required=True);p.add_argument('--run-writes',action='store_true');a=p.parse_args()
assert a.run_writes and a.mailbox.is_absolute() and a.mailbox.is_dir()
urllib3.disable_warnings();t=Smoke('https://localhost:44391');run=uuid.uuid4().hex[:12];address='otp-repair-'+run+'@example.test';pwd='OtpRepair!'+run+'Aa7';count=0;sessions=[];owned_mail=set()
def check(ok,label):
 global count
 assert ok,label
 count+=1;print('PASS '+label,flush=True)
def code(role):
 found=[]
 for path in a.mailbox.glob('*.eml'):
  message=email.message_from_bytes(path.read_bytes())
  if address not in str(message.get('To','')):continue
  owned_mail.add(path)
  for part in message.walk():
   if part.get_content_type()=='text/plain':
    text=(part.get_payload(decode=True) or b'').decode(part.get_content_charset() or 'utf-8');match=re.search('vai trò '+role+r':\s*([0-9]{6})',text)
    if match:found.append((path.stat().st_mtime_ns,match.group(1)))
 assert found
 return max(found)[1]
try:
 for role in ('Client','Freelancer'):
  s=requests.Session();s.verify=False;s.trust_env=False;sessions.append(s)
  check('data-mail-delivery="pickup"' in t.get(s,'/Account/Register').text,'local pickup is visible '+role)
  t.post(s,'/Account/Register',{'Name':'OTP regression '+run,'Email':address,'Role':role,'Password':pwd},'/Account/Register')
 client,worker=sessions;first=code('Client');other=code('Freelancer');page='/Account/ConfirmRegistration?email='+address+'&role=Client'
 if first!=other:
  result=t.post(client,'/Account/ConfirmRegistration',{'Email':address,'Role':'Client','Code':other},page,False)
  check(result.status_code==200,'another role OTP cannot verify the Client profile')
 wrong='111111' if first!='111111' else '222222'
 for attempt in range(5):t.post(client,'/Account/ConfirmRegistration',{'Email':address,'Role':'Client','Code':wrong},page,False)
 blocked=t.post(client,'/Account/ConfirmRegistration',{'Email':address,'Role':'Client','Code':first},page,False)
 check(blocked.status_code==200 and sql(f"SELECT COUNT(*) FROM Users WHERE Email='{address}' AND EmailVerifiedAtUtc IS NOT NULL")=='0','five failures lock the current code even when the correct value is later supplied')
 t.post(client,'/Account/ResendVerification',{'Email':address,'Role':'Client'},'/Account/ResendVerification');fresh=code('Client')
 check(sql(f"SELECT COUNT(*) FROM RegistrationCodes c JOIN Users u ON u.Id=c.UserAccountId WHERE u.Email='{address}' AND u.Role='Client' AND c.ConsumedAtUtc IS NULL AND c.FailedAttempts=0")=='1','resend creates exactly one fresh code with a fresh attempt allowance')
 t.post(client,'/Account/ConfirmRegistration',{'Email':address,'Role':'Client','Code':fresh},page)
 check(sql(f"SELECT COUNT(*) FROM Users WHERE Email='{address}' AND Role='Client' AND EmailVerifiedAtUtc IS NOT NULL")=='1','fresh code verifies only its intended role')
 check(sql(f"SELECT COUNT(*) FROM Users WHERE Email='{address}' AND Role='Freelancer' AND EmailVerifiedAtUtc IS NULL")=='1','the other role still needs separate verification')
 t.post(worker,'/Account/ConfirmRegistration',{'Email':address,'Role':'Freelancer','Code':other},'/Account/ConfirmRegistration?email='+address+'&role=Freelancer')
 for s,role in [(client,'Client'),(worker,'Freelancer')]:
  t.post(s,'/Account/Login',{'Email':address,'Role':role,'Password':pwd},'/Account/Login')
  check('data-journey-role="'+role+'"' in t.get(s,'/Dashboard').text,'verified login enters the correct role '+role)
 print('EMAIL_VERIFICATION_REPAIR_CHECKS='+str(count)+' PASS',flush=True)
finally:
 if sessions:
  sql(f"""SET XACT_ABORT ON; BEGIN TRAN;
IF EXISTS(SELECT 1 FROM Projects WHERE ClientId IN(SELECT Id FROM Users WHERE Email='{address}')) OR EXISTS(SELECT 1 FROM Proposals WHERE ApplicantId IN(SELECT Id FROM Users WHERE Email='{address}')) THROW 51780,'Unexpected fixture participation; preserve data',1;
IF EXISTS(SELECT 1 FROM WalletEntries WHERE UserAccountId IN(SELECT Id FROM Users WHERE Email='{address}')) OR EXISTS(SELECT 1 FROM WalletAccounts WHERE UserAccountId IN(SELECT Id FROM Users WHERE Email='{address}') AND (Balance<>0 OR Held<>0)) THROW 51781,'Fixture money activity; preserve data',1;
DELETE RegistrationCodes WHERE UserAccountId IN(SELECT Id FROM Users WHERE Email='{address}');
DELETE EmailVerificationTokens WHERE UserAccountId IN(SELECT Id FROM Users WHERE Email='{address}');
DELETE PasswordResetTokens WHERE UserAccountId IN(SELECT Id FROM Users WHERE Email='{address}');
DELETE WalletAccounts WHERE UserAccountId IN(SELECT Id FROM Users WHERE Email='{address}');
DELETE Users WHERE Email='{address}' AND Role IN('Client','Freelancer') AND Name=N'OTP regression {run}'; COMMIT;""")
  check(sql(f"SELECT COUNT(*) FROM Users WHERE Email='{address}'")=='0','only this run’s disposable auth fixtures were removed')
 for path in owned_mail:path.unlink()
