"""Actual HTTP password reset across two role profiles sharing one email.
Seeds a reset token in SQL, and verifies only its own profile session is revoked.
No live reset email delivery is claimed by this test.
"""
import argparse, hashlib, re, secrets
import requests, urllib3
from SmokeE2E import Smoke
from V3FlowE2E import sql,assert_eq
def main():
    p=argparse.ArgumentParser();p.add_argument('--run-writes',action='store_true');p.add_argument('--fixture-run',required=True);a=p.parse_args()
    if not a.run_writes or not re.fullmatch('[a-f0-9]{12}',a.fixture_run):p.error('write flag and test fixture required')
    urllib3.disable_warnings();smoke=Smoke('https://localhost:44391');address=f'v3-{a.fixture_run}@example.test';old='NexusV3!'+a.fixture_run+'Aa7';new='RenewedV3!'+a.fixture_run+'Bb8'
    def sess():s=requests.Session();s.verify=False;return s
    def login(s,role,pwd):return smoke.post(s,'/Account/Login',{'Email':address,'Role':role,'Password':pwd},'/Account/Login',False)
    client=sess();free=sess();assert_eq(login(client,'Client',old).status_code,302);assert_eq(login(free,'Freelancer',old).status_code,302)
    cid=int(sql(f"SELECT Id FROM Users WHERE Email='{address}' AND Role='Client'"));fid=int(sql(f"SELECT Id FROM Users WHERE Email='{address}' AND Role='Freelancer'"))
    def check(label,fn):return smoke.check(label,fn)
    try:
        token=secrets.token_urlsafe(32);digest=hashlib.sha256(token.encode('ascii')).hexdigest().upper()
        sql(f"INSERT PasswordResetTokens(UserAccountId,TokenHash,CreatedAtUtc,ExpiresAtUtc) VALUES({fid},'{digest}',GETUTCDATE(),DATEADD(minute,30,GETUTCDATE()))")
        anon=sess();smoke.post(anon,'/Account/ResetPassword',{'Token':token,'Password':new,'ConfirmPassword':new},'/Account/ResetPassword?token='+token)
        check('reset revokes old freelancer session',lambda:smoke.get(free,'/Dashboard',403))
        check('reset preserves client session on same email',lambda:smoke.get(client,'/Dashboard'))
        check('old freelancer password no longer signs in',lambda:assert_eq(login(sess(),'Freelancer',old).status_code,200))
        check('new freelancer password signs in',lambda:assert_eq(login(sess(),'Freelancer',new).status_code,302))
        check('client password unchanged',lambda:assert_eq(login(sess(),'Client',old).status_code,302))
        check('freelancer password cannot sign into client role',lambda:assert_eq(login(sess(),'Client',new).status_code,200))
        ambiguous=smoke.post(sess(),'/Account/Login',{'Email':address,'Password':old},'/Account/Login',False)
        check('missing role for shared email denied cleanly',lambda:assert_eq(ambiguous.status_code,200))
        before=sql(f'SELECT COUNT(*) FROM PasswordResetTokens WHERE UserAccountId={cid}')
        r=smoke.post(sess(),'/Account/ForgotPassword',{'Email':address,'Role':'Client'},'/Account/ForgotPassword',False)
        check('forgot password role request responds generically',lambda:assert_eq(r.status_code,200))
        check('reset email targets selected role',lambda:assert_eq(int(sql(f'SELECT COUNT(*) FROM PasswordResetTokens WHERE UserAccountId={cid}')),int(before)+1))
        print(f'V3_ACCOUNT_ISOLATION_PASSED checks={len(smoke.rows)}')
    finally:
        # Restore only the known disposable fixture for reproducible companion tests.
        sql(f'UPDATE f SET PasswordHash=c.PasswordHash,PasswordChangedAtUtc=NULL FROM Users f CROSS JOIN Users c WHERE f.Id={fid} AND c.Id={cid}')
if __name__=='__main__':main()
