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
    /// Central indirection over Unity's legacy Input so every key flows through the player's bindings
    /// (<see cref="ClientSettings"/>) instead of a hardcoded <see cref="KeyCode"/> — the foundation for
    /// rebindable controls. Call <see cref="Use"/> once at startup with the loaded settings; an unbound action
    /// falls back to <see cref="DefaultKey"/> (the key it had before remapping existed), so migrating a call
    /// site is behaviour-preserving until the player actually rebinds it.
    /// </summary>
    public static class InputMap
    {
        private static ClientSettings _settings;

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

        /// <summary>Points the map at the active settings (called once after <c>ClientSettings.Load()</c>).</summary>
        public static void Use(ClientSettings settings) => _settings = settings;

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

        public static bool Down(InputAction action) => Input.GetKeyDown(Key(action));
        public static bool Held(InputAction action) => Input.GetKey(Key(action));
        public static bool Up(InputAction action) => Input.GetKeyUp(Key(action));
    }
}
