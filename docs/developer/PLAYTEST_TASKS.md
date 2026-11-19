# Playtest tasks — the "Test Pilot" page

Players can help test the game on purpose: the **Test Pilot** page (desktop builds; main menu → *Join in*, and a
row in the pause menu) lists **playtest tasks** — "try the heavy shot", "paint a texture with two frames" — each
with a few steps to tick off and a short closing **report**. A sent report earns a **badge**; the developers can
confirm it, which turns the badge gold. Badges add up to a rank. That is the whole reward: a badge buys nothing in
the world.

It is deliberately **not** the in-game achievement system (`data/achievements.json`, counters in the savegame,
item rewards): playtest progress belongs to the install, spans worlds, the main menu and the editors, and its
content is not part of a build.

```
report inbox (ReportHost)                       game client
┌───────────────────────────────┐   fetch (ETag)   ┌────────────────────────────────────────────┐
│ playtests.db  ── /api/playtests ─────────────────► PlaytestCatalogClient → playtests/catalog.json │
│ /admin/playtests  (edit tasks) │                  │ PlaytestTracker  ── tasks for this build   │
│                                │                  │ PlaytestProgress → playtests/progress.json │
│ reports.db  ◄── /api/bugreport ◄──────────────────  PlaytestReport (reportType playtest-report) │
│ category "playtest", confirm   ├── thank-you ─────► reply poll → gold badge                    │
│ /api/playtests/mine ───────────┼──────────────────► badges restored / confirmed                │
└───────────────────────────────┘                  └───────────────┬────────────────────────────┘
                                                     watch list ▼   ▲ PlaytestEvent
                                                   game server: one listener on the game-event bus
```

## Why the catalogue comes from the inbox

A task must be changeable **without a release** — that is the point of asking players to test something. So the
tasks are not in `data/`: the client fetches them from the report inbox it already talks to
(`GET /api/playtests`, same spam key as report submission). The inbox is also where the reports about a task
land, so its admin page can show a task next to its numbers.

What can change without a release: tasks, texts, questions, order, the version window, the kill switch, every
step the player ticks off himself. What cannot: a **new event** a step is recognised by (a new server game event
or a new editor event needs code) — and a task that waits for such an event needs a `minVersion` (see
*An event newer than a client* below).

## The catalogue

One document, read and written by `Shared/Playtests/PlaytestCatalogJson.cs` (one implementation for the inbox,
the client and the tests):

```json
{ "schema": 1, "enabled": true,
  "tasks": [ {
    "id": "heavy-shot", "rev": 2, "enabled": true, "sort": 10,
    "area": "ingame",                          // or "editor" + "editor": "ship|avatar|station|settlement|texture|form|toollook"
    "minVersion": "2026.11.21", "maxVersion": "",
    "title": { "de": "…", "en": "…" }, "intro": { "de": "…", "en": "…" }, "minutes": 10,
    "world": { "seed": 42, "startPlanet": "gas_giant", "preset": "sandbox", "tutorial": "off",
               "story": false,                     // true = the world keeps the game's story (a task about its beginning)
               "options": { "Hazards": 1 },
               "commands": [ { "cmd": "set_time", "arg": "18:30" } ] },
    "steps": [ { "id": "launch", "text": { … }, "check": { "event": "space:enter", "arg": "launch", "required": 1 } },
               { "id": "feel",   "text": { … } } ],
    "questions": [ { "id": "fun",  "type": "stars",  "text": { … } },
                   { "id": "hard", "type": "choice", "text": { … }, "options": [ { "id": "easy", "text": { … } } ] } ],
    "thanks": { "de": "…", "en": "…" }, "retest": false } ] }
```

- **Texts ride in the catalogue**, German and English (`PlaytestText`); every other language reads English. They
  are not locale keys — those are part of a build.
- **Reading is tolerant.** An unknown field is ignored, a wrong-typed one reads as its default. The client reads in
  `Safe` mode: texts are cleaned (rich-text tags and links removed — a fetched text must never style or link
  anything) and cut, lists cut to `PlaytestLimits`, entries it cannot use left out (a step without an id, a
  question type it does not know — every question is mandatory, so an unaskable one must not exist). A catalogue of
  a higher `schema` reads as switched off.
