"""Independent contract-first MVC5/EF6 HTTP + SQL regression.

Baseline (no fixture/configuration required)::
    python -B tools/ContractsV2E2E.py --gate-only

Disposable local integration run::
    python -B tools/ContractsV2E2E.py --run-writes --mailbox C:\\absolute\\pickup

Full mode requires requests, local IIS Express, NexusFreelanceMvp on SQLExpress,
and NEXUS_VNPAY_HASH_SECRET matching the local sandbox configuration. No request
is sent to VNPay: checkout redirects are inspected, never followed. Mail codes,
credentials, signed redirect queries, SQL output and HTTP bodies are not logged.
All fixtures are uniquely named and retained. Only a newly registered fixture
account is temporarily promoted to Admin, to approve a simulated wallet deposit.
No existing account or project is edited or deleted.

This is the independent reproduction-test half of the TDD subagent workflow;
it intentionally does not import or inspect the Contracts/Payments implementation.
The public API in the task is the specification; legacy helpers supply fixture
routes, anti-forgery parsing, and the local SQL connection convention only.
"""

import argparse
from concurrent.futures import ThreadPoolExecutor
from dataclasses import dataclass
from datetime import datetime, timedelta, timezone
from decimal import Decimal, ROUND_FLOOR
import email
import hashlib
import hmac
import html
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
from threading import Barrier
import time
from urllib.parse import parse_qsl, quote_plus, urlencode, urlsplit
import uuid

# Importing the existing fixture helper must not create __pycache__ files.
sys.dont_write_bytecode = True

import requests
import urllib3


def require(condition, message):
    if not condition:
        raise AssertionError(message)


def local_base(value):
    parsed = urlsplit(value)
    require(parsed.scheme in ("http", "https") and
            parsed.hostname in ("localhost", "127.0.0.1", "::1") and
            not parsed.username and not parsed.password and
            parsed.path in ("", "/") and not parsed.query and not parsed.fragment,
            "--base must be a localhost HTTP(S) origin")
    return value.rstrip("/")


def session():
    result = requests.Session()
    result.verify = False  # Local IIS Express development certificate only.
    result.trust_env = False  # Never route fixture requests through a proxy.
    return result


def request(actor, method, base, path, **kwargs):
    require(path.startswith("/") and not path.startswith("//"),
            "HTTP requests must use a local absolute path")
    try:
        result = actor.request(method, base + path, timeout=45,
                               allow_redirects=False, **kwargs)
    except requests.RequestException:
        # requests exceptions can contain complete signed callback URLs.
        raise AssertionError(f"{method} {urlsplit(path).path}: local request failed") from None
    result.encoding = "utf-8"
    return result


def status(response, expected, label):
    require(response.status_code == expected,
            f"{label}: HTTP {response.status_code}, expected {expected}")
    return response


def contracts_gate(base):
    """The FIRST network action in either mode. Do not follow the login redirect."""
    with session() as guest:
        result = request(guest, "GET", base, "/Contracts")
    status(result, 302, "GET /Contracts (guest)")
    target = urlsplit(result.headers.get("Location", ""))
    require(target.path.rstrip("/").lower() == "/account/login",
            "GET /Contracts (guest): expected redirect to Account/Login")
    require(not target.netloc or target.netloc == urlsplit(base).netloc,
            "GET /Contracts (guest): login redirect must stay local")
    print("PASS GET /Contracts (guest): HTTP 302 -> Account/Login", flush=True)


def sql_literal(value):
    return "N'" + str(value).replace("'", "''") + "'"


class Oracle:
    """Independent persisted-state oracle; SQL mutations are fixture-admin-only."""

    def __init__(self):
        self.command = shutil.which("sqlcmd") or (
            r"C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\180\Tools\Binn\SQLCMD.EXE")
        self.projects = []
        self.users = []
        require(self.scalar("SELECT DB_NAME()") == "NexusFreelanceMvp",
                "Expected the dedicated NexusFreelanceMvp local database")

    def execute(self, query):
        # -y 0 preserves complete JSON and suppresses headings; SQLCMD rejects
        # combining it with -h (including -h -1).
        result = subprocess.run([
            self.command, "-S", r"lpc:.\SQLEXPRESS", "-d", "NexusFreelanceMvp",
            "-E", "-C", "-I", "-b", "-y", "0", "-w", "65535",
            "-Q", "SET NOCOUNT ON; " + query,
        ], capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=60)
        require(result.returncode == 0, "Independent SQL oracle failed (SQL output suppressed)")
        return result.stdout.strip()

    def scalar(self, query):
        return self.execute(query)

    def rows(self, query):
        try:
            # SQL Server can return NULL/no text for an empty FOR JSON result.
            json_query = "SELECT COALESCE((" + query + " FOR JSON PATH, INCLUDE_NULL_VALUES),N'[]')"
            return json.loads("".join(self.execute(json_query).splitlines()))
        except (ValueError, TypeError):
            raise AssertionError("Independent SQL oracle returned invalid JSON") from None

    def one(self, query, label):
        found = self.rows(query)
        require(len(found) == 1, label + ": expected exactly one persisted row")
        return found[0]

    def snapshot(self):
        """Include immutable terms, signatures, work, escrow, ledgers AND PSP intent."""
        pids = ",".join(map(str, self.projects)) or "-1"
        uids = ",".join(map(str, self.users)) or "-1"
        contracts = f"SELECT Id FROM dbo.Contracts WHERE ProjectId IN ({pids})"
        workspaces = f"SELECT Id FROM dbo.Workspaces WHERE ProjectId IN ({pids})"
        queries = {
            "Projects": f"Id IN ({pids})",
            "Contracts": f"ProjectId IN ({pids})",
            "ContractVersions": f"ContractId IN ({contracts})",
            "ContractMembers": f"ContractId IN ({contracts})",
            "ContractAcceptances": "ContractVersionId IN (SELECT Id FROM dbo.ContractVersions "
                                   f"WHERE ContractId IN ({contracts}))",
            "Workspaces": f"ProjectId IN ({pids})",
            "EscrowMilestones": f"ProjectId IN ({pids})",
            "EscrowShares": f"ProjectId IN ({pids})",
            "WalletAccounts": f"UserAccountId IN ({uids})",
            "WalletEntries": f"UserAccountId IN ({uids})",
            "MoneyRequests": f"UserAccountId IN ({uids})",
            "PaymentIntents": f"ContractId IN ({contracts})",
            "Tasks": f"WorkspaceId IN ({workspaces})",
            "TaskHistories": f"WorkTaskId IN (SELECT Id FROM dbo.Tasks WHERE WorkspaceId IN ({workspaces}))",
            "Deliverables": f"WorkspaceId IN ({workspaces})",
            "WorkspaceMessages": f"WorkspaceId IN ({workspaces})",
            "WorkspaceFiles": f"WorkspaceId IN ({workspaces})",
            "WorkspaceFileVersions": "WorkspaceFileId IN (SELECT Id FROM dbo.WorkspaceFiles "
                                     f"WHERE WorkspaceId IN ({workspaces}))",
        }
        # Most tables have Id. ContractMembers/Acceptances may use composite keys.
        parts = [f"JSON_QUERY(COALESCE((SELECT * FROM dbo.[{table}] WHERE {where} "
                 f"FOR JSON PATH, INCLUDE_NULL_VALUES),N'[]')) AS [{table}]"
                 for table, where in queries.items()]
        data = json.loads("".join(self.execute("SELECT " + ",".join(parts) +
                                             " FOR JSON PATH, WITHOUT_ARRAY_WRAPPER").splitlines()))
        for rows in data.values():
            rows.sort(key=lambda row: json.dumps(row, sort_keys=True))
        return data

    def wallet(self, uid):
        row = self.one(f"SELECT COALESCE(SUM(Balance),0) AS Balance, "
                       f"COALESCE(SUM(Held),0) AS Held FROM dbo.WalletAccounts "
                       f"WHERE UserAccountId={uid}", "wallet totals")
        return Decimal(str(row["Balance"])), Decimal(str(row["Held"]))

    def reconcile(self):
        ids = ",".join(map(str, self.users))
        require(self.scalar(f"""SELECT COUNT(*) FROM dbo.WalletAccounts w
WHERE w.UserAccountId IN ({ids}) AND (w.Balance<0 OR w.Held<0
 OR w.Balance<>COALESCE((SELECT SUM(e.BalanceDelta) FROM dbo.WalletEntries e
 WHERE e.UserAccountId=w.UserAccountId),0)
 OR w.Held<>COALESCE((SELECT SUM(e.HeldDelta) FROM dbo.WalletEntries e
 WHERE e.UserAccountId=w.UserAccountId),0))""") == "0",
                "Fixture wallets must reconcile exactly to their ledgers")


@dataclass
class Actor:
    http: requests.Session
    uid: int
    address: str
    role: str


