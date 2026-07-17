# Player Feedback ("Spieler Feedback")

The **F1** hotkey lets any player send a bug report **or** a feature wish — one
form, no type distinction: a title, a description, an optional e-mail, and a short note that game data plus
a screenshot are attached. On send the client posts the report to the official report inbox (the
ReportHost on the VPS — see [REPORT_HOST](REPORT_HOST.md)) and also fires the existing `/bump`
snapshot. (F1 is advertised in the on-foot HUD controls hint, `ui.hud.hint`, and in the space-flight
cruise hint, `ui.space.controls` — it works in both modes; only menus/chat/death-prompt block it.)

This is deliberately **player-facing** and separate from the developer `/bump` chat command (which still
exists and produces the rich local diagnostic snapshot — see [BUG_REPORTS](BUG_REPORTS.md) if present, or
`GameServerBump.cs`).

## Flow

```
F1
   │  capture full-frame JPG  (HUD visible, dialog NOT yet shown)
   ▼
FeedbackUi dialog  (title, description, optional e-mail, privacy hint)
   │  Send  (body serialized ONCE on the main thread)
   ├──────────────► HTTPS POST ──►  reports.blocksbeyondthestars.de/api/bugreport
   │                desktop: FeedbackUploader.UploadRawJson on a background Task
   │                WebGL:   UnityWebRequest coroutine (no HttpClient/threads in WASM;
   │                         the ReportHost answers the CORS preflight)
   │                on failure ──► FeedbackSpool (persistentDataPath/feedback) — retried on later
   │                              session starts, max 5 attempts, then parked in givenup/
   └──────────────► NetworkClient.SendBumpReport()  ──►  GameServer  (rich local snapshot on own/SP server)
                                                            │  when a crash-upload sink is configured
                                                            └──► same snapshot (minus image) ──► report inbox
```

### Why client-direct for the web upload

The web POST is sent **from the client**, not relayed through the game server, so feedback reaches the
developers even when the player is on someone else's dedicated server. It is fully decoupled from the game
protocol. The parallel `/bump` message is kept so that, when the player *is* on their own / singleplayer
server, the server still writes its rich snapshot (inventory, position, surroundings, 30 s history).

### Server-side snapshot forwarding (singleplayer + fleet)

