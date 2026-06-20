# URP rendering — how it works

Status: implemented (see TODO.md for live Done/Open status). Date: 2026-06-19.

The Unity client (Unity 6000.4.9f1) renders through the **Universal Render Pipeline**. URP has been
the shipping pipeline since 2026-06-10; the migration off Built-in RP is complete and merged to main.
This doc describes the final shape of that rendering setup and where it lives.

## Overview

The move to URP was the "fundamental" graphics upgrade: it unlocked real-time sun shadows, a
maintained post-processing Volume (bloom / ACES tonemap / vignette / colour grade), URP SSAO, and a
cleaner cross-platform lighting model. The hard constraint that shaped the work: a headless build can
*compile* a broken shader (it renders magenta yet still "builds"), and URP breaks every Built-in shader
until ported — so every shader was kept **dual-pipeline** and each stage was visually verified by the
developer in the editor before continuing.

## How it works

- **Pipeline assets** live under `client/Assets/Settings/` (`BlocksBeyondTheStarsURP.asset` +
  `.Renderer.asset`). `GraphicsSettings` and every Quality level reference the URP asset, so shipped
  builds are URP. HDR on, MSAA 4×, soft shadows on (4096 shadowmap), shadow distance 110 in the asset
  with 2 cascades (runtime-scaled down per preset, see below); depth + opaque textures are **both on**
  (the gate for fog / water-depth / SSR-class effects).
- **Dual-pipeline shaders.** Every hand-written shader carries a URP `HLSLPROGRAM` SubShader
  (`"RenderPipeline"="UniversalPipeline"`) plus the original Built-in CG SubShader below it as a
  fallback, so the project renders under either pipeline and rollback stays trivial. The custom
  fragment math and the global uniforms (`_Sc_SunDir`, `_Sc_Lamp*`, `_Sc_Light`, `_Sc_FloraTint`,
  grade) were preserved 1:1. Opaque world/model shaders also gained URP shadow-receive + ShadowCaster
  passes; transparents (water/glass) receive the sun term but cast no shadow by design.
- **Post-processing** moved from the old `OnRenderImage` stack to a global URP **Volume**
  (`UrpScenePost.cs`, added by `WorldRig` and also by `MenuBackground`): ACES tonemap + bloom + vignette
  + colour adjustments (+ SMH, lens flare, motion blur, menu-blur DoF, event vignette). The per-system
  biome×sun grade (`Sky.SetGrade`) drives the Volume via `UrpScenePost.ApplyGrade`. **Caveat:** the
  Volume only takes effect on a camera with `renderPostProcessing = true`, which today is set only on the
  menu-background camera — see *Known gaps* below. **SSAO** is a renderer feature on the URP Renderer
  asset and applies regardless.
- **Diegetic visor HUD** is re-implemented as a render-graph blit pass: the `BlocksBeyondTheStars/Visor`
  shader gained a URP Blit SubShader, and `VisorUrpCompositor.cs` enqueues an `AddBlitPass` per frame
  from `RenderPipelineManager.beginCameraRendering` (code-only, no renderer-asset edits), running after
  post. The dezent/off toggle (`ClientSettings.VisorEffects`) drives its params each frame.
- **Sun shadows** are turned on (soft) in `Sky.cs` when an SRP is active; the Built-in path stays
  shadowless. Avatars, NPCs, remote players, creatures, held items, doors and station/editor models all
  cast + receive (planet enemies were flipped from `Unlit/Color` to `LitColor`); creature eyes/glints
  stay unlit by design.
- **Per-preset cost.** One URP asset serves all Quality levels, so `ClientSettings.Apply()` scales the
  effective shadow distance by preset (Potato 0 = shadows off for weak machines, Low 40, Medium 70,
  High 90) and forces depth/opaque textures off on Potato/Low so dependent effects early-out for free.

## Key files

- `client/Assets/Settings/BlocksBeyondTheStarsURP.asset` (+ `.Renderer.asset`) — pipeline + renderer config.
- `client/Assets/BlocksBeyondTheStars/Scripts/UrpScenePost.cs` — the post Volume + grade application.
- `client/Assets/BlocksBeyondTheStars/Scripts/VisorUrpCompositor.cs` — render-graph visor blit pass.
- `client/Assets/BlocksBeyondTheStars/Scripts/Sky.cs` — sun light, shadows, grade, sky colour.
- `client/Assets/BlocksBeyondTheStars/Scripts/ClientSettings.cs` — per-preset shadow / depth-texture scaling.
- `client/Assets/BlocksBeyondTheStars/Shaders/*.shader` — dual-pipeline shaders (`BlockAtlas`,
  `BlockAtlasTransparent`, `LitColor`, `VertexColorOpaque`, `Cloud`, `Starfield`, `SunGlow`, `Visor`, …).
- `client/Assets/BlocksBeyondTheStars/Editor/UrpMigration.cs` — editor menu to assign/revert the URP asset.

## Known gaps / deferred

- `renderPostProcessing` is **not** enabled on the in-game player camera (`WorldRig` builds the camera
  but never sets it), so the global URP Volume that `UrpScenePost` creates — tonemap / bloom / grade /
  lens-flare / SMH / motion-blur / event-vignette — almost certainly **does not apply during gameplay**.
  The only place `renderPostProcessing = true` is currently set is the **menu-background camera**
  (`MenuBackground.cs`), so the URP post stack is effectively a menu-only feature today. SSAO (a renderer
  feature) and the visor (an `AddBlitPass`) are independent of that flag and *do* apply in-game. Note also
  that the Built-in `PostFx` stack disables itself under URP (`PostFx.cs`: `currentRenderPipeline != null`
  ⇒ `enabled = false`), so under the shipping URP path **in-game has no full-screen tonemap/bloom/grade**
  beyond what the per-block shaders bake in. Enabling `renderPostProcessing` on the in-game camera is the
  open question.
- `PostFx.cs` and the `Post*` shaders remain as the **Built-in RP fallback** path (gamma-tuned) rather
  than being deleted; URP is the shipping path. (Because `PostFx` self-disables under URP, these only do
  anything if the project is reverted to Built-in.)
- Global metal/hull **SSR** and URP **decals** are deferred (low ROI / risky full-screen pass).
- **Rollback** (if ever needed): revert `GraphicsSettings.asset` (custom pipeline → 0), remove URP from
  `client/Packages/manifest.json`, drop the Settings assets; the dual-pipeline shaders still render
  under Built-in.
