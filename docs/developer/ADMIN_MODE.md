# Admin mode — the in-game admin view

Epic #3502 (#3503–#3509). A world admin gets an **Admin** view inside the game, next to the normal HUD: who is playing
and where, an event log, a direct chat with a player, a jump (and follow) to a player as an invisible observer, and
every report filed in the world. This is the *how it fits together* reference; the player-facing side is in
[USER_MANUAL.md](../user/USER_MANUAL.md) §7, the access rules for operators in [SELF_HOSTING.md](SELF_HOSTING.md).

## 1. Who gets what

| Who | Variant (`AdminModes`) | What it does |
|---|---|---|
| A player | — | nothing; F8 and the Admin button are hidden |
| World admin (`Admin` / `WorldAdmin`) on a self-hosted, LAN or singleplayer world | **Invisible** (1) | panel + observer: invisible, no damage, free flight, jump, follow |
| Fleet admin (`BBS_FLEET_ADMINS`) anywhere | **Invisible** (1) | the same, on every world of the fleet |
| World owner on an **official hosted** world (`JoinTokenSecret` set) who is not a fleet admin | **Visible** (2) | the panel only — players, log, chat, reports; no invisibility, no god mode, no jump or follow |

The hosted rule is a child-safety decision (2026-11-12): on an official world the owner is any registered player, and
invisible watching plus private messages to children are exactly what a stranger must not be able to do. The owner's
direct lines are screened by the chat filter like a player's, and every direct line is logged. Admin mode needs **no
cheats** (hosted worlds never have them).

The client learns its role from `PlayerStateUpdate` (self only): `Role`, `AdminMode` and `AdminModeAvailable`
(0 = no access, 1 = invisible available, 2 = visible only). `PlayerPresence` is untouched — it must stay under the
256-byte LZ4 threshold.

## 2. Entering and leaving

- **F8** (`InputAction.AdminMode`, rebindable, no pad default) or **Admin** in the pause menu sends
  `AdminModeIntent { On = true }`; the panel opens once the server confirms the mode.
- The invisible variant reuses the observer path (`ObserverSystem.EnterSpectate` / `ExitSpectate`, now parameterised
  so the fleet `/spectate` and admin mode share it): `PlayerLeft`, out of the presence stream and the star-map list,
  god mode, noclip flight, no ship and no landing pad, not counted as a player, ignored by NPCs and the story. It is
  refused unless the admin stands on foot (not aboard, in space, in a vehicle, on EVA or dead — `srv.admin.mode_on_foot`)
  and nothing else holds them: a duel, an open trade, a seat, a moving lift (`MovingFallGraceUntil`), a visit to a
  friend's ship or a guest aboard their own (`srv.admin.mode_busy`).
- The **entry point** (body, position, facing) is kept on the session, with the body the admin's **ship** stood on
  (`EntryShipLocationId` — an observer's travel moves the ship's body along). Leaving puts the admin back there —
  another body is travelled to with the admin bypass, onto the first free two-cell spot at or above the entry cell —
  and the ship back on its body. A refused way back keeps admin mode and the entry point (`srv.admin.mode_return_failed`).
  Autosave and quit store the entry point and the entry ship body (`SavePlayerRecord`, `SaveFleet` →
  `WithAdminEntryShipBody`), never the hover position. A reconnect drops the mode.
- **Watching is no progress**: in admin mode a travel books nothing (no `LandedBodies`, no `KnownSystems`, no
  `OnPlayerTravelled` mission hook — the latter for every observer). The fleet's own `/spectate` keeps the silent booking
  of #2460 N1, because its save keeps the hover body.
- **Animals** react to players only (`_creatureReactors`): an observer wakes no sleeper, scares no skittish animal, draws
  no Soundrak scream or VEGA announcement and is nobody's prey; the population and the pruning still count them.
- Closing the panel (F8 / Esc) keeps the mode: the admin flies and watches; F8 opens it again. *Leave admin mode* ends
  it. The HUD badge reads **ADMIN** (or **ADMIN VIEW** for the visible variant; the fleet `/spectate` keeps
  **SPECTATOR**), with a line for open reports, unread direct lines and the followed player.
- **Mining** while observing stays allowed — removing an offensive build is the in-world moderation lever — but the
  block goes into a pool nobody holds (no items, no samples, no mission hooks), a crate that still holds items is
  refused, and every removal is logged. This applies to the fleet `/spectate` too.

## 3. The panel (`AdminPanelUi`, four tabs)

| Tab | Source | Actions |
|---|---|---|
| **Players** | `AdminRoster` (1 Hz while the panel is open): id, name, role, fleet flag, body, position, health, state, duel / protected / silenced, idle seconds | Jump, Follow (invisible only), Chat, Silence / Unsilence, Kick (second click) |
| **Log** | `AdminEventBacklog` on open, then `AdminEvent` | category filter, Jump on rows with a position |
| **Chat** | `DirectChatMessage` | threads per player, send |
| **Reports** | `AdminReportList` on open and on change | Done / Reopen, Jump to the player or place, Chat with the reporter |

