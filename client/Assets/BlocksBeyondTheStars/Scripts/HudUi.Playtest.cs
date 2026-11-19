// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client.Playtests;
using BlocksBeyondTheStars.Shared.Playtests;
using TMPro;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The playtest card of <see cref="HudUi"/>: while the player works on a playtest task in a world (the Test Pilot
    /// page), its current step stays on the HUD as a small card in the right column's context stack — under the
    /// objectives, like the pinned recipe — with the count of a step the game recognises. It is the player's own
    /// reminder; nothing is asked of the server for it. The text is rebuilt only when the hub's state moved.
    /// </summary>
    public sealed partial class HudUi
    {
        private GameObject _playtestPanel;
        private TMP_Text _playtestBody;
        private int _playtestStamp = int.MinValue;

        private void BuildPlaytestCard(Transform root)
        {
            _playtestPanel = Panel(root, W - 260f, 460, 250, 92).gameObject;
            UiText.Add(_playtestPanel.transform, 10, 6, 230, 18, Game?.Localizer?.Get("ui.testpilot.hud") ?? "TEST PILOT", 12, UiKit.Warn,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            _playtestBody = UiText.Add(_playtestPanel.transform, 10, 26, 230, 60, string.Empty, 13, UiKit.TextCol, TextAnchor.UpperLeft);
            UiText.Wrap(_playtestBody, truncate: true);
            _playtestPanel.SetActive(false);
        }

        private void RefreshPlaytestCard()
        {
            var hub = PlaytestHub.Instance;
            var task = hub != null ? hub.Tracker.Active : null;
            var step = task != null && task.Area != PlaytestAreas.Editor ? hub.Tracker.CurrentStep : null;
            bool show = step != null;
            bool appeared = show && !_playtestPanel.activeSelf;
            if (_playtestPanel.activeSelf != show)
            {
                _playtestPanel.SetActive(show);
            }

            if (!show)
            {
                return;
            }

            StackContextCard(_playtestPanel, appeared);
            if (hub.Revision == _playtestStamp && !appeared)
            {
                return;
            }

            _playtestStamp = hub.Revision;
            string lang = Settings != null ? Settings.Language : "en";
            string text = step.Text.For(lang);
            if (step.Check != null && step.Check.Required > 1)
            {
                string count = string.Format(Game?.Localizer?.Get("ui.testpilot.step_count") ?? "{0} / {1}",
                    hub.Progress.StepOf(task, step.Id).Count, step.Check.Required);
                text += "\n" + count;
            }

            _playtestBody.richText = false; // a task's text comes from the fetched catalogue — shown verbatim
            _playtestBody.text = text;
        }
    }
}
