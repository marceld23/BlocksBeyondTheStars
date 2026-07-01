# Input abstraction & controller support

Gameplay input flows through **`InputMap`** (a static facade), not through direct `UnityEngine.Input`
polling. This is the seam that lets a new input device be added without touching gameplay code — it is
how controller support was built and how touch (tablet-web) will be.

## Layers

- **`IInputSource`** (`client/Assets/BlocksBeyondTheStars/Scripts/Input/IInputSource.cs`) — one hardware
  backend expressed as the game's control verbs: `MoveX/MoveY`, `LookX/LookY`, `HotbarScroll`,
  `Jump/Crouch/Primary/Secondary`, `HotbarSlotDown`, and the discrete `ActionDown/Held/Up(InputAction)`.
- **`DesktopInputSource`** — a 1:1 wrapper over the exact legacy calls the code used before (`GetAxis`,
  `GetButton`, mouse buttons). Routing a call site onto it is behaviour-preserving.
- **`GamepadInputSource`** — reads the joystick axes + `KeyCode.JoystickButton*` buttons.
- **`InputMap`** — **combines** both sources (it does not switch between them): axes are summed/clamped and
  the button verbs OR together, so keyboard+mouse and a pad are always live at once and neither can lock
  the other out. With no pad connected the gamepad source returns zero/false, so the combined result equals
  the pure keyboard/mouse behaviour. `InputMap.ActiveDevice` tracks the most recently used family purely to
  pick HUD glyphs (`InputMap.Glyph(action)`).

## Migrating a call site

Replace the raw call with the `InputMap` verb, e.g. `Input.GetAxis("Horizontal")` → `InputMap.MoveX()`,
`Input.GetMouseButtonDown(0)` → `InputMap.PrimaryDown()`, `Input.GetButton("Jump")` → `InputMap.JumpHeld()`.
The migrated gameplay surface is `PlayerController` (on-foot + speeder) and `SpaceView` (flight + EVA + turret).
A handful of number-key / Escape picks are intentionally still keyboard-only (secondary, not locomotion).

## Pad rebinding & glyphs

- **Rebinding:** every control row in the settings screen has two buttons — the keyboard key and the pad
  button. The pad column captures `KeyCode.JoystickButton0..19` and persists to
  `ClientSettings.PadBindings` (mirroring `KeyBindings`); `GamepadInputSource.ButtonFor` resolves
  override-then-default (`DefaultButtonFor`). Reset clears both lists. An action with no stock pad button
  (shown "—") can still be bound.
- **Glyphs:** `InputMap.Glyph(action)` returns the pad label while a pad is the active device, else the
  bound key name; `InputMap.PadGlyph(keyCode)` names all 20 pad buttons. Wired into: the HUD control hint,
  the speeder exit/refuel hint, the flight + EVA controls hints (`ui.space.controls_pad` /
  `ui.space.eva_controls_pad`), and the board/land prompts (`ui.space.*_fmt` keys take the glyph as `{0}`).
- **Default conflict rule:** `FlightEnterInterior` deliberately has NO default pad button —
  `ToggleThirdPerson` (Y) is also polled during flight, so sharing Y would fire both on one press.

## Retuning the gamepad (needs real hardware — issue #201)

The mapping targets **Xbox / XInput on Windows**. Two places hold every tunable:

