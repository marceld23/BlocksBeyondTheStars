# Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
# SPDX-License-Identifier: AGPL-3.0-or-later
# This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
"""Step 3 (PAID): redraws the previous tiles in the calm look, with the previous tile as the reference (one
image-edit call per tile). The default list is the PICTURE tiles (devices, furniture, doors, lamps, panels — tiles
that show an object in fixed colours); --surfaces takes the hides, the suit and the tool materials, --cutouts the
cut-out plants, leaves and critters, --mural the 4 x 2 mural, --only a list of keys (the liquids were redrawn with
--surfaces --only <LIQUIDS>). Results go to tools/ai-assets/out/calm_tiles/redraws/ (1024 px, git-ignored) — never
into the game; build_tiles.py turns them into 64 px tiles. A redraw that exists is not generated again, so a second
run only fills gaps. Image generation costs money and is not reproducible; no test runs this.

The previous tiles are read from tools/ai-assets/out/calm_tiles/previous (README.md): redrawing the CURRENT tiles
would redraw a redraw. Animated and see-through tiles are skipped automatically.

    uv run --project tools/ai-assets --with numpy python tools/ai-assets/calm_tiles/restyle_tiles.py [--only a,b] [--dry-run]
"""
from __future__ import annotations

import argparse
import base64
import io
import os
import sys
import time
from concurrent.futures import ThreadPoolExecutor

import numpy as np
from PIL import Image

from tilelib import OUT as CALM_OUT
from tilelib import PREVIOUS as BUNDLED
from tilelib import REDRAWS, REPO, TILE

OUT = REDRAWS
PROMPT = ("Redraw this pixel-art game block tile as the same object, in the same layout and the same colours, but in "
          "a soft stylised hand-painted look like smooth matte modelling clay: large simple shapes with soft rounded "
          "edges, gentle even shading, very little detail, no pixel grid, no dithering, no noise, no outlines, no "
          "text. Keep every element where it is and keep the picture filling the whole image edge to edge.")

MACHINES = """alarm_siren algae_tank announcer arms_rack auto_drill_1 auto_drill_2 auto_drill_3 base_core base_pad bed
bio_lab bridge_motor caller campfire chime clinic_post clone_tank crew_bunk crystal_button crystal_switch
daylight_sensor decontaminator detoxifier device_eye dice_block drill_laser environment_sensor fabricator
factory_terminal forge guardian_spawner heal_tank horn hydro_tray lift_motor lift_stop matter_receiver matter_sender
melody_block mission_board piston power_relay press_desk proximity_sensor quarry_post radio_beacon rail_post rail_pylon
rail_stop sage_lectern sentry_post ship_sensor shop_counter signal_display signal_receiver signal_sender
station_container station_core station_vendor step_plate storage_sensor streamer_post stretcher tamer_post thumper
timer_block watcher water_spout workbench factory_pipe logic_block machine_block piston_head""".split()
FACES = """face_alarm_siren_top face_algae_tank_top face_base_core_side face_container_side face_crate_top
face_crystal_side face_crystal_top face_detoxifier_top face_energy_fence_top face_forge_side face_forge_top
face_gaming_pc_side face_heal_tank_top face_hydro_tray_side face_industrial_side face_industrial_top face_light_side
face_light_top face_matter_receiver_top face_matter_sender_top face_plastic_side face_plastic_top
face_radio_beacon_top face_rail_pylon_top face_rust_side face_rust_top face_sage_lectern_side face_sage_lectern_top
face_tech_side face_tech_top face_transport_crate_top face_wood_crate_top face_wood_side face_wood_top
face_workbench_side""".split()
BUILDING = """crate flower_pot ladder rug transport_crate wood_crate gaming_keyboard gaming_monitor gaming_mouse
gaming_pc bridge_deck carved_obsidian gift_box insulated_wall iron_wall lift_platform maze_stone maze_stone_carved
mine_cart mine_track phase_block steel_floor steel_wall verdigris_copper stairs crystal_conduit door_hinge door_slide
door_wood trapdoor trapdoor_open lantern paper_lantern pumpkin_lantern torch diya_lamp beam_block broken_machine
geyser_vent rusted_panel scrap_metal scrap_pile tainted_copper_ore tainted_diamond_ore tainted_iron_ore
tainted_titanium_ore asteroid_rock dark_sandstone blackened_sandstone""".split()
# Round 4 (2026-11-22, the owner: "there are no hand-painted tiles. make new tiles."): the tiles the first pass left
# out as somebody's design — the rune stone and the ancient brick with their eight biome looks, the school club's and
# the story's blocks. The mural is redrawn as ONE picture (restyle_mural), or its eight tiles would not meet.
PALETTES = "verdant desert frost volcanic marsh rock crystal toxic".split()
STORY = ("""rune_stone ancient_brick metal_panel chime_moss fifi_stem fifi_leaf fifi_blossom paul_leaf paul_petals
paul_stem burger_bun burger_bun_sesame burger_cheese burger_lettuce burger_patty burger_tomato gt_eye
gds_diving_helmet gds_guardian_head gds_skull ressaw_block ressaw_block_cracked ressaw_conduit capsule_tank
capsule_tank_open sps_console song_spindle glyph_stone glyph_stone_band resonance_crystal ressaw_crystal_ore
mushroom_cap saguaro""".split()
         + [f"rune_stone_{p}" for p in PALETTES] + [f"ancient_brick_{p}" for p in PALETTES])
