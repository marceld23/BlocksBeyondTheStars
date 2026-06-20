# Marketing screenshots

Automatically generated in-game screenshots for the website / marketing material, captured
straight from the real game at **1920×1080** (PNG), once per language.

```
docs/screenshots/
├── de/   ← German HUD
└── en/   ← English HUD
```

## The shots

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
    <td width="50%"><img src="en/planet_surface.png" width="100%" alt="Planet surface"><br><sub><b>planet_surface.png</b> — Player view on a planet surface, the landed ship behind</sub></td>
    <td width="50%"></td>
  </tr>
</table>

## How to regenerate

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
```

**Requirements:** the Unity editor (6000.4.x) for the build, and a machine with a GPU — the
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

**Determinism:** a fixed world (`MarketingShots`) + a fixed seed (`-seed`, default `424242`) make
every run reproducible, so the set can be regenerated after any game change.

### Tuning

The wait timings and the flight heading are constants at the top of `ScreenshotDirector.cs`
(`MenuSettle`, `ChunkSettle`, `PoseSettle`, `FlightHeading`, `DefaultSeed`). Adjust them if a shot
is mis-timed or you want a different framing, then rebuild and re-run.
