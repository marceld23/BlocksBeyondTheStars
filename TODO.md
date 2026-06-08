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
5. ✅ **Bug/feature — in-space movement: pilot ↔ inside ship ↔ EVA** (done 2026-06-07; in-editor pass pending).
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
   - ✅ **Stage 6 — R1: EVA landing targets (done 2026-06-07).** From an EVA you may board your own ship + dock
     stations (already the only EVA actions); landing on a body is now **restricted to asteroids** by a server
     guard: `HandleLeaveSpace` rejects a land while `InEva` unless the target body's `WorldSizeClass` is
     `Asteroid` (`EvaLandingAllowed`). So planets/moons are never landable on foot — board the ship first. Test
     `Eva_CannotLandOnAPlanet_OnlyAnAsteroid` (324 pass). **Note:** walkable **asteroid bodies aren't generated
     into the flight view's landables yet** (`_landables` = planets + moons only), so asteroid-landing-from-EVA
     is enforced-but-not-yet-reachable; making asteroid bodies landable in flight is a separate task — the guard
     is already correct for when they exist.
   - ✅ **Stage 7 — R3 + R4 (done 2026-06-07).**
     - **R4 — death respawn with the ship.** `RespawnPlayer` now recovers correctly from anywhere: on foot on
       the ship's own world it just snaps to the heal-tank (fast path, no loading screen), but dying in the
       flight view / on an EVA / inside the ship / on a station does a full **world transition** to the ship's
       planet heal-tank (`RecoverToShip` → `LeaveSpace` + `LoadWorld` + `StampShip` + `WorldReset`), so you're
       never left stuck in the flight view or a stale world and always come back **with your ship**. Also:
       **stations now sate hunger** (life support — no more starving while docked). Test
       `DeathOnAnEva_RecoversToTheShip_NotStuckInSpace`.
     - **R3 — the ship stays floating.** Docking a station **while on an EVA** records the ship's floating
       position (`_dockedFromEva`); **undocking returns you to the float next to the waiting ship** (InEva
       restored, ship position restored, no take-off) instead of re-launching. The client keeps `InEva` set
       through the dock so the server knows. Test
       `DockingAStationOnAnEva_KeepsTheShipFloating_AndUndockReturnsToEva`. (326 pass.)
   - ✅ **Stage 8 — R2: multiplayer visibility in space (done 2026-06-07; needs an in-engine 2-player test).**
     Additive (visibility only — the shared `ShipPosition` still drives collision). Server: `SpaceInstance.
     PlayerPoses` tracks each player's pose (pos + yaw + EVA flag, from `InEva`), updated on `ShipMove`;
     `SpaceState.Players` (new `NetSpacePlayer[]`) carries the OTHER players in the instance, filled in
     `SendSpaceState` (already broadcast every space tick). `ShipMoveIntent` gained `Yaw`. Client: the flight
     view + EVA now report yaw (and EVA reports the suit pose); `SyncRemotePlayers` pools a remote avatar per
     player — a ship cube when piloting, a small suit cube on an EVA — placed from the broadcast poses. Test
     `TwoPilotsInTheSameSystem_SeeEachOtherInSpace` (327 pass). *(Single shared ship position for collision is
     a pre-existing limitation, untouched.)*

   **✅ Item 5 complete** (stages 1–8 + R1–R4): pilot ⇄ walkable ship interior ⇄ EVA, oxygen, board/dock,
   asteroid-only EVA landing, ship stays put, death recovers with the ship, and other players are visible in
   space. The Unity client parts across these stages still want one in-editor pass.

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

   ### Item 5 follow-ups (in-space / EVA polish — added 2026-06-07, do before the bigger tasks below)
   These refine the just-shipped pilot ↔ ship-interior ↔ EVA feature (user feedback after testing).
   - ✅ **5b — Player ship exterior textures (done 2026-06-07).** No new assets — `SpaceView.BuildShip` and the
     remote avatars (`BuildRemoteAvatar`) now use the **same block textures as the on-planet/station hull**
     (`LitTex` with `iron_wall` hull, `glass` cockpit, `carbon` engine) + wingtip nav lights, instead of flat
     `Unlit` cubes. Reuses the existing `Lit` material/path (already in the build for stations), so ships read
     as real textured hulls from space.
   - ✅ **5c — Show the ship's entry hatch while on an EVA (done 2026-06-07).** `BuildShip` now has a glowing
     cyan **hatch marker** on the ship's tail (where the voxel hatch is); it glows steady normally and **pulses
     strongly while on an EVA** (`_hatchMat` pulse in `LateUpdate`) so you can spot where to board back in from
     a distance.
   - ✅ **5d — EVA must not fly *into* the ship (done 2026-06-07).** `SpaceView.UpdateEva` now bounces the suit
     off a `ShipKeepOut` (3.5) shell around the parked ship and slides it along the hull (like the station
     keep-out); the board range (11) is larger so you still drift to the hatch and press E.
   - ✅ **5e — "Launch into space" from inside the ship no longer plays the take-off animation (done
     2026-06-07).** In the ship-interior void world (`Game.LoadingPlanetType == "ship_interior"`) the ship-tab
     button now reads **"Take the helm (fly)"** and calls `SendExitShip` (the skip-launch helm path) instead of
     `SendEnterSpace`; on a real surface it stays the normal "Launch into space".
   - ✅ **5f — Ship hatch starts sealed (done 2026-06-07; user feedback).** The hatch's sliding door used to be
     **open on spawn** because you spawn at the heal-tank, which is inside the slide door's 4.5-block open range.
     Added a **per-door `OpenRange`** (`ServerDoor.OpenRange`, used in `TickDoors`); the **ship hatch** now uses a
     tighter **`ShipHatchOpenRange = 1.8`** so it stays **closed where you spawn** and only slides open when you
     **walk right up to it** to leave. Settlement/station slide doors keep 4.5. Test
     `ShipHatchDoor_StaysSealedAtTheSpawn_ButOpensWhenYouWalkUpToIt` (364 green). Server rebuilt.
