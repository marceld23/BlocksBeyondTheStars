# Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
# SPDX-License-Identifier: AGPL-3.0-or-later
# This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
"""Pull what players submitted from the game's editors out of the report inbox (#1966, #3262).

A player presses "Submit" in the texture editor, the Ship Designer, the Station/Town editor or the Avatar Designer.
The inbox stores it as a report of category texture / ship / structure / avatar with the creation attached. This tool
lists those reports and unpacks each one into the SAME bundle the editor's own Save/Export writes — so adopting a
submission is the one step every export takes:

    python tools/pull_submissions.py --kind ship                  # list what is waiting
    python tools/pull_submissions.py --kind ship --fetch          # unpack into analysis/submissions/ship_exports/<key>/
    python tools/merge_ship.py analysis/submissions/ship_exports/<key>

    --kind texture    -> analysis/texture-submissions/<id>_<key>/       then tools/merge_texture.py <dir>
    --kind ship       -> <out>/ship_exports/<key>/{ship.json, layout.json}                 then tools/merge_ship.py <dir>
    --kind structure  -> <out>/station_exports/<key>/ or settlement_exports/<key>/
                         {structure.json, layout.json[, kit.json]}                         then tools/merge_structure.py <dir>
    --kind avatar     -> <out>/avatar_exports/<key>/skin.json
                         then tools/gen_pioneer_skins.py --from-skin <dir>/skin.json --key <key> --author <nickname>

A submission is only unpacked when all three consents are recorded (made it myself / the developers may use, change and
distribute it / 16 or older, or the parents agreed). One without them is listed as "NO CONSENT" and left alone —
delete it in the inbox. Every block key of a ship or structure is checked against data/blocks.json: unknown keys (a
block renamed or removed since the player's version) are listed per submission and in its submission.json — they
would load as air, i.e. as silent holes.

Names and texts are NEVER copied into the locale files by this tool: a player's ship name, description, sign or label
is free text from a child's keyboard. Read the inbox card's "free text" list first, then name the adopted design
yourself (the merge tools write placeholders for the locale keys). Ship stats are re-balanced by hand at merge.

Adopted one? Answer the report in the inbox with "fixed in version" set to the release that ships it: the player
hears it in the game, and the submission is exempt from the twelve-month deletion of submissions that were not
adopted. The author goes into NOTICES.md under the NICKNAME they gave — never a real name.

Configuration (environment, never the command line — keys do not belong in a shell history):
    BBS_REPORTS_URL        e.g. https://reports.example.com
    BBS_REPORTS_READ_KEY   the inbox's read key

Standard library only (plus tools/content_edit.py from this folder).
"""
import argparse
import gzip
import io
import json
import os
import re
import sys
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path

from content_edit import load_entries

REPO = Path(__file__).resolve().parents[1]
KINDS = ("texture", "ship", "structure", "avatar")
# gitignored: submissions are personal data until adopted
DEFAULT_OUT = {"texture": REPO / "analysis" / "texture-submissions"}
DEFAULT_CREATION_OUT = REPO / "analysis" / "submissions"
FRAME = 64 * 64 * 4
TEXTURE_BUNDLE_NAMES = {"image/png": "texture.png", "application/octet-stream": "texture.bytes"}
KEY_RE = re.compile(r"^[a-z0-9_]{1,64}$")
MAX_UNPACKED = 64 * 1024 * 1024
FILE_NAMES = {
    "ship": ("ship.json", "layout.json"),
    "structure": ("structure.json", "layout.json", "kit.json"),
    "avatar": ("skin.json",),
}


def api(base, key, path, raw=False):
    request = urllib.request.Request(base.rstrip("/") + path, headers={"x-report-read-key": key})
    with urllib.request.urlopen(request, timeout=30) as response:
        data = response.read()
    return data if raw else json.loads(data.decode("utf-8"))


def submissions(base, key, kind, status):
    cursor = None
    while True:
        query = {"category": kind, "limit": "100"}
        if status:
            query["status"] = status
        if cursor:
            query["cursor"] = cursor
        page = api(base, key, "/api/reports?" + urllib.parse.urlencode(query))
        yield from page.get("items", [])
        if not page.get("hasMore") or not page.get("nextCursor"):
            return
        cursor = page["nextCursor"]


