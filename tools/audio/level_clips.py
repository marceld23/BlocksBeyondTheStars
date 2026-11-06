# Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
# SPDX-License-Identifier: AGPL-3.0-or-later
# This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
"""Levels every SFX clip in client/Assets/Resources/audio to its class target and records it (#3237).

Why: the clips are raw ElevenLabs renders. Unity's ``normalize`` import flag only acts during a ``forceToMono`` downmix,
so most clips played at whatever level the generator produced — groups the code plays at the SAME volume were 20–48 dB
apart (mining stone was ~30 dB quieter than mining ore). Code volumes are tuned by ear on top of the class level, so the
class targets below sit near each class's old median: the mix keeps its shape, the outliers come in.

Measures with ffmpeg's EBU R128 filter: one-shots by their loudest moment (momentary max, 400 ms window), loops and beds
by their integrated loudness. A clip outside its target ± ``SLACK`` dB is re-gained (with a -1 dBFS limiter) and
re-encoded; every clip's class, level and SHA-256 land in ``tools/audio/audio_levels.json``. The xUnit guard
``AudioLevelManifestTests`` fails when a clip is missing from the manifest, its bytes changed since it was levelled,
or its level is off its class target.

Usage (repository root, ffmpeg + ffprobe on PATH):
    python tools/audio/level_clips.py --measure         # report only, write nothing
    python tools/audio/level_clips.py --apply           # level what is off, rewrite the manifest
    python tools/audio/level_clips.py --import <dir>    # trim + level new clips from <dir>, add .meta files, then --apply
"""
from __future__ import annotations

import argparse
import hashlib
import json
import re
import shutil
import statistics
import subprocess
import sys
import tempfile
import uuid
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
AUDIO = ROOT / "client" / "Assets" / "Resources" / "audio"
MANIFEST = ROOT / "tools" / "audio" / "audio_levels.json"

# Class → (metric, target LUFS). "mmax" = momentary max (one-shots), "integrated" = loops/beds.
CLASSES: dict[str, tuple[str, float]] = {
    # Targets sit at (or, where a whole class was off, near) the class median measured on 2026-11-05.
    "step": ("mmax", -24.0),       # footsteps: the most repeated sound (median was -35: barely audible)
    "foley": ("mmax", -20.0),      # body sounds: climbing, landing, swimming, breath, wings (median was -37)
    "creature": ("mmax", -10.0),   # creature calls and combat banks (median -8: the loudest clips in the game)
    "voice": ("mmax", -11.0),      # NPC babble/reactions, VEGA chatter
    "sting": ("mmax", -9.0),       # festival jingles, chapter stings, splash
    "weather": ("integrated", -18.0),
    "bed": ("integrated", -20.0),  # biome/night/fluid beds and quiet ambience loops
    "machine": ("integrated", -18.0),  # engines, motors, hums
    "oneshot": ("mmax", -12.0),
}

RULES: list[tuple[str, str]] = [
    ("step", r"^step_"),
    ("foley", r"^(climb_|land$|land_|jump$|swim_|breath_|heartbeat|surface_gasp|water_splash|wing_flap|suit_thruster|"
              r"spring_jump$|glider_open$|rcs_puff|ship_cooling_tick|cargo_creak)"),
    ("creature", r"^creature_"),
    ("voice", r"^(npc_voice_|npc_human_|npc_alien_|vega_chatter_)"),
    ("sting", r"^(festival_|chapter_sting|jumave_sting$|splash_intro$)"),
    ("weather", r"^(rain_loop|storm_loop|drizzle_loop|gale_loop|blizzard_loop|sandstorm_loop|ash_loop|acid_hiss|"
                r"ion_crackle|spore_amb|meteor_streak|toxic_storm_loop|wind_light|wind_strong|rain_on_hull)$"),
    ("bed", r"^(amb_|night_|water_(brook|fall|shore|surf)$|lava_fall$|lava_bubble$|fire_crackle$|energy_fence_hum$|glider_wind$|"
            r"underwater_bubbles$|suit_room_tone$|cockpit_ambience$|life_support_cycle$|slither_loop$|vent_hiss$)"),
    ("machine", r"^(engine_idle|engine_boost|drill_loop|auto_drill_loop|vehicle_engine_loop|"
                r"boat_engine_loop|rope_reel|lift_motor|factory_hum|data_cube_hum|anomaly_hum|wormhole_hum|"
                r"pod_beacon|beam_idle|clone_tank_bubble|reactor_hum|ship_exterior_idle|alarm_siren_\d)$"),
]
SLACK = 1.5       # dB — closer than this counts as levelled; the guard test allows TOLERANCE
TOLERANCE = 3.0   # dB — the guard's allowance (re-encoding moves a level by a fraction of a dB)
PEAK_CEILING = -2.0  # dBFS — a clip whose peak reaches this is limiter-bound: it may sit below its target (a click
                    # or a gunshot is all peak; more gain would only flatten it)
