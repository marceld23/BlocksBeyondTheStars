# Translating Blocks Beyond the Stars — the complete guide

How to translate the whole game into a new language — or improve an existing one. Steps 1–3
need no programming; steps 4–6 are the (small) wiring a maintainer does once per language.

English is the source language. German and English must stay complete; every other language
falls back to English **per missing key**, so a partial translation is always safe to ship.
The settings screen offers a language automatically once its coverage clears the picker bar
(45 % — `GameContent.SelectableLocales`); below that it can still be hand-set for testing.

## Where text lives (the map)

| Layer | Location | Format | What it covers |
|---|---|---|---|
| Main locale table | `data/locales/<code>.json` | flat `"key": "text"` JSON | All UI, HUD, menus, editors, minigames (`minigame.*`, `ui.minigame.*`), server messages (`srv.*`), block/item/planet/ship/blueprint names + descriptions, achievements (`achv.*`), missions |
| Story packs | `data/stories/<id>/locales/<code>.json` | same flat JSON | Story beats, VEGA prologue |
| Codex wiki | `data/wiki/articles.json` | per-article `{"en": …, "de": …, "fr": …, "es": …}` maps | Guide chapters (untranslated articles fall back to English) |
| What's New | `data/whatsnew.json` | `title_<code>` / `body_<code>` per entry | In-game changelog (translate new entries going forward; the backlog stays EN/DE) |
| AI backend | `ai-backend/app/llm.py` `_LANGUAGE_NAMES` | code → language name | LLM-generated NPC lines, VEGA banter, mission flavour |
| Web portal | `src/BlocksBeyondTheStars.WorldHost/Locales/<code>.json` | flat `"key": "text"` JSON, embedded in the assembly | play.blocksbeyondthestars.de (landing, My Worlds, rules, page chrome, API error texts) + the in-game community-rules screen through `GET /api/terms` |
| Browser-play shell | `client/Assets/WebGLTemplates/BlocksBeyondTheStars/index.html` | `BBS_SHELL_TEXT` map | The two words shown while the WebGL build loads ("Loading", "Fullscreen") |
| Launcher | nothing to do | reads `data/locales/<code>.json` itself | Splash screen |

Everything else (`blocks.json`, `items.json`, `missions.json`, `achievements.json`,
`minigames/catalog.json`, …) contains **keys, not text** — translating the locale table
covers it all automatically.

## Step 1 — The locale file

Generate the worklist instead of copying by hand:

```bash
uv run --no-project python tools/locale_report.py                      # all languages, coverage
uv run --no-project python tools/locale_report.py fr --missing ui.menu # keys + English text, paste-ready
```

Create/extend `data/locales/<code>.json`, one key group at a time (block, item, ui.menu,
achv, srv, minigame, …), keeping keys in the same order as `en.json`. Translate **values
only** — never keys.

Hard rules (CI fails on violations; `locale_report.py --check` finds them first):

- Placeholders like `{name}`, `{item}`, `{count}` must survive unchanged (position may move).
- No blank values, no invented keys.
- Keep formatting characters (`\n`, leading `[`, trailing `:`) intact.

Style rules:

- Kid-friendly tone, informal address (German "du", French "tu", Spanish "tú").
- Keep UI strings short — panels do not grow. One English word → one translated word if possible.
- Proper names stay: VEGA, Blocks Beyond the Stars. Translate game terms consistently
  (build a small glossary first: blueprint, knowledge, suit energy, airlock, …).

## Step 2 — Story packs

