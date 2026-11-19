// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections;
using System.IO;
using BlocksBeyondTheStars.Client.Playtests;
using BlocksBeyondTheStars.Shared.Playtests;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// <c>-captureTestPilot</c>: photographs the Test Pilot page's screens in a built player and quits — the way to
    /// look at them without playing (docs/developer/PLAYTEST_TASKS.md). It needs no world: it opens the "Join in"
    /// dialog, the page, a task's view, then works a ship-editor task in the Ship Editor (the tab, the list, the report
    /// dialog with "send my work along") and shows the "confirmed" dialog. The tasks are whatever the inbox serves this
    /// build, so point a development build at an inbox of your own (<c>BBS_REPORTS_DEV_URL</c> +
    /// <c>BBS_REPORTS_DEV_KEY</c>) and run it with a data folder of its own (<c>-dataDir &lt;folder&gt;</c>): the run
    /// ticks steps off in that install's progress file. Nothing is sent.
    /// Writes <c>testpilot/NN-&lt;screen&gt;.png</c> under <c>-shotOut</c>; <c>-lang de|en</c> picks the language.
    /// </summary>
    public sealed partial class ScreenshotDirector
    {
        private const float TestPilotFetchTimeout = 20f; // the catalogue fetch, off the main thread
        private const float TestPilotSettle = 1.5f;      // a page or dialog has built and its entrance has played

        private bool _testPilot;
        private string _testPilotDir;

        /// <summary>The <c>-captureTestPilot</c> switch; never on in the browser build (the page does not exist there).</summary>
        private static bool TestPilotCaptureRequested()
        {
#if UNITY_WEBGL
            return false;
#else
            return Array.Exists(Environment.GetCommandLineArgs(), a => string.Equals(a, "-captureTestPilot", StringComparison.OrdinalIgnoreCase));
#endif
        }

        /// <summary>The whole Test Pilot run. Quits.</summary>
        private IEnumerator CaptureTestPilot(AppShell shell)
        {
            // The run ticks steps off and books a confirmed report in the progress file — never in a player's own
            // install: it only starts with a data folder of its own.
            if (!Array.Exists(Environment.GetCommandLineArgs(), a => string.Equals(a, "-dataDir", StringComparison.OrdinalIgnoreCase)))
            {
                Debug.LogError("[Capture] -captureTestPilot needs -dataDir <folder>: the run writes playtest progress and must not touch a real install.");
                Quit(1);
                yield break;
            }

            bool AtMenu() => shell.ContentReady && (shell.Phase == ShellPhase.MainMenu || shell.Phase == ShellPhase.Intro);
            yield return WaitUntil(AtMenu, EditorMenuTimeout);
            var hub = PlaytestHub.Instance;
            if (!AtMenu() || shell.Settings == null || hub == null)
            {
                Debug.LogError($"[Capture] The Test Pilot run cannot start (phase {shell.Phase}, hub {(hub != null ? "there" : "missing")}).");
                Quit(1);
                yield break;
            }

            shell.Settings.Language = _lang;
            shell.LoadLocalizer();
            shell.Settings.LastSeenVersion = AppShell.Version; // no "What's new?" over the menu
            shell.GoTo(ShellPhase.MainMenu);
            _testPilotDir = Path.Combine(ResolveOutDir(), "testpilot");
            Directory.CreateDirectory(_testPilotDir);
            Debug.Log($"[Capture] Test Pilot: lang={_lang} out={_testPilotDir} inbox={hub.IngestEndpoint} configured={hub.Configured}");

            // The catalogue first, so the menu already shows its "new" mark.
            hub.Refresh(playerAsked: true);
            yield return WaitUntil(() => !hub.Fetching, TestPilotFetchTimeout);
            shell.GoTo(ShellPhase.Editors); // leave and re-enter the menu: it is built once per entry
            yield return null;
            shell.GoTo(ShellPhase.MainMenu);
            yield return new WaitForSecondsRealtime(MenuSettle);
            if (ClickButtonLabelled(shell.L("ui.menu.contribute")))
            {
                yield return TestPilotShot("00-join-in");
            }

            shell.OpenTestPilot();
            yield return WaitUntil(() => !hub.Fetching, TestPilotFetchTimeout);
            yield return TestPilotShot("01-page");
            if (hub.Tracker.Tasks.Count == 0)
            {
                Debug.LogWarning("[Capture] The inbox served no task for this build — only the empty page was captured.");
                Quit(0);
                yield break;
            }

            yield return TaskViewShots(shell, hub);
            yield return EditorTaskShots(shell, hub);
            yield return TestWorldShots(shell, hub); // last: the run ends inside the world
            Debug.Log("[Capture] Done (Test Pilot).");
            Quit(0);
        }

        /// <summary>A task's test world — the world this install creates for it, started the way the task's button starts
        /// it: the start card once the start commands have run, the HUD with the task's card, the pause menu with the
        /// page's row, and the task's view over the held world (what the Test Pilot key opens). Needs the bundled server
        /// (a full build); without a task that brings a world it is skipped.</summary>
        private IEnumerator TestWorldShots(AppShell shell, PlaytestHub hub)
        {
            PlaytestTask task = null;
            foreach (var candidate in hub.Tracker.Tasks)
            {
                var status = hub.Progress.StatusOf(candidate);
                if (hub.WorldRefusal(candidate) == PlaytestWorldRefusal.None
                    && (status == PlaytestTaskStatus.New || status == PlaytestTaskStatus.InProgress))
                {
                    task = candidate;
                    break;
                }
            }

            if (task == null)
            {
                Debug.LogWarning("[Capture] No task with a test world this build can create — the world screens were skipped.");
                yield break;
            }

            ClickButtonLabelled(shell.L("ui.testpilot.ok")); // the "confirmed" dialog of the step before
            yield return null;
            if (!hub.StartTestWorld(task))
            {
                Debug.LogWarning($"[Capture] The test world of {task.Id} was refused.");
                yield break;
            }

            yield return WaitForPhase(shell, ShellPhase.InGame, WorldLoadTimeout);
            var boot = shell.CurrentBoot;
            if (boot == null || boot.Network == null)
            {
                Debug.LogError("[Capture] The test world did not start (bundled server missing?).");
                yield break;
            }

            yield return WaitUntil(() => boot.WorldReady, WorldLoadTimeout);
            var overlay = FindAnyObjectByType<WorldLoadingOverlay>();
            yield return WaitUntil(() => overlay == null || !overlay.VeilActive, WorldLoadTimeout);
            yield return new WaitForSecondsRealtime(ChunkSettle + 6f); // the start commands run a few seconds after the world is up
            var entry = hub.Progress.Get(task.Id);
            Debug.Log($"[Capture] Test world '{shell.Settings.LastWorld}': server reports events={hub.ServerReportsEvents}, "
                      + $"start commands ran={(entry != null && entry.CommandsRan)}, open steps={hub.Progress.OpenSteps(task)}");
            yield return WorldScreenShots(shell);
        }

        /// <summary>The screens of a test world that is up: the start card, the HUD, the pause menu, the task's view.</summary>
        private IEnumerator WorldScreenShots(AppShell shell)
        {
            // #3848: the task introduces itself once the start commands have run.
            yield return WaitUntil(() => PlaytestStartCard.IsOpen, 10f);
            if (PlaytestStartCard.IsOpen)
            {
                yield return TestPilotShot("12-world-start-card");
                ClickButtonLabelled(shell.L("ui.testpilot.start.go"));
                yield return new WaitForSecondsRealtime(PoseSettle);
            }
            else
            {
                Debug.LogWarning("[Capture] The start card did not open in the test world.");
            }

            yield return Capture(Path.Combine(_testPilotDir, "13-world-hud.png"));

            shell.OpenPauseMenu();
            yield return new WaitForSecondsRealtime(PoseSettle);
            yield return Capture(Path.Combine(_testPilotDir, "14-world-pause-menu.png"));

            shell.OpenTestPilot(); // the task's view over the held world — what the Test Pilot key opens (#3847)
            yield return TestPilotShot("15-world-task");
        }

        /// <summary>A task's view as a player finds it, then with one step done and one that did not work.</summary>
        private IEnumerator TaskViewShots(AppShell shell, PlaytestHub hub)
        {
            PlaytestTask task = null;
            foreach (var candidate in hub.Tracker.Tasks)
            {
                if (candidate.Area == PlaytestAreas.Ingame && (task == null || (task.World == null && candidate.World != null)))
                {
                    task = candidate; // a task with a test world shows the most
                }
            }

            task ??= hub.Tracker.Tasks[0];
            UiTestPilot.Open(task.Id);
            shell.RebuildTestPilot();
            yield return TestPilotShot("02-task");

            int ticked = 0;
            foreach (var step in task.Steps)
            {
                if (step.Check == null && ticked < 2)
                {
                    bool failed = ticked == 1;
                    hub.SetStep(task, step.Id, failed ? PlaytestStepStates.Failed : PlaytestStepStates.Done,
                        failed ? (_lang == "de" ? "Hier kam ich nicht weiter." : "I got stuck here.") : null);
                    ticked++;
                }
            }

            yield return TestPilotShot("03-task-in-progress");
            UiTestPilot.OpenTaskId = null;
            shell.RebuildTestPilot();
            yield return TestPilotShot("04-page-in-progress");
        }

        /// <summary>A ship-editor task worked on in the Ship Editor: the tab, the list, the report dialog, "confirmed".</summary>
        private IEnumerator EditorTaskShots(AppShell shell, PlaytestHub hub)
        {
            var task = FirstShipEditorTask(hub);
            if (task == null || !hub.Activate(task.Id))
            {
                Debug.LogWarning("[Capture] No ship-editor task to work on — the editor screens were skipped.");
                yield break;
            }

            shell.CloseTestPilot();
            shell.OpenShipEditor();
            yield return WaitFrames(2); // Start builds the room, the palette and the panels
            var editor = FindAnyObjectByType<ShipEditor>();
            var ship = SampleShip(shell.Content);
            if (editor != null && ship != null)
            {
                editor.CaptureLoadBuiltIn(ship); // something on the bench, so "send my work along" has work to send
            }

            yield return new WaitForSecondsRealtime(EditorSettle);
            var panel = FindAnyObjectByType<PlaytestEditorPanel>();
            if (panel != null)
            {
                panel.SetExpanded(false); // the list is open on the first entry (#3849) — the chip first, as the names say
            }

            yield return TestPilotShot("05-editor-tab");
            if (panel != null)
            {
                panel.SetExpanded(true);
            }

            yield return TestPilotShot("06-editor-list");

            // Close every step the way a tester would: by hand where the game cannot notice, by the editor's own
            // signal where it can.
            foreach (var step in task.Steps)
            {
                if (step.Check == null)
                {
                    hub.SetStep(task, step.Id, PlaytestStepStates.Done, null);
                }
            }

            EditorPlaytest.Saved(EditorPlaytest.Ship);
            yield return TestPilotShot("07-editor-ready");
            if (hub.Progress.OpenSteps(task) == 0)
            {
                PlaytestReportDialog.Open(shell, task, shell.PlaytestWorkOfOpenEditor());
                yield return TestPilotShot("08-report");
            }
            else
            {
                Debug.LogWarning($"[Capture] {hub.Progress.OpenSteps(task)} step(s) of {task.Id} stayed open — no report dialog.");
            }

            yield return ConfirmedShots(shell, hub, task);
        }

        /// <summary>What a confirmed report looks like: the report counts as sent and confirmed in this install's progress
        /// file (nothing goes to the inbox), then the page with its gold badge, the task's read-only view and the dialog
        /// the game shows for it.</summary>
        private IEnumerator ConfirmedShots(AppShell shell, PlaytestHub hub, PlaytestTask task)
        {
            PlaytestReportDialog.CloseOpen();
            const string captureReport = "capture-run";
            hub.Progress.RecordSent(task, captureReport, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            hub.Progress.MarkConfirmed(captureReport);
            hub.Deactivate(); // a sent task is no longer the one being worked on
            shell.CloseShipEditor();
            shell.OpenTestPilot();
            shell.RebuildTestPilot();
            yield return TestPilotShot("09-page-confirmed");
            UiTestPilot.Open(task.Id); // its view is read-only now
            shell.RebuildTestPilot();
            yield return TestPilotShot("10-task-confirmed");
            UiTestPilot.OpenTaskId = null;
            shell.RebuildTestPilot();
            PlaytestConfirmedDialog.Open(shell, new PlaytestConfirmation
            {
                TaskId = task.Id,
                Text = task.Thanks.For(_lang),
                FixedInVersion = AppShell.Version,
            });
            yield return TestPilotShot("11-confirmed");
        }

        /// <summary>The first task of the catalogue that is worked on in the Ship Editor, or null.</summary>
        private static PlaytestTask FirstShipEditorTask(PlaytestHub hub)
        {
            foreach (var candidate in hub.Tracker.Tasks)
            {
                if (candidate.Area == PlaytestAreas.Editor && candidate.Editor == EditorPlaytest.Ship)
                {
                    return candidate;
                }
            }

            return null;
        }

        /// <summary>Clicks the first visible button showing <paramref name="label"/>; false when there is none.</summary>
        private static bool ClickButtonLabelled(string label)
        {
            foreach (var button in FindObjectsByType<Button>(FindObjectsSortMode.None))
            {
                var text = button.GetComponentInChildren<Text>();
                if (text != null && text.text == label && button.isActiveAndEnabled)
                {
                    button.onClick.Invoke();
                    return true;
                }
            }

            return false;
        }

        /// <summary>Lets the screen settle, then writes <c>testpilot/&lt;name&gt;.png</c>.</summary>
        private IEnumerator TestPilotShot(string name)
        {
            yield return new WaitForSecondsRealtime(TestPilotSettle);
            yield return Capture(Path.Combine(_testPilotDir, name + ".png"));
        }
    }
}
