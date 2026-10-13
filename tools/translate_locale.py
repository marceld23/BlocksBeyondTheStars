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
Placeholders like {name} are validated per key (same set as English, no brace outside a token), and so are
markup tags (the Codex articles' <p>/<b>/<span …> — the same tags as English, #2508) —
a mismatch discards the chunk and retries
once, then leaves those keys untranslated (locale_report.py will list them).

Each request carries the target language's own wording from its base table (data/locales/<lang>.json): the menu
tabs and a few recurring terms (GLOSSARY_KEYS), plus the item/block/blueprint/mission names and quoted UI labels that
occur in that request's English strings — so a story line names "the Blueprints tab" or "the Titanium Drill" the way
the player's menu shows it (--no-glossary leaves this out). A value that comes back as the unchanged English sentence
counts as a failure like a broken placeholder.

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
PLACEHOLDER = re.compile(r"\{(?:key:)?[A-Za-z0-9_]+\}")  # {name} and the {key:Action} control tokens (#1077)
# A markup tag: the Codex articles' HTML subset (data/wiki/locales, #2508) and the few <b>/<code>/<color> tags of the
# other tables. A translation must carry exactly the English tags, attributes included.
MARKUP_TAG = re.compile(r"</?[A-Za-z][^<>]*>")


def same_markup(source: str, value: str) -> bool:
    """True when the translation carries the same markup tags as the English, attributes included (order may move
    with the sentence; tests/BlocksBeyondTheStars.Client.Tests/CodexTextGuardTests.cs holds the Codex to the block order)."""
    return sorted(MARKUP_TAG.findall(source)) == sorted(MARKUP_TAG.findall(value))


def has_stray_brace(text: str) -> bool:
    """True when a brace survives outside a token. The game substitutes tokens by plain replacement — no
    conditional or plural syntax — so a model's "{count?100:100}" would render literally (#1973)."""
    rest = PLACEHOLDER.sub("", text)
    return "{" in rest or "}" in rest

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
- Never ADD a placeholder or any brace syntax the English does not have — no plural or conditional forms like \
{{count?a:b}}. Where the English has a plain number, write the plain number.
- Keep formatting: leading/trailing punctuation, newlines (\\n), brackets, ALL-CAPS style where used.
- Some values are article markup (<p>, <ul>, <li>, <b>, <i>, <img src="…">, <span class="link" data-link="…">): keep \
every tag byte for byte, attributes included, in the same order, and translate only the text between the tags. Keep \
entities like &amp; as they are.
- Tone: friendly, concise, kid-appropriate. Use the informal address (German "du", French "tu", \
Spanish "tú", Portuguese "você", Polish "ty", Turkish "sen", Dutch "je/jij", Russian "ты", \nUkrainian "ти"). Japanese: friendly polite です/ます; Korean: friendly 해요체; Chinese: plain friendly register.
- Keep proper names untranslated: VEGA, Blocks Beyond the Stars, the Scout and Pioneer Service and its \
short form SPS (the old exploration service of the story — always the English name), and the wreck The Long Quiet. \
Game terms translate naturally and CONSISTENTLY across keys (e.g. blueprint, knowledge, suit energy, airlock).
- UI strings must stay short: if the English is one or two words, the translation should be too.
"""

# Terms the player sees on screen — menu tabs, screen titles, a few machines and places. A new sentence that says
# "open the Journal" or "the Blueprints tab" must use the word the player's menu actually shows, so the prompt carries
# these keys' existing translations from the target language's base table (#2459). Keys the target does not have
# yet are skipped.
GLOSSARY_KEYS = (
    "ui.tab.story", "ui.tab.tech", "ui.tab.map", "ui.tab.missions", "ui.tab.ship", "ui.tab.settings",
    "ui.tab.companions", "ui.tab.photos", "ui.tab.wiki", "ui.tab.achievements", "ui.tab.alliances",
    "ui.crafting.title", "ui.inventory.title", "ui.actions.title", "ui.station.cargo", "ui.vega.restart",
    "ui.journal.skip_step", "ui.journal.cat_tips", "ui.story.start_now",
    "block.workbench.name", "block.forge.name", "block.fabricator.name", "block.heal_tank.name", "module.medbay.name",
    "module.jump_generator.name", "module.radar_array.name", "item.comm_radio.name", "block.bio_lab.name",
    "ui.marker.mission_board", "ui.tech.cat_crystal", "item.access_code.name",
    "poi.guardian_core", "poi.observatory", "poi.alien_shrine",
)


# Keys whose text is a NAME the game shows somewhere (an item, block, blueprint, module, place, mission, tab). A story
# line that mentions "the Titanium Drill" or the job "First Light" must use the translated name the player sees in the
# inventory or the Missions tab — left alone, the model keeps capitalised names in English.
NAME_KEY = re.compile(
    r"^(?:item|block|blueprint|module|planet|poi|ship|mission|achv)\.[a-z0-9_.]+\.name$|^ui\.tab\.[a-z_]+$")
QUOTED = re.compile(r"[“\"‘]([^”\"’]{3,40})[”\"’]")
MAX_NAMES = 60


class Glossary:
    """The target language's established wording, added to each request's system prompt: the fixed on-screen terms
    (GLOSSARY_KEYS) plus the names and quoted UI labels that occur in that request's English strings."""

    def __init__(self, lang: str, language: str, enabled: bool = True) -> None:
        def table(code: str) -> dict:
            path = REPO_ROOT / "data" / "locales" / f"{code}.json"
            return json.loads(path.read_text(encoding="utf-8")) if path.exists() else {}

        self.language = language
        self.english = table("en") if enabled else {}
        self.target = table(lang) if enabled else {}
        self.fixed = [(self.english[k], self.target[k]) for k in GLOSSARY_KEYS if self.has(k)]
        self.names: dict[str, str] = {}
        for key, en in self.english.items():
            if NAME_KEY.match(key) and self.has(key) and len(en) >= 3 and "{" not in en:
                tr = self.target[key]
                self.names.setdefault(en, tr)
                # "Crystal Workshop: First Light" is quoted as "First Light" in a sentence
                if ":" in en and (":" in tr or "：" in tr):
                    sub_en = en.split(":", 1)[1].strip()
                    sub_tr = re.split(r"[:：]", tr, maxsplit=1)[1].strip()
                    if len(sub_en) >= 3 and sub_tr:
                        self.names.setdefault(sub_en, sub_tr)
        self.labels: dict[str, str] = {}
        for key, en in self.english.items():
            if self.has(key) and 3 <= len(en) <= 40 and "{" not in en:
                self.labels.setdefault(en, self.target[key])

    def has(self, key: str) -> bool:
        return key in self.english and bool(str(self.target.get(key, "")).strip())

    def block_for(self, texts) -> str:
        """The prompt block for one request ("" when there is nothing to add)."""
        text = "\n".join(texts)
        found: dict[str, str] = {}
        for en, tr in self.names.items():
            multi = " " in en
            pattern = r"(?<![\w-])" + re.escape(en) + r"(?![\w-])"
            if (multi or en[:1].isupper()) and re.search(pattern, text, re.IGNORECASE if multi else 0):
                found[en] = tr
        for quoted in QUOTED.findall(text):
            if quoted in self.labels:
                found.setdefault(quoted, self.labels[quoted])
        fixed = {en: tr for en, tr in self.fixed}
        found = {en: tr for en, tr in found.items() if en not in fixed}
        # the longest names first, so "Titanium Drill" is not crowded out by "Drill"
        named = sorted(found.items(), key=lambda p: -len(p[0]))[:MAX_NAMES]
        out = ""
        if fixed:
            out += (f"\n\nOn-screen terms of this game's {self.language} UI. When the English names one of these "
                    f"(a menu tab, a screen, a machine, a place), use exactly this {self.language} wording:\n"
                    + "\n".join(f"- {en} → {tr}" for en, tr in fixed.items()) + "\n")
        if named:
            out += (f"\nNames and labels in these strings as the {self.language} game shows them (items, blocks, "
                    "blueprints, missions, buttons) — use these names, inflected only where the grammar needs it:\n"
                    + "\n".join(f"- {en} → {tr}" for en, tr in named) + "\n")
        return out


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


def is_untranslated_copy(source: str, value: str) -> bool:
    """True when a sentence came back as the English source — the model sometimes returns a whole chunk unchanged.
    Short labels may legitimately read the same ("Journal", "Chat"), so only strings of four words or more count."""
    return value.strip() == source.strip() and len(re.findall(r"[A-Za-z]{3,}", PLACEHOLDER.sub("", source))) >= 4


def translate_chunk(api_key: str, model: str, language: str, chunk: dict[str, str],
                    timeout: float, effort: str, glossary: Glossary | None = None) -> dict[str, str]:
    """One API call for one chunk; returns only entries that pass validation."""
    system = SYSTEM_PROMPT.format(language=language) + (glossary.block_for(chunk.values()) if glossary else "")
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
        if set(PLACEHOLDER.findall(source)) != set(PLACEHOLDER.findall(value)) or has_stray_brace(value):
            continue
        if not same_markup(source, value):
            continue
        if is_untranslated_copy(source, value):
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
    ap.add_argument("--no-glossary", action="store_true",
                    help="leave out the target's established terms and names (GLOSSARY_KEYS, NAME_KEY) "
                         "read from its base table")
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
            json.dumps(ordered, ensure_ascii=False, indent=2) + "\n", encoding="utf-8",
            newline="\n")  # LF like the rest of the repo — text mode would write CRLF on Windows

    keys = list(missing)
    language = LANGUAGES[args.lang]
    glossary = Glossary(args.lang, language, enabled=not args.no_glossary)

    def translate_batch(batch: list[str]) -> dict[str, str]:
        """Translate one batch of keys, retrying once for request errors and validation failures."""
        chunk = {k: missing[k] for k in batch}
        got: dict[str, str] = {}
        for attempt in (1, 2):
            try:
                got.update(translate_chunk(api_key, args.model, language, chunk,
                                           args.timeout, args.effort, glossary))
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
