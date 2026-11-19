// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using BlocksBeyondTheStars.Shared.Playtests;

namespace BlocksBeyondTheStars.Client.Playtests
{
    /// <summary>Where a playtest task stands for this install.</summary>
    public enum PlaytestTaskStatus
    {
        /// <summary>Not started.</summary>
        New,

        /// <summary>Started; at least one step is still open.</summary>
        InProgress,

        /// <summary>Every step is closed (done or "did not work") — the report can be written.</summary>
        ReadyToReport,

        /// <summary>The report was sent: the badge is earned.</summary>
        Submitted,

        /// <summary>The developers confirmed the report: the badge is gold.</summary>
        Confirmed,

        /// <summary>A finished task the developers opened again ("please test again").</summary>
        Retest,
    }

    /// <summary>The state of one step.</summary>
    public static class PlaytestStepStates
    {
        /// <summary>Not closed yet.</summary>
        public const string Open = "";

        /// <summary>Done — recognised by the game or ticked off by the player.</summary>
        public const string Done = "done";

        /// <summary>The player said it did not work. A valid test result: it closes the step.</summary>
        public const string Failed = "failed";
    }

    /// <summary>What this install knows about one step of a task.</summary>
    public sealed class PlaytestStepProgress
    {
        public string State { get; set; } = PlaytestStepStates.Open;

        /// <summary>How often the step's event was seen (a recognised step).</summary>
        public int Count { get; set; }

        /// <summary>True when the game recognised the step, false when the player ticked it off.</summary>
        public bool Auto { get; set; }

        /// <summary>The player's short note to a step that did not work.</summary>
        public string Note { get; set; } = string.Empty;
    }

    /// <summary>What this install knows about one task: its steps, the draft of the report, and the sent report.</summary>
    public sealed class PlaytestTaskProgress
    {
        public string TaskId { get; set; } = string.Empty;

        /// <summary>The task revision the steps below belong to.</summary>
        public int Rev { get; set; }

        public Dictionary<string, PlaytestStepProgress> Steps { get; set; } = new Dictionary<string, PlaytestStepProgress>(StringComparer.Ordinal);

        /// <summary>The draft answers by question id (a star count or an option id), kept until the report is sent.</summary>
        public Dictionary<string, string> Answers { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>The draft of the free text.</summary>
        public string FreeText { get; set; } = string.Empty;

        public long StartedUnix { get; set; }

        /// <summary>The inbox's id of the last sent report ("" = none sent).</summary>
        public string ReportId { get; set; } = string.Empty;

        /// <summary>The task revision the last sent report answered.</summary>
        public int SentRev { get; set; }

        public long SentUnix { get; set; }

        /// <summary>The developers confirmed the last sent report.</summary>
        public bool Confirmed { get; set; }

        /// <summary>How many reports were sent for this task (a re-test counts again).</summary>
        public int Runs { get; set; }

        /// <summary>The name of the test world the client created for the task ("" = none).</summary>
        public string World { get; set; } = string.Empty;

        /// <summary>The world's start commands were run (they run once).</summary>
        public bool CommandsRan { get; set; }
    }

    /// <summary>
    /// This install's playtest progress and badges: <c>playtests/progress.json</c> under the data root. It lives per
    /// INSTALL and not in a savegame, because a task spans worlds, the main menu and the editors. Nothing here is
    /// authoritative for the game world — a badge buys nothing — so the file is the client's own, like
    /// <c>feedback/sent.json</c>. Best-effort like that file: never throws, a lost file merely forgets local ticks
    /// (the sent reports themselves are in the inbox).
    /// </summary>
    public sealed class PlaytestProgress
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
        };

        /// <summary>
        /// The start of a placeholder report id: the report could not be delivered and waits in the feedback queue for a
        /// later start. The badge is earned all the same; the real id replaces the placeholder when the inbox lists the
        /// report among this install's own (<see cref="PlaytestTracker.ApplyOwnReports"/>).
        /// </summary>
        public const string QueuedPrefix = "queued:";

        private readonly string _path;
        private readonly object _gate = new object();
        private Dictionary<string, PlaytestTaskProgress>? _tasks;
        private string _active = string.Empty;
        private bool _visited;

