// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Shared.Playtests;
using BlocksBeyondTheStars.Shared.Story;

namespace BlocksBeyondTheStars.Client.Playtests
{
    /// <summary>What the line under a step tells the tester to do about it.</summary>
    public enum PlaytestStepHint
    {
        /// <summary>The game recognises the step: nothing to press.</summary>
        Recognised,

        /// <summary>The tester closes the step ("Done" / "Did not work").</summary>
        TickOff,
    }

    /// <summary>How the task being worked on announces itself when a world comes up.</summary>
    public enum PlaytestStartNotice
    {
        /// <summary>Nothing to announce: no task, an editor task, or nothing left to do in a world.</summary>
        None,

        /// <summary>One status line naming the task — the tester's own world is not interrupted.</summary>
        Line,

        /// <summary>The start card: this world was created for the task.</summary>
        Card,
    }

    /// <summary>
    /// What the HUD card of the task being worked on shows (#3846): which task, which of its steps, how far a counting
    /// step is and what the tester has to do about it — or that every step is closed and the report is due.
    /// </summary>
    public sealed class PlaytestCard
    {
        public PlaytestCard(PlaytestTask task, PlaytestStep? step, int stepNumber, int count, PlaytestStepHint hint)
        {
            Task = task;
            Step = step;
            StepNumber = stepNumber;
            Count = count;
            Hint = hint;
        }

        /// <summary>The task being worked on.</summary>
        public PlaytestTask Task { get; }

        /// <summary>The first open step, or null when every step is closed.</summary>
        public PlaytestStep? Step { get; }

        /// <summary>True when no step is open any more: the card says that the report is due.</summary>
        public bool AllDone => Step is null;

        /// <summary>The number of <see cref="Step"/> in the task's list, from 1 (0 when all are closed).</summary>
        public int StepNumber { get; }

        /// <summary>How many steps the task has.</summary>
        public int StepCount => Task.Steps.Count;

        /// <summary>How far a counting step is.</summary>
        public int Count { get; }

        /// <summary>What a counting step needs; 1 for every other step.</summary>
        public int Required => Step?.Check is { } check ? check.Required : 1;

        /// <summary>True for a step that counts ("break three asteroids"): the card shows "1 of 3".</summary>
        public bool Counts => Step?.Check != null && Required > 1 && Hint == PlaytestStepHint.Recognised;

        /// <summary>What the tester has to do about <see cref="Step"/>.</summary>
        public PlaytestStepHint Hint { get; }
    }

    /// <summary>
    /// The rules behind what the game says about the task being worked on while it is played: the HUD card, the line
    /// under a step, and how a task announces itself when a world comes up. Pure logic over
    /// <see cref="PlaytestTracker"/>, so the page, the list over an editor and the HUD agree and it is tested headless.
    /// </summary>
    public static class PlaytestGuide
    {
        /// <summary>True for an event only a server raises — not one of the client's own (an editor event, a UI moment).</summary>
        public static bool IsServerEvent(string key) => !PlaytestEditorEvents.IsEditorEvent(key) && !GameEventKeys.IsUiEvent(key);

        /// <summary>
        /// Whether the game closes <paramref name="step"/> by itself here. A step without a check never is; a step that
        /// waits for a server event is not when the server of this world said it does not report that event
        /// (<paramref name="serverReports"/> answers false — an older server, #3833). No answer yet counts as yes.
        /// </summary>
        public static bool Recognised(PlaytestStep step, Func<string, bool?>? serverReports)
        {
            if (step.Check is not { } check)
            {
                return false;
            }

            return !(IsServerEvent(check.Event) && serverReports?.Invoke(check.Event) == false);
        }

        /// <summary>The hint for <paramref name="step"/> (see <see cref="Recognised"/>).</summary>
        public static PlaytestStepHint HintFor(PlaytestStep step, Func<string, bool?>? serverReports)
            => Recognised(step, serverReports) ? PlaytestStepHint.Recognised : PlaytestStepHint.TickOff;

        /// <summary>
        /// The HUD card for the task being worked on, or null when there is nothing to show in a world: no task, a task
        /// that is done in an editor, or one whose report is sent.
        /// </summary>
        public static PlaytestCard? CardFor(PlaytestTracker tracker, Func<string, bool?>? serverReports)
        {
            if (tracker.Active is not { } task || task.Area == PlaytestAreas.Editor)
            {
                return null;
            }

            var status = tracker.Progress.StatusOf(task);
            if (status == PlaytestTaskStatus.Submitted || status == PlaytestTaskStatus.Confirmed)
            {
                return null;
            }

            for (int i = 0; i < task.Steps.Count; i++)
            {
                var step = task.Steps[i];
                var progress = tracker.Progress.StepOf(task, step.Id);
                if (progress.State == PlaytestStepStates.Open)
                {
                    return new PlaytestCard(task, step, i + 1, progress.Count, HintFor(step, serverReports));
                }
            }

            return new PlaytestCard(task, null, 0, 0, PlaytestStepHint.TickOff);
        }

        /// <summary>
        /// How the task being worked on announces itself in the world that just came up (#3848): the start card in the
        /// world this install created for the task, a status line in any other world, nothing for an editor task or
        /// when no step is open any more.
        /// </summary>
        /// <param name="tracker">This install's playtest state.</param>
        /// <param name="localWorld">True for a world this client started itself.</param>
        /// <param name="worldName">The name of that world.</param>
        public static PlaytestStartNotice StartNoticeFor(PlaytestTracker tracker, bool localWorld, string? worldName)
        {
            if (tracker.Active is not { } task || task.Area == PlaytestAreas.Editor || tracker.CurrentStep is null)
            {
                return PlaytestStartNotice.None;
            }

            bool itsTestWorld = localWorld && PlaytestWorldPlan.IsTestWorldName(worldName)
                                && string.Equals(tracker.Progress.TaskOfWorld(worldName ?? string.Empty), task.Id, StringComparison.Ordinal);
            return itsTestWorld ? PlaytestStartNotice.Card : PlaytestStartNotice.Line;
        }
    }
}
