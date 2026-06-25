# Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
# SPDX-License-Identifier: AGPL-3.0-or-later
# This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
"""Generate the full M26 sound-effect set from ElevenLabs in one approved batch.

This is the *batch* counterpart to gen_sound.py (which stays one-file-per-run). It only runs when
invoked explicitly and the user has approved the whole set. Each entry is still a separate API call;
the run is **resumable** (existing non-empty out/<id>.mp3 files are skipped) and tolerant of single
failures (they are retried once, then recorded and skipped so the batch completes).

The catalogue mirrors docs/developer/SOUND_DESIGN.md. Outputs land in out/ (git-ignored); chosen files are
moved into the Unity client and logged in NOTICES.md afterwards.

Usage:
    uv run gen_batch.py            # generate everything missing
    uv run gen_batch.py --dry-run  # just list what would be generated
"""
from __future__ import annotations

import argparse
import os
import sys
import time
from pathlib import Path

from dotenv import load_dotenv

OUT = Path("out")


def build_catalogue() -> list[tuple[str, str, float, bool]]:
    """Returns (id, prompt, duration_seconds, loop) for every sound. Keep prompts ASCII."""
    s: list[tuple[str, str, float, bool]] = []

    # --- Player actions: mining (per material), place, loot, eat, heal ---
    s += [
        ("mine_stone",   "short punchy pickaxe hitting solid stone rock, dry mining impact", 1.0, False),
        ("mine_metal",   "short metallic clang mining a metal ore vein, sci-fi", 1.0, False),
        ("mine_crystal", "short crystalline shatter chime mining a glowing crystal", 1.0, False),
        ("mine_dirt",    "short soft dig scoop into dirt and gravel", 1.0, False),
        ("place_block",  "short solid thunk placing a heavy block, construction", 1.0, False),
        ("loot",         "short sci-fi container unlock and item pickup chime", 1.0, False),
        ("eat",          "short organic chewing and swallow gulp eating", 1.0, False),
        ("heal",         "short warm healing energy restore, soft rising chime", 1.0, False),
    ]

    # --- Tools & weapons ---
    s += [
        ("drill_loop",    "looping handheld mining drill motor whirring", 5.0, True),
        ("drill_impact",  "short drill bit grinding burst into hard rock", 1.0, False),
        ("weapon_scrap",  "short crude makeshift scrap pistol shot, rattly metallic pop with a small mechanical clack, low-tech and weak", 1.0, False),
        ("weapon_gauss",  "short kinetic gauss coilgun shot, sharp metallic snap", 1.0, False),
        ("weapon_laser",  "short sci-fi laser pistol zap shot", 1.0, False),
        ("weapon_plasma", "short heavy plasma blaster discharge, deep energy boom", 1.0, False),
        ("weapon_charge", "short sci-fi energy weapon charging whine before firing", 1.0, False),
        ("melee_swing",   "short fast whoosh of a blade swinging through air", 1.0, False),
        ("melee_hit",     "short blade impact thud hitting a target", 1.0, False),
        ("hurt_player",   "short human pain grunt taking damage, sci-fi suit impact", 0.7, False),
    ]

    # --- Movement: footsteps per surface + jump/land ---
    for sid, surf in [("rock", "hard rock"), ("sand", "soft sand"), ("metal", "metal deck"),
                      ("grass", "grass"), ("snow", "crunchy snow")]:
        s.append((f"step_{sid}", f"single footstep on {surf}, boot, close up", 1.0, False))
    s += [
        ("jump", "short suit servo jump effort with a light whoosh", 1.0, False),
        ("land", "short boots landing thud on the ground", 1.0, False),
    ]

    # --- Ship as a place: doors ---
    s += [
        ("door_open",  "sci-fi sliding airlock door opening with a pneumatic hiss", 1.5, False),
        ("door_close", "sci-fi sliding airlock door closing with a pneumatic clunk", 1.5, False),
    ]

    # --- Ship systems & space ---
    s += [
        ("engine_idle",       "looping low spaceship engine idle hum in space", 5.0, True),
        ("engine_thrust",     "looping spaceship engine thrust accelerating jet roar", 5.0, True),
        ("ship_launch",       "powerful spaceship liftoff launch roar rising", 2.5, False),
        ("ship_landing",      "spaceship descending and settling with landing thrusters", 2.5, False),
        ("hyperspace_charge", "sci-fi hyperdrive charging up rising energy whine", 2.5, False),
        ("hyperspace_jump",   "sci-fi hyperspace jump warp whoosh fast departure", 1.5, False),
        ("ship_weapon",       "spaceship cannon firing an energy bolt in space", 1.0, False),
        ("ship_hull_hit",     "heavy impact on a spaceship hull, metallic boom", 1.0, False),
        ("ship_shield_hit",   "energy shield absorbing a hit, shimmering buzz", 1.0, False),
        ("ship_destroyed",    "large spaceship exploding with debris, space boom", 2.0, False),
        ("asteroid_break",    "asteroid cracking and shattering into rubble", 1.5, False),
    ]

    # --- Creatures: 6 voice banks (size x disposition) x 5 states; pitch-shifted per creature in game ---
    sizes = [("small", "small"), ("medium", "medium-sized"), ("large", "huge hulking")]
    disps = [("calm", "docile gentle"), ("hostile", "vicious aggressive")]
    states = [
        ("idle",   "idle murmur chirp"),
        ("alert",  "startled alert call"),
        ("attack", "aggressive attack lunge snarl"),
        ("hurt",   "pained yelp when struck"),
        ("die",    "dying death groan"),
    ]
    for size_id, size_tx in sizes:
        for disp_id, disp_tx in disps:
            for st_id, st_tx in states:
                sid = f"creature_{size_id}_{disp_id}_{st_id}"
                prompt = f"{size_tx} {disp_tx} alien creature, {st_tx}, organic monster sound, no music"
                s.append((sid, prompt, 1.0, False))

    # --- NPCs: human + alien, NON-VERBAL only (no speech) ---
    npc_states = [
        ("idle",  "quiet idle murmur"),
        ("greet", "friendly greeting acknowledgement"),
        ("ack",   "short affirmative grunt"),
        ("trade", "pleased trade-confirm hum"),
        ("alert", "alarmed startled call"),
    ]
    for kind, ktx in [("human", "human"), ("alien", "alien")]:
        for st_id, st_tx in npc_states:
            sid = f"npc_{kind}_{st_id}"
            prompt = f"{ktx} {st_tx}, non-verbal vocalization only, grunt or chirp, absolutely no words or speech"
            s.append((sid, prompt, 1.0, False))

    # --- Weather & environment ambience (loops + thunder one-shots) ---
    s += [
        ("wind_light",  "gentle wind breeze ambience", 5.0, True),
        ("wind_strong", "strong howling wind ambience", 5.0, True),
        ("rain_loop",   "steady rain falling ambience", 5.0, True),
        ("storm_loop",  "heavy thunderstorm rain and wind ambience", 5.0, True),
        ("thunder_1",   "distant thunder rumble", 2.0, False),
        ("thunder_2",   "close thunder crack and boom", 2.0, False),
        ("thunder_3",   "long rolling thunder", 2.5, False),
        ("amb_forest",  "alien forest ambience with birds and insects", 6.0, True),
        ("amb_desert",  "barren desert wind ambience", 6.0, True),
        ("amb_ice",     "cold icy tundra wind ambience", 6.0, True),
        ("amb_lava",    "bubbling lava volcanic ambience", 6.0, True),
        ("amb_swamp",   "swamp bog ambience with frogs and bubbles", 6.0, True),
        ("amb_cave",    "dark cave dripping water echo ambience", 6.0, True),
        ("lava_bubble", "lava bubbling and hissing loop", 5.0, True),
        ("water_shore", "water lapping against a shore loop", 5.0, True),
        ("night_amb",   "alien night ambience with crickets and distant calls", 6.0, True),
    ]

    return s


