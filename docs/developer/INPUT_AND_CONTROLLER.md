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
The migrated gameplay surface is `PlayerController` (on-foot + speeder), `SpaceView` (flight + EVA + turret),
`PlayerInteractions` (trade / dock / undock), `WorldMap` (`PlanetMap`), `VegaPanel` (`VegaContinue`) and
`FinaleView` (the breach hold is `PrimaryFire` held). Number-key picks keep a device-neutral fallback: the
on-foot / EVA hotbar and the ship-systems quick-bar all also step on `InputMap.HotbarScroll()` (wheel, d-pad
◄►, touch ◄►). Escape stays keyboard; every panel that a pad can reach also closes on **B**
(`KeyCode.JoystickButton1`).

### Context-actions list + injected edges (#1042/#1043)

Twenty-odd on-foot / flight verbs have a letter each on the keyboard, but the stock pad has two free
buttons and a tablet a handful of thumb targets. `ContextActionsUi` (`Scripts/ContextActionsUi.cs`) is the
device-neutral answer: one control — `InputAction.ContextActions` (pad **LS** click, touch **ACT**, no key by
default) — opens a list of every verb whose *applicability probe* is true right now (rotate with a rotatable
block held, trade / dock with a player in reach, undock when docked, loot / stash with a container in reach,
lamp, thermal with binoculars raised, EVA deploy, speeder exit / refuel, VEGA continue, …). Picking an entry
calls **`InputMap.InjectNextFrame(action)`**, which makes `InputMap.Down(action)` true exactly once on the
following frame — after the list has closed and gameplay polls again — so the existing poll sites fire
unchanged; the list is a second *front-end*, not a second rule set. Probes live next to the handlers they
mirror (`PlayerController.HeldRotatable / NearContainer / NearCrate / NearWreck / NearOwnParkedSpeeder /
HoldsWeapon / BinocularsRaised`, `PlayerInteractions.CanRequestTradeOrDock / CanDisembark`,
`VegaPanel.LineShowing`); add a probe + a `Table` row when a new verb lands, or it stays keyboard-only.
`InputMap.LabelKey(action)` (`ui.key.*`) names entries and the settings rows from one table.

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
- **Stock pad layout (Xbox):** A jump · B crouch / close · X Interact · Y ToggleThirdPerson · LB place ·
  RB mine · d-pad ◄► hotbar / ship system · **d-pad ▲ = OpenChat** · **d-pad ▼ = RotateShape** ·
  **LS = ContextActions** (the list above) · **RS = HotbarAction** (slot pie) · **Back = VegaContinue** ·
  Start = menu. Every other action reaches the pad through the context-actions list or a rebind.
