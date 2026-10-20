"""V3 embedded job creation flow and industry preservation. Uses a known disposable fixture."""
import argparse,re,uuid
import requests,urllib3
from SmokeE2E import Smoke
from V3FlowE2E import sql,assert_eq
def main():
    p=argparse.ArgumentParser();p.add_argument('--fixture-run',required=True);p.add_argument('--run-writes',action='store_true');a=p.parse_args()
    if not a.run_writes or not re.fullmatch('[a-f0-9]{12}',a.fixture_run):p.error('valid fixture and write flag required')
    urllib3.disable_warnings();s=requests.Session();s.verify=False;test=Smoke('https://localhost:44391');run=uuid.uuid4().hex[:10]
    test.post(s,'/Account/Login',{'Email':f'v3-{a.fixture_run}@example.test','Role':'Client','Password':'NexusV3!'+a.fixture_run+'Aa7'},'/Account/Login')
    page=test.get(s,'/Projects/Create')
    test.check('AI entry present inside create project',lambda:assert_eq('AI Job Studio' in page.text,True))
    dashboard=test.get(s,'/Dashboard')
    test.check('dashboard has no standalone JobStudio link',lambda:assert_eq('href="/JobStudio' in dashboard.text,False))
    r=test.post(s,'/JobStudio/Generate',{'Industry':'Thiết kế & sáng tạo','Idea':'Create a design system and prototypes '+run,'ProjectType':'SOLO','Budget':10000,'Deadline':'2027-02-01','UseExternalAi':'false'},'/JobStudio/New')
    draft=int(re.search(r'/Edit/(\d+)',r.headers['Location']).group(1));edit=test.get(s,r.headers['Location'])
    test.check('industry-based solo role suggested',lambda:assert_eq('UI/UX Designer' in edit.text,True))
    test.post(s,f'/JobStudio/Apply/{draft}',{},f'/JobStudio/Edit/{draft}')
    pid=int(sql(f'SELECT AppliedProjectId FROM AIJobDrafts WHERE Id={draft}'))
    test.check('AI draft creates unpublished project',lambda:assert_eq(sql(f'SELECT Status FROM Projects WHERE Id={pid}'),'Draft'))
    test.check('AI industry survives project creation',lambda:assert_eq(sql(f"SELECT COUNT(*) FROM Projects WHERE Id={pid} AND Industry=N'Thiết kế & sáng tạo'"),'1'))
    test.post(s,f'/JobStudio/Apply/{draft}',{},f'/JobStudio/Edit/{draft}')
    test.check('repeated apply preserves same project',lambda:assert_eq(sql(f'SELECT AppliedProjectId FROM AIJobDrafts WHERE Id={draft}'),str(pid)))
    print(f'V3_JOB_STUDIO_PASSED checks={len(test.rows)}')
if __name__=='__main__':main()
