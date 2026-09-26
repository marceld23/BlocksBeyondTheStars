# ADR 0013 — The Crystal Net: a binary, visible signal network without power

- **Status:** Accepted
- **Date:** 2026-09-27
- **Context source:** [#2045](https://github.com/marceld23/BlocksBeyondTheStars/issues/2045) (parts
  #2046–#2059); the design and implementation are described in
  [../CRYSTAL_NET.md](../CRYSTAL_NET.md)

## Context

Players asked for a way to make a base *react*: a doorbell, a night light, an airlock that locks while the
alarm runs, a quarry that fills a crate. The reference everyone names is redstone, which brings signal
strength, repeaters, comparators and a wiki's worth of rules — too much for the eight-year-olds the game is
made for, and its update model (a voxel per wire, a block update per change) is expensive on the
authoritative server and on every client that re-meshes a chunk per flicker.

Several existing systems already had "ports" in all but name: the beam pad, the radio beacon, the sentry post,
the thumper, the water spout, the energy gate, the hydro tray and every lamp. A base also already has a power
concept (the base core's zone and the power relay chain, #1714) that feeds sentries; #1101 asked that nothing
new should demand energy the player has to farm.

The open questions were: signal model (bits vs strength), how the signal reaches the client, whether wires must
touch (or items may be beamed), whether machines need power, where the net may run, and whether a base keeps
working while its owner is away.

## Decision

1. **One bit, visible.** A crystal conduit carries ON/OFF only — no strength, no colours, no directions on the
   wire. A network is the connected component of conduit and device cells; it is ON when any source is ON.
   The conduit is a real, placed block that glows while ON, so a circuit can be read by looking at it. Gates
   (one logic block with AND/OR/NOT/XOR, one timer block with delay/clock/counter/toggle) sit *between*
   networks and add one beat of delay each.
2. **No power, no consumption.** Conduits and devices cost crystal to craft and nothing to run (#1101
   honoured). Every device is blueprint-gated instead; the caps are the balance.
3. **Cell-keyed rows + entity-list visuals instead of voxel state.** The signal never rides in the voxel: a
   device's owner / mode / config is a `crystal_cell` row (like beacons and beam pads), networks are rebuilt
   from rows on world activation, and the client learns which cells glow through one `CrystalNetList` per
   change instead of a `BlockChanged` per conduit. The only voxel change is the lamp swap to an unlit twin
   block, rate-limited per actuator.
4. **Paired converters for matter.** Items cross distance only through a matter sender / receiver pair (a
   named receiver picked in the sender's menu), one stack of ≤ 16 per shot, free per shot, same world only.
   No pipes, no item conduits.
5. **Planets, moons, asteroids and player stations — not ships.** A boarded station is an ordinary world and
   gets its own net; ship hulls are objects and stay out. The caller and the clone tank are planet-only
   because creatures never tick on a void world.
6. **No offline simulation.** The net ticks only on a resident world, on a 100 ms logic beat and a 500 ms
   sensor beat, with hard caps (256 cells per network, 64 networks and 32 sensors per world, 8 sound loops,
   per-owner machine caps). Timers, pulses and loops restart OFF on activation; a switch keeps its lever.
   "Your base wakes up with you."
7. **Everything rides Interact.** No new bindings: look at a switch → toggle, a button → press, a
   configurable device → its menu, on keyboard, pad and touch alike.
8. **Protocol bump accepted.** `Protocol.Version` 6 → 7 (new lists, `NetDoor.Mode`); older game versions
   cannot join a v7 server — noted in the release.

## Consequences

- **Kids can read a circuit.** A glowing line is the whole state; there is nothing hidden to debug. The price
  is expressiveness: no analog values, no compact wireless logic — a long chain of gates is visibly slow by
  design.
- **Server cost is bounded and stated.** Nothing runs on a world without net cells; on a built base the cost is
  one pass over the cells per logic beat, the entity lists gathered once per sensor beat, and at most one world
  change per actuator per 500 ms. A base at the caps is still a small fixed cost, not a world sweep.
- **Wire cost is per change, not per cell.** Both lists are sent whole on change (a delta format is the first
  optimisation if a flickering base at the cap ever shows in profiles).
- **Existing blocks gained behaviour without new blocks.** Lamps, doors, beam pads, beacons, sentries,
  thumpers, spouts, energy gates and hydro trays join the net only when a conduit is laid beside them, so an
  old base is unchanged until its owner wires it; mining the last conduit beside a port restores its plain
  behaviour.
- **Saves grow by one row per conduit / device**, and adding the blocks shifts numeric block ids as every block
  addition does (a palette break for pre-existing chunks, consistent with prior additions).
- **The no-power decision is revisable**: a later "needs a power relay in reach" rule would slot into
  `OverCrystalCap` / registration without touching the signal model. The binary model is not meant to be
  revisited — strength would undo the readability the whole system is built for.
- **Compatibility:** a v6 client meets a v7 server with a version refusal instead of a decode error; the
  release note says so.
