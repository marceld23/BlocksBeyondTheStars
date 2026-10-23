# Marketing screenshots

Automatically generated in-game screenshots for the README, the store pages and other marketing
material, captured straight from the real game at **1920×1080**. Two sets:

```
docs/screenshots/
├── de/      ← "how it plays": the HUD set, German HUD (PNG)
├── en/      ← "how it plays": the HUD set, English HUD (PNG)
└── world/   ← "what the worlds look like": HUD-free stills — planet types, creatures, life (JPEG, one set)
```

The world stills carry no text, so there is one set for every language. They come from
`scripts/capture-stills.ps1` (see [World stills](#world-stills--capture-stillsps1) below); the HUD set
from `scripts/capture-screenshots.ps1`. The older per-planet HUD shots `de|en/surface_<key>.png`
(2026-08-09) stay in place as reference images for other material that links them; the README no
longer shows them.

## The HUD set

Gallery shows the English set (`en/`); a matching German set lives in `de/`.

<table>
  <tr>
    <td width="50%"><img src="en/start_screen.png" width="100%" alt="Main menu"><br><sub><b>start_screen.png</b> — Main menu (title, planet, ship, nebula)</sub></td>
    <td width="50%"><img src="en/cockpit_hud.png" width="100%" alt="Cockpit HUD"><br><sub><b>cockpit_hud.png</b> — The ship cockpit with the in-game HUD</sub></td>
  </tr>
  <tr>
    <td width="50%"><img src="en/cockpit_menu.png" width="100%" alt="Cockpit menu"><br><sub><b>cockpit_menu.png</b> — The in-game Tab menu open over the cockpit</sub></td>
    <td width="50%"><img src="en/space_flight.png" width="100%" alt="Space flight"><br><sub><b>space_flight.png</b> — Space flight: ship + flight HUD, asteroids and a planet behind</sub></td>
  </tr>
  <tr>
    <td width="50%"><img src="en/planet_surface.png" width="100%" alt="Planet surface"><br><sub><b>planet_surface.png</b> — Player view on a planet surface, looking out over the terrain</sub></td>
    <td width="50%"></td>
  </tr>
</table>

`planet_surface.png` stands at the best view near the landed ship (the `vista` pose, see below) in
morning light, with the block selection frame and the first-person hand hidden (#2765).

## The world set

HUD-free stills, one language-neutral set (`world/<name>.jpg`, the names are the entries of the shot manifest).

**Planet types**

<table>
  <tr>
    <td width="25%"><img src="world/world_jungle.jpg" width="100%" alt="Jungle"><br><sub>Jungle</sub></td>
    <td width="25%"><img src="world/world_lava.jpg" width="100%" alt="Lava"><br><sub>Lava</sub></td>
    <td width="25%"><img src="world/world_icecap.jpg" width="100%" alt="Ice cap"><br><sub>Ice cap</sub></td>
    <td width="25%"><img src="world/world_crystal.jpg" width="100%" alt="Crystal"><br><sub>Crystal</sub></td>
  </tr>
  <tr>
    <td width="25%"><img src="world/world_fungal.jpg" width="100%" alt="Fungal"><br><sub>Fungal</sub></td>
    <td width="25%"><img src="world/world_skylands.jpg" width="100%" alt="Skylands"><br><sub>Skylands</sub></td>
    <td width="25%"><img src="world/world_ocean.jpg" width="100%" alt="Ocean"><br><sub>Ocean</sub></td>
    <td width="25%"><img src="world/world_desert.jpg" width="100%" alt="Desert"><br><sub>Desert</sub></td>
  </tr>
  <tr>
    <td width="25%"><img src="world/world_flower_fields.jpg" width="100%" alt="Flower fields"><br><sub>Flower fields</sub></td>
    <td width="25%"><img src="world/world_rainbow_sea.jpg" width="100%" alt="Rainbow planet"><br><sub>Rainbow planet</sub></td>
    <td width="25%"><img src="world/world_savanna.jpg" width="100%" alt="Savanna"><br><sub>Savanna</sub></td>
    <td width="25%"><img src="world/world_highland.jpg" width="100%" alt="Highland"><br><sub>Highland</sub></td>
  </tr>
  <tr>
    <td width="25%"><img src="world/world_arch_lands.jpg" width="100%" alt="Arch lands"><br><sub>Arch lands</sub></td>
    <td width="25%"><img src="world/world_pillar_world.jpg" width="100%" alt="Pillar world"><br><sub>Pillar world</sub></td>
    <td width="25%"><img src="world/world_toxica_maxima.jpg" width="100%" alt="Toxica-Maxima"><br><sub>Toxica-Maxima</sub></td>
    <td width="25%"><img src="world/world_boreal.jpg" width="100%" alt="Boreal"><br><sub>Boreal</sub></td>
  </tr>
</table>

**Creatures, giants and life**

<table>
  <tr>
    <td width="25%"><img src="world/creature_boreal.jpg" width="100%" alt="Wildlife on a boreal world"><br><sub>Wildlife on a boreal world</sub></td>
    <td width="25%"><img src="world/creature_meadow.jpg" width="100%" alt="Something flying over the meadows"><br><sub>Something flying over the meadows</sub></td>
    <td width="25%"><img src="world/creature_fungal.jpg" width="100%" alt="A herd on the fungal world"><br><sub>A herd on the fungal world</sub></td>
    <td width="25%"><img src="world/giant_colossus.jpg" width="100%" alt="A colossus, 50 blocks tall"><br><sub>A colossus, 50 blocks tall</sub></td>
  </tr>
  <tr>
    <td width="25%"><img src="world/life_town.jpg" width="100%" alt="A town"><br><sub>A town</sub></td>
    <td width="25%"><img src="world/life_monument.jpg" width="100%" alt="An ancient gate"><br><sub>An ancient gate</sub></td>
    <td width="25%"><img src="world/life_astronaut.jpg" width="100%" alt="Your astronaut at a lookout"><br><sub>Your astronaut at a lookout</sub></td>
    <td width="25%"><img src="world/life_village_night.jpg" width="100%" alt="A village at night"><br><sub>A village at night</sub></td>
  </tr>
  <tr>
    <td width="25%"><img src="world/creature_jungle.jpg" width="100%" alt="Floaters over the jungle"><br><sub>Floaters over the jungle</sub></td>
    <td width="25%"><img src="world/life_reef.jpg" width="100%" alt="Under the sea"><br><sub>Under the sea</sub></td>
  </tr>
</table>

## World stills — `capture-stills.ps1`

Each still is a one-frame clip for the in-game `ClipDirector` (`-captureClip`), described by an
entry of the shot manifest [`scripts/shot-manifests/marketing.json`](../../scripts/shot-manifests/marketing.json):
a planet type, a pose, a still camera, a pinned time of day and weather. A world seed decides how a
motif turns out, so every entry lists a few `seeds`; the runner shoots each of them, builds a contact
sheet per entry, and the chosen seed goes into the entry's `pick`.

```powershell
./scripts/build-client.ps1                                      # a current player build
./scripts/capture-stills.ps1                                    # every entry × every seed (~1 min each) + contact sheets
./scripts/capture-stills.ps1 -Shots world_lava,creature_savanna # only these entries
./scripts/capture-stills.ps1 -Shots world_lava -Seeds 2468      # one entry, one seed
# look at marketing/stills/_sheets/<name>.jpg, write the chosen seed into the entry's "pick", then:
./scripts/capture-stills.ps1 -Publish                           # picks → docs/screenshots/world/<name>.jpg
```

Frames land in the git-ignored `marketing/stills/<name>/<name>__<seed>.png`, next to that run's
`Player.log` (`<name>__<seed>.log` — grep `[Capture]` and `[Clip]` when a frame looks wrong). The
contact sheets order the variants by a rough score (colour, contrast, detail; a nearly black or
single-colour frame scores low) — the pick is made by eye. Do **not** capture while a Unity build or a
test run loads the machine: the client then lifts its spawn freeze before the chunks are in, and the
pose search finds nothing.

What makes a still (fields of the clip spec, `ClipManifest.cs`):

| Field | Meaning |
|-------|---------|
| `hud: false` | The HUD-free clone camera. Since #2765 it also hides the first-person hand and the block selection frame (`GameBootstrap.CaptureCleanView`) |
| `pose: "vista"` / `"here"` | `here` stays where a `poi` jump put the player (the middle of the site) and only turns to the best view. `vista`: a raised, open, dry spot near the spawn with the best view. Every streamed column gives its standable top; the highest and most prominent tops are kept, and for each sixteen view directions are scored by marching rays through the voxels — depth, relief, variety (water, lava, flora, different ground; built blocks after a `poi` jump), minus a wall or tree crown in the first metres, the landed ship in view, or looking into the sun (`PlayerCapture.PlaceForCaptureVista`) |
| `camera: "rig"` + `camHeight` / `camBack` / `camSide` / `camPitch` | The recording camera sits above, behind and beside the player's eye and looks along the player's heading, `camPitch` degrees down — an elevated view without flight or noclip (the player stays on the ground, only the recording camera moves) |
| `camera: "subject"` + `subject` (`creature` / `giant`) + `subjectFill` | Frames an awake, wild creature near the player: the camera stands where the creature fills `subjectFill` of the frame height, sees it, and has open land behind it; it faces the camera if it can, and sits on a third line looking into the frame (`ClipDirector.TryFrameSubject`) |
| `fov` | The recording camera's vertical field of view (0 = the player's setting) |
| `astronaut: true` | Third person: the player's avatar stands in the world, for scale |
| `creatures` | The world's creature abundance at creation (0 Off … 4 Extreme) — a creature still asks for Extreme |
| `poi` | A `/tp <kind>` target (`village`, `city`, `lodge`, `monument`, `factory`, …) reached before the pose |
| `adminCommands` | Admin intents sent once the pose is reached, `"<command> [argument]"` — e.g. `summon_giant colossus` |
| `settleSeconds` | An extra wait before the frame (the spawn ring filling, a giant walking up) |
| `timeOfDay`, `weather` | Pinned on the client **and** sent to the server as `/settime` / `/setweather`, so the animals that are awake and the server's weather match the sky (which species sleep follows the server clock) |
| `seeds`, `pick` | Read by the runner only: the seeds to try, and the chosen one (0 = not picked yet) |

## How to regenerate the HUD set

The shots are produced by **running the built game player** with a `-captureShots` flag. A
self-installing in-game director then drives the client through the scene sequence and writes
each PNG. One run per language (the HUD language is fixed when the world starts), handled by the
wrapper script.

```powershell
# 1) Build the Windows player (bundles the local server that singleplayer needs)
./scripts/build-client.ps1

# 2) Capture both languages → docs/screenshots/de and /en
./scripts/capture-screenshots.ps1

# Build + capture in one go:
./scripts/capture-screenshots.ps1 -Build

# Single language / custom world seed / custom output:
./scripts/capture-screenshots.ps1 -Lang de
./scripts/capture-screenshots.ps1 -Seed 424242 -OutRoot ./docs/screenshots

# The old per-planet HUD surface shots (surface_<key>.png) are still available on request:
./scripts/capture-screenshots.ps1 -SkipMain -Planets lava -Seed 2468
```

**Requirements:** the project's Unity editor (6000.6.x) for the build, and a machine with a GPU — the
capture renders real frames, so it is *not* a headless `-nographics` run.

## How it works (for maintainers)

| Piece | File | Role |
|-------|------|------|
| Director | [`client/Assets/BlocksBeyondTheStars/Scripts/ScreenshotDirector.cs`](../../client/Assets/BlocksBeyondTheStars/Scripts/ScreenshotDirector.cs) | Self-installs when `-captureShots` is on the command line; drives the sequence and writes the PNGs (`ScreenCapture.CaptureScreenshotAsTexture`, full frame incl. HUD) |
| Editor menu | [`client/Assets/BlocksBeyondTheStars/Editor/CaptureMenu.cs`](../../client/Assets/BlocksBeyondTheStars/Editor/CaptureMenu.cs) | *BlocksBeyondTheStars → Capture Screenshots → Run (German/English)* for a quick in-editor run (set the Game view to 1920×1080 for exact size) |
| Wrapper | [`scripts/capture-screenshots.ps1`](../../scripts/capture-screenshots.ps1) | Runs the player exe once per language with the right flags |

**Sequence the director runs:** main menu → cockpit HUD → open the Tab menu (cockpit menu) →
take off to space flight → land back and step out onto the planet surface.

Most of this uses ordinary gameplay intents (no cheats). Three tiny **capture hooks** were added
because some state can't be reached without input during an unattended run:

- `SpaceView.SetFlightYaw(float)` — choose the flight heading (`FlightHeading` constant in the director).
- `PlayerController.SetCapturePose(Vector3, yaw, pitch)` — step the on-foot player out of the ship onto open terrain.
- `GameMenu.SetMenuOpen(bool)` — open/close the Tab menu exactly as the Tab key does.

The stills (#2765) add a few more, all inert in normal play:

- `GameBootstrap.CaptureCleanView` — hides the first-person hand (`Viewmodel`) and the block selection frame (`MiningFx`).
- `PlayerCapture.PlaceForCapturePose("vista", …)` with `CaptureSunDirection` / `CaptureVistaFavorsBuilt`, and the
  voxel sight helpers `IsCaptureCellOpen` / `CaptureSightDistance` the subject camera uses. `PlayerCapture` (#2911) is
  the player's capture component: the controller adds it on first use of `PlayerController.Capture`, so normal play
  never creates it, and it moves the body only through the controller's hooks (`SetCapturePose`, `SetLookAngles`).
- `PlayerCapture.SetCaptureThirdPerson(bool)` and `CaptureEyePosition` — the astronaut in frame, the rig camera's origin.
- `CreatureView.TryBounds` / `TryFacing` — where a creature is drawn and which way it faces.
- `ClipFrameWriter.FieldOfView` — the still's own lens.

**Determinism:** a fixed world (`MarketingShots`) + a fixed seed (`-seed`, default `424242`) make
every run reproducible, so the set can be regenerated after any game change.

**Capture worlds are Sandbox/Creative worlds** (`sandbox: true` in the director): planet enemies,
bandit turrets and the temperature hazard all gate on Survival, so nothing can shoot the player or
burn "Taking damage!" into a frame — while the HUD (vitals, minimap, hotbar) looks the same as in
Survival. Direct lava CONTACT damage is unconditional even in Sandbox, which is one reason the lava
seed is hand-picked (see below).

For the optional per-planet HUD shots (`-Planets`): lava framed badly on the default seed (the
placement ring found footing amid a lava ocean → contact damage in frame), so it was shot with
**`-Seed 2468`** (visible lava river, dry basalt footing) — the world still `world_lava` lists that
seed first for the same reason.
Note that the per-planet worlds are persisted saves (`singleplayer-saves/MarketingShots_<type>`) — a
re-run reuses the existing world and ignores `-seed`; delete the save to regenerate from a new seed.
Beware when only ONE language of a per-planet shot needs a re-shoot: a reused save resumes the player
on foot where the last run left them (#401), and a wandering/sleeping creature can end up blocking the
new placement — delete the save so the world regenerates fresh (same seed ⇒ same world).
The MAIN-sequence world (`MarketingShots`) is deleted automatically by the wrapper before each run:
since #401 a quit saves the live player position, so a reused save would resume the player on foot
outside the ship, breaking the cockpit shots and the take-off. Before every shot the director
dismisses VEGA's intro lines (`VegaPanel.DismissSpeechForCapture`) and clears the single-line HUD
toast (`GameBootstrap.ShowMessage("")` + a 0.25 s settle — HudUi only copies the message to the label
on its 10 Hz refresh), so lingering notices ("Data fragment recovered!", the mode line, space-return
messages) never land in a frame.

### Tuning

The wait timings and the flight heading are constants at the top of `ScreenshotDirector.cs`
(`MenuSettle`, `ChunkSettle`, `PoseSettle`, `FlightHeading`, `DefaultSeed`). Adjust them if a shot
is mis-timed or you want a different framing, then rebuild and re-run.

## Video clips (`-captureClip`) — and look checks without a playtest

The moving-picture sibling is `ClipDirector` (`-captureClip`): one run records ONE clip from a JSON
manifest (`-clipManifest <path> -clipName <name>`, default manifest `marketing/clips/clips.json`) as a
PNG frame sequence plus a frame-synced WAV; `scripts/capture-clips.ps1` loops the player over every
clip and muxes MP4s with FFmpeg. Each `ClipSpec` (see `ClipManifest.cs`) picks the scene
(`space` / `surface` / `cockpit` / `land` / `intro`), HUD on/off, length, fps, a camera move
(`static` / `yaw_sweep` / `orbit` / `dolly` / `pan`) and diegetic motion (ship throttle, walking, look).

Since #2405 a clip can also pin the **environment** and the **pose**, which is how a shader or lighting
change is verified without playing — same seed before and after, Low and High:

| Field | Values | Meaning |
|-------|--------|---------|
| `timeOfDay` | `0..1` (`0` midnight, `0.27` dawn, `0.5` noon, `0.75` dusk); negative = off | Local time of day pinned for the clip (longitude-compensated, like the screenshot pin) |
| `weather` | a state (`clear`, `clouds`, `rain`, `storm`, `fog`, `ground_fog`, `blizzard`, `gale`, …) or a precipitation form (`snow`, `sleet`, `hail`, `sandstorm`, `dust`, `ash`, …); `""` = off | Weather pinned for the clip; the wire fields (state, family, precipitation, intensity, wind) are filled as the server would |
| `pose` | `spawn` (default), `cave`, `underwater`, `forest`, `shore`, `ridge`, `lava` (dry footing a few blocks from lava, facing it), `vista` (the best view around, #2765 — see [World stills](#world-stills--capture-stillsps1)) | Where the on-foot player stands (surface scenes). Found by scanning the streamed chunks near the spawn (`PlayerCapture.PlaceForCapturePose`); when no such spot is loaded the clip logs it and uses the spawn pose |

Every time-smoothed look (weather easing, eye adaptation, wet/snowy ground, …) is snapped to its target
right before the first frame (`GameBootstrap.RequestCaptureSnap`), so two captures of one seed are
identical. A short frames-only check needs no FFmpeg: run the built player directly, e.g.

```powershell
Start-Process client\Build\Windows\BlocksBeyondTheStars.exe -Wait -ArgumentList `
  -captureClip,-clipManifest,scripts\clip-manifests\atmosphere-check.json,-clipName,jungle_dawn, `
  -clipOut,$env:TEMP\clips,-lang,en,-seed,424242,-screen-width,1920,-screen-height,1080,-screen-fullscreen,0
```

`-preset Potato|Low|Medium|High` captures under that quality preset and `-atmosphere Off|Some|All|Custom`
under that *Atmosphere effects* mode (#2404) — "Off" against "All" on one build is the before/after pair
for every effect of the atmosphere package; both are restored before the player quits, so a capture run
never rewrites the saved settings. Each clip runs in a throwaway singleplayer world `ClipShots_<clip>` (the
seed, peaceful, creative, and — unless the clip plays a `cutscene` — no main story and no tutorial: since the story
packs the server starts the prologue cutscene a few seconds into a fresh world, and its camera took over every
`static` clip, #2765); the director deletes it after the run and sweeps any leftover from an earlier or
killed run at the start (#2417), so the singleplayer world list stays clean.

→ `%TEMP%\clips\jungle_dawn\frames\frame_0000N.png`. The tracked manifest
[`scripts/clip-manifests/atmosphere-check.json`](../../scripts/clip-manifests/atmosphere-check.json)
is the standing set of look-check scenes (dawn, rain, fog, cave, under water, forest, night, orbit).

When a capture looks unchanged, read the `Player.log` of that run first: the pose search logs
`[Capture] PlaceForCapturePose: '<pose>' at (x,y,z)` (or the fallback to the spawn pose), and while the
environment is pinned `Sky` prints an `[Atmosphere] fog … mist … cloud … aboard=… sky=…` line every two
seconds with the globals the shaders received. A surface clip steps the player out of the spawn hull and
waits for the server's *aboard* flag to drop — while it is set, the interior fill, the sky exposure and the
haze are off by design, and VEGA's prologue is dismissed for the run (its staged cinematic would otherwise
film the orbit around the ship instead of the pose).
