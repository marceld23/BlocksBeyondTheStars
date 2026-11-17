# Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
# SPDX-License-Identifier: AGPL-3.0-or-later
# This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
"""The player figure's extra voice clips (#3767): every prompt, so a clip can be re-generated later.

The figure's voice is ONE set of mouth and breath clips that the game retunes per player (a pitch and an age slider,
``PlayerVoice`` in Shared) — so every clip here is recorded in a NEUTRAL, mid-pitched voice: the sliders move it both
ways. Two more takes of the pain grunt (``hurt_player`` had one) and a short breath of effort for a jump under strain.

Calls ``tools/ai-assets/gen_sound.py`` once per clip (that tool stays one-file-per-run). Resumable: a clip whose
``tools/ai-assets/out/voice/<id>.mp3`` already exists is skipped unless it is named in ``--only``. Afterwards level and
copy them into the client:

    uv run --no-project python tools/audio/level_clips.py --import tools/ai-assets/out/voice --only <ids>

and add their lines to NOTICES.md. Check for an existing clip of the same name first — an import overwrites it.

Usage (from the repository root):
    uv run --project tools/ai-assets python tools/audio/voice_batch.py [--only id,id] [--dry-run]
"""
from __future__ import annotations

import argparse
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
AI = ROOT / "tools" / "ai-assets"
OUT = AI / "out" / "voice"

VOICE = ("a neutral androgynous mid-pitched voice, close, clean, dry, no echo, no words, no music, "
         "mild and not scary, for a kid-friendly video game")
BREATH = "breath only, no vocal tone, close, clean, dry, no echo, no words, no music, calm, for a kid-friendly video game"

# (id, prompt, duration seconds, prompt influence)
CLIPS: list[tuple[str, str, float, float]] = [
    ("hurt_player_2", f"one short grunt of someone taking a bump, a quick soft 'oof', {VOICE}", 0.6, 0.5),
    ("hurt_player_3", f"one short surprised 'ugh' of someone knocked back a step, {VOICE}", 0.6, 0.5),
    ("breath_effort", f"one short sharp exhale of effort through the mouth, like a quick 'hup' when jumping, {BREATH}", 0.5, 0.5),
    ("breath_effort_2", f"a quick puff of breath of someone pushing off the ground, a short 'hff', {BREATH}", 0.5, 0.5),
]


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--only", default="", help="comma-separated ids (re-rolls them even if they exist)")
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args()
    only = {s for s in args.only.split(",") if s}
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
        if args.dry_run:
            continue
        for _attempt in range(2):
            r = subprocess.run(cmd, cwd=AI, capture_output=True, text=True, check=False)
            if r.returncode == 0 and out.exists() and out.stat().st_size > 0:
                made += 1
                break
            print(f"[retry] {cid}: {r.stderr.strip()[-300:]}", flush=True)
        else:
            failed.append(cid)
    print(f"[done] made: {made}, failed: {failed}", flush=True)


if __name__ == "__main__":
    main()
