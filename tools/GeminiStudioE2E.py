import sys,json,re,argparse
from pathlib import Path
from html import unescape
import urllib3
sys.path.insert(0,str(Path('outputs/NexusFreelance/tools').resolve()))
from PlatformJourneyE2E import Journey,Fields
urllib3.disable_warnings();j=Journey('https://localhost:44392','NexusFreelanceAudit20261019');s=j.login('client.minhan','Client')
p=j.get(s,'/JobStudio/New');text=unescape(p.text)
j.check('Google Gemini' in text and 'Free Tier' in text,'Gemini provider and free-quota notice shown')
f=Fields(p.text).values;j.check(f.get('ExternalAiProvider')=='Gemini','Consent form binds the actual provider')
j.check(not re.search(r'<input[^>]*checked[^>]*name="UseProjectKnowledge"',p.text) and not re.search(r'<input[^>]*name="UseProjectKnowledge"[^>]*checked',p.text),'Private project memory is opt-in for Gemini free-tier demo')
j.check('fixture-audit-ui-only-invalid-key' not in p.text and 'JobPrompt.System' not in p.text,'No server key or hidden prompt exposed')
before=int(j.sql('SELECT COUNT(*) FROM dbo.AIJobDrafts'))
brief={'Idea':'Thiết kế logo cho tiệm bánh minh họa '+j.tag,'Industry':'Thiết kế & sáng tạo','ProjectType':'SOLO','Budget':5000000,'UseExternalAi':'true','ExternalAiProvider':'OpenAI','UseProjectKnowledge':'false','UseSharedKnowledge':'true'}
p=j.post(s,'/JobStudio/Generate',brief,form='/JobStudio/New')
j.check(int(j.sql('SELECT COUNT(*) FROM dbo.AIJobDrafts'))==before and 'Nhà cung cấp AI đã thay đổi' in unescape(p.text),'Stale or forged provider consent cannot transmit or save a draft')
invalid=j.post(s,'/JobStudio/Generate',{**brief,'Idea':'','Budget':0},form='/JobStudio/New')
j.check('Nhà cung cấp AI đã thay đổi' in unescape(invalid.text) and not re.search(r'<input[^>]*(?:checked[^>]*name="UseExternalAi"|name="UseExternalAi"[^>]*checked)',invalid.text),'Invalid brief cannot carry old checked consent into a different provider')
p=j.post(s,'/JobStudio/Generate',{**brief,'ExternalAiProvider':'Gemini','UseExternalAi':'false'},form='/JobStudio/New');m=re.search(r'/JobStudio/Edit/(\d+)',p.url);assert m,'Local draft not created';did=int(m.group(1))
j.check(j.sql(f'SELECT Generator FROM dbo.AIJobDraftVersions WHERE AIJobDraftId={did}')=='RuleBasedDemo-v2','Explicit local choice remains local while Gemini configured')
j.check('Mẫu nội bộ theo quy tắc' in unescape(p.text),'Draft editor honestly labels local result')
j.check('data-role-match=' in p.text and 'Graphic Designer' in p.text,'Candidate preview remains available for generated hiring role')
p=j.post(s,f'/JobStudio/Apply/{did}',form=f'/JobStudio/Edit/{did}');pid=int(re.search(r'/Projects/Manage/(\d+)',p.url).group(1))
j.check(j.sql(f'SELECT COUNT(*) FROM dbo.ProjectSkillRequirements WHERE ProjectId={pid}')=='1','Local JD still maps verified skills after provider extension')
j.check(j.sql(f'SELECT COUNT(*) FROM dbo.RoleRequirements WHERE ProjectId={pid}')=='1','SOLO still creates exactly one hiring position')
for route in ['/Knowledge','/Knowledge/Completed','/Projects/Manage/'+str(pid)]:j.check(j.get(s,route).status_code==200,'Existing workflow '+route)
result={'checks':j.checks,'draftId':did,'projectId':pid};Path('work/gemini-20261019/http.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8');print('GEMINI_STUDIO_HTTP_CHECKS='+str(len(j.checks)))
