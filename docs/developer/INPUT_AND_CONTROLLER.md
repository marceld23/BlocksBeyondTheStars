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

## Retuning the gamepad (needs real hardware — issue #195)

The mapping targets **Xbox / XInput on Windows**. Two places hold every tunable:

1. **`GamepadInputSource`** — the button table (`BtnA`…`BtnRb`, `ButtonFor`), stick deadzone, and the look
   rates (`LookYawSpeed` / `LookPitchSpeed`, applied as `rate × Time.deltaTime` so the value lands in the
   same space as a mouse delta and the caller's sensitivity slider still scales it).
2. **`client/ProjectSettings/InputManager.asset`** — the joystick **axes**. The left stick already feeds the
   shared `Horizontal`/`Vertical` axes (so movement is free); this project adds `RightStickX` (axis 3),
   `RightStickY` (axis 4, inverted so up = look up) and `DPadX` (axis 5). Other pad families report
   different axis numbers — change them here.

CI never compiles or runs the Unity client (`.github/workflows/ci.yml` is .NET-only), and it cannot drive a
controller, so the pad path is validated by a **local Unity build + manual on-device test**. The
Unity-free parts (inert-without-pad guarantee, mapping tables, key resolution, glyphs) have EditMode tests
in `client/Assets/Tests/EditMode/InputAbstractionEditModeTests.cs`.

## Menu navigation

`UiNavFocus` (`Scripts/UiNav.cs`) makes a menu pad-navigable: uGUI's `StandaloneInputModule` already turns
the stick/d-pad into directional nav and A/B into Submit/Cancel, so the only gap is that a mouse-built menu
has nothing selected — this component selects (and re-selects, self-healing) the first interactable control
while a pad is the active device, and is completely inert on keyboard/mouse. Wire it with `UiNav.Enable(root)`;
it is currently on the main menu and the in-game (Tab/Start) menu. Broader panel coverage is follow-up.

## Touch controls (tablet-web)

`TouchInputSource` (`Scripts/Input/`) + `TouchControlsUi` (`Scripts/TouchControlsUi.cs`) implement the touch
layer, added to `InputMap`'s combine exactly like the pad — no gameplay changes.

- **`TouchControlsUi`** builds the on-screen UI (left virtual joystick, full-screen right look pad, and
  buttons: JUMP / MINE-hold / PLACE / USE / DOWN / hotbar ◄► / menu) on its own overlay canvas, using uGUI
  pointer handlers (`TouchStick`, `TouchLookPad`, `TouchButton`) so the canvas scaler and multitouch routing
  are the EventSystem's job. It is created in `WorldRig` on `root`.
- **Inert on desktop / behaviour-preserving:** the UI is only built when the device has touch
  (`TouchControlsUi.ShouldShow()` = `Application.isMobilePlatform || Input.touchSupported`), and
  `TouchInputSource` reads zero whenever the controls aren't `Visible`. A desktop mouse rig (and a desktop
  browser, which reports no touch) never builds it.
- **Scope:** on-foot only — the controls hide during flight/EVA and while a menu is open (menus are
  tap-navigable through the EventSystem directly). `TouchButton.DownThisFrame` is a frame-idempotent edge
  (like `GetKeyDown`) computed in an early `Update` (order −100), because a single action is polled at more
  than one call site per frame — a consume-on-read latch would let the first site eat the edge.
- **Device glyphs:** `InputDeviceKind.Touch` is tracked; the HUD text hint blanks on touch (the on-screen
  buttons are self-labelling).

Deferred (need on-device testing — issue #197): flight/speeder/EVA touch layouts, contextual button labels
(board/loot/dock), on-screen **text entry** (name/chat need a soft-keyboard — uGUI's custom text capture
doesn't open one), and menu touch-sizing.

## Web / performance (P5)

- **Lite graphics default:** on a fresh install on a tablet or the WebGL build, `ClientSettings.Load` starts
  the quality `Preset` at `Low` (the scene is heavy: custom URP, SSAO, SMAA, PBR). Only on a genuine first
  run — a returning player keeps their choice.
- **Browser gamepad:** the same `GamepadInputSource` runs under WebGL, but the browser Gamepad API's axis /
  button numbering can differ from native XInput; verifying + remapping it is a playtest item (issue #198),
  not shipped as an untested guess.
