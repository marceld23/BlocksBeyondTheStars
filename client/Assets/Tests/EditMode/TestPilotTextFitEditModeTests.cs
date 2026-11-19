// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BlocksBeyondTheStars.Client;
using BlocksBeyondTheStars.Client.Feedback;
using BlocksBeyondTheStars.Client.Playtests;
using BlocksBeyondTheStars.Shared.Localization;
using BlocksBeyondTheStars.Shared.Playtests;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client.Tests.EditMode
{
    /// <summary>
    /// The Test Pilot screens in Unity itself (#3812), in every game language: the page (empty and with tasks in every
    /// state), a task's view (with a test world, with a world this build cannot create, half done, sent, an editor
    /// task), the report dialog (with and without "send my work"), the "did not work" note, the "confirmed" dialog and
    /// the list over an editor (chip and open). Every <see cref="Text"/> is held to the rules of
    /// <see cref="UiTextFit"/>. The tasks are the repository's own task files, so their German and English texts are
    /// measured too (a task's texts are German + English; every other language shows the English one).
    /// </summary>
    public sealed class TestPilotTextFitEditModeTests
    {
        private static readonly string[] Screens =
        {
            "page-empty", "page", "task-world", "task-world-refused", "task-progress", "task-sent", "task-editor",
            "report", "report-editor", "note", "confirmed", "editor-chip", "editor-list",
        };

        private readonly List<GameObject> _spawned = new();
        private bool _hadEventSystem;
        private Func<string, string> _previousLocalize;

        private static IEnumerable<object[]> ScreensAndLocales()
        {
            foreach (string screen in Screens)
            {
                foreach (GameLocale locale in Enum.GetValues(typeof(GameLocale)))
                {
                    yield return new object[] { screen, locale };
                }
            }
        }

        [SetUp]
        public void SetUp()
        {
            _hadEventSystem = UnityEngine.Object.FindAnyObjectByType<EventSystem>() != null;
            _previousLocalize = UiKit.Localize;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _spawned)
            {
                if (go != null)
                {
                    UnityEngine.Object.DestroyImmediate(go);
                }
            }

            _spawned.Clear();
            UiTestPilot.OpenTaskId = null;
            PlaytestModal.ResetForTest(); // edit mode runs no OnDestroy: the dialogs never said they closed
            UiKit.Localize = _previousLocalize;
            LogAssert.ignoreFailingMessages = false;
            var es = UnityEngine.Object.FindAnyObjectByType<EventSystem>();
            if (!_hadEventSystem && es != null)
            {
                UnityEngine.Object.DestroyImmediate(es.gameObject);
            }
        }

        [TestCaseSource(nameof(ScreensAndLocales))]
        public void EveryText_IsReadableAndFitsItsBox(string screen, GameLocale locale)
        {
            UiTextFit.RequireFontRasterization();
            var shell = NewShell(locale);
            var before = new HashSet<GameObject>(Roots());
            LogAssert.ignoreFailingMessages = true; // scene set-up logs (Destroy in edit mode) are not this test's subject
            Build(screen, shell);
            var texts = Roots().Where(go => !before.Contains(go)).Select(Track)
                .SelectMany(go => go.GetComponentsInChildren<Text>(false)).ToList();
            Assert.IsNotEmpty(texts, $"'{screen}' built no text at all");

            var problems = UiTextFit.Problems(texts, UiKit.EditorMinFontSize);
            Assert.IsEmpty(problems, $"'{screen}' in {locale.Code()}:\n" + string.Join("\n", problems));
        }

        [Test]
        public void TheRepositorysTasks_AreAllOnThePage()
        {
            var shell = NewShell(GameLocale.English);
            var hub = PlaytestHub.CreateForTest(shell, RepositoryCatalog(), planetsKnown: true);
            Assert.GreaterOrEqual(hub.Tracker.Tasks.Count, 3, "the starter catalogue has an in-game task, a test-world task and an editor task");
            Assert.IsTrue(hub.Tracker.Tasks.Any(t => t.World != null), "one task brings a test world");
            Assert.IsTrue(hub.Tracker.Tasks.Any(t => t.Area == PlaytestAreas.Editor), "one task is done in an editor");
        }

        // ---------------------------------------------------------------- building the screens

        private void Build(string screen, AppShell shell)
        {
            bool empty = screen == "page-empty";
            var hub = PlaytestHub.CreateForTest(shell, empty ? new PlaytestCatalog() : RepositoryCatalog(), planetsKnown: screen != "task-world-refused");
            if (empty)
            {
                Track(UiTestPilot.Build(shell));
                return;
            }

            var world = hub.Tracker.Tasks.First(t => t.World != null);
            var editor = hub.Tracker.Tasks.First(t => t.Area == PlaytestAreas.Editor);
            var ingame = hub.Tracker.Tasks.First(t => t.World == null && t.Area != PlaytestAreas.Editor);
            switch (screen)
            {
                case "page":
                    HalfDone(hub, world);
                    Send(hub, ingame, confirm: true);
                    Send(hub, editor, confirm: false);
                    Track(UiTestPilot.Build(shell));
                    break;
                case "task-world":
                case "task-world-refused":
                    Page(shell, hub, world);
                    break;
                case "task-progress":
                    HalfDone(hub, world);
                    Page(shell, hub, world);
                    break;
                case "task-sent":
                    Send(hub, ingame, confirm: true);
                    Page(shell, hub, ingame);
                    break;
                case "task-editor":
                    Page(shell, hub, editor);
                    break;
                case "report":
                    PlaytestReportDialog.Open(shell, world);
                    break;
                case "report-editor":
                    PlaytestReportDialog.Open(shell, editor, () => new FeedbackAttachment());
                    break;
                case "note":
                    PlaytestNotePrompt.Open(shell, _ => { });
                    break;
                case "confirmed":
                    Send(hub, ingame, confirm: true);
                    PlaytestConfirmedDialog.Open(shell, new PlaytestConfirmation
                    {
                        TaskId = ingame.Id,
                        Text = "Thank you — the boost bar was fixed because of your report. It refills by itself now, a little faster than before.",
                        FixedInVersion = "2026.11.23",
                    });
                    break;
                case "editor-chip":
                case "editor-list":
                    HalfDone(hub, editor);
                    EditorPanel(shell, hub, editor, expanded: screen == "editor-list");
                    break;
            }
        }

        private void Page(AppShell shell, PlaytestHub hub, PlaytestTask task)
        {
            hub.Activate(task.Id);
            UiTestPilot.OpenTaskId = task.Id;
            Track(UiTestPilot.Build(shell));
        }

        /// <summary>The task being worked on with one step done, one that did not work (with a note) and the rest open.</summary>
        private static void HalfDone(PlaytestHub hub, PlaytestTask task)
        {
            hub.Activate(task.Id);
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            hub.Progress.SetStep(task, task.Steps[0].Id, PlaytestStepStates.Done, null, now);
            hub.Progress.SetStep(task, task.Steps[1].Id, PlaytestStepStates.Failed,
                "The button did nothing the first time; after a second press it worked.", now);
            foreach (var step in task.Steps.Where(s => s.Check != null && s.Check.Required > 1))
            {
                hub.Progress.CountEvent(task, step.Check.Event, step.Check.Arg, step.Check.Required / 2, now);
            }
        }

        /// <summary>The task with its report sent (the badge is earned) and, if asked, confirmed (the badge is gold).</summary>
        private static void Send(PlaytestHub hub, PlaytestTask task, bool confirm)
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            foreach (var step in task.Steps)
            {
                hub.Progress.SetStep(task, step.Id, PlaytestStepStates.Done, null, now);
            }

            string reportId = "report-" + task.Id;
            hub.Progress.RecordSent(task, reportId, now);
            if (confirm)
            {
                hub.Progress.MarkConfirmed(reportId);
            }
        }

        /// <summary>The list over an editor — built by its Update in the game, by hand here (edit mode runs no Update).</summary>
        private void EditorPanel(AppShell shell, PlaytestHub hub, PlaytestTask task, bool expanded)
        {
            var panel = Track(new GameObject("PlaytestEditorPanelUnderTest")).AddComponent<PlaytestEditorPanel>();
            panel.Shell = shell;
            panel.SetExpanded(expanded);
            typeof(PlaytestEditorPanel).GetMethod("Rebuild", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(panel, new object[] { hub, task });
        }

        /// <summary>Every task file of the repository (<c>data-online/playtests</c>), switched on — a draft is measured
        /// before it goes up, not after.</summary>
        private static PlaytestCatalog RepositoryCatalog()
        {
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "data-online", "playtests"));
            if (!Directory.Exists(dir))
            {
                Assert.Ignore($"task folder not found: {dir}");
            }

            var catalog = new PlaytestCatalog();
            foreach (string file in Directory.GetFiles(dir, "*.json").OrderBy(f => f, StringComparer.Ordinal))
            {
                Assert.IsTrue(PlaytestCatalogJson.TryReadTask(File.ReadAllText(file), out var task), $"{Path.GetFileName(file)} is a task the game can read");
                task.Enabled = true;
                task.MinVersion = string.Empty;
                task.MaxVersion = string.Empty;
                catalog.Tasks.Add(task);
            }

            return catalog;
        }

        /// <summary>An AppShell that never ran Awake, with settings in the locale's language and the locale's table
        /// (English behind it).</summary>
        private AppShell NewShell(GameLocale locale)
        {
            var shell = Track(new GameObject("ShellUnderTest")).AddComponent<AppShell>();
            shell.SetSettingsForTest(new ClientSettings { Language = locale.Code() });
            var localizer = new Localizer(locale, UiTextFit.LocaleTable(locale), UiTextFit.LocaleTable(GameLocale.English));
            typeof(AppShell).GetProperty(nameof(AppShell.Localizer))?.SetValue(shell, localizer);
            UiKit.Localize = shell.L;
            return shell;
        }

        private GameObject Track(GameObject go)
        {
            if (go != null && !_spawned.Contains(go))
            {
                _spawned.Add(go);
            }

            return go;
        }

        private static IEnumerable<GameObject> Roots()
            => UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();
    }
}
