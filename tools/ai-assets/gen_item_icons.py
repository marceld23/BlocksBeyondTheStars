"""Generate content-styled inventory icons (Task 4) via OpenAI images.

Full-colour, centered, transparent-background object icons for every non-block item, ship module
and space tool — the menu/hotbar surface these by key (see IconResolver in the client). Materials
that map to a block already get their block-atlas tile, so they are intentionally NOT listed here.

One API call per icon; resumable (existing out/item_icons/item_<key>.png are skipped) and tolerant
of single failures. Files are named with an ``item_`` prefix so they sit alongside the cyan-line UI
icons in client/Assets/Resources/icons without colliding.

Usage:
    uv run gen_item_icons.py --dry-run            # list, no API calls
    uv run gen_item_icons.py --only creature_meat # one icon (style approval)
    uv run gen_item_icons.py                       # full batch (skips existing)
    uv run gen_item_icons.py --install             # copy out/item_icons -> client Resources/icons
"""
from __future__ import annotations

import argparse
import base64
import os
import shutil
import sys
import time
from io import BytesIO
from pathlib import Path

from dotenv import load_dotenv

OUT = Path("out/item_icons")
CLIENT_ICONS = Path("../../client/Assets/BlocksBeyondTheStars/../..") / "client" / "Assets" / "Resources" / "icons"
# Resolve the client icons dir relative to the repo root regardless of CWD quirks.
CLIENT_ICONS = (Path(__file__).resolve().parents[2] / "client" / "Assets" / "Resources" / "icons")

# Full colour, single centered object, transparent — reads next to the block-texture material icons.
STYLE = ("a single video-game inventory icon, full colour, painterly sci-fi, one centered object, "
         "soft rim light, subtle shading, fully transparent background, no text, no words, no "
         "letters, no border, no background scenery")