TRIM_CLASSES = {"step", "foley"}  # tight attack: a footstep that starts 80 ms late feels laggy


def class_of(cid: str) -> str:
    for name, rx in RULES:
        if re.search(rx, cid):
            return name
    return "oneshot"


def measure(path: Path) -> dict:
    # Padded with a second of silence: a trimmed footstep is shorter than the 400 ms momentary window, and an
    # unpadded clip that short reads as -inf (and would be "levelled" by +46 dB into a limiter).
    p = subprocess.run(["ffmpeg", "-hide_banner", "-nostats", "-i", str(path), "-af", "apad=pad_dur=1,ebur128=peak=true",
                        "-f", "null", "-"], capture_output=True, text=True, timeout=120)
    txt = p.stderr
    summ = txt[txt.rfind("Summary:"):] if "Summary:" in txt else ""
    out: dict = {}
    m = re.search(r"I:\s+(-?[\d.]+)\s+LUFS", summ)
    out["integrated"] = float(m.group(1)) if m else -70.0
    m = re.search(r"Peak:\s+(-?[\d.]+|-inf)\s+dBFS", summ)
    out["peak"] = float(m.group(1)) if m and m.group(1) != "-inf" else -70.0
    mm = [float(x) for x in re.findall(r"M:\s*(-?[\d.]+)", txt)]
    out["mmax"] = max([v for v in mm if v > -120] or [-70.0])
    if out["integrated"] <= -69.9:  # too short for the gate: fall back to the loudest moment
        out["integrated"] = out["mmax"]
    return out


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def channels(path: Path) -> int:
    p = subprocess.run(["ffprobe", "-v", "error", "-show_entries", "stream=channels", "-of", "csv=p=0", str(path)],
                       capture_output=True, text=True)
    try:
        return int(p.stdout.strip().splitlines()[0])
    except (ValueError, IndexError):
        return 2


def encode(src: Path, dst: Path, gain_db: float, trim: bool, mono: bool | None) -> None:
    filters = []
    if trim:
        # Drop the leading silence (a step must start on the frame) and the trailing one (reverse, trim, reverse back).
        filters += ["silenceremove=start_periods=1:start_threshold=-50dB:start_silence=0.004",
                    "areverse", "silenceremove=start_periods=1:start_threshold=-55dB:start_silence=0.02", "areverse"]
    filters += [f"volume={gain_db:.2f}dB", "alimiter=limit=0.89:level=false:attack=2:release=40"]
    cmd = ["ffmpeg", "-y", "-hide_banner", "-loglevel", "error", "-i", str(src), "-af", ",".join(filters)]
    if mono is True:
        cmd += ["-ac", "1"]
    cmd += ["-ar", "44100", "-c:a", "libmp3lame", "-b:a", "128k", "-id3v2_version", "0", str(dst)]
    subprocess.run(cmd, check=True)


def level_one(path: Path, trim: bool = False, mono: bool | None = None) -> dict:
    cid = path.stem
    cls = class_of(cid)
    metric, target = CLASSES[cls]
    m = measure(path)
    level = m[metric]
    changed = False
    if trim or abs(level - target) > SLACK:
        gain = target - level
        with tempfile.TemporaryDirectory() as td:
            tmp = Path(td) / path.name
            # Two passes at most: the limiter can eat part of a big boost, the second pass tops it up.
            src = path
            for _ in range(2):
                encode(src, tmp, gain, trim, mono)
                m = measure(tmp)
                level = m[metric]
                if abs(level - target) <= SLACK:
                    break
                shutil.copyfile(tmp, Path(td) / ("pass_" + path.name))
                src = Path(td) / ("pass_" + path.name)
                gain = target - level
                trim = False
            shutil.copyfile(tmp, path)
        changed = True
    return {"id": cid, "class": cls, "level": round(level, 1), "peak": round(m["peak"], 1), "changed": changed}


META_TEMPLATE = """fileFormatVersion: 2
guid: {guid}
AudioImporter:
  externalObjects: {{}}
  serializedVersion: 8
  defaultSettings:
    serializedVersion: 2
    loadType: {load}
    sampleRateSetting: 0
    sampleRateOverride: 44100
    compressionFormat: 1
    quality: {quality}
    conversionMode: 0
    preloadAudioData: 0
  platformSettingOverrides: {{}}
  forceToMono: {mono}
  normalize: 0
  loadInBackground: 0
  ambisonic: 0
  3D: 1
  userData:
  assetBundleName:
  assetBundleVariant:
"""


