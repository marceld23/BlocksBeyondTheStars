# Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
# SPDX-License-Identifier: AGPL-3.0-or-later
# This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
"""Step 2: turns the generated forms into 64 px tiles and draws the contact sheets for the approval (also the
shared form and colour helpers of build_tiles.py).

Per candidate: greyscale -> seam healing (Moisan's periodic part, the method of tools/ai-assets/heal_seams.py; a
cross-fade with a shifted copy where a structure still breaks at the border) -> 64 px by area average -> form only
(mean 0, spread 1). Colour is added from a small ramp around the mean colour of the previous tile, so brightness and
colour of a world stay what they are; only the pattern changes. No model, no cost.

    uv run --project tools/ai-assets --with numpy python tools/ai-assets/calm_tiles/make_sheet.py
"""
from __future__ import annotations

import colorsys

import numpy as np
from PIL import Image

from tilelib import FORMS, SHEETS, TILE, luma, read_previous, repeat, scale, sheet

RAW = FORMS
OUT = SHEETS / "candidates"
# key -> (name on the sheet, the two motifs shown)
TILES = {
    "grass": ("Grass", "ac"), "dirt": ("Dirt", "ab"), "sand": ("Sand", "ab"), "stone": ("Stone", "ab"),
    "snow": ("Snow", "ab"), "wood_log": ("Bark", "ac"), "basalt": ("Basalt", "ac"), "mud": ("Mud", "ab"),
}
VERY_CALM, CALM, STRONG = 0.012, 0.022, 0.040   # spread of the brightness (0..1) of the three strengths
SHADER_CALM = 0.85                              # the texture calm on the previous tiles: share mixed to the mean colour
HEAL_MAX = 1.3                                  # above this seam ratio the cross-fade takes over
# Forms whose border is part of the motif (a plate's bevel, the gaps of planks): a cross-fade would lay the border
# across the middle of the tile, so these are only healed.
NO_CROSSFADE = {"metal_block_a", "metal_brushed_a", "metal_panels_a", "metal_studs_a", "wood_planks_a"}
# A plate's bevel is light on one side and dark on the other: healing would level exactly that. Kept as drawn.
NO_HEAL = {"metal_block_a", "metal_brushed_a", "metal_panels_a", "metal_studs_a"}


def periodic(u: np.ndarray) -> np.ndarray:
    """Moisan's periodic component: the picture minus the smooth ramp that carries the jumps across its borders."""
    h, w = u.shape
    v = np.zeros_like(u)
    v[0, :] += u[-1, :] - u[0, :]
    v[-1, :] += u[0, :] - u[-1, :]
    v[:, 0] += u[:, -1] - u[:, 0]
    v[:, -1] += u[:, 0] - u[:, -1]
    q = np.arange(h).reshape(-1, 1)
    r = np.arange(w).reshape(1, -1)
    denom = 2 * np.cos(2 * np.pi * q / h) + 2 * np.cos(2 * np.pi * r / w) - 4
    denom[0, 0] = 1
    s = np.fft.fft2(v) / denom
    s[0, 0] = 0
    return u - np.real(np.fft.ifft2(s))


