"""Reproduce dispute committing while QueryDR awaits its provider response."""
import argparse,os,subprocess
from pathlib import Path
import urllib3
from ContractsV2E2E import Regression,contracts_gate,local_base,require

def main():
    p=argparse.ArgumentParser();p.add_argument('--run-writes',action='store_true');p.add_argument('--mailbox',type=Path,required=True);a=p.parse_args()
    if not a.run_writes or not a.mailbox.is_absolute() or not a.mailbox.is_dir():p.error('Disposable local write fixtures and mailbox required')
    urllib3.disable_warnings();secret=os.environ.get('NEXUS_VNPAY_HASH_SECRET');require(bool(secret),'Synthetic local sandbox secret required');base=local_base('https://localhost:44391');contracts_gate(base);r=Regression(base,a.mailbox,secret)
    client=r.register('race-client','Client');worker=r.register('race-worker','Freelancer');admin=r.register('race-admin','Client');r.deposit(client,admin,10000)
    pid,roles=r.project(client,'SOLO',['Two-milestone race specialist'])
    r.post(worker,'/Proposals/Apply',{'ProjectId':pid,'RoleRequirementId':roles[0],'QuotedPrice':10000,'DeliveryDays':14,'Pitch':'Independent two milestone handover with documented acceptance','MilestonePlan':'Initial scope | 5000\nFinal delivery | 5000'},f'/Projects/Details/{pid}')
    proposal=int(r.db.scalar(f'SELECT Id FROM dbo.Proposals WHERE ProjectId={pid}'));cid=r.accept(client,pid,proposal);r.confirm(client,cid);r.confirm(worker,cid)
    milestones=r.db.rows(f'SELECT Id FROM dbo.EscrowMilestones WHERE ContractId={cid} ORDER BY Id')
    r.post(client,f"/Wallet/Fund/{milestones[0]['Id']}",{},f'/Contracts/Details/{cid}')
    r.post(client,f"/Payments/Checkout/{milestones[1]['Id']}",{},f'/Contracts/Details/{cid}')
    payment=int(r.db.scalar(f'SELECT Id FROM dbo.PaymentIntents WHERE ContractId={cid}'))
    result=subprocess.run(['powershell','-NoProfile','-ExecutionPolicy','Bypass','-File',str(Path(__file__).with_name('TestPspRefunds.ps1')),'-CapturePaymentId',str(payment),'-ClientId',str(client.uid),'-ProjectId',str(pid)],capture_output=True,text=True,encoding='utf-8',errors='replace')
    print(result.stdout,flush=True)
    if result.returncode:print(result.stderr,flush=True)
    require(result.returncode==0,'QueryDR versus dispute race must preserve unallocated money and existing hold')
    r.db.reconcile();print('PSP_CAPTURE_RACE_E2E_PASSED run='+r.run_id,flush=True)
if __name__=='__main__':main()