6. ✅ **Bug — save the player's position per planet** (done 2026-06-07; scope: last planet). When I land on
   another planet, my **position there** should be saved too, so on **loading the save I'm back there** (not
   just the last/home world).

   ### Analysis (2026-06-07)
   **Root cause:** on join/load the player is **always placed on the home/default body**. `HandleJoin`
   (`GameServer.cs:1122,1132`) does `LoadWorld(_meta.DefaultPlanetType, _meta.ActiveLocationId)` and then
   `new PlayerSession(...) { CurrentLocationId = _meta.ActiveLocationId }` — the player's saved **Position** is
   restored, but in the *home* world's coordinate space, so if you saved on another planet you're dropped onto
   the home world at the wrong spot. **Why:** `PlayerSnapshot` (`Snapshots.cs:87`) persists the position
   (X/Y/Z) but **not which body** the player was on — only the **ship** persists `CurrentLocationId`
   (`ShipSnapshot.CurrentLocationId`). The player's body lives on the (non-persisted) `PlayerSession`, and
   the player + ship travel together via `HandleTravel`, so the body is recoverable, but it isn't restored.

   **Plan (pending the scope question):**
   - **Persist the player's body:** add `CurrentLocationId` to `PlayerSnapshot` + round-trip it (saved from
     the session on `SavePlayer`; the session already syncs it). On join, set the session's location from the
     saved value (fall back to the ship's `CurrentLocationId`, then the home body), `LoadWorld` that body's
     type, and place the player at the saved `Position` (still guarded by `EnsureSafeSpawn`).
   - *(If per-planet memory — option B below — also keep a per-body position map and restore it when you travel
     back to a previously-visited body, instead of the landing-zone spawn.)*

   **Decision (user): (a) last planet.** **Implemented:** `PlayerState.CurrentLocationId` is now persisted
   (added to `PlayerSnapshot` + the mappers; `PlayerSession.CurrentLocationId` delegates to it so every
   existing assignment is saved). On join both paths (`HandleJoin`, `AddLocalPlayer`) call `RestoreJoinBody`:
   if the saved body is a real landable galaxy body (planet/moon/asteroid) it loads + places the player there
   at the saved `Position`; otherwise (first join, or a transient station/space save) it falls back to the home
   body. Test `Reload_RestoresPlayerToTheLastPlanet_NotHome` (328 pass). *(Full per-planet memory — option b —
   left for later if wanted.)*

   - ✅ **6b — Auto-save on landing / docking (done 2026-06-07).** New `CheckpointSave(reason)` (wraps
     `SaveAll` + a log) is called when the player lands on a body (`HandleTravel`; `HandleLeaveSpace` return-to-
     surface) and when docking a station (`BoardStation`) — the natural checkpoints, so item 6's per-planet
     position is captured there, not only on the autosave timer / explicit save.
7. ✅ **Bug — creatures chase forever + spawn only at the ship (done 2026-06-07).** Creatures seem to **follow
   the player constantly**. After a while, pursuing/attacking creatures should **leave the player alone**. Also:
   creatures **shouldn't only spawn at the ship** but be **distributed across their biomes**.
   - **(a) Give-up leash (done).** `CombatEntity` gained `ChaseTimer` + `GiveUpTimer`. In `MoveCreatures` an
     aggressor (Aggressive/PackHunter, or a provoked territorial one) within aggro range accumulates `ChaseTimer`;
     past `CreatureChaseGiveUpSeconds` (**7 s**) it gives up — sets `GiveUpTimer` = `CreatureGiveUpCooldownSeconds`
     (**15 s**), during which `Step` gets `null` (it wanders off) **and** the damage loop skips it (no biting).
     `ChaseTimer` decays when not chasing; after the cooldown it can re-engage. New `WithinAggro` mirrors `Step`'s
     2D test. (User pick: "Kürzer & milder ~7 s → ~15 s".)
   - **(b) Spread spawns (done).** `SpawnRing` widened from a tight 7–13 block circle to **~18–45 blocks**,
     scattered across mixed radii/angles (inner/mid/outer bands), so fauna populates the surrounding biomes out of
     immediate sight instead of clustering on the ship. Biome-native gating kept. (User pick: "weiter weg +
     verstreut".)
   - Tested: new `Aggressor_GivesUpAfterChasingTooLong_ThenItsCooldownDecays`; full suite 329 green.

   ### Analysis (2026-06-07)
   - **(a) Chase never ends.** `MoveCreatures` (`GameServerCreatures.cs:290`) feeds the nearest player into
     `CreatureBehaviour.Step`, which makes an aggressor/pack-hunter move **toward** any player within
     `CreatureAggroRange` (10) — with **no give-up timer**. So while you stay within ~10 blocks an aggressor (or
     a provoked territorial one, `ProvokeTimer`) keeps coming + attacking (the damage loop at `:142-177`)
     indefinitely. (Running >10 away already breaks aggro, but staying near = relentless.)
   - **(b) Spawn clusters on you/the ship.** `TrySpawnCreatureNear` (`:198`) spawns each creature at
     `player.Position + SpawnRing[...]` where the ring is a **tight 7–13 block** circle, biome-gated by the spot.
     With a small cap (4/player, ≤12) + 70-block despawn, fauna only ever exists in a tight cluster around the
     player — and since you start/return at the ship, it reads as "they only spawn at the ship".

   ### Plan
   - **(a) Give-up / leash:** add a per-creature `ChaseTimer` + `GiveUpTimer` (on `CombatEntity`). An aggressor
     chasing within aggro range accumulates `ChaseTimer`; past a cap it **gives up** — sets a `GiveUpTimer`
     cooldown during which it ignores the player (wanders off, `Step` gets `nearest = null`) **and does not
     attack** — then can re-engage. `ChaseTimer` decays when no player is near.
   - **(b) Spread spawns:** widen + randomise the spawn placement (farther out, e.g. ~18–45 blocks at a
     scattered angle/rotor) so creatures populate the **surrounding biomes out of immediate sight** and you meet
     them as you explore, instead of popping in 7–13 blocks away. Keep the biome-native gating.

   **Questions:** (1) Give-up feel — roughly how long should an aggressor chase before giving up, and how long
   should it leave you alone after? (default ~12 s chase → ~10 s cooldown). (2) Spawn spread — OK to spawn
   creatures **farther out + scattered** (so you discover them around the biome) rather than right next to you?
8. ✅ **Task 4 — content-styled icons (done 2026-06-07).** Real per-item art everywhere instead of crude
   procedural glyphs / shared category icons.
   - **Client `IconResolver`** (new) — one key-based source for hotbar + crafting/tech/ship menu + space-systems
     bar: a generated full-colour PNG (`Resources/icons/item_<key>.png`) → else the in-game **block atlas tile**
     for a material that places/equals a block → else the old procedural glyph. Toxic consumables
     (`ConsumeHealth < 0`) get a runtime **green tint** (user pick — no extra assets).
   - **60 AI icons generated** via new `tools/ai-assets/gen_item_icons.py` (OpenAI `gpt-image-1-mini`, full-colour
     transparent, downscaled to 128²): 39 non-block items (mats/components, consumables incl. a steak, tools,
     weapons, suit gear) + 21 ship modules; space laser/tractor reuse `ship_laser_basic`/`tractor_beam`.
     Block-backed materials reuse their block tile (no asset). Blueprints reuse their unlocked key's icon.
   - **Wired:** `AddCard(..., contentKey)` in the menu (recipes/blueprints/modules/inventory), the hotbar
     (RawImage path + `PlacesBlock` tile fallback + green tint), and a selected-system icon over the space bar.
   - **Tested:** `IconCoverageTests` (every item/module resolves to an icon; toxic flagged) — 332 green.
     `NOTICES.md` updated; full client rebuilt (icons bundled).

   ### Analysis (2026-06-07)
   **Two parallel icon systems today, neither gives per-item art:**
   - **Hotbar (on-foot)** `HudUi.cs:311-321`: block-category items already draw a **downscaled block atlas tile**
     (`Game.Atlas.TileUv(numericId)` on a `RawImage`). Non-block items fall to `IconFactory.ForItem(key, kind)`
     — a 16×16 **procedural glyph** (ring/diamond/cross/box, colour hashed from the key). Crude, not per-item.
   - **Crafting / tech / ship menu** `CraftingTechShipUI.cs:1287-1301`: every row uses a **category** icon string
     (`cat_tools`, `cat_weapons`, …) via `UiKit.Icon()` → `Resources.Load<Texture2D>("icons/<name>.png")`. So a
     whole category shares one icon; individual items/blueprints/modules are indistinguishable.
   - **Block textures** are real, AI-generated 64×64 tiles in `Resources/textures/*.bytes` (34 of them: ores,
     metals, flora, glass, …) loaded by `BlockTextureAtlas`. **Materials that correspond to a block already have
     usable art** — it just isn't surfaced as an inventory/menu icon.
   - **Space view** `SpaceView.cs`: ship weapons/modules are **text labels** only (`ui.space.sys_laser`,
     `sys_tractor`); the tractor beam is a cyan cube. No icons.

   **Asset pipeline (ready to extend):** `tools/ai-assets/` has `gen_icons.py` (OpenAI `gpt-image-1-mini`,
   cyan-line transparent PNGs → `client/Assets/Resources/icons/<name>.png`, ~$0.005 each, `--only`/`--dry-run`,
   resumable) and `gen_textures.py` + `bundle_textures.py` (full-colour tiles → `Resources/textures/*.bytes`).
   Keys in `tools/ai-assets/.env`. Adding a new `gen_item_icons.py` manifest mirrors the existing pattern.

   **What needs an icon (audited — `data/*.json`):**
   - **60 items** (`items.json`): ~**22 map to a block** (placesBlock or same key → free **atlas tile** icon:
     stone/dirt/ores/glass/iron_wall/sand/mud/crystal/ice/basalt/wood_log/ladder/stairs/seeds…). The other **~38
     have no block art** and need a real icon: processed mats (iron_ingot, iron_plate, copper_wire, cable,
     carbon_composite, energy_cell_1, titanium_plate, data_fragment, plant_fiber), 7 consumables
     (creature_meat, **toxic_gland**, berries, **toxic_berries**, emergency_ration, medpack, oxygen_tank_1),
     6 weapons, 6 tools, 11 suit/wearable components.
   - **23 ship modules** (`ship_modules.json`) + the **space tools** (laser, tractor_beam) — for the builder/space
     UI.
   - **36 blueprints** (`blueprints.json`): each unlocks an item/module of (almost) the **same key** → **reuse
     that icon**, no separate blueprint art needed (fallback to category icon if unmatched).
   - No JSON schema change required: icons resolve **by key** at load (block-atlas tile, else `item_<key>.png`,
     else procedural fallback). Toxic = `consumeHealth < 0` (drives the green-meat variant).

   ### Plan
   1. **Client `IconResolver`** (new) — single source of truth used by BOTH hotbar and crafting/tech/ship menu:
      given an item/module/blueprint key return, in order: (a) **block atlas tile** as a `Sprite`
      (`Sprite.Create(atlas.Texture, tileRectInPixels, …)`) when the key places/equals a textured block; (b) a
      loaded **`Resources/icons/item_<key>.png`** sprite if present; (c) **`IconFactory`** procedural glyph
      fallback. Blueprint → unlocked-key lookup first. This makes the menu show the same real icon as the hotbar.
   2. **Generate the missing icons** via a new `tools/ai-assets/gen_item_icons.py` (manifest of the ~38 items +
      ~21 modules + space tools, each with a short prompt; downscale to 128×128 transparent PNG). **Test-first:**
      one sample icon generated + shown for approval, then the batch (per the asset-approval rule). Copy to
      `Resources/icons/item_<key>.png`; update `NOTICES.md`.
   3. **Meat/toxic:** a steak icon for `creature_meat`; toxic consumables (`toxic_gland`, `toxic_berries`) get a
      **green** treatment (either a dedicated green icon or a runtime green tint of the base icon — see Q).
   4. **Wire space view:** show the laser/tractor (and ship modules) icons in the ship systems bar + builder UI.
   5. **Bilingual + tests:** no new user text (icons are visual); add a small test that `IconResolver` returns a
      non-null sprite for every item/module key (catches missing art) and that toxic resolves to the green path.
   6. Build + client rebuild (icons are `Resources`, picked up by the player build).

   **Open questions for the user → see chat.**
9. ✅ **Feature — holographic visor HUD (done 2026-06-07; confirmed working in-game).** The diegetic HUD reads as
   a hologram projected onto the inside of the suit visor. **Decisions (user):** (a) **direct true HUD
   projection** (B1, not the mild whole-frame option); (b) **always on**; (c) **reflections yes**. *(Needed two
   in-build fixes the player log surfaced: build the composite material lazily — `OnEnable` ran before the shader
   was set — and resolve the HUD layer by index, since a freshly-added layer NAME isn't baked into a batch build;
   see [[unity-layer-index-not-name]].)*
   - **B1 pipeline:** the diegetic HUD canvases (HudUI + Space radar) render through a dedicated **UI camera**
     (`VisorHud`) on a new **`VisorHud` layer (8)** into an ARGB32 **render texture**; the main camera excludes
     that layer. A new `VisorComposite` (after `PostFx`) runs a fullscreen **`Spacecraft/Visor`** pass over the
     post-processed world: **barrel curvature**, **chromatic-edge fringe**, **scanlines + flicker**, a 4-tap
     **glow**, a faux-**fresnel rim glow**, and a faint **world reflection** (the requested reflections). **Head
     parallax** lags the projection slightly via the camera's yaw/pitch delta.
   - **Safe + idiomatic:** menus/dialogs/map/loading/death stay flat screen-space overlays (readability); the
     diegetic HUD is non-interactive so no raycast remap is needed; `HudLayerEnforcer` keeps dynamic children on
     the layer. **Degrades gracefully** — if the layer or shader is missing, `UiKit.HudCamera` stays null and the
     HUD falls back to a normal overlay (never lost). New shader registered in `m_AlwaysIncludedShaders`
     (see [[shaders-must-be-always-included]]). Always on; gentler under the reduced-effects preset.
   - Files: `Shaders/Visor.shader`, `Scripts/VisorHud.cs` (+ `VisorComposite`, `HudLayerEnforcer`),
     `UiKit.CreateDiegeticCanvas`, wired in `WorldRig`; `HudUi`/`SpaceRadar` use the diegetic canvas. *(Space
     overlay systems bar left as flat overlay — it shares the canvas with the full-screen launch fade.)*

   ### Original ask + analysis (kept for reference)
   *(Analysis was: can the UI look like a holographic HUD on a curved visor; which effects — curvature/parallax,
   fresnel/rim glow, scanlines, chromatic fringe, bloom/glow, distortion + reflections.)*

   ### Analysis (2026-06-07)
   - **All UI is `RenderMode.ScreenSpaceOverlay`** (`UiKit.CreateCanvas`), across ~11 canvases ordered by
     `sortingOrder`: diegetic HUD (Nameplates 8, **HudUI 10**, Space radar 10, **Space overlay 12**) vs.
     interactive chrome (Interactions 22, Chat 25, **Crafting/Tech/Ship 50**, Map 60, Warp 70, Loading 75,
     Death 80). **Key constraint:** overlay canvases draw *after* the camera, so the existing `OnRenderImage`
     post-FX **cannot** touch them — a visor look on the HUD needs the HUD rendered through a camera/RT first.
   - **A real post-FX pipeline already exists** — `PostFx.OnRenderImage` (on the main camera, `WorldRig.cs:67`)
     chains SSAO → **bloom** → composite (ACES tonemap + exposure + **vignette**), via `Spacecraft/PostBloom`,
     `PostComposite`, `PostAO`. So bloom/glow + vignette + a final fullscreen blit hook are **already there** to
     build on; the holographic glow is largely free.
   - **Full-screen overlays today:** `WeatherFx` uses IMGUI `OnGUI`/`GUI.DrawTexture` washes (underwater/rain/
     lightning) *behind* the HUD; `DeathFx` uses a high-order canvas `Image`. Both are independent layers.
   - **Single main camera** (reparented only by SpaceView); no UI camera. Custom shaders are `Shader.Find`-loaded
     with fallbacks — **a new visor shader must be added to GraphicsSettings `m_AlwaysIncludedShaders`** or it
     strips from the build (see [[shaders-must-be-always-included]]). Settings tab + a `ReducedEffects` preset
     already exist (accessibility/perf toggle hooks).
   - **Effect feasibility** (all cheap, one fullscreen pass): curvature = barrel UV-warp; chromatic fringe =
     radius-scaled R/G/B UV offset; scanlines = `sin(uv.y*N)`; distortion = animated noise UV nudge; rim glow =
     faux-fresnel radial gradient brightening toward the visor edge; glow = reuse bloom; parallax = offset the
     HUD-sample UV by the camera's frame-to-frame yaw/pitch delta; reflections = a faint additive static
     smudge/streak texture (optionally tinted by the world frame). True 3D fresnel/parallax only comes with the
     world-space-mesh variant (B2).

   ### Plan (when greenlit)
   - **Scope split (important):** apply the visor look ONLY to the **diegetic always-on HUD** (crosshair, vitals,
     hotbar, compass, location, toasts, scan, space systems bar + space overlay) — **NOT** to menus/dialogs/map/
     loading (those stay flat + crisp for readability). Conveniently the diegetic HUD is mostly **non-interactive**
     (hotbar is number-key selected), so routing it through a render texture needs **no raycast remapping**.
   - **Recommended mechanism — HUD → RenderTexture → visor composite (B1):**
     1. Render the diegetic HUD canvases via a dedicated **UI camera** (own "HUD" layer, transparent clear) into a
        screen-sized, **full-res** RT (keep text crisp); menus keep their own overlay canvases untouched. (UiKit
        gains a `CreateHudCanvas` that targets the UI camera in Screen-Space-Camera mode.)
     2. New **`Spacecraft/Visor` shader** — one fullscreen pass over the world frame that samples the HUD RT and
        applies barrel curvature + chromatic fringe + scanlines + noise distortion + faux-fresnel rim glow +
        emissive bloom + faint reflection, then composites over the crisp world. Hooked as the **final step in
        `PostFx`** (or a Blit right after). Uniforms: `_Time` (scanline/flicker), camera angular velocity
        (parallax), `_Intensity` (settings). Register it in `m_AlwaysIncludedShaders`.
     3. **Settings + accessibility:** a "Visor HUD" on/off + intensity in the Settings tab; **auto-off under
        `ReducedEffects`**; subtle by default (legibility first — kids play this).
   - **Lighter alternative (B0, ship-first option):** skip the RT/second camera; add mild **whole-frame** barrel +
     edge chromatic fringe + faint scanlines + stronger vignette inside `PostComposite`. Reads as "inside a
     helmet", touches no canvas, near-zero risk — but warps the world view slightly and isn't a true "projected
     hologram". Could ship as Phase 1, with B1 as Phase 2.
   - **World-space-mesh variant (B2, most immersive, most work):** map the HUD RT onto a curved spherical-section
     mesh in front of the camera with a holographic additive material — gives real curvature + head parallax +
     true fresnel, but adds mesh gen, depth/occlusion handling and is the heaviest. Defer unless B1 isn't enough.
   - **Open decisions for greenlight:** (a) B0 mild-whole-frame vs B1 true HUD-projection (recommended B1, with B0
     as a quick first pass); (b) default intensity / always-on vs only-in-EVA-or-space; (c) include faked
     reflections or keep it clean.
10. ✅ **Feature — build high enough to leave the atmosphere into space (done 2026-06-07).** Build a tower tall
   enough and you climb out of the atmosphere into space on foot. **Decisions (user):** on-foot **zero-g in the
   same world** (not the flight view); **per-planet height by density**; **airless bodies = a short climb**.
   - **Server-authoritative:** new `PlanetType.AtmosphereHeight` (absolute Y, in `planets.json`). `TickEnvironment`
     → `UpdateAboveAtmosphere` flips `PlayerState.AboveAtmosphere` for an on-foot player (not aboard/EVA/ship-
     interior/station) crossing the line, with a 4-block **hysteresis** so it doesn't flicker. Broadcast in
     `PlayerStateUpdate`.
   - **In-space-on-foot:** **oxygen drains** above the line even on a breathable world (extractor gives no
     benefit in vacuum); the client sets the dormant **`OnFootInSpace`** → existing `PlayerController` **zero-g
     float** kicks in; **`Sky`/`Starfield`** switch to a **space sky** (black + stars) regardless of the planet's
     own sky. A bilingual toast on crossing up/down (`hud.atmosphere.left`/`.entered`, DE+EN).
   - **Per-body heights:** breathable jungle/varied 240, swamp 230; toxic rocky/desert 190, ice 200; airless
     crystal/lava 150, asteroid 100 (all well above terrain peaks ~80-98). Void worlds 0 = disabled.
   - **Tested:** climb sets `AboveAtmosphere` + drains O₂ on a breathable world; descend clears it; hysteresis
     doesn't flicker; aboard-ship never counts; per-body heights differ — full suite **337 green**. Client +
     bundled server rebuilt.

   ### Original ask + analysis (kept for reference)

   ### Analysis (2026-06-07)
   - **The world is vertically unbounded** — `ChunkCoord` has a real Y axis, chunks stream in 3D
     (`GameServer` load loop `dy=-radius..radius`), and there is **no build ceiling**: `HandlePlace` only checks
     air/reach (`MaxReach` 8) — no max-Y. So a tall tower is fully representable; terrain tops out ~Y 80-98.
   - **Atmosphere is binary per planet, with no altitude logic anywhere.** `PlanetType.Atmosphere`
     (`breathable`/`toxic`/`none`), `SpaceSky` (always-space sky on airless bodies), `OxygenExtractability`.
     Oxygen in `TickEnvironment` regenerates (breathable + on-surface + not EVA) or drains; **nothing thins air
     with height** and the sky/starfield never fade with altitude.
   - **Groundwork already exists (dormant).** Client `GameBootstrap.OnFootInSpace` + `PlayerController` already
     implement **zero-g on foot** (Jump rises, Ctrl/C sinks, otherwise drifts to a stop) — *"nothing sets it yet,
     so it's a no-op until item 10 lands."* So the float is wired; only a trigger + sky + oxygen are missing.
   - **The transition pattern is established and reusable.** `TickEnvironment` runs a per-tick spatial check and
     flips state — `SteppedOutOfShipHull(pos)` → `StartEvaFromShip` is the exact template. Oxygen already drains
     in EVA regardless of the body. So an `AboveAtmosphere(pos)` check + a state flip mirrors existing code.

   ### Plan (recommended — confirm via questions)
   - **Mechanic (server-authoritative):** add a per-body **atmosphere height** (`AtmosphereHeight` on
     `PlanetType`, from `planets.json`; absolute Y). In `TickEnvironment`, for an on-foot player on a real planet
     (not aboard / not EVA / not ship-interior / not station), when `Position.Y` rises above it, flip a new
     `PlayerState.AboveAtmosphere = true`; drop it when descending below `height - margin` (hysteresis, like the
     hull check). Broadcast the flag in the player-state message.
   - **In-space-on-foot effects (reuse, same voxel world — you stay by your tower, can climb back down):**
     1. **Zero-g** — client sets `OnFootInSpace` from the flag → the existing `PlayerController` float kicks in.
     2. **Oxygen drains** — add `AboveAtmosphere` to the drain branch (you're above the air, even on a breathable
        world), so you need a tank/jetpack; running out suffocates via the existing death/recovery.
     3. **Space sky** — `Sky`/`Starfield` show black + stars + the sun when `OnFootInSpace` (an altitude fade
        into space), independent of the per-planet `SpaceSky`.
     4. **Feedback** — a bilingual toast on crossing up ("You have left the atmosphere — zero gravity") and down
        ("Re-entering atmosphere"); locale keys (DE+EN) per the parity rule.
   - **Per-body heights:** thick/breathable worlds sit high (a big tower); thin/toxic mid; **airless / `SpaceSky`
     bodies very low** (a short climb reaches space — fits "an asteroid barely has atmosphere"). Default sensibly.
   - **Tests:** climbing above the height sets `AboveAtmosphere` + drains O₂ even on a breathable world;
     descending clears it; hysteresis doesn't flicker; airless bodies trip at a low height.
   - **Out of scope (note):** this keeps you **on foot in the same world** (zero-g), NOT the flight/EVA combat
     view — a later "launch pad to flight view" could bridge to item 5's space instance if wanted.

   **Open questions → see chat.**
11. ✅ **Feature — trade knowledge points (done 2026-06-07).** Trade knowledge for materials/equipment; knowledge
   **never goes away** and each point can only be passed to a given player once. **Decisions (user):** unlock is a
   **threshold (knowledge not spent), material cost stays**; anti-abuse rule = **"teach up to your level"**.
   - **Knowledge is now a permanent threshold:** `HandleUnlock` no longer does `KnowledgePoints -= KnowledgeCost`
     — it only checks `>=` (materials still consumed). Knowledge is synced to the client via `InventoryUpdate`.
   - **Knowledge in a trade (giver keeps points):** `TradeSession` gains `KnowledgeA/B`; new `TradeKnowledgeIntent`
     (NetCodec tag **97**), `TradeUpdate` carries each side's offer + my total + my teach-cap. On commit the
     **giver keeps** their knowledge and the **receiver gains** it, alongside the atomic item swap — so you trade
     **knowledge ⇄ materials/equipment**.
   - **Loop-proof give-once ledger:** `PlayerState.KnowledgeGivenTo` (receiverId→points, persisted). The teachable
     amount = `min(myKnowledge − alreadyGivenToThem, myKnowledge − theirKnowledge)` ≥ 0 — you can't lift anyone
     above your own level and can't out-give what you know, so the same points can't be cycled to inflate totals.
   - **Client:** a "Knowledge →N/max" row on each side of the trade panel (− / + / Max), bilingual
     (`ui.trade.knowledge` / `ui.trade.max`).
   - **Tested:** giver keeps + receiver gains for goods; can't exceed giver's level; give-once blocks
     back-and-forth inflation; threshold unlock no longer deducts (materials still spent) — full suite **340
     green**. Client + bundled server rebuilt.

   ### Analysis (2026-06-07)
   - **⚠️ Premise mismatch with the current code.** Knowledge is stored as `PlayerState.KnowledgePoints` (int,
     persisted in `Snapshots`), earned on **first-time scans** (`GameServerScanning` — hostile 5 / creature 3 /
     block 1 / asteroid 4, ×`ScanKnowledgeMultiplier`). **But unlocking a blueprint currently SPENDS it**:
     `GameServer.cs:1783-1785` does `pool.Remove(bp.UnlockCost); KnowledgePoints -= bp.KnowledgeCost;` — i.e.
     knowledge is a *consumed cost today*, not a permanent threshold. The task's "knowledge never goes away / no
     points spent" describes a **different model** → this feature must also change unlock to **threshold-only**
     (check `KnowledgePoints >= KnowledgeCost`, don't subtract) for the premise to hold. (Material cost is
     separate — see Q.)
   - **Trade system is a solid base to extend.** `GameServerTrade` already does a proximity-gated (8 blocks)
     request→accept→offer→both-confirm→**atomic commit** handshake with `TradeSession { A,B, OfferA, OfferB,
     ConfirmA, ConfirmB }`; `MaterialPool.Remove/Add` move items with spill-back. Messages
     (`TradeRequest/Respond/Offer/Confirm/Cancel` Intents, `TradeUpdate`/`TradeClosed`) are registered in
     `NetCodec` (tags 30-34, 80-81). Client UI in `PlayerInteractions` (±buttons per item, two offer columns).
   - **No per-pair ledger exists.** Nothing tracks who gave whom what. Need a persisted, per-pair cumulative
     "knowledge already given to X" record (add `Dictionary<string,int> KnowledgeGivenTo` to `PlayerState` +
     persist in `Snapshots`).

   ### Plan (recommended — confirm via questions)
   - **Make knowledge permanent (threshold-only unlock):** drop the `KnowledgePoints -= KnowledgeCost` deduction;
     keep the `>=` gate. Knowledge becomes a non-spent stat (matches "never goes away"). *(Update the PlayerState
     comment + the unlock path + any test asserting deduction.)*
   - **Knowledge in a trade (no deduction from the giver):** extend `TradeSession` with `KnowledgeA/KnowledgeB`
     and `TradeUpdate`/a new `TradeKnowledgeIntent` (register in NetCodec!). On commit, the **giver keeps** their
     points; the **receiver gains** the offered amount — gated by the give-once rule below. Items still swap
     atomically alongside, so you can offer **knowledge ⇄ materials/equipment**.
   - **Give-once / anti-inflation ledger (loop-proof — see Q for the exact rule):** persist
     `KnowledgeGivenTo[receiver]` on the giver. Recommended rule: a gift may only **raise the receiver up to the
     giver's own current level** (`credit = min(offered, giverKnowledge − receiverKnowledge)`), and the
     cumulative `givenTo` may never exceed the giver's knowledge — so the same knowledge can't be handed back and
     forth to inflate totals (once equal, nothing flows). The giver never loses points.
   - **Client:** add a "Knowledge" line to each side of the trade panel (a number with ±, like an item row),
     show the per-pair remaining you can still teach this partner; bilingual locale keys (DE+EN).
   - **Tests:** giver keeps knowledge + receiver gains; can't raise receiver above giver; cumulative cap per pair
     (no back-and-forth inflation); threshold-only unlock no longer deducts; knowledge⇄items swap is atomic.

   **Open questions → see chat.**
12. ✅ **Feature — NPCs should have names (done 2026-06-07).** Every NPC gets a deterministic coined name,
   mirroring the flora/creature `NameGenerator`; shown as **"Name · Role"** on the nameplate (user pick).
   - `NameGenerator.Person(rng)` (short given name + longer surname, both capitalised — thousands of combos) and
     `NameGenerator.Robot(rng)` ("Vex-42" unit designation) for `IsRobot` NPCs.
   - `ServerNpc.Name` + `NetNpc.Name`, coined in `MakeNpc` from the existing settlement/station-seeded rng (so
     names are stable per NPC without persistence, like creatures). `NpcView.Label` shows "Name · Role" (falls
     back to the role label if absent). Procedural → identical DE/EN; the role label stays localized.
   - Tested: `NameGeneratorTests` (Person/Robot deterministic, two capitalised parts, highly varied, robot has a
     stem + unit number) — full suite **344 green**. Client + bundled server rebuilt.

   ### Analysis (2026-06-07)
   - **NPCs have a stable id + role but no personal name.** `ServerNpc` (`GameServerNpcs.cs`) = `Id, Role
     (vendor/quartermaster/settler), Theme (settlers/miners/traders/researchers), NameKey, IsRobot, Pos…`.
     `NameKey` is only a **role label key** (`npc.role.vendor` / `npc.theme.settlers`). Mirrored to the client as
     `NetNpc` in `NpcList` (NetCodec tag 84 — adding a field is non-breaking).
   - **Deterministic seed already available.** NPCs are made in `MakeNpc(role, theme, robotic, home, rng)`, drawn
     from a settlement/station-seeded `System.Random` (`_meta.Seed ^ StableHash("settlement:"+key)` /
     `"station-npc:"+id`) — the same per-world pattern creatures use. NPCs aren't persisted (regenerated on load),
     so a name coined from this rng is **stable per NPC** without any new persistence.
   - **`NameGenerator`** (`Spacecraft.WorldGeneration`, pure/deterministic) already has `Creature` (two-part
     "Vexilth krool") + `Flora` + a private `Word(rng,min,max)` syllable builder. Easy to add a `Person` (First +
     Surname) and a `Robot` (designation) variant.
   - **Display path:** `NpcView.Label(nd)` returns `loc.Get(nd.NameKey)` → shown as a floating nameplate via
     `ScreenLabelLayer.World(...)`. One place to change to show the name.

   ### Plan
   - Add `NameGenerator.Person(rng)` (e.g. `Word(1,2)` first + `Word(2,3)` surname, both capitalised — distinct
     from the lowercase-epithet creature style) and `NameGenerator.Robot(rng)` (a short stem + a number, e.g.
     "Vex-42") for `IsRobot` NPCs. Thousands of combinations, deterministic from the rng.
   - `ServerNpc.Name` + `NetNpc.Name`; set in `MakeNpc` (`robotic ? Robot : Person`), map in `ToNetNpc`.
   - `NpcView.Label` shows the name (format = the display question), falling back to the role label if empty.
   - Names are procedural → identical in DE/EN (no locale change); the role label stays localized.
   - Tests: `Person`/`Robot` deterministic for the same seed, varied across seeds, non-empty, two-part for Person.
13. ✅ **Feature — mission-giver NPCs that always have a mission on offer (done 2026-06-07).** The quartermaster
   is a mission-giver that never runs dry. **Decisions (user):** refill-on-take; keep **~3** available.
   - **Never dry (deterministic rolling window):** a giver offers an endless sequence of procedural missions
     (slot 0,1,2,…). `SendMissionList` shows a player the lowest **`BoardWindow` (3)** slots they haven't taken;
     accepting one slides the window so a fresh one appears — `EnsureBoardWindow`/`EnsureSettlementWindow`/
     `EnsureStationWindow`. Slots are coined deterministically from `(boardKey, slot)` (`BuildBoardMission`) so
     they **survive a reload without persistence**; `StockBoard` seeds the first window at stamp time. Old
     boards' missions are scoped out of the list (only the board you're at offers its jobs).
   - **Mission ↔ giver NPC:** `MissionDefinition.GiverName` (+ `NetMission.GiverName`); the settlement/station
     **quartermaster** NPC is named via `CoinGiverName(boardKey)` so its name matches its missions. Client shows
     **"Mission from {Name}"** in the detail (bilingual `ui.missions.giver`); mission titles are now localized.
   - Replaced the old fixed 1-2/2 pools (`GenerateSettlement/StationMissions` removed). Foundation for items 14/15.
   - **Tested:** `Board_NeverRunsDry_KeepsMissionsAvailable_AsYouKeepAccepting` (≥3 after 12 accepts),
     `BoardMissions_CarryAGiverName`; existing board accept/turn-in still green — full suite **346 green**.
     Client + bundled server rebuilt.

   ### Analysis (2026-06-07)
   - **Missions CAN run dry today.** `MissionDefinition` (id, source, nameKey/title, objectives[Collect/Mine/
     Deliver], rewards[], `Repeatable`, `Active`) + per-player `MissionProgress`. Settlements mint **1-2** missions
     at stamp time; stations **2** at board time — a **finite pool, default non-repeatable, no refill**. The
     per-player available list hides anything accepted/turned-in, so once a player takes them the board is empty.
   - **No NPC ↔ mission link.** Missions are **board-centric**: `NearSettlementMissionBoard` (a `mission_board`
     marker, reach-gated) gates accept; the **quartermaster NPC is purely cosmetic** (rendering/name only).
     `MissionDefinition` has no giver field (only `CreatorId` for player-made; `MissionPlan.GiverName` is unused
     flavour). Quartermaster NPCs stand at the board, so near-board ≈ near-quartermaster.
   - **Plumbing to reuse:** `HandleAcceptMission`/`HandleTurnInMission`/`SendMissionList` (server),
     `RequestMissions`/`AcceptMissionIntent`/`TurnInMissionIntent` + `MissionList`/`MissionResult` (NetCodec
     10-13 / 62-63), `NetMission` schema, the Missions tab in `CraftingTechShipUI` (Available/Active + accept/
     turn-in). `MissionProgress` is persisted per player; board defs regenerate per load.

   ### Plan (recommended — confirm via questions)
   - **Tie missions to a giver NPC:** add `GiverNpcId` to `MissionDefinition` (+ `GiverName` on `NetMission` for
     display). When generating settlement/station missions, assign them to that location's **quartermaster** NPC.
     Foundation for items 14 (NPC memory) + 15 (AI dialog).
   - **Never run dry (refill-on-take):** keep each giver topped up to **≥1 available** procedural mission. When a
     player accepts a giver's mission, immediately **mint a fresh replacement** for that giver (rolling unique id,
     randomised template + counts), so the giver always shows something to take. (One-shot missions; the refill —
     not repeatability — is what guarantees endless availability.) `EnsureGiverStocked(npcId, target)` called at
     generation + after accept.
   - **Client:** show the giver's name on the mission ("Auftrag von {Name}"); bilingual key. The Missions tab and
     the accept/turn-in flow stay as-is.
   - **Tests:** a giver always has ≥1 available even after accepting repeatedly (never dry); accepting mints a
     replacement; missions carry the giver id/name; turn-in still pays the reward.

   **Open questions → see chat.**