The server only streams while the panel is open (`AdminPanelIntent`). A full open (roster, backlog, report list) goes out
at most every 2 s per session — another open inside that only resumes the stream; a close always passes. Report actions
are capped at 10 per 10 s per admin, a change that changes nothing is ignored, and a toggle of the same report within 5 s
is logged once. The **visible** variant gets bodies but no positions (`SeesPositions`): `NetAdminPlayer.HasPosition` is
false and X/Y/Z are zero, events and reports go out without a place, and the panel shows no coordinates and no Jump. The
older chat commands `/players` and `/where` follow the same rule (body and last-seen only, no `/goto` line).
Unity-free models live in
`Client.Core/Admin/` (`AdminRosterModel`, `AdminEventLogModel`, `DirectChatThreads`, `AdminReportsModel`,
`AdminFollowTracker`, `AdminPanelText`); `NetworkClientAdmin.cs` holds the sends and events.

**Jump** (`AdminJumpIntent`): same body → a free cell about four blocks behind and above the player; another body →
travel with the admin bypass, silent arrival. A place jump carries body + position (reports, log rows).
**Follow** is client-side: the observer steers to a point 6 blocks behind and 3 above the followed player's streamed
pose; any movement input stops it, and so does the player leaving the body (no presence for 2 s) — then a local log
line says so.

## 4. The event log (`AdminEventLog`)

A ring buffer of **500** events per server, each also one `server.log` line (control characters stripped). An event: id,
time, category, actor, target, body, position, a text key (`admin.ev.*`) and arguments (`{0}`, `{1}` …). Only the server
sets a locale-key argument (a single leading `@`, e.g. the death causes `admin.cause.*`); everything a player wrote is
literal — `LiteralArg` doubles a leading `@`, which `AdminEventLogModel.Fill` reads back as one, and `Fill` fills the
template's placeholders in one pass, never a `{n}` inside an argument. A player's masked chat lines are logged at most
once per 30 s (`filter_masked`).

| Category | Events |
|---|---|
| `pvp` | a defeat (attacker, target, body, position) — for the whole world, not only the body where it happened; a duel's end |
| `death` | every other death with its cause (creature, fall, oxygen, lava, …) |
| `join` | joins and leaves |
| `report` | every report route (§6), done / reopened |
| `moderation` | kick, silence, unsilence, auto-mute, chat-filter hits |
| `admin` | everything `CheatLog` records, admin mode on/off, mining while observing, jumps |

On an official hosted world the owner does not see the staff's own events (the fleet admin's log lines, mining,
leaving while invisible, a report about a fleet admin or an observer), and a report about an admin — player ids compare
case-insensitively — is never shown to that admin. `/players` and `/where` leave an observer out altogether (the live
session and the saved record) for an admin who may not see observers.

## 5. Direct chat

`DirectChatIntent { ToPlayerId, Text }` → `DirectChatMessage { FromId, FromName, ToId, ToName, Text, FromAdmin, UnixTime }`.
An admin **in admin mode** (either variant) may write to anyone; outside it they are a player here — they may answer an
admin who wrote to them, but not start a conversation (`srv.dm.admin_view_only`). A player may only **answer** an admin
who wrote to them, within 30 minutes of that admin's last line — there are no player-to-player whispers. A watching
admin never gets a copy of a conversation in which either side ever reported them. Players answer with **`/r text`** (intercepted in
`HandleChat`, one path for keyboard and pad; the chat input is pre-filled with `/r ` the first time it opens after a
direct line). A player's lines pass the throttle, mute, burst limit and screening, and are kept as report evidence; the
hosted owner's lines are screened too. To anyone but the player they wrote to, an invisible admin reads as "not
online".

## 6. Reports reach the world admin

Before this, every report went to the operators or the developers only. Now every route **also** files a row in the
world's own `player_report` table (SQLite, PostgreSQL — also in the backup table list — and the in-memory repository):

| Kind | Route |
|---|---|
| `player` | `/report Name [note]` and the Report button in Ship → Alliances. On the portal route the client also sends `PlayerReportIntent { AlsoFiledWithPortal = true }`, which files the row without forwarding it a second time |
| `paint`, `shape`, `texture` | `/reportpaint`, `/reportshape`, `/reporttexture` — with the place, so the admin can jump there |
| `feedback` | the F1/F2 `[feedback]` bump: the dialog's **title field only** (`BumpReport.FeedbackTitle`, at most 80 characters — the description is never parsed; an older client sends none and the row has no text) and the reporter's position — never the description, the e-mail or the screenshot |
| `help` | **Call an admin** — `/admin text` or the pause-menu button (`CallAdminIntent`), for help without naming a player |

