"""Run only against the isolated audit site; uses local templates, never external AI.

python tools/JobStudioRoleRowsE2E.py --base https://localhost:44392
"""
import argparse
import re
from html import unescape
import requests
import urllib3
from PlatformJourneyE2E import catalog, Fields


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--base', required=True)
    args = parser.parse_args()
    assert args.base == 'https://localhost:44392', 'Isolated audit site only'
    urllib3.disable_warnings()
    session = requests.Session()
    session.verify = False
    checks = []

    def check(ok, label):
        assert ok, label
        checks.append(label)
        print('PASS ' + label, flush=True)

    def get(route):
        response = session.get(args.base + route, timeout=100)
        check(response.status_code == 200, 'GET ' + route)
        return response

    def post(route, data, page):
        token = Fields(page.text).values['__RequestVerificationToken']
        response = session.post(args.base + route, data={**data, '__RequestVerificationToken': token}, timeout=100)
        check(response.status_code == 200, 'POST ' + route)
        return response

    page = get('/Account/Login')
    page = post('/Account/Login', {'Email': 'client.thuha@nexus.example.test', 'Role': 'Client', 'Password': catalog()['Password']}, page)
    check('GoogleLoginButton' not in page.text, 'Client login')
    for kind in ['SOLO', 'TEAM']:
        page = get('/JobStudio/New')
        page = post('/JobStudio/Generate', {'Industry': 'Thiết kế & sáng tạo', 'ProjectType': kind,
                    'Budget': '8000000', 'Idea': 'Minh họa kiểm thử giao diện thiết kế logo trên hộp giấy, bàn giao tệp vector chỉnh sửa được.',
                    'UseExternalAi': 'false', 'UseSharedKnowledge': 'false', 'UseProjectKnowledge': 'false'}, page)
        check('/JobStudio/Edit/' in page.url, kind + ' creates a local draft')
        fields = Fields(page.text).values
        titles = [v for k, v in fields.items() if re.fullmatch(r'Roles\[\d+\]\.Title', k)]
        check(len(titles) == (1 if kind == 'SOLO' else 6), kind + ' editor row count follows project type')
        if kind == 'SOLO':
            draft_id = int(page.url.rsplit('/', 1)[-1])
            post('/JobStudio/Delete/' + str(draft_id), {}, page)
        else:
            actual = sum(bool(v.strip()) for v in titles)
            draft_id = int(page.url.rsplit('/', 1)[-1])
            applied = post('/JobStudio/Apply/' + str(draft_id), {}, page)
            check('/Projects/Manage/' in applied.url, 'TEAM draft applies to project')
            page = get('/JobStudio/Edit/' + str(draft_id))
            fields = Fields(page.text).values
            titles = [v for k, v in fields.items() if re.fullmatch(r'Roles\[\d+\]\.Title', k)]
            check(len(titles) == actual and all(titles), 'Applied TEAM only renders actual hiring positions')
            check('Các vị trí đã chuyển sang dự án' in unescape(page.text), 'Applied draft explains where to manage hiring')
    print('JOB_STUDIO_ROLE_ROW_CHECKS=' + str(len(checks)))


if __name__ == '__main__':
    main()