def block_of(item, kind):
    """The kind's block of a submission: reportJson.reportJson.<kind> (the client's payload inside the row)."""
    payload = item.get("reportJson") or {}
    if isinstance(payload, str):
        try:
            payload = json.loads(payload)
        except ValueError:
            payload = {}
    inner = payload.get("reportJson") if isinstance(payload.get("reportJson"), dict) else payload
    block = inner.get(kind) if isinstance(inner, dict) else None
    return block if isinstance(block, dict) else {}


def consent_ok(block):
    consent = block.get("consent") or {}
    return all(consent.get(name) is True for name in ("self", "grant", "age"))


def author_of(block):
    return str(block.get("nickname") or "").strip()[:40]


# ---------------------------------------------------------------- textures (#1966)


def fetch_texture(base, key, item, texture, out_root):
    tex_key = texture.get("key", "")
    if not KEY_RE.match(tex_key):
        return "bad texture key"
    frames = int(texture.get("frames") or 1)
    target = out_root / f"{item['id']}_{tex_key}"
    target.mkdir(parents=True, exist_ok=True)
    raw = None
    for attachment in item.get("attachments", []):
        name = TEXTURE_BUNDLE_NAMES.get(attachment.get("mimeType"))
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
        "author": author_of(texture),
        "submission": item["id"],
        "consentTextVersion": (texture.get("consent") or {}).get("textVersion"),
    }
    if isinstance(texture.get("faces"), dict):
        bundle["faces"] = texture["faces"]
    (target / "texture.json").write_text(json.dumps(bundle, indent=2) + "\n", encoding="utf-8")
    return None


# ---------------------------------------------------------------- ships, structures, skins (#3262)


def unpack(data, mime):
    """The payload document: gzip is unpacked up to MAX_UNPACKED bytes."""
    if mime == "application/gzip":
        with gzip.GzipFile(fileobj=io.BytesIO(data)) as stream:
            data = stream.read(MAX_UNPACKED + 1)
        if len(data) > MAX_UNPACKED:
            raise ValueError(f"unpacks to more than {MAX_UNPACKED // (1024 * 1024)} MB")
    return json.loads(data.decode("utf-8"))


def payload_of(base, key, item):
    for attachment in item.get("attachments", []):
        mime = attachment.get("mimeType")
        if mime in ("application/json", "application/gzip"):
            data = api(base, key, f"/api/reports/{item['id']}/attachment/{attachment['index']}", raw=True)
            return unpack(data, mime)
    return None


def unknown_blocks(files, block_keys):
    """{block key: cells} of every block cell whose id is not in data/blocks.json (markers/elements are no blocks)."""
    unknown = {}
    layout = files.get("layout.json")
    for cell in (layout or {}).get("cells", []) if isinstance(layout, dict) else []:
        if not isinstance(cell, dict) or (cell.get("kind") or "block") != "block":
            continue
        block_id = str(cell.get("id") or "")
        if block_id not in block_keys:
            unknown[block_id] = unknown.get(block_id, 0) + 1
    return unknown


def target_dir(out_root, kind, files, block_key, item_id):
    """<out>/<kind>_exports/<key>/ — station/settlement for a structure; a second submission of the same key gets
    the report id appended instead of overwriting the first."""
    folder = kind
    if kind == "structure":
        meta = files.get("structure.json") or {}
        folder = "settlement" if meta.get("kind") == "settlement" else "station"
    target = out_root / f"{folder}_exports" / block_key
    marker = target / "submission.json"
    if marker.exists():
        try:
            if json.loads(marker.read_text(encoding="utf-8")).get("submission") != item_id:
                target = target.with_name(f"{block_key}_{item_id[:8]}")
        except ValueError:
            target = target.with_name(f"{block_key}_{item_id[:8]}")
    return target


