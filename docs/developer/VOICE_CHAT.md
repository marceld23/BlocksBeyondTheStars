# Voice chat (live, opt-in) + tiered radio reach

This document covers two related features added together:

1. **Tiered radio reach** — the radio item now has upgrade tiers that decide how far comms carry. This
   applies to **both** the existing text chat **and** the new voice chat.
2. **Live voice chat** — push-to-talk microphone audio, relayed by the server to the same audience as text
   chat. Opt-in on the server, and a build-time opt-in on the client (the Opus codec is an optional plugin).

The design rationale (why a thin server relay rather than P2P, why Concentus, why push-to-talk first) is in
the memory note `voice-chat-analysis`. This file is the implementation reference.

## Tiered radio reach

Three craftable radio items, each a strict upgrade in **reach**:

| Item | Reach | Recipe (workshop) |
|---|---|---|
| `comm_radio` | same **world** (planet/moon/station you're on) | unchanged |
| `system_radio` | same **star system** | `comm_radio` + circuit boards / titanium / data fragment |
| `galaxy_radio` | **everyone** in the game (the old flat behaviour) | `system_radio` + more of the same |

Defined in `data/items.json`, `data/recipes.json`, `data/blueprints.json`, localized in
`data/locales/{de,en}.json`. The blueprints chain via `prerequisites`, so you unlock comm → system → galaxy.

**Server side** (`GameServer.cs`): the audience is chosen by the **widest** tier the sender **wears** — since
#2110 a radio works only in a suit module slot (`PlayerState.Equipment`); holding one does nothing:

- `HasAnyRadio(session)` — the chat/voice gate (wears any tier).
- `CarriesUnwornRadio(session)` (#2567) — a radio in the backpack/quick-bar or the active ship's hold, not worn.
  A refused chat line then carries `@srv.misc.radio_not_worn` ("put it on") instead of `@srv.misc.need_radio`.
- `RadioAudience(sender)` — the recipient set: galaxy = all joined; system = same `CelestialBody.SystemId`;
  comm = same `CurrentLocationId`. Station/void worlds (no resolvable system) fall back to same-world reach.
- `SendToRadioAudience(...)` / `SendToRadioAudienceExcept(...)` — encode once, send to that set. Text chat
  includes the sender (local echo); voice excludes the sender (you don't hear yourself).

`HandleChat` was rewired from a flat `Broadcast` to `SendToRadioAudience`. Tests:
`tests/BlocksBeyondTheStars.Tests/RadioTierTests.cs`.

## Voice chat — architecture

Thin, opaque **server relay** (no P2P): the server never decodes audio. One `VoiceFrame` carries ~20 ms of
Opus-encoded mono audio.

```
mic (48 kHz mono) → 20 ms frames → Opus encode → VoiceFrame{Opus,Sequence}  (client → server, Unreliable)
server: HandleVoice → gate (voice enabled + radio) → stamp FromPlayerId → relay to RadioAudience (except self)
client: VoiceReceived → per-speaker jitter buffer → Opus decode → streaming AudioSource (flat 2D)
```

- **Message:** `VoiceFrame` (`Messages/ChatMessages.cs`), NetCodec tag **169**, used both directions. Sent
  `DeliveryMode.Unreliable` — a dropped 20 ms frame is inaudible and beats waiting for a resend.
- **Server:** `GameServer.HandleVoice` — drops the frame unless `ServerConfig.VoiceChatEnabled`, the player
  wears a radio, and the payload is ≤ 4 KB; stamps the authoritative sender id; relays via the tiered
  audience. The server does **zero** audio processing.
- **Server flag:** `ServerConfig.VoiceChatEnabled`. Overridable by `--voice true` (CLI) or `BBS_VOICE=true`
  (env, for Docker). Surfaced to clients on `ServerRules.VoiceChatEnabled`. Default is **false** in raw
  config, but the **bundled host launcher passes `--voice true`** so a host-a-world-for-friends session has
  working voice out of the box; only standalone **dedicated** servers stay opt-in (admin sets it).
- **Client:** `VoiceChat.cs` (Unity) — push-to-talk capture + per-speaker streaming playback;
  `NetworkClient.SendVoice` / `NetworkClient.VoiceReceived` plumbing (in `Client.Core`, always compiled). A
  master **Settings → Voice chat** toggle (`ClientSettings.VoiceEnabled`, default on) turns the whole
  feature off client-side.

### Push-to-talk key and the worn-radio check (#2568/#2569)

- **Default key: Caps Lock** (`PushToTalkKeys.Default` in `Client.Core/Input/PushToTalkKeys.cs`). The old
  default V was `InputAction.ToggleThirdPerson`'s key, so every word flipped the camera. `ClientSettings.Load`
  moves a saved `"V"` to Caps Lock once (`PushToTalkKeys.Migrate`, done-flag `PushToTalkMigrated` — a V picked
  later on purpose is kept).
- **Picker** (Settings → Voice chat): cycles `PushToTalkKeys.Candidates` minus every key an `InputAction` is
  bound to right now (`InputMap.Key` for every action, rebinds included — `PushToTalkKeys.Next`). No candidate
  is read by a hard-wired control (WASD, Space, Ctrl/C crouch, Shift, the number keys, Esc/Tab/Enter).
- **Collision guard:** should a player rebind an action onto the talk key anyway, `VoiceChat.TalkKeyClaims`
  makes the camera-view poll sites (`PlayerController`, `SpaceView`) ignore the press while voice is live and
  the talk key is held.
- **Caps Lock caveat:** the key still toggles the OS caps-lock state; Unity's `Input.GetKey(KeyCode.CapsLock)`
  reads the physical press on Windows and Linux (macOS would report the lock state — no macOS client ships).
- **Worn radio:** `VoiceChat` captures and sends nothing without a worn radio (`RadioGear.Worn` over
  `GameBootstrap.Wears`); holding the key then shows `ui.voice.no_radio` instead of `ui.voice.talking` (the server
  would drop every frame anyway). `VoiceChat.Current` exposes the live instance.
- **Discoverability:** the radio tab (Tab → Alliances → Radio, `CraftingTechShipUI.DetailFunk`) names the worn
  radio and its reach, warns when none is worn (with a "Wear …" button for a carried one — `RadioGear.Carried`,
  `SendEquipItem(slot, -1, fromCargo)`), shows `ui.voice.hint` ("Hold {key}: talk") while voice works, or
  `ui.voice.unavailable` when the server has voice off. The context-actions list shows the same hint on foot.
- Tests: `tests/BlocksBeyondTheStars.Client.Tests/PushToTalkKeysTests.cs`, `RadioGearTests.cs`; the EditMode
  `InputAbstractionEditModeTests.PushToTalkDefault_IsBoundToNoAction_AndThePickerSkipsEveryDefaultKey` checks the
  default against the live `InputMap.DefaultKey` table.

Privacy: audio is relayed live and **never recorded or stored**, by client or server.

## Codec (Concentus) — shipped by default

Voice capture/playback uses the **Opus** codec via **Concentus** (pure managed C#, IL2CPP/AOT-safe). It is
**shipped in the standard build** — the `BBS_VOICE` define is on by default. The wiring (already in place):

1. **NuGet reference:** `Concentus` (2.2.2, BSD-3-Clause) is referenced by `BlocksBeyondTheStars.Client.Core`
   purely so it flows through `dotnet publish` → `scripts/sync-client-libs.ps1` → `client/Assets/Plugins`
   (Client.Core doesn't call it; the Unity `VoiceChat` does). `Concentus.dll` is the only new shipped DLL.
2. **asmdef reference:** `"Concentus.dll"` is in `precompiledReferences` of
   `client/Assets/BlocksBeyondTheStars/BlocksBeyondTheStars.Client.asmdef` (the player build needs this, or
   it fails `CS0246` — the standard asmdef gotcha).
3. **Scripting define:** `BBS_VOICE` is set for Standalone in `client/ProjectSettings/ProjectSettings.asset`
   (next to `BBS_UWB`). Removing it falls the client back to **text comms only** with no microphone access.

Because `client/Assets/Plugins` is gitignored, run `scripts/sync-client-libs.ps1` before a Unity build so
`Concentus.dll` is present; the Windows installer then carries it automatically (`vpk pack` over the whole
player folder — see "installer" analysis).

## Voice on a server

The flag is `voiceChatEnabled`. The **bundled singleplayer/host launcher sets `--voice true`** automatically,
so local hosting "just works". For standalone **dedicated** servers it stays off by default — enable it with
`config/server.json` `"voiceChatEnabled": true`, CLI `--voice true`, or env `BBS_VOICE=true` — and only do so
if you can moderate voice (there is no automated voice moderation; players mute individuals client-side and
can turn voice off entirely).

**Per-player mute (#1209).** `ClientSettings.MutedPlayers` — one list for **text and voice** — is written by
`/mute <name>` in the chat box and by the Unmute rows under Settings → Muted players. `VoiceChat` drops a
muted speaker's frames **before decode**, so they cost no Opus work either. It is purely local: the server is
never told, which keeps the voice fan-out a single broadcast (per-recipient filtering there would cost
Raspberry-Pi tick) and leaks no social signal. Entries match the id the server stamps on chat lines
(`ChatMessage.SenderId`) *or* the display name, so the list survives ids and names diverging. The legacy
voice-only `MutedVoicePlayers` field is folded in on load and then left empty.

**Bad packets are dropped, never thrown (#2579).** A relayed frame is untrusted input, and Concentus answers a
packet it cannot read with an exception. Unguarded, that exception ran out of `NetworkClient.Poll`, aborted the
frame's whole `GameBootstrap.Update` and was filed as a client crash. `VoiceChat.TryDecode` drops an empty
packet, an oversized one (> 1275 bytes, the Opus maximum) and any packet the decoder rejects. That speaker then
gets a fresh decoder, because a failed decode can leave the old one unusable, and a warning is logged at most
every 30 s (the crash reporter files exceptions only). A recycled speaker's decoder is disposed. Covered by
`VoiceDecodeEditModeTests` (EditMode, compiled only with `BBS_VOICE`).

## Known limitations (v1)

- **Native client only.** Unity's `Microphone` API does not work in WebGL, so a future browser client would
  be text-only (consistent with `WEBCLIENT_FEASIBILITY.md`).
- **No echo cancellation.** Opus/Concentus has no AEC; push-to-talk + a headset is the v1 mitigation.
- **Flat 2D playback.** No positional/spatial voice yet (the `AudioSource` is `spatialBlend = 0`). The
  presence stream already carries positions, so spatial voice is a possible follow-up.
- **Simple jitter buffer.** A small fixed pre-roll (`JitterFrames`) into a per-speaker streaming ring
  buffer; no adaptive resizing or packet-loss concealment beyond Opus's own.
