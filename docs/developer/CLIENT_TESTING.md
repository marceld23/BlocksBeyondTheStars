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
| 1.5 | `UnityEdit` | Unity EditMode unit tests (ClientWorld, BlockTextureAtlas) | yes (Editor) | no |
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

`tests/BlocksBeyondTheStars.Client.Tests/` (net8.0, xUnit). It references `Client.Core` **and** the real
`GameServer`, and wires them through one `LoopbackLink` — no Unity, no sockets, same authoritative logic as
the shipping game.

[`ClientServerHarness`](../../tests/BlocksBeyondTheStars.Client.Tests/ClientServerHarness.cs) does the wiring:
a real `GameServer` on a `LoopbackServerTransport` and a real `NetworkClient` on a `LoopbackClientTransport`
sharing the link. `Tick()` advances the server one tick and drains the client inbox; `PumpUntil(condition)`
loops until the client has received what you assert on. The client's authoritative state (join, chunks, block
changes, inventory, craft results, rejections) is captured into public fields, plus a `ClientWorld` view fed
from the captured chunk/block messages.

These are the bulk of the value and run in `dotnet test`. All test classes carry `[Trait("Suite","ClientCore")]`.

## Tier 1.5 — Unity EditMode (`UnityEdit`)

`client/Assets/Tests/EditMode/`. Headless in-Editor unit tests for **Unity-runtime-safe** logic — `ClientWorld`
round-trips run inside the Editor/Mono. (Code that calls `Object.Destroy` cannot run here — `Destroy` is illegal
in edit mode — so the atlas test lives in PlayMode below.)

## Tier 2 — Unity PlayMode (`UnityPlay`)

`client/Assets/Tests/PlayMode/`. Two tests:
- **The shipping path** — a `[UnityTest]` coroutine launches the **published** dedicated server
  (`LocalServerLauncher`, the same code Singleplayer uses) as a child process on loopback UDP, then drives a
  real `NetworkClient` through connect → join → first chunks. Skipped if the server has not been published into
  `StreamingAssets/server/`. Deliberately smoke-level (slow; needs the Editor + the exe).
- **The real `BlockTextureAtlas` build** from the synced content (runs in PlayMode because the atlas builder
  calls `Object.Destroy`). Skipped if `StreamingAssets/data` is missing.

The PlayMode assembly must NOT be Editor-only (`includePlatforms: []`), otherwise Unity classifies it as an
EditMode assembly and the tests run in the wrong mode (and `UnityPlay` finds nothing).

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
`Unity.exe` (pass `-UnityPath` if it is not at the default `6000.4.9f1` location); the runner first syncs the
shared libs/content, and for `UnityPlay` also publishes the bundled server. Unity results (NUnit XML) and logs
land under `TestResults/`.

You can still run a single suite directly: `dotnet test tests/BlocksBeyondTheStars.Client.Tests`.

## Notes & caveats

- **Unity batch exit codes are unreliable** (the Editor can relaunch a child process), so the runner waits for
  Unity to exit and parses the NUnit `<test-run>` result file for the authoritative pass/fail counts.
- The Unity test assemblies are gated by `UNITY_INCLUDE_TESTS` / Editor platform, so they never enter the
  player build.
- `com.unity.test-framework` is pinned in `client/Packages/manifest.json`; adjust the version if your Editor
  resolves a different one.
- CI is intentionally **not** wired up yet — these run locally. Tier 1 (`Dotnet` + `ClientCore`) is the part
  that would drop straight into a `dotnet test` CI job with no Unity license needed.
