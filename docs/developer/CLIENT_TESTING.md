# Client Testing — testing the Unity client against the real game server

**Status:** implemented 2026-06-20. How the Unity client is tested, why it is split the way it is, and how
to run each tier. Contributor rules are in [../../AGENTS.md](../../AGENTS.md); the build pipeline is in
[DEVELOPER.md](DEVELOPER.md).

## The idea

The golden rule (see AGENTS.md) is that the **.NET server is authoritative** and the **client only renders
what the server reports**. So the most valuable client test asks: *given the real server, does the client
correctly send intents and apply the authoritative replies?* We test that against the **real `GameServer`** —
not a mock — at three tiers, fastest first.

| Tier | Suite | What runs | Needs Unity? | Needs server exe? |
|---|---|---|---|---|
| 1 | `ClientCore` | Real `NetworkClient` ↔ real in-process `GameServer` over a `LoopbackLink` | no | no |
| 1.5 | `UnityEdit` | Unity EditMode unit tests (ClientWorld, mesh packing, shader/asset rules, the main-menu golden, the panel and view registries) | yes (Editor) | no |
| 2 | `UnityPlay` | Real `NetworkClient` ↔ the **published** server exe over loopback UDP | yes (Editor) | yes |
| — | `Dotnet` | The existing .NET server/shared xUnit suite | no | no |

## The `Client.Core` split

To make Tier 1 possible, the **Unity-free** client logic lives in its own assembly:

```
src/BlocksBeyondTheStars.Client.Core/   netstandard2.1 — NetworkClient, ClientWorld
```

- It targets `netstandard2.1` (like `Shared`/`WorldGeneration`/`Networking`), so the **exact same**
  `NetworkClient` + `ClientWorld` run inside the Unity player *and* inside `dotnet test`.
- It must stay **Unity-free** (no `UnityEngine` reference). `NetworkClient` speaks Shared's `Vector3f`; the
  Unity layer adds `UnityEngine.Vector3` overloads via
  [`NetworkClientUnityExtensions`](../../client/Assets/BlocksBeyondTheStars/Scripts/NetworkClientUnityExtensions.cs)
  so existing call-sites (PlayerController, SpaceView, …) compile unchanged.
- Unity consumes it as a **precompiled DLL**, exactly like the other shared libs. Two places must list it
  (miss either and the player build fails `CS0246`, while the Editor still compiles — a classic trap):
  1. `client/Assets/BlocksBeyondTheStars/BlocksBeyondTheStars.Client.asmdef` → `precompiledReferences`.
  2. `scripts/sync-client-libs.ps1` → the `$projects` list (so the DLL is published into `Plugins/`).

Unity-**coupled** code (meshers producing `Mesh`, MonoBehaviours, views) stays in the client asmdef and is
only reachable from the Unity tiers (1.5 / 2).

## Tier 1 — headless client ↔ server (`ClientCore`)

`tests/BlocksBeyondTheStars.Client.Tests/` (net10.0, xUnit). It references `Client.Core` **and** the real
`GameServer`, and wires them through one `LoopbackLink` — no Unity, no sockets, same authoritative logic as
the shipping game.

[`ClientServerHarness`](../../tests/BlocksBeyondTheStars.Client.Tests/ClientServerHarness.cs) does the wiring:
a real `GameServer` on a `LoopbackServerTransport` and a real `NetworkClient` on a `LoopbackClientTransport`
sharing the link. `Tick()` advances the server one tick and drains the client inbox; `PumpUntil(condition)`
loops until the client has received what you assert on. The client's authoritative state (join, chunks, block
changes, inventory, craft results, rejections) is captured into public fields, plus a `ClientWorld` view fed
from the captured chunk/block messages.

These are the bulk of the value and run in `dotnet test`. All test classes carry `[Trait("Suite","ClientCore")]`.

