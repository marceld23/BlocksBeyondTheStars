# Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
# SPDX-License-Identifier: AGPL-3.0-or-later
# This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
"""The feel pass's clip set (docs/developer/GAME_FEEL.md §3): every prompt, so a clip can be re-generated later.

Calls ``tools/ai-assets/gen_sound.py`` once per clip (that tool stays one-file-per-run). Resumable: a clip whose
``tools/ai-assets/out/aaa-feel/<id>.mp3`` already exists is skipped unless it is named in ``--only``. Afterwards the
clips are levelled and copied into the client with ``level_clips.py --import``.

Two ids differ between this list and the game: the takes kept from the second round's ``_alt`` prompts ship as the
family's third take — ``place_dirt_2_alt`` is ``place_dirt_3`` and ``weapon_plasma_2_alt`` is ``weapon_plasma_3``.

Usage (from the repository root):
    uv run --project tools/ai-assets python tools/audio/aaa_feel_batch.py [--only id,id] [--dry-run]
"""
from __future__ import annotations

import argparse
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
AI = ROOT / "tools" / "ai-assets"
OUT = AI / "out" / "aaa-feel"

ONE = "close, clean, dry, no echo, no voice, no music"
UI = "soft holographic sci-fi user interface sound, gentle, very short, clean, no voice, no music"
EMOTE = "short, close, clean, dry, no echo, no spoken words, no voice, no music, for a kid-friendly video game"
STING = "short instrumental musical sting for a friendly sci-fi adventure game, no voice, no drums, ends cleanly"