- **The two d-pad verbs are fixed (#1220).** An axis cannot be written as a `KeyCode`, so `OpenChat` and
  `RotateShape` fire from inside `GamepadInputSource.DpadActionDown` rather than through the binding table —
  both are still bindable to a *button* as well. Up went to chat because that is what makes text entry
  reachable at all; down went to `RotateShape` because it is the frequent verb with no stock pad button,
  while the context list keeps LS.
- **Triggers (opt-in).** `Settings → Controller → mine and place on the triggers` adds RT = mine and
  LT = place on top of RB/LB. Shipped **off**: the combined trigger axis is the one reading that genuinely
  differs between XInput, Proton and the browser Gamepad API, and a pad that idles at full deflection would
  otherwise mine on its own. `HadActivityThisFrame` ignores the axis while the option is off for the same
  reason.
- **Precision look.** While LB or RB is held, pad look runs at half speed — those buttons *are* place and
  mine, so "holding one" and "aiming carefully" are the same moment. Mouse look is untouched.

## Retuning the gamepad (needs real hardware — issue #201)

The mapping targets **Xbox / XInput on Windows**. Two places hold every tunable:

1. **`GamepadInputSource`** — the button table (`BtnA`…`BtnRb`, `DefaultButtonFor`), stick deadzone, and the
   look rates (`LookYawSpeed` / `LookPitchSpeed`, applied as `rate × Time.deltaTime` so the value lands in
   the same space as a mouse delta and the caller's sensitivity slider still scales it).
2. **`client/ProjectSettings/InputManager.asset`** — the joystick **axes**. The left stick already feeds the
   shared `Horizontal`/`Vertical` axes (so movement is free); this project adds `RightStickX` (axis 3),
   `RightStickY` (axis 4, inverted so up = look up), `DPadX` (axis 5), `DPadY` (axis 6, positive = up) and
   `Triggers` (axis 2 — the combined XInput trigger axis, LT positive / RT negative). Other pad families
   report different axis numbers — change them here (players can fix the *buttons* themselves via
   rebinding). If a real pad turns out to report d-pad up as negative, flip `invert` on `DPadY`; that is the
   whole fix, and the trigger option can simply stay off until someone confirms its number on that platform.

Since #1219 the dead zone and the pad look speeds are **player settings**, not constants — `Settings →
Controller` writes `PadDeadzone`, `PadLookX/Y`, `PadInvertY`, `PadGlyphs`, `PadVibration` (stored, no-op:
the legacy Input Manager has no rumble API) and `PadTriggersMinePlace`. Their defaults are exactly the
values the code used before they were settings, so an existing `client_settings.json` feels unchanged.

CI never compiles or runs the Unity client (`.github/workflows/ci.yml` is .NET-only), and it cannot drive a
controller, so the pad path is validated by a **local Unity build + manual on-device test**. The
Unity-free parts (inert-without-pad guarantee, mapping tables, key resolution, glyphs) have EditMode tests
in `client/Assets/Tests/EditMode/InputAbstractionEditModeTests.cs`.

## Menu navigation

`UiNavFocus` (`Scripts/UiNav.cs`) makes a menu pad-navigable: uGUI's `StandaloneInputModule` already turns
the stick/d-pad into directional nav and A/B into Submit/Cancel, so the only gap is that a mouse-built menu
has nothing selected — this component selects (and re-selects, self-healing) the first interactable control
while a pad is the active device, and is completely inert on keyboard/mouse. Wire it with `UiNav.Enable(root)`.

> **Put it on the CANVAS, not on the owning MonoBehaviour.** `UiKit.CreateCanvas` returns a **scene-root**
> GameObject that is never reparented, so a `UiNavFocus` sitting on the screen's owner sees none of its
> controls — it only walks its own subtree. That was #1198: `GameMenu` enabled UiNav on its own object while
> `CraftingTechShipUI` built the 11 tabs on a canvas of its own, so the entire in-game menu was unreachable by
> pad while the code claimed it "covers every tab".

Three rules the component enforces so it never does harm off-screen:

* **It respects `Canvas.enabled`.** Screens hide two ways — `gameObject.SetActive(false)` (which disables the
  component for you) and `canvas.enabled = false` (which does **not**: the GameObject stays active). Without
  the check a hidden screen keeps pulling the selection off whatever is really on screen.
* **It never auto-selects an `InputField`.** A focused field swallows the navigation axes, so a pad player
  dropped into one is stuck until the on-screen keyboard lands (#1211). Screens that called
  `ActivateInputField()` on open now skip that while a pad is the active device.
* **It restores the position of the last selection after a rebuild** instead of snapping back to control #1
  (the crafting pane rebuilds all three panels on every pick).

`UiNav.SetSuspended(root, true)` hands the sticks to a screen's 3D viewport and clears the selection — used by
the ship and face editors, where **Start** swaps between panel and viewport focus. `WantsFocus`, `FocusTarget()`
and `NoteSelection()` are the seams `UiNavEditModeTests` checks, since CI has no pad.

Wired on: the main menu, the in-game (Tab/Start) menu **and all 11 of its tabs**, settings, the world picker,
vendor trade, the Codex (Wiki), the Arcade, credits, the feedback dialog, the respawn prompt, the slot-action
pie, the context-actions list, the landing-pad chooser (land map), the trade / dock prompts + trade panel, the
bandit demand, the planet map, the flight system chart (#1043), the blueprint tool, the beacon-label and
beam-pad modals, and the ship / face editors (#1198). Add the same one-liner after `UiKit.CreateCanvas` for any
new interactive screen, and let **B** close it (see `WorldMap.Update` for the pattern). Still pointer-only: the
map *markers* (waypoint by click) and the finale duel's IMGUI choices.

### Menu verbs: `UiCancel` / `UiMenu`

Nine screens used to poll `KeyCode.JoystickButton1` / `JoystickButton7` raw, right next to their `Escape` /
`Tab` check — not rebindable, and a reader had to know that "1" means B. They now go through two actions (#1198):

| Action | Keyboard | Pad | Rebindable |
|---|---|---|---|
| `InputAction.UiCancel` | `Escape` | **B** | pad column only |
| `InputAction.UiMenu` | `Tab` | **Start** | pad column only |

`InputMap.KeyboardLocked(action)` makes the keyboard column fixed and `UiSettings.KeyRow` renders those rows
with a disabled key button. Escape and Tab must not be bindable away: a player who did could end up inside a
modal with no key that leaves it.

**`AppShell` is the exception.** Its shell-level cancel accepts pad B in the *menu* phases only — in
`ShellPhase.InGame` B is crouch (`GamepadInputSource.CrouchHeld`), and ducking must never pop the quit dialog.
The in-game screens are safe because each gates its cancel on being open, and an open screen freezes player
control.

### Named pad buttons (`PadButton`)

For screens that need more of the pad than the rebindable action set covers, `InputMap.PadDown/PadHeld` take a
`PadButton` (`A`, `B`, `X`, `Y`, `Lb`, `Rb`, `Back`, `Start`, `L3`, `R3`), and `PadStickX/Y`, `PadLookX/Y` and
`PadDpadStep` expose the sticks and the repeat-gated d-pad **without** the mouse merged in. The XInput button
numbers stay in `GamepadInputSource` alone. The two editors use them today; the minigame host (#1218) is next.

### Editors on a pad

Both standalone editors are pointer-native tools, so they get a **focus toggle** rather than a cursor
emulation: **Start** swaps between the side panels (UiNav) and the work surface, because one stick cannot walk
a list and steer a brush at the same time. **B** leaves the surface; from the panels it leaves the editor.

| | Ship editor (`ShipEditor`) | Pixel editor (`FaceEditor`) |
|---|---|---|
| Aim | centre reticle (`PickPoint()`) | cell cursor, stepped + repeat-gated |
| Left stick | fly | move the cursor |
| Right stick | look | — |
| A / X / Y | place / remove / turn brush | paint / erase / pick up colour |
| LB / RB | down / up | LB = fill's "whole colour" modifier |
| D-pad | step the palette | — |
| L3 | fly faster | — |

## Touch controls (tablet-web)

`TouchInputSource` (`Scripts/Input/`) + `TouchControlsUi` (`Scripts/TouchControlsUi.cs`) implement the touch
layer, added to `InputMap`'s combine exactly like the pad — no gameplay changes.

- **`TouchControlsUi`** builds the on-screen UI on its own overlay canvas, using uGUI pointer handlers
  (`TouchStick`, `TouchLookPad`, `TouchButton`) so the canvas scaler and multitouch routing are the
  EventSystem's job. It is created in `WorldRig` on `root` and builds **lazily on the first Update** (so the
  localizer is up — button labels are DE/EN via `ui.touch.*`). Shared controls: left virtual joystick,
  full-screen right look pad, hotbar ◄►, slot-actions "…" (#940), **ACT** (the context-actions list, #1042),
  **NEXT ▶** (VEGA continue, shown only while a line is up, #1041), menu (≡). Three per-context **clusters**
  swap with the control state: **on foot** (JUMP / MINE-hold / PLACE / USE / DOWN / CHAT / VIEW / MAP, plus
  contextual **ROTATE** while a rotatable block is held and **ATTACK** (tap = swing, hold = finale breach)
  while a weapon is held), **flight + EVA** (`Game.SpaceViewActive`: FIRE-hold / USE / VIEW / UP / DOWN,
  and at the helm LAND / SHIP / AUTO / MAP which swap for **PLACE / DEPLOY** in EVA — `Game.InEva`) and
  **speeder** (`Game.DrivenSpeeder != null`: BOOST-hold / JUMP / EXIT / FUEL). Discrete buttons map to
  `InputAction`s through a lookup the `TouchInputSource.ActionDown/ActionHeld` methods read, so
  rebind-consuming call sites work unchanged; everything without a button is one tap away behind ACT.
  Contextual buttons hide while their verb does not apply (probes on `PlayerController` / `FinaleView`), so
  a tablet never carries a dead target.
- **Inert on desktop / behaviour-preserving:** the UI is only built when touch is actually in use
  (`TouchControlsUi.ShouldShow()` = `Application.isMobilePlatform` OR a real touch has been seen —
  `Input.touchCount > 0` latches once), and `TouchInputSource` reads zero whenever the controls aren't
  `Visible`. `Input.touchSupported` is deliberately NOT consulted: it reports the OS/browser touch API
  (true on Windows ≥ 8 and in desktop Chrome without any touchscreen, #219). A desktop mouse rig never
  latches; touch laptops and iPad-Safari's desktop UA get the controls on the first tap.
- **Edges:** `TouchButton.DownThisFrame` is a frame-idempotent edge (like `GetKeyDown`) computed in an early
  `Update` (order −100), because a single action is polled at more than one call site per frame — a
  consume-on-read latch would let the first site eat the edge. All widget state clears on disable, so a
  button can't stay "held" across a cluster switch. The whole layer hides while a menu is open.
- **Device glyphs:** `InputDeviceKind.Touch` is tracked; the HUD/flight text hints blank on touch (the
  on-screen buttons are self-labelling).
- **Tap-vs-drag:** the shared EventSystem scales its `pixelDragThreshold` to ≈1 mm from `Screen.dpi` on
  touch devices (the 10 px mouse default misreads finger taps as drags in menus).

## Text entry on a gamepad (#1211)

A pad could reach every screen but not a single text field: uGUI activates an `InputField` the moment it is
selected, and a focused field swallows the navigation axes — so a pad player who landed on one could neither
type nor leave. `OnScreenKeyboardUi` (`Scripts/OnScreenKeyboardUi.cs`) is the answer: a uGUI button grid on
its own canvas (`sortingOrder` 5000, `UiNav`-enabled), with the layout and every text edit in
`Client.Core/OnScreenKeyboardLayout.cs` so they are unit-tested headlessly.

- `UiKit.AddInput` attaches a `PadTextEntryBridge` to **every** field it builds (63 call sites), so no screen
  opts in. While a pad is the active device the bridge keeps the field deactivated every frame (one
  `ActivateInputField` arms a flag the field re-reads next Update), and **A on the field** opens the
  keyboard; the result is written back through `field.text`, so the field's own `onValueChanged` listeners
  fire exactly as they would after typing.
- `ChatUi.OpenInput` takes the same route, and **d-pad up** opens it (`InputAction.OpenChat`).
- While the keyboard is up, `InputMap.ModalCapture` blanks `UiCancel`/`UiMenu` for every *other* screen, so
  one press of B closes exactly one thing; the keyboard itself reads the physical button through
  `InputMap.PadDown`. `UiKit.TextFieldFocused()` reports true, which is the gate the rest of the game
  already uses for "the player is typing".
- Layout: QWERTY + digits + `äöüß`, a symbol page (no `<`/`>` — a label has no rich-text neutralising pass),
  space / delete / done / cancel. Shift uppercases per character with the invariant culture so `ß` is left
  alone rather than becoming "SS" and quietly costing two characters of the limit.
- Native mobile keeps the OS keyboard and WebGL-touch keeps the browser prompt below; the keyboard is
  wanted only when `InputMap.ActiveDevice == Gamepad`.

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
  change (see "Retuning" above) — except the triggers, which a player can simply leave switched off.
