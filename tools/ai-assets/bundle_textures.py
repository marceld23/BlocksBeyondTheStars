"""Bundle the block textures into the client as raw RGBA bytes (not PNG).

The client decodes these with Texture2D.LoadRawTextureData (UnityEngine.CoreModule), avoiding
Texture2D.LoadImage which lives in the non-auto-referenced ImageConversionModule and so does not
compile from the client asmdef. Each tile is written as 64x64 RGBA32 = 16384 raw bytes, flipped
vertically so it matches Unity's bottom-up texture layout (same orientation LoadImage produced).

Usage (from tools/ai-assets):
    uv run python bundle_textures.py          # convert the bundled Resources/.bytes in place
    uv run python bundle_textures.py --from-out  # (re)bundle from out/textures/*.png
"""
from __future__ import annotations

import argparse
from io import BytesIO
from pathlib import Path

from PIL import Image

TILE = 64
REPO = Path(__file__).resolve().parents[2]
RES = REPO / "client" / "Assets" / "Resources" / "textures"
OUT = Path("out/textures")


def to_raw(img: Image.Image) -> bytes:
    img = img.convert("RGBA").resize((TILE, TILE), Image.NEAREST).transpose(Image.FLIP_TOP_BOTTOM)
    return img.tobytes()


def main() -> None:
    ap = argparse.ArgumentParser(description="Bundle block textures as raw RGBA .bytes.")
    ap.add_argument("--from-out", action="store_true", help="source out/textures/*.png instead of Resources/*.bytes")
    ap.add_argument("--avatar", action="store_true", help="bundle out/avatar/*.png as Resources/textures/avatar_<key>.bytes")
    ap.add_argument("--creatures", action="store_true", help="bundle out/creatures/*.png as Resources/textures/creature_<key>.bytes")
    args = ap.parse_args()

    RES.mkdir(parents=True, exist_ok=True)
    count = 0

    if args.avatar or args.creatures:
        sub, prefix = ("out/avatar", "avatar_") if args.avatar else ("out/creatures", "creature_")
        for png in sorted(Path(sub).glob("*.png")):
            (RES / f"{prefix}{png.stem}.bytes").write_bytes(to_raw(Image.open(png)))
            count += 1
            print(f"{prefix}{png.stem}: out/png -> raw {TILE*TILE*4} bytes")
    elif args.from_out:
        for png in sorted(OUT.glob("*.png")):
            (RES / f"{png.stem}.bytes").write_bytes(to_raw(Image.open(png)))
            count += 1
            print(f"{png.stem}: out/png -> raw {TILE*TILE*4} bytes")
    else:
        for f in sorted(RES.glob("*.bytes")):
            data = f.read_bytes()
            if len(data) == TILE * TILE * 4:
                print(f"{f.name}: already raw, skip")
                continue
            f.write_bytes(to_raw(Image.open(BytesIO(data))))
            count += 1
            print(f"{f.name}: PNG -> raw {TILE*TILE*4} bytes")

    print(f"[bundle] done. converted={count}")


if __name__ == "__main__":
    main()
