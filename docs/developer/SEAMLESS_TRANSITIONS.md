# Seamless transitions: landing, lift-off, docking

How the client turns the switch between a surface (or a station) and the flight view into one continuous, cinematic
scene (#3139–#3142). Purely presentation — the server moves the player exactly as before; the client only decides what
is on screen while it happens.

## The problem it solves

The flight view (`SpaceView`) is its own scene at `SceneOrigin` (y 6000) with the player camera re-parented under it;
the surface is the chunk world around the player. Before #3140 every switch was a cut:

- a **landing** flew the ship away from a frozen camera into the planet sphere (the "re-entry" was a closed shield
  bubble — players saw "a red orb"), then cut to the black `WorldLoadingOverlay` veil for at least 3 s;
- a **launch** cut from the cockpit to black space, the ship rising for 1.6 s;
- **docking** cut to the same veil;
- a **hyperjump** landing could show the OLD planet for seconds (#3139): `SpaceClosed` went out before the server had
  built the cold destination world, the veil only armed on the later `WorldReset`, and the client stood on the old
  world's chunks in between.

## The pieces

| Piece | Where | Role |
|---|---|---|
| `TransitionGate` | `Client.Core` (tested in `Client.Tests/TransitionGateTests`) | The pure rule for when an arrival cover may open: the arrival signal for its kind came in **after** the cover began (a landing: the touchdown snap `RespawnNotice "@srv.land.touchdown"`; a docking: the station's world reset), and the player is settled (`WorldReady`, no snap pending, `PlayerController.IsSettling` false). A minimum against flicker, a 25 s give-up. |
| `TransitionCover` | client | The seam: a full-screen uGUI cover that belongs to the scene — **Clouds** (drifting puffs over a sky-tinted wash, the destination's colours), **Glare** (an airless body's retro burn; honours Reduce flashes) or **Doors** (hangar doors with hazard stripes). Code-drawn textures, the `cloud_rush` wind loop and the door sounds. Sorting order 74 (above the HUD, under the loading veil). |
| `TransitionDirector` | client, `[DefaultExecutionOrder(212)]` | Orchestrates: closes and opens the cover, runs the gate, flies the **touchdown** and the **lift-off** cinematics on the surface (taking the player camera like the cutscene rig: `CinematicCameraActive` + the cutscene-gate reason `transition`, Esc / pad cancel skips), and hands the flight view its **emergence**. |
| `ShipCinematicFx` | client | Shared pieces: a render-only stand-in of a parked hull, engine fire out of the hull's real exhausts, dust in the ground's colour (`Atlas.AverageColor` of the block under the pad), the touchdown burst, steam, the vapour cone. `ShipTransitView` (other players' and traders' landings and launches, #3177) uses them too. |
| `ShipTransitView` (+ `.Cinematic`) | client | Another pilot's or a trader's ship seen from the surface (#3177): a landing FX waits up to 0.75 s for `LandedShipView` to build the parked hull (checked in `LateUpdate`, so it is hidden before it is ever drawn), then a stand-in sinks from ~56 blocks with the touchdown beats and the real hull shows at the thump; a launch FX waits for `LandedShipView.HandOverDeparting` to hand the removed hull over (`AdoptDeparting`) and flies it with the lift-off beats. No camera takeover. The old ghost stays the fallback when no hull comes. |
| `SpaceView.Descent` / `SpaceView.Ascent` | client | The flight view's halves: the descent (chase camera, belly-first arc at the SURFACE, an open bow-shock cap of `FxShell` + plasma ribbons in the destination's air colour, the sky turning from black to the planet's, `Starfield.SkyFade`), and the climb out (the cover parts, the sky fades to black, the stars come out) or the slide out of a station's hangar mouth. |

## The flows

**Landing.** `SpaceView` starts `BeginDescent()` when the flight closes. At 70 % of the 3.9 s descent it calls
`TransitionDirector.CoverLanding()`; the descent ends (`Exit()`) only once the cover is shut. The gate then waits for the
touchdown snap and a settled player — however long the server takes to build the world. `WorldLoadingOverlay` sees
`OwnsArrival` and raises no veil. When the gate opens, the director hides the real parked hull (its colliders stay where
the server put it), flies a stand-in down from ~40 blocks, opens the cover on that shot, plays the touchdown (dust,
thump, `ship_touchdown`, steam) and glides the camera into the player's eyes.

**Early start (#3152).** Since #3139 the server builds the destination world before it closes the flight, so waiting for
`!Game.InSpace` left the ship hanging for that build. A pad picked in the chooser (`LandOnPad`, `LandOnBasePad`) starts
the descent at once (`BeginEarlyDescent`): `_descentAwaitingServer` clamps the sequence at its end behind the shut cover
and keeps `Exit()` back until the flight has closed (the `!InSpace` branch clears the flag). An `ActionRejected` "land" or
"travel" (`GameBootstrap.LandRefusals`) or 24 s without an answer (below the gate's 25 s give-up) calls it off (`AbortDescent`): `CancelArrival()` opens the
cover, the ship goes back to the pose of the pick, cruise resumes; a timeout keeps the destination. `CoverLanding` notes only live signals while the flight is still open — its counters would still hold the previous close. No flight pose is sent meanwhile (`SendShipMove` is a
cruise-only payload), so the server's ship never moved.

**Pad chooser (#3151).** `ShowPadLoading` puts the chooser's panel (title, spinner, the first-visit note, Cancel) up the
moment the request goes out; the map replaces it. The re-request comes after 8 s and the give-up after 30 s in all — a
first pad search on a slow machine takes ~10 s (#3068).

**What steps aside.** The flight view's target HUD (`HideTargetHud`, also the scanner's lock frame since #3153) outside
cruise; the suit lamp (shader light + cone on the player camera) while `CinematicCameraActive` (#3154) — dark, not
switched off; and the flight view's old black landing fade (it still timed the 2.2 s fly-away and went black 1.2 s into
the 3.9 s dive) — with a director the cover is the only seam. The descent looks the launch body up under "" (the id
`SpaceView.Scene` lists it with), not under the id the chooser resolved it to — it used to dive at the body ahead of the nose.

**Lift-off.** When the server opens the flight, `SpaceView` asks `TransitionDirector.ClaimDeparture()` every frame
before `Enter()`; true holds the flight view back. The server removed the parked hull in the same batch —
`LandedShipView` hands the local player's own hull to the director (`AdoptDepartingShip`, colliders stripped) instead of
destroying it, so the very ship the player stood in lifts off: ignition (`engine_ignite`), dust, a heavy rise, the nose
up, the climb (`ship_launch`), the vapour cone (`sonic_boom`), into the closing clouds. Once shut the director hands
off; `SpaceView.Enter()` takes the emergence (`TakeEmergence`: sky colour, airless, roar already played) and the ascent
opens the cover. A transit reports its take-off stage only after all of it, so `ServerConfig.TransitLaunchTimeoutSeconds`
is 15 s.

**Stations.** The dock approach (#1917) ends with `CoverDocking()` — the doors close, and only once they are shut is
the board intent sent; the gate opens them on the station's settled world. Undocking: `ClaimDeparture()` sees the world
before the last reset was a station and closes the doors; the ascent waits (doors shut) until the station's hull design
and hangar mouth are known, places the ship inside the mouth and opens the doors on it sliding out (`_yaw` faces away
from the station when the pilot takes over).

**Hyperjumps.** A jump that lands on a new world (`WorldReset.Hyperjump`) holds `HyperspaceWarp` in its tunnel — before
the punch-through flash — until `WorldReady`, and turns its overlay opaque when the flight view ended under it.

**Server side (#3139).** `HandleTravel` builds the destination world before `LeaveSpace`, then re-asserts the active
world (leaving the flight can send a docked visitor home). `LandingWorldOrderTests` pins it with a spy transport.

## The shot plan and the cockpit camera (#3258)

The touchdown and the lift-off used to pick their camera spot with Physics (`CheckSphere` + one `Linecast` to the hull at
rest, and an unchecked "straight above the rear" fallback). Chunk colliders are not cooked yet on a fresh world or beyond
the collider range, the line of sight was never checked along the flight, and the glides never at all — on a pad in a
hall or a courtyard the camera ended up inside the buildings, where the chunk visibility walk (#1823, active because the
player is aboard) correctly hid everything it could not see: "only the plants were left". Now every shot is planned up
front against the client's voxel world, like the VEGA prologue (#777) and the cutscene rig (#2468):

| Piece | Where | Role |
|---|---|---|
| `TransitionFlightPath` | `Client.Core` | The touchdown stand-in's drop and ease-out and the lift-off's rise, hover and climb as pure functions of the stage time, plus the beat constants. The director flies the hull with them; the plan samples the very same path. |
| `TransitionShotPlan` (+ `.Planner`, `.Sampling`) | `Client.Core` (tested in `Client.Tests/TransitionShotPlanTests`) | Picks the outside spot and the stretch it sees. |
| `TransitionCockpit` | `Client.Core` (tested in `Client.Tests/TransitionCockpitTests`) | Where the cockpit camera sits and looks, and whether the way helm ↔ eyes is open. |
| `TransitionDirector.Shot.cs` | client | The samplers, the cockpit rig, the safety net, the blink, the log line. |

**Samplers.** The prologue's: a cell is clear only when its chunk is loaded and it is air (`ClientWorld.TryGetBlock`);
the *spot* sampler also blocks every parked hull (`LandedShipBlockAt`), the *path* sampler blocks other parked ships but
reads the own hull as air (every look target sits inside it). One exception to "unknown = blocked": above the first chunk
the server has not streamed over the pad (the near column reaches a few chunks over the player, the stand-in starts
higher) is open sky — a sky island or an arch over the pad streams with its column.

**Candidates.** 8 directions (rear quarters first — the engines in frame — then the front quarters, the sides, rear,
front) × 2 distances (the hull's radius + `max(10, r·1.6)` for the touchdown / `max(11, r·1.8)` for the lift-off, and
70 % of that) × 3 heights over the hull's bottom (ground 2.2 / 1.8, roofline `max(6, hull height + 1)`, crane
`max(14, hull height + 8)`). Each stage is sampled at 16 moments: the camera spot clear (`SpotClear`, not inside the
hull's box), the lines of sight to the hull's centre **and** near its top clear (`PathClear`), and the camera's way from
the neighbouring sample clear (covers the touchdown's push-in and the lift-off's follow). The score is the share of the
shot played outside, a small bonus for a clear glide between the spot and the eyes, and a small preference for the low,
rear-quarter, far spot: a clear glide outweighs a direction, a height step outweighs a glide, so on open ground the shot
is exactly the classic one (the search stops at the first spot nothing can beat — one candidate on open ground).

**Touchdown.** Outside from the start while the spot sees the descent; at its first blocked sample the shot cuts into the
cockpit — back on the dust beat (`TransitionFlightPath.DustHeight`) when that is at most 0.6 s earlier, and not at all
before 1 s (then the cover opens on the cockpit). The thump, the dust and the steam play inside (the camera shakes with
them). Home to the eyes: from outside the classic push-in and glide when that way is clear (checked live — a skip starts
it from another pose), else a 0.25 s black blink (`CinematicFrame` fade, sorting order 73); from the cockpit a glide through
the cabin when the straight way crosses no cell of the ship, else the blink.

**Lift-off.** The classic glide out of the eyes to the spot only when the spot sees all of the lift-off and that way is
clear. Else the lift-off starts at the helm (a cabin glide or a blink) and cuts outside on the first beat — lift, climb,
boom — from which the spot sees the rest of the climb (or the first visible sample after the last beat); a spot that would
show less than 0.5 s before the clouds is not worth the cut, and a lift-off no spot ever sees stays at the helm up to the
cloud cover.

**Cockpit camera.** The helm cell is the ship's `"cockpit"` station (the stock ships' console, `Game.Stations`) or a
`ship_helm` block (self-built ships). The camera stands one cell behind it — against the nose, which runs from the engines'
centroid to the helm (+Z without engines) — at 1.6 above the floor there (over the helm itself when that cell is built
up), looking along the nose 8° down. It is set in the hull's own frame and follows the stand-in / adopted hull every frame,
so it sinks, rises and sways with it. A ship without a helm cell looks from the player's own eyes toward the nose.

**Safety net.** Every 4 frames the director checks the outside shot's spot and its line of sight to the hull's centre
now (`CinematicStageScan.CameraClear`); a block placed or a chunk streamed in after the plan cuts into the cockpit at
once (the lift-off then stays inside).

**Log.** One line per cinematic, e.g. `[Transition] shot plan: touchdown: outside 0–2.2 s, cockpit 2.2–4.1 s, cabin glide
to the eyes; spot rear-right near crane (sees 0.0–2.2 s), 48 of 48 spots sampled; cockpit camera behind the helm.`

The viewmodel/avatar hiding, the hatch doors (#3159), the skip and the cover/gate logic are unchanged; a plan costs a few
thousand cell reads (about 12 000 in a courtyard where all 48 spots are sampled).

## Adding to it

- A new arrival kind: add it to `TransitionArrival` and its signal to `TransitionGate.Arrived` (with a test), then a
  `Cover…()` entry point on the director.
- Never move what the server placed: fly render-only stand-ins (`ShipCinematicFx.CloneRenderers`) and hide the real
  renderers meanwhile.
- A new camera move in a transition cinematic: put its path into `TransitionFlightPath` / the plan's camera functions so
  `TransitionShotPlan` samples it — never a spot or a glide the voxel scan has not seen (#3258).
- Anything that holds the screen must give up on its own (the gate's 25 s, the undock's 1.5 s wait for the hull).
