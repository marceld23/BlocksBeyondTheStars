# World Wrap (walk around the planet) — Plan

**Status (2026-06-19):** **SUPERSEDED / SHIPPED as a full TORUS** — W0–W4 shipped, and **W5 (poles) was
DROPPED** (user decision 2026-06-11) in favour of wrapping latitude (Z) exactly like longitude (X). The
world is now a **torus**, not the cylinder-with-poles this doc originally planned: you can walk in **any**
direction (east/west **and** north/south, diagonals included) and loop back seamlessly with no invisible
barrier. The per-axis seam-free requirement holds in both axes. See the **"Round worlds (torus)"** DONE
block in [../../TODO.md](../../TODO.md) for the authoritative live status; this doc is retained for the original
analysis and the W0–W4 phasing history. (Sections 1, 4 and 5 below describe the *cylinder* design that was
implemented for X first and then extended to a torus — read them as historical.) Written in English per
project doc policy.

> **Progress / what actually shipped:**
> - ✅ **W0** — `WorldConstants.Circumference` (helpers `WrapX`/`WrapDeltaX`/`CanonicalChunk`/
>   `CanonicalBlock`). NOTE: circumference is **no longer a single global 6000** — it is **per body**:
>   `WorldConstants.CircumferenceFor(bodyKey, sizeClass)` sizes asteroids (800–1600), moons (2500–4000)
>   and planets (5000–12000), all chunk-aligned. The `Circumference = 6000` constant is now just the
>   default for tests/legacy callers. Every wrap/canonicalize helper takes an explicit circumference
>   (there is deliberately NO default overload for `CanonicalChunk`/`CanonicalBlock` — using 6000 on a
>   differently-sized world was the "cannot mine any block" bug).
> - ✅ **W1** — seam-free generation via circular-domain noise. The X-only helpers `Noise.FbmCylX`/
>   `ValueCylX`/`Value4D` exist but are **superseded by the torus helpers** `Noise.FbmTorus`/`ValueTorus`/
>   `Value5D`, which are periodic in **both** X and Z. Surface height, biomes, caves and ore are periodic
>   across X = 0 ≡ X = C **and** Z = ±LatitudePeriod/2; proven by `WorldWrapTests`.
> - ✅ **W2** — `ServerWorld`/`ClientWorld` canonicalize the block/chunk/persistence boundary (now on
>   both axes: `WrapZ`/`WrapDeltaZ`/`CanonicalChunkZ`).
> - ✅ **W3** — server wraps player longitude AND latitude (`HandleMove`) and streams across both seams.
> - ✅ **W4** — client renders seamlessly (`GameBootstrap.SceneX`/`SceneZ` + per-block chunk reposition on
>   X or Z; entities/remotes/NPCs via `ScenePos`; world map wrap-aware) and interaction is cross-seam
>   (canonical mine/place/repair, wrap-aware `WithinReach`/`LandingZone.Contains`/wreck mask).
>   **Rule for every client compare (#2613):** the server sends positions canonical, the player's transform
>   runs unbounded past the seam — so a server position is only ever compared with a scene position through
>   `GameBootstrap.ScenePos`/`SceneX`/`SceneZ`, `WorldConstants.WrapDeltaX`/`WrapDeltaZ`, or, for cells,
>   `WorldConstants.SameCell`. The "what does the key reach" picks (the person E talks to, the crate G/H work
>   on, the crate in the crosshair, the base zone) live in Client.Core's `InteractReach`, and the key handler and
>   its HUD prompt call the same pick. The home landing pad lies at X = 0 on every body, so a base built beside
>   the ship straddles the seam: a raw `==` or `-` there broke crates, NPC talk, the base-core and beacon aim,
>   the base-zone hint and the air dome for a whole session (Lyxette's ice moon, 2026-10-15).
> - ❌ **W5 — poles: DROPPED 2026-06-11.** The ice-wall/pole barrier was never built; instead latitude (Z)
>   now wraps (torus), so "north" loops straight back to "south" — no pole at all. See the torus DONE block
>   in TODO.md.
> - ⏳ **Deferred** — a rare floating-origin rebase after very many laps (float precision); the world map
>   waypoint stays in scene space (goes stale after a full lap). Neither affects normal play.

---

## 1. Target topology — a cylinder, not a true sphere  *(HISTORICAL — superseded by a torus)*

> **Superseded.** This section is the *original* cylinder plan (X wraps, Z bounded by poles). It shipped
> for X first, then the poles were dropped and Z was made to wrap too, so the **actual topology is a torus**
> (both axes wrap). The day/night `DayCircumference` is also no longer a single 6000 — circumference is
> per-body (`WorldConstants.CircumferenceFor`). Read this section for the historical rationale only.

A flat `(x, z)` heightmap cannot be a real sphere without pole distortion. The practical, seam-free model
that reads as "round planet" was first chosen to be a **cylinder**:

- **X = longitude → wraps** at a fixed circumference `C`. Walk `C` blocks east → back to the start.
- **Z = latitude → bounded** between two impassable **poles** *(NOT shipped — replaced by a Z-wrap)*.
- This matched the day/night model: `GameBootstrap.DayCircumference` treats world-X as longitude for the
  terminator.

The cylinder was then extended to a **torus** (wrap Z exactly like X) instead of adding pole barriers — see
the status block above and the torus DONE block in TODO.md. A "true sphere" with tapering longitude near
the poles remains out of scope (large distortion/projection problem).

---

## 2. The hard part: seam-free terrain (the user's explicit requirement)

Today terrain is a **pure, non-periodic** function of `(x, z)` — `WorldGenerator.SurfaceHeight`,
`BiomeIndexAt`, caves, and ore all feed raw `x` into `Fbm2D` / the `Noise` hash, which is a chaotic
XOR-multiply chain with **no periodicity** (`src/BlocksBeyondTheStars.WorldGeneration/Noise.cs`,
`WorldGenerator.cs:47-74`). Wrapping `x` naively (`x % C`) would put two unrelated noise values next to each
other at the seam → a hard cliff/biome wall.

**Fix — make the X axis periodic by sampling noise on a circle.** Instead of sampling along a straight
X line, map longitude onto a circle of circumference `C` and sample a higher-dimensional noise domain:

```
theta = 2*pi * x / C
nx = cos(theta) * R          // R chosen so the circle's arc-length frequency == the old linear frequency:
ny = sin(theta) * R          // R = C / (2*pi)   (then divide by TerrainScale as before)
height = Fbm( seed, nx/scale, ny/scale, z/scale )   // z stays linear (bounded latitude)
```

Because `cos/sin` are exactly periodic, `noise(x=0) == noise(x=C)` **including slope** — a continuous,
invisible seam. This is the standard "tileable noise via circular domain" technique.

**Scope of this change — every place raw X enters noise must move to the circular mapping:**
- `WorldGenerator.SurfaceHeight` (terrain height)
- `WorldGenerator.BiomeIndexAt` / `BiomeIndex` (biome field — must wrap too, or biomes seam)
- cave / 3D density noise (any `x`-dependent sample)
- ore / resource frequency fields
- the per-biome weather offset field if it samples `x`

Add a single helper, e.g. `Noise.SampleCyl(seed, x, z, C, scale)`, and route **all** X-dependent samples
through it so the mapping is defined once.

---

## 3. Wrapping everything else (or it corrupts)

Once `x` is canonical in `[0, C)`, every subsystem that consumes X must agree, or persistence collides and
rendering tears at the seam. Checklist (each verified against today's code):

| Area | Today | Needed |
|---|---|---|
| **Player position** | unbounded float, stored as-is (`GameServer.HandleMove`, `PlayerController`) | wrap X into `[0,C)` on the **server** (authority) and mirror on the client transform so the camera doesn't jump |
| **Movement / camera** | continuous transform | when crossing the seam, shift the local world origin by `C` so the `CharacterController` never sees a teleport (a "floating-origin"-style rebase at the seam) |
| **Chunk streaming** | `ChunkCoord(cx,cz)` raw, `HashSet<ChunkCoord>` (`GameServer.StreamChunks:781`) | chunk-X computed `mod (C/16)`; near the seam, stream the wrapped neighbours from the far side |
| **Persistence** | `block_edit PRIMARY KEY (planet,x,y,z)` raw (`SqliteWorldRepository:53`) | store **canonical wrapped X** so `x=100` and `x=100+C` are the same cell (else duplicate/overwrite) |
| **Distance math** | `dx = a.x - b.x` everywhere | wrapped delta `dx = wrapDelta(a.x-b.x, C)` (= shortest way round) for render culling, AI aggro, interaction reach, radar, remote-player placement |
| **Landing zones** | march `index * spacing` in +X forever (`GameServerSpace.EnsureLandingZone`) | space players around the circumference (`(index*spacing) mod C`) with a min-gap guard; cap players-per-world |
| **Settlements / wrecks / ship stamp** | absolute X (`GameServerSettlements`, `GameServerShipStructure.StampShip`) | place at wrapped X; ship/structure stamps near the seam must wrap their block writes |
| **Remote players / entities** | absolute positions rendered | render at the wrapped-nearest copy relative to the local camera (so a player just across the seam appears adjacent, not `C` away) |

---

## 4. Suggested phasing (each phase shippable + testable, server stays green)

- **W0 — Decide constant & topology.** Promote `DayCircumference` to an authoritative
  `WorldConstants.Circumference` (server + shared); pick `C` (start with 6000 to match day length) and the
  pole half-width. Pure refactor, no behaviour change.
- **W1 — Seam-free generation (offline-provable).** Add `Noise.SampleCyl` and route all X-dependent
  worldgen through it. **Test:** assert `SurfaceHeight(x=0)==SurfaceHeight(x=C)`, `BiomeIndexAt` equal, and
  bounded slope across the seam, for many seeds. This is the user's core requirement and is fully unit-
  testable without the client.
- **W2 — Canonical persistence + distance helpers.** Introduce `WrapX`/`WrapDelta`; route block-edit keys,
  landing-zone/settlement/wreck placement, and all distance checks through them. **Test:** an edit at `x`
  and at `x+C` resolve to the same DB row; landing zones never collide.
- **W3 — Server position authority.** Wrap player X in `HandleMove`; wrap chunk streaming + far-side
  neighbour streaming. **Test:** walking past `C` reports `x≈0`; chunks stream continuously across the seam.
- **W4 — Client seam crossing.** Floating-origin rebase so the camera/`CharacterController` cross the seam
  without a visible jump; render remote players/entities at wrapped-nearest. Build-only verify + manual.
- ~~**W5 — Poles.** Bound Z with an ice-wall/barrier biome and turn-back; tune so it reads as a planet.~~
  **DROPPED 2026-06-11.** Superseded by the round-worlds **torus**: instead of bounding latitude with a
  pole barrier, Z now wraps exactly like X (period ≈ circumference/2). See the torus DONE block in TODO.md
  (`Noise.FbmTorus`/`Value5D`, `WorldConstants.WrapZ`/`CanonicalChunkZ`, the N–S streaming/reposition path).

---

## 5. Risks / open questions

- **Cross-cutting blast radius.** Distance math is sprinkled across AI, interaction, radar, rendering — a
  missed `dx` is a subtle seam bug (things ignore you / pop) rather than a crash. Centralising in
  `WrapDelta` and grepping every `\.x -` site is mandatory.
- **Circumference vs. exploration.** `C=6000` is ~6 km around. Big enough to feel open, small enough to lap.
  *(Shipped: this is now per-body, not global — `WorldConstants.CircumferenceFor` sizes asteroids 800–1600,
  moons 2500–4000, planets 5000–12000.)*
- **Existing saves.** Worlds generated pre-wrap have edits at `x>C`; a migration must `WrapX` existing rows
  (or gate wrap behind new-world creation only). Decide before shipping W2.
- **Structures straddling the seam.** Ship stamp / settlement footprints that cross `x=0` must write wrapped
  blocks — easiest to **forbid placement within `footprint/2` of the seam** initially.
- **True sphere** (pole-convergent longitude) is explicitly **out of scope** here; cylinder first.

---

## 6. Bottom line

Feasible and self-contained if phased. The seam-free requirement is solved by **circular-domain noise**
(W1) and is independently unit-testable before any client work. The larger cost is the discipline of
routing **every** X coordinate through one wrap helper (W2–W4). Recommend starting with W0+W1 since they
prove the hardest part (no visible seam) with zero gameplay risk.
