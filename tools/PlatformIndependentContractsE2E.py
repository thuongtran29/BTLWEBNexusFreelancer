"""Two independent contracts, isolated audit DB only. Leaves one PSP fixture unfunded."""
import argparse
import json
import re
from pathlib import Path
import urllib3
from PlatformJourneyE2E import Journey


def run(j):
    client=j.login('client.quanghuy','Client'); front=j.login('minh.frontend','Freelancer'); back=j.login('linh.backend','Freelancer')
    p=j.post(client,'/Projects/Create',{'Title':j.tag+' nhiều hợp đồng','Description':'Giao diện và API là hai phạm vi tách biệt với ngân sách và nghiệm thu riêng.','Industry':'Công nghệ thông tin','ProjectType':'ENTERPRISE','Budget':40000000,'SkillIds':[1,2],'MinimumSkillLevel':1},form='/Projects/Create')
    pid=int(re.search(r'/Projects/Manage/(\d+)',p.url).group(1))
    for role in ['Frontend Developer','Backend Developer']:j.post(client,'/Projects/AddRequirement',{'ProjectId':pid,'Title':role,'RequiredCount':1},form=f'/Projects/Manage/{pid}')
    j.post(client,f'/Projects/Publish/{pid}',form=f'/Projects/Manage/{pid}')
    roles=[int(x) for x in j.sql(f'SELECT Id FROM dbo.RoleRequirements WHERE ProjectId={pid} ORDER BY Id').splitlines()]
    for session,role in zip([front,back],roles):
        j.post(session,'/Proposals/Apply',{'ProjectId':pid,'RoleRequirementId':role,'Pitch':'Triển khai phạm vi riêng, kiểm thử và bàn giao độc lập.','QuotedPrice':10000000,'DeliveryDays':14},form=f'/Projects/Details/{pid}')
    proposals=[int(x) for x in j.sql(f'SELECT Id FROM dbo.Proposals WHERE ProjectId={pid} ORDER BY Id').splitlines()]
    for proposal in proposals:j.post(client,f'/Projects/Accept/{pid}',{'proposalId':proposal},form=f'/Projects/Manage/{pid}')
    contracts=[int(x) for x in j.sql(f'SELECT Id FROM dbo.Contracts WHERE ProjectId={pid} ORDER BY Id').splitlines()]
    c1,c2=contracts
    j.check(len(contracts)==2,'Two selected individual bids create two separate contracts')
    version,digest=j.sql(f"SELECT CAST(Id AS varchar)+'|'+Hash FROM dbo.ContractVersions WHERE ContractId={c1}").split('|')
    for session in [client,front]:j.post(session,f'/Contracts/Confirm/{c1}',{'versionId':version,'hash':digest},form=f'/Contracts/Details/{c1}')
    j.check(j.sql(f'SELECT Status FROM dbo.Contracts WHERE Id={c2}')=='AwaitingSignatures','Confirming one contract does not confirm another')
    m1=int(j.sql(f'SELECT Id FROM dbo.EscrowMilestones WHERE ContractId={c1}'));m2=int(j.sql(f'SELECT Id FROM dbo.EscrowMilestones WHERE ContractId={c2}'))
    j.post(client,f'/Wallet/Fund/{m1}',form=f'/Contracts/Details/{c1}')
    j.check(j.sql(f'SELECT Status FROM dbo.EscrowMilestones WHERE Id={m2}')=='Planned','Funding one contract does not fund the other')
    w1=int(j.sql(f'SELECT Id FROM dbo.Workspaces WHERE ContractId={c1}'));w2=int(j.sql(f'SELECT Id FROM dbo.Workspaces WHERE ContractId={c2}'))
    j.check(j.get(back,f'/Workspace/Index/{w1}').status_code==403 and j.get(front,f'/Workspace/Index/{w2}').status_code==403,'Members of different contracts cannot cross into each others workspaces')
    j.post(front,'/Workspace/Submit',{'WorkspaceId':w1,'Title':'Bàn giao giao diện độc lập','Url':'https://example.com/front','EscrowMilestoneId':m1},form=f'/Workspace/Index/{w1}')
    delivery=int(j.sql(f'SELECT Id FROM dbo.Deliverables WHERE WorkspaceId={w1}'))
    j.post(client,f'/Workspace/Decide/{delivery}',{'decision':'Approved'},form=f'/Workspace/Index/{w1}')
    j.check(j.sql(f'SELECT Status FROM dbo.Contracts WHERE Id={c1}')=='Completed' and j.sql(f'SELECT Status FROM dbo.Contracts WHERE Id={c2}')=='AwaitingSignatures','Acceptance completes only the delivered contract')
    j.check(j.sql(f'SELECT COUNT(*) FROM dbo.WalletEntries WHERE EscrowMilestoneId={m2}')=='0','Independent unfunded contract has no ledger mutations')
    version,digest=j.sql(f"SELECT CAST(Id AS varchar)+'|'+Hash FROM dbo.ContractVersions WHERE ContractId={c2}").split('|')
    for session in [client,back]:j.post(session,f'/Contracts/Confirm/{c2}',{'versionId':version,'hash':digest},form=f'/Contracts/Details/{c2}')
    j.check(j.sql(f'SELECT Status FROM dbo.Contracts WHERE Id={c2}')=='AwaitingEscrow','Second contract advances independently and provides PSP regression fixture')
    hashes=j.sql(f'SELECT Hash FROM dbo.ContractVersions WHERE ContractId IN ({c1},{c2}) ORDER BY Id')
    j.post(client,f'/Projects/UpdateSkills/{pid}',{'skillIds':[1,2],'minimumSkillLevel':2},form=f'/Projects/Manage/{pid}')
    j.check(j.sql(f'SELECT MIN(MinimumLevel) FROM dbo.ProjectSkillRequirements WHERE ProjectId={pid}')=='2','Client can update discovery core skills on an existing published project')
    j.check(j.sql(f'SELECT Hash FROM dbo.ContractVersions WHERE ContractId IN ({c1},{c2}) ORDER BY Id')==hashes,'Updating discovery skills does not rewrite signed contract terms')
    p=j.post(back,f'/Projects/UpdateSkills/{pid}',{'skillIds':[1],'minimumSkillLevel':5},form=f'/Projects/Details/{pid}')
    j.check(p.status_code==403 and j.sql(f'SELECT MIN(MinimumLevel) FROM dbo.ProjectSkillRequirements WHERE ProjectId={pid}')=='2','Freelancer cannot change client recruitment core skills')
    # Malformed IDs and invalid filter values should reject input, not expose MVC/IIS errors.
    for path in ['/Workspace/Index/not-a-number','/Projects/Manage/not-a-number','/Contracts/Details/not-a-number','/Notifications?page=bad','/Talent?skillId=bad']:
        p=j.get(client,path);j.check(p.status_code==400 and 'Server Error' not in p.text and 'Detailed Error' not in p.text,'Malformed request gets controlled 400 '+path)
    print('PLATFORM_INDEPENDENT_CONTRACT_CHECKS='+str(len(j.checks)),flush=True)
    return {'checks':j.checks,'projectId':pid,'contracts':contracts,'workspaces':[w1,w2]}


if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--base',required=True);p.add_argument('--database',required=True);p.add_argument('--result',required=True);a=p.parse_args();urllib3.disable_warnings(urllib3.exceptions.InsecureRequestWarning)
    Path(a.result).write_text(json.dumps(run(Journey(a.base,a.database)),ensure_ascii=False,indent=2),encoding='utf-8')