# Non-block ITEMS (materials with a block get their atlas tile instead, so they are omitted here).
ITEMS = [
    # Processed materials & components
    ("iron_ingot", "a shiny grey iron metal ingot bar"),
    ("iron_plate", "a flat rectangular riveted iron metal plate"),
    ("copper_wire", "a neat coil of orange copper wire"),
    ("cable", "a bundle of insulated electrical cables"),
    ("carbon_composite", "a woven black carbon-fibre composite panel"),
    ("energy_cell_1", "a glowing blue rechargeable energy cell battery"),
    ("titanium_plate", "a flat brushed silvery titanium plate"),
    ("data_fragment", "a glowing translucent blue data crystal chip"),
    ("ai_memory_fragment", "a cracked dark crystal memory shard with faint amber circuit traces glowing inside"),
    ("ai_core_mk2", "a compact cubic ship AI computer core, brushed gunmetal housing with one glowing cyan ring eye"),
    ("ai_core_mk3", "an advanced spherical ship AI computer core, dark metal lattice with bright cyan energy seams"),
    ("plant_fiber", "a small bundle of dried green plant fibres"),
    # Consumables (toxic ones are tinted green at runtime; base art stays natural)
    ("creature_meat", "a juicy grilled steak of meat on a small bone"),
    ("berries", "a small cluster of round red berries with a leaf"),
    ("toxic_gland", "a glistening wet alien organ gland"),
    ("toxic_berries", "a small cluster of round berries with a leaf"),
    ("emergency_ration", "a sealed silver foil ration food pack"),
    ("medpack", "a white medical kit box with a red cross"),
    ("field_medkit", "a compact handheld medical injector gadget, white and green casing with a small red cross and a glowing green emitter tip"),
    ("stasis_projector", "a sleek handheld sci-fi stasis projector gun, dark metal with a glowing cyan crystal emitter and faint blue energy rings"),
    ("terrain_blaster", "a chunky heavy handheld sci-fi terrain blaster cannon, dark armoured metal with glowing orange energy vents and a wide barrel muzzle"),
    ("creature_translator", "a sleek handheld sci-fi creature translator device, dark rounded casing with a small glowing cyan screen and a soft concentric sound-wave emitter on the front"),
    ("forage_bait", "a small tied bundle of leafy green plant forage and seeds"),
    ("meat_bait", "a raw red strip of fresh meat on a small metal hook"),
    ("nectar_lure", "a small glass vial of glowing golden sweet nectar with a cork stopper"),
    ("oxygen_tank_1", "a small cyan compressed oxygen gas canister"),
    # Suit / wearable components
    ("suit_teleporter", "a glowing belt teleporter device with swirling portal energy"),
    ("oxygen_extractor", "a compact atmospheric oxygen extractor device"),
    ("stealth_suit", "a sleek dark stealth cloaking bodysuit"),
    ("armor_chest", "an armored chest breastplate"),
    ("armor_legs", "a pair of armored leg greaves"),
    ("helmet", "a space suit helmet with a tinted visor"),
    ("oxygen_tank_2", "a large high-capacity cyan oxygen tank"),
    ("suit_lamp", "a helmet-mounted headlamp casting light"),
    ("jetpack", "a rocket jetpack backpack with twin thrusters"),
    ("radar_scanner", "a handheld radar scanner with a small dish"),
    ("comm_radio", "a handheld communications radio with an antenna"),
    # Tools
    ("advanced_scanner", "a sleek handheld scanner with a glowing blue screen"),
    ("mining_beam", "a high-tech handheld mining laser beam emitter"),
    ("basic_drill", "a handheld mining power drill"),
    ("titanium_drill", "a heavy-duty titanium mining drill"),
    ("block_placer", "a construction tool projecting a glowing cube"),
    ("hand_scanner", "a small handheld scanner device"),
    # Weapons
    ("machete", "a steel machete blade with a grip handle"),
    ("vibro_knife", "a glowing high-tech vibro combat knife"),
    ("plasma_sword", "a glowing energy plasma sword"),
    ("gauss_pistol", "a sci-fi gauss pistol"),
    ("laser_pistol", "a sci-fi laser pistol"),
    ("plasma_blaster", "a heavy sci-fi plasma blaster rifle"),
    # Task 5 — refined metals / rare-earths (ingots + raw refined materials).
    ("gold_ingot", "a shiny yellow gold metal ingot bar"),
    ("silver_ingot", "a shiny metallic silver ingot bar"),
    ("aluminium_ingot", "a dull silvery aluminium ingot bar"),
    ("tin_ingot", "a dull pale silvery tin ingot bar"),
    ("nickel_ingot", "a greenish-silver nickel ingot bar"),
    ("cobalt_ingot", "a deep-blue cobalt ingot bar"),
    ("platinum_ingot", "a bright silvery-white platinum ingot bar"),
    ("lead_ingot", "a dull bluish-grey lead ingot bar"),
    ("zinc_ingot", "a bluish-silver zinc ingot bar"),
    ("tungsten_ingot", "a dark heavy tungsten ingot bar"),
    ("lithium", "a soft pinkish-white lithium metal nugget"),
    ("uranium", "a faintly glowing yellow-green uranium pellet"),
    ("neodymium", "a purple-grey rare-earth neodymium chunk"),
    ("sulfur", "a small heap of bright yellow sulfur powder"),
    # Task 5 — alloy / electronic components crafted from the new metals.
    ("steel", "a stack of brushed steel metal bars"),
    ("bronze", "a warm golden-brown bronze metal bar"),
    ("brass", "a bright yellow-gold brass metal bar"),
    ("circuit_board", "a green printed circuit board with gold traces and chips"),
    ("power_cell", "a glowing blue high-capacity power cell battery"),
    ("reactor_fuel", "a glowing green reactor fuel rod in a metal casing"),
    ("carbide", "a dark hard tungsten-carbide drill tip"),
    ("magnet", "a red horseshoe magnet with glowing poles"),
    ("light_alloy", "a lightweight brushed silver alloy plate"),
    ("radio_beacon", "a sci-fi radio beacon transmitter device, a slim metal mast on a tripod base with a glowing cyan antenna ring and a blinking status light"),
    ("speeder", "a sleek futuristic single-seat hover speeder vehicle seen at a three-quarter angle, smooth silver-blue aerodynamic hull with an open cockpit seat, swept side pods and two glowing cyan engine thrusters at the rear, hovering"),
    # Materialvielfalt — new tiers + functional alloy sinks (non-block items only).
    ("diamond", "a brilliant cut clear pale-blue diamond gemstone with sparkling facets"),
    ("diamond_drill", "a heavy-duty sci-fi mining drill tipped with a glittering blue industrial diamond bit"),
    ("polymer", "a coil of glossy dark synthetic polymer sheet, like industrial plastic"),
    ("biofuel", "a small glass flask of glowing green-amber biofuel liquid with a cork stopper"),
    ("bronze_gear", "a single toothed bronze mechanical gear cog with a warm golden-brown sheen"),
    ("brass_fitting", "a small polished brass pipe fitting coupling with threads"),
]