# Round 5 ("the remaining textures in one go"): what was still left — the solid plants' own tiles, glass and
# the energy fields (their see-through comes from the material, the tile is opaque) and the sound block.
REST = """flora_alienfern flora_bulb flora_cactus flora_cavecap flora_cropshroom flora_crystal flora_emberbloom
flora_glowcap flora_glowvine flora_mushroom flora_pitcher flora_prismbloom flora_puffball flora_pumpkin
flora_shardbloom flora_sporepod flora_succulent flora_tendril door_energy energy_gate phase_block_open glass
glass_clear sound_block""".split()
KEYS = MACHINES + FACES + BUILDING + STORY + REST

# The liquids are surfaces that repeat from block to block (redrawn with --surfaces --only …, healed by build_tiles.py).
LIQUIDS = "water lava gas oil gt_fluid ressaw".split()
# Tiles whose alpha is a soft falloff (flames, glowing plankton): shown to the model over black like a cut-out.
SOFT_ALPHA = {"fire", "microfauna_glowplankton"}

# SURFACES: tiles that are no picture of an object but a surface that repeats on a body — the creatures' hides, the
# avatar's suit, the tools' materials. Their own prompt (the pattern, not an object), and build_tiles.py heals their
# seams and keeps their colour (most are grey: the game colours them).
SURFACE_PROMPT = ("Redraw this pixel-art surface texture with the same pattern, the same layout and the same "
                  "brightness and colours, but in a soft stylised hand-painted look like smooth matte modelling clay: "
                  "large simple shapes with soft edges, gentle even shading, very little detail, no pixel grid, no "
                  "dithering, no noise, no outlines, no text. It is a seamless texture that repeats: keep it filling "
                  "the whole image edge to edge with nothing new added.")


def surface_keys() -> list[str]:
    """Every bundled hide, suit and tool-material tile (not the coat pattern masks and horn ramps, which are data)."""
    keys = []
    for path in sorted(BUNDLED.glob("*.bytes")):
        k = path.stem
        if k.endswith("__anim") or k.startswith(("creature_pattern_", "creature_horn_")):
            continue
        if k.startswith(("creature_", "avatar_", "held_")):
            keys.append(k)
    return keys


