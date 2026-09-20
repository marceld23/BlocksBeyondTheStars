# Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
# SPDX-License-Identifier: AGPL-3.0-or-later
# This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
"""Where each bundled texture came from — and which ones a generator must never overwrite (#1953).

The committed client/Assets/Resources/textures/*.bytes are the only source of truth: image generation is not
reproducible, and the PNGs under tools/ai-assets/out/ are git-ignored, so they exist on one machine only. A tile
somebody painted by hand in the game's texture editor lives ONLY as its .bytes. Any script that re-bundles from
out/ would silently put an old AI image back over it — on the one machine that has out/, long after the painter's
pull request was merged.

texture_provenance.json records the hand-made ones: key -> "hand:<author>". Everything not listed is "ai". The
bundlers ask `guard()` before they write a tile and skip a hand-made one unless --force is given.

Plain stdlib; imported by bundle_textures.py, bake_leaf_alpha.py, bundle_fire.py and tools/merge_texture.py.
"""
from __future__ import annotations

import json
from pathlib import Path

MANIFEST = Path(__file__).resolve().parent / "texture_provenance.json"
HAND = "hand:"


def load() -> dict:
    """The manifest as a dict with a `textures` map (empty when the file is missing)."""
    if not MANIFEST.exists():
        return {"version": 1, "default": "ai", "textures": {}}
    data = json.loads(MANIFEST.read_text(encoding="utf-8"))
    data.setdefault("textures", {})
    return data


def save(data: dict) -> None:
    data["textures"] = dict(sorted(data.get("textures", {}).items()))
    MANIFEST.write_text(json.dumps(data, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")


def origin(key: str, data: dict | None = None) -> str:
    """ "ai" or "hand:<author>" for a texture key (the resource name without .bytes)."""
    data = data if data is not None else load()
    return data.get("textures", {}).get(key, data.get("default", "ai"))


def is_hand(key: str, data: dict | None = None) -> bool:
    return origin(key, data).startswith(HAND)


def mark_hand(key: str, author: str) -> None:
    """Records that `key` was painted by hand. `author` is a display name or "anonymous"."""
    data = load()
    data["textures"][key] = HAND + (author.strip() or "anonymous")
    save(data)


def guard(key: str, force: bool, data: dict | None = None) -> bool:
    """True when a generator may write the tile `key`. Prints why when it may not."""
    who = origin(key, data)
    if who.startswith(HAND) and not force:
        print(f"skip {key}: hand-painted ({who[len(HAND):]}) — it exists only as its .bytes; "
              f"pass --force to overwrite it")
        return False
    return True
