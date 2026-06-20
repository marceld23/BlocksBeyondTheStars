# UI & Render Concept — unified sci‑fi look

A design concept for a **consistent, futuristic sci‑fi** presentation across every screen and for a
**fancier rendered look** of the game world. All in‑game text stays **bilingual (DE/EN)**; only
permissive/own assets, logged in `NOTICES.md`.

> **Status (2026-06-19) — partly implemented; see [../../TODO.md](../../TODO.md) for live status.** Done vs. open
> at a glance:
>
> **Render overhaul (§6) — largely DONE** (in-game verified, see TODO "stylised-clean sci‑fi" / render-look
> entries): depth + opaque textures on (Phase 0); post polish (bloom, ACES tonemap, teal-orange grade,
> lens flare, motion blur, AlphaToMask foliage — Phase 1); **god-rays**, **water SSR**, **depth-aware water +
> refraction** (Phase 3); the **VFX migration** off cube-FX (ParticleSystem thruster/explosion/mining/weapon/
> dust); plus directional block-light shading on the baked flood-fill, per-world sky colour, nebula/sun/
> celestial phases, and the temperature/weather/waterfall screen FX. Block lighting stays **custom-unlit**
> (the §6 "lit shader" was deliberately *not* done — wins come from screen-space post + VFX, see TODO).
>
> **Splash (§5) — DONE differently:** the pre-Unity black-startup gap is filled by a separate **launcher
> splash** (animated stars/title/progress bar); the in-engine rendered intro of §5 was superseded by it.
>
> **UI system (§1–§4, §7) — MIGRATED to uGUI (M27 rework):** the shell, menus and HUD are now built on a
> central **uGUI** component kit (`UiKit.cs` — procedural Canvas/Image/Text, deep-navy panels + cyan accents,
> DPI scaling, `TransitionIn`/`ReducedMotion`), with a bundled sci-fi font (`Resources/fonts/Rajdhani-Medium`,
> OFL; LegacyRuntime only as fallback) and the §2 blue-holographic palette in code (see [ART_BIBLE.md](ART_BIBLE.md)
> §3). IMGUI (`OnGUI`) now survives **only** in a few screen-space effects (weather rain/frost overlay,
> world-space labels), not in the shell/menus. Menu polish (hotbar quick-slots, tab dimming, menu badges,
> the Story Log tab) is in. **Still open from §3/§4/§7:** a full authored **line-icon set** (icons are still
> procedural/PNG-atlas, not the unified white line-icon library) and UI Toolkit is not used (uGUI was the
> chosen path).

## 1. Visual identity

- **Theme:** clean, futuristic spaceship UI — dark "space" backdrop with glowing **blue** accents,
  thin holographic frames, subtle scanlines/grid, soft glow. Calm and readable, not noisy.
- **Mood words:** holographic, translucent, precise, high‑tech, deep‑space.

## 2. Colour scheme

- **Menu base:** deep navy / near‑black blue (e.g. `#0A1020` → `#0E1A33`), slightly translucent
  panels over a blurred/space background.
- **Primary accent (blue):** electric/cyan blue (e.g. `#3FA9FF` / `#00D0FF`) for panel borders,
  active tabs, sliders, focus, key lines. **Menus are blue.**
- **Text:** **white** (`#FFFFFF`) primary, light‑grey (`#B8C4D8`) secondary/disabled.
- **Icons:** **white**, monochrome **line icons** (consistent stroke weight), tinted by the accent
  when active/selected.
- **Semantic / allegiance colours** (shared with the space radar plan):
  - **white = neutral**, **blue = friendly**, **red = hostile**.
  - health/danger **red** (`#FF5A5A`), oxygen **cyan**, energy **amber/yellow**, hunger **orange**,
    success **green** — used sparingly, only for state meaning.
- **Sun/atmosphere tints** still recolour the *world*, never the UI chrome (UI stays on‑brand).

## 3. Typography

- One clean techno/sans family (e.g. a permissively‑licensed font like Orbitron/Exo for headers +
  a readable sans for body); **white** text, generous tracking on headers. Sizes on a simple scale
  (H1/H2/body/caption). Localised strings must fit both DE and EN (German runs longer).

## 4. Unified UI system (components)

- Move the shell + menus from raw **IMGUI** to **uGUI (or UI Toolkit)** with a single **theme**
  (colours, font, 8‑px spacing grid, corner radius, glow) so every screen looks the same.
