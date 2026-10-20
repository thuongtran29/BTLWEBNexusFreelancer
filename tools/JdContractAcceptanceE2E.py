"""Repeatable JD-to-group-acceptance test on the isolated audit site/database only.

Uses an explicitly selected local JD template and synthetic signed PSP callbacks.
No external AI, email, bank or VNPay request is sent. Start the audit site with
the AUDIT020 synthetic merchant configuration documented in the journey report.
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
from PlatformJourneyE2E import Journey, Fields


def run(j):
    client = j.login('client.thuha', 'Client')
    leader = j.login('minh.frontend', 'Freelancer')
    member = j.login('linh.backend', 'Freelancer')
    page = j.post(client, '/JobStudio/Generate', {
        'Industry': 'Công nghệ thông tin', 'Idea': j.tag + ' web đặt lịch MVC với backend và frontend riêng.',
        'ProjectType': 'TEAM', 'Budget': 40000000, 'UseExternalAi': 'false',
        'UseProjectKnowledge': 'false', 'UseSharedKnowledge': 'false'}, form='/JobStudio/New')
    match = re.search(r'/JobStudio/Edit/(\d+)', page.url)
    j.check(bool(match), 'Explicit local JD template creates an editable draft')
    draft = int(match.group(1))
    form = Fields(page.text).values
    data = {k: v for k, v in form.items() if k in ['Id', 'ExpectedVersionId', 'Title', 'Description',
            'Objectives', 'Scope', 'UserStories', 'RequiredSkills', 'Deliverables', 'Milestones']}
    for index, title in enumerate(['Frontend Developer', 'Backend Developer', '', '', '', '']):
        data.update({f'Roles[{index}].Title': title, f'Roles[{index}].RequiredCount': 1})
    j.post(client, '/JobStudio/Edit', data, form=f'/JobStudio/Edit/{draft}')
    page = j.post(client, f'/JobStudio/Apply/{draft}', form=f'/JobStudio/Edit/{draft}')
    project = int(re.search(r'/Projects/Manage/(\d+)', page.url).group(1))
    j.check(j.sql(f'SELECT COUNT(*) FROM dbo.RoleRequirements WHERE ProjectId={project}') == '2',
            'Edited JD creates exactly two distinct project positions')
    j.post(client, f'/Projects/Publish/{project}', form=f'/Projects/Manage/{project}')
    page = j.post(leader, '/Groups/Create', {'Name': j.tag + ' JD team', 'Description': 'Nhóm web MVC có frontend và backend.',
                  'Industry': 'Công nghệ thông tin', 'WorkStyle': 'Remote', 'TargetSize': 2, 'MinimumBudget': 1000000,
                  'Specialties': 'MVC, giao diện, API', 'Collaboration': 'Trưởng nhóm tổng hợp và bàn giao.',
                  'IsPublic': 'true'}, form='/Groups')
    group = int(re.search(r'/Groups/Details/(\d+)', page.url).group(1))
    j.post(leader, '/Groups/Invite', {'GroupId': group, 'Email': 'linh.backend@nexus.example.test'}, form=f'/Groups/Details/{group}')
    j.post(member, f'/Groups/Respond/{group}', {'accept': 'true'}, form='/Groups')
    page = j.get(client, f'/Projects/Manage/{project}')
    j.check(j.tag + ' JD team' in unescape(page.text), 'Client sees the newly matched verified group')
    roles = [int(x) for x in j.sql(f'SELECT Id FROM dbo.RoleRequirements WHERE ProjectId={project} ORDER BY Id').splitlines()]
    plan = json.dumps([{'Title': 'Luồng đặt lịch và giao diện', 'Amount': 10000000, 'DueDay': 7,
                       'AcceptanceCriteria': 'Kiểm thử luồng đặt lịch và phân quyền theo phạm vi đã chốt.'},
                      {'Title': 'Mã nguồn và tài liệu bàn giao', 'Amount': 20000000, 'DueDay': 21,
                       'AcceptanceCriteria': 'Chạy được mã nguồn và SQL theo tài liệu hướng dẫn.'}], ensure_ascii=False)
    data = {'FreelancerGroupId': group, 'ProjectId': project, 'Pitch': 'Nhóm phân vai rõ, kiểm thử và bàn giao theo hai mốc.',
            'DeliveryDays': 21, 'MilestonePlan': plan}
    for index, (user, role, price) in enumerate(zip([7, 8], roles, [12000000, 18000000])):
        data.update({f'Candidates[{index}].Selected': 'true', f'Candidates[{index}].UserAccountId': user,
                     f'Candidates[{index}].RoleRequirementId': role, f'Candidates[{index}].QuotedPrice': price})
    j.post(leader, '/GroupBids/Create', data, form=f'/GroupBids/Create?groupId={group}&projectId={project}')
    bid = int(j.sql(f'SELECT Id FROM dbo.GroupBids WHERE ProjectId={project}'))
    j.post(client, f'/GroupBids/Decide/{bid}', {'accept': 'true'}, form=f'/Projects/Manage/{project}')
    contract = int(j.sql(f'SELECT Id FROM dbo.Contracts WHERE ProjectId={project}'))
    workspace = int(j.sql(f'SELECT Id FROM dbo.Workspaces WHERE ContractId={contract}'))
    milestones = [int(x) for x in j.sql(f'SELECT Id FROM dbo.EscrowMilestones WHERE ContractId={contract} ORDER BY Id').splitlines()]
    j.check(len(milestones) == 2, 'Selected roster preserves the two agreed milestones')
    j.post(client, f'/Projects/LockTeam/{project}', form=f'/Projects/Manage/{project}')
    version, digest = j.sql(f"SELECT CONCAT(Id,'|',Hash) FROM dbo.ContractVersions WHERE ContractId={contract}").split('|')
    for session in [client, leader]:
        j.post(session, f'/Contracts/Confirm/{contract}', {'versionId': version, 'hash': digest}, form=f'/Contracts/Details/{contract}')
    j.check(j.sql(f'SELECT Status FROM dbo.Contracts WHERE Id={contract}') == 'AwaitingSignatures',
            'Client and leader cannot bypass the member confirmation')
    for _ in range(2):
        j.post(member, f'/Contracts/Confirm/{contract}', {'versionId': version, 'hash': digest}, form=f'/Contracts/Details/{contract}')
    j.check(j.sql(f'SELECT COUNT(*) FROM dbo.ContractAcceptances WHERE ContractVersionId={version}') == '3',
            'Repeated confirmation is idempotent for all three signers')
    first, second = milestones
    j.post(client, f'/Wallet/Fund/{first}', form=f'/Contracts/Details/{contract}')
    j.post(leader, '/Workspace/Submit', {'WorkspaceId': workspace, 'EscrowMilestoneId': first,
           'Title': 'Bàn giao lần đầu', 'Url': 'https://example.com/audit/first'}, form=f'/Workspace/Index/{workspace}')
    delivery = int(j.sql(f'SELECT Id FROM dbo.Deliverables WHERE WorkspaceId={workspace}'))
    j.post(client, f'/Workspace/Decide/{delivery}', {'decision': 'RevisionsRequested', 'feedback': 'Bổ sung kiểm thử và hướng dẫn theo tiêu chí đã thống nhất.'}, form=f'/Workspace/Index/{workspace}')
    j.check(j.sql(f'SELECT COUNT(*) FROM dbo.WalletEntries WHERE EscrowMilestoneId={first} AND Kind=\'Payout\'') == '0',
            'Requesting revision keeps escrow without any payout')
    j.post(leader, '/Workspace/Submit', {'WorkspaceId': workspace, 'EscrowMilestoneId': first, 'RevisesId': delivery,
           'Title': 'Bản sửa đã bổ sung kiểm thử', 'Url': 'https://example.com/audit/revision'}, form=f'/Workspace/Index/{workspace}')
    revised = int(j.sql(f'SELECT Id FROM dbo.Deliverables WHERE RevisesId={delivery}'))
    j.post(client, f'/Workspace/Decide/{revised}', {'decision': 'Approved'}, form=f'/Workspace/Index/{workspace}')
    entries = j.sql(f'SELECT COUNT(*) FROM dbo.WalletEntries WHERE EscrowMilestoneId={first}')
    repeat = j.post(client, f'/Workspace/Decide/{revised}', {'decision': 'Approved'}, form=f'/Workspace/Index/{workspace}')
    j.check(repeat.status_code == 409 and entries == j.sql(f'SELECT COUNT(*) FROM dbo.WalletEntries WHERE EscrowMilestoneId={first}'),
            'Repeated acceptance cannot settle or pay twice')
    j.check(j.sql(f'SELECT Status FROM dbo.Contracts WHERE Id={contract}') == 'Active', 'First milestone does not complete the contract')
    response = j.post(client, f'/Payments/Checkout/{second}', form=f'/Contracts/Details/{contract}', follow=False)
    location = urlsplit(response.headers.get('Location', ''))
    j.check(response.status_code == 302 and location.hostname == 'sandbox.vnpayment.vn', 'Checkout produces a signed sandbox redirect; test does not follow it')
    payment, reference, amount, merchant = j.sql(f"SELECT CONCAT(Id,'|',TxnRef,'|',Amount,'|',MerchantCode) FROM dbo.PaymentIntents WHERE EscrowMilestoneId={second}").split('|')
    assert merchant == 'AUDIT020', 'Only the synthetic audit merchant is permitted'
    fields = {'vnp_TmnCode': merchant, 'vnp_TxnRef': reference, 'vnp_Amount': str(int(Decimal(amount) * 100)),
              'vnp_CurrCode': 'VND', 'vnp_ResponseCode': '00', 'vnp_TransactionStatus': '00',
              'vnp_TransactionNo': str(700000000000 + int(payment)), 'vnp_PayDate': '20261020010000'}

    def signed(data):
        query = urlencode(sorted(data.items()))
        signature = hmac.new(b'audit-20261020-vnpay-only-not-a-real-secret-0001', query.encode(), hashlib.sha512).hexdigest()
        return query + '&vnp_SecureHash=' + signature

    query = signed(fields)
    j.get(client, '/Payments/Return?' + query)
    j.check(j.sql(f'SELECT Status FROM dbo.PaymentIntents WHERE Id={payment}') == 'Pending', 'Browser return never credits escrow')
    j.check(j.get(client, '/Payments/Ipn?' + signed({**fields, 'vnp_Amount': '100'})).json()['RspCode'] == '04', 'Signed amount mismatch is rejected')
    j.check(j.get(client, '/Payments/Ipn?' + query).json()['RspCode'] == '00', 'Synthetic signed PSP IPN funds the second milestone')
    entries = j.sql(f'SELECT COUNT(*) FROM dbo.WalletEntries WHERE EscrowMilestoneId={second}')
    j.check(j.get(client, '/Payments/Ipn?' + query).json()['RspCode'] == '02' and entries == j.sql(f'SELECT COUNT(*) FROM dbo.WalletEntries WHERE EscrowMilestoneId={second}'),
            'Repeated PSP IPN cannot duplicate capture or hold')
    j.post(leader, '/Workspace/Submit', {'WorkspaceId': workspace, 'EscrowMilestoneId': second,
           'Title': 'Bàn giao cuối cùng', 'Url': 'https://example.com/audit/final'}, form=f'/Workspace/Index/{workspace}')
    final = int(j.sql(f'SELECT Id FROM dbo.Deliverables WHERE WorkspaceId={workspace} AND EscrowMilestoneId={second}'))
    j.post(client, f'/Workspace/Decide/{final}', {'decision': 'Approved'}, form=f'/Workspace/Index/{workspace}')
    j.check(j.sql(f'SELECT Status FROM dbo.Contracts WHERE Id={contract}') == 'Completed', 'Final acceptance completes the group contract')
    j.check(j.sql(f'SELECT Status FROM dbo.Projects WHERE Id={project}') == 'Completed', 'Closed recruitment and completed contract finish the project')
    j.check(j.sql(f"SELECT COUNT(*) FROM dbo.EscrowMilestones m WHERE ContractId={contract} AND m.Amount=m.Fee+m.Refunded+(SELECT SUM(e.Amount) FROM dbo.WalletEntries e WHERE e.EscrowMilestoneId=m.Id AND e.Kind='Payout')") == '2',
            'Both milestones conserve gross = fee + refund + member payouts')
    result = {'checks': j.checks, 'draftId': draft, 'projectId': project, 'groupId': group, 'contractId': contract,
              'workspaceId': workspace, 'milestoneIds': milestones, 'provider': 'synthetic local IPN; no external request'}
    print('JD_CONTRACT_ACCEPTANCE_CHECKS=' + str(len(j.checks)), flush=True)
    return result


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--base', required=True)
    parser.add_argument('--database', required=True)
    parser.add_argument('--result', required=True)
    args = parser.parse_args()
    urllib3.disable_warnings()
    Path(args.result).write_text(json.dumps(run(Journey(args.base, args.database)), ensure_ascii=False, indent=2), encoding='utf-8')