Reusable fakes live in `tests/BlocksBeyondTheStars.Client.Tests/Helpers/` — today
[`RecordingClientTransport`](../../tests/BlocksBeyondTheStars.Client.Tests/Helpers/RecordingClientTransport.cs): a
client-side `IClientTransport` whose `Sent` holds every decoded message the `NetworkClient` sent, `Deliver(message)`
feeds it a server message, `DeliverRaw(bytes)` hands it raw wire bytes no message encodes to (an unknown tag, an empty
packet), `Disconnects` counts hang-ups. Do not copy it into a test class — the server suite's
`SharedTestHelperGuardTests` scans this project too.

Some tests here are **source guards**: the Unity scripts cannot be compiled into this suite (they need `UnityEngine`),
so a guard reads a script's text and asserts that a wiring is there. A large client class is split into partial files
(`HudUi.cs` + `HudUi.<Widget>.cs`, `CraftingTechShipUI.cs` + `CraftingTechShipUI.<Part>.cs`, `SpaceView.cs` +
`SpaceView.<Concern>.cs`, `PlayerController.cs` + `PlayerController.<Concern>.cs`, `GameBootstrap.cs` +
`GameBootstrap.<Concern>.cs`, `UiMainMenu.cs` + the `UiOfficialWorlds*.cs` overlay), so a guard over such a class reads
all of its files, not one by name: `ClientTestPaths.ScriptParts("HudUi")` returns `HudUi.cs` and every `HudUi.*.cs`
part in a stable order, and `ScriptPartsText("HudUi")` joins them into one text — the guard then keeps finding a member
whichever part it moves to (`SeamWiringTests`, `RumbleMixerTests`; `CraftDetailLayoutTests` reads every
`CraftingTechShipUI*.cs`, `AuditLowLeftoversTests` the menu and its overlay). `ClientTestPaths.PlayerControllerSources()`
also appends the controller's sibling components `PlayerCapture.cs`, `PlayerVehicleDrive.cs` and `PlayerTrainRider.cs`,
so a `DoesNotContain` guard still covers the code that moved into them. A guard on one `GameBootstrap` concern reads
that concern's file by name (`GameBootstrap.Entities.cs`, `GameBootstrap.Menus.cs`, …), and `VendorTradeWiringTests`
pins the exact set of `GameBootstrap*.cs` files, so a renamed or added part cannot slip past its scan.

## Tier 1.5 — Unity EditMode (`UnityEdit`)

