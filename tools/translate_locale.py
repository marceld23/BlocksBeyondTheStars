#!/usr/bin/env python3
# Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
# SPDX-License-Identifier: AGPL-3.0-or-later
# This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
"""Machine-translate a locale file's missing keys from en.json via an OpenAI-compatible API.

This is the maintainer workflow for bootstrapping a NEW language (or topping up an existing
one after new keys land): generate a first pass, validate it, ship it gated behind the
settings picker's coverage bar, and let community review improve a *playable* language
instead of starting from zero. See docs/developer/TRANSLATION_GUIDE.md.

The tool is resumable and incremental: keys already present in the target file are skipped,
output is written after every chunk, and a re-run only translates what is still missing.
Placeholders like {name} are validated per key — a mismatch discards the chunk and retries
once, then leaves those keys untranslated (locale_report.py will list them).

Chunks are translated concurrently (--workers, default 4) and each request asks the model for
minimal reasoning effort (--effort; pass --effort "" for models that reject the parameter) —
together roughly an order of magnitude faster than the old one-request-at-a-time loop.

Usage (stdlib only — no venv needed):
    uv run --no-project python tools/translate_locale.py fr
    uv run --no-project python tools/translate_locale.py es --file data/stories/vega_protocol/locales/es.json \
        --source data/stories/vega_protocol/locales/en.json
    uv run --no-project python tools/translate_locale.py fr --dry-run     # count + cost estimate only

Reads OPENAI_API_KEY from the environment or from tools/ai-assets/.env (git-ignored).
"""
from __future__ import annotations

import argparse
import json
import os
import re
import sys
import time
import urllib.error
import urllib.request
from concurrent.futures import ThreadPoolExecutor, as_completed
from pathlib import Path

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

REPO_ROOT = Path(__file__).resolve().parent.parent
PLACEHOLDER = re.compile(r"\{[A-Za-z0-9_:]+\}")  # {name} and the {key:Action} control tokens (#1077)

LANGUAGES = {
    "de": "German",
    "fr": "French",
    "es": "Spanish",
    "it": "Italian",
    "pt": "Brazilian Portuguese",
    "pl": "Polish",
    "tr": "Turkish",
    "nl": "Dutch",
    "ru": "Russian",
    "uk": "Ukrainian",
    "zh": "Simplified Chinese",
    "ja": "Japanese",
    "ko": "Korean",
}

SYSTEM_PROMPT = """You translate UI strings for "Blocks Beyond the Stars", a kid-friendly sci-fi \
voxel space-exploration game (mining, crafting, ships, planets, a ship AI called VEGA).

Rules:
- Translate from English to {language}. Return ONLY a JSON object with the SAME keys and the \
translated values. No commentary, no markdown fence.
- Keep every placeholder like {{name}}, {{item}}, {{count}} EXACTLY as-is (position may move). \
Control tokens like {{key:ToggleLamp}} are placeholders too — copy them byte for byte, never translate the word inside.
- Keep formatting: leading/trailing punctuation, newlines (\\n), brackets, ALL-CAPS style where used.
- Tone: friendly, concise, kid-appropriate. Use the informal address (German "du", French "tu", \
Spanish "tú", Portuguese "você", Polish "ty", Turkish "sen", Dutch "je/jij", Russian "ты", \nUkrainian "ти"). Japanese: friendly polite です/ます; Korean: friendly 해요체; Chinese: plain friendly register.
- Keep proper names untranslated: VEGA, Blocks Beyond the Stars. Game terms translate naturally \
and CONSISTENTLY across keys (e.g. blueprint, knowledge, suit energy, airlock).
- UI strings must stay short: if the English is one or two words, the translation should be too.
"""


def load_env_key() -> str:
    key = os.environ.get("OPENAI_API_KEY", "")
    if key:
        return key
    for env_path in (REPO_ROOT / "tools" / "ai-assets" / ".env",):
        if env_path.exists():
            for line in env_path.read_text(encoding="utf-8").splitlines():
                if line.startswith("OPENAI_API_KEY="):
                    return line.split("=", 1)[1].strip()
    return ""


def chat(api_key: str, model: str, system: str, user: str, timeout: float, effort: str) -> str:
    payload: dict = {
        "model": model,
        "messages": [
            {"role": "system", "content": system},
            {"role": "user", "content": user},
        ],
        "response_format": {"type": "json_object"},
    }
    if effort:  # reasoning models only — translation needs no chain of thought, so "minimal" is much faster
        payload["reasoning_effort"] = effort
    body = json.dumps(payload).encode("utf-8")
    req = urllib.request.Request(
        os.environ.get("OPENAI_BASE_URL", "https://api.openai.com/v1").rstrip("/") + "/chat/completions",
        data=body,
        headers={"Content-Type": "application/json", "Authorization": f"Bearer {api_key}"},
    )
    with urllib.request.urlopen(req, timeout=timeout) as resp:
        data = json.load(resp)
    return data["choices"][0]["message"]["content"]


