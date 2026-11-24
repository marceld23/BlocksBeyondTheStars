# Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
# SPDX-License-Identifier: AGPL-3.0-or-later
# This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
"""Step 4: builds every calm tile from the generated forms and the redraws and writes the sheets; with --bundle also
the .bytes files into client/Assets/Resources/textures. No model, no cost.

A tile is FORM x COLOUR SLOTS, never a coloured picture:
  SINGLE  one material: a greyscale form, coloured by a small ramp around the mean colour of the previous tile of
          the key. One form can serve many keys (the metal blocks, the planks, the sandstones).
  DOUBLE  two materials: a base form, a MASK that says where the second material lies (ore in rock, moss on stone,
          the marks of a birch) and optionally a form of its own for it. The two colours are read from the previous
          tile (its pixels split into two groups).
  REDRAWN every redraw of restyle_tiles.py, reduced to 64 px (cut-outs keep the previous alpha, surfaces wrap).
The strength is the one chosen on 2026-11-22: half of the first round's.

    uv run --project tools/ai-assets --with numpy python tools/ai-assets/calm_tiles/build_tiles.py [--bundle]
"""
from __future__ import annotations

import argparse
import json

import numpy as np
from PIL import Image

from make_sheet import CALM, RAW, colourise, crossfade, form_of, mean_of, periodic, seam_ratio, wall
from restyle_tiles import LIQUIDS, SOFT_ALPHA, cutout_reference
from tilelib import BUILT, PREVIOUS, REDRAWS, REPO, SHEETS, TEXTURES, TILE, luma, read_previous, repeat, scale, sheet

STRENGTH = CALM * 0.5
DONE = BUILT
DEST = TEXTURES

# The metal blocks: four forms, dealt so that metals of a similar colour (six of them are greys) differ by form.
METALS = {
    "gold_block": "metal_block_a", "copper_block": "metal_block_a", "cobalt_block": "metal_block_a",
    "nickel_block": "metal_block_a", "lead_block": "metal_block_a",
    "silver_block": "metal_studs_a", "platinum_block": "metal_studs_a", "tungsten_block": "metal_studs_a",
    "aluminium_block": "metal_brushed_a", "titanium_block": "metal_brushed_a",
    "iron_block": "metal_panels_a", "tin_block": "metal_panels_a", "zinc_block": "metal_panels_a",
    "bronze_block": "metal_panels_a", "brass_block": "metal_panels_a",
}

# Strength of a key's pattern against the common one: where lines ARE the block (the gaps of planks, a plate's
# bevel and panels), half strength would leave a plain colour.
BOOST = {"wood_planks": 3.0, "wood_planks_light": 3.0, "wood_planks_mahogany": 3.0, "crystal": 1.6,
         **{metal: 2.2 for metal in METALS}}

# Masks whose shapes should cover less than drawn (the threshold of the coverage, default 0.10).
MASK_TIGHT = {"patches": 0.55}

# Keys whose base colour is another key's mean colour instead of a reading of their own tile (the previous moss
# stone is so overgrown that its "plain" pixels are green too: its rock takes the stone's grey).
BASE_COLOUR_OF = {"moss_stone": "stone"}

# key -> candidate (its form)
SINGLE: dict[str, str] = {
    "grass": "grass_c", "dirt": "dirt_b", "sand": "sand_b", "stone": "stone_b", "snow": "snow_c",
    "basalt": "basalt_a", "mud": "mud_a", "deepslate": "deepslate_r_a", "sandstone": "sandstone_a",
    # dark_sandstone and blackened_sandstone are MASONRY (bricks), not rock: they are redrawn from their own tile
    # (restyle_tiles.py), a banded rock form would lose what they are.
    "scree": "scree_a", "ash": "ash_a",
    "salt": "salt_a", "black_sand": "sand_b", "peat": "peat_a", "coral_rock": "coral_rock_a",
    "sulfur_stone": "sulfur_stone_a", "ice": "ice_a", "bone": "bone_a", "dripstone": "dripstone_a", "tar": "tar_a",
    "travertine": "travertine_a", "concrete": "concrete_a", "crystal": "crystal_a", "silicate": "granite_a",
    "wood_log": "wood_log_a", "wood_log_look2": "wood_log_look2_a", "wood_log_look3": "wood_log_look3_r_a",
    "wood_log_look4": "wood_log_look4_a", "wood_log_look5": "wood_log_look5_a", "wood_log_look6": "wood_log_look6_a",
    "giant_log": "giant_log_a", "mushroom_stem": "mushroom_stem_a",
    "wood_planks": "wood_planks_a", "wood_planks_light": "wood_planks_a", "wood_planks_mahogany": "wood_planks_a",
    **METALS,
}

