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

## Adding touch later (tablet-web)

Implement a `TouchInputSource : IInputSource` (virtual joystick → `MoveX/MoveY`, drag zone → `LookX/LookY`,
on-screen buttons → the verb methods) and add it to `InputMap`'s combine. No gameplay changes required —
that is the point of this seam.