def translate_chunk(api_key: str, model: str, language: str, chunk: dict[str, str],
                    timeout: float, effort: str) -> dict[str, str]:
    """One API call for one chunk; returns only entries that pass validation."""
    system = SYSTEM_PROMPT.format(language=language)
    user = json.dumps(chunk, ensure_ascii=False, indent=0)
    raw = chat(api_key, model, system, user, timeout, effort)
    try:
        result = json.loads(raw)
    except json.JSONDecodeError:
        return {}

    good: dict[str, str] = {}
    for key, source in chunk.items():
        value = result.get(key)
        if not isinstance(value, str) or not value.strip():
            continue
        if set(PLACEHOLDER.findall(source)) != set(PLACEHOLDER.findall(value)):
            continue
        good[key] = value
    return good


def main() -> int:
    ap = argparse.ArgumentParser(description="Translate missing locale keys via an OpenAI-compatible API.")
    ap.add_argument("lang", choices=sorted(LANGUAGES), help="target language code")
    ap.add_argument("--source", default=None, help="source en.json (default: data/locales/en.json)")
    ap.add_argument("--file", default=None, help="target file (default: data/locales/<lang>.json)")
    ap.add_argument("--model", default="gpt-5-mini", help="chat model (any OpenAI-compatible id)")
    ap.add_argument("--chunk", type=int, default=50, help="keys per request")
    ap.add_argument("--workers", type=int, default=4, help="concurrent requests")
    ap.add_argument("--effort", default="minimal",
                    help='reasoning_effort for reasoning models (gpt-5*); "" omits the parameter')
    ap.add_argument("--timeout", type=float, default=180.0, help="per-request timeout seconds")
    ap.add_argument("--dry-run", action="store_true", help="only report what would be translated")
    args = ap.parse_args()

    source_path = Path(args.source) if args.source else REPO_ROOT / "data" / "locales" / "en.json"
    target_path = Path(args.file) if args.file else REPO_ROOT / "data" / "locales" / f"{args.lang}.json"

    source = json.loads(source_path.read_text(encoding="utf-8"))
    target: dict[str, str] = {}
    if target_path.exists():
        target = json.loads(target_path.read_text(encoding="utf-8"))

    missing = {k: v for k, v in source.items() if k not in target or not str(target.get(k, "")).strip()}
    print(f"{target_path.name}: {len(source) - len(missing)}/{len(source)} present, {len(missing)} to translate")
    if not missing:
        return 0
    if args.dry_run:
        chars = sum(len(k) + len(v) for k, v in missing.items())
        print(f"dry run: ~{chars:,} source chars in {(len(missing) + args.chunk - 1) // args.chunk} requests")
        return 0

    api_key = load_env_key()
    if not api_key:
        print("ERROR: OPENAI_API_KEY not set (env or tools/ai-assets/.env)", file=sys.stderr)
        return 1

    def write_target() -> None:
        # Mirror en.json's key order so diffs stay readable (untranslated keys are simply absent).
        ordered = {k: target[k] for k in source if k in target}
        ordered.update({k: v for k, v in target.items() if k not in source})  # target-only extras, kept last
        target_path.parent.mkdir(parents=True, exist_ok=True)
        target_path.write_text(
            json.dumps(ordered, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")

    keys = list(missing)
    language = LANGUAGES[args.lang]

    def translate_batch(batch: list[str]) -> dict[str, str]:
        """Translate one batch of keys, retrying once for request errors and validation failures."""
        chunk = {k: missing[k] for k in batch}
        got: dict[str, str] = {}
        for attempt in (1, 2):
            try:
                got.update(translate_chunk(api_key, args.model, language, chunk,
                                           args.timeout, args.effort))
            except (urllib.error.URLError, urllib.error.HTTPError, TimeoutError, OSError) as ex:
                detail = getattr(ex, "read", None)
                print(f"  request failed (attempt {attempt}): {ex}"
                      + (f" — {detail()[:300]}" if callable(detail) else ""), file=sys.stderr)
                time.sleep(2 * attempt)
                continue
            chunk = {k: v for k, v in chunk.items() if k not in got}
            if not chunk:
                break  # second pass only for the keys that failed validation
        return got

    batches = [keys[start:start + args.chunk] for start in range(0, len(keys), args.chunk)]
    done = 0
    # Requests run concurrently; the target file is only touched from this thread, after each
    # completed batch, so the tool stays resumable even when interrupted mid-run.
    with ThreadPoolExecutor(max_workers=max(1, args.workers)) as pool:
        futures = {pool.submit(translate_batch, batch): len(batch) for batch in batches}
        for future in as_completed(futures):
            got = future.result()
            target.update(got)
            done += len(got)
            write_target()
            skipped = futures[future] - len(got)
            print(f"  {done}/{len(missing)} translated"
                  + (f" ({skipped} skipped in batch)" if skipped else ""))

    still = [k for k in missing if k not in target]
    if still:
        print(f"WARNING: {len(still)} keys untranslated (validation/API failures) — rerun to retry:")
        for k in still[:20]:
            print(f"    {k}")
    print("done — validate with: uv run --no-project python tools/locale_report.py "
          f"{args.lang} && python tools/locale_report.py --check")
    return 0


if __name__ == "__main__":
    sys.exit(main())