# key -> (base form, mask, form of the second material or None, which colour group is the second material)
DOUBLE: dict[str, tuple[str, str, str | None, str]] = {
    "granite": ("granite_a", "speckles", None, "darker"),
    "moss_stone": ("stone_b", "patches", "grass_c", "saturated"),
    "alien_grass": ("alien_grass_a", "tufts", None, "lighter"),
    "mycelium": ("mycelium_a", "threads", None, "lighter"),
    "wood_log_b": ("wood_log_look3_r_a", "birch", None, "darker"),
    "wood_log_b_look2": ("wood_log_look3_r_a", "poplar", None, "darker"),
    "wood_log_b_look3": ("wood_log_look3_r_a", "flakes", None, "darker"),
    "tainted_soil": ("dirt_b", "flecks", None, "saturated"),
    "tainted_subsoil": ("dirt_b", "threads", None, "saturated"),
    "tainted_stone": ("stone_b", "veins", None, "saturated"),
    "tainted_log": ("wood_log_a", "threads:rot", None, "saturated"),
    "iron_ore": ("stone_b", "flecks", None, "saturated"), "copper_ore": ("stone_b", "veins", None, "saturated"),
    "titanium_ore": ("stone_b", "flecks", None, "lighter"), "gold_ore": ("stone_b", "veins", None, "saturated"),
    "silver_ore": ("stone_b", "veins", None, "lighter"), "aluminium_ore": ("stone_b", "flecks", None, "lighter"),
    "tin_ore": ("stone_b", "flecks", None, "lighter"), "nickel_ore": ("stone_b", "veins", None, "lighter"),
    "cobalt_ore": ("stone_b", "veins", None, "saturated"), "lithium_ore": ("stone_b", "streaks", None, "lighter"),
    "uranium_ore": ("stone_b", "flecks", None, "saturated"), "platinum_ore": ("stone_b", "flecks", None, "lighter"),
    "lead_ore": ("stone_b", "flecks", None, "lighter"), "zinc_ore": ("stone_b", "crystals", None, "lighter"),
    "tungsten_ore": ("stone_b", "flecks", None, "smaller"), "sulfur_ore": ("stone_b", "crystals", None, "saturated"),
    "neodymium_ore": ("stone_b", "veins", None, "saturated"), "diamond_ore": ("stone_b", "crystals", None, "lighter"),
    "carbon": ("stone_b", "flecks", None, "lighter"),
}

_forms: dict[str, np.ndarray] = {}
_masks: dict[str, np.ndarray] = {}


def form(candidate: str) -> np.ndarray:
    if candidate not in _forms:
        _forms[candidate] = form_of(candidate)
    return _forms[candidate]


def mask(name: str) -> np.ndarray:
    """A mask as 64 x 64 coverage 0..1 (white = the second material), healed to wrap like a form."""
    if name.endswith(":rot"):
        return np.rot90(mask(name[:-4]))   # the same shapes turned by a quarter (veins that run down a trunk)
    if name not in _masks:
        raw = np.asarray(Image.open(RAW / f"mask_{name}_a.png").convert("L"), dtype=np.float64) / 255.0
        n = raw.shape[0] // TILE

        def small(a: np.ndarray) -> np.ndarray:
            return a.reshape(TILE, n, TILE, n).mean(axis=(1, 3))

        g = small(periodic(raw))
        if seam_ratio(g) > 1.6:
            g = small(periodic(crossfade(raw)))
        lo, hi = np.percentile(g, 3), np.percentile(g, 99.5)
        t = np.clip((g - lo) / max(hi - lo, 1e-6), 0.0, 1.0)
        # Thin shapes (threads, dashes) come out of the 16 x 16 area average as greys: a low threshold keeps them.
        t = np.clip((t - MASK_TIGHT.get(name, 0.10)) / 0.35, 0.0, 1.0)
        m = t * t * (3.0 - 2.0 * t)
        print(f"mask {name}: covers {m.mean() * 100:.0f} %, seam {seam_ratio(g):.2f}")
        _masks[name] = m
    return _masks[name]


