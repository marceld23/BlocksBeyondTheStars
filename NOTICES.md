# NOTICES

Spacecraft is licensed under the MIT License (see `LICENSE`). This file records attribution
for third-party software and assets bundled with the project. Add every new dependency or
asset here **with its licence** before bundling it.

## Bundled assets (textures, models, audio, fonts)

**Sound effects:** `client/Assets/Resources/audio/*.mp3` (93 files, incl. the splash intro) are **AI-generated** with the
**ElevenLabs** text-to-sound-effects API by the project owner — see `tools/ai-assets/gen_batch.py`
for the exact prompts and `docs/SOUND_DESIGN.md` for the catalogue. They are AI-synthesised audio
(not third-party copyrighted recordings); use is governed by the **ElevenLabs Terms of Service**
for the generating account's plan. Background **music** and the remaining UI/feedback cues stay
**generated procedurally in code** (`ClientMusic`, `ClientAudio`).

**No third-party textures, models or fonts** are bundled — all visuals remain runtime-generated
(vertex/atlas block colours, code-built avatars, procedural icons/IMGUI). See
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

- **LiteNetLib** — MIT — reliable UDP transport (`Spacecraft.Networking`).
- **MessagePack-CSharp** — MIT — network serialization (`NetCodec`).
- **Microsoft.Data.Sqlite** — MIT — SQLite persistence (`Spacecraft.Persistence`).
- **System.Text.Json** — MIT — config/definition/locale serialization.
- **xUnit** — Apache-2.0 — test framework (test project only, not shipped).

The Unity client additionally uses the Unity engine and its packages under the Unity
Companion / Unity software licence; those are not redistributed by this repository.
