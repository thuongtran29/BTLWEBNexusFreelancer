"""Create a source-only distribution. Never modifies the running installation.

Usage: python tools/PackageShare.py --output D:/FreelancerBTLWEB_ChiaSe
The destination must be new and outside the source tree. No secrets are copied.
"""
from __future__ import annotations

import argparse
import hashlib
import html
import json
from pathlib import Path
import re
import shutil
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parent.parent
SOURCE_DIRS = {"NexusFreelance.Web", "tools", ".github", "docs"}
ROOT_FILES = {".gitignore", "NexusFreelance.sln", "CauHinhDichVu.cmd", "ERD.jpg"}
SUBMISSION_FILES = {
    "BaoCaoTongKet_NexusFreelance_BanChot.docx", "DOI_CHIEU_TIEU_CHI.md",
    "HUONG_DAN_CAI_DAT_BAN_CHOT.md", "HUONG_DAN_API.md", "THIRD_PARTY_NOTICES.md",
    "SO_TAY_BAN_CHOT_DU_LIEU_CHUC_NANG_TEST_CASE.md", "TAI_KHOAN_DEMO.md",
    "TEST_CASES_BAN_CHOT.csv", "KET_QUA_KIEM_THU_BAN_CHOT.csv",
    "MA_TRAN_CHUC_NANG_BAN_CHOT.csv", "KICH_BAN_THUYET_TRINH_BAN_CHOT.md",
    "TuDienDuLieu.csv", "KhoaNgoai.csv", "RangBuocDuLieu.json", "DU_LIEU_BAN_CHOT.json",
}
SUBMISSION_DIRS = {"Chuong_2_Phan_tich_he_thong", "Anh_Chuong_4_5_BanMoi", "So_do_lop_Tong_quan", "SoDo"}
EXCLUDED_DIRS = {".git", ".vs", ".vscode", "bin", "obj", "App_Data", "_local", "_offline", "_runtime", "__pycache__", "node_modules", "validation", "docx_qa", "Bang_chung"}
EXCLUDED_SUFFIXES = {".bak", ".bkp", ".dat", ".eml", ".pfx", ".suo", ".user", ".log", ".tmp", ".pyc", ".zip", ".sha256", ".mdf", ".ldf", ".publishsettings", ".exe", ".dll", ".pdb", ".cache"}


def eligible(relative: Path) -> bool:
    if any(part in EXCLUDED_DIRS for part in relative.parts[:-1]):
        return False
    name = relative.name.lower()
    if name.startswith(("~$", ".$", ".env")) or "client_secret" in name or relative.suffix.lower() in EXCLUDED_SUFFIXES:
        return False
    if not relative.parts:
        return False
    if len(relative.parts) == 1:
        return relative.name in ROOT_FILES
    if relative.parts[0] in SOURCE_DIRS:
        return True
    if relative.parts[0] == "Database":
        return len(relative.parts) == 2 and relative.name in {"NexusFreelance_BanChot.sql", "VerifySubmission.sql"}
    if relative.parts[0] == "Submission":
        return (len(relative.parts) == 2 and relative.name in SUBMISSION_FILES) or (len(relative.parts) > 2 and relative.parts[1] in SUBMISSION_DIRS)
    return False


def digest(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()


def copy_source(source: Path, output: Path) -> list[str]:
    required = ["NexusFreelance.sln", "NexusFreelance.Web/Web.config", "Database/NexusFreelance_BanChot.sql", "tools/ConfigureMachineKey.ps1"]
    for name in required:
        if not (source / name).is_file():
            raise RuntimeError("Missing source requirement: " + name)
    copied = []
    # Walk without descending into private/runtime folders or filesystem links.
    import os
    for directory, folders, filenames in os.walk(source, followlinks=False):
        base = Path(directory)
        folders[:] = [name for name in folders if name not in EXCLUDED_DIRS and not (base / name).is_symlink() and not ((base / name).is_junction() if hasattr(Path, "is_junction") else False)]
        for name in filenames:
            path = base / name
            relative = path.relative_to(source)
            if path.is_symlink() or not eligible(relative):
                continue
            target = output / relative
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(path, target)
            copied.append(relative.as_posix())
    # The recipient imports the included SQL; never ship this workstation's DB name or SQL credentials.
    web_config = output / "NexusFreelance.Web/Web.config"
    # Preserve config text. ElementTree serialization hoists assemblyBinding's
    # namespace onto <configuration>, which ASP.NET rejects at runtime.
    config = web_config.read_text(encoding="utf-8-sig")
    ET.fromstring(config)
    pattern = r'''(<add\b(?=[^>]*\bname\s*=\s*["']NexusDb["'])[^>]*\bconnectionString\s*=\s*)(["'])(.*?)\2'''
    connection = r"Data Source=.\SQLEXPRESS;Initial Catalog=NexusFreelanceSubmission;Integrated Security=True;MultipleActiveResultSets=True;Encrypt=True;TrustServerCertificate=True"
    config, replacements = re.subn(pattern, lambda m: m[1] + m[2] + html.escape(connection, quote=True) + m[2], config, flags=re.S)
    if replacements != 1:
        raise RuntimeError("Expected one NexusDb connection string")
    ET.fromstring(config)
    if re.search(r'<configuration\b[^>]*\bxmlns', config):
        raise RuntimeError("ASP.NET configuration root must not declare XML namespaces")
    web_config.write_text(config, encoding="utf-8")
    return copied


def package(output: Path, source: Path = ROOT) -> Path:
    source = source.resolve()
    output = output.absolute()
    archive = output.with_name(output.name + ".zip")
    if output.exists() or archive.exists() or output.is_relative_to(source) or source.is_relative_to(output):
        raise RuntimeError("Use a new destination outside the running installation; existing data is never replaced")
    output.mkdir(parents=False)
    copied = copy_source(source, output)
    template = source / "docs/README_CHIA_SE.md"
    if template.is_file():
        shutil.copy2(template, output / "README.md")
    (output / ".nexus-share.json").write_text(json.dumps({"project": "NexusFreelance", "kind": "source-only", "liveDatabaseCopied": False, "privateSettingsCopied": False}, indent=2), encoding="utf-8")
    files = sorted(p for p in output.rglob("*") if p.is_file())
    manifest = "".join(digest(p) + "  " + p.relative_to(output).as_posix() + "\n" for p in files)
    (output / "MANIFEST_SHA256.txt").write_text(manifest, encoding="utf-8")
    with zipfile.ZipFile(archive, "x", zipfile.ZIP_DEFLATED, compresslevel=6) as z:
        for path in files + [output / "MANIFEST_SHA256.txt"]:
            z.write(path, output.name + "/" + path.relative_to(output).as_posix())
    with zipfile.ZipFile(archive) as z:
        if z.testzip() is not None or len(z.infolist()) != len(files) + 1:
            raise RuntimeError("Distribution archive failed CRC/inventory verification")
        for path in files:
            if hashlib.sha256(z.read(output.name + "/" + path.relative_to(output).as_posix())).hexdigest().upper() != digest(path):
                raise RuntimeError("Distribution archive checksum mismatch")
    archive.with_suffix(".zip.sha256").write_text(digest(archive) + "  " + archive.name + "\n", encoding="ascii")
    print(json.dumps({"folder": str(output), "archive": str(archive), "files": len(files) + 1, "archiveMB": round(archive.stat().st_size / 1048576, 2)}, ensure_ascii=True))
    return archive


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=ROOT.with_name(ROOT.name + "_ChiaSe"))
    package(parser.parse_args().output)