def mask_sheet() -> None:
    names = sorted({v[1] for v in DOUBLE.values()})
    cells = [Image.fromarray((mask(n) * 255.0).astype(np.uint8), "L").convert("RGB").resize((192, 192), Image.NEAREST)
             for n in names]
    half = (len(cells) + 1) // 2
    sheet([(", ".join(names[:half]), cells[:half]), (", ".join(names[half:]), cells[half:])], [""] * half,
          SHEETS / "masks.png", "Masks (white = the second material)", label_w=10)


def two_colours(key: str, second: str) -> tuple[np.ndarray, np.ndarray]:
    """The two colours of the previous tile: (base colour, second material's colour). `second` says how the second
    material stands out: "saturated" (a coloured ore, moss, poison in a dull host), "lighter" or "darker" (pale
    metal flecks, the marks of a birch) — then it is the mean of the pixels that stand out most and the base is the
    mean of the plain rest; "smaller" splits the pixels into two groups (k-means) and takes the smaller one."""
    px = np.asarray(read_previous(key), dtype=np.float64).reshape(-1, 3)
    lum = luma(px)
    if second != "smaller":
        measure = {"saturated": px.max(axis=1) - px.min(axis=1), "lighter": lum, "darker": -lum}[second]
        accent = px[measure >= np.percentile(measure, 88)].mean(axis=0)
        plain = px[measure <= np.percentile(measure, 55)].mean(axis=0)
        return plain, accent

    centres = np.stack([px[lum <= np.percentile(lum, 30)].mean(axis=0), px[lum >= np.percentile(lum, 70)].mean(axis=0)])
    for _ in range(12):
        d = ((px[:, None, :] - centres[None, :, :]) ** 2).sum(axis=2)
        which = d.argmin(axis=1)
        for k in (0, 1):
            if (which == k).any():
                centres[k] = px[which == k].mean(axis=0)
    share = np.array([(which == 0).mean(), (which == 1).mean()])
    sat = centres.max(axis=1) - centres.min(axis=1)
    lumc = luma(centres)
    b = {"smaller": int(share.argmin()), "saturated": int(sat.argmax()), "lighter": int(lumc.argmax()),
         "darker": int(lumc.argmin())}[second]
    return centres[1 - b], centres[b]


SURFACES = ("creature_", "avatar_", "held_", "enemy_")   # redrawn tiles that repeat on a body: healed, colour kept


def heal_rgb(a: np.ndarray, key: str) -> np.ndarray:
    """A redrawn surface made to wrap: Moisan's periodic part per channel; a cross-fade first where a structure still
    breaks at the border (the creature tests allow a seam ratio of 1.3)."""
    n = a.shape[0] // TILE

    def ratio(img: np.ndarray) -> float:
        return seam_ratio(luma(img).reshape(TILE, n, TILE, n).mean(axis=(1, 3)))

    healed = np.stack([periodic(a[..., c] / 255.0) for c in range(3)], axis=-1) * 255.0
    if ratio(healed) > 1.3:
        healed = np.stack([periodic(crossfade(a[..., c] / 255.0)) for c in range(3)], axis=-1) * 255.0
    if ratio(healed) > 1.3:
        print(f"surface {key}: seam ratio still {ratio(healed):.2f}")
    return np.clip(healed, 0.0, 255.0)


def keep_colour(small: np.ndarray, key: str) -> np.ndarray:
    """Gives a redrawn surface the mean colour of the previous tile. A tile that was grey stays exactly grey: the game
    colours hides and suits itself, a tint of the model's would shift every creature."""
    old = np.asarray(read_previous(key), dtype=np.float64)
    chroma = (old.max(axis=2) - old.min(axis=2)).mean() / 255.0
    if chroma < 0.04 or key == "sound_block":   # the sound block must carry no hue at all (gen_textures.neutral_grey)
        grey = luma(small) * 255.0
        grey += luma(old).mean() * 255.0 - grey.mean()
        return np.repeat(grey[..., None], 3, axis=2)
    return small + (old.reshape(-1, 3).mean(axis=0) - small.reshape(-1, 3).mean(axis=0))


def previous_rgba(key: str) -> Image.Image:
    return Image.frombytes("RGBA", (TILE, TILE), (PREVIOUS / f"{key}.bytes").read_bytes()).transpose(Image.FLIP_TOP_BOTTOM)


