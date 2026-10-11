# Cutscenes — data format, wire, seen ledger

Story packs stage their big moments as **data-driven cutscenes** (#2467, story-packs step 8): a pack's
`data/stories/<pack>/cutscenes.json` describes each scene (shots, cameras, captions as locale keys, grade, sound, options);
the **server** only decides *when* a player gets which scene and keeps a per-player **seen ledger**; the **client** resolves
the scene from its own copy of the file and plays it (the client director is step 9 — its part of this document follows
then). Everything is additive: no protocol bump, no save-version bump.

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

- Only a client that announces **`cutscenes1`** in `JoinRequest.Features` (`ClientFeatures.Cutscenes1`) gets
  `CutscenePlay`; the client announces it from step 9 on. A local player (`AddLocalPlayer`) does not.
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

`PlayerSession.CutsceneQueue` holds committed scenes not sent yet; the 1 Hz `TickShipAi` path (`TickCutscenesFor`) and
every new commit flush it:

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

## Singleplayer and multiplayer

- **SP hold** only for T3/T4 scenes that take the camera, by the client's `WorldHoldArbiter` (`Cutscene` holder) after
  its streaming wait — never for an `overlay` on a flight, never for a card. The server keeps one pause flag per session.
- **MP** (synthesis §5.3 "MP-Schutz v1", done by the director in step 9): a safe moment before a scene starts, a danger
  interrupt (damage ends the scene, the text goes back to the panel), a 30 s cap on camera takeovers. **No** server
  grace period. Every player gets their own copy, deferred on their own; replays never hold in MP.

## The client director

Step 9 (#2468) adds the client half here: `CutscenePlan`, `SafeMomentGate`, `CutsceneQueue` in Client.Core, the
`CutsceneDirector`, the skip rules on every device, the "Story text: wait for me" setting, and the `cutscenes1`
announcement.

## Tests

- `CutsceneContentTests` — loader and defaults, the object and array form, every validator rule, the official ids, the
  ledger keys, the codec round trips of tags 295/296 and `StoryPackIntent.CutsceneId`.
- `CutsceneLedgerTests` — the `cutscene_test` fixture pack (`tests/.../Fixtures/story_packs/cutscene_test`): first play
  after the client is in the world, chained card, receipt, older clients, the runtime hooks' order, cooldown / banners /
  "now or never", the veteran, `skipPack`, join catch-up (also with an older client), the recap collapse, recap and
  anchor on the wire, replays, observers, the pause whitelist.
- `StoryPackValidationTests` — bindings must resolve; every data pack's `cutscenes.json` reads strictly.
