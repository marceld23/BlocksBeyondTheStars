"""Generate ONE sound effect from a text prompt via ElevenLabs' text-to-sound-effects API.

Design: **one file per run** (same cost discipline as gen_image.py). Reads
``ELEVENLABS_API_KEY`` from the environment / a local ``.env``. ElevenLabs bills sound effects
in credits (roughly per second of audio), so keep durations short for game SFX.

Usage:
    uv run gen_sound.py --prompt "short mechanical mining drill hit on rock" \
        --out out/mine.mp3 --duration 1.0
"""
from __future__ import annotations

import argparse
import os
import sys
from pathlib import Path

from dotenv import load_dotenv


def main() -> None:
    ap = argparse.ArgumentParser(description="Generate one sound effect via ElevenLabs (one file per run).")
    ap.add_argument("--prompt", required=True, help="text description of the sound effect")
    ap.add_argument("--out", required=True, help="output .mp3 path")
    ap.add_argument("--duration", type=float, default=0.0,
                    help="length in seconds (0.5-30); 0 = let the model choose")
    ap.add_argument("--influence", type=float, default=0.3,
                    help="prompt_influence 0..1 (higher = stick closer to the prompt)")
    ap.add_argument("--loop", action="store_true", help="seamless loop (eleven_text_to_sound_v2)")
    ap.add_argument("--model", default="eleven_text_to_sound_v2")
    ap.add_argument("--format", dest="fmt", default="mp3_44100_128", help="output_format, e.g. mp3_44100_128")
    args = ap.parse_args()

    load_dotenv()
    key = os.environ.get("ELEVENLABS_API_KEY")
    if not key:
        sys.exit("ELEVENLABS_API_KEY is not set (put it in .env or the environment).")

    print("[cost] ElevenLabs bills sound effects in credits (≈ per second of audio); "
          "check your plan's credit cost before running.")

    from elevenlabs.client import ElevenLabs

    client = ElevenLabs(api_key=key)
    kwargs = dict(
        text=args.prompt,
        prompt_influence=args.influence,
        model_id=args.model,
        output_format=args.fmt,
    )
    if args.duration > 0:
        kwargs["duration_seconds"] = args.duration
    if args.loop:
        kwargs["loop"] = True

    audio = client.text_to_sound_effects.convert(**kwargs)
    data = audio if isinstance(audio, (bytes, bytearray)) else b"".join(audio)

    out = Path(args.out)
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_bytes(data)
    print(f"[ok] wrote {out} ({out.stat().st_size} bytes)")


if __name__ == "__main__":
    main()