        /// <summary>The file's shape: the task the player is working on, and what is known about each task.</summary>
        private sealed class FileShape
        {
            public string Active { get; set; } = string.Empty;

            public bool Visited { get; set; }

            public List<PlaytestTaskProgress> Tasks { get; set; } = new List<PlaytestTaskProgress>();
        }

        /// <summary>The id of the task the player is working on ("" = none) — the one the HUD line shows and the one
        /// events count for. It survives a restart, so a task goes on where it stopped.</summary>
        public string ActiveTaskId
        {
            get
            {
                lock (_gate)
                {
                    EnsureLoaded();
                    return _active;
                }
            }
        }

        /// <summary>
        /// Whether this install has ever opened the Test Pilot page. It is the gate for fetching the catalogue by
        /// itself at a later start: a game whose player never looked at the page contacts nothing for it (the rule of
        /// the reply poll — nothing phones home without a reason).
        /// </summary>
        public bool Visited
        {
            get
            {
                lock (_gate)
                {
                    EnsureLoaded();
                    return _visited;
                }
            }
        }

        /// <summary>The player opened the Test Pilot page.</summary>
        public void MarkVisited()
        {
            lock (_gate)
            {
                EnsureLoaded();
                if (!_visited)
                {
                    _visited = true;
                    Save();
                }
            }
        }

        /// <summary>Makes <paramref name="taskId"/> the task being worked on ("" = none).</summary>
        public void SetActive(string? taskId)
        {
            lock (_gate)
            {
                EnsureLoaded();
                string next = taskId ?? string.Empty;
                if (next != _active)
                {
                    _active = next;
                    Save();
                }
            }
        }

        /// <param name="path">The JSON file (typically <c>&lt;data root&gt;/playtests/progress.json</c>); empty keeps
        /// everything in memory only.</param>
        public PlaytestProgress(string path)
        {
            _path = path ?? string.Empty;
        }

        /// <summary>What is stored for a task, or null. A copy is not made — callers read it, the methods below change it.</summary>
        public PlaytestTaskProgress? Get(string taskId)
        {
            lock (_gate)
            {
                EnsureLoaded();
                return _tasks!.TryGetValue(taskId, out var entry) ? entry : null;
            }
        }

        /// <summary>Where <paramref name="task"/> stands.</summary>
        public PlaytestTaskStatus StatusOf(PlaytestTask task)
        {
            lock (_gate)
            {
                EnsureLoaded();
                if (!_tasks!.TryGetValue(task.Id, out var entry))
                {
                    return PlaytestTaskStatus.New;
                }

                if (IsFinished(entry))
                {
                    if (task.Retest && task.Rev > entry.SentRev)
                    {
                        return PlaytestTaskStatus.Retest;
                    }

                    return entry.Confirmed ? PlaytestTaskStatus.Confirmed : PlaytestTaskStatus.Submitted;
                }

                if (!Touched(entry))
                {
                    return entry.ReportId.Length > 0 ? PlaytestTaskStatus.Retest : PlaytestTaskStatus.New;
                }

                return OpenStepsLocked(task, entry) > 0 ? PlaytestTaskStatus.InProgress : PlaytestTaskStatus.ReadyToReport;
            }
        }

        /// <summary>How many steps of <paramref name="task"/> are still open.</summary>
        public int OpenSteps(PlaytestTask task)
        {
            lock (_gate)
            {
                EnsureLoaded();
                return _tasks!.TryGetValue(task.Id, out var entry) ? OpenStepsLocked(task, entry) : task.Steps.Count;
            }
        }

        /// <summary>The state of one step (<see cref="PlaytestStepStates"/>).</summary>
        public PlaytestStepProgress StepOf(PlaytestTask task, string stepId)
        {
            lock (_gate)
            {
                EnsureLoaded();
                return _tasks!.TryGetValue(task.Id, out var entry) && entry.Steps.TryGetValue(stepId, out var step)
                    ? step
                    : new PlaytestStepProgress();
            }
        }