The rich `/bump` snapshot no longer stays local-only: when the server has a crash-upload sink configured
(`CrashReportEndpoint` + `CrashReportApiKey`, see [REPORT_HOST](REPORT_HOST.md)), `GameServer` also posts
the snapshot to the inbox — wrapped in the same wire shape as crash reports (`reportJson.reportType:
"bump"`, `source: "server"`, deliberately **no** `reportJson.kind` so the inbox files it under category
*feedback*, not *crash*). The screenshot is never re-sent (the client's own POST already carries it) and
the send is one best-effort background attempt; the on-disk `bumps/` file remains the source of truth.

Who has a sink: **singleplayer** — `LocalServerLauncher` hands the client's baked-in feedback key to the
bundled server as `BBS_CRASH_REPORT_KEY` (release builds only; dev builds have an empty key and stay
local-only). This also means SP server **crash reports** now upload automatically. **Hosted fleet** —
world instances get the key from the WorldHost (`BBS_WH_CRASH_REPORT_KEY`, #363). **Self-hosted
dedicated servers** — off by default (no phone-home), opt-in via the config/env above.

## Code map

| Concern | File |
|---|---|
| Payload model | `src/BlocksBeyondTheStars.Client.Core/Feedback/FeedbackReport.cs` |
| HTTP uploader (testable, no Unity) | `src/BlocksBeyondTheStars.Client.Core/Feedback/FeedbackUploader.cs` |
| Bounded offline retry queue | `src/BlocksBeyondTheStars.Client.Core/Feedback/FeedbackSpool.cs` |
| Tests (local `HttpListener` endpoint) | `tests/BlocksBeyondTheStars.Client.Tests/FeedbackUploaderTests.cs` |
| Tests (spool life cycle) | `tests/BlocksBeyondTheStars.Client.Tests/FeedbackSpoolTests.cs` |
| UI + capture + dual send | `client/Assets/BlocksBeyondTheStars/Scripts/FeedbackUi.cs` |
| Wired into the world | `client/Assets/BlocksBeyondTheStars/Scripts/WorldRig.cs` |
| API key (build secret) | `client/Assets/BlocksBeyondTheStars/Scripts/BugReportBuildSecrets.cs` |
| Icon | `client/Assets/Resources/icons/btn_feedback.png` |
| Strings | `data/locales/{de,en}.json` — `ui.feedback.*`, `ui.contribute.feedback` |

The uploader lives in the Unity-free `Client.Core` assembly and uses `System.Net.Http.HttpClient` (not
`UnityWebRequest`) so the **exact same code** runs in the Unity player and in the headless test suite, which
points it at a local `HttpListener` ("simulierte lokale Schnittstelle"). Only the blocking HTTP POST runs on
a background `Task`; the report (which reads Unity APIs) is built and serialized on the main thread first.
**Exception: WebGL** — WASM has neither sockets nor threads, so `FeedbackUi` posts the identical serialized
body via a `UnityWebRequest` coroutine instead (same endpoint + `x-bugreport-key` header; the ReportHost's
CORS answer on `/api/bugreport` makes the cross-origin call possible from play.* / glitch.fun pages).

A failed upload is never lost: the body is queued in `FeedbackSpool` (`persistentDataPath/feedback`,
IndexedDB-backed on WebGL) and retried on later session starts — bounded to `MaxAttempts = 5` per report
(counted in the file name), after which the file is parked in `givenup/` for a manual send. The player sees
`ui.feedback.queued` instead of an error.

## The API key (spam gate, not a secret)

The key only gates spam/abuse for the alpha — it ships inside the client and can be extracted, so the
endpoint must accept feedback **only**, cap payload size, and rate-limit. The ReportHost does exactly
that (see [REPORT_HOST](REPORT_HOST.md)); the CI secret's value is its `BBS_REPORTS_WRITE_KEY`.
(Historical: the original inbox was a Wix/Velo function at `www.blocksbeyondthestars.com/_functions/bugreport`
on the same wire contract — builds released before the cutover still post there.)

`BugReportBuildSecrets.ApiKey` is empty in committed/dev builds (so dev builds never post to production;
the dialog then reports `sent_local` after writing the `/bump` snapshot). A release build injects the real
key via a **git-ignored** partial that implements `ApplyApiKey`:

```
client/Assets/BlocksBeyondTheStars/Scripts/BugReportBuildSecrets.Generated.cs   (git-ignored)
```

### CI step (release builds)

The release workflows write the partial from the GitHub **Environment secret** `BBS_BUGREPORT_API_KEY`
(scoped to the `release` environment / `v*` tags) — never echo it:

```yaml
- name: Generate feedback API-key secret
  shell: pwsh
  env:
    BBS_BUGREPORT_API_KEY: ${{ secrets.BBS_BUGREPORT_API_KEY }}
  run: |
    $path = "client/Assets/BlocksBeyondTheStars/Scripts/BugReportBuildSecrets.Generated.cs"
    @"
    namespace BlocksBeyondTheStars.Build
    {
        public static partial class BugReportBuildSecrets
        {
            static partial void ApplyApiKey(ref string key) => key = "$env:BBS_BUGREPORT_API_KEY";
        }
    }
    "@ | Set-Content $path
```

## Open items

- Set the `BBS_BUGREPORT_API_KEY` Environment secret to the deployed ReportHost's write key (the old
  `WIX_BUGREPORT_API_KEY` secret is obsolete after the cutover).
- Keep the legacy Wix endpoint accepting until pre-cutover builds have died out, then retire its key.
- Confirm the GDPR/privacy note on the website matches `ui.feedback.hint`.
