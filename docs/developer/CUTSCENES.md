# Cutscenes — data format, wire, seen ledger, client director

Story packs stage their big moments as **data-driven cutscenes** (#2467, story-packs step 8): a pack's
`data/stories/<pack>/cutscenes.json` describes each scene (shots, cameras, captions as locale keys, grade, sound, options);
the **server** only decides *when* a player gets which scene and keeps a per-player **seen ledger**; the **client**
resolves the scene from its own copy of the file and plays it with the **cutscene director** (#2468, step 9 — see
[The client director](#the-client-director)). Everything is additive: no protocol bump, no save-version bump.

Related: [STORY_IMPLEMENTATION.md](STORY_IMPLEMENTATION.md) (story engine, the player-pack runtime, the wire table),
[TUTORIAL_PACK.md](TUTORIAL_PACK.md) (pack manifest and bindings), [STORY_PACK_AUTHORING.md](STORY_PACK_AUTHORING.md)
(#2483: the authoring guide — a pack's scenes in context, the pilot side story's card and banner as the example) and
[STORY_VOCABULARY.md](STORY_VOCABULARY.md#cutscenes-cutscenesjson) (the camera types, anchors, caption styles, grades and
budgets generated from the code).

## Presentation tiers

| Tier | Name | What the player sees | Camera / input / HUD | World |
|---|---|---|---|---|
| T0 | VEGA line | today's panel line — **not a cutscene** | free / free / kept | runs |
| `1` | Banner | top banner: icon, kicker, one line, 3–4 s | free / free / kept | runs |
| `2` | Card | title card or beat card in the frame (letterbox, speaker box) | **free / free** / dimmed | runs |
| `3` | Staged scene | 1–5 shots on real objects, captions in the frame, grade, music | taken / frozen / hidden | SP holds, MP runs |
| `4` | Set piece | multi-part sequence (B12 reveal, finale, ending) | taken / frozen / hidden | SP holds, MP runs |

Binding rules (synthesis §5.3 "Stufen"): a banner or card **never takes the camera** (only `player_view` / `overlay`
shots), never holds the world and never freezes the controls; only a T3/T4 scene that takes the camera may hold a
singleplayer world or lock input. One scene plays at a time.

## `cutscenes.json`

```jsonc
{
  "cutscenes": [                                    // a bare array of cutscenes is accepted too
    {
      "id": "prologue",                             // unique in the pack; letters, digits, _ - . ; "__…" is built in
      "tier": 3,                                    // 1 banner, 2 card (default), 3 scene, 4 set piece
      "chapter": "awakening",                       // optional: a chapter of the pack (Story-tab grouping, recap)
      "template": false,                            // see "Templates" below
      "replaceLineKeys": [ "vega.prologue.1" ],     // narrator lines the scene shows itself
      "music": "music_mystery_signal",              // a file in client/Music (no extension); empty = keep the music
      "arg": "",                                    // what fills {0}: "" = the bound chapter, "body", "system" (#2473)
      "shots": [
        {
          "camera": { "type": "orbit", "anchor": "ship" },
          "duration": 12,                           // seconds, > 0 and <= 120
          "grade": "none",                          // none | memory | flashback
          "captions": [
            { "style": "subtitle", "key": "vega.prologue.1", "at": 0, "hold": 0 }
          ],
          "sfx": [ { "cue": "ai_blip", "at": 0 } ]  // a clip in client/Assets/Resources/audio (no extension)
        }
      ],
      "options": {
        "holdWorld": true, "hideHud": true, "lockInput": true,   // null/absent = derived from the tier
        "skippable": true, "minDisplay": 0,
        "chainNext": [ "tutorial/tut.welcome" ],
        "cooldownExempt": false, "replayable": true,
        "returnOnSkip": []                                       // #2474: replaced lines a skip must not lose
      }
    }
  ]
}
```

| Field | Default | Meaning |
|---|---|---|
| `id` | — | the ledger name (`story:<pack>:cut:<id>`); the official packs use the ids of synthesis §5.3 (below) |
| `tier` | `2` | presentation tier, see above |
| `chapter` | `""` | the chapter the scene belongs to; must exist in the pack's `chapters` |
| `template` | `false` | one definition for several bindings (each binding is its own instance) |
| `replaceLineKeys` | `[]` | lines a client playing the scene keeps out of the VEGA panel (they still reach the log). Resolved from the client's **own** definition, so a client without the scene drops nothing |
| `music` | `""` | the scene's track |
| `arg` | `""` | #2473: what fills the captions' `{0}` when the server plays the scene — `""` the bound chapter (its title, place and count, see [the argument](#bindings--where-a-pack-plays-a-scene)), `body` the name of the body the player is on (the tutorial's first landing), `system` the name of the player's star system |
| `shots[]` | — | at least one; played in order |
| `shots[].camera.type` | `player_view` | `player_view` (letterbox + captions over the live view), `overlay` (on a running flight sequence, never moves the camera), `static`, `orbit` (radius by ship size), `crane`, `sky` (tilted up from the player; always clear), `chart` (the galaxy chart; #2474: a `memory`/`flashback` grade tints it sepia, the `core` anchor rings the revealed Guardian system and draws a violet route to it), and since #2477 the moves `dolly`, `pan`, `flyover` and the synthetic stages `space_vista`, `diorama` — see [Cameras](#cameras) |
| `shots[].camera.anchor` | `""` = `player` | `player`, `ship`, `planet`, `station` (the space station the player is aboard), `star_map`, `heal_tank` (the own ship's medbay on this world; #2474: a `static` shot frames it up close inside the room, and a scene whose camera shot needs it plays as a card where it is not found), `core` (the Guardian core: the coordinates the server sends with the play — the shaft mouth for the descent, the column for the breach; #2478: a `static` shot frames the column up close inside its chamber; on a `chart`: the Guardian system), `custom` (the coordinates of the `CutscenePlay` message), `players` (#2478: on a `chart` only — the other players' names on their systems; a scene whose camera shot needs it plays as a card when nobody else is on the star map). A `diorama` shows what its anchor names: `core` or `star_map` (#2477); a `space_vista` reads `ship` and `planet` |
| `shots[].duration` | — | seconds (a page-gated caption may hold the shot longer) |
| `shots[].grade` | `none` | `none`, `memory` (VEGA's memory, sepia), `flashback` |
| `captions[].key` | — | locale key in the core or the pack table; English **and** German mandatory; `{0}` = the play's argument |
| `captions[].style` | `subtitle` | `subtitle` (lower third, paged at sentence ends), `title_card` (kicker + title), `speaker` (speaker box: avatar, name, typewriter, voice), `reading_frame` (framed panel over the dimmed scene — recap, epilogue), `credits` |
| `captions[].kickerKey` | `""` | `title_card`: the small line above the title; `reading_frame`: its heading (#2478 — the recap, the epilogue's title) |
| `captions[].speaker` | `""` = the pack's narrator | `speaker` only: who talks — the narrator's id or one of the pack's `speakers` (#2478, `story.json`: id, `nameKey`, `icon`, `color`, `lineStart`, like the narrator; VEGA's pack has `core`, the Guardian core) |
| `captions[].at` / `hold` | `0` / `0` | seconds into the shot; how long it stays (`0` = to the end of the shot) |
| `sfx[].cue` / `at` | — / `0` | the clip and the second it plays |
| `options.holdWorld` | T3/T4 taking the camera | hold a singleplayer world while the scene plays |
| `options.hideHud` | tier ≥ 3 | hide the HUD (a card only dims it) |
| `options.lockInput` | T3/T4 taking the camera | freeze the controls |
| `options.skippable` | `true` | the player may skip |
| `options.minDisplay` | `0` | seconds before a skip is accepted (against an accidental double press) |
| `options.chainNext` | `[]` | scenes that may follow this one **at once** instead of after the cooldown: `<id>` (same pack), `<pack>/<id>`, `__recap`. Chaining only lifts the cooldown — it never plays the follower by itself |
| `options.cooldownExempt` | `false` | a "now or never" moment (first launch, after-warp card, finale arrival) — never waits for the cooldown |
| `options.replayable` | `true` | the Story tab may replay it once seen |
| `options.returnOnSkip` | `[]` | #2474: keys of `replaceLineKeys` the player must not miss — when the player **skips** the scene before such a line's caption was fully on screen, the client gives the line back to the narrator panel (VEGA's Guardian reveal, the one line that names the jump generator). Every other replaced line stays in the log only after a skip. Each key must be one of the scene's `replaceLineKeys` (validator) |

The model is `Shared/Story/CutsceneDefinition.cs` (`StoryCutsceneFile`, `CutsceneDefinition`, `CutsceneShot`,
`CutsceneCamera`, `CutsceneCaption`, `CutsceneSfx`, `CutsceneOptions`, the vocabularies `CutsceneTiers`,
`CutsceneCameras`, `CutsceneAnchors`, `CutsceneCaptionStyles`, `CutsceneGrades`, and `StoryCutsceneIds`).
`ContentLoader.LoadPackCutscenes(packDir)` reads the file into `StoryDefinition.Cutscenes`; `StoryDefinition.FindCutscene(id)`
finds a definition (an instance id finds its template). The built-in VEGA fallback (`StoryRegistry.Default`) carries no
cutscenes — its narrator lines still play.

**A new file under `data/` must be listed in the WebGL `FallbackManifest`** (`client/.../StreamingAssetsCache.cs`;
`StoryUiFixesClientTests.TheBuiltInFallbackList_MatchesTheDataFolder` fails otherwise). Whether a `cutscenes.json`
reaches the browser is only proven by a local WebGL build (PR CI builds no Unity).

### Cameras

Every camera type (`CutsceneCameras`), what the player sees, what it needs, and what it falls back to when the place cannot
carry it (`CutsceneStaging.Chain`; the director stages each shot when it starts — see [the client director](#the-client-director)).
Every fallback chain ends in `player_view`, which is always possible. A T1 banner or a T2 card uses only the first two
(validator); every other type takes the camera, so only a T3/T4 scene may use it.

| Type | What the player sees | Needs | Fallback chain |
|---|---|---|---|
| `player_view` | the live view under the letterbox and the captions | — | — |
| `overlay` | the same, on a running flight sequence (the camera is never touched) | — | — |
| `static` | a fixed framing 9 blocks (× ship size) from the anchor with a slow push-in; at the `heal_tank` a close framing inside the medbay (#2474), at the `core` the same inside the Guardian core's chamber (#2478: 2.4–4 blocks from the red column, under the glass panes) | loaded terrain, a clear spot and line of sight | orbit → crane → sky → view |
| `orbit` | circling the anchor (16 × size out), ping-ponging across the open arc when the circle is blocked | ≥ 110° of open arc | crane → sky → view |
| `crane` | rising above the anchor, looking down | ≥ 60° of open arc | orbit → sky → view |
| `sky` | from the player's eye, tilted up into the sky, slowly turning | the open sky (not aboard, not in the flight view) | view |
| `chart` | the galaxy chart between the letterbox and the speaker box; #2478: at the `players` anchor the other players' names on their systems, in live colours even under a memory grade | the star map | sky → view |
| `dolly` (#2477) | a straight, eased push-in from 14 to 8 blocks out (× size), sliding 20° around the anchor; it settles at its end while a caption still waits | every point of the path a clear camera spot that sees the anchor (the higher leg 18 → 11 when the low one is blocked) | static → orbit → crane → sky → view |
| `pan` (#2477) | turning in place across ±35° (±20° when tight) and back: from just above the player's head (2.2 blocks — never the own avatar), facing the view, else the sides or behind; or from a vantage point 9 blocks across the anchor (a ship) | the view open 6 blocks in every direction of the sweep | static → sky → view |
| `flyover` (#2477) | a high arc 18 blocks out (× size) at 16 (else 24) blocks up over 110°, rising 6 more in the middle, always looking at the anchor | every sampled point of the arc clear | crane → orbit → sky → view |
| `space_vista` (#2477) | a synthetic space panorama: the stars, the nebula, the sun and a living world; at `planet` the world close, at `ship` the player's **own** ship drifting by in front of it (no ship for a player who never flew — never one that is not theirs) | the Medium or High quality preset | sky → view |
| `diorama` (#2477) | a small synthetic stage: at `core` the **sleeping Guardian core** — a column of red light in an iron shell open to the front (the finale chamber's blocks, 130 blocks, ≤ 200), the camera rising along it while the red halo breathes slowly (0.4 Hz); at `star_map` **VEGA's memory chart** — the star map's systems as soft glows on a disc (sepia under a memory grade), the camera drifting from the player's system out to the rim, where something faint and red pulses beyond the last star (no name, no marker) | the Medium or High quality preset (the chart: the star map) | sky → view |

- **Moves.** A move is checked along its whole path when the shot starts (`CinematicStageScan`: unloaded chunks blocked,
  the parked hull blocks the camera spot) and flown over the shot's length with an ease-in-out (`CutsceneMoves`): the dolly
  and the flyover settle at their end, the pan turns back and forth while a caption waits for the reader. The rig's per-frame
  safety net keeps the last clear pose if a late chunk blocks the line of sight. "Camera motion" off: still framings — the
  dolly's close end, the pan's and the flyover's middle.
- **Synthetic stages** (`CutsceneStage` on the client, the layout and camera paths in Client.Core `SyntheticStages`) need no
  terrain, so they work wherever the player is — on the ground, aboard, in a station or in the flight view; beats fire on
  score, not on a place. The stage stands far above the world with its own camera drawn over the world camera, which keeps
  running but draws nothing meanwhile; it is built from the menu's attract-scene pieces (the real starfield and nebula in
  menu mode, the layered sun glow, a phase-lit planet with its cloud shell, the ship mesher) and the always-included
  shaders only — no new shader, texture, keyword or light. The backdrop is built once per world and kept hidden between
  shots; a shot's set is freed when the shot ends.
- **Low presets.** On Potato and Low the synthetic stages are skipped: a scene whose only camera shots are synthetic plays
  as a **card** (`CutsceneRules.NeedsCardFallback`, the same captions on the live view, free input, nothing held); a scene
  with another camera shot keeps it and lets the synthetic shot fall back to the sky. A stage that cannot be built (a
  stripped shader, no content yet, no star map) is staged without it.
- **The shot's grade** reaches a synthetic stage through the post stack like the world (VEGA's memory tones), and the
  memory chart tints its stars itself.

### Ids

- **VEGA** (`StoryCutsceneIds.Vega`): `prologue`, `ch1.open` … `ch7.open` (`ch7.open` = the B12 reveal, T4), the beat
  moments `b01`, `b03`, `b04`, `b07`, `b08`, `b10`, `fin.arrival`, `fin.descent`, `fin.breach`, `fin.duel`,
  `fin.standdown`, `resolution`.
- **Tutorial** (`StoryCutsceneIds.Tutorial`): `tut.welcome`, `tut.strip`, `tut.course`, `tut.done`, `tut.first_launch`,
  `tut.first_dock`, `tut.first_landing`, `tut.first_jump`, `tut.first_base`, `tut.ship_commissioned`, `tut.first_circuit`,
  `tut.first_tame`, `tut.graduation`.
- **Built in:** `__recap` — the "The story so far" card. Never defined or bound by a pack; the server sends it.

`CutsceneContentTests.TheOfficialPacks_UseOnlyTheBindingIds` holds the two official packs to these lists.
`vega_protocol/cutscenes.json` holds the `prologue`, bound as VEGA's `introCutscene` (and chapter 0's opener) since #2470 —
see [The VEGA prologue](#the-vega-prologue-2470) — and since #2474 the chapter scenes `ch1.open` … `ch7.open` and the beat
cards `b01`, `b03`, `b04`, `b07`, `b08`, `b10` — see [VEGA's chapter scenes and beat cards](#vegas-chapter-scenes-and-beat-cards-2474);
`tutorial/cutscenes.json` holds the basic training's cards since #2473 — see [The tutorial cards](#the-tutorial-cards-2473);
#2478 added VEGA's finale set pieces and its ending — see [VEGA's finale](#vegas-finale-2478) — so every VEGA id of the
list is defined now; #2479 added the flight courses' firsts `tut.first_dock` and `tut.first_jump`, #2480 the building
courses' `tut.first_base` and `tut.ship_commissioned`, #2481 the crystal workshop's banner `tut.first_circuit`, #2482 the
animal friends' banner `tut.first_tame` — every tutorial id of the list is defined now.

### The VEGA prologue (#2470)

The cold open of the game, "Awakening" (T3, chapter `awakening`, music `music_mystery_signal`, `holdWorld` + `lockInput`):

| Shot | Camera | Caption |
|---|---|---|
| 1 | `player_view` (out of black) | title card: kicker `ui.story.prologue` "Prologue", title `story.vega.ch.awakening` "Awakening" |
| 2 | `orbit` around the player's ship (terrain-aware, scaled to the ship) | subtitle `vega.prologue.1` |
| 3 | `static` close on the ship (the push-in) | subtitle `vega.prologue.2` |
| 4 | `player_view` — the cut to the pilot's seat, `ai_blip` | subtitle `vega.prologue.3` |
| 5 | `player_view` | VEGA's speaker box: `story.vega.beat00` (B0 — it counts as heard with the prologue) |
| 6 | `player_view` | VEGA's speaker box: `story.vega.call`, the call to adventure, the last page |

Subtitles and speaker pages follow "Story text: wait for me"; the title runs on its own. VEGA's speaker box glitches in for
her first words of a scene (`CutsceneDirector.GlitchIn`: a short glitch burst — never in the browser, none with Reduce
effects — and in a staged scene a soft flash that follows Reduce flashes). `replaceLineKeys` holds all five caption keys.
The server plays it for a newcomer to the story (no `story:vega_protocol:*` key at the join) or everyone online when a
"none" world switches to VEGA — once, never on a tutorial restart; "Watch again" lists it as "Prologue — Awakening". A
client without `cutscenes1` (an older build, a local player) and the built-in fallback pack (no cutscenes) get the old
five full-screen pages instead (Kind 4: the three narration keys, B0, and the call as server-composed text). The
`VegaPanel` + `PrologueCinematic` path stays only for a server that predates this (it sends Kind-4 pages to everyone); a
current server never sends both. `chainNext` holds `__recap` and `ch1.open` (#2474) — the story track starting in the same
join: "the story so far" or chapter 1's card follows at once — and `tutorial/tut.welcome` (#2473): when the basic training
starts, its welcome card follows the cold open at once.

### The tutorial cards (#2473)

The basic training gets **cards, not cinema** (synthesis §6.4; `data/stories/tutorial/cutscenes.json`). A card never takes
the camera, never holds the world, never freezes the controls and shows **titles only** — never the VEGA line the panel says
next (G12); title captions run on their own, so a card never stops to wait for "continue" while the player walks on. The
graduation is the one card with a line: VEGA's closing line, which it takes out of the panel (`replaceLineKeys`).

| Id | Tier | Bound to (TUTORIAL_PACK.md) | Shots and captions | Pacing |
|---|---|---|---|---|
| `tut.welcome` | T2 card, chapter `b1` | part 1's `openCutscene` | title "Basic training" (kicker "Six short parts"), then "First steps" (kicker "Part 1 of 6") | VEGA's prologue chains to it: it follows the cold open at once |
| `tut.strip` | T2 card, template | `openCutscene` of parts 2–6 | title = the part's title, kicker "Basic training · Part n of 6" (`{1}`/`{2}` of the argument) | waits for the cooldown; dropped once its part is over |
| `tut.done` | T1 banner, template | every part's `closeCutscene` | "Part complete!" + the part's title | never waits (a banner) |
| `tut.course` | T2 card, template | every deep dive's `openCutscene` (C2–C5 since #2479, C1, C6, C7, C17 since #2480, C8, C14 since #2481, C9–C13, C15, C16, C18 since #2482) | kicker "Short course" + the chapter's title | waits for the cooldown; dropped once its course is over |
| `tut.first_launch` | T3, `overlay` | `b6.1` `onComplete` | "Lift-off!" (kicker "First launch"), then "Space begins right above the clouds" — a full letterbox over the launch rise, the flight's camera untouched, no hold | "now or never" (`cooldownExempt`) |
| `tut.first_dock` | T2 card, `"arg": "body"` (#2479) | `c4.1` `onComplete` (the stations course's docking) | title = the station's name, kicker "Docked"; then "Vendors and a mission board wait inside" | waits for the station's loading veil and the cooldown (not "now or never") |
| `tut.first_landing` | T3, `orbit`/`static` on the ship, `"arg": "body"` | `b6.7` `onComplete` (not the stand-in `b6.7a`: a known world is no new one) | title = the world's name, kicker "Touchdown on a new world"; subtitle "Every world has its own rocks, plants and creatures." — a singleplayer world holds while the camera circles the ship | waits for the landing's loading veil (so not "now or never"); chains to the graduation |
| `tut.first_jump` | T2 card, `"arg": "system"` (#2479) | `c5.8` `onComplete` (the navigator course's hyperjump) | title = the new system's name at 2.8 s (after the warp), kicker "New system"; then "Unknown stars show “?” until you have been there" | "now or never" (`cooldownExempt`); the server raises `hyperjump:any` after the warp-in's flight state, so the card lands on the warp like `fin.arrival` |
| `tut.first_base` | T3, `crane` then `orbit` on the player (#2480) | `c7.1` `onComplete` (the homestead course's base founded) | "Your home" (kicker "Base founded"), then the subtitle "Walls keep animals away — and on airless worlds, sealed rooms hold air." — the camera rises over the player standing at the new core; a singleplayer world holds | waits for a safe moment and the cooldown; 10.5 s |
| `tut.ship_commissioned` | T3, `orbit`/`static` on the ship (#2480) | `c6.7` `onComplete` (the ship builder course's commissioning) | "Your own ship" (kicker "Commissioned"), then the subtitle "Built by you, block by block — and it flies." — the camera circles the **new** ship (the commissioned ship is the parked, active one now; the radius scales with its size); a singleplayer world holds | waits for a safe moment and the cooldown; 11.5 s |
| `tut.first_circuit` | T1 banner (#2481) | `c8.1` `onComplete` (the crystal workshop course's first job handed in — a switch lit a lamp) | "It works!" (kicker "First circuit") with `chime_1` — the camera, the HUD and the controls stay the player's (a building moment) | a banner never waits for the cooldown; it waits while a menu is open (the hand-in happens in the Missions tab), then shows; never for a job handed in before the course; 4 s |
| `tut.first_tame` | T1 banner (#2482) | `c9.3` `onComplete` (the animal friends course's first tame — by its favourite food or the translator) | "A new friend!" (kicker "Tamed") with `chime_1` — the camera and the controls stay the player's, the new companion is right in front of them | a banner never waits for the cooldown; never for a companion the player had before the course (`alreadyDoneIf world:companion_owned`), never in a replay; 4 s |
| `tut.graduation` | T2 card | `graduation.cutscene` | "Graduate" (kicker "★ Achievement"), then VEGA's speaker page `story.tutorial.graduated` | chains to `__recap` and `vega_protocol/ch1.open` (#2474): the graduation starts VEGA's track, whose recap or chapter 1 card follows at once |

Interruption budget (synthesis §6.4): within the six parts at most one card or scene per part, except part 6's flight firsts
— welcome, five part cards, first launch, first landing = 8 — plus the graduation card at the hand-over
(`TutorialCardsTests.TheBasicTraining_HasAtMostEightHeavierMoments_OnePerPart_ButTheFlightFirsts`). Part 1 has no separate
part card: the welcome is its opener (the first join shows only the prologue and the tutorial's start card, §6.2). Since the
card carries "Part n of 6", the parts' first step lines no longer say it. The tutorial's pacing relies on the general rules
below: the cooldown, chains, a part card dropped once its part is over, and a line held with its waiting card. A deep dive
(#2479) shows at most two cards — its course card and at most one first
(`TutorialDeepDivesTests.TheCourseCards_AreCardsAndFirsts_AtMostTwoPerCourse`); the docking card lives in the stations course,
so the basic training's budget stays at eight. The two building firsts (#2480) are short **scenes** (T3, synthesis §6.4) —
the deep dives' only camera moments: the base is seen from above, the new ship from outside.

### VEGA's chapter scenes and beat cards (#2474)

Maintainer decision (a) — "more and more prominent VEGA": every chapter of the arc opens with its scene; B5 "The Guardian",
B9 "What You Are" (at the player's **real** heal tank) and B12 (the Guardian system on the star map) are big scenes; every
other beat is a **beat card in the frame with free input**. All captions reuse the beat texts (no new lore) and the scenes
keep them out of VEGA's panel (`replaceLineKeys`; the Journal has them either way). Beats fire on score, not on a place, so
every shot is place-neutral (the player's view, the sky, the chart, a synthetic stage) or anchored to the player's own ship.
#2477 staged chapters 4 and 6 too (synthesis §6.5 lists them as T3) and gave chapters 3, 4 and 6 a synthetic stage; on the
Potato/Low presets those three play as cards again (their only camera shots are synthetic).

| Id | Bound to | Tier | Shots and captions | Sound / music |
|---|---|---|---|---|
| `ch1.open` | chapter `signature` (B1) | T2 card | title "Chapter 1" / "A Familiar Signature" — **title only**: a track may start before the world crossed B1; `chainNext: b01` | `chapter_sting` |
| `b01` | beat B1 | T2 card | VEGA's speaker box with B1 (no title — at a track start it follows chapter 1's card), memory grade | `chapter_sting_memory` |
| `ch2.open` | chapter `service` (B2) | T3 | title "Chapter 2" / "The Service", then the **star chart tinted sepia** (VEGA's memory: "It mapped these stars") with B2 in her box | `chapter_sting_memory`, `music_mystery_signal` |
| `b03`, `b04` | beats B3, B4 | T2 cards | "VEGA remembers" over the beat's title ("Not scattered — erased", "Ours, once"), then the beat in VEGA's box, memory grade | `chapter_sting_memory` |
| `ch3.open` | chapter `guardian` (B5) | **T3 prominent** | out of black onto "Chapter 3" / "The Guardian", then the **sleeping-core diorama** (#2477): the camera rises along the column of red light in its iron shell while the halo breathes and VEGA says B5 — no grade, the red must read (without the diorama: up into the open sky) | `chapter_sting_reveal`, `anomaly_hum`, `music_mystery_signal` |
| `ch4.open` | chapter `verdict` (B6) | T3 (#2477; a card before) | out of black onto "Chapter 4" / "The Verdict", then a **space vista** of a living world close (`planet`) in VEGA's memory tones while she says B6 — the darkest line stays words on a calm picture | `chapter_sting`, `music_mystery_signal` |
| `b07`, `b08` | beats B7, B8 | T2 cards | "VEGA remembers" / "Her stand", "The thought-arcs", then the beat | `chapter_sting_memory` |
| `ch5.open` | chapter `what_you_are` (B9) | **T3 prominent** | "Chapter 5" / "What You Are", then a close framing of the player's **real heal tank** inside the medbay with a slow push-in while VEGA says B9 — warm, no grade. **No own heal tank on this world** (the ship parked elsewhere, the code-box starter without a medbay): the scene plays as a card (`CutsceneRules.NeedsCardFallback`) | `chapter_sting_reveal`, `clone_tank_bubble`, `music_deep_space_lonely` |
| `b10` | beat B10 | T3 beat moment (#2478; a card before) | "VEGA remembers" / "Many minds", then the beat over the **live star chart with the other players' names** on their systems (the `players` anchor; synthesis §6.5 "T2; in MP T3"). **Alone** — nobody else on the star map — it plays as the card it was (`CutsceneRules.NeedsCardFallback`); the world never holds in multiplayer | `chapter_sting_memory` |
| `ch6.open` | chapter `sleeps` (B11) | T3 (#2477; a card before) | out of black onto "Chapter 6" / "It Still Sleeps", then **VEGA's memory chart** drifting from the player's system to the rim, a faint red pulse beyond the last star, memory grade, while she says B11 — no marker, no name | `chapter_sting`, `music_mystery_signal` |
| `ch7.open` | chapter `core` (B12 + the Guardian system revealed, one server call) | **T4** | the sky with B12; the **live star chart** with a violet route from the player's system to the newly revealed Guardian system (ringed, named) while VEGA says `story.vega.guardian_revealed` — the jump-generator line; then "Chapter 7" / "The Guardian Core". `returnOnSkip`: the reveal line comes back to the panel when the player skips before it showed | `chapter_sting_reveal`, `music_boss_approach` |

- **Titles.** The kicker `story.vega.cut.chapter` ("Chapter {1}") takes the chapter's number from the argument: a world
  story's arc chapters are numbered as the Story tab numbers them — by their index, the prologue's chapter is 0
  (`CutsceneArgs.PlaceOf`, `IsArcChapter`). Beat titles `story.vega.cut.b03.title` … are the beats' authored titles.
- **When.** A chapter scene plays at the beat that starts its chapter and once for the player's current chapter at their
  track start (after "the story so far"); a beat card at its beat — only for players whose story track started (the others
  get the beat with their track start). Chapter 1's card is followed at once by B1's card when B1 was already crossed
  (server `PlayOpeningBeatAfterTitle`, the chain `ch1.open → b01`). The prologue and the tutorial's graduation chain into
  `ch1.open`.
- **The join catch-up (B09).** A started player who missed beats while offline: the current chapter's opening is committed
  before any line; a `cutscenes1` client whose missed beats crossed into a later chapter gets "The story so far" first; a
  single missed beat plays its card ([STORY_IMPLEMENTATION.md](STORY_IMPLEMENTATION.md)).
- **Multiplayer.** Each player's track gets its own copy, deferred on its own; the world is never held in multiplayer (the
  client's `CutsceneRules.HoldsWorld`); after 30 s with the camera a scene goes on as a card.
- **Older clients** (no `cutscenes1`) and the built-in fallback pack get the beat lines as before; the scenes are stamped
  seen and never owed.
- **Stingers** (`client/Assets/Resources/audio`, ElevenLabs `gen_sound.py`, 2026-10-12, each ≤ 50 KB for the browser
  payload): `chapter_sting` (calm, a soft pad resolving on a chime), `chapter_sting_memory` (shimmering bells — VEGA
  remembers), `chapter_sting_reveal` (a wondrous rising chord — the big scenes); see `docs/developer/SOUND_DESIGN.md`.

### VEGA's finale (#2478)

The finale's set pieces (synthesis §6.5), bound in `story.json`'s `finale.cutscenes` (mirrored in `StoryRegistry`), each played
**once per player** (the ledger) and only for a player whose story track has started — never for an observer. The finale code
(`GameServerStoryFinale.cs`, `PlayFinaleCutscene`) plays each at its moment; anchors ride along as the `core` anchor.

| Stage → id | When (server) | Tier | What the player sees | Multiplayer |
|---|---|---|---|---|
| `arrival` → `fin.arrival` | the player's flight warps into the Guardian system (`JumpFlightToSystem`), while the core still waits | T2 card, "now or never" | "Arrival" over "The Guardian Core" as the warp clears (the title at 2.8 s, the warp lasts 2.6 s) — no camera, free input; with danger near it is dropped (the gauntlet banner still counts the machines); `chainNext: fin.descent` | only the pilot who warps in |
| `descent` → `fin.descent` | landing on the core world (`ShipAiWorldFlavour`, before the `guardian_core` hint) — not once the duel runs | T3 | the camera rises from the own ship, flies over the iron-ringed shaft (the server sends the shaft mouth; a shaft too far away to be streamed in shows the sky) while VEGA says the hint itself (`vega.hint.guardian_core`, token-clean since #2463 — no new key), then the sleeping-core diorama; the hint is kept out of the panel and given back on an early skip (`returnOnSkip`) | whoever lands |
| `breach` → `fin.breach` | the hack completes (`BeginCoreDuel`), for the finale's players **on the core world** | T3, "now or never" | a close framing of the red column in its chamber (the server sends the column), "The core wakes" over "The Guardian Core", `music_boss_dialogue`; then the duel dialogue | on the core world |
| `duel` → `fin.duel` | the hack completes, for the finale's players **elsewhere** | T1 banner | "The Guardian Core — The duel of arguments has begun at the core." (not replayable) | everyone else of the finale |
| `standdown` → `fin.standdown` | the duel is won (`WinDuel` → `DeliverResolution`), and at the next join / track start / end of observing for a started player who missed it | T4 | VEGA's resolution line (`story.vega.finale_resolved`, kept out of the panel) over the sleeping-core diorama, then the own ship before a living world with "The Guardian core powers down …"; `chainNext: resolution`; on Low/Potato a card | every started player |
| `resolution` → `resolution` | right after the stand-down; "Watch the ending again" replays it | T4 | the open sky under "Resolution — The VEGA Protocol", the credits rolling over a space vista on a dark stage (the director dims 0.85 while a `credits` caption shows), the epilogue in a reading frame headed "The relay network"; `music_boss_resolution`; the epilogue line is kept out of the panel | every started player |

- **The duel's dialogue mode** (E28; client, `CutsceneDirectorDuel.cs` + `DuelDialogueScreen.cs`, rules in Client.Core
  `CoreDuel.cs`): every `CoreDialogueMessage` (additive since #2478: `NodeCount`, `HasAnchor` + `AnchorX/Y/Z` — the column)
  feeds `CoreDuelState`; **on the core world** (landed — not its flight, not as an observer) the director shows the
  dialogue whenever no scene plays: a full letterbox, the camera close on the column (`CutsceneStaging` close framing, else
  the player's own view), the core's box under the top bar (the pack speaker `core`: its name, Guardian red, the anomaly's
  hum per node; its reply to the last answer in italics, then its statement; "Argument n of 4") and the answers as the
  dialogue's **own buttons** (mouse, pad via `UiNav`, touch) — never the hotbar digits. After a pick the buttons wait for the
  core's next node (or 4 s). HUD, touch layer and controls step aside (gate reason `duel`); a singleplayer world holds while
  it shows (`CutsceneRules.DuelHoldsWorld`; the answer passes the pause — `PausedMayHandle`); in multiplayer the camera
  returns after 30 s (the dialogue stays) and danger closes it until it is safe again (`CoreDuelRules.MayOpen` /
  `StepsAsideForDanger`). Esc / pad B / the corner button **steps back**; near the core (12 blocks) the use control
  (`{key:Interact}`, `ui.finale.duel_resume` from `FinaleView`) faces it again, and a new node reopens it by itself. The
  breach plays first. The IMGUI duel box and its number keys are gone (`FinaleView` keeps the duel's state for the music,
  which plays `boss_dialogue` only on the core world).
- **Server authority (B50):** `HandleCoreDialogueChoice` takes an answer only from a player **on the core world** (landed,
  not in flight — `OnGuardianCoreWorld`); whoever lands or joins while the duel runs gets its current node
  (`SendDuelNodeTo`) — a relog mid-duel used to leave the dialogue empty until somebody else answered.
- **The ending on the director, never twice:** `DeliverResolution` gives a `cutscenes1` client (on a pack that defines the
  ending) the stand-down and the ending as scenes — committed before the lines they show — and the legacy key
  `story:<id>:resolved` for older builds; any other client gets the resolution line, the epilogue (live) and
  `StoryResolved` as before. "Handled" = the ledger's seen key **or** its `:sent` key (`ResolutionHandled`), so a
  half-watched ending comes back through the cutscene catch-up, never a second delivery. `RequestStoryResolutionIntent`
  replays the director's ending (`Replay`) for a client that plays it. `ResolutionCinematic` stays for older servers.
- **The recap never folds the finale away:** a join backlog's recap collapse skips scenes bound to a finale stage
  (`IsFinaleScene`), so an owed stand-down and ending play as themselves after "The story so far". A re-offered descent or
  breach gets its place again (`FinaleAnchorFor`: the shaft mouth, the column — while the core world is loaded); other
  `custom` anchors are still not stored.

### Bindings — where a pack plays a scene

| Field | Plays when (server) |
|---|---|
| pack `introCutscene` | a **player** pack starts for the player — not on "I know the game" (`skipPack`), and not when the pack would complete silently for this player (every mandatory chapter through, by `autoCompleteIf` or by every step done / `alreadyDoneIf` — a veteran). The **world story's** intro (#2470) plays for a player who arrives with no key of the story (VEGA's prologue, above) |
| chapter `openCutscene` | a player pack: the chapter's first step is **presented** (activates with its line) — not during a replay, not for a chapter that completes silently. The world story (#2470): at the beat that starts the chapter (before its line), and once for the current chapter at the player's story-track start (never the intro) |
| chapter `closeCutscene` | a chapter the player **was shown** this session is done (`PlayerSession.PresentedPackChapters`) — not when skipped, replayed or completed silently by evidence |
| step `onStart.cutscene` | the step activates with its line (before the line; not in a replay) |
| step `onComplete.cutscene` | the step completes **non-silently** (it was the chip step, or a react step) — before the done line; not in a replay |
| beat `cutscene` | the beat reaches a player whose story track has started (#2470: `RevealBeatTo`, after the chapter opening, before the line) |
| pack `graduation.cutscene` | #2473: a player pack's graduation (its mandatory chapters finished by playing — not skipped, not by migration evidence), before the closing line; once per player, so a full restart's second graduation shows no card |
| `finale.cutscenes{arrival, descent, breach, duel, standdown, resolution}` | #2478, by the finale code — see [VEGA's finale](#vegas-finale-2478) (`StoryFinaleStages`; `duel` is the banner for the finale's players away from the core world, an addition to the synthesis' list) |

A scene goes out **before** its trigger's narrator line, so a client that plays it knows which lines it shows.

**Templates.** A `"template": true` scene (the tutorial's chapter card `tut.strip`, the banner `tut.done`) is one
definition for many bindings: each binding is its own **instance** `<id>@<owner>` (owner = the chapter id, the step id,
or the pack id for the intro), with its own ledger entry, and plays with the bound chapter's title key as its argument. A
plain scene plays **once per player** however often it is bound (the prologue can be the pack intro and chapter 0's
opener). On the wire and in receipts the instance id is the `CutsceneId`; the definition is the part before `@`.

**The argument** (`CutscenePlay.Arg`, the captions' `{0}`, `{1}`…; one rule for server and client,
`Shared/Story/CutsceneArgs.For`): a scene bound to a chapter — a template instance's owner chapter (or its owner step's), else
the scene's own `chapter` — sends the chapter's title **key** as `{0}` (a client localizes an argument that is a key), its
place among the pack's chapters of the same kind (mandatory among the mandatory ones, optional among the optional ones,
counted from 1) as `{1}` and how many there are as `{2}` (#2473: the tutorial's "Part 3 of 6"). A world story's arc
chapter (reached by a beat or an event) is numbered as the Story tab numbers it — by its index, the prologue's chapter is 0,
`{2}` the number of the last chapter (#2474: VEGA's "Chapter 3"). A scene with `"arg": "body"`
or `"system"` sends the name of the body / star system the player is on at that moment instead (a replay or a join re-offer
reads where the player is then). Several values are joined by U+001F (`LineText.ArgSeparator`); `LineText.Compose` fills
every slot, not only `{0}`. The server API may pass any text. For `__recap` it is the reached chapters' title keys in
chapter order — titles only, no beat text, so no spoilers. A template played without a binding (the admin preview, the dev
palette) plays as the instance of the first chapter that binds it (`CutsceneArgs.DefaultOwner`).

## Validation

`StoryPackValidator` (Shared, run by `StoryPackValidationTests` for every data pack and the test packs) and its twin
`tools/validate_story_pack.py` (`uv run --no-project python tools/validate_story_pack.py [<pack-dir>]`) check:

- every binding names a cutscene of the pack — never `__recap`, never an instance id (`graduation.cutscene` too, #2473);
  #2478: a `finale.cutscenes` key is one of `StoryFinaleStages` (`arrival`, `descent`, `breach`, `duel`, `standdown`,
  `resolution`);
- #2478: the pack's `speakers` — ids key-safe and unique next to the narrator's, a `nameKey` in both languages, a
  `#RRGGBB` colour, a `lineStart` sound that exists; a caption's `speaker` naming neither the narrator nor a speaker is a
  warning; the `players` anchor only on a `chart`;
- `arg` is empty, `body` or `system`;
- ids unique and key-safe, none starting with `__`; tier 1–4; at least one shot; known cameras, anchors, caption styles
  and grades (#2477: a `diorama` shows `core` or `star_map`); shot length > 0 and ≤ 120 s; captions and cues inside their
  shot; `hold` ≥ 0;
- the tier rules: a T1/T2 never takes the camera; `holdWorld` / `lockInput` only on a T3/T4 that takes it;
- every caption, kicker and `replaceLineKeys` key exists in English **and** German, within its style's budget
  (`subtitle` / `speaker` 220 EN / 264 DE — one VEGA panel page, the director pages it further; `title_card` title and
  kicker 60 / 72; `reading_frame` 600 / 720; `credits` none; a `{key:…}` token counts 3, a `{0}` slot 10);
- `chapter` names a chapter of the pack; `chainNext` resolves (an id of the pack, `<pack>/<id>` of an installed pack,
  `__recap`; a pack that is not installed is a warning); `music` exists in `client/Music`, every `sfx` cue in
  `client/Assets/Resources/audio`; every `returnOnSkip` key is one of the scene's `replaceLineKeys` (#2474);
- warnings: a `kickerKey` outside a `title_card` or a `reading_frame` (#2478: the reading frame shows it as its heading), a
  `speaker` outside a `speaker` caption, unknown fields (the Python tool; the C# tests read the file strictly).

## Wire (additive)

| Tag | Message | Dir | Fields |
|---|---|---|---|
| 295 | `CutscenePlay` | S→C | `PackId`, `CutsceneId` (definition or instance id, or `__recap`), `Arg`, `HasAnchor` + `AnchorX/Y/Z` (the `custom` anchor), `Replay` |
| 296 | `CutsceneSeenIntent` | C→S | `PackId`, `Ids[]` (as received), `Skipped`, `Unsupported` |
| 297 | `StoryPackIntent` | C→S | `Action = "replayCutscene"` with the additive `CutsceneId` — "watch again" from the Story tab |
| 139 | `StoryStateMessage` | S→C | additive `SeenCutscenes[]` (#2468): this player's confirmed scenes of every pack as `<pack>/<id>`, sorted — the Story tab's "Watch again" list |

- Only a client that announces **`cutscenes1`** in `JoinRequest.Features` (`ClientFeatures.Cutscenes1`) gets
  `CutscenePlay`. `NetworkClient.Features` holds `packs1` by default; `NetworkClient.PlaysCutscenes = true` adds
  `cutscenes1` — the Unity client sets it right before the join when its `CutsceneDirector` exists (a headless test client
  opts in the same way). A local player (`AddLocalPlayer`) does not get it.
- `CutsceneSeenIntent` and `StoryPackIntent{replayCutscene}` pass a held singleplayer world (`PausedMayHandle`: the
  director confirms while its own hold is still on). Observers (`/spectate`) cannot confirm.
- Both new messages are `NetCodec.Register`ed and in `NetCodecTests.ProtocolGoldenList`.

## The seen ledger (server, `GameServerCutscenes.cs`)

| Key (`PlayerState.Milestones`) | Written when |
|---|---|
| `story:<pack>:cut:<id>:sent` | the server **commits** the scene to the player — sent, or queued behind the cooldown or until the client is in the world |
| `story:<pack>:cut:<id>` | the client confirmed it (watched, skipped, downgraded, unsupported) — or the client cannot play cutscenes and the narrator line carried it |

`StoryPackKeys.CutsceneSent` / `CutsceneSeen` / `TryParseCutsceneSent` build and read them; `<id>` is the instance id for a
template. Rules:

- **Once.** A scene with either key is not played again (`PlayCutscene` returns false); an owed one comes back at the
  join.
- **Older clients** (no `cutscenes1`): nothing extra is sent — the trigger's own line carries the moment — and the scene
  is stamped **seen** at once, so it is never owed. Old clients drop tag 295 anyway.
- **Receipt.** Each id the pack defines, in the form the server sends it (a template only as an instance, a plain scene
  never as one, at most 32 ids of ≤ 96 characters), is stamped seen; unknown ids are ignored; `__recap` is not in the
  ledger; `Unsupported` stamps seen too, so a scene a build cannot play is not offered forever.
- **Join catch-up** (`CutscenesOnJoin`, before anything of the join can commit a new scene): every `:sent` without the
  seen key is queued again (pack order: definition order, then the bound chapter). A client without `cutscenes1` gets
  them stamped seen; a scene the pack no longer defines is dropped (stamped seen); a pack not installed on this server,
  or not enabled for the player (opted out, the world story switched off), keeps its promise. A **world story**'s backlog of **3 or more** chapter scenes (T2+ with a chapter) collapses into ONE
  `__recap` card with those chapters' titles (the scenes are stamped seen; the Story tab replays them) — never the pack's
  intro (the prologue is the cold open, not a chapter line; #2470).
- **Replay** (`replayCutscene`): a replayable scene the player has a key for (spoiler guard) is re-sent with
  `Replay = true` — any installed pack, the world story included; no ledger change; nothing for a client without
  `cutscenes1`. The ending's own "Watch the ending again" (`RequestStoryResolutionIntent`) replays the `resolution` scene the
  same way for a client that plays it (#2478), else it re-sends `StoryResolved`.
- **Observers** never get a scene (`PlayCutscene` refuses a spectating session, `PlayCutsceneForListeners` uses
  `StoryListeners()`).

### Pacing (per player, RAM)

`PlayerSession.CutsceneQueue` holds committed scenes not sent yet; `TickCutscenes` (every server tick, outside the
per-world loop, for every joined non-observing player — a pilot on a body whose world is not loaded, e.g. after an
in-flight hyperjump, included; #2468 moved it out of `TickShipAi`, which only reaches loaded worlds) and every new commit
flush it:

- **Client in the world first.** After a network join nothing goes out until the client's first position report
  (`LastMoveAt`) or 15 s (`CutsceneJoinSettleSeconds`) — the join burst never carries a scene.
- **Cooldown.** At most one unrequested T2+ scene per **90 s** of server time (`CutsceneCooldownSeconds`; the clock
  stands still while the world is held).
- **Banners** (T1) never wait (the client queues them behind a running scene).
- **Chains.** A scene named in the last sent scene's `chainNext` goes at once (prologue → `tut.welcome`, graduation →
  `ch1.open` / recap, B12 → reveal) — it may jump the queue. The built-in recap chains to its pack's next **chapter
  scene** (bound to a chapter, not the intro — `StoryCutsceneIds.AnyOfPack`, #2470): "the story so far" leads straight
  into the current chapter's opening at a story-track start. The client keeps the same rule (`QueuedCutscene.ChapterScene`).
- **Now or never** (`cooldownExempt`) goes at once and restarts the cooldown.
- **A chapter opener past its chapter** (#2473): a queued `openCutscene` of a player pack whose chapter the player finished
  or skipped meanwhile (the tutorial's part card behind the cooldown while a quick part went by) is dropped and stamped seen
  instead of announcing a part that is over (`IsStaleChapterOpener`).
- **Lines wait with their scene** (#2473): a narrator line that a scene still waiting in this queue shows itself (a key of its
  `replaceLineKeys`) is held with that scene and sent right after its `CutscenePlay` (`HoldLineForQueuedCutscene`, at the one
  line fill point `SendVegaWithObjective`), so the client keeps it out of the VEGA panel — without this, a card held back by
  the cooldown would arrive after its line and the player would read it twice (the graduation card). A held line goes out
  anyway when its scene leaves the queue unsent (dropped, confirmed); it is lost with the session only together with the
  scene, which the join re-offers with the text on it.
- The queue is lost with the session; the `:sent` key re-offers its scenes at the next join.

### Server API for later steps

- `PlayCutscene(session, packId, cutsceneId, arg = "", anchor = null, replay = false)` — one player; returns true when sent
  or queued.
- `PlayCutsceneForListeners(packId, cutsceneId, arg, anchor)` — every joined, non-observing player (beats, finale).
- `PlayRecap(session, packId, chapterTitleKeys)` — the built-in recap card.
- `PlayBoundCutscene(session, pack, cutsceneId, owner)` — a pack binding (template instance + chapter-title argument);
  used by the player-pack runtime.

Test seams: `PlayCutsceneForTest`, `PlayCutsceneForListenersForTest`, `PlayRecapForTest`, `TickCutscenesForTest`,
`CutscenesOnJoinForTest`.

### Admin preview: `/cutscene <pack> <id>`

`AdminCommandIntent{Command = "cutscene", StringArg}` (chat `/cutscene`, listed under `/help admin`) plays any scene of an
installed pack for the **asking admin only**, as a replay (`Replay = true`): no ledger, no receipt, no cooldown, nobody
else sees it. `StringArg` is `<pack> <id>`, `<pack>/<id>` or a bare id (the world story first, then any pack that defines
it); a template without an owner previews as the instance of the pack's first chapter, `__recap` lists every chapter
title, and the admin's position goes along as the `custom` anchor. World management, not a cheat (role-gated like
`/story`, so family worlds with cheats off can use it). Answers: `@srv.admin.cutscene_preview:<pack>/<id>`, or a rejection
`cutscene_usage` / `cutscene_unknown` / `cutscene_unsupported` (the admin's client has no director).

## Singleplayer and multiplayer

- **SP hold** only for T3/T4 scenes that take the camera, by the client's `WorldHoldArbiter` (`Cutscene` holder) after
  its streaming wait — never for an `overlay` on a flight, never for a card. The server keeps one pause flag per session.
- **MP** (synthesis §5.3 "MP-Schutz v1", done by the director): a safe moment before a scene starts, a danger interrupt
  (damage ends the scene, the text goes back to the panel), a 30 s cap on camera takeovers. **No** server grace period.
  Every player gets their own copy, deferred on their own; the director never asks for a hold in MP (it would be a group
  vote — the others' pause dialogs would wait for this player), replays included.

## The client director

Step 9 (#2468). The Unity-free rules live in Client.Core (`src/BlocksBeyondTheStars.Client.Core/Cutscenes/`, unit-tested in
`tests/BlocksBeyondTheStars.Client.Tests`); the Unity side presents them.

### Client.Core

| Type | What it decides |
|---|---|
| `CutscenePlan` | a definition (+ the play's `Arg`) → `PlannedShot`s with resolved `PlannedCaption`s: text through `LineText.Compose` with the one key-token resolver (`HudUi.KeyTokenGlyph`, passed in a `CutsceneTextSource`), pages (`CaptionPager`: subtitle 140, speaker 220, reading frame 600 characters, cut at sentence ends), timings. `Build`, `Recap` (the built-in card), `LocalBanner` ("Story moment saved"), `AsCard()` (the downgrade: same captions, no camera, no hold/lock/HUD) |
| `CutscenePlayback` | the clock: shot index and time, which page each caption shows, the hold at a page-gated caption's end, `Continue()` (completes a typing speaker box first), `CanSkip`, due sound cues, `TakeShotChanged()` |
| `SafeMomentGate` | `Evaluate(request, inputs)` → `Play` / `Defer` / `Abort` with a reason; `ShouldInterrupt` (danger while playing); `CameraCapReached` (MP 30 s) |
| `CutsceneQueue` | one scene at a time; dedupe by `pack/id`; requests (replays, previews, the dev palette) first; the 90 s cooldown after an unrequested T2+ (measured from its **end** — a scene that waited minutes must not be followed by the next one a second later); `chainNext` lifts it for the named follower; "now or never" and banners never wait |
| `CutsceneStaging` | a shot's camera with the fallback chain (requested → raised orbit → crane → sky → player view; [per type](#cameras)) on `CinematicStageScan` (unloaded chunks blocked, the parked hull blocks the camera spot) and `PrologueOrbit.Scale`; #2477: the moves' paths (`TryDolly`/`TryPan`/`TryFlyover`, checked along the whole path) and the synthetic stages (`syntheticAllowed`) |
| `CutsceneMoves` | #2477: how a move travels over its shot — eased one way (dolly, flyover: they settle) or back and forth (pan), the still framing with "Camera motion" off, the position and the pan's yaw at a progress; no allocation (the rig calls it every frame) |
| `SyntheticStages` | #2477: what a synthetic stage shows (`SetOf(camera, anchor)` → `SyntheticSet`), its layout in stage coordinates (the vista's planet, sun and ship path; the core diorama's ≤ 200 blocks; the memory chart from the star map via `GalaxyChartLayout`, its rim and the red glow beyond it) and its camera paths; the core's breath (`CorePulse`, 0.4 Hz) |
| `CutsceneRules` | (#2474) hold / freeze / heal-tank / card fallback; #2477: `SyntheticStagesAllowed(preset)` (Medium and up) and the card fallback of a scene made only of synthetic stages on the low presets |
| `CutsceneCatalog` | the "Watch again" rows (seen, replayable scenes of installed packs, in pack/definition order) and their names (title card "Prologue — Awakening", else the bound chapter's title, else the first caption) |

**Arguments.** `CutscenePlay.Arg` is split at U+001F; a value the localizer knows is localized (a chapter binding sends the
chapter's title **key**), anything else stays (a body name). For `__recap` the values are chapter title keys: the card
lists them as "✓ title" under "The story so far · <pack name>" plus "the full texts are in the Story tab".

**Timing.** "Story text: wait for me" (`ClientSettings.StoryTextWaitForMe`, default on) makes subtitles, speaker boxes and
reading frames **page-gated**: each page waits for the continue control, and once the caption's window has run out the
shot holds there (the camera keeps moving) until the reader has turned the last page. Off — and always for title cards,
credits and banners — captions are **timed**: each page shows for its reading floor `max(3 s, 1.2 s + chars / 12)`, at most
10 s, measured on the localized text; a caption (and its shot) shorter than that is stretched.

**When a scene may start** (`SafeMomentGate`):

| Scene | Waits for | Gives up |
|---|---|---|
| banner (T1) | loading veil, another cinematic, observer mode, an open menu, the respawn prompt | never |
| card (T2) | the above + chat input, the duel, the spawn settle; danger: hostile ≤ 20 m, damage in the last 10 s, space combat in the last 14 s, health or oxygen < 30 % | 600 s → text back to the panel, confirmed |
| scene taking the camera (T3/T4) | the above + falling, climbing, head under water, a speeder or train, EVA, piloting in space, a warp/transit; prefers the player **at home** (aboard, docked, ≤ 24 m from the parked ship) for 300 s | 1200 s → tried once more **as a card** (`AsCard`), then as above |

A "now or never" scene (`cooldownExempt`) aborts at once when the moment is not safe (its text goes to the panel). A
requested replay never gives up and skips "prefer at home".

### Unity: `CutsceneDirector`

`client/Assets/BlocksBeyondTheStars/Scripts/CutsceneDirector.cs` (built by `WorldRig`, execution order −20 so the Esc that
skips a card is marked handled before the pause menu sees it), with `CutsceneScreen.cs` (the presentation) and
`CutsceneCameraRig.cs` (the camera, `LateUpdate` at order 211, after the prologue rig's 210).

- **Receive** (`NetworkClient.CutscenePlayReceived`, tag 295): resolve the pack and definition from the client's own
  content; an unknown id (or a template without an owner) is confirmed `Unsupported` at once and its lines show as usual.
  Known scenes are queued with the `Owed` payload (definition, pack, the narrator lines it took over).
- **Narrator lines.** `VegaPanel.OnLine` asks `TryClaimLine` first: a line named in the `replaceLineKeys` of a queued or
  playing (unrequested) scene stays out of the panel. When the scene is interrupted or given up the lines come back
  (`VegaPanel.Requeue`, no flashback pulse) — text is never lost; the story log recorded them on arrival anyway.
- **Tiers on screen** (one `CinematicFrame` per scene at sorting order 67 — above the prologue 65 and intro 66, below the
  ending 68, the warp 70 and the veil 75): T1 a top banner with the narrator's icon, no letterbox, no input; T2 a thin
  letterbox (45 %), a light dim, the captions, **free input** (the HUD stays; VEGA's panel, the memory flashback and the
  ending wait while it shows — `CutsceneDirector.HoldsScreen`); while the Tab menu is open over a card the card pauses
  hidden. T3/T4: full letterbox, a fade out of black, the camera staged per shot.
- **Caption styles** (`CutsceneScreen`): `subtitle` (the frame's lower-third caption), `title_card` (kicker + big title,
  centred), `speaker` (box above the lower bar with the narrator's icon, name and colour from the pack's `narrator` — VEGA's
  defaults `ui.vega.name`, `icon_vega`, cyan, `ai_blip` when a page starts; #2478: another id is looked up in the pack's
  `speakers`, else it reads as its own name; typewriter 42 chars/s), `reading_frame` (a framed panel ≤ 1100 px wide over a
  55 % dim, its kicker as the heading — the recap, the epilogue), `credits` (a roll over the caption's window; #2478: on a
  dark stage — the picture dims to 0.85 while it shows). Fonts follow the UI scale; positions stay on the 1920×1080 frame
  canvas.
- **The duel dialogue** (#2478): whenever no scene plays, the director runs the Guardian core's duel as its dialogue mode on
  the core world — see [VEGA's finale](#vegas-finale-2478).
- **Camera** (`CutsceneStaging` + `CutsceneCameraRig`): anchors `player`, `ship` (scaled by the own ship's size),
  `heal_tank` (the own ship's `medbay` station within 64 m on this world, `CutsceneRules.TryFindHealTank`), `planet` (the
  player, wider), `station`/`star_map` (the player), `core` and `custom` (the message's coordinates, else the player).
  `orbit` circles (ping-pong across the open arc), `crane` rises, `static` frames with a slow push-in, `sky` looks up from
  the player's eye (only under the open sky, not aboard), `chart` shows the galaxy chart (`GalaxyChartWidget`: every star,
  known ones named, the current one ringed, the relay lanes) between the letterbox and the speaker box. #2474: a `static`
  shot of the heal tank first tries a **close framing inside the medbay** (`CutsceneStaging.TryCloseUp`: 2.4–4 blocks off at
  standing height, the line of sight inside the room — `ObjectLine` reads the hull as solid except the tank's own cells —
  flown with that line too), then the usual chain (the ship from outside); a scene whose camera shot needs the heal tank
  and finds none plays as a card (`CutsceneRules.NeedsCardFallback`). A `chart` anchored at `core` rings the revealed
  Guardian system, names it and draws a violet route to it from the player's system; a `memory`/`flashback` grade tints the
  chart sepia (the grade itself only reaches the 3D picture). A world shot that fails while chunks may still stream is
  retried every 0.5 s for 4 s (`StagingRetry`) showing
  the player's view, then the fallback is taken. Nothing world-anchored in the space view (only the chart and the synthetic
  stages). "Camera motion" off turns moves into still framings; a late-streamed chunk that blocks the line of sight freezes
  the last clear pose. #2477: a `dolly` / `pan` / `flyover` is flown over the shot's length (`CutsceneCameraRig.Apply(...,
  duration)` → `CutsceneMoves`); a `space_vista` / `diorama` goes to `CutsceneCameraRig.ApplySynthetic`, which shows
  `CutsceneStage` (its own camera at y 40000, over the world camera, whose culling mask is emptied meanwhile and restored
  after; the block shaders' globals are set for it in `RenderPipelineManager.beginCameraRendering`) — a stage that cannot be
  built is staged without it, and the director never asks for one on Potato/Low ([Cameras](#cameras)).
- **Shared state and hold.** A T3/T4 scene that takes the camera (or has `lockInput`) opens the `director` reason in
  `GameBootstrap.Cutscenes` — HUD, touch layer, chat, nameplates and IMGUI step aside, the controls freeze, Esc belongs to
  the scene, and the cursor is freed for the buttons. A singleplayer world is held (`WorldHoldArbiter.Cutscene`) only while
  the camera is actually taken (a `player_view` shot in between releases it), and never in multiplayer
  (`CutsceneRules.HoldsWorld` / `FreezesControls`, #2474 — unit-tested in Client.Core). A staged scene opens
  out of black only when it takes the camera (`CutscenePlan.OpensFromBlack`, #2473): a T3 that lies over a running
  sequence (`overlay`, the tutorial's first launch) keeps the full letterbox but fades in like a card, so it never blacks
  out the moment it frames.
- **Grades:** `memory` → `UrpScenePost.SetFlashback(0.55)`, `flashback` → 0.85 plus one glitch `Burst` per shot (not under
  Reduced effects, never in the browser); halved under Reduced effects. Banners leave the picture alone.
- **Music and sound:** a scene's `music` is played as `ClientMusic.CutsceneTrack` (context `cutscene:<track>`, looping, in
  either music mode; outranks every context but the ending's own track; a track still downloading leaves the current music
  playing — a scene is never delayed for music). `sfx` cues go to `ClientAudio.Cue`.
- **Skip:** one press — Esc or the pad's cancel button on a scene that owns the screen; on a card (input free, pad B is
  crouch) the keyboard's Esc — or the frame's skip button (click/tap), after `minDisplay`, at once on a replay. The hint
  follows the device (`CinematicFrame.SkipLabel`); on a pad a card shows none (it pages with the continue button). A skip
  keeps the claimed lines in the log only — except a scene's `returnOnSkip` lines whose caption had not been fully on screen
  (`CutscenePlayback.ShownKeys` / `KeysToReturnOnSkip`, #2474): they go back to VEGA's panel (the Guardian reveal's
  jump-generator line).
- **Continue:** the VEGA continue key (N / pad Back), the "Continue" button at the bottom centre (clear of the touch stick,
  the action buttons and the hotbar arrows), or a tap on the speaker box / reading frame.
- **Danger:** damage or space combat since the scene started, or a hostile ≤ 20 m while the world is not held, ends a card
  or scene: the lines go back to the panel, a "Story moment saved — watch it again in the Story tab" banner follows (for a
  replayable scene), and the scene is confirmed. MP: after 30 s with the camera the scene continues as a card.
- **Receipt** (`NetworkClient.SendCutsceneSeen`, tag 296): after a scene ends — watched, skipped (`Skipped`), interrupted or
  given up — never for a replay, a local play or the recap.
- **Watch again** (Story tab, `CraftingTechShipUI.BuildStoryList`): `CutsceneDirector.ReplayEntries()` = the server's
  `SeenCutscenes` plus what this client confirmed since, as rows with a "Watch" button →
  `NetworkClient.SendReplayCutscene` → the server re-sends it with `Replay = true` (spoiler guard server-side); the menu
  closes and the scene plays once the screen is free.

### Developer tools

- **Dev palette** (`CutsceneDevPalette.cs`): in the Editor, a development build, or any build started with **`-devSeq`**,
  **F9** opens a list of every cutscene of the loaded packs (and each world story's `__recap`); a click plays it on this
  client alone (`CutsceneDirector.PlayLocal`: no server, no ledger, no receipt; a template as the instance of the first
  chapter that binds it, the argument by the server's rule with the body and system from the star map —
  `CutsceneCatalog.PlayArg`; the recap with every chapter title). The smoke path for VEGA's `prologue` before step 11 binds it.
- **Admin preview** `/cutscene <pack> <id>` (server round trip, see above) — what the scene looks like for a real
  `CutscenePlay`.
- **Clip recorder** (`-captureClip`): a `ClipSpec` with `"cutscene": "<pack>/<id>"` plays the scene through the director
  when recording starts — use `"camera": "static"` (the capture follows the director's camera), `"hud": true` (captions and
  letterbox are UI) and a `lengthSeconds` that covers the scene.

### Known gaps (later steps)

- Hold-to-skip and pause-then-skip (d4 §2.4) are not built: one press skips (step 2's decision); the skip button sits
  bottom right, where on a touch device the touch layer may cover it during a card (the layer only hides under a staged
  scene).
- A card dims the whole picture by 18 % (HUD included — the crosshair and the chip are not kept bright as d4 sketched); no
  ambience ducking; music is not prefetched when a scene is queued.
- No `ctx:` variants (a scene has one shape for every place — chapter 5 falls back to a card away from the ship); the
  speaker box has no vocoder chatter (only the line-start cue). ~~`b10` has no multiplayer variant~~ — #2478 (the live
  chart with the other players). ~~No synthetic stages, no `dolly`/`pan`/`flyover`~~ — #2477 (chapters 3, 4 and 6 use them).
- #2478: the finale's pictures are built from what exists — the stand-down shows the sleeping-core diorama (its red glow
  still breathes; "the core sleeps"), no client-side "red light fades to warm" of the real chamber; the descent's flyover
  shows the real shaft only when it is streamed in (the first pad lies about 50 blocks from it — farther pads see the sky);
  the arrival has no gauntlet variant (the gauntlet banner counts the machines). The other beat cards stay cards (decision
  (a)); upgrade them after the playtest.
- #2477's dioramas are built in (`core`, `star_map`), not data: d4's data-defined `stages` (block boxes and props per pack)
  are not built — add them when a pack needs a diorama of its own. A `space_vista` shows the player's own ship only once its
  design reached the client (after the first flight). A clip recording (`-captureClip`) of a synthetic shot films the
  emptied world camera, not the stage.
- ~~The VEGA prologue still runs on `VegaPanel` + `PrologueCinematic`~~ — #2470 bound it as VEGA's `introCutscene`; the
  old path is kept for older servers only.

## Tests

- `CutsceneContentTests` — loader and defaults, the object and array form, every validator rule, the official ids, the
  ledger keys, the codec round trips of tags 295/296 and `StoryPackIntent.CutsceneId`.
- `CutsceneLedgerTests` — the `cutscene_test` fixture pack (`tests/.../Fixtures/story_packs/cutscene_test`): first play
  after the client is in the world, chained card, receipt, older clients, the runtime hooks' order, cooldown / banners /
  "now or never", the veteran, `skipPack`, join catch-up (also with an older client), the recap collapse, recap and
  anchor on the wire, replays, observers, the pause whitelist.
- `StoryPackValidationTests` — bindings must resolve; every data pack's `cutscenes.json` reads strictly.
- `CutsceneLedgerTests` (#2468 additions) — the queue flushes for a pilot whose world is not loaded (`TickCutscenes`), the
  admin preview (every spelling, refusals), `SeenCutscenes` in the story state (confirmed only, codec round trip), the
  seen-key parser.
- Client.Tests: `CutscenePlanTests` (plan, pager, recap, VEGA's prologue from the data pack, playback), `SafeMomentGateTests`
  (every unsafe reason, give-up, interrupt, cap; the queue), `CutsceneStagingTests` (fallback chain on fake worlds; the
  "Watch again" rows and labels), `CutsceneClientTests` (the real `NetworkClient` against the real server: `cutscenes1`,
  the preview event, the receipt, `SeenCutscenes`, the replay request), `StoryPackClientTests` (the feature words).
- #2470: `VegaStoryPackTests` (the prologue scene for a newcomer, the five pages for an older client, once / never on a
  restart, a "none" world switched to VEGA, the recap chaining into a chapter scene, a bound chapter opening at the track
  start and at its beat); `SafeMomentGateTests.TheRecap_LeadsIntoItsPacksNextChapterScene_Only`.
- #2473: `TutorialCardsTests` (the cards' ids, tiers and titles-only rule, the bindings, the interruption budget, the part
  number on the card and not in the lines; a fresh player's prologue → welcome chain, the part cards behind the cooldown and
  dropped once their part is over, the "now or never" first launch, the first landing with the world's name chaining into
  the graduation card, a graduation line held with its waiting card, once per player through "I know the game" and a full
  restart, older clients, migrated saves; `CutsceneArgs` and `LineText.Compose`'s later slots; the validator's `arg` and
  `graduation.cutscene` checks); Client.Tests `TutorialCardsClientTests` (the cards planned from the data pack in both
  languages, the overlay that does not open from black, the "Watch again" names, the dev palette's argument, the client
  queue's cross-pack chain).
- #2474: `VegaChapterScenesTests` (every chapter of the arc bound to its opening and the other beats to their cards, the
  built-in mirror; the tiers — cards with free input, the big scenes' cameras; every caption a beat text kept out of the
  panel; "Chapter n" as the Story tab numbers it, both languages; the stingers' size; the track start in chapter 1 (card +
  B1's card at once, the line held for it), before B1, later on (recap → only the current chapter's scene), with the finale
  open (ch7 with the reveal line after it); a live beat as a card for started players only; B12's set piece; an older
  client's lines; the join catch-up across chapters (recap + scene + the Journal line), within a chapter, one missed beat,
  a missed chapter opener), `CutsceneContentTests.AReturnOnSkipLine_MustBeOneTheSceneReplaces`; Client.Tests
  `VegaChapterScenesClientTests` (the cards planned from the data pack in both languages, the heal tank finder, the
  close-up in a cabin and the card fallback, ch7's hold SP-only, the skip give-back, the "Watch again" names, the chain
  `ch1.open → b01`).
- #2477: Client.Tests `CutsceneCameraModesTests` (the dolly's clear straight push-in, its easing and settling, a wall in
  its way, the fallback to the still framing; the pan in place above the head, turning away from a cliff, the cave
  fallback, its back-and-forth; the flyover's high arc rising in the middle and its overhang fallback; the moves without
  chunks or a world; the synthetic stages without a world and on the low presets, the card fallback of a scene made only of
  them; the anchor → set mapping; the core diorama's block budget, open front and rising camera; the calm pulse; the memory
  chart's fit, rim, red glow beyond it and drift; the vista's camera, planet and ship path), `VegaChapterScenesClientTests`
  (chapter 4 staged over its vista, the three synthetic openings as cards on the low presets with the same words);
  `CutsceneContentTests` (the new cameras accepted in a staged scene and refused on a card, the diorama's anchors);
  `VegaChapterScenesTests` (the tiers and cameras of chapters 3, 4 and 6, their unchanged captions within 12–25 s).
- #2478: `VegaFinaleSetPiecesTests` (the finale's bindings and their mirror, the core's voice; the set pieces' tiers,
  cameras and chains; b10 the one beat moment; the duel node's new fields on the wire; the arrival for the pilot who warps
  in — once, nobody else, an older client stamped seen, never after the win; the descent over the shaft before the hint
  line, re-offered with its place after a relog; the breach on the core world and the banner elsewhere, the breach before the first node, answers only from the core
  world — never from a pilot over it; the node for whoever lands or relogs mid-duel; an answer through a held world; the win
  on the director vs `StoryResolved` for an older client, never both, the replay; the catch-up of a player who missed the
  win with the achievement, once; the recap that never folds the ending away; an observer's ending when observing ends;
  every loaded world's machines standing down), `CutsceneContentTests.FinaleStages_Speakers_AndThePlayersAnchor_AreValidated`;
  Client.Tests `FinaleSetPiecesClientTests` (the duel state, where the dialogue shows, the buttons waiting for the core,
  the core's words and "Argument n of 4" in both languages, facing the core again across the seam, no hold in multiplayer,
  the close framing in the chamber at any yaw, the chart's player labels, b10 as a chart or a card, the arrival, descent,
  stand-down and ending planned from the data pack).
