"""Visit every Admin sidebar link, using a temporary development Admin only."""
import argparse
from html.parser import HTMLParser
import uuid

from AdminE2E import register, request, power_shell


class SidebarLinks(HTMLParser):
    def __init__(self):
        super().__init__()
        self.inside = False
        self.links = []

    def handle_starttag(self, tag, attributes):
        attrs = dict(attributes)
        if tag == "aside" and attrs.get("id") == "app-sidebar":
            self.inside = True
        if self.inside and tag == "a" and attrs.get("href"):
            self.links.append(attrs["href"])

    def handle_endtag(self, tag):
        if tag == "aside":
            self.inside = False


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--run-writes", action="store_true")
    if not parser.parse_args().run_writes:
        parser.error("Creates temporary NexusFreelanceMvp test Admin; add --run-writes")
    email = "admin-e2e-navigation-" + uuid.uuid4().hex[:12] + "@example.invalid"
    session = register("Admin E2E", email)
    elevated = False
    try:
        power_shell("BootstrapAdmin.ps1", email)
        elevated = True
        page = request(session, "GET", "/Admin/Index")
        links = SidebarLinks()
        links.feed(page.text)
        assert len(links.links) >= 8, links.links
        assert "/Admin/Questions" in links.links, links.links
        for path in links.links:
            assert path.startswith("/") and not path.startswith("//"), path
            request(session, "GET", path)
            print("PASS Admin sidebar " + path, flush=True)
        print(f"ADMIN_NAVIGATION_E2E_PASSED={len(links.links)}")
    finally:
        if elevated:
            power_shell("ResetTestAdmin.ps1", email)


if __name__ == "__main__":
    main()
