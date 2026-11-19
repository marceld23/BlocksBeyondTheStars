// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Playtests;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The start card of a playtest task (#3848): when the test world created for a task comes up, the task introduces
    /// itself once — its title, what it is about, how many steps it has, where the current step is shown and how to
    /// reach the whole list — with one button, "Let's go". The world waits behind it like behind the feedback dialog
    /// (its own hold, so closing another dialog does not let the world run), and the card owns the controls meanwhile.
    /// A test world has no story, so nothing competes with it. The task's texts come from the fetched catalogue and are
    /// shown verbatim. Opened by <see cref="PlaytestHub"/>; a tester's own world only gets a status line.
    /// </summary>
    public sealed class PlaytestStartCard : MonoBehaviour
    {
        private const float W = 900f, M = 44f, Inner = W - (2f * M);
        private static PlaytestStartCard _instance;

        private AppShell _shell;
        private Canvas _canvas;
        private GameBootstrap _boot;

        /// <summary>True while the start card is on screen.</summary>
        public static bool IsOpen => _instance != null;

        /// <summary>Opens the card for <paramref name="task"/> over the world that just came up.</summary>
        public static void Open(AppShell shell, PlaytestTask task)
        {
            if (_instance != null || shell == null || task == null)
            {
                return;
            }

            _instance = new GameObject("PlaytestStartCard").AddComponent<PlaytestStartCard>();
            _instance._shell = shell;
            _instance.Build(task);
        }

        private void Build(PlaytestTask task)
        {
            string lang = _shell.Settings.Language;
            string intro = task.Intro.For(lang);
            string facts = string.Format(_shell.L("ui.testpilot.start.steps"), task.Steps.Count);
            if (task.Minutes > 0)
            {
                facts += " · " + string.Format(_shell.L("ui.testpilot.minutes"), task.Minutes);
            }

            // A pad has no Test Pilot key: its way to the list is the pause menu, and the card says so.
            string hint = InputMap.ActiveDevice == InputDeviceKind.Gamepad
                ? _shell.L("ui.testpilot.start.hint_pad")
                : _shell.L("ui.testpilot.start.hint_key").Replace("{key}", InputMap.Glyph(InputAction.TestPilot));
            float introH = UiKit.MeasureHeight(intro, Inner, 21, FontStyle.Normal, false);
            float hintH = UiKit.MeasureHeight(hint, Inner, 17, FontStyle.Normal, false);
            float h = 150f + introH + 24f + 30f + 20f + hintH + 34f + 64f + 36f;

            Transform panel;
            (_canvas, panel) = PlaytestModal.Build("PlaytestStartUI", W, h);
            PlaytestModal.Opened();
            UiKit.AddIcon(panel, M, 30f, 44f, "btn_testpilot");
            UiKit.AddKicker(panel, M + 56f, 40f, Inner - 56f, _shell.L("ui.testpilot.start.kicker"), UiKit.Warn);
            var title = UiKit.AddText(panel, M, 92f, Inner, 44f, task.Title.For(lang), 32, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
            title.supportRichText = false;
            UiKit.FitLabelSingleLine(title, Inner, 18, 32);

            float y = 150f;
            var body = UiKit.AddMeasuredText(panel, M, y, Inner, intro, 21, UiKit.TextCol, out float measured);
            body.supportRichText = false;
            y += Mathf.Max(introH, measured) + 24f;
            var line = UiKit.AddText(panel, M, y, Inner, 30f, facts, 18, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.FitLabelSingleLine(line, Inner, 13, 18);
            y += 30f + 20f;
            UiKit.AddMeasuredText(panel, M, y, Inner, hint, 17, UiKit.CyanDim, out float hintMeasured);
            y += Mathf.Max(hintH, hintMeasured) + 34f;

            var go = UiKit.AddTierButton(panel, (W - 320f) / 2f, y, 320f, 64f, _shell.L("ui.testpilot.start.go"), Close, ButtonTier.Primary);
            if (go.GetComponentInChildren<Text>() is { } label)
            {
                label.alignment = TextAnchor.MiddleCenter; // a lone button without an icon: its word in the middle
            }

            HoldWorld();
        }

        /// <summary>The card owns the controls and holds a singleplayer world while it is read. It is a HUD-level owner:
        /// the HUD stays up behind the card's scrim, so it has come online by the time "Let's go" is pressed (a
        /// screen-level owner hides the HUD, and its boot — panels wiping on one after the other — would start only then,
        /// with the task's own card among the last), and the card the text points at is there to be seen.</summary>
        private void HoldWorld()
        {
            _boot = _shell.CurrentBoot;
            if (_boot == null)
            {
                return; // an edit-mode test builds the card without a world
            }

            _boot.SetHudMenuOwner(this, true);
            _boot.WorldHold.Hold(BlocksBeyondTheStars.Client.WorldHoldArbiter.Playtest, Time.realtimeSinceStartup);
        }

        private void Update()
        {
            // The card is the world's: it goes with it (a join that failed, a leave through another path) — and it makes
            // way for the Tab menu, which a HUD-level owner does not keep shut.
            if (_boot == null || _shell.CurrentBoot != _boot || _boot.ConsoleOpen || InputMap.Down(InputAction.UiCancel))
            {
                Close();
            }
        }

        private void Close() => Destroy(gameObject);

        private void OnDestroy()
        {
            if (_canvas != null)
            {
                Destroy(_canvas.gameObject);
                _canvas = null;
                PlaytestModal.Closed();
            }

            if (_boot != null)
            {
                _boot.WorldHold.Release(BlocksBeyondTheStars.Client.WorldHoldArbiter.Playtest);
                _boot.SetHudMenuOwner(this, false);
                _boot = null;
            }

            if (_instance == this)
            {
                _instance = null;
            }
        }
    }
}
