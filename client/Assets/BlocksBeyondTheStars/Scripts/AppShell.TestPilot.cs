// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Client.Feedback;
using BlocksBeyondTheStars.Client.Playtests;
using BlocksBeyondTheStars.Shared.Playtests;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The shell's part of the Test Pilot page (desktop builds): opening and leaving it — from the main menu, like
    /// Settings from the pause menu over a held world, and with the Test Pilot key straight from the world (#3847) —
    /// keeping the page in step with <see cref="PlaytestHub"/>, showing the "confirmed" dialog in a menu, and putting
    /// the task list over an open editor.
    /// </summary>
    public sealed partial class AppShell
    {
        private GameObject _uiTestPilot;
        private int _testPilotRevision = -1;
        private string _testPilotView;
        private ShellPhase _testPilotReturnPhase = ShellPhase.MainMenu;
        private bool _testPilotResumesGame; // opened with the key: leaving the page goes back into the game, not the pause menu
        private PlaytestEditorPanel _editorPanel;
        private readonly HashSet<string> _editorListFolded = new HashSet<string>(StringComparer.Ordinal);

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

            _testPilotResumesGame = false;
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

        /// <summary>Leaves the page: back into the game when the key opened it, to the pause menu over the world it was
        /// opened from, or to the main menu.</summary>
        public void CloseTestPilot()
        {
            UiTestPilot.OpenTaskId = null;
            if (_testPilotReturnPhase == ShellPhase.InGame && _gameRoot != null)
            {
                Phase = ShellPhase.InGame;
                if (_testPilotResumesGame)
                {
                    CancelQuit(); // lets the world run again and hands the screen back to the game
                }
                else
                {
                    ShowQuitDialog(true);
                }
            }
            else
            {
                Phase = ShellPhase.MainMenu;
            }

            _testPilotResumesGame = false;
        }

        /// <summary>The page is rebuilt on its next frame (something it shows changed outside the hub).</summary>
        public void RebuildTestPilot() => _testPilotRevision = -1;

        /// <summary>Esc / pad B on the page: out of a task's view first, then out of the page. Opened with the key over a
        /// world, the page IS the task's view: one press goes back into the game.</summary>
        private void BackOutOfTestPilot()
        {
            if (!string.IsNullOrEmpty(UiTestPilot.OpenTaskId) && !_testPilotResumesGame)
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

        /// <summary>
        /// #3847: the Test Pilot key (F4 by default) while a task is being worked on. In a world it opens the page on that
        /// task — the world is held as from the pause menu — and the same key (or Esc) goes straight back into the game.
        /// In an editor whose task is being worked on it opens and folds the task's list. It does nothing without a
        /// task, while a text field has the focus, or while a dialog, the chat or a cutscene owns the screen.
        /// </summary>
        private void HandleTestPilotKey(PlaytestHub hub)
        {
            if (hub == null || hub.Tracker.Active == null || !InputMap.Down(InputAction.TestPilot)
                || PlaytestModal.OwnsCancel || UiKit.TextFieldFocused() || InputMap.TextEntryActive)
            {
                return;
            }

            if (Phase == ShellPhase.TestPilot)
            {
                if (_testPilotResumesGame)
                {
                    CloseTestPilot();
                }
            }
            else if (Phase == ShellPhase.InGame)
            {
                var boot = Boot();
                if (!_confirmQuit && boot != null && !PauseMenuBlocked(boot) && !boot.ChatTyping)
                {
                    OpenPauseMenu(); // holds the world and takes the screen — the page then tucks the menu away
                    OpenTestPilot();
                    _testPilotResumesGame = true;
                }
            }
            else if (_editorPanel != null)
            {
                _editorPanel.Toggle();
            }
        }

        /// <summary>Every frame: the page follows the hub, a confirmed report gets its dialog while a menu is up (never
        /// over the running world), and an open editor shows the task being worked on.</summary>
        private void SyncTestPilot()
        {
            var hub = PlaytestHub.Instance;
            HandleTestPilotKey(hub);
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

        /// <summary>True while the page lies over a world (opened from the pause menu or with the key): a test world cannot
        /// be created and no editor opened from there — the world has to be left first.</summary>
        public bool TestPilotOverWorld => CurrentBoot != null;

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

        /// <summary>#3850: opens the editor a task is about straight from the task's view (the main menu's page only —
        /// never over a world). Entering the editor is its "opened" event, so that step is recognised at once.</summary>
        public void OpenPlaytestEditor(string editor)
        {
            if (TestPilotOverWorld || Phase != ShellPhase.TestPilot)
            {
                return;
            }

            UiTestPilot.OpenTaskId = null;
            switch (editor)
            {
                case PlaytestEditorEvents.Ship: OpenShipEditor(); break;
                case PlaytestEditorEvents.Avatar: OpenAvatarEditor(); break;
                case PlaytestEditorEvents.Station: OpenStationEditor(); break;
                case PlaytestEditorEvents.Settlement: OpenSettlementEditor(); break;
                case PlaytestEditorEvents.Texture: OpenTextureEditor(); break;
                case PlaytestEditorEvents.Form: OpenFormEditor(); break;
                case PlaytestEditorEvents.ToolLook: OpenToolLookEditor(); break;
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

        /// <summary>#3849: the tester folded (or opened) the list over an editor. A list folded once stays folded for that
        /// task until the game is restarted — the first entry shows it open, so the task's text is seen at all.</summary>
        public void NoteEditorListFolded(string taskId, bool folded)
        {
            if (string.IsNullOrEmpty(taskId))
            {
                return;
            }

            if (folded)
            {
                _editorListFolded.Add(taskId);
            }
            else
            {
                _editorListFolded.Remove(taskId);
            }
        }

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
                _editorPanel.SetExpanded(!_editorListFolded.Contains(task.Id)); // open on the first entry (#3849)
            }
            else if (!show && _editorPanel != null)
            {
                Destroy(_editorPanel.gameObject);
                _editorPanel = null;
            }
        }
    }
}
