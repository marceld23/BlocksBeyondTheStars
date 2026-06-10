# URP migration — staged plan

Goal: move the Unity client from **Built-in RP** to **URP** to unlock real-time shadows, a proper
post-processing Volume (bloom / ACES tonemap / vignette / colour-grade / LUT), built-in SSAO, and a
cleaner cross-platform lighting model — the "fundamental" graphics upgrade.

## ⚠️ Hard constraint (read first)

The coding agent **cannot see the rendered image** — it can only confirm the headless build *compiles*
(`Spacecraft build: Succeeded`). A broken shader renders **magenta/black yet still "builds"**. URP also
**breaks every Built-in-RP shader until ported**. Therefore this migration MUST be **staged**, and at each
stage the **developer verifies the look in the Unity editor** (Play mode) before continuing. Do it on a
branch so it's trivially revertible.

## Current state (2026-06-10)

- Unity **6000.4.9f1**; **Built-in RP** (`GraphicsSettings.asset` `m_CustomRenderPipeline: 0`); no URP package.
- ~12 hand-written **CG** shaders (`CGPROGRAM` + `UnityCG.cginc`): `BlockAtlas`, `BlockAtlasTransparent`,
  `LitColor`, `VertexColorOpaque`, `Cloud`, `Starfield`, `SunGlow`, `Visor`, `VisorGlass`, `PostAO`,
  `PostBloom`, `PostComposite`.
- Post-FX via **`OnRenderImage`** (`PostFx.cs`: bright-pass+Gaussian bloom, ACES tonemap, SSAO, vignette,
  per-biome grade). **URP never calls `OnRenderImage`** → must be reworked.
- Diegetic **visor HUD**: a 2nd UI camera → RT, composited by `Spacecraft/Visor` in `OnRenderImage`
  (`VisorHud.cs` / `VisorComposite`). Also must move to a URP render feature.
