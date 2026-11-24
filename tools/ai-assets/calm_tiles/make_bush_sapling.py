# Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
# SPDX-License-Identifier: AGPL-3.0-or-later
# This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
"""#4003 (PAID, one call): the bush sapling's tile `flora_sapling_bush`, in the calm style of the other saplings.

The calm fruit-tree sapling is the image-edit reference (on black, like every sapling tile), the model turns it into a young
leafy bush; the picture is reduced to 64 px and written OPAQUE to client/Assets/Resources/textures. The cut-out comes after:

    uv run --project tools/ai-assets --with numpy python tools/ai-assets/calm_tiles/make_bush_sapling.py
    cd tools/ai-assets && uv run python bake_leaf_alpha.py --key flora_sapling_bush --force

(bake_leaf_alpha cuts the black ground and roots the plant on the bottom edge, like the other saplings.) A picture that
exists in out/calm_tiles/redraws is not generated again.
"""
from __future__ import annotations

import base64
import io
import os
import sys

import numpy as np
from PIL import Image
from tilelib import REDRAWS, REPO, TEXTURES, TILE

KEY = "flora_sapling_bush"
REFERENCE = "flora_sapling_fruit_round"
PROMPT = ("Redraw this game sprite of a young fruit tree sapling as a young leafy BUSH sapling instead: no tall stem and no "
          "fruit, a very short brown stem that splits right at the ground into four or five short twigs, densely covered "
          "with small round fresh green leaves, a low rounded little shrub that is wider than tall, standing on the bottom "
          "edge of the image. Same soft stylised hand-painted look like smooth matte modelling clay, gentle even shading, "
          "very little detail, no pixel grid, no outlines, no text. Keep the background pure black.")


def reference_on_black() -> Image.Image:
    raw = (TEXTURES / f"{REFERENCE}.bytes").read_bytes()
    rgba = np.asarray(Image.frombytes("RGBA", (TILE, TILE), raw).transpose(Image.FLIP_TOP_BOTTOM), dtype=np.float64)
    rgb = rgba[..., :3] * (rgba[..., 3:4] / 255.0)
    return Image.fromarray(rgb.astype(np.uint8), "RGB")


def main() -> None:
    REDRAWS.mkdir(parents=True, exist_ok=True)
    out = REDRAWS / f"{KEY}.png"
    if not out.exists():
        from dotenv import load_dotenv
        load_dotenv(REPO / "tools" / "ai-assets" / ".env")
        key = os.environ.get("OPENAI_API_KEY")
        if not key:
            sys.exit("OPENAI_API_KEY is not set.")
        from openai import OpenAI
        client = OpenAI(api_key=key)
        buf = io.BytesIO()
        reference_on_black().resize((1024, 1024), Image.NEAREST).save(buf, format="PNG")
        buf.seek(0)
        buf.name = f"{REFERENCE}.png"
        resp = client.images.edit(model="gpt-image-2.6-sunburst", image=buf, prompt=PROMPT, size="1024x1024",
                                  quality="low", n=1)
        out.write_bytes(base64.b64decode(resp.data[0].b64_json))
        print(f"{KEY}: generated")
    a = np.asarray(Image.open(out).convert("RGB"), dtype=np.float64)
    n = a.shape[0] // TILE
    small = a.reshape(TILE, n, TILE, n, 3).mean(axis=(1, 3))
    tile = Image.fromarray(np.clip(small + 0.5, 0, 255).astype(np.uint8), "RGB").convert("RGBA")
    (TEXTURES / f"{KEY}.bytes").write_bytes(tile.transpose(Image.FLIP_TOP_BOTTOM).tobytes())
    print(f"{KEY}: written (opaque; bake its alpha next)")


if __name__ == "__main__":
    main()
