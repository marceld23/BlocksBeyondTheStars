# Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
# SPDX-License-Identifier: AGPL-3.0-or-later
# This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
"""Pull the textures players submitted from the game out of the report inbox (#1966).

A player paints a texture in the texture editor and presses "Submit to the developers". The inbox stores it as a
report of category "texture" with the picture (PNG) and the texture in the game's own format attached. This tool
lists those reports and unpacks each one into the SAME bundle "Export for the game" writes — so adopting a
submission is the one step every texture takes:

    python tools/pull_texture_submissions.py              # list what is waiting
    python tools/pull_texture_submissions.py --fetch      # unpack into analysis/texture-submissions/<id>_<key>/
    python tools/merge_texture.py analysis/texture-submissions/<id>_<key>

A submission is only unpacked when all three consents are recorded (painted it myself / the developers may use,
change and distribute it / 16 or older, or the parents agreed). One without them is listed as "NO CONSENT" and left
alone — delete it in the inbox.

Adopted one? Answer the report in the inbox with "fixed in version" set to the release that ships it: the player
hears it in the game, and the submission is exempt from the twelve-month deletion of submissions that were not
adopted. The author goes into NOTICES.md under the NICKNAME they gave — never a real name.

Configuration (environment, never the command line — keys do not belong in a shell history):
    BBS_REPORTS_URL        e.g. https://reports.example.com
    BBS_REPORTS_READ_KEY   the inbox's read key

Standard library only.
"""
import argparse
import json
import os
import re
import sys
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
DEFAULT_OUT = REPO / "analysis" / "texture-submissions"  # gitignored: submissions are personal data until adopted
FRAME = 64 * 64 * 4
BUNDLE_NAMES = {"image/png": "texture.png", "application/octet-stream": "texture.bytes"}
KEY_RE = re.compile(r"^[a-z0-9_]{1,64}$")


def api(base, key, path, raw=False):
    request = urllib.request.Request(base.rstrip("/") + path, headers={"x-report-read-key": key})
    with urllib.request.urlopen(request, timeout=30) as response:
        data = response.read()
    return data if raw else json.loads(data.decode("utf-8"))


def submissions(base, key, status):
    cursor = None
    while True:
        query = {"category": "texture", "limit": "100"}
        if status:
            query["status"] = status
        if cursor:
            query["cursor"] = cursor
        page = api(base, key, "/api/reports?" + urllib.parse.urlencode(query))
        yield from page.get("items", [])
        if not page.get("hasMore") or not page.get("nextCursor"):
            return
        cursor = page["nextCursor"]


def texture_of(item):
    """The texture block of a submission: reportJson.reportJson.texture (the client's payload inside the row)."""
    payload = item.get("reportJson") or {}
    if isinstance(payload, str):
        try:
            payload = json.loads(payload)
        except ValueError:
            payload = {}
    inner = payload.get("reportJson") if isinstance(payload.get("reportJson"), dict) else payload
    texture = inner.get("texture") if isinstance(inner, dict) else None
    return texture if isinstance(texture, dict) else {}


def consent_ok(texture):
    consent = texture.get("consent") or {}
    return all(consent.get(name) is True for name in ("self", "grant", "age"))


def fetch(base, key, item, texture, out_root):
    tex_key = texture.get("key", "")
    if not KEY_RE.match(tex_key):
        return "bad texture key"
    frames = int(texture.get("frames") or 1)
    target = out_root / f"{item['id']}_{tex_key}"
    target.mkdir(parents=True, exist_ok=True)
    raw = None
    for attachment in item.get("attachments", []):
        name = BUNDLE_NAMES.get(attachment.get("mimeType"))
        if not name:
            continue
        data = api(base, key, f"/api/reports/{item['id']}/attachment/{attachment['index']}", raw=True)
        (target / name).write_bytes(data)
        if name == "texture.bytes":
            raw = data
    kind = texture.get("kind") or "tile"
    if kind == "tile" and (raw is None or len(raw) != frames * FRAME):
        return f"texture.bytes is missing or not {frames} frame(s) of 64x64 RGBA"
    bundle = {
        "key": tex_key,
        "kind": kind,
        "frames": frames,
        "fps": int(texture.get("fps") or 0),
        "author": str(texture.get("nickname") or "").strip()[:40],
        "submission": item["id"],
        "consentTextVersion": (texture.get("consent") or {}).get("textVersion"),
    }
    if isinstance(texture.get("faces"), dict):
        bundle["faces"] = texture["faces"]
    (target / "texture.json").write_text(json.dumps(bundle, indent=2) + "\n", encoding="utf-8")
    return None


def main():
    parser = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    parser.add_argument("--fetch", action="store_true", help="unpack the submissions into bundles")
    parser.add_argument("--status", default="", help="only this inbox status (new, triaged, ...); default: all")
    parser.add_argument("--out", type=Path, default=DEFAULT_OUT, help=f"bundle folder (default {DEFAULT_OUT})")
    args = parser.parse_args()

    base = os.environ.get("BBS_REPORTS_URL", "")
    key = os.environ.get("BBS_REPORTS_READ_KEY", "")
    if not base or not key:
        sys.exit("Set BBS_REPORTS_URL and BBS_REPORTS_READ_KEY in the environment.")

    try:
        count = 0
        for item in submissions(base, key, args.status):
            count += 1
            texture = texture_of(item)
            label = f"{item['id']}  {item.get('createdAt', '')[:10]}  {texture.get('key', '?'):<28} by {texture.get('nickname', '?')}"
            if item.get("fixedInVersion"):
                label += f"  [adopted in {item['fixedInVersion']}]"
            if not consent_ok(texture):
                print("NO CONSENT  " + label)
                continue
            if not args.fetch:
                print("waiting     " + label)
                continue
            problem = fetch(base, key, item, texture, args.out)
            print(("SKIPPED     " + label + " — " + problem) if problem else ("unpacked    " + label))
        print(f"{count} submission(s)." + ("" if args.fetch or count == 0 else "  Re-run with --fetch to unpack them."))
    except urllib.error.HTTPError as error:
        sys.exit(f"The inbox answered {error.code} — check BBS_REPORTS_URL / BBS_REPORTS_READ_KEY.")
    except urllib.error.URLError as error:
        sys.exit(f"Could not reach the inbox: {error.reason}")


if __name__ == "__main__":
    main()
