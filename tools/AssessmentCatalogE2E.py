"""Read-only HTTP regression for sourced assessments and safe filtering."""
import argparse
import requests
import urllib3

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--base', default='https://localhost:44391')
    args = parser.parse_args()
    urllib3.disable_warnings()
    session = requests.Session(); session.verify = False
    count = 0
    def check(ok, label):
        nonlocal count
        assert ok, label
        count += 1
        print('PASS: ' + label)
    page = session.get(args.base + '/Assessments', timeout=40)
    check(page.status_code == 200, 'official assessment catalog opens without login')
    text = page.text
    for provider in ('Microsoft Learn', 'AWS', 'Scrum.org', 'HubSpot Academy'):
        check(provider in text, 'official provider present: ' + provider)
    check('Chứng chỉ hành nghề' not in text, 'technical assessments are not mislabeled as occupational licences')
    check('2026-10-16' in text, 'source review date is visible')
    result = session.get(args.base + '/Assessments?provider=Microsoft%20Learn', timeout=20)
    check('data-resource="azure-fundamentals"' in result.text and 'data-resource="aws-cloud-practitioner"' not in result.text, 'provider filter limits actual resource rows')
    result = session.get(args.base + '/Assessments', params={'industry':'Marketing & truyền thông'}, timeout=20)
    check('data-resource="hubspot-digital-marketing"' in result.text and 'data-resource="azure-fundamentals"' not in result.text, 'industry filter preserves Vietnamese text')
    for suffix in ('?provider=forged', '?industry=forged', '?q=' + ('x' * 121)):
        check(session.get(args.base + '/Assessments' + suffix, timeout=20).status_code == 400, 'invalid filter rejected: ' + suffix[:32])
    result = session.get(args.base + '/Assessments', params={'q':'<script>alert(1)</script>'}, timeout=20)
    check(result.status_code == 400 and '<script>alert(1)</script>' not in result.text, 'MVC request validation blocks script markup')
    result = session.get(args.base + '/Assessments', params={'q':"No results & O'Reilly"}, timeout=20)
    check(result.status_code == 200 and 'data-resource=' not in result.text and 'No results &amp; O' in result.text, 'benign query encoded and empty result handled')
    check(session.get(args.base + '/Skills/Certifications?resource=azure-fundamentals', allow_redirects=False, timeout=20).status_code == 302, 'certificate submission requires authentication')
    print(f'ASSESSMENT_CATALOG: {count}/{count} PASS')

if __name__ == '__main__':
    main()
