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
    /// The playtest task's list over an open editor (desktop builds): the steps — the ones the editor reports itself
    /// (opened, loaded, saved, exported, submitted) tick by themselves, the others have "Done" / "Did not work" — and
    /// the way to the closing report, with the offer to send the work along. It is open the first time the editor is
    /// entered for a task, so the task's text is seen at all (#3849); folded, it is a chip in the top-right corner with
    /// the task's state ("TEST PILOT 2/4"), which says for a few seconds when the game recognised a step — an editor
    /// has no status line for it — and "Write report" once every step is closed. It belongs to the TASK, not to the
    /// editor: every editor gets the same list, on its own canvas above the editor's. The chip is clicked, and the Test
    /// Pilot key opens and folds the list (#3847). The shell creates it while the task being worked on is about the
    /// open editor (AppShell.TestPilot.cs).
    /// </summary>
    public sealed class PlaytestEditorPanel : MonoBehaviour
    {
        private const float PanelW = 440f, PanelH = 720f, PanelX = 1920f - PanelW - 16f, PanelY = 96f, Pad = 20f;
        private const float TabW = 220f, TabH = 44f, RecognisedSeconds = 4f;

        public AppShell Shell;

        private Canvas _canvas;
        private bool _expanded;
        private int _revision = -1;
        private bool _builtExpanded, _builtRecognised;

        /// <summary>Opens the list or folds it back to its chip — how the shell and a test set the first state. The
        /// tester's own choice goes through <see cref="Toggle"/>, which the shell remembers.</summary>
        public void SetExpanded(bool expanded) => _expanded = expanded;

        /// <summary>Opens the folded list, folds the open one (the chip, the list's own link and the Test Pilot key).</summary>
        public void Toggle()
        {
            _expanded = !_expanded;
            var hub = PlaytestHub.Instance;
            if (Shell != null && hub != null && hub.Tracker.Active != null)
            {
                Shell.NoteEditorListFolded(hub.Tracker.Active.Id, !_expanded);
            }
        }

        /// <summary>True while the chip says that a step was just recognised.</summary>
        private static bool JustRecognised(PlaytestHub hub) => Time.realtimeSinceStartup - hub.RecognisedAt < RecognisedSeconds;

        private void Update()
        {
            var hub = PlaytestHub.Instance;
            if (hub == null || Shell == null || hub.Tracker.Active == null)
            {
                return;
            }

            if (_canvas == null || _revision != hub.Revision || _builtExpanded != _expanded || _builtRecognised != JustRecognised(hub))
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
            _builtRecognised = JustRecognised(hub);
            if (_expanded)
            {
                BuildPanel(hub, task);
            }
            else
            {
                BuildTab(hub, task, _builtRecognised);
            }
        }

        /// <summary>Folded: one chip in the top-right corner of the work area, clicked to open. It sits where the open
        /// list has its heading and where an editor's right card only carries its own heading — a tab further down the
        /// edge lay over the card's controls. It reads the task's count, "Recognised by the game" for a few seconds after
        /// a step closed by itself, and "Write report" (as the one primary button) when no step is open any more.</summary>
        private void BuildTab(PlaytestHub hub, PlaytestTask task, bool recognised)
        {
            int open = hub.Progress.OpenSteps(task);
            int done = task.Steps.Count - open;
            bool ready = open == 0;
            var tab = UiKit.AddButton(_canvas.transform, 1920f - TabW - 28f, PanelY + 14f, TabW, TabH, string.Empty, Toggle);
            UiKit.StyleTier(tab, ready ? ButtonTier.Primary : ButtonTier.Secondary);
            var label = tab.GetComponentInChildren<Text>();
            label.text = ready ? Shell.L("ui.testpilot.report")
                : recognised ? "✔ " + Shell.L("ui.testpilot.step_recognised_chip")
                : Shell.L("ui.testpilot.hud") + "  " + done + "/" + task.Steps.Count;
            if (!ready)
            {
                label.color = recognised ? UiKit.Ok : UiKit.Warn;
            }

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
            UiKit.AddLinkButton(panel, PanelW - Pad - 130f, 20f, 130f, 28f, Shell.L("ui.testpilot.collapse"), Toggle, 15,
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
