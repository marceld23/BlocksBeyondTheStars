# Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
# SPDX-License-Identifier: AGPL-3.0-or-later
# This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
"""Generate ONE background-music track from a text prompt (or a composition plan) via the ElevenLabs
Music API (#1175).

Design: **one file per run** (same cost discipline as gen_sound.py / gen_image.py). Reads
``ELEVENLABS_API_KEY`` from the environment / a local ``.env``. Music generation bills credits per
generated second — check the plan before a batch, and listen to a trial track before generating a
family of variants.

Two ways to drive the model:

* ``--prompt`` — a free-text description (the same style of prompt the Suno library was made with, see
  docs/developer/MUSIC_TRACKS.md). ``--length`` fixes the duration; instrumental is forced.
* ``--plan plan.json`` — a **composition plan** (sections with styles + durations). ``--make-plan`` writes
  such a plan from a prompt WITHOUT spending credits (``composition_plan.create`` is free), so the plan
  can be reviewed/edited and then rendered with several ``--seed`` values — seed siblings of one plan
  form a coherent "family" (same structure, different performance).

Usage:
    uv run gen_music.py --prompt "calm ambient sci-fi loop for an ice planet at night, instrumental" \
        --length 165 --out out/music/music_planet_ice_3.mp3
    uv run gen_music.py --make-plan "…" --length 165 --plan-out out/music/plan_ice.json
    uv run gen_music.py --plan out/music/plan_ice.json --seed 2 --out out/music/music_planet_ice_4.mp3
"""
from __future__ import annotations

import argparse
import json
import os
import sys
from pathlib import Path

from dotenv import load_dotenv


def main() -> None:
    ap = argparse.ArgumentParser(description="Generate one music track via ElevenLabs Music (one file per run).")
    src = ap.add_mutually_exclusive_group(required=True)
    src.add_argument("--prompt", help="text description of the track (instrumental is forced)")
    src.add_argument("--plan", help="path to a composition-plan JSON (from --make-plan, possibly edited)")
    src.add_argument("--make-plan", metavar="PROMPT",
                     help="only create a composition plan from PROMPT (free, no audio) and write it to --plan-out")
    ap.add_argument("--out", help="output .mp3 path (required unless --make-plan)")
    ap.add_argument("--plan-out", help="where --make-plan writes the plan JSON")
    ap.add_argument("--length", type=float, default=0.0,
                    help="track length in seconds (3-600); 0 = let the model choose (prompt/plan modes)")
    ap.add_argument("--seed", type=int, default=None,
                    help="random seed (plan mode only — the API rejects seed with a prompt)")
    ap.add_argument("--format", dest="fmt", default="mp3_44100_128",
                    help="output_format, e.g. mp3_44100_128 (192 kbps needs the Creator tier)")
    ap.add_argument("--model", default="music_v1")
    args = ap.parse_args()

    load_dotenv(Path(__file__).with_name(".env"))
    key = os.environ.get("ELEVENLABS_API_KEY")
    if not key:
        sys.exit("ELEVENLABS_API_KEY is not set (put it in .env or the environment).")

    from elevenlabs.client import ElevenLabs

    client = ElevenLabs(api_key=key)
    length_ms = int(args.length * 1000) if args.length > 0 else None

    if args.make_plan:
        if not args.plan_out:
            sys.exit("--make-plan needs --plan-out <file.json>.")
        plan = client.music.composition_plan.create(
            prompt=args.make_plan, music_length_ms=length_ms, model_id=args.model)
        data = plan.model_dump() if hasattr(plan, "model_dump") else plan.dict()
        out = Path(args.plan_out)
        out.parent.mkdir(parents=True, exist_ok=True)
        out.write_text(json.dumps(data, indent=2), encoding="utf-8")
        print(f"[ok] wrote plan {out} ({len(data.get('sections', []))} sections, free of charge)")
        return

    if not args.out:
        sys.exit("--out is required when generating audio.")

    print("[cost] ElevenLabs bills music generation in credits (per generated second); "
          "check your plan's credit cost before a batch.")

    kwargs = dict(output_format=args.fmt, model_id=args.model)
    if args.prompt:
        kwargs["prompt"] = args.prompt
        kwargs["force_instrumental"] = True
        if length_ms:
            kwargs["music_length_ms"] = length_ms
        if args.seed is not None:
            print("[warn] --seed is ignored in prompt mode (the API only accepts it with a plan).")
    else:
        from elevenlabs.types import MusicPrompt

        plan_data = json.loads(Path(args.plan).read_text(encoding="utf-8"))
        kwargs["composition_plan"] = MusicPrompt.model_validate(plan_data) if hasattr(MusicPrompt, "model_validate") \
            else MusicPrompt.parse_obj(plan_data)
        if args.seed is not None:
            kwargs["seed"] = args.seed

    audio = client.music.compose(**kwargs)
    data = audio if isinstance(audio, (bytes, bytearray)) else b"".join(audio)

    out = Path(args.out)
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_bytes(data)
    print(f"[ok] wrote {out} ({out.stat().st_size} bytes)")


if __name__ == "__main__":
    main()
