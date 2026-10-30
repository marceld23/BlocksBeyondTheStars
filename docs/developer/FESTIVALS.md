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

## Tests

`FestivalCalendarTests` (windows, overlap, upcoming, parsers), `FestivalContentTests` (file shape, en/de texts, secular
wording, the yearly reminder), `FestivalServerTests` (old-save default, order of authority, live edit, `/festival`,
persistence, the greeting), `FestivalCardTests` (client card and chip). The pinned tables (`ServerConfigOptionsTests`,
`AdminCommandGateTests`, `StoryConditionTests`, `STORY_VOCABULARY.md`) carry the new rows.

## Adding a festival

One entry in `data/festivals.json` (key, theme, glyph, window, effects) and the three texts in `en.json` + `de.json`,
then `tools/translate_locale.py` for the other twelve. Keep the explanation to what people do and where, in two
sentences, with no rite in it; the content test checks the wording.
