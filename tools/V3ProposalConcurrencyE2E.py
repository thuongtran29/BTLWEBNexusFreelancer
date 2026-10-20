"""Regression for the observed Proposals unique-index supremum-gap deadlock.
Posts quotes simultaneously to different projects, alongside aggregate invariants.
Disposable local NexusFreelanceMvp fixtures only.
"""
import argparse,re,threading,uuid
from concurrent.futures import ThreadPoolExecutor
import requests,urllib3
from SmokeE2E import Smoke,forms,route_id
from V3FlowE2E import sql,assert_eq
def main():
    p=argparse.ArgumentParser();p.add_argument('--run-writes',action='store_true');p.add_argument('--fixture-run',required=True);a=p.parse_args()
    if not a.run_writes or not re.fullmatch('[a-f0-9]{12}',a.fixture_run):p.error('valid fixture and write flag required')
    urllib3.disable_warnings();test=Smoke('https://localhost:44391');fixture=a.fixture_run;pwd='NexusV3!'+fixture+'Aa7';address=f'v3-{fixture}@example.test';run=uuid.uuid4().hex[:10]
    def login(role):
        s=requests.Session();s.verify=False;test.post(s,'/Account/Login',{'Email':address,'Role':role,'Password':pwd},'/Account/Login');return s
    client=login('Client');free=login('Freelancer')
    def make_project(round_number,side):
        r=test.post(client,'/Projects/Create',{'Title':f'Concurrent quote {run} {round_number} {side}','Description':'Independent aggregate insertion regression','ProjectType':'SOLO','Industry':'Công nghệ thông tin','Budget':10000},'/Projects/Create');pid=route_id(r.headers['Location']);test.post(client,'/Projects/AddRequirement',{'ProjectId':pid,'Title':'Backend Developer','RequiredCount':1},f'/Projects/Manage/{pid}');test.post(client,f'/Projects/Publish/{pid}',{},f'/Projects/Manage/{pid}');rid=int(sql(f'SELECT Id FROM RoleRequirements WHERE ProjectId={pid}'))
        response=test.get(free,f'/Projects/Details/{pid}');token=next(f['fields']['__RequestVerificationToken'] for f in forms(response.text) if '__RequestVerificationToken' in f['fields'])
        return pid,rid,token
    for round_number in range(5):
        projects=[make_project(round_number,side) for side in range(2)];gate=threading.Barrier(2)
        def apply(item):
            pid,rid,token=item;s=requests.Session();s.verify=False;s.cookies.update(free.cookies);gate.wait(timeout=15)
            return s.post(test.base+'/Proposals/Apply',data={'__RequestVerificationToken':token,'ProjectId':pid,'RoleRequirementId':rid,'Pitch':'Atomic quote with matching snapshot','QuotedPrice':10000,'DeliveryDays':14},allow_redirects=False,timeout=60).status_code
        with ThreadPoolExecutor(max_workers=2) as pool:statuses=list(pool.map(apply,projects))
        test.check(f'parallel quote round {round_number+1} controlled success',lambda:assert_eq(statuses,[302,302]))
        ids=','.join(str(x[0]) for x in projects)
        test.check(f'parallel quote round {round_number+1} both persisted',lambda:assert_eq(sql(f'SELECT COUNT(*) FROM Proposals WHERE ProjectId IN({ids})'),'2'))
        test.check(f'parallel quote round {round_number+1} both have matching snapshots',lambda:assert_eq(sql(f'SELECT COUNT(*) FROM MatchingResults m JOIN Proposals p ON p.Id=m.ProposalId WHERE p.ProjectId IN({ids})'),'2'))
    print(f'V3_PROPOSAL_CONCURRENCY_PASSED checks={len(test.rows)} run={run}')
if __name__=='__main__':main()
