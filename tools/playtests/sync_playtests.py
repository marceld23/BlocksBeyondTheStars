# Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
# SPDX-License-Identifier: AGPL-3.0-or-later
# This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
"""Keep the playtest tasks in the repository and the report inbox in step.

The inbox's database is the truth (it is what the game clients fetch and what the admin page edits); the files
under data-online/playtests/ are its mirror with a history. One file per task: <id>.json.

    pull    inbox -> repository: writes every stored task (switched-off ones too) as <id>.json
    push    repository -> inbox: sends every file; the inbox validates each task and names what is wrong
    check   local only: the files are JSON objects, named after their id, with German and English texts

The inbox refuses a push whose revision is lower than the stored one (someone changed the task on the admin
page since the last pull) — pull first, or pass --force to overwrite.

Credentials come from the environment, never from the command line:
    BBS_REPORTS_URL             e.g. https://reports.example.com
    BBS_REPORTS_ADMIN_USER
    BBS_REPORTS_ADMIN_PASSWORD

Usage:
    uv run --no-project python tools/playtests/sync_playtests.py check
    uv run --no-project python tools/playtests/sync_playtests.py pull
    uv run --no-project python tools/playtests/sync_playtests.py push [--only <id>] [--force]
"""
from __future__ import annotations

import argparse
import base64
import json
import os
import re
import sys
import urllib.error
import urllib.request
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
TASKS = REPO / "data-online" / "playtests"
ID_PATTERN = re.compile(r"^[a-z0-9](?:[a-z0-9-]{0,38}[a-z0-9])?$")
RESERVED = {"export", "mine", "enabled"}


def task_files(only: str | None) -> list[Path]:
    """The task files to work on, sorted; exits when --only names no file."""
    if only:
        path = TASKS / f"{only}.json"
        if not path.is_file():
            sys.exit(f"no such task file: {path}")
        return [path]
    return sorted(TASKS.glob("*.json")) if TASKS.is_dir() else []


def local_problems(path: Path) -> list[str]:
    """What is wrong with one file before it is even sent — the inbox does the real validation."""
    try:
        task = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as ex:
        return [f"not readable JSON: {ex}"]
    if not isinstance(task, dict):
        return ["the file must hold one JSON object"]

    problems = []
    task_id = task.get("id", "")
    if task_id != path.stem:
        problems.append(f"id '{task_id}' does not match the file name '{path.stem}'")
    if not isinstance(task_id, str) or not ID_PATTERN.match(task_id):
        problems.append("id needs 1 to 40 characters of a-z, 0-9 and '-'")
    if task_id in RESERVED:
        problems.append(f"id '{task_id}' is a route word of the inbox")
    for field in ("title", "intro"):
        text = task.get(field)
        if not isinstance(text, dict) or not str(text.get("de", "")).strip() or not str(text.get("en", "")).strip():
            problems.append(f"{field} needs a German and an English text")
    if not isinstance(task.get("steps"), list) or not task["steps"]:
        problems.append("a task needs at least one step")
    return problems


class Inbox:
    """The inbox's scriptable playtest routes."""

    def __init__(self) -> None:
        self.base = os.environ.get("BBS_REPORTS_URL", "").rstrip("/")
        user = os.environ.get("BBS_REPORTS_ADMIN_USER", "")
        password = os.environ.get("BBS_REPORTS_ADMIN_PASSWORD", "")
        if not self.base or not user or not password:
            sys.exit("set BBS_REPORTS_URL, BBS_REPORTS_ADMIN_USER and BBS_REPORTS_ADMIN_PASSWORD")
        if not self.base.startswith(("https://", "http://localhost", "http://127.0.0.1")):
            sys.exit("BBS_REPORTS_URL must be https (plain http only for a local inbox)")
        token = base64.b64encode(f"{user}:{password}".encode()).decode("ascii")
        self.auth = f"Basic {token}"

    def call(self, method: str, path: str, body: str | None = None) -> tuple[int, dict]:
        """One request; returns the status and the parsed JSON answer ({} when there is none)."""
        request = urllib.request.Request(self.base + path, method=method)
        request.add_header("Authorization", self.auth)
        data = None
        if body is not None:
            data = body.encode("utf-8")
            request.add_header("Content-Type", "application/json")
        try:
            # The scheme was checked in __init__: https, or plain http for a local inbox only.
            with urllib.request.urlopen(request, data=data, timeout=30) as response:
                return response.status, parse(response.read())
        except urllib.error.HTTPError as ex:
            return ex.code, parse(ex.read())
        except urllib.error.URLError as ex:
            sys.exit(f"cannot reach the inbox: {ex.reason}")


