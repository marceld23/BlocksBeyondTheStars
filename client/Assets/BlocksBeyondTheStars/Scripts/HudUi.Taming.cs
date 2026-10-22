// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.World;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The creature-taming panel of <see cref="HudUi"/>: the translator's decoded mood and need, the trust bar and the
    /// response buttons, held as a HUD-level menu owner while a ritual runs (#2530).
    /// </summary>
    public sealed partial class HudUi
    {
        // Creature taming prompt: decoded mood + what the creature wants now, with the four response buttons.
        private GameObject _tamePanel;
        private TMP_Text _tameName, _tameMood, _tameNeed, _tameTrust;
        private Button _tameFeed, _tameCalm, _tameApproach, _tameSpace, _tameStop;

        // #2530: while a ritual runs the panel holds the cursor like NpcDialogUi (a HUD-level menu owner — the HUD stays
        // up and Tab may still open the menu over it), answers [1]–[4] / Esc / pad, and hides only under another menu.
        private readonly object _tameMenuOwner = new object();
        private TameProgress _tameDismissed;  // the state the player just stopped (the server's "inactive" reply is in flight)
        private TameProgress _tameShownState; // the state whose text the panel shows — a new one refreshes it at once
        private readonly string[] _tameCaptionBase = new string[5];
        private static readonly string[] TameResponses = { "feed", "calm", "approach", "space" };

        private void BuildTaming(Transform root)
        {
            // Creature-taming prompt (bottom-centre, above the hotbar): the translator's decoded mood + need,
            // a trust bar of correct responses, and the four response actions. Captions are set in RefreshTaming.
            // #2530: taller need box (message + need + the bait line naming the item and how to craft it), a wider Stop
            // for its "[Esc]" key label, and pad navigation over the five buttons (UiNav on the panel's own subtree).
            _tamePanel = Panel(root, W / 2f - 250f, 150f, 500f, 206f).gameObject;
            _tameName = UiText.Add(_tamePanel.transform, 14, 8, 360, 22, string.Empty, 18, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            _tameMood = UiText.Add(_tamePanel.transform, 14, 36, 472, 20, string.Empty, 15, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
            _tameNeed = UiText.Add(_tamePanel.transform, 14, 58, 472, 72, string.Empty, 14, UiKit.CyanDim, TextAnchor.UpperLeft);
            UiText.Wrap(_tameNeed);
            _tameTrust = UiText.Add(_tamePanel.transform, 14, 132, 472, 18, string.Empty, 13, UiKit.TextCol, TextAnchor.MiddleLeft);
            const float tby = 156f, tbw = 112f, tbh = 40f, tgap = 6f;
            _tameFeed = UiKit.AddButton(_tamePanel.transform, 14 + 0 * (tbw + tgap), tby, tbw, tbh, string.Empty, () => Respond("feed"));
            _tameCalm = UiKit.AddButton(_tamePanel.transform, 14 + 1 * (tbw + tgap), tby, tbw, tbh, string.Empty, () => Respond("calm"));
            _tameApproach = UiKit.AddButton(_tamePanel.transform, 14 + 2 * (tbw + tgap), tby, tbw, tbh, string.Empty, () => Respond("approach"));
            _tameSpace = UiKit.AddButton(_tamePanel.transform, 14 + 3 * (tbw + tgap), tby, tbw, tbh, string.Empty, () => Respond("space"));
            // Stop is built last so a pad's first focus lands on an answer, never on "Stop" (UiNav takes the first control).
            _tameStop = UiKit.AddButton(_tamePanel.transform, 378, 6, 108, 28, string.Empty, StopTaming);
            UiNav.Enable(_tamePanel); // pad: stick walks the answers, A picks, B stops — the number keys stay
            _tamePanel.SetActive(false);
        }

        /// <summary>Sends the player's chosen response in the current taming ritual (read from the live state).</summary>
        private void Respond(string response)
        {
            var t = Game?.TameState;
            if (t != null && Game?.Network != null)
            {
                Game.Network.SendTameRespond(t.CreatureId, response);
            }
        }

        /// <summary>Stop (#2530): ends the ritual on the server and closes the panel at once — the cursor re-locks this
        /// frame instead of waiting for the server's "inactive" reply.</summary>
        private void StopTaming()
        {
            Respond("cancel");
            _tameDismissed = Game?.TameState;
            if (_tamePanel != null && _tamePanel.activeSelf)
            {
                _tamePanel.SetActive(false);
            }

            Game?.SetHudMenuOwner(_tameMenuOwner, false);
        }

        // The server ends an attempt beyond 12 blocks (GameServer.TameLoseRange) and says so; the client lets go a little
        // later on its own, in case that word is lost — a frozen player must never wait on it.
        private const float TameClientReach = 16f;

        /// <summary>Safety net (#2530): whether the creature being tamed is still a wild animal in the streamed creature
        /// list within <see cref="TameClientReach"/> of the player. Gone (died, despawned, tamed by someone else) or far
        /// away ends the panel even if the server's own "it's gone" never arrives.</summary>
        private bool TameTargetNearby(string creatureId)
        {
            var creatures = Game.Creatures;
            if (creatures == null)
            {
                return false;
            }

            foreach (var c in creatures)
            {
                if (c != null && c.Id == creatureId)
                {
                    return string.IsNullOrEmpty(c.OwnerId)
                        && (Game.ScenePos(c.X, c.Y, c.Z) - Game.PlayerPosition).sqrMagnitude <= TameClientReach * TameClientReach;
                }
            }

            return false;
        }

        /// <summary>Per frame (#2530): while a taming ritual runs, the panel is a HUD-level menu owner — the cursor is free
        /// for the buttons and the player controller pauses, so the hotbar number keys answer the creature instead of
        /// switching away from the translator ([1]–[4] answer, Esc / pad B stops, the pad walks the buttons via UiNav,
        /// touch taps them). It hides only under ANOTHER menu (the Tab menu may open over it to craft the bait) and owns
        /// nothing under a cutscene. A ritual the player can no longer continue (dead, flying, its animal gone or far
        /// away) is ended, so it never holds the controls for nothing; a tame, a bolt or a provoked animal clears the state
        /// through the server's result.</summary>
        private void UpdateTaming(bool hudShown)
        {
            var st = Game.TameState;
            bool active = st != null && st.Active && !ReferenceEquals(st, _tameDismissed);
            if (active && (Game.Health <= 0f || Game.AwaitingRespawnConfirm || Game.SpaceViewActive || !TameTargetNearby(st.CreatureId)))
            {
                StopTaming();
                return;
            }

            bool sceneOwnsScreen = GameBootstrap.CutsceneActive || Game.VegaPrologueActive || Game.CinematicCameraActive
                                   || GameBootstrap.HudHidden;
            bool own = active && !sceneOwnsScreen;
            Game.SetHudMenuOwner(_tameMenuOwner, own);

            bool visible = own && hudShown;
            if (_tamePanel.activeSelf != visible)
            {
                _tamePanel.SetActive(visible);
                if (visible) { UiKit.TransitionIn(_tamePanel); }
            }

            if (!visible)
            {
                _tameShownState = null;
                return;
            }

            if (!ReferenceEquals(st, _tameShownState))
            {
                _tameShownState = st; // a new step / message from the server shows at once, not on the next Refresh
                RefreshTaming(Game.Localizer);
            }

            if (Game.ChatTyping || Game.MenuInputHandledThisFrame || OnScreenKeyboardUi.IsOpen)
            {
                return;
            }

            for (int i = 0; i < TameResponses.Length; i++)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1 + i) || Input.GetKeyDown(KeyCode.Keypad1 + i))
                {
                    Respond(TameResponses[i]);
                    return;
                }
            }

            if (InputMap.Down(InputAction.UiCancel))
            {
                Game.MarkMenuInputHandled(); // this Esc stops the ritual — it must not also open the pause menu
                StopTaming();
            }
        }

        /// <summary>The taming panel's texts. Showing/hiding is <see cref="UpdateTaming"/>'s (per frame); this only fills an
        /// open panel — from there on a new server state, else from the throttled <see cref="Refresh"/>.</summary>
        private void RefreshTaming(BlocksBeyondTheStars.Shared.Localization.Localizer loc)
        {
            var st = Game.TameState;
            if (st == null || loc == null || !_tamePanel.activeSelf) return;

            string name = string.IsNullOrEmpty(st.CreatureName) ? loc.Get("creature.generic.name") : st.CreatureName;
            _tameName.text = $"{loc.Get("ui.tame.title")} — {name}";
            _tameMood.text = string.IsNullOrEmpty(st.MoodKey) ? string.Empty : loc.Get(st.MoodKey);

            // #2530: the bait line names the actual item and how to make it ("Meat Bait — craft it by hand …") on a line
            // of its own; the old "(fresh meat)" read as if raw meat would do.
            string need = string.IsNullOrEmpty(st.NeedKey) ? string.Empty : loc.Get(st.NeedKey);
            if (!string.IsNullOrEmpty(st.BaitKey)) { need += "\n" + loc.Get(st.BaitKey); }
            if (!string.IsNullOrEmpty(st.MessageKey)) { need = loc.Get(st.MessageKey) + "\n" + need; }
            _tameNeed.text = need;
            _tameTrust.text = $"{loc.Get("ui.tame.trust")}: {st.Trust}/{st.Required}";

            // #2530: the keys on the buttons, like the NPC dialogue's "[1] " (#1127).
            SetTameCaption(0, _tameFeed, "[1] ", loc.Get("ui.tame.feed"));
            SetTameCaption(1, _tameCalm, "[2] ", loc.Get("ui.tame.calm"));
            SetTameCaption(2, _tameApproach, "[3] ", loc.Get("ui.tame.approach"));
            SetTameCaption(3, _tameSpace, "[4] ", loc.Get("ui.tame.space"));
            SetTameCaption(4, _tameStop, "[Esc] ", loc.Get("ui.tame.cancel"));
        }

        /// <summary>Sets a taming button's "[key] label" caption — only when the localized label changed (a language
        /// switch), so the throttled refresh does not build five strings a tick.</summary>
        private void SetTameCaption(int index, Button button, string keyPrefix, string label)
        {
            if (ReferenceEquals(_tameCaptionBase[index], label))
            {
                return;
            }

            _tameCaptionBase[index] = label;
            SetButtonText(button, keyPrefix + label);
        }

        private static void SetButtonText(Button button, string text)
        {
            var t = button.GetComponentInChildren<Text>();
            if (t != null) { t.text = text; }
        }
    }
}
