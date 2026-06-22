# Player Feedback ("Spieler Feedback")

The **F1** hotkey lets any player send a bug report **or** a feature wish — one
form, no type distinction: a title, a description, an optional e-mail, and a short note that game data plus
a screenshot are attached. On send the client posts the report to the website API and also fires the
existing `/bump` snapshot. (F1 is advertised in the on-foot HUD controls hint, `ui.hud.hint`.)

This is deliberately **player-facing** and separate from the developer `/bump` chat command (which still
exists and produces the rich local diagnostic snapshot — see [BUG_REPORTS](BUG_REPORTS.md) if present, or
`GameServerBump.cs`).

## Flow

```
F1
   │  capture full-frame JPG  (HUD visible, dialog NOT yet shown)
   ▼
FeedbackUi dialog  (title, description, optional e-mail, privacy hint)
   │  Send
   ├──────────────► FeedbackUploader.Upload()  ──HTTPS POST──►  www.blocksbeyondthestars.com/_functions/bugreport
   │                (client-direct; reaches the devs on ANY server)
   └──────────────► NetworkClient.SendBumpReport()  ──►  GameServer  (rich local snapshot on own/SP server)
```

### Why client-direct for the web upload

The web POST is sent **from the client**, not relayed through the game server, so feedback reaches the
developers even when the player is on someone else's dedicated server. It is fully decoupled from the game
protocol. The parallel `/bump` message is kept so that, when the player *is* on their own / singleplayer
server, the server still writes its rich snapshot (inventory, position, surroundings, 30 s history).

## Code map

| Concern | File |
|---|---|
| Payload model | `src/BlocksBeyondTheStars.Client.Core/Feedback/FeedbackReport.cs` |
| HTTP uploader (testable, no Unity) | `src/BlocksBeyondTheStars.Client.Core/Feedback/FeedbackUploader.cs` |
| Tests (local `HttpListener` endpoint) | `tests/BlocksBeyondTheStars.Client.Tests/FeedbackUploaderTests.cs` |
| UI + capture + dual send | `client/Assets/BlocksBeyondTheStars/Scripts/FeedbackUi.cs` |
| Wired into the world | `client/Assets/BlocksBeyondTheStars/Scripts/WorldRig.cs` |
| API key (build secret) | `client/Assets/BlocksBeyondTheStars/Scripts/BugReportBuildSecrets.cs` |
| Icon | `client/Assets/Resources/icons/btn_feedback.png` |
| Strings | `data/locales/{de,en}.json` — `ui.feedback.*`, `ui.contribute.feedback` |

The uploader lives in the Unity-free `Client.Core` assembly and uses `System.Net.Http.HttpClient` (not
`UnityWebRequest`) so the **exact same code** runs in the Unity player and in the headless test suite, which
points it at a local `HttpListener` ("simulierte lokale Schnittstelle"). Only the blocking HTTP POST runs on
a background `Task`; the report (which reads Unity APIs) is built on the main thread first.

## The API key (spam gate, not a secret)

The key only gates spam/abuse for the alpha — it ships inside the client and can be extracted, so the
website endpoint must accept feedback **only**, cap payload size, and rate-limit. See the requirements
document for the Wix/Velo backend (`http-functions.js`, the `BugReports` CMS collection, media upload).

`BugReportBuildSecrets.ApiKey` is empty in committed/dev builds (so dev builds never post to production;
the dialog then reports `sent_local` after writing the `/bump` snapshot). A release build injects the real
key via a **git-ignored** partial that implements `ApplyApiKey`:

```
client/Assets/BlocksBeyondTheStars/Scripts/BugReportBuildSecrets.Generated.cs   (git-ignored)
```

### CI step (release builds)

Add to the release workflow, using a GitHub **Environment secret** `WIX_BUGREPORT_API_KEY` (scoped to the
`release` environment / `v*` tags) — and never echo it:

```yaml
- name: Generate feedback API-key secret
  shell: pwsh
  env:
    WIX_BUGREPORT_API_KEY: ${{ secrets.WIX_BUGREPORT_API_KEY }}
  run: |
    $path = "client/Assets/BlocksBeyondTheStars/Scripts/BugReportBuildSecrets.Generated.cs"
    @"
    namespace BlocksBeyondTheStars.Build
    {
        public static partial class BugReportBuildSecrets
        {
            static partial void ApplyApiKey(ref string key) => key = "$env:WIX_BUGREPORT_API_KEY";
        }
    }
    "@ | Set-Content $path
```

## Open items

- Stand up the Wix/Velo endpoint (`/_functions/bugreport`) per the requirements doc, then wire the CI step
  above and the `WIX_BUGREPORT_API_KEY` secret.
- Optional: keep a local copy of a feedback report when the upload fails, for a later retry.
- Confirm the GDPR/privacy note on the website matches `ui.feedback.hint`.
