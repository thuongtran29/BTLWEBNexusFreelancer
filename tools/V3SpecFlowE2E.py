"""Additional v3 specification flows: negotiation, plans, skill evidence.
Uses the disposable fixture printed by V3FlowE2E. Simulated time/grades are
seeded only into the named local development database, never a real provider.
"""
import argparse,re,uuid
import requests,urllib3
from SmokeE2E import Smoke,route_id
from V3FlowE2E import sql,assert_eq
from ContractFixture import confirm_project, dispute_contract
def main():
    p=argparse.ArgumentParser();p.add_argument('--run-writes',action='store_true');p.add_argument('--fixture-run',required=True);a=p.parse_args()
    if not a.run_writes or not re.fullmatch('[a-f0-9]{12}',a.fixture_run):p.error('valid disposable fixture and write flag required')
    urllib3.disable_warnings();smoke=Smoke('https://localhost:44391');run=uuid.uuid4().hex[:10];fixture=a.fixture_run;pwd='NexusV3!'+fixture+'Aa7';address=f'v3-{fixture}@example.test'
    def post(s,path,fields,page):return smoke.post(s,path,fields,page)
    def get(s,path,status=200):return smoke.get(s,path,status)
    def check(label,fn):return smoke.check(label,fn)
    def login(email,role):
        s=requests.Session();s.verify=False;post(s,'/Account/Login',{'Email':email,'Role':role,'Password':pwd},'/Account/Login');return s
    cid=int(sql(f"SELECT Id FROM Users WHERE Email='{address}' AND Role='Client'"));fid=int(sql(f"SELECT Id FROM Users WHERE Email='v3-worker-{fixture}@example.test' AND Role='Freelancer'"));aid=int(sql(f"SELECT Id FROM Users WHERE Email='v3-admin-{fixture}@example.test'"));outside_email=f'v3-outsider-{fixture}@example.test';oid=int(sql(f"SELECT Id FROM Users WHERE Email='{outside_email}'"));sql(f"UPDATE Users SET Role='Admin' WHERE Id={aid}")
    try:
        client=login(address,'Client');free=login(f'v3-worker-{fixture}@example.test','Freelancer');admin=login(f'v3-admin-{fixture}@example.test','Admin');outsider=login(outside_email,'Freelancer');guest=requests.Session();guest.verify=False
        def project(kind,count=1):
            r=post(client,'/Projects/Create',{'Title':'V3 spec '+kind+' '+run,'Description':'Documented API contract, scoped deliverables and acceptance tests.','ProjectType':kind,'Budget':10000,'Industry':'Công nghệ thông tin','Deadline':'2027-02-01'},'/Projects/Create');pid=route_id(r.headers['Location']);post(client,'/Projects/AddRequirement',{'ProjectId':pid,'Title':'Backend Developer','RequiredCount':count},f'/Projects/Manage/{pid}');post(client,f'/Projects/Publish/{pid}',{},f'/Projects/Manage/{pid}');rid=int(sql(f'SELECT Id FROM RoleRequirements WHERE ProjectId={pid}'));return pid,rid
        pid,rid=project('SOLO');plan='API prototype | 4000\nFinal API and documentation | 6000'
        post(free,'/Proposals/Apply',{'ProjectId':pid,'RoleRequirementId':rid,'Pitch':'Detailed scope and acceptance handover','QuotedPrice':10000,'DeliveryDays':9,'MilestonePlan':plan},f'/Projects/Details/{pid}');pr=int(sql(f'SELECT Id FROM Proposals WHERE ProjectId={pid}'))
        check('quote delivery days persisted',lambda:assert_eq(sql(f'SELECT DeliveryDays FROM Proposals WHERE Id={pr}'),'9'))
        post(client,f'/Projects/Shortlist/{pid}',{'proposalId':pr},f'/Projects/Manage/{pid}')
        check('client can shortlist individual quote',lambda:assert_eq(sql(f'SELECT Status FROM Proposals WHERE Id={pr}'),'Shortlisted'))
        thread=f'/Negotiation/Thread?proposalId={pr}'
        post(client,'/Negotiation/Send',{'proposalId':pr,'body':'Please confirm API acceptance and milestones.'},thread);post(free,'/Negotiation/Send',{'proposalId':pr,'body':'Confirmed scope and both milestone amounts.'},thread)
        check('two-way pre-contract conversation',lambda:assert_eq(sql(f'SELECT COUNT(*) FROM NegotiationMessages WHERE ProposalId={pr}'),'2'))
        check('outsider cannot read negotiation',lambda:get(outsider,thread,403))
        unauthorized=smoke.post(outsider,'/Negotiation/Send',{'proposalId':pr,'body':'Must not be posted'},'/Profile',False)
        check('outsider cannot post negotiation',lambda:assert_eq(unauthorized.status_code,403))
        post(client,f'/Projects/Accept/{pid}',{'proposalId':pr},f'/Projects/Manage/{pid}');post(client,f'/Projects/LockTeam/{pid}',{},f'/Projects/Manage/{pid}')
        check('accepted plan copied into two escrow milestones',lambda:assert_eq(sql(f'SELECT COUNT(*) FROM EscrowMilestones WHERE ProjectId={pid}'),'2'))
        check('milestone plan preserves contract total',lambda:assert_eq(sql(f'SELECT CONVERT(int,SUM(Amount)) FROM EscrowMilestones WHERE ProjectId={pid}'),'10000'))
        closed=smoke.post(free,'/Negotiation/Send',{'proposalId':pr,'body':'Must use workspace after contract'},thread,False)
        check('negotiation closes after contract',lambda:assert_eq(closed.status_code,409))
        def deposit(amount,ref):
            post(client,'/Wallet/RequestMoney',{'kind':'Deposit','amount':amount,'reference':ref,'details':'Disposable v3 specification fixture'},'/Wallet');req=int(sql(f"SELECT Id FROM MoneyRequests WHERE Reference='{ref}'"));post(admin,f'/Wallet/ReviewMoney/{req}',{'approve':'true','reason':'Specification test fixture only'},'/Wallet/Admin')
        confirm_project(smoke,pid,client,free)
        deposit(10000,'v3-spec-'+run);mids=[int(x) for x in sql(f'SELECT Id FROM EscrowMilestones WHERE ProjectId={pid} ORDER BY Id').splitlines()];wid=int(sql(f'SELECT Id FROM Workspaces WHERE ProjectId={pid}'))
        post(client,f'/Wallet/Fund/{mids[1]}',{},f'/Wallet/Project/{pid}')
        check('funding a later milestone does not bypass first milestone gate',lambda:assert_eq(sql(f'SELECT Status FROM Projects WHERE Id={pid}'),'AwaitingEscrow'))
        for i,mid in enumerate(mids):
            post(client,f'/Wallet/Fund/{mid}',{},f'/Wallet/Project/{pid}');post(free,'/Workspace/Submit',{'WorkspaceId':wid,'EscrowMilestoneId':mid,'Title':'API milestone '+str(i+1),'Url':'https://example.test/spec'},f'/Workspace/Index/{wid}')
            if i==0:
                did=int(sql(f'SELECT MAX(Id) FROM Deliverables WHERE WorkspaceId={wid}'));post(client,f'/Workspace/Decide/{did}',{'decision':'Approved'},f'/Workspace/Index/{wid}')
                check('first milestone leaves contract active',lambda:assert_eq(sql(f'SELECT Status FROM Projects WHERE Id={pid}'),'InProgress'))
        post(free,f'/Wallet/Dispute/{pid}',{'contractId':dispute_contract(pid),'reason':'Full refund for the second milestone after scope disagreement'},f'/Wallet/Project/{pid}');d=int(sql(f'SELECT Id FROM EscrowDisputes WHERE ProjectId={pid}'));post(admin,f'/Wallet/Resolve/{d}',{'refund':6000,'reason':'Return all remaining funded milestone to the customer'},'/Wallet/Admin')
        check('full disputed milestone refunded',lambda:assert_eq(sql(f"SELECT Status FROM EscrowMilestones WHERE Id={mids[1]}"),'Refunded'))
        check('full refund incurs zero fee',lambda:assert_eq(sql(f'SELECT CONVERT(int,Fee) FROM EscrowMilestones WHERE Id={mids[1]}'),'0'))
        check('full remaining refund closes cancelled contract',lambda:assert_eq(sql(f'SELECT Status FROM Projects WHERE Id={pid}'),'Cancelled'))
        # Group negotiation is shared with the leader only, never other applicants.
        group=post(free,'/Groups/Create',{'Name':'V3 spec group '+run,'Description':'Backend integration team'},'/Groups');gid=int(re.search(r'/Details/(\d+)',group.headers['Location']).group(1));post(free,'/Groups/Invite',{'GroupId':gid,'Email':outside_email},f'/Groups/Details/{gid}');post(outsider,f'/Groups/Respond/{gid}',{'accept':'true'},'/Groups');gp,gr=project('TEAM',2)
        fields={'ProjectId':gp,'FreelancerGroupId':gid,'Pitch':'Two person API team','DeliveryDays':10,'MilestonePlan':plan}
        for i,uid in enumerate([fid,oid]):fields.update({f'Candidates[{i}].Selected':'true',f'Candidates[{i}].UserAccountId':uid,f'Candidates[{i}].RoleRequirementId':gr,f'Candidates[{i}].QuotedPrice':5000})
        post(free,'/GroupBids/Create',fields,f'/GroupBids/Create?groupId={gid}&projectId={gp}');gpr=int(sql(f'SELECT MIN(Id) FROM Proposals WHERE ProjectId={gp}'));gb=int(sql(f'SELECT Id FROM GroupBids WHERE ProjectId={gp}'));post(client,f'/Projects/Shortlist/{gp}',{'proposalId':gpr},f'/Projects/Manage/{gp}')
        check('shortlist group updates all candidate quotes',lambda:assert_eq(sql(f"SELECT COUNT(*) FROM Proposals WHERE GroupBidId={gb} AND Status='Shortlisted'"),'2'))
        check('shortlist group updates bid',lambda:assert_eq(sql(f'SELECT Status FROM GroupBids WHERE Id={gb}'),'Shortlisted'))
        gt=f'/Negotiation/Thread?proposalId={gpr}';post(client,'/Negotiation/Send',{'proposalId':gpr,'body':'Leader please confirm the group agreement'},gt)
        check('leader reads group negotiation',lambda:get(free,gt))
        check('group member cannot impersonate leader in negotiation',lambda:get(outsider,gt,403))
        # Admin skill evidence: independent from internal exam badges.
        skills=[]
        for i in range(3):
            code='V3E_'+run+'_'+str(i);post(admin,'/Admin/CreateSkill',{'Code':code,'Title':'V3 proof '+run+' '+str(i),'Description':'Verified API skill evidence','MatchKeywords':'backend api','Industry':'Công nghệ thông tin','IsActive':'true'},'/Admin/Skills');skills.append(int(sql(f"SELECT Id FROM SkillTracks WHERE Code='{code.upper()}'")))
        claims=[]
        for skill in skills:
            post(free,'/SkillEvidence/SaveClaim',{'skillId':skill,'level':3,'years':2},'/SkillEvidence');claims.append(int(sql(f'SELECT Id FROM SkillClaims WHERE UserAccountId={fid} AND SkillTrackId={skill}')))
        def certificate(claim,expires='2027-06-01'):
            return {'claimId':claim,'evidence[0].Type':'Certificate','evidence[0].Url':'https://example.test/certificate','evidence[0].Issuer':'V3 fixture organization','evidence[0].IssuedOn':'2026-01-01','evidence[0].ExpiresOn':expires}
        post(free,'/SkillEvidence/Submit',certificate(claims[0]),'/SkillEvidence');post(free,'/SkillEvidence/Submit',certificate(claims[0]),'/SkillEvidence')
        r1=int(sql(f'SELECT Id FROM SkillEvidenceRequests WHERE SkillClaimId={claims[0]}'))
        check('only one pending request for a skill',lambda:assert_eq(sql(f'SELECT COUNT(*) FROM SkillEvidenceRequests WHERE SkillClaimId={claims[0]}'),'1'))
        forbidden=smoke.post(free,'/SkillEvidence/Review',{'id':r1,'approve':'true','reason':'Cannot self verify'},'/SkillEvidence',False)
        check('freelancer cannot self verify',lambda:assert_eq(forbidden.status_code,403))
        post(admin,f'/SkillEvidence/Review/{r1}',{'approve':'true','reason':'Reviewed certificate evidence for this skill'},'/SkillEvidence/Admin')
        check('admin approval verifies declared skill',lambda:assert_eq(sql(f'SELECT Status FROM SkillEvidenceRequests WHERE Id={r1}'),'Approved'))
        post(free,'/SkillEvidence/SaveClaim',{'skillId':skills[0],'level':5,'years':4},'/SkillEvidence')
        check('editing level preserves valid verification',lambda:assert_eq(sql(f'SELECT Status FROM SkillEvidenceRequests WHERE Id={r1}'),'Approved'))
        public=get(guest,f'/Talent/Freelancer/{fid}')
        check('approved skill visible on public profile',lambda:assert_eq('V3 proof '+run+' 0' in public.text,True))
        group_page=get(guest,f'/Talent/Group/{gid}')
        check('group inherits active member verified skill',lambda:assert_eq('V3 proof '+run+' 0' in group_page.text,True))
        sql(f"UPDATE SkillEvidence SET IssuedOn=DATEADD(day,-10,CONVERT(date,GETDATE())),ExpiresOn=DATEADD(day,-1,CONVERT(date,GETDATE())) WHERE SkillEvidenceRequestId={r1}; UPDATE SkillEvidenceRequests SET ValidUntil=DATEADD(day,-1,CONVERT(date,GETDATE())) WHERE Id={r1}")
        check('expired skill hidden immediately',lambda:assert_eq('V3 proof '+run+' 0' in get(guest,f'/Talent/Freelancer/{fid}').text,False))
        post(admin,'/Wallet/ProcessDue',{},'/Wallet/Admin')
        check('expiry worker changes request status',lambda:assert_eq(sql(f'SELECT Status FROM SkillEvidenceRequests WHERE Id={r1}'),'Expired'))
        # The score is copied from a seeded actual exam-result row; browser Score is ignored.
        exam=int(sql(f'INSERT SkillExamAttempts(UserAccountId,SkillTrackId,QuestionCount,CorrectCount,[Percent],Passed,SubmittedAtUtc) VALUES({fid},{skills[0]},5,5,100,1,GETUTCDATE()); SELECT SCOPE_IDENTITY()'))
        post(free,'/SkillEvidence/Submit',{'claimId':claims[0],'evidence[0].Type':'Test','evidence[0].SkillExamAttemptId':exam,'evidence[0].Score':1},'/SkillEvidence');rt=int(sql(f'SELECT MAX(Id) FROM SkillEvidenceRequests WHERE SkillClaimId={claims[0]}'))
        check('test evidence score comes from server record',lambda:assert_eq(sql(f'SELECT Score FROM SkillEvidence WHERE SkillEvidenceRequestId={rt}'),'100'))
        post(admin,f'/SkillEvidence/Review/{rt}',{'approve':'true','reason':'Reviewed linked exam record for this declared skill'},'/SkillEvidence/Admin')
        check('reviewed test evidence restores valid verification',lambda:assert_eq(sql(f'SELECT Status FROM SkillEvidenceRequests WHERE Id={rt}'),'Approved'))
        post(free,'/SkillEvidence/Submit',certificate(claims[1]),'/SkillEvidence');rr=int(sql(f'SELECT Id FROM SkillEvidenceRequests WHERE SkillClaimId={claims[1]}'));post(admin,f'/SkillEvidence/Review/{rr}',{'approve':'false','reason':'Need independently verifiable issuing organization'},'/SkillEvidence/Admin');post(free,'/SkillEvidence/Submit',certificate(claims[1]),'/SkillEvidence')
        check('rejection cooldown blocks immediate resubmission',lambda:assert_eq(sql(f'SELECT COUNT(*) FROM SkillEvidenceRequests WHERE SkillClaimId={claims[1]}'),'1'))
        check('rejection reason shown to owner',lambda:assert_eq('Need independently verifiable issuing organization' in get(free,'/SkillEvidence').text,True))
        post(free,'/SkillEvidence/Submit',certificate(claims[2]),'/SkillEvidence');post(free,f'/SkillEvidence/DeleteClaim/{claims[2]}',{},'/SkillEvidence')
        check('deleting skill cancels pending evidence request',lambda:assert_eq(sql(f'SELECT Status FROM SkillEvidenceRequests WHERE SkillClaimId={claims[2]}'),'Cancelled'))
        check('evidence administration is role protected',lambda:get(client,'/SkillEvidence/Admin',403))
        result=get(guest,f'/Talent?kind=freelancer&skillId={skills[0]}&verifiedOnly=true&sort=az')
        check('talent filters verified skill on person',lambda:assert_eq(f'href="/Talent/Freelancer/{fid}"' in result.text,True))
        result=get(guest,f'/Talent?kind=freelancer&skillId={skills[1]}&verifiedOnly=true')
        check('talent excludes rejected skill evidence',lambda:assert_eq(f'href="/Talent/Freelancer/{fid}"' in result.text,False))
        result=get(guest,f'/Talent?kind=group&skillId={skills[0]}&verifiedOnly=true')
        check('talent filters verified skill union on group',lambda:assert_eq(f'href="/Talent/Group/{gid}"' in result.text,True))
        check('verified person filter works without skill id',lambda:get(guest,'/Talent?verifiedOnly=true'))
        check('verified group filter works without skill id',lambda:get(guest,'/Talent?kind=group&verifiedOnly=true'))
        sql(f'UPDATE FreelancerProfiles SET IsPublic=0 WHERE UserAccountId={fid}')
        try:
            check('skill search respects private person profile',lambda:assert_eq(f'href="/Talent/Freelancer/{fid}"' in get(guest,f'/Talent?skillId={skills[0]}&verifiedOnly=true').text,False))
            check('skill search respects private group leader',lambda:assert_eq(f'href="/Talent/Group/{gid}"' in get(guest,f'/Talent?kind=group&skillId={skills[0]}&verifiedOnly=true').text,False))
        finally:sql(f'UPDATE FreelancerProfiles SET IsPublic=1 WHERE UserAccountId={fid}')
        post(outsider,f'/Groups/Leave/{gid}',{},f'/Groups/Details/{gid}')
        check('group membership may change after bid snapshot',lambda:assert_eq(sql(f'SELECT Status FROM FreelancerGroupMembers WHERE FreelancerGroupId={gid} AND UserAccountId={oid}'),'Left'))
        post(client,f'/GroupBids/Decide/{gb}',{'accept':'true'},f'/Projects/Manage/{gp}');post(client,f'/Projects/LockTeam/{gp}',{},f'/Projects/Manage/{gp}')
        check('submitted roster survives later group change',lambda:assert_eq(sql(f'SELECT COUNT(*) FROM EscrowShares WHERE ProjectId={gp}'),'2'))
        group_workspace=int(sql(f'SELECT Id FROM Workspaces WHERE ProjectId={gp}'))
        check('assigned member retains project access after leaving source group',lambda:get(outsider,f'/Workspace/Index/{group_workspace}'))
        print(f'V3_SPEC_FLOW_PASSED checks={len(smoke.rows)} run={run}')
    finally:sql(f"UPDATE Users SET Role='Client' WHERE Id={aid}")
if __name__=='__main__':main()