def with_old_alpha(small: np.ndarray, key: str) -> Image.Image:
    """A redrawn cut-out tile: the new colours inside the OLD shape. The shape is data — where a billboard roots, what
    the shader clips, the leaf holes — so the alpha of the previous tile is kept exactly. Where the redrawn picture is
    background-black inside the old shape (its outline moved a texel or two), the colour is filled in from the lit
    neighbours, or the plant would get a black rim. Outside the shape the previous RGB stays (the normal atlas and the mean
    mip read it)."""
    old = np.asarray(previous_rgba(key), dtype=np.float64)
    inside = old[..., 3] > (16 if key in SOFT_ALPHA else 128)
    rgb = small.copy()
    if key in SOFT_ALPHA:
        # Shown to the model darkened by its own alpha (a flame fades out): undo that where the tile is not clear.
        rgb = np.clip(rgb / np.maximum(old[..., 3:4] / 255.0, 0.35), 0.0, 255.0)
    bad = inside & (luma(rgb) < 0.10) & (luma(old[..., :3]) > 0.16)
    good = inside & ~bad
    for _ in range(8):
        if not bad.any():
            break
        acc = np.zeros_like(rgb)
        count = np.zeros(rgb.shape[:2])
        for dy in (-1, 0, 1):
            for dx in (-1, 0, 1):
                if dy or dx:
                    g = np.roll(good, (dy, dx), axis=(0, 1))
                    acc += np.roll(rgb, (dy, dx), axis=(0, 1)) * g[..., None]
                    count += g
        fill = bad & (count > 0)
        rgb[fill] = acc[fill] / count[fill][:, None]
        good |= fill
        bad &= ~fill
    rgb[bad] = old[..., :3][bad]              # no lit neighbour at all: the previous texel
    rgb[~inside] = old[..., :3][~inside]
    out = np.dstack([np.clip(rgb + 0.5, 0, 255), old[..., 3]]).astype(np.uint8)
    return Image.fromarray(out, "RGBA")


def single(key: str) -> Image.Image:
    return colourise(form(SINGLE[key]), mean_of(key), STRENGTH * BOOST.get(key, 1.0))


