# SpaceCraft — Project Status

The single source of truth for **what is built** and **what is still open**. Design notes and deep
plans live under [docs/](docs/) (committed); this file is the high-level status. Player-facing operation
(controls, mechanics, editors, commands) is documented in [docs/USER_MANUAL.md](docs/USER_MANUAL.md) —
keep it current when controls/features change. Last consolidated 2026-06-04.

**Build:** `scripts/build-client.ps1` (publishes shared libs + bundled server + Unity Windows player).
**Test:** `dotnet test` — currently **302 passing**. Locale parity (en/de) is enforced by a test.
**Conventions:** English docs/comments; in-game text bilingual DE+EN; commit to `main` with the
`Co-Authored-By: Claude Opus 4.8` trailer; paid/AI asset generation is gated (propose + approve first).

Architecture: Unity 6 (Built-in RP) client + authoritative .NET 8 server, everything built in code (no
scene authoring). One shared world; contractless MessagePack networking; deterministic seed world-gen;
SQLite persistence.

---

## ▶ Open backlog — priority order (updated 2026-06-07)
At-a-glance order of everything still open (new items added 2026-06-07 interleaved with the remaining
analysis-first tasks below). **Same workflow** unless noted: analyse → write the plan here → ask questions →
only then implement. Items marked *(analysis only)* must NOT be implemented yet.

1. ✅ **Task 3 — softer shadows + lit cave mouths** (done 2026-06-07). The mesher's hard binary skylight is now
   a **soft sky-occlusion** (5×5 horizontal kernel): cave mouths feather into a soft-lit gradient and overhang
   shadows soften, while deep caves stay dark (lamp needed). Mesher-only; the shader already took a continuous
   sky. See the Task 3 plan below.
2. ✅ **Bug — no stars in the space background** (FIXED + confirmed 2026-06-07 — the `Spacecraft/Starfield`
   shader was stripped from the build; added to `m_AlwaysIncludedShaders`). Analyse: in the space view there are **no background stars** —
   I see the system's sun but no stars behind it; there should be stars. *This was already attempted once* —
   analyse precisely and find the actual cause. Check whether the cause has **further implications** elsewhere,
   e.g. **stars should also be visible at night on the planet**.

   ### Analysis + Plan (2026-06-07)
   The starfield IS wired and the render path is correct: `Starfield` builds a 1500-quad dome (additive,
   `Background` queue, `Cull Off`, `ZWrite Off`) that follows the camera and its quads face inward toward it
   (`Starfield.cs`); `SpaceView` reparents the same camera and clears to near-black (`SpaceView.cs:629-631`),
   so nothing culls or occludes the dome. `TargetBrightness()` correctly returns 1 in space / airless / station
   and the day/night curve on a planet. **Likely causes the stars don't read:** (1) the brightness **fades in
   over ~1.4 s** — `_brightness = MoveTowards(_brightness, target, dt*0.7)` (`Starfield.cs:61`) — so on entering
   space they're near-invisible at first; (2) at full they're still **dim/small** (`MaxBrightness 0.9`, angular
   half-size ~0.005 rad, twinkle dips to 0.10) — easy to miss next to the bright bloomed sun + ACES tonemap.
   No hard occlusion found in code. **Implication:** the same component draws planet **night** stars (same slow
   fade + dimness), so the fix helps there too. **Plan:** snap brightness to target instantly when the sky is
   "space/airless/station" (keep the smooth dusk/dawn fade on planets); raise `MaxBrightness`, lift the twinkle
   floor and bump star size a touch so they're clearly visible without washing the sky. Then verify in a build;
   if stars still don't show it points to a render issue only visible at runtime (revisit with the game running).

   ✅ **ROOT CAUSE FOUND + FIXED 2026-06-07:** the **`Spacecraft/Starfield` shader was stripped from the build**
   — the game builds all materials in code via `Shader.Find` (no `.mat` assets), and a shader not listed in
   `GraphicsSettings.asset → m_AlwaysIncludedShaders` is dropped from the player build, so `Shader.Find` returns
   null → `Starfield` disabled itself → black sky. The sun rendered because its `SunGlow` shader WAS listed.
   This is why earlier code tweaks never helped (the shader wasn't in the build at all). **Fix:** added the
   Starfield GUID (`1fe9729…`) to `m_AlwaysIncludedShaders`. Also found **`BlockAtlasTransparent` (`152a7b5…`)
   was missing too** — the glass/water transparent shader — and added it (very likely the root of the **glass
   "not milky" bug, item 3**). The brightness/size/twinkle tweaks above stay (now the stars actually render).
   Saved as a memory ([[shaders-must-be-always-included]]). *Verify in the new build.*
3. ✅ **Bug — glass is still only transparent, not milky** (FIXED + confirmed 2026-06-07 — **same root cause as
   item 2**: the `Spacecraft/BlockAtlasTransparent` shader (glass + water) was stripped from the build, so in the
   built `.exe` glass fell back to a plain transparent material with no milky frost. Adding its GUID to
   `m_AlwaysIncludedShaders` restored the frosted glass — confirmed by the user). The shader's milky/water logic
   was correct all along; it just wasn't in the build. See [[glass-milky-not-transparent]] +
   [[shaders-must-be-always-included]].
4. ✅ **Bug — landing pop-in; want a loading screen** (done 2026-06-07). When I land on a planet or station I
   can see the planet/station **building up before my ship appears** (for stations, before everything is
   finished). Instead I want a **loading screen that disappears once everything is ready**. Analyse precisely,
   then make a plan; only then start implementing.

   ### Analysis + Plan (2026-06-07)
   **Flow:** land (`SpaceView` descent) / board → server sends **`WorldReset`** + the new world's chunks
   (`GameServer.HandleTravel` / `GameServerSpaceStations.BoardStation`). Client `OnWorldReset` drops old chunks,
   clears the world, **bumps `WorldEpoch`** (`GameBootstrap.cs:511`) and nulls `ServerSpawn`. Chunks then stream
   in + mesh (`OnChunk`→`RebuildChunk`, all dirty chunks meshed per frame). `PlayerController` watches
   `WorldEpoch` (`:116`), re-snaps to `ServerSpawn`, and holds a **settle-freeze** (`_settling`) until a downward
   raycast finds the ground collider below spawn (`:146-157`) or a 20 s timeout. **Why the build-up shows:**
   nothing covers the window between `WorldReset` (old world gone, new streaming) and the ground being ready —
   so you watch chunks/the ship/station assemble. `WorldReset.Hyperjump` already marks jumps (those are covered
   by the `HyperspaceWarp` overlay). There's a clean uGUI overlay pattern to reuse (`HyperspaceWarp`,
   `UiKit.CreateCanvas`).

   **Decisions (user):** (a) **show the destination name** (planet/station) + type + a spinner/progress feel;
   (b) the overlay **also covers the very first spawn** at app launch.

   **Implemented:** new **`WorldLoadingOverlay`** (`client/Assets/Spacecraft/Scripts/WorldLoadingOverlay.cs`,
   pure uGUI on a DPI-scaled canvas, sortingOrder 75, fade in/out) wired in `WorldRig.Build` next to the warp.
   - **Trigger:** `GameBootstrap` raises a new **`WorldLoadStarted`** event on `JoinAccepted` (first spawn) and
     on non-hyperjump `OnWorldReset` (landing / station boarding / same-system travel). Hyperjumps keep firing
     `HyperjumpStarted` instead, so the warp VFX (not this veil) covers those.
   - **Don't veil the descent:** the overlay only *arms* on the event and waits while `SpaceViewActive` (the
     ship's landing descent plays in the space scene, which already hides the surface). It raises the veil the
     moment we're back on the surface — and skips it entirely if the world already streamed in during the
     descent (`WorldReady` already true).
   - **Ready signal:** `GameBootstrap.WorldReady` (reset false on join/reset) is set true by `PlayerController`
     the instant the **settle-freeze releases** (ground collider exists below spawn). Veil fades out then, after
     a **0.7 s min-show**; a **25 s max-show** safety guarantees it can never trap the player.
   - **Content:** big destination title (`LocationName`), localized world-type subtitle (reuses the existing
     `planet.<type>.name` keys, e.g. *Felsplanet* / *Orbitalstation*), a 12-dot comet-tail spinner, and a
     localized footer with animated dots — new keys `ui.loading.world` / `ui.loading.station` (DE+EN, all four
     locale files). Station boardings read *Betrete Station* / *Entering station*.

   No change to the landing/chunk/settle logic itself — purely an overlay + one readiness flag + one event.
