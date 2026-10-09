# Visual effects (VFX) — how weapons, scanners, mining, gadgets and flight effects are built

Status: **implemented** (VFX overhaul, epic #2159: #2151–#2158, 2026-10-01). Normative style rules stay in
[ART_BIBLE.md](ART_BIBLE.md); this doc explains the effect system and catalogues what each effect looks like.

## Goals and constraints

- **Clean sci-fi, kid-friendly.** Readable at a glance, glowing technology, no gore.
  - Defeated creatures break apart into sparkles.
  - Robots fall to parts.
  - Bandits beam away; they are never killed on screen (see [PARENTS.md](../user/PARENTS.md)).
- **Every device has its own look**, chosen by data, not by code that guesses from item keys.
- **WebGL2 and small hardware.** The browser build runs on WebGPU where the browser has it (#2390), but
  WebGL2 stays the automatic fallback, so every effect must still work there:
  - Not available: VFX Graph (needs compute), geometry shaders, DBuffer decals, Forward+.
  - Everything is Shuriken `ParticleSystem`s, meshes and hand-written shaders.
  - Particle budgets scale per quality preset.
  - WebGPU adds its own rule: no texture *samples* (implicit derivatives) inside loops with a data-dependent
    exit — use texel loads there (the water SSR march does).
- **No URP additional lights.** They stay off by design ([ADR 0003](adr/0003-urp-custom-unlit-shaders-baked-lighting.md)).
  - Glow comes from HDR colour plus bloom.
  - "Light" on the surroundings comes from shader-global **FX lights** (below).
- **Potato/Low run LDR without bloom.** Every effect must also read as a bright core plus a soft additive halo.
- **Server authority.** Every effect is render-only.
  - The client draws *intent* effects (muzzle, beam, crack progress) at once.
  - It draws *outcome* effects (a gadget's heal or blast, a defeat) on the server's message.

## Architecture

All effect code lives in `client/Assets/BlocksBeyondTheStars/Scripts`.

| Part | What it does |
|---|---|
| `FxKit` | The toolkit on the world root (added first in `WorldRig`). See the list below. |
| `FxLights` | Up to 8 shader-global point lights: one-shot flashes, lights that track a moving point, persistent ones attached to a transform. Each frame it uploads the most relevant (intensity × radius ÷ camera distance) as `_Sc_FxLightPos/_Sc_FxLightCol/_Sc_FxLightCount`. Slots: 8; Low 4; Potato 2. |
| `FxLightBridge` | Mirrors an existing Unity `Light` (beam pad, emergency lamp, landing-engine glow, data cube, glowing creature) into an FX light. Those lights rendered nothing before. |
| `FxScanWave` | The scan wave: `_Sc_ScanWave/_Sc_ScanWaveCol/_Sc_ScanWaveParams`. It is an expanding band (sharp lead edge, soft trail, faint lines) that BlockAtlas, LitColor and VertexColorOpaque draw from their own world position. No full-screen pass, no depth texture. |
| `FxCamera` | Trauma-based camera shake (Eiserloh: trauma², rotation-only Perlin noise), weapon kick (a spring) and FOV punch. Sampled by `PlayerController` on foot and `SpaceView` in flight. Scaled by *Screen shake*, silenced by *Camera motion*. |
| `FxLook` | Unity view of the data-driven `fx` look of an item or ship module, resolved by `FxStyleResolver` (Client.Core, unit-tested). The resolver falls back to key heuristics for content without `fx`. |
| `FxShots` | On-foot weapon personalities, projectiles, slash ribbons, muzzle flashes, impacts, electric arcs. |
| `FxGadgets` | Hand scan, terrain scan, weather scan, translator, medkit, stasis, blaster, pump, teleport column, holo boxes. |
| `FxDefeat` | Hit flash (a `_HitFlash` property block on LitColor/VertexColorOpaque, a `_Color` override on Unlit parts), break-apart, beam-out. |
| `MiningFx` | Crack overlay, chips from the struck face in the block's own colours (sampled from the atlas tile), 2×2×2 break, pickup gem, place pop. |
| `OreScanView` | Terrain-scan finds as holographic ghost cubes through rock, popping in as the wave reaches them. |
| `SpaceFx`, `FxShield`, `FxSpaceDust` | Ship weapons, hostile bolts, explosions, asteroid break-up, engine plumes, tractor cone, warp zip, scan pulses and planet-scanner sweep; the shield bubble; the space dust. |
| `FxRemote` | Draws other players' actions (`ActionFx`) and every confirmed gadget outcome. |

What `FxKit` provides:

- **Shared emitters.** One world-space `ParticleSystem` per particle kind, fed only through `Emit(EmitParams)`, so there is no GameObject churn. The kinds are `Sparks` (stretched, gravity), `Motes`, `Glow` (flash cards), `Dust`, `Smoke`, `Debris` and `DebrisFloat` (lit mesh cubes).
- **A material cache** keyed by shader + style (+ colour).
- **Pooled renderers** for beams (LineRenderer + `FxBeam`), rings, shells and holo cubes.
- **One tween list** that animates all of them.
- **Density control:** `Scaled(n)` (preset × Reduced effects), `FlashScale` (Reduce flashes) and `Rich` (optional extra layers).

### Shaders

All of these are URP SubShaders. The FX shaders fall back to `Particle`/`ParticleAlpha`/`VertexColorOpaque` on the Built-in RP. Each one is listed in `GraphicsSettings` "Always Included" and in `BuildScript.RuntimeShaders`, and is warmed in `ShaderPrewarm`.

| Shader | Use |
|---|---|
| `FxCommon.hlsl` | FX light + scan-wave globals and functions, value noise. Included by BlockAtlas, LitColor, VertexColorOpaque, SkyBodyPhase, FxDebris, FxBeam, FxShell, FxTunnel, FxCrack. |
| `FxBeam` | Layered beam: white core, coloured glow, two procedural noises, optional pulse rings. Used for LineRenderers (Tile mode: uv.x in world units) and for the slash ribbons (`_Tint`). |
| `FxRing` | Flat ring / shockwave / scan ring on a quad. |
| `FxShell` | Fresnel sphere: scan shells, shield hex bubble with up to 8 hit ripples (`_FxHits` array via a property block), re-entry plasma. |
| `FxHolo` | Hologram cube. A `SRPDefaultUnlit` pass with `ZTest Greater` draws the x-ray glow behind terrain; a `UniversalForward` pass draws the bright edges in view. Queue `Transparent+50`. |
| `FxDebris` | Lit vertex-colour mesh particles (chips, rubble, robot parts). |
| `FxCrack` | Procedural Voronoi crack overlay with mining-beam heat. |
| `FxSpaceDust` | Camera-wrapped dust quads stretched along velocity, one draw call. |
| `FxTunnel` | The hyperjump tunnel streaks. |
| `Wormhole` | The wormhole rift (#2242): a jagged tear with a hot inner edge and a halo; on Medium+ (`_Sc_ScreenFx`) it bends the scene behind it via `SampleSceneColor`. See [WORMHOLES.md](WORMHOLES.md). |

Edits to existing shaders:

- `Particle`: `_Intensity` (HDR).
- BlockAtlas / LitColor / VertexColorOpaque: FX lights, scan wave and `_HitFlash` (models).
- `SkyBodyPhase`: the `_Sc_BodyScan*` planet-scanner sweep and resource dots.

**Rule:** the `_Sc_*` globals and the property-block-only values (`_HitFlash`, `_FxHits`) stay **outside**
`UnityPerMaterial` (#573). Inside it, the SRP Batcher would serve each material a stale copy.

## Data: the `fx` look

Tools (`data/items.json`, in the `tool` object) and ship modules (`data/ship_modules.json`, next to `stats`) carry:

```json
"fx": { "style": "rail", "color": "#8ce6ff", "color2": "#ffffff", "charge": 0.12, "size": 1, "speed": 0 }
```

Fields:

- `style`: one of `FxStyles`.
- `color`: primary colour, sRGB.
- `color2`: core / hot colour.
- `charge`: cosmetic charge-up time in seconds.
- `size`: scale factor.
- `speed`: projectile speed; 0 = the style's default.

`FxContentTests` guard that every weapon, drill, scanner and gadget, and every module with `weapon_damage`, has a known style and parseable colours. A new weapon with an existing style needs **no code**. A new *style* needs:

- an entry in `FxStyles`;
- a case in `FxShots` / `SpaceFx` / `FxGadgets`.

Styles in use:

| Group | Styles |
|---|---|
| On-foot weapons | `slug`, `rail`, `laser`, `plasma`, `slash`, `vibro`, `plasma_blade`, `fist`, `shock_push`, `energy_fist` |
| Drills | `drill`, `drill_hot`, `drill_crystal`, `mining_beam` |
| Scanners | `scan`, `scan_pro` |
| Gadgets | `blueprint`, `heal_pulse`, `stasis`, `blast`, `pump`, `terrain_scan`, `translate`, `weather_scan`, `generic`, `rope` (#2317: `RopeFx` — the line stays while the rope holds) |
| Ship | `twin_pulse`, `plasma_bolt`, `heavy_beam`, `drill_beam`, `tractor`, `planet_scan`, `shield`, `warp` |

## Effect catalogue

### On foot — weapons (`FxShots`)

Shots leave the **real barrel**: the viewmodel's foremost part (`HeldItem.MuzzleOf`), or the avatar's hand in third person. They used to start at the screen centre.

| Style | Look |
|---|---|
| `slug` (scrap pistol) | Brass muzzle star; a chunky glowing slug (a stream of glow cards) with a short trail; "clonk" sparks and dust. |
| `rail` (gauss) | A short charge: motes converge, the glow grows. Then an instant thin rail beam leaves expanding rings along its path; FOV punch. |
| `laser` | Layered red beam (core, glow, flowing noise), impact glow + cooling hot spot; FX-light flashes. |
| `plasma` | Wobbling violet plasma ball with an ember trail, lighting the walls as it flies (tracked FX light); splash ring on impact. |
| `slash` / `vibro` / `plasma_blade` / `fist` | 110° slash ribbons. Steel with a glint; jittering blue with electric arcs; glowing pink with an afterimage and FX light; a faint whoosh. |
| `shock_push` (shock gloves, #2278) | No ribbon — a blow. The palms glow through the 0.12 s wind-up (`fx.charge`, own push only), then a cyan ring of pushed air (`FxKit.Ring`, normal = view) leaves between the palms and runs forward, a second fainter one behind it (rich quality), a cone of air motes, a small flash + FX light, camera kick 0.6. A hit adds a small ring at the target and a dust puff at its feet. |
| `energy_fist` (energy gloves, #2278) | A gold glow at the jabbing fist with two small `ElectricArcs` round the knuckles (rich quality), camera kick 0.4; a hit throws `Impact` sparks, three arcs and a flash. |

The gloves start at the spot their blow peaks — between the palms, or in front of the jabbing fist (`Viewmodel.TryMuzzle`,
`PlayerAvatar.TryMuzzle`) — and use only existing builders, so there is no new shader.

**The daze** (#2278): while the server's `Staggered` flag is set on a creature or a planet enemy, `FxShots.Daze` draws three
or four little gold stars circling over the head of a creature or bandit, or a fizz of cyan sparks over a robot (a few
glow cards re-emitted per frame, no renderer of its own). `glove_stagger` plays once as it starts.

Hits:

- The server's hull drop flashes the creature or robot white with a small squash (`FxDefeat.HitFlash`).
- Player damage adds camera trauma.
- A red marker on a ring around the crosshair points at the nearest hostile. The server reports damage, not its source.

### Defeat (kid-friendly)

- **Creatures:** the server sends `CreatureDefeated` (it used to be indistinguishable from a despawn). The jointed cube parts come loose, tumble, shrink and pop into sparkles, with a soft puff. A despawn just goes, silently.
- **Robots** (`PlanetEnemyDefeated`): fall to parts with sparks and little bolts.
- **Bandits:** beam out in a rising teleport column.

### Mining (`MiningFx`)

- **Crack:** `FxCrack` spreads with `MiningProgress.Fraction` and wobbles on every hit. Under the mining beam it glows red→white-hot.
- **Per tick, from the struck face**, in the block's colours:
  - basic drill: dust and grit;
  - titanium: hot orange sparks;
  - diamond: cyan glitter;
  - mining beam: a continuous `FxBeam` from the tool tip plus an FX light.
- **Break:** a glow flash, 8 tumbling 2×2×2 cubes, a dust puff. Only the first 6 breaks per frame get the full effect, so a terrain blast does not spawn hundreds.
- **Pickup:** the mined resource pops up as a glowing gem, curves into the player with a chime, then the HUD tile flies to the hotbar.
- **Place:** a springy holo frame and motes.
- **Sounds:** the break sound follows the material (`ClientAudio.MineCue`); every counted hit plays `drill_impact`.
- **EVA:** asteroid and hull cells chip and break in space too.

### Scanners and gadgets (`FxGadgets`, `OreScanView`)

| Device | Look |
|---|---|
| Hand / advanced scanner | Light fan to the target, holographic bracket box with a scan plane sweeping down, data motes streaming back, short local scan wave (advanced: faster, extra ring). |
| Terrain scanner | Scan wave over the terrain (20 blocks at 14 blocks/s), ground ring, faint shell. Ores appear as ghost cubes through rock exactly as the wave reaches them, tinted by ore type. |
| Weather scanner | Probe beam into the sky, widening sky ring. |
| Creature translator | Sound-wave rings travelling to the creature, glyph motes back. |
| Medkit | Green ground ring and a rising sparkle helix. |
| Stasis projector | Cyan shockwave, shell, snow motes. |
| Terrain blaster | White flash, fireball glow, ground shockwave, sphere front, smoke, sparks, FX light, distance-scaled trauma. |
| Fluid pump | A vortex sucked into the nozzle. |
| Beam pad / teleport | Light pillar with rising rings. |
| Binoculars | Range readout in the reticle. |

Gadget outcomes play on the server's confirmation (`ActionFx.Outcome`). A refused use only shows the device's short charge glow.

### Crystal Net world effects (#2251)

Server-sent `WorldFx` kinds, played by `CreatureView.PlayWorldFx` with the shared `WeaponFx` helpers. Small and
friendly: no camera shake, and their `Radius` is always 0 — on the client a `WorldFx` radius is the reach of a blast
that throws players clear, so a device effect must never carry one.

| Kind | When | Look |
|---|---|---|
| `phase_shimmer` | a phase block opens / closes | violet sparks and a pulse ring |
| `field_flicker` | a force field switches | cyan sparks and a short flash |
| `piston_puff` | a piston pushes | a dust puff (bigger when it moved blocks) |
| `motor_sparks` | a bridge motor lays / takes up a plank | a few amber sparks |
| `signal_ping` | a signal receiver picks up its sender | a cyan pulse ring |
| `dice_win` | the dice block passes a pulse | golden sparks and a flash |
| `laser` | the drill laser cuts a cell (#2108) | a beam from the device down to the cell (here `Radius` is the depth; only `stomp`, `strike` and `sea_strike` are blasts that throw players) |

The **display faces** (#2263) are no effect but floating labels (`ScreenLabelLayer`, from `CrystalNetView`), and the
lift platform is a meshed object (`LiftView`).

### Flight (`SpaceView` + `SpaceFx`)

- **Weapons:**
  - `twin_pulse`: alternating wing pulses.
  - `plasma_bolt`: bolts with flight time.
  - `heavy_beam`: charge, then a thick beam with a muzzle ring.
  - `drill_beam`: pulse rings running into the rock.
  - Mining shots read amber.
- **Hostile fire:** red bolts. Where they land, the **shield bubble** (`FxShield`) flares with a hex ripple, tinted by the shield level. With the shield down the **hull** sparks. When the shield breaks, the bubble shatters.
- **Damage:** below 50 % hull the ship smokes. Hostiles flash on hits.
- **Destruction:**
  - Hostiles explode: flash, fireball glows, shockwave ring, their cube parts tumbling away, light, distance-scaled trauma and sound.
  - Asteroids break up into floating rock chunks with ore glints.
- **Engines** (placement fixed in #2162):
  - The plumes sit on the hull's real engines. An engine is a group of touching `engine_nozzle`/`ship_engine` blocks with open space behind them (−Z).
    - `ShipExhausts` (Client.Core, unit-tested) groups the blocks into engines. `ShipMeshBuilder.ExhaustPoints` turns them into ship-local points.
    - Each engine gets one plume at the middle of its open rear faces (max 6, biggest first). The plume grows with the engine: a 2×2 engine block flames twice as wide as a single nozzle.
  - The server stamps the stock ships' engines as `engine_nozzle`: a ship layout's `engine` element and the starter box ship's two rear corner nozzles. Before #2162 they were plain `carbon`, the client found no engine, and the one centred plume sat on the rear door.
  - A hull with no engine block (a bare custom build) flames from its lower rear corners, never from the middle of the stern, where the boarding door is.
  - A world-space glow trail streams from the engines.
  - An engine FX light sits between the engines and follows the throttle.
  - Other players' ships get the same per-engine plumes in flight (from their design). So does their landing or launch seen from the surface (`ShipTransitView`). Hostile cruisers and bandit ships have plumes too.
  - A throttle surge punches the FOV.
- **Speed and lighting:** `FxSpaceDust` turns velocity into streaks. Flight motion blur finally follows speed (`UrpScenePost.SetMotion` had no caller). The hull is lit from the system's star (`Sky.SpaceSunDir`).
- **Hyperjump** (`HyperspaceWarp`): an `FxTunnel` around the camera, the dust stretched into star lines, FOV punch at the flash; the 2D overlay remains as a faint wash.
- **Traders** warp in and out as stretched light zips.
- **Landing and launch:** the landing descent builds a re-entry plasma sheath (scaled by the body's atmosphere). Launch sends cloud wisps rushing past.
- **Planet scanner:** a pulse from the ship and a beam to the body. A band then sweeps the globe pole to pole, and the found resources glow as seeded points on it.
- **Ship scanner** (#2237, [SHIP_SCANNER.md](SHIP_SCANNER.md)): corner brackets snap onto the target and a ring fills while fire is held. The charge fans four beams from the nose to the target's corners, sweeps a holo plane over it and pulls motes back. Completion runs a scan wave over the target with a flash and a short FX light. The colour is the scanner module's `fx` look (cockpit cyan, Deep scanner teal, Quantum gold). The Quantum scanner's system sweep is a wide pulse from the ship.
- **Life pods and anomalies** (#2241): a pod blinks its beacon and its passenger waves; a rescue plays a short tractor pull. An anomaly is a soap-bubble shell with glitching cubes orbiting it, which ripples and calms when scanned.
- **Wormholes** (#2242): the `Wormhole` shader rift with arcs, motes and an FX light. Flying through runs `WormholeTransitFx` — a crack spreading over the screen, the rush, then an arrival flash. Other players see a burst at the rift (`SpaceWarpFx.Style = "wormhole"`).

### Atmosphere (#2408)

The atmosphere package's world effects ride on the same kit and the same rules (see `ADVANCED_GRAPHICS.md` for
the shader side):

- **Weather drops** (`WeatherFx3D`): one particle system fed through `SetParticles` with the simulation paused —
  the class still integrates every drop itself (open-sky columns, roofs, the hull), the system only draws them:
  velocity-stretched streaks for rain, sleet, acid and sand, round flakes for snow, hail, ash and spores. A drop
  that meets the ground within 14 m leaves a `Dust` splash (every fourth hit; none for the dry forms).
- **Shooting stars** (`ShootingStars`): a stretched particle on the night dome every 45–110 s (2–5 s in a meteor
  shower), and very rarely a slow blinking "distant ship". Off by day, in space, aboard a station.
- **Light-shaft cards** (`LightShafts`): Cloud-shader quads hanging along the sun direction from openings the chunk
  scan finds (open-sky columns at the camera's height with roofed neighbours), with `Motes` dust drifting in them.
  Only while the camera is in the shade and the sun is up; High 12 cards, Medium 6, Low none.
- **Bubbles** (`AtmosphereProbe`): `Motes` rising past the visor while submerged; the caustics and the water haze
  are shader-side (`AtmosphereCommon.hlsl`, `Sky.ApplyFog`).
- **Soft particles** (#2403): `Particle` / `ParticleAlpha` fade within 0.5 m of the depth behind them on Medium+
  (`_Sc_ScreenFx` × `_Sc_SoftParticles`).

### Multiplayer (#2158)

The client mirrors its own tool actions as a cosmetic **`FxIntent`** (tag 278):

- fields: kind = `FxActionKinds` shot / melee / mine / scan / gadget / place, item key, from/to, hit;
- rate-limited on the client (0.1 s);
- sent in the sender's frame: the scene on a world, the flight-instance frame in space.

The server (`GameServerActionFx`) validates it cheaply:

- a 12/s token bucket;
- the key must be known;
- finite floats;
- `From` within 6 blocks of the sender and `To` within 64, measured across the seam (space: 24 / 128);
- then relays it as **`ActionFx`** (tag 279) to the *other* players in the same world within 96 blocks (space: the same flight instance, 160).

A melee `ActionFx` (#2279) also moves the remote avatar: `PlayerAvatar.Swing()` for blades, tools and fists,
`PlayerAvatar.Punch(push)` for the gloves (the energy gloves alternate left and right per avatar — no sync needed), and
plays the swing cue in 3D where the other player stands (plus the hit cue at the target). Before #2279 nobody saw
or heard another player's melee attack at all.

Successful gadget uses broadcast `ActionFx` with `Outcome = true` to everyone in range, including the user. Creature kills broadcast **`CreatureDefeated`** (tag 280) before the list drops the creature.

**No protocol bump:** the messages are cosmetic, and an older peer's `NetCodec.Decode` returns null for an unknown tag (`UnknownOrMalformedExtendedFrames_AreDropped`).

## Budgets

| Preset | Particle density | FX lights | Notes |
|---|---|---|---|
| Potato | 0.35 | 2 | LDR, no bloom |
| Low | 0.6 | 4 | LDR on WebGL |
| Medium | 0.85 | 8 | |
| High | 1.0 | 8 | |

*Reduced effects* halves the density and drops the optional layers (`FxKit.Rich`).

Emitter caps per kind are in `FxKit.BaseBudget`. Full breaks are limited to 6 per frame. The space dust is 900 quads at High, scaled down with density.

## Accessibility

- **Screen shake** (0–100 %, default 70 %) scales all trauma and kicks. *Camera motion* off silences them.
- **Reduce flashes** follows XAG 118 / WCAG 2.3.1. It clamps the full-screen flashes: damage, death, ship destroyed, the hyperjump flash, the photo shutter, the flight hit flash and the big explosion and blast flashes.
- **Reduced effects** is now exposed in the settings (Comfort section).
- Colour is never the only signal. Shield vs hull also differs in shape: hex ripple vs sparks.

## Gotchas

- **New shader:**
  - add the `.meta` GUID to `GraphicsSettings` Always-Included **and** `BuildScript.RuntimeShaders`; `RuntimeShaderInclusionTests` catches missed literals;
  - keep all `Properties` inside one identical `UnityPerMaterial` cbuffer (`ShaderSrpBatcherEditModeTests`);
  - never put `_Sc_*` globals there.
- **Linear colour space:** authored colours go through `FxKit.Lin` / `ShaderColor.Srgb` before reaching a material or a particle/mesh stream.
- **A `MaterialPropertyBlock`** takes that renderer out of SRP batching. It is fine for the few short-lived effect renderers, but don't put one on terrain.
- **`FxHolo` x-ray pass** relies on the terrain's depth being written. It sits in `Transparent+50`, never in the Background queue, which is why the old `SunGlow` ore markers were hidden (#2151).
- **Positions:**
  - On a world, effect positions are scene positions; network positions go through `GameBootstrap.ScenePos`.
  - In the flight view, entity positions are local to the `SpaceScene` root; convert with `_root.transform.TransformPoint`.
- **No real lights** for effects. Use `FxLights` or `FxLightBridge`.
