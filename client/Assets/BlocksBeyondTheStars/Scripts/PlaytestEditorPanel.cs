// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client.Playtests;
using BlocksBeyondTheStars.Shared.Playtests;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The playtest task's list over an open editor (desktop builds): a narrow tab on the right edge with the task's
    /// state ("TEST PILOT 2/4"), which opens into the steps — the ones the editor reports itself (opened, loaded, saved,
    /// exported, submitted) tick by themselves, the others have "Done" / "Did not work" — and the way to the closing
    /// report, with the offer to send the work along. It belongs to the TASK, not to the editor: every editor gets the
    /// same list, on its own canvas above the editor's, and no editor key is taken (the tab is clicked). The shell
    /// creates it while the task being worked on is about the open editor (AppShell.TestPilot.cs).
    /// </summary>
    public sealed class PlaytestEditorPanel : MonoBehaviour
    {
        private const float PanelW = 440f, PanelH = 720f, PanelX = 1920f - PanelW - 16f, PanelY = 96f, Pad = 20f;
        private const float TabW = 220f, TabH = 44f;

        public AppShell Shell;

        private Canvas _canvas;
        private bool _expanded;
        private int _revision = -1;
        private bool _builtExpanded;

        /// <summary>Opens the list or folds it back to its tab (the tab and the list's own button do the same).</summary>
        public void SetExpanded(bool expanded) => _expanded = expanded;

        private void Update()
        {
            var hub = PlaytestHub.Instance;
            if (hub == null || Shell == null || hub.Tracker.Active == null)
            {
                return;
            }

            if (_canvas == null || _revision != hub.Revision || _builtExpanded != _expanded)
            {
                Rebuild(hub, hub.Tracker.Active);
            }
        }

        private void Rebuild(PlaytestHub hub, PlaytestTask task)
        {
            if (_canvas != null)
            {
                Destroy(_canvas.gameObject);
            }

            _canvas = UiKit.CreateCanvas("PlaytestEditorPanelUI");
            _canvas.sortingOrder = 40; // above the editor's own canvas, below its dialogs (70) and the report dialog (75)
            _revision = hub.Revision;
            _builtExpanded = _expanded;
            if (_expanded)
            {
                BuildPanel(hub, task);
            }
            else
            {
                BuildTab(hub, task);
            }
        }

        /// <summary>Collapsed: one chip with the state in the top-right corner of the work area, clicked to open. It sits
        /// where the open list has its heading and where an editor's right card only carries its own heading — a tab
        /// further down the edge lay over the card's controls.</summary>
        private void BuildTab(PlaytestHub hub, PlaytestTask task)
        {
            int done = task.Steps.Count - hub.Progress.OpenSteps(task);
            var tab = UiKit.AddButton(_canvas.transform, 1920f - TabW - 28f, PanelY + 14f, TabW, TabH, string.Empty, () => _expanded = true);
            UiKit.StyleTier(tab, ButtonTier.Secondary);
            var label = tab.GetComponentInChildren<Text>();
            label.text = Shell.L("ui.testpilot.hud") + "  " + done + "/" + task.Steps.Count;
            label.color = UiKit.Warn;
            label.fontSize = 17;
            var rt = label.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(TabW - 16f, TabH - 8f);
            label.alignment = TextAnchor.MiddleCenter;
            UiKit.FitLabelSingleLine(label, TabW - 16f, 12, 17);
        }

        /// <summary>Expanded: the task, its steps and the report button.</summary>
        private void BuildPanel(PlaytestHub hub, PlaytestTask task)
        {
            string lang = Shell.Settings.Language;
            // The list lies over the editor's own right card: a genuinely opaque backing (a card lets a quarter of
            // what is behind it through on a build without the holo shader), and it takes the clicks itself.
            var panel = UiKit.AddDialogPanel(_canvas.transform, PanelX, PanelY, PanelW, PanelH);
            if (panel.GetComponent<Image>() is { } backing)
            {
                backing.raycastTarget = true;
            }

            float w = PanelW - (2f * Pad);
            UiKit.AddIcon(panel, Pad, 14f, 40f, "btn_testpilot");
            UiKit.AddKicker(panel, Pad + 50f, 22f, w - 190f, Shell.L("ui.testpilot.hud"), UiKit.Warn);
            UiKit.AddLinkButton(panel, PanelW - Pad - 130f, 20f, 130f, 28f, Shell.L("ui.testpilot.collapse"), () => _expanded = false, 15,
                TextAnchor.MiddleRight);
            var title = UiKit.AddText(panel, Pad, 62f, w, 30f, task.Title.For(lang), 19, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.FitLabelSingleLine(title, w, 13, 19);

            const float viewTop = 102f, viewH = PanelH - viewTop - 140f;
            var content = UiKit.AddScrollViewport(panel, "Steps", Pad, viewTop, w, viewH, out var scroll);
            float y = 0f;
            for (int i = 0; i < task.Steps.Count; i++)
            {
                y += PlaytestStepRow.Add(content, Shell, hub, task, task.Steps[i], i + 1, y, w - 16f, readOnly: false, compact: true) + 6f;
            }

            content.sizeDelta = new Vector2(0f, Mathf.Max(viewH, y));
            if (y > viewH)
            {
                UiKit.AddVerticalScrollbar(panel, scroll, PanelW - Pad - 8f, viewTop, 8f, viewH);
            }

            int open = hub.Progress.OpenSteps(task);
            string openText = open == 0 ? Shell.L("ui.testpilot.open_none")
                : open == 1 ? Shell.L("ui.testpilot.open_one")
                : string.Format(Shell.L("ui.testpilot.open_many"), open);
            var note = UiKit.AddText(panel, Pad, PanelH - 126f, w, 44f, openText, 14, UiKit.CyanDim, TextAnchor.LowerLeft);
            note.horizontalOverflow = HorizontalWrapMode.Wrap;
            var report = UiKit.AddTierButton(panel, Pad, PanelH - 74f, w, 54f, Shell.L("ui.testpilot.report"), () =>
            {
                if (hub.Progress.OpenSteps(task) == 0)
                {
                    PlaytestReportDialog.Open(Shell, task, Shell.PlaytestWorkOfOpenEditor());
                }
            }, ButtonTier.Primary, "btn_feedback");
            report.interactable = open == 0;
        }

        private void OnDestroy()
        {
            if (_canvas != null)
            {
                Destroy(_canvas.gameObject);
            }
        }
    }
}
