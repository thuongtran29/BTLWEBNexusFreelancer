"""Cancellation, disputes, refunds and withdrawals on the explicit audit site only.

Synthetic PSP IPNs are local; real refund calls are left to the fake C# transport.
"""
import argparse
import hashlib
import hmac
import json
import re
from decimal import Decimal
from html import unescape
from pathlib import Path
from urllib.parse import urlencode, urlsplit
import urllib3
from PlatformJourneyE2E import Journey


def run(j):
    client = j.login('client.quanghuy', 'Client')
    worker = j.login('linh.backend', 'Freelancer')
    outside = j.login('an.design', 'Freelancer')
    admin = j.login('admin', 'Admin')

    def create_contract(label):
        page = j.post(client, '/Projects/Create', {'Title': j.tag + ' ' + label,
                      'Description': 'Fixture đối chiếu hợp đồng, ký quỹ và quyết toán trên CSDL audit.',
                      'Industry': 'Công nghệ thông tin', 'ProjectType': 'SOLO', 'Budget': 1000000}, form='/Projects/Create')
        project = int(re.search(r'/Projects/Manage/(\d+)', page.url).group(1))
        j.post(client, '/Projects/AddRequirement', {'ProjectId': project, 'Title': 'Backend Developer', 'RequiredCount': 1}, form=f'/Projects/Manage/{project}')
        role = int(j.sql(f'SELECT Id FROM dbo.RoleRequirements WHERE ProjectId={project}'))
        j.post(client, f'/Projects/Publish/{project}', form=f'/Projects/Manage/{project}')
        j.post(worker, '/Proposals/Apply', {'ProjectId': project, 'RoleRequirementId': role, 'QuotedPrice': 100000,
               'DeliveryDays': 7, 'Pitch': 'Fixture nghiệm thu tài chính, không giao dịch ngân hàng thật.'}, form=f'/Projects/Details/{project}')
        proposal = int(j.sql(f'SELECT Id FROM dbo.Proposals WHERE ProjectId={project}'))
        j.post(client, f'/Projects/Accept/{project}', {'proposalId': proposal}, form=f'/Projects/Manage/{project}')
        contract = int(j.sql(f'SELECT Id FROM dbo.Contracts WHERE ProjectId={project}'))
        milestone = int(j.sql(f'SELECT Id FROM dbo.EscrowMilestones WHERE ContractId={contract}'))
        return project, contract, milestone

    def confirm(contract):
        version, digest = j.sql(f"SELECT CONCAT(Id,'|',Hash) FROM dbo.ContractVersions WHERE ContractId={contract}").split('|')
        for session in [client, worker]:
            j.post(session, f'/Contracts/Confirm/{contract}', {'versionId': version, 'hash': digest}, form=f'/Contracts/Details/{contract}')

    def settle(project, contract, refund, reporter):
        j.post(reporter, f'/Wallet/Dispute/{project}', {'contractId': contract,
               'reason': 'Đối chiếu bằng chứng và yêu cầu quyết toán hợp đồng trong fixture audit.'}, form=f'/Contracts/Details/{contract}')
        dispute = int(j.sql(f'SELECT Id FROM dbo.EscrowDisputes WHERE ContractId={contract}'))
        j.check(j.sql(f'SELECT Status FROM dbo.Contracts WHERE Id={contract}') == 'Disputed', 'Contract freezes while its dispute is pending')
        j.check(j.post(worker, f'/Wallet/Resolve/{dispute}', {'refund': refund, 'reason': 'Không có quyền quản trị để giải quyết.'}, form=f'/Contracts/Details/{contract}').status_code == 403,
                'Freelancer cannot decide its own dispute')
        j.check(j.post(client, f'/Wallet/Resolve/{dispute}', {'refund': refund, 'reason': 'Không có quyền quản trị để giải quyết.'}, form=f'/Contracts/Details/{contract}').status_code == 403,
                'Client cannot decide its own dispute')
        j.post(admin, f'/Wallet/Resolve/{dispute}', {'refund': refund,
               'reason': 'Chỉ audit: đã đối chiếu phạm vi và bằng chứng, quyết toán đúng khoản đang giữ.'}, form=f'/Wallet/ReviewDispute/{dispute}')
        before = j.sql(f'SELECT COUNT(*) FROM dbo.WalletEntries e JOIN dbo.EscrowMilestones m ON m.Id=e.EscrowMilestoneId WHERE m.ContractId={contract}')
        j.post(admin, f'/Wallet/Resolve/{dispute}', {'refund': refund,
               'reason': 'Thử lặp quyết định audit, không được ghi tiền lần hai.'}, form='/Wallet/Admin')
        j.check(before == j.sql(f'SELECT COUNT(*) FROM dbo.WalletEntries e JOIN dbo.EscrowMilestones m ON m.Id=e.EscrowMilestoneId WHERE m.ContractId={contract}'),
                'Repeated dispute resolution cannot duplicate settlement')
        return dispute

    project, contract, milestone = create_contract('decline')
    j.check(j.post(outside, f'/Contracts/Decline/{contract}', {'reason': 'Không thuộc hợp đồng nên không được từ chối.'}, form='/Dashboard').status_code == 403,
            'Unrelated freelancer cannot decline a private contract')
    j.post(client, f'/Contracts/Decline/{contract}', {'reason': 'Phạm vi chưa thống nhất, dừng ký kết trước thanh toán.'}, form=f'/Contracts/Details/{contract}')
    j.check(j.sql(f'SELECT Status FROM dbo.Contracts WHERE Id={contract}') == 'Declined' and j.sql(f'SELECT COUNT(*) FROM dbo.RoleAssignments WHERE ProjectId={project}') == '0',
            'Client decline before escrow frees the vacancy without deleting contract history')
    j.check(j.sql(f'SELECT COUNT(*) FROM dbo.WalletEntries WHERE EscrowMilestoneId={milestone}') == '0', 'Unfunded cancellation has no money journal')

    project, active, milestone = create_contract('partial-dispute')
    confirm(active)
    j.post(client, f'/Wallet/Fund/{milestone}', form=f'/Contracts/Details/{active}')
    j.post(worker, f'/Contracts/Decline/{active}', {'reason': 'Không được bỏ qua quyết toán sau khi đã ký quỹ.'}, form=f'/Contracts/Details/{active}')
    j.check(j.sql(f'SELECT Status FROM dbo.Contracts WHERE Id={active}') == 'Active', 'Funded contract cannot use the pre-payment decline shortcut')
    settle(project, active, 50000, worker)
    j.check(j.sql(f"SELECT CONCAT(Refunded,'|',Fee) FROM dbo.EscrowMilestones WHERE Id={milestone}") == '50000.00|5000.00',
            'Partial settlement refunds 50000 and charges fee only on the remaining 50000')
    j.check(j.sql(f"SELECT SUM(Amount) FROM dbo.WalletEntries WHERE EscrowMilestoneId={milestone} AND Kind='Payout'") == '45000.00', 'Remaining net payout goes to the contractual freelancer')

    payment_ids = []
    for label in ['unknown', 'failure', 'partial-psp']:
        project, contract, milestone = create_contract(label)
        confirm(contract)
        response = j.post(client, f'/Payments/Checkout/{milestone}', form=f'/Contracts/Details/{contract}', follow=False)
        j.check(response.status_code == 302 and urlsplit(response.headers['Location']).hostname == 'sandbox.vnpayment.vn', 'Synthetic checkout stays a redirect; no external request')
        payment, reference, amount, merchant = j.sql(f"SELECT CONCAT(Id,'|',TxnRef,'|',Amount,'|',MerchantCode) FROM dbo.PaymentIntents WHERE EscrowMilestoneId={milestone}").split('|')
        assert merchant == 'AUDIT020'
        fields = {'vnp_TmnCode': merchant, 'vnp_TxnRef': reference, 'vnp_Amount': str(int(Decimal(amount) * 100)),
                  'vnp_CurrCode': 'VND', 'vnp_ResponseCode': '00', 'vnp_TransactionStatus': '00', 'vnp_TransactionNo': str(810000000000 + int(payment))}
        query = urlencode(sorted(fields.items()))
        signature = hmac.new(b'audit-20261020-vnpay-only-not-a-real-secret-0001', query.encode(), hashlib.sha512).hexdigest()
        j.check(j.get(client, '/Payments/Ipn?' + query + '&vnp_SecureHash=' + signature).json()['RspCode'] == '00', 'Valid synthetic PSP receipt creates exactly one held payment')
        settle(project, contract, 100000, client)
        j.check(j.sql(f'SELECT Status FROM dbo.Contracts WHERE Id={contract}') == 'Terminated', 'Full held-money refund terminates the funded contract')
        for session in [client, worker]:
            j.check(j.post(session, f'/Payments/Refund/{payment}', {'amount': 100000, 'reason': 'Tài khoản thường không được gửi lệnh hoàn tiền PSP.'}, form=f'/Contracts/Details/{contract}').status_code == 403,
                    'Only Admin may initiate a PSP refund')
        payment_ids.append(int(payment))

    # Withdrawal limits are reserved by pending requests; no real bank transfer occurs.
    balance = Decimal(j.sql('SELECT Balance FROM dbo.WalletAccounts WHERE UserAccountId=13'))
    requests = []
    for amount in [200000, 300000]:
        reference = j.tag + ' withdraw ' + str(amount)
        j.post(outside, '/Wallet/RequestMoney', {'kind': 'Withdrawal', 'amount': amount, 'reference': reference,
               'details': 'AUDIT BANK / DEMO-13 / Người nhận minh họa'}, form='/Wallet')
        requests.append(int(j.sql("SELECT Id FROM dbo.MoneyRequests WHERE Reference=N'" + reference + "'")))
    j.check(Decimal(j.sql('SELECT Balance FROM dbo.WalletAccounts WHERE UserAccountId=13')) == balance, 'Pending withdrawals reserve capacity without prematurely debiting the journal')
    page = j.get(outside, '/Wallet')
    shown = Decimal(re.sub('[^0-9]', '', re.search(r'<span>Số dư khả dụng</span>\s*<strong[^>]*>(.*?)</strong>', page.text, re.S).group(1)))
    pending = Decimal(j.sql("SELECT COALESCE(SUM(Amount),0) FROM dbo.MoneyRequests WHERE UserAccountId=13 AND Kind='Withdrawal' AND Status='Pending'"))
    j.check(shown == balance - pending, 'Wallet displays the same available amount enforced by the server')
    reference = j.tag + ' overspend'
    j.post(outside, '/Wallet/RequestMoney', {'kind': 'Withdrawal', 'amount': str(balance), 'reference': reference,
           'details': 'AUDIT ONLY invalid overspend'}, form='/Wallet')
    j.check(j.sql("SELECT COUNT(*) FROM dbo.MoneyRequests WHERE Reference=N'" + reference + "'") == '0', 'Pending requests prevent a second withdrawal from overspending')
    j.check(j.post(client, '/Wallet/RequestMoney', {'kind': 'Withdrawal', 'amount': 1, 'reference': j.tag, 'details': 'Audit'}, form='/Wallet').status_code == 403,
            'Client cannot call the freelancer withdrawal command')
    first, second = requests
    for session in [client, outside]:
        j.check(j.post(session, f'/Wallet/ReviewMoney/{first}', {'approve': 'true', 'reason': 'Không có quyền quản trị.'}, form='/Dashboard').status_code == 403, 'Non-admin cannot approve a withdrawal')
    j.post(admin, f'/Wallet/ReviewMoney/{first}', {'approve': 'true', 'reason': 'AUDIT ONLY: xác nhận chi mô phỏng, không gửi ngân hàng.'}, form='/Wallet/Admin')
    j.post(admin, f'/Wallet/ReviewMoney/{first}', {'approve': 'true', 'reason': 'AUDIT ONLY: thử lặp không được ghi trừ lần hai.'}, form='/Wallet/Admin')
    j.check(j.sql(f"SELECT COUNT(*) FROM dbo.WalletEntries WHERE Reference='money:{first}'") == '1', 'Withdrawal approval is journaled once despite repeated submission')
    j.post(admin, f'/Wallet/ReviewMoney/{second}', {'approve': 'false', 'reason': 'AUDIT ONLY: từ chối yêu cầu, trả hạn mức khả dụng.'}, form='/Wallet/Admin')
    j.check(Decimal(j.sql('SELECT Balance FROM dbo.WalletAccounts WHERE UserAccountId=13')) == balance - 200000, 'Rejected withdrawal does not debit funds')
    j.check(j.sql(f"SELECT COUNT(*) FROM dbo.AdminAuditLogs WHERE Action='ReviewMoneyRequest' AND (Reason LIKE 'Request #{first}:%' OR Reason LIKE 'Request #{second}:%')") == '2', 'Both decisions preserve the responsible Admin and reason')
    result = {'checks': j.checks, 'adminId': 3, 'paymentIds': payment_ids, 'withdrawalIds': requests, 'partialDisputeContractId': active}
    print('SETTLEMENT_ROLE_CHECKS=' + str(len(j.checks)))
    return result


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--base', required=True)
    parser.add_argument('--database', required=True)
    parser.add_argument('--result', required=True)
    args = parser.parse_args()
    urllib3.disable_warnings()
    Path(args.result).write_text(json.dumps(run(Journey(args.base, args.database)), ensure_ascii=False, indent=2), encoding='utf-8')