5. 🔄 **Bug/feature — in-space movement: pilot ↔ inside ship ↔ EVA** (reopened 2026-06-07 for a redesign).
   Original: the player must **not fall in space** — float in the suit, get back into the ship, dock a station.
   Stages 1–3 shipped a pilot↔EVA float (oxygen drain + 6-DOF + board) but that **skipped a state the user
   wants**.

   ### Redesign (2026-06-07) — three states, requested by the user
   The player wants **three** modes in space, not two:
   1. **Pilot** — flying the ship (the current flight view).
   2. **Inside the ship (on foot)** — walk around inside your ship's interior **while it floats in space**.
   3. **EVA** — spacewalk outside the hull, **started from inside the ship** (not from the pilot view).
   **Transitions (all bidirectional):** Pilot ⇄ Inside ship; Inside ship ⇄ EVA. (Pilot does **not** go straight
   to EVA any more — my stage-2 `G` from cruise must move to the inside-ship state.)

   **Approach (analysis):** there is no walkable ship interior in space today — on a planet the ship is stamped
   voxels you can walk in; in the flight view it's an abstract model. The clean, consistent way to add a
   walkable interior in space is to **board your own ship like a station**: reuse the station-interior infra
   (`GameServerSpaceStations` — load a small world, stamp the ship structure, spawn the player on foot, keep the
   ship's flight-view position in `SpaceInstance.ShipPosition`). From inside: a **helm/pilot console** returns
   to the flight view (skip-launch, no take-off animation), an **airlock** starts an EVA; the EVA float +
   oxygen + board-back from stages 1–2 are reused (only the entry point changes — EVA from the airlock instead
   of from cruise). Needs confirmation of the approach + the control scheme before building (see questions).

   ### Pending follow-up requirements (from the user, 2026-06-07)
   - **R1 — EVA landing targets:** from EVA you may dock **your own ship** and **space stations**, and land on
     **asteroids**, but **not on planets or moons** (those need the ship). *(server guard + client prompt)*
   - **R2 — multiplayer visibility:** an EVA/in-space player must be **visible to other players** flying in the
     same space instance. *(Big — the flight view renders only your own ship today; remote ships/players in
     space aren't broadcast/rendered. Needs new networking + rendering.)*
   - **R3 — the ship stays put:** if you **dock a station while in EVA**, your **ship remains in space** at its
     position (you didn't take it with you); returning puts you back at the floating ship.
   - **R4 — death respawn:** on death, respawn at the **last planet or station, with your ship** (today: the
     heal-tank `RespawnPoint`; verify it already lands you at the last body, and set it on station boarding).
   - **EVA boarding UX (done 2026-06-07, pending the redesign):** the EVA HUD board hint always shows (distance
     to the parked ship when far), the wrong "G ship" text was fixed, board range widened. *Will be folded into
     the redesign (EVA is launched from the airlock, so the cruise `G` hint changes to "enter ship").*

   ### Plan (locked by the user's answers, 2026-06-07)
   (a) **Inside the ship = the real ship interior** — board your own ship as a walkable world (reuse the
   station-interior infra). (b) **Diegetic controls** — a **pilot console** (E → fly) and an **airlock** (E →
   EVA) inside the ship, with on-screen prompts (the discoverability the user was missing). (c) **Multiplayer
   visibility (R2) is in scope now.**

   **Staged build (server-first, testable; client parts need an in-editor pass):**
   - 🔄 **Stage 4 — board your own ship (walkable interior in space).**
     - ✅ **Server (done 2026-06-07).** New `GameServerShipInterior.cs`: `EnterShipInterior` loads a
       **`ship_interior`** void world (new planet type, clone of `orbital_station`) and **stamps the existing
       ship** into it via the same `StampShip` used on planets (the user's point: the interior already exists),
       spawns the player on foot at the heal-tank, `AboardShip=true`. `ExitShipToFlight` (helm) restores the
       planet world + re-enters the flight view with the ship **put back exactly where it was parked**
       (`ShipPosition` saved across the visit, even though the empty instance unloads). Intents
       `EnterShipIntent`/`ExitShipIntent` + dispatch + `SendEnterShip`/`SendExitShip`; cleared on respawn. Tests
       `EnterShipInterior_ThenHelm_RoundTripsThroughTheFlightView`, `EnterShipInterior_OnlyWorksFromSpace`
       (321 pass).
     - ✅ **Client (done 2026-06-07; needs in-engine test).** Flight view: **F** steps inside the ship
       (`SendEnterShip`), cruise hint updated. Inside the ship the **cockpit is the helm** — using it (E, the
       existing station interaction) calls `ExitShipToFlight` (server branches the cockpit on `InShipInterior`);
       the HUD prompt reads **"Take the helm (fly)"** there (`ui.station.helm`). Returning to the helm **skips
       the take-off animation**: new `SpaceState.SkipLaunch` (set by `EnterSpace(skipLaunch:true)` from
       `ExitShipToFlight`), latched on entry in `GameBootstrap.SpaceSkipLaunch`, so `SpaceView.Enter` starts in
       `Cruise` with no roar. Test `UsingTheCockpitInsideTheShip_TakesTheHelm`. *(Airlock→EVA is stage 5; EVA is
       still `G` from cruise for now.)*
   - ✅ **Stage 5 — EVA from the airlock (done 2026-06-07; needs in-engine test).** A new **airlock** station
     marker by the ship's hatch (`StampShip`); using it in space (`UseStation "airlock"` → `StartEvaFromShip`)
     cycles out into the flight view as a floating EVA suit (`InEva=true`, oxygen drains). The cruise `G`→EVA is
     **removed** — EVA is only reached from inside the ship now (hint updated). Client mirrors the server's
     `InEva` (`BeginEvaMode`, no client-initiated EVA); **EVA → board returns you to the ship interior** on foot
     (`BoardShipFromEva` → `SendEnterShip`), not to the helm. New `_enteringInterior` flag tears the flight view
     down at once (no stray landing descent) when stepping inside. New `ui.station.airlock`. Test
     `AirlockInsideTheShip_StepsOutIntoAnEva` (323 pass). **The three-state loop is now complete:** pilot —F→
     inside ship —(cockpit)→ helm/fly, —(airlock)→ EVA —(E at ship)→ back inside.
   - ⏳ **Stage 6 — R1: EVA landing targets.** From EVA: dock own ship + stations, land on **asteroids only**
     (not planets/moons) — server guard in the leave-space/land path + client prompt filtering by
     `WorldSizeClass.Asteroid`.
   - ⏳ **Stage 7 — R3 + R4: ship stays + respawn.** Docking a station from EVA leaves the ship in space (don't
     move its location); returning resumes at the floating ship. Death respawns at the **last planet/station
     with the ship** (verify `RespawnPoint`; set it on station boarding).
   - ⏳ **Stage 8 — R2: multiplayer visibility in space.** Broadcast each player's ship position + EVA/in-ship
     state to others in the same space instance; render remote ships and floating EVA players in the flight
     view. (The flight view renders only your own ship today — this is new networking + rendering.)

   **Interim (committed before the redesign):** the pilot↔EVA float (stages 1–3) + the EVA-boarding UX fix ship
   now, so the current build is usable/clearer while the redesign is built on top.

   ### Analysis (2026-06-07)
   **Today there is no "on foot in space" state at all.** "In space" only ever means *piloting the ship* in the
   flight view:
   - **Gravity / falling** — `PlayerController.Move()` does `_verticalVelocity -= Gravity * dt` whenever the
     player is in the air (`PlayerController.cs:~779`, `Gravity = 20f`). Gravity is only skipped while:
     `SpaceViewActive` (on-foot control frozen entirely, `:168-171`), a menu/chat is open (gravity-only,
     `:175-179`), the spawn **settle-freeze** (`:146-164`), or **swimming** (buoyant branch, `:760-766`).
   - **Jetpack** — hold Jump in the air to thrust up; needs the `jetpack` item **and** `SuitEnergy > 0`
     (`CanJetpack()` `:724`); server drains energy in `GameServerEquipment.TickJetpack` (~9/s). Caps rise at
     `JetpackMaxRise`. This is the closest existing "suit thrust" code.
   - **"In space" state** — `GameBootstrap.InSpace` flips true on `SpaceStateReceived`, false on `SpaceClosed`.
     Server-side a player is "in space" iff they have a space-instance id. `EnterSpace` (`GameServerSpaceCombat
     .cs:217`) **requires `AboardShip`** ("Board your ship before launching into space.") — so you can only be
     in space *as the ship*.
   - **Launch** — the **`ui.space.enter`** button in the ship tab (`CraftingTechShipUI.cs:612`) → `SendEnterSpace`
     → server `EnterSpace` → `SpaceState` → client `InSpace=true` → `SpaceView.Update` (`:112`,
     `if (Game.InSpace && !_active) Enter()`) → `Enter()` **always** starts `Phase.Launch` — the rising take-off
     animation + `ship_launch` roar + `SpaceViewActive=true` (`SpaceView.cs:573-619`). There is no "skip the
     take-off" path.
   - **Docking** already exists: in the flight view, E near a station → `Phase.Boarding` dock animation →
     `SendBoardStation` → `GameServerSpaceStations.BoardStation` (range 70). On-planet there's also
     `HandleStations` (E to board a nearby station). `LeaveStation` relaunches you as the ship into space.

   ### Plan (subsystems — exact behaviour pending the questions below)
   1. **New state: on-foot in space (zero-g).** Add a server/player flag (e.g. `InSpaceOnFoot`) distinct from
      the ship's space instance, mirrored to the client (`WorldEnvironment`/a small message). While set:
      - **No gravity** — add it to the list of gravity-skip conditions in `PlayerController.Move()`; replace the
        fall path with a **float/drift** path (zero vertical pull; velocity damps to 0 when no input).
      - **Suit float movement** — *(feel TBD, see Q)* either full 6-DOF (mouse-look + WASD + ascend/descend, like
        the ship's `UpdateCruise`) or on-foot WASD + jetpack-style up/down with no ground. Slow, "floaty" accel +
        drag, not walk speed.
      - **Oxygen/energy** — *(TBD, see Q)* either drains suit oxygen like being submerged (risk → must get back),
        or free.
   2. **Board your ship from space (no take-off animation).** A "board/enter ship" action available while
      floating → transitions straight into the **flight view at `Phase.Cruise`** (skip `Phase.Launch`). Implement
      by passing a flag on `SpaceState` (e.g. `SkipLaunch`/`AlreadyInSpace`) so `SpaceView.Enter()` starts at
      Cruise with no `ship_launch` roar. **Launch button fix:** when the player is already in space (on foot),
      the `ui.space.enter` action must use this no-animation path (and read "Board ship" rather than "Launch").
      *(Where the ship is / how you reach it — see Q.)*
   3. **Dock at a station from a float** — reuse the existing station-boarding path (range-gated `BoardStation`)
      from the on-foot-in-space state, not only from the flight view.
   4. **Reverse (EVA) — leave the ship into a float** — *(only if wanted, see Q)* a "step out / EVA" action from
      the flight view that drops you into the on-foot zero-g state next to the ship.

   No world-gen or persistence changes expected beyond the new flag; mostly `PlayerController` (gravity + float
   movement), `SpaceView.Enter` (skip-launch), one server flag + message field, and the launch-button label/branch.

   **Decisions (user, 2026-06-07):** (a) scope = **both** — build EVA now **and** keep the float logic generic
   so item 10 (build into space) docks on later; (b) movement = **full 6-DOF free-fly** (mouse-look + WASD +
   ascend/descend, like piloting); (c) board the ship in space by **floating up to it** (E to board,
   range-gated like station docking); (d) floating **drains suit oxygen** (time pressure → get back in time).

   **Implementation — staged:**
   - ✅ **Stage 1 — server EVA foundation (done 2026-06-07).** `PlayerState.InEva` + the `SetEvaIntent`
     message/handler (`HandleSetEva`, only honoured while actually in a space instance). The oxygen tick now
     treats `InEva` as "no life support, no atmosphere" so oxygen drains even over a breathable world and the
     extractor can't help; empty → the existing suffocation damage. `InEva` is cleared on leave-space, station
     docking, ship loss and death, and mirrored to the client in `PlayerStateUpdate` (`GameBootstrap.InEva`) +
     `NetworkClient.SendSetEva`. Test: `Eva_DrainsOxygen_EvenOverABreathableWorld`. (319 tests pass.)
   - ✅ **Stage 2 — client EVA mode in `SpaceView` (done 2026-06-07; needs in-engine test).** **G** in the
     flight view steps out into a first-person **6-DOF** spacewalk (`EnterEva` → `SendSetEva(true)`): the ship
     stays parked, mouse-look + WASD + **Space/Ctrl** up/down at `EvaSpeed`, bounds + body keep-out reused.
     Float up to the parked ship and **E boards it with no take-off animation** (`BoardShipFromEva`); E near a
     station docks it (`DockStationFromEva`, reuses the board teardown). EVA HUD: float-controls hint, a
     ship/station board prompt, and an **O₂ readout** (red + pulsing under 25 %). Cruise hint now lists `G EVA`;
     crosshair/flare/systems/engine gated off during EVA. New keys `ui.space.eva_controls` /
     `.eva_board_ship` / `.eva_oxygen` (DE+EN). *Unity client — couldn't be compiled in the sandbox; verify in
     the editor (feel of the float, board ranges, the O₂ drain loop).*
   - ✅ **Stage 3 — launch-button finding + zero-g groundwork (done 2026-06-07).** **Launch button:** already
     correct — the `ui.space.enter` button is guarded by `if (!Game.InSpace)` (`CraftingTechShipUI.cs:609`) and
     shows **Leave** instead while in a space instance, so it never replays the take-off animation in space. The
     EVA→ship board (stage 2) is client-side, so it's animation-free too. (Undocking a station deliberately
     re-launches from the return planet — left as designed.) The remaining no-anim case belongs to item 10
     (launching from a tower already in space); a `SpaceState` skip-launch flag will be added there. **Zero-g
     net:** `GameBootstrap.OnFootInSpace` + a float branch in `PlayerController.Move` — above the atmosphere
     there's no gravity, so the player floats (Jump rises, Ctrl/C sinks, else drifts to a stop) instead of
     falling. Nothing sets the flag yet (a no-op until item 10 flips it) — the "must not fall in space" net.
6. **Bug — save the player's position per planet.** When I land on another planet, my **position there** should
   be saved too, so on **loading the save I'm back there** (not just the last/home world).
7. **Bug — creatures chase forever + spawn only at the ship.** Analyse: creatures seem to **follow the player
   constantly**. After a while, pursuing/attacking creatures should **leave the player alone**. Also: **where do
   creatures spawn?** Make sure they **don't only spawn at the ship** but are **distributed across their biomes**.
   Analyse precisely, then make a plan, ask questions, only then implement.
8. **Task 4 — content-styled icons** for everything pickup-able / hand-held. (Detailed below.)
9. **Feature — holographic visor HUD.** Analyse + plan: can the in-game UI be adapted so it looks like a
   **holographic HUD projected onto the inside of an outward-curved glass** (the space-suit visor glass)? Look
   at which **effects** can be used to achieve it (e.g. curvature/parallax, a fresnel/rim glow on the visor,
   scanlines, chromatic-edge fringing, bloom/glow, subtle distortion + reflections). Analyse + plan only for now.
10. **Feature — build high enough to leave the atmosphere into space.** Analyse + plan thoroughly: it should be
   theoretically possible, on a world / moon / asteroid, to **build a structure tall enough that it leaves the
   atmosphere** (if there is one) and you are **in space**. Analyse precisely, then plan, ask questions, only
   then implement. (Ties into item 5 and the sphere analysis 18.)
11. **Feature — trade knowledge points.** Players should be able to **trade knowledge points**. Key constraint:
   knowledge **never goes away** — unlocking blueprints only needs a knowledge **threshold**, no points are
   spent; so trading must **not deduct** knowledge points either. But it must be ensured that **each knowledge
   point can only be passed to another player once** — track how many points you've **already given to a given
   other player**, so no endless back-and-forth trade is possible. It should be possible to **offer knowledge
   for materials or for equipment** in a trade. Analyse precisely, then plan, ask questions, only then implement.
12. **Feature — NPCs should have names.** Give NPCs a name too. Build a **random name generator** with as many
   random name **combinations** as possible. (Mirror the existing syllable-based flora/creature `NameGenerator`
   approach so names are deterministic per NPC.) — *Foundation for items 14 + 15.*
13. **Feature — mission-giver NPCs that always have a mission on offer.** There should also be NPCs that **offer
   missions** — and each such NPC should **always have a mission available** (never run dry). Make sure this is
   the case. Analyse the current NPC/mission wiring, then plan, ask questions, only then implement.
14. **Feature (plan ahead) — NPCs remember their interactions with a player.** NPCs should **"remember"** when
   they have interacted with a player. Store **what kind** of interaction it was: just a **dialog**, a **trade**,
   or a **mission accepted**. This then **improves the relationship value** to that player. Keep an **interaction
   log of at most the last 10** interactions (per player). (Feeds item 15 — name, role, relationship and these
   logs are part of what the dialog backend receives.) Analyse precisely, then plan, ask questions, only then
   implement.
15. **Feature — AI dialog backend (`./ai-backend/`, Python) for NPC dialog text.** *(Analyse precisely + plan
   first; research online if needed. Do NOT implement yet.)* Under `./ai-backend/` there should be a **Python
   backend** exposing an **API** the game can later call to **fetch NPC dialog texts**. The backend **consumes /
   uses a language model available via an API call** — at the start a **local model running in LM Studio**.
   - **Graceful skip:** if the backend has **no language model available**, the in-game function is **simply
     skipped** (the NPC falls back to its normal/canned dialog).
   - **Request parameters** the game server sends: **name of the planet / station**, the NPC's **function/role**,
     its **name**, the NPC's **relationship status** to the player, the **logs of the last interactions** (item
     14), the **offer** the NPC makes (trader: **what kind of goods**; quest giver: **type of quest**), plus the
     current **weather** and **time of day**.
   - **Flow:** the backend wraps these in a **system prompt**, sends them to the LLM, generates a **short text**,
     and returns it to the game engine, which **displays the text on interaction** with the NPC.
   - **Non-blocking:** generating an LLM response can take a while — the game must **wait but not block** (async;
     show the text once ready, with an instant fallback/placeholder meanwhile).
   - **Toggle:** the LLM feature must be **on/off-switchable in the game settings** (i.e. when **creating a game
     world**).
   - **Separate process:** the backend should be **startable and run separately** from the game (at least at
     first).
16. **Task 5 — crafting / tech-tree / materials overhaul + more metals & rare earths.** (Detailed below; big.)
17. **Task 6 — drastically more flora & fauna variety** (with generated textures + sounds). (Detailed below; big.)
18. **Analysis only — make a world more spherical (vertical wrap too).** *(Analysis only — do NOT implement.)*
   Analyse and estimate: how could the game be changed so a world can be circumnavigated **not only horizontally
   but also vertically** — i.e. how to make a world behave **more like a sphere**. Assess what's **realistically
   possible**, weighing **complexity and performance cost**. For now, just **estimate and analyse**.

---

## ⏭ Requested 2026-06-07: six analysis-first tasks (do one at a time)
Workflow for these (per the user): for **each** task — (1) thorough analysis of the current code, (2) write
an **Analysis + Plan** block here **before** any implementation, (3) ask clarifying questions if needed,
(4) only then implement + commit. One task at a time. Asset generation (OpenAI textures / ElevenLabs sounds)
is **pre-approved** (keys in `tools/ai-assets/.env`, run via `uv`).

- ✅ **Task 1 — Swimming / diving + ship landing underwater** (done 2026-06-07). Part 1: the chunk collider
  now excludes fluids so the player swims/dives — buoyant sink, **Jump = rise/surface**, water breaks falls;
  submerging spends the **suit oxygen** even on a breathable world. Part 2: water renders **transparent**
  (alpha submesh, clear-blue tile alpha, no frost) so you see down into seas. Part 3: the fluid sim is
  **ship-aware** (`FluidCanEnter`) — ships land at the seabed (so underwater on water worlds) with a **dry,
  watertight cabin** that water can't flow into. Tests: `Submerged_DrainsSuitOxygen_…`,
  `Fluid_DoesNotFlowIntoAShipInterior`. ✅ **Polish:** a subtle blue full-screen wash while the eye is
  submerged (`WeatherFx.EyeUnderwater` + an IMGUI wash, smoothed, hidden in space/menu).
- ✅ **Task 2 — Walk all the way around a planet** (done 2026-06-07). **Verdict: yes** — the world is a
  cylinder (X = wrapping longitude, Circumference 6000, seam-free noise), a lap ≈ 16 min. Shipped: **Fix 1** —
  `WrapDistanceSquared`/`WrapDistSq` make every on-planet proximity check seam-aware (creatures, doors,
  enemies, NPCs, vendors, containers, trade, ship station, bump) so interactions work across X=0 (space combat
  left alone). **Fix 2** — `WorldConstants.LatitudeLimit` + an invisible **pole barrier** (server clamps Z,
  client wall) so N/S is bounded instead of an infinite strip. **Sizes** — each planet/moon gets a
  deterministic random **size in the orbit view** (`BodySizeScale`); walkable circumference stays 6000.
  Tests: `WrapDistanceSquared_MeasuresProximityAcrossTheSeam`, `WalkingTowardThePole_IsBoundedByTheLatitude-
  Barrier`. *(Future option: true per-world walkable circumference — see Task 2b below.)*

  ### Task 2b — Per-world walkable circumference (requested 2026-06-07, analysis-first)
  **Finding: there is NO per-world walkable size today** — every world is the global `WorldConstants.
  Circumference = 6000`. `PlanetType.WorldRadius` exists but is *informational only* (not wired into gen/wrap);
  the orbit `BodySizeScale` is cosmetic. **Blast radius:** ~23 sites + the 6 static wrap helpers (`WrapX`/
  `WrapDeltaX`/`CanonicalChunkX`/`CanonicalChunk`/`CanonicalBlock`/`WrapDistanceSquared`) + derived
  `ChunksAround`/`LatitudeLimit`, across server (`ServerWorld` block/chunk canonicalisation, `GameServer`
  streaming/move/reach), client (`GameBootstrap.SceneX`/`RepositionChunks`/`DayCircumference`, `ClientWorld`),
  and worldgen (`Noise.FbmCylX`/`ValueCylX` circular domain + flora `WrapX`). **Persistence:** circumference is
  baked into chunk keys (`ChunksAround`) — it must be **immutable per world**, or saved chunks break.

  **Plan (multi-commit, non-breaking for old saves):**
  1. **Shared `WorldGeometry`** — an instance object `{ int Circumference; ChunksAround; LatitudeLimit; WrapX;
     WrapDeltaX; CanonicalChunkX/Chunk/Block; WrapDistanceSquared }`. Keep the old static `WorldConstants`
     helpers (default 6000) as thin wrappers so nothing else breaks mid-refactor. Add `CircumferenceFor(key)`
     — deterministic per body (moons smaller than planets, e.g. moons ~3000–4500, planets ~5000–9000).
  2. **Persist** the chosen circumference in world metadata at creation (default 6000 when absent → old saves
     stay 6000 and keep working); load it immutably.
  3. **Server** threads the world's `WorldGeometry` into `ServerWorld` (block/chunk canonicalisation), the
     `WorldGenerator` (ctor param → noise domain + flora wrap), move/reach/stream, and `LatitudeLimit`.
  4. **Network** — send `Circumference` (+ `LatitudeLimit`) in `WorldEnvironment` (already broadcast on
     join/world-switch).
  5. **Client** caches it into a `WorldGeometry`, uses it for `SceneX`/`RepositionChunks`/`DayCircumference`/
     `ClientWorld` wrap; the **orbit `BodySizeScale` now reflects each body's real circumference** (derive via
     `CircumferenceFor(body.Id)`), so the space-view size ≈ the walkable size.
  6. **Tests** — wrap consistency across several circumferences; a small vs large world differ.

  **Decisions (2026-06-07):** ignore save compatibility (derive deterministically, no metadata persistence);
  round circumference to a multiple of ChunkSize (16). **Three size classes** by body: **asteroid**
  (landable, PlanetType `asteroid`) **800–1600**, **moon** (`CelestialKind.Moon`) **2500–4000**, **planet**
  **5000–12000** — `CircumferenceFor(bodyId, class)` deterministic; `SizeClassFor(kind, planetKey)` in shared
  so server (active world from `_galaxy.FindBody`) + client (orbit from `NetBody`) agree.

  ✅ **DONE 2026-06-07** (3 stages): **Stage 1** (`b21a299`) — `WorldConstants` circumference overloads +
  `WorldSizeClass`/`SizeClassFor`/`CircumferenceFor`. **Stage 2** (`d35a587`) — server sizes each world from
  its body (`LoadWorld` → `SetCircumference` on the generator + `ServerWorld.Circumference`); terrain/caves/
  ore/biomes/flora wrap at it; move-wrap, pole clamp, reach + proximity read the active size; `WorldEnvironment`
  carries Circumference + LatitudeLimit. **Stage 3** — client caches it (`GameBootstrap.Circumference` +
  `ClientWorld._circumference`) for `SceneX`/day span/chunk wrap; the orbit view sizes each body by its real
  circumference (`OrbitDiameterFor`). Tests read `server.World.Circumference`; 318 pass.

  ### Task 2 — Analysis + Plan (2026-06-07)
  **Verdict: circumnavigation already works (W0–W4).** The world is a **cylinder**: X is a wrapping longitude,
  **Circumference = 6000 blocks** (`WorldConstants.cs:22`). Terrain/biomes/caves/ore are seam-free via
  circular-domain noise (`Noise.FbmCylX`/`ValueCylX`), **proven by 10 `WorldWrapTests`** (height/biome/caves/
  ore identical at X=0 ≡ 6000). The server wraps the player's X (`GameServer.cs:1184`); the client renders the
  nearest wrapped copy via `SceneX`, so crossing X=0 has **no visible jump/seam**. Mining/placing wrap
  (`WithinReach` uses `WrapDeltaX`). A full lap ≈ **6000 ÷ 6 m/s ≈ 16–17 min** of straight walking. Latitude
  (Z) is **not** wrapped (you circle the equator, not over the poles).

  **Two gaps:**
  1. **~21 unwrapped `DistanceSquared` proximity checks** break interactions across the seam: `GameServer-
     Creatures.cs` (171/375/391/402), `GameServerDoors.cs:184`, `GameServerSettlements.cs:233`,
     `GameServerTrade.cs:49`, `GameServerEnemies.cs` (71/146), `GameServerContainers.cs:56`,
     `GameServerShipStructure.cs:410`, `GameServerSpaceCombat.cs` (365/562/671/694). An object just across X=0
     reads as ~6000 blocks away. Mining/placing are fine (already wrapped); these AI/interaction checks aren't.
  2. **Poles (W5) not done** — Z is unbounded: you can walk north/south forever into generated terrain with no
     barrier, so the planet doesn't feel bounded N/S.

  **Plan:**
  - **Fix 1 (seam interactions):** add a wrap-aware `WrapDistanceSquared(a, b)` helper (X the short way round,
    plain Y/Z) and replace the unwrapped proximity `DistanceSquared` calls in the listed server systems. Add a
    test that a creature/door/vendor just across the seam is reachable.
  - **Fix 2 (poles, optional):** bound latitude with a **pole barrier** — past a latitude limit the surface
    rises into an impassable ice wall (frozen biome), so walking N/S ends at a wall instead of infinite void.
  - **Planet size:** 6000 blocks ≈ 16 min/lap; can shrink for a faster "around the world" feel if desired.
- **Task 3 — Shadows & darkness on planets.** Analyse how shadow-casting + darkness work. A **cave entrance
  currently looks like a black wall** — change it so the entrance reads as **softly lit**. Shadows should be
  **softer**, not so **hard-edged**.

  ### Task 3 — Analysis + Plan (2026-06-07)
  **There is no real shadow-mapping.** "Shadow"/darkness comes from a per-face **skylight** flag the mesher
  bakes into `TEXCOORD1.x`: `sky = ny > Top(nx,nz) ? 1 : 0` (`ChunkMesher.cs:128`) — 1 if the air cell the face
  looks into is above its column's highest solid (open to the sky), else 0. `BlockAtlas.shader` turns it into
  `amb = lerp(0.24, 0.70, sky)` and `col = albedo*(light*(amb + 0.5*ndl*sky) + 0.05)*faceAo`
  (`BlockAtlas.shader:107-117`). So an open face is ~0.70 + sun; an occluded face (cave/overhang/indoor) is a
  flat **0.24** (+0.05 floor). **Root cause of both symptoms = the binary skylight:** one block into a cave
  jumps 0.70→0.24, so the mouth is an abrupt dark wall and overhang shadows have hard edges. The shader already
  consumes a *continuous* `sky`, so the fix is mesher-side only. (The `ndl` sun term is already smooth; there's
  no sun shadow-casting at all, just the dark side of blocks + the skylight occlusion.)

  **Implications elsewhere:** the same skylight gates ship/station interiors (compensated by `_Sc_Indoor`),
  overhangs, cliff undersides and tree shade — softening it improves all of them, and bleeds a little light
  into doorways/windows (a plus). It does **not** touch night-on-planet (that's the day/night `light` term).

  **Plan (mesher-only):** replace the binary `sky` with a **smooth sky-occlusion** 0..1 — average the
  open-to-sky test (`ny > Top`) over a small horizontal kernel (e.g. 3×3 or 5×5 columns) around the air cell,
  blended with the cell's own openness. A face at a cave mouth (some open neighbours) gets a partial value → a
  soft gradient instead of a wall; a **deep** cave (no open neighbours) stays ~0, so it still needs a lamp.
  `Top()` is column-cached, so this is a few extra dict lookups per drawn face at **mesh time** (not per
  frame). Optionally nudge the cave-ambient floor (0.24) up slightly. No shader change required.

  **Decisions:** only soften+light the **entrance** (deep caves stay dark, lamp required); **strong (5×5)**.
  ✅ **Implemented 2026-06-07:** `ChunkMesher.Skylight(wx,wy,wz)` returns 1 if the cell sees open sky, else the
  fraction of a 5×5 horizontal column-neighbourhood open at that height — a smooth gradient feeding the shader's
  existing `lerp(0.24,0.70,sky)`. `Top()` is column-cached so the extra lookups are mesh-time only. No shader
  change.
- **Task 4 — Appealing icons for everything pickup-able / hand-held.** Current icons are crude and off-style.
  Plan: **materials** → a downscaled in-game **texture** (like the harvested-plant icon), generated from game
  content. **Meat** → a steak icon (green if toxic, else normal). **Items + tools** → same style as the in-game
  icons. **Audit which items + materials need an icon, make a list**, then use the **OpenAI** generator to
  create + wire them. Use the icons in the **player menu (crafting)** and on **blueprints**. Also make icons
  for the **space view** (laser, tractor beam) and for **ship upgrades/modules**, and use them in the menu.
- **Task 5 — Crafting + tech-tree + materials overhaul.** Analyse the crafting/tech tree + existing materials.
  Goal: a **working crafting base that builds up in stages** with real **prerequisites** — some materials are
  gathered, others are **crafted from base materials**. Find inconsistencies. Plan how to expand materials +
  crafting for **player items, ship parts, and ships**. Plan what **kinds of objects** are still needed to
  **build on worlds**. **Expand the metals/materials found on planets** — gold, silver, copper, etc.: take all
  plausible **metals, rare earths, raw resources**. Generate their **textures (OpenAI)** and fold the new
  materials into the crafting logic.
- **Task 6 — Drastically increase flora & fauna variety.** Add new **base types** with their **sounds +
  textures** generated immediately (OpenAI textures, ElevenLabs sounds — via the Python tools). Remember some
  flora/fauna can (rarely) serve as a **material substitute**. Generate textures + sounds for the new fauna too.

### Task 1 — Analysis + Plan (2026-06-07) — swimming/diving, transparent water, underwater ship
**Analysis of today's behaviour (file:line):**
- **Player can't swim.** `PlayerController` is a `CharacterController` with simple gravity/jump
  (`Gravity 20`, `JumpSpeed 7`, vertical at `PlayerController.cs:759/763/698`). It has **no water detection**
  for movement. `water` block is `Solid` (no `solid:false` in `data/blocks.json:18`) and the chunk
  **`MeshCollider` uses the whole render mesh** (`GameBootstrap.cs:594/599`), so the player **walks on top of
  water like ground** — no sinking, diving or buoyancy. (Only `ClientAudio.HeadInFluid` samples water, for the
  muffle.)
- **Water is opaque.** `ChunkMesher.IsTransparent` returns true only for `glass`/`force_field`
  (`ChunkMesher.cs:223-231`); water renders in the opaque submesh 0. A `BlockAtlasTransparent` shader exists
  (used by glass). Transparent faces are only drawn toward air (`ChunkMesher.cs:101`).
- **Ship lands on the seabed.** `StampShip` anchors at `SurfaceHeight` (= terrain/seabed) at
  `GameServerShipStructure.cs:55`, so on a water world it is already **underwater**. The interior is stamped to
  air (clears any water there at stamp time). `FillShipFoundation` plugs only **air** cavities below the ship,
  not water. **The fluid sim has zero ship awareness** (`GameServerFluids.cs` TickFluids/Spread/FillFluid only
  test `IsAir`), so woken water can flow through the hatch/gaps into the interior; the hull is protected from
  mining but **not watertight against fluids**.

**Plan (3 parts):**
1. **Swim/dive (client).** Build the chunk **MeshCollider from solid blocks only** (exclude `water`/`lava`),
   so the player falls *into* water instead of standing on it (ChunkMesher emits a collider triangle set that
   skips fluids; `GameBootstrap` assigns it). Add water physics to `PlayerController`: detect submerged (sample
   the water block at the body), replace gravity with gentle buoyant **sink**, **Jump = swim up / surface**, and
   a real jump-out when the head breaches the surface; damp horizontal speed in water. (Dive-deeper control TBD
   — see questions.)
2. **Transparent water (client).** Add `water` to `IsTransparent` → renders in the alpha submesh; give the
   **water tile an alpha (~0.6)** and have `BlockAtlasTransparent` blend by texture alpha (glass stays ~0.85
   milky per the glass memory). Internal water faces already cull (only faces toward air draw), so a sea shows
   its surface + you can see down into it while diving.
3. **Underwater ship, watertight (server).** Make the fluid sim **ship-aware**: never fill/flow into cells
   inside `ShipInteriorContains` (water stops at the doorway plane). Explicitly **clear water/lava in the
   interior** (and a 1-block margin) at stamp; extend `FillShipFoundation` to also replace water/lava under the
   footprint so nothing seeps up. Leave landing at the seabed (so it *can* land underwater) unless the user
   prefers dry-land preference (see questions). Tests: collider excludes fluids; fluid won't enter a stamped
   ship interior; ship interior is water-free after landing in a sea.

---

## ✅ Done (2026-06-06): world block — terrain archetypes, seas, trees
Done in the user's reconsidered order (terrain shapes the basins → fluids fill them → trees on the land):
- **Regional terrain archetypes** — `SurfaceHeight` modulates the base terrain by a large-scale field that
  blends a seed-picked subset of archetypes (flats / rolling plains / hills / mountains / canyons; canyons
  ridged), so a world varies between flat and rugged and worlds differ. Deterministic + X-seam-safe.
- **Surface seas** — basins fill below a per-world sea level: **water** on atmosphere worlds, **lava** on
  volcanic/airless ones (never both); `PlanetType.WaterAbundance`/`LavaAbundance` (null = auto). Depth/island
  variety falls out of the terrain. Land flora skips underwater.
- **Trees** — new `wood_log` + `tree_leaves` blocks (with **OpenAI tile textures**) + a generator pass:
  multi-block trunk + leaf crown on grass/earth/mud, seam-safe across chunk/longitude edges.
  `PlanetType.TreeDensity` (null = auto). 5 new world-gen tests; 297 pass.
- **Two earlier bugs**: ship-station prompt is now look-based (not "always Workshop"); ships fill caves
  under their footprint + the spawn holds longer so you can't fall into a cave on load.
- Follow-ups noted (für später): aquatic fauna + water flora. (Mineable water/lava — beam + source logic —
  shipped 2026-06-06; see the planned-list ✅ below.)

## ✅ Done (2026-06-06): menu/editor/polish wave
- **Craft quantity** stepper (− [n] + / Max), server clamps batch 1..999.
- **Delete singleplayer worlds** from the save picker (with a confirm dialog).
- **Editors load saved designs** — ship + structure editors gained a LOAD button (lists exports, rebuilds
  the chosen design to keep editing).
- **Connect-to-server** dialog (editable IP/port) on the menu's Join button (remote MP was already wired).
- **Round background stars** in space (removed the blocky star cubes; the Starfield dome provides them).
- **Fall damage** on hard landings (client reports impact speed → server applies armor-reduced damage; a
  lethal fall respawns with the death flash). New `FallDamageIntent` (47).
- **Credits** rewritten to the JuMaVe Games family project (+ community call), en/de.

## ✅ Done (2026-06-06): bug-fix wave — glass, space, combat, death feedback
- **Milky glass** (transparent shader: non-emissive glass alpha 0.72 + white frost; fields stay see-through);
  ship-editor glass relabelled **Window**.
- **Space:** planets/moons now never overlap (a relaxation/separation pass de-overlaps the whole set, not
  just a moon vs. its own parent); render planets even when `PlanetType` is missing; **station keep-out** so
  you slide around a station instead of flying through (still dockable with E); radar no longer paints a
  phantom red blip at the rim for an out-of-range enemy; **asteroid fields slowly respawn** over a session.
- **Combat/vendor:** machete 30→12 damage (no longer one-shots most animals, HP≈14–36); pressing **E at a
  vendor opens the market/trade view** (OpenMarket reordered so the category sticks).
- **Death feedback:** planet death → **red screen flash + `player_death` sound**; ship destroyed in space →
  **explosion glare + `space_death` sound** (new `DeathFx`; `RespawnNotice.Died` distinguishes a death from
  the void-rescue). Two new ElevenLabs cues.
- Persistence analysed: planet/moon/station positions are deterministic from the seed (stable across
  land/relaunch); only asteroids weren't replenishing — now they do.

## ✅ Done (2026-06-06): Void-fall fix, ship-editor doors, NPC walls/animation, studio rename
- **Infinite-fall fix (the "PC2 falls forever" bug):** root-caused — the world has no bedrock floor, so a
  player below the terrain with nothing under them falls forever; their position is **persisted and restored
  verbatim on join** ([GameServer.cs](src/Spacecraft.GameServer/GameServer.cs) `LoadPlayer ?? CreateNewPlayer`,
  and `SetupPlayerShip` doesn't reset Position), so one fall **poisons the save** and every launch drops them
  again — machine-specific because each PC has its own `singleplayer-saves`. New `GameServerSpawnSafety`:
  `EnsureSafeSpawn` validates a joining player's position (snaps a void position — and a poisoned respawn
  point — back to safe ground), and `TickVoidRescue` recovers anyone plummeting at runtime before the fall
  can be saved. "In the void" = well below the column's terrain surface with nothing solid within reach.
  2 tests.
- **Studio rename:** Unity `companyName` → **"JuMaVe Games"** (productName stays "Spacecraft"). Note: this
  moves `persistentDataPath` to `…/LocalLow/JuMaVe Games/Spacecraft/` — old singleplayer saves live under the
  former `…/Spacecraft/Spacecraft/` path.
- **Ship-editor doors:** the **ship editor** palette gained a **Door**; designed-ship `door_slide`/`door_hinge`
  cells are opened (3 tall) and registered as sci-fi slide doors by the door registry (now rebuilt from every
  settlement **and** ship stamp in the world). Settlement editor already had slide+hinge doors.
- **NPCs no longer walk through walls:** `MoveNpcs` only checked the ship before; it now also rejects a step
  into a solid block (`BlockedByWorld`) — inhabitants stay inside their building (doorway openings stay
  passable). Tighter wander leash (2.5 → 1.6).
- **Smoother NPC motion + walk animation:** NPC position broadcast 0.5 s → 0.2 s and the client interpolates
  without fully catching up (lerp 8 → 5), so NPCs glide instead of stop-start jerking; the shared avatar
  walk cycle reaches a full stride a little sooner so slow strolls read as walking (helps the player avatar
  too).

## ✅ Done (2026-06-06): Doors (settlements) — D1–D5
Settlement doorways now hold real, server-authoritative doors (rendered + collided client-side, since
movement is client-side). Three bespoke **ElevenLabs** SFX: `door_slide_open`, `door_slide_close`,
`door_hinge`.
- **D1 — server `GameServerDoors`:** a per-world `ServerDoor` registry built from `door_slide`/`door_hinge`
  markers on stamp; the wall axis + gap width are **inferred by probing the surrounding blocks**, so doors
  line up regardless of facing. `TickDoors` auto-opens slide doors for a player within range and auto-closes
  them a moment after the last one leaves; `HandleDoorInteract` toggles a hinge door a player stands at.
  Cleared in `ResetWorldRuntimeState`; sent on join/travel (`SendDoors`) + broadcast on change. Messages:
  `DoorInteractIntent` (46), `DoorList` (93).
- **D2 — client `DoorView`:** renders each door from `DoorList` (slide = two panels swoosh apart; hinge =
  a leaf swings ~96°), with a `BoxCollider` that blocks the `CharacterController` while closed and lifts
  while open. Plays the slide/hinge SFX on state change; shows an "E" hint over a reachable hinge door.
- **D3 — generator places doors:** the settlement generator emits a door marker at each (non-ruined)
  building's doorway — **slide** for towns/cities, **hinge** for villages/hamlets; the opening stays air.
- **D4 — editor:** the settlement editor palette gained **Slide door** + **Hinge door** markers (they flow
  through `StructureTemplate` cells → the D1 registry).
- **D5 — localization + tests:** `ui.door.hint` (en/de); 3 server tests — a slide door auto-opens on approach
  + auto-closes, a hinge door toggles on interact (and not from afar), doors register at real doorways.
- **Follow-up DONE (2026-06-06):** auto-doors for **orbital stations** (each cut module doorway emits a
  `door_slide` marker → `RegisterStationDoors` on board) and **the box starter ship's hatch** (now a sliding
  door); designed ships already had editor door cells.

## ✅ Done (2026-06-06): JuMaVe Games studio splash + moon-overlap fix
- **Studio splash:** a new `StudioSplash` (+ `ShellPhase.Studio`, now the first phase) shows the **JuMaVe
  Games** developer-studio screen for **5 s** right after the "Made with Unity" screen, before the SPACECRAFT
  title splash. Code-built uGUI: an assembling block-cluster emblem inside a sweeping orbit ring (a little
  rocket circling it + twinkling stars + a glow pulse), the gradient wordmark (**Ju** cyan · **Ma** white ·
  **Ve** orange) with "GAMES", the slogan **"Built from imagination."**, and a reveal flash. Skippable after
  a moment. A whoosh→tada sting lands on the reveal (`AppShell.PlayStudioSting`) — falls back to the intro
  sting until the bespoke **ElevenLabs** `audio/jumave_sting` is bundled (proposed for approval).
- **Moon overlap fixed:** the compact `SystemViewScale` (0.16) had sunk moons *inside* their planets (moon
  orbit 90 system-units × 0.16 = 14 flight units < a planet's 23-unit radius). `BuildSystemBodies` now places
  each moon relative to its **parent planet** (nearest in system space) and pushes it **out past the planet's
  surface**, so moons orbit clear of their planet at any view scale.

---

## ✅ Done (2026-06-06): Closer planets, radar bearings, ship-systems quick-bar
- **Planets closer together:** flight distance = orbit spacing (~520 system units between adjacent orbits)
  × `SystemViewScale`. That was 0.30 → ~156 flight units (~11 s cruise) between neighbours; lowered to **0.16
  → ~83 units (~6 s)** so the system is a short hop, not a slog. Client-only (no universe regen).
- **Radar bearings to planets:** the cockpit radar (`SpaceRadar`) now reads `SpaceView.Landables` and draws a
  **green marker per planet/moon**, pinned to the rim when far so it reads as a **direction arrow** ("head
  that way"); the readout under the radar shows the nearest dockable station or, failing that, **➜ nearest
  planet · distance**.
- **Ship-systems quick-bar:** a HUD bar in flight built from the active ship's fitted modules
  (`ShipCombatStatus.Modules`, new) — **Laser** (any weapon module) + **Tractor** (tractor beam). Pick with
  **1–9**, **use with LMB**: the laser auto-locks the best target ahead (mines + fights), the tractor does a
  **manual wide sweep** to pull in salvage (new `TractorPullIntent` → wider-range `CollectSalvage`). The
  starter ship now carries a tractor beam too. Bottom controls hint reworded (en/de).

---

## ✅ Done (2026-06-06): Machete actually hits + in-game Settings tab
- **Melee attack fixed:** equipping the machete *reduced* your reach (the server used the weapon's short
  range 3.5 while the client targets within the 6-block swing reach, so hits at 3.5–6 silently rejected). A
  weapon now never reaches *less* than the default swing (`reach = Max(weaponRange, EnemyAttackReach)`), and
  **left-click attacks** when a weapon is held (same swing as F). Test
  `Machete_HitsCreature_WithinDefaultReach_EvenBeyondItsShortRange`.
- **In-game Settings tab:** the Tab menu's old "Character" tab is now **Settings** — keeps the appearance
  rows and adds **master volume** (− / +, applied + persisted live) and an explicit **Save game now** button
  (new `SaveGameIntent` → server `SaveAll()`). en/de.

---

## ✅ Done (2026-06-06): Starter weapons + ship weapons finished (W1–W5)
- **W1 — starter melee weapon:** new players spawn with a **machete** (the existing melee attack +
  slash-arc VFX already worked). Test `StarterKit_IncludesASimpleMeleeWeapon`.
- **W2 — dual ship laser:** a new **`ship_laser_basic`** module (`weapon_class: 2`) fitted on every starter
  ship (starter/scout/hauler). A new dual class means **one laser both mines asteroids AND fights hostiles**
  (`WeaponSpec` now carries `IsCombat` + `CanMine`; `weapon_class` 0=mining, 1=combat, 2=dual). Tests
  `StarterShip_HasADualLaser_ThatMinesAsteroids`, `…DualLaser_AlsoDamagesHostiles`.
- **W3 — fire in flight:** hold **LMB / Space** in the space view to fire — it auto-locks the best target
  in range ahead (a centre crosshair brightens cyan on lock), rate-limited by the weapon cooldown. (Was only
  fireable from a tech-UI list before.)
- **W4 — SFX + VFX:** a laser **bolt** from the ship to the target + an **impact flash** (amber for mining,
  cyan for combat), with new procedural **`ship_laser`/`ship_mine`** sounds (the old `ship_weapon` cue never
  existed, so firing was silent).
- **W5 — craft + editor:** weapon modules are built/fitted from the ship tech UI (craftable; cannons keep
  their blueprints, the starter laser is always buildable) and the **ship editor palette** now carries a
  Laser Cannon + Ship Cannon. Controls hint updated (en/de).
- **Follow-ups:** fire-in-flight always uses the starter laser (could auto-pick the best fitted weapon);
  cooldown/energy are client-side only (no server-side rate/energy gate yet).

---

## ✅ Done (2026-06-06): New-world spawn fall-through — nearest-first chunk streaming
Spawning on a fresh planet dropped you **below the surface**. Cause: `StreamChunks` streamed a fixed
bottom-up column (`dy=-3` first), so with a large view distance + a new world's slow first-gen the **surface
chunk (your floor) arrived only after many ticks** — past the client's settle-freeze timeout, which then
released and let you fall through. Fix: stream **nearest-first** (sort the view column by distance to the
player), so your own chunk (its floor) loads before anything else and the freeze releases onto solid ground
immediately; the freeze timeout was also raised 8s→12s as a margin. Same spirit as the station floor pad.
Test `StreamChunks_SendsThePlayersOwnChunkFirst_SoSpawnGetsGroundFast`.

---

## ✅ Done (2026-06-06): Land on moons + real textured station/enemy models
- **E-to-land did nothing on some bodies because moons were rejected.** `HandleTravel` only allowed
  `CelestialKind.Planet`, but the space view offers landing on **planets and moons** — flying to a moon
  showed "Press E to land" and the server silently rejected it. Now both are accepted. Test
  `Travel_LandsOnAMoon_NotJustPlanets`. (Worlds generate on demand in `LoadWorld`, so an unvisited body was
  never the blocker.)
- **Asteroids/stations/enemies — textures.** Asteroids were already stone-textured; stations + space enemies
  were flat colour cubes. They're now **real textured multi-cube models** (mirrors the ship), built from the
  bundled block textures: a station (iron hull hub + glass viewport collar + docking arms/pods + solar wings
  + beacon, slow spin), a drone (carbon body + glowing eye + pods), a UFO (titanium saucer + glass dome +
  underside lights, fast spin) and a cruiser (iron hull + glass bridge + twin glowing engines). `Spin` got a
  `Configure(axis, speed)` for the fixed rotations. No external art needed.

---

## ✅ Done (2026-06-06): Only real bodies in space — every planet is landable
You couldn't land on a planet you flew to because it was a **decorative** sphere (`planet2`) not in the
landable set — the land prompt never appeared. Removed the two decorative planets entirely; the space view
now renders **only real celestial bodies**: the planet you launched from (rendered "below" you, landable →
E returns home) and the system's actual planets/moons at their scaled orbit coords (from the star map), all
lit + textured + landable via a shared `SpawnBody`. `EnterSpace` now also `SendStarMap` so the system's
bodies are always available when the space scene builds. Per-body land approach already shows "Press E to
land" just outside each body's keep-out.

---

## ✅ Done (2026-06-06): Safe launch + reach the system — combat was killing you on spawn
The real cause of "I take continuous hull damage then suddenly respawn on the planet":
- **Hostile drones/UFOs spawned ~25u from the launch point**, inside `ShipEngageRange` — so a combat preset
  (coop-survival/dangerous/pvp) hammered the ship the instant it launched, destroyed it, and `DisableShip`
  respawned you at base. They now spawn **far** (≥150u) → launching/docking is safe, combat is opt-in.
- **Ship weapon range was measured from the origin** (0,0,0), not the ship — so you literally couldn't
  shoot anything you flew out to. Now measured from `instance.ShipPosition`; engage range tightened 85→70 to
  sit near the weapon ranges (40–70).
- **System flight reachable:** with combat no longer spawn-camping you, the system's other planets (placed
  at their scaled orbit coords, ~156u+ out) are flyable + landable again. Per-body land approach (radius +
  margin + band) shows "Press E to land" just outside each body's keep-out, and the **launch planet is now
  landable too** (E returns you home) so there's always a planet to land on, not only the station.
- Tests updated (drones now spawn far): `NpcDrones_DisableShip…`, `ShipCannon_DestroysDrone…` fly the ship
  to the drones first; `DistantHostile_DoesNotDamageShip_UntilWithinRange` covers the range gate.

---

## ✅ Done (2026-06-06): No fly-into-planet / auto-land; E-land prompt actually shows
Follow-up to the E-landing change:
- **Keep-out barrier around every body** (landables + the two decorative planets): the ship slides along a
  body's keep-out sphere instead of flying into it (radial push-out each frame in `UpdateCruise`). No more
  "flew into the planet / dropped onto it" — you stop at the approach distance and press **E** to land.
- **Prompt + E priority by distance:** the land/dock prompt now shows whichever you're **closest** to (was
  station-always-wins, so the planet prompt never appeared when a station sat near the body cluster). E acts
  on the closer of the two.
- **L = return to the launch body only** (`SendLeaveSpace("")`) — it no longer lands you on a nearby body
  (that's E now), so there's no surprise drop. Confirm reworded to "Return to the surface…".
- Note: a likely extra cause of "auto-landing near planets" was the ship being **disabled by off-screen
  drone fire** (now range-gated) → `DisableShip` forces you out of space onto the base.

---

## ✅ Done (2026-06-06): Land on a planet with E (like docking a station)
Flying near a planet/moon now shows **"Press E to land on \<name\>"** and **E lands** there — the same
proximity → E flow as station docking (was "Press L" + an Enter/Esc confirm). In `SpaceView.UpdateCruise`,
E is the context action: dock a station you're next to, else land on the body you've flown up to
(`SendLeaveSpace(_landTargetId)`, server flies the descent). **L** stays as the "return to the body you
launched from" shortcut. Reworded `ui.space.land_prompt` + `ui.space.controls` (en/de).

---

## ✅ Done (2026-06-06): Space follow-ups — no relentless shake, a real sun disc
- **The "ship being shaken" + red screen was continuous damage feedback.** `incoming` ship damage summed
  **all** hostiles in the instance with **no range check**, so a distant/off-screen drone plinked the ship
  every tick → permanent `_shake` + red `_hit` overlay. Now hostiles only fire within `ShipEngageRange`
  (85u), so flying clear stops the damage + recharges the shield. Client feedback is now **proportional and
  set-not-accumulated**: chip damage is a faint rumble, a real hit is a sharp jolt (no more max-pinned
  shake/flash). Test `DistantHostile_DoesNotDamageShip_UntilWithinRange`.
- **The sun now reads as a sun.** The single additive billboard in the raw star colour looked like a vague
  red/blue wash (and an orange-red star is in the palette). It's now layered — a coloured corona + a
  **white-hot core** — so the star is a clear bright disc in any colour; the lens flare is much subtler +
  desaturated (no red screen wash).

---

## ✅ Done (2026-06-06): Station/space polish wave — undock, NPCs, sun, windows
- **Undock returns to flight:** leaving a station now relaunches you into the **space instance (ship view)**
  around the orbited planet instead of dropping you onto the surface (`LeaveStation` restores the planet
  world underneath, then `EnterSpace`). Test `LeaveStation_UndocksBackIntoSpaceFlight`.
- **Station crew stands on the deck:** station NPCs snapped their feet to the floor grid (the marker sits
  +0.5 centred in the air cell) — they no longer float. Settlement NPCs already snapped (verified). Test
  `StationCrew_StandsOnTheDeck_NotFloating`.
- **Ship no longer "wobbles" in space:** the only continuous motion on the ship was the thruster exhaust
  flicker (`Sin(t·28)·0.12`, ~4.5 Hz) — tamed to a slow gentle shimmer; the launch animation itself is a
  clean ease.
- **The system sun, in its colour:** space view now renders the star as a bright additive billboard tinted
  by `Environment.SunColor`, plus a **screen-space lens flare** that blooms as you turn to look into it.
- **Real station windows + a view out:** the viewport band is now 2 blocks tall (proper windows, see-through
  via the transparent pass); a new `StationBackdrop` shows the **orbited planet + the sun** outside while you
  walk the station, so looking out a window shows the solar system (stars already showed through). Editor
  already carries the glass "Viewport".

---

## ✅ Done (2026-06-06): Station safety + see-through fields + a twinkling starfield
- **Stations are peaceful:** void worlds now hard-skip `TickEnemies`/`TickCreatures`/`TickFlora`, and
  `ResetWorldRuntimeState` clears the species roster on every world switch — so no hostile aliens or
  wandering wildlife ever spawn aboard a station; only the peaceful crew NPCs live there.
- **No more holes to space:** the hangar mouth is glazed with a new **`force_field`** block (unbreakable,
  `mineable:false`) instead of an open air gap — you can't walk out into the void.
- **Real see-through rendering:** glass + force fields render in a second, alpha-blended chunk submesh
  (`Spacecraft/BlockAtlasTransparent`) with transparency-aware face culling, so station **viewports and the
  hangar energy field actually show space** (and stars) through them. Force fields glow cyan. Added to the
  station editor palette ("Energy field").
- **Twinkling starfield:** `Starfield` + `Spacecraft/Starfield` shader draw an additive star dome behind the
  world that follows the camera and fades in for space, airless skies, station interiors (seen through the
  windows) and **planet nights**, fading out toward noon. Each star pulses on its own phase.

---

## ✅ Done (2026-06-06): Station boarding fall-through — the real root cause
The "dock and fall **immediately** into space" bug survived every prior fix because the settle-freeze was
being released by a **ghost collider**. `OnWorldReset` removed the old world's chunks with `Destroy(go)`,
but Unity defers `Destroy` to frame-end — so the old planet chunk colliders still existed the same frame the
player snapped to the station spawn. The freeze's downward raycast hit one, decided "ground is here",
released instantly; then frame-end destroyed those colliders and the player dropped through into the void
before the station floor had streamed. Fix: `OnWorldReset` now `SetActive(false)` on each chunk **before**
`Destroy` (deactivation is immediate), so the freeze raycast can't see stale colliders and holds the player
until the real station floor chunk streams in. Also hardens normal planet-to-planet travel.

---

## ✅ Done (2026-06-05): Space stations as their own locations
Boarding a station is now a real world transition (the proven `WorldReset` path): each station is its **own
void world** (space sky, no weather/clouds, lit interior, life support, NPCs), so you land **inside** the
station floating in space instead of falling through to the planet. Implemented S1–S7 of
**[docs/STATION_AS_LOCATION_PLAN.md](docs/STATION_AS_LOCATION_PLAN.md)** (`PlanetType.Void` + all-air gen,
`orbital_station` type, `LoadWorld` skips surface content for void worlds, `BoardStation`/`LeaveStation`
travel in/out, station NPCs per-world). Tests: `BoardStation_PutsPlayerInOwnVoidWorld_OnSolidGround_…`,
`LeaveStation_TravelsBackToThePlanet`, `VoidPlanet_GeneratesEmptySpace`.

---

## ✅ Done

### Foundation & server (M0–M20)
- .NET solution; shared data-driven content model + bilingual i18n; deterministic procedural universe
  (systems → bodies) from seed; SQLite persistence; LiteNetLib + loopback + MessagePack codec.
- Authoritative game server: tick loop, mine/place/craft/blueprint validators, admin API, self-hosting
  publish scripts.
- Game modes (Survival/Creative) + authoritative `GameRules` + presets; death/respawn at Medbay
  heal-tank + salvage capsule; admin roles + logged cheats.
- Mission system (system + player missions, reward depot); per-world content packs (missions/blueprints).
- WebSocket gateway + composite transport + web portal; optional Python AI mission backend (off by default).
- Personal landing zones; ship docking (request/accept/undock handshake, guest access, undock-on-disconnect).
- Space flight + PvE combat slice: ship hull/shield, ship-weapon blueprints/modules, local space
  instances, NPC drones + asteroids, planet enemies — all rule-gated, no permanent ship loss.
- Client shell: AppShell phase machine (splash → menu → settings → loading → in-game), local settings.

### World & exploration
- Seed world-gen: terrain, caves, ores (depth-banded veins), flora, multi-biome planets, 8 planet types
  (`data/planets.json`); atmosphere/oxygen rules; per-system suns.
- **Hyperspace travel** between systems (gated by a `jump_generator` module).
- **Station + settlement template world-gen** — generated worlds pick hand-designed station/town/village
  templates from a pool (~35% chance) when present, else stay procedural.
- Space stations: boarding, interiors + NPC populations (scaled by size tier), tractor beam + cargo,
  radar scanner tiers + named stations + location readout.

### Gameplay systems
- Mining/placing/crafting/blueprints; tech progression; trade (player↔player atomic swap) **with client
  panel**; scanning (handheld + ship) **with HUD readout**; wreck repair (server + progress UI).
- Survival: health/oxygen/hunger/energy; suit lamp; flora harvest drops (e.g. berries); creatures
  (habitat-gated spawns, temperament, visible attacks).
- **No building inside the ship**; ship interior stays a fixed hollow structure on landing.

### Client / UX
- In-game HUD + Tab menu (Inventory/Map/Missions/Character/Space), modern uGUI theme, UI sounds.
- **Chat** (open/type/send + scrollback) → `ChatIntent`/`ChatMessage`; `/bump` debug snapshot command.
- **World map / planet overlay** (M): top-down fog-of-war terrain, player/ship/station markers, waypoints.
- **Day/night clock** on the planet HUD; weather (IMGUI rain + lightning, density-scaled), silenced in caves.
- Singleplayer **save selection / new world** picker.
- First-person viewmodel + held tool; networked gear/held items; **avatar reflects equipped gear**
  (helmet/chest/legs/pack/lamp); procedural player + creature animation.

### Editors & tooling (full suite)
- **Ship editor**, **Avatar/skin designer**, **Station editor**, **Town editor**, **Item & recipe editor**,
  **Material editor** — each in the Editors submenu, each exporting a JSON bundle.
- Python merge tools fold bundles into `data/`: `merge_ship.py`, `merge_structure.py`, `merge_recipe.py`,
  `merge_material.py`. Material editor paints a 64×64 tile, sets mining/look/spawn (world-type targeting),
  and its look is **data-driven** via optional `BlockDefinition` fields (Gloss/Metal/Emission/Color).

### Audio & graphics
- Fully procedural audio (synthesised cues/ambiences/loops; the game is audible with zero recorded assets);
  hyperspace + boarding hooks; spatial creature voices.
- Lit block shader (per-material gloss/metal/emission, normal-mapped atlas); **per-face skylight** so caves
  + interiors go dark except lamp/emissive light; camera feel (head-bob, FOV kick, landing shake); denser
  starfields + drifting cloud shell on the menu planet; ship + station window panes.

---

## 🔧 Open / pending

### ✅ Done (was "partial — client polish/VFX")
1. ✅ **Jetpack (done).** Craftable item + blueprint + recipe (`jetpack`, workshop, gated). Hold Space in
   the air to thrust up (`PlayerController`); server-authoritative suit-energy drain + force-off when empty
   (`SetJetpackIntent` → `HandleSetJetpack`/`TickJetpack`), suit energy recharges aboard ship. VFX: twin
   thrust flames + a looping thrust hiss (`ClientAudio.JetClip`), shown on remote players too (presence
   `Jetpacking`). Test `Jetpack_DrainsSuitEnergy_WhileActive_AndRejectsWithoutOne`.
2. ✅ **Weather (done).** IMGUI rain wash + lightning (`WeatherFx`), storm/rain ambience bed + thunder
   (`ClientAudio`, cave-silenced), 3D in-world rain falling around the player + storm fog/view-distance
   scaling (`WeatherFx3D`). All gated on open sky + intensity-scaled.
3. ✅ **Animation pass (done).** NPCs now play ambient **work gestures** (theme/role-paced arm swings —
   miners chip often, settlers/builders place, vendors gesture occasionally; `NpcView.WorkCadence`).
   Creatures got a **head pivot** with **per-temperament idle gestures** — passive graze (head dips to the
   ground), skittish alert (head snaps up + looks around), hostile lunge (sharp aggressive thrust), asleep
   rests low; plus idle breathing and a quicker tail on hostiles (`CreatureAnimator`/`CreatureBuilder`).
4. ✅ **Weapon/equipment VFX (done).** Beam/tracer, muzzle flash, impact sparks, scanner pulse — plus
   **kinetic projectile bolts** (gauss/slug fly to the target, trail sparks, burst on arrival), **melee
   slash arcs** (short-range weapons/fists sweep a fading arc, whiff included) and a **visible suit-lamp
   cone** (a faint warm translucent light shaft on the `Spacecraft/Cloud` shader, parented to the camera).
   Weapon effect picked by held item in `PlayerController.HeldWeaponFx` (`WeaponFx.Projectile`/`MeleeArc`).

### ✅ Playtest fixes (2026-06-04)
A large wave of playtest bugs, all fixed + committed:
- **Space view:** asteroids textured + slowly tumbling; reddish tint (planet day/night + biome grade leaked
  in) neutralised; on-foot hotbar + hand viewmodel hidden in space.
- **World/feel:** crushed-dark shadows lifted; monsters spawn on the surface 9–13 blocks away (not buried /
  on top); mining slowed; blocks can't be placed in the cell you stand in.
- **Ship:** the −X side was mineable (wrap-canonicalisation mismatch in the ship-stamp protection) — fixed;
  respawn snaps you to the ship heal-tank; **boarding a station no longer drops you on the planet** (client
  never moved to the station spawn).
- **NPCs/structures:** NPCs got faces (eyes) + stand on the ground; settlement + station doorways widened
  from 1×2 to 2×3.
- **UI:** Esc shows a DE/EN quit confirmation and returns to the **main** menu (was the editor submenu);
  hotkeys (M/Tab/T/K/U) no longer fire while typing in chat; scanner shows the localized material name;
  "suit" inventory filter includes suit gear.
- **Vendor trading (new feature):** E next to a vendor (or aboard ship) opens the **Market** category to
  barter resources for goods. Possible extension: vendor *selling* (goods → resources) + a priced shop UI.

### Landing + docking — reviewed (2026-06-04)
End-to-end trace done (launch/land, same-system travel, hyperjump, space-station boarding, player↔player
docking). Findings:
- **Fixed:** boarding a space station was a one-way trip — the client never sent `LeaveStationIntent`
  (server handler existed). Added `SendLeaveStation` + a **U = leave station** prompt while boarded.
- **Done:** **landing confirmation** (L opens an Enter/Esc prompt instead of dropping instantly) and a
  **station dock-approach animation** (`Phase.Boarding`: the ship flies in + fades before boarding).
- **Remaining polish (cosmetic):** **player↔player docking** is still an instant logical transition with
  no animation (a dock-approach there would match the station boarding feel).

### Recently shipped (was partial → now done)
- **Disassemble button** — Inventory detail pane shows a Disassemble button + recovered-parts preview,
  gated on a workshop (`CraftingTechShipUI.DetailInventory`).
- **Wreck repair hint** — the HUD wreck panel now tells the player to aim at a breach + press **R** and
  lists the blocks still needed (`WreckRepairStatus.Needs`).
- **Menu closes on launch/jump** — the gameplay menu auto-closes when a launch/landing flight sequence
  begins (planet or station → `SpaceViewActive`) or a hyperspace jump starts (`HyperjumpStarted`), so the
  launch/warp animation is visible (`GameMenu`).
- **In-game admin console** — admin cheats are now typed in chat (`/give /tp /tpp /settime /setweather
  /fly /god /instant /ai`, `/help` lists them). The client parses them → `AdminCommandIntent`; the server
  still gates on `IsAdmin` + `CheatsAllowed`. `/bump` stays a chat message.

### Multi-world + system-scale flight — planned (not started) ⭐
**Firm requirement:** in multiplayer, players can be on **different planets / different star systems**
simultaneously, plus **fly between planets in a system and land on any of them**. This makes the
multi-world core (per-player worlds + per-player ship) mandatory, with a system-scale flight layer on top.
Full phased design (P1 body positions → **P2 WorldManager indirection, the keystone** → P3 multi-world +
per-player location → P4 per-player ship → P5 system flight + land-anywhere → P6 inter-system → P7
cross-world MP polish) in **[docs/MULTIWORLD_AND_SYSTEM_FLIGHT_PLAN.md](docs/MULTIWORLD_AND_SYSTEM_FLIGHT_PLAN.md)**.
Key enabler found: persistence is already **location-scoped** (the save DB can hold many worlds; only the
in-memory single `_world` blocks it). **Decision: one ship per player, no crew.**

**Progress:**
- ✅ **P1** — seeded system-space coordinates on every body (`CelestialBody`/`NetBody`/`UniverseGenerator`),
  deterministic, existing universes unchanged (`0e4162c`).
- ✅ **P2** — `WorldManager`/`LoadedWorld` seam; the active world is routed through it, behaviour-preserving
  (`f45bd41`).
- ✅ **P3a** — relocated the per-world runtime state (fauna/enemies/npcs/flora/fluids/containers/
  structures/landing zones) into `LoadedWorld` via forwarding properties to `_worlds.Active` (`e4e251a`).
- ✅ **P3b** — relocated the remaining per-world stragglers (settlement/wreck stamp scalars, creature/
  enemy/npc/fluid sim timers) into `LoadedWorld`. Weather + time-of-day stay global for now (all resident
  worlds share the sky — a known temporary limitation, refined in P7). Behaviour-preserving (`be5d48e`).
  **Every per-world gameplay system now has isolated state** — the foundation is complete.
- ✅ **P3c-1** — multi-world cache scaffolding: `WorldManager.GetOrCreate/Loaded/IsLoaded/Unload` + settable
  Active cursor; per-session `CurrentLocationId` (set on join + travel). Behaviour-preserving (`c0474ae`).
- ✅ **P3c-2a/b** — relocated weather/environment state per-world; per-world init reads the Active world,
  not global `_meta` (`bf8be4c`, `b450491`).
- ✅ **P3c-2c** — restructured the central `Tick` to iterate occupied worlds with the Active cursor; added
  `JoinedInActiveWorld`/`BroadcastToWorld`/`OccupiedLocations`/`SetActiveWorld`; scoped chunk streaming +
  presence + entity + block-change broadcasts per world; `OnPayload` sets Active to the sender's world
  (`e283a88`).
- ✅ **P3c-2d** — **per-player travel** (`HandleTravel` moves only the requester via cached `LoadWorld`,
  per-player `WorldReset`, unload-on-empty); join/disconnect world-scoped; test
  `TwoPlayers_OnDifferentPlanets_HaveIsolatedWorlds` (`3849ccb`).

**✅ P3 DONE — two players can now be on different planets / systems at once with isolated terrain, edits,
fauna and weather (261 tests).**

**✅ P7 DONE — cross-world MP polish (263 tests).** Four parts:
- **Per-player ship-stamp** (`a*`) — two players on one planet get **separate ships at distinct start
  points** (ship structure is per-player in each world; `StampShip` anchors at the served player's own
  landing zone; protection/interior cover everyone's ships). Test
  `TwoPlayers_GetSeparateShips_AtDistinctStartPoints`.
- **Position-based day/night** (`d1debb0`) — world X is a longitude: `GameBootstrap.LocalTimeOfDay` shifts
  the global day fraction by `playerX / 6000`, so one player can be on the **day side** while another is on
  the **night side** of the same planet (sky/clouds/HUD clock use local time).
- **Per-biome weather + larger biomes** (`a6a88dd`) — weather is per **biome** (a stormy biome rains while a
  clear one stays sunny), shifted by a persistent per-biome offset; the env broadcast is per-player. Biome
  noise scale 140 → 360 so each biome is a large region.
- **Star map shows the party** (`afd4ad9`) — `StarMapData.Players` lists who is on each body ("◈ Alice, Bob").

**🎉 The whole multi-world + system-flight plan (P1–P7) is complete.**

**✅ P6 DONE — inter-system travel via hyperspace jump.** Jumping between systems is the existing
`TravelIntent` + `jump_generator` from the star map (Tab → Map), reachable mid-flight. Fixed the rough
edge: jumping *from* flight no longer plays the old planet's landing descent under the warp — `SpaceView`
tears down on `HyperjumpStarted` and the full-screen warp covers the transition; the ship holds position
while the map is open. So you **fly within a system** and **jump between systems** (`ec59a31`).

**✅ P5 DONE (262 tests) — system-scale flight + land anywhere in the system.** In space you now fly
between the system's planets/moons (rendered at their P1 system coordinates, relative to the body you
launched from; the flight clamp spans the system). The nearest body in approach range is the land target —
the HUD prompts "Press L to land on <name>" and the confirm names it; `LeaveSpaceIntent.DestinationBodyId`
makes the server land you there (per-player travel; same-system = free). With nothing in range, L returns
you to where you launched. **Inter-system travel stays the hyperspace jump** (star map + `jump_generator`),
per the requirement. (`8fdfcdc` server, `c582afb` client.)

**✅ P4 DONE (merged to `main`, 261 tests) — one ship per player, no crew.** Each player owns their own
**fleet (multiple ships) with exactly one active ship**, created/loaded on join, stamped into their world,
persisted per player. Implemented with a single-threaded **ship cursor** (`_current`): `_ship`/`_ships`/
`_activeShipId` resolve to the served player; `OnPayload` + the public entry methods (`HandleTravel` top,
`CraftShip`, `Craft`, `RequestDock`) `Serve(session)` first; combat-stat caches recompute on cursor set.
Persistence is per player (`ship_<playerId>`). Built on branch `p4-per-player-ship-wip` then merged.
**Remaining edge (→ P7):** two players on the *same* planet share that world's ship-stamp state (anchor/
heal-tank); fine for different-planet play (the requirement), needs per-player ship-stamp for shared
worlds.

**Original P4 plan:** the fleet (`_ships`/`_activeShipId` in `GameServerShips`) is
currently global; make it per-player via a **session cursor** (`_current`) so `_ship`/`_ships`/
`_activeShipId` resolve to the served player's ship (mirrors the world Active cursor; single-threaded).
Sub-steps: **P4a** add per-player fleet to `PlayerSession` + the `_current` cursor + route `_ship`/`_ships`/
`_activeShipId` through it (recompute the combat-stat caches `_shipHullMax/Shield/Regen/Radar` on cursor
set); **P4b** move the ship lifecycle from Start (one shared ship) to per-join (each player loads/creates
their own ship; persistence keyed by player id); **P4c** set `_current` in `OnPayload` + before per-player
StampShip in join/travel + per-player in the space-combat tick; **P4d** untangle the test/public accessors
(`server.Ship`/`OwnedShips` → first joined player) + a two-player fleet-isolation test. Ship-stamp state
stays per-world (fine while each occupied world has one player; shared-world multi-ship is P7).

_(Done 2026-06-06 — see the "Starter weapons + ship weapons finished (W1–W5)" entry above.)_

### Orbital bodies in the planet sky — planned (not started) ⭐ (2026-06-06)
On a planet's surface, the **other nearby bodies of the system** (neighbouring planets/moons) and the
**space stations orbiting this planet** should be visible as **small objects in the sky** — like a moon you
can see by day + night. Cosmetic, client-side (mirrors `Sky`/`Starfield`/the sun disc):
- A `SkyBodies` component that, only while on a planet surface (not in space / not boarded), places small
  **lit sphere billboards** for the system's neighbours and small **station icons** for stations orbiting
  the current body, in the sky dome — direction derived from the star map's relative system coords
  (`Game.StarMap`, already client-side), distance scaled to the far plane, following the camera like the
  starfield. Tint/size from each body's planet type; stations read as tiny metallic specks/cross shapes.
- Visible day + night (a touch brighter at night); a very slow drift so they feel like they orbit.
- Phasing: **O1** render neighbours + stations from the star map; **O2** slow orbital drift; **O3** per-type
  look + stations-of-this-body shown nearer/bigger; **O4** optional labels on look/scan.

### Doors — ✅ shipped for settlements (see the Done entry above); stations/ship still open
**Settlement doors are done** (D1–D5 — slide for towns/cities, hinge for villages/hamlets, with the three
ElevenLabs SFX). **Still open:** auto-doors for **orbital stations + the ship** — their doorways are cut as
air but don't yet emit `door_slide` markers; the `GameServerDoors` registry already accepts them once the
station/ship stamp adds the markers (plus station/ship editor palette entries). Original plan kept below for
that remaining work:
- **Sci-fi sliding doors** — auto open/close: the server opens them when a player is within range and
  auto-closes them after a short delay. For **stations + cities/towns** (and the **ship**).
- **Hinged "normal" doors** — manual: press **E** to toggle. For **villages/hamlets**.

Doors are **markers**, not voxel blocks (a 2×3 doorway opening stays air; the door entity fills it; its
collider closes the gap when shut). Phased plan:
- **D1 — server `GameServerDoors`:** a per-world `ServerDoor { Id, Type(slide/hinge), Pos, Facing, Open,
  AutoCloseTimer }` registry built from structure markers on stamp; `TickDoors` auto-opens slide doors near
  players + auto-closes after a delay; `HandleDoorInteract` toggles a hinge door the player faces;
  broadcast `DoorList`/`DoorStateChanged` per world (via `BroadcastToWorld`). Cleared in
  `ResetWorldRuntimeState`.
- **D2 — client `DoorView`:** renders each door from `DoorList`; slide = two panels that swoosh apart,
  hinge = a leaf that swings ~90°; a `BoxCollider` enabled while ~closed, disabled while open; sci-fi
  swoosh vs. wood creak SFX (`ClientAudio`). Mirrors `NpcView`.
- **D3 — generators place doors at doorways:** `StationGenerator.CutDoor` → a slide-door marker; settlement
  generator picks **slide** for city/town, **hinge** for village/hamlet; `StationGenerator`/ship hull
  doorways → slide. Keep the opening air so the entity shows through.
- **D4 — editors:** add door markers to the palettes — **station editor** (slide), **settlement editor**
  (both slide + hinge so the designer picks; generator still auto-picks by tier), **ship editor** (slide).
  Markers flow through `StructureTemplate` cells (kind="marker", id="door_slide"/"door_hinge") → D1 registry.
- **D5 — localization + tests:** en/de names; tests: a slide door opens when a player steps within range +
  auto-closes; a hinge door toggles on interact; the collider blocks passage while closed.

### ✅ Done (2026-06-07): species/flora/colour/naming overhaul
A multi-phase feature requested 2026-06-07 — random per-world flora & fauna species with generated looks +
names, wilder colours, uniform per-planet flora hue, and full OpenAI/ElevenLabs asset coverage. The user chose
**"work the plan in order"** and **gave blanket approval for paid asset generation** (OpenAI textures +
ElevenLabs sounds — no further per-batch confirmation needed; keys are in `tools/ai-assets/.env`, run via `uv`).

- ✅ **Phase 1 — per-system star colour** (done 2026-06-07) — replaced the 5-swatch sun palette with
  `StarColor(system)`: a weighted hot→cool stellar ramp (blue-white→white→yellow→orange→red) blended by a
  second hash, so every system gets a distinct sun tint. Already shared between the planet surface + space +
  station views via `WorldEnvironment.SunColor`; unified the env-null fallback too. (`0d988a9`)
- ✅ **Phase 2 — flora re-tint engine** (done 2026-06-07) — the server picks a **per-planet flora base hue**
  (`FloraColor`: green-dominant palette with rarer brown/pink/purple/amber, deterministic from seed+planet),
  broadcast via `WorldEnvironment.FloraTint`. The mesher tags flora vertices with a flag in `TEXCOORD1.y`
  (`IsFloraBlock` — `flora_*` only; trees keep their colours); `Sky` feeds the hue to the block shader as the
  global `_Sc_FloraTint`; `BlockAtlas.shader` **desaturates the flora tile to its luminance and re-tints it**
  by the hue. No tile regen + no re-mesh needed (it's a live shader global, off in space/stations). Grayscale
  flora tiles (Phase 5a) become optional polish now. Test: `FloraTint_IsAColourfulPlanetHue`.
- ✅ **Phase 3 — FloraGenerator + names** (done 2026-06-07) — a new `FloraGenerator.GenerateRoster(planet,
  seed)` derives a per-world flora roster: each archetype block (`FloraCatalog`) becomes a named species
  (`NameGenerator.Flora`) with an edible/**toxic** trait, deterministic from seed+planet. The server maps
  block→species (`FloraSpeciesForBlock`); **scanning a plant now shows its coined name + Edible/Toxic** (the
  block branch in `GameServerScanning`). Per-world identity = the Phase-2 hue + the coined names + toxicity
  over the shared archetype blocks (no dynamic blocks needed). Tests: `FloraRoster_NamesEveryArchetype_…`,
  `FloraRoster_IsEmpty_OnABarrenWorld`. ✅ **toxic flora now bites back** (2026-06-07): harvesting a toxic
  species yields **`toxic_berries`** (a harmful consumable, `consumeHealth -18`) instead of edible berries —
  `BreakBlockAt` remaps the drop by the world's flora species, so the scan's Edible/Toxic warning has teeth.
  Test: `ToxicFlora_YieldsToxicBerries_…`. ✅ **per-world archetype subsetting** (2026-06-07): each world now
  activates only ~60% of the flora forms (`FloraSpecies.Active`), so worlds differ in plant *shapes*, not just
  hue/names — with an `EnsureCoverage` pass that force-activates the minimum so every land host surface + the
  seas keep ≥1 active species (no bare biome). `WorldGenerator.ResolveFlora` builds the per-surface pools +
  kelp/lily gating from the active subset; placement + scan share `_meta.Seed` so they always agree. Test:
  `FloraRoster_ActivatesASubset_ButKeepsFullCoverage`.
- ◑ **Phase 4 — fauna polish + names** — ✅ **names + colours done** (`3c1500c`): a shared `NameGenerator`
  coins per-species names (shown on scan as the readout subject; rides `NetCreature.Name`); `PickColor` now
  makes ~half of species vivid exotics (HSV hues → pink/violet/yellow/teal). ⏳ **still to do:** per-**biome**
  species affinity (spawn species by the player's current biome, not just per-planet). ✅ **done 2026-06-07**:
  `CreatureSpecies.BiomeAffinity` (assigned per species from the planet's biome count; -1 on single-biome
  worlds); `TrySpawnCreatureNear` now does a biome-first two-pass spawn (native species preferred, any as
  fallback) so a multi-biome world shows different fauna per region. Test:
  `Roster_AssignsBiomeAffinity_SpreadAcrossAMultiBiomeWorld`.
- ◑ **Phase 5 — asset generation (paid, approved)** — ✅ **(a) flora textures complete**: generated +
  bundled `flora_kelp` + `flora_lily` (OpenAI), so **all 15 `flora_*` blocks now have tiles** (audit confirmed
  flora 15/15 + creature hides 12/12 covered; only special blocks — lights/force-field/ladder/stairs/data-cache
  — use BaseColor, by design). Grayscale regen is unnecessary — the Phase-2 shader re-tints by luminance, so
  the existing colour tiles already work. ✅ **(c) more creature calls**: generated 6 new ElevenLabs signature
  calls (trill / click / rumble / bellow / hiss / chitter) → `CreatureView.Calls[]` now has **12** (audio
  106 → 112), so a world's fauna sounds far more varied. ⏳ **still to do:** (b) more creature hides only if
  new body parts are added.

Also still in the backlog: **multiplayer player-name reservation**.

Everything builds + **302 tests pass** as of `0d988a9`.

### Planned — requested 2026-06-06 (für später)
- ✅ **Mineable water/lava (with the mining beam) + source logic** (done 2026-06-06) — water/lava are now
  `mineable` but `requiredTool: drill` + `minToolTier: 3`, so **only the mining beam** clears them (the
  basic/titanium drills can't); each drops its placeable item. Removing a fluid cell calls `OnFluidRemoved`,
  which **wakes the surrounding fluid** so a body refills the hole — worldgen sea cells act as full sources,
  so you can only drain a **finite pool** by taking its last feeding cells. A settle guard in `TickFluids`
  (`HasAirNeighbor`) lets calm full cells go dormant, so a big sea doesn't keep every cell active. Tests:
  `WaterAndLava_AreMineable_OnlyByTheMiningBeam`, `Water_BasicDrillCannot_MiningBeamCan`,
  `WaterBody_RefillsAMinedHole`.
- **Multiplayer player-name reservation** — a player name must be **reserved on the server** so two clients
  can't collide on the same name/identity. (Today join takes any name.) Requested 2026-06-06.
- ✅ **Creature swim undulation + dive** (done 2026-06-06) — `CreatureBuilder` now hangs every part off a
  `BodyRig` pivot; for aquatic species (`Habitat == "Water"`) `CreatureAnimator` undulates that rig (a yaw
  weave that lags the tail beat + a counter-roll + a slow vertical glide), beats the tail faster/wider, and
  drops the legs to a fin-flutter instead of a stride. Server **dive**: `AdjustHabitatHeight` porpoises
  swimmers up and down the water column over time (`sin(_creatureClock·0.22 + pos)`), clamped to the column,
  instead of holding one depth. (Earlier: terrain/water-following Y — land/lava walk, fliers hover.)
- ✅ **Underwater sound filter** (done 2026-06-06) — `ClientAudio` adds an `AudioLowPassFilter` to its own
  GameObject (which also hosts `ClientMusic`), so when the player's **head sits inside a water/lava block**
  (`HeadInFluid`: sample the block ~1.5 above the player root) the cutoff sweeps to ~680 Hz and the whole
  bed + music muffle; it sweeps back to open above water. 3D one-shots (`At`) get a per-source low-pass while
  submerged too. A low-pass on the AudioListener does nothing in Unity — it must live on the sources, which
  is why this rides the ClientAudio object rather than the camera.
- ✅ **Aquatic fauna + water flora** (done 2026-06-07) — **Water flora:** two new flora blocks `flora_kelp`
  (stalk rooted on the seabed, grows a few cells up, top stays open water) + `flora_lily` (pad on the surface
  water cell); a `StampWaterFlora` worldgen pass seeds them in submerged columns of **water** seas (never
  lava). Both in `FloraCatalog` with seabed/water hosts + an `Aquatic` flag that keeps them out of the
  land surface-flora pool (their hosts overlap dry-land blocks). Mining underwater now refills the hole
  (`BreakBlockAt` wakes adjacent fluid via `HasFluidNeighbor`), so harvesting kelp doesn't leave air pockets.
  BaseColor fallbacks (sea-green) until OpenAI tiles are generated. Tests:
  `AtmosphereWorld_GrowsAquaticFlora`, `AirlessFloraWorld_GrowsNoAquaticFlora`. **Aquatic fauna** already
  worked (water-habitat species generate on water-life planets + spawn in water columns + swim/dive).
  *Follow-up: generate kelp/lily textures (OpenAI) for parity with the other flora tiles.*
- **Flora re-tint per species × biome × planet** — flora should get a **random final colour applied on top
  of the texture** (texture-independent), **uniform per flora species within a biome/planet** (not per
  individual). Creatures already do this (grayscale hides × species colour); flora can't yet because the
  opaque block atlas shader has **no free albedo-tint channel** (vertex colour = gloss/metal/AO; UV1 =
  skylight). Plan: (1) regenerate the flora tiles **grayscale** (OpenAI) so they tint cleanly; (2) add a
  **tint UV channel** (e.g. TEXCOORD2 RGB) in `ChunkMesher` — white for normal blocks, a per-(species,
  planet[, biome]) colour for flora; (3) the `BlockAtlas` shader multiplies albedo by it. Needs the planet
  seed (+ a client-side biome-index for multi-biome worlds) plumbed to the mesher. Requested 2026-06-06.

### Not started / larger future work
- **World wrap (walk around the planet)** — ✅ **W0–W4 shipped**: X is a wrapping longitude (cylinder
  world), so you can walk east and arrive back at the start with a **seam-free** edge (terrain/biomes/caves/
  ore/structures continuous across X = 0 ≡ X = 6000). Seam-free generation via circular-domain noise; server
  + client + persistence + interaction all route X through one wrap helper. **Remaining: W5 (poles)** — bound
  latitude (Z) with an ice-wall/barrier biome. Full plan + progress in
  [docs/WORLD_WRAP_PLAN.md](docs/WORLD_WRAP_PLAN.md).
- **Advanced graphics roadmap** — Built-in RP vs URP decision, god rays, reflection probes, LUT grade.
  Full research in [docs/ADVANCED_GRAPHICS_PLAN.md](docs/ADVANCED_GRAPHICS_PLAN.md).
- **Texture audit** — review/expand item & icon art and creature/NPC texture variety.
- **uGUI theme polish** — remaining icon/symbol pass on the sci-fi theme.
- **Deferred by design** (see [docs/SPACE_COMBAT_CONCEPT.md](docs/SPACE_COMBAT_CONCEPT.md)): PvP ship
  combat, large cruisers/bosses. (Per-player ships shipped in P4.)

---

## Reference docs (committed, under docs/)
Concept/design detail for the larger systems: `STATION_AS_LOCATION_PLAN`, `WORLD_WRAP_PLAN`, `MULTIWORLD_AND_SYSTEM_FLIGHT_PLAN`, `SPACE_COMBAT_CONCEPT`,
`CLIENT_COMPLETION_PLAN`, `CRAFTING_TECH_SHIP_UI_PLAN`, `STATION_SETTLEMENT_EDITOR_PLAN`,
`SHIP_TYPE_EDITOR_PLAN`, `ADVANCED_GRAPHICS_PLAN`, `SOUND_DESIGN`, `SELF_HOSTING`, `AI_MISSION_BACKEND`,
`CLIENT_SHELL_AND_ASSETS`.
