"""Pending withdrawal display regression; writes only to the explicit audit site/DB."""
import argparse
import re
from decimal import Decimal
from html import unescape
import urllib3
from PlatformJourneyE2E import Journey


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--base', required=True)
    parser.add_argument('--database', required=True)
    args = parser.parse_args()
    urllib3.disable_warnings()
    j = Journey(args.base, args.database)
    worker = j.login('an.design', 'Freelancer')
    admin = j.login('admin', 'Admin')
    reference = j.tag + ' available'
    j.post(worker, '/Wallet/RequestMoney', {'kind': 'Withdrawal', 'amount': 200000,
           'reference': reference, 'details': 'AUDIT BANK / DEMO-13 / Người nhận minh họa'}, form='/Wallet')
    request = int(j.sql("SELECT Id FROM dbo.MoneyRequests WHERE Reference=N'" + reference + "'"))
    try:
        values = j.sql("SELECT CONCAT(w.Balance,'|',COALESCE((SELECT SUM(r.Amount) FROM dbo.MoneyRequests r "
                       "WHERE r.UserAccountId=w.UserAccountId AND r.Kind='Withdrawal' AND r.Status='Pending'),0)) "
                       "FROM dbo.WalletAccounts w WHERE w.UserAccountId=13").split('|')
        expected = max(Decimal('0'), Decimal(values[0]) - Decimal(values[1]))
        page = j.get(worker, '/Wallet')
        match = re.search(r'<span>Số dư khả dụng</span>\s*<strong[^>]*>(.*?)</strong>', page.text, re.S)
        assert match, 'Wallet available amount is not visible'
        displayed = Decimal(re.sub(r'[^0-9]', '', unescape(match.group(1))))
        j.check(displayed == expected, 'Displayed available balance subtracts all pending withdrawal requests')
        j.check('chờ rút' in unescape(page.text), 'Wallet explains its temporary withdrawal reservation')
        print('WALLET_AVAILABLE_CHECKS=' + str(len(j.checks)))
    finally:
        j.post(admin, f'/Wallet/ReviewMoney/{request}', {'approve': 'false',
               'reason': 'Kết thúc fixture hiển thị số dư audit, không chuyển tiền.'}, form='/Wallet/Admin')