- A small **component kit**, reused everywhere (main menu, settings, in‑game `GameMenu` tabs,
  HUD panels, dialogs):
  - **Panel:** translucent dark‑blue with a thin glowing border + cut/【corner】 accents.
  - **Button:** outline → fills with accent on hover; pressed/disabled states.
  - **Tabs:** underline/edge‑glow on the active tab.
  - **Slider / toggle / dropdown / text field:** accent track/handle, white labels.
  - **Tooltip / toast:** small translucent card (the HUD toast adopts this).
  - **List/grid rows** (inventory, crafting, missions, hangar) with hover + selected states.
  - **Icon set:** white line icons for items/blocks, vitals (health/oxygen/energy/hunger), stations,
    compass/minimap, map/mission markers, hotbar — replacing text/emoji placeholders. Can be
    generated procedurally first (extend `IconFactory`), then authored/AI‑made later (same slots).
- **Layout:** centred, grid‑aligned panels (fixes the noted off‑centre settings menu); consistent
  margins/headers/footers; controller/keyboard focus order. Accessibility flags (larger UI,
  reduced effects) already in `ClientSettings` drive scale + glow.
- **HUD:** vitals as white icons + bars in a holographic frame; hotbar with framed slots + glow on
  the selected one; ship compass/minimap and the **space radar** (minimap + colour‑coded edge
  arrows, see the radar plan) in the same style.

## 5. Splash screen overhaul (new rendering)

- Replace the current static splash with a **rendered intro**: an animated **starfield / slow
  warp‑streak** background, the **Blocks Beyond the Stars logo** revealing with a glow/scanline sweep, a thin
  progress/​"initialising" line, then a smooth fade into the main menu. A short **hyperspace‑style**
  flourish ties it to the in‑game warp jump. Skippable (key/click), shortened by "reduced effects".
- Built as a real rendered scene (camera + code‑built starfield/particles + the logo), not an IMGUI
  rectangle — the first thing that sells the sci‑fi look.

## 6. Renderer overhaul — fancy sci‑fi world look

Goal: lift the world from "unlit voxels + global tint" to a richer lit, glossy, glowing look while
keeping the blocky charm and staying performant (quality presets, incl. Potato, must still run).

- **Lit block shader:** move blocks from unlit to a **lit** shader (vertex normals + the directional
  sun + ambient + the planned **suit lamp / point lights**), so faces shade by light direction and
  the sun **colour** + day/night read naturally (replaces the global `_Sc_Light` tint approximation).
- **Per‑material reflectivity** (ties into the "reflective materials" plan): smoothness/metalness
  per block — matte (mud/dirt), glossy (ice/glass), metallic (iron/titanium), sparkly (crystal) —
  so the sun/lamps make material‑specific specular highlights.
- **Emissive:** glowing blocks/props (crystal shimmer, lava, placed lights, bioluminescent
  creatures, ship lights) emit light/bloom and stay visible at night.
- **Post‑processing stack:** bloom (glow), tonemapping/colour‑grading (cool sci‑fi grade), subtle
  ambient occlusion, vignette, and **fog** (depth/atmosphere — ties into the atmosphere‑driven
  view‑distance plan; space/asteroids get a far/clear fog + starfield).
- **Better skies:** a proper starfield + sun disc (shared by surface space‑sky bodies and the space
  view), planets as lit spheres, nicer water/lava (animated, translucent/refractive) shaders.
- **Particles/VFX:** weather (blocky clouds + rain/snow/dust + lightning), thruster trails, weapon
  shots/impacts, mining/placing feedback, collision sparks, hyperspace warp streaks.
- **Performance:** everything gated by the quality presets; the lit/post stack scales down (or off)
  on low tiers so the Potato build still runs. Greedy meshing / chunk LOD as needed.

## 7. Phasing

- **Concept (this doc) → component kit + theme** (uGUI/UI Toolkit) → reskin shell + menus + HUD →
  **icon set** → **splash overhaul** → **renderer overhaul** (lit shader → materials → post stack →
  skies/particles). Sequence within **M27 (art, icons & polish)** plus a dedicated **render
  milestone**; interleave with the weather/lighting/reflective‑materials plans (they share the lit
  shader + post stack). Server stays authoritative throughout — this is presentation only.