# Ship MODULES (builder UI). Space-view laser/tractor reuse ship_laser_basic / tractor_beam.
MODULES = [
    ("cockpit", "a spaceship cockpit pod with a windshield"),
    ("reactor", "a glowing ship fusion reactor core"),
    ("life_support", "a ship life-support oxygen recycler unit"),
    ("quarters", "a ship crew quarters bunk module"),
    ("medbay", "a ship medical bay pod with a red cross"),
    ("workshop", "a ship workshop crafting bench with tools"),
    ("cargo_hold_basic", "a small stack of ship cargo crates"),
    ("cargo_hold_1", "a large ship cargo container module"),
    ("refinery", "a ship ore refinery smelter module"),
    ("detoxifier", "a ship chemical detoxifier purifier module"),
    ("tractor_beam", "a ship tractor-beam emitter dish projecting a beam"),
    ("oxygen_generator", "a ship oxygen generator with a tank"),
    ("docking_module", "a ship docking clamp port ring"),
    ("jump_generator", "a glowing ship jump-drive warp generator coil"),
    ("radar_array", "a ship radar dish array"),
    ("hull_plating", "layered reinforced ship hull armor plating"),
    ("shield_generator", "a ship shield generator projecting an energy bubble"),
    ("asteroid_breaker", "a ship-mounted asteroid mining cannon"),
    ("ship_laser_basic", "a small ship laser turret"),
    ("ship_cannon_1", "a ship cannon turret"),
    ("laser_cannon_2", "a heavy ship laser cannon turret"),
]

ALL = [("item", k, d) for k, d in ITEMS] + [("item", k, d) for k, d in MODULES]


def main() -> None:
    ap = argparse.ArgumentParser(description="Generate content-styled inventory/module icons (Task 4).")
    ap.add_argument("--dry-run", action="store_true", help="list the manifest without calling the API")
    ap.add_argument("--only", metavar="KEY", help="generate just this one key (style approval)")
    ap.add_argument("--install", action="store_true", help="copy out/item_icons/*.png into the client Resources/icons")
    args = ap.parse_args()

    if args.install:
        CLIENT_ICONS.mkdir(parents=True, exist_ok=True)
        n = 0
        for png in sorted(OUT.glob("item_*.png")):
            shutil.copy2(png, CLIENT_ICONS / png.name)
            n += 1
        print(f"[install] copied {n} icons -> {CLIENT_ICONS}")
        return

    manifest = [(p, k, d) for (p, k, d) in ALL if not args.only or k == args.only]
    if args.only and not manifest:
        sys.exit(f"--only {args.only}: no such key in the manifest")

    print(f"[item-icons] {len(manifest)} icons")
    if args.dry_run:
        for i, (prefix, key, desc) in enumerate(manifest, 1):
            print(f"  {i:2d}. {prefix}_{key:22s} {desc}")
        return

    load_dotenv()
    key_env = os.environ.get("OPENAI_API_KEY")
    if not key_env:
        sys.exit("OPENAI_API_KEY is not set.")

    from openai import OpenAI
    from PIL import Image

    client = OpenAI(api_key=key_env)
    OUT.mkdir(parents=True, exist_ok=True)

    done = skipped = failed = 0
    fails: list[str] = []
    total = len(manifest)
    print(f"[cost] ~${0.005 * total:.3f} for {total} images (gpt-image-1-mini, low, 1024x1024)")

    for i, (prefix, key, desc) in enumerate(manifest, 1):
        out = OUT / f"{prefix}_{key}.png"
        if out.exists() and out.stat().st_size > 0:
            skipped += 1
            print(f"[{i}/{total}] {key}: skip (exists)")
            continue

        prompt = f"{STYLE}, of {desc}"
        ok = False
        for attempt in (1, 2):
            try:
                resp = client.images.generate(
                    model="gpt-image-1-mini", prompt=prompt, size="1024x1024",
                    quality="low", n=1, background="transparent")
                raw = base64.b64decode(resp.data[0].b64_json)
                img = Image.open(BytesIO(raw)).convert("RGBA").resize((128, 128), Image.LANCZOS)
                img.save(out)
                done += 1
                ok = True
                print(f"[{i}/{total}] {key}: ok ({out.stat().st_size} bytes)")
                break
            except Exception as exc:  # noqa: BLE001
                print(f"[{i}/{total}] {key}: attempt {attempt} failed: {exc}")
                time.sleep(2)

        if not ok:
            failed += 1
            fails.append(key)

    print(f"\n[item-icons] done. generated={done} skipped={skipped} failed={failed} of {total}")
    if fails:
        print("[item-icons] failed: " + ", ".join(fails))
    print("[item-icons] next: review out/item_icons, then `uv run gen_item_icons.py --install`")


if __name__ == "__main__":
    main()
