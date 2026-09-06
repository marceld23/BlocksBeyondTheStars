// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Remappable, discrete input actions, each resolved to a <see cref="KeyCode"/> through the player's
    /// bindings. This is the seed set for the controls-remapping work (Stream C): the legacy hardcoded
    /// <c>Input.GetKey(KeyCode.X)</c> call sites migrate onto these one subsystem at a time, and a settings UI
    /// can then rebind them. Continuous movement axes still go through the legacy Input Manager for now.
    /// </summary>
    public enum InputAction
    {
        Interact,          // generic "use / board / open" — default E
        PrimaryFire,       // melee swing / fire the held weapon — default F
        StowVehicle,       // pack up a deployed speeder you're standing next to — default X
        RecallVehicle,     // at the own cockpit/console: the ship brings a stranded speeder/boat back (#1661) — default X
        ToggleThirdPerson, // switch first/third-person camera — default V
        LootContainer,     // loot the nearest container — default G
        DepositToCrate,    // deposit into the nearest storage crate — default H
        RepairWreck,       // repair the nearest wreck cell (on foot) — default R
        ToggleLamp,        // toggle the suit lamp — default L
        RotateShape,       // cycle a held building shape's orientation (auto → the 6 up-faces) — default R
        ToggleThermal,     // infrared mode while looking through the thermal binoculars — default I
        ToggleChat,        // mute/unmute the chat scrollback overlay for this session (#636) — default J
        OpenChat,          // open the chat input box (#1211) — default Return, pad d-pad up
        HotbarAction,      // slot actions on the selected hotbar slot (swap/colour/form) — default middle mouse

        // Flight / EVA (cockpit + spacewalk). Interact (dock/land/board) and ToggleThirdPerson (view) are
        // reused from the on-foot set so one binding works in both contexts.
        FlightEnterInterior,  // leave the helm and walk the ship interior — default F
        FlightPadChooser,     // open the landing-pad chooser for your launch body — default L
        FlightAutopilot,      // toggle VEGA autopilot — default P
        FlightMap,            // open the system chart to set a nav waypoint (#597) — default M
        EvaDeployStation,     // deploy a station core during EVA — default B

        // Vehicle (speeder) + multiplayer dock/trade
        SpeederBoost,         // hold to boost the speeder — default LeftShift
        SpeederExit,          // dismount the speeder — default F
        SpeederRefuel,        // refuel the speeder — default R
        Disembark,            // leave a boarded station / undock — default U
        RequestTrade,         // request a trade with a nearby player — default T
        RequestDock,          // request to dock with a nearby player — default K

        // Device-neutral verbs (#1041–#1043). These used to be raw keys (or did not exist), which left
        // touch and gamepad without any path to them: VEGA's continue key was keyboard-only, the planet
        // map polled KeyCode.M directly, and there was no way to reach the long tail of on-foot verbs
        // from a pad with only two free buttons.
        VegaContinue,         // advance / dismiss the VEGA speech line — default N, pad Back
        PlanetMap,            // toggle the on-foot planet map — default M (the flight chart is FlightMap)
        ContextActions,       // open the list of currently applicable verbs — no key by default, pad LS, touch "⋯"
        PingMarker,           // "look here!" ping at the crosshair (#1217) — default C; pad/touch reach it
                              // through the ContextActions list, like the rest of the on-foot long tail

        // Menu verbs (#1198). Nine screens used to poll KeyCode.JoystickButton1 / JoystickButton7 right next
        // to their Escape / Tab check, which left the pad's two menu buttons outside the binding system:
        // not remappable, and a reader had to know that "1" means B. Routing them through actions makes the
        // pad column rebindable and leaves ONE place that says what closes a screen.
        // Their KEYBOARD key is deliberately fixed (see <see cref="InputMap.KeyboardLocked"/>): Escape and
        // Tab are the two keys a player must not be able to bind away — that can strand them in a modal.
        UiCancel,             // close / step back out of a screen — Escape, pad B
        UiMenu,               // toggle the in-game Tab menu — Tab, pad Start
    }

    /// <summary>
    /// Central indirection over the game's input. Two things flow through here: (1) discrete rebindable
    /// actions (<see cref="InputAction"/>) resolved to a <see cref="KeyCode"/> through the player's bindings
    /// (<see cref="ClientSettings"/>), and (2) the continuous locomotion/camera/interaction core (move, look,
    /// jump, mine/place, hotbar) that used to poll <c>UnityEngine.Input</c> directly.
    ///
    /// Rather than switching between input devices (a mode that could strand the keyboard), the map
    /// <b>combines</b> a keyboard/mouse backend and a gamepad backend: every getter reads BOTH and merges
    /// them, so a player can mix mouse and pad and neither can lock the other out. With no pad connected the
    /// gamepad backend returns zero/false, so this is exactly the legacy behaviour — the abstraction is
    /// behaviour-preserving on keyboard+mouse. <see cref="ActiveDevice"/> tracks which family was used most
    /// recently, purely to choose which button glyphs to show (see <see cref="Glyph"/>).
    ///
    /// Call <see cref="Use"/> once at startup with the loaded settings; an unbound action falls back to
    /// <see cref="DefaultKey"/> (the key it had before remapping existed).
    /// </summary>
    public static class InputMap
    {
        private static ClientSettings _settings;

        // The live backends. All are polled every frame and merged; see the class summary. The touch source
        // is inert unless a touch UI is active (tablet / touch browser), so it contributes nothing on desktop.
        private static readonly IInputSource _desktop = new DesktopInputSource();
        private static readonly IInputSource _pad = new GamepadInputSource();
        private static readonly IInputSource _touch = new TouchInputSource();

        private static int _deviceFrame = -1;
        private static InputDeviceKind _activeDevice = InputDeviceKind.KeyboardMouse;

        /// <summary>Test seam: pins <see cref="ActiveDevice"/> to a device the build machine cannot plug in,
        /// so the gamepad-only paths (the on-screen keyboard, glyph swapping) are reachable in CI. Null in
        /// normal play, where the answer always comes from what the player actually touched.</summary>
        public static InputDeviceKind? DeviceOverrideForTest;

        /// <summary>The input family used most recently — drives which glyphs the HUD shows. Recomputed at
        /// most once per frame; sticks to the last device when neither backend is active this frame.</summary>
        public static InputDeviceKind ActiveDevice
        {
            get
            {
                if (DeviceOverrideForTest.HasValue)
                {
                    return DeviceOverrideForTest.Value;
                }

                if (Time.frameCount != _deviceFrame)
                {
                    _deviceFrame = Time.frameCount;
                    if (_touch.HadActivityThisFrame())
                    {
                        _activeDevice = InputDeviceKind.Touch;
                    }
                    else if (_pad.HadActivityThisFrame())
                    {
                        _activeDevice = InputDeviceKind.Gamepad;
                    }
                    else if (_desktop.HadActivityThisFrame())
                    {
                        _activeDevice = InputDeviceKind.KeyboardMouse;
                    }
                }

                return _activeDevice;
            }
        }

        /// <summary>True if a gamepad is connected (any slot). Cheap; safe to poll from UI.</summary>
        public static bool GamepadConnected => GamepadInputSource.Connected();

        /// <summary>On-foot actions exposed in the controls-rebinding UI, in display order.</summary>
        public static readonly InputAction[] Remappable =
        {
            InputAction.Interact, InputAction.PrimaryFire, InputAction.StowVehicle, InputAction.RecallVehicle,
            InputAction.ToggleThirdPerson, InputAction.LootContainer, InputAction.DepositToCrate,
            InputAction.RepairWreck, InputAction.ToggleLamp, InputAction.RotateShape,
            InputAction.ToggleThermal, InputAction.ToggleChat, InputAction.OpenChat, InputAction.HotbarAction,
            InputAction.PlanetMap, InputAction.VegaContinue, InputAction.ContextActions,
            InputAction.PingMarker,
        };

        /// <summary>Flight / EVA actions exposed as a second rebinding group.</summary>
        public static readonly InputAction[] FlightRemappable =
        {
            InputAction.FlightEnterInterior, InputAction.FlightPadChooser,
            InputAction.FlightAutopilot, InputAction.FlightMap, InputAction.EvaDeployStation,
        };

        /// <summary>Vehicle (speeder) + dock/trade actions exposed as a third rebinding group.</summary>
        public static readonly InputAction[] VehicleRemappable =
        {
            InputAction.SpeederBoost, InputAction.SpeederExit, InputAction.SpeederRefuel,
            InputAction.Disembark, InputAction.RequestTrade, InputAction.RequestDock,
        };

        /// <summary>Menu verbs shown in the rebinding UI with their PAD column only (#1198). Their keyboard
        /// key is fixed — see <see cref="KeyboardLocked"/>.</summary>
        public static readonly InputAction[] MenuRemappable =
        {
            InputAction.UiCancel, InputAction.UiMenu,
        };

        /// <summary>True for actions whose keyboard key cannot be rebound. Escape closes every screen and Tab
        /// opens the in-game menu; a player who bound either of them away could end up inside a modal with no
        /// key that leaves it. The PAD button of these actions stays freely rebindable (#1198).</summary>
        public static bool KeyboardLocked(InputAction action) =>
            action == InputAction.UiCancel || action == InputAction.UiMenu;

        /// <summary>Points the map at the active settings (called once after <c>ClientSettings.Load()</c>).
        /// Also hands them to the gamepad backend so pad-button rebinds resolve.</summary>
        public static void Use(ClientSettings settings)
        {
            _settings = settings;
            GamepadInputSource.Settings = settings;
        }

        /// <summary>The built-in default key for an action (its binding before remapping existed).</summary>
        public static KeyCode DefaultKey(InputAction action) => action switch
        {
            InputAction.Interact => KeyCode.E,
            InputAction.PrimaryFire => KeyCode.F,
            InputAction.StowVehicle => KeyCode.X,
            InputAction.RecallVehicle => KeyCode.X, // shares X with the pack-up: a parked vehicle beside you wins, the cockpit recall otherwise
            InputAction.ToggleThirdPerson => KeyCode.V,
            InputAction.LootContainer => KeyCode.G,
            InputAction.DepositToCrate => KeyCode.H,
            InputAction.RepairWreck => KeyCode.R,
            InputAction.ToggleLamp => KeyCode.L,
            InputAction.RotateShape => KeyCode.R,
            InputAction.ToggleThermal => KeyCode.I, // "infrared"; N was taken by the VEGA dialogue advance
            InputAction.ToggleChat => KeyCode.J,    // one of the last free letters near the movement hand
            InputAction.OpenChat => KeyCode.Return, // the key that always opened the chat box (#1211)
            InputAction.HotbarAction => KeyCode.Mouse2, // middle click — free, and "act on what I hold" reads naturally there
            InputAction.FlightEnterInterior => KeyCode.F,
            InputAction.FlightPadChooser => KeyCode.L,
            InputAction.FlightAutopilot => KeyCode.P,
            InputAction.FlightMap => KeyCode.M,
            InputAction.EvaDeployStation => KeyCode.B,
            InputAction.SpeederBoost => KeyCode.LeftShift,
            InputAction.SpeederExit => KeyCode.F,
            InputAction.SpeederRefuel => KeyCode.R,
            InputAction.Disembark => KeyCode.U,
            InputAction.RequestTrade => KeyCode.T,
            InputAction.RequestDock => KeyCode.K,
            InputAction.VegaContinue => KeyCode.N,  // the key VegaPanel always used; now rebindable + reachable from pad/touch (#1041)
            InputAction.PlanetMap => KeyCode.M,     // WorldMap's historical key — same letter as FlightMap, different context (#1042)
            InputAction.ContextActions => KeyCode.None, // keyboard players have every verb on a key already; pad LS / touch "⋯"
            InputAction.PingMarker => KeyCode.C,
            InputAction.UiCancel => KeyCode.Escape, // the key every screen already closed on (#1198)
            InputAction.UiMenu => KeyCode.Tab,      // the key that always opened the in-game menu (#1198)
            _ => KeyCode.None,
        };

        // #1512: the per-action key table. Resolving a binding used to cost action.ToString() + a linear string
        // scan over the overrides + Enum.TryParse on EVERY poll — ~25–35 polls per frame across the controller,
        // the flight view and the UI, i.e. ~100 small allocations per frame. The table is rebuilt only when the
        // settings object or its BindingsVersion changes (rebind UI, reset, reload).
        private static KeyCode[] _keyTable;
        private static ClientSettings _keyTableSettings;
        private static int _keyTableVersion = -1;

        /// <summary>The currently bound key for an action — the player's override if set, else the default.</summary>
        public static KeyCode Key(InputAction action)
        {
            if (_settings == null || KeyboardLocked(action))
            {
                return DefaultKey(action);
            }

            if (_keyTable == null || !ReferenceEquals(_keyTableSettings, _settings) || _keyTableVersion != _settings.BindingsVersion)
            {
                RebuildKeyTable();
            }

            int i = (int)action;
            return i >= 0 && i < _keyTable.Length ? _keyTable[i] : ResolveKey(action);
        }

        /// <summary>The uncached resolution (the player's override if set, else the default) — the table's source.</summary>
        private static KeyCode ResolveKey(InputAction action)
        {
            var def = DefaultKey(action);
            if (_settings == null || KeyboardLocked(action))
            {
                return def;
            }

            string name = _settings.BoundKeyName(action.ToString());
            return !string.IsNullOrEmpty(name) && System.Enum.TryParse<KeyCode>(name, out var kc) ? kc : def;
        }

        private static void RebuildKeyTable()
        {
            var values = (InputAction[])System.Enum.GetValues(typeof(InputAction));
            int max = 0;
            foreach (var v in values)
            {
                max = Mathf.Max(max, (int)v);
            }

            var table = new KeyCode[max + 1];
            foreach (var v in values)
            {
                table[(int)v] = ResolveKey(v);
            }

            _keyTable = table;
            _keyTableSettings = _settings;
            _keyTableVersion = _settings.BindingsVersion;
        }

        // ---- Injected edges ------------------------------------------------------------------------------
        // A UI (the context-actions list, #1042/#1043) can fire an action on the caller's behalf. The edge is
        // armed for the NEXT frame, not this one: the list closes on the pick, and the gameplay poll sites
        // (PlayerController / PlayerInteractions / SpaceView) only run once Game.MenuOpen is false again —
        // which is the following Update. Frame-stable like every other Down(): every read that frame agrees.
        private static InputAction _injected;
        private static int _injectedFrame = -1;

        /// <summary>Makes <see cref="Down"/> report <paramref name="action"/> exactly once, on the next frame —
        /// the path a menu pick takes to fire a gameplay verb without a physical control.</summary>
        public static void InjectNextFrame(InputAction action)
        {
            _injected = action;
            _injectedFrame = Time.frameCount + 1;
        }

        private static bool Injected(InputAction action) => _injectedFrame == Time.frameCount && _injected == action;

        /// <summary>Set while a modal owns the pad's menu buttons — today the on-screen keyboard (#1211).
        /// While it is true, <see cref="UiCancel"/> and <see cref="UiMenu"/> read false for EVERY caller, so
        /// one press of B closes exactly one thing instead of the keyboard and the screen behind it in the
        /// same frame (Update order between two MonoBehaviours is not something a screen may rely on). The
        /// modal itself polls the physical button through <see cref="PadDown"/>, which is unaffected.</summary>
        public static bool ModalCapture;

        /// <summary>Whether <paramref name="action"/> is currently being swallowed by an open modal — true
        /// only for the two menu verbs, and only while <see cref="ModalCapture"/> is up. Public because it
        /// is the whole rule: a screen that wants to know why its Cancel went quiet can ask, and CI can pin
        /// it without a pad in the machine.</summary>
        public static bool ModalCaptures(InputAction action)
            => ModalCapture && (action == InputAction.UiCancel || action == InputAction.UiMenu);

        // Discrete rebindable actions — combined across all backends so a pad button, the touch USE button, or
        // the bound key all fire the action. The keyboard resolution is unchanged (DesktopInputSource calls Key).
        public static bool Down(InputAction action) => !ModalCaptures(action) && (_desktop.ActionDown(action) || _pad.ActionDown(action) || _touch.ActionDown(action) || Injected(action));
        public static bool Held(InputAction action) => !ModalCaptures(action) && (_desktop.ActionHeld(action) || _pad.ActionHeld(action) || _touch.ActionHeld(action));
        public static bool Up(InputAction action) => !ModalCaptures(action) && (_desktop.ActionUp(action) || _pad.ActionUp(action) || _touch.ActionUp(action));

        // ---- Continuous locomotion / camera / interaction core -------------------------------------------
        // Each merges the backends. Movement + look are additive (mouse delta + stick delta + touch); the
        // button verbs OR together. This is what the migrated PlayerController/SpaceView call sites read
        // instead of Input.GetAxis / GetButton / GetMouseButton. The touch source is zero unless a touch UI
        // is live, so on desktop these equal the keyboard/mouse (+ pad) behaviour exactly.

        /// <summary>Scripted locomotion injected by automation (PerfProbe traversal). Zero in normal play;
        /// additive like the other sources, so a real key press still wins the clamp.</summary>
        public static Vector2 ScriptedMove;

        /// <summary>Strafe axis, −1..1 — replaces <c>Input.GetAxis("Horizontal")</c>.</summary>
        public static float MoveX() => Mathf.Clamp(_desktop.MoveX() + _pad.MoveX() + _touch.MoveX() + ScriptedMove.x, -1f, 1f);

        /// <summary>Forward axis, −1..1 — replaces <c>Input.GetAxis("Vertical")</c>.</summary>
        public static float MoveY() => Mathf.Clamp(_desktop.MoveY() + _pad.MoveY() + _touch.MoveY() + ScriptedMove.y, -1f, 1f);

        /// <summary>Yaw look delta (caller still multiplies by sensitivity) — replaces <c>GetAxis("Mouse X")</c>.</summary>
        public static float LookX() => _desktop.LookX() + _pad.LookX() + _touch.LookX();

        /// <summary>Pitch look delta (caller still multiplies by sensitivity) — replaces <c>GetAxis("Mouse Y")</c>.</summary>
        public static float LookY() => _desktop.LookY() + _pad.LookY() + _touch.LookY();

        /// <summary>Hotbar scroll: &gt;0 = previous slot, &lt;0 = next — replaces <c>GetAxis("Mouse ScrollWheel")</c>.
        /// Mouse wheel takes precedence; the pad d-pad / touch ◄► buttons fill in when the wheel is idle.</summary>
        public static float HotbarScroll()
        {
            float d = _desktop.HotbarScroll();
            if (Mathf.Abs(d) > 0.0001f)
            {
                return d;
            }

            float p = _pad.HotbarScroll();
            return Mathf.Abs(p) > 0.0001f ? p : _touch.HotbarScroll();
        }

        // ---- Named pad buttons + raw sticks ---------------------------------------------------------------
        // For screens that need more of the pad than the rebindable action set covers: an editor viewport
        // where the sticks drive a camera or a paint cursor rather than the player (#1198), and later the
        // minigame host (#1218). Everything stays behind InputMap so no screen spells out a JoystickButton
        // number, and "no pad connected" keeps returning zero/false in one place.

        /// <summary>A named pad button pressed this frame (see <see cref="PadButton"/>).</summary>
        public static bool PadDown(PadButton button) => GamepadInputSource.Down(button);

        /// <summary>A named pad button held this frame.</summary>
        public static bool PadHeld(PadButton button) => GamepadInputSource.Held(button);

        /// <summary>Left stick X on its own, deadzoned — see <see cref="GamepadInputSource.RawStickX"/>.</summary>
        public static float PadStickX() => GamepadInputSource.RawStickX();

        /// <summary>Left stick Y on its own, deadzoned.</summary>
        public static float PadStickY() => GamepadInputSource.RawStickY();

        /// <summary>D-pad X as a raw axis (no cooldown) — the minigame host runs its own repeat (#1218).</summary>
        public static float PadDpadX() => GamepadInputSource.RawDpadX();

        /// <summary>D-pad Y raw (positive = up) — see <see cref="PadDpadX"/>.</summary>
        public static float PadDpadY() => GamepadInputSource.RawDpadY();

        /// <summary>Right stick Y as a raw −1..1 axis (deadzoned, positive = up), for menus that scroll a
        /// pane with it — unlike <see cref="PadLookY"/> this carries no look rate or sensitivity.</summary>
        public static float PadScrollY() => GamepadInputSource.RawRightStickY();

        /// <summary>Right-stick look from the PAD ALONE (already a per-frame delta). <see cref="LookX"/>
        /// merges the mouse in; an editor viewport wants stick look without the mouse, which moves the
        /// pointer over its panels instead.</summary>
        public static float PadLookX() => _pad.LookX();

        /// <summary>Right-stick pitch from the pad alone — see <see cref="PadLookX"/>.</summary>
        public static float PadLookY() => _pad.LookY();

        /// <summary>One repeat-gated d-pad left/right step from the pad alone: &gt;0 = left/previous,
        /// &lt;0 = right/next, 0 while the d-pad rests or its repeat is on cooldown. Same sign convention and
        /// the same cooldown as the hotbar cycle (<see cref="HotbarScroll"/>), minus the mouse wheel — a
        /// screen that steps a list on the d-pad wants the gating without the wheel.</summary>
        public static float PadDpadStep() => _pad.HotbarScroll();

        public static bool JumpHeld() => _desktop.JumpHeld() || _pad.JumpHeld() || _touch.JumpHeld();
        public static bool JumpDown() => _desktop.JumpDown() || _pad.JumpDown() || _touch.JumpDown();
        public static bool CrouchHeld() => _desktop.CrouchHeld() || _pad.CrouchHeld() || _touch.CrouchHeld();
        public static bool PrimaryDown() => _desktop.PrimaryDown() || _pad.PrimaryDown() || _touch.PrimaryDown();
        public static bool PrimaryHeld() => _desktop.PrimaryHeld() || _pad.PrimaryHeld() || _touch.PrimaryHeld();
        public static bool SecondaryDown() => _desktop.SecondaryDown() || _pad.SecondaryDown() || _touch.SecondaryDown();

        /// <summary>Hotbar slot 0..8 picked directly this frame (number keys), or −1. Pad + touch have no
        /// direct pick (they cycle via <see cref="HotbarScroll"/>), so this is the keyboard's answer.</summary>
        public static int HotbarSlotDown()
        {
            int s = _desktop.HotbarSlotDown();
            return s >= 0 ? s : _pad.HotbarSlotDown();
        }

        /// <summary>Locale key of an action's human name (<c>ui.key.*</c>) — the settings rebind rows and the
        /// context-actions list (#1042) both label actions through this one table.</summary>
        public static string LabelKey(InputAction action) => action switch
        {
            InputAction.Interact => "ui.key.interact",
            InputAction.PrimaryFire => "ui.key.primary_fire",
            InputAction.StowVehicle => "ui.key.stow_vehicle",
            InputAction.RecallVehicle => "ui.key.recall_vehicle",
            InputAction.ToggleThirdPerson => "ui.key.toggle_third_person",
            InputAction.LootContainer => "ui.key.loot_container",
            InputAction.DepositToCrate => "ui.key.deposit_to_crate",
            InputAction.RepairWreck => "ui.key.repair_wreck",
            InputAction.ToggleLamp => "ui.key.toggle_lamp",
            InputAction.RotateShape => "ui.key.rotate_shape",
            InputAction.ToggleThermal => "ui.key.toggle_thermal",
            InputAction.ToggleChat => "ui.key.toggle_chat",
            InputAction.OpenChat => "ui.key.open_chat",
            InputAction.HotbarAction => "ui.key.hotbar_action",
            InputAction.FlightEnterInterior => "ui.key.flight_enter_interior",
            InputAction.FlightPadChooser => "ui.key.flight_pad_chooser",
            InputAction.FlightAutopilot => "ui.key.flight_autopilot",
            InputAction.FlightMap => "ui.key.flight_map",
            InputAction.EvaDeployStation => "ui.key.eva_deploy_station",
            InputAction.SpeederBoost => "ui.key.speeder_boost",
            InputAction.SpeederExit => "ui.key.speeder_exit",
            InputAction.SpeederRefuel => "ui.key.speeder_refuel",
            InputAction.Disembark => "ui.key.disembark",
            InputAction.RequestTrade => "ui.key.request_trade",
            InputAction.RequestDock => "ui.key.request_dock",
            InputAction.VegaContinue => "ui.key.vega_continue",
            InputAction.PlanetMap => "ui.key.planet_map",
            InputAction.ContextActions => "ui.key.context_actions",
            InputAction.PingMarker => "ui.key.ping",
            InputAction.UiCancel => "ui.key.ui_cancel",
            InputAction.UiMenu => "ui.key.ui_menu",
            _ => string.Empty,
        };

        // ---- Glyphs -------------------------------------------------------------------------------------

        /// <summary>A short on-screen label for an action's control, matched to the <see cref="ActiveDevice"/>:
        /// the pad face-button letter when a pad is in use and the action is mapped, otherwise the bound
        /// keyboard key. Used for HUD control hints so they read correctly whichever device is in hand.</summary>
        public static string Glyph(InputAction action)
        {
            if (ActiveDevice == InputDeviceKind.Gamepad)
            {
                string pad = PadGlyph(GamepadInputSource.ButtonFor(action));
                if (pad != null)
                {
                    return pad;
                }
            }

            return Key(action).ToString();
        }

        /// <summary>Locale key for a mouse button's short on-screen name (<c>ui.key.mouse_*</c>), or null for
        /// non-mouse codes. Returned as a KEY because InputMap has no localizer — HUD callers resolve it.
        /// <see cref="Glyph"/> alone prints the raw KeyCode name ("Mouse2") for mouse-bound actions, which
        /// reads like a debug string next to the localized LMB/RMB wording of the hint line (#935).</summary>
        public static string MouseLocaleKey(KeyCode key) => key switch
        {
            KeyCode.Mouse0 => "ui.key.mouse_left",
            KeyCode.Mouse1 => "ui.key.mouse_right",
            KeyCode.Mouse2 => "ui.key.mouse_middle",
            _ => null,
        };

        /// <summary>Human label for a pad button, or null for <see cref="KeyCode.None"/> / non-pad codes.
        /// The wording follows the layout the player picked in the controller settings (#1219): the BUTTON
        /// NUMBER never changes — a pad reports the same code whatever is printed on it — only the name
        /// does, matched by PHYSICAL POSITION (JoystickButton0 is the bottom face button: Xbox A,
        /// PlayStation Cross, Nintendo B).
        ///
        /// The PlayStation shapes are spelled out rather than drawn: the bundled UI font (Rajdhani) is
        /// Latin-only, so ✕ ○ □ △ would come out as missing-glyph boxes — and glyph SPRITES would cost the
        /// WebGL build more than a control hint is worth.</summary>
        public static string PadGlyph(KeyCode button)
        {
            var set = GamepadInputSource.Settings != null ? GamepadInputSource.Settings.PadGlyphs : PadGlyphSet.Xbox;
            return button switch
            {
                KeyCode.JoystickButton0 => set switch { PadGlyphSet.PlayStation => "(Cross)", PadGlyphSet.Nintendo => "(B)", _ => "(A)" },
                KeyCode.JoystickButton1 => set switch { PadGlyphSet.PlayStation => "(Circle)", PadGlyphSet.Nintendo => "(A)", _ => "(B)" },
                KeyCode.JoystickButton2 => set switch { PadGlyphSet.PlayStation => "(Square)", PadGlyphSet.Nintendo => "(Y)", _ => "(X)" },
                KeyCode.JoystickButton3 => set switch { PadGlyphSet.PlayStation => "(Triangle)", PadGlyphSet.Nintendo => "(X)", _ => "(Y)" },
                KeyCode.JoystickButton4 => set switch { PadGlyphSet.PlayStation => "L1", PadGlyphSet.Nintendo => "L", _ => "LB" },
                KeyCode.JoystickButton5 => set switch { PadGlyphSet.PlayStation => "R1", PadGlyphSet.Nintendo => "R", _ => "RB" },
                // "View" is what the button is called on every Xbox pad since the One (the two overlapping
                // rectangles); "Back" was the 360's name, which is Unity's — and no player looking at a
                // modern pad found it, so VEGA lines seemed undismissable on a controller.
                KeyCode.JoystickButton6 => set switch { PadGlyphSet.PlayStation => "Share", PadGlyphSet.Nintendo => "-", _ => "View" },
                // Same story for "Start": since the Xbox One it is the "Menu" button (three lines, right of
                // the logo). A player reading "Start" reached for the Xbox-logo button — which Windows'
                // Game Bar owns — and concluded the game had no way into its menus on a pad.
                KeyCode.JoystickButton7 => set switch { PadGlyphSet.PlayStation => "Options", PadGlyphSet.Nintendo => "+", _ => "Menu" },
                KeyCode.JoystickButton8 => set == PadGlyphSet.Xbox ? "LS" : "L3",
                KeyCode.JoystickButton9 => set == PadGlyphSet.Xbox ? "RS" : "R3",
                >= KeyCode.JoystickButton10 and <= KeyCode.JoystickButton19 => "B" + (button - KeyCode.JoystickButton0),
                _ => null,
            };
        }
    }
}
