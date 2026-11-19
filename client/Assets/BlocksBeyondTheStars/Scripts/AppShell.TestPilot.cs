// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Client.Feedback;
using BlocksBeyondTheStars.Client.Playtests;
using BlocksBeyondTheStars.Shared.Playtests;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The shell's part of the Test Pilot page (desktop builds): opening and leaving it — from the main menu and, like
    /// Settings, from the pause menu over a held world — keeping the page in step with <see cref="PlaytestHub"/>,
    /// showing the "confirmed" dialog in a menu, and putting the task list over an open editor.
    /// </summary>
    public sealed partial class AppShell
    {
        private GameObject _uiTestPilot;
        private int _testPilotRevision = -1;
        private string _testPilotView;
        private ShellPhase _testPilotReturnPhase = ShellPhase.MainMenu;
        private PlaytestEditorPanel _editorPanel;

        /// <summary>True while the player is in a world this client started itself (singleplayer or hosting).</summary>
        public bool PlayingLocalWorld => _hostLocal && CurrentBoot != null;

        /// <summary>One more row in the pause menu when the Test Pilot page exists in this build.</summary>
        private static int PauseTestPilotRowCount => PlaytestHub.Instance != null ? 1 : 0;

        /// <summary>Opens the Test Pilot page — from the main menu, or from the pause menu over the held world.</summary>
        public void OpenTestPilot()
        {
            var hub = PlaytestHub.Instance;
            if (hub == null)
            {
                return;
            }

            _testPilotReturnPhase = Phase == ShellPhase.InGame ? ShellPhase.InGame : ShellPhase.MainMenu;
            if (Phase == ShellPhase.InGame)
            {
                ShowQuitDialog(false); // tuck the pause menu away while the page is up — the world stays held
            }

            hub.Progress.MarkVisited(); // from now on the catalogue may also be fetched when the game starts
            hub.Refresh(playerAsked: true);
            UiTestPilot.OpenTaskId = hub.Tracker.Active != null ? hub.Tracker.Active.Id : null; // go on where the player stopped
            Phase = ShellPhase.TestPilot;
        }

        /// <summary>Leaves the page: back to the pause menu over the world it was opened from, or to the main menu.</summary>
        public void CloseTestPilot()
        {
            UiTestPilot.OpenTaskId = null;
            if (_testPilotReturnPhase == ShellPhase.InGame && _gameRoot != null)
            {
                Phase = ShellPhase.InGame;
                ShowQuitDialog(true);
            }
            else
            {
                Phase = ShellPhase.MainMenu;
            }
        }

        /// <summary>The page is rebuilt on its next frame (something it shows changed outside the hub).</summary>
        public void RebuildTestPilot() => _testPilotRevision = -1;

        /// <summary>Esc / pad B on the page: out of a task's view first, then out of the page.</summary>
        private void BackOutOfTestPilot()
        {
            if (!string.IsNullOrEmpty(UiTestPilot.OpenTaskId))
            {
                UiTestPilot.OpenTaskId = null;
                RebuildTestPilot();
            }
            else
            {
                CloseTestPilot();
            }
        }

        /// <summary>The pause menu's "Test Pilot" row; returns the next row's y.</summary>
        private float AddPauseTestPilotRow(Transform panel, float y)
        {
            if (PlaytestHub.Instance == null)
            {
                return y;
            }

            UiKit.AddTierButton(panel, PauseRowX, y, PauseRowW, 56f, L("ui.testpilot.title"), OpenTestPilot, ButtonTier.Secondary, "btn_testpilot");
            return y + PauseRow;
        }

        /// <summary>Every frame: the page follows the hub, a confirmed report gets its dialog while a menu is up (never
        /// over the running world), and an open editor shows the task being worked on.</summary>
        private void SyncTestPilot()
        {
            var hub = PlaytestHub.Instance;
            if (Phase != ShellPhase.TestPilot || hub == null)
            {
                if (_uiTestPilot != null)
                {
                    Destroy(_uiTestPilot);
                    _uiTestPilot = null;
                }
            }
            else if (_uiTestPilot == null || _testPilotRevision != hub.Revision || _testPilotView != UiTestPilot.OpenTaskId)
            {
                bool fresh = _uiTestPilot == null;
                if (!fresh)
                {
                    Destroy(_uiTestPilot);
                }

                _uiTestPilot = UiTestPilot.Build(this);
                _testPilotRevision = hub.Revision;
                _testPilotView = UiTestPilot.OpenTaskId;
                if (fresh)
                {
                    UiKit.BootScreen(_uiTestPilot); // the entrance plays once, not on every tick of a step
                }
            }

            if (hub != null && hub.Confirmations.Count > 0 && !PlaytestModal.AnyOpen
                && (Phase == ShellPhase.MainMenu || Phase == ShellPhase.TestPilot))
            {
                PlaytestConfirmedDialog.Open(this, hub.Confirmations.Dequeue());
            }

            SyncEditorPanel(hub);
        }

        /// <summary>The editor a shell phase is, in the catalogue's words — or null when the phase is no player editor.</summary>
        public string PlaytestEditorOfPhase()
        {
            switch (Phase)
            {
                case ShellPhase.ShipEditor: return PlaytestEditorEvents.Ship;
                case ShellPhase.AvatarEditor: return PlaytestEditorEvents.Avatar;
                case ShellPhase.StructureEditor: return StructureEditorPlaytestKind;
                case ShellPhase.TextureEditor: return PlaytestEditorEvents.Texture;
                case ShellPhase.FormEditor: return PlaytestEditorEvents.Form;
                case ShellPhase.ToolLookEditor: return PlaytestEditorEvents.ToolLook;
                default: return null;
            }
        }

        /// <summary>The open editor's "send my work along" source for the report dialog, or null when no editor is open
        /// (the dialog then shows no such box).</summary>
        public Func<FeedbackAttachment> PlaytestWorkOfOpenEditor()
        {
            var source = _editorRoot != null ? _editorRoot.GetComponentInChildren<IPlaytestWorkSource>() : null;
            return source != null ? source.WorkForPlaytest : (Func<FeedbackAttachment>)null;
        }

        /// <summary>Which of the two structure editors is open (set when one opens).</summary>
        public string StructureEditorPlaytestKind { get; private set; } = PlaytestEditorEvents.Station;

        private ShellPhase _playtestPhaseSeen = ShellPhase.Splash;

        /// <summary>The task list over an open editor: there while the task being worked on is about THIS editor. Entering
        /// an editor is also its "opened" event — reported here, once per entry, for all seven alike.</summary>
        private void SyncEditorPanel(PlaytestHub hub)
        {
            string editor = hub != null ? PlaytestEditorOfPhase() : null;
            if (_playtestPhaseSeen != Phase)
            {
                _playtestPhaseSeen = Phase;
                EditorPlaytest.Opened(editor);
            }

            var task = editor != null ? hub.Tracker.Active : null;
            bool show = task != null && task.Area == PlaytestAreas.Editor && task.Editor == editor;
            if (show && _editorPanel == null)
            {
                _editorPanel = new GameObject("PlaytestEditorPanel").AddComponent<PlaytestEditorPanel>();
                _editorPanel.Shell = this;
            }
            else if (!show && _editorPanel != null)
            {
                Destroy(_editorPanel.gameObject);
                _editorPanel = null;
            }
        }
    }
}
