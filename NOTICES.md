# NOTICES

Blocks Beyond the Stars is licensed under the GNU Affero General Public License v3.0 or later
(AGPL-3.0-or-later — see `LICENSE`). This file records attribution
for third-party software and assets bundled with the project. Add every new dependency or
asset here **with its licence** before bundling it.

## Bundled assets (textures, models, audio, fonts)

**Sound effects:** `client/Assets/Resources/audio/*.mp3` (125 files, incl. the splash intro, the
`terrain_scan` prospecting pulse (Feature 40), the
`jumave_sting` studio-splash whoosh-tada, the door SFX `door_slide_open`/`door_slide_close`/`door_hinge`
the death cues `space_death`/`player_death`, the per-species creature calls `creature_call_*`, the
item-21 world ambiences `amb_ocean`/`amb_ashen`/`amb_fungal`/`amb_corrupted`/`amb_wind_high` + the
`geyser_erupt` eruption, the planet-enemy vocals `enemy_growl`/`enemy_attack`/`enemy_hurt`/`enemy_die`,
the ship-AI radio chirp `ai_blip` (VEGA companion), the water-body ambient loops
`water_surf`/`water_brook` (coastal surf + flowing brook), and the beam-block teleporter cues
`beam_teleport` (jump whoosh) + `beam_idle` (pad idle hum loop))
are **AI-generated** with the
**ElevenLabs** text-to-sound-effects API by the project owner — see `tools/ai-assets/gen_batch.py`
for the exact prompts and `docs/developer/SOUND_DESIGN.md` for the catalogue. They are AI-synthesised audio
(not third-party copyrighted recordings); use is governed by the **ElevenLabs Terms of Service**
for the generating account's plan. Background **music** comes in two player-selectable sets: (1) four AI-generated ambient loops
(`music_menu`/`music_planet`/`music_space`/`music_combat`, same ElevenLabs API + terms) with
code-synthesized fallbacks; and (2) a **Suno**-generated track library of 23 instrumental ambient
tracks (`client/Assets/Resources/music/*.mp3`) composed with the **Suno** text-to-music service by
the project owner — see `docs/developer/MUSIC_TRACKS.md` for every track's prompt and in-game context. These
are AI-synthesised instrumental audio (not third-party recordings); use is governed by the **Suno
Terms of Service** for the generating account's plan. The remaining UI/feedback cues stay
**generated procedurally in code** (`ClientMusic`, `ClientAudio`).

**UI icons:** `client/Assets/Resources/icons/*.png` (40 files, incl. menu category icons, the world-map
marker set `map_*` and the VEGA avatar `icon_vega`) are **AI-generated** with the
**OpenAI** image API (`gpt-image-1-mini`, transparent cyan line icons — see
`tools/ai-assets/gen_icons.py`); they are AI-synthesised images, use governed by the OpenAI
usage terms for the generating account.

**Item / module icons:** `client/Assets/Resources/icons/item_*.png` (content-styled inventory icons for
non-block items, ship modules, space tools, the Task-5 metal ingots + alloy/electronic components, the
VEGA ship-AI set `item_ai_memory_fragment`/`item_ai_core_mk2`/`item_ai_core_mk3`, the creature-taming
set `item_creature_translator`/`item_forage_bait`/`item_meat_bait`/`item_nectar_lure`, and the
material-variety tier `item_diamond`/`item_diamond_drill`/`item_polymer`/`item_biofuel`/`item_bronze_gear`/`item_brass_fitting`) are
**AI-generated** with the same **OpenAI** image API
(`gpt-image-1-mini`, full-colour transparent object icons — see `tools/ai-assets/gen_item_icons.py`);
same OpenAI usage terms. Block-backed materials reuse their in-game block atlas tile instead.

**Block textures:** `client/Assets/Resources/textures/*.bytes` (raw 64x64 RGBA32 tiles, incl. the full flora set — every `flora_*` block, kelp + lily included — wood-log/tree-leaves, the Task-5 metal/rare-earth ores + alloy blocks, the item-21 `geyser_vent`, the `base_core` planet-base cornerstone, the `beam_block` teleporter pad, the material-variety set (`detoxifier`, `diamond_ore`, `insulated_wall`, the 13 metal storage blocks `iron_block`…`titanium_block`), and the planet-enemy
`enemy_hide` chitin tile) are
**AI-generated** with the same **OpenAI** image API (`gpt-image-1-mini`, 64px pixel-art tiles — see
`tools/ai-assets/gen_textures.py`, bundled as raw bytes by `bundle_textures.py`), loaded into the
block atlas at runtime via `Texture2D.LoadRawTextureData`; same OpenAI usage terms.