# CUTOUTS: tiles with a cut-out shape — the plants' crossed pictures, the leaves, small critters. The shape (alpha) is
# data the game relies on (where a billboard roots, what the shader clips), so it is KEPT: the tile is shown to the
# model on black, redrawn, and build_tiles.py puts the old alpha back over the new colours.
CUTOUT_PROMPT = ("Redraw this pixel-art game sprite as the same thing, in exactly the same shape, position and "
                 "colours, but in a soft stylised hand-painted look like smooth matte modelling clay: large simple "
                 "shapes with soft rounded edges, gentle even shading, very little detail, no pixel grid, no "
                 "dithering, no noise, no outlines, no text. Keep the background pure black exactly where it is black "
                 "now and do not add anything.")


def cutout_reference(key: str) -> Image.Image | None:
    """The previous cut-out tile over black — or None when the tile is opaque, animated or see-through (a pane of glass or
    an energy field has an alpha between: over black it would only get darker)."""
    path = BUNDLED / f"{key}.bytes"
    if not path.exists() or (BUNDLED / f"{key}__anim.bytes").exists():
        return None
    rgba = np.asarray(Image.frombytes("RGBA", (TILE, TILE), path.read_bytes()).transpose(Image.FLIP_TOP_BOTTOM))
    alpha = rgba[..., 3]
    if key in SOFT_ALPHA:
        return Image.fromarray((rgba[..., :3] * (alpha[..., None] / 255.0)).astype(np.uint8), "RGB")
    if alpha.min() == 255 or ((alpha > 16) & (alpha < 240)).mean() > 0.10 or (alpha > 128).mean() < 0.02:
        return None
    rgb = rgba[..., :3] * (alpha[..., None] > 128)
    return Image.fromarray(rgb.astype(np.uint8), "RGB")


def cutout_keys() -> list[str]:
    return [p.stem for p in sorted(BUNDLED.glob("*.bytes"))
            if not p.stem.endswith("__anim") and not p.stem.startswith(("creature_pattern_", "creature_horn_"))
            and cutout_reference(p.stem) is not None]


def reference(key: str) -> Image.Image | None:
    """The previous tile as an RGB picture — or None when it must not be redrawn (missing, animated, see-through)."""
    path = BUNDLED / f"{key}.bytes"
    if not path.exists() or (BUNDLED / f"{key}__anim.bytes").exists():
        return None
    rgba = Image.frombytes("RGBA", (TILE, TILE), path.read_bytes()).transpose(Image.FLIP_TOP_BOTTOM)
    if np.asarray(rgba)[..., 3].min() < 255:
        return None
    return rgba.convert("RGB")


