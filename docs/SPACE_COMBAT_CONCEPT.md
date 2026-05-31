# Free Space Flight & Combat — Concept & MVP

Concept and feasibility note for the space-flight / combat / enemy systems
(`anf_space_flight.md` §6–14). The spec explicitly asks for a **concept and feasibility
check before** building the full system (§6.5, §12); this document is that check, plus the
scope of the server MVP that ships with milestone **M19**.

## Decision

**Build a small, fully server-authoritative PvE slice now; defer the rest.** The ship is too
central to progression to risk on an over-scoped combat system, and the spec recommends
**no permanent ship loss by default** (§8.5). So M19 delivers:

- Local **space instances** (orbit / asteroid field) that players enter and leave.
- A simple **shield + hull** model on the ship (§8.4 "for an early version, keep it simple:
  shield points, hull points").
- **Ship-weapon blueprints** (cannon, laser, asteroid breaker, shield generator) that only
  work when the server's rules allow them (§7.2).
- Simple **NPC drones** and destructible **asteroids** in space instances, gated by rules.
- **Planet enemies** that spawn, deal proximity damage and can be killed, gated by rules.
- **No permanent ship loss**: a defeated ship is disabled, the player respawns at the
  Medbay heal-tank, and the ship is recovered to its landing base with restored hull.

Everything below the line "Deferred" is intentionally **not** in the MVP.

## Why the architecture already supports it

- **Server is authoritative (§14).** Clients send intents; the server owns every outcome —
  whether a weapon hits, whether an NPC spawns, whether a ship is damaged. The existing
  intent→validate→broadcast pattern (`OnPayload`, `Reject`, `Broadcast`) extends directly.
- **Rules already exist.** M17 added `SpaceCombat`, `ShipWeapons`, `SpaceNpcEnemies`,
  `AlienUfos`, `PlanetEnemies`, `AsteroidDestruction`, `FreeSpaceFlight` to `GameRules`
  with presets. M19 enforces them; it does not invent new settings.
- **Data-driven content.** Weapons are blueprints + recipes + ship modules in `data/`, like
  every other craftable, so no code change is needed to tune or add weapons.
- **Deterministic tick.** The authoritative `Tick(dt)` loop already drives environment and
  chunk streaming; space/enemy simulation hangs off the same loop.

## Concept answers (from §6.5)

| Question | MVP answer |
|---|---|
| Control complexity | **Arcade**, server-validated. The client flies; the server owns combat state. |
| Camera | Client concern (third-person ship cam first). Out of server scope. |
| Instance size | Small bounded volumes (one orbit / field per location), unloaded when empty. |
| Concurrent ships | MVP: the player's single shared ship vs NPCs. Per-player ships = later. |
| Multiplayer sync | Server broadcasts entity state; clients render. No client authority. |
| Docking precision | Delivered in M18 (handshake, rule-gated). |
| Hit calculation | Server-side: target by entity id, range + cooldown + energy/ammo checks, apply damage. |
| How much combat | A light PvE slice. Combat never gates core progression (mining/crafting/building). |
| Per-server toggle | Yes — `SpaceCombat = Off` disables all of it; flight/docking can still exist. |

## MVP model (server)

- **SpaceEntity**: `Id`, `Kind` (Asteroid / Drone / Ufo / Cruiser), `Hull`, `Position`,
  `Hostile`, optional loot table. In-memory, per instance.
- **SpaceInstance**: `Id` (bound to a location), `Kind` (Orbit / AsteroidField), entities,
  the set of present player ids. Created on first entry, unloaded when the last player leaves.
- **Ship combat stats** on `ShipState`: `Hull` / `Shield` current values; `HullMax` /
  `ShieldMax` / `ShieldRegenPerSecond` derived from built modules (`hull_plating`,
  `shield_generator`). Shield regenerates out of combat; hull does not (needs repair / base).
- **Weapons**: ship modules with `weapon_*` stats (`weapon_damage`, `weapon_range`,
  `weapon_cooldown`, `weapon_energy`, and a `weapon_class`: Tool vs Combat). Firing is gated:
  - `SpaceCombat == Off` → no combat weapons fire at all.
  - `ShipWeapons` decides Tool-only (asteroid breaker) vs NPC vs PvP.
  - Asteroid mining is allowed even on weapons-off servers if `AsteroidDestruction` permits it
    (§7.4: tools ≠ combat weapons).
- **No permanent loss**: hull ≤ 0 ⇒ `DisableShip` ⇒ respawn player, restore hull to max,
  clear the instance, ship "recovered to base". PvP ship damage is **not** in the MVP (the
  world has one shared ship; per-player ships come later) — `ShipDamageByPlayers` is read but
  PvP hits are rejected with a clear reason.

## Planet enemies (server)

- Gated by `PlanetEnemies` and disabled in Creative / when `PassiveCreatures`-only
  (§12.4 peaceful servers). Hostiles spawn near players on the surface, deal proximity
  damage to the player's vitals, and are killed by an `AttackEntityIntent` (using the held
  tool/weapon). Killing drops loot. Spawn rate scales with the `AlienActivity` setting.

## Deferred (explicitly not in M19)

- Per-player / multiple ships and **PvP ship combat** (needs a ship-ownership model first).
- Large cruisers / boss events (§10.5), Alien UFO special behaviour (§10.4 beyond a flag).
- Module-level damage, overheating, ammo types beyond a simple energy cost (§7.6, §8.4).
- Salvage / boarding of defeated ships (§8.5 hard-PvP path).
- Full flight physics, dogfight AI pathing — the client renders arcade flight; the server
  keeps a simple positional model.
- Unity flight/combat **UI is scaffold-only** in this milestone (hooks in `NetworkClient`).

## What ships in M19

- `docs/SPACE_COMBAT_CONCEPT.md` (this file).
- `ShipState` hull/shield + module-derived combat stats.
- Data: `ship_cannon_1`, `laser_cannon_2`, `asteroid_breaker`, `shield_generator`,
  `hull_plating` blueprints + recipes + modules, with en/de locale strings.
- Protocol: enter/leave-space, fire-weapon, attack-entity intents; space-state, entity-update,
  entity-destroyed, ship-combat-status, space-result notices + codec tags.
- `GameServerSpaceCombat` (instances, weapons, NPC drones, asteroids, ship defeat/recovery)
  and `GameServerEnemies` (planet enemies), both rule-gated and server-authoritative.
- Tests covering rule gating, firing, NPC damage, ship-defeat respawn, and planet enemies.
- Unity `NetworkClient` hooks for the new messages (rendering = later).
