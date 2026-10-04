# ADR 0014 — Moving blocks: twin swaps, step-wise bridges and pistons, server-owned lift platforms

- **Status:** Accepted
- **Date:** 2026-10-04
- **Context source:** [#2251](https://github.com/marceld23/BlocksBeyondTheStars/issues/2251) (parts #2264, #2265,
  #2266); the implementation is described in [../MOVING_BLOCKS.md](../MOVING_BLOCKS.md)

## Context

With the Crystal Net (ADR 0013) a base can *sense* and *decide*; players then asked for things that *move*: a secret
door, a trapdoor, a drawbridge, a piston, a lift. A voxel engine offers two ways to move things — rewrite blocks, or
turn blocks into moving entities with physics. The game is made for eight-year-olds on small hardware, its server is
authoritative and lightweight (no physics engine), and a moving thing must never hurt a child or trap one.

## Decision

1. **Doors and hatches are twin swaps.** A phase block, a trapdoor (and, as ports, a force field, a campfire, a forge)
   swap between a plain block and its open / unlit twin, keeping dye, glow and form — the lamp mechanism of ADR 0013.
   No new state, no new message.
2. **Bridges and pistons move blocks one step at a time.** A bridge motor places or removes one deck block per step; a
   piston pushes a short line of ordinary blocks one cell and leaves a head. Every step is an ordinary block write,
   rate-limited per device and budgeted per tick for the world. What they may touch is a positive list (ordinary
   mineable blocks the owner may edit), never containers, devices, doors or someone else's things.
3. **The lift platform is the one smooth mover — owned by the server, drawn by the client.** The server keeps only the
   platform's height and moves it at a fixed speed, refusing to descend onto a body; it sends a small list on start,
   stop and five times a second while moving. The client draws the 3×3 platform with a collider, eases it between
   lists and carries the local rider by the height change. No block is rewritten while a lift moves, and no frame of
   reference is needed for a vertical ride.
4. **Kid rule by construction.** Nothing closes onto a body (it waits); every floor that opens, and every lift ride,
   grants a short fall grace; moving blocks never hurt.
5. **Not aboard ships** (except the twin swaps): a bridge, a piston or a lift would edit a hull that is meshed in three
   places.

## Consequences

- The server stays physics-free; the cost of a moving block is a block write per step (or a float per lift per tick).
- A bridge or piston is visibly step-wise (0.25 s / 0.5 s) — readable for kids, but no smooth sliding animation.
- A lift needs a client view of its own and a rider hook in the player controller; other players see the platform
  move smoothly, the rider is carried locally and corrected by the server's position like any movement.
- Pistons cannot build "flying machines": they move at most four ordinary blocks and never a device, so a self-pushing
  contraption is impossible by design.
- Save rows grow by a few config keys (`ext=`, `out=`, `at=`) so everything resumes where it was.