`client/Assets/Tests/EditMode/`. Headless in-Editor unit tests for **Unity-runtime-safe** logic — `ClientWorld`
round-trips run inside the Editor/Mono. (Code that calls `Object.Destroy` cannot run here — `Destroy` is illegal
in edit mode — so the full atlas build test lives in PlayMode below. `BlockAtlasLooksGoldenEditModeTests` builds the
atlas in EditMode anyway, with the per-tile `Destroy` errors ignored, to pin the code-painted block tiles by checksum,
#2906 — see [DEVELOPER.md § Adding or removing a block](DEVELOPER.md#adding-or-removing-a-block).)

Three EditMode suites pin the client's code-built structure (#2910, #2913, #2914):

- **`MainMenuHierarchyEditModeTests`** builds the main menu (`UiMainMenu.Build`) and compares a dump of the whole canvas
  hierarchy — every GameObject in sibling order with its components, rect, texts and colours — with
  `client/Assets/Tests/EditMode/MainMenuHierarchy.golden.txt`; its wiring tests click the real buttons. A deliberate
  menu change fails it: re-bake by copying the `MainMenuHierarchy.actual.txt` the failing test writes (its path is in
  the failure message) over the golden. The dump covers the native branch only, shows locale keys instead of texts and
  masks the version. The golden was captured **without the menu-button and system-check icons** (their textures did
  not load in the Editor, so `UiKit.AddIcon` added nothing): once they load, re-bake it deliberately and check that the
  diff is only the new icon images.
- **`UiPanelEditModeTests`** opens the `UiPanel` pilots without a `Game` (sort order, pad focus, overlay count) and pins
  `ClientContext`'s register/unregister rules; **`RigViewsEditModeTests`** pins `RigViews` (null while unset, the
  registered view, null again once that view is destroyed). See [UI_PANELS.md](UI_PANELS.md#tests).

This tier is also where **asset-level** rules are checked, because they need the `AssetDatabase`:
`ShaderSrpBatcherEditModeTests` asserts that the project's shaders compile and that every per-material property
is declared inside `CBUFFER_START(UnityPerMaterial)`, which is what makes a shader SRP-Batcher-compatible
(#573). ⚠️ One caveat worth knowing before adding more graphics assertions: the runner passes `-nographics`, and
Unity's own compatibility API can only answer for a SubShader it has actually **compiled** — headless it reports
`Not initialized ()` for every URP SubShader, URP's own `Lit` included. That test therefore canaries on `Lit`
and skips itself rather than fail on a blind probe; the source-level check next to it needs no graphics device
and is the one that holds in CI. Run the suite from the Editor's Test Runner window for the real verdict.

## Tier 2 — Unity PlayMode (`UnityPlay`)

`client/Assets/Tests/PlayMode/`. Six test classes:
- **The shipping path** — a `[UnityTest]` coroutine launches the **published** dedicated server
  (`LocalServerLauncher`, the same code Singleplayer uses) as a child process on loopback UDP, waits for the
  server's own ready line (`LocalServerLauncher.Ready`, up to 60 s — never a fixed sleep, a fresh world takes ~8 s
  to boot, #2923), then drives a real `NetworkClient` through connect → join → first chunks. Skipped if the
  server has not been published into `StreamingAssets/server/`. Deliberately smoke-level (slow; needs the
  Editor + the exe).
- **The real `BlockTextureAtlas` build** from the synced content (runs in PlayMode because the atlas builder
  calls `Object.Destroy`). Skipped if `StreamingAssets/data` is missing.
- **`InputFocusGuardPlayModeTests`** — a focused input field whose canvas is switched off must be released without
  the uGUI caret exception (it needs real frames and caret blinks).
- **`UiPanelPlayModeTests`** — destroying a `UiPanel` takes its scene-root canvas along (the rig-teardown leak,
  #2303/#2925); PlayMode because `OnDestroy` does not run in edit mode. See [UI_PANELS.md](UI_PANELS.md#tests).
- **`RigCanvasCleanupPlayModeTests`** — the same for the rig's prompts and screens that are not `UiPanel`s
  (`LaunchPrompt`, `VendorChoicePrompt`, `RespawnPrompt`, `DeathFx`, `TouchControlsUi`, `VendorTradeUI`, the star chart,
  the Tab menu's dialogs, …, #2936). Its PR-CI twin is the Client.Tests source guard `RigCanvasCleanupGuardTests`:
  every canvas a component of the rig (`WorldRig.Build` and what it adds) assigns from `UiKit.Create…Canvas` must be
  kept in a field and destroyed in that component's teardown (`OnDestroy`, or an `OnDisable` that closes it — the
  surface map's way).
- **`SpaceEntityAssetsPlayModeTests`** — a space entity's model (and another pilot's placeholder hull) takes the
  Materials and 64×64 block tiles `ModelKit` made for it along when it despawns, counted with
  `Resources.FindObjectsOfTypeAll`; a part flung off an exploding hostile keeps its look until it is gone too (#2938).
  The models are built between `ModelKit.BeginOwned`/`EndOwned`, which hands every part that wears them a
  `ModelAssetLease`; the last lease to go destroys them. Its PR-CI twin is the Client.Tests source guard
  `SpaceEntityAssetGuardTests` (every kit asset is recorded, both spawn builders run in an owned scope, no entity
  builder caches a kit material in a field).

The PlayMode assembly must NOT be Editor-only (`includePlatforms: []`), otherwise Unity classifies it as an
EditMode assembly and the tests run in the wrong mode (and `UnityPlay` finds nothing).

## Booting the whole rig: `-perfProbe`

No tier boots the whole in-game rig (`WorldRig` + `GameBootstrap` + `PlayerController` against a running server, frame
after frame). The built player's **`-perfProbe`** mode (`PerfProbe.cs`) does: it starts a fixed-seed singleplayer
world, idles, walks a scripted path, writes frame-time/GC numbers (`perf_baseline_*.json` + `.txt`) and quits with exit
code 0 — 1 when the world does not start. A change to the rig wiring is checked with a PerfProbe run before and after:
both exit 0, `Player.log` shows no exception, and the numbers stay comparable.

## Running the tests

Use the selectable runner `scripts/run-tests.ps1`. The default runs **only the fast .NET suites**; the Unity
suites are opt-in, so you choose whether they ride along.

```powershell
./scripts/run-tests.ps1                          # Dotnet + ClientCore (default, no Unity)
./scripts/run-tests.ps1 -Suites All              # everything, including the Unity Editor suites
./scripts/run-tests.ps1 -Suites ClientCore       # just the headless client<->server tests
./scripts/run-tests.ps1 -Suites Dotnet,UnityEdit # server suite + Unity EditMode
./scripts/run-tests.ps1 -Coverage                # the .NET suites with a coverage report
```

`-Suites` accepts any of `Dotnet`, `ClientCore`, `UnityEdit`, `UnityPlay`, `All`. The Unity suites need
`Unity.exe` (found by `scripts/resolve-unity.ps1` from `ProjectVersion.txt`; pass `-UnityPath` to override); the runner first syncs the
shared libs/content, and for `UnityPlay` also publishes the bundled server. Unity results (NUnit XML) and logs
land under `TestResults/`.

You can still run a single suite directly: `dotnet test tests/BlocksBeyondTheStars.Client.Tests`.

## Look checks without a playtest

Rendering changes (shaders, lighting, post, weather) have no assertion to fail, so they are checked by
**capturing the same world twice** — before and after, same seed, Low and High — with the built player's
clip recorder (`-captureClip`). A clip pins time of day, weather and where the player stands (cave, under
water, forest, shore, ridge), so dawn mist, a rain shower or a lava cave can be requested on purpose
instead of hoping the spawn rolls them. The manifest fields and a frames-only recipe are documented in
[docs/screenshots/README.md](../screenshots/README.md#video-clips--captureclip--and-look-checks-without-a-playtest);
the standing set of look-check scenes is `scripts/clip-manifests/atmosphere-check.json`.

Two rules learned the hard way (#2405): **never run a capture set while a Unity or IL2CPP build or the test
suite is running** — a starved client lifts its spawn freeze before any chunk has streamed and every pose falls
back to the spawn over the far-terrain mesh (the log says `cells loaded=0`); and **count `Shader error` lines in
`client/build.log` after every build** — a broken include still ends in `build: Succeeded`, with every shader that
includes it rendering its fallback.

The browser build has its own check: `scripts/webgl-browser-check.py` loads a local WebGL build in Playwright's
Chromium (WebGL 2 in the headless shell, WebGPU with `--channel chromium`) and records the console and screenshots —
see [WEBCLIENT_FEASIBILITY.md](WEBCLIENT_FEASIBILITY.md#webgpu-first-webgl-2-as-the-fallback-2026-10-2390).

## Notes & caveats

- **Unity batch exit codes are unreliable** (the Editor can relaunch a child process), so the runner waits for
  Unity to exit and parses the NUnit `<test-run>` result file for the authoritative pass/fail counts.
- The Unity test assemblies are gated by `UNITY_INCLUDE_TESTS` / Editor platform, so they never enter the
  player build.
- `com.unity.test-framework` is pinned in `client/Packages/manifest.json`; adjust the version if your Editor
  resolves a different one.
- **CI runs Tier 1 on every pull request.** [`.github/workflows/ci.yml`](../../.github/workflows/ci.yml)
  builds and runs the two headless suites (`Dotnet` + `ClientCore`) on `ubuntu-latest` with **no Unity
  license needed**, and fails the build on any warning (`-warnaserror`). The Unity tiers (`UnityEdit` /
  `UnityPlay`) stay **local-only** — they need the Editor — so run `./scripts/run-tests.ps1 -Suites All`
  yourself before a client-affecting change. See [DEVELOPER.md](DEVELOPER.md#continuous-integration-pull-requests).