# (id, group, prompt, duration seconds, prompt influence)
CLIPS: list[tuple[str, str, str, float, float]] = [
    # --- mining: two more takes per material class ---
    ("mine_stone_2", "mine", f"one hit of a mining tool breaking a block of stone, a dry crack and a few small rock chips falling, {ONE}", 0.6, 0.5),
    ("mine_stone_3", "mine", f"a chunk of rock cracking and crumbling apart after one tool hit, a short gritty crunch, {ONE}", 0.6, 0.5),
    ("mine_metal_2", "mine", f"a mining tool striking and breaking a block of metal ore, a short bright metallic clank with a dull crack, {ONE}", 0.6, 0.5),
    ("mine_metal_3", "mine", f"one short heavy metallic clunk as a metal plate is knocked loose, a slight ring, {ONE}", 0.6, 0.5),
    ("mine_crystal_2", "mine", f"a crystal block shattering after one hit, a short bright glassy chime and tinkling shards, gentle, {ONE}", 0.7, 0.5),
    ("mine_crystal_3", "mine", f"a small crystal breaking, a delicate glassy crack with a few sparkling shard tinkles, {ONE}", 0.7, 0.5),
    ("mine_dirt_2", "mine", f"a tool digging out a block of soil, a short soft earthy thump and crumble, {ONE}", 0.5, 0.5),
    ("mine_dirt_3", "mine", f"a clump of dry earth breaking apart, a soft crumbly thud, {ONE}", 0.5, 0.5),
    # --- placing a block, by material (two takes each) ---
    ("place_stone", "place", f"a heavy stone block being set down on stone, one short solid thud with a slight gritty scrape, {ONE}", 0.5, 0.6),
    ("place_stone_2", "place", f"a stone brick set into place, one short dull knock, {ONE}", 0.5, 0.6),
    ("place_metal", "place", f"a metal block locking into place on a metal floor, one short solid clank with a tiny mechanical click, {ONE}", 0.5, 0.6),
    ("place_metal_2", "place", f"a steel plate set down on steel, one short muted clunk, {ONE}", 0.5, 0.6),
    ("place_crystal", "place", f"a crystal block set down gently, one short glassy clink with a soft ring, no breaking, {ONE}", 0.5, 0.6),
    ("place_crystal_2", "place", f"a small glassy crystal placed on stone, one bright short tick, no breaking, {ONE}", 0.5, 0.6),
    ("place_dirt", "place", f"a block of soil dropped into place, one short soft earthy pat, {ONE}", 0.5, 0.6),
    # Round 2 (2026-11-17): the first take ("packed earth pressed down by hand, one soft muffled thump") was rejected
    # at the listening check; both new takes were kept (the `_alt` one ships as `place_dirt_3`).
    ("place_dirt_2", "place", f"a clump of damp soil dropped onto soft ground, one short earthy thud with a little crumble, {ONE}", 0.5, 0.6),
    ("place_dirt_2_alt", "place", f"a shovelful of loose earth tipped onto the ground, one short soft gritty plop, {ONE}", 0.5, 0.6),
    ("place_wood", "place", f"a wooden block set down on wood, one short hollow wooden knock, {ONE}", 0.5, 0.6),
    ("place_wood_2", "place", f"a wooden plank placed on a wooden floor, one short warm wooden tap, {ONE}", 0.5, 0.6),
    ("place_glass", "place", f"a pane of glass set carefully into a frame, one short soft glassy tap, no breaking, {ONE}", 0.5, 0.6),
    ("place_glass_2", "place", f"a glass block placed down, one short clear glass clink, no breaking, {ONE}", 0.5, 0.6),
    # --- melee ---
    ("melee_swing_2", "combat", f"a single fast swing of a blade through the air, one short whoosh, {ONE}", 0.5, 0.5),
    ("melee_swing_3", "combat", f"one quick swish of a staff swung through the air, a short airy whoosh, {ONE}", 0.5, 0.5),
    ("melee_hit_2", "combat", f"a blunt tool hitting a padded target, one short punchy thud, cartoon game hit, not violent, {ONE}", 0.5, 0.5),
    ("melee_hit_3", "combat", f"one short solid smack impact, a punchy video game hit sound, not violent, {ONE}", 0.5, 0.5),
    # --- weapons: one more take each ---
    ("weapon_laser_2", "combat", f"a single short sci-fi laser shot, a bright pew with a quick falling pitch, toy-like, {ONE}", 0.6, 0.5),
    # Round 2 (2026-11-17): the first take ("a short soft bassy whomp with a fizzing tail") was rejected at the
    # listening check; both new takes were kept (the `_alt` one ships as `weapon_plasma_3`).
    ("weapon_plasma_2", "combat", f"one shot of a sci-fi plasma blaster, a short punchy burst of energy with a quick sizzling crackle, bright and clean, {ONE}", 0.8, 0.5),
    ("weapon_plasma_2_alt", "combat", f"a single plasma bolt fired, a short round energy thump with a hot hissing tail, sci-fi game weapon, {ONE}", 0.8, 0.5),
    ("weapon_gauss_2", "combat", f"a single gauss rifle shot, a short sharp electromagnetic snap with a low thump, sci-fi, {ONE}", 0.7, 0.5),
    ("weapon_scrap_2", "combat", f"a makeshift scrap launcher firing once, a short rattly metallic pop and clatter, {ONE}", 0.7, 0.5),
    # --- body ---
    ("land_2", "body", f"boots landing on firm ground after a small jump, one short soft thud, {ONE}", 0.5, 0.6),
    ("land_3", "body", f"a light landing on the ground on two feet, one short muffled thump with a tiny scuff, {ONE}", 0.5, 0.6),
    ("jump_2", "body", f"a person pushing off the ground to jump, a short soft scuff of boots and a rustle of fabric, {ONE}", 0.5, 0.6),
    # --- hit confirmation ---
    ("hit_confirm", "combat", f"a very short crisp tick, a tiny bright click that confirms a hit in a video game, {ONE}", 0.5, 0.6),
    ("hit_confirm_defeat", "combat", f"a short satisfying two-tone pop that confirms a defeated target in a video game, bright and friendly, {ONE}", 0.5, 0.6),
    # --- user interface ---
    ("ui_click", "ui", f"a tiny soft click with a faint glassy tone, a button press, {UI}", 0.5, 0.6),
    # NONE of the `ui_hover*` takes in this file is in the game. The hover sound took four rounds of listening
    # (2026-11-17): this take was too bright and too present, darkened and cut short it read as hammering, the breath
    # of air `_d` was not it either, and of the "technical, not shrill" round a SYNTHESISED blip was chosen —
    # `tools/audio/synth_ui_hover.py` builds the clip that ships. The prompts stay as the record of what was tried.
    ("ui_hover", "ui", f"a barely audible very soft high tick, the pointer moving over a button, {UI}", 0.5, 0.6),
    ("ui_hover_b", "ui", "one very soft low dull muted tick, like a fingertip tapping felt, warm, dark, quiet, very short, "
                         "no high frequencies, no ring, no chime, no voice, no music", 0.5, 0.6),
    ("ui_hover_c", "ui", "one tiny soft low-pitched rounded 'pup' sound, a gentle muted wooden tap far away, dark and "
                         "unobtrusive, very short, no brightness, no ring, no voice, no music", 0.5, 0.6),
    # Round 3: the darkened short tick read as hammering when the pointer crossed several buttons. A hover sound must
    # have NO attack: these three fade in instead of striking. `_d` was in the game for one build.
    ("ui_hover_d", "ui", "a very soft tiny breath of air, one gentle short airy 'fff' swish that fades in and out smoothly, "
                         "no click, no tap, no knock, no impact, no tone, quiet, no voice, no music", 0.5, 0.6),
    ("ui_hover_e", "ui", "one tiny soft warm rounded 'bloop', like a small water droplet, a gentle mid-pitched sine tone "
                         "that swells and fades smoothly, no click, no knock, no impact, quiet, no voice, no music", 0.5, 0.6),
    ("ui_hover_f", "ui", "a faint soft glassy hum, one gentle mid-pitched note that fades in and fades out smoothly, like a "
                         "holographic button softly lighting up, quiet, not bright, not sharp, no click, no knock, no voice, "
                         "no music", 0.5, 0.6),
    # Round 4: the breath of air did not please either — asked for "technical, not shrill". Three generated
    # candidates; the one that was chosen is none of them but a synthesised blip (tools/audio/synth_ui_hover.py).
    ("ui_hover_h", "ui", "one tiny soft electronic relay tick from a spaceship control panel, a single short muted mechanical "
                         "click, mid-pitched, dry, quiet, not sharp, not bright, no voice, no music", 0.5, 0.6),
    ("ui_hover_i", "ui", "one very short soft digital blip from a sci-fi computer terminal, a single low-mid pitched rounded "
                         "electronic 'bip', dry, quiet, not shrill, not bright, no voice, no music", 0.5, 0.6),
    ("ui_hover_j", "ui", "one tiny short servo motor zip, a soft quick mechanical whirr of a small robot part moving, very "
                         "short, muted, mid-pitched, dry, quiet, not shrill, no voice, no music", 0.5, 0.6),
    ("ui_open", "ui", f"a short soft rising shimmer as a holographic window opens, {UI}", 0.6, 0.6),
    ("ui_close", "ui", f"a short soft falling shimmer as a holographic window closes, {UI}", 0.6, 0.6),
    ("ui_tab", "ui", f"a light soft double tick, switching to another tab, {UI}", 0.5, 0.6),
    ("ui_confirm", "ui", f"a short pleasant two-note rising chime, confirming a choice, {UI}", 0.6, 0.6),
    ("ui_back", "ui", f"a short soft low descending blip, going back one step, {UI}", 0.5, 0.6),
    ("ui_error", "ui", f"a short soft low double buzz, a gentle 'not possible' tone, not harsh, {UI}", 0.6, 0.6),
    ("ui_success", "ui", f"a short bright friendly three-note rising chime, a task completed, {UI}", 0.8, 0.6),
    ("ui_toast", "ui", f"a soft single bell-like ping, a small notification, {UI}", 0.6, 0.6),
    ("tech_unlock", "ui", "a short uplifting sparkling chime with a soft whoosh, a new technology unlocked, sci-fi, friendly, "
                          "no voice, no music", 1.2, 0.5),
    # --- musical accents ---
    ("sting_new_planet", "sting", f"a wondrous swell of a soft synth pad with a few bright bell notes, discovering a new planet, hopeful, {STING}", 3.0, 0.5),
    ("sting_site_found", "sting", f"two low warm notes and a shimmering high tone, finding an ancient ruin, curious, not scary, {STING}", 3.0, 0.5),
    ("sting_blueprint", "sting", f"a quick ascending arpeggio on a soft synth, an idea clicking into place, clever and light, {STING}", 2.0, 0.5),
    ("sting_achievement", "sting", f"a cheerful little fanfare of bright synth brass and bells, proud and friendly, {STING}", 2.5, 0.5),
    ("sting_home", "sting", f"a gentle resolving chord on soft synth and piano, coming home safe, warm, {STING}", 3.0, 0.5),
    # --- emotes (2026-11-17): all accepted at the listening check. Cheer and bow ship both second-round takes: the
    # `_alt` one is the family's take 2 (`emote_cheer_2`, `emote_bow_2`). `emote_bow` was low-passed at 5 kHz with
    # ffmpeg before the import (a quiet rustle that needs +20 dB). ---
    ("emote_wave", "emote", f"an arm in a soft space suit waving twice, two light quick fabric swishes, friendly, {EMOTE}", 0.8, 0.6),
    ("emote_nod", "emote", f"a tiny friendly two-note rising blip from a space suit, like a wordless 'yes', soft and round, {EMOTE}", 0.6, 0.6),
    # Round 2: the first take ("a quick fabric whoosh with a short bright sparkling rising chime") was too shrill.
    ("emote_cheer", "emote", f"both arms thrown up in a space suit, a quick soft fabric whoosh with a short warm mellow rising two-note tone, round and low-pitched, happy, no high-pitched sounds, no sparkle, {EMOTE}", 1.0, 0.5),
    ("emote_cheer_alt", "emote", f"a short soft happy 'whoop' made by a small synth, one warm rounded rising note with a gentle fabric rustle, mellow, not shrill, no bells, {EMOTE}", 1.0, 0.5),
    ("emote_point", "emote", f"an arm stretched out to point at something, one short light fabric swish with a tiny soft tick at the end, {EMOTE}", 0.6, 0.6),
    ("emote_head_shake", "emote", f"a tiny friendly two-note falling blip from a space suit, like a wordless 'no', soft and round, not sad, {EMOTE}", 0.6, 0.6),
    ("emote_dance", "emote", f"a short bouncy playful little synth riff of four notes with soft suit rustles in rhythm, a happy dance, {EMOTE}", 1.6, 0.5),
    ("emote_clap", "emote", f"four quick muffled hand claps in thick padded gloves, soft and dull, cheerful applause by one person, {EMOTE}", 1.0, 0.6),
    ("emote_sit", "emote", f"someone in a padded space suit sitting down on the ground, a soft fabric rustle and one gentle low thump, {EMOTE}", 0.9, 0.6),
    # Round 2: the first take ("one slow fabric swish down and back up, gentle") was too harsh.
    ("emote_bow", "emote", f"someone in a thick padded suit bowing politely, one slow muffled fabric rustle, dull, warm and low, no hiss, no bright or sharp sounds, {EMOTE}", 1.0, 0.6),
    ("emote_bow_alt", "emote", f"a clearly audible warm low two-note synth tone, first falling then rising, like a polite bow, round and mellow, with a muffled cloth rustle, not bright, {EMOTE}", 1.0, 0.6),
]


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--only", default="", help="comma-separated ids (re-rolls them even if they exist)")
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args()
    only = {s for s in args.only.split(",") if s}
    OUT.mkdir(parents=True, exist_ok=True)
    made, failed = 0, []
    for cid, _group, prompt, dur, infl in CLIPS:
        if only and cid not in only:
            continue
        out = OUT / f"{cid}.mp3"
        if out.exists() and out.stat().st_size > 0 and not only:
            continue
        cmd = [sys.executable, str(AI / "gen_sound.py"), "--prompt", prompt, "--out", str(out),
               "--duration", str(dur), "--influence", str(infl)]
        print(f"[gen] {cid} ({dur}s)", flush=True)
        if args.dry_run:
            continue
        for _attempt in range(2):
            r = subprocess.run(cmd, cwd=AI, capture_output=True, text=True)
            if r.returncode == 0 and out.exists() and out.stat().st_size > 0:
                made += 1
                break
            print(f"[retry] {cid}: {r.stderr.strip()[-300:]}", flush=True)
        else:
            failed.append(cid)
    print(f"[done] made: {made}, failed: {failed}", flush=True)


if __name__ == "__main__":
    main()
