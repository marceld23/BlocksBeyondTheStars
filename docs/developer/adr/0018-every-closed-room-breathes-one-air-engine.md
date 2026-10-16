# ADR 0018 — Every closed room breathes: one air engine for bases and stations, doors are bulkheads

- **Status:** Accepted
- **Date:** 2026-10-16
- **Context source:** [#2649](https://github.com/marceld23/BlocksBeyondTheStars/issues/2649) (epic) with
  [#2650](https://github.com/marceld23/BlocksBeyondTheStars/issues/2650)–[#2654](https://github.com/marceld23/BlocksBeyondTheStars/issues/2654);
  a player report of 2026-10-15; decisions by the maintainer, 2026-10-15. Supersedes the size, reach and "connected" rules
  of #794 / #2539–#2543 and the "only energy doors hold air" rule of #793.

## Context

Bases and player-built stations computed sealed-room air with two copies of a cell flood fill, under different rules:

| | Player station (#1473) | Planet base (#794) |
|---|---|---|
| Ordinary door | holds air | leaks, even closed |
| Closed room | always breathes | only if it reaches into the 17³ core zone or hangs on an energy-door chain to a supplied sealed room |
| Size | 64 000 cells per room | 64 000 cells per room and a reach box of 48–192 blocks |

A child who learned on their station that "a closed room has air" built an upper floor at their base up the stairs behind
an ordinary door, and it had no air. The blue dome over the core read as "air ends here". The limits existed because
the fill remembered every air cell in a `HashSet`: a 60 000-cell hall already cost tens of milliseconds, so one million
cells would be about half a second and 50–80 MB — impossible on the small hardware the game must run on.

## Decision

1. **One rule for bases and player stations / derelicts:** every enclosed room breathes, however many rooms and however
   big. There is no size limit, no horizontal reach box and no "connected" rule.
2. **Every door is a wall for air** — wooden, hinged, sliding or energy, open or closed.
3. **Doors are bulkheads.** A leak empties only the room it is in; rooms behind its doors keep their air. Every door within
   a base's reach is a seed of its own, so a leaky room never cuts off the rooms behind it.
4. **The base core stays the air machine.** On planets a room belongs to a base when it is reached from the core's
   neighbours or from a door within the base's reach, and onward through the doors of every sealed room found (at any
   distance). A closed room of no base does not breathe. The 17³ core zone keeps its unconditional air as start help.
5. **One engine, built for small hardware** (`AirEngine.cs`, `GameServerAirSpace.cs`):
   - The fill walks vertical spans (maximal open runs in a column). A 100³ hall is 10 000 spans, about 1.7 MB.
   - A span reaching the sky line (planet) or leaving the station box (void) is outside — the test that ends every leak.
   - Fills are time-sliced to 2 ms per tick; until a fill is done, the previous result answers, so nobody loses air
     mid-fill.
   - Unloaded cells are unknown, not a leak. Rooms the fill could not reach because of them keep their last state.
   - An emergency brake (per room 2 M cells / 400 k spans, per run 4 M cells / 600 k spans) keeps the previous result,
     logs once and never takes air away.
6. **Warnings name the opening** of a leak at its height, and only to the players in or at the damaged room. *Too
   large*, *too far* and *not connected* are gone.

## Consequences

- Rooms behind ordinary doors in existing worlds gain air; nothing that breathed before loses it.
- The energy door is no longer the condition for air; it remains the bulkhead you walk through.
- The client's dome now marks only the start zone and fades once a sealed room of the base has air (#2659).
- The bed assignment uses the engine's room identity (`AirRoomIdAt`) to prefer a bed in the same room as a work post
  (#2655–#2657), and a leak never evicts a resident (`BaseIndex.StickyInside`).
- See [BASE_LIFE_SUPPORT.md](../BASE_LIFE_SUPPORT.md) for the engine in detail.
- Amended 2026-10-16 (#2675): a generated derelict no longer breathes at all — its life support is dead, so even its
  closed rooms give no air; the engine still finds its pockets (a closed room vs a breached one). See
  [DERELICT_STATIONS.md §5](../DERELICT_STATIONS.md#5-air-gravity-falling-out).
