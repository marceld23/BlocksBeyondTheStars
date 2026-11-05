# Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
# SPDX-License-Identifier: AGPL-3.0-or-later
# This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
"""The sound-immersion clip set (#3237–#3249): every prompt, so a clip can be re-generated later.

Calls ``tools/ai-assets/gen_sound.py`` once per clip (that tool stays one-file-per-run). Resumable: a clip whose
``tools/ai-assets/out/sound-immersion/<id>.mp3`` already exists is skipped. Afterwards the clips are levelled and copied
into the client with ``level_clips.py --import``.

Usage (from the repository root):
    uv run --project tools/ai-assets python tools/audio/sound_immersion_batch.py [--only id,id] [--dry-run]
"""
from __future__ import annotations

import argparse
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
AI = ROOT / "tools" / "ai-assets"
OUT = AI / "out" / "sound-immersion"

ONE = "close, clean, no voice, no music"
STEP = "one single short step, close, dry, no echo, no other sounds"

# (id, prompt, duration seconds, loop, prompt influence)
CLIPS: list[tuple[str, str, float, bool, float]] = [
    # --- footsteps: extra takes for the five old surfaces + five new surfaces (#3239) ---
    ("step_rock_2", f"a single footstep of a boot on hard rock, a dry crunchy step, {STEP}", 0.5, False, 0.6),
    ("step_rock_3", f"a single boot footstep on rough stone ground with a little grit, {STEP}", 0.5, False, 0.6),
    ("step_grass_2", f"a single footstep on soft grass, a gentle swish and soft thud, {STEP}", 0.5, False, 0.6),
    ("step_grass_3", f"a single footstep in a green meadow, soft grass rustle, {STEP}", 0.5, False, 0.6),
    ("step_sand_2", f"a single footstep on dry sand, a soft crunchy shuffle, {STEP}", 0.5, False, 0.6),
    ("step_sand_3", f"a single footstep on a sandy beach, a soft sandy crunch, {STEP}", 0.5, False, 0.6),
    ("step_snow_2", f"a single footstep in fresh snow, a soft squeaky crunch, {STEP}", 0.5, False, 0.6),
    ("step_snow_3", f"a single footstep on packed snow, a crisp crunch, {STEP}", 0.5, False, 0.6),
    ("step_metal", f"a single footstep of a boot on a metal spaceship deck, a short hollow metallic clank, {STEP}", 0.5, False, 0.6),
    ("step_metal_2", f"a single boot footstep on a steel floor plate inside a spaceship, a soft metallic thud, {STEP}", 0.5, False, 0.6),
    ("step_metal_3", f"a single footstep on a metal deck, a light ringing clank, {STEP}", 0.5, False, 0.6),
    ("step_wood", f"a single footstep of a boot on wooden floor planks, a hollow wooden knock, {STEP}", 0.5, False, 0.6),
    ("step_wood_2", f"a single footstep on an old wooden plank floor with a faint creak, {STEP}", 0.5, False, 0.6),
    ("step_wood_3", f"a single footstep on a wooden bridge, a soft hollow thump, {STEP}", 0.5, False, 0.6),
    ("step_leaves", f"a single footstep on dry forest floor with leaves and twigs, a rustling crunch, {STEP}", 0.5, False, 0.6),
    ("step_leaves_2", f"a single footstep on fallen autumn leaves, a crisp leafy rustle, {STEP}", 0.5, False, 0.6),
    ("step_leaves_3", f"a single footstep on a mossy forest path with small twigs, {STEP}", 0.5, False, 0.6),
    ("step_water", f"a single footstep splashing in a shallow puddle of water, a short wet splash, {STEP}", 0.5, False, 0.6),
    ("step_water_2", f"a single footstep in ankle-deep water, a small wet slosh, {STEP}", 0.5, False, 0.6),
    ("step_water_3", f"a single footstep in a muddy puddle, a short wet splat, {STEP}", 0.5, False, 0.6),
    ("step_grate", f"a single footstep of a boot on a metal grate walkway, a short rattling metallic clang, {STEP}", 0.5, False, 0.6),
    ("step_grate_2", f"a single footstep on a steel mesh catwalk, a light rattle, {STEP}", 0.5, False, 0.6),
    ("step_grate_3", f"a single footstep on a metal grid floor, a hollow rattling clunk, {STEP}", 0.5, False, 0.6),
    ("step_crystal", f"a single footstep on hard glassy crystal ground, a bright glassy clink and crunch, {STEP}", 0.5, False, 0.6),
    ("step_crystal_2", f"a single footstep on crystal shards, a delicate glassy crunch, {STEP}", 0.5, False, 0.6),
    ("step_crystal_3", f"a single footstep on smooth crystal, a soft ringing glassy tap, {STEP}", 0.5, False, 0.6),
    ("land_heavy", "a heavy landing after a high jump: boots hitting the ground hard with a deep thud, a little dust and "
                   "a short exhaled grunt of effort, not painful, game sound", 0.8, False, 0.5),
    # --- water, breath, suit (#3240, #3241) ---
    ("water_splash_in", f"a person jumping into a lake, one splash of water with bubbles, short, {ONE}", 1.0, False, 0.5),
    ("water_splash_out", f"a person climbing out of water onto the shore, water dripping and a short splash, {ONE}", 1.0, False, 0.5),
    ("swim_stroke", f"one gentle swimming stroke in calm water, a soft splash and swirl of water by an arm, short, {ONE}", 0.7, False, 0.5),
    ("swim_stroke_2", f"one calm breaststroke swimming stroke, water swishing past, short, {ONE}", 0.7, False, 0.5),
    ("underwater_bubbles", "seamless loop of quiet underwater ambience, a muffled low rumble with soft occasional bubbles "
                           "rising, calm, no music", 6.0, True, 0.4),
    ("surface_gasp", "a person surfacing from under water and taking one relieved deep breath of air, a short inhale with "
                     "water dripping, gentle, not scary, no words", 1.0, False, 0.5),
    ("breath_exhale", "one calm relaxed exhale through the mouth after running, a soft single breath out, close "
                      "microphone, gentle, no voice, no words", 0.8, False, 0.5),
    ("breath_exhale_2", "a single soft tired sigh of breath after exercise, gentle and calm, close microphone, no voice, "
                        "no words", 0.8, False, 0.5),
    ("breath_quick", "two quick soft breaths in and out, slightly out of breath, gentle and calm, close microphone, no "
                     "voice, no words", 1.0, False, 0.5),
    ("heartbeat_soft", "one soft muffled heartbeat, a single gentle lub-dub, low and warm, quiet, no music", 0.8, False, 0.5),
    ("suit_room_tone", "seamless loop of the quiet inside of a space suit helmet: a soft steady air recirculation hiss and "
                       "a faint low electronic hum, calm, no beeps, no voice", 8.0, True, 0.4),
    ("suit_thruster", "a short soft puff of a small gas thruster on a space suit, one quick pressurised hiss burst, clean, "
                      "sci-fi, no voice", 0.5, False, 0.5),
    ("suit_thruster_2", "a quick small cold gas jet puff from an astronaut backpack thruster, short hiss, clean, sci-fi",
     0.5, False, 0.5),
    # --- creature foley + voice material (#3242, #3246) ---
    ("wing_flap", f"a single wing flap of a medium sized flying creature, one soft whoosh of wings beating the air, {ONE}",
     0.5, False, 0.5),
    ("wing_flap_2", f"one leathery wing beat of a flying animal, a soft flappy whoosh, {ONE}", 0.5, False, 0.5),
    ("slither_loop", "seamless loop of a snake-like creature slithering over sand and stones, a soft continuous scraping "
                     "and rustling, quiet, close", 4.0, True, 0.4),
    ("creature_call_ethereal", "a soft ethereal glassy hum-song of a floating jellyfish-like alien creature, a gentle "
                               "rising and falling tone like a wet finger on a wine glass, calm, magical, no beat", 1.5, False, 0.5),
    ("creature_call_ethereal_2", "a gentle ethereal humming call of a glowing alien creature, a soft glassy whale-like "
                                 "tone, calm and dreamy, short, no beat", 1.5, False, 0.5),
    ("creature_call_chime_drone", "a gentle alien creature call made of a low soft drone with shimmering bell-like "
                                  "overtones, glowing and peaceful, short, no music", 1.5, False, 0.5),
    ("creature_call_buzz", "a short insect-like alien creature call, a quick wing buzz and a dry chirp, like a giant "
                           "cicada, close, short", 1.0, False, 0.5),
    ("creature_call_buzz_2", "a short buzzing call of a big friendly alien beetle, a whirring wing buzz, close, short",
     1.0, False, 0.5),
    ("creature_call_rattle", "a short dry rattling chirp of a many-legged alien insect, clicking mandibles and a rattle, "
                             "close, short", 1.0, False, 0.5),
    ("creature_call_gibber_squeal", "a small cute alien creature happily squealing gibberish, a short high excited babble, "
                                    "like a cartoon critter, no real words", 1.2, False, 0.5),
    ("creature_call_click_hiss", "a spider-like alien creature: a short series of dry clicks ending in a soft hiss, close, "
                                 "short", 1.0, False, 0.5),
    ("creature_call_echo_click", "a cave creature echolocation call: a rapid series of sharp wet tongue clicks, close, "
                                 "short", 1.0, False, 0.5),
    ("creature_call_flutter", "a flying alien creature call: a quick fluttering trill of wings and a soft chirp, close, "
                              "short", 1.0, False, 0.5),
    # --- ship as a place, flight (#3247, #3248) ---
    ("rain_on_hull", "seamless loop of steady rain drumming on the metal roof of a small spaceship cabin, heard from inside, "
                     "cosy, pattering drops on metal, no thunder, no music", 10.0, True, 0.4),
    ("ship_cooling_tick", "hot metal of a landed spaceship engine cooling down: a few soft metallic ticks and pings, slow "
                          "and sparse, quiet, close", 1.5, False, 0.5),
    ("ship_cooling_tick_2", "a cooling metal engine nozzle: two or three small metallic pings and clicks, quiet, close",
     1.5, False, 0.5),
    ("ship_exterior_idle", "seamless loop of a parked small spaceship idling outside: a soft steady low electric hum with "
                           "a quiet fan whir, calm, no engine roar", 6.0, True, 0.4),
    ("reactor_hum", "seamless loop of a small spaceship reactor: a deep calm pulsing electric hum with a slow rhythmic "
                    "throb, sci-fi, steady, no alarms", 6.0, True, 0.4),
    ("life_support_cycle", "seamless loop of spaceship life support: soft air vents breathing, a gentle airflow whoosh that "
                           "rises and falls slowly, calm, no beeps", 8.0, True, 0.4),
    ("cargo_creak", "a single soft creak of a metal cargo crate shifting inside a spaceship, a short groan of metal and a "
                    "light thump, quiet", 1.2, False, 0.5),
    ("cargo_creak_2", "a metal shelf with cargo rattling briefly inside a spaceship, a soft clank and creak, quiet",
     1.2, False, 0.5),
    ("hull_creak", "the metal hull of a damaged spaceship groaning under stress, one long deep metallic creak, not scary, "
                   "short", 1.5, False, 0.5),
    ("hull_creak_2", "a spaceship hull plate bending with a low metallic moan and a small pop, not scary, short",
     1.5, False, 0.5),
    ("engine_boost", "seamless loop of a sci-fi spaceship afterburner boost: a powerful roaring jet thrust with a high "
                     "whine, steady, no explosions", 4.0, True, 0.4),
    ("rcs_puff", "a short sharp hiss of a small spaceship maneuvering thruster firing once, a quick pneumatic gas puff, "
                 "clean, sci-fi", 0.5, False, 0.5),
    ("rcs_puff_2", "one quick burst of a spacecraft attitude control jet, a crisp short gas hiss, clean, sci-fi",
     0.5, False, 0.5),
    ("cockpit_ambience", "seamless loop of a calm spaceship cockpit: quiet soft computer chirps and a gentle electronic "
                         "hum, very subtle, no alarms, no voices, no music", 10.0, True, 0.4),
    ("shield_recharge", "a sci-fi energy shield recharging: a soft rising electric hum that swells and settles with a "
                        "gentle shimmer, friendly, short", 1.5, False, 0.5),
    # --- re-takes of clips too quiet to level by gain alone (#3237) ---
    ("climb_grab", f"a gloved hand firmly grabbing a rock ledge, one short scrape and grip, {ONE}", 0.6, False, 0.6),
    ("climb_slide", f"a climber slowly sliding down a rock wall, gloves scraping over stone with small pebbles falling, "
                    f"short, {ONE}", 1.2, False, 0.5),
    ("drizzle_loop", "seamless loop of light drizzle rain, gentle fine raindrops falling on grass and leaves, soft and "
                     "calm, no thunder, no wind", 8.0, True, 0.4),
    ("weapon_scrap", "a short crude makeshift scrap pistol shot, a rattly metallic pop with a small mechanical clack, "
                     "low-tech, punchy but small, game sound", 1.0, False, 0.5),
    ("mine_stone", "a short punchy pickaxe hit breaking solid stone, a dry rocky crack with small crumbling debris, close, "
                   "game sound", 1.0, False, 0.5),
]


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--only", default="", help="comma-separated ids")
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args()
    only = {s for s in args.only.split(",") if s}
    OUT.mkdir(parents=True, exist_ok=True)
    failed = []
    for cid, prompt, dur, loop, infl in CLIPS:
        if only and cid not in only:
            continue
        out = OUT / f"{cid}.mp3"
        if out.exists() and out.stat().st_size > 0 and not only:
            continue
        cmd = [sys.executable, str(AI / "gen_sound.py"), "--prompt", prompt, "--out", str(out),
               "--duration", str(dur), "--influence", str(infl)]
        if loop:
            cmd.append("--loop")
        print(f"[gen] {cid}", flush=True)
        if args.dry_run:
            continue
        for attempt in range(2):
            r = subprocess.run(cmd, cwd=AI, capture_output=True, text=True)
            if r.returncode == 0 and out.exists():
                break
            print(f"[retry] {cid}: {r.stderr.strip()[-300:]}", flush=True)
        else:
            failed.append(cid)
    print(f"[done] failed: {failed}", flush=True)


if __name__ == "__main__":
    main()
