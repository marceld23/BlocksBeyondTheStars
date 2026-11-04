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

## Adding to it

- A new arrival kind: add it to `TransitionArrival` and its signal to `TransitionGate.Arrived` (with a test), then a
  `Cover…()` entry point on the director.
- Never move what the server placed: fly render-only stand-ins (`ShipCinematicFx.CloneRenderers`) and hide the real
  renderers meanwhile.
- Anything that holds the screen must give up on its own (the gate's 25 s, the undock's 1.5 s wait for the hull).