14. ✅ **Feature (plan ahead) — NPCs remember their interactions with a player (done 2026-06-07).** Per-NPC,
   per-player relationship + a log of the last 10 interactions. **Decisions (user):** memory plumbing only
   (record today's trade + mission-accept; "Dialog" wired for item 15); **internal** (no UI standing).
   - **Model (shared):** `NpcInteractionKind {Dialog,Trade,MissionAccepted}`, `NpcInteraction`,
     `NpcRelationship {Name,Role,Value,Log}`; `PlayerState.NpcMemory : Dictionary<NpcKey, NpcRelationship>`
     persisted via `Snapshots` (deep-cloned). **NpcKey = `{locationKey}:{role}`** (e.g. `settle_<hash>:vendor`,
     `station_<hash>:quartermaster`) — stable across reloads since the runtime `ServerNpc.Id` isn't.
   - **Recording** (`GameServerNpcMemory`): `RecordNpcInteraction` raises the relationship by a per-kind weight
     (Dialog +1 / Trade +2 / MissionAccepted +3, clamped) and appends to the **capped last-10** log. Hooked into
     `HandleAcceptMission` (quartermaster) and the market-barter path in `HandleCraft` (vendor; aboard-ship
     console doesn't count). `NpcRelationshipFor(playerId, npcKey)` exposes it for item 15.
   - **Tested:** `Quartermaster_RemembersMissionAccepts_RelationshipRises_LogCapsAtTen`,
     `Player_RoundTripsNpcMemory` (persists) — full suite **348 green**. Bundled server rebuilt.

   ### Analysis (2026-06-07)
   - **No NPC dialog/talk system exists yet.** The only player↔NPC interactions today are **vendor barter** (a
     market trade gated by `NearSettlementVendor`/`NearSpaceStationVendor`) and **accepting a mission** at the
     quartermaster's board (item 13). "Dialog" has **no trigger yet** — that's item 15's job.
   - **NPCs aren't persisted** (regenerated each load), so memory can't live on `ServerNpc`. But their identity is
     stable by **location + role**: the vendor / quartermaster of a given settlement (`settle:<hash>`) or station
     (`station:<id>`). Coined names are deterministic (items 12/13) but not unique → key memory by a stable
     **NpcKey = `{locationKey}:{role}`**, not the runtime `ServerNpc.Id`.
   - **Persistence pattern ready:** `PlayerState` already carries persisted dictionaries (e.g. `KnowledgeGivenTo`);
     a per-player record of each NPC (= the NPC's memory of that player) persists cleanly via `Snapshots` and is
     exactly what item 15's backend needs (relationship + recent log for this player+NPC).

   ### Plan (recommended — confirm via questions)
   - **Data model (shared):** `enum NpcInteractionKind { Dialog, Trade, MissionAccepted }`; `NpcInteraction
     { Kind; }` (optionally a turn/detail); `NpcRelationship { int Value; List<NpcInteraction> Log }` capped at
     **10** (FIFO). `PlayerState.NpcMemory : Dictionary<string, NpcRelationship>` keyed by NpcKey. Persist in
     `Snapshots` (like `KnowledgeGivenTo`).
   - **Recording:** `RecordNpcInteraction(player, npcKey, kind)` — appends to the log (trim to 10) and raises the
     relationship by a per-kind weight (e.g. Dialog +1, Trade +2, MissionAccepted +3; clamped). Hook it into the
     **mission-accept** handler (quartermaster NpcKey) and the **vendor barter** path (vendor NpcKey).
   - **Lookup for item 15:** `NpcRelationshipFor(player, npcKey)` returning value + last-10 log + the NPC's
     name/role, so the dialog backend (item 15) receives name, role, relationship and logs.
   - **Tests:** interactions accumulate + raise the value; the log caps at 10 (oldest dropped); memory persists
     across a reload; distinct NPCs/players keep separate records.

   **Open questions → see chat.**
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

   ### Analysis + Plan (2026-06-07) — design only, NOT implemented
   **What's already in place (the inputs exist):** items 12-14 give every NPC a stable name (`CoinGiverName` /
   item 12), a role (`vendor`/`quartermaster`/`settler`), a per-player **relationship + last-10 interaction log**
   (`PlayerState.NpcMemory`, item 14), and the **offer** (a quartermaster's current board missions / a vendor's
   market goods). `GameServerWeather` exposes weather + time-of-day; `ActiveLocationNames()` + the boarded-station
   name give the planet/station. So the **server already holds every request parameter** the task lists.
   **What's missing:** (a) **no "talk to NPC" interaction** exists yet (item 14 confirmed) — needed to trigger +
   show dialog; (b) no outward HTTP call / async path from the server; (c) no world-creation toggle for it; (d)
   the Python backend itself.

   **LM Studio facts (researched):** LM Studio runs a local **OpenAI-compatible** server at
   `http://localhost:1234/v1` — `GET /v1/models` (lists the *loaded* model) and `POST /v1/chat/completions`. The
   **graceful-skip probe** = `GET /v1/models`: connection refused or an empty list ⇒ no model ⇒ skip. `api_key`
   is ignored on localhost (send any non-empty string). (A richer `GET /api/v0/models` reports load state too.)

   **Architecture (chain, each link degrades gracefully):**
   `Game server (C#)` → `./ai-backend (Python/FastAPI)` → `LM Studio /v1/chat/completions`.
   The **C# server** owns the context (it has NpcMemory/weather/offer) and calls the Python backend; the backend
   is a thin **provider abstraction** over the LLM (LM Studio now, others later) that owns the prompt. If the
   backend is down → server skips; if LM Studio has no model → backend replies "unavailable" → server skips.

   **`./ai-backend/` (Python, `uv`, FastAPI + httpx):**
   - `GET /health` → `{ available: bool }` (probes LM Studio `/v1/models`), so the game cheaply knows whether to try.
   - `POST /npc-dialog` body `{ planet, npcName, npcRole, relationshipValue, relationshipBand, recent:[kinds],
     offer:{type, detail}, weather, timeOfDay, language }` → `{ text }` (or 503 when no model). Builds a **system
     prompt** ("You are {name}, a {role} on {planet}; relationship {band}; recent: {…}; weather/time {…};
     offering {…}; reply in {language}, ≤2 short sentences, in-character, no markdown"), calls chat/completions
     (`max_tokens` ~80, temp ~0.8), returns the trimmed line. `.env` for `LMSTUDIO_URL`/model. **Bilingual** via
     the `language` field (matches the player's locale). Runs standalone: `uv run uvicorn app:app --port 8770`.
   - Mirrors the existing `tools/ai-assets` Python/`uv` style; keep it dependency-light + offline-friendly.
   - Optional: a tiny in-memory cache keyed by (npcKey, player, coarse-context) to avoid re-generating on repeat
     talks; a short request timeout; one retry.

   **Game integration (C#):**
   - **Talk interaction (new):** client presses **E** on a nearby NPC → `TalkToNpcIntent{npcId}` (register in
     NetCodec). Server `HandleTalkToNpc`: resolve the NPC + its `NpcKey`, **record a `Dialog` interaction**
     (item 14 — finally wires the dormant `Dialog` kind), and **immediately** send a `NpcDialog{npcId, text}` with
     the **canned line** (placeholder). A small client panel shows name · role + the text.
   - **Async, non-blocking:** if the world's AI toggle is on AND a cached `/health` says available, the server
     fires the `POST /npc-dialog` on a background task (`HttpClient`, ~8-12 s timeout) off the tick loop; when the
     text returns it sends a second `NpcDialog{npcId, text, final:true}` that **replaces** the placeholder. Timeout
     / error / disabled ⇒ keep the canned line. (Never block `Tick`.)
   - **Toggle (world creation):** add an `AiDialog` flag to `ServerConfig` + the world-create UI (alongside PvP /
     space-combat etc.), persisted in world metadata. Off ⇒ never call the backend. Also a backend URL setting
     (default `http://localhost:8770`).
   - **Params source:** name/role/relationship/log from `NpcMemory` + the NPC; offer from the quartermaster's
     board window (mission type) or vendor goods; planet/station from `ActiveLocationNames()`/station; weather +
     time from `GameServerWeather`.

   **Open decisions for greenlight:** (a) does the C# server call the backend, or the client directly? (recommend
   **server** — it holds the context + the toggle); (b) backend framework (FastAPI recommended); (c) cache + cost
   controls; (d) how the backend process is launched (manual `uv run` first; later a launcher script / bundled).
   **Out of scope here:** no code — this entry is the plan; implement only when greenlit.
16. ✅ **Task 5 — crafting / tech-tree / materials overhaul + more metals & rare earths.** (Big, staged — full
   Analysis + Plan below.) **Done so far: Stage 1** (14 metals/rare-earths + alloy tier + soft-lock/dead-station
   cleanups), **Stage 3** (placeable workbench/forge = on-world crafting + decor blocks), **Stage 3b** (placeable
   storage crate), **Stage 3c** (placeable hinge + slide doors), **Stage 4** (ships & ship parts fold onto the new
   materials), **Stage 2** (knowledge-economy + ore depth-tier rebalance). **✅ All stages done** — every Task-5
   sub-stage is complete (in-engine playtest still wanted for the on-world build objects: crate, doors, workbench/
   forge, + the visor HUD).

   ### Analysis (2026-06-07)
   **Current graph** (`data/{items,blocks,recipes,blueprints,ship_modules,ships,planets}.json`):
   - **Raw (mined):** iron_ore, copper_ore, silicate, carbon, titanium_ore, crystal + `data_fragment` (from
     `data_cache` blocks). **Flora drops:** plant_fiber, berries, crystal. **Creature drops:** creature_meat,
     toxic_gland, etc. (so `creature_meat`/`toxic_gland` ARE obtainable — via fauna, not a bug).
   - **Crafted chain (shallow, ~2-3 tiers):** ore→ingot→plate (iron); copper_ore→copper_wire→cable(+silicate);
     carbon→carbon_composite; silicate→glass; titanium_ore→titanium_plate(refinery); cable+carbon_composite→
     energy_cell_1. Everything advanced (weapons/suit/modules/ships) = plate/cable/energy_cell + a blueprint.
   - **Tech tree:** ~36 blueprints, mostly shallow chains (machete→vibro_knife→plasma_sword, etc.), gated by a
     **data_fragment + plate/cable** unlock cost + a knowledge threshold (item 11). 6 stations in the enum.
   **Real inconsistencies to fix:** (1) **`Lab` + `MachineRoom` stations are dead** — in the enum + station
   mapping (`GameServer.cs`) but **no module provides them and no recipe uses them**. (2) **Duplicate market
   recipes are strictly worse than crafting** (`market_buy_medpack` 3c+2s vs hand 2c+1s; `market_buy_oxygen_tank`
   dupes the blueprint craft) → dead / trap entries. (3) **Single-planet soft-lock:** silicate/copper absent on
   lava/jungle/swamp → no `cable` → progression stalls without travel/market (travel-gating is fine, a hard wall
   isn't). (4) **`data_fragment` economy:** ~20× rarer than ore yet needed by nearly every unlock → grind.
   (5) minor: `comm_radio` blueprint vs recipe cost ordering. *(Not bugs / by-design: titanium scarcity =
   travel-gating; pre-built starter ship carries `tractor_beam`/`ship_laser_basic` un-blueprinted.)*
   **Gaps vs the task goal:** few metals (no gold/silver/aluminium/nickel/…), no **alloys/electronics** mid-tier,
   thin "build on a world" object set (mostly walls/lights/ladders/stairs), no real **staged prerequisites**
   beyond plate/cable.

   ### Plan — staged (each stage shippable + tested)
   - ✅ **Stage 1 — New metals & raw resources + textures + base processing (done 2026-06-07).** Added **14 new
     ores** (gold, silver, aluminium, tin, nickel, cobalt, lithium, uranium, platinum, lead, zinc, tungsten,
     sulfur, neodymium) — each a mineable block + material item with a **generated OpenAI texture**, distributed
     across every planet's ore tables (rare metals deeper/rarer). **Soft-lock fixed:** copper_ore + silicate now
     on every ore-bearing world, so all can reach `cable`. **Base smelting** (ore→ingot/refined) for all 14. To
     avoid dead-ends, a **mid-tier of 9 alloys/components** (steel, bronze, brass, circuit_board, power_cell,
     reactor_fuel, carbide, magnet, light_alloy) consumes every new metal and is **folded into existing recipes**
     (armor/comm_radio/scanners/drills/jetpack + radar/tractor/jump modules) + 3 buildable decor blocks
     (steel_wall, bronze/brass block). Removed the **dead `Lab`/`MachineRoom` stations** + the **worse-than-craft
     market dupes**. **Tested:** `CraftingConsistencyTests` (no broken refs, every input obtainable, no dead-ends,
     every planet reaches cable, dead stations gone) — 354 green. ~40 assets generated; client rebuilt.
   - ✅ **Stage 2 — knowledge-economy + ore depth-tier rebalance (done 2026-06-07).** *(The staged alloy/
     electronics intermediates this stage originally scoped — steel/bronze/electronics/alloy plates gating the
     advanced items — were already delivered in Stage 1; Stage 2 became the **economy + depth** rebalance the
     user picked.)* **Eased the `data_fragment` grind** (≈89 fragments unlock everything, ~1–5 per blueprint;
     `data_cache` was ~10–75× rarer than ore): the cache now **drops 2** (was 1), worldgen cache rarity **×1.5
     across all worlds** (floor 0.0012), the market fallback is cheaper (**3 titanium_ore + 1 crystal → 1**, was
     4+2), and the two steepest unlocks (laser_cannon_2, plasma_blaster) drop **5→4** fragments — on top of the
     existing cache/market/mission/combat/structure-loot sources. **Light depth-tiering:** valuable/rare ores now
     sit deeper (gold 16→20, silver/cobalt 12→16, lithium 8→12, tungsten/neodymium 20→26, uranium/platinum 24→30)
     while construction ores stay shallow — rewards deep mining without a soft-lock (every vein still reachable in
     its crust). **Tested:** new `EconomyBalanceTests` (eased cache yield, every vein reachable, valuable-deeper-
     than-construction) — 361 green; data synced to the bundled server.
   - ✅ **Stage 3 — Buildable world objects (done 2026-06-07).** New placeable blocks for base-building on
     worlds: a **workbench** (enables **workshop** crafting when you stand near it, no ship needed) and a
     **forge** (enables **refinery** crafting) — `StationAvailable` now also accepts a placed station block via
     `NearStationBlock` (3-block reach) — plus decorative building blocks (**steel_floor**, **metal_panel**,
     **concrete**). Each = block + item + generated texture + recipe + bilingual name. **Tested:** `WorkbenchTests`
     (workbench→workshop, forge→refinery on a world without the ship) — 356 green. Client + bundled server rebuilt.
   - ✅ **Stage 3b — Storage crate (done 2026-06-07).** A placeable **crate** is a persistent container for base
     storage: placing it registers a container (reusing `StoredContainer`/`GameServerContainers`), **H stashes**
     all your loose materials/components into the nearest crate (tools/weapons stay), **G takes** them back out
     (existing loot path), and **mining** the crate returns its contents + the crate block. New
     `DepositContainerIntent` (NetCodec **98**), `DepositToContainer`, place/mine hooks, a `PlaceBlock` test
     entrypoint, and a crate-specific HUD prompt ("Stash · G take · H store"). Tested: `CrateStorageTests`
     (stash-not-tools + take-back, mining returns contents) — **358 green**. *(Placeable door still a follow-up.)*
   - ✅ **Stage 3c — Placeable doors (done 2026-06-07; needs in-engine test).** Two new placeable blocks —
     **`door_hinge`** (manual: press **E** to swing it open/shut) and **`door_slide`** (sci-fi auto door: opens
     as you approach, auto-closes) — both crafted at a workbench (3 `metal_panel` + 1/2 `circuit_board`). They
     **reuse the existing door entity system end-to-end, so the client needed no changes**: placing one calls a
     new server **`PlaceDoor`** that fills the (air) cell with a width-1 `ServerDoor` (wall axis from the jambs or
     the player's facing), **persists** it by cell (new `door` table + `SaveDoor`/`ListDoors`/`DeleteDoor`), and
     broadcasts the `DoorList` the client already renders/animates/collides. **`LoadPlayerDoors`** re-appends the
     persisted player doors after every deterministic rebuild (`RegisterDoors`/`RegisterStationDoors`) + on world
     load, so settlement/ship stamps never wipe them. **Mining** is intercepted (a door fills an air cell):
     `RemovePlayerDoorAt` removes the door, returns the item, and persist-deletes — to remove an open door, close
     it (E for hinge; step back so the slide auto-closes — `Reach 6` > slide range `4.5`). **Tested:**
     `PlaceableDoorTests` (place→register+persist+consume, E toggles, mine returns the item + forgets it, and a
     **reload** re-loads the door) — **363 green**; client + bundled server rebuilt. *(The collider-to-mine feel +
     the animation want a playtest.)*

     <details><summary>Original analysis + plan (kept)</summary>

     **As-is (mapped):** the game already has a full **door system** — doors are **server-authoritative entities**
     (`ServerDoor` in `GameServerDoors.cs`), *not* voxel blocks: a doorway stays **air**, and a door entity fills
     the gap. The client `DoorView.cs` already renders them as GameObjects, **animates** them (slide panels
     retract / hinge leaf swings), toggles a **BoxCollider** by open-state, plays SFX, and creates/destroys them
     live from the server's `DoorList` — so **the client needs no changes**. Two kinds exist: **slide** (auto —
     opens when a player is within range, auto-closes) and **hinge** (manual — press **E** at it; already wired in
     `PlayerController` via `NearestHinge` + `SendDoorInteract`, NetCodec tag 46 / DoorList 93). Today doors are
     built only from generated **markers** (settlements/ship/stations) via `RegisterDoors()`/`MakeDoor()` (which
     probes the surrounding blocks for wall axis + gap width). **Two gotchas for player doors:** (1) `RegisterDoors()`
     **clears + rebuilds** `_doors` from markers on every settlement/ship stamp + world load → player-built doors
     must be **persisted** and re-appended afterwards (the generated ones are deterministic, player ones are not);
     (2) doors fill an **air** cell (no solid block), so removal can't rely on a normal block-break.
     **Plan (server-only, reuses DoorView):** add a **hinge** door block+item (`door` → category block, `placesBlock`,
     texture, recipe, bilingual names); in `HandlePlace`, after the cell is placed, call a new **`PlaceDoor(pos)`**
     that (a) keeps the cell **air** (not solid), (b) registers a **width-1 `ServerDoor`** centred on the cell
     (axis from the player's facing or probed jambs), (c) **persists** it (mirror the container repo:
     `SaveDoor`/`ListDoors`/`DeleteDoor`), (d) `BroadcastDoors()`. Re-append persisted player doors at the end of
     `RegisterDoors`/`RegisterStationDoors` + on load (so stamps don't wipe them). **Removal:** mining the door's
     cell (its closed collider is raycast-hittable) finds the player-door there → remove + persist-delete + drop
     the `door` item; if it's open, press **E** to close first. Server-unit-testable (place → `DoorCount`/persisted
     → interact toggles → mine removes + drops); the **feel** (collider-to-mine mapping, animation) needs a
     **playtest**. *(Door **kind** + recipe to confirm with the user before building.)*
     </details>
   - ✅ **Stage 4 — Ships & ship parts on the new materials (done 2026-06-07).** Folded the new alloys/electronics
     into ship + module builds: **hauler** craftCost += steel + circuit_board (heavy hull + avionics), **scout**
     += light_alloy + circuit_board (light + sensors); module builds **hull_plating** += steel, **shield_generator**
     + **jump_generator** += circuit_board (on top of the Stage-1 magnet/reactor_fuel folds for radar/tractor/jump).
     So high-tier ships/modules now require the deeper material tier. Tested (updated the hull-plating + scout-craft
     tests to supply the new mats) — 358 green; data synced to the bundled server. *(Intermediate ship tier
     deferred; a placeable door is the remaining Stage-3 follow-up.)*
   - Tests at each stage: every recipe input/unlock cost is obtainable; no dead-end outputs; every planet has a
     path to `cable`; new ores mine + smelt; content-load + locale-parity stay green.

   **Open scope questions → see chat.**
17. ✅ **Task 6 — drastically more flora & fauna variety** (first big batch done 2026-06-07; needs in-engine test).
   Added in one batch (all generated): **+14 flora archetypes** — palm, moss, orchid, succulent, pitcher, puffball,
   lichen, coral, seagrass, sporepod, thornbush, bellflower, ashweed, glowvine (each = `flora_*` block + drops +
   `FloraCatalog` hosts across biomes + OpenAI texture + DE/EN name; glowvine/sporepod also glow via
   `ChunkMesher.GlowFor`); **+10 creature hide textures** — mossy, crystalline, metallic, banded, shaggy, spined,
   mottled, iridescent, barkskin, veined (OpenAI grayscale tiles wired into `CreatureBuilder`'s hide-selection
   pools, incl. diversified glow/winged/water/hostile sets); **a new procedural body feature** — a **dorsal crest**
   (`HasCrest` on `CreatureSpecies`/`NetCreature`, rolled ~⅓, rendered as spine plates) plus **wider generator
   rolls** (body segments 1→4, eyes incl. 1/8, horns incl. 4); **+10 creature calls** — purr, moan, squeak, drone,
   gurgle, yelp, snarl, whistle, cluck, wail (ElevenLabs clips added to `CreatureView`'s call list). 363 green;
   textures bundled + sounds installed; client + bundled server rebuilt. *(Visual/audio variety wants a playtest;
   the pipeline supports further batches — see analysis below.)*

   ### Analysis (2026-06-07) — as-is + plan
   **As-is (mapped):** Three deterministic-from-seed systems, all already fairly rich:
   - **Flora = fixed archetypes.** 15 `flora_*` blocks (`data/blocks.json`) each with a host-surface list in
     `FloraCatalog` + a 64² texture; `FloraGenerator` per world just coins a name + rolls toxic/active per
     archetype (no per-world data). So **more flora variety = add archetypes** (block + `FloraCatalog` entry +
     texture + bilingual name). `FloraTests` enforces every host surface keeps ≥1 active species (adding more is safe).
   - **Fauna = fully procedural.** `CreatureGenerator` builds species from fields (habitat/temperament/size/legs/
     wings/horns/segments/eyes/colour/glow + a **hide texture** from **12** grayscale tiles); `CreatureBuilder`
     renders the blocky body + tints it. So **more fauna variety = more hide textures + richer generator rolls**.
   - **Sound already has real recordings** (~112 clips in `client/Assets/Resources/audio/`): **12 signature creature
     calls** (chirp/croak/growl/…) + **6 size×disposition voice banks × 5 states** + 6 biome ambiences; a procedural
     synth is only the fallback. So **more fauna audio = add new call types** (1 ElevenLabs clip each, listed in
     `CreatureView`) — the most-heard creature sound.
   **Pipeline:** OpenAI textures via `gen_textures.py` (blocks/flora) + `gen_creatures.py` (grayscale hides) →
   `bundle_textures.py` → `client/Assets/Resources/textures/*.bytes`; ElevenLabs via `gen_sound.py` →
   `client/Assets/Resources/audio/*.mp3` (auto-loaded by key). Guards: `FloraTests`, `CreatureTests`,
   locale-parity, content-load.
   **Plan:** (1) **+ new flora archetypes** — new `flora_*` blocks across biomes (palm/moss/succulent/pitcher/
   puffball/lichen/coral/sporepod…), each block + `FloraCatalog` hosts + OpenAI texture + DE/EN name; (2) **+ new
   creature hide textures** — new grayscale tiles + add them to `CreatureBuilder`'s hide-selection pools so the
   procedural creatures actually use them; (3) **+ new creature calls** — new ElevenLabs call clips added to
   `CreatureView`'s call list (and optionally a wider colour/body roll in `CreatureGenerator`). Test + build + commit.
   **Scope/counts to confirm with the user before generating.**
18. **Analysis only — make a world more spherical (vertical wrap too).** *(Analysis only — do NOT implement.)*
   Analyse and estimate: how could a world be circumnavigated **not only horizontally but also vertically** — more
   like a sphere. Assess what's **realistically possible**, weighing **complexity and performance cost**.

   ### Analysis + estimate (2026-06-07) — no implementation
   **Today the world is a CYLINDER (proven by Task 2/2b):** X is a **wrapping longitude** (`WorldConstants.
   Circumference`, per-body, seam-free via circular-domain noise `Noise.FbmCylX`/`ValueCylX`), the server wraps
   the player's X and the client renders the nearest wrapped copy (`SceneX`); **Z (latitude) is NOT wrapped** —
   it's bounded by an invisible **pole barrier** (`WorldConstants.LatitudeLimit`); **Y is unbounded**. Chunks are
   3D, X-canonicalised in the chunk key. The **orbit/star-map view already draws bodies as spheres** (`BodySize-
   Scale`/`OrbitDiameterFor`) — so the *space view is fine regardless*; only the **walkable surface topology** is
   the question. ~30 sites depend on the wrap helpers (`WrapX`/`WrapDeltaX`/`Canonical*`/`WrapDistanceSquared`).

   **Three realistic options (cheap → expensive):**
   - **(A) Torus — wrap Z like X (cheapest, ~days).** Mirror the entire X-wrap machinery on Z: a `CircumferenceZ`,
     circular noise in Z, Z-canonical chunk keys, `WrapDeltaZ`/`WrapDistance` over both axes, client `SceneZ`,
     drop the pole barrier. You can then circle **both** ways seam-free. **Cost:** moderate, well-understood
     (it's the X work again). **Perf:** identical to today. **Catch:** topologically a **donut, not a sphere** —
     no poles; "south" loops straight back to "north". Functionally satisfies "circle vertically too", but it
     isn't really spherical.
   - **(B) Pole-pinch cylinder (moderate, ~1-2 weeks).** Keep the cylinder grid, but at the latitude limit the
     player **crosses the pole** instead of hitting a wall: a coordinate transform flips them to the antipode
     (X += half-circumference, Z direction reversed). Gives genuine **sphere-like traversal** (over the poles and
     back) on the existing grid with **no grid rewrite**. **Perf:** identical (a transform at the seam). **Catch:**
     the grid doesn't converge at the poles (a pole is a *line*, not a *point*), so there's a visible **pole-seam
     line** + mild distortion near it; needs careful handling of movement/rendering/structures crossing the seam.
   - **(C) True sphere — cubed-sphere voxel world (expensive, multi-month rewrite).** Map the surface to a
     **cubed-sphere** (6 face-grids with edge seams) so longitude truly converges at poles. **Touches everything:**
     chunk addressing, meshing, physics/movement, noise domain, persistence keys, structure stamping, the wrap
     helpers — a near-total worldgen/engine rewrite. **Perf:** workable but heavier (face-seam handling, non-
     uniform cells). **Verdict: not worth it** for this game's scope.

   **Recommendation:** if "circumnavigate vertically too" is the goal, **(A) torus** is the pragmatic win
   (cheap, zero perf cost, reuses the X machinery) — accept that it's a donut. **(B) pole-pinch** is the choice
   if it must *feel* like a sphere (poles exist) and a ~1-2 week effort is acceptable. **(C) true sphere is out of
   scope.** Either way the orbit/star-system view needs no change. *(Estimate only — confirm direction before any
   build.)*
19. ✅ **Feature — bigger HUDs + weaker visor + menu matches HUD look (done 2026-06-07; ✅ confirmed good in-game).**
   All four parts shipped (client-only; ~1.25× HUD, visor ~0.6, translucent menu, all chosen with the user):
   **(a) Bigger HUDs** — `UiKit.CreateCanvas`/`CreateDiegeticCanvas` take an optional reference resolution; the
   **HUD canvases** (person `HudUi` + `SpaceView` flight overlay) now use **1536×864** (`UiKit.HudRefW/HudRefH`)
   so ScaleWithScreenSize draws them **~1.25× larger**, while the **menu stays at 1920×1080** (its absolute layout
   intact). Expand match mode keeps it fitting 16:9/ultrawide/4K. **(b) Weaker visor** — base `_Intensity` 1→**0.6**
   (preset 0.5→0.4) plus softer `_Curvature` 0.11→0.07, `_Chroma` 0.022→0.011, `_Reflect` 0.14→0.10, `_RimIntensity`
   0.2→0.13 (glow/opacity kept for readability). **(c) Menu look** — the menu backdrop alpha **0.96→0.6** so it
   reads as a translucent holographic overlay over the world/HUD (panels already use the HUD cyan palette); kept
   flat (not routed through the visor — that would misalign clicks). Client rebuilt. *(Exact magnitudes want a
   playtest — all are single constants, easy to nudge.)*
   - ✅ **19d — Menu "visor glass" helmet look (done 2026-06-07; ✅ confirmed good in-game).**
     The menu was flat (no helmet) because routing it through the visor would **displace clicks** (the shader
     bends the image). Chosen with the user: give the menu the **helmet look without the curvature** — a new
     additive **`Spacecraft/VisorGlass`** shader (cyan fresnel rim glow + faint animated scanlines + top glint,
     no barrel warp; registered always-included) drawn as a **click-through** (`raycastTarget=false`) top-most
     full-screen overlay via **`VisorMenuGlass.Add`**, only when the visor pipeline is active. So buttons stay
     exactly where they're drawn. Client rebuilt; needs a playtest (rim-glow strength is the `_RimIntensity`/
     `_Intensity` knobs).
   ### Analysis (2026-06-07)
   - **HUD sizing.** All UI uses `UiKit` `CanvasScaler` ScaleWithScreenSize, **ref 1920×1080**, match=Expand.
     `HudUi` (person HUD, **diegetic** → visor RT) positions right/bottom elements with **absolute** `W/H=1920/1080`
     consts; `SpaceView` flight HUD uses **screen-edge anchors** (auto-scales); the **menu** (`CraftingTechShipUI`)
     uses **absolute 1920 coords** (full-screen). So a *global* ref drop would enlarge the HUDs **but push the
     menu's absolute layout off-screen**. → Give **HUD canvases a lower reference** (e.g. 1536×864 ≈ 1.25×, bigger)
     and keep the **menu at 1920** (already full-screen). Add an optional ref param to `UiKit.CreateCanvas`/
     `CreateDiegeticCanvas`; set `HudUi.W/H` + the SpaceView overlay to the HUD ref. Expand keeps it fitting on
     16:9/ultrawide/4K.
   - **Visor strength.** `VisorHud.OnRenderImage` sets `_Intensity=1` + `_Curvature 0.11`, `_Chroma 0.022`,
     `_RimIntensity 0.2` (scanlines/rim/reflection scale by `_Intensity`). → lower base intensity + chroma +
     curvature + rim for a subtler bow/fringe, keep glow/opacity for readability.
   - **Menu look.** Menu panels already use the HUD cyan palette (`UiKit.Panel`/`Cyan`), but the menu has a
     near-opaque backdrop (`0.96` alpha) and is a flat overlay. Routing it through the **visor would misalign
     clicks** (the shader warps/curves) — so keep it flat but **drop the backdrop alpha** so it reads as a
     translucent holographic overlay like the HUD. (Magnitudes confirmed with the user before building.)
   - **Loading screens (item 21, bundled in this pass).** `WorldLoadingOverlay` **skips the veil when the world is
     already ready** (fast/cached load) and `MinShow=0.7`. → remove the skip + set `MinShow=3.0` so it **always**
     shows ~3s on planet-landing **and** station-board (shared overlay).
20. **Feature — build in space + erect a player-built space station (requested 2026-06-07).** *(ANALYSIS FIRST —
   do NOT implement yet; analyse the current state, then plan, then ask, then build.)* Players should be able to
   **build in space** by placing **individual blocks**, and once the structure is **closed and/or has an airlock**
   it should **become a space station**. Such stations must be **persisted in the star system** and **accessible**
   to the player (boardable later). **Required analysis before tackling it:** the **as-is** behaviour — how the
   **station interior vs. exterior** work today (interior = a void-world clone of `orbital_station` stamped from a
   template / `GameServerSpaceStations`, `BoardableStation`, markers; exterior = a body/backdrop in the **space
   flight view** + an entry on the **star map**), and **whether/how this can fundamentally work** or **what would
   need to change** in the **space + star-system view**. Key open problem to call out: **space today is a
   non-voxel `SpaceInstance`** (asteroids/entities + a parked ship), **not a placeable voxel world** — so
   "placing blocks in space" needs either a buildable voxel volume in the flight/EVA view or a different model
   (e.g. build the station in a void world, then register + persist it as a new boardable body in the system).
21. ✅ **Feature — loading screens always show + stay readable ≥3s (done 2026-06-07; needs in-engine test).**
   `WorldLoadingOverlay` (the shared planet-landing + station-board veil) used to **skip** when the world was
   already ready (fast/cached load) and only held `MinShow=0.7s`. Removed the skip-when-ready early return so the
   veil **always** raises after the descent, and set **`MinShow=3.0s`** so it stays up long enough to read on both
   transitions (`WorldReady` now only gates the fade-out, with the 25s safety cap unchanged). Client rebuilt.
   *(Done together with item 19.)*
22. **Analysis + plan — immersive tutorial / onboarding mode (requested 2026-06-07).** *(ANALYSIS + PLAN ONLY —
   do NOT implement yet.)* Design how a **tutorial mode** could **introduce a new player**: in-game **tips** that
   teach the **basic functions** (move/look, mine, place, craft at a station/workbench, inventory/hotbar, the suit
   vitals/oxygen, flying the ship, landing, scanning, missions, …). It should be **immersive**, not a wall of
   pop-ups — e.g. the player's **ship AI** speaks the tips in-character (voiced/text lines triggered by context:
   first time you open the inventory, first low-oxygen warning, first ore mined, first station approach, etc.).
   **Analyse:** what onboarding exists today (does anything explain controls? the HUD prompts?), what the natural
   first-session beat sequence is, how to gate/trigger tips by player state + first-time events (a persisted
   "seen" set), how to present them (a diegetic ship-AI voice line + subtitle vs. a HUD toast), localisation
   (DE+EN), and a skip/replay option. **Plan** a staged, low-risk first version. *(Pairs with item 23 — the ship
   AI is the natural narrator.)*
23. **Analysis + plan — the ship AI as a recurring in-game character / system (requested 2026-06-07).**
   *(ANALYSIS + PLAN ONLY — do NOT implement yet.)* Plan a **ship AI** companion as a persistent presence and
   what **role it could play across future expansions**, beyond the tutorial (item 22). **Explore ideas:** a named
   on-board AI that comments on events (arrivals, danger, discoveries, low resources), gives **mission/quest
   hints + lore**, reads out **scan/sensor** results, warns about **hazards** (toxic atmosphere, hull/oxygen),
   manages **ship systems** as an in-fiction interface (travel, modules, fleet), reacts to the player's choices
   (a light **personality / relationship** track), and could later branch into **story beats**. **Analyse** how it
   would hook into existing systems (events the server already raises — arrivals, combat, scans, missions, vitals;
   the audio/voice pipeline; the HUD), the **content/voice** cost (lines + DE/EN + generated VO), and how to keep
   it **non-annoying** (frequency caps, mute/skip). **Plan** an incremental rollout (tips → event barks → systems
   narrator → story) so each step is shippable on its own.

### Small "rest" TODOs surfaced from completed items (promoted here 2026-06-07 for visibility)
*(These were implicit/deferred notes buried inside already-✅ items — captured as explicit backlog entries.)*
24. ✅ **Large landable asteroids (done 2026-06-07; needs in-engine test).** **Implemented:** worldgen now
   places **2–3 large landable asteroid bodies per system** (`UniverseGenerator` — `CelestialKind.AsteroidField`
   with **`PlanetType = "asteroid"`**, scattered orbit positions, ids `{system}-a{0..2}`), so each is **Asteroid
   size-class** → a defined small walkable world (circumference 800–1600 per id) and the existing
   `EvaLandingAllowed` guard permits it. The flight view (`SpaceView.BuildSystemBodies`) gained a **third loop**
   that renders `AsteroidField` bodies + adds them to `_landables`, so you **fly up + press E to land** — with
   the **ship or on an EVA** (travel/land already route through `LoadWorld(body.PlanetType, …)`; planets/moons
   stay EVA-rejected). The **small mineable rocks** are unchanged (space entities you shoot/harvest). Test
   `Universe_GeneratesLargeLandableAsteroidBodies_PerSystem` (2–3 per system, all asteroid-class) — 365 green;
   client + bundled server rebuilt. *(Original analysis below.)* *(from item 5, Stage 6; re-scoped 2026-06-07 — MEDIUM,
   not small).* **Finding on closer look:** there are **no discrete walkable asteroid bodies** — a system has at
   most one **asteroid *belt*** (`CelestialKind.AsteroidField`, no `PlanetType`, rendered in space as mineable
   rock *entities*). `RestoreJoinBody` already treats an `AsteroidField` as a travelable body, but it has no
   `PlanetType`, so `SizeClassFor` doesn't classify it as `Asteroid` and the flight view's **`_landables`** only
   loops **planets + moons**. So landing on an asteroid needs: **(1) worldgen** — give the `AsteroidField` body
   the **`asteroid` `PlanetType`** so travel/land loads the walkable asteroid world (+ `SizeClass=Asteroid`);
   **(2) client** — add an `AsteroidField` loop to `SpaceView._landables` (+ approach/land prompt + descent);
   **(3) server** — allow ship-landing on it (the generic land-to-body flow). **Design nuance to resolve:** the
   belt is *both* a field of mineable rocks in space **and** (after this) a landable asteroid world — decide how
   they coexist (e.g. land = descend to a walkable asteroid; staying in space = mine the rocks). Touches worldgen
   + flight view + server + needs an in-engine playtest. **Decision (user 2026-06-07): landable with the ship too**
   (like planets/moons), not EVA-only. *(Was mislabeled "small"; it's a medium feature — pick it deliberately.)*
   **Clarified design (user 2026-06-07):** **two asteroid scales.** Keep the **small** asteroids as mineable rocks
   you **shoot + harvest from the ship** (already exist as space combat entities). **Add bigger asteroids** with a
   **defined size (in the world generator)** that you can **land on** — **with the ship or via EVA** — descending
   onto a walkable asteroid world. So small = mine-from-ship (existing), large = a sized **landable** body (new).
   ### Analysis + plan (2026-06-07)
   **As-is (mapped):** small asteroids = `CombatEntity Kind=Asteroid` tiers 0/1/2 spawned in the per-body
   `SpaceInstance` (`CreateSpaceInstance` spawns 3; `RespawnAsteroids` refills) — shoot to split + harvest loot
   (works from the ship). A system has one **`AsteroidField`** belt body (no `PlanetType`). Landing flow:
   approach a `_landables` body → **E** → `SendLeaveSpace(bodyId)` → server `HandleTravel`/`LeaveSpace` →
   `LoadWorld(body.PlanetType, body.Id)`; the walkable world's size = `CircumferenceFor(id, SizeClassFor(kind,
   planetType))` (Asteroid class = **800–1600** blocks, deterministic per id). **EVA guard** `EvaLandingAllowed`
   already permits only `WorldSizeClass.Asteroid`. **Gap:** the `AsteroidField` body has no `PlanetType`, so it's
   not Asteroid-class and never appears in `SpaceView._landables` (planets+moons only) → unreachable.
   **Plan (small, mostly wiring):** (1) **worldgen** — `AsteroidField` body gets `PlanetType = "asteroid"` (→
   Asteroid size-class → defined walkable size 800–1600; EVA guard + travel then work as-is); (2) **client** —
   add an `AsteroidField` loop to `SpaceView.BuildSystemBodies` so the belt renders + is in `_landables` (ship +
   EVA can fly up + **E** to land), rendered with the `asteroid` look; (3) **bigger/sized large asteroids** — see
   the open questions; (4) tests (lands from ship + EVA; planets/moons still EVA-rejected; world loads as
   asteroid-class). Small mineable rocks stay as the space-instance entities.
   **Open design questions (ask the user):** how many **large landable** asteroids per system (just the one belt
   body, or several scattered)? and the **size** model — the auto deterministic 800–1600 per id, or an explicit
   small/large tier (a new size field in worldgen)?
25. ⏭️ **SKIPPED (user 2026-06-07) — covered by item 6a + the fixed landing zone.** You already land at the same
   persisted landing zone (with your ship) on every visit, and a reload restores your current body + position, so
   a per-body position map adds little and would *separate you from your just-landed ship* on travel-back. Left
   out by decision. *(Original below for reference.)*
   **Full per-planet position memory (option b)** *(from item 6).* Today only the **last** planet's position is
   restored on load (`PlayerState.CurrentLocationId` + `Position`). Option b = keep a **per-body position map** so
   travelling **back** to a previously-visited body drops you where you left it (instead of the landing-zone
   spawn). **To do:** persist a `{ locationId → position }` map on the player snapshot; on arrival at a visited
   body, restore that position (still guarded by `EnsureSafeSpawn`); update it on leave/checkpoint. Server-only,
   testable.
26. ✅ **Intermediate ship tier — Corvette (done 2026-06-07).** Added a balanced mid-class **corvette** between
   scout (fast/light) and hauler (slow/cargo): baseHull 130, baseShield 45, flightSpeed 1.1, handling 1.1, cargo
   60; `requiredBlueprint: ship_corvette` (data_fragment 4 + titanium_plate 11 + cable 5); craftCost on the new
   materials (titanium/iron plate + cable + energy_cell + **steel** + **light_alloy** + **circuit_board**); DE/EN
   names. Data-only — 364 green (consistency + locale parity). *(Original note below.)*
   **Intermediate ship tier** *(from item 16, Stage 4).* Stage 4 folded the new alloys/electronics onto the
   existing ships (starter/hauler/scout) but **deferred a genuinely new mid-tier ship** between them. **To do
   (data-only):** add 1–2 intermediate ships (stats + craftCost on the new materials + blueprint + bilingual
   names + texture not needed since ships are voxel-built). Small + balance-driven. **Decision (user 2026-06-07):
   a balanced **corvette** between scout (fast/light) and hauler (slow/cargo) — mid hull/shield/cargo/speed.**
27. **Already-listed future work (see "Not started / larger future work" near the end):** **W5 — poles**
   (bound latitude Z with an ice-wall/barrier biome; relates to item 18), **per-species/planet flora colour
   tint** (a `ChunkMesher` tint-UV pass, requested 2026-06-06), **texture audit**, **uGUI theme/icon polish**.
   Kept in that section; pointer here so they're not forgotten. *(PvP ship combat + big cruisers stay **deferred
   by design**.)*
28. ✅ **Bug — multi-second freeze after "start new game" before the loading screen (done 2026-06-07; needs
   in-engine test).** Clicking Singleplayer → entering a new name / starting a new game → **several seconds of
   nothing**, *then* the loading screen (percent bar) appears.
   **Fix (done):** the loading screen is now shown **first** and the local server is spawned **off the main
   thread**, so the blocking `Process.Start` can never freeze the menu. `LocalServerLauncher` was split into
   **`Prepare`** (main thread — builds the launch info from Unity paths) + **`LaunchPrepared`** (thread-safe —
   does `Process.Start`); `AppShell.StartSingleplayerWorld` now calls `Prepare` + sets `Phase = Loading`
   immediately, and `Update` kicks `LaunchPrepared` on a `Task` once the loading UI is on screen. The client's
   existing connect-retry (`GameBootstrap` — every 2s, up to 6×) covers the case where the server isn't listening
   yet, so no launch gate is needed; `StopLocalServer` waits for an in-flight spawn so backing out can't orphan a
   server. *(The percent bar is still the time-based `MinShow` timer — driving it from real load stages is a
   future polish, noted below.)* Client rebuilt.
   ### Analysis (2026-06-07) — root cause
   Two things combine; both are in `AppShell.StartSingleplayerWorld` + `LocalServerLauncher` + `LoadingScreen`:
   - **(1) The loading screen is only shown *after* the server is spawned, and the spawn can block the UI
     thread.** `StartSingleplayerWorld` calls **`_localServer.Start(...)` synchronously** and only **then** sets
     `Phase = ShellPhase.Loading` (`AppShell.cs:157,169`). `Start()` itself just does `Process.Start()` on the
     bundled **self-contained .NET server EXE** (`LocalServerLauncher.cs:133`) — normally fast, **but on Windows
     the first launch of a freshly-built EXE is commonly stalled for seconds by Defender/SmartScreen real-time
     scanning** (the EXE changes on every `build-client.ps1`, so the scan re-runs). While `Process.Start()`
     blocks, the **menu is frozen** and the loading screen hasn't been built yet → "nothing happens".
   - **(2) The percent bar is fake + a fixed warm-up wait.** `LoadingScreen.Progress` is **purely time-based**
     (`_elapsed / MinShow`, `MinShow = 2.5s` set explicitly to "give the server time to start listening",
     `AppShell.cs:161`); it is **not** real world-load progress. After the 2.5s timer `LaunchGame()` runs, the
     client connects, joins, and streams the world. The bundled server's **cold start** (runtime init + content
     load + open/create the new world's SQLite DB + generate the spawn region + bind the socket) takes **~3s**
     end-to-end — server log: process at `13:41:39`, listening `13:41:40`, client connect `13:41:42`, join
     `13:41:43`.
   **So the perceived gap = the blocking `Process.Start()` (Defender first-scan) + the fixed 2.5s warm-up, none
   of which shows real feedback.** **Fix direction (when tackled):** (a) set `Phase = Loading` and render the
   loading screen **before** spawning the server (one frame), then start the server + connect on a coroutine so
   `Process.Start()` never freezes the menu; (b) drive the bar from **real stages** ("starting server →
   connecting → generating world → streaming chunks") instead of a fixed timer, and drop `MinShow` once real
   progress exists; (c) optionally reduce the Defender hit (don't rebuild the server EXE when unchanged / sign it
   / document a Defender exclusion for the saves+server dir). *(Analysis only — not yet implemented.)*

---

## 🐞 Reported bugs — triaged 2026-06-07 (sorted into the backlog; **not yet fixed**)
A batch of user-reported issues. Each checked against the code for validity + a code pointer + fix direction.
Tags: **VALID** (confirmed in code), **PARTIAL** (partly true / nuance), **FEATURE** (a new system, not a bug),
**PLAYTEST** (needs in-engine confirmation).
**✅ Fixed 2026-06-07 (quick tunables, 365 green, built):** B1 (audio `maxDistance 45→20` + Linear rolloff),
B5 (hard-block hardness ×1.6 — stone 3.8→6.1, ores/metals up; soft blocks unchanged), B9 (asteroid respawn
40→120s), B10 (asteroids scattered by golden-angle within weapon range), B13 (machete `damage 12→7`), B18
(temperament weights → Aggressive 15→9 / PackHunter 5→3 + aggro range 10→8). *Each still wants a playtest.*
**✅ Fixed 2026-06-07 (second quick batch, 365 green, built):** B23 (slide-door `OpenRange 4.5→2.8` so station/
village doors close again; ship hatch stays 1.8), B3 (place guard now only rejects the **head** cell, so you can
pillar-jump by placing at your feet), B16 (right-click a held **consumable** → eat: wires up the existing
`SendConsume` which was defined-but-never-called + a new ElevenLabs **eat** sound), B19 (volcanic lava abundance
`0.55→0.7` so lava pools are higher/more visible). *Each still wants a playtest.*
**✅ Fixed 2026-06-07 (medium batch, 365 green, built):** B2 (dedicated `asteroid_rock` OpenAI texture on the
mineable rocks), B20 (avatar face made clearer — bigger eyes + a mouth; it already *had* eyes/visor, so the
report was likely a pre-fix build), B17 (rounded **sphere** creature eyes — bigger, dark pupil + a white glint),
B12 (organic **meandering** wander — hold a hashed heading per segment + weave, instead of milling in tight
circles), B14 (weapon-aware attack animation — blade **slash arc**, gun **recoil**, else jab), B4 **partial**
(ship editor: added **Hinged Door**, relabelled **Medbay → "Medbay (Heal-Tank)"** + "Sliding Door"). *Playtest
wanted.* **B4 remainder + B22 still open** — see those entries.
**✅ Fixed 2026-06-07 (B21 — damage feedback, built):** on-foot **red screen flash** + a **cause label** in
`HudUi` whenever health drops, cause inferred from local state (in **lava** → "Burning", O₂≤0 → "Suffocating",
hunger≤0 → "Starving", else "Taking damage"), DE+EN. No more dying "out of nowhere" — you see the flash + why.
Client-only. *Playtest wanted.*

- **B1 — Creature sounds carry too far; should fade with distance, be silent when far. [✅ FIXED 2026-06-07 — playtest]**
  `ClientAudio.At` plays 3D at `minDistance 4`, `maxDistance 45` with Unity's default (logarithmic) rolloff →
  audible from far. *Fix:* lower `maxDistance` (~18–22) + use a Linear/custom rolloff so distant creatures fade
  to silence. (`ClientAudio.cs:316-329`.) Relates to [[netcodec-register-messages]]-era audio work.
- **B2 — Small mineable asteroids need their own texture. [✅ FIXED 2026-06-07 — playtest]** They're **not** untextured — `AsteroidMat()`
  reuses the **stone** block texture tinted grey (`SpaceView.cs:2027`). *Fix:* generate a dedicated asteroid-rock
  texture (OpenAI — **pre-approved**) + use it. ([[asset-generation-approved]].)
- **B3 — Can't pillar-jump (place a block under yourself). [✅ FIXED 2026-06-07 — playtest]** `HandlePlace` rejects placing in the cell you
  occupy — feet `fy` or head `fy+1` — "You can't place a block where you're standing" (`GameServer.cs:1586`); a
  jump-place targets your feet cell → rejected, so it seems to "break instantly". *Fix:* allow placing at the feet
  cell while airborne (keep the head-cell guard) so building straight up works.
- **B4 — Ship editor missing usable items; audit ship + station + settlement editors. [PARTIAL]** The ship-editor
  palette **does** include **Medbay** (the heal-tank) + a **Door** (door_slide) (`ShipEditor.cs:59-78`) — "no
  heal-tank" is a labelling/visibility issue. But newer placeables are missing (e.g. **crate**, **door_hinge**,
  workbench/forge, modules like refinery/detoxifier/oxygen_generator/cargo_hold_1). *Fix:* audit + complete the
  ship palette, and do the same for the **station** + **settlement** editors. **Partly done 2026-06-07:** added
  **Hinged Door** + relabelled Medbay/Door in the ship editor. **Remainder:** airlock/lab/console/crate in the
  ship editor (needs an editor→stamp round-trip check that designed-ship station cells register correctly), and
  the **station + settlement (structure) editors** audit.
- **B5 — Stone (and other) mining still too fast. [✅ FIXED 2026-06-07 — playtest]** Stone `hardness 3.8` (`blocks.json`); the
  drill's mining power clears it quickly. *Fix:* raise stone + rock/metal hardness (and/or lower tool power) and
  rebalance the hardness table for a slower dig.
- **B6 — Plants are a solid textured cube; want transparent leaves (also tree crowns). [FIXED 2026-06-07 — real
  alpha-cutout leaves]** `ChunkMesher` rendered `tree_leaves` + `flora_*` as full opaque cube faces. **User chose
  real alpha leaf textures** (over procedural shader holes / translucent blend). Implemented with the actual leaf
  art (no normal-map artefacts, holes follow the real leaf shapes):
  - **Bake** (`tools/ai-assets/bake_leaf_alpha.py`): the block atlas was already RGBA32 + preserves tile alpha,
    but foliage tiles shipped fully opaque. The script derives an alpha mask from each tile's OWN art (the
    darkest ~34% — the shadow gaps between leaves/petals — become transparent), leaving RGB untouched so the
    Sobel normal atlas stays clean. Rewrote the 20 foliage `.bytes` in place (`tree_leaves` + leafy `flora_*`;
    excludes structural/glowing flora: cactus, crystal, succulent, mushroom, puffball, pitcher, glowcap,
    emberbloom, sporepod, glowvine — and never the ground `grass`).
  - **Mesher**: a per-vertex **foliage flag** (new uv2.x) marks leaf faces (`IsFoliageBlock`, set kept in sync
    with the bake script). Opaque blocks now also draw faces toward foliage neighbours (`nbSeeThrough`), so you
    see leaf layers + the block behind through the holes (not a hollow shell). Leaves still collide.
  - **Shader** (`BlockAtlas.shader`): leaf-flagged faces `clip(texel.a - _LeafCutoff)` (default 0.5) — every
    other tile is fully opaque and unaffected. No new shader/submesh/material (alpha-test lives fine in the
    opaque queue → no GraphicsSettings always-include needed). Shader compiles clean; client build verified.
  - *Playtest:* check crown/bush density + whether distant foliage thins too much (alpha-test + mips); tune
    `--hole` (bake) or `_LeafCutoff` (shader) if so.
  - **Follow-up 2026-06-07 (first build still looked solid):** three fixes — (1) **atlas overflow**: the block
    atlas was only 8×8 = 64 tiles but `data/blocks.json` has **80 blocks**, so every block id ≥ 64 (newer flora +
    doors) got an untextured grey, alpha-less tile (also no cutout). Bumped to **16×16** (256 slots). (2) Holes
    were baked from the darkest *single pixels* → scattered sub-pixel specks that mip-average back to opaque;
    re-baked on a **coarse 16×16 grid** so holes are chunky, visible gaps. (3) Reverted the "draw faces toward
    foliage" culling back to a **thin shell**, so the holes show the sky/world BEHIND the tree (a dense volume
    just showed more leaves through the holes → read as solid).
- **B7 — How often is there water; lakes/ponds or only seas? [FIXED 2026-06-07 — upland ponds]** Worlds only had
  seas (basins flooded to one global level); no isolated lakes/ponds, and swim-deep water was rare (B26).
  **Carve-and-fill upland ponds** in `WorldGenerator.Generate`: per column **above** the sea, a low-frequency
  pond-mask noise on **flat ground** (slope-gated so the water sits level) carves a shallow bowl
  (`seabedY = surfaceY − depth`, depth tapering 0→5 rim→centre) and fills it with water up to the original
  surface — a pond flush with the ground, 2–5 deep, so B26's `IsSubmerged` swimming actually triggers. **Frequency
  derives from the world's `WaterAbundance`** (the same property that sets the sea level — the user's ask): wet
  worlds get more/larger ponds, dry worlds almost none, lava/airless worlds none (their sea isn't water). Pond
  columns grow aquatic flora (kelp/lily) not land plants. Deterministic (pure noise). Tuned scattered/rare
  ("water rare except oceans"); regression test `WateryWorld_GeneratesUplandPonds_AboveSeaLevel`; the atmosphere
  tests were made pond-robust (find the open-air surface, since a pond can now sit at the origin). 368 green; build
  verified. *(Knobs: `pondThreshold`, `PondMaxDepth`, `PondBand`, `PondMaxSlope` in `WorldGenerator`.)*
- **B8 — Thunderstorm has thunder audio but no visible lightning. [FIXED 2026-06-07 — weather Stage 3]** The faint
  pre-existing screen flash (`_flash*0.35`, no bolt) was the reason "no lightning showed". `WeatherFx` now: a
  **brighter** sky flash (`_flash*0.6`) **plus a jagged white bolt** (`DrawBolt`, glow + hot core, ~0.18 s) drawn
  from the top of the screen down to mid-screen on each strike; strikes every 4–11 s. Gated to **rain
  thunderstorms only** (`Weather=="storm" && Precipitation=="rain"`) — no bolts in blizzards/sandstorms/ashfall;
  thunder audio gated the same way (`ClientAudio.cs`).
- **B9 — Mined asteroids respawn too fast. [✅ FIXED 2026-06-07 — playtest]** `AsteroidRespawnInterval = 40s`, target 3
  (`GameServerSpaceCombat.cs:765-766`). *Fix:* raise the interval.
- **B10 — Mineable asteroids too clustered; spread them around the planet. [✅ FIXED 2026-06-07 — playtest]** `CreateSpaceInstance` spawns
  the 3 at a fixed near-line `(12 + i*8, 0, 18)` (`GameServerSpaceCombat.cs:327`). *Fix:* scatter them over a
  wider volume around the body. *(Note: pairs with item 24's new large landable asteroids.)*
- **B11 — Per-planet temperature + climate precipitation. [✅ DONE 2026-06-07 — Stages 1–3; playtest]**
  **✅ Stage 1 done 2026-06-07 (weather pass):** added `PlanetType.BaseTemperature` (per type in `planets.json` —
  lava 115, ice -38, desert 44, jungle 28, rocky 12, crystal 4, swamp 22, varied 16, asteroid -25, void 20) +
  `GameServerWeather.CurrentTemperature`: base + a **per-world seeded variation** (±14 → "especially hot/cold"
  worlds) + a weather cooling (storm -8/rain -5/clouds -2/clear +2) + a **day↔night swing** (±6 with air, ±16
  airless). Networked via `WorldEnvironment.Temperature` (+ a `Precipitation` field) and shown in the HUD
  (time-of-day panel, °C). **User refinement:** ship/station cabins read **22 °C**; **vacuum** worlds
  (space-sky asteroid/crystal) + **above the atmosphere** read **"—"**. `PrecipitationFor` classifies
  none/rain/snow/hail/ash/**sandstorm** by temp + surface block (server-side; deserts now `weather:"dynamic",
  stormChance 0.18` → sandstorms). 365 green.
  **✅ Stage 2 done 2026-06-07:** `WeatherFx3D` now renders by `env.Precipitation` (gated on `Precipitation!="none"`)
  with a per-form `Style` — **rain** (blue streaks), **snow** (white, slow, sideways wobble), **hail** (grey, fast),
  **fire-ash** (glowing orange, slow, drifting), **sandstorm** (tan horizontal streaks). Fog tinted per form
  (sandstorm = dense tan blind, ash = smoky, snow/hail = white-out, rain storm = grey-blue). `WeatherFx` screen wash
  tinted per form; 2D streaks limited to actual rain. Audio (B34): two new ElevenLabs loops `sandstorm_loop` +
  `ash_loop`; `ClientAudio.OnEnvironment` picks the bed by precip (sandstorm/ash/snow+hail→`wind_strong`/rain/storm).
  **✅ Stage 3 done 2026-06-07:** lightning flash + bolt — see B8.
- **B12 — Creature movement too simple (a few steps back-and-forth). [✅ FIXED 2026-06-07 — playtest]** Wander runs through
  `CreatureBehaviour.Step` (`GameServerCreatures.cs:352`); the wander is basic. *Fix:* richer roaming — varied
  headings, pauses/grazing, longer wanders, maybe light flocking.
- **B13 — Machete one-shots most animals. [✅ FIXED 2026-06-07 — playtest]** Machete `damage: 12` (`items.json`) vs creature
  `MaxHealth = 10 + size*8 (+10 if hostile)` (`CreatureGenerator.cs:78`) → small fauna ~15 HP die in one hit.
  *Fix:* lower machete damage or raise creature HP.
- **B14 — Weapon attack animations are poor. [✅ FIXED 2026-06-07 — playtest]** One generic `Swing()` for everything
  (`PlayerController.TriggerSwing` → Avatar/viewmodel `Swing`). *Fix:* better per-weapon-class swing/thrust/fire
  animations.
- **B15 — A "red creature" of two stacked blocks with no texture — what is it? [PLAYTEST]** There is **no**
  lava/2-block entity in space combat; it's almost certainly a **fauna creature** rendered **red** (hostile
  creatures get a red tint in `CreatureBuilder`) with a **missing/failed hide texture** (untextured → flat red)
  and/or a minimal body. *Fix:* confirm in-engine (scan it), then fix the hide fallback / body so a creature never
  renders as bare red cubes.
- **B16 — Can't eat food. [✅ FIXED 2026-06-07 — playtest]** The server has `ConsumeItemIntent` + `HandleConsume`/`ConsumeItem` (food
  heals) (`GameServer.cs:1101`, `GameServerCreatures.cs:498`), but the **client never sends it** (no eat key/UI).
  *Fix:* add a client eat action (a key, or an inventory "eat") that sends `ConsumeItemIntent` for the selected
  food. *(Hunger has a separate auto-consume path, `GameServer.cs:747`, but manual eating is unreachable.)*
- **B17 — Creature eyes look bad (tiny). [✅ FIXED 2026-06-07 — playtest]** Eyes are small flat-coloured quads + pupil
  (`CreatureBuilder.cs:79-94`, eyeSize ≈ 0.24·headScale). *Fix:* bigger/rounder eyes, better shading/highlight.
- **B18 — Creatures still too aggressive. [✅ FIXED 2026-06-07 — playtest]** Despite item 7's give-up leash, the temperament roll
  still makes hostiles (Aggressive 15 / PackHunter 5 weights in `CreatureGenerator`) + the aggro range bites.
  *Fix:* lower the hostile weights / aggro range / damage, or make more species flee. Pairs with B13.

### Second batch (reported 2026-06-07)
- **B19 — No lava on lava planets? [✅ FIXED 2026-06-07 — lava abundance bumped; playtest]** The code **does** generate a lava sea: the lava planet's
  `surfaceBlock "basalt"` → `volcanic` → `lavaAb 0.55`, and `WorldGenerator.ResolveSeaFluid` pools **lava in
  basins** below ~`BaseHeight − 0.25·Amplitude` (≈ Y 50 on the lava world). So lava only fills **low ground**;
  on high terrain / the landing zone you may see none. *Investigate in-engine:* dig down to ~Y 50 / find a basin;
  if still none, it's a gen/render bug. *(Possible tweak: raise `lavaAb` so lava is more visible.)*
- **B20 — Player avatar has no face. [✅ FIXED 2026-06-07 — bigger eyes + mouth; playtest]** The procedural avatar has no facial features (eyes/visor). *Fix:*
  add a face — at least eyes / a helmet visor — to the avatar model (`AvatarBuilder`/`AvatarEditor`).
- **B21 — "Died suddenly with no idea why" — audit the damage indicators. [✅ FIXED 2026-06-07 — red flash + cause label; playtest]**
  **Damage sources (all server-side, `GameServer.TickEnvironment` + combat):** **oxygen suffocation** — toxic/
  airless atmosphere (or being submerged) drains O₂; at 0 → **Health −5/s** (`GameServer.cs:651-653`); **lava** —
  standing in lava → **Health −15/s** (kills from full in ~7s!) (`:658-660`); **starvation** — hunger 0 → **−3/s**
  (`:677-679`); **fall damage** (`HandleFallDamage`, scales with impact speed); **creature** attacks
  (`GameServerCreatures.cs:181`) + **NPC enemy** attacks (`GameServerEnemies.cs:73`); **toxic food** (negative
  `ConsumeHealth`). **Display gap:** the HUD has health/O₂/hunger **bars** (bigger since item 19), but there is
  **no on-foot damage feedback** — no red hurt-flash/vignette, no hit indicator, and **no cause warning**
  ("Suffocating" / "Burning — lava" / "Starving"). The space view has a hit flash; on foot there's none. So a
  fast drain (lava 15/s, or O₂ hitting 0) kills with only a quietly-emptying bar → "out of nowhere". *Fix:* add
  an on-foot damage cue — a red flash/vignette pulse on health loss + a short cause label, and verify each source
  decrements health correctly. *(Likely culprit for the sudden deaths: lava or a fast O₂ drain on a toxic world.)*
- **B22 — Vendor "E" opens the inventory/crafting menu, not a trade screen. [✅ FIXED 2026-06-07 — barter screen; playtest]** A vendor sets
  `NearbyStation = "market"` and **E** calls `GameMenu.OpenMarket()`, which just opens the unified **crafting/tech
  menu on the "market" category** (`GameMenu.cs:69` — its own comment flags the "crafting list instead of the
  vendor's trade view" issue), not a dedicated barter screen. *Fix:* give the vendor its own trade/barter screen
  (or a clearly trade-only view) instead of the crafting menu. *(Deferred from the medium batch — it's a
  medium-**large** new UI; do it as its own focused task.)*
  **Analysis 2026-06-07:** the economy is **pure barter, no currency** — vendor trades are the **market recipes**
  in `data/recipes.json` (`station:"market"`, e.g. 5 iron_ore → 1 titanium_ore), executed via the normal
  `CraftIntent` → `GameServer.HandleCraft` (validates `MarketAvailable`, consumes inputs, grants outputs, sends
  `InventoryUpdate`). E at a vendor sets `NearbyStation="market"` (`NearVendor`, vendor NPC within 3.6) → `GameMenu.
  OpenMarket()` opens the crafting menu on the market category. **Plan (chosen = barter screen, no server change):**
  a dedicated client `VendorTradeUI` opened on E at a vendor instead of the crafting menu — lists the market
  exchanges as "give X → get Y" cards, greys out the ones the player can't afford (lacks inputs), and on click
  sends the existing `CraftIntent(recipeKey)`; refreshes on `InventoryUpdate`/`CraftResult`. Shows the vendor's
  name. No new messages/NetCodec/economy. (Currency-based shop with credits considered but rejected — it'd be a
  whole new economy: a balance on `PlayerState`, prices on every item, buy/sell intents.)
  **[FIXED 2026-06-07 — user chose barter]** New client `VendorTradeUI` (own canvas, sortingOrder 60): a focused
  centred panel titled with the nearest vendor's name, listing each market exchange as a "give X → get Y" card —
  unaffordable ones show the short count in red + a disabled "Not enough" button; affordable ones have a "Trade"
  button that sends `SendCraft(key, 1)`. Refreshes on `InventoryUpdated`; closes on Esc/Tab/close-button/world-
  reset. `GameMenu.OpenMarket()` now opens this instead of the crafting menu (the two are mutually exclusive via
  `SetOpen`/`CloseForTransition`). E at the vendor opens it (`PlayerController` unchanged). Bilingual keys
  `ui.vendor.*` added (EN+DE). No server/protocol change. 367 tests green (locale parity); client build verified.
- **B23 — Station doors don't auto-open/close; open radius too large. [✅ FIXED 2026-06-07 — OpenRange 4.5→2.8; playtest]**
  Station slide doors are registered via `RegisterStationDoors → MakeDoor("slide", …)` with the **default
  `SlideDoorOpenRange = 4.5`**; the per-door tighter range from the ship-hatch fix (1.8) was applied **only** to
  ship-stamp doors, not stations. In a station's tight rooms 4.5 means you're always within range → doors stay
  open. *Fix:* give station (and tight interior) slide doors a smaller open range, like the hatch.
- **B24 — Red dots in the space HUD — enemies? Flew to one, saw no enemy ship. [PLAYTEST/analysis]** Most likely
  the **enemy drones at long range**: combat spawns drones **150+ units away** from the launch point
  (`GameServerSpaceCombat.cs:349` — deliberately far so launching is safe) and each drone has a **glowing red
  sensor "eye"** (`SpaceView.cs:1232`), so at distance it reads as a small red dot; singleplayer runs
  `--space-combat PvE --space-npcs Normal` so drones do spawn. (Less likely: reddish background **stars**, or the
  ship's own red **port nav-light**.) Flying "to a dot" and seeing nothing could mean: it was far/you stopped
  short, the drone moved, or it isn't an enemy at all. *Investigate in-engine:* fly fully out toward one + scan;
  if confirmed enemies, they may need a clearer HUD contact marker (a labelled hostile blip / off-screen arrow)
  so "red dot" reliably means "enemy ship there". *(Relates to B21's missing on-foot feedback — the space HUD's
  contact cues are thin.)*

### Third batch (reported 2026-06-07)
- **B25 — Avatar has no face in the avatar editor; the in-game colour menu shows no avatar at all. [FIXED
  2026-06-07]** **(a)** Already resolved by B20: `AvatarEditor` builds its preview with `PlayerAvatar.Build`, which
  now adds the eyes/pupils/brow/mouth (+ the visor moved below the eyes) — so the editor inherits the face; the
  report predated the B20 fix. **(b)** The colour tab (`CraftingTechShipUI` Mode.Character) showed only colour
  swatches, no figure. Added a **live faced-avatar preview**: new `AvatarPreviewRig` builds a real `PlayerAvatar`
  (same faced body) at an isolated far spot with its own short-range point light + camera rendering to a
  `RenderTexture`, shown via a `RawImage` in the colour tab's detail pane. It rotates while the tab is open and
  recolours live as you cycle a part (`BuildCharacterList` → `SetColors`); the camera only renders while the
  Character tab is visible (`ShowMode`/`Hide` toggle). One shared faced-build path (`PlayerAvatar.Build`) across
  the in-world avatar, the editor, and this preview. Bilingual key `ui.settings.preview`. 367 tests green; build
  verified.
- **B26 — Water can't be scanned, and you don't sink in / can't swim. [(a) FIXED 2026-06-07; (b) = B7]**
  **(a) [FIXED]** Scanning water returned nothing because the client scan used `Physics.Raycast`, and fluids have
  **no collider** (excluded from the chunk collider so you can swim) — so the ray passed straight through water.
  The server already scans any block (water yields a sensible "Yields: water×1" result via `ScanSubject`). Fix:
  `PlayerController.ScanTarget` now targets via the **voxel ray-march with `includeFluids: true`** (a new param on
  `AimBlock`), so a water/lava block under the crosshair is hit + scanned. Client build verified.
  **(b) NOT a bug — gated on water depth (B7).** The swim code is intact and correct: the collider excludes fluids
  (you fall/sink into water), and `IsSubmerged` (water at **chest height**, feet+1.1) switches to buoyant swimming
  (idle sink, hold Jump to rise, slower horizontal). It only triggers in water **≥ ~2 blocks deep**; the world's
  seas sit BELOW `BaseHeight` so only genuine low ground floods, and deep basins are uncommon → the player usually
  meets shallow, wadeable water and never submerges. Making swimmable water common (lakes/ponds + deeper basins)
  is **B7** — do that to make swimming actually reachable.

### Fourth batch (reported 2026-06-07)
- **B27 — On a planet, the landed ship's door "stands in the air" / "on the roof". [FIXED 2026-06-07]** A
  regression test (`ShipHatchDoor_SitsAtCabinFloor_NotOnTheRoof`) proved the box-ship hatch door is registered at
  cabin-floor level (y0+1, `dY=1.0`), so the placement was never on the roof — the real cause is the hull being a
  flat box anchored at the *centre's* surface height: on sloped/jungle ground the rear hatch hangs over (or buries
  into) terrain at a different height, and a rear engine nozzle sat directly in the left hatch column.
  *Fix (`GameServerShipStructure.StampShip`):* moved the engine nozzles outboard (`cx ± _shipHalfX`) so they never
  block the way out, and carve a **flush rear doorstep** — a solid threshold at floor level out the hatch, the
  doorway cleared above it, and a foundation under it — so the door always meets level, walkable ground regardless
  of the terrain it landed on. *(Playtest to confirm the visual.)*
- **B28 — German menu tab "Einstellungen" overflows its button graphic. [FIXED 2026-06-07]** The tab buttons are a
  fixed 150 px wide with 22 pt bold text (`CraftingTechShipUI.BuildHeader` + `UiKit.AddButton`, overflow wrap), so
  the 14-char "Einstellungen" spilled out. *Fix:* enable best-fit on each tab label (`resizeTextForBestFit`,
  max 22 / min 12) so a long localized tab auto-shrinks to fit its button while short labels keep full size — no
  row-width change (the 8-tab row is already ~1296 px).
- **B29 — A space station is stuck inside a moon / large asteroid. [FIXED 2026-06-07]** Stations + wrecks were
  placed by `DiscPoint` with no overlap check (and they skip the flight-view body-separation pass, which only
  de-overlaps planets/moons/asteroids). *Fix (`UniverseGenerator`):* a `SeparateFromBodies` pass pushes each
  station/wreck radially clear of every planet/moon/asteroid at generation (system-unit clearances sized for the
  0.16× flight scale — planet 300 / moon 185 / asteroid 150), so the authoritative position is clear for both the
  render and the server's board-range check. Regression test `StationsAndWrecks_NeverSpawnInsideABody`. 367 green.
- **B30 — NPCs walk out through the station's window/glass. [FIXED 2026-06-07]** Two causes: NPC world-collision
  only checked the wander **endpoint** cell (so an NPC's wander arc could teleport across a one-block glass pane
  when the far side was open air), and it keyed on "non-air" rather than the block's **Solid** flag. *Fix
  (`GameServerNpcs`):* the step is now **swept** (`PathBlockedByWorld` samples the segment every ¼ block, so no
  tunnelling through a 1-block wall/pane), and collision uses a new `IsSolidCell` keyed on `BlockDefinition.Solid`
  — keeping **solid** and **transparent** distinct exactly as noted: **glass** is solid-but-transparent (blocks
  NPCs, you see through it) while a non-solid transparent block like **water** stays passable.
- **B31 — Trees (flora) spawn on top of the landed ship. [✅ FIXED 2026-06-08]** On a jungle planet the
  player landed and **trees were standing on the ship**. World decoration (flora/trees, and likely also rocks/
  creatures) is placed without checking the landed ship's stamped footprint, so vegetation grows through/on the
  hull. *Fix:* when stamping the ship (or when scattering flora/decoration), **keep a keep-out around the ship
  footprint** so nothing spawns on or inside it — either skip decoration cells whose column intersects a ship
  stamp (`GameServerShipStructure` `CurStamp` bounds), or stamp the ship *after* clearing any flora in its
  footprint. Check whether the order is worldgen-decoration-then-ship-stamp (ship should clear what it overlaps)
  and whether ongoing flora respawn also needs the keep-out. Likely also applies to settlements/stations stamped
  onto terrain. Medium.
  **FIXED 2026-06-08:** `StampShip` now calls `ClearShipKeepOut(cx,cz,halfX,halfZ,y0,height)` in **both** stamp
  paths (the default box hull and a designed-ship voxel layout) **before** laying the hull. It clears any
  vegetation (`flora_*` / `wood_log` / `tree_leaves`, via the existing `IsFlammable` predicate) in the ship's
  footprint **plus a 3-block crown margin** and **up to 9 blocks above the roof** — so a tree world-gen grew there
  can neither stand on the hull nor poke through it, and an overhanging neighbour's crown is removed too. Only
  vegetation is cleared (terrain + hull stay); runs at world-load before chunks stream, so no `BlockChanged`
  broadcast is needed (same as the hull stamp). Test: `ShipKeepOut_ClearsTreesOnAndAboveTheHull` (plants a trunk
  in the cabin + leaves above the roof → both cleared). **Settlements/stations stamped onto terrain may want the
  same keep-out** — left as a follow-up (not reported yet). Playtest: land on a jungle world, no trees on the ship.
- **B32 — Sometimes a block can't be mined (e.g. mud/grass): one block mines, the adjacent one won't. [✅ FIXED 2026-06-08 — client ghost-clear; orig VALID —
  reported 2026-06-07]** Intermittent: the player mines one mud/grass block fine, but a neighbouring identical-
  looking block doesn't break. Mud/grass need no tool, so it's not a tool-tier gate. *Hypotheses to investigate:*
  (a) **mining protection** — the block is part of a ship/settlement/structure footprint (`IsProtectedShipBlock`
  / `_shipExtra` / settlement-protected cells, or the **foundation fill** under a stamp) that silently rejects the
  dig, so a stray protected block sits among normal terrain; (b) **client↔server target mismatch** — the raycast/
  aim resolves a slightly different cell than the server validates (off-by-one at block edges / longitude wrap),
  so the dig lands on air/another cell and "does nothing"; (c) a **block-state/hardness desync** (mined on the
  server but still shown client-side, or hardness/durability not reset); (d) the cell is actually a different
  block than it looks (a flora/decoration or a stamped block re-textured like mud). *Where:* `GameServer.MineBlock`
  + the mining-protection checks + the client `PlayerController` dig raycast. Needs a repro (which block, is it
  near a ship/settlement?). Medium.
  **Analysis 2026-06-07:** `GameServer.HandleMine` (GameServer.cs:1398) rejects a dig via `Reject("mine", reason)`
  on: air, **ship block** (`IsShipBlock` — hull AABB + `_shipExtra` incl. the 6-deep stone foundation), **settlement
  block** (`IsSettlementBlock` — a coarse min/max **AABB that also covers the natural terrain between buildings**),
  **station block**, not-mineable, **out of reach** (server MaxReach 8 vs client raycast Reach **6**), another
  player's landing zone, tool-tier. Mud/grass are hardness 0.6 / no tool → one-shot, so it's not a progress issue.
  Each `Reject` sends `ActionRejected` → client plays an error sound + sets `LastMessage`, which the HUD shows as
  the cyan **toast** (`HudUi.cs:270`). So **two distinct cases**: (1) a *reject* (toast + buzz) ⇒ a protection
  AABB is covering plain terrain — fix = exempt natural-terrain blocks from settlement/structure protection (or
  track exact built cells); (2) *nothing at all* (no buzz/toast) ⇒ the client never sent the dig (raycast missed /
  collider gap at a chunk seam, or the cell is just past the **6**-unit client reach while looking adjacent), or a
  client↔server state desync. Awaiting user repro detail (sound+message vs nothing; near a ship/settlement or open
  wilderness) to pick the fix.
  **User repro 2026-06-07: "nothing happens" (no buzz/toast), "everywhere".** ⇒ case (2): the dig intent was
  never sent — `HandleInteract` does `Physics.Raycast(...Reach...)` and **silently `return`s** when it misses.
  Two causes: the client `Reach` was **6** vs the server's **8** (a 2-unit dead-band where a click does nothing),
  and the chunk **MeshCollider is rebuilt right after every dig**, so a raycast against it can miss a block that's
  clearly there for a frame. **[FIXED 2026-06-07]** (a) bumped client `Reach` to 8 to match the server; (b)
  replaced the block mine/place targeting with a **voxel ray-march** (`PlayerController.AimBlock`, Amanatides &
  Woo over `ClientWorld` — the source of truth, always in sync, immune to collider rebuild/recook lag + seams;
  fluids passed through to match the collider). Mining/placing now never silently fails when a block is in front.
  Client build verified. *Playtest to confirm.*
  **Update 2026-06-07 (still happens after the fix):** the raymarch/reach fix did **not** resolve it, so it is
  **not** a targeting miss. The user's pattern: mining gets **stuck for a while** (several blocks in a row won't
  break), then **un-sticks** (some block suddenly mines) and **the previously-stuck blocks then work too**. That
  "all blocked, then all allowed" cadence points at a **per-player global gate**, not a per-block one — i.e. a
  **mining cooldown / rate-limit / swing-lock** (client or server) that drops dig attempts within a window, OR a
  server-tick/cadence (e.g. mines only processed on a timer; progressive `_miningProgress` gated by a cooldown).
  Re-investigate: a `GetMouseButtonDown` vs continuous-hold mismatch, any mine cooldown/swing-lock in
  `PlayerController`, and any per-player throttle in `GameServer.HandleMine`/the tick loop. Tool power (Basic
  Drill `MiningPower` vs mud/grass hardness 0.6) may mean a click only adds partial progress, so a block needs
  several clicks — feels like "won't mine" until enough accumulate.
  **Root cause found 2026-06-07 (the real one):** there are **two** mining paths and the first fix only touched
  one. Path A `HandleInteract` (single click, bare hands) → now voxel-raymarched. **Path B `HandleDrillAudio`**
  (drill **held**, sends a mine every 0.28 s) still used **`Physics.Raycast`** — and the user mines with the
  **Basic Drill**, so all their mining went through Path B. After a block breaks the chunk collider is rebuilt;
  the raycast misses for a frame, and the `if (Physics.Raycast(...))` wrapped the **whole** drill body, so the
  tick/mine/sparks all stalled until it settled — then everything resumed (exactly "stuck, then a block mines and
  the stuck ones work"). **[FIXED 2026-06-07]** `HandleDrillAudio` now targets via `AimBlock` (the voxel
  raymarch) too — sparks use the targeted cell centre. Both mining paths are now collider-independent. Client
  build verified. *Playtest to confirm.*
  **Real root cause 2026-06-07 (third pass — precise):** still happened; the user now sees HUD **"mine: block is
  already empty"** while **scanning the same cell shows grass**. So it's a genuine **client↔server desync**: the
  CLIENT renders+scans a block (grass) that the SERVER has as **air**. The client only ever learns of edits via
  `BlockChanged`; some server path set the cell to air **without a broadcast the client applied**, leaving a
  **ghost block** in the stale client chunk. Confirmed non-broadcasting `SetBlock(air)` paths: the structure
  **stamps** (ship/settlement/wreck/station clear cells to air — incl. the B27 doorstep carve) and any **re-stamp**
  (ship expand/return) that modifies chunks a client already has. **[FIXED 2026-06-07 — self-heal]** `HandleMine`'s
  "already empty" branch now calls **`ResyncStaleChunk`**: it sends the authoritative block for the cell
  immediately AND drops the chunk from the session's `SentChunks` so `StreamChunks` re-sends the current chunk next
  tick — every ghost in it vanishes. Robust against ANY desync source (not just stamps). 367 tests green; client
  build verified. *Follow-up if still seen: make the live re-stamps broadcast their clears so ghosts never form.*
  **✅ RESOLVED 2026-06-07 (user-confirmed: "problem no longer occurs").** The server-side re-stream alone wasn't
  enough (timing / a stale client chunk GameObject). The fix that worked: a **client-side ghost clear** — the
  server is authoritative, so when a dig is rejected as **"already empty"**, the client now sets that cell to air
  locally (+ remesh) using the last-mined cell (`GameBootstrap.ActionRejected` + `PlayerController.LastMineCell`).
  The phantom block vanishes on the first dig and the next dig hits what's actually behind it. Kept the server
  `ResyncStaleChunk` too (belt-and-suspenders). Root cause stands: structure **stamps** `SetBlock`-clear terrain
  without a `BlockChanged` broadcast → stale already-streamed clients get ghost blocks; *optional hardening: have
  live re-stamps broadcast their clears so ghosts never form in the first place.*
- **B33 — Station NPC name labels float in a planet's sky. [FIXED 2026-06-07]** User saw blue floating NPC name
  labels up in the air on a planet — space-station inhabitants leaking into the planet view. Cause: `NpcView`
  subscribed only to `NpcsReceived` and **never cleared its NPCs on a world change**. Each world keeps its own NPC
  list with IDs that restart at 1, so the station's NPCs (sitting at the station's void-world Y≈64) lingered in
  the client after returning to the planet (their high Y → floating in the sky; `ScenePos` passes Y through
  unwrapped). *Fix:* `NpcView` now also subscribes to `WorldResetReceived` and **destroys all NPCs on every world
  reset** (the new world's `NpcList` repopulates). Note: the other entity views (`CreatureView`, `RemotePlayers`,
  `DoorView`) rely on the same snapshot-pruning and have the same latent gap — apply the same `WorldReset` clear if
  creatures/remote players ever leak across worlds. Client build verified.
- **B34 — Loading flashes the empty/half-built world before the loading screen. [FIXED 2026-06-08 for dock +
  enter-ship; planet landing keeps its descent — re-check]**
  When a world / station / ship interior loads, the player briefly sees the **empty (still-streaming) world**,
  *then* the loading overlay appears, *then* the finished world. Wanted: the **loading screen from the very first
  frame** of the transition, hiding everything until the destination (planet / station / the player's ship,
  depending on where they load) is fully built — then reveal. *Investigate:* the loading-overlay timing in
  `GameBootstrap` (`WorldLoadStarted`/`HyperjumpStarted` events, `WorldReady`, the `OnWorldReset` order) + the
  `PlayerController` settle-freeze that dismisses the overlay. The overlay is currently raised *inside*
  `OnWorldReset` (after chunks are torn down) and dismissed once the player "settles onto solid ground", so there's
  a gap at the very start (old world torn down, overlay not yet up) and possibly an early dismiss before the world
  finishes meshing. *Fix idea:* show the overlay the instant a travel/board/land intent is sent (before any world
  teardown), and only dismiss after the destination's chunks around the spawn have meshed (not just collider-
  settle). Medium.
  **Re-reported 2026-06-08:** the same flash happens specifically when **docking a station, landing on a planet,
  and boarding your own ship** — you briefly see the current planet / station / ship before the loading overlay
  comes up. Same root + fix as above (raise the overlay the instant the dock/land/board intent is sent).
  **FIXED 2026-06-08:** added `GameBootstrap.BeginWorldTransition()` + a `WorldTransitionStarted` event; the
  overlay now **pre-raises the veil immediately** on a descent-less transition — **boarding a station**
  (`SendBoardStation`) and **stepping into the ship interior** (`SendEnterShip`, from the cockpit and from an EVA)
  — instead of waiting for the server's `WorldLoadStarted` (which only fires after the world swap, leaving the old
  view visible for the round-trip). A **2.5 s confirmation timeout** drops the pre-raised veil if no world load
  follows (a rejected action can't get stuck behind it), and `WorldLoadStarted` confirms + refreshes the title
  once the destination is known (both paths send `WorldReset{Hyperjump=false}`, verified). **Planet landing was
  left as-is on purpose** — it plays the in-space **descent** which already masks the build-up, and the overlay
  raises as the descent ends; veiling it immediately would hide that animation. **Playtest:** dock a station +
  enter your ship from space → no flash; **then check a planet landing** — if it still flashes at the end of the
  descent, tell me and I'll veil just that last moment without killing the descent.
- **B35 — Trees (flora) stand *in* the water. [FIXED 2026-06-08]** On a world the player saw **trees
  growing inside water** (submerged trunks). Almost certainly a **side-effect of B7 (upland ponds/lakes, just
  shipped)**: the flora scatter places trees from the **terrain surface height** without checking whether that
  column is **under water** (a pond/lake carved below the surface). *Investigate:* `WorldGenerator`'s flora/tree
  placement — it should skip any column whose surface cell is water or that sits **below the local water level**
  (use the same `PondDepthAt`/pond-mask that carves the water, or test the top solid cell for a fluid above it).
  *Fix idea:* before stamping a tree/flora, reject the site if the surface is a fluid or within the pond depth;
  only plant on **dry** land. Small, pairs with B36 (same root: placement ignores the new scattered water).
  **CONFIRMED 2026-06-08:** `WorldGenerator.StampTrees` excludes only the **global** sea (`sy + 1 <= fluidLevel`)
  and never consults the B7 pond mask, so a tree on a pond column gets a trunk standing on the pond water (the
  small `flora_*` loop already guards with `seabedY + 1 > waterTop`; trees were missed). *Plan:* add a centralized
  `WorldGenerator.SurfacePondDepth(planet,x,z)` (computes this world's pond-enable + threshold + seed internally,
  reusing `PondDepthAt`) and, in `StampTrees`, `continue` when `SurfacePondDepth > 0` (after the existing sea
  check). Same helper fixes B36. **FIXED:** added `WorldGenerator.SurfacePondDepth` + `IsSurfaceWater`;
  `StampTrees` now skips pond columns. Test: `Trees_DoNotStandInUplandPonds` (no `wood_log` has water directly
  beneath it on a forested watery world) + `IsSurfaceWater_FlagsPondsAndDryLand`.
- **B36 — The ship lands *in* a lake (stands in the water). [FIXED 2026-06-08]** The player's ship
  **landed inside a see/pond**, sitting in the water. Task 1 made landing **fluid-aware for oceans** (ships rest on
  the seabed on water worlds, dry cabin) — but for a **scattered upland pond/lake (B7)** on an otherwise dry land
  world, landing the ship submerged in a small pond looks like a bug, not intended seabed-landing. *Investigate:*
  the **landing-site selection** (where the server picks the ship's touchdown column on a planet) — it likely
  doesn't avoid pond/lake columns. *Fix idea:* on non-ocean worlds, the landing-site search should **prefer dry
  land** (reject columns that are water/pond at the surface, nudge to the nearest dry spot), reserving seabed-
  landing for genuine ocean worlds. Small-medium, pairs with B35 (both = pond placement not respected).
  **CONFIRMED 2026-06-08:** `EnsureLandingZone` (`GameServerSpace.cs`) marches the zone +X (spacing 24, radius 8)
  skipping only `OverlapsSettlement` — **no water check** — and `StampShip` anchors at `y0 = SurfaceHeight` (the
  terrain top, water-blind), so a pond/sea column at the zone centre puts the ship in the water. The zone is
  computed once per (player, world) and persisted, so the fix only affects *new* landings. *Plan:* extend the
  march to also skip columns where `SurfacePondDepth > 0` (and, per the chosen policy below, genuine sea), keeping
  zones ≥ spacing apart (wrap-aware) so players don't collide; if the whole budget is water (all-ocean world),
  **fall back** to the settlement-only spot so Task 1's seabed-landing (dry cabin) still applies — no far-marching.
  **FIXED 2026-06-08 (chosen policy: prefer dry land, seabed fallback on all-ocean):** `EnsureLandingZone` now
  marches to the first spot clear of the settlement AND of other zones AND on dry land (`LandingFootprintWet`
  samples the pad centre + radius edges via `WorldGenerator.IsSurfaceWater`); keeps the first town/zone-clear spot
  as the all-ocean fallback. Test: `Ship_LandsOnDryLand_NotInWater` (jungle pad comes out dry, via the
  `LandingPadIsDry` hook). Note: only affects *new* landing zones (existing ones are persisted).
- **B37 — Audit: is the (varying) sun colour used *consistently* everywhere? [AUDITED + FIXED 2026-06-08]** The
  star colour is meant to **vary per system**. It *is* generated server-side (`GameServerWeather.StarColor(system)`
  → `_sunColor`, networked as `Environment.SunColor`) and consumed in several places — `Sky.cs` (planet sky +
  directional sun light + `SetGrade` colour grade), `SpaceView.cs` (the sun disc/corona in space), `Clouds.cs`
  (cloud tint). **Audit goal:** confirm the variation is (a) actually *visible* (real spread between systems, not
  all near-white), (b) used **consistently** — the **system's sun in the space/system view** matches the colour the
  **planet** sees, and (c) the planet is **lit & tinted to match** (directional light colour, **ambient/skybox**,
  and the terrain/world colour-grade all follow the star — not a fixed white ambient that washes the tint out).
  *Check:* does `SpaceView` use *each planet's* star colour or one global sun? Is `RenderSettings.ambientLight`
  derived from `SunColor` or constant? Does the colour-grade meaningfully shift block/terrain colours? Report
  gaps; fix only after. Small audit, possibly small fixes.
  **FINDINGS 2026-06-08:** the source varies well — `StarColor` blends across a real hot→cool ramp
  (blue-white → white → yellow → orange → red-orange). Already **consistent**: the planet **directional sun light**
  + the block-shader **diffuse** global (`Sky.cs` `LightId = sunColor*brightness`), the **sun disc**, the
  `SetGrade` post **colour-grade**, the **cloud** tint at sunset (`Clouds.cs`), and the **space sun disc/corona +
  lens flare** (`SpaceView` `_sunColor` from `Environment.SunColor`) and the **station-window sun**
  (`StationBackdrop`). **GAP found + FIXED:** the **daytime sky was a hardcoded blue** (`Sky.cs.ApplyLighting`),
  ignoring the star — so a red-star world still had a blue sky. Now the day sky (and the fog + reflection global
  that follow it) is **tinted toward the star's hue** (normalise the sun colour to a pure hue, lerp the sky 0.5
  toward `daySky*hue`): warm/red stars → warmer skies, blue-white stars → cooler. **Remaining (lower-priority,
  not done):** Unity `RenderSettings.ambientLight` is unmanaged (world blocks don't need it — they use the
  sun-tinted `LightId` global — but standard-shader objects could pick up a star-tinted ambient); **space-view
  planets + their cloud shells** use fixed biome tints (not star-lit) and the starfield is generic (arguably
  correct — those are *other* suns). Playtest: land under a warm/orange star and a blue-white star, compare skies.
- **B38 — Audit: are creatures & flora generated with the intended *random* colours? [AUDITED + FIXED 2026-06-08]**
  Two parts. **(a) Flora:** a per-planet `FloraColor`/`Environment.FloraTint` re-tints flora via the
  block shader global `_Sc_FloraTint` — but `ChunkMesher` only flags the **small `flora_*` plants** for the tint
  (`IsFlora`, comment "small flora plants"). **Verify tree crowns (`tree_leaves`) are also flagged/tinted** — if
  not, trees stay one fixed green while ground flora recolours (likely gap). **(b) Creatures:** creatures carry
  `ColorRgb`/`BellyRgb` (client `CreatureBuilder` builds from them), but spawn defaults to `0xFFFFFF` when unset
  (`GameServerCreatures` `ColorRgb = sp?.ColorRgb ?? 0xFFFFFF`). **Verify spawning actually assigns the intended
  *randomized* colours** (per-individual variation, not all white/default) — a white/default fallback is also the
  suspected root of **B15** (the untextured red/blank creature). *Check:* where creature `ColorRgb` is set at spawn
  (species palette + per-spawn RNG), and whether `tree_leaves` gets the flora-tint vertex flag. Report gaps; fix
  after. Small audit; pairs with B15.
  **FINDINGS 2026-06-08:** **(a) Flora — GAP found + FIXED:** `ChunkMesher.IsFloraBlock` flagged only the small
  `flora_*` plants, so **tree crowns stayed a fixed green** while ground flora recoloured per planet — yet the
  server's `FloraColor` is documented as *"one hue for all of a planet's plant life."* Fix: `IsFloraBlock` now also
  flags **`tree_leaves`**, so a planet's leaves take its flora hue (alien purple/amber foliage on exotic worlds);
  the `wood_log` trunk keeps its natural bark colour. **(b) Creatures — VERIFIED WORKING (no change):**
  `CreatureGenerator.PickColor` already randomises every species' `ColorRgb`/`BellyRgb` — ~50 % vivid exotics (a
  fully random hue at high saturation) and ~50 % habitat-tinted naturals with ±60 per-channel jitter — assigned at
  species generation. The `?? 0xFFFFFF` white default only triggers if the **species is null** (a data/desync
  error, never normal spawn); that — or a `PickHide` texture-load failure — is the **B15** lead, a *separate* bug,
  not a colour-generation gap. Playtest: trees on a non-green-flora world should now match its foliage hue.
- **B39 — New-world spawn sometimes fails: player falls in space, then the loading screen hangs forever.
  [FIXED 2026-06-08]** Occasionally when **generating/entering a new world** the player **doesn't spawn
  on the surface** — instead they **fall through into the void/space** — and the **"Loading world" overlay then
  comes up and stays stuck**, with nothing further happening (a softlock; the only way out is to quit). This is
  worse than B34 (a *flash*): here the load **never completes**. *Likely root:* the spawn position isn't resolved
  to solid ground for that world (placed above terrain that hasn't streamed, or in a column with no surface — cf.
  the B7 pond / B36 landing + `EnsureSafeSpawn` / the `SurfacePlayer` open-air scan), so the player free-falls; and
  the loading overlay's **dismiss condition is "player settles onto solid ground"** (see B34) — which **never fires
  while they're falling in the void**, so the overlay hangs forever. *Investigate:* the new-world spawn-Y
  resolution (does it scan for the surface / clamp onto terrain before handing control over?), `EnsureSafeSpawn`,
  and the overlay-dismiss watchdog. *Fix ideas:* (1) make spawn **always** resolve to a solid surface cell for the
  destination (scan down/up from the spawn column once its chunks are meshed, like the landing-zone + SurfacePlayer
  logic), and (2) give the loading overlay a **timeout / fallback** so it can never hang indefinitely (e.g. after N
  seconds without a settle, force a safe re-spawn or surface the player) — a softlock should never be reachable.
  Medium; **higher priority than B34** (softlock vs cosmetic). Pairs with B34's overlay-dismiss logic.
  **ROOT FOUND + FIXED 2026-06-08:** the server already (a) streams the **spawn's own chunk nearest-first** so
  ground arrives fast (`StreamChunks`, tested) and (b) runs a **runtime void-rescue** every 1 s + a join-time guard
  (`GameServerSpawnSafety`, tested). The hole was on the **client**: `PlayerController` froze the player at spawn
  until ground streamed in, but after a **20 s timeout it released them UNCONDITIONALLY** — so when a fresh world's
  spawn chunk was slow, the freeze ended with **nothing under them → an endless fall**, and the overlay
  (dismissed by that same release) revealed an empty void. Fix (client): the settle-freeze now **never drops the
  player into the void** — it **reveals the world** (dismisses the overlay) after ≤8 s so the screen never
  overstays, but **holds the player at spawn until there is real ground below**; only as an absolute last resort
  (30 s) does it release, and then the server's runtime void-rescue immediately teleports them back to safe ground.
  So a slow/never-arriving chunk can no longer become either an endless fall or a frozen softlock. Server tests
  cover the safety net (`RuntimeRescue_PullsAPlummetingPlayer_BackToSafeGround`, `JoinGuard_…`,
  `StreamChunks_SendsThePlayersOwnChunkFirst_…`); the settle change is client-side. **Playtest:** start several new
  games / land on fresh worlds and confirm you always settle onto the surface (never fall into space / hang).
- **B40 — "Launch to space" from the ship while already in space replays the planet take-off animation; and the
  planet landing/take-off animation isn't oriented to the planet. [(a) FIXED 2026-06-08; (b) OPEN — needs
  playtest iteration]** Two related
  flight-animation issues. **(a)** When you're **already flying in space**, step **into the ship interior**, and
  pick **"Launch to space"** in the menu, the **planetary take-off animation plays** (ship rising as if leaving a
  surface) — but you came from space, so it should **skip the take-off** and just return to the flight view. Item
  **5e** already added a **`SpaceState.SkipLaunch`** path ("launch from inside the ship no longer plays the
  take-off animation"; `EnterSpace(skipLaunch:true)`) — so this case is either **not taking that path** or it
  **regressed**; *investigate* the ship-interior → launch flow (`SendEnterSpace`/`GameMenu` launch vs.
  `EnterSpace`) and gate the take-off on "was the player actually on a planet surface?", not just "is the ship
  stamped." **(b)** The planet **landing + take-off animations move the ship straight up (take-off) / down
  (landing)**, which is fine — **but the planet may not be *below* the ship** (it can end up **in front of** the
  player), so the up/down motion doesn't read as leaving/approaching the surface. *Fix:* **orient the ship (and/or
  the camera + planet placement) before the animation** so the planet sits **beneath** the ship and the vertical
  motion makes sense (point the descent/ascent along the planet direction). Where: the space-view landing/launch
  sequence in `SpaceView` (the `Phase.Landing` descent + the launch/take-off path) + how the planet body is
  positioned relative to the ship in that view. Medium; (a) is the clearer bug, (b) is a polish/orientation pass.
  **(a) FIXED 2026-06-08:** the ship interior is **only ever entered from a space instance** (`EnterShipInterior`
  requires one), so launching to space from there must always skip the take-off. The client already shows the
  **helm** there (skips), but the server's `EnterSpaceIntent` handler (`HandleEnterSpace`) always used
  `skipLaunch=false`. Now `HandleEnterSpace` routes a player who is `_inShipInterior` through `ExitShipToFlight`
  (restores the parked ship position + `skipLaunch:true`) — so **every** path that fires the intent from the
  interior skips the take-off; only a launch from a real planet surface animates. Test:
  `LaunchToSpaceFromShipInterior_TakesTheSkipPath_NotAFreshPlanetLaunch` (380 green). **(b) STILL OPEN —
  needs live playtest iteration:** the up/down motion is fine, but framing the **planet beneath the ship** during
  the sequence is a camera/orientation choreography problem I can't tune blind. The home planet *is* placed below
  (`SpaceView` `homePos = (0,-150,-20)`) and the ship moves straight in `_root` Y, but the third-person camera
  looks ~forward at the ship (so the planet sits low/off-frame), and when you **land on a body you flew *to*** (E),
  that body is at its orbit position — **not below** — so the vertical descent doesn't point at it (the reported
  "planet in front"). *Plan when tackled (with playtest):* at the start of `Phase.Landing`/`Launch`, reorient so
  the **actual target body** is directly beneath the ship and pitch the camera down to keep it framed; needs the
  landing-target body position threaded into `UpdateSequence`. Deferred so it can be tuned against the real view.

### Reported-bug batch (2026-06-08, after item 38) — B41–B52
- **B41 — Player-ship hatch sits *lengthwise* inside the ship (not parallel to / at the opening); + suffocated
  inside the ship on an airless planet. [TODO]** Two issues on the player's own landed ship: **(a)** the hatch
  door is oriented wrong — it should sit **at the doorway opening, parallel to it**, but renders **along the ship's
  length** inside the cabin. **(b)** On a planet with **no atmosphere/oxygen**, the player **suffocated inside the
  ship** — the sealed cabin should supply breathable air (oxygen refill / no drain) like a suit-safe space. Where:
  `GameServerShipStructure` (hatch door placement/axis) + the oxygen/suffocation logic vs. "inside ship interior".
- **B42 — Wood (tree trunk / `wood_log`) must not be hand-mineable. [TODO]** Currently `wood_log` can be mined by
  hand; it should require a tool (axe/drill tier). Fix: set its `requiredTool`/`minToolTier` in `blocks.json` so
  bare hands can't harvest it.
- **B43 — Underwater, the deep↔shallow water boundary draws a water *surface* texture at the block edge. [TODO]**
  Where deep (swimmable) water meets shallow (non-swimmable) water, the client mesher draws a **water-surface face
  at the vertical block edge** that shouldn't be there when the player is **underwater**. Fix: in the client
  `ChunkMesher` water-face logic, don't emit the surface face at a water↔water depth seam (only at the true
  air/water top). Client rendering.
- **B44 — Machete needs a 1.5 s cooldown. [TODO]** The melee machete can be swung with no rate limit. Add a
  **1.5 s** attack cooldown (server-side melee cooldown, like the gadget cooldowns).
- **B45 — Can't land on asteroids: "you can only land on a planet or moon." [TODO]** Landing on a (landable)
  asteroid is refused by `HandleTravel`'s body-kind check (only Planet/Moon accepted). Asteroids ARE landable
  (item 24/33) — allow `CelestialKind.Asteroid` (with a `PlanetType`) as a land destination.
- **B46 — Bottomless world: removed a few blocks and fell forever. [TODO]** Worlds need a **depth limit** with an
  **unmineable bottom layer**: **lava** at the very bottom on **planets**, **solid rock** on **asteroids/moons**.
  The last layer must **not be mineable**. Where: `WorldGenerator` (emit a floor/bedrock at min-Y) + mining
  (reject mining the bottom layer / bedrock). Relates to the void-fall self-heal (a real floor prevents the fall).
- **B47 — How does the player open settlement wooden (hinge) doors? [TODO]** Settlement hinge doors should be
  **player-openable**. Check the interact flow: `HandleDoorInteract` (E) toggles hinge doors within `HingeDoorReach`,
  and the client `DoorView.NearestHinge` + the E handler — confirm settlement hinge doors are reachable + the prompt
  shows, and fix if they can't be opened.
- **B48 — Launching from a moon takes off over the *start planet*, not the moon. [TODO]** After landing on a moon
  and launching again, the take-off / flight view shows the **home/start planet** below, not the moon you launched
  from. The launch location (`EnterSpace` `locationId` / `_ship.CurrentLocationId`) isn't the current body. Fix the
  per-player launch-from-current-body so you rise off the **moon** you're on.
- **B49 — Flying the ship into a small (mineable) asteroid: instant destruction, no explosion VFX/sound. [TODO]**
  Ramming a small mineable asteroid with the ship currently **destroys it instantly** and the player **respawns**
  with no effect. **B56 (clarification):** the collision should NOT instantly destroy the ship — it should take
  **shield damage**, or **hull damage** once the shield is gone/absent — only destroyed (with explosion VFX +
  sound) when the hull actually reaches 0. Where: the space collision/damage path (`GameServerSpaceCombat`
  `ApplyShipDamage` / `ShipCollisionMaxDamage`/`Factor` — too high, one-shots) + the ship-loss VFX/audio
  (`DeathFx` flash + a missing explosion sound).
- **B50 — Space stations can still stick inside planets. [TODO]** Despite B29's keep-out, orbital stations still
  sometimes **overlap a planet/large body**. Re-audit station placement vs. body radius + keep-out margin in the
  system layout (`GameServerSpaceStations` / the galaxy body positions).
- **B51 — Some of the player's own ship blocks are still mineable when landed on a planet. [TODO]** `IsShipBlock`
  should protect every stamped hull cell, but some ship blocks can still be mined on a planet. Audit the ship-stamp
  bounds vs. `IsShipBlock` (footprint/height, the energy-door cells, multi-block parts) so no hull cell is mineable.
- **B52 — Inconsistent mining: a material sometimes breaks instantly on one click, then the same material takes
  longer. [TODO]** Mining progress is erratic — the first hit on a block sometimes one-shots it, then an identical
  block needs several hits. Likely the `_miningProgress` accumulator keyed/seeded wrong (stale progress carried to a
  new cell, or the first-hit applies full hardness). Investigate `HandleMine` progress accounting + fix so a block
  of a given hardness always takes the same hits.
- **B53 — Appearance/ship colour tabs show the WRONG preview (character ↔ ship swapped). [TODO]** In the in-game
  menu: **Settings → character/colour** shows a character submenu but **no colour controls**, and the right detail
  view shows the **ship** instead of the character. Conversely **Ship → paint** shows the **ship** in the detail
  view but **also the character**. The avatar-preview rig and the ship-preview rig (`AvatarPreviewRig` /
  `ShipPreviewRig` in `CraftingTechShipUI`) are mixed up / both active on the wrong tab. Fix: the colour/appearance
  tab shows ONLY the avatar (+ colour controls); the ship paint tab shows ONLY the ship.
- **B54 — A lava planet showed no visible lava. [TODO]** A planet typed as volcanic/lava rendered with **no lava**
  visible on the surface. Relates to B19 (lava pools only in low basins below ~`BaseHeight − 0.25·Amplitude`), so a
  high-terrain landing/view can show none. Re-check the lava-world sea-fill (`WorldGenerator.ResolveSeaFluid` +
  the volcanic `lavaAb`/sea level) so a lava planet reliably shows lava (raise the lava sea level / abundance, or
  guarantee a visible lava basin near a landing pad).
- **B55 — Station vendors all sell the SAME goods / offer the same trade. [TODO]** Multiple traders on one station
  offer identical stock. Each vendor should have its **own** (deterministic but varied) inventory/offers. Where:
  vendor/market stock generation (`GameServerTrade` / vendor inventory seeding) — seed per-vendor, not per-station.
- **B57 — "Einstellungen" + "Schließen" don't fit their button frames in the in-game player menu. [TODO]** Two
  button labels overflow the button graphic in the in-game menu (German, longer words). Like B28 (tab overflow) but
  for these menu buttons. Fix: size the button / shrink-to-fit the label so the text fits (`GameMenu`/`UiKit`
  button sizing).
- **B58 — Can't customise the hotbar/quick-bar (add/remove items). [TODO]** The on-foot quick-bar shows fixed
  slots; the player can't choose what goes in it. Add a way to move items into/out of the quick-bar slots (e.g.
  drag from the inventory, or an assign action) so the player controls their loadout. Where: the inventory/hotbar
  UI (`HudUi` hotbar + the inventory panel) + the slot model. Medium (client UI + maybe a server slot-swap intent).
- **B59 — Deleting a singleplayer world from the save picker does nothing. [TODO]** In the main menu's
  singleplayer world list, clicking the ✕ on a world opens a Delete/Cancel dialog, but **Delete does nothing** —
  the world isn't removed. Where: the world-picker delete handler (`AppShell`/the save-list UI → the delete
  confirm button's action + the actual save-folder delete).
- **Feature 40 — Terrain scanner gadget (pulse reveals nearby valuable ores, incl. underground). [TODO — analysis
  first.]** A new equipment/gadget item: emits an energy pulse around the player and **highlights valuable
  materials**, including **underground** ones. For a few seconds the surrounding terrain at the trigger point goes
  briefly **"transparent"** and only the **valuable blocks in line of sight stay visible** for the effect's
  duration. Like the item-36 gadgets (tool kind `gadget`, cooldown + suit energy) + an OpenAI icon + ElevenLabs
  sound + a client VFX (an x-ray/see-through pass that fades terrain and keeps ore blocks opaque/glowing). Needs:
  the gadget item/recipe/blueprint, a server `UseGadget` branch (or a client-only reveal driven by the local chunk
  data), and the client x-ray VFX. Medium — the see-through terrain effect is the novel client piece.
  scanned**, **no texture**. **User's read (2026-06-07): it's a creature, not lava** — lava wouldn't spawn as a
  lone two-block thing. So most likely a **hostile fauna creature** rendered **red** (hostile tint) with a
  **failed/missing hide texture** (`CreatureBuilder.PickHide` returned null → untextured red material) and a
  **minimal body** (e.g. ~2 stacked cubes: body + head, legs=0). The "can't scan" is the real puzzle — creatures
  *should* be scannable (`GameServerScanning`), so either the scan missed it (range/aim) or this entity isn't a
  normal creature. **When tackled:** make `PickHide` never return null (always fall back to a real hide so a
  creature can't render as bare red cubes), give a sane minimal body, and check why the scan didn't catch it.
Features: B7/B11. Rendering: B6/B8/B17/B20. B15/B19 need an in-engine look; B21 is the damage-feedback audit.)*

## 📋 More feature requests — 2026-06-07 (backlog only, analysis-first, not started)
29. **In-game integration of the editors (load player-made ships/stations/settlements + content).** *(Analysis +
   effort estimate first.)* The ship editor, **space-station / city / village (structure) editor**, **material
   editor**, **blueprint editor** and **ship-parts editor** exist as standalone tools — can their **outputs be
   loaded into an actual game / own server / singleplayer** so players *use* what they built? **Assess the effort
   per editor:** where each editor saves its design, what format, and what the server/worldgen would need to
   ingest it (stamp a player-designed station/settlement into a system; register a player-designed ship as
   craftable; load player material/blueprint/ship-part definitions as content). Likely needs a persistence/import
   path + a content-merge layer. *Estimate complexity per editor + a staged plan.*
30. **Harvestable water + lava, flowing placement, and a fire system. [✅ DONE 2026-06-08 — playtest]** *(Analysis first.)* **Partly exists:**
   `water`/`lava` blocks are `mineable` (drill **tier 3**) and placing a fluid flows (`RegisterFluidSource`). The
   request: make water **harvestable** (confirm it works / lower the tool gate?) and when **placed it flows**;
   make **lava** harvestable too, and when **placed it ignites the surroundings** — the **flora catches fire**
   (needs a **new fire system**: fire spreads across flammable blocks, a **visual flame effect + sound**, burns
   them away); and **water extinguishes fire**. New systems: a fire/burning simulation (server) + flame VFX/SFX
   (client) + flammability per block + water↔fire interaction. Medium-large.
   **Plan (fire system) 2026-06-07:** model fire as a transient **`fire` block** (server-simulated, broadcast via
   the existing `BlockChanged`, client renders it emissive + alpha + non-colliding — like a fluid). A new
   `GameServerFire` cellular tick mirrors `GameServerFluids` (per-world state in `WorldData`, ~6 Hz, per-tick
   budget): a burning flammable block becomes `fire` for a short duration then turns away; each tick it ignites
   flammable neighbours (spread) and is extinguished if a **water** neighbour touches it. **Lava ignites**: the
   fluid tick, when it processes an (active, i.e. placed/flowing) lava cell, ignites its flammable neighbours.
   Flammable = `flora_*` + `wood_log` + `tree_leaves`. Fire damages a player standing in it (like lava). Bounded
   by the budget + active-set dormancy (existing lava seas don't ignite unless flowing). Client: `ChunkMesher`
   treats `fire` like a see-through, non-collidable, glowing block; a fire-crackle ambience near fire. No new
   message/NetCodec. Tests mirror `FluidTests`.
   **[FIXED 2026-06-07 — user chose controlled→ash]** New blocks `fire` (non-solid, non-mineable, emissive,
   alpha-blended) + `ash` (charred remains) appended to blocks.json (+ bilingual names). New `GameServerFire`
   partial: per-world `FireTimer`/`ActiveFire` state, ~6 Hz tick, 300-cell budget; a flammable block
   (`flora_*`/`wood_log`/`tree_leaves`) set alight becomes `fire` for ~3.5 s, igniting flammable neighbours, then
   collapses to `ash`; a `water` neighbour douses it to air. The fluid tick ignites the flammable neighbours of
   any active/flowing **lava** cell (placed/harvested lava → place near plants → fire). Fire burns a player
   standing in it (10 hp/s, armour-reduced). Client: `ChunkMesher` renders `fire` see-through + non-collidable +
   emissive (atlas tile from a generated **transparent flame texture** — alpha baked from brightness so flames
   show on transparent gaps); `ash` is a solid charred tile; a **fire-crackle loop** (ElevenLabs) plays near
   fire via the fluid-proximity ambience. 5 `FireTests` (ignite→ash, spread, water-douse, lava-ignite); 373
   green; client build verified. *(Water/lava are already mineable (tier-3 drill) + flow on placement — the
   item-30 "harvestable + flowing" parts pre-existed.)*
31. **Player-created missions (a real player-to-player mission board). [✅ DONE 2026-06-08 — playtest]** *(Analysis first.)* A player can **post a
   mission** others accept from a mission board: the poster types a **name + description**, **stakes a reward**
   (an item/material they give up) and defines **what the completer receives**; on success the **poster is
   notified** and gets back a **multiple of their stake**. The poster sets the **objectives** — e.g. *mine N of a
   material*, *travel to a place* — with **multi-select** (several objectives per mission). Builds on the existing
   mission board + `GameServerMissions`/`StoredMission` persistence (admin/player-created missions already persist)
   + the NPC-memory/relationship hooks. Needs: a post-mission UI, objective tracking + verification, the
   stake/escrow + payout, and cross-player notification. Big-ish.
   **Analysis 2026-06-07 — most of the SERVER already exists:** `CreateMissionIntent` (NetCodec 13) →
   `GameServerMissions.HandleCreateMission` makes a `MissionDefinition{Source=Player, CreatorId, Objectives,
   Rewards}`, **escrows the reward** into a depot `StoredContainer`, persists via `_repo.SaveMission`; `Accept`/
   `TurnIn` verify objectives (`OnBlockMined` tracks **Mine**; Collect/Deliver checked at turn-in) and pay the
   completer from the depot (`PayoutDepot`). **Missing:** (1) a **client UI to POST a mission** (the form — title,
   description, objectives, reward stake); (2) **notify the poster** when completed (+ the "multiple of stake"
   payout); (3) **Travel** objective tracking (only Mine is hooked). Plan: build the post-mission form in the
   Missions tab (a "+ New" view), add a poster-notification (`ServerMessage`/a small notice) + the chosen payout
   on turn-in, and hook `HandleTravel`/landing for Travel objectives. Reuses the existing messages — no NetCodec
   change for posting.
   **[DONE 2026-06-08 — "Einsatz mit Multiplikator" payout.]** Shipped the three missing pieces:
   (1) **Post-mission form** — a new "**Post a mission**" entry in the Missions sidebar (`CraftingTechShipUI.
   BuildMissionForm`): title + optional description inputs, an **objectives builder** (cycle type Mine/Collect/
   Deliver × target material × count, **+ Add** to stage several, ✕ to remove) and a **stake** (reward item +
   count); **Post** sends `CreateMissionIntent` (`NetworkClient.SendCreateMission`). (2) **Poster payout +
   notice** — `GameServerMissions.RewardMissionPoster` pays the poster **1.5× their staked reward** on turn-in
   (rounded, online posters only) and sends a `ServerMessage` notice; **self-completion earns no bonus**
   (`CreatorId == completer` guard) so you can't mint items off your own mission. (3) **Travel objective
   tracking** — `OnPlayerTravelled` (hooked after `ActiveLocationNames()` on arrival) completes any matching
   **Travel** objective; `HandleCreateMission` now accepts Travel objectives. Tests: existing
   `PlayerCreatedMission_…` (escrow + self-turn-in, implicitly the no-self-bonus guard) +
   `PlayerCreatedTravelMission_ProgressesWhenPlayerArrives`. **Playtest:** post a mission from the Missions tab,
   have a second player complete it, confirm the poster gets 1.5× back + the notice.
32. **Choose a ship hull colour (tints the hull texture), changeable in the in-game menu. [DONE 2026-06-08]**
   *(Analysis first.)*
   Let the player pick a **colour** that **tints the ship's hull texture**, set from the **in-game menu**. The
   ship already renders its hull with a textured material (`SpaceView.BuildShip` + the remote avatars use `LitTex`
   with the `iron_wall` hull texture — item 5b); tinting = multiply a per-player **HullColor** into that material
   (and the on-planet/station stamped ship + remote ships, for consistency). Needs: a `HullColor` on the ship/
   player state (persisted + networked), a colour picker in the ship tab of the menu, and applying the tint
   everywhere the ship is built (flight view, interior, remote avatars). Small-medium, mostly client + one state
   field. *(Pairs with the existing avatar colour customization.)*
   **ANALYSIS + PLAN 2026-06-08 (user chose: Ship tab WITH a live ship preview).** Mirrors the avatar-colour
   pipeline. The flight ship (`SpaceView.BuildShip`) and remote ships (`BuildRemoteAvatar`) already build their
   hull with `LitTex("iron_wall", tint)` — so tinting = set that tint to the player's hull colour. Remote ships
   render in **space** from `NetSpacePlayer` (no colour field yet); the voxel planet/interior ship is mesher-based
   and **out of scope** for this small pass (noted as a follow-up). Pieces: (1) `ClientSettings.HullColor`
   (persisted client-side like the avatar colours) + `GameBootstrap.HullRgb` (set from settings in `WorldRig`);
   (2) network it — add `Hull` to `SetAppearanceIntent` + `NetSpacePlayer`, extend `SendAppearance`, store
   `PlayerSession.HullColor`, populate it in `OtherPlayersInSpace` (MessagePack is contractless, so new fields
   need no codec work); (3) `GameMenu.CycleHull` + `ApplyAppearance` sends the 5th colour; (4) a new
   `ShipPreviewRig` (mirrors `AvatarPreviewRig` — a textured rotating ship rendered to a RenderTexture, with
   `SetHullColor`); (5) Ship tab gets a **"paint"** sidebar category → a hull-colour swatch + cycle in the list and
   the live ship preview in the detail pane; (6) `SpaceView` tints the local ship (`_hullMat`) + remote ships from
   the hull colour, re-tinting live when it changes. Default hull `0xD1D6E0` = today's tint, so unchanged ships
   look identical. Bilingual `ui.ship.cat_paint` / `ui.ship.hull_color`.
   **SHIPPED 2026-06-08 exactly as planned.** New Ship-tab **"Paint"** category: a hull-colour swatch (cycles the
   shared palette via `GameMenu.CycleHull`) + a **live rotating ship preview** (`ShipPreviewRig`, mirrors
   `AvatarPreviewRig`). The colour persists in `ClientSettings.HullColor`, flows through `GameBootstrap.HullRgb` +
   `SetAppearanceIntent.Hull` → `PlayerSession.HullColor` → `NetSpacePlayer.Hull`, and tints the local flight ship
   (live re-tint mid-flight) and other players' ships in space. 377 tests green (locale parity); client built.
   **Out of scope (follow-up):** the **voxel** stamped ship you walk on (planet/interior) stays the default hull —
   per-player tinting of mesher blocks is a separate, bigger change. **Playtest:** Ship tab → Paint → cycle the
   colour, watch the preview, then fly and confirm the ship + (multiplayer) other players' ships show it.
33. **Cratered terrain for airless moons + landable asteroids. [✅ DONE 2026-06-08 — playtest]** *(Analysis first.)* On **moons without an
   atmosphere** and on the **landable asteroids** (item 24 — the big ones you land on; never the mini mineable
   rocks) — both always airless — add a **frequently-used crater landscape**: mostly **flat** ground pocked with
   **many round craters**. Adjust the **terrain generator** accordingly (a crater-field height function — e.g.
   sum of inverted radial bumps / worley-style pits — selected for airless moons + asteroid bodies, blended with
   the existing terrain). Where: `WorldGenerator` surface-height + the per-planet terrain params (the `asteroid`
   planet type + a moon-airless variant). Medium.
   **SHIPPED 2026-06-08.** `SurfaceHeight` branches for cratered worlds: a near-flat regolith base (0.30×
   amplitude undulation, no archetype hills/ridges) with `CraterCarve` on top — a seam-safe FBM mask (the B7
   pond-mask approach, so it wraps the X seam) carving smooth bowls (to −7) each ringed by a raised ejecta rim
   (+2). Selected two ways: a data flag `PlanetType.Cratered` (set on the **`asteroid`** type → landable
   asteroids, craters everywhere incl. standalone queries) **and** a per-world `SetCratered` the server flips for
   **airless moons** (`Kind==Moon && atmosphere=="none"`) at `LoadWorld` (beside `SetCircumference`), so airless
   *planets* stay normal — only moons + asteroids get craters. **User add-ons:** *some* (not all) craters carry
   a few small clumps of **rare metal** on their deeper floors — `CraterFloorMetal` gates whole craters with a
   coarse region mask, then scatters tiny clumps (titanium/gold/platinum/cobalt/uranium/tungsten/neodymium) on the
   top two cells. Test: `CrateredWorld_FlatRegolithWithPits_AndRareMetalInSomeCraters` (flat-with-pits + sparse
   metal); 385 green; client built. Playtest: land on an asteroid / airless moon — flat cratered ground, dig the
   odd crater floor for metal.
34. **Precipitation by climate — fire/ash rain on very hot worlds (and snow on cold). [DONE 2026-06-07 — folded
   into B11 Stage 2.]** Resolved as part of the weather pass: server `PrecipitationFor(weather, temp)` picks the
   form from the per-world temperature (B11) + surface block — **ash** ≥55 °C (lava worlds), **hail** ≤-15 °C,
   **snow** ≤2 °C, **sandstorm** on sand surfaces, else **rain**. Client renders all five in `WeatherFx3D` with
   tinted fog + screen wash, and `ClientAudio` plays a matching bed. See the B11 Stage 2 note for details.
35. **Energy door — an automatic sliding door with a passable blue force-field in the opening; use it as the
   ship's outer door. [✅ DONE 2026-06-08 — playtest]** *(Analysis first.)* A **new door type**
   that behaves like the existing **automatic slide door** (auto-open/close on proximity) but, while **open**,
   shows a **transparent blue energy field filling the doorway** that the **player can walk through** (passable —
   it's a visual/atmospheric membrane, not a barrier). Then **replace the starter ship's outer hatch with this
   energy door**. *Pointers:* doors are server entities over **air** cells (the client mesher renders+collides
   every non-air block, so passable cells stay air + a server door entity — see the door system; `DoorSnapshots`
   `Kind == "slide"`). The blue field can reuse the existing **`force_field`** look (already in `IsTransparent`)
   but must stay **non-solid/passable** (solidity ≠ transparency — glass is solid+transparent, this is
   non-solid+transparent); render it only in the **open** state (a thin emissive-blue alpha plane in the opening),
   not when closed. The ship hatch is stamped in `StampShip` (the 2-wide gap in the `-Z` wall). **Bundle the
   related fix:** the **hatch isn't centred** on the starter ship — the gap is at `x == cx || x == cx-1` (offset
   half a block), so **centre it**, widening the ship by one if needed for an even/odd-symmetric doorway, and keep
   `RegisterDoors`/the interior clear. Needs: a door-kind/flag for "energy" + the open-state field render
   (client), the hatch re-centre + hull width tweak (server `StampShip`), and bilingual names. Medium.
   **SHIPPED 2026-06-08:** new door kind **`"energy"`** — server treats it exactly like a slide door for
   auto-open/close (`GameServerDoors` open tick now matches `slide` **or** `energy`); the **ship's outer hatch** is
   now registered as `"energy"` (`RegisterDoors`). Client `DoorView`: an energy door builds the normal cyan slide
   panels **plus** a translucent blue **energy field** quad filling the opening (a thin cube on the door pivot,
   no collider → passable) whose alpha **fades in with the open amount** + a faint shimmer; reuses the
   always-included `Spacecraft/Cloud` alpha shader (no texture → a solid tinted quad, can't strip to pink). The
   field shows only while open; the door's own collider still blocks while closed. **Hatch centred (bundled fix):**
   the box ship's hatch is now a **3-wide** gap `cx-1..cx+1` (was 2-wide `cx-1..cx`, half a block off) — symmetric
   about the hull centre `cx+0.5`, no hull-width change needed; the threshold pad + door marker were moved to
   match. No new blocks/items, so no locale change (it's ship structure, not craftable). Tests:
   `ShipHatch_IsAnEnergyDoor_CentredOnTheHull` + updated the two hatch tests to expect `"energy"`; 379 green.
   **Note:** designed (editor) ships' doors were already all converted to slide on stamp, so they become energy
   too (a consistent sci-fi look). **Playtest:** walk up to the ship hatch — it slides open with a blue field you
   can walk through, and sits centred on the rear wall.
36. **New gadget items + ship systems: medpack, stasis projector, terrain blaster (with their own sounds,
   textures and visual effects). [✅ DONE 2026-06-08 — playtest]** *(Analysis first.)* Three new
   craftable gadgets: **(a) Medpack** — heal **yourself and other players** (a use-on-self + aim-at-ally heal, a
   chunk of HP, consumable or charged). **(b) Stasis projector** — a beam that **briefly "freezes" a creature**
   (suspends its movement/AI for a few seconds) so you can **scan it safely** at close range. **(c) Terrain
   blaster** — destroys a **large volume of terrain** in one shot (a radius/sphere of blocks → air). For **each**:
   an item def + **blueprint unlock** + craft recipe (`items.json`, the blueprint/unlock system), server handling
   (heal players; pause a creature's AI/aggro timer in `GameServerCreatures`; bulk-`SetBlock`→air that **broadcasts
   `BlockChanged`** for every changed cell — see the block-broadcast rule — and respects protections/landing
   zones), client use (aim + fire), **OpenAI textures/icons**, **ElevenLabs sounds**, and **visual effects** (heal
   sparkle, stasis shimmer/blue tint on the frozen creature, blaster shockwave + debris). Asset gen is pre-approved
   (keys in `tools/ai-assets/.env`, run via `uv`). Big — best split into three sub-items (one gadget at a time),
   each its own pass (item + assets + VFX + tests). *(Stasis pairs with the scan system; the blaster must honour
   the same protection checks as mining so it can't grief towns/other players' zones.)*
   **SHIPPED 2026-06-08 (3 commits, design via AskUserQuestion).** All three are **reusable, blueprint-gated
   gadgets** that right-click at the aim point, cost **suit energy** with a per-player **cooldown** (a shared
   `ToolKind.Gadget` + `UseGadgetIntent` + `HandleUseGadget` framework; the medpack decision: the existing basic
   medpack stays a self-heal consumable, this is the separate area-heal gadget). **(a) Field medkit** — heals you
   + every on-foot ally within 6 blocks (+45 HP), green pulse VFX + heal chime. **(b) Stasis projector** — freezes
   creatures within 7 blocks for 6 s (no movement, no biting → safe to scan); icy-blue stasis shell + `NetCreature.
   Frozen`; cyan burst VFX. **(c) Terrain blaster** — clears a radius-3 sphere of terrain to air **with no loot**
   (chosen for balance — not a super-miner), honouring ship/settlement/station/landing-zone protection + leaving
   indestructible blocks, broadcasting `BlockChanged` per cell + waking fluids; orange detonation + debris VFX.
   Each has an **OpenAI icon**, an **ElevenLabs sound**, a handheld emitter held-model, DE/EN locale, and a test
   (`FieldMedkit_…`, `StasisProjector_…`, `TerrainBlaster_…`); 384 green. *VFX are local-to-user for now;
   multiplayer-visible gadget flashes could be a follow-up.* Playtest: unlock + craft each at a workshop, then
   right-click — heal a teammate, freeze + scan a creature, blast a crater.
37. **Craftable radio beacon — a placeable block that appears as a labelled point on the map + minimap.**
   **[✅ DONE 2026-06-08 — playtest]** *(Analysis first. Backlog only — not started, requested 2026-06-08.)* A **craftable, placeable block** the
   player drops on a planet; once placed it shows up on the **world map** and the **minimap** as **its own point**
   with a **player-typed, freely-editable label** (a personal waypoint/landmark, e.g. "Eisensee", "Basis 1"). It is
   **unlocked as a blueprint first** (not available from the start) and has **OpenAI-generated textures**. Needs: a
   new block (`blocks.json`) + its blueprint/craft recipe + unlock; a **server-tracked beacon entity** (position +
   label + owner, **persisted per world**, networked to clients) created on place and removed on mine; a small
   **label-entry UI** when placing (type the name); **map + minimap rendering** of beacons (a distinct marker +
   the label, like the existing station/landing markers); and the **OpenAI texture**. Bilingual UI strings.
   Medium — touches blocks/blueprints, a new persisted entity + message, and the map/minimap client layers.

   **[ANALYSIS DONE 2026-06-08 — plan below, mirrors the door entity pattern]**
   - **Reuse:** the **door** entity is the exact template — a server-tracked, per-world-persisted, networked entity
     created in `HandlePlace` and removed in `HandleMine` (`GameServerDoors.cs`, `StoredDoor` via
     `IWorldRepository`/SQLite, `DoorList` over NetCodec). Beacons copy this wholesale.
   - **Data:** append `radio_beacon` to `blocks.json` (drops `radio_beacon`), `items.json`
     (`placesBlock: radio_beacon`), `recipes.json` (workshop, `requiredBlueprint: radio_beacon`),
     `blueprints.json` (category Tools). OpenAI **block** texture via `gen_textures.py` + **item** icon via
     `gen_item_icons.py`. 5 locale keys each in en/de (block/item name+desc, blueprint name+desc).
   - **Server:** new `GameServerBeacons.cs` partial — `ServerBeacon { Id, Pos, Label, OwnerId }`,
     `LoadedWorld.Beacons` + `NextBeaconId`, `StoredBeacon` DTO + `SaveBeacon/ListBeacons/DeleteBeacon` on the repo,
     `PlaceBeacon`/`RemovePlayerBeaconAt`/`LoadBeacons`/`BroadcastBeacons`/`SendBeacons`. Hook `HandlePlace`
     (`blockDef.Key == "radio_beacon"`) + `HandleMine`. Beacon block stays an **air cell + entity** (like doors) so
     the label travels with the entity, not the voxel.
   - **Networking:** `NetBeacon { Id, X, Y, Z, Label, OwnerId }` + `BeaconList`; register a free NetCodec tag;
     a `SetBeaconLabelIntent { Id, Label }` (rename) registered too. Sent on join + on change.
   - **Client:** label-entry overlay on place (uses `UiKit.AddInput`) → sends label with the place (new
     `PlaceBlockIntent.Label` field, free on MessagePack) ; rename via interact (E) on an owned beacon.
     `GameBootstrap` stores `Beacons`; `WorldMap.cs` draws a beacon glyph + label (extend `PoiLook`); `HudUi.cs`
     compass gets beacon blips.
   - **Tests:** `BeaconTests.cs` mirroring `PlaceableDoorTests` — place→persist→reload→mine lifecycle, label set,
     locale parity.
   - **Open design questions (asking before impl):** (a) is the label **re-editable** after placement, or only typed
     once at placement? (b) are beacon map markers **visible to everyone** in the world, or **owner-only** (personal)?
   **[SHIPPED 2026-06-08]** Chosen: **re-editable** (type at placement + rename later) and **visible to everyone**
   (owner stored; only the owner renames). Implemented exactly to the door-entity template: new `radio_beacon`
   block/item/recipe (workshop, `requiredBlueprint`)/blueprint (Tools) + OpenAI block texture + item icon + 5 DE/EN
   locale keys each (+ `ui.beacon.*` overlay strings). Server: `GameServerBeacons.cs` (`ServerBeacon`,
   place/remove/load/broadcast/rename, owner-gated + reach-checked), `StoredBeacon` table + `Save/List/DeleteBeacon`,
   `LoadedWorld.Beacons`/`NextBeaconId`; hooked into `HandlePlace`/`BreakBlockAt` (covers area-mine) + the terrain
   blaster cleanup + join/world-init sends. The beacon is a **real voxel block** (mesher draws it + the block edit
   persists) plus a metadata entity for the label/owner. Networking: `NetBeacon`/`BeaconList` (tag 100),
   `SetBeaconLabelIntent` (tag 101), `PlaceBlockIntent.Label`. Client: a modal `BeaconLabelUi` (name at placement /
   E-rename your own), markers + names on the world map (`WorldMap`), amber blips on the HUD compass (`HudUi`), and a
   floating world-space name above the block (`BeaconView`). 4 `BeaconTests` (place/persist/reload/mine/owner-rename);
   **389 tests green** (locale parity included); client rebuilt. Playtest: unlock + craft at a workshop, place →
   name it → see it on M + compass + above the block; walk up + E to rename; mine to take it back.
38. **Fixed, pre-planned landing zones (capacity-limited, no-build, ship-size aware).** **[✅ DONE 2026-06-08 —
   playtest]** *(ANALYSIS FIRST — ask clarifying questions before any implementation. Backlog only — not started, requested 2026-06-08.)* Planets,
   moons and **landable asteroids** should have a set of **fixed landing zones for ships**, **baked into the
   generated map** (deterministic positions, not the current dynamic march): a player always lands on one of these
   pre-planned pads. The **number of zones varies with map size** (a small asteroid has few, a big planet many).
   **No building** is allowed on a landing zone (reserve the pad). In **multiplayer**, when a body is **full** (all
   its zones occupied) that must be **shown in-game** (e.g. on the star map / when trying to land — "all landing
   zones taken"). The design **must account for variable ship sizes** — it should be possible to own **bigger ships
   than the starter**, so a zone needs enough clearance for the largest ship (or zones come in sizes / a big ship
   needs a big pad). *Builds on what exists:* there is already a per-player `LandingZone` (`GameServerSpace.
   EnsureLandingZone`) with **protection** (others can't mine/build in it — `IsLandingZoneBlockedForOther`) and the
   B36 **dry-land + settlement-clear + collision-free** search; this feature turns that **dynamic** allocation into
   a **fixed, map-planned set with a capacity limit + a "full" signal + ship-size clearance**, and extends the
   no-build rule to *everyone* on the pad. *Open questions to raise at planning:* how many zones per size class +
   how spaced; are zones visible markers on the surface/map; what happens when full (refuse landing / queue / send
   elsewhere); do zones have size tiers for big vs small ships, or one generous size; how this interacts with the
   existing settlement/station placement + the persisted per-player zones (migration); single-player behaviour
   (always a free pad?). Medium-large — worldgen planning + server allocation/capacity + UI for "full" + ship-size
   handling. *(Relates to the bigger-ships goal and to item 20 "build in space / player station".)*

   **[ANALYSIS DONE 2026-06-08 — plan below; asking 4 design questions before impl]**
   - **As-is:** `EnsureLandingZone(playerId)` (`GameServerSpace.cs`) is **dynamic** — marches +X at `LandingZoneSpacing=24`
     from `baseIndex = _landingZones.Count`, prefers dry land (B36 `LandingFootprintWet`), and persists **one zone per
     `(player, body)` forever** (`landing_zone` table, key `player_id+location_id`). `LandingZone { PlayerId, LocationId,
     CenterX, CenterZ, Radius=8, Protected }`. Protection (`IsLandingZoneBlockedForOther`) blocks only **other** players.
     Landing: client `SpaceView` E → `SendLeaveSpace(bodyId)` → `HandleLeaveSpace`/`HandleTravel` → `StampShip` at the
     zone centre + spawn. Star map `NetBody` has **no** occupancy. Settlement/wreck anchor at the **first** zone (+48,+48 /
     −56,+56). Largest ship = `hauler` 7×9 (halfX3/halfZ4); the current radius-8 pad (17×17) already clears it.
   - **Plan:** turn the dynamic march into a **deterministic, map-planned pad set per body**:
     1. `LandingPads(body)` helper: from `seed ^ StableHash("landingpad:"+key)` + circumference, place **N pads** at
        deterministic equator-band longitudes, each nudged deterministically to dry, settlement/wreck-clear ground.
        **N by size class** (asteroid 1–2, moon 2–4, planet 4–8; scaled by circumference). **One generous pad size**
        (radius 8 — already clears the hauler; no size tiers).
     2. **Live occupancy** (proposed): a pad is held by whoever is on the body, released when they leave for space/another
        world; `LoadedWorld` keeps `pad→playerId`. Replace `EnsureLandingZone` with `AssignPad(player)` (free-pad finder).
     3. **Full signal:** no free pad on a land attempt → `Reject("land", "all landing zones taken")`; `NetBody` gains
        `PadsTotal`/`PadsFree` so the star map + land prompt show occupancy / "FULL".
     4. **No-build for everyone:** a pad-area check that blocks **all** players (not just others) from mining/placing on a
        pad (reserve it); the assigned ship still stamps there.
     5. **Re-anchor** settlement/wreck offsets to deterministic **pad[0]** (removes the player-dependency).
     6. **Migration:** old per-player `landing_zone` rows are superseded by deterministic pads — ignore/clear them on load
        for bodies using fixed pads.
     7. **Client:** star map per-body occupancy + "FULL"; world-map/compass pad markers; optional visible pad platform.
     8. **Tests:** deterministic pad set (same seed→same pads), capacity (N players fill N pads, N+1 refused), release on
        leave, no-build-for-everyone, dry-land pads.
   - **DECISIONS 2026-06-08:** (a) **live occupancy** — a pad is held while the player is on the body, released on
     leave/disconnect (SP never full); (b) **player picks the pad** — landing opens a pad chooser (keyboard 1–N in the
     flight view, no cursor) showing free/occupied; (c) **refuse + message** when full ("all landing zones taken") +
     "FULL"/free-count on the star map; (d) **invisible pads** — no built platform, terrain stays natural, pad shown
     only as a map/compass marker. One generous pad size (radius 8, clears the hauler). **N = a seeded-random count
     per body within its size-class range** (so it varies body to body, deterministically): asteroid 1–2, moon 2–4,
     planet 4–8 — asteroids fewest, moons more, planets most. Messages: `RequestLandingPadsIntent{BodyId}` →
     `LandingPadList{BodyId, Pads[]{Index,X,Z,Occupied,Occupant}}`; `LeaveSpaceIntent`/`TravelIntent` gain `PadIndex`;
     `NetBody` gains `PadsTotal`/`PadsFree`. No-build on a pad applies to **everyone**.
   **[SHIPPED 2026-06-08]** Replaced the dynamic per-player landing zones with deterministic, seeded-random pad
   sets. Server: rewrote `GameServerSpace.cs` — `LandingPad {Index,CenterX,CenterY,CenterZ}`, `PadCountFor`
   (seeded-random per size class: asteroid 1–2, moon 2–4, planet 4–8), `BuildLandingPads` (pad 0 on the prime
   meridian, the rest spread round the body, each nudged to dry land), live occupancy derived from sessions
   (`AssignedPadIndex` + on-body + not-in-space), `TryClaimPad`/`ClaimPadOrReject` (refuse when taken/full),
   `PlayerPad`, `IsOnLandingPad` (reserves only the **landing volume** above the pad — placing blocked for everyone,
   mining + building high above are fine), star-map occupancy via `ToNetBody`. Hooked into `HandleTravel` /
   `HandleLeaveSpace` (same-body relocate) / join / `StampShip` / settlement+wreck anchoring (now pad 0). **Cleanup:**
   deleted the old `EnsureLandingZone` march, `LandingZone` struct, the `landing_zone` table + `Save/ListLandingZone`
   repo methods, and `IsLandingZoneBlockedForOther`. Messages: `RequestLandingPadsIntent`/`LandingPadList`/
   `NetLandingPad` (tags 102/103), `LeaveSpaceIntent`/`TravelIntent.PadIndex`, `NetBody.PadsTotal/Free`. Client: a
   keyboard **pad chooser** in the flight view (E/L → request pads → pick a free pad 1–N, "ALL FULL" when none),
   pad markers on the world map (green free / red taken), and the star-map cards show `⊕ free/total` or `VOLL`.
   **Plus (requested mid-build):** in MP, others on the body now see the landing/launch **animation** of the moving
   player's ship — `ShipTransitFx` (tag 104) broadcast on land (descend) + launch (ascend) to the others present,
   played by a new `ShipTransitView` (procedural hull tinted to the mover's hull colour, eased descent/ascent,
   engine glow + thruster roar). 3 new `LandingPadTests` replace the old zone tests; **392 tests green**; client
   rebuilt. Playtest: fly to a body, press E → pick a pad; fill a small body's pads in MP to see FULL + the refuse;
   watch another player's ship land/launch. *(Star-map "Travel" auto-picks the first free pad; the in-flight E/L is
   the manual chooser.)*
39. **Document where the ASP admin dashboard is reached.** *(Docs only — backlog, requested 2026-06-08.)* There is
   an ASP.NET server admin component (`src/Spacecraft.Api`); **find out how/where its admin dashboard is opened**
   (which executable/host serves it, the **URL/port**, any route/auth, and how to launch it) and **make sure that's
   written in the README** (and/or the project docs) so it's discoverable — currently it doesn't appear to be
   documented. *Steps when tackled:* check `Spacecraft.Api` (Program/Startup, launch settings, any dashboard
   page/Razor/Blazor), confirm the real access path by running it, then add a short "Admin dashboard" section to
   `README.md` (URL, how to start, what it's for). Small — investigation + a docs edit.

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
