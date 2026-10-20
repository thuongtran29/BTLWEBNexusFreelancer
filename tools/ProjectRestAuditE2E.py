"""Full resource CRUD contract checks. Isolated audit site/database only."""
import argparse
import json
from pathlib import Path
import requests
import urllib3
from PlatformJourneyE2E import Journey, Fields


def run(j):
    client = j.login('client.quanghuy', 'Client')
    other = j.login('client.minhan', 'Client')
    worker = j.login('linh.backend', 'Freelancer')
    admin = j.login('admin', 'Admin')
    guest = requests.Session(); guest.verify = False
    def token(session):
        return {'X-CSRF-Token': Fields(j.get(session, '/Dashboard').text).values['__RequestVerificationToken']}
    headers = token(client)
    body = {'Title': j.tag, 'Description': 'Mô tả văn bản thường để kiểm tra API có phân quyền và phiên bản.',
            'Industry': 'Công nghệ thông tin', 'ProjectType': 'SOLO', 'Budget': 3000000,
            'SkillIds': [1], 'MinimumSkillLevel': 2, 'ClientId': 3, 'Status': 'Completed'}
    def req(s, verb, path, **kw):
        r = s.request(verb, j.base + path, timeout=90, **kw)
        assert r.status_code < 500, (r.status_code, r.text[:300])
        if r.status_code >= 400: assert isinstance(r.json()['error']['code'], str)
        return r
    collection = '/api/v1/projects'
    j.check(req(guest, 'POST', collection, json=body).status_code == 401, 'Anonymous creation rejected as JSON 401')
    for s, label in [(worker, 'Freelancer'), (admin, 'Admin')]:
        j.check(req(s, 'POST', collection, json=body, headers=token(s)).status_code == 403, label + ' cannot create as Client')
    j.check(req(client, 'POST', collection, json=body).status_code == 400, 'Missing CSRF rejected')
    j.check(req(client, 'POST', collection, json={**body, 'Title': ' '}, headers=headers).status_code == 422, 'Whitespace title rejected')
    j.check(req(client, 'POST', collection, json={**body, 'Budget': 3.5}, headers=headers).status_code == 422, 'Fractional VND rejected')
    j.check(req(client, 'POST', collection, json={**body, 'SkillIds': [1, 1]}, headers=headers).status_code == 422, 'Duplicate skills rejected')
    r = req(client, 'POST', collection, json=body, headers=headers)
    j.check(r.status_code == 201 and 'ETag' in r.headers and 'Location' in r.headers, 'Create returns 201, Location and ETag')
    pid = r.json()['id']; path = collection + '/' + str(pid); etag = r.headers['ETag']
    j.check(j.sql(f'SELECT CONCAT(ClientId,\'|\',Status,\'|\',CAST(Budget AS bigint)) FROM Projects WHERE Id={pid}') == '6|Draft|3000000', 'Overposting cannot change owner/status; VND preserved')
    read = req(client, 'GET', path)
    j.check(read.json()['description'] == body['Description'] and read.headers['ETag'] == etag, 'Owner reads DTO with stable ETag after SQL decimal roundtrip')
    j.check(req(guest, 'GET', path).status_code == 404 and req(other, 'GET', path).status_code == 404, 'Draft privacy prevents enumeration')
    j.check(req(other, 'PUT', path, json=body, headers=token(other)).status_code == 404, 'Other Client cannot update')
    j.check(req(client, 'PUT', path, json=body, headers=headers).status_code == 412, 'Update requires If-Match')
    updated = {**body, 'Title': j.tag + ' cập nhật'}
    r = req(client, 'PUT', path, json=updated, headers={**headers, 'If-Match': etag})
    j.check(r.status_code == 200 and r.json()['title'] == updated['Title'] and r.headers['ETag'] != etag, 'PUT updates with fresh version')
    fresh = r.headers['ETag']
    j.check(req(client, 'PUT', path, json=body, headers={**headers, 'If-Match': etag}).status_code == 412, 'Stale update cannot overwrite')
    j.check(req(client, 'PATCH', path, json=body, headers=headers).status_code == 405, 'Unsupported method returns 405 with Allow')
    j.check(req(client, 'GET', collection + '/abc').status_code == 400, 'Malformed resource ID gives JSON 400')
    j.check(req(client, 'DELETE', path, headers=headers).status_code == 412, 'Delete requires current version')
    j.check(req(client, 'DELETE', path, headers={**headers, 'If-Match': fresh}).status_code == 204, 'Draft DELETE archives with 204')
    j.check(req(client, 'DELETE', path, headers=headers).status_code == 204, 'Repeated DELETE is idempotent')
    j.check(req(client, 'GET', path).status_code == 404, 'Archived resource is absent from REST reads')
    j.check(j.sql(f"SELECT COUNT(*) FROM ActivityLogs WHERE ProjectId={pid} AND Action='Archived draft via REST API'") == '1', 'Deletion preserves exactly one audit receipt')
    existing = int(j.sql("SELECT TOP 1 p.Id FROM Projects p JOIN Contracts c ON c.ProjectId=p.Id WHERE p.ClientId=6 AND p.Status IN ('AwaitingEscrow','InProgress') ORDER BY p.Id DESC"))
    r = req(client, 'GET', collection + '/' + str(existing)); current = r.json()
    j.check(req(client, 'DELETE', collection + '/' + str(existing), headers={**headers, 'If-Match': r.headers['ETag']}).status_code == 409, 'Contract-bearing project cannot be deleted')
    values = {'Title': current['title'], 'Description': current['description'], 'Industry': current['industry'],
              'ProjectType': current['projectType'], 'Budget': current['budget'] + 1, 'Deadline': current['deadline'],
              'SkillIds': [s['skillId'] for s in current['skills']], 'MinimumSkillLevel': min([s['minimumLevel'] for s in current['skills']] or [1])}
    j.check(req(client, 'PUT', collection + '/' + str(existing), json=values, headers={**headers, 'If-Match': r.headers['ETag']}).status_code == 409, 'Contract terms cannot be changed through REST')
    return {'checks': j.checks, 'archivedProjectId': pid}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(); parser.add_argument('--base', required=True); parser.add_argument('--database', required=True); parser.add_argument('--result', required=True)
    a = parser.parse_args(); urllib3.disable_warnings()
    result = run(Journey(a.base, a.database)); Path(a.result).write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')
    print('REST_CHECKS=' + str(len(result['checks'])))
