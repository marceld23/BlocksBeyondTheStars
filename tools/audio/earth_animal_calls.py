# Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
# SPDX-License-Identifier: AGPL-3.0-or-later
# This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
"""The Earth animals' recorded calls (#3973-#3975): every prompt, so a clip can be re-rolled.

Every prompt is recorded here so a clip can be re-rolled. Calls ``tools/ai-assets/gen_sound.py`` once per clip (that
tool stays one-file-per-run). Resumable: a clip whose ``out/<id>.mp3`` already exists is skipped unless it is named in
``--only``. Nothing lands in the game: the output folders sit next to this file, inside the git-ignored ``analysis/``
tree. ``--listen`` (run automatically after a generation) writes peak-matched copies into ``listen/`` for the
sign-off page and prints each clip's length and level, so a silent or clipped take is spotted without listening.

Usage (from the repository root):
    uv run --project tools/ai-assets python tools/audio/earth_animal_calls.py [--only id,id] [--dry-run]
    uv run --project tools/ai-assets python tools/audio/earth_animal_calls.py --listen
"""
from __future__ import annotations

import argparse
import re
import subprocess
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent / "out_animals"
ROOT = Path(__file__).resolve().parents[2]
AI = ROOT / "tools" / "ai-assets"
OUT = HERE / "out"
LISTEN = HERE / "listen"

REAL = "a real animal, natural recording, close, clean, dry, no echo, no music, no human voice, no background noise"
SEA = "natural underwater recording, clean, no music, no human voice, no boat noise"

# (id, prompt, duration seconds, prompt influence) — two takes (a, b) per sound, each with its own wording.
CLIPS: list[tuple[str, str, float, float]] = [
    # --- dog ---
    ("animal_dog_bark_a", f"a friendly medium-sized dog barking twice, clear and lively, {REAL}", 1.0, 0.55),
    ("animal_dog_bark_b", f"one short friendly woof of a medium-sized dog, {REAL}", 0.7, 0.55),
    ("animal_dog_growl_a", f"a dog growling low in warning, steady, about one second, not vicious, {REAL}", 1.2, 0.55),
    ("animal_dog_growl_b", f"a short low dog growl that ends in a small huff, {REAL}", 1.0, 0.55),
    ("animal_dog_whine_a", f"a dog whining softly, one short high pleading whimper, {REAL}", 1.0, 0.55),
    ("animal_dog_whine_b", f"a young dog giving two small soft whines, {REAL}", 1.0, 0.55),
    # --- cat ---
    ("animal_cat_meow_a", f"a house cat meowing once, a clear friendly meow, {REAL}", 1.0, 0.55),
    ("animal_cat_meow_b", f"a short questioning cat meow, a bright little mrow, {REAL}", 0.8, 0.55),
    ("animal_cat_purr_a", f"a contented house cat purring steadily, {REAL}", 2.0, 0.55),
    ("animal_cat_purr_b", f"a deep soft cat purr, very close, calm, {REAL}", 2.0, 0.55),
    ("animal_cat_hiss_a", f"a cat hissing once, short and sharp, {REAL}", 0.8, 0.55),
    ("animal_cat_hiss_b", f"an annoyed cat giving a short hiss and a small low growl, {REAL}", 1.0, 0.55),
    # --- rabbit ---
    ("animal_rabbit_squeak_a", f"a rabbit making one soft short squeak, a tiny gentle animal sound, {REAL}", 0.6, 0.55),
    ("animal_rabbit_squeak_b", f"two tiny soft honking grunts of a happy rabbit, very gentle, {REAL}", 0.8, 0.55),
    ("animal_rabbit_thump_a", f"a rabbit thumping its hind foot on the ground once, one soft dull thump on soil, {REAL}", 0.5, 0.6),
    ("animal_rabbit_thump_b", f"two quick soft thumps of a rabbit's hind foot on earth, {REAL}", 0.7, 0.6),
    # --- cow ---
    ("animal_cow_moo_a", f"a cow mooing once, long and calm, {REAL}", 2.0, 0.55),
    ("animal_cow_moo_b", f"a shorter, lower moo of a relaxed cow, {REAL}", 1.5, 0.55),
    # --- sheep ---
    ("animal_sheep_baa_a", f"a sheep bleating once, a clear baa, {REAL}", 1.2, 0.55),
    ("animal_sheep_baa_b", f"a young sheep giving a higher, shorter bleat, {REAL}", 1.0, 0.55),
    # --- chicken ---
    ("animal_chicken_cluck_a", f"a hen clucking calmly, a few soft clucks, {REAL}", 1.2, 0.55),
    ("animal_chicken_cluck_b", f"a hen giving a quick excited run of clucks ending in one louder cackle, {REAL}", 1.5, 0.55),
    ("animal_chicken_crow_a", f"a rooster crowing once, a bright full morning crow, {REAL}", 2.5, 0.55),
    ("animal_chicken_crow_b", f"a shorter rooster crow, a little hoarse, {REAL}", 2.0, 0.55),
    # --- monkey ---
    ("animal_monkey_call_a", f"a small monkey calling hoo hoo hoo, playful and friendly, {REAL}", 1.2, 0.55),
    ("animal_monkey_call_b", f"a small monkey chattering briefly, a few quick cheerful chirps and hoots, {REAL}", 1.2, 0.55),
    ("animal_monkey_screech_a", f"a small monkey giving one short excited screech, {REAL}", 0.8, 0.55),
    ("animal_monkey_screech_b", f"one short alarm shriek of a small monkey, bright but not painful to hear, {REAL}", 0.8, 0.55),
    # --- parrot ---
    ("animal_parrot_squawk_a", f"a parrot squawking once, a loud rough tropical bird call, {REAL}", 0.8, 0.55),
    ("animal_parrot_squawk_b", f"a macaw giving two short raspy squawks, {REAL}", 1.0, 0.55),
    ("animal_parrot_whistle_a", f"a parrot whistling a short cheerful two-note whistle, {REAL}", 1.0, 0.55),
    ("animal_parrot_whistle_b", f"a parrot giving one short rising whistle and a small beak click, {REAL}", 1.2, 0.55),
    # --- elephant ---
    ("animal_elephant_trumpet_a", f"an elephant trumpeting once, a bright powerful trumpet call, {REAL}", 1.8, 0.55),
    ("animal_elephant_trumpet_b", f"one short elephant trumpet blast, {REAL}", 1.2, 0.55),
    ("animal_elephant_rumble_a", f"an elephant's deep low rumble, calm and long, {REAL}", 2.5, 0.55),
    ("animal_elephant_rumble_b", f"a shorter low throaty rumble of an elephant, {REAL}", 1.8, 0.55),
    # --- eagle ---
    ("animal_eagle_scream_a", f"an eagle's high piercing scream, one call, {REAL}", 1.5, 0.55),
    ("animal_eagle_scream_b", f"a bird of prey calling, one high cry that falls in pitch, {REAL}", 1.5, 0.55),
    # --- dolphin ---
    ("animal_dolphin_click_a", f"a dolphin making a quick series of clicks, a short chattering click train, {SEA}", 1.0, 0.55),
    ("animal_dolphin_click_b", f"a dolphin's short playful chatter of squeaks and clicks, {SEA}", 1.2, 0.55),
    ("animal_dolphin_whistle_a", f"a dolphin whistling, one short rising high whistle, {SEA}", 0.8, 0.55),
    ("animal_dolphin_whistle_b", f"a dolphin's two-tone whistle, high and clean, {SEA}", 1.0, 0.55),
    # --- whale ---
    ("animal_whale_song_a", f"a humpback whale singing, one long slow moaning call that rises and falls, deep and calm, {SEA}", 7.0, 0.5),
    ("animal_whale_song_b", f"a whale's long low groan followed by a high sweeping cry, slow and calm, {SEA}", 8.0, 0.5),
]


