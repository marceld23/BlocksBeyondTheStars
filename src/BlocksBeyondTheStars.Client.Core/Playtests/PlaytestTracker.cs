// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Playtests;
using BlocksBeyondTheStars.Shared.Story;

namespace BlocksBeyondTheStars.Client.Playtests
{
    /// <summary>
    /// Where the menu editors (and the client's own UI moments) report what happened, without knowing who listens:
    /// <c>PlaytestSignals.Raise(PlaytestEditorEvents.Saved, PlaytestEditorEvents.Ship)</c>. The editors run in the menu
    /// without a server, so their events never reach the server's event bus — this hub is their bus. Raising with no
    /// listener costs one null check.
    /// </summary>
    public static class PlaytestSignals
    {
        /// <summary>Raised on the caller's thread (the Unity main thread): key, argument, amount.</summary>
        public static event Action<string, string, int>? Raised;

        /// <summary>Reports that <paramref name="key"/> happened with <paramref name="arg"/>, <paramref name="amount"/>
        /// times.</summary>
        public static void Raise(string key, string arg = "", int amount = 1)
        {
            if (!string.IsNullOrEmpty(key) && amount > 0)
            {
                Raised?.Invoke(key, arg ?? string.Empty, amount);
            }
        }
    }

    /// <summary>
    /// The playtest state the client's screens read: the catalogue's tasks that fit this build, the task being worked
    /// on, and the bridge from events to ticked steps. Pure logic over <see cref="PlaytestProgress"/> — no Unity, no
    /// network — so the page, the HUD line and the editor list all show the same thing and the whole flow is tested
    /// headless.
    /// </summary>
    public sealed class PlaytestTracker
    {
        /// <summary>How many event patterns the client may ask a server to watch (the server's own cap).</summary>
        public const int MaxWatchPatterns = 16;

        private readonly PlaytestProgress _progress;
        private List<PlaytestTask> _tasks = new List<PlaytestTask>();

        public PlaytestTracker(PlaytestProgress progress)
        {
            _progress = progress ?? throw new ArgumentNullException(nameof(progress));
        }

        /// <summary>A step closed because the game recognised it (the task, the step) — the "step recognised" line.</summary>
        public event Action<PlaytestTask, PlaytestStep>? StepRecognised;

        /// <summary>This install's progress file.</summary>
        public PlaytestProgress Progress => _progress;

        /// <summary>The tasks shown to this build, in the catalogue's order.</summary>
        public IReadOnlyList<PlaytestTask> Tasks => _tasks;

        /// <summary>
        /// Takes a fetched (or cached) catalogue: keeps the tasks that are switched on and whose version window holds
        /// <paramref name="gameVersion"/>. A catalogue that is switched off shows nothing. The task being worked on is
        /// dropped when it is no longer among them.
        /// </summary>
        public void SetCatalog(PlaytestCatalog catalog, string gameVersion)
        {
            var visible = new List<PlaytestTask>();
            if (catalog != null && catalog.Enabled)
            {
                foreach (var task in catalog.Tasks)
                {
                    if (task.Enabled && PlaytestVersion.InRange(gameVersion, task.MinVersion, task.MaxVersion))
                    {
                        visible.Add(task);
                    }
                }
            }

            _tasks = visible;
            if (_progress.ActiveTaskId.Length > 0 && Find(_progress.ActiveTaskId) is null)
            {
                _progress.SetActive(string.Empty);
            }
        }

        /// <summary>The shown task with this id, or null.</summary>
        public PlaytestTask? Find(string? taskId)
        {
            foreach (var task in _tasks)
            {
                if (string.Equals(task.Id, taskId, StringComparison.Ordinal))
                {
                    return task;
                }
            }

            return null;
        }

        /// <summary>The task being worked on, or null.</summary>
        public PlaytestTask? Active => Find(_progress.ActiveTaskId);

        /// <summary>The first open step of the task being worked on — what the HUD line shows — or null.</summary>
        public PlaytestStep? CurrentStep
        {
            get
            {
                if (Active is not { } task)
                {
                    return null;
                }

                foreach (var step in task.Steps)
                {
                    if (_progress.StepOf(task, step.Id).State == PlaytestStepStates.Open)
                    {
                        return step;
                    }
                }

                return null;
            }
        }