def fetch_creation(base, key, item, block, kind, out_root, block_keys):
    """Unpacks one ship/structure/avatar. Returns (problem or None, notes)."""
    creation_key = block.get("key", "")
    if not KEY_RE.match(creation_key):
        return "bad key", []
    try:
        document = payload_of(base, key, item)
    except (ValueError, OSError) as error:
        return f"the file could not be read ({error})", []
    if not isinstance(document, dict) or not isinstance(document.get("files"), dict):
        return "no payload file", []
    files = {name: content for name, content in document["files"].items() if name in FILE_NAMES[kind]}
    if not files:
        return "the payload holds none of " + ", ".join(FILE_NAMES[kind]), []
    unknown = unknown_blocks(files, block_keys) if kind in ("ship", "structure") else {}
    target = target_dir(out_root, kind, files, creation_key, item["id"])
    target.mkdir(parents=True, exist_ok=True)
    for name, content in files.items():
        (target / name).write_text(json.dumps(content, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    info = {
        "submission": item["id"],
        "kind": kind,
        "key": creation_key,
        "author": author_of(block),
        "basedOn": block.get("basedOn") or "",
        "gameVersion": block.get("gameVersion") or "",
        "blockFingerprint": block.get("blockFingerprint") or "",
        "consentTextVersion": (block.get("consent") or {}).get("textVersion"),
        "unknownBlocks": unknown,
    }
    (target / "submission.json").write_text(json.dumps(info, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    notes = [f"-> {target}"]
    if unknown:
        listed = ", ".join(f"{k or '?'} ({n})" for k, n in sorted(unknown.items()))
        notes.append(f"UNKNOWN BLOCKS (would load as air): {listed}")
    if kind == "avatar":
        notes.append(
            f'adopt: python tools/gen_pioneer_skins.py --from-skin "{target / "skin.json"}" '
            f"--key {creation_key} --author \"{info['author'] or 'player'}\""
        )
    return None, notes


def load_block_keys():
    return {entry.get("key") for entry in load_entries(REPO / "data" / "blocks.json") if isinstance(entry, dict)}


def run(base, key, args):
    out_root = args.out or DEFAULT_OUT.get(args.kind, DEFAULT_CREATION_OUT)
    block_keys = load_block_keys() if args.kind in ("ship", "structure") else set()
    count = 0
    for item in submissions(base, key, args.kind, args.status):
        count += 1
        block = block_of(item, args.kind)
        label = f"{item['id']}  {item.get('createdAt', '')[:10]}  {block.get('key', '?'):<28} by {block.get('nickname', '?')}"
        if item.get("fixedInVersion"):
            label += f"  [adopted in {item['fixedInVersion']}]"
        if not consent_ok(block):
            print("NO CONSENT  " + label)
            continue
        if not args.fetch:
            print("waiting     " + label)
            continue
        if args.kind == "texture":
            problem, notes = fetch_texture(base, key, item, block, out_root), []
        else:
            problem, notes = fetch_creation(base, key, item, block, args.kind, out_root, block_keys)
        print(("SKIPPED     " + label + " — " + problem) if problem else ("unpacked    " + label))
        for note in notes:
            print("            " + note)
    print(f"{count} {args.kind} submission(s)." + ("" if args.fetch or count == 0 else "  Re-run with --fetch to unpack them."))


def main(argv=None, default_kind=None):
    parser = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    parser.add_argument("--kind", choices=KINDS, default=default_kind, required=default_kind is None,
                        help="what to pull: texture, ship, structure or avatar")
    parser.add_argument("--fetch", action="store_true", help="unpack the submissions into bundles")
    parser.add_argument("--status", default="", help="only this inbox status (new, triaged, ...); default: all")
    parser.add_argument("--out", type=Path, default=None,
                        help=f"bundle root (default {DEFAULT_OUT['texture']} for textures, {DEFAULT_CREATION_OUT} otherwise)")
    args = parser.parse_args(argv)

    base = os.environ.get("BBS_REPORTS_URL", "")
    key = os.environ.get("BBS_REPORTS_READ_KEY", "")
    if not base or not key:
        sys.exit("Set BBS_REPORTS_URL and BBS_REPORTS_READ_KEY in the environment.")

    try:
        run(base, key, args)
    except urllib.error.HTTPError as error:
        sys.exit(f"The inbox answered {error.code} — check BBS_REPORTS_URL / BBS_REPORTS_READ_KEY.")
    except urllib.error.URLError as error:
        sys.exit(f"Could not reach the inbox: {error.reason}")


if __name__ == "__main__":
    main()