class Regression:
    def __init__(self, base, mailbox, secret):
        # Loaded only AFTER the route gate, never for --gate-only.
        from SmokeE2E import forms
        self.forms = forms
        self.base, self.mailbox, self.secret = base, mailbox, secret
        self.db = Oracle()
        self.run_id = uuid.uuid4().hex[:12]
        self.password = "ContractV2!" + uuid.uuid4().hex + "Aa7"
        self.passed = 0

    def pass_check(self, label):
        self.passed += 1
        print("PASS " + label, flush=True)

    def get(self, actor, path, expected=200):
        return status(request(actor.http, "GET", self.base, path), expected,
                      "GET " + urlsplit(path).path)

    def token(self, actor, page="/"):
        markup = self.get(actor, page).text
        candidates = [form["fields"].get("__RequestVerificationToken")
                      for form in self.forms(markup)]
        token = next((value for value in candidates if value), None)
        require(token is not None, "Accessible fixture page must supply anti-forgery token")
        return token

    def post(self, actor, path, fields=None, page="/", expected=302, csrf=True, files=None):
        data = dict(fields or {})
        if csrf:
            data["__RequestVerificationToken"] = self.token(actor, page)
        response = request(actor.http, "POST", self.base, path, data=data, files=files)
        if expected is not None:
            status(response, expected, "POST " + urlsplit(path).path)
        return response

    def unchanged(self, before, label):
        require(self.db.snapshot() == before, label + ": persisted state changed")

    def deny(self, actor, path, fields, label, page="/", csrf=True, files=None, private=False):
        # Obtain CSRF before the snapshot, from this actor's own accessible page.
        data = dict(fields)
        if csrf:
            data["__RequestVerificationToken"] = self.token(actor, page)
        before = self.db.snapshot()
        response = request(actor.http, "POST", self.base, path, data=data, files=files)
        allowed = (403, 404) if private else (200, 302, 400, 403, 409, 422)
        require(response.status_code in allowed, label + ": unexpected response status")
        if response.status_code == 302:
            target = urlsplit(response.headers.get("Location", ""))
            require(bool(target.path) and (not target.netloc or target.netloc == urlsplit(self.base).netloc)
                    and target.path.lower() != "/account/login",
                    label + ": denial must remain in authenticated local application")
        self.unchanged(before, label)
        self.pass_check(label)
        return response

    def pickup_code(self, address, role):
        deadline = time.monotonic() + 15
        while True:
            found = []
            for path in self.mailbox.glob("*.eml"):
                message = email.message_from_bytes(path.read_bytes())
                if address not in str(message.get("To", "")):
                    continue
                for part in message.walk():
                    if part.get_content_maintype() != "text":
                        continue
                    raw = part.get_payload(decode=True)
                    if raw is None:
                        continue
                    body = raw.decode(part.get_content_charset() or "utf-8", errors="replace")
                    match = re.search(r"\b(\d{6})\b", body)
                    if role in body and match:
                        found.append((path.stat().st_mtime_ns, match.group(1)))
            if found:
                return max(found)[1]
            require(time.monotonic() < deadline, "No OTP for this fixture in configured pickup mailbox")
            time.sleep(0.2)

    def register(self, label, role):
        address = f"contractv2-{self.run_id}-{label}@example.test"
        actor = Actor(session(), 0, address, role)
        response = self.post(actor, "/Account/Register", {
            "Name": "Contract V2 " + label + " " + self.run_id,
            "Email": address, "Password": self.password, "Role": role,
        }, "/Account/Register")
        confirm = response.headers.get("Location", "")
        require(urlsplit(confirm).path.lower() == "/account/confirmregistration",
                "Disposable registration must require OTP")
        if urlsplit(confirm).netloc:
            require(urlsplit(confirm).netloc == urlsplit(self.base).netloc,
                    "OTP page must remain local")
            confirm = urlsplit(confirm).path + "?" + urlsplit(confirm).query
        self.post(actor, "/Account/ConfirmRegistration", {
            "Email": address, "Role": role,
            "Code": self.pickup_code(address, role),
        }, confirm)
        self.post(actor, "/Account/Login", {
            "Email": address, "Role": role, "Password": self.password,
        }, "/Account/Login")
        require(".ASPXAUTH" in actor.http.cookies, "OTP-verified fixture must authenticate")
        actor.uid = int(self.db.scalar("SELECT Id FROM dbo.Users WHERE Email=" +
                                      sql_literal(address) + " AND Role=" + sql_literal(role)))
        self.db.users.append(actor.uid)
        return actor

    def deposit(self, client, admin, amount):
        # This is the ONLY direct SQL write, scoped to a freshly registered user.
        guard = f"Id={admin.uid} AND Email={sql_literal(admin.address)}"
        self.db.execute(f"UPDATE dbo.Users SET Role='Admin' WHERE {guard} AND Role='Client'; "
                        "IF @@ROWCOUNT<>1 THROW 51000,'Fixture admin promotion failed',1;")
        try:
            self.post(admin, "/Account/Login", {"Email": admin.address, "Role": "Admin",
                                               "Password": self.password}, "/Account/Login")
            require(self.db.wallet(client.uid) == (0, 0), "Fresh client wallet must start empty")
            reference = "contracts-v2-" + self.run_id
            self.post(client, "/Wallet/RequestMoney", {
                "kind": "Deposit", "amount": amount, "reference": reference,
                "details": "Disposable local test; simulated deposit, no bank transfer",
            }, "/Wallet")
            rid = int(self.db.scalar("SELECT Id FROM dbo.MoneyRequests WHERE Reference=" +
                                     sql_literal(reference) + f" AND UserAccountId={client.uid}"))
            self.post(admin, f"/Wallet/ReviewMoney/{rid}", {
                "approve": "true", "reason": "Simulated local fixture deposit only",
            }, "/Wallet/Admin")
            require(self.db.wallet(client.uid) == (amount, 0),
                    "Deposit must actually fund the client before unsigned-funding denials")
            self.db.reconcile()
        finally:
            self.db.execute(f"UPDATE dbo.Users SET Role='Client' WHERE {guard} AND Role='Admin'")
        self.pass_check("fresh client has sufficient wallet balance before negative funding tests")

    def project(self, client, kind, titles):
        response = self.post(client, "/Projects/Create", {
            "Title": f"Contracts V2 {kind} {self.run_id}",
            "Description": "Independent contract scopes, immutable signed terms and escrow isolation.",
            "ProjectType": kind, "Industry": "Công nghệ thông tin", "Budget": 1000000,
            "Deadline": (datetime.now(timezone.utc) + timedelta(days=90)).strftime("%Y-%m-%d"),
        }, "/Projects/Create")
        from SmokeE2E import route_id
        pid = route_id(response.headers.get("Location"))
        self.db.projects.append(pid)
        manage = f"/Projects/Manage/{pid}"
        for title in titles:
            self.post(client, "/Projects/AddRequirement", {
                "ProjectId": pid, "Title": title, "RequiredCount": 1,
            }, manage)
        self.post(client, f"/Projects/Publish/{pid}", {}, manage)
        roles = self.db.rows(f"SELECT Id,Title FROM dbo.RoleRequirements WHERE ProjectId={pid} ORDER BY Id")
        require(len(roles) == len(titles), "HTTP project fixture must persist all independently hireable roles")
        return pid, [row["Id"] for row in roles]

    def proposal(self, worker, pid, role_id, price):
        self.post(worker, "/Proposals/Apply", {
            "ProjectId": pid, "RoleRequirementId": role_id,
            "Pitch": "Independent backend deliverable, reproducible SQL checks and documented handover.",
            "QuotedPrice": price, "DeliveryDays": 14,
            "MilestonePlan": f"Complete isolated delivery | {price}",
        }, f"/Projects/Details/{pid}")
        return int(self.db.scalar(f"SELECT Id FROM dbo.Proposals WHERE ProjectId={pid} "
                                  f"AND ApplicantId={worker.uid} AND RoleRequirementId={role_id}"))

    def accept(self, client, pid, proposal_id):
        self.post(client, f"/Projects/Accept/{pid}", {"proposalId": proposal_id},
                  f"/Projects/Manage/{pid}")
        row = self.db.one("SELECT c.* FROM dbo.Contracts c JOIN dbo.ContractMembers m "
                          f"ON m.ContractId=c.Id WHERE c.ProjectId={pid} AND m.ProposalId={proposal_id}",
                          "Individual proposal acceptance creates its contract immediately")
        return row["Id"]

    def version(self, cid):
        return self.db.one("SELECT v.* FROM dbo.ContractVersions v JOIN dbo.Contracts c "
                           f"ON c.Id=v.ContractId WHERE c.Id={cid} AND v.VersionNumber=c.CurrentVersion",
                           "CurrentVersion must identify one immutable version")

    def workspace(self, cid):
        row = self.db.one(f"SELECT Id FROM dbo.Workspaces WHERE ContractId={cid}",
                          "Each contract owns one workspace/channel")
        return row["Id"]

    def milestone(self, cid):
        return self.db.one(f"SELECT * FROM dbo.EscrowMilestones WHERE ContractId={cid}",
                           "Fixture one-line plan creates one contract-scoped milestone")

    def roster(self, cid, expected):
        members = self.db.rows(f"SELECT UserAccountId,ProposalId,Weight FROM dbo.ContractMembers WHERE ContractId={cid}")
        shares = self.db.rows(f"SELECT UserAccountId,Weight FROM dbo.EscrowShares WHERE ContractId={cid}")
        require({row["UserAccountId"] for row in members} == {actor.uid for actor in expected}
                and len(members) == len(expected), "Contract roster must exactly match accepted proposal members")
        require(all(row["ProposalId"] and Decimal(str(row["Weight"])) > 0 for row in members),
                "Contract roster must retain proposal provenance and positive payout weights")
        require({(row["UserAccountId"], Decimal(str(row["Weight"]))) for row in shares} ==
                {(row["UserAccountId"], Decimal(str(row["Weight"]))) for row in members}
                and len(shares) == len(members), "Escrow shares must be scoped to this exact contract roster")

    def confirm(self, actor, cid, version=None):
        version = version or self.version(cid)
        self.post(actor, f"/Contracts/Confirm/{cid}", {
            "versionId": version["Id"], "hash": version["Hash"],
        }, f"/Contracts/Details/{cid}")
        records = self.db.rows(f"SELECT AcceptedHash,AcceptedAtUtc FROM dbo.ContractAcceptances "
                               f"WHERE ContractVersionId={version['Id']} AND UserAccountId={actor.uid}")
        require(len(records) == 1 and records[0]["AcceptedHash"] == version["Hash"]
                and records[0]["AcceptedAtUtc"] is not None,
                "Confirmation must persist one current-version/hash acceptance for the authenticated signer")

    def signatures(self, cid, actors):
        version = self.version(cid)
        rows = self.db.rows("SELECT UserAccountId,AcceptedHash,AcceptedAtUtc FROM dbo.ContractAcceptances "
                            f"WHERE ContractVersionId={version['Id']}")
        require(len(rows) == len(actors) and {row["UserAccountId"] for row in rows} ==
                {actor.uid for actor in actors} and all(row["AcceptedHash"] == version["Hash"]
                and row["AcceptedAtUtc"] is not None for row in rows),
                "Every required party must sign the same current version/hash exactly once")

    def unsigned_denials(self, client, cid, label):
        mid = self.milestone(cid)["Id"]
        self.deny(client, f"/Wallet/Fund/{mid}", {}, label + ": wallet hold blocked",
                  f"/Contracts/Details/{cid}")
        self.deny(client, f"/Payments/Checkout/{mid}", {}, label + ": PSP intent blocked",
                  f"/Contracts/Details/{cid}")

    def revise_payload(self, cid, marker):
        version = self.version(cid)
        return {"scope": "Independent revised scope " + marker + " " + self.run_id,
                "milestonePlan": f"Revised complete delivery | {int(version['Price'])}",
                "deliveryDays": 21}

    def exercise_revision(self, client, worker, other, cid, other_cid):
        old = self.version(cid)
        require(old["VersionNumber"] == 1 and old["Price"] == 100000 and old["DeliveryDays"] == 14
                and bool(old["Hash"]) and bool(old["SnapshotJson"]),
                "Initial immutable terms must reflect the accepted HTTP proposal")
        self.deny(worker, f"/Contracts/Confirm/{cid}", {"versionId": old["Id"], "hash": "0" * 64},
                  "wrong hash cannot create a signature", f"/Contracts/Details/{cid}")
        foreign = self.version(other_cid)
        self.deny(worker, f"/Contracts/Confirm/{cid}", {
            "versionId": foreign["Id"], "hash": foreign["Hash"],
        }, "another contract's valid version/hash cannot be signed here", f"/Contracts/Details/{cid}")
        for path, payload in ((f"/Contracts/Confirm/{cid}", {"versionId": old["Id"], "hash": old["Hash"]}),
                              (f"/Contracts/Revise/{cid}", self.revise_payload(cid, "csrf")),
                              (f"/Contracts/Decline/{cid}", {"reason": "Missing anti-forgery must not alter contract"})):
            response = self.deny(client, path, payload, "contract mutation requires CSRF: " + urlsplit(path).path,
                                 csrf=False)
            status(response, 400, "Missing contract anti-forgery")
        self.confirm(client, cid)
        self.confirm(worker, cid)
        self.signatures(cid, [client, worker])
        self.confirm(worker, cid)  # Signature replay must not append another acceptance.
        self.signatures(cid, [client, worker])
        old_acceptances = self.db.rows(f"SELECT * FROM dbo.ContractAcceptances WHERE ContractVersionId={old['Id']} ORDER BY UserAccountId")
        self.deny(worker, f"/Contracts/Revise/{cid}", self.revise_payload(cid, "nonowner"),
                  "counterparty cannot revise owner terms", f"/Contracts/Details/{cid}", private=True)
        self.post(client, f"/Contracts/Revise/{cid}", self.revise_payload(cid, "v2"), f"/Contracts/Details/{cid}")
        current = self.version(cid)
        require(current["Id"] != old["Id"] and current["VersionNumber"] == 2
                and current["Hash"] != old["Hash"] and current["DeliveryDays"] == 21
                and current["Price"] == old["Price"] and current["SnapshotJson"] != old["SnapshotJson"],
                "Revision must create new terms, hash and monotonically increasing immutable version")
        require(self.db.one(f"SELECT * FROM dbo.ContractVersions WHERE Id={old['Id']}", "old version") == old,
                "Revision must preserve original version bytes and terms")
        require(self.db.rows(f"SELECT * FROM dbo.ContractAcceptances WHERE ContractVersionId={old['Id']} ORDER BY UserAccountId") == old_acceptances,
                "Revision must preserve historical signature evidence")
        self.signatures(cid, [])
        for version_id, digest, label in ((old["Id"], old["Hash"], "stale version"),
                                          (current["Id"], old["Hash"], "stale hash"),
                                          (old["Id"], current["Hash"], "mixed version/hash")):
            self.deny(worker, f"/Contracts/Confirm/{cid}", {"versionId": version_id, "hash": digest},
                      label + " cannot satisfy current signature", f"/Contracts/Details/{cid}")
        self.unsigned_denials(client, cid, "revision invalidates readiness")
        self.confirm(client, cid)
        self.unsigned_denials(client, cid, "only owner re-signed")
        self.confirm(worker, cid)
        self.signatures(cid, [client, worker])
        self.unsigned_denials(client, other_cid, "signing contract A cannot authorize contract B")
        self.deny(other, f"/Contracts/Confirm/{cid}", {"versionId": current["Id"], "hash": current["Hash"]},
                  "other contractor cannot sign contract A", private=True)
        self.pass_check("immutable revision, stale terms and exact per-contract current signatures")

    def fund_wallet(self, client, cid):
        milestone = self.milestone(cid)
        before = self.db.wallet(client.uid)
        self.post(client, f"/Wallet/Fund/{milestone['Id']}", {}, f"/Contracts/Details/{cid}")
        require(self.milestone(cid)["Status"] == "Funded", "Signed contract must fund by wallet")
        amount = Decimal(str(milestone["Amount"]))
        require(self.db.wallet(client.uid) == (before[0] - amount, before[1] + amount),
                "Wallet funding must debit available balance and hold exactly its contract amount")
        self.deny(client, f"/Wallet/Fund/{milestone['Id']}", {},
                  "wallet funding replay cannot hold twice", f"/Contracts/Details/{cid}")
        self.deny(client, f"/Contracts/Revise/{cid}", self.revise_payload(cid, "funded"),
                  "funded terms cannot be revised", f"/Contracts/Details/{cid}")
        self.db.reconcile()

    @staticmethod
    def encoded_signature(fields, secret):
        # VNPay v2.1: sort names ordinally, URL-encode key/value, HMAC-SHA512.
        canonical = "&".join(quote_plus(key, safe="") + "=" + quote_plus(str(value), safe="")
                             for key, value in sorted(fields.items())
                             if key.startswith("vnp_") and key not in
                             ("vnp_SecureHash", "vnp_SecureHashType") and str(value) != "")
        return hmac.new(secret.encode("utf-8"), canonical.encode("utf-8"), hashlib.sha512).hexdigest()

    def callback(self, fields):
        result = dict(fields)
        result["vnp_SecureHash"] = self.encoded_signature(result, self.secret)
        return result

    def ipn(self, fields, actor=None):
        # Anonymous GET: no CSRF token or authenticated cookie is sent.
        owned = actor is None
        http = actor or session()
        try:
            response = request(http, "GET", self.base, "/Payments/Ipn?" + urlencode(fields))
            status(response, 200, "Anonymous PSP IPN")
            try:
                payload = response.json()
            except ValueError:
                raise AssertionError("PSP IPN must return protocol JSON (body suppressed)") from None
            require(isinstance(payload, dict) and isinstance(payload.get("RspCode"), str),
                    "PSP IPN must return a string RspCode")
            return payload["RspCode"]
        finally:
            if owned:
                http.close()

    def return_no_writes(self, fields, label):
        before = self.db.snapshot()
        with session() as guest:
            response = request(guest, "GET", self.base, "/Payments/Return?" + urlencode(fields))
        require(response.status_code in (200, 302, 400), label + ": unexpected return status")
        self.unchanged(before, label)
        self.pass_check(label)

    def exercise_psp(self, client, cid):
        milestone = self.milestone(cid)
        mid = milestone["Id"]
        before_checkout = self.db.snapshot()
        missing_csrf = self.deny(client, f"/Payments/Checkout/{mid}", {}, "checkout requires CSRF", csrf=False)
        status(missing_csrf, 400, "Missing checkout anti-forgery")
        response = self.post(client, f"/Payments/Checkout/{mid}", {}, f"/Contracts/Details/{cid}")
        # Never follow this Location: it is a protocol artifact, not a network target.
        location = urlsplit(response.headers.get("Location", ""))
        require(location.scheme == "https" and location.hostname == "sandbox.vnpayment.vn",
                "Local checkout must yield an HTTPS VNPay SANDBOX redirect")
        pairs = parse_qsl(location.query, keep_blank_values=True)
        require(len(pairs) == len(dict(pairs)), "Checkout redirect cannot contain duplicate query keys")
        query = dict(pairs)
        require(query.get("vnp_Version") == "2.1.0" and query.get("vnp_Command") == "pay"
                and query.get("vnp_CurrCode") == "VND" and bool(query.get("vnp_TmnCode"))
                and bool(query.get("vnp_TxnRef")), "Checkout must carry complete VNPay v2.1 payment identity")
        require(query.get("vnp_Amount") == str(int(Decimal(str(milestone["Amount"])) * 100)),
                "Checkout must encode the exact milestone amount in VND minor units")
        require(hmac.compare_digest(query.get("vnp_SecureHash", "").lower(),
                                    self.encoded_signature(query, self.secret)),
                "Checkout redirect must have a valid independent HMAC-SHA512 signature")
        return_url = urlsplit(query.get("vnp_ReturnUrl", ""))
        require(return_url.netloc == urlsplit(self.base).netloc and return_url.path == "/Payments/Return",
                "Sandbox checkout must return to the configured local Payments/Return")
        after_checkout = self.db.snapshot()
        intent = self.db.one(f"SELECT * FROM dbo.PaymentIntents WHERE ContractId={cid} "
                             f"AND EscrowMilestoneId={mid}", "Checkout persists one contract/milestone intent")
        require(intent["Status"] == "Pending" and intent["TxnRef"] == query["vnp_TxnRef"]
                and Decimal(str(intent["Amount"])) == Decimal(str(milestone["Amount"]))
                and intent["Currency"] == "VND" and intent["MerchantCode"] == query["vnp_TmnCode"]
                and str(intent["Environment"]).lower() == "sandbox" and bool(intent["CheckoutUrl"]),
                "Pending SQL intent must bind exact contract, milestone, txn reference, VND amount and sandbox merchant")
        require(after_checkout["PaymentIntents"] != before_checkout["PaymentIntents"],
                "Checkout must persist its payment intent")
        for table in ("WalletAccounts", "WalletEntries", "EscrowMilestones"):
            require(after_checkout[table] == before_checkout[table],
                    "Checkout alone must not fund escrow or credit a wallet")
        self.deny(client, f"/Contracts/Revise/{cid}", self.revise_payload(cid, "intent"),
                  "payment intent freezes signed terms before funding", f"/Contracts/Details/{cid}")
        callback = {key: query[key] for key in ("vnp_TmnCode", "vnp_TxnRef", "vnp_Amount", "vnp_CurrCode")}
        callback.update({"vnp_ResponseCode": "00", "vnp_TransactionStatus": "00",
                         "vnp_TransactionNo": str(int(self.run_id, 16)), "vnp_BankCode": "NCB",
                         "vnp_OrderInfo": query.get("vnp_OrderInfo", "Local contract regression"),
                         "vnp_PayDate": datetime.now(timezone(timedelta(hours=7))).strftime("%Y%m%d%H%M%S")})
        valid = self.callback(callback)
        self.return_no_writes(valid, "signed browser success return cannot write intent/escrow/wallet")
        self.return_no_writes(self.callback({**callback, "vnp_ResponseCode": "24", "vnp_TransactionStatus": "02"}),
                              "signed browser failure return cannot write intent/escrow/wallet")
        for label, changed, sign in (
            ("invalid signature", {}, False),
            ("wrong amount", {"vnp_Amount": str(int(callback["vnp_Amount"]) + 100)}, True),
            ("wrong currency", {"vnp_CurrCode": "USD"}, True),
            ("wrong merchant", {"vnp_TmnCode": "OTHER001" if callback["vnp_TmnCode"] != "OTHER001" else "OTHER002"}, True),
        ):
            fields = self.callback({**callback, **changed})
            if not sign:
                fields["vnp_SecureHash"] = "0" * 128
            before = self.db.snapshot()
            code = self.ipn(fields)
            require(code not in ("00", "02"), label + ": invalid callback must be rejected, not accepted/paid")
            self.unchanged(before, label + " leaves intent eligible for genuine callback")
            self.pass_check("PSP rejects " + label + " without poisoning intent or moving funds")
        before = self.db.snapshot()
        wallet_before = self.db.wallet(client.uid)
        barrier = Barrier(2)

        def competing_ipn():
            with session() as http:
                barrier.wait(timeout=15)
                return self.ipn(valid, actor=http)

        with ThreadPoolExecutor(max_workers=2) as pool:
            first, second = pool.submit(competing_ipn), pool.submit(competing_ipn)
            codes = sorted((first.result(timeout=75), second.result(timeout=75)))
        require(codes == ["00", "02"], "Concurrent anonymous IPNs must yield exactly one success and one duplicate")
        amount = Decimal(str(milestone["Amount"]))
        require(self.milestone(cid)["Status"] == "Funded", "Genuine IPN must fund its contract milestone")
        paid = self.db.one(f"SELECT * FROM dbo.PaymentIntents WHERE Id={intent['Id']}", "Paid intent")
        require(paid["Status"] == "Paid" and paid["TxnRef"] == intent["TxnRef"]
                and paid["ContractId"] == cid and paid["EscrowMilestoneId"] == mid
                and str(paid["ProviderTransactionNo"]) == callback["vnp_TransactionNo"]
                and paid["Amount"] == intent["Amount"] and paid["Currency"] == "VND"
                and paid["MerchantCode"] == intent["MerchantCode"],
                "Successful IPN must persist Paid once against the exact original intent/provider transaction")
        require(self.db.wallet(client.uid) == (wallet_before[0], wallet_before[1] + amount),
                "PSP success must hold exactly one payment without spending existing wallet balance")
        after = self.db.snapshot()
        new_entries = [row for row in after["WalletEntries"] if row["Id"] not in
                       {old["Id"] for old in before["WalletEntries"]}]
        require(new_entries and all(row["EscrowMilestoneId"] == mid and row["UserAccountId"] == client.uid
                                    for row in new_entries), "PSP ledger changes must belong only to this milestone/client")
        require(sum(Decimal(str(row["HeldDelta"])) for row in new_entries) == amount
                and sum(Decimal(str(row["BalanceDelta"])) for row in new_entries) == 0
                and sum(Decimal(str(row["HeldDelta"])) > 0 for row in new_entries) == 1,
                "PSP race must produce exactly one hold and no extra available funds")
        self.db.reconcile()
        require(self.ipn(valid) == "02", "Sequential IPN replay must return duplicate code 02")
        self.unchanged(after, "sequential paid IPN replay")
        late_failure = self.callback({**callback, "vnp_ResponseCode": "24", "vnp_TransactionStatus": "02"})
        require(self.ipn(late_failure) == "02", "Late signed failure after success must return duplicate code 02")
        self.unchanged(after, "late failure cannot reverse paid intent or funds")
        self.return_no_writes(valid, "browser return after paid IPN is read-only")
        self.pass_check("PSP race/replay/late failure fund exactly once with independent reconciled SQL ledger")
        return valid, late_failure

    def workspace_artifacts(self, client, worker, cid):
        wid = self.workspace(cid)
        page = f"/Workspace/Index/{wid}"
        self.get(client, page)
        self.get(worker, page)
        task_title = f"Private task {cid} {self.run_id}"
        self.post(client, "/Workspace/CreateTask", {
            "WorkspaceId": wid, "Title": task_title, "AssigneeId": worker.uid, "Priority": "High",
        }, page)
        tid = int(self.db.scalar(f"SELECT Id FROM dbo.Tasks WHERE WorkspaceId={wid} AND Title={sql_literal(task_title)}"))
        self.post(worker, f"/Workspace/UpdateTask/{tid}", {"status": "Doing"}, page)
        require(self.db.scalar(f"SELECT Status FROM dbo.Tasks WHERE Id={tid}") == "Doing",
                "Contract assignee must actually update its own task")
        message = f"Private channel {cid} {self.run_id}"
        self.post(worker, "/Workspace/SendMessage", {"WorkspaceId": wid, "Body": message}, page)
        require(self.db.scalar(f"SELECT COUNT(*) FROM dbo.WorkspaceMessages WHERE WorkspaceId={wid} "
                               f"AND AuthorId={worker.uid} AND Body={sql_literal(message)}") == "1",
                "Contract member must post to its own persistent discussion channel")
        content = (f"Private file bytes for contract {cid} {self.run_id}\n").encode("utf-8")
        self.post(worker, "/WorkspaceFiles/Upload", {"WorkspaceId": wid}, page,
                  files={"upload": ("contract-source.txt", content, "text/plain")})
        file_row = self.db.one("SELECT f.Id AS FileId,v.Id AS VersionId,v.Sha256,v.ByteLength "
                               "FROM dbo.WorkspaceFiles f JOIN dbo.WorkspaceFileVersions v ON v.WorkspaceFileId=f.Id "
                               f"WHERE f.WorkspaceId={wid}", "Contract private binary file")
        require(file_row["Sha256"].lower() == hashlib.sha256(content).hexdigest()
                and file_row["ByteLength"] == len(content), "SQL file hash/length must match independently generated bytes")
        for party in (client, worker):
            downloaded = self.get(party, f"/WorkspaceFiles/Download/{file_row['VersionId']}")
            require(downloaded.content == content, "Authorized download must return original private file bytes")
        mid = self.milestone(cid)["Id"]
        delivery_title = f"Private delivery {cid} {self.run_id}"
        self.post(worker, "/Workspace/Submit", {
            "WorkspaceId": wid, "Title": delivery_title, "Url": "https://example.test/contract-delivery",
            "EscrowMilestoneId": mid,
        }, page)
        did = int(self.db.scalar(f"SELECT Id FROM dbo.Deliverables WHERE WorkspaceId={wid} "
                                 f"AND Title={sql_literal(delivery_title)} AND SubmittedById={worker.uid} "
                                 f"AND EscrowMilestoneId={mid} AND Status='Submitted'"))
        return {"cid": cid, "wid": wid, "mid": mid, "tid": tid, "did": did,
                "file": file_row["FileId"], "version": file_row["VersionId"],
                "markers": (task_title, message, delivery_title)}

    def exercise_isolation(self, client, worker_a, worker_b, outsider, cid_a, cid_b):
        a = self.workspace_artifacts(client, worker_a, cid_a)
        b = self.workspace_artifacts(client, worker_b, cid_b)
        require(a["wid"] != b["wid"], "Contracts in one project must have distinct workspace/channel IDs")
        for party, own, foreign in ((worker_a, a, b), (worker_b, b, a)):
            markup = self.get(party, f"/Workspace/Index/{own['wid']}").text
            require(all(marker in html.unescape(markup) for marker in own["markers"]),
                    "Authorized contract workspace must display its task/channel/delivery")
            require(all(marker not in html.unescape(markup) for marker in foreign["markers"]),
                    "Contract workspace must never render another contract's private records")
            index = html.unescape(self.get(party, f"/Contracts?projectId={self.db.projects[0]}").text)
            links = set(re.findall(r"/Contracts/Details/(\d+)(?!\d)", index, re.I))
            require(str(own["cid"]) in links and str(foreign["cid"]) not in links,
                    "Contract index must show the contractor's own contract without exposing another hire")
        for actor, foreign in ((worker_b, a), (worker_a, b), (outsider, a), (outsider, b)):
            for path in (f"/Contracts/Details/{foreign['cid']}",
                         f"/Workspace/Index/{foreign['wid']}",
                         f"/WorkspaceFiles/Download/{foreign['version']}"):
                response = request(actor.http, "GET", self.base, path)
                require(response.status_code in (403, 404), "Outsider/other contractor must not read private contract/workspace/file")
            attacks = (
                ("/Workspace/CreateTask", {"WorkspaceId": foreign["wid"], "Title": "Intrusion", "AssigneeId": actor.uid}),
                (f"/Workspace/UpdateTask/{foreign['tid']}", {"status": "Done"}),
                ("/Workspace/EditTask", {"Id": foreign["tid"], "Title": "Intrusion", "AssigneeId": actor.uid, "Priority": "Low"}),
                ("/Workspace/SendMessage", {"WorkspaceId": foreign["wid"], "Body": "Intrusion into other contract"}),
                ("/Workspace/Submit", {"WorkspaceId": foreign["wid"], "Title": "Intrusion", "Url": "https://example.test/intrusion", "EscrowMilestoneId": foreign["mid"]}),
                (f"/Workspace/Decide/{foreign['did']}", {"decision": "Approved"}),
                (f"/Wallet/Fund/{foreign['mid']}", {}),
                (f"/Payments/Checkout/{foreign['mid']}", {}),
                (f"/Contracts/Decline/{foreign['cid']}", {"reason": "Attempt to decline someone else's agreement"}),
            )
            for path, fields in attacks:
                self.deny(actor, path, fields, "contract access isolation: " + urlsplit(path).path,
                          private=not path.startswith("/Wallet/"))
            self.deny(actor, "/WorkspaceFiles/Upload", {"WorkspaceId": foreign["wid"]},
                      "outsider/other contractor cannot upload to private workspace", private=True,
                      files={"upload": ("intrusion.txt", b"blocked private upload", "text/plain")})
        self.deny(worker_b, "/WorkspaceFiles/Upload", {"WorkspaceId": b["wid"], "FileId": a["file"]},
                  "own workspace ID cannot authorize another contract's file revision", private=True,
                  page=f"/Workspace/Index/{b['wid']}", files={"upload": ("revision.txt", b"blocked revision", "text/plain")})
        self.deny(client, "/Workspace/CreateTask", {
            "WorkspaceId": a["wid"], "Title": "Cross-contract assignment", "AssigneeId": worker_b.uid,
        }, "owner cannot assign A task to a project member hired only on B", f"/Workspace/Index/{a['wid']}")
        self.deny(worker_a, "/Workspace/Submit", {
            "WorkspaceId": a["wid"], "Title": "Cross-contract revision", "Url": "https://example.test/blocked",
            "EscrowMilestoneId": a["mid"], "RevisesId": b["did"],
        }, "own workspace cannot revise another contract's deliverable", f"/Workspace/Index/{a['wid']}")
        self.pass_check("same-project contractors/outsider isolated across tasks, deliveries, files and discussion channels")
        return a, b

    def exercise_settlement(self, client, worker_a, worker_b, a, b, valid, late_failure):
        before = self.db.snapshot()
        b_contract = self.db.one(f"SELECT * FROM dbo.Contracts WHERE Id={b['cid']}", "Other contract before settlement")
        b_milestone = self.milestone(b["cid"])
        a_version = self.version(a["cid"])
        amount = Decimal(str(self.milestone(a["cid"])["Amount"]))
        fee = (amount * Decimal(str(a_version["FeeRate"]))).to_integral_value(rounding=ROUND_FLOOR)
        payout_before = self.db.wallet(worker_a.uid)
        client_before = self.db.wallet(client.uid)
        self.post(client, f"/Workspace/Decide/{a['did']}", {"decision": "Approved"},
                  f"/Workspace/Index/{a['wid']}")
        require(self.milestone(a["cid"])["Status"] == "Released"
                and Decimal(str(self.milestone(a["cid"])["Fee"])) == fee,
                "Approval must release only A escrow using A's signed immutable fee rate")
        require(self.db.wallet(worker_a.uid) == (payout_before[0] + amount - fee, payout_before[1]),
                "Only contract A's freelancer receives its exact net payout")
        require(self.db.wallet(worker_b.uid) == (0, 0), "Contract A settlement must not pay contractor B")
        require(self.db.wallet(client.uid) == (client_before[0], client_before[1] - amount),
                "Contract A settlement releases exactly its own client hold")
        require(self.db.one(f"SELECT * FROM dbo.Contracts WHERE Id={b['cid']}", "Other contract after settlement") == b_contract
                and self.milestone(b["cid"]) == b_milestone,
                "Settling A must preserve all of B's contract/milestone state")
        require(self.db.scalar(f"SELECT Status FROM dbo.Contracts WHERE Id={a['cid']}") == "Completed",
                "A's fully settled contract must complete")
        require(self.db.scalar(f"SELECT Status FROM dbo.Projects WHERE Id={self.db.projects[0]}") != "Completed",
                "Project cannot complete while contract B remains unsettled")
        after = self.db.snapshot()
        for table in ("ContractVersions", "ContractMembers", "ContractAcceptances", "EscrowShares", "PaymentIntents"):
            require(after[table] == before[table], "Settlement must preserve immutable terms/roster/signatures/PSP evidence")
        self.deny(client, f"/Workspace/Decide/{a['did']}", {"decision": "Approved"},
                  "approval replay cannot pay A twice", f"/Workspace/Index/{b['wid']}")
        settled = self.db.snapshot()
        require(self.ipn(valid) == "02" and self.ipn(late_failure) == "02",
                "Paid PSP callbacks stay duplicates after another contract settles")
        self.unchanged(settled, "late PSP duplicates cannot revive/reverse settled allocations")
        self.return_no_writes(valid, "browser return after settlement remains read-only")
        self.db.reconcile()
        self.pass_check("single-contract settlement cannot finish/pay another contract or complete their project")
        b_version = self.version(b["cid"])
        b_amount = Decimal(str(b_milestone["Amount"]))
        b_fee = (b_amount * Decimal(str(b_version["FeeRate"]))).to_integral_value(rounding=ROUND_FLOOR)
        a_wallet = self.db.wallet(worker_a.uid)
        pid = self.db.projects[0]
        contracts_before_lock = self.db.rows(f"SELECT * FROM dbo.Contracts WHERE ProjectId={pid} ORDER BY Id")
        require(self.db.scalar(f"SELECT Status FROM dbo.Projects WHERE Id={pid}") == "Published",
                "Independent contract work must leave recruitment open until the client closes it")
        self.post(client, f"/Projects/LockTeam/{pid}", {}, f"/Projects/Manage/{pid}")
        require(self.db.scalar(f"SELECT CONVERT(int,Locked) FROM dbo.ProjectTeams WHERE ProjectId={pid}") == "1"
                and self.db.scalar(f"SELECT Status FROM dbo.Projects WHERE Id={pid}") == "InProgress",
                "Explicit client LockTeam must close recruitment while funded contract B is active")
        require(self.db.rows(f"SELECT * FROM dbo.Contracts WHERE ProjectId={pid} ORDER BY Id") == contracts_before_lock,
                "Closing recruitment must not create additional contracts or rewrite existing agreements")
        self.post(client, f"/Workspace/Decide/{b['did']}", {"decision": "Approved"},
                  f"/Workspace/Index/{b['wid']}")
        require(self.milestone(b["cid"])["Status"] == "Released"
                and self.db.wallet(worker_b.uid) == (b_amount - b_fee, 0)
                and self.db.wallet(worker_a.uid) == a_wallet and self.db.wallet(client.uid)[1] == 0,
                "Independent B settlement must pay only B and exhaust its own remaining hold")
        require(self.db.scalar(f"SELECT Status FROM dbo.Contracts WHERE Id={b['cid']}") == "Completed"
                and self.db.scalar(f"SELECT Status FROM dbo.Projects WHERE Id={self.db.projects[0]}") == "Completed",
                "Project may complete only once both independent contracts settle")
        fully_settled = self.db.snapshot()
        require(self.ipn(valid) == "02" and self.ipn(late_failure) == "02",
                "Late success/failure IPNs after their own contract settles must both return 02")
        self.unchanged(fully_settled, "late IPNs cannot recreate held funds or reverse a settled PSP payment")
        self.db.reconcile()
        self.pass_check("PSP duplicate after its own settlement cannot refill escrow or reverse payouts")

    def group_contract(self, client, leader, member):
        created = self.post(leader, "/Groups/Create", {
            "Name": "Contract roster " + self.run_id,
            "Description": "Two disposable specialists signing one group agreement",
        }, "/Groups/Index")
        match = re.search(r"/Groups/Details/(\d+)", created.headers.get("Location", ""))
        require(match is not None, "HTTP group fixture must create a private group")
        gid = int(match.group(1))
        self.post(leader, "/Groups/Invite", {"GroupId": gid, "Email": member.address}, f"/Groups/Details/{gid}")
        invitation_forms = [form for form in self.forms(self.get(member, "/Groups/Index").text)
                            if "/Groups/Respond/" in form["action"] and
                            "accept=true" in html.unescape(form["action"]).lower()]
        require(len(invitation_forms) == 1, "Fresh member must have exactly one accept-invitation action")
        self.post(member, html.unescape(invitation_forms[0]["action"]), {}, "/Groups/Index")
        pid, roles = self.project(client, "TEAM", ["Backend Developer", "Frontend Developer"])
        page = f"/GroupBids/Create?groupId={gid}&projectId={pid}"
        bid_forms = [form for form in self.forms(self.get(leader, page).text) if "/GroupBids/Create" in form["action"]]
        require(len(bid_forms) == 1, "Leader must receive exactly one group bid fixture form")
        payload = dict(bid_forms[0]["fields"])
        payload.pop("__RequestVerificationToken", None)
        candidates = sorted(int(match.group(1)) for key in payload
                            if (match := re.fullmatch(r"Candidates\[(\d+)\]\.UserAccountId", key)))
        require(len(candidates) == 2 and {int(payload[f"Candidates[{index}].UserAccountId"]) for index in candidates}
                == {leader.uid, member.uid}, "Group bid fixture must select exactly leader and invited roster member")
        for index, role_id in zip(candidates, roles):
            payload.update({f"Candidates[{index}].Selected": "true",
                            f"Candidates[{index}].RoleRequirementId": role_id,
                            f"Candidates[{index}].QuotedPrice": 60000})
        payload.update({"ProjectId": pid, "FreelancerGroupId": gid, "Pitch": "Two specialists with distinct signatures",
                        "DeliveryDays": 14, "MilestonePlan": "Complete roster delivery | 120000"})
        self.post(leader, "/GroupBids/Create", payload, page)
        bid = int(self.db.scalar(f"SELECT Id FROM dbo.GroupBids WHERE ProjectId={pid} AND FreelancerGroupId={gid}"))
        self.post(client, f"/GroupBids/Decide/{bid}?accept=true", {}, f"/Projects/Manage/{pid}")
        contract = self.db.one(f"SELECT * FROM dbo.Contracts WHERE ProjectId={pid}",
                               "Whole group acceptance must create one contract immediately")
        cid = contract["Id"]
        require(contract["CounterpartyId"] == leader.uid, "Group representative must be the accepting bid's leader")
        require(self.db.scalar(f"SELECT COUNT(*) FROM dbo.Proposals WHERE GroupBidId={bid} AND Status='Accepted'") == "2",
                "Both group member proposals must be accepted atomically")
        self.roster(cid, [leader, member])
        self.workspace(cid)
        self.get(member, f"/Contracts/Details/{cid}")
        self.confirm(client, cid)
        self.unsigned_denials(client, cid, "TEAM missing representative and member")
        self.confirm(leader, cid)
        self.signatures(cid, [client, leader])
        self.unsigned_denials(client, cid, "TEAM representative signature cannot substitute for roster member")
        self.confirm(member, cid)
        self.signatures(cid, [client, leader, member])
        # Revise after ALL parties sign: require the full roster to re-sign as well.
        self.post(client, f"/Contracts/Revise/{cid}", self.revise_payload(cid, "team"), f"/Contracts/Details/{cid}")
        self.signatures(cid, [])
        self.confirm(client, cid)
        self.confirm(leader, cid)
        self.unsigned_denials(client, cid, "TEAM revision requires fresh signature from every roster member")
        self.confirm(member, cid)
        self.signatures(cid, [client, leader, member])
        self.fund_wallet(client, cid)
        self.get(member, f"/Workspace/Index/{self.workspace(cid)}")
        require(self.db.scalar(f"SELECT Status FROM dbo.Projects WHERE Id={pid}") == "Published",
                "Group contract can be active while project recruitment remains Published")
        membership = self.db.one(f"SELECT * FROM dbo.FreelancerGroupMembers WHERE FreelancerGroupId={gid} "
                                  f"AND UserAccountId={member.uid}", "Active contracted group membership")
        require(membership["Status"] == "Active", "Roster member must be active before attempted leave")
        self.deny(member, f"/Groups/Leave/{gid}", {},
                  "active Published contract blocks leaving its source group", f"/Groups/Details/{gid}")
        require(self.db.one(f"SELECT * FROM dbo.FreelancerGroupMembers WHERE FreelancerGroupId={gid} "
                            f"AND UserAccountId={member.uid}", "Membership after blocked leave") == membership,
                "Published project status must not allow an active contract member to leave")
        self.pass_check("TEAM whole-bid contract requires client, representative and every roster member on latest terms")

    def exercise_decline(self, client, worker, outsider):
        pid, roles = self.project(client, "SOLO", ["Declinable Backend Developer", "Protected Frontend Developer"])
        proposal = self.proposal(worker, pid, roles[0], 50000)
        cid = self.accept(client, pid, proposal)
        protected_proposal = self.proposal(outsider, pid, roles[1], 30000)
        protected_cid = self.accept(client, pid, protected_proposal)
        self.confirm(client, protected_cid)
        self.confirm(outsider, protected_cid)
        protected_version = self.version(protected_cid)["Id"]

        def protected_evidence():
            snapshot = self.db.snapshot()
            identifiers = {"Contracts": ("Id", protected_cid),
                           "ContractVersions": ("ContractId", protected_cid),
                           "ContractMembers": ("ContractId", protected_cid),
                           "ContractAcceptances": ("ContractVersionId", protected_version),
                           "Workspaces": ("ContractId", protected_cid),
                           "EscrowMilestones": ("ContractId", protected_cid),
                           "EscrowShares": ("ContractId", protected_cid),
                           "PaymentIntents": ("ContractId", protected_cid)}
            return {table: [row for row in snapshot[table] if row[field] == value]
                    for table, (field, value) in identifiers.items()}

        self.post(client, f"/Projects/LockTeam/{pid}", {}, f"/Projects/Manage/{pid}")
        require(self.db.scalar(f"SELECT Status FROM dbo.Projects WHERE Id={pid}") == "AwaitingEscrow"
                and self.db.scalar(f"SELECT CONVERT(int,Locked) FROM dbo.ProjectTeams WHERE ProjectId={pid}") == "1",
                "Decline reproduction must begin with every position accepted and team explicitly locked")
        protected_before = protected_evidence()
        self.deny(outsider, f"/Contracts/Decline/{cid}", {"reason": "Outsider must never cancel a private contract"},
                  "outsider cannot decline unsigned contract", private=True)
        self.post(worker, f"/Contracts/Decline/{cid}", {"reason": "Disposable participant declines before funding"},
                  f"/Contracts/Details/{cid}")
        require(self.db.scalar(f"SELECT Status FROM dbo.Contracts WHERE Id={cid}") == "Declined",
                "Counterparty decline must persist the terminal contract state")
        require(self.db.scalar(f"SELECT Status FROM dbo.Projects WHERE Id={pid}") == "Published"
                and self.db.scalar(f"SELECT CONVERT(int,Locked) FROM dbo.ProjectTeams WHERE ProjectId={pid}") == "0",
                "Pre-funding decline after LockTeam must reopen recruitment and unlock the shared team")
        require(self.db.scalar(f"SELECT COUNT(*) FROM dbo.RoleAssignments WHERE ProposalId={proposal}") == "0"
                and self.db.scalar(f"SELECT COUNT(*) FROM dbo.RoleAssignments WHERE ProposalId={protected_proposal}") == "1",
                "Decline must free only the declined position and preserve the other contract's assignment")
        require(protected_evidence() == protected_before,
                "Declining one locked-team contract must preserve the other agreement, signatures and escrow")
        version = self.version(cid)
        self.deny(client, f"/Contracts/Confirm/{cid}", {"versionId": version["Id"], "hash": version["Hash"]},
                  "declined contract cannot collect fresh signatures")
        self.unsigned_denials(client, cid, "declined contract cannot fund or create checkout intent")
        replacement = self.register("replacement", "Freelancer")
        replacement_proposal = self.proposal(replacement, pid, roles[0], 50000)
        replacement_cid = self.accept(client, pid, replacement_proposal)
        require(replacement_cid not in (cid, protected_cid)
                and self.db.scalar(f"SELECT Status FROM dbo.Proposals WHERE Id={replacement_proposal}") == "Accepted"
                and self.db.scalar(f"SELECT COUNT(*) FROM dbo.RoleAssignments WHERE ProposalId={replacement_proposal}") == "1",
                "Reopened position must accept a replacement proposal into a new independent contract")
        require(protected_evidence() == protected_before,
                "Hiring a replacement must leave the other original contract unaffected")
        self.pass_check("participant decline is terminal and cannot authorize escrow")
        self.pass_check("locked-team pre-funding decline reopens recruitment and permits an isolated replacement hire")

    def exercise_group_dashboard(self, client, leader_a, member_a, leader_b):
        """Two disjoint group bids in one project must never share dashboard counts."""
        member_b = self.register("dashboard-member", "Freelancer")
        pid, roles = self.project(client, "ENTERPRISE", ["Group A Backend", "Group A Frontend",
                                                       "Group B Backend", "Group B Frontend"])

        def accepted_group(leader, member, selected_roles, label):
            created = self.post(leader, "/Groups/Create", {
                "Name": "Dashboard " + label + " " + self.run_id,
                "Description": "Separate contract channel in a shared Enterprise project",
            }, "/Groups/Index")
            match = re.search(r"/Groups/Details/(\d+)", created.headers.get("Location", ""))
            require(match is not None, "Dashboard fixture must create its own group")
            gid = int(match.group(1))
            self.post(leader, "/Groups/Invite", {"GroupId": gid, "Email": member.address},
                      f"/Groups/Details/{gid}")
            invitations = [form for form in self.forms(self.get(member, "/Groups/Index").text)
                           if "/Groups/Respond/" in form["action"]
                           and "accept=true" in html.unescape(form["action"]).lower()]
            require(len(invitations) == 1, "Dashboard member must have one isolated group invitation")
            self.post(member, html.unescape(invitations[0]["action"]), {}, "/Groups/Index")
            page = f"/GroupBids/Create?groupId={gid}&projectId={pid}"
            bid_forms = [form for form in self.forms(self.get(leader, page).text)
                         if "/GroupBids/Create" in form["action"]]
            require(len(bid_forms) == 1, "Dashboard leader must receive a group bid form")
            payload = dict(bid_forms[0]["fields"])
            payload.pop("__RequestVerificationToken", None)
            candidates = sorted(int(found.group(1)) for key in payload
                                if (found := re.fullmatch(r"Candidates\[(\d+)\]\.UserAccountId", key)))
            require(len(candidates) == 2 and
                    {int(payload[f"Candidates[{index}].UserAccountId"]) for index in candidates} ==
                    {leader.uid, member.uid}, "Dashboard group roster must be disjoint and complete")
            for index, role_id in zip(candidates, selected_roles):
                payload.update({f"Candidates[{index}].Selected": "true",
                                f"Candidates[{index}].RoleRequirementId": role_id,
                                f"Candidates[{index}].QuotedPrice": 20000})
            payload.update({"ProjectId": pid, "FreelancerGroupId": gid,
                            "Pitch": "Independent " + label + " delivery team", "DeliveryDays": 14,
                            "MilestonePlan": "Complete dashboard contract | 40000"})
            self.post(leader, "/GroupBids/Create", payload, page)
            bid = int(self.db.scalar(f"SELECT Id FROM dbo.GroupBids WHERE ProjectId={pid} AND FreelancerGroupId={gid}"))
            self.post(client, f"/GroupBids/Decide/{bid}?accept=true", {}, f"/Projects/Manage/{pid}")
            cid = self.db.one("SELECT DISTINCT c.Id FROM dbo.Contracts c JOIN dbo.ContractMembers m ON m.ContractId=c.Id "
                              f"JOIN dbo.Proposals p ON p.Id=m.ProposalId WHERE c.ProjectId={pid} AND p.GroupBidId={bid}",
                              "Accepted group bid has exactly its own contract")["Id"]
            self.roster(cid, [leader, member])
            for party in (client, leader, member):
                self.confirm(party, cid)
            self.signatures(cid, [client, leader, member])
            self.fund_wallet(client, cid)
            return gid, cid, self.workspace(cid)

        group_a, cid_a, wid_a = accepted_group(leader_a, member_a, roles[:2], "A")
        group_b, cid_b, wid_b = accepted_group(leader_b, member_b, roles[2:], "B")
        require(cid_a != cid_b and wid_a != wid_b
                and self.db.scalar(f"SELECT COUNT(*) FROM dbo.Contracts WHERE ProjectId={pid}") == "2",
                "ENTERPRISE fixture must have two independently funded group contracts and channels")
        self.post(client, "/Workspace/CreateTask", {
            "WorkspaceId": wid_a, "Title": "Dashboard A complete " + self.run_id,
            "AssigneeId": leader_a.uid, "Priority": "Normal",
        }, f"/Workspace/Index/{wid_a}")
        task_a = int(self.db.scalar(f"SELECT Id FROM dbo.Tasks WHERE WorkspaceId={wid_a}"))
        for stage in ("Doing", "Review", "Done"):
            self.post(client, f"/Workspace/UpdateTask/{task_a}", {"status": stage}, f"/Workspace/Index/{wid_a}")
        for index in range(2):
            self.post(client, "/Workspace/CreateTask", {
                "WorkspaceId": wid_b, "Title": f"Dashboard B private {index} {self.run_id}",
                "AssigneeId": leader_b.uid, "Priority": "Normal",
            }, f"/Workspace/Index/{wid_b}")

        def dashboard(actor, gid, own_wid, foreign_wid, done, total):
            markup = self.get(actor, f"/Groups/Details/{gid}").text
            heading = re.search(r'id=["\']group-progress-title["\']', markup, re.I)
            require(heading is not None, "Group dashboard must render its assigned-project progress section")
            end = markup.find("</section>", heading.end())
            require(end != -1, "Group dashboard progress section must have a closing boundary")
            progress = html.unescape(markup[heading.start():end])
            links = {int(value) for value in re.findall(r"/Workspace/Index/(\d+)(?!\d)", progress, re.I)}
            require(links == {own_wid} and foreign_wid not in links,
                    "Group dashboard must link only its own contract channel, even under a shared project/team")
            counts = [(int(a), int(b)) for a, b in re.findall(r"(\d+)\s*/\s*(\d+)\s+Task", progress)]
            require(counts == [(done, total)],
                    "Group dashboard task totals must exclude every foreign contract task")
            require(self.db.scalar(f"SELECT COUNT(*) FROM dbo.Tasks WHERE WorkspaceId={own_wid}") == str(total)
                    and self.db.scalar(f"SELECT COUNT(*) FROM dbo.Tasks WHERE WorkspaceId={own_wid} AND Status='Done'") == str(done),
                    "Expected dashboard progress must agree with the independent contract workspace SQL oracle")

        for party in (leader_a, member_a):
            dashboard(party, group_a, wid_a, wid_b, 1, 1)
        for party in (leader_b, member_b):
            dashboard(party, group_b, wid_b, wid_a, 0, 2)
        task_b = int(self.db.scalar(f"SELECT MIN(Id) FROM dbo.Tasks WHERE WorkspaceId={wid_b}"))
        for stage in ("Doing", "Review", "Done"):
            self.post(client, f"/Workspace/UpdateTask/{task_b}", {"status": stage}, f"/Workspace/Index/{wid_b}")
        dashboard(leader_b, group_b, wid_b, wid_a, 1, 2)
        for party in (leader_a, member_a):
            dashboard(party, group_a, wid_a, wid_b, 1, 1)
        self.pass_check("two ENTERPRISE group dashboards isolate workspace links and task progress, including foreign updates")

    def run(self):
        client = self.register("client", "Client")
        worker_a = self.register("worker-a", "Freelancer")
        worker_b = self.register("worker-b", "Freelancer")
        outsider = self.register("outsider", "Freelancer")
        leader = self.register("leader", "Freelancer")
        member = self.register("member", "Freelancer")
        admin = self.register("deposit-reviewer", "Client")
        self.deposit(client, admin, 1000000)
        pid, roles = self.project(client, "SOLO", ["Backend Developer", "Frontend Developer"])
        pa = self.proposal(worker_a, pid, roles[0], 100000)
        pb = self.proposal(worker_b, pid, roles[1], 150000)
        cid_a = self.accept(client, pid, pa)
        require(self.db.scalar(f"SELECT COUNT(*) FROM dbo.Contracts WHERE ProjectId={pid}") == "1",
                "First accepted individual proposal must immediately create one contract")
        require(self.db.scalar(f"SELECT Status FROM dbo.Proposals WHERE Id={pb}") == "Pending",
                "First hire must not reject an independent unfilled position's proposal")
        cid_b = self.accept(client, pid, pb)
        require(cid_a != cid_b and self.db.scalar(f"SELECT COUNT(*) FROM dbo.Contracts WHERE ProjectId={pid}") == "2",
                "SOLO must permit two individually hired contracts in the same project")
        contracts = self.db.rows(f"SELECT Id,CounterpartyId,SourceKey FROM dbo.Contracts WHERE ProjectId={pid}")
        require({row["CounterpartyId"] for row in contracts} == {worker_a.uid, worker_b.uid}
                and len({row["SourceKey"] for row in contracts}) == 2 and all(row["SourceKey"] for row in contracts),
                "Individually accepted proposals must retain distinct source identities/counterparties")
        self.roster(cid_a, [worker_a])
        self.roster(cid_b, [worker_b])
        require(self.workspace(cid_a) != self.workspace(cid_b), "Each contract must have its own workspace/channel")
        self.get(client, f"/Contracts?projectId={pid}")
        self.get(client, f"/Contracts/Details/{cid_a}")
        self.get(worker_a, f"/Contracts/Details/{cid_a}")
        self.unsigned_denials(client, cid_a, "unsigned contract despite sufficient client balance")
        self.unsigned_denials(client, cid_b, "second unsigned contract despite sufficient client balance")
        self.deny(worker_a, "/Workspace/Submit", {
            "WorkspaceId": self.workspace(cid_a), "Title": "Premature work", "Url": "https://example.test/early",
            "EscrowMilestoneId": self.milestone(cid_a)["Id"],
        }, "work cannot start before signed and funded contract")
        self.exercise_revision(client, worker_a, worker_b, cid_a, cid_b)
        self.confirm(client, cid_b)
        self.unsigned_denials(client, cid_b, "B owner signature alone cannot fund B")
        self.confirm(worker_b, cid_b)
        self.signatures(cid_b, [client, worker_b])
        for actor in (worker_a, outsider):
            self.deny(actor, f"/Wallet/Fund/{self.milestone(cid_b)['Id']}", {},
                      "nonowner cannot wallet-fund another fully signed, still planned contract")
            self.deny(actor, f"/Payments/Checkout/{self.milestone(cid_b)['Id']}", {},
                      "nonowner cannot create checkout for another fully signed, still planned contract", private=True)
        b_before = self.milestone(cid_b)
        self.fund_wallet(client, cid_a)
        require(self.milestone(cid_b) == b_before, "Funding A cannot move B escrow")
        self.pass_check("two same-project contracts require independent signatures and funding")
        valid, late_failure = self.exercise_psp(client, cid_b)
        require(self.milestone(cid_a)["Status"] == "Funded", "PSP funding B cannot alter A's wallet-funded escrow")
        # Both milestone IDs are valid AND funded here. A rejection therefore
        # cannot pass merely because the foreign milestone was already submitted.
        self.deny(worker_a, "/Workspace/Submit", {
            "WorkspaceId": self.workspace(cid_a), "Title": "Cross-contract milestone",
            "Url": "https://example.test/blocked", "EscrowMilestoneId": self.milestone(cid_b)["Id"],
        }, "own workspace cannot consume another contract's genuinely funded milestone",
                  f"/Workspace/Index/{self.workspace(cid_a)}")
        a, b = self.exercise_isolation(client, worker_a, worker_b, outsider, cid_a, cid_b)
        self.exercise_settlement(client, worker_a, worker_b, a, b, valid, late_failure)
        self.group_contract(client, leader, member)
        self.exercise_decline(client, outsider, worker_b)
        self.exercise_group_dashboard(client, worker_a, worker_b, outsider)
        self.db.reconcile()
        print(f"CONTRACTS_V2_E2E_PASSED checks={self.passed} run={self.run_id}", flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--base", default="https://localhost:44391")
    parser.add_argument("--gate-only", action="store_true", help="Only anonymous Contracts route/auth gate")
    parser.add_argument("--run-writes", action="store_true")
    parser.add_argument("--mailbox", type=Path)
    args = parser.parse_args()
    base = local_base(args.base)
    urllib3.disable_warnings(urllib3.exceptions.InsecureRequestWarning)
    contracts_gate(base)
    if args.gate_only:
        return
    if not args.run_writes:
        parser.error("Full integration mode creates disposable fixtures; pass --run-writes")
    if args.mailbox is None or not args.mailbox.is_absolute() or not args.mailbox.is_dir():
        parser.error("Full integration mode requires --mailbox pointing to an existing absolute local pickup directory")
    secret = os.environ.get("NEXUS_VNPAY_HASH_SECRET")
    if not secret:
        parser.error("Full integration mode requires NEXUS_VNPAY_HASH_SECRET for the local sandbox")
    regression = Regression(base, args.mailbox, secret)
    regression.run()


if __name__ == "__main__":
    try:
        main()
    except AssertionError as error:
        print("FAIL AssertionError: " + str(error), file=sys.stderr, flush=True)
        sys.exit(1)
    except Exception as error:
        # Never let library exception text disclose credentials, OTPs or queries.
        print("FAIL " + type(error).__name__ + ": local regression aborted (details suppressed)",
              file=sys.stderr, flush=True)
        sys.exit(1)