        /// <summary>Starts (or goes on with) a task; a finished task that asks for a re-test is opened again. False
        /// when the task is not shown to this build or its report is sent already.</summary>
        public bool Activate(string taskId, long nowUnix)
        {
            if (Find(taskId) is not { } task)
            {
                return false;
            }

            var status = _progress.StatusOf(task);
            if (status == PlaytestTaskStatus.Submitted || status == PlaytestTaskStatus.Confirmed)
            {
                return false;
            }

            if (status == PlaytestTaskStatus.Retest && _progress.Get(task.Id) is { } entry && entry.Rev <= entry.SentRev)
            {
                _progress.BeginRetest(task, nowUnix);
            }

            _progress.SetActive(task.Id);
            return true;
        }

        /// <summary>Puts the task being worked on aside (its ticks stay).</summary>
        public void Deactivate() => _progress.SetActive(string.Empty);

        /// <summary>How many shown tasks are new for this install (or ask for a re-test) — the menu entry's mark.</summary>
        public int NewCount
        {
            get
            {
                int count = 0;
                foreach (var task in _tasks)
                {
                    var status = _progress.StatusOf(task);
                    if (status == PlaytestTaskStatus.New || status == PlaytestTaskStatus.Retest)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        /// <summary>
        /// The server game events the open steps of the task being worked on wait for — what the client asks a server
        /// to report back. UI moments and editor events are the client's own and are never asked for. At most
        /// <see cref="MaxWatchPatterns"/>, without doubles.
        /// </summary>
        public IReadOnlyList<string> WatchPatterns()
        {
            var patterns = new List<string>();
            if (Active is not { } task)
            {
                return patterns;
            }

            foreach (var step in task.Steps)
            {
                if (step.Check is not { } check || patterns.Count >= MaxWatchPatterns
                    || GameEventKeys.IsUiEvent(check.Event) || PlaytestEditorEvents.IsEditorEvent(check.Event)
                    || _progress.StepOf(task, step.Id).State != PlaytestStepStates.Open || patterns.Contains(check.Event))
                {
                    continue;
                }

                patterns.Add(check.Event);
            }

            return patterns;
        }

        /// <summary>
        /// An event happened — from the server (a watched game event), from the client's UI or from an editor. It counts
        /// for the task being worked on; every step it closes is announced through <see cref="StepRecognised"/>.
        /// Returns how many steps closed.
        /// </summary>
        public int OnEvent(string key, string arg, int amount, long nowUnix)
        {
            if (Active is not { } task)
            {
                return 0;
            }

            var closed = _progress.CountEvent(task, key, arg, amount, nowUnix);
            foreach (string stepId in closed)
            {
                foreach (var step in task.Steps)
                {
                    if (step.Id == stepId)
                    {
                        StepRecognised?.Invoke(task, step);
                    }
                }
            }

            return closed.Count;
        }

        /// <summary>
        /// Brings the local badges in line with what the inbox knows about this install's reports: a report confirmed
        /// there turns its badge gold here, and a report this install has no memory of (a reinstall) gives the badge
        /// back. Returns the ids of the tasks that were confirmed just now — the "confirmed" message is shown for each.
        /// </summary>
        public IReadOnlyList<string> ApplyOwnReports(IReadOnlyList<PlaytestOwnReport> reports, long nowUnix)
        {
            var confirmedNow = new List<string>();
            // Oldest first, so the newest report of a task is the one that stays.
            for (int i = reports.Count - 1; i >= 0; i--)
            {
                var report = reports[i];
                if (Find(report.TaskId) is not { } task || string.IsNullOrEmpty(report.ReportId))
                {
                    continue;
                }

                var entry = _progress.Get(task.Id);
                bool unknownHere = entry == null || entry.ReportId.Length == 0
                                   || entry.ReportId.StartsWith(PlaytestProgress.QueuedPrefix, StringComparison.Ordinal);
                if (unknownHere)
                {
                    _progress.Restore(task, report.ReportId, report.Rev, report.CreatedUnix > 0 ? report.CreatedUnix : nowUnix);
                }

                if (report.Confirmed && _progress.MarkConfirmed(report.ReportId) != null)
                {
                    confirmedNow.Add(task.Id);
                }
            }

            return confirmedNow;
        }
    }
}
