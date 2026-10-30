# Festivals — the real-world calendar in the game

**Status:** shipped 2026-10 (epic [#3000](https://github.com/marceld23/BlocksBeyondTheStars/issues/3000); foundation
#3001, calendar content #3002, announcement #3003, mood #3004; the pumpkin #3005, festival spawns #3006 and the
challenges #3007 follow in the second PR). Player-facing description: [USER_MANUAL.md](../user/USER_MANUAL.md)
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
  blocks), `shootingStars`, `music` (the Festive synth flavour: cosy, spooky, bright, calm).

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
The wall clock sits behind `SetFestivalClockForTest`, like the NPCs' welcome-back clock.

**VEGA's greeting** is once per player and festival-year (milestone `festival:<key>:<year>`), kind 3 (system) so the hints
mute never swallows it; the line key is `festival.<key>.vega`. **Story predicates** `world:festival_any` and
`world:festival_<theme>` let a pack start while a festival of its theme is on.

## Client

- **Main menu card** (`UiFestivalCard`, built onto the menu canvas by `UiMainMenu.Build` before the overlays): "Today: ✦
  Halloween" with the explanation, or the next festival within fourteen days; "More festivals" lists the next sixty days.
  Nothing is built without content or a festival near, so the Editor's hierarchy golden is unaffected.
- **HUD chip** next to the weather chip, the **celebration banner** when a festival begins while the player is online
  (`GameBootstrap.FestivalsBegan`), the Character tab's **world-rules row** (value = the festival that is on).
- **Mood** (`FestivalMood`, from `ServerRules.ActiveFestivals` in a game, the client calendar on the menu): motes in
  `AmbientParticles`, grade and fog in `Sky`, snow caps in `TickWeatherTraces`, `ShootingStars`, the `SynthMood.Festive`
  piece in `ClientMusic` (in Tracks mode every other piece). Settings → Comfort: **Festivals (calendar)** (global, passed
  to the bundled host) and **Festival mood** (this client's cosmetics).
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

A festival with a `spawn` block (the harvest festivals: Halloween, Erntedank, the two Thanksgivings) lays that block down on
**existing** worlds at runtime, on the weather snow's pattern — never through world generation, so natural blocks stay as
they are and old saves get the festival:

- every 3 s per world, at most `perPass` cells near each player on foot, never more than `cap` per world, loaded chunks
  only, breathable worlds only, never in a base zone or a ship, only the air cell on a host block (`hosts`) under the open sky;
- every placement is broadcast (`BlockChanged`) and written to the `weather_deposit` table — the same ledger as the snow,
  told apart by the block (`LoadWeatherDeposits` routes the rows into `FestivalDeposits`); the block-palette remap covers it;
- **cleanup:** while no festival spawns that block, the pass removes a few of its own deposits per pass — only cells still
  holding the festival block, so a player's own pumpkin is never touched;
- **the harvest stays harvested:** a mined deposit leaves the ledger (`ForgetFestivalDeposit`) instead of entering the
  regrow queue.

Cost: one pass per 3 s, 6 cells, one transaction; nothing while no festival spawns and the ledger is empty.

## Challenges (#3007)

Eight theme packs under `data/stories/festival_<theme>/` (side, player-scoped, `start.after: world:festival_<theme>`,
nothing skippable — a skipped pack must not pay): one `read` step that names the challenge and one counter step, kid-sized
(harvest 60 pumpkins; place 30 lights and, optionally, scan 10 conifers; craft 25 things; plant 25 plants; dye 10 things;
scan 5 bodies; build 3 campfires; eat 10 things). The graduation counter `festival:<theme>` is watched by an
`achievements.json` row of the `festival` category that pays a **decoration present** (pumpkin lanterns, coloured lamps,
light strips, flower pots, rugs, torches); the pumpkin harvest also has lifetime tiers on `mine:flora_pumpkin` (60 / 250 /
500). A pack runs once per player; the festival it belongs to can be switched on any day, so no present can be missed.

**Distinct scans:** `scan:block` (`GameEventKeys.ScanBlock`) carries the scanned block key and fires once per *cell* per
player — `ScanIntent` names the aimed cell (`HasCell`/`CellX..Z`, nothing from an older client), `PlayerState.ScannedCells`
remembers the cells (bounded, persisted), and the hand scanner is throttled to two scans a second. Conifers are
`pine_needles`, the crown block only they use.

## Tests

`FestivalCalendarTests` (windows, overlap, upcoming, parsers), `FestivalContentTests` (file shape, en/de texts, secular
wording, the yearly reminder), `FestivalServerTests` (old-save default, order of authority, live edit, `/festival`,
persistence, the greeting), `FestivalCardTests` (client card and chip). The pinned tables (`ServerConfigOptionsTests`,
`AdminCommandGateTests`, `StoryConditionTests`, `STORY_VOCABULARY.md`) carry the new rows.

## Adding a festival

One entry in `data/festivals.json` (key, theme, glyph, window, effects) and the three texts in `en.json` + `de.json`,
then `tools/translate_locale.py` for the other twelve. Keep the explanation to what people do and where, in two
sentences, with no rite in it; the content test checks the wording.
