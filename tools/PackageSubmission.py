"""Package final academic deliverables using the source-only sharing rules.
Example: python tools/PackageSubmission.py --group 2 --class-name N01
Writes a new sibling folder/ZIP; never overwrites the running installation.
"""
import argparse
from pathlib import Path
import re
from PackageShare import ROOT, package

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--group', default='CHUADIEN')
    parser.add_argument('--class-name', default='LopCHUADIEN')
    parser.add_argument('--output-parent', type=Path, default=ROOT.parent)
    args = parser.parse_args()
    if not all(re.fullmatch(r'[A-Za-z0-9-]{1,40}', value) for value in (args.group, args.class_name)):
        parser.error('Group/class must use ASCII letters, digits or hyphens only.')
    for relative in ('Submission/BaoCaoTongKet_NexusFreelance_BanChot.docx',
                     'Database/NexusFreelance_BanChot.sql',
                     'Submission/TEST_CASES_BAN_CHOT.csv'):
        if not (ROOT / relative).is_file():
            raise RuntimeError('Missing final deliverable: ' + relative)
    name = 'NexusFreelanceSubmission_Nhom' + args.group + '__' + args.class_name
    package(args.output_parent / name)

if __name__ == '__main__':
    main()
