# Cutscenes — data format, wire, seen ledger, client director

Story packs stage their big moments as **data-driven cutscenes** (#2467, story-packs step 8): a pack's
`data/stories/<pack>/cutscenes.json` describes each scene (shots, cameras, captions as locale keys, grade, sound, options);
the **server** only decides *when* a player gets which scene and keeps a per-player **seen ledger**; the **client**
resolves the scene from its own copy of the file and plays it with the **cutscene director** (#2468, step 9 — see
[The client director](#the-client-director)). Everything is additive: no protocol bump, no save-version bump.

Related: [STORY_IMPLEMENTATION.md](STORY_IMPLEMENTATION.md) (story engine, the player-pack runtime, the wire table),
[TUTORIAL_PACK.md](TUTORIAL_PACK.md) (pack manifest and bindings).

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
        "cooldownExempt": false, "replayable": true
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
| `shots[]` | — | at least one; played in order |
| `shots[].camera.type` | `player_view` | `player_view` (letterbox + captions over the live view), `overlay` (on a running flight sequence, never moves the camera), `static`, `orbit` (radius by ship size), `crane`, `sky` (tilted up from the player; always clear), `chart` (the galaxy chart). Step 17a adds `dolly`, `pan`, `flyover`, `space_vista` |
| `shots[].camera.anchor` | `""` = `player` | `player`, `ship`, `planet`, `station` (the space station the player is aboard), `star_map`, `heal_tank` (the ship's medbay), `core` (the Guardian core chamber), `custom` (the coordinates of the `CutscenePlay` message) |
| `shots[].duration` | — | seconds (a page-gated caption may hold the shot longer) |
| `shots[].grade` | `none` | `none`, `memory` (VEGA's memory, sepia), `flashback` |
| `captions[].key` | — | locale key in the core or the pack table; English **and** German mandatory; `{0}` = the play's argument |
| `captions[].style` | `subtitle` | `subtitle` (lower third, paged at sentence ends), `title_card` (kicker + title), `speaker` (speaker box: avatar, name, typewriter, voice), `reading_frame` (framed panel over the dimmed scene — recap, epilogue), `credits` |
| `captions[].kickerKey` | `""` | `title_card` only: the small line above the title |
| `captions[].speaker` | `""` = the pack's narrator | `speaker` only: the narrator id of the box |
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

The model is `Shared/Story/CutsceneDefinition.cs` (`StoryCutsceneFile`, `CutsceneDefinition`, `CutsceneShot`,
`CutsceneCamera`, `CutsceneCaption`, `CutsceneSfx`, `CutsceneOptions`, the vocabularies `CutsceneTiers`,
`CutsceneCameras`, `CutsceneAnchors`, `CutsceneCaptionStyles`, `CutsceneGrades`, and `StoryCutsceneIds`).
`ContentLoader.LoadPackCutscenes(packDir)` reads the file into `StoryDefinition.Cutscenes`; `StoryDefinition.FindCutscene(id)`
finds a definition (an instance id finds its template). The built-in VEGA fallback (`StoryRegistry.Default`) carries no
cutscenes — its narrator lines still play.

**A new file under `data/` must be listed in the WebGL `FallbackManifest`** (`client/.../StreamingAssetsCache.cs`;
`StoryUiFixesClientTests.TheBuiltInFallbackList_MatchesTheDataFolder` fails otherwise). Whether a `cutscenes.json`
reaches the browser is only proven by a local WebGL build (PR CI builds no Unity).

### Ids

- **VEGA** (`StoryCutsceneIds.Vega`): `prologue`, `ch1.open` … `ch7.open` (`ch7.open` = the B12 reveal, T4), the beat
  moments `b01`, `b03`, `b04`, `b07`, `b08`, `b10`, `fin.arrival`, `fin.descent`, `fin.breach`, `fin.duel`,
  `fin.standdown`, `resolution`.
- **Tutorial** (`StoryCutsceneIds.Tutorial`): `tut.welcome`, `tut.strip`, `tut.course`, `tut.done`, `tut.first_launch`,
  `tut.first_dock`, `tut.first_landing`, `tut.first_jump`, `tut.first_base`, `tut.ship_commissioned`, `tut.first_circuit`,
  `tut.first_tame`, `tut.graduation`.
- **Built in:** `__recap` — the "The story so far" card. Never defined or bound by a pack; the server sends it.

`CutsceneContentTests.TheOfficialPacks_UseOnlyTheBindingIds` holds the two official packs to these lists. Today
`vega_protocol/cutscenes.json` holds only `prologue` (mirroring the three prologue pages); nothing binds it yet — VEGA's
start plays it from step 11 on, and steps 14/15/17 add the tutorial cards, chapter scenes and finale set pieces.

### Bindings — where a pack plays a scene

| Field | Plays when (server) |
|---|---|
| pack `introCutscene` | a **player** pack starts for the player — not on "I know the game" (`skipPack`), and not when the pack would complete silently for this player (every mandatory chapter through, by `autoCompleteIf` or by every step done / `alreadyDoneIf` — a veteran). The world story's start is wired in step 11 |
| chapter `openCutscene` | the chapter's first step is **presented** (activates with its line) — not during a replay, not for a chapter that completes silently |
| chapter `closeCutscene` | a chapter the player **was shown** this session is done (`PlayerSession.PresentedPackChapters`) — not when skipped, replayed or completed silently by evidence |
| step `onStart.cutscene` | the step activates with its line (before the line; not in a replay) |
| step `onComplete.cutscene` | the step completes **non-silently** (it was the chip step, or a react step) — before the done line; not in a replay |
| beat `cutscene` | the API exists (`PlayCutsceneForListeners`); the beat reveal wires it in step 11 |
| `finale.cutscenes{arrival, descent, breach, standdown, resolution}` | wired by the finale code in step 17b |

A scene goes out **before** its trigger's narrator line, so a client that plays it knows which lines it shows.

**Templates.** A `"template": true` scene (the tutorial's chapter card `tut.strip`, the banner `tut.done`) is one
definition for many bindings: each binding is its own **instance** `<id>@<owner>` (owner = the chapter id, the step id,
or the pack id for the intro), with its own ledger entry, and plays with the bound chapter's title key as its argument. A
plain scene plays **once per player** however often it is bound (the prologue can be the pack intro and chapter 0's
opener). On the wire and in receipts the instance id is the `CutsceneId`; the definition is the part before `@`.

**The argument** (`CutscenePlay.Arg`, the captions' `{0}`): a chapter binding sends the chapter's title **key** (a client
localizes an argument that is a key), the server API may pass any text (a body name), several values joined by U+001F
(`LineText.ArgSeparator`). For `__recap` it is the reached chapters' title keys in chapter order — titles only, no beat
text, so no spoilers.

## Validation

`StoryPackValidator` (Shared, run by `StoryPackValidationTests` for every data pack and the test packs) and its twin
`tools/validate_story_pack.py` (`uv run --no-project python tools/validate_story_pack.py [<pack-dir>]`) check:

- every binding names a cutscene of the pack — never `__recap`, never an instance id;
- ids unique and key-safe, none starting with `__`; tier 1–4; at least one shot; known cameras, anchors, caption styles
  and grades; shot length > 0 and ≤ 120 s; captions and cues inside their shot; `hold` ≥ 0;
- the tier rules: a T1/T2 never takes the camera; `holdWorld` / `lockInput` only on a T3/T4 that takes it;
- every caption, kicker and `replaceLineKeys` key exists in English **and** German, within its style's budget
  (`subtitle` / `speaker` 220 EN / 264 DE — one VEGA panel page, the director pages it further; `title_card` title and
  kicker 60 / 72; `reading_frame` 600 / 720; `credits` none; a `{key:…}` token counts 3, a `{0}` slot 10);
- `chapter` names a chapter of the pack; `chainNext` resolves (an id of the pack, `<pack>/<id>` of an installed pack,
  `__recap`; a pack that is not installed is a warning); `music` exists in `client/Music`, every `sfx` cue in
  `client/Assets/Resources/audio`;
- warnings: a `kickerKey` outside a `title_card`, a `speaker` outside a `speaker` caption, unknown fields (the Python
  tool; the C# tests read the file strictly).

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
  `__recap` card with those chapters' titles (the scenes are stamped seen; the Story tab replays them).
- **Replay** (`replayCutscene`): a replayable scene the player has a key for (spoiler guard) is re-sent with
  `Replay = true` — any installed pack, the world story included; no ledger change; nothing for a client without
  `cutscenes1`. The ending keeps its own `RequestStoryResolutionIntent` path.
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
  `ch1.open` / recap, B12 → reveal) — it may jump the queue.
- **Now or never** (`cooldownExempt`) goes at once and restarts the cooldown.
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
| `CutsceneStaging` | a shot's camera with the fallback chain (requested → raised orbit → crane → sky → player view) on `CinematicStageScan` (unloaded chunks blocked, the parked hull blocks the camera spot) and `PrologueOrbit.Scale` |
| `CutsceneCatalog` | the "Watch again" rows (seen, replayable scenes of installed packs, in pack/definition order) and their names (title card "Prologue — The VEGA Protocol", else the bound chapter's title, else the first caption) |

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
  defaults `ui.vega.name`, `icon_vega`, cyan, `ai_blip` when a page starts; another `speaker` id reads as its own name;
  typewriter 42 chars/s), `reading_frame` (a framed panel ≤ 1100 px wide over a 55 % dim — the recap), `credits` (a roll
  over the caption's window). Fonts follow the UI scale; positions stay on the 1920×1080 frame canvas.
- **Camera** (`CutsceneStaging` + `CutsceneCameraRig`): anchors `player`, `ship` (scaled by the own ship's size),
  `heal_tank` (the ship's `medbay` station), `planet` (the player, wider), `station`/`star_map` (the player), `core` and
  `custom` (the message's coordinates, else the player). `orbit` circles (ping-pong across the open arc), `crane` rises,
  `static` frames with a slow push-in, `sky` looks up from the player's eye (only under the open sky, not aboard), `chart`
  shows the galaxy chart (`GalaxyChartWidget`: every star, known ones named, the current one ringed, the relay lanes) over
  the frame. A world shot that fails while chunks may still stream is retried every 0.5 s for 4 s (`StagingRetry`) showing
  the player's view, then the fallback is taken. Nothing world-anchored in the space view (only the chart). "Camera motion"
  off turns moves into still framings; a late-streamed chunk that blocks the line of sight freezes the last clear pose.
- **Shared state and hold.** A T3/T4 scene that takes the camera (or has `lockInput`) opens the `director` reason in
  `GameBootstrap.Cutscenes` — HUD, touch layer, chat, nameplates and IMGUI step aside, the controls freeze, Esc belongs to
  the scene, and the cursor is freed for the buttons. A singleplayer world is held (`WorldHoldArbiter.Cutscene`) only while
  the camera is actually taken (a `player_view` shot in between releases it), and never in multiplayer.
- **Grades:** `memory` → `UrpScenePost.SetFlashback(0.55)`, `flashback` → 0.85 plus one glitch `Burst` per shot (not under
  Reduced effects, never in the browser); halved under Reduced effects. Banners leave the picture alone.
- **Music and sound:** a scene's `music` is played as `ClientMusic.CutsceneTrack` (context `cutscene:<track>`, looping, in
  either music mode; outranks every context but the ending's own track; a track still downloading leaves the current music
  playing — a scene is never delayed for music). `sfx` cues go to `ClientAudio.Cue`.
- **Skip:** one press — Esc or the pad's cancel button on a scene that owns the screen; on a card (input free, pad B is
  crouch) the keyboard's Esc — or the frame's skip button (click/tap), after `minDisplay`, at once on a replay. The hint
  follows the device (`CinematicFrame.SkipLabel`); on a pad a card shows none (it pages with the continue button).
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
  client alone (`CutsceneDirector.PlayLocal`: no server, no ledger, no receipt; a template as its first chapter's
  instance, the recap with every chapter title). The smoke path for VEGA's `prologue` before step 11 binds it.
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
- No `ctx:` variants, no synthetic stages (`space_vista`, dioramas), no `dolly`/`pan`/`flyover` (step 17a); the speaker box
  has no vocoder chatter (only the line-start cue).
- The VEGA prologue still runs on `VegaPanel` + `PrologueCinematic` until step 11 binds `introCutscene: "prologue"`.

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