Same flat-JSON drill for `data/stories/<id>/locales/<code>.json` (copy the key list from the
pack's `en.json`).

## Step 3 — Test in-game without any code change

Set `"Language": "<code>"` by hand in `client_settings.json` and start the game — every
locale file present is loaded even when the picker does not offer the language yet. Check:

- Special characters render (accents, `œ « » ¡ ¿` — the bundled font covers Latin-Extended,
  but verify on screen).
- Long strings: settings buttons, hotbar hints, VEGA dialog pages, editor labels.
- `[some.key]` in brackets on screen = missing or typo'd key.

Open a PR per key group — small is genuinely better; CI validates defects automatically and
comments coverage.

## Step 4 — Wire a NEW language into the game (maintainer, once)

1. `src/BlocksBeyondTheStars.Shared/Localization/GameLocale.cs`: enum member + `Code()` +
   `TryParse` cases (code, `xx-xx`, English + native name) + `NativeName()`.
2. `client/Assets/BlocksBeyondTheStars/Scripts/StreamingAssetsCache.cs`: add
   `locales/<code>.json` (+ story-pack locale paths) to `FallbackManifest` — the real
   manifest is build-generated; the fallback list must not drift.
3. First-run OS-language default (optional): `ClientSettings.cs` maps `SystemLanguage`.
4. `ai-backend/app/llm.py`: add the code to `_LANGUAGE_NAMES` so LLM text follows the player.
5. The content-load failure dialog is hardcoded per language by design (the locale files are
   part of the content that failed): extend the switch in `AppShell.BuildContentErrorUi`.

The picker, coverage gating, launcher splash and everything key-driven need **no** change.

## Step 5 — Prose layers (optional, per language)

- **Wiki**: add your language to each article's `title`/`body` map in
  `data/wiki/articles.json`; high-traffic articles first. Untranslated ones fall back to EN.
- **What's New**: add `title_<code>`/`body_<code>` to NEW entries in `data/whatsnew.json`.
- **Web portal**: `src/BlocksBeyondTheStars.WorldHost/Locales/<code>.json` — same flat JSON as
  the game locales, translated the same way (`translate_locale.py`, see Step 6). It also feeds
  the in-game community-rules screen. `{rules}`/`{worlds}` are substitution slots and `%s` a
  runtime value: keep them, and keep the handful of `<b>`/`<code>` tags. The Impressum and
  Datenschutz **bodies** stay German (the legally authoritative text) — only their chrome and
  the plain-language summary card are translated.
- **Browser-play shell**: two words in `BBS_SHELL_TEXT`
  (`client/Assets/WebGLTemplates/BlocksBeyondTheStars/index.html`).

## Step 6 — Machine first pass (maintainer workflow)

For a language without a community translator (or to top up after new keys land):

```bash
uv run --no-project python tools/translate_locale.py fr --dry-run   # count + request estimate
uv run --no-project python tools/translate_locale.py fr             # translates only MISSING keys
uv run --no-project python tools/translate_locale.py fr --file data/stories/vega_protocol/locales/fr.json \
    --source data/stories/vega_protocol/locales/en.json
uv run --no-project python tools/translate_locale.py fr \
    --source src/BlocksBeyondTheStars.WorldHost/Locales/en.json \
    --file   src/BlocksBeyondTheStars.WorldHost/Locales/fr.json   # the web portal
uv run --no-project python tools/locale_report.py --check           # hard-defect validation
```

The tool is incremental and resumable, validates placeholder sets per key, and keeps
`en.json` key order. It reads `OPENAI_API_KEY` from the environment or
`tools/ai-assets/.env`. Spot-review names and recurring game terms afterwards; community
PRs then improve a playable language instead of starting from zero.

## "The complete game is covered" checklist

- [ ] `data/locales/<code>.json` at 100 % (`locale_report.py`)
- [ ] `data/stories/*/locales/<code>.json` complete
- [ ] Wiki articles translated (or accepted EN fallback)
- [ ] What's New: new entries carry the language
- [ ] AI backend maps the language name
- [ ] Portal + community rules translated (`src/BlocksBeyondTheStars.WorldHost/Locales/<code>.json`
      — `PortalLocalizationTests` fails on a gap, so this one is not optional)
- [ ] Browser-play shell carries the language (`BBS_SHELL_TEXT`)
- [ ] Content-error dialog switch extended
- [ ] Glyph + overflow spot-check done in-game
- [ ] Settings picker offers the language (coverage ≥ 45 %)