def write_meta(mp3: Path) -> None:
    meta = mp3.with_suffix(".mp3.meta")
    if meta.exists():
        return
    cls = class_of(mp3.stem)
    loop = CLASSES[cls][0] == "integrated"
    # Loops/beds: Compressed In Memory (#1524 — a decompressed 10 s loop is ~10x the RAM); one-shots stay Decompress On
    # Load, which SampleKit's GetData bakes need. Positional one-shots are mono: a point source wastes the second channel.
    meta.write_text(META_TEMPLATE.format(guid=uuid.uuid4().hex, load=1 if loop else 0, quality="0.5" if loop else "1",
                                         mono=1 if not loop else 0), encoding="utf-8", newline="\n")


def fix_normalize_flags() -> int:
    """Unity's ``normalize`` peak-normalises a forceToMono clip on import and would undo the levelling."""
    n = 0
    for meta in AUDIO.glob("*.mp3.meta"):
        t = meta.read_text(encoding="utf-8")
        if "normalize: 1" in t:
            meta.write_text(t.replace("normalize: 1", "normalize: 0"), encoding="utf-8", newline="\n")
            n += 1
    return n


def write_manifest(rows: list[dict]) -> None:
    clips = {}
    for r in sorted(rows, key=lambda r: r["id"]):
        path = AUDIO / f"{r['id']}.mp3"
        clips[r["id"]] = {"class": r["class"], "level": r["level"], "peak": r["peak"], "sha256": sha256(path)}
    doc = {
        "_comment": "Written by tools/audio/level_clips.py — never by hand. Guarded by AudioLevelManifestTests.",
        "tolerance": TOLERANCE,
        "peakCeiling": PEAK_CEILING,
        "classes": {k: {"metric": v[0], "target": v[1]} for k, v in CLASSES.items()},
        "clips": clips,
    }
    MANIFEST.write_text(json.dumps(doc, indent=1, ensure_ascii=False) + "\n", encoding="utf-8", newline="\n")


def run_all(apply: bool, only: set[str] | None = None) -> list[dict]:
    paths = sorted(AUDIO.glob("*.mp3"))

    def job(p: Path) -> dict:
        if apply and (not only or p.stem in only):
            return level_one(p)
        cls = class_of(p.stem)
        m = measure(p)
        return {"id": p.stem, "class": cls, "level": round(m[CLASSES[cls][0]], 1), "peak": m["peak"], "changed": False}

    with ThreadPoolExecutor(max_workers=8) as ex:
        return list(ex.map(job, paths))


def report(rows: list[dict]) -> None:
    by: dict[str, list[float]] = {}
    for r in rows:
        by.setdefault(r["class"], []).append(r["level"])
    for cls, vals in sorted(by.items()):
        tgt = CLASSES[cls][1]
        off = [r for r in rows if r["class"] == cls and abs(r["level"] - tgt) > TOLERANCE
               and not (r["level"] < tgt and r["peak"] >= PEAK_CEILING)]
        print(f"{cls:9s} n={len(vals):3d} median={statistics.median(vals):6.1f} target={tgt:6.1f} off={len(off)}")
    big = sorted((r for r in rows if r["level"] < CLASSES[r["class"]][1] - 25), key=lambda r: r["level"])
    if big:
        print("needs > 25 dB of gain (re-generate rather than boost):", ", ".join(r["id"] for r in big))


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    g = ap.add_mutually_exclusive_group(required=True)
    g.add_argument("--measure", action="store_true")
    g.add_argument("--apply", action="store_true")
    g.add_argument("--import", dest="imp", metavar="DIR")
    ap.add_argument("--only", default="", help="with --apply: level only these comma-separated ids (others are measured)")
    args = ap.parse_args()
    if args.measure:
        report(run_all(False))
        return
    if args.imp:
        src_dir = Path(args.imp)
        for src in sorted(src_dir.glob("*.mp3")):
            dst = AUDIO / src.name
            shutil.copyfile(src, dst)
            cls = class_of(dst.stem)
            mono = True if CLASSES[cls][0] == "mmax" else None
            r = level_one(dst, trim=cls in TRIM_CLASSES, mono=mono)
            write_meta(dst)
            print(f"[import] {r['id']:28s} {r['class']:9s} {r['level']:6.1f}")
    n = fix_normalize_flags()
    rows = run_all(True, {s for s in args.only.split(",") if s} or None)
    write_manifest(rows)
    print(f"normalize flags cleared: {n}; levelled: {sum(r['changed'] for r in rows)} of {len(rows)}")
    report(rows)


if __name__ == "__main__":
    sys.exit(main())
