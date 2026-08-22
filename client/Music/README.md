# Background-music library (source of truth)

The 51 MP3 tracks of the **Tracks** music mode (40 Suno-generated + 11 composed with the ElevenLabs Music
API via `tools/ai-assets/gen_music.py`). See
[docs/developer/MUSIC_TRACKS.md](../../docs/developer/MUSIC_TRACKS.md) for every track's prompt, context
mapping and the pools in `MusicLibrary`.

This folder lives **outside `client/Assets/` on purpose** (#1167):

- As `Resources/` assets Unity imported every song, re-encoded it to AAC at 100 % quality and baked the
  whole library (164 MB → ~205 MB) into the WebGL player data file — so each browser visitor downloaded
  all 40 songs before the first frame (193 MB of the ~208 MB first load on glitch.fun / `/play`).
- Here Unity never sees the files. `scripts/sync-client-libs.ps1` / `.sh` (run by every build path) copy
  them verbatim to the git-ignored `client/Assets/StreamingAssets/music/`, and `ClientMusic` streams a
  track on first use with `UnityWebRequestMultimedia` — over HTTP in the browser, from disk on desktop.

Adding a track: drop the `.mp3` here, add it to the matching pool in `MusicLibrary`
(`src/BlocksBeyondTheStars.Client.Core/Music/MusicLibrary.cs`; `MusicLibraryTests` checks that every pool
entry ships from this folder), document it in `MUSIC_TRACKS.md`. Keep the music out of `client/Assets/StreamingAssets/data/` — that folder's manifest is
prefetched eagerly by the browser client, which would defeat the on-demand loading.
