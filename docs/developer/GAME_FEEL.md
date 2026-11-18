# Game feel — the feel pass

Status: **implemented** (2026-11-17, epic #3738, parts #3739–#3748). The sounds went through listening checks; a play
session is open for everything else — every number here is a first value.

The feel pass adds no content and no mechanics. It changes how the game *answers*: how the body starts and
stops, how the ship turns, what a hit sounds like, how a window leaves, whether a figure looks alive. Every
rule that has no Unity in it lives in `src/BlocksBeyondTheStars.Client.Core/Feel/` (and `Audio/`, `Music/`)
with headless tests in `tests/BlocksBeyondTheStars.Client.Tests/Feel/`; the Unity side is thin and sits in
`*.Feel.cs` partial files next to the class it extends.

Two rules hold everywhere:

- **Comfort switches win.** Every new motion reads the existing switches — "Camera motion", "Screen shake",
  "Reduced effects" (`FxKit.CameraMotion`, `UiKit.ReducedMotion`). With them off the picture is as steady as before.
- **The browser pays nothing extra.** No new file under `Assets/Resources` that is not small, no filter
  component (Unity Web ignores them), no new shader.

## 1. Moving on foot

| Rule | Where | What it does |
|---|---|---|
| Walk curve | `Feel/WalkInertia.cs`, `PlayerController.Feel.cs` (`ReadWalkInput`) | The walk reads the RAW axes (`InputMap.MoveXRaw/MoveYRaw`) and eases them itself: 0.08 s to full input and 0.10 s to a stop on the ground, 0.16 s / 0.30 s in the air and in water. It replaces the input manager's generic smoothing (a third of a second both ways), so the body stops on the block the player meant. Speeds are unchanged. |
| Coyote time | `Feel/JumpAssist.cs`, `TryCoyoteJump` | For 0.12 s after the feet walked off an edge a press of Jump still jumps. Never after a jump. |
| Jump buffer | `Feel/JumpAssist.cs` | A TAP of Jump up to 0.12 s before the landing jumps on the landing. A held key always did. |
| Step smoothing | `Feel/StepSmoothing.cs`, `AbsorbStep` | The auto-step lifts the body within one frame; the eye stays behind by the step's height and catches up (rate 14/s). Only a real step counts (a rise of more than 0.1 m at more than 6 m/s), so a lift or a train is followed rigidly. |
| Third-person follow | `ThirdPersonLag` | The whole camera boom follows the body's HEIGHT softly (rate 12/s, at most 0.6 m off). Horizontal stays rigid — the boom's wall check starts from the body. |
| Held item | `Viewmodel.Feel.cs` | The item trails the view a little while looking around and dips on a landing. |
| Hit confirmation | `HitPause.cs`, `HudUi.ShowHitMarker` | A confirmed hit ticks (`hit_confirm`, `hit_confirm_defeat`) and holds the swing of the player's own hand for 0.05 s (0.08 s on a defeat). Deliberately NOT `Time.timeScale`: the world, the other players and the server's clock run on. |

`JumpAssist`, `WalkInertia` and `StepSmoothing` are reset by `PlayerController.SnapTo` (a teleport, a respawn).
The jump impulse now starts in one place, `BeginJump`.

## 2. The ship at the helm

`Feel/FlightFeel.cs` + `SpaceView.Feel.cs`. "Arcade", not Newton:

- **Velocity** follows the input: toward the wanted velocity at `1 / SpoolSeconds` (0.4 s to top speed), toward a
  stop at `1 / CoastSeconds` (0.8 s). The keep-out spheres, the station hulls and the rocks resolve the position
  exactly as before; `SettleFlightVelocity` then takes what they blocked out of the velocity, so the ship slides
  along a wall instead of pressing into it.
- **Bank**: the hull leans into a turn (up to 25°, plus up to 8° while strafing). It is shown only — the heading the
  ship flies on is the unrolled one, and `CameraHeading()` reads the heading back without the lean.
- **Camera**: the chase camera follows its rigid place softly (rate 9/s, at most 4 units off); the cockpit view
  takes 30 % of the lean. Both are rigid with "Camera motion" off.
- A frame without the helm (a menu, a transition) starts from rest; the autopilot parks the ship on arrival
  (`StopFlightDrift`).

## 3. Sound

| Part | Where |
|---|---|
| Takes | `Audio/ClipTakes.cs`, `ClientAudio.Takes.cs`. A rotating family plays `name`, `name_2`, `name_3` … in turn, never the same twice in a row. Opt-in (`ClipTakes.Families`), because many other clips carry a number that means something (a creature's call, a footstep take). A new take is only a file. |
| Placing by material | `Audio/BlockSoundRules.cs`. Stone, metal, crystal, earth, wood, glass — from the block key, via the footsteps' own heuristic. `place_block` stays the fallback. |
| Interface set | `UiSound.cs`. Bundled recordings (`Resources/audio/ui_*.mp3`) first, the code-made stand-ins as fallback. `UiPanel` plays open and close. |
| Musical accents | `ClientAudio.Stings.cs`. `sting_new_planet`, `sting_site_found`, `sting_blueprint`, `sting_achievement`, `sting_home`: never two at once, 8 s of rest between any two; otherwise the event's plain cue plays. `sting_new_planet` plays **once per session** — on the first landing on a second body (`Client.Core/SessionArrivals`, #3795); the sunrise track still opens the first visit of every body. |
| Ducking | `MusicDuck.cs`, `ClientMusic.Feel.cs`. The music steps back to 55 % while someone speaks (VEGA, a cutscene's speaker, an NPC conversation) and to 40 % under an accent. |
| Tension music | `Music/TensionMusic.cs`, context `MusicLibrary.Tension`. A hostile close by on foot for 2 s starts the pool; it plays at least 20 s and ends 8 s after the last hostile left. |
| Sliders | `ClientSettings.AmbienceVolume`, `UiVolume` — both sit under the effects volume. |

New clips go through `tools/audio/level_clips.py --import <dir> --only <ids>`. The class `ui` (−18 LUFS, trimmed)
holds the interface set and the hit tick; `sting_*` joined the `sting` class. The prompts of the feel pass's
clips are in `tools/audio/aaa_feel_batch.py`. No AudioMixer by design: the browser ignores audio filters, so
reverb and muffling are baked into the clips (`SampleKit`), and a mixer would be a second path that drifts.

## 4. Interface

| Part | Where |
|---|---|
| Tooltip | `UiTooltip.cs`. One shared view; `UiTooltip.Attach(go, title, body)`. Mouse rest 0.35 s, the pad's focus, a finger's long press 0.5 s. Never takes a click. |
| Drag and drop | `UiDragSlot.cs`. Mouse only; the drop calls the same move the click-to-pick sends. A finger's drag is handed on to the scroll view. |
| Button click | `UiClickFx.cs`. Every `UiKit.AddButton` button (and so every tier / fit button) gives a little while the pointer holds it (`UiPressFx`: 0.97 about its centre) and its outline lights up and fades when the click fires (`UiClickFx.Flash`, 0.22 s). The flash is drawn on a canvas of its own (`UiClickFx`, order 890) at the button's screen rect, because many clicks rebuild or close their window at once and a flash on the button would die with it. The pad's and a finger's click flash too. Off under "Reduced effects" and outside play mode. Not covered: text links, switches, the wheels' wedges and the ~20 hand-built buttons (editors, cutscenes). The main menu's golden (`MainMenuHierarchy.golden.txt`) lists `UiPressFx` on each button. |
| Closing | `UiClosing.cs`. `UiPanel.HideCanvas` / `DestroyCanvas` fade the picture out over 0.12 s; the panel itself is closed at once. Instant under "Reduced effects" and outside play mode. |
| Wheel | `UiWheel.cs`. A radial menu of any number of wedges; one sprite per count. Used by the emote wheel. The hotbar's four-wedge ring (`HotbarActionUi`) keeps its own code. |
| Gear comparison | `Feel/GearCompare.cs`, `CraftingTechShipUI.Feel.cs`. The numbers of a wearable piece next to the piece worn in its slot, with ▲ / ▼. |
| Pinned recipe | `HudUi.PinnedRecipe.cs`. A context card in the right column: have / need per ingredient. The pin is `ClientSettings.PinnedRecipe`. |
| HUD off | `InputAction.ToggleHud` (F10), `GameBootstrap.Feel.cs`. Rides on `GameBootstrap.HudHidden`; a note names the key. |
| Settings | `UiSettings.Feel.cs`, `ClientSettings.Feel.cs`. Resolution, monitor, pause on focus loss, 3D resolution (render scale + FSR 1), story text size and backdrop, colour vision (`UiPalette`), run / crouch as a toggle, pad glyphs by the pad's name (`PadGlyphTable.Detect`). |

## 5. Figures

`PlayerAvatar.Feel.cs`, `Feel/EmoteCurves.cs`. The blocky rig stays; it gets more to say:

- **Emotes** (`PlayerAvatar.PlayEmote`): wave, nod, cheer, point, shake the head, dance, clap, sit, bow. Timed
  emotes ride an envelope (in from rest, hold, back to rest). "Sit" lasts until the figure moves.
- **Body states**: a flinch when hurt, sinking down when defeated (until the figure is somewhere else, or 12 s), a
  swimming stroke in water, a give in the knees on landing.
- **Eased legs**: the leg joints follow their pose (rate 22/s) instead of snapping from walk to tuck to stand.
- **Face**: the stock face blinks (3–6.5 s, different for every figure) and its mouth moves while its owner speaks
  (`Talk`). A drawn pixel face hides the stock features.
- **Contact shadow** (`BlobShadow.cs`): a soft quad under players, NPCs, bandits and walking animals where the sun's
  shadow map does not reach — the Potato preset, under a roof, underground, aboard, at night. It hides with the
  figure (the own body in first person, a cloaked player).

### The emote wheel and the wire

`EmoteWheelUi.cs`. The keyboard HOLDS the key (default P): the wheel shows, the pointer picks a wedge, letting go
plays it. A TAP of the key (let go within 0.3 s with no wedge under the pointer) leaves the wheel open for a click, with
a close button in the middle — so the key need not sit under the left hand. The pad and the tablet open it from the
action list (`ContextActionsUi`). The default is P and not Y: keys are read by POSITION (the Input Manager's "Use
Physical Keys"), and Y and Z swap places on a German keyboard — the wheel sat on the key labelled Z there.

Two messages, appended (ids 349 and 350, no protocol bump — an older peer drops the unknown tag):

- `PlayerEmoteIntent { Emote }` — client → server. The server (`GameServerEmotes.cs`) takes only a player's own
  emotes (`PlayerEmotes.IsPlayable`) and at most one per 0.8 s.
- `PlayerEmote { PlayerId, Emote }` — server → the players within 48 blocks on the same side of a ship's hull.
  The server adds the two states only it knows: `Hurt` (paced to one per 0.6 s) and `Down`, both from the one gate
  every hit passes (`DamagePlayer`).

Each emote may have a small sound (`EmoteSounds`, clips `emote_<name>`; class `foley`): the own one plays flat at the
listener, another player's where the figure stands. A missing clip is no error — the emote is then silent. Cheer and
bow have two takes each that rotate.

A villager who sees a wave waves back with a hello of its own (`WaveBack`, the passing greeting's rules: awake, not
busy, in sight, not hostile; at most every 12 s).

**Startled, never afraid (#3769).** A weapon fired or swung next to NPCs makes the nearest ones call out with both
hands up and the mouth wide open (`GameServerNpcStartle.cs`, `PlayerAvatar.Startle`, the curve
`AvatarGestures.HandsUp`). The lines are indignant or funny; there is no cry for help and nobody flees — the game has
no fear-driven mechanics. Speech bubbles sit on a framed plate (`NpcBubbleFrame`, `ScreenLabelLayer`), and so does the
status toast (`HudUi.ToastPlate.cs`), which used to run through the name tags under it.

## 6. Adding to it

- **An emote**: the next id in `PlayerEmotes` (and raise `LastPlayable` to it), a length in `EmoteCurves.Seconds`, a
  pose in `PlayerAvatar.Feel.cs`, a wedge in `EmoteWheelUi.Emotes`, a text `ui.emote.<name>` in all 14 languages, and —
  optional — a clip `emote_<name>.mp3`.
- **A take**: drop `name_4.mp3` next to the others and run `level_clips.py --import … --only name_4`. A new rotating
  family needs its name in `ClipTakes.Families` (the test `EveryRotatingFamily_ShipsAtLeastTwoTakes` guards it).
- **A tooltip**: `UiTooltip.Attach(element, () => title, () => body)`.

## 7. Not done

- Soft edge at the view distance, more view distance on Medium, cheaper tiers of the "Details (High)" switches:
  they need measurements on the reference machine first.
- Pad glyphs as pictures (they are still text), numbers that count up, an autosave indicator. Tooltips are on the
  inventory's slots only.
- Acting in cutscenes and TextMeshPro in the menus: each its own design.
- The enemy's own freeze on a hit: only the player's hand holds.