        /// <summary>
        /// The player closes (or re-opens) a step by hand: <see cref="PlaytestStepStates.Done"/>,
        /// <see cref="PlaytestStepStates.Failed"/> with a short note, or <see cref="PlaytestStepStates.Open"/>.
        /// False when the task has no such step.
        /// </summary>
        public bool SetStep(PlaytestTask task, string stepId, string state, string? note, long nowUnix)
        {
            if (FindStep(task, stepId) is null)
            {
                return false;
            }

            lock (_gate)
            {
                var entry = EntryFor(task, nowUnix);
                if (IsFinished(entry))
                {
                    return false; // the report is sent — its steps are a record now, not a checklist
                }

                if (!entry.Steps.TryGetValue(stepId, out var step))
                {
                    entry.Steps[stepId] = step = new PlaytestStepProgress();
                }

                step.State = state == PlaytestStepStates.Done || state == PlaytestStepStates.Failed ? state : PlaytestStepStates.Open;
                step.Auto = false;
                step.Note = step.State == PlaytestStepStates.Failed ? Cut(note, PlaytestLimits.MaxTextLength) : string.Empty;
                Save();
                return true;
            }
        }

        /// <summary>
        /// An event happened (a server game event, a UI moment, an editor event): every open step of
        /// <paramref name="task"/> that watches it counts it, and a step whose count is reached closes as done. Returns
        /// the ids of the steps that closed just now (for the "step recognised" line).
        /// </summary>
        public IReadOnlyList<string> CountEvent(PlaytestTask task, string key, string arg, int amount, long nowUnix)
        {
            var closed = new List<string>();
            if (amount <= 0 || string.IsNullOrEmpty(key))
            {
                return closed;
            }

            lock (_gate)
            {
                EnsureLoaded();
                PlaytestTaskProgress? entry = null;
                foreach (var step in task.Steps)
                {
                    if (step.Check is not { } check || !check.Matches(key, arg ?? string.Empty))
                    {
                        continue;
                    }

                    entry ??= EntryFor(task, nowUnix);
                    if (IsFinished(entry))
                    {
                        return closed; // the report is sent — nothing counts any more
                    }

                    if (!entry.Steps.TryGetValue(step.Id, out var progress))
                    {
                        entry.Steps[step.Id] = progress = new PlaytestStepProgress();
                    }

                    if (progress.State != PlaytestStepStates.Open)
                    {
                        continue; // closed already — by the game or by the player
                    }

                    progress.Count = Math.Min(check.Required, progress.Count + amount);
                    if (progress.Count >= check.Required)
                    {
                        progress.State = PlaytestStepStates.Done;
                        progress.Auto = true;
                        closed.Add(step.Id);
                    }
                }

                if (entry != null)
                {
                    Save();
                }
            }

            return closed;
        }

