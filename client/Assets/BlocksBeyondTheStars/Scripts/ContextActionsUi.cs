// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The context-actions list (#1042/#1043): one control — the touch ACT button, the pad's left-stick
    /// click, or a bound key — opens a short list of every gameplay verb that applies <i>right now</i>
    /// (rotate the held shape, trade / dock with the player beside you, undock, loot the crate in reach,
    /// toggle the lamp, deploy a station in EVA, …). Picking an entry injects that <see cref="InputAction"/>
    /// for the next frame through <see cref="InputMap.InjectNextFrame"/>, so every existing poll site
    /// (<see cref="PlayerController"/>, <see cref="PlayerInteractions"/>, <see cref="SpaceView"/>) fires
    /// exactly as if its key had been pressed — no second rule set, no per-verb wiring.
    ///
    /// Why a list and not more buttons: the keyboard has a letter for each of these 20-odd verbs, but the
    /// stock Xbox layout has two free buttons and a tablet screen has room for a handful of thumb targets.
    /// The frequent verbs still get direct touch buttons (ROTATE, PLACE, MAP, NEXT, ATTACK); this list is
    /// the long tail, filtered by applicability so it never offers a dead action.
    ///
    /// Same construction as <see cref="HotbarActionUi"/>: a plain screen-space canvas (not the diegetic
    /// HUD canvas, whose RT hit-testing lands wrong), <see cref="UiNav"/> for stick navigation, and menu
    /// ownership through the cursor arbiter (#413) so on-foot / flight control freezes while it is up.
    /// </summary>
    public sealed class ContextActionsUi : MonoBehaviour
    {
        public static ContextActionsUi Instance { get; private set; }

        public GameBootstrap Game;
        public PlayerController Player;
        public PlayerInteractions Interactions;
        public VegaPanel Vega;

        private Canvas _canvas;
        private int _openedFrame = -1;
        private readonly List<InputAction> _scratch = new List<InputAction>(16);

        /// <summary>One candidate verb: the action, and whether it applies in the current situation.</summary>
        private readonly struct Entry
        {
            public readonly InputAction Action;
            public readonly Func<ContextActionsUi, bool> Applies;

            public Entry(InputAction action, Func<ContextActionsUi, bool> applies)
            {
                Action = action;
                Applies = applies;
            }
        }

        // The verb table, in display order. Predicates mirror the precondition each handler checks (see the
        // PlayerController / PlayerInteractions probes) so an offered verb always does something. Verbs the
        // player already has a direct touch button for (jump/mine/place/use/…) are not listed.
        private static readonly Entry[] Table =
        {
            new Entry(InputAction.VegaContinue, u => u.Vega != null && u.Vega.LineShowing),

            // On foot.
            new Entry(InputAction.RotateShape, u => u.OnFoot && u.Player != null && u.Player.HeldRotatable),
            new Entry(InputAction.RequestTrade, u => u.OnFoot && u.Interactions != null && u.Interactions.CanRequestTradeOrDock),
            new Entry(InputAction.RequestDock, u => u.OnFoot && u.Interactions != null && u.Interactions.CanRequestTradeOrDock),
            new Entry(InputAction.Disembark, u => u.OnFoot && u.Interactions != null && u.Interactions.CanDisembark),
            new Entry(InputAction.LootContainer, u => u.OnFoot && u.Player != null && u.Player.NearContainer),
            new Entry(InputAction.DepositToCrate, u => u.OnFoot && u.Player != null && u.Player.NearCrate),
            new Entry(InputAction.RepairWreck, u => u.OnFoot && u.Player != null && u.Player.NearWreck),
            new Entry(InputAction.StowVehicle, u => u.OnFoot && u.Player != null && u.Player.NearOwnParkedSpeeder),
            new Entry(InputAction.PrimaryFire, u => u.OnFoot && u.Player != null && u.Player.HoldsWeapon),
            new Entry(InputAction.ToggleThermal, u => u.OnFoot && u.Player != null && u.Player.BinocularsRaised),
            new Entry(InputAction.PlanetMap, u => u.OnFoot && !u.Game.InSpace),
            new Entry(InputAction.ToggleThirdPerson, u => u.OnFoot),
            new Entry(InputAction.ToggleLamp, u => u.OnFoot),
            new Entry(InputAction.ToggleChat, u => u.OnFoot),

            // At the helm.
            new Entry(InputAction.FlightPadChooser, u => u.Piloting),
            new Entry(InputAction.FlightEnterInterior, u => u.Piloting),
            new Entry(InputAction.FlightAutopilot, u => u.Piloting),
            new Entry(InputAction.FlightMap, u => u.Piloting),
            new Entry(InputAction.ToggleThirdPerson, u => u.Piloting),

            // EVA.
            new Entry(InputAction.EvaDeployStation, u => u.Eva),

            // Speeder.
            new Entry(InputAction.SpeederExit, u => u.Driving),
            new Entry(InputAction.SpeederRefuel, u => u.Driving),
        };

        private bool OnFoot => Game != null && !Game.SpaceViewActive && Game.DrivenSpeeder == null && !Game.Spectating;
        private bool Piloting => Game != null && Game.SpaceViewActive && !Game.InEva;
        private bool Eva => Game != null && Game.SpaceViewActive && Game.InEva;
        private bool Driving => Game != null && !Game.SpaceViewActive && Game.DrivenSpeeder != null;

        private void Awake() => Instance = this;

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            Close();
        }

        /// <summary>True while the list is open (gameplay hotkeys stand down through the menu-owner arbiter).</summary>
        public bool IsOpen => _canvas != null;

        /// <summary>True while the list could open right now: no other panel owns the input, no text entry, and
        /// at least one verb applies. Also gates the touch ACT button's visibility.</summary>
        public bool CanOpen
        {
            get
            {
                if (Game == null || Game.MenuOpen || Game.ChatTyping || Game.AwaitingRespawnConfirm)
                {
                    return false;
                }

                for (int i = 0; i < Table.Length; i++)
                {
                    if (Table[i].Applies(this))
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        /// <summary>Opens the list, or closes it — the touch button's entry point. The key/pad path in
        /// <see cref="Update"/> funnels through the same gates.</summary>
        public void Toggle()
        {
            if (IsOpen)
            {
                Close();
            }
            else if (CanOpen)
            {
                Open();
            }
        }

        private void Update()
        {
            if (Game == null)
            {
                return;
            }

            if (IsOpen)
            {
                // The opening control toggles (not on the very frame that opened it), Esc always closes, and
                // pad B backs out — the list is stick-navigable, so the pad needs an exit besides re-clicking LS.
                bool toggle = Time.frameCount != _openedFrame && InputMap.Down(InputAction.ContextActions);
                if (toggle || Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.JoystickButton1))
                {
                    Game.MarkMenuInputHandled(); // spent here — the app shell / pause menu must not also act on it (#413)
                    Close();
                }

                return;
            }

            if (InputMap.Down(InputAction.ContextActions) && CanOpen)
            {
                Open();
            }
        }

        private void Open()
        {
            _openedFrame = Time.frameCount;
            _canvas = UiKit.CreateCanvas("ContextActionsUi");
            _canvas.sortingOrder = 40; // above the HUD (10) and the flight overlay (12), like the slot-action pie
            UiNav.Enable(_canvas.gameObject); // pad: auto-focus so the stick walks the entries, A picks
            Game.SetMenuOwner(this, true);    // freezes player control + frees the cursor via the arbiter (#413)
            Build();
        }

        private void Close()
        {
            if (_canvas != null)
            {
                Destroy(_canvas.gameObject);
                _canvas = null;
                Game?.SetMenuOwner(this, false);
            }
        }

        private void Build()
        {
            _scratch.Clear();
            for (int i = 0; i < Table.Length; i++)
            {
                if (Table[i].Applies(this) && !_scratch.Contains(Table[i].Action))
                {
                    _scratch.Add(Table[i].Action);
                }
            }

            var root = _canvas.transform;
            var dim = UiKit.AddModalDim(root, 0.55f); // lighter than a dialog — the world stays readable behind the list
            var t = dim.transform;

            const float cx = 960f;   // canvas reference centre (1920×1080)
            const float w = 460f, h = 54f, gap = 8f;
            int rows = _scratch.Count + 1; // + Close
            float totalH = 44f + rows * (h + gap);
            float y = 540f - totalH * 0.5f;

            var head = UiKit.AddText(t, cx - w * 0.5f, y, w, 34f, L("ui.actions.title"), 24, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.AddOutline(head);
            y += 44f;

            for (int i = 0; i < _scratch.Count; i++)
            {
                var action = _scratch[i];
                UiKit.AddButton(t, cx - w * 0.5f, y, w, h, L(InputMap.LabelKey(action)), () => Pick(action));
                y += h + gap;
            }

            UiKit.AddButton(t, cx - w * 0.5f, y, w, h, L("ui.action.close"), Close);
        }

        /// <summary>Fires the picked verb on the next frame (once control is back with gameplay) and closes.</summary>
        private void Pick(InputAction action)
        {
            InputMap.InjectNextFrame(action);
            Close();
        }

        private string L(string key)
        {
            var loc = Game != null ? Game.Localizer : null;
            return loc != null ? loc.Get(key) : key;
        }
    }
}
