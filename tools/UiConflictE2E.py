"""Regression: invalid publish retains HTTP 409 without exposing the IIS error page.

Use an owned, disposable localhost draft with zero role requirements.
python tools/UiConflictE2E.py --project-id ID --email FIXTURE@example.test
Set NEXUS_UI_TEST_PASSWORD in the process environment. No credentials are logged.
"""
import argparse
import html
import os
import requests
import urllib3
from SmokeE2E import Smoke

parser=argparse.ArgumentParser()
parser.add_argument('--project-id',type=int,required=True)
parser.add_argument('--email',required=True)
args=parser.parse_args()
assert args.project_id>0 and args.email.endswith('@example.test')
urllib3.disable_warnings(urllib3.exceptions.InsecureRequestWarning)
session=requests.Session();session.verify=False
test=Smoke('https://localhost:44391')
test.post(session,'/Account/Login',{'Email':args.email,'Password':os.environ['NEXUS_UI_TEST_PASSWORD'],'Role':'Client'},'/Account/Login')
manage=f'/Projects/Manage/{args.project_id}'
before=test.get(session,manage)
assert 'Chưa khai báo vị trí' in before.text, 'Use a draft with no role requirements'
response=test.post(session,f'/Projects/Publish/{args.project_id}',{},manage,expect_redirect=False)
response.encoding='utf-8'
assert response.status_code==409, 'Invalid publish must remain HTTP 409'
assert 'Không thể thực hiện thao tác' in response.text, 'Conflict must render the friendly application page'
assert 'Về tổng quan' in response.text
assert 'Physical Path' not in response.text and 'Detailed Error Information' not in response.text
after=test.get(session,manage)
assert 'Bản nháp' in html.unescape(after.text) and f'/Projects/Edit/{args.project_id}' in after.text and 'Chưa khai báo vị trí' in after.text, 'Rejected publish must leave the draft unchanged'
print('PASS friendly conflict, HTTP 409 preserved, technical paths hidden, draft unchanged')