**Avatar/creature textures:** `client/Assets/Resources/textures/avatar_*.bytes` (suit/armor/visor/skin)
and `creature_*.bytes` (12 hide tiles: scales/fur/chitin/hide/slime/feathers/spots/stripes/warty/plated/finned/tentacled) are **AI-generated** the same way (`gpt-image-1-mini`, grayscale
tileable 64px tiles that the avatar/creatures tint by colour — see `tools/ai-assets/gen_avatar.py` and
`gen_creatures.py`); same OpenAI usage terms.

**UI font:**

- `client/Assets/Resources/fonts/Rajdhani-Medium.ttf` — Rajdhani, by Indian Type Foundry —
  SIL Open Font License 1.1 (bundled as `Rajdhani-OFL.txt` next to the font) —
  https://fonts.google.com/specimen/Rajdhani

**No third-party models** are bundled — the rest of the visuals remain runtime-generated
(vertex/atlas block colours, code-built avatars + logo, procedural UI panels). See
`docs/developer/CLIENT_SHELL_AND_ASSETS.md` for the placeholder strategy and asset folder layout.

When real assets are added, list each here as:

```
- <asset path> — <source/author> — <licence> — <link>
```

Only **permissive** licences (e.g. CC0, CC-BY with attribution, OFL for fonts) are accepted.
Copyleft (e.g. CC-BY-SA for code-coupled assets) and non-commercial (CC-*-NC) assets are
**not** bundled. Note this stays deliberately permissive even though the project code is now
AGPL-3.0: every bundled asset and library must remain permissive or owned so the founders can
also ship the closed-platform (console) build — see the licensing note in
[CONTRIBUTING.md](CONTRIBUTING.md).

## Third-party libraries

Server / shared (.NET, via NuGet — see each project's `.csproj` for exact versions):

- **LiteNetLib** — MIT — reliable UDP transport (`BlocksBeyondTheStars.Networking`).
- **MessagePack-CSharp** — MIT — network serialization (`NetCodec`).
- **Microsoft.Data.Sqlite** — MIT — SQLite persistence (`BlocksBeyondTheStars.Persistence`).
- **System.Text.Json** — MIT — config/definition/locale serialization.
- **xUnit** — Apache-2.0 — test framework (test project only, not shipped).

Client (Unity, bundled in `client/Assets/Plugins` — vendored by `scripts/sync-velopack-libs.ps1`):

- **Velopack** — MIT — in-app installer/auto-update runtime (`ClientUpdater`); the `vpk` CLI
  (also MIT) builds the Setup.exe + update feed in `scripts/publish-client-installer.ps1`.
- **Newtonsoft.Json** — MIT — pulled in as a Velopack runtime dependency (client only).
- **.NET BCL polyfills** — MIT (.NET Foundation) — the `System.*` and `Microsoft.Bcl.*` support
  DLLs alongside the libraries above (e.g. `System.Memory`, `System.Buffers`,
  `System.Text.Json`, `System.Runtime.CompilerServices.Unsafe`, `Microsoft.Bcl.AsyncInterfaces`),
  shipped so the netstandard2.1 libs run under the Unity Mono runtime.

Client (Unity, UPM packages compiled into / shipped with the player — see `client/Packages/manifest.json`):

- **UniTask** — MIT — Cysharp — zero-allocation async/await for Unity (`com.cysharp.unitask`).
  https://github.com/Cysharp/UniTask
- **Concentus** — BSD-3-Clause (© Xiph.Org Foundation, Skype Ltd., Microsoft, Jean-Marc Valin,
  Logan Stromberg and others; a pure-C# port of the Opus codec) — the **live voice-chat** codec, shipped
  in the standard build (the `BBS_VOICE` define is on by default). Pulled in as a `Client.Core` NuGet
  reference and vendored into `client/Assets/Plugins` by `scripts/sync-client-libs.ps1`. Players can turn
  voice off in Settings. See `docs/developer/VOICE_CHAT.md`. https://github.com/lostromb/concentus

The Unity client additionally uses the Unity engine and its first-party packages (URP, Burst,
Collections, Mathematics, ShaderGraph, TextMeshPro, the test framework, etc.) under the Unity
Companion / Unity software licence; the engine runtime is redistributed as part of any Unity
player, the editor-only packages are not.

## Shipped with the build

The Windows installer (`scripts/publish-client-installer.ps1`) copies this `NOTICES.md` (as
`THIRD-PARTY-NOTICES.txt`) and the project `LICENSE` (as `LICENSE.txt`) into the player folder
before packing, so every distribution — Setup.exe, the portable zip **and** the MSI — carries the
full attribution. The in-game
**Credits** screen also names the key third-party software and points players at these files.
