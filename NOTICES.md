# NOTICES

Spacecraft is licensed under the MIT License (see `LICENSE`). This file records attribution
for third-party software and assets bundled with the project. Add every new dependency or
asset here **with its licence** before bundling it.

## Bundled assets (textures, models, audio, fonts)

**None.** The client ships **no third-party art or audio files**. All visuals are
runtime-generated placeholders (flat/vertex block colours, code-built avatars, IMGUI text) and
all sound effects are **generated procedurally in code** (`ClientAudio`); there is no texture
atlas, model, sound/music file or custom font bundled. See
`docs/CLIENT_SHELL_AND_ASSETS.md` for the placeholder strategy and the asset folder layout
real assets will drop into later.

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
