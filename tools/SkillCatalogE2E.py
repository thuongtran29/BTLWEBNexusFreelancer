"""Read-only checks: eight career groups and only complete internal exam banks."""
import argparse,html,re
import requests,urllib3
from SmokeE2E import Smoke
from V3FlowE2E import sql
p=argparse.ArgumentParser();p.add_argument('--fixture-run',required=True);a=p.parse_args()
assert re.fullmatch('[a-f0-9]{12}',a.fixture_run)
urllib3.disable_warnings();s=requests.Session();s.verify=False;t=Smoke('https://localhost:44391')
t.post(s,'/Account/Login',{'Email':f'v3-worker-{a.fixture_run}@example.test','Password':'NexusV3!'+a.fixture_run+'Aa7','Role':'Freelancer'},'/Account/Login')
body=html.unescape(t.get(s,'/SkillEvidence').text)
groups=re.findall(r'<optgroup label="([^"]+)"',body)
expected={'Công nghệ thông tin','Thiết kế & sáng tạo','Marketing & truyền thông','Nội dung & biên dịch','Kinh doanh & tư vấn','Dữ liệu & AI','Kỹ thuật & kiến trúc','Khác'}
assert set(groups)==expected,'Skill choices must cover all eight career groups'
exams=html.unescape(t.get(s,'/Skills').text)
for track in sql("SELECT Id FROM SkillTracks t WHERE (IsActive IS NULL OR IsActive=1) AND (SELECT COUNT(*) FROM SkillExamQuestions q WHERE q.SkillTrackId=t.Id)<5").splitlines():
    assert not re.search(r'/Skills/Exam/'+track+r'(?=["?])',exams), 'Incomplete exam bank must not be offered as a ready exam'
assert 'V3 proof ' not in body and 'Admin Skills E2E ' not in body and 'Timer test ' not in body
print('PASS eight career groups; incomplete exam banks not offered; historical test tracks not public')