def parse(raw: bytes) -> dict:
    """A JSON object from an answer body, or {}."""
    try:
        value = json.loads(raw.decode("utf-8")) if raw else {}
    except (UnicodeDecodeError, json.JSONDecodeError):
        return {}
    return value if isinstance(value, dict) else {}


def cmd_check(args: argparse.Namespace) -> int:
    files = task_files(args.only)
    bad = 0
    for path in files:
        problems = local_problems(path)
        bad += bool(problems)
        print(("FAIL " if problems else "ok   ") + path.name)
        for problem in problems:
            print("       " + problem)
    print(f"{len(files)} file(s), {bad} with problems")
    return 1 if bad else 0


def cmd_pull(args: argparse.Namespace) -> int:
    status, answer = Inbox().call("GET", "/api/playtests/export")
    if status != 200:
        sys.exit(f"export failed: HTTP {status} {answer.get('error', '')}")
    tasks = [t for t in answer.get("tasks", []) if isinstance(t, dict) and ID_PATTERN.match(str(t.get("id", "")))]
    if args.only:
        tasks = [t for t in tasks if t["id"] == args.only]
    TASKS.mkdir(parents=True, exist_ok=True)
    for task in tasks:
        path = TASKS / f"{task['id']}.json"
        text = json.dumps(task, ensure_ascii=False, indent=2) + "\n"
        changed = not path.is_file() or path.read_text(encoding="utf-8") != text
        path.write_text(text, encoding="utf-8", newline="\n")
        print(("wrote  " if changed else "same   ") + path.name + f" (rev {task.get('rev', 1)})")
    stored = {t["id"] for t in tasks}
    if not args.only:
        for path in sorted(TASKS.glob("*.json")):
            if path.stem not in stored:
                print(f"only here, not in the inbox: {path.name}")
    print(f"catalogue is switched {'on' if answer.get('enabled', True) else 'OFF'}; {len(tasks)} task(s) pulled")
    return 0


def cmd_push(args: argparse.Namespace) -> int:
    files = task_files(args.only)
    for path in files:
        problems = local_problems(path)
        if problems:
            sys.exit(f"{path.name}: " + "; ".join(problems) + " — nothing was sent")

    inbox = Inbox()
    failed = 0
    for path in files:
        query = "?force=true" if args.force else ""
        status, answer = inbox.call("PUT", f"/api/playtests/{path.stem}{query}", path.read_text(encoding="utf-8"))
        if status == 200:
            print(f"sent   {path.name} (rev {answer.get('rev')})")
            for warning in answer.get("warnings", []):
                print(f"       warning {warning.get('where')}: {warning.get('message')}")
            continue

        failed += 1
        if status == 409 and answer.get("error") == "stale_rev":
            print(f"STALE  {path.name}: the inbox holds rev {answer.get('storedRev')} — pull first, or --force")
        else:
            print(f"FAIL   {path.name}: HTTP {status} {answer.get('error', '')}")
            for problem in answer.get("problems", []):
                print(f"       {problem.get('severity')} {problem.get('where')}: {problem.get('message')}")
    print(f"{len(files) - failed} of {len(files)} task(s) sent")
    return 1 if failed else 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    sub = parser.add_subparsers(dest="command", required=True)
    for name, handler in (("check", cmd_check), ("pull", cmd_pull), ("push", cmd_push)):
        p = sub.add_parser(name)
        p.add_argument("--only", metavar="ID", help="work on this one task")
        if name == "push":
            p.add_argument("--force", action="store_true", help="overwrite a newer revision in the inbox")
        p.set_defaults(handler=handler)
    args = parser.parse_args()
    return args.handler(args)


if __name__ == "__main__":
    sys.exit(main())