1. **`GamepadInputSource`** — the button table (`BtnA`…`BtnRb`, `DefaultButtonFor`), stick deadzone, and the
   look rates (`LookYawSpeed` / `LookPitchSpeed`, applied as `rate × Time.deltaTime` so the value lands in
   the same space as a mouse delta and the caller's sensitivity slider still scales it).
2. **`client/ProjectSettings/InputManager.asset`** — the joystick **axes**. The left stick already feeds the
   shared `Horizontal`/`Vertical` axes (so movement is free); this project adds `RightStickX` (axis 3),
   `RightStickY` (axis 4, inverted so up = look up) and `DPadX` (axis 5). Other pad families report
   different axis numbers — change them here (players can fix the *buttons* themselves via rebinding).

CI never compiles or runs the Unity client (`.github/workflows/ci.yml` is .NET-only), and it cannot drive a
controller, so the pad path is validated by a **local Unity build + manual on-device test**. The
Unity-free parts (inert-without-pad guarantee, mapping tables, key resolution, glyphs) have EditMode tests
in `client/Assets/Tests/EditMode/InputAbstractionEditModeTests.cs`.

## Menu navigation

`UiNavFocus` (`Scripts/UiNav.cs`) makes a menu pad-navigable: uGUI's `StandaloneInputModule` already turns
the stick/d-pad into directional nav and A/B into Submit/Cancel, so the only gap is that a mouse-built menu
has nothing selected — this component selects (and re-selects, self-healing) the first interactable control
while a pad is the active device, and is completely inert on keyboard/mouse. Wire it with `UiNav.Enable(root)`.
Wired on: the main menu, the in-game (Tab/Start) menu, settings, the world picker, vendor trade, the Codex
(Wiki), credits, the feedback dialog and the respawn prompt. Add the same one-liner after
`UiKit.CreateCanvas` for any new interactive screen.

## Touch controls (tablet-web)

`TouchInputSource` (`Scripts/Input/`) + `TouchControlsUi` (`Scripts/TouchControlsUi.cs`) implement the touch
layer, added to `InputMap`'s combine exactly like the pad — no gameplay changes.

- **`TouchControlsUi`** builds the on-screen UI on its own overlay canvas, using uGUI pointer handlers
  (`TouchStick`, `TouchLookPad`, `TouchButton`) so the canvas scaler and multitouch routing are the
  EventSystem's job. It is created in `WorldRig` on `root` and builds **lazily on the first Update** (so the
  localizer is up — button labels are DE/EN via `ui.touch.*`). Shared controls: left virtual joystick,
  full-screen right look pad, hotbar ◄►, menu (≡). Three per-context **clusters** swap with the control
  state: **on foot** (JUMP / MINE-hold / PLACE / USE / DOWN / CHAT), **flight + EVA** (`Game.SpaceViewActive`:
  FIRE-hold / LAND / SHIP / AUTO / VIEW / USE / UP / DOWN) and **speeder** (`Game.DrivenSpeeder != null`:
  BOOST-hold / JUMP / EXIT / FUEL). Discrete buttons map to `InputAction`s through a lookup the
  `TouchInputSource.ActionDown/ActionHeld` methods read, so rebind-consuming call sites work unchanged.
- **Inert on desktop / behaviour-preserving:** the UI is only built when the device has touch
  (`TouchControlsUi.ShouldShow()` = `Application.isMobilePlatform || Input.touchSupported`), and
  `TouchInputSource` reads zero whenever the controls aren't `Visible`. A desktop mouse rig (and a desktop
  browser, which reports no touch) never builds it.
- **Edges:** `TouchButton.DownThisFrame` is a frame-idempotent edge (like `GetKeyDown`) computed in an early
  `Update` (order −100), because a single action is polled at more than one call site per frame — a
  consume-on-read latch would let the first site eat the edge. All widget state clears on disable, so a
  button can't stay "held" across a cluster switch. The whole layer hides while a menu is open.
- **Device glyphs:** `InputDeviceKind.Touch` is tracked; the HUD/flight text hints blank on touch (the
  on-screen buttons are self-labelling).
- **Tap-vs-drag:** the shared EventSystem scales its `pixelDragThreshold` to ≈1 mm from `Screen.dpi` on
  touch devices (the 10 px mouse default misreads finger taps as drags in menus).

## Text entry on touch

Native tablets (Android/iOS) need nothing — uGUI's `InputField` opens the OS soft keyboard by itself. The
gap is **WebGL on a touch device**: `TouchScreenKeyboard` is unsupported in the browser, so a tapped field
would be dead. `TouchTextEntry` (`Scripts/TouchTextEntry.cs`) + `client/Assets/Plugins/BbsTextPrompt.jslib`
fall back to the browser's `window.prompt()` (which opens the OS keyboard on every mobile browser):
`UiKit.AddInput` attaches the bridge to every themed input field (main-menu name/host/port/password,
settings, feedback), and `ChatUi.OpenInput` — also reachable via the touch CHAT button — prompts directly
and submits. On every other platform `TouchTextEntry.NeedsPrompt` is false and nothing is even attached.

## Web / performance (P5)

- **Lite graphics default:** on a fresh install on a tablet or the WebGL build, `ClientSettings.Load` starts
  the quality `Preset` at `Low` (the scene is heavy: custom URP, SSAO, SMAA, PBR). Only on a genuine first
  run — a returning player keeps their choice.
- **Browser gamepad:** the same `GamepadInputSource` runs under WebGL, but the browser Gamepad API's axis /
  button numbering can differ from native XInput; verifying it is a playtest item (issue #203). Players can
  already fix wrong *buttons* themselves via the pad rebinding UI; wrong *axes* need an `InputManager.asset`
  change (see "Retuning" above).
