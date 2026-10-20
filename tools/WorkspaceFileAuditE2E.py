"""Audit-only regression for Admin evidence downloads and participant-only uploads.

Use a workspace owned by seeded Client quanghuy with backend freelancer Linh,
and at least one already uploaded file. Never creates a file or sends external data.
"""
import argparse
import json
from pathlib import Path
import urllib3
from PlatformJourneyE2E import Journey, Fields


def run(j, workspace):
    assert workspace > 0
    assert j.sql(f'SELECT COUNT(*) FROM dbo.Workspaces w JOIN dbo.Contracts c ON c.Id=w.ContractId '
                 f'JOIN dbo.Projects p ON p.Id=c.ProjectId WHERE w.Id={workspace} AND p.ClientId=6 '
                 'AND c.CounterpartyId=8') == '1', 'Expected disposable seeded-account audit fixture'
    version = int(j.sql(f'SELECT TOP 1 v.Id FROM dbo.WorkspaceFileVersions v JOIN dbo.WorkspaceFiles f '
                        f'ON f.Id=v.WorkspaceFileId WHERE f.WorkspaceId={workspace} ORDER BY v.Id DESC'))
    client = j.login('client.quanghuy', 'Client')
    worker = j.login('linh.backend', 'Freelancer')
    admin = j.login('admin', 'Admin')
    outsider = j.login('vy.translation', 'Freelancer')
    route = f'/WorkspaceFiles/Download/{version}'
    owned = j.get(worker, route)
    j.check(owned.status_code == 200 and len(owned.content) > 0, 'Contract member downloads evidence')
    for session, role in [(client, 'Client'), (admin, 'Admin')]:
        response = j.get(session, route)
        j.check(response.status_code == 200 and response.content == owned.content, role + ' reads exact evidence bytes')
        j.check('no-store' in response.headers.get('Cache-Control', '') and
                response.headers.get('X-Content-Type-Options') == 'nosniff' and
                'attachment' in response.headers.get('Content-Disposition', ''), role + ' download remains private attachment')
    j.check(j.get(outsider, route).status_code == 403, 'Unrelated user cannot download evidence')
    for session, role in [(admin, 'Admin'), (outsider, 'Outsider')]:
        page = j.get(session, f'/Workspace/Index/{workspace}' if role == 'Admin' else '/Dashboard')
        token = Fields(page.text).values['__RequestVerificationToken']
        response = session.post(j.base + '/WorkspaceFiles/Upload', data={
            'WorkspaceId': workspace, '__RequestVerificationToken': token},
            files={'upload': ('blocked.txt', b'Permission regression fixture only', 'text/plain')}, timeout=60)
        j.check(response.status_code == 403, role + ' cannot upload into this workspace')
    return {'checks': j.checks, 'workspaceId': workspace, 'versionId': version}


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--base', required=True)
    parser.add_argument('--database', required=True)
    parser.add_argument('--workspace', required=True, type=int)
    parser.add_argument('--result', required=True)
    args = parser.parse_args()
    urllib3.disable_warnings()
    result = run(Journey(args.base, args.database), args.workspace)
    Path(args.result).write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')
    print('WORKSPACE_FILE_AUDIT_CHECKS=' + str(len(result['checks'])))
