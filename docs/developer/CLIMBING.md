# Wall climbing

**Status:** shipped with #2195 (#2188–#2194), 2026-10. Player-facing rules: [USER_MANUAL.md §5 → Climbing walls](../user/USER_MANUAL.md#climbing-walls).

On planets, moons and asteroids the player can climb any solid wall: jump at it, hold on, climb up, down and sideways,
and pull up over the top. Ladders keep their own, older rules (#126): walk in and go straight up.

## Where it lives

| Piece | File | Role |
|---|---|---|
| Surfaces | `Shared/Definitions/ClimbSurface.cs` | `ClimbSurfaces.Of(block)`: how a block holds a climber, from block data |
| Probe | `Client.Core/ClimbProbe.cs` | Pure cell reads: grab a wall, wall/ledge/lost ahead, side edge, overhang, pull-up target |
| Grip | `Client.Core/ClimbGrip.cs` | The hidden 0..1 grip, drain and refill, speeds and the slide |
| Movement | `client/.../PlayerController.cs` (region "wall climbing") | The state machine inside `Move()`, the pull-up, sounds, the hint |
| Pose | `client/.../PlayerAvatar.cs` `SetClimbing` / `PoseClimb` | Climb, hang, strain, slide and pull-up poses |
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
reported look), so no facing field is sent. Gloves and claws ride the presence gear mask as bits 128 and 256.

## The movement chain

`Move()` decides in this order: spectator → **running pull-up** → water → ladder → **wall** → creative flight → ground
→ zero-g float → gravity and jetpack. `UpdateWallClimb` runs after water, ladder and flight are known, because all of
them win, and before the vertical branches, which a climb replaces.

- **Grab:** the player is airborne, pushes towards the wall, is not crouching, and the jetpack is not firing this
  frame. `ClimbProbe.TryFindWall` then needs a hold at the knees **and** the hands, on the axis the push points at
  within 60°, so a one-block step is never a wall. The grip must hold more than 20 %, and a let-go starts a 0.4 s
  cooldown.
- **Air pull-up:** before the grab, a jump that falls short of a ledge (vertical speed ≤ 0.5) is pulled over by
  `TryFindLedgeAhead`. This is what makes 2-block walls crossable.
- **Hanging:** steering is relative to the wall. Push in or hold Jump to climb up, pull away to climb down, move
  along the wall to go sideways. `Ahead` reads the wall (keep going), a ledge (pull up if `TryFindLedge` finds room)
  or nothing (let go). `WallContinues` stops a sideways step at the wall's edge, and `BlockedAbove` stops the way up
  under an overhang. The capsule leans into the wall at 1 m/s and auto-step is off while hanging.
- **Ends:** crouch lets go with a small push away from the wall. Losing the wall lets go too. Feet on the ground
  while not climbing up end the climb. `SnapTo`, the speeder, a seat and a train end any climb. A menu opened while
  hanging keeps you on the wall: `ApplyGravityOnly` returns early.
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
  shaking pose. At 0 the climber slides down at 2.5 m/s and can still steer sideways.

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

## Tests

- `tests/BlocksBeyondTheStars.Client.Tests/ClimbProbeTests.cs`: grab rules, the one-block step, approach angle,
  glass and slippery holds, wall/ledge/lost, the side edge, the overhang, the pull-up target and its refusals, the
  2-block jump pull-up.
- `tests/BlocksBeyondTheStars.Client.Tests/ClimbGripTests.cs`: drain per motion, gravity, slippery walls and gear,
  slow, strain and slide, refill.
- `tests/BlocksBeyondTheStars.Tests/WallClimbingTests.cs`: surfaces from the real block data, the gear, its research
  and recipes, icons, the codec, `HandleMove` → state, and presence including the gear bits.

The feel (speeds, pull-up timing, the poses) is a playtest matter: try a planet, a moon and an asteroid, each with
keyboard, gamepad and touch.
