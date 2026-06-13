# NOTICES

Blocks Beyond the Stars is licensed under the MIT License (see `LICENSE`). This file records attribution
for third-party software and assets bundled with the project. Add every new dependency or
asset here **with its licence** before bundling it.

## Bundled assets (textures, models, audio, fonts)

**Sound effects:** `client/Assets/Resources/audio/*.mp3` (125 files, incl. the splash intro, the
`terrain_scan` prospecting pulse (Feature 40), the
`jumave_sting` studio-splash whoosh-tada, the door SFX `door_slide_open`/`door_slide_close`/`door_hinge`
the death cues `space_death`/`player_death`, the per-species creature calls `creature_call_*`, the
item-21 world ambiences `amb_ocean`/`amb_ashen`/`amb_fungal`/`amb_corrupted`/`amb_wind_high` + the
`geyser_erupt` eruption, the planet-enemy vocals `enemy_growl`/`enemy_attack`/`enemy_hurt`/`enemy_die`,
the ship-AI radio chirp `ai_blip` (VEGA companion), and the water-body ambient loops
`water_surf`/`water_brook` (coastal surf + flowing brook))
are **AI-generated** with the
**ElevenLabs** text-to-sound-effects API by the project owner — see `tools/ai-assets/gen_batch.py`
for the exact prompts and `docs/SOUND_DESIGN.md` for the catalogue. They are AI-synthesised audio
(not third-party copyrighted recordings); use is governed by the **ElevenLabs Terms of Service**
for the generating account's plan. Background **music** comes in two player-selectable sets: (1) four AI-generated ambient loops
(`music_menu`/`music_planet`/`music_space`/`music_combat`, same ElevenLabs API + terms) with
code-synthesized fallbacks; and (2) a **Suno**-generated track library of 23 instrumental ambient
tracks (`client/Assets/Resources/music/*.mp3`) composed with the **Suno** text-to-music service by
the project owner — see `docs/MUSIC_TRACKS.md` for every track's prompt and in-game context. These
are AI-synthesised instrumental audio (not third-party recordings); use is governed by the **Suno
Terms of Service** for the generating account's plan. The remaining UI/feedback cues stay
**generated procedurally in code** (`ClientMusic`, `ClientAudio`).

**UI icons:** `client/Assets/Resources/icons/*.png` (40 files, incl. menu category icons, the world-map
marker set `map_*` and the VEGA avatar `icon_vega`) are **AI-generated** with the
**OpenAI** image API (`gpt-image-1-mini`, transparent cyan line icons — see
`tools/ai-assets/gen_icons.py`); they are AI-synthesised images, use governed by the OpenAI
usage terms for the generating account.

**Item / module icons:** `client/Assets/Resources/icons/item_*.png` (content-styled inventory icons for
non-block items, ship modules, space tools, the Task-5 metal ingots + alloy/electronic components, and the
VEGA ship-AI set `item_ai_memory_fragment`/`item_ai_core_mk2`/`item_ai_core_mk3`) are
**AI-generated** with the same **OpenAI** image API
(`gpt-image-1-mini`, full-colour transparent object icons — see `tools/ai-assets/gen_item_icons.py`);
same OpenAI usage terms. Block-backed materials reuse their in-game block atlas tile instead.

**Block textures:** `client/Assets/Resources/textures/*.bytes` (raw 64x64 RGBA32 tiles, incl. the full flora set — every `flora_*` block, kelp + lily included — wood-log/tree-leaves, the Task-5 metal/rare-earth ores + alloy blocks, the item-21 `geyser_vent`, the `base_core` planet-base cornerstone, and the planet-enemy
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
`docs/CLIENT_SHELL_AND_ASSETS.md` for the placeholder strategy and asset folder layout.

When real assets are added, list each here as:

```
- <asset path> — <source/author> — <licence> — <link>
```

Only permissive licences compatible with MIT (e.g. CC0, CC-BY with attribution, OFL for
fonts) are accepted. Copyleft (e.g. CC-BY-SA for code-coupled assets) and non-commercial
(CC-*-NC) assets are **not** bundled.

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

The Unity client additionally uses the Unity engine and its packages under the Unity
Companion / Unity software licence; those are not redistributed by this repository.