def main() -> None:
    ap = argparse.ArgumentParser(description="Generate the full M26 SFX set (approved batch).")
    ap.add_argument("--dry-run", action="store_true", help="list the catalogue without calling the API")
    args = ap.parse_args()

    catalogue = build_catalogue()
    print(f"[batch] catalogue: {len(catalogue)} sounds")

    if args.dry_run:
        for i, (sid, prompt, dur, loop) in enumerate(catalogue, 1):
            print(f"  {i:3d}. {sid:28s} {'loop ' if loop else '     '}{dur:>4}s  {prompt}")
        return

    load_dotenv()
    key = os.environ.get("ELEVENLABS_API_KEY")
    if not key:
        sys.exit("ELEVENLABS_API_KEY is not set (put it in .env or the environment).")

    from elevenlabs.client import ElevenLabs

    client = ElevenLabs(api_key=key)
    OUT.mkdir(parents=True, exist_ok=True)

    done = skipped = failed = 0
    failures: list[str] = []
    total = len(catalogue)

    for i, (sid, prompt, dur, loop) in enumerate(catalogue, 1):
        out = OUT / f"{sid}.mp3"
        if out.exists() and out.stat().st_size > 0:
            skipped += 1
            print(f"[{i}/{total}] {sid}: skip (exists)")
            continue

        kwargs = dict(text=prompt, prompt_influence=0.3,
                      model_id="eleven_text_to_sound_v2", output_format="mp3_44100_128")
        if dur > 0:
            kwargs["duration_seconds"] = dur
        if loop:
            kwargs["loop"] = True

        ok = False
        for attempt in (1, 2):
            try:
                audio = client.text_to_sound_effects.convert(**kwargs)
                data = audio if isinstance(audio, (bytes, bytearray)) else b"".join(audio)
                out.write_bytes(data)
                done += 1
                ok = True
                print(f"[{i}/{total}] {sid}: ok ({len(data)} bytes)")
                break
            except Exception as exc:  # noqa: BLE001 - keep the batch going
                print(f"[{i}/{total}] {sid}: attempt {attempt} failed: {exc}")
                time.sleep(2)

        if not ok:
            failed += 1
            failures.append(sid)

    print(f"\n[batch] done. generated={done} skipped={skipped} failed={failed} of {total}")
    if failures:
        print("[batch] failed ids: " + ", ".join(failures))
        print("[batch] re-run to retry just the failures (existing files are skipped).")


if __name__ == "__main__":
    main()
