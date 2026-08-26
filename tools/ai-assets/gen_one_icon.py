# Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
# SPDX-License-Identifier: AGPL-3.0-or-later
# This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
"""Generate ONE UI icon from the gen_icons.py manifest (same style/model) straight into the client's
Resources/icons — for topping up a single new category icon without re-rolling the whole approved set
(out/icons is git-ignored, so a fresh checkout would otherwise regenerate everything).

Usage:
    uv run gen_one_icon.py cat_machines
"""
from __future__ import annotations
import base64
import os
import sys
from io import BytesIO
from pathlib import Path
from dotenv import load_dotenv

import gen_icons

CLIENT_ICONS = Path(__file__).resolve().parents[2] / "client" / "Assets" / "Resources" / "icons"


def main() -> None:
    if len(sys.argv) != 2:
        sys.exit("usage: gen_one_icon.py <icon id from gen_icons.ICONS>")
    sid = sys.argv[1]
    desc = dict(gen_icons.ICONS).get(sid)
    if desc is None:
        sys.exit(f"{sid} is not in gen_icons.ICONS — add it to the manifest first")

    load_dotenv()
    key = os.environ.get("OPENAI_API_KEY")
    if not key:
        sys.exit("OPENAI_API_KEY is not set.")

    from openai import OpenAI
    from PIL import Image

    client = OpenAI(api_key=key)
    prompt = f"minimal flat UI line icon of {desc}, {gen_icons.STYLE}"
    resp = client.images.generate(model="gpt-image-1-mini", prompt=prompt, size="1024x1024",
                                  quality="low", n=1, background="transparent")
    raw = base64.b64decode(resp.data[0].b64_json)
    img = Image.open(BytesIO(raw)).convert("RGBA").resize((128, 128), Image.LANCZOS)
    out = CLIENT_ICONS / f"{sid}.png"
    img.save(out)
    print(f"[icon] {sid} -> {out}")


if __name__ == "__main__":
    main()
