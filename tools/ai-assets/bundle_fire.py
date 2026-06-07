"""Bundle the fire + ash block tiles (item 30). Fire gets a brightness-derived ALPHA so the flames show
on transparent gaps (the black background → fully transparent) — it renders in the see-through, emissive
submesh. Ash is a solid charred tile. Writes the raw 64x64 RGBA .bytes the client atlas loads."""
from pathlib import Path

from PIL import Image

TILE = 64
REPO = Path(__file__).resolve().parents[2]
RES = REPO / "client" / "Assets" / "Resources" / "textures"
OUT = Path("out/textures")


def lum(r: int, g: int, b: int) -> float:
    return (0.299 * r + 0.587 * g + 0.114 * b) / 255.0


def main() -> None:
    # Fire: alpha from luminance — dark background goes transparent, bright flames stay opaque (a soft ramp
    # in between for feathered flame edges). RGB kept (the flame colours), so the alpha-blended tile reads as
    # glowing tongues of flame over the world rather than a solid orange cube.
    fire = Image.open(OUT / "fire.png").convert("RGBA").resize((TILE, TILE), Image.NEAREST)
    px = fire.load()
    lo, hi = 0.16, 0.55
    trans = 0
    for y in range(TILE):
        for x in range(TILE):
            r, g, b, _ = px[x, y]
            l = lum(r, g, b)
            a = 0.0 if l <= lo else (1.0 if l >= hi else (l - lo) / (hi - lo))
            px[x, y] = (r, g, b, int(a * 255))
            if a < 0.5:
                trans += 1
    fire.save(OUT / "fire.png")  # keep the alpha'd png for the record
    (RES / "fire.bytes").write_bytes(fire.transpose(Image.FLIP_TOP_BOTTOM).tobytes())
    print(f"fire: {trans}/{TILE*TILE} px transparent")

    ash = Image.open(OUT / "ash.png").convert("RGBA").resize((TILE, TILE), Image.NEAREST)
    (RES / "ash.bytes").write_bytes(ash.transpose(Image.FLIP_TOP_BOTTOM).tobytes())
    print("ash: bundled (solid)")


if __name__ == "__main__":
    main()
