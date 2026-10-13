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
| Story packs | `data/stories/<id>/locales/<code>.json` | same flat JSON | The VEGA story (`vega_protocol`: prologue, beats, chapters, leads, story missions, cutscene captions, lore) and the basic training with its short courses (`tutorial`) |
| Codex articles | `data/wiki/locales/<code>.json` (list and order: `data/wiki/articles.json`) | same flat JSON, `wiki.<id>.title` / `wiki.<id>.body`, bodies in a small HTML subset | The Codex guide chapter (an article your language does not have reads English) — see "Step 2b" |
| What's New | `data/whatsnew.json` (German + English) · `data-online/whatsnew/<code>.json` (every other language) | generated — do not edit | In-game changelog. The language files are exported from the devblog translations; a release without a translation reads English |
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

- Placeholders like `{name}`, `{item}`, `{count}` and key-binding tokens like `{key:ToggleLamp}` must
  survive unchanged (position may move).
- No other braces. Named tokens are filled by plain replacement; positional `{0}` slots go through
  `string.Format` after the key-binding tokens are expanded (`VegaText.Compose`, #2363). There is no
  plural or conditional syntax, so something like `{count?100:100}` is shown to players literally. Where
  the English has a plain number, write the plain number.
- No blank values, no invented keys.
- Keep formatting characters (`\n`, leading `[`, trailing `:`) intact.
- No word that mixes Latin and Cyrillic letters ("Uплінк", an Italian "Rottа" whose last letter is a Cyrillic а) and
  no combining accent (a stress mark like "Ста́йный", or "s" + U+0301 instead of "ś") — `CommunityLocaleTests`
  checks every table, the web portal's included (#2501).
- Terms in `CommunityLocaleTests.ForbiddenTerms` fail CI in their language: the English names "Guardian", "AI Core"
  (where the language has its own name), "Ship Keel/Helm/Engine", "Shipwright", "Self-built Ship", "Starter Ship",
  and the misfits tr "Hiperspace", pl "Fabryfikator", it "Fabricatore", fr "Étrier", nl "afmeren", uk
  "Страж"/"Сторожа" (#2501). The table below lists what to write instead.

Style rules:

- Kid-friendly tone, informal address (German "du", French "tu", Spanish "tú").
- Keep UI strings short — panels do not grow. One English word → one translated word if possible.
  VEGA's two answer buttons (`ui.vega.choice.*`, #2499) share one row of her panel: the right one reads
  "Let's go · [N]" in a pill of about 200 px, the left one "Hold [N]: I know the game — skip"
  (`ui.vega.choice.hold`) in about 330 px — roughly 12 and 30 characters for the labels in Latin scripts (German:
  "Kenne ich schon – überspringen"), fewer in CJK. The Official Worlds status chip (`ui.portal.needs_update`, a 220 px cell) reuses the portal's own
  `worlds.js.status.needs_update` word.
- Never name a key in the text ("press Tab", "E at the cockpit"): write the `{key:Action}` token instead
  (`{key:UiMenu}`, `{key:Interact}`, `{key:PlanetMap}`, …). The game shows the player's own binding, the pad
  button, or on a tablet the on-screen button (USE, MAP, ≡) — a literal key is wrong on two of three devices
  (#2463). Texts that expand tokens: VEGA's lines and the story/tutorial texts, every `@srv.*` server message, the
  HUD prompts (stations, crafting stations, bed, heal tank, transporter pad, factory terminal, loot/stash/feed,
  scanner, wreck repair, base core, station/dock/trade hints, data cube and net fragment labels), the keyboard
  controls line and the flight/EVA lines (keyboard and pad — in the pad's `ui.space.controls_pad` /
  `eva_controls_pad` keep " · ", a dot with a space on each side, between the controls and keep every token: the game
  drops a control the player left without a button segment by segment, #2507), the chat toast, the crate buttons and filter hint, the launch and
  vendor/workshop questions ("Launch ({key:Interact})"), the map/chart details and the creative-flight, photo and
  discovery hints (#2502), and the item, block, blueprint and ship-module descriptions (#2503 — the crafting,
  inventory, Blueprints and ship panes and the Codex's lists: "press {key:Interact}", "Use {key:Place} to …",
  "{key:LootContainer} to take"), and the Codex articles (`data/wiki/locales/<code>.json`, #2508 — "press
  {key:Interact}", "hold {key:Jump}", "the planet map ({key:PlanetMap})").
- In a short "X: verb" prompt a tablet reads an action without its own touch button as **ACT** (`ui.touch.actions`,
  the label of the button that opens the action list); VEGA's longer lines say "the on-screen action menu"
  (`ui.vega.key_touch`). A token reads differently per device, so it takes no case ending or particle: Turkish
  writes "{key:Interact} tuşuna bas", Korean "{key:Interact} 키를 누르세요" (for the mouse actions `Place`, `Mine`,
  `Scan`: "{key:Place} 버튼으로" — on a pad and a tablet they are buttons), Japanese "{key:Interact} を押す"
  (#2502, #2503). A pad glyph wears its own parentheses ("(X)"); "Launch ({key:Interact})" then reads "Launch (X)",
  never "((X))".
- A text the **server** composes itself and sends as plain text (`npc.streamer.ask`, the NPC greetings, `srv.*` keys
  the server `Localize`s directly) must not name a key at all — neither a letter nor a token (the client would show
  the braces). Say "come and talk to me", the HUD names the key (`VegaLineBudgetTests` guards the known ones).
- VEGA's speech panel holds about 220 characters per page. English and German VEGA lines (`vega.*`, and
  `story.vega.*` in the story pack) stay within one page: at most 220 shown characters in English and 264 in
  German (+20 %), counting a `{key:…}` token as 3 characters and a `{0}` slot as 10 (`VegaLineBudgetTests`).
  Aim for the same in other languages; a longer line pages instead of being cut off.
- Proper names stay: VEGA, Blocks Beyond the Stars, the **Scout and Pioneer Service** and its short form **SPS**
  (the story's old exploration service — English in every language, German included), and the wreck **The Long
  Quiet** (the game shows its name in English). "The Service" on its own is an ordinary noun and is translated.
  Translate game terms consistently (build a small glossary first: blueprint, knowledge, suit energy, airlock, …).
- A sentence that names a menu tab or screen ("open the Journal", "the Blueprints tab", "menu → Crafting") uses
  the word that tab shows in your language (`ui.tab.*`, `ui.crafting.title`, …), not a new synonym. There is no
  "tech tree" in the game: blueprints are researched "under Blueprints" (`ui.tab.tech`).
- The menu tab with the character page, "People you know" and the in-world options (world rules, VEGA hints, the
  tutorial switch) is **Character** (`ui.tab.settings` — the key kept its old name when the tab was renamed from
  "Settings", #2505); it uses the same word as the page's own `ui.settings.character`. **Settings** (`ui.menu.settings`,
  `ui.settings.title`) is the separate screen from the main menu and the pause menu: graphics, sound, controls,
  comfort, language. A text that sends the player to their look, their friends, the world rules or the tutorial switch
  names the Character tab; one about graphics, keys or comfort options names Settings.
- Running text that names an item, block, blueprint or module uses that thing's `.name` (inflected where the grammar
  needs it). Rename a thing, and every sentence that names it follows.
- VEGA is female: her first-person lines take feminine forms where the language marks them (ru "поняла",
  "я проверяла"; uk "я перевірила"; pl "wykryłam"; pt "preocupada").

### Canonical terms per language (#2501)

The majority form across `ui.tab.*`, the `.name` keys, the VEGA story and the reviewed tutorial. A new text uses
these; `translate_locale.py` feeds the names to the model from the base table.

| Lang | Blueprints tab | Hyperspace tab | Jump Generator | Radar Array | Guardian / its core | AI core | Ship keel · helm · engine |
|---|---|---|---|---|---|---|---|
| es | Planos | Hiperespacio | Generador de salto | Antena radar | El Guardián / Núcleo guardián | Núcleo IA Mk2 | Quilla · Timón · Propulsor de nave |
| fr | Plans | Hyperespace | Générateur de saut | Antenne radar | Le Gardien / Noyau Gardien | AI Core Mk2 | Quille de vaisseau · Poste de pilotage · Propulseur de vaisseau |
| it | Progetti | Iperspazio | Generatore di Salto | Array Radar | Il Guardiano / Nucleo Guardiano | AI Core Mk2 | Chiglia · Timone · Motore della nave |
| ja | 設計図 | ハイパースペース | ジャンプジェネレーター | レーダーアレイ | ガーディアン / ガーディアン・コア | AIコア Mk2 | 船のキール · 操縦席 · 船のエンジン |
| ko | 도면 | 하이퍼스페이스 | 점프 엔진 | 레이더 어레이 | 가디언 / 가디언 코어 | AI 코어 Mk2 | 함선 용골 · 조타석 · 함선 엔진 |
| nl | Blauwdrukken | Hyperruimte | Spronggenerator | Radararray | De Bewaker / Bewakerskern | AI-kern Mk2 | Scheepskiel · Scheepsroer · Scheepsmotor |
| pl | Schematy | Nadprzestrzeń | Generator skoku | Zestaw radarowy | Strażnik / Rdzeń Strażnika | Rdzeń AI Mk2 | Kil statku · Sterownia · Silnik statku |
| pt | Plantas | Hiperespaço | Gerador de Salto | Matriz de Radar | O Guardião / Núcleo Guardião | Núcleo de IA Mk2 | Quilha · Leme · Motor da Nave |
| ru | Чертежи | Гиперпространство | Генератор прыжка | Радарная решётка | Страж / Ядро Стража | Ядро ИИ Mk2 | Киль · Штурвал · Двигатель корабля |
| tr | Planlar | Hiperuzay | Atlama Jeneratörü | Radar Dizisi | Koruyucu / Koruyucu Çekirdek | Yapay Zeka Çekirdeği Mk2 | Gemi Omurgası · Gemi Dümeni · Gemi Motoru |
| uk | Креслення | Гіперпростір | Генератор стрибка | Радарна антена | Охоронець / Серце Охоронця | Ядро ШІ Mk2 | Кіль · Штурвал · Двигун корабля |
| zh | 蓝图 | 超空间 | 跳跃发生器 | 雷达阵列 | 守护者 / 守护核心 | AI 核心 Mk2 | 飞船龙骨 · 飞船驾驶台 · 飞船引擎 |

Notes: nl "helm" is the helmet (`item.helmet.name`), so the ship's helm is "roer"; nl docking is "aanmeren", undocking
"loskoppelen" ("afmeren" means to moor). pl names the Fabricator "Fabrykator", uk the jump lane "стрибкова смуга". fr
and it keep the English "AI Core" in all four names on purpose. "The Service" (the old SPS) is translated in it
("il Servizio"), while the full name "Scout and Pioneer Service" stays English everywhere.

## Step 2 — Story packs

Same flat-JSON drill for `data/stories/<id>/locales/<code>.json` (copy the key list from the
pack's `en.json`). Both shipped packs have all twelve community files: `vega_protocol` (the VEGA story) and
`tutorial` (the basic training and its short courses, #2459). A pack's keys are looked up over the base table, and a
key missing from a pack's `<code>.json` reads that pack's English. A **new** pack file (a new pack, or the first
`<code>.json` of a pack) must also be listed in the WebGL `FallbackManifest`
(`client/Assets/BlocksBeyondTheStars/Scripts/StreamingAssetsCache.cs`), which
`StoryUiFixesClientTests.TheBuiltInFallbackList_MatchesTheDataFolder` compares with `data/`.

Line keys an older server still sends stay in the tables even when the current game no longer uses them
(`vega.resume`, `vega.s.dock.start_far`, `vega.sys.crystal_first`): a new client can join an older server — the
protocol version did not change — and a missing key would show as `[vega.resume]`. Purely client-side keys can be
deleted from all 14 files once nothing reads them.

## Step 2b — Codex articles

The Codex guide's articles (#2508) are flat tables too: `data/wiki/locales/<code>.json`, two keys per article —
`wiki.<id>.title` and `wiki.<id>.body` — in the order of `en.json` (which follows the article list
`data/wiki/articles.json`). English and German hold every article. Any other language may hold any subset: an
article reads in your language only when your table has its title **and** its body; otherwise the whole article
reads English (never your title over an English body). So translate an article completely or leave both keys out.

A body is a small HTML subset (`WikiMarkup`). Rules on top of the hard rules above (`CodexTextGuardTests`):

- Translate only the words between the tags. Keep every tag byte for byte, attributes included —
  `<span class="link" data-link="cat:blocks">`, `<img src="wiki/img/circuit_lift.png">` — and keep `&amp;` as it is.
- Keep the structure: the same paragraphs (`<p>…</p>`), list items (`<li>`) and pictures in the same order. Within
  one paragraph or list item the inline tags (`<b>`, `<i>`, `<span …>`) may move with your word order — Japanese,
  Korean or Turkish often put the bold object before the verb — but keep each pair, and close them in order
  (`<b>…<i>…</i>…</b>`, never `<b>…<i>…</b>…</i>`).
- Keep every `{key:Action}` token (they name the player's own key, pad button or touch button — "press E", "(X)",
  "USE"); the token rules above apply (no case ending or particle on the token itself).
- Name menu tabs, items, machines and places with your table's own words (the canonical terms above, `ui.tab.*`, the
  `.name` keys). The menu tab with the character page is **Character** (`ui.tab.settings`, #2505).

Adding or changing an article is a developer step (`MINIGAMES_AND_WIKI.md`, "Codex articles"): the English and German
texts land first, the community languages follow with the next translation pass — until then the article reads
English there. A changed English article: delete its two keys from the twelve community tables, then re-translate.

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
   `locales/<code>.json` (+ story-pack locale paths and `wiki/locales/<code>.json`) to `FallbackManifest` — the real
   manifest and the build-time fallback list (`Resources/streaming_data_files.txt`, #2462) are generated by the
   WebGL build; the built-in last-resort list must still match `data/`, which
   `StoryUiFixesClientTests.TheBuiltInFallbackList_MatchesTheDataFolder` checks.
3. First-run OS-language default (optional): `ClientSettings.cs` maps `SystemLanguage`.
4. `ai-backend/app/llm.py`: add the code to `_LANGUAGE_NAMES` so LLM text follows the player.
5. The content-load failure dialog is hardcoded per language by design (the locale files are
   part of the content that failed): extend the switch in `AppShell.BuildContentErrorUi`.

The picker, coverage gating, launcher splash and everything key-driven need **no** change.

## Step 5 — Prose layers (optional, per language)

- **Codex articles**: `data/wiki/locales/<code>.json` (Step 2b); high-traffic articles first. A new table file must
  also be listed in the WebGL `FallbackManifest` (`StreamingAssetsCache.cs`, like the pack files in Step 2).
  Untranslated articles read English.
- **What's New**: nothing to translate by hand. The release notes are the devblog release posts, and
  the devblog is translated into all twelve other website languages by the maintainer's (git-ignored)
  blog-sync tooling, which also writes `data-online/whatsnew/<code>.json` — the files the game fetches
  online for the player's language (routine: AGENTS.md, release section; format:
  [data-online/README.md](../../data-online/README.md)). A new game language therefore needs the
  website language first. Improving a translation: fix the blog post, and the next sync carries it
  over. Do not add fields to `data/whatsnew.json` — the export rewrites that file at every release.
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
uv run --no-project python tools/translate_locale.py fr --chunk 4 \
    --source data/wiki/locales/en.json --file data/wiki/locales/fr.json   # the Codex articles (long bodies: small chunks)
uv run --no-project python tools/locale_report.py --check           # hard-defect validation
```

The tool is incremental and resumable, validates placeholder sets and markup tags per key (a value whose tags differ
from the English is retried, #2508), and keeps `en.json` key order (LF line endings). It reads `OPENAI_API_KEY` from the environment or
`tools/ai-assets/.env`. Every request carries the target language's own on-screen terms — the menu tabs, screen
titles and a few machines and places (`GLOSSARY_KEYS`, read from `data/locales/<code>.json`; `--no-glossary` leaves
them out) — so translate the base table before a pack whose texts name those tabs. A key whose English text
changed must be **deleted** from the community tables first: the tool only fills missing keys.

Spot-review names and recurring game terms afterwards (it still gets short labels wrong without context: a bench
seat becomes a *workbench*, a docking port a *harbour*, a short course a *flight heading*, a lead a *metal*); community
PRs then improve a playable language instead of starting from zero. After a pass, regenerate the picker's coverage
manifest with `python scripts/locale-coverage.py` (`LocaleCoverageManifestTests`).

## "The complete game is covered" checklist

- [ ] `data/locales/<code>.json` at 100 % (`locale_report.py`)
- [ ] `data/stories/*/locales/<code>.json` complete
- [ ] Codex articles translated: `data/wiki/locales/<code>.json` (or accepted EN fallback), listed in the
      `FallbackManifest`
- [ ] What's New: `data-online/whatsnew/<code>.json` exists (exported from the devblog translations; optional —
      missing releases read English)
- [ ] AI backend maps the language name
- [ ] Portal + community rules translated (`src/BlocksBeyondTheStars.WorldHost/Locales/<code>.json`
      — `PortalLocalizationTests` fails on a gap, so this one is not optional)
- [ ] Browser-play shell carries the language (`BBS_SHELL_TEXT`)
- [ ] Content-error dialog switch extended
- [ ] Glyph + overflow spot-check done in-game
- [ ] Settings picker offers the language (coverage ≥ 45 %)
