# Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
# SPDX-License-Identifier: AGPL-3.0-or-later
# This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
"""The interface's hover sound (`ui_hover`): a short, dark console blip — synthesised, no model, no recording.

The hover sound is the most frequent sound of the interface and took four rounds of listening (2026-11-17, see
docs/developer/SOUND_DESIGN.md §39): a generated high tick was too bright, the same take darkened read as hammering,
a breath of air was not it either. What was asked for in the end: technical, not shrill. This is that sound, so it
can be rebuilt or retuned (pitch, length) without a generator:

* a 520 Hz tone with a little of its 3rd and 5th partial (a triangle-like colour), decaying with a 22 ms time constant;
* band-limited to 350 Hz – 4.5 kHz (no thud below, nothing shrill above);
* 70 ms long, a 3 ms fade-in so it does not click, a 30 ms fade-out.

Usage (from the repository root; needs ffmpeg on the PATH):
    uv run --no-project --with numpy python tools/audio/synth_ui_hover.py <folder>
writes ``<folder>/ui_hover.mp3``; then level and copy it into the client with
    uv run --no-project python tools/audio/level_clips.py --import <folder> --only ui_hover
"""
from __future__ import annotations

import subprocess
import sys
from pathlib import Path

import numpy as np

SR = 44100
SECONDS = 0.07
PITCH_HZ = 520.0
DECAY_SECONDS = 0.022
BAND_HZ = (350.0, 4500.0)
FADE_IN_SECONDS = 0.003
FADE_OUT_SECONDS = 0.03


def band(x: np.ndarray, lo: float, hi: float) -> np.ndarray:
    """A 4th-order-like band-pass in the frequency domain (zero phase, so the envelope keeps its shape)."""
    spec = np.fft.rfft(x)
    f = np.fft.rfftfreq(len(x), 1 / SR)
    gain = 1 / (1 + (lo / np.maximum(f, 1)) ** 4) / (1 + (f / hi) ** 4)
    return np.fft.irfft(spec * gain, len(x))


def hover() -> np.ndarray:
    t = np.arange(int(SECONDS * SR)) / SR
    tone = (np.sin(2 * np.pi * PITCH_HZ * t)
            + 0.22 * np.sin(2 * np.pi * 3 * PITCH_HZ * t)
            + 0.08 * np.sin(2 * np.pi * 5 * PITCH_HZ * t)) * np.exp(-t / DECAY_SECONDS)
    x = band(tone, *BAND_HZ)
    k = max(1, int(FADE_IN_SECONDS * SR))
    m = min(len(x) - k, int(FADE_OUT_SECONDS * SR))
    w = np.ones(len(x))
    w[:k] = np.linspace(0, 1, k)
    w[len(x) - m:] *= 0.5 + 0.5 * np.cos(np.linspace(0, np.pi, m))
    x = x * w
    return x / max(1e-9, float(np.max(np.abs(x)))) * 0.7


def main() -> None:
    if len(sys.argv) != 2:
        sys.exit(__doc__)
    folder = Path(sys.argv[1])
    folder.mkdir(parents=True, exist_ok=True)
    out = folder / "ui_hover.mp3"
    subprocess.run(["ffmpeg", "-hide_banner", "-loglevel", "error", "-y", "-f", "f32le", "-ac", "1", "-ar", str(SR), "-i",
                    "-", "-b:a", "192k", str(out)], input=hover().astype(np.float32).tobytes(), check=True)
    print(f"[ok] wrote {out} ({SECONDS * 1000:.0f} ms, {PITCH_HZ:.0f} Hz)")


if __name__ == "__main__":
    main()