def double(key: str) -> Image.Image:
    base, mask_name, inlay, second = DOUBLE[key]
    colour_a, colour_b = two_colours(key, second)
    if key in BASE_COLOUR_OF:
        colour_a = mean_of(BASE_COLOUR_OF[key])
    m = mask(mask_name)
    z_b = form(inlay) if inlay else (m - 0.5) * 1.6 + form(base) * 0.4   # a nugget is lighter in its middle
    a = np.asarray(colourise(form(base), colour_a, STRENGTH), dtype=np.float64)
    b = np.asarray(colourise(z_b, colour_b, STRENGTH * 1.5), dtype=np.float64)
    edge = 4.0 * m * (1.0 - m)                                            # the rim seats the inlay in the base
    out = (a + (b - a) * m[..., None]) * (1.0 - 0.10 * edge[..., None])
    return Image.fromarray(np.clip(out + 0.5, 0, 255).astype(np.uint8), "RGB")


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--bundle", action="store_true", help="also write the .bytes files into client/Assets/Resources/textures")
    args = ap.parse_args()

    DONE.mkdir(parents=True, exist_ok=True)
    tiles: dict[str, Image.Image] = {}
    for key in SINGLE:
        tiles[key] = single(key)
    for key in DOUBLE:
        tiles[key] = double(key)
    for key, tile in tiles.items():
        tile.save(DONE / f"{key}.png")

    # Sheets: two tiles per row, each as previous | new.
    keys = list(tiles)
    rows = []
    for i in range(0, len(keys), 2):
        pair = keys[i:i + 2]
        cells = []
        for key in pair:
            cells += [wall(read_previous(key)), wall(tiles[key])]
        rows.append(("\n".join(pair), cells))
    for n in range(0, len(rows), 8):
        sheet(rows[n:n + 8], ["previous", "NEW", "previous", "NEW"], SHEETS / f"built-forms-{n // 8 + 1}.png",
              "Calm tiles (half strength): 3 x 3 blocks each", label_w=190)
    mask_sheet()

    # RESTYLED: the picture tiles (devices, furniture, doors, lamps, panels) redrawn in the calm look with the
    # previous tile as the reference (restyle_tiles.py) — full colour, because an object has fixed colours. 64 px by area mean.
    restyled: dict[str, Image.Image] = {}
    cutouts: list[str] = []
    for path in sorted(REDRAWS.glob("*.png")):
        key = path.stem
        if key.endswith("_64") or key in tiles:
            continue
        a = np.asarray(Image.open(path).convert("RGB"), dtype=np.float64)
        cutout = cutout_reference(key) is not None
        surface = (key.startswith(SURFACES) or key in LIQUIDS) and not cutout
        if surface:
            a = heal_rgb(a, key)
        n = a.shape[0] // TILE
        small = a.reshape(TILE, n, TILE, n, 3).mean(axis=(1, 3))
        if surface or key == "sound_block":
            small = keep_colour(small, key)
        if cutout:
            restyled[key] = with_old_alpha(small, key)
            cutouts.append(key)
        else:
            restyled[key] = Image.fromarray(np.clip(small + 0.5, 0, 255).astype(np.uint8), "RGB")
        restyled[key].save(DONE / f"{key}.png")

    # Cut-out tiles on their own sheets, over a flat grey: the old shape with the new colours in it.
    def on_grey(img: Image.Image) -> Image.Image:
        back = Image.new("RGBA", img.size, (74, 80, 92, 255))
        back.alpha_composite(img.convert("RGBA"))
        return back.convert("RGB").resize((192, 192), Image.NEAREST)

    rows = []
    for i in range(0, len(cutouts), 4):
        four = cutouts[i:i + 4]
        cells = []
        for key in four:
            cells += [on_grey(previous_rgba(key)), on_grey(restyled[key])]
        rows.append(("\n".join(four), cells))
    for n in range(0, len(rows), 10):
        sheet(rows[n:n + 10], ["previous", "NEW"] * 4, SHEETS / f"built-cutouts-{n // 10 + 1}.png",
              "Cut-out tiles (plants, leaves, critters): previous shape, new colours", label_w=200)

    # Surfaces (hides, suit, tool materials) on their own sheets, laid 2 x 2: they repeat on a body.
    surfaces = [k for k in restyled if k.startswith(SURFACES) and k not in cutouts]
    rows = []
    for i in range(0, len(surfaces), 3):
        three = surfaces[i:i + 3]
        cells = []
        for key in three:
            cells += [scale(repeat(read_previous(key), 2), 2), scale(repeat(restyled[key], 2), 2)]
        rows.append(("\n".join(k.replace("creature_", "") for k in three), cells))
    for n in range(0, len(rows), 8):
        sheet(rows[n:n + 8], ["previous", "NEW"] * 3, SHEETS / f"built-surfaces-{n // 8 + 1}.png",
              "Creature hides, suit and tool materials in the calm style: laid 2 x 2 each", label_w=150)
    names = [k for k in restyled if not k.startswith(SURFACES) and k not in cutouts]
    rows = []
    for i in range(0, len(names), 4):
        four = names[i:i + 4]
        cells = []
        for key in four:
            cells += [read_previous(key).resize((192, 192), Image.NEAREST), restyled[key].resize((192, 192), Image.NEAREST)]
        rows.append(("\n".join(four), cells))
    for n in range(0, len(rows), 10):
        sheet(rows[n:n + 10], ["previous", "NEW"] * 4, SHEETS / f"built-pictures-{n // 10 + 1}.png",
              "Devices, furniture, doors, lamps in the calm style: each tile previous and new", label_w=190)
    tiles.update(restyled)
    print(f"{len(tiles)} tiles ({len(SINGLE)} single, {len(DOUBLE)} double, {len(restyled)} restyled)")

    if args.bundle:
        DEST.mkdir(parents=True, exist_ok=True)
        # Round 6 (rock_tiles.py, #4000) replaced the rock and ore tiles: never write the older build over them.
        provenance = json.loads((REPO / "tools" / "ai-assets" / "texture_provenance.json").read_text(encoding="utf-8"))
        later = {k for k, v in provenance["textures"].items() if "rock_tiles.py" in v}
        tiles = {k: v for k, v in tiles.items() if k not in later}
        for key, tile in tiles.items():
            raw = tile.convert("RGBA").transpose(Image.FLIP_TOP_BOTTOM).tobytes()
            assert len(raw) == TILE * TILE * 4
            (DEST / f"{key}.bytes").write_bytes(raw)
        print(f"bundled {len(tiles)} tiles into {DEST}")


if __name__ == "__main__":
    main()
