# Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
# SPDX-License-Identifier: AGPL-3.0-or-later
# This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
"""Merge a texture export bundle (from the in-game texture editor) into the game (#1953).

"Export for the game" in the texture editor writes a bundle to <game data folder>/texture_exports/<key>/:

  - texture.json   -> { "key", "kind": "tile" | "icon", "frames", "fps", "author", "faces"? }
  - texture.bytes  -> kind "tile": the frames back to back, raw 64x64 RGBA32, rows bottom-up — EXACTLY the layout
                      the game loads, so this tool only copies bytes. No image library, no generation output
                      folder, no API key: a fresh clone can paint, export, merge and open a pull request.
  - texture.png    -> the same picture for humans (kind "icon": the icon itself, copied as is)

What it writes:

  - client/Assets/Resources/textures/<key>.bytes          frame 0
  - client/Assets/Resources/textures/<key>__anim.bytes    frames 1..n of an animated texture (removed when still)
  - client/Assets/Resources/icons/<name>.png              kind "icon"
  - data/blocks.json                                      "anim": { "fps": n } and, if the bundle carries them,
                                                          the "faces" slots of a picture block
  - tools/ai-assets/texture_provenance.json               key -> "hand:<author>", so no generator script puts an
                                                          old image back over the painting

Afterwards: add the painter to NOTICES.md, review the diff, commit. New files get their Unity .meta on the next
editor import / build — commit those too.

Usage:
    python tools/merge_texture.py <path-to-export-bundle-dir>
"""
import json
import shutil
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent / "ai-assets"))

import texture_provenance  # noqa: E402  (needs the path above)
from content_edit import load_entries, upsert_entry  # noqa: E402

REPO = Path(__file__).resolve().parents[1]
DATA = REPO / "data"
RES_TEX = REPO / "client" / "Assets" / "Resources" / "textures"
RES_ICONS = REPO / "client" / "Assets" / "Resources" / "icons"
FRAME = 64 * 64 * 4
MAX_FRAMES = 8
SPEEDS = (2, 4, 8, 12)
ANIM_SUFFIX = "__anim"


def alpha_mode(key):
    """Mirror of TextureTiles.AlphaModeOf (src/BlocksBeyondTheStars.Shared/Textures/TextureTiles.cs) — keep in step."""
    if key.startswith(("creature_", "microfauna_", "avatar_")):
        return "free"
    if key.startswith("flora_") or key in (
            "tree_leaves", "pine_needles", "palm_frond", "giant_leaves", "fire", "torch", "lantern"):
        return "cutout"
    return "opaque"


def valid_key(key):
    return (isinstance(key, str) and 0 < len(key) <= 64
            and all(c in "abcdefghijklmnopqrstuvwxyz0123456789_" for c in key))


def force_opaque(frame):
    data = bytearray(frame)
    changed = 0
    for i in range(3, len(data), 4):
        if data[i] != 255:
            data[i] = 255
            changed += 1
    return bytes(data), changed


def remove_with_meta(path):
    for p in (path, Path(str(path) + ".meta")):
        if p.exists():
            p.unlink()


def merge_tile(bundle, meta):
    key = meta["key"]
    raw = (bundle / "texture.bytes").read_bytes()
    if len(raw) == 0 or len(raw) % FRAME != 0:
        sys.exit(f"texture.bytes is {len(raw)} bytes — expected a multiple of {FRAME} (64x64 RGBA32 frames)")

    frames = [raw[i:i + FRAME] for i in range(0, len(raw), FRAME)]
    if len(frames) > MAX_FRAMES:
        sys.exit(f"{len(frames)} frames — an animated texture has at most {MAX_FRAMES}")
    if meta.get("frames", len(frames)) != len(frames):
        sys.exit(f"texture.json says {meta.get('frames')} frames, texture.bytes holds {len(frames)}")

    fps = int(meta.get("fps", 0))
    if len(frames) > 1 and fps not in SPEEDS:
        sys.exit(f"fps {fps} — an animated texture runs at one of {SPEEDS}")

    # The alpha rule: a block tile must be opaque (the shaders read tile alpha as a meaning — a see-through tile
    # renders as water, and see-through terrain would be an x-ray). The editor enforces it too; never trust a file.
    if alpha_mode(key) == "opaque":
        fixed = []
        total = 0
        for f in frames:
            f, changed = force_opaque(f)
            fixed.append(f)
            total += changed
        frames = fixed
        if total:
            print(f"  alpha: forced {total} see-through pixel(s) opaque ('{key}' is a block tile)")

    RES_TEX.mkdir(parents=True, exist_ok=True)
    (RES_TEX / f"{key}.bytes").write_bytes(frames[0])
    print(f"  texture -> {(RES_TEX / (key + '.bytes')).relative_to(REPO)}")

    anim = RES_TEX / f"{key}{ANIM_SUFFIX}.bytes"
    if len(frames) > 1:
        anim.write_bytes(b"".join(frames[1:]))
        print(f"  {len(frames)} frames @ {fps} fps -> {anim.relative_to(REPO)}")
    elif anim.exists():
        remove_with_meta(anim)
        print(f"  still texture: removed {anim.relative_to(REPO)}")

    # Block data: the animation speed, and the face slots of a picture block when the bundle carries them.
    blocks = DATA / "blocks.json"
    entry = next((b for b in load_entries(blocks) if b.get("key") == key), None)
    if entry is not None:
        before = json.dumps(entry, sort_keys=True)
        if len(frames) > 1:
            entry["anim"] = {"fps": fps}
        else:
            entry.pop("anim", None)
        if isinstance(meta.get("faces"), list):
            entry["faces"] = meta["faces"]
        if json.dumps(entry, sort_keys=True) != before:
            upsert_entry(blocks, key, entry)
            print("  data/blocks.json: updated anim / faces")
    elif len(frames) > 1:
        print(f"  ! '{key}' is not a block: only block tiles animate in the world, the extra frames are unused")


def merge_icon(bundle, meta):
    name = meta["key"]
    src = bundle / "texture.png"
    if not src.exists():
        sys.exit(f"missing {src}")
    RES_ICONS.mkdir(parents=True, exist_ok=True)
    dst = RES_ICONS / f"{name}.png"
    shutil.copyfile(src, dst)
    print(f"  icon -> {dst.relative_to(REPO)}")


def main():
    if len(sys.argv) != 2:
        sys.exit("usage: python tools/merge_texture.py <export-bundle-dir>")

    bundle = Path(sys.argv[1])
    meta = json.loads((bundle / "texture.json").read_text(encoding="utf-8"))
    key = meta.get("key")
    if not valid_key(key):
        sys.exit(f"bad texture key {key!r}: lowercase letters, digits and underscores only")

    kind = meta.get("kind", "tile")
    if kind == "icon":
        merge_icon(bundle, meta)
        provenance_key = "icon:" + key
    elif kind == "tile":
        merge_tile(bundle, meta)
        provenance_key = key
    else:
        sys.exit(f"unknown kind {kind!r} (tile | icon)")

    author = (meta.get("author") or "anonymous").strip()
    texture_provenance.mark_hand(provenance_key, author)
    print(f"  provenance: {provenance_key} -> hand:{author}")
    print(f"merged '{key}'. Add the painter to NOTICES.md, review the diff, build once so Unity writes the .meta "
          f"of any new file, and commit.")


if __name__ == "__main__":
    main()