def crossfade(u: np.ndarray, band: float = 0.3) -> np.ndarray:
    """Makes a picture wrap by fading each border zone into a copy shifted by half a tile (first across, then down).
    The mix keeps the contrast (divided by the length of the two weights), so the zone does not go flat."""
    out = u
    for axis in (1, 0):
        n = out.shape[axis]
        d = np.abs((np.arange(n) + 0.5) / n * 2.0 - 1.0)          # 0 in the middle, 1 at the border
        t = np.clip((1.0 - d) / band, 0.0, 1.0)
        wgt = t * t * (3.0 - 2.0 * t)                             # 1 inside, 0 at the border
        wgt = wgt.reshape(1, -1) if axis == 1 else wgt.reshape(-1, 1)
        mean = out.mean()
        other = np.roll(out, n // 2, axis=axis)
        out = mean + (wgt * (out - mean) + (1.0 - wgt) * (other - mean)) / np.sqrt(wgt ** 2 + (1.0 - wgt) ** 2)
    return out


def seam_ratio(g: np.ndarray) -> float:
    """heal_seams.py's measure: the wrap step against an ordinary neighbour step (about 1 = seamless)."""
    inner_v = np.abs(np.diff(g, axis=0)).mean()
    inner_h = np.abs(np.diff(g, axis=1)).mean()
    seam_v = np.abs(g[0, :] - g[-1, :]).mean()
    seam_h = np.abs(g[:, 0] - g[:, -1]).mean()
    return max(seam_v / max(inner_v, 1e-6), seam_h / max(inner_h, 1e-6))


def form_of(name: str) -> np.ndarray:
    """The 64 px form of a raw candidate (mean 0, spread 1); prints its seam ratio before and after the healing."""
    raw = np.asarray(Image.open(RAW / f"{name}.png").convert("L"), dtype=np.float64) / 255.0
    n = raw.shape[0] // TILE

    def small(a: np.ndarray) -> np.ndarray:
        return a.reshape(TILE, n, TILE, n).mean(axis=(1, 3))

    before = seam_ratio(small(raw))
    if name in NO_HEAL:
        z = small(raw)
        print(f"{name}: seam {before:.2f} (kept: its border is the motif)")
        return np.clip((z - z.mean()) / max(z.std(), 1e-6), -2.6, 2.6)

    g = small(periodic(raw))
    how = "healed"
    if seam_ratio(g) > HEAL_MAX and name not in NO_CROSSFADE:
        g = small(periodic(crossfade(raw)))
        how = "cross-faded"
    print(f"{name}: seam {before:.2f} -> {seam_ratio(g):.2f} ({how})")
    z = (g - g.mean()) / max(g.std(), 1e-6)
    return np.clip(z, -2.6, 2.6)


def toward(h: float, target: float, amount: float) -> float:
    """Moves a hue (0..1) by `amount` along the shorter way to `target`."""
    d = (target - h + 0.5) % 1.0 - 0.5
    return (h + np.sign(d) * min(abs(d), abs(amount))) % 1.0


def colourise(z: np.ndarray, mean_rgb: np.ndarray, spread: float) -> Image.Image:
    """Form x colour ramp: the mean colour in the middle, darker parts a touch richer and shifted to the shadow side,
    lighter parts a touch paler. The brightness follows the form exactly."""
    lm = float(luma(mean_rgb))
    spread = min(spread, (min(lm, 1.0 - lm) - 0.01) / 2.6)
    h0, s0, v0 = colorsys.rgb_to_hsv(*(mean_rgb / 255.0))
    out = np.zeros((TILE, TILE, 3))
    for y in range(TILE):
        for x in range(TILE):
            t = z[y, x] / 2.6
            want = lm + z[y, x] * spread
            h = toward(h0, 0.69, 0.012 * -t) if t < 0 else toward(h0, 0.14, 0.008 * t)
            s = float(np.clip(s0 * (1.0 - 0.10 * t), 0, 1))
            rgb = np.array(colorsys.hsv_to_rgb(h, s, v0))
            rgb = rgb * (want / max(float(luma(rgb * 255.0)), 1e-4))
            out[y, x] = np.clip(rgb, 0, 1)
    return Image.fromarray((out * 255.0 + 0.5).astype(np.uint8), "RGB")


def calmed(img: Image.Image, share: float) -> Image.Image:
    a = np.asarray(img, dtype=np.float64)
    mean = a.reshape(-1, 3).mean(axis=0)
    return Image.fromarray((a + (mean - a) * share + 0.5).astype(np.uint8), "RGB")


def wall(img: Image.Image) -> Image.Image:
    return scale(repeat(img, 3), 2)


def mean_of(key: str) -> np.ndarray:
    return np.asarray(read_previous(key), dtype=np.float64).reshape(-1, 3).mean(axis=0)


def toned(img: Image.Image, k: float) -> Image.Image:
    return Image.fromarray(np.clip(np.asarray(img, dtype=np.float64) * k + 0.5, 0, 255).astype(np.uint8), "RGB")


def old_variants(img: Image.Image, seed: int) -> list[Image.Image]:
    """The classic two variant tiles of a ground block, after BlockTextureAtlas.PaintVariant: a touch darker with three
    thin cracks, and a touch lighter with thirty small speckles. (Its random numbers are .NET's, so the cracks sit
    elsewhere than in the game; their number, length and strength are the game's.)"""
    rng = np.random.default_rng(seed)
    a = np.asarray(img, dtype=np.float64) * 0.96
    for _ in range(3):
        x, y = int(rng.integers(TILE)), int(rng.integers(TILE))
        for _ in range(TILE // 2 + int(rng.integers(TILE // 2))):
            a[min(max(y, 0), TILE - 1), min(max(x, 0), TILE - 1)] *= 0.70
            x += int(rng.integers(3)) - 1
            y += int(rng.integers(3)) - 1
    b = np.asarray(img, dtype=np.float64) * 1.06
    for _ in range(30):
        x, y = int(rng.integers(TILE - 1)), int(rng.integers(TILE - 1))
        k = 0.72 if rng.integers(2) == 0 else 1.18
        b[y, x] *= k
        b[y, x + 1] *= k
        b[y + 1, x] *= k
    return [Image.fromarray(np.clip(v + 0.5, 0, 255).astype(np.uint8), "RGB") for v in (a, b)]


def calm_variants(img: Image.Image) -> list[Image.Image]:
    """The proposed variants of a calm tile: the same form mirrored, a touch darker and a touch lighter (the game's
    0.96 and 1.06), without cracks and speckles."""
    return [toned(img.transpose(Image.FLIP_LEFT_RIGHT), 0.96), toned(img.transpose(Image.FLIP_TOP_BOTTOM), 1.06)]


def laid(base: Image.Image, variants: list[Image.Image] | None, n: int = 8) -> Image.Image:
    """Ground seen from above, laid the way ChunkMesher lays it: a hash of the block's place picks the base tile or
    one of its two variants and turns the top face in steps of 90 degrees. A block without variants just repeats."""
    out = Image.new("RGB", (TILE * n, TILE * n))
    for bz in range(n):
        for bx in range(n):
            wx, wy, wz = 100 + bx, 64, 200 + bz
            h = ((wx * 73856093) ^ (wy * 19349663) ^ (wz * 83492791)) & 0xFFFFFFFF
            tile = base
            if variants:
                pick = h % 3
                tile = (base if pick == 0 else variants[pick - 1]).rotate(90 * ((h >> 8) & 3))
            out.paste(tile, (bx * TILE, bz * TILE))
    return out


GROUND_VARIED = {"stone", "dirt", "sand", "snow", "basalt", "mud"}   # BlockTextureAtlas.VariantKeys of the probe set
GROUND_ROWS = [("grass", "c", "a"), ("dirt", "b", "a"), ("sand", "b", "a"), ("stone", "b", "a"),
               ("snow", "c", "b"), ("basalt", "a", "c"), ("mud", "a", "b")]


def ground_sheets(forms: dict[str, np.ndarray]) -> None:
    rows = []
    for i, (key, first, second) in enumerate(GROUND_ROWS):
        old = read_previous(key)
        varied = key in GROUND_VARIED
        cells = [laid(old, old_variants(old, i) if varied else None)]
        for variant in (first, second):
            name = f"{key}_{variant}"
            if name not in forms:
                forms[name] = form_of(name)
            tile = colourise(forms[name], mean_of(key), CALM)
            tile.save(OUT / f"{name}_calm.png")
            cells.append(laid(tile, calm_variants(tile) if varied else None))
        rows.append((TILES[key][0], cells))
    heads = ["previous (classic)", "NEW, proposed", "NEW, other motif"]
    title = "Ground from above, 8 x 8 blocks, laid as in the game (every block turned and toned a little)"
    sheet(rows[:4], heads, SHEETS / "contact-5a-ground.png", title, label_w=110)
    sheet(rows[4:], heads, SHEETS / "contact-5b-ground.png", title, label_w=110)


def main() -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    forms: dict[str, np.ndarray] = {}
    rows = []
    for key, (label, variants) in TILES.items():
        old = read_previous(key)
        mean = mean_of(key)
        cells = [wall(old), wall(calmed(old, SHADER_CALM))]
        for variant in variants:
            name = f"{key}_{variant}"
            z = forms[name] = form_of(name)
            grey = Image.fromarray(np.clip((0.5 + z * 0.09) * 255.0 + 0.5, 0, 255).astype(np.uint8), "L")
            grey.save(OUT / f"{name}_form.png")
            tile = colourise(z, mean, CALM)
            tile.save(OUT / f"{name}_calm.png")
            cells.append(wall(tile))
        rows.append((label, cells))

    heads = ["previous (classic)", "previous, calmed (pattern 85 % faded)", "NEW, motif 1", "NEW, motif 2"]
    title = "Calm nature tiles, trial round: 3 x 3 blocks each, without light, edges and fine grain"
    sheet(rows[:4], heads, SHEETS / "contact-1.png", title, label_w=110)
    sheet(rows[4:], heads, SHEETS / "contact-2.png", title, label_w=110)

    # How strong the pattern is: the same form at three spreads.
    rows = []
    for name in ("sand_b", "stone_b", "grass_a", "wood_log_a"):
        key = name.rsplit("_", 1)[0]
        rows.append((TILES[key][0], [wall(colourise(forms[name], mean_of(key), s))
                                     for s in (VERY_CALM, CALM, STRONG)]))
    sheet(rows, ["very calm", "calm (as on sheets 1 and 2)", "stronger"], SHEETS / "contact-3-strength.png",
          "How strong should the pattern be? The same tile at three strengths", label_w=110)

    # Form independent of colour: the same form in other colours, as another world could roll them.
    palettes = {
        "grass_a": [None, (40, 150, 140), (150, 80, 190), (190, 150, 50)],
        "sand_b": [None, (236, 170, 170), (232, 226, 214), (176, 92, 60)],
        "stone_b": [None, (110, 84, 150), (84, 104, 130), (150, 122, 92)],
        "wood_log_a": [None, (84, 60, 110), (170, 164, 158), (140, 62, 44)],
    }
    rows = []
    for name, colours in palettes.items():
        key = name.rsplit("_", 1)[0]
        rows.append((TILES[key][0], [
            wall(colourise(forms[name], mean_of(key) if c is None else np.array(c, dtype=np.float64), CALM))
            for c in colours]))
    sheet(rows, ["previous colour", "other world 1", "other world 2", "other world 3"],
          SHEETS / "contact-4-other-colours.png",
          "The same form in other colours (for viewing only, no colour changes in the game)", label_w=110)

    ground_sheets(forms)


if __name__ == "__main__":
    main()