def restyle_mural(args: argparse.Namespace) -> None:
    """The Grand Technicians' mural (gt_mural_0..7, 4 x 2 tiles, row-major from the top left — cut_mural.py): put
    together, redrawn in one call on a 1536 x 1024 canvas (the mural in the middle band, its edge rows stretched into
    the margin), and cut into eight 384 px pieces named like the tiles."""
    cols, rows = 4, 2
    names = [f"gt_mural_{i}" for i in range(cols * rows)]
    if all((OUT / f"{n}.png").exists() for n in names):
        print("[mural] exists")
        return
    mural = Image.new("RGB", (cols * TILE, rows * TILE))
    for i, name in enumerate(names):
        ref = reference(name)
        if ref is None:
            sys.exit(f"mural tile {name} cannot be read")
        mural.paste(ref, ((i % cols) * TILE, (i // cols) * TILE))
    band = mural.resize((1536, 768), Image.NEAREST)
    canvas = Image.new("RGB", (1536, 1024))
    canvas.paste(band.crop((0, 0, 1536, 1)).resize((1536, 128), Image.NEAREST), (0, 0))
    canvas.paste(band, (0, 128))
    canvas.paste(band.crop((0, 767, 1536, 768)).resize((1536, 128), Image.NEAREST), (0, 896))
    if args.dry_run:
        print("[mural] would redraw the mural as one picture")
        return
    from dotenv import load_dotenv
    load_dotenv(REPO / "tools" / "ai-assets" / ".env")
    from openai import OpenAI
    client = OpenAI(api_key=os.environ["OPENAI_API_KEY"])
    buf = io.BytesIO()
    canvas.save(buf, format="PNG")
    buf.seek(0)
    buf.name = "mural.png"
    text = PROMPT.replace("game block tile", "wall mural") + " Do not move anything: the picture is cut into tiles."
    resp = client.images.edit(model=args.model, image=buf, prompt=text, size="1536x1024", quality=args.quality, n=1)
    whole = Image.open(io.BytesIO(base64.b64decode(resp.data[0].b64_json))).convert("RGB")
    whole.save(CALM_OUT / "mural_gt_whole.png")   # kept apart: the redraw folder holds tiles only
    band = whole.resize((1536, 1024), Image.LANCZOS).crop((0, 128, 1536, 896))
    for i, name in enumerate(names):
        x, y = (i % cols) * 384, (i // cols) * 384
        band.crop((x, y, x + 384, y + 384)).save(OUT / f"{name}.png")
    print("[mural] ok")


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--only", default=None)
    ap.add_argument("--model", default="gpt-image-2.6-sunburst")
    ap.add_argument("--quality", default="low")
    ap.add_argument("--workers", type=int, default=4)
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--surfaces", action="store_true", help="the hides, suit and tool-material tiles instead")
    ap.add_argument("--mural", action="store_true", help="the 4 x 2 mural, redrawn as one picture")
    ap.add_argument("--cutouts", action="store_true", help="the cut-out tiles (plants, leaves, critters) instead")
    args = ap.parse_args()
    prompt = CUTOUT_PROMPT if args.cutouts else SURFACE_PROMPT if args.surfaces else PROMPT
    keys = ([k.strip() for k in args.only.split(",")] if args.only else cutout_keys() if args.cutouts
            else surface_keys() if args.surfaces else KEYS)
    read = cutout_reference if args.cutouts else reference
    OUT.mkdir(parents=True, exist_ok=True)
    if args.mural:
        restyle_mural(args)
        return
    jobs = []
    for k in keys:
        if (OUT / f"{k}.png").exists():
            continue
        ref = read(k)
        if ref is None:
            print(f"skip {k} (missing, animated or see-through)")
            continue
        jobs.append((k, ref))
    print(f"[restyle] {len(keys)} keys, {len(jobs)} to generate, model {args.model}")
    if args.dry_run or not jobs:
        return

    from dotenv import load_dotenv
    load_dotenv(REPO / "tools" / "ai-assets" / ".env")
    key = os.environ.get("OPENAI_API_KEY")
    if not key:
        sys.exit("OPENAI_API_KEY is not set.")
    from openai import OpenAI
    client = OpenAI(api_key=key)

    def run(job: tuple[str, Image.Image]) -> str:
        k, ref = job
        for attempt in (1, 2, 3):
            buf = io.BytesIO()
            ref.resize((1024, 1024), Image.NEAREST).save(buf, format="PNG")
            buf.seek(0)
            buf.name = f"{k}.png"
            try:
                resp = client.images.edit(model=args.model, image=buf, prompt=prompt, size="1024x1024",
                                          quality=args.quality, n=1)
                (OUT / f"{k}.png").write_bytes(base64.b64decode(resp.data[0].b64_json))
                return f"{k}: ok"
            except Exception as exc:  # noqa: BLE001
                last = str(exc)[:160]
                time.sleep(4 * attempt)
        return f"{k}: FAILED {last}"

    done = 0
    with ThreadPoolExecutor(max_workers=args.workers) as pool:
        for line in pool.map(run, jobs):
            done += 1
            print(f"[{done}/{len(jobs)}] {line}", flush=True)


if __name__ == "__main__":
    main()
