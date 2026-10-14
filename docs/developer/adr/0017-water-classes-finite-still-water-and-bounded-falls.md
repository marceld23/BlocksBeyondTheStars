# ADR 0017 — Endless water by class, finite still water, and falls that keep their level

- **Status:** Accepted
- **Date:** 2026-10-13
- **Context source:** [#2515](https://github.com/marceld23/BlocksBeyondTheStars/issues/2515) (the water model) and
  [#2516](https://github.com/marceld23/BlocksBeyondTheStars/issues/2516) (terrain generation 24, sealed water); decisions
  W1–W7 by the maintainer, 2026-10-13

## Context

The fluid automaton (`GameServerFluids.cs`) is a Minecraft-style level model: a source cell is bottomless, a flowing cell
carries a level 7..1 and dries up without a feed. Two of its rules together made floods unbounded:

- **Every untracked water cell was a source** — the whole sea, every pond, every river cell and every block a player
  placed.
- **A fall refilled to full strength.** Every step down re-armed a fresh seven-cell spread, so on stepped ground, down a
  cave shaft and across each cave floor a flow never ended.

World generation never sealed its water: caves were carved beside the sea, ponds filled each column to its own ground (a
stepped surface), river cross-sections stood above a low bank. All of it lay dormant until a player touched it. A tester
on v2026.10.12 walled in a pond and got "new oceans in fractions of a second": one wake at a sea–cave contact flooded
15 000–31 000 cells in 40 s, 90 blocks below the sea line. Her questions set the bar: *where does the water level stop?*
and *where would a raised lake get endless water from?* The maintainer's requirement: waterfalls with (simulated)
endless water **and** flowing water that behaves believably and never floods endlessly.

A full mass-conserving automaton (water units per cell, partial-height rendering) was rejected for now: it needs a
protocol change, partial-height meshing, swimming and oxygen at partial depths, a save migration of every row, and it
keeps a disturbed body awake until it balances.

## Decision

1. **A fall keeps its level** (water and lava). A falling cell carries the level of the cell above it; a source above
   still feeds it in full. No flow travels more than about seven cells from the source that feeds it.
2. **Endless water by class** (`WorldGenerator.WaterKindAt`, netstandard2.1): everything at or below the planet's sea
   level (W7 — the sea stays endless, and so does the ground water below it), routed rivers, generated waterfall columns
   (river falls and waterfall bands — they fall but never spread sideways, and a dug-out foot stays confined like a
   `water_spout` column), and water a player placed (W1, below). Lava sources stay endless (W4).
3. **Finite still water** — every other water source: ponds, lakes, gen-1 bodies above the sea line, and water a settle
   moved. It never spreads as a flow and feeds no flow. When a woken body has somewhere to go it **settles**
   (`GameServerFluidSettle.cs`): layer by layer from the top, its cells move into the lowest air reachable through cells
   no higher than the layer (communicating vessels; a sill holds back what lies below it). A layer whose reach finds the
   open surface of lower endless water, more than eight blocks of runoff past the body, or more room than the exploration
   cap seeps away (W3). The plan is applied at a rate that follows the number of openings (one cell per step and gap —
   slow and visible, W2), a draining outlet pours a short stream into its gap, and the plan is renewed every second, so
   plugging the gap stops the drain.
4. **Caps** (W5): a body over 2 048 cells is a reservoir — endless, bounded by rule 1 — and a body that meets a cell
   already known as reservoir is one as a whole; one layer's reach explores at most 4 096 open cells (walls do not
   count, so a sealed winding body is never judged open). At most two plans run per fluid step (the rest wait a step),
   a cell another plan of the step already covered needs none of its own, and a body found at rest is not planned again
   within the second — its wakes are held back and replayed when the memo clears, never dropped. A cell whose settle
   met another running settle is retried when that one ends. Settles never read or write a chunk that is not resident
   (they end cleanly instead). Planning a body at the cap measured 8–14 ms warm (the once-a-second re-plan of a running
   settle, machine under load); the first plan of a session took 19–50 ms in the tests, JIT and the terrain generated
   under the breach included (around a player those chunks are resident).
5. **Placed water stays a source** (W1), marked in a new `fluid_source` table (`CREATE TABLE IF NOT EXISTS`). A one-time
   migration (`named_blob` key `migration.fluid_source.v1`) marks every water edit a player is the last editor of and
   that carries no level row — water placed before the marks existed. The `water_spout` stays the tool for cascades.
6. **Generation 24 seals generated water** (#2516): caves and worm tunnels keep one cell from the neighbouring columns'
   water (and a tunnel never opens the bed under a column's own water), each pond fills to one level — the lowest ground
   on its rim, a column may be dug at most two blocks below its ground to hold it —, the pond gate also looks at the
   direct neighbours, and a flowing river reach lowers its water to its lower bank (W6, no lips are raised).

## Consequences

- **Floods are bounded.** On the tester's seed, one wake at a sea–cave contact spills 87–225 cells and rests in about
  three seconds (was 15 000–31 000 and still growing); walling in a pond and filling it with sand leaves no stray water
  (was 60–1 800 cells); a woken generation-0 river waterfall stays a column (was 18 000 flowing cells).
- **Existing saves heal on load.** `LoadFluidState` wakes every saved flowing cell; re-evaluated under rule 1, a long
  tongue loses its feed and recedes (a 14 956-cell flood fell to 814 within a minute in the prototype; the rest waits at
  the loaded edge).
- **Generation-23-and-older worlds keep their open ponds**, and those are finite now: touched, an open pond drains
  through its open side — down to the sill, the water running off as a short stream — instead of flooding. That is
  intended (W2): the water runs to where it would go.
- **New worlds have fewer and flatter ponds.** One level from the lowest rim leaves the higher parts of a pond mask dry
  (one sample: 2 408 → 595 pond water cells).
- **Builders notice** that one placed block no longer cascades down terraces; it runs about seven cells. The waterfall
  block does what a cascade needs.
- **No client or protocol change.** The client draws whole cubes and `BlockChanged` carries only the block id; the
  stream, a draining layer and a moved cell are ordinary block changes.
- **No save-version change.** No block was added, the classes are positional, and the new table is additive.
- **The server stays light.** Classification is memoised per cell above the sea line; a settle runs only when a finite
  body with an open side is woken, re-plans once a second while it moves, and moves at most 24 cells per step.

## Addendum — doors are walls to fluids (#2561, 2026-10-14)

A tester's base on v2026.10.13 flooded through its wooden door: a door is an entity standing in an **air** cell, every
fluid path asked only "is it air?", and nothing may be built into a doorway, so the gap could not be plugged. Under this
ADR a woken finite pond would also have **settled** through it into the lowest air — a base floor behind the door.

Every cell a door fills (its width × the three-high opening; open or shut, any kind, hung by a player or stamped with a
structure) is now a wall to both fluids: `FluidCanEnter` (flow, fall, spout, drain stream), the settle's body collection
and reach, and a running settle's fills, which re-plan when a door is hung in a target cell. An open door is no drain —
one rule regardless of state. Lava follows the same rule because it enters cells through the same check. Fluid an older
save left in a doorway is removed when the step wakes the cell, and what it fed downstream recedes; still water that
already settled into a room stays (the manual explains how to clear it). The cells live in a per-world set rebuilt only
when the door registry changes, so the hot check stays one hash lookup without allocation; a rebuild wakes the fluid
standing in a resident doorway and the fluid beside a doorway that went away. No protocol or save change.
