# Festivals — the real-world calendar in the game

**Status:** shipped 2026-10 (epic [#3000](https://github.com/marceld23/BlocksBeyondTheStars/issues/3000); foundation
#3001, calendar content #3002, announcement #3003, mood #3004; the pumpkin #3005, festival spawns #3006 and the
challenges #3007 follow in the second PR). Follow-up epic [#3019](https://github.com/marceld23/BlocksBeyondTheStars/issues/3019):
spawner and scanner guards #3020, "My festivals" #3021, core days and priority #3022, Halloween's and Christmas' own
challenges #3023, decorations #3024, jingles #3025, Orthodox Christmas #3026. Player-facing description: [USER_MANUAL.md](../user/USER_MANUAL.md)
§ "Festivals". Decision record: [ADR 0020](adr/0020-seasonal-festivals-calendar-sets-defaults-no-fomo.md).

## What it is

At certain times of the real year the game plays with a slightly different mood: a cosy-spooky Halloween week, lantern-lit
December nights, cherry-blossom petals at Hanami, shooting stars at Tanabata, colours at Holi and Carnival. The game is
multicultural — fifty-two festivals from the fourteen language communities and beyond, each with a two-sentence,
kid-friendly explanation of what people do and where. The texts stay secular (lanterns, lights, harvest, blossoms, stars),
never a rite; `FestivalContentTests` guards that.

Two rules shaped the design (see the ADR):

- **No FOMO.** [mission.md](../strategy/mission.md) says no to timers and fear-of-missing-out. The calendar therefore sets
  the **default only**: inside a window a festival switches itself on; outside, a world admin (and every singleplayer, who is
  the admin of their own worlds) can switch any festival on by hand with `/festival <key>`. Rewards (second PR) are decoration
  only and progress persists, so nothing can be missed.
- **Server is the truth, the bundle is the schedule.** The server decides from its bundled `data/festivals.json` and its UTC
  clock; the client shows the same calendar in the main menu from the same file and the same code. No online fetch.

## Data: `data/festivals.json`

```jsonc
{
  "datesFrom": 2027, "datesThrough": 2028,
  "festivals": [
    { "key": "halloween", "theme": "harvest", "glyph": "✦",
      "window": { "kind": "fixed", "month": 10, "day": 31, "daysBefore": 7, "daysAfter": 1 },
      "effects": { "motes": "lamplight", "warmth": -0.05, "saturation": 0.9, "fogTint": "#a89cc8", "fogBoost": 0.35, "music": "spooky" } },
    { "key": "thanksgiving_us", "theme": "harvest", "glyph": "❋",
      "window": { "kind": "weekday", "month": 11, "day": 22, "weekday": "Thursday", "daysBefore": 1 } },
    { "key": "advent", "theme": "lights", "glyph": "✧",
      "window": { "kind": "weekday", "month": 11, "day": 27, "weekday": "Sunday", "untilMonth": 12, "untilDay": 23 } },
    { "key": "diwali", "theme": "lights", "glyph": "✧",
      "window": { "kind": "dates", "ranges": [["2026-11-06", "2026-11-10"], ["2027-10-27", "2027-10-31"]] } }
  ]
}
```

- **Windows are whole UTC days.** `fixed`: a month/day anchor every year, widened by `daysBefore`/`daysAfter` or ended by
  `untilMonth`/`untilDay`. `weekday`: the first named weekday on or after month/day (Thanksgiving, Erntedank, Advent).
  `dates`: explicit inclusive ranges for the lunar, Hebrew, Islamic and Easter-bound calendars. A window over New Year
  (Kwanzaa, Hanukkah 2027) belongs to both years.
- **Dated festivals must cover every year from `datesFrom` to `datesThrough`** (content validation).
  `FestivalContentTests.DatedFestivals_CoverNextYear_OrTheFileNeedsExtending` turns red on 1 January of `datesThrough`: add
  the next year's ranges and raise the number. The file's header comment lists the sources the dates were checked against.
- **Several festivals may overlap** (Christmas and Kwanzaa, Chuseok and the Mid-Autumn Festival, Qixi and Chilseok). The
  *primary* festival is the one that started last; ties keep the file's order. The chip and the mood follow the primary one,
  the menu card names up to three.
- **Themes** (`FestivalThemes`: harvest, lights, newyear, blossoms, colours, stars, midsummer, children) group what the
  festivals share under the hood — the challenge packs and spawns are per theme, the texts per festival.
- **Texts:** `festival.<key>.name`, `.explain` (two sentences: what and where) and `.vega` (the greeting) in all fourteen
  locale files; `festival.theme.<theme>` names the themes. Glyphs are single plain symbols (☀ ❄ ★ ✧ …) — the game font
  draws no colour emoji.
- **Effects** (client, cosmetic): `motes` (an `AmbientMotes` style by name), `warmth` (−1 … 1 grade shift), `saturation`
  (multiplier), `fogTint` + `fogBoost` (a cosy haze, at most 40 % nearer), `snowCap` (the snow-cap shader global, no snow
  blocks), `shootingStars`, `music` (the Festive synth flavour: cosy, spooky, bright, calm), and since #3022 `coreDays`
  (the mood shows on the window's first N days only, default 3, `0` = never, a large number = the whole window) or
  `coreWeekday` (Advent: its Sundays). Card, chip, greeting and challenges keep the whole window.
- **Selection fields** (#3021, #3022, #3023): `big` marks the fourteen big world festivals (New Year, Lunar New Year, Carnival,
  Holi, Eid al-Fitr, Easter, Hanami, Midsummer, Mid-Autumn, Halloween, Día de los Muertos, Diwali, Hanukkah, Christmas);
  `locales` the language communities that mainly celebrate a festival (`*` = everybody) — an estimate the native-speaker
  review (#3008) keeps honest; `priority` lets a festival lead the chip and the mood over one that started later (Halloween
  and Christmas: 10); `pack` names a challenge pack of the festival's own (`festival_<key>`), which then no longer counts
  for its theme's pack. The content validation checks the codes, the core fields and the pack name.

### How often (#3019)

With all 53 festivals a festive mood would be on 163 days of 2027. "My festivals" and the core days bring that to about
37–43 days for a player with the default selection (2027, depending on the language); the calendar card still names every
celebrated festival a fortnight ahead.

## Server (`GameServerFestivals.cs`)

Order of authority, evaluated in `ComputeActiveFestivals`:

1. **Operator override** `ServerConfig.FestivalsOverride` — `--festivals` / `BBS_FESTIVALS`: empty = nothing to say,
   `off` = no festivals on this server, a festival key = that one on every world. Never written into a save (the chat
   filter's pattern). The bundled singleplayer host receives the client's global "Festivals" setting this way, so an
   existing save is never touched. WorldHost forwards `BBS_WH_FESTIVALS` as `BBS_FESTIVALS` to every instance.
2. **World rule** `GameRules.Festivals` (default on; old saves deserialize without the field and read on — every existing
   world gets the festivals without a lift). Live-editable by the world admin in the Character tab and by `/festival on|off`.
3. **Manual pick** `GameRules.FestivalPick` — `/festival <key>` or `SetWorldRulesIntent.FestivalPick`; persisted with the
   world's rules, so a Halloween switched on in May stays on until `/festival calendar`.
4. **The calendar** — `FestivalCalendar.ActiveOn(content.Festivals, FestivalUtcNow)`.

`TickFestivals` (a pre-world tick step) looks every 30 s of uptime; a change re-sends `ServerRules` to everyone online and
VEGA greets the festivals that began. `ServerRules.Festivals` ("On"/"Off", empty from an older server),
`ServerRules.FestivalPick` and `ServerRules.ActiveFestivals` (primary first) are additive fields — no protocol bump.
The wall clock sits behind `SetFestivalClockForTest`, like the NPCs' welcome-back clock; the server test assembly pins every
server's default clock to a quiet day (`TestFestivalClock.QuietDay`, 1 May 2031, via `GameServer.FestivalClockDefaultForTest`)
so the suite never depends on the real date, and the client harness passes `FestivalsOverride = "off"` for the same reason.

**VEGA's greeting** is once per player and festival-year (milestone `festival:<key>:<year>`), kind 3 (system) so the hints
mute never swallows it; the line key is `festival.<key>.vega`. **Story predicates** `world:festival_any` and
`world:festival_<theme>` let a pack start while a festival of its theme is on; `world:festival_halloween` and
`world:festival_christmas` (#3023) start those two festivals' own packs, and a festival with its own pack no longer counts
for its theme's predicate.

### My festivals (#3021, `GameServerFestivalPrefs.cs`)

Each player celebrates their own selection of what is on: a `FestivalPreset` (Shared `FestivalSelection`) — `BigAndLanguage`
(the default: the big world festivals plus the ones of the player's language), `All`, `Language`, `None` — or their own list
(`Custom`). The client keeps it per installation (`ClientSettings.FestivalPreset`/`FestivalCustom`) and sends it with the
join (`JoinRequest.FestivalPreset`/`FestivalCustom`, so the join's own greeting already follows it) and on every change
(`SetFestivalPrefsIntent`, tag 313, served while the singleplayer pause holds the world). It is never saved on the server;
an older client that sends nothing gets the default for its join locale.

`ActiveFestivalsFor(session)` is what one player sees of the world's list: the festivals they celebrate, ordered by
`priority`, then the one that started last. It feeds that player's `ServerRules.ActiveFestivals` (chip, banner, mood), VEGA's
greeting (a festival that becomes celebrated while it is on is greeted at once), the story predicates (all of them take the
player) and the spawner. A festival switched on by hand (`/festival <key>`, the operator's key) is **forced**: it shows for
everybody whatever their selection, its mood on every day (`ServerRules.FestivalForced`). The world rule and the operator
override stay above everything. The bundled singleplayer host no longer gets an operator override from the client — the
selection does that job live, in the desktop host and the browser's alike (#3020).

## Client

- **Main menu card** (`UiFestivalCard`, built onto the menu canvas by `UiMainMenu.Build` before the overlays): "Today: ✦
  Halloween" with the explanation, or the next celebrated festival within fourteen days. Nothing is built without content or
  a celebrated festival near, so the Editor's hierarchy golden is unaffected. "More festivals" opens **My festivals**
  (`UiFestivalPicker`, #3021): every festival by its next start with its explanation and a "Celebrate" switch, the four
  presets on top; built as the canvas' last child when pressed, so nothing draws over it (#3017). Settings → Comfort has the
  same as a preset row and "Choose festivals…".
- **HUD chip** next to the weather chip, the **celebration banner** when a festival begins while the player is online
  (`GameBootstrap.FestivalsBegan`), the Character tab's **world-rules row** (value = the festival that is on).
- **Mood** (`FestivalMood`, from `ServerRules.ActiveFestivals` in a game, the client calendar on the menu): motes in
  `AmbientParticles`, grade and fog in `Sky`, snow caps in `TickWeatherTraces`, `ShootingStars`, the `SynthMood.Festive`
  piece in `ClientMusic` (in Tracks mode every other piece). Since #3022 only on the leading festival's core days
  (`FestivalCalendar.IsCoreDay`), every day for a forced one; the answer is cached per rules message and UTC day, since the
  scene scripts ask every frame. Settings → Comfort: **My festivals** (#3021) and **Festival mood** (this client's
  cosmetics).
- **Jingles** (#3025): the festival banner plays `festival_<key>` (Halloween has its own), else `festival_<theme>` — eight
  short sounds in `client/Assets/Resources/audio`, about 49 KB each — and the plain success chime when the festival mood is
  off.
- `FestivalCard` (Client.Core, Unity-free) holds the card model, the chip text and the began-diff; `FestivalCardTests`.

## The pumpkin (#3005)

A real plant of the game, not a festival prop: `flora_pumpkin` is a **solid wild species** of the green worlds from terrain
generation 32 (`WorldDescription.PumpkinGeneration`) — rolled into a world's roster like every species (`FloraTag.Lush`: 85 %
on a lush world, 40 % elsewhere, toxic on a few), harvested for the fruit `pumpkin` (or `toxic_pumpkin`, which the detoxifier
washes) and `pumpkin_seed`, planted again from the seeds on grass, soil or a hydroponic tray. `pumpkin_lantern` is the carved
one: a warm, dyeable light block (pumpkin + torch by hand). Save version 8, block ids shifted (the usual palette remap).

**Every pumpkin its own colour, with no traffic:** the species carries a *palette* (`FloraCatalog.Species.Palette`,
`FloraPalettes.Pumpkin`: mostly orange, then yellow, pale green, white, blue-grey) and the mesher's `TraitPaletteFlora`
colours each cell from `FloraTints.PaletteAt(cell, palette)` — the rainbow class's idea with an authored list, drawn as a
rigid solid body like the saguaro (bark mode 4). The tile stays a plain mid-orange.

## Festival spawns (#3006, `GameServerFestivalSpawns.cs`)

A festival with a `spawn` block (the harvest festivals: Halloween, Erntedank, the two Thanksgivings, and since #3023 Sukkot
and the autumn equinox) lays that block down on **existing** worlds at runtime, on the weather snow's pattern — never through
world generation, so natural blocks stay as they are and old saves get the festival:

- every 3 s per world, at most `perPass` cells near each player on foot **who celebrates that festival** (#3021), never more
  than `cap` per world, loaded chunks only, breathable worlds only, never in a base zone or a ship, only the air cell on a
  host block (`hosts`) under the open sky;
- **never on what players made** (#3020): no player edit in the 3×3×3 box around the cell and its host (`HasPlayerBlockEdits`,
  one box query per candidate), not in a settlement, and at least two blocks away from a player, an NPC or a creature — a
  pumpkin is solid;
- every placement is broadcast (`BlockChanged`) and written to the `weather_deposit` table — the same ledger as the snow,
  told apart by the block (`LoadWeatherDeposits` routes the rows into `FestivalDeposits`); the block-palette remap covers it;
- **cleanup:** while no player here celebrates a festival that spawns that block, the pass removes a few of its own deposits
  per pass — only cells still holding the festival block, so a player's own pumpkin is never touched; with nobody on the
  world the world's own festivals decide, so nothing is cleared overnight;
- **the harvest stays harvested:** a mined deposit leaves the ledger (`ForgetFestivalDeposit`) instead of entering the
  regrow queue.

Cost: one pass per 3 s, 6 cells, one transaction; nothing while no festival spawns and the ledger is empty.

## Challenges (#3007)

Eight theme packs under `data/stories/festival_<theme>/` plus, since #3023, **Halloween's and Christmas' own packs**
(`festival_halloween`, `festival_christmas`, started by `world:festival_halloween` / `world:festival_christmas` — those two
festivals no longer start their theme's pack). All are side packs, player-scoped, nothing skippable (a skipped pack must not
pay): one `read` step that names the challenge and kid-sized counter steps — Halloween: harvest 60 pumpkins (optionally put
up 3 pumpkin lanterns); Christmas: scan 25 different fir trees (optionally put up 10 lights); harvest: bring in 60 plants
(`harvest:any`, every harvest counts — Chuseok, Mid-Autumn and the equinox have no pumpkins on an older world); lights: place
30 lights; new year: craft 25 things; blossoms: plant 25 plants; colours: dye 10 things; stars: scan 5 bodies; midsummer:
build 3 campfires; children: eat 10 things. The graduation counter `festival:<pack>` is watched by an `achievements.json` row
of the `festival` category that pays a **decoration present** (the festival decorations below, pumpkin lanterns, coloured
lamps, light strips, flower pots, rugs, torches); the pumpkin harvest has lifetime tiers on `mine:flora_pumpkin` (60 / 250 /
500), the fir trees on `scan_tree:conifer` (25 / 100). A pack runs **once per player** — no yearly restart (decided
2026-10-30); the festival it belongs to can be switched on any day, so no present can be missed.

**Distinct scans:** `scan:block` (`GameEventKeys.ScanBlock`) carries the scanned block key and fires once per *cell* per
player — `ScanIntent` names the aimed cell (`HasCell`/`CellX..Z`, nothing from an older client), `PlayerState.ScannedCells`
remembers the cells (bounded, persisted), and the hand scanner is throttled to two scans a second. Since #3020 the server
counts only a loaded cell within reach that really holds the named block. **Trees, not cells** (#3023, `GameServerScanTrees.cs`):
`scan:tree` walks from a scanned leaf or log to its trunk's foot (the nearest log column under the crown, then down) and fires
once per tree per player (`PlayerState.ScannedTrees`, bounded, persisted) with the tree's kind by its crown — `conifer` for
`pine_needles`, `broadleaf`, `palm`. A crown without a trunk, or a trunk whose foot a player placed, counts nothing.

## Festival decorations (#3024)

Four blocks that belong to many festivals, in one change (block ids shift once — save version 9, the usual palette remap):
the **gift box** (white paper with a red ribbon, dyeable), the **paper lantern** and the **diya lamp** (warm dyeable lights;
the diya sits on the flower pot's form), and the **marigold** (`flora_marigold`, a flower planted from `marigold_seed` on
grass, soil, a tray or a flower pot). The marigold is an *authored* catalog species: in no world roster and no greenhouse
(`CultivatedKeys` unchanged), so no world generation changes. All craft by hand from early things; marigold seeds also come
from the gatherers' market and from every marigold harvested. They are the presents of the challenges where they fit.

## Tests

`FestivalCalendarTests` (windows, overlap, upcoming, parsers), `FestivalContentTests` (file shape, en/de texts, secular
wording, the yearly reminder), `FestivalServerTests` (old-save default, order of authority, live edit, `/festival`,
persistence, the greeting), `FestivalCardTests` (client card and chip, the celebrated-only card, the "My festivals" list),
`FestivalFollowUpTests` (#3019: presets, core days, the per-player list, forced festivals, the greeting of a newly celebrated
festival, the intent during the pause, the own packs' predicates, the spawner's guards, trees counted once, `harvest:any`). The pinned tables (`ServerConfigOptionsTests`,
`AdminCommandGateTests`, `StoryConditionTests`, `STORY_VOCABULARY.md`) carry the new rows.

## Adding a festival

One entry in `data/festivals.json` (key, theme, glyph, window, effects) and the three texts in `en.json` + `de.json`,
then `tools/translate_locale.py` for the other twelve. Keep the explanation to what people do and where, in two
sentences, with no rite in it; the content test checks the wording.
