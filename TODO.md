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
24. **Make asteroid bodies landable in the flight view** *(from item 5, Stage 6; re-scoped 2026-06-07 — MEDIUM,
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