        /// <summary>Keeps the report draft (answers by question id, the free text) so a closed dialog loses nothing.</summary>
        public void SetDraft(PlaytestTask task, IReadOnlyDictionary<string, string> answers, string? freeText, long nowUnix)
        {
            lock (_gate)
            {
                var entry = EntryFor(task, nowUnix);
                entry.Answers = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var pair in answers)
                {
                    entry.Answers[pair.Key] = pair.Value;
                }

                entry.FreeText = Cut(freeText, 5000);
                Save();
            }
        }

        /// <summary>The report for <paramref name="task"/> was accepted by the inbox: the badge is earned, the draft is
        /// cleared.</summary>
        public void RecordSent(PlaytestTask task, string reportId, long nowUnix)
        {
            if (string.IsNullOrWhiteSpace(reportId))
            {
                return;
            }

            lock (_gate)
            {
                var entry = EntryFor(task, nowUnix);
                entry.ReportId = reportId;
                entry.SentRev = task.Rev;
                entry.SentUnix = nowUnix;
                entry.Confirmed = false;
                entry.Runs++;
                entry.Answers.Clear();
                entry.FreeText = string.Empty;
                Save();
            }
        }

        /// <summary>
        /// Gives a badge back that this install has no memory of: the inbox knows a report of this install for the task
        /// (after a reinstall, or with a lost progress file). The steps are not known any more — only that the report
        /// was sent, for which revision, and when.
        /// </summary>
        public void Restore(PlaytestTask task, string reportId, int sentRev, long sentUnix)
        {
            if (string.IsNullOrWhiteSpace(reportId))
            {
                return;
            }

            lock (_gate)
            {
                EnsureLoaded();
                if (!_tasks!.TryGetValue(task.Id, out var entry))
                {
                    _tasks[task.Id] = entry = new PlaytestTaskProgress { TaskId = task.Id, StartedUnix = sentUnix };
                }

                entry.ReportId = reportId;
                entry.SentRev = Math.Max(1, sentRev);
                entry.Rev = entry.SentRev;
                entry.SentUnix = sentUnix;
                entry.Runs = Math.Max(1, entry.Runs);
                entry.Steps.Clear();
                Save();
            }
        }

        /// <summary>The developers confirmed the report with this id. Returns the task's id, or null when the report is
        /// not one of this install's playtest reports (or was confirmed before).</summary>
        public string? MarkConfirmed(string reportId)
        {
            if (string.IsNullOrEmpty(reportId))
            {
                return null;
            }

            lock (_gate)
            {
                EnsureLoaded();
                foreach (var entry in _tasks!.Values)
                {
                    if (entry.ReportId == reportId && !entry.Confirmed)
                    {
                        entry.Confirmed = true;
                        Save();
                        return entry.TaskId;
                    }
                }

                return null;
            }
        }

        /// <summary>Starts a re-test: the steps are open again, the earned badge and its history stay.</summary>
        public void BeginRetest(PlaytestTask task, long nowUnix)
        {
            lock (_gate)
            {
                var entry = EntryFor(task, nowUnix);
                entry.Rev = task.Rev;
                entry.Steps.Clear();
                entry.Answers.Clear();
                entry.FreeText = string.Empty;
                entry.StartedUnix = nowUnix;
                entry.CommandsRan = false;
                if (entry.SentRev >= task.Rev)
                {
                    entry.SentRev = task.Rev - 1; // the re-test is a run of its own
                }

                Save();
            }
        }

        /// <summary>Remembers the test world created for a task and whether its start commands ran.</summary>
        public void SetWorld(PlaytestTask task, string world, bool commandsRan, long nowUnix)
        {
            lock (_gate)
            {
                var entry = EntryFor(task, nowUnix);
                entry.World = world ?? string.Empty;
                entry.CommandsRan = commandsRan;
                Save();
            }
        }

        /// <summary>The task whose test world is <paramref name="world"/>, or null.</summary>
        public string? TaskOfWorld(string world)
        {
            if (string.IsNullOrEmpty(world))
            {
                return null;
            }

            lock (_gate)
            {
                EnsureLoaded();
                foreach (var entry in _tasks!.Values)
                {
                    if (string.Equals(entry.World, world, StringComparison.Ordinal))
                    {
                        return entry.TaskId;
                    }
                }

                return null;
            }
        }

        /// <summary>The earned badges: one per task with a sent report, and how many of them are confirmed.</summary>
        public (int Badges, int Confirmed) BadgeCount()
        {
            lock (_gate)
            {
                EnsureLoaded();
                int badges = 0, confirmed = 0;
                foreach (var entry in _tasks!.Values)
                {
                    if (entry.ReportId.Length > 0)
                    {
                        badges++;
                        if (entry.Confirmed)
                        {
                            confirmed++;
                        }
                    }
                }

                return (badges, confirmed);
            }
        }

        // ---------------------------------------------------------------- internals (all under _gate)

        /// <summary>A report was sent for the revision the stored steps belong to — the task is done until a re-test
        /// (<see cref="BeginRetest"/>) opens its steps again for a newer revision.</summary>
        private static bool IsFinished(PlaytestTaskProgress entry) => entry.ReportId.Length > 0 && entry.Rev <= entry.SentRev;

        private static bool Touched(PlaytestTaskProgress entry)
        {
            foreach (var step in entry.Steps.Values)
            {
                if (step.State != PlaytestStepStates.Open || step.Count > 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static int OpenStepsLocked(PlaytestTask task, PlaytestTaskProgress entry)
        {
            int open = 0;
            foreach (var step in task.Steps)
            {
                if (!entry.Steps.TryGetValue(step.Id, out var progress) || progress.State == PlaytestStepStates.Open)
                {
                    open++;
                }
            }

            return open;
        }

        private static PlaytestStep? FindStep(PlaytestTask task, string stepId)
        {
            foreach (var step in task.Steps)
            {
                if (string.Equals(step.Id, stepId, StringComparison.Ordinal))
                {
                    return step;
                }
            }

            return null;
        }

        /// <summary>The entry for <paramref name="task"/>, created on first use and brought in line with the task's
        /// current revision: steps the task no longer has are dropped (a changed task must not keep ticks for steps
        /// that are gone), steps it still has keep their state.</summary>
        private PlaytestTaskProgress EntryFor(PlaytestTask task, long nowUnix)
        {
            EnsureLoaded();
            if (!_tasks!.TryGetValue(task.Id, out var entry))
            {
                _tasks[task.Id] = entry = new PlaytestTaskProgress { TaskId = task.Id, Rev = task.Rev, StartedUnix = nowUnix };
                return entry;
            }

            if (entry.Rev != task.Rev && !IsFinished(entry))
            {
                var kept = new Dictionary<string, PlaytestStepProgress>(StringComparer.Ordinal);
                foreach (var step in task.Steps)
                {
                    if (entry.Steps.TryGetValue(step.Id, out var progress))
                    {
                        kept[step.Id] = progress;
                    }
                }

                entry.Steps = kept;
                entry.Rev = task.Rev;
            }

            return entry;
        }

        private void EnsureLoaded()
        {
            if (_tasks != null)
            {
                return;
            }

            _tasks = new Dictionary<string, PlaytestTaskProgress>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(_path))
            {
                return;
            }

            try
            {
                if (!File.Exists(_path))
                {
                    return;
                }

                var file = JsonSerializer.Deserialize<FileShape>(File.ReadAllText(_path), JsonOptions) ?? new FileShape();
                _active = file.Active ?? string.Empty;
                _visited = file.Visited;
                foreach (var entry in file.Tasks ?? new List<PlaytestTaskProgress>())
                {
                    if (entry is null || string.IsNullOrEmpty(entry.TaskId))
                    {
                        continue;
                    }

                    entry.Steps = new Dictionary<string, PlaytestStepProgress>(entry.Steps ?? new Dictionary<string, PlaytestStepProgress>(), StringComparer.Ordinal);
                    entry.Answers = new Dictionary<string, string>(entry.Answers ?? new Dictionary<string, string>(), StringComparer.Ordinal);
                    _tasks[entry.TaskId] = entry;
                }
            }
            catch (Exception)
            {
                // A broken or unreadable file starts empty — the next save replaces it.
                _tasks.Clear();
                _active = string.Empty;
                _visited = false;
            }
        }

        private bool Save()
        {
            if (string.IsNullOrEmpty(_path))
            {
                return false;
            }

            try
            {
                string? dir = System.IO.Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var file = new FileShape { Active = _active, Visited = _visited, Tasks = new List<PlaytestTaskProgress>(_tasks!.Values) };
                file.Tasks.Sort((a, b) => string.CompareOrdinal(a.TaskId, b.TaskId));
                string temp = _path + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(file, JsonOptions));
                if (File.Exists(_path))
                {
                    File.Delete(_path);
                }

                File.Move(temp, _path);
                return true;
            }
            catch (Exception)
            {
                return false; // best-effort, like the sent-reports log
            }
        }

        private static string Cut(string? text, int max)
        {
            string value = (text ?? string.Empty).Trim();
            return value.Length <= max ? value : value.Substring(0, max);
        }
    }

    /// <summary>The Test Pilot ranks: a rank is reached with a number of badges (one badge per task with a sent
    /// report).</summary>
    public static class PlaytestRanks
    {
        /// <summary>The badges needed for rank 1, 2, 3 and 4.</summary>
        public static readonly IReadOnlyList<int> Thresholds = new[] { 1, 3, 10, 25 };

        /// <summary>The rank for <paramref name="badges"/>: 0 (none yet) to 4.</summary>
        public static int RankFor(int badges)
        {
            int rank = 0;
            foreach (int threshold in Thresholds)
            {
                if (badges >= threshold)
                {
                    rank++;
                }
            }

            return rank;
        }

        /// <summary>The badges the next rank needs, or 0 at the highest rank.</summary>
        public static int NextThreshold(int badges)
        {
            foreach (int threshold in Thresholds)
            {
                if (badges < threshold)
                {
                    return threshold;
                }
            }

            return 0;
        }
    }
}