- **A check this build cannot use** leaves the step for the player to tick off: the reader drops a check without
  an event, and the client takes a check off whose event it does not know (`PlaytestTracker.SetCatalog`, #3833). Not
  the reader: the inbox reads with the same reader before it serves, and an inbox older than an event must not take
  the check away from every client.
- **`rev`** counts up when what players read or do changed. A report names the revision it answered.
- **`retest`** with a higher `rev` opens a finished task again ("please test again").
- **Validation** (`PlaytestCatalogValidator`) is the one rule set for the admin page, the scriptable routes and the
  tests. An event name the build does not know is only a *warning*.

### Steps: three ways to close one

| Kind | How | Where the event comes from |
|---|---|---|
| Ticked off | "Done" or "Did not work" (+ a note). Both close the step — a step that did not work is a result | the player |
| Recognised in a world | `check.event` is a server game event (`GameEventKeys`, a `prefix*` pattern) | the server's playtest watch (below) |
| Recognised in an editor | `check.event` is `editor:opened\|saved\|exported\|submitted\|loaded`, `arg` the editor | `PlaytestSignals` (`EditorPlaytest.*` in the editors) |

A UI moment (`ui:menu_open` …) is counted by the client itself: `NetworkClient.SendUiTutorialEvent` raises the same
flag as a signal.

**A recognised step has no buttons.** It closes when the event comes and not otherwise — the tester cannot answer
"did not work". So recognise only what a tester can always bring about (walking, mining, opening an editor,
launching) and leave everything that depends on luck or skill to the two buttons ("find a raider ship", "defeat
the giant").

#### An event newer than a client

The catalogue is newer than any build. Three things keep a step from waiting for an event nobody raises (#3833):

| Who does not know the event | What happens |
|---|---|
| The client (a build older than the event) | `PlaytestTracker.SetCatalog` takes the check off; the step shows its two buttons |
| The server (a new client on an older multiplayer server) | The server leaves the pattern out of its `PlaytestWatchAck`; `PlaytestWatchSync.Reports` says no; the step shows its two buttons |
| The **first** Test Pilot client, v2026.11.23 | It does neither: the step shows no buttons and never closes. **It must not be shown such a task** |

Hence the rule the validator enforces as an **error**: a step that waits for an event newer than v2026.11.23 needs a
`minVersion` at or above the release that event shipped in. The table is `PlaytestEventVersions`
(`Shared/Playtests/PlaytestCatalogValidator.Events.cs`); **a new game event a playtest may wait for gets a row
there**. Today it holds the nine events of #3831/#3832 (first possible release: 2026.11.24):

| Event | Arg | Raised when |
|---|---|---|
| `ship:rolled` | — | a barrel roll was granted |
| `ship:boosted` | — | the boost came on (once per boost) |
| `ship:evaded` | `heavy` | a heavy shot missed because of the roll |
| `carry:lifted` | block key | the selected block lifted over the head |
| `throw:any` | block key | the carried block thrown |
| `throw:hit` | `creature` \| `giant` \| `machine` \| `bandit` | the thrown block hit a body |
| `emote:played` | `wave` … `bow` | an emote of the wheel played |
| `player:fallen` | — | fallen in a Hard Survival world (the restart at the ship is `player:respawned`, an old event) |
| `player:revived` | `fallen` \| `medic` | a medkit brought a fallen player back (both hear it) |

The tasks in the repository use none of them yet, so they work on v2026.11.23. A later revision of a task can switch
a hand-ticked step to one of them together with a `minVersion`.

### Test worlds

`world` lets a task bring a prepared singleplayer world. `PlaytestWorldPlan` (Client.Core) checks it: the planet
type must be one this build knows (creating the world anyway would silently start on another planet and save
that), only the world options on its own list are applied, and only the start commands on the **fixed list**
(`PlaytestWorldCommands`: `set_time`, `set_weather`, `teleport_to_location`, `teleport_to_named`, `give_item`). The
catalogue is fetched text and in singleplayer the player is the world's admin, so nothing else is ever sent. A test
world starts **without the story** (`StoryRegistry.NoneStoryId`): no opening scene to sit through, no story
missions between the tester and the steps. A task that tests the story's beginning switches it on with
`"story": true` in its `world`; anything else leaves it off, and a client older than the switch ignores it. The
commands run **once**, a few seconds after the world came up, **only** in a local world this install created for a
task (its name starts with `Testpilot - ` AND the progress file knows it) — never in a player's own world.

- **The presets.** `standard`, `peaceful` (animals, but no machines, robbers or hostile ships; easy hazards),
  `hard` (Hard Survival) and `sandbox`. A **sandbox** world runs the server in Creative mode: nothing hurts the
  player and hostiles do not see them — a step about a fall can never happen there, and a fight is one-sided. The
  validator warns about such a step (`CheckWorldFitsSteps`, #3837); a fight belongs in `standard` or `hard`.
- **World options** are the sliders of the new-world screen by name, as a step index. Each is cut to the last step
  of its own slider (`PlaytestWorldPlan.MaxIndexOf`: five steps for the activity and frequency sliders, four for
  oxygen, hunger, hazards and the death penalty) — the client looks the index up in the slider's own table, and one
  past the end would stop the world from being created (#3840).

#### Seed-bound tasks

Something rare — a guardian giant that can still wake, one of the school club's giant buildings — is only in a test
world by its **seed**: the list of start commands has no "spawn" on purpose. Such a task names the seed and takes the
tester there with `teleport_to_named` (`giant1`, `landmark1` — the words `/tp` lists).

- **Finding a seed:** `BBS_PLAYTEST_SEED_SEARCH=<planet>:<kind>:<from>-<to>` (`rocky:giant:1-60`) and
  `dotnet test tests/BlocksBeyondTheStars.Tests --filter PlaytestSeedBoundTasksTests.SeedSearch` prints the seeds
  of the range whose start world holds such a place (and whether a giant can wake, which building a landmark is)
  into `bbs_playtest_seed_search.txt` in the temp folder. About eight seconds per seed.
- **The guard:** `PlaytestSeedBoundTasksTests` builds the world of every task that teleports to a generated place
  the way the bundled server does and fails when the place is missing. Such a task uses the `standard` preset, no
  world options and names its start planet — a slider can move the place away, and the guard rebuilds exactly that.
- **A seed-bound task ages.** A test world is created with the terrain generation of the build that plays it. After
  a generation step the same seed may no longer hold the place, the teleport moves nobody and the task cannot be done.
  **So when a change raises the terrain generation and this guard goes red:** search a new seed, count the task's
  `rev` up and set its `minVersion` to the release that will carry the generation — clients of the old generation
  no longer see the task then, which is right: its world is gone for them too.

## The inbox side (`src/BlocksBeyondTheStars.ReportHost/`)

| Route | Auth | Purpose |
|---|---|---|
| `GET /api/playtests` | spam key, own per-IP budget (`BBS_REPORTS_PLAYTEST_PER_MINUTE`, 60) | The catalogue for the game: the kill switch and the switched-on tasks, at most 40, with an `ETag` (`304` for a known one) |
| `GET /api/playtests/mine?key=` | spam key + reply key (its budget) | An install's own playtest reports: task, revision, status, `confirmed` — restores badges after a reinstall |
| `GET /api/playtests/export` | admin | Every stored task as written (switched-off ones too) — the sync tool's pull |
| `PUT /api/playtests/{id}` · `DELETE …` | admin, JSON body | Create/replace/delete one task; `400` names every problem, `409 stale_rev` when the stored revision is higher (`?force=true` overrides) |
| `POST /api/playtests/enabled` | admin, JSON body | The kill switch |
| `POST /api/reports/{id}/confirm` | admin, JSON body | Confirms a playtest report (below) |
| `GET /admin/playtests`, `/new`, `/{id}` · `POST …/save`, `/{id}/toggle`, `/{id}/delete`, `/switch` | admin + CSRF | The task list, the editing form (JSON, findings, preview in both languages, evaluation) |
| `POST /admin/report/{id}/confirm` | admin + CSRF | The confirm button of a playtest report's card |

- **Storage:** `playtests.db` next to `reports.db` (`PlaytestStore`) — authored content, apart from player data. The
  served catalogue is built once per change and cached with its ETag.
- **The admin page is the catalogue's truth.** Its save always wins and counts the revision up by itself when the
  content changed (switching a task on/off or moving it in the list is not such a change). The repository mirrors
  it: `tools/playtests/sync_playtests.py pull|push|check` over `data-online/playtests/<id>.json`; a push with a
  lower revision than the stored one is refused.
- **Reports:** `reportJson.reportType = "playtest-report"` → category `playtest` (its own filter). It is not a
  submission — never offered for adoption, never pruned as one — but may carry the tester's work as an attachment
  (capped like a ship); attachments go after `BBS_REPORTS_SUBMISSION_RETENTION_DAYS`, the report stays for the
  evaluation (`ReportStore.PrunePlaytestAttachments`).
- **Pairing:** a report sent from a world arrives twice (the client's row + the server's `/bump` snapshot). The two
  have different categories here (`playtest` / `feedback`), so `ReportHostPages.SameBucket` pairs them explicitly —
  otherwise a confirm would reach only one half.
- **Confirm** (`PlaytestActions.Confirm`) is ONE action: status `done` on the pair **and** a thank-you in the
  reply thread (the operator's text, else the task's `thanks` in the tester's language, else a default). The game
  only ever learns about a report through its thread — the reply poll returns threads with an unread developer
  entry and nothing else — so a bare status change would never reach the tester.
- **The report card** on the detail page (`ReportHostPlaytestPages.ReportCard`) shows the steps with their outcome
  and note, the answers in words, how the game was drawn (a report from a world: preset, render scale, screen,
  graphics API — #3838), the attached work and the confirm form. `PlaytestStats` adds a task's reports up
  for the list and the editing page.

Deploy the reports image **before** a client release with the page: an older inbox files these reports as plain
`feedback`, keeps no attachment and serves no catalogue (the page then shows nothing).

## The client side

Logic (Client.Core, `Playtests/`, tested headless):

| Class | Job |
|---|---|
| `PlaytestCatalogClient` | Fetches the catalogue (ETag, at most every 6 h by itself, always when the player opens the page), caches it in `playtests/catalog.json`; `FetchMine` |
| `PlaytestProgress` | `playtests/progress.json` per install: step states, the report draft, the sent report, badges, the task being worked on, test worlds, and whether the page was ever opened (`Visited` — only then is the catalogue fetched at a start, see *Privacy*) |
| `PlaytestWork` | "Send my work along": the editor's work as the report's one attachment (below) |
| `PlaytestTracker` | The tasks for this build (version window), taking off a check whose event this build does not know, activating a task, events → ticked steps, the server watch patterns, applying the inbox's list of own reports |
| `PlaytestReport` | Builds the report body: refused while a step is open or a question unanswered; the description is the whole report as plain text (the inbox requires one, and an operator can read it without a special view); no e-mail, no install token (`playerId` empty — the reply key is the anonymous id), the nickname instead of the in-game name; the `graphics` node when the caller hands it over (a report sent from inside a world, #3838) |
| `PlaytestWatchSync` | Paces the watch list and decides whether the server reports events (ack) or is an older one (silence → the player ticks those steps), and which patterns of the list the server took (`Reports`) |
| `PlaytestWorldPlan`, `PlaytestDevInbox` | The checked test world; a development build's own inbox (`BBS_REPORTS_DEV_URL` + `BBS_REPORTS_DEV_KEY`, loopback or https only, never in a release build) |

Unity (`client/Assets/BlocksBeyondTheStars/Scripts/`):

| File | Job |
|---|---|
| `PlaytestHub.cs` | The one component on the shell: owns the tracker, fetches off the main thread, binds to a world's `NetworkClient`, runs a test world's start commands, sends the report. `EditorPlaytest` — the editors' one-line signals |
| `UiTestPilot.cs` | The page (tasks, rank, badges) and a task's view; `PlaytestStepRow` is shared with the editor list |
| `PlaytestReportDialog.cs` | The report dialog, the "did not work" note, the "confirmed" dialog; `PlaytestModal` (who owns the cancel key) |
| `PlaytestEditorPanel.cs` | The task's list over an open editor: a tab on the right edge that opens into the steps |
| `HudUi.Playtest.cs` | The current step as a context card in the HUD's right column |
| `AppShell.TestPilot.cs` | Opening/leaving the page (also from the pause menu over a held world), rebuilds, the editor's "opened" event |

Desktop only: `PlaytestHub.Available` is false in a browser build, the menu entry is absent there.

### "Send my work along"

A report written from an editor's task list offers one box: send the thing on the bench along, so a developer can
load exactly what the tester had on the screen. Every player editor is an `IPlaytestWorkSource`
(`WorkForPlaytest()`), and the shell hands the open editor's method to the report dialog
(`AppShell.PlaytestWorkOfOpenEditor`); the page in the main menu has no editor open and shows no box.

| Editor | What travels | File |
|---|---|---|
| Ship, station/settlement, figure | The bundle Export writes, in the document format a submission of that kind carries (`{"formatVersion":1,"kind":…,"key":…,"files":{…}}`). It is downloaded from the report's card; `tools/pull_submissions.py` lists submissions only and does not fetch it | `work-ship.json`, `work-structure.json`, `work-avatar.json` (`.json.gz` above 300 KB) |
| Texture, form, tool look | The editor's **draft** (`IEditorDocument.SaveDraft`): what is on the canvas, whether it could be saved or not | `work-texture.json`, `work-form.json`, `work-toollooks.json` — `{"formatVersion":1,"editor":…,"fields":{"name":…,"draft":…}}` |

It is not a submission: no consent boxes, because nothing of it is ever put into the game, and no save gate — a
module that does not pass the gate is exactly what a tester wants to show. An empty bench, or a file above
`PlaytestWork.MaxBytes` (1.5 MB as it travels), is simply not attached; the report never fails over it.

## The server side: the playtest watch

`PlaytestWatchIntent` (355) names the game events the task being worked on waits for; the server
(`GameServerPlaytestWatch.cs`) registers **one listener per session** on the game-event bus and reports every such
event **that player** causes as `PlaytestEvent` (357), once per tick, added up per key and argument. It answers
with `PlaytestWatchAck` (356). It stores nothing, pays nothing, counts nothing: what happened is still the server's
word, only the tick mark is the client's — and a badge buys nothing, so the server has no say in it. No protocol
bump: an older server drops the intent, no ack comes, and the client lets the player tick those steps off.

- At most 16 patterns, published server events only (UI and editor events are the client's own). The ack names the
  patterns the server took; one it does not know is left out, and the client then offers that step's two buttons.
- Passes a held singleplayer world (`PausedMayHandle`): the page is reached from the pause menu.
- The listener leaves the bus with the session (`LeaveCleanupSteps`).
- Registering it is what makes the "measured only while a step listens" events (`move:walked`, `flight:flown` …)
  start measuring.

## Tests

| Suite | File |
|---|---|
| Model, reader, validator, command list, version window | `tests/BlocksBeyondTheStars.Tests/PlaytestCatalogTests.cs` |
| The validator's rules about a step's event: newer than the first client, a world that cannot bring it about | `PlaytestCatalogEventRulesTests.cs` |
| The new events on a live server | `PlayerActionEventTests.cs`, `SpaceManeuverTests.cs` |
| Seed-bound tasks: the named place stands in the world of the seed (and the seed search) | `PlaytestSeedBoundTasksTests.cs` |
| Inbox: store, catalogue routes | `ReportHostPlaytestTests.cs` |
| Inbox: category, attachment, pairing | `ReportHostPlaytestReportTests.cs` |
| Inbox: report data, stats, confirm, `mine`, admin pages, retention | `ReportHostPlaytestAdminTests.cs` |
| Client logic | `tests/BlocksBeyondTheStars.Client.Tests/PlaytestProgressTests.cs`, `PlaytestReportTests.cs`, `PlaytestWorldPlanTests.cs`, `PlaytestWorkTests.cs` |
| **End to end**: the real client classes against the real inbox in-process | `PlaytestInboxEndToEndTests.cs` |
| The watch over the wire against the real server, also while held | `PlaytestWatchClientTests.cs` |
| The repository's task files: no validator finding, shown as written, planets and items exist | `tests/BlocksBeyondTheStars.Tests/PlaytestRepositoryTasksTests.cs` |
| **Unity EditMode**: every Test Pilot screen in all 14 languages, each text measured | `client/Assets/Tests/EditMode/TestPilotTextFitEditModeTests.cs` |

The end-to-end test lives in the client suite on purpose: the server suite must not reference `Client.Core` (the
CI's client lane relies on that, `scripts/lane-test-filter.py`).

**Looking at the screens without playing:** a development build run with `-captureTestPilot` against a local inbox
photographs the page, a task's view, the list over the Ship Editor, the report dialog and the "confirmed" dialog
([docs/screenshots/README.md](../screenshots/README.md) § *Test Pilot screenshots*). It is the quick check after a
change to these screens — the unit tests cannot see a list that lies over an editor's own card.

**Text fit in every language:** `TestPilotTextFitEditModeTests` builds the page (empty and full), a task's view in
its states, the report dialog, the note prompt, the "confirmed" dialog and the list over an editor with the
repository's own task files on them, and holds every text to the rules of `UiTextFit` (shared with the editors'
test: words at least 13 px, wrapping only between words, nothing cut, a one-line text inside its box). It builds
its hub with `PlaytestHub.CreateForTest` — progress in memory, no inbox, no file. The Unity suites do not run in
PR CI: run it locally after a change to these screens or to a task's texts
(`-runTests -testPlatform EditMode -testFilter TestPilotTextFitEditModeTests`).

## Privacy

A playtest report carries the answers, each step's outcome and note, the time the task took, the game version and
platform, the reply key, an optional nickname and — only when the tester ticked it — the editor work. No e-mail
field exists. In a world the server's `/bump` snapshot goes out as with an F1 report, and the report itself carries
the `graphics` node of an F1 report (preset, render scale, anti-aliasing, screen size, graphics API — no device
name): the privacy text promises "the same technical game data as an in-game bug report" for exactly that case. A
report from the main menu or an editor carries none (`PlaytestHub.BuildReport`, #3838). The audience includes
children: task texts never ask for personal data, and the admin page says so on the editing form.
