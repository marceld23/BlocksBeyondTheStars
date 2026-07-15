# Changelog

All notable changes to **Blocks Beyond the Stars** are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html)
(pre-1.0: minor bumps carry features, patch bumps carry fixes and small additions).

Each release below mirrors its [GitHub release notes](https://github.com/marceld23/BlocksBeyondTheStars/releases);
the richer, screenshot-laden versions live there. `(#123)` references the pull request or issue.

## [Unreleased]

_Nothing yet — [v0.8.1](#081--2026-07-15) is the latest release._

## [0.8.1] — 2026-07-15

A small launch-day patch for the glitch.fun edition, straight from the first live test. (#332)

### 🕹️ glitch.fun: arcade joins actually work now
- The arcade session gateway validated a visitor's install **before** registering it with Glitch — but Glitch's contract requires create-then-validate, so every fresh browser session was rejected with "could not be verified" and nobody could enter the arcade worlds. The gateway now follows the required call order (create/resume first, then validate), with a regression test pinning the sequence. (#334)
- **CORS knew the wrong address:** Glitch serves the actual game files from its S3 content bucket, not from `play.glitch.fun` — so the browser refused to even send the arcade requests. The bucket origin is now part of the default allowlist. (#336)
- **The arcade never sleeps:** the pool worlds now run permanently — woken at startup, never idle-exiting, re-woken automatically if one crashes — so a store visitor lands in a running world instead of waiting out a cold world generation. (`BBS_WH_GLITCH_KEEP_AWAKE=false` opts tight hosts out.) (#336)
- Fleet side (no download needed): the arcade gateway env passthrough was fixed in deployment, so the gateway is actually switched on. (#330)

### 🕹️ glitch.fun: the menu never offers server picking anymore
- On glitch.fun the arcade join runs automatically when the page loads, so the menu only ever appears when that join failed. That menu still offered the generic browser actions: **Play** dialed a meaningless default host and dropped the player into an empty, serverless void, and **"Connect to a server…"** suggested picking a server — which on Glitch is never the player's job. In the Glitch context, Play now simply retries the arcade join and the manual picker is gone. (#331)

## [0.8.0] — 2026-07-15

The glitch.fun edition: Blocks Beyond the Stars becomes instantly playable on [glitch.fun](https://glitch.fun) — shared arcade worlds you join with one click, plus full singleplayer that runs entirely in your browser and follows you across devices via cloud saves. (#326)

### 🕹️ Arcade worlds on glitch.fun (#322)
- A small pool of persistent **multiplayer arcade worlds that exist only on glitch.fun**: one click on the store and you are in — no account, no world creation, no password. The platform's `install_id` is validated server-side, you get a stable guest identity, and a sleeping world wakes on demand.
- These worlds never appear on our own portal (not in the public browser, not in any world list) — **the Baumhaus rule for our own platform is unchanged**: your worlds stay password + word-of-mouth only. The arcade is a separate playground under Glitch's platform accounts and rules; a devblog explains the amendment.
- Moderation without accounts: the operator can ban a Glitch install — banned installs get no new sessions and are live-kicked from a running world.

### 🌍 Singleplayer in the browser (#323)
- The **real authoritative game server now runs inside the WebGL page** — the same simulation the fleet and desktop run, pumped over an in-memory loopback instead of sockets. The browser menu has its Singleplayer button back, and `?singleplayer=1` deep-links straight into your world.
- One persistent world per browser: saved automatically every two minutes, when the tab goes to the background, and on exit — into the browser's own storage, as a compact snapshot of the whole world.
- Under the hood this retargets the server simulation to a Unity-consumable library (desktop singleplayer keeps its bundled server), adds a fully managed save backend (no native SQLite in WASM), and switches the in-browser wire to the JSON envelope — MessagePack's runtime formatters don't run under IL2CPP.

### ☁️ Cloud saves for logged-in Glitch players (#324)
- Playing on glitch.fun with a Glitch account? Your singleplayer world syncs to **Glitch Cloud Saves** and follows you to any device. Conflicts use Glitch's explicit resolve flow (the live session wins; nothing is silently overwritten), and guests simply stay local.
- The sync runs through our WorldHost as a relay, so the Glitch API credential never ships inside the public browser build; uploads are size-capped and rate-limited.

### 🚀 Releases now ship to glitch.fun automatically (#325)
- Every tagged release mirrors its WebGL build to glitch.fun through the platform's deployments API — same gating as the itch.io mirror (full test suite first, skipped cleanly on forks). One build serves both our portal's `/play` and the Glitch store.
- Small fix on the way: the browser menu's *My Worlds* button now always points at our worlds portal (on glitch.fun it used to be a dead link) — the door from the arcade to creating your own world with friends.

### ✅ Verification
- The suite grows to **1127 tests**, including 31 for the glitch gateway/registry (guest tokens, capacity, bans, heartbeat + save relay, CORS) and 7 for the browser persistence path (full mine→save→reload round-trip on the real server + client). An automated real-browser smoke test drives the in-browser world end-to-end: boot, join, durable save. (#326)

### ⚠️ Known limitations
- In-game player reports need a portal session and are unavailable for arcade guests and browser singleplayer (operator recourse: install bans, world stop/reset).
- The in-browser world runs with AI texts off (static lines) — the LLM backend is not reachable from a browser.

## [0.7.7] — 2026-07-14

A pure stability release: a full code audit of the game server and the Unity client turned up eight bugs — all fixed here, the risky ones with new regression tests. Highlights: your saves are now safe across future content updates, multi-world hosting got fairer and lighter, and long play sessions stop growing in memory. (#319)

### 💾 Saves are now safe across content updates
- Saved worlds stored every block as a numeric id derived from the sorted content list — adding a new block in a later update could shift those ids and silently decode your existing builds into the *wrong* blocks. Worlds now persist an id→block palette and remap all stored ids (block & structure edits, regrown flora, space structures) when the content set changes — atomically, on SQLite and PostgreSQL alike. (#318)

### 🛰️ Multi-world hosting: fairer, lighter, harder to freeze
- Presence updates, enemy sync and void-rescue checks each ran on a **single shared timer** for the whole server — with several occupied worlds, one world could starve all the others of those updates. Each occupied world now keeps its own cadence. (#315)
- A world now really unloads (and frees its memory) when its last player disconnects — before, the unload could silently do nothing and the world stayed resident. (#313)
- `/ai_mission` called the AI backend synchronously on the tick thread, so a slow LLM response froze the entire server for everyone. Mission generation now runs off-thread and the result is applied on a later tick. (#314)

### 🪐 Planets: flora variety restored
- The set of plants for a planet was resolved once — for the first planet visited after server start — and then reused everywhere, so every world grew that first planet's flora. Each planet now resolves its own subset, deterministic from the seed again. (#317)

### 🧠 Client: mesh memory leaks plugged
- Unity never frees procedurally-built meshes on its own — chunk re-meshing, chunk unload, world reset and ship/asteroid re-meshing each leaked their outgoing render/collision meshes. All of these now destroy the replaced meshes, so long sessions no longer creep up in memory. (#316)

### 🤝 Trading: the right cargo on both sides (security fix)
- Player-to-player trades (and the admin `give_item` command) now validate and swap each partner's items against that player's **own** ship cargo instead of the last ship the server happened to look at — an offer made while aboard someone else's ship no longer resolves against the wrong hold. (#319)
- **Security note** *(disclosed only after the official fleet was patched)*: the wrong-cargo lookup was player-reproducible and could be abused to **duplicate items** in multiplayer trades. The official hosted worlds already run v0.7.7 — if you host your own server, please update / pull the rebuilt Docker image promptly.

### ✅ Verification
- The full suite is now **1116 tests** (998 server + 118 client), including 5 new regression tests: block-palette remap, per-partner trade cargo, per-planet flora determinism, and world unload on disconnect. (#319)

## [0.7.6] — 2026-07-12

A small under-the-hood release: the entire server-side stack moves to .NET 10 LTS (with a security fix included), and a subtle singleplayer start-world bug is gone for good. (#307, #309)

### ⚙️ .NET 10 LTS (server stack)
- The game server, web portal/API, world host, launcher and tooling now run on **.NET 10 LTS** (supported until November 2028) instead of .NET 8, whose support ends this November. (#309)
- **Security:** the upgrade surfaced a known high-severity vulnerability in the SQLite native library we were shipping ([GHSA-2m69-gcr7-jv3q](https://github.com/advisories/GHSA-2m69-gcr7-jv3q)) — patched in this release.
- **No action needed for players** — the bundled singleplayer server and the launchers are self-contained and bring their own runtime. Self-hosters just pull the rebuilt Docker image; only local development now needs the .NET 10 SDK.

### 🪐 Singleplayer: start world can no longer get stuck on an old default
- The bundled singleplayer server used to read a `config/server.json` it once wrote next to its exe — a leftover file from an older version could silently pin outdated defaults, most visibly the start planet staying the old **rocky** world instead of the varied start worlds introduced in v0.7.x. The singleplayer host now always starts from current defaults and ignores any stale config. (#307)
- Official installers were never affected — this hardens portable-zip-overwrite setups and local builds. Dedicated servers are unchanged and keep their editable `config/server.json`.

### 📖 Docs
- The README now spells out the hosted-worlds friend flow up front: create your own world → set a join password → list it publicly so friends can find and join it. (#305)

## [0.7.5] — 2026-07-10

A small onboarding-and-trust release: newcomers now get the hosted-worlds model explained where they actually look, and Windows builds are moving to free, verified code signing. (#301, #302)

### 🌐 "Official Worlds," explained for first-timers
The game has **no open public servers** — everyone creates their own world, sets a join password, and lists it publicly so friends can find it. That rule was enforced everywhere but never spelled out for someone opening the menu for the first time. Now it is. (#301)
- **The signed-out Official Worlds screen** opens with a one-line explainer of how online play works: create your own world → set a join password → list it publicly → friends join with the password.
- **The public worlds list** now shows the note that was written for it but never displayed — these are worlds shared by other players, and you need the creator's password to join.
- **Creating a world** shows a friends hint right in the dialog: set a join password now, then list the world publicly under *Manage* so friends can find and join it.
- **The web portal landing page** gains a *"So funktioniert's / How it works"* card with the three steps and the no-open-servers rule, instead of just a one-line tagline before you log in.
- **The README and user manual** now state the model and the friends-join path up front.

### 🔒 Windows code signing (SignPath)
- Windows installers are moving to **free, verified code signing** through the [SignPath Foundation](https://signpath.org)'s open-source program, so Microsoft Defender SmartScreen stops flagging them as coming from an "unknown publisher." A new **CODE_SIGNING.md** documents exactly what is signed, from where, and by whom; the README security notice and SECURITY.md link to it. (#302)
- **During onboarding:** while the certificate is still being provisioned, some builds may remain unsigned and SmartScreen may still warn — the notice explains the one-time *More info → Run anyway* step until signing is fully live.

### 📓 Devblog
- We keep a **development blog** ([blocksbeyondthestars.com/blog](https://www.blocksbeyondthestars.com/en/blog)) with the story behind the game — it now has its own link in the README header row.

## [0.7.4] — 2026-07-08

A round of new-player and comfort fixes from a hands-on playtest by **Severin**, plus Official Worlds & portal polish. (#289, #290, #298, #299)

### 🎮 Comfort & controls
- **Pause menu on Esc** — pressing **Esc** in-game now opens a small pause menu (**Resume / Settings / Quit to main menu**) instead of jumping straight to a "leave the game?" prompt. You can finally reach **Settings — and the volume — without leaving your world**, and volume changes are audible immediately. (#291)
- **Settings apply live in a running world** — language now switches the live HUD/chat instantly (the world previously kept its own snapshot until re-entry), quality preset / VSync / FPS cap / lens flare / motion blur / volumetric fog / mouse sensitivity / invert-Y push straight to the running world, and the view-distance row honestly says it takes effect at the next world start (the server streams the radius it was started with). (#291)
- **Settings screen readability** — the window (main menu and in-game, it's the same screen) is wider (600 → 900), every label/control lines up on one shared column pair, and the section headers are now real headers: bold, uppercase, with a divider line and breathing room, so the long list finally scans. (#291)
- **Mouse no longer escapes after Alt-Tab** — tabbing out and back used to leave the cursor free to drift or click outside the window; the game now re-locks it on focus return during normal play. (#292)

### 🌊 Survival tweaks
- **Shallow water breaks your fall** — landing in even a single block of water now cushions the drop (like Minecraft), instead of only saving you when you were chest-deep. (#293)
- **Oxygen lasts longer** — suit-oxygen drain has been softened again across all rates (at Normal a full tank now lasts ~285 s on foot). (#294)

### 🧭 New-player guidance
- **The oxygen mechanic is finally visible** — on a breathable world the vitals bar now reads **"Oxygen (breathable)"** so you understand why it never drains here, and a one-time advisor line explains that space, water and toxic worlds will empty your suit tank. (#294)
- **Finding iron** — the starter mission and the mining tutorial now tell you iron sits just a few blocks underground, so "where's the iron?" has an answer. Ores in general — and iron in particular — are also a bit more common now. (#295)
- **The first minutes stay yours** — the guaranteed starter data cube no longer glows right beside your ship; it now sits a short walk off the landing pad (~20 blocks), so new players aren't pulled into a minigame before they've even looked around. (#296)

### 🙌 Credits
- **Severin** is credited as a playtester (README + in-game credits) for the feedback behind all of the above. (#297)

### 🖱️ Official Worlds & portal UX polish
Follow-up polish to the hosted-worlds flow (portal + in-game *Official Worlds* menu). (#289, #290)
- **Loading spinners** everywhere an action runs in the background — waking/playing, creating, stopping, deleting and uploading a save. The web portal now also gives stopping and deleting a progress state (button disabled + "Stopping…/Deleting…" → "stopped/deleted"); before, those looked like nothing happened until the list refreshed.
- **Play → clearer name handling in the game client**: if the chosen player name is reserved or not allowed, an in-place prompt lets you pick another name and retries the join, instead of a dead-end error you could only fix from the main menu.
- **"Play" (portal)** reads a prefilled, remembered player-name field instead of a pop-up prompt; the browser-play button is the clear primary action with the native host/port/token in an expander.
- **Official Worlds window** widened and laid out as a clean grid: the five action buttons share one uniform row, the per-world Play/Manage buttons line up with the columns above them, and the world **status** ("starting…", "running") gets its own wide column so it never hides behind the buttons.
- **One window for both world lists (game client)**: public worlds no longer open a second window. The *Official Worlds* screen now shows your own worlds and the public ones in a single scrollable view, split into clearly labelled **"My worlds"** and **"Public worlds"** sections (own worlds are filtered out of the public list).

## [0.7.3] — 2026-07-07

Find and join worlds other players are running, plus a round of interface fixes. (#287)

### 🌍 Public world browser
Hosted worlds used to be invite-only — there was no way to discover a world someone else made; you needed a link or token from its owner. Now there's an opt-in public list.
- Owners can list a world publicly with a per-world toggle — in the portal **and** in the game's **Official Worlds** menu — but **only once the world has a join password**, so a listed world is never wide open. Worlds stay private by default, and removing the password automatically un-lists the world.
- A new **"Public worlds"** section (portal card + in-game browse dialog) shows everyone's listed worlds; joining still asks for the owner-shared password, so discovery never means free entry.

### 🖥️ Interface fixes
- **Credits screen** — the contributor text now scrolls inside its panel (with a scrollbar) instead of spilling out the bottom and being covered by the Back button.
- **"Create world" (portal)** — the button now shows a "Creating…" → "Created 🚀" state and refreshes the list itself, so it no longer looks like nothing happened until you reload the page.
- **"Play" (portal)** — reads a prefilled, remembered player-name field instead of a pop-up prompt; clearer "waking…/ready" messages, with the browser-play button as the obvious primary action and the native host/port/token tucked into an expander.
- **Official Worlds** — the "View rules" button no longer crowds the panel's edge.

## [0.7.2] — 2026-07-06

Play the whole hosted-worlds flow without ever opening a browser, grow your own food, pen in your creatures, get treasure hints from villagers, and start new games somewhere friendlier — plus a round of UI polish.

### 🖥️ Play without a browser — full client-portal parity
Everything you could previously only do on the web portal now works inside the game's **Official Worlds** menu; for desktop players the website is optional. (#271, closes #268–#270, #272)
- **Sign up in-game** with the community rules shown and accepted right there. A new anonymous `GET /api/terms` single-sources the rules text with the `/rules` page (via a new `CommunityRules` class) so they can never drift, and an outdated-terms login opens a **re-accept flow**.
- **Create a world** (name + optional join password with a repeat check) and a **per-world manage dialog**: set/remove the join password, stop the world, or delete it behind a type-the-name confirmation.
- **Save backup round-trip without a browser** — download to / upload from `portal_saves/<worldId>-world.db` with an "Open folder" button (world must be stopped; the 50 MB cap surfaces server-side).
- **Feedback & ideas** form and **GDPR account deletion** (type-the-name confirm; erases the account, its worlds and saves) are both reachable in-game.
- **WebGL round-trip** — the browser client gains a visible "My Worlds / Account" button linking back to the worlds portal on the same origin (#272). It still never selects servers itself, so no portal features are duplicated in WebGL.
- **UI polish** — modal dialogs are now fully opaque everywhere (forms no longer shine through), the settings list scrolls, overlay notices wrap instead of clipping, the Play/Manage row buttons are wide enough for their labels, signup shows a proper "Password (at least 8 characters)" label, and the rules screen shows a friendly offline message + retry instead of silently dropping "accept" when the terms can't be reached.

### 🌱 Algae tank — grow food from water at a base
- New **algae tank** station block, the game's first food-producing machine (detoxifier station-recipe pattern, base-only). (#265, closes #261)
- Crafted at the workshop (2 metal panel + 3 glass, no blueprint); turns **1 water → 2 algae rations** (+30 hunger). A new hand recipe (2 ice → 1 water) feeds it.
- Dedicated bubbling craft cue, new block tile + ration icon, and full bilingual Codex coverage (survival-guide article + auto chapters).

### 🐾 Energy fence & gate — pen in creatures, companions and enemies
Wild creatures, companions and planet enemies never consult the voxel world for collision, so ordinary walls can't hold them. Two new blocks change that. (#266, closes #263)
- **`energy_fence`** — a solid emissive pylon that blocks players, NPCs *and* all fauna (workshop: 2 metal panel + 2 cable → 4).
- **`energy_gate`** — a non-solid membrane players and NPCs walk through but fauna bounce off; a door with no open/close state (workshop: 2 metal panel + 1 energy cell + 1 circuit board → 1).
- A new fauna fence sweep wires this into creature, companion and enemy movement — so a pen holds your own animals and the fence doubles as base defense. Flying creatures glide over normal-height fences (documented).
- New atlas tiles for both blocks plus an `energy_fence_hum` loop; DE+EN locales, taming wiki article and manual sections.

### 🗺️ NPC treasure hints
Settlement NPCs can now point you toward the crashed wreck and hidden caches. (#267, closes #262)
- On greeting there's a **35% chance to emit a hint** instead of the flavour line: the wreck (while unclaimed) for everyone; the nearest unlooted chest only once your relationship reaches "known".
- The reveal is **world-global and co-op friendly** — the POI is added to the map and broadcast to all joined players, and persisted in world metadata (no codec change).
- The spoken line is deterministic and localized DE+EN: an 8-way compass direction + distance, torus-wrap aware. Claimed wrecks and looted chests drop out automatically and are never re-hinted.

### 🪨 Friendlier start planet
- New games now start on a **hospitable planet** (varied) instead of toxic, plantless rocky; carbon ore was added to varied so the medpack/energy-cell chain still works. Only new worlds are affected — existing saves keep their persisted start type. (#264, closes #260)
- World generation is hardened: an unknown or unmatched start type no longer crashes world load (it falls back to the first breathable planet with flora).
- Fixed a latent bug this surfaced: breathable-surface health regen ran before the same-tick death check, so a 0-HP player was nudged to 0.2 HP and **never respawned**. No regen at 0 HP now.

### 🌐 Portal polish & security
- **Discoverable DE/EN language switcher** — a header "DE | EN" pill (the old switcher was invisible grey footer links), plus `Accept-Language` detection on first visit. Auto-detection sets no cookie; only an explicit choice persists. (#259)
- Resolved both open CodeQL alerts: the `bbs_lang` cookie now sets `Secure` + `HttpOnly`, and the `/admin/announce` handler logs the world id through the safe helper. (#259)

### 📚 Docs
- README now links to the game's website. (#264)

## [0.7.1] — 2026-07-06

This release is all about **playing together safely**: world creators can password-protect their worlds, server restarts announce themselves with an in-game countdown, reporting and feedback are easier to find (especially for kids), the portal got a kid-friendly rework, and the Play button finally launches the game right in your browser.

### 🔒 World join passwords
World creators can lock a hosted world with a password — perfect for "just my friends" worlds. (#255)
- Set, change or remove the password any time in the portal; new worlds can get one at creation (with a confirm field so no typos lock you out).
- Joining asks for the password — in the portal and in the game client (masked input). The world owner never needs one.
- Guess-proof: 10 wrong tries in 15 minutes puts a cooldown on that world, and sleeping worlds aren't even woken for wrong guesses.

### 🔧 Maintenance announcements
Server restarts no longer come out of nowhere. (#255)
- In-game countdown banner when a maintenance restart is scheduled, plus an acknowledgeable dialog and a proper "server is restarting" screen instead of a generic disconnect.
- New commands `//restart <minutes>` and `//cancelrestart` for world admins, a token-gated `/announce` endpoint, and an **Announce card in `/admin`** with a per-world graceful-restart button.
- Worlds finish the countdown, save, and stop cleanly — no more surprise kicks.

### 🧒 Kid-friendly portal & feedback
The web portal now reads like the family project it is. (#258)
- New **"Feedback & ideas" card** on the worlds page — kids and parents can send wishes directly, without filing a "report" against anyone; feedback lands in its own `/admin` queue, separate from player reports.
- Parental notice on the landing page, and "ask your parents first" is now the first community rule.
- Kid UX pass: create-account card comes first, the name field speaks plain words, a "write your password down!" warning replaces jargon, and the join prompt remembers your player name.
- Reporting is discoverable: rules and portal now point at the in-game Report button and `/report` command first.

### 🛡️ Easier reporting in-game
- New **`/report <name>` chat command** on official worlds — it attaches the last 10 chat lines as evidence. (#248)
- Every report now records **which world it happened in** — from the button, the command and the portal form. (#248)
- `/help` now leads with the commands for everyone (`/report`, `/bump`), and official worlds show a one-time chat tip pointing at both report paths (DE+EN). (#258)

### 🌐 Play in the browser, for real
- The portal **Play button now launches the game directly in your browser** — signed in, world picked, one click, playing — by deep-linking the WebGL client with a short-lived join token. (#256)
- Fixed the Play button doing nothing for returning players (a permission wipe ate the grant). (#256)
- The whole portal is now fully bilingual (DE/EN, `?lang=en` or the toggle) and finally shows the game logo and a favicon. (#256)

### 🛠️ Fixes & polish
- **Stopping a server during initial world generation now works** — previously the stop was ignored and the container was force-killed (exit 137); the generation grace period is also longer now. (#247)
- Admin server-health card shows the real host core count, so the load bar means what it says.
- Hardened the admin stop endpoint against log forging (CodeQL): world ids are sanitized before logging.

## [0.7.0] — 2026-07-05

A big visual glow-up with the **organic look overhaul**, plus **gamepad and touch controls**, hosted **Official Worlds** with a web portal, a fleshed-out in-game **Codex**, and a **browser client** that's actually playable end-to-end.

### 🎨 Organic look overhaul
A full presentation pass — all client-side, no save/world migration needed. (#192)
- Softer voxel surfaces (per-vertex + cavity AO, beveled convex edges). (#186)
- GPU-instanced grass tufts and pebbles on open ground. (#187)
- 3D mesh flora — leaning 3-plane plant rosettes; cacti, crystals and mushrooms are real shapes. (#188)
- Finer building shapes: panel, post, beam, low ramp, quarter cube. (#189)
- Fully orientable shapes (all 24 orientations, auto-orient, rebindable rotate key). (#190)
- Sparse raised plating on exposed ship hull faces. (#191)

### 🎮 Gamepad & touch controls (experimental)
The whole input epic (#193) shipped — inert on desktop keyboard+mouse.
- Gamepad/controller support behind a new input-abstraction layer (flight, EVA, menus, building). (#199)
- Pad rebinding in Settings with device-aware button glyphs. (#206)
- On-screen touch controls with localized labels (DE+EN). (#204, #206)
- Text entry on touch in the browser; menus navigable by pad focus-nav. (#206)
- ⚠️ On-device feel still being playtested (#201–#203).

### 🌍 Hosted Official Worlds (beta)
Player-created worlds hosted by us, joinable in one click — no port forwarding, no server setup. (#227)
- "Official Worlds" in the main menu; sleeping worlds wake on demand and idle worlds shut down cleanly.
- Web portal with privacy-minimal sign-up (name + password, no email), world create/manage, and save upload/download.
- Kid-friendly bilingual community rules + a beta notice; **DSGVO account self-deletion**. (#233)
- One-tap in-game **Report** button + portal report form; operators can review and ban, enforced at join.
- Optional AI backend runs containerized in the fleet (internal-only, OpenAI-compatible) and feeds NPC chatter. (#234)
- Basic-Auth `/admin` panel: live fleet overview, report queue, ban/unban, server-health card; public aggregate-only `/api/stats`. (#234, #246)
- Per-world memory/CPU/pids fences + a global cap on awake worlds. (#234)
- Fixed a stale container blocking every re-wake, found in the first real end-to-end test. (#235)
- Developer names protected against impersonation + a blocked-name filter. (#230)
- Worlds untouched for months are archived, not deleted, and restored transparently on join. (#230)
- Rate limits on signups, logins, save uploads and reports + Prometheus metrics. (#230)
- Self-hosting unchanged — everything inert by default. Fleet deploys from the repo via an approval-gated SSH workflow. (#229, #231)

### 🐛 Self-owned bug-report inbox
- New **ReportHost** service: a small Docker container that receives F1 feedback and crash reports (unchanged wire contract), stores them in SQLite with screenshots, and offers a filterable triage UI. (#227)
- One-click JSON bulk export honoring the current filters. (#234)

### 📖 Codex
- Every block, item and all 19 worlds now have bilingual (EN/DE) descriptions, plus 14 new guide articles. (#184)
- Codex is fully navigable again: working chapter links, list descriptions, correct scan names. (#178)

### 🌐 Browser client
The experimental WebGL path is now genuinely playable.
- Fixed falling through terrain on landing — chunks and colliders now build synchronously on WebGL. (#205, #207)
- One-click `/play` flow polished (required name, deep-linking, dark loading template, reliable touch). (#217–#222)
- WebGL builds no longer drag in the desktop updater (Velopack); dedicated WebGL attach workflow. (#151)

### 🛠️ Fixes & polish
- You can walk out of your ship again (nozzles moved, doorway cleared, hatch raised to 3 blocks). (#179, #211, #212, #214, #215)
- Deep water reads see-through — vertical depth, softer reflections and glint. (#213, #216)
- Helmet lamp switches off when you leave a planet. (#180, #182)
- Opaque world-options dialog with non-overlapping footer buttons. (#181, #182, #209, #210)
- All 27 Unity compiler/build warnings resolved — the client builds warning-clean.
- Game-input hardening on every server: packet-size caps, per-connection rate limiting, validated face data, voice frame-rate cap, control characters stripped from chat/labels. (#233)
- CI: two-tier test gate — fast PR checks, full suite on main + before release. (#208)

## [0.6.2] — 2026-06-29

Sharpens core gameplay, adds oxygen-tank tiers, seals a station/space exploit, and lays the groundwork for playing in the browser.

### 🎮 Gameplay
- Shaped blocks now have proper icons in hotbar, crafting and inventory. (#125)
- Ladders are climbable (forward/Jump up, back/Ctrl down). (#126)
- Auto-step onto slabs & stairs without jumping. (#127)
- Tool-gated mining — stone needs a drill (the starter kit grants one); soft blocks stay bare-handed. (#128)

### 🫧 Survival
- Oxygen Tank tiers I/II/III — worn components giving +50/+100/+200 max oxygen; only your best tank counts, Tank III gated behind titanium. (#133)
- Easier to climb out of water. (#133)

### 🌐 Browser client (experimental)
- Hosted WebGL client + optional PostgreSQL backend, servable from the dedicated-server Docker image. Contributed by **[@ProdigyView](https://github.com/ProdigyView) (Devin Dixon)**. (#116, #123)
- Slimmed browser start menu to name + Play. (#122)
- ⚠️ Not yet verified end-to-end — ships as experimental.

### 🛠️ Fixes & polish
- Sealed a station-to-space gap via a bounded flood-fill enclosure check. (#134, #135)
- Dev builds isolated under a separate pack ID. (#115)
- German localization fixes — thanks to **[@Maqbool61](https://github.com/Maqbool61) (Maqbool Ahmed)**. (#112)

## [0.6.1] — 2026-06-28

A focused follow-up to v0.6.0: an experimental macOS build, automatic crash reports, and one-click client downloads from your own server.

- 🍎 **Experimental macOS build** (`StandaloneOSX`, portable zip) — unsigned/un-notarized, not yet tested on real hardware; testers wanted ([#87](https://github.com/marceld23/BlocksBeyondTheStars/issues/87)). (#84, #105)
- 🐧 Native Linux client (new in v0.6.0) gains a one-click download from the server portal. (#83)
- 🛡️ **Automatic crash reporting** (server + client, opt-in, with a PII scrubber); the server tick is hardened so one bad event can't crash the world. (#103)
- ⬇️ The dedicated-server portal now hands out Windows, Linux (AppImage) and macOS clients directly. (#107)
- 👨‍👩‍👦 README, Code of Conduct, in-game Credits and Codex now state the kid-friendly intent. (#109)
- ⚙️ Faster, cleaner release CI (version resolved once and shared; Docker build decoupled; overlapping runs serialized). (#106)

## [0.6.0] — 2026-06-27

Our biggest update yet: claim your own factory, explore alien ruins — and play natively on **Linux**.

- 🎉 **First community contribution** — native Linux support by **[@corarona](https://github.com/corarona) (Cora de la Mouche)**. (#69)
- 🏭 **Factories** — rare industrial halls with animated machine bays and a production terminal that turns cheap materials into bulk output.
- 🏚️ **Ruins** — collapsed settlements you can mine, plus standalone treasure chests with richer loot.
- 🔑 **SPS access codes** — a rare item that lets you claim a factory as your own editable base, shared with allies.
- 🐧 **Native Linux client** — real `StandaloneLinux64` build as AppImage + Portable.zip; the Windows-only embedded browser (CEF) was removed so the Codex Wiki and Arcade run without it.
- 🛠️ Deeper crafting: new **Transmuter** station, the **Refinery** becomes full metallurgy, and the titanium/carbide deadlock is fixed.
- 🌍 Bigger in-game editors, longer view distance, smoother movement, downhill rivers/lava with waterfalls, brighter worlds, per-world named tree species.
- 📚 New factory/ruins docs (manual + Codex, EN/DE) and a canonical source-code URL (AGPL).

## [0.5.0] — 2026-06-25

A graphics-quality pass and a licensing/foundation cleanup.

- 🎨 Pro-look graphics quick-wins: in-game post + SMAA + SSAO + emissive glow. (#50)
- 🎨 Per-biome cinematic mood LUTs (#51), PBR-ish GGX specular + roughness-aware reflections on voxels (#52), and a global Brightness control. (#53)
- 💧 Soft water reflections + visible sky bodies (#55); clear water you can see into again. (#56)
- 📜 Relicensed **MIT → AGPL-3.0-or-later** + a Contributor License Agreement. (#59)
- 🧩 Native in-game Arcade + Wiki — removed UnityWebBrowser/CEF, unblocking Linux/macOS. (#58)
- ⚡ Runtime-quality pass: client perf, netcode area-of-interest & interpolation, input remapping. (#60)
- 🚀 Ship cargo hold is now actively usable — manual transfer, capacity, auto-stow. (#62)

## [0.4.2] — 2026-06-23

- 🍽️ **You no longer starve in the first few minutes** — starter food (5 berries + 2 rations), an "eat" onboarding objective, an earlier hunger warning, and clearer food descriptions. (#46)
- 🔫 **A fighting chance against early hostiles** — a weak ranged starter **scrap pistol**, fixed ranged weapons only firing at point-blank, and a 3D line-of-sight gate so enemies can't hit what they can't see. (#45)
- 🐧 **Linux/Proton: no more hard 30 fps lock** — VSync is now its own setting with a separate frame-rate cap. (#47)
- 📝 README intro reworked around the family-and-community project story. (#44)

## [0.4.1] — 2026-06-22

- 🚀 No more creatures trapped in your ship — the ship evicts wild creatures on landing (companions may still follow you aboard); same guard for planet enemies. (#43)
- 🛡️ You're truly safe once aboard — visible attacks (drone fire, robot claws, creature bites) now stop the moment you board. (#41)
- 🧭 Cleaner HUD in space — the planet compass and day/night–temperature–gravity panel hide while piloting (compass stays on during EVA). (#39)
- ⌨️ Feedback is now simply **F1** — the never-clickable bottom-right button is gone. (#42)
- 🎥 Automated in-game clip recorder (video + frame-synced audio) for marketing clips. (#40)
- 🐳 Docker Desktop local-test walkthrough in SELF_HOSTING. (#38)

## [0.4.0] — 2026-06-21

- 👥 **Raised the default player cap from 4 to 12** + a 12-player smoke test. (#19, #21)
- 📻 **Tiered radio reach + live voice chat.** (#30)
- 🐳 **Optional Docker image for the dedicated server.** (#26)
- 🧰 UI fix: split the in-game menu top bar into two rows so Close no longer overlaps tabs. (#35)
- ⚙️ CI hardening: PR build/test gate (warnings-as-errors, docs-only skip), format/lint/CodeQL checks, Actions pinned to SHAs, CodeQL compiles C# in manual build mode, release notes prepend the Docker pull command. (#16–#37)

## [0.3.2] — 2026-06-21

- 🚀 Explosion stays with no landing animation after ship destruction (#10); rear hatch shows a blue energy door in flight, not an open hole. (#11)
- 🛬 Landing flies the descent to the chosen planet, not the launch body. (#12)
- 👾 Floating scan-drones fire a ranged laser. (#13)
- 📸 Per-planet surface screenshots for variety (#8) with terrain-aware capture placement. (#14)
- 📝 README gains a Project Status section, table of contents and itch.io/releases links. (#6, #15)

## [0.3.1] — 2026-06-20

- 🧹 Release CI strips the Burst `_DoNotShip` folder and mirrors installers to itch.io. (#4)

## [0.3.0] — 2026-06-20

- 🔀 Require a pull-request workflow (repo public, `main` protected). (#1)
- 🔒 Patched langchain/langsmith/starlette security alerts in the AI backend. (#2)
- 🧹 Cleared analyzer warnings (nullable, async, dispose). (#3)

## [0.2.0] — 2026-06-20

- Early release. See the [full diff](https://github.com/marceld23/BlocksBeyondTheStars/compare/v0.1.0...v0.2.0).

## [0.1.0] — 2026-06-20

- Initial public release.

[Unreleased]: https://github.com/marceld23/BlocksBeyondTheStars/compare/v0.8.1...HEAD
[0.8.1]: https://github.com/marceld23/BlocksBeyondTheStars/compare/v0.8.0...v0.8.1
[0.8.0]: https://github.com/marceld23/BlocksBeyondTheStars/compare/v0.7.7...v0.8.0
[0.7.7]: https://github.com/marceld23/BlocksBeyondTheStars/compare/v0.7.6...v0.7.7
[0.7.6]: https://github.com/marceld23/BlocksBeyondTheStars/compare/v0.7.5...v0.7.6
[0.7.5]: https://github.com/marceld23/BlocksBeyondTheStars/compare/v0.7.4...v0.7.5
[0.7.4]: https://github.com/marceld23/BlocksBeyondTheStars/compare/v0.7.3...v0.7.4
[0.7.3]: https://github.com/marceld23/BlocksBeyondTheStars/compare/v0.7.2...v0.7.3
[0.7.2]: https://github.com/marceld23/BlocksBeyondTheStars/compare/v0.7.1...v0.7.2
[0.7.1]: https://github.com/marceld23/BlocksBeyondTheStars/compare/v0.7.0...v0.7.1
[0.7.0]: https://github.com/marceld23/BlocksBeyondTheStars/compare/v0.6.2...v0.7.0
[0.6.2]: https://github.com/marceld23/BlocksBeyondTheStars/compare/v0.6.1...v0.6.2
[0.6.1]: https://github.com/marceld23/BlocksBeyondTheStars/compare/v0.6.0...v0.6.1
[0.6.0]: https://github.com/marceld23/BlocksBeyondTheStars/compare/v0.5.0...v0.6.0
[0.5.0]: https://github.com/marceld23/BlocksBeyondTheStars/compare/v0.4.2...v0.5.0
[0.4.2]: https://github.com/marceld23/BlocksBeyondTheStars/compare/v0.4.1...v0.4.2
[0.4.1]: https://github.com/marceld23/BlocksBeyondTheStars/compare/v0.4.0...v0.4.1
[0.4.0]: https://github.com/marceld23/BlocksBeyondTheStars/compare/v0.3.2...v0.4.0
[0.3.2]: https://github.com/marceld23/BlocksBeyondTheStars/compare/v0.3.1...v0.3.2
[0.3.1]: https://github.com/marceld23/BlocksBeyondTheStars/compare/v0.3.0...v0.3.1
[0.3.0]: https://github.com/marceld23/BlocksBeyondTheStars/compare/v0.2.0...v0.3.0
[0.2.0]: https://github.com/marceld23/BlocksBeyondTheStars/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/marceld23/BlocksBeyondTheStars/releases/tag/v0.1.0