def generate(only: set[str], dry_run: bool) -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    made, failed = 0, []
    for cid, prompt, dur, infl in CLIPS:
        if only and cid not in only:
            continue
        out = OUT / f"{cid}.mp3"
        if out.exists() and out.stat().st_size > 0 and not only:
            continue
        cmd = [sys.executable, str(AI / "gen_sound.py"), "--prompt", prompt, "--out", str(out),
               "--duration", str(dur), "--influence", str(infl)]
        print(f"[gen] {cid} ({dur}s)", flush=True)
        if dry_run:
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


def levels(path: Path) -> tuple[float, float, float]:
    """(seconds, peak dB, mean dB) of a clip, by ffmpeg's volumedetect."""
    p = subprocess.run(["ffmpeg", "-hide_banner", "-nostats", "-i", str(path), "-af", "volumedetect", "-f", "null", "-"],
                       capture_output=True, text=True)
    err = p.stderr
    dur = re.search(r"Duration: (\d+):(\d+):(\d+\.\d+)", err)
    peak = re.search(r"max_volume: (-?\d+(?:\.\d+)?) dB", err)
    mean = re.search(r"mean_volume: (-?\d+(?:\.\d+)?) dB", err)
    seconds = (int(dur[1]) * 3600 + int(dur[2]) * 60 + float(dur[3])) if dur else 0.0
    return seconds, float(peak[1]) if peak else 0.0, float(mean[1]) if mean else -99.0


def listen_copies() -> None:
    """Peak-matched copies (-1.5 dBFS) for the sign-off page, plus a table of length and level."""
    LISTEN.mkdir(parents=True, exist_ok=True)
    missing = []
    for cid, _prompt, dur, _infl in CLIPS:
        src = OUT / f"{cid}.mp3"
        if not src.exists():
            missing.append(cid)
            continue
        seconds, peak, mean = levels(src)
        gain = -1.5 - peak
        subprocess.run(["ffmpeg", "-y", "-hide_banner", "-loglevel", "error", "-i", str(src),
                        "-af", f"volume={gain:.2f}dB", "-codec:a", "libmp3lame", "-b:a", "128k", str(LISTEN / f"{cid}.mp3")],
                       check=True)
        flag = " QUIET" if mean < -42.0 else ""
        print(f"[listen] {cid}: {seconds:.2f}s (asked {dur}s), peak {peak:.1f} dB, mean {mean:.1f} dB{flag}", flush=True)
    print(f"[listen] missing: {missing}", flush=True)


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--only", default="", help="comma-separated ids (re-rolls them even if they exist)")
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--listen", action="store_true", help="only rebuild the listening copies")
    args = ap.parse_args()
    if not args.listen:
        generate({s for s in args.only.split(",") if s}, args.dry_run)
    if not args.dry_run:
        listen_copies()


if __name__ == "__main__":
    main()