**Player reports** name a player this world **knows** (`FindKnownPlayer`: online or offline, any letter case — the row
stores the record's id). The answer is the same for an online, an offline and an invisible player, so a report is no
probe for an invisible admin; a name nobody played under is refused (`srv.report.no_target`), and the portal mirror
ignores it silently. The row's place: where the player stands; an observer at the admin mode's entry point (no place
for a `/spectate` without one), never the hover position; an offline player from the save. The event about a fleet
admin or an observer is staff-only. The mirror's name and note are control-stripped and capped (24 / 200 characters).

**"Call an admin"** answers one neutral line (`srv.admin.call_sent`: *saved — the admins will see it*), whether an admin
is online or not. An empty text (`srv.admin.call_usage`) or a call from an admin (`srv.admin.call_is_admin`) files
nothing. On an official hosted world the call also goes to the operators (`NotifyOperator` + `PostReportToInbox`,
`reportType` `admin-call`). The client offers it only when `JoinAccepted.AdminCallSupported` is set and the player has no
admin view (`NetworkClient.OffersCallAdmin`); against an older server `/admin` stays the admin help.

Every route shares the report rate limit (`TakeReportSlot`, 3 per 10 minutes; quietly for feedback). Notes and feedback
titles are screened before a world admin sees them. The server keeps at most **500** rows in RAM (the oldest done row goes
first) and does not read back done rows older than **90 days**; the list sent to an admin holds at most 200, open ones
first, newest first. Admins with the panel closed get `@srv.admin.new_report` (*F8 opens the admin view*). The
confirmation texts say the report went to the admins of this world (and, on official worlds, the team). The reported
player is still never told.

## 7. Observer leaks closed

Slide doors no longer open for an observer, Crystal Net presence sensors ignore one, the `/bump` snapshot's
`nearby.players` leaves observers out, and the admin-only player-mode roster drops them (and is re-sent when someone
starts or stops observing). The admin intents are on `SpectatorMayHandle` and, except the jump, on `PausedMayHandle`.

## 8. Wire

Tags **336–347** (`NetCodec.Later.cs`, `RegisterAdminViewMessages`): `AdminModeIntent` 336, `AdminPanelIntent` 337,
`AdminRoster` 338, `AdminJumpIntent` 339, `AdminEventBacklog` 340, `AdminEvent` 341, `DirectChatIntent` 342,
`DirectChatMessage` 343, `AdminReportList` 344, `AdminReportAction` 345, `CallAdminIntent` 346, `PlayerReportIntent` 347.
Additive — an older peer drops them, no protocol bump. Additive fields on existing messages: `BumpReport.FeedbackTitle`,
`JoinAccepted.AdminCallSupported`, `NetAdminPlayer.HasPosition`.

## 9. Key files

| Area | Files |
|---|---|
| Server | `GameServerAdminMode.cs`, `GameServerAdminRoster.cs`, `GameServerAdminEvents.cs`, `AdminEventLog.cs`, `GameServerDirectChat.cs`, `GameServerAdminReports.cs` (the list), `GameServerAdminReportRoutes.cs` (the routes), `PlayerSessionAdmin.cs`, `ObserverSystem.cs` |
| Wire | `Networking/Messages/AdminMessages.cs`, `PlayerMessages.cs` (`PlayerStateUpdate`) |
| Persistence | `IWorldRepository` + the three repositories (`player_report`) |
| Client.Core | `NetworkClientAdmin.cs`, `Admin/*` |
| Client | `AdminPanelUi*.cs`, `AppShell.Admin.cs`, `HudUi.Admin.cs`, `ChatUi.Direct.cs`, `PlayerController.AdminFollow.cs`, `InputMap.cs` |
| Tests | `AdminModeTests`, `AdminModeFixTests`, `ObserverLeakTests`, `AdminRosterTests`, `AdminEventLogTests`, `DirectChatTests`, `WorldReportTests`, `AdminReportFixTests`, `PlayerReportPersistenceTests`; Client: `AdminModelsTests`, `AdminViewClientTests`, `AdminPanelTextTests`, `EditorTextFitTests.Admin` |

## 10. Known limits

- Only the autosave and the quit store the entry point; an incidental player save elsewhere (a cutscene, …) stores
  an invisible admin's hover position. (Every fleet save stores the entry ship body.)
- The fleet's `/spectate` keeps its silent booking of watched bodies (#2460 N1) and its save keeps the hover body; only
  the admin mode books nothing.
- Following ends when the followed player switches on their stealth field, and when the world rig is rebuilt by a
  change of body.
- Join and leave are logged for local singleplayer players too.
