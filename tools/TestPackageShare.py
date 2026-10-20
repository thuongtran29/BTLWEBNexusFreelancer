"""Distribution tests use only synthetic files; never copy local credentials."""
import hashlib
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import zipfile

from PackageShare import eligible, package


class ShareTests(unittest.TestCase):
    def test_sensitive_and_generated_paths_are_excluded(self):
        for name in ["_local/integrations.dat", "NexusFreelance.Web/App_Data/machineKey.config", "NexusFreelance.Web/bin/app.dll", "tools/_runtime/test.log", ".vs/session.json", "Database/private.bak", "client_secret_google.json", "docs/validation/session.html", "Submission/So_do_lop_Tong_quan/~$report.docx", "tools/old-test.exe", "tools/old-library.dll", "tools/debug.pdb"]:
            with self.subTest(path=name):
                self.assertFalse(eligible(Path(name)))
        for name in ["NexusFreelance.Web/Controllers/AccountController.cs", "NexusFreelance.Web/Content/vendor/jquery.js", "tools/ConfigureExternalServices.ps1", "Database/NexusFreelance_BanChot.sql", "Submission/TEST_CASES_BAN_CHOT.csv"]:
            with self.subTest(path=name):
                self.assertTrue(eligible(Path(name)))

    def test_archive_has_portable_config_and_no_private_fixture(self):
        with tempfile.TemporaryDirectory(prefix="NexusShareTest-") as temp:
            source = Path(temp) / "source"
            source.mkdir()
            files = {
                "NexusFreelance.sln": "synthetic solution",
                "NexusFreelance.Web/Web.config": '<configuration><connectionStrings><add name="NexusDb" connectionString="Data Source=secret-server;User ID=private;Password=synthetic-private-password" /></connectionStrings><runtime><assemblyBinding xmlns="urn:schemas-microsoft-com:asm.v1"><dependentAssembly><assemblyIdentity name="Fixture" /></dependentAssembly></assemblyBinding></runtime></configuration>',
                "Database/NexusFreelance_BanChot.sql": "-- synthetic schema",
                "tools/ConfigureMachineKey.ps1": "# synthetic setup",
                "NexusFreelance.Web/Content/app.css": "body{}",
                "_local/integrations.dat": "synthetic-private-key",
                "NexusFreelance.Web/App_Data/machineKey.config": "synthetic-private-key",
                "docs/README_CHIA_SE.md": "Synthetic installation guide",
            }
            for name, text in files.items():
                path = source / name
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text(text, encoding="utf-8")
            output = Path(temp) / "distribution"
            archive = package(output, source)
            with zipfile.ZipFile(archive) as z:
                self.assertIsNone(z.testzip())
                all_bytes = b"".join(z.read(name) for name in z.namelist())
                self.assertNotIn(b"synthetic-private-key", all_bytes)
                self.assertNotIn(b"synthetic-private-password", all_bytes)
                self.assertNotIn(b"secret-server", all_bytes)
                self.assertIn(b"NexusFreelanceSubmission", all_bytes)
                self.assertIn("distribution/README.md", z.namelist())
                self.assertIn("distribution/NexusFreelance.Web/Content/app.css", z.namelist())
                config = z.read("distribution/NexusFreelance.Web/Web.config").decode("utf-8")
                # ASP.NET rejects namespace declarations on <configuration>, even
                # though an ordinary XML parser accepts the serialized document.
                import re
                root_tag = re.search(r'<configuration\b[^>]*>', config).group(0)
                self.assertNotIn('xmlns', root_tag)
                self.assertIn('<assemblyBinding xmlns="urn:schemas-microsoft-com:asm.v1">', config)
            marker = json.loads((output / ".nexus-share.json").read_text())
            self.assertFalse(marker["privateSettingsCopied"])
            previous = hashlib.sha256(archive.read_bytes()).digest()
            with self.assertRaises(RuntimeError):
                package(output, source)
            self.assertEqual(previous, hashlib.sha256(archive.read_bytes()).digest())

    def test_academic_entry_point_uses_final_deliverables(self):
        import PackageSubmission
        with tempfile.TemporaryDirectory(prefix="NexusAcademicTest-") as temp:
            root = Path(temp) / "source"
            for relative in ("Submission/BaoCaoTongKet_NexusFreelance_BanChot.docx",
                             "Database/NexusFreelance_BanChot.sql",
                             "Submission/TEST_CASES_BAN_CHOT.csv"):
                target = root / relative
                target.parent.mkdir(parents=True, exist_ok=True)
                target.write_text("synthetic final deliverable", encoding="utf-8")
            with patch.object(PackageSubmission, "ROOT", root), patch.object(PackageSubmission, "package") as build:
                with patch("sys.argv", ["PackageSubmission.py", "--group", "2", "--class-name", "N01", "--output-parent", temp]):
                    PackageSubmission.main()
                build.assert_called_once_with(Path(temp) / "NexusFreelanceSubmission_Nhom2__N01")
                build.reset_mock()
                with patch("sys.argv", ["PackageSubmission.py", "--group", "../escape"]):
                    with self.assertRaises(SystemExit) as error:
                        PackageSubmission.main()
                self.assertEqual(error.exception.code, 2)
                build.assert_not_called()


if __name__ == "__main__":
    unittest.main(verbosity=2)
