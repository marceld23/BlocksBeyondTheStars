# Wall climbing

**Status:** shipped with #2195 (#2188–#2194), 2026-10; hold-to-grip controls, the quick two-block pull-up and the
refused-pull-up message with #2384–#2387 (2026-10-08). Player-facing rules: [USER_MANUAL.md §5 → Climbing walls](../user/USER_MANUAL.md#climbing-walls).

On planets, moons and asteroids the player can climb any solid wall: jump at it, hold Jump to hold on, climb up, down
and sideways, let go of Jump to slide down, and pull up over the top. Ladders keep their own, older rules (#126): walk
in and go straight up.

## Where it lives

| Piece | File | Role |
|---|---|---|
| Surfaces | `Shared/Definitions/ClimbSurface.cs` | `ClimbSurfaces.Of(block)`: how a block holds a climber, from block data |
| Probe | `Client.Core/ClimbProbe.cs` | Pure cell reads: grab a wall, wall/ledge/lost ahead, side edge, overhang, pull-up target |
| Grip | `Client.Core/ClimbGrip.cs` | The hidden 0..1 grip, drain and refill, speeds and the slide |
| Movement | `client/.../PlayerController.cs` (region "wall climbing") | The state machine inside `Move()`, the pull-up, sounds, the hint |
| Pose | `client/.../PlayerAvatar.cs` `SetClimbing` / `PoseClimb` | Climb, hang, strain, slide and pull-up poses |
| First person | `client/.../Viewmodel.cs` `SetClimbing` / `PoseClimbHand` (#2287) | The held item sinks away and both suit hands climb on the same signals and rhythm as `PoseClimb` (the mirrored left holder of the #2278 gloves); the climbing gear's pads from `HeldItemShapes.ClimbGear` |
| Others | `client/.../RemotePlayers.cs` | Climb pose plus facing the wall for remote players |
| Gear | `data/items.json` `climbGrip` / `climbIce`, `Shared/State/SuitEquipment.cs` | Climbing gloves and claws |

## Why the client owns it

On-foot movement is the client's: the server takes the reported position (`GameServer.HandleMove`), as it does for
the ladder, swimming and the jetpack's thrust. Climbing therefore adds no server-side movement rules. What stays on
the server is what always does: the gear (research, crafting, equipment slots) and fall damage, which the client
reports and the server computes. A grab zeroes the vertical speed, so catching a fall on a wall reports no impact. The
slide of a spent grip (2.5 m/s) stays far below the safe-landing speed.

The only thing on the wire is the pose: `MoveIntent.Climbing` → `PlayerState.Climbing` (runtime, never saved) →
`PlayerPresence.Climbing`. All three are new fields on existing contractless MessagePack messages. That needs no
`NetCodec.Register` and no protocol bump, and an older peer simply ignores them. The receiving client turns the
avatar towards the wall itself (`RemotePlayers.TryWallYaw`: the solid neighbour at hand height closest to the
reported look), so no facing field is sent. Gloves and claws ride the presence gear mask as bits 128 and 256
(`Shared/State/GearLook.cs`, the one mask server and client both build).

## The movement chain

`Move()` decides in this order: spectator → **running pull-up** → water → ladder → **wall** → creative flight → ground
→ zero-g float → gravity and jetpack (or the glider, #2296: gliding at a wall while pushing towards it grabs the wall).
`UpdateWallClimb` runs after water, ladder and flight are known, because all of
them win, and before the vertical branches, which a climb replaces.

- **Holding on is a held button (#2384):** Jump (`InputMap.JumpHeld`: Space, pad (A), touch JUMP). A grab needs it,
  and on the wall it is the grip: let go of it and the climber slides.
- **Grab:** the player is airborne, pushes towards the wall, holds Jump, is not crouching, and the jetpack is not
  firing this frame (Jump held with suit energy left keeps flying; an empty tank lets the held Jump grab). A jump
  still rising faster than `ClimbProbe.GrabRiseLimit` (0.5 m/s) does not grab yet (#2385): it carries the player to
  its top first, and a grab still catches any fall. `ClimbProbe.TryFindWall` then needs a hold at the knees **and**
  the hands, on the axis the push points at within 60°, so a one-block step is never a wall. The grip must hold more
  than 20 %, and every let-go (crouch, a lost wall) starts a 0.4 s cooldown.
- **Air pull-up:** before the grab, `ClimbProbe.TryPullUpFromJump` pulls a jump over a ledge it falls short of. The
  ledge must rise more than `JumpPullUpMinRise`: a step (0.6), and the jump's remaining rise v²/2g plus a hair — so a
  ledge the jump clears by itself (a one-block step, spring boots, a light world) is landed on, and a two-block wall
  is pulled over on the way up, a few frames after take-off (#2385). It needs no held Jump: running and hopping at a
  low wall is enough. At the top of a jump the hands can just reach a three-block edge, as before — at most frame
  rates (the stepped jump peaks a little above v²/2g); otherwise the held-Jump grab takes over there.
- **Hanging:** steering is relative to the wall. Push in to climb up, pull away to climb down, move along the wall to
  go sideways, Jump alone to hang still (mining from the wall keeps working). `Ahead` reads the wall (keep going), a
  ledge (pull up if `TryFindLedge` finds room) or nothing (let go). `WallContinues` stops a sideways step at the
  wall's edge, and `BlockedAbove` stops the way up under an overhang. The capsule leans into the wall at 1 m/s and
  auto-step is off while hanging.
- **Slide:** `ClimbGrip.SlideSpeedFor(holding, exhausted)` — Jump let go slides at 4 m/s (`ReleaseSlideSpeed`), a
  spent grip still held at 2.5 m/s, sideways steering at half speed either way; no grip is spent while sliding, and
  pressing Jump again stops the slide while the grip lasts. Both stay below the safe-landing speed on every world.
- **Refused pull-up (#2386):** pushing up at a ledge whose pull-up `TryFindLedge` refuses (no room on top, the own
  column blocked, an edge without hold) shows `ui.hud.climb_no_room` once per climb after 0.25 s, at most every 8 s.
- **Ends:** crouch drops the climber with a small push away from the wall (a real fall). Losing the wall lets go too.
  Feet on the ground while not climbing up end the climb. `SnapTo`, the speeder, a seat and a train end any climb. A
  menu opened while hanging keeps you on the wall: `ApplyGravityOnly` returns early, and a window without focus counts
  as still holding. A Jump still held when a climb ends (on the ground, after a pull-up, or through the fall after a
  let-go) does not jump until it is let go or pressed afresh (`_climbJumpLatch`), so the grip never turns into a hop
  on arrival.
- **Pull-up:** 0.35 s with the controller off. The body rises in its own column, then swings over the edge. The
  probe has already checked both paths: an open own column and a body that fits standing on top.

Climbing exists only on foot under gravity on a world surface: not `OnFootInSpace`, `StationZeroG`, `InEva`,
`Aboard` (the ship) or `CurrentStationId` (a station), and never in water, flight, sneaking, a seat or a train.

## Grip: felt, never shown

Marcel's decision: no HUD bar. The value lives in `ClimbGrip`, client-only, unsaved.

- Drain per second at 1 g on a normal wall: up 0.10 (≈ 10 s ≈ 20 blocks), sideways 0.07, hanging 0.04, down 0.03.
- It is multiplied by the world's gravity factor (asteroid 0.35–0.55, moon 0.55–0.85, planet 0.8–1.6), ×2 on a
  slippery wall, and × (1 − gear grip), with the gear capped at 0.8.
- Refill: 1.5 s from empty, but only standing on the ground or a ladder. In the air there is no refill, so letting
  go and grabbing again gains nothing.
- How the player feels it: below 40 % the climb slows, down to half speed at empty (`SpeedFactor`). Below 25 %,
  `Strain` drives a camera tremble (off with the camera-motion comfort switch), the `climb_strain` breath and a
  shaking pose. At 0 the climber slides down at 2.5 m/s and can still steer sideways (4 m/s once Jump is let go).

## Surfaces and gear (data)

- `ClimbSurfaces.Of`: no hold for air, liquids (and lava), ladders, `flora_*`, doors (category `door`, which
  includes force fields and energy fences), non-solid blocks, and `"climb": "none"` (glass).
- `"climb": "icy"` (ice) holds only with claws. Granular blocks (sand, snow, ash) are `Slippery` unless the data
  says otherwise. Everything else holds normally.
- The client additionally requires a collider (`PlayerController.IsCollidingKey`), so props, plants and walk-through
  tree crowns give no hold. Both are folded into one table per block id, rebuilt when the world or the content
  changes.
- Gear: `ItemDefinition.ClimbGrip` (share of the drain removed; best worn piece, no stacking) and `ClimbIce`.
  `climbing_gloves` (0.4) and `climbing_claws` (0.6 + ice; crafted from the gloves) sit in the module slots,
  researched in the Suit branch.

## The energy rope (#2317)

The rope gun (2026-10) shares the climb's rules and pieces. The pull is on-foot movement the client owns —
`RopeRig` in `Client.Core` (attached → pulling → hanging; the arrival by face: land on a top, pull up over a free
ledge, hang anywhere else; a snap on distance, lost sight or no progress), driven from `PlayerController.UpdateRope`
in `Move()` right before the wall climb. The server treats the shot as a gadget (`GameServerGadgets.UseRopeGun`: in
range of the eyes, a solid block behind the hit face, line of sight — checks no other gadget makes) and carries the
anchor in the presence (`Roped`, `RopeX..Z`, additive fields like `Climbing`); `ReleaseRopeIntent` (tag 291) is the
only new message. A side-face arrival with a free top ends in the climb's own `StartPullUp`. Letting go caps the fall
at half the safe landing speed (`RopeRig.CappedFall`), so the rope never reports an impact of its own; a slack rope
catches a fall past `RopeRules.CatchDrop` but not a jump. The look is `RopeFx` (the `FxBeam` line, pulses running
anchor → hand), the first-person gun turns toward the anchor (`Viewmodel.SetRoped`), the avatar's arm points at it
(`PlayerAvatar.SetRoped`), remote ropes come from the presence (`RemotePlayers`). Tests: `RopeRigTests`
(Client.Tests), `RopeGunTests` (server: data, codec, the shot's refusals, release, presence).

## Tests

- `tests/BlocksBeyondTheStars.Client.Tests/ClimbProbeTests.cs`: grab rules, the one-block step, approach angle,
  glass and slippery holds, wall/ledge/lost, the side edge, the overhang, the pull-up target and its refusals, the
  2-block jump pull-up; a jump flown frame by frame (#2385): a two-block wall is pulled over on the way up, a one-block
  step and a jump that clears the wall are landed on, a three-block edge is reached only at the top of the jump, a
  four-block wall is no pull-up and the grab waits for the top of the jump.
- `tests/BlocksBeyondTheStars.Client.Tests/ClimbGripTests.cs`: drain per motion, gravity, slippery walls and gear,
  slow, strain and slide, refill; the slide of a let-go Jump vs. a spent held grip, and that it never hurts (#2384).
- `tests/BlocksBeyondTheStars.Tests/WallClimbingTests.cs`: surfaces from the real block data, the gear, its research
  and recipes, icons, the codec, `HandleMove` → state, and presence including the gear bits.

The feel (speeds, pull-up timing, the poses) is a playtest matter: try a planet, a moon and an asteroid, each with
keyboard, gamepad and touch.
