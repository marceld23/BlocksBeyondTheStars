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
        ToggleThirdPerson, // switch first/third-person camera — default V
        LootContainer,     // loot the nearest container — default G
        DepositToCrate,    // deposit into the nearest storage crate — default H
        RepairWreck,       // repair the nearest wreck cell (on foot) — default R
        ToggleLamp,        // toggle the suit lamp — default L
        RotateShape,       // cycle a held building shape's orientation (auto → the 6 up-faces) — default R

        // Flight / EVA (cockpit + spacewalk). Interact (dock/land/board) and ToggleThirdPerson (view) are
        // reused from the on-foot set so one binding works in both contexts.
        FlightEnterInterior,  // leave the helm and walk the ship interior — default F
        FlightPadChooser,     // open the landing-pad chooser for your launch body — default L
        FlightAutopilot,      // toggle VEGA autopilot — default P
        EvaDeployStation,     // deploy a station core during EVA — default B

        // Vehicle (speeder) + multiplayer dock/trade
        SpeederBoost,         // hold to boost the speeder — default LeftShift
        SpeederExit,          // dismount the speeder — default F
        SpeederRefuel,        // refuel the speeder — default R
        Disembark,            // leave a boarded station / undock — default U
        RequestTrade,         // request a trade with a nearby player — default T
        RequestDock,          // request to dock with a nearby player — default K
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

        /// <summary>The input family used most recently — drives which glyphs the HUD shows. Recomputed at
        /// most once per frame; sticks to the last device when neither backend is active this frame.</summary>
        public static InputDeviceKind ActiveDevice
        {
            get
            {
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
            InputAction.Interact, InputAction.PrimaryFire, InputAction.StowVehicle,
            InputAction.ToggleThirdPerson, InputAction.LootContainer, InputAction.DepositToCrate,
            InputAction.RepairWreck, InputAction.ToggleLamp, InputAction.RotateShape,
        };

        /// <summary>Flight / EVA actions exposed as a second rebinding group.</summary>
        public static readonly InputAction[] FlightRemappable =
        {
            InputAction.FlightEnterInterior, InputAction.FlightPadChooser,
            InputAction.FlightAutopilot, InputAction.EvaDeployStation,
        };

        /// <summary>Vehicle (speeder) + dock/trade actions exposed as a third rebinding group.</summary>
        public static readonly InputAction[] VehicleRemappable =
        {
            InputAction.SpeederBoost, InputAction.SpeederExit, InputAction.SpeederRefuel,
            InputAction.Disembark, InputAction.RequestTrade, InputAction.RequestDock,
        };

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
            InputAction.ToggleThirdPerson => KeyCode.V,
            InputAction.LootContainer => KeyCode.G,
            InputAction.DepositToCrate => KeyCode.H,
            InputAction.RepairWreck => KeyCode.R,
            InputAction.ToggleLamp => KeyCode.L,
            InputAction.RotateShape => KeyCode.R,
            InputAction.FlightEnterInterior => KeyCode.F,
            InputAction.FlightPadChooser => KeyCode.L,
            InputAction.FlightAutopilot => KeyCode.P,
            InputAction.EvaDeployStation => KeyCode.B,
            InputAction.SpeederBoost => KeyCode.LeftShift,
            InputAction.SpeederExit => KeyCode.F,
            InputAction.SpeederRefuel => KeyCode.R,
            InputAction.Disembark => KeyCode.U,
            InputAction.RequestTrade => KeyCode.T,
            InputAction.RequestDock => KeyCode.K,
            _ => KeyCode.None,
        };

        /// <summary>The currently bound key for an action — the player's override if set, else the default.</summary>
        public static KeyCode Key(InputAction action)
        {
            var def = DefaultKey(action);
            if (_settings == null)
            {
                return def;
            }

            string name = _settings.BoundKeyName(action.ToString());
            return !string.IsNullOrEmpty(name) && System.Enum.TryParse<KeyCode>(name, out var kc) ? kc : def;
        }

        // Discrete rebindable actions — combined across all backends so a pad button, the touch USE button, or
        // the bound key all fire the action. The keyboard resolution is unchanged (DesktopInputSource calls Key).
        public static bool Down(InputAction action) => _desktop.ActionDown(action) || _pad.ActionDown(action) || _touch.ActionDown(action);
        public static bool Held(InputAction action) => _desktop.ActionHeld(action) || _pad.ActionHeld(action) || _touch.ActionHeld(action);
        public static bool Up(InputAction action) => _desktop.ActionUp(action) || _pad.ActionUp(action) || _touch.ActionUp(action);

        // ---- Continuous locomotion / camera / interaction core -------------------------------------------
        // Each merges the backends. Movement + look are additive (mouse delta + stick delta + touch); the
        // button verbs OR together. This is what the migrated PlayerController/SpaceView call sites read
        // instead of Input.GetAxis / GetButton / GetMouseButton. The touch source is zero unless a touch UI
        // is live, so on desktop these equal the keyboard/mouse (+ pad) behaviour exactly.

        /// <summary>Strafe axis, −1..1 — replaces <c>Input.GetAxis("Horizontal")</c>.</summary>
        public static float MoveX() => Mathf.Clamp(_desktop.MoveX() + _pad.MoveX() + _touch.MoveX(), -1f, 1f);

        /// <summary>Forward axis, −1..1 — replaces <c>Input.GetAxis("Vertical")</c>.</summary>
        public static float MoveY() => Mathf.Clamp(_desktop.MoveY() + _pad.MoveY() + _touch.MoveY(), -1f, 1f);

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

        /// <summary>Human label for a pad button (XInput names for the well-known ones, "B10"… for the rest),
        /// or null for <see cref="KeyCode.None"/> / non-pad codes.</summary>
        public static string PadGlyph(KeyCode button) => button switch
        {
            KeyCode.JoystickButton0 => "(A)",
            KeyCode.JoystickButton1 => "(B)",
            KeyCode.JoystickButton2 => "(X)",
            KeyCode.JoystickButton3 => "(Y)",
            KeyCode.JoystickButton4 => "LB",
            KeyCode.JoystickButton5 => "RB",
            KeyCode.JoystickButton6 => "Back",
            KeyCode.JoystickButton7 => "Start",
            KeyCode.JoystickButton8 => "LS",
            KeyCode.JoystickButton9 => "RS",
            >= KeyCode.JoystickButton10 and <= KeyCode.JoystickButton19 => "B" + (button - KeyCode.JoystickButton0),
            _ => null,
        };
    }
}
