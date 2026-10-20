"""Local HTTP fixtures + real SQL refund service with a fake provider boundary.
No request is sent to VNPay. Requires the synthetic sandbox secret of local IIS.
"""
import argparse,json,os,subprocess,uuid
from pathlib import Path
import urllib3
from ContractsV2E2E import Regression,contracts_gate,local_base,require,sql_literal

def main():
    p=argparse.ArgumentParser();p.add_argument('--run-writes',action='store_true');p.add_argument('--mailbox',type=Path,required=True);p.add_argument('--base',default='https://localhost:44391');a=p.parse_args()
    if not a.run_writes or not a.mailbox.is_absolute() or not a.mailbox.is_dir():p.error('Explicit local write flag and existing absolute pickup directory required')
    secret=os.environ.get('NEXUS_VNPAY_HASH_SECRET');require(bool(secret),'Local synthetic sandbox secret required')
    urllib3.disable_warnings();base=local_base(a.base);contracts_gate(base);r=Regression(base,a.mailbox,secret)
    client=r.register('refund-client','Client');worker=r.register('refund-worker','Freelancer');admin=r.register('refund-reviewer','Client')
    ids=[];contracts=[];guard=f"Id={admin.uid} AND Email={sql_literal(admin.address)}"
    r.db.execute(f"UPDATE dbo.Users SET Role='Admin' WHERE {guard} AND Role='Client'")
    try:
        r.post(admin,'/Account/Login',{'Email':admin.address,'Role':'Admin','Password':r.password},'/Account/Login')
        for i in range(3):
            pid,roles=r.project(client,'SOLO',['Refund integration specialist'])
            proposal=r.proposal(worker,pid,roles[0],10000);cid=r.accept(client,pid,proposal);r.confirm(client,cid);r.confirm(worker,cid)
            milestone=r.milestone(cid)
            r.post(client,f"/Payments/Checkout/{milestone['Id']}",{},f'/Contracts/Details/{cid}')
            payment=r.db.one(f'SELECT Id,TxnRef,MerchantCode,Amount FROM dbo.PaymentIntents WHERE ContractId={cid}','refund payment fixture')
            fields={'vnp_TmnCode':payment['MerchantCode'],'vnp_TxnRef':payment['TxnRef'],'vnp_Amount':str(int(payment['Amount'])*100),'vnp_CurrCode':'VND','vnp_ResponseCode':'00','vnp_TransactionStatus':'00','vnp_TransactionNo':str(int(uuid.uuid4().hex[:12],16))}
            require(r.ipn(r.callback(fields))=='00','Refund fixture must first capture a genuine signed local callback')
            r.post(client,f'/Wallet/Dispute/{pid}',{'contractId':cid,'reason':'Disposable refund fixture requires complete milestone refund'},f'/Contracts/Details/{cid}')
            dispute=int(r.db.scalar(f'SELECT Id FROM dbo.EscrowDisputes WHERE ContractId={cid}'))
            r.post(admin,f'/Wallet/Resolve/{dispute}',{'refund':10000,'reason':'Local fixture adjudication returns full held amount to client'},'/Wallet/Admin')
            require(r.milestone(cid)['Status']=='Refunded','Refund fixture must complete escrow adjudication before PSP refund')
            ids.append(int(payment['Id']))
            contracts.append(cid)
    finally:r.db.execute(f"UPDATE dbo.Users SET Role='Client' WHERE {guard} AND Role='Admin'")
    result=subprocess.run(['powershell','-NoProfile','-ExecutionPolicy','Bypass','-File',str(Path(__file__).with_name('TestPspRefunds.ps1')),
        '-AdminId',str(admin.uid),'-PaymentOne',str(ids[0]),'-PaymentTwo',str(ids[1]),'-PaymentThree',str(ids[2])],capture_output=True,text=True,encoding='utf-8',errors='replace')
    print(result.stdout,flush=True)
    if result.returncode:print(result.stderr,flush=True)
    require(result.returncode==0,'PSP SQL refund harness must pass')
    for cid,payment in zip(contracts,ids):
        page=r.get(client,f'/Contracts/Details/{cid}')
        require('Yêu cầu hoàn #' in page.text,'Client must see refund history for own contract')
        require(not any('/Payments/Refund/' in form['action'] or '/Payments/ReconcileRefund/' in form['action'] for form in r.forms(page.text)), 'Client must not receive administrator refund controls')
        refund=int(r.db.scalar(f'SELECT Id FROM dbo.PspRefunds WHERE PaymentIntentId={payment}'))
        r.deny(client,f'/Payments/Refund/{payment}',{'amount':1,'reason':'Owner must never bypass refund administration'},'owner cannot send PSP refund',f'/Contracts/Details/{cid}',private=True)
        r.deny(client,f'/Payments/ReconcileRefund/{refund}',{},'owner cannot perform admin refund reconciliation',f'/Contracts/Details/{cid}',private=True)
    r.pass_check('client reads refund history while admin refund commands remain protected')
    # A cancelled/failed checkout may report a late success after an alternative
    # wallet hold. Receive the money once, but do not recreate or enlarge that hold.
    pid,roles=r.project(client,'SOLO',['Late callback specialist'])
    cid=r.accept(client,pid,r.proposal(worker,pid,roles[0],5000));r.confirm(client,cid);r.confirm(worker,cid)
    milestone=r.milestone(cid);mid=milestone['Id']
    r.post(client,f'/Payments/Checkout/{mid}',{},f'/Contracts/Details/{cid}')
    payment=r.db.one(f'SELECT Id,TxnRef,MerchantCode FROM dbo.PaymentIntents WHERE ContractId={cid}','late payment fixture')
    fields={'vnp_TmnCode':payment['MerchantCode'],'vnp_TxnRef':payment['TxnRef'],'vnp_Amount':'500000','vnp_CurrCode':'VND','vnp_ResponseCode':'24','vnp_TransactionStatus':'00','vnp_TransactionNo':'0'}
    before=r.db.wallet(client.uid)
    require(r.ipn(r.callback(fields))=='00' and r.db.wallet(client.uid)==before and r.milestone(cid)['Status']=='Planned','ResponseCode failure with success status must not receive money')
    r.pass_check('both success fields are required before crediting payment')
    fields.update(vnp_ResponseCode='00',vnp_TransactionStatus='02')
    require(r.ipn(r.callback(fields))=='00' and r.db.wallet(client.uid)==before,'API success with failed transaction must not receive money')
    r.pass_check('successful response code alone never creates a capture')
    r.post(client,f'/Wallet/Fund/{mid}',{},f'/Contracts/Details/{cid}')
    require(r.milestone(cid)['Status']=='Funded','alternative wallet hold must genuinely exist')
    r.post(client,f'/Wallet/Dispute/{pid}',{'contractId':cid,'reason':'Late provider response must not restart disputed contract'},f'/Contracts/Details/{cid}')
    before=r.db.wallet(client.uid);fields.update(vnp_ResponseCode='00',vnp_TransactionStatus='00',vnp_TransactionNo=str(int(uuid.uuid4().hex[:12],16)))
    valid=r.callback(fields)
    require(r.ipn(valid)=='00' and r.db.wallet(client.uid)==(before[0]+5000,before[1]),'late received money must stay available without another escrow hold')
    require(r.db.scalar(f"SELECT Status FROM dbo.PaymentIntents WHERE Id={payment['Id']}")=='PaidUnallocated' and r.db.scalar(f'SELECT Status FROM dbo.Contracts WHERE Id={cid}')=='Disputed','late PSP success cannot resume disputed work')
    require(r.db.scalar(f"SELECT COUNT(*) FROM dbo.WalletEntries WHERE EscrowMilestoneId={mid} AND Kind='EscrowHold'")=='1','late capture must not create a second hold')
    after=r.db.wallet(client.uid);require(r.ipn(valid)=='02' and r.db.wallet(client.uid)==after,'late duplicate cannot credit twice')
    r.pass_check('late success after wallet funding credits once without changing hold or disputed contract')
    r.db.reconcile();print('PSP_REFUND_E2E_PASSED run='+r.run_id,flush=True)

if __name__=='__main__':main()
