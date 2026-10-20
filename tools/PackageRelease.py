"""Compatibility entry point for the safe source-only distribution packager.
Run: python tools/PackageRelease.py --output D:/FreelancerBTLWEB_ChiaSe
Use a new destination. Existing folders and archives are never replaced.
"""
import argparse
from pathlib import Path
from PackageShare import ROOT, eligible as eligible_relative, package

def eligible(path: Path) -> bool:
    """Retain the absolute-path filter interface used by older local scripts."""
    try:
        relative = path.relative_to(ROOT)
    except ValueError:
        return False
    return path.is_file() and not path.is_symlink() and eligible_relative(relative)

if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, default=ROOT.with_name(ROOT.name + '_ChiaSe'))
    package(parser.parse_args().output)