- Sun is a directional light with **shadows OFF** (`Sky.cs`) — the main thing URP buys us.
- Block shader already does per-pixel normals + sun diffuse + specular + baked AO + skylight occlusion, driven
  by global uniforms `_Sc_SunDir` / `_Sc_LampPos/Dir/Color` (set from C#) — keep these.

## Strategy

Port each world/model shader to a **dual-pipeline shader** (add a URP `HLSLPROGRAM` SubShader tagged
`"RenderPipeline"="UniversalPipeline"`, keep the Built-in CG SubShader below it as fallback) so the project
renders in *both* pipelines during the transition and is easy to roll back. Replace the custom `OnRenderImage`
post stack with **URP Volume** components (Bloom, Tonemapping=ACES, Vignette, Color Adjustments/LUT) + URP's
**built-in SSAO** renderer feature — deleting `PostAO/PostBloom/PostComposite/PostFx`. Re-implement the visor
composite + any remaining fullscreen passes (Cloud/Starfield/SunGlow if camera-blit) as a
**ScriptableRendererFeature** (Blit pass) running after post.

## Progress (branch `urp-migration`)

- ✅ **Stage 0** — URP 17.4.0 installed (via the editor Package Manager; batch resolve hit an EPERM/AV lock).
- ✅ **Enable/Disable menu** — `Assets/Spacecraft/Editor/UrpMigration.cs` (`Spacecraft → URP`): assigns the URP
  asset to Graphics + every Quality level (and reverts), logs the effective pipeline. Uses the base
  `RenderPipelineAsset` type, so no URP asmdef ref needed.
- ✅ **BlockAtlas** dual-pipeline (URP forward + **shadow receive** + **ShadowCaster**; Built-in pass unchanged).
- ✅ **Sun shadows** under URP (`Sky.cs`: soft shadows when a SRP is active; Built-in stays shadowless).
- ✅ **VisorHud + PostFx guarded OFF under URP** (both are OnRenderImage/depthless-RT → unsupported by URP's
  render graph; HUD falls back to a flat overlay, post moves to a Volume).
- ✅ **URP post Volume** (`UrpScenePost.cs`, wired in `WorldRig`): ACES tonemap + bloom + vignette + colour grade.
  Runtime asmdef now references `Unity.RenderPipelines.Universal/Core.Runtime`.
- ✅ **Developer-verified:** real terrain shadows + post look correct under URP; world otherwise unchanged.
- ✅ **Model/creature shadows** — `LitColor` + `VertexColorOpaque` ported dual-pipeline (URP forward receives the
  main-light shadow; ShadowCaster pass casts). Avatars/NPCs/remotes, creature bodies, held items, doors,
  station/editor models all cast + receive; planet enemies flipped from `Unlit/Color` to `LitColor`. Creature
  eyes/glints stay unlit by design. **Developer-verified.**

- ✅ **Headless URP player build Succeeded** (first try died on a stale editor project lock; retry clean). The
  exe grew ~9 MB (URP shader variants). GraphicsSettings + all Quality levels reference the URP asset, so
  shipped builds are URP now.
- ✅ **SSAO** renderer feature added (by the developer, on the URP Renderer asset).
- ✅ **Assets tidied + shadows tuned:** pipeline assets moved to `Assets/Settings/SpacecraftURP(.Renderer).asset`
  (GUIDs kept via .meta); soft shadows enabled on the asset (`m_SoftShadowsSupported: 1` — the sun already asks
  for Soft), shadow distance 50→90, 2 cascades.

- ✅ **Water/glass** — `BlockAtlasTransparent` ported dual-pipeline (URP transparent pass; receives the sun
  shadow on its directional term; water clarity + milky glass + energy-field look preserved; transparents cast
  no shadows by design).
- ✅ **Per-preset shadow cost** — `ClientSettings.Apply()` scales `urp.shadowDistance` by preset (Potato 0 =
  shadows off for Pi-class machines, Low 40, Medium 70, High 90), since one URP asset serves all levels.

**Remaining (each a dev-verified step):**
- Sky/FX shaders `Cloud`/`Starfield`/`SunGlow` URP ports (render fine as SRPDefaultUnlit today; port for
  correctness + SRP batching).
- **Diegetic visor HUD** — re-implement the `Spacecraft/Visor` composite as a URP `ScriptableRendererFeature`
  (full-screen blit after post) + restore the on/off + dezent toggle; until then the HUD is a flat overlay.
- Tune shadow distance/cascades; verify the Potato preset.

## Stages (each ends with a developer visual check + a green build)

- **Stage 0 — scaffold (reversible):** branch; add `com.unity.render-pipelines.universal` to
  `client/Packages/manifest.json`; let Unity resolve. *Build still Built-in (no asset assigned).* ✅ project opens.
- **Stage 1 — pipeline assets:** an Editor script (`-executeMethod`, run by the build) creates a
  `UniversalRenderPipelineAsset` + `UniversalRendererData` (HDR on, soft shadows on, shadow distance tuned),
  saved under `client/Assets/Settings/`. **Not yet assigned.**
- **Stage 2 — world + model shaders:** port `BlockAtlas`, `BlockAtlasTransparent`, `LitColor`,
  `VertexColorOpaque` to dual-pipeline (URP SubShader keeps the exact fragment math + the `_Sc_*` globals).
  Assign the URP asset in Graphics + Quality. **DEV CHECK:** world/ship/avatar/creatures render correctly
  (not magenta), atlas + transparency + cave darkening intact.
- **Stage 3 — post-processing:** add a global URP **Volume** (Bloom, Tonemapping ACES, Vignette, Color
  Adjustments; per-biome grade driven from `Sky`/biome) + enable URP **SSAO** renderer feature; delete
  `PostFx` + `Post*` shaders. **DEV CHECK:** bloom/tonemap/AO/vignette look right at each quality preset.
- **Stage 4 — sky + HUD passes:** port `Cloud`, `Starfield`, `SunGlow`; re-implement the **visor composite**
  (`Spacecraft/Visor` + the dezent/off toggle already added) as a `ScriptableRendererFeature` after post.
  **DEV CHECK:** sky, clouds, stars, sun, and the diegetic HUD composite correctly; visor on/off works.
- **Stage 5 — real shadows + lighting:** turn the sun's shadows on (soft), set ambient + optional fog;
  tune shadow distance/bias. **DEV CHECK:** terrain/structures cast + receive shadows; no peter-panning/acne.
- **Stage 6 — polish + perf:** verify the **Potato/Pi** preset still runs (shadows/SSAO off there), drop dead
  Built-in code paths, update `docs/ADVANCED_GRAPHICS_PLAN.md`.

## Rollback

All on a branch. To abort: revert `GraphicsSettings.asset` (custom pipeline → 0) + `manifest.json` (remove URP)
+ the new Settings assets; the dual-pipeline shaders still render under Built-in.

## Risks

- Custom-lit shaders are the hard part — URP lighting/keywords differ; the dual SubShader keeps Built-in as a
  safety net. - The visor HUD + post rework is non-trivial (OnRenderImage → RendererFeature). - Every stage is
  **visually unverifiable by the agent** → developer-in-the-loop is mandatory. - Watch SRP Batcher breakage
  from per-material `Shader.Find` + `new Material` patterns (FX spawn lots of materials).
