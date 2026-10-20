"""Read-only timestamp regression on the explicitly isolated audit workspace."""
import argparse
import re
from html import unescape
import urllib3
from PlatformJourneyE2E import Journey


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--base', required=True)
    parser.add_argument('--database', required=True)
    parser.add_argument('--workspace', required=True, type=int)
    args = parser.parse_args()
    assert args.workspace > 0
    urllib3.disable_warnings()
    journey = Journey(args.base, args.database)
    session = journey.login('client.minhan', 'Client')
    page = journey.get(session, f'/Workspace/Index/{args.workspace}')
    assert page.status_code == 200
    text = unescape(re.sub('<[^>]+>', ' ', page.text))
    rows = journey.sql("SELECT CONCAT(d.Id,'|',u.Name,'|',FORMAT(DATEADD(hour,7,d.CreatedAtUtc),'dd/MM/yyyy HH:mm'),' (UTC+7)') "
                       "FROM dbo.Deliverables d JOIN dbo.Users u ON u.Id=d.SubmittedById "
                       f"WHERE d.WorkspaceId={args.workspace} ORDER BY d.Id").splitlines()
    assert rows, 'Audit workspace must contain a delivery'
    for row in rows:
        identity, name, expected = row.split('|')
        journey.check(bool(re.search(re.escape(name) + r'\s*·\s*' + re.escape(expected), text)),
                      'Delivery #' + identity + ' displays its recorded instant in Vietnam time')
    print('WORKSPACE_DELIVERY_TIME_CHECKS=' + str(len(journey.checks)))
