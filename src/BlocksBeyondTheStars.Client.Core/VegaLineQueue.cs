// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>What a waiting line is to the rules of VEGA's speech queue (#2551) — read from the line's
    /// <see cref="ShipAiLine.Kind"/> and <see cref="ShipAiLine.PackId"/> (<see cref="VegaLineQueue.ClassOf"/>), or named by the
    /// client for the lines it raises itself.</summary>
    public enum VegaLineClass
    {
        /// <summary>Never dropped, never moved: story beats, dialogue, prologue pages, cutscene narration given back, system
        /// notices, VEGA's own onboarding lines, the break reminder.</summary>
        Story,

        /// <summary>A story pack's line (Kind 0 with a pack id): the current tutorial or course step's start, hint, done line or
        /// skip offer, a course offer. Never dropped; it goes ahead of every hint still waiting, never ahead of a story line or an
        /// earlier step line.</summary>
        Step,

        /// <summary>A server advisor hint (Kind 1) or a repeated context tip (Kind 5): waits behind step lines but never expires
        /// (the server books it as told).</summary>
        Hint,

        /// <summary>A lesson the client raises for the situation the player is in right now (<c>VegaPanel.SayLocal</c>: the wall in
        /// front of them, a screen just opened): a hint that is also dropped unseen once it waited
        /// <see cref="VegaLineQueue.LessonExpirySeconds"/> — the moment has passed, and its once-flag stays unspent, so it comes
        /// again the next time the situation does.</summary>
        Lesson,
    }

    /// <summary>
    /// VEGA's speech queue (#2551). It used to be a plain first-in-first-out queue without expiry: a player who did not press
    /// Continue got the wall-climbing lesson minutes later, standing aboard her ship, and the training's next instruction sat
    /// behind tips. Now:
    /// <list type="bullet">
    /// <item>a client lesson (<see cref="VegaLineClass.Lesson"/>) still waiting after <see cref="LessonExpirySeconds"/> is dropped
    /// unseen (<see cref="Expire"/>) — its <c>shown</c> callback, which spends the caller's once-flag, never runs; a lesson asked
    /// for again while it waits keeps its place and starts its wait over (<see cref="RefreshLesson"/>);</item>
    /// <item>a step line (<see cref="VegaLineClass.Step"/>) goes ahead of every hint and lesson waiting when it arrives — the
    /// hints step back behind it in their own order;</item>
    /// <item>everything else keeps its arrival order and is never dropped by the queue itself; before the prologue has started its
    /// pages go first (<see cref="PromotePrologue"/>, #754).</item>
    /// </list>
    /// Unity-free so the rules are covered by plain .NET tests; <c>VegaPanel</c> feeds it the clock and owns the drawing.
    /// </summary>
    public sealed class VegaLineQueue
    {
        /// <summary>How long a client lesson may wait unseen before it is dropped.</summary>
        public const double LessonExpirySeconds = 30.0;

        private readonly List<Entry> _lines = new List<Entry>();

        /// <summary>Waiting lines (the one on screen is not part of the queue).</summary>
        public int Count => _lines.Count;

        /// <summary>The waiting lines in the order they will show.</summary>
        public IEnumerable<VegaQueuedLine> Lines
        {
            get
            {
                foreach (var entry in _lines)
                {
                    yield return entry.Line;
                }
            }
        }

        /// <summary>Whether any waiting line is a prologue page.</summary>
        public bool HasPrologue => _lines.Exists(e => e.Line.Prologue);

        /// <summary>The class of a server line: Kind 1 and 5 are hints, a Kind-0 line of a story pack is a step line, the rest
        /// (beats, system notices, prologue pages, VEGA's own onboarding, an LLM line) is a story line.</summary>
        public static VegaLineClass ClassOf(byte kind, string? packId)
        {
            if (kind == 1 || kind == 5)
            {
                return VegaLineClass.Hint;
            }

            return kind == 0 && !string.IsNullOrEmpty(packId) ? VegaLineClass.Step : VegaLineClass.Story;
        }

        private static bool IsHint(VegaLineClass lineClass) => lineClass == VegaLineClass.Hint || lineClass == VegaLineClass.Lesson;

        /// <summary>Queues a line at <paramref name="now"/> (seconds on any steady clock). <paramref name="shown"/> runs when the
        /// line comes up on screen (<see cref="TryDequeue"/>) — never for a line dropped unseen.</summary>
        public void Enqueue(VegaQueuedLine line, VegaLineClass lineClass, double now, Action? shown = null)
        {
            var entry = new Entry(line, lineClass, now, shown);
            if (lineClass != VegaLineClass.Step || !_lines.Exists(e => IsHint(e.Class)))
            {
                _lines.Add(entry);
                return;
            }

            // A step line goes ahead of the hints still waiting: they step back behind it in their own order, every other line
            // keeps its place — story beats and step lines stay in arrival order among themselves.
            var hints = _lines.FindAll(e => IsHint(e.Class));
            _lines.RemoveAll(e => IsHint(e.Class));
            _lines.Add(entry);
            _lines.AddRange(hints);
        }

        /// <summary>The situation asks for a lesson that is already waiting: it keeps its place and its wait starts over at
        /// <paramref name="now"/>. False when no lesson with this key waits (only <see cref="VegaLineClass.Lesson"/> lines count).</summary>
        public bool RefreshLesson(string? key, double now)
        {
            if (string.IsNullOrEmpty(key))
            {
                return false;
            }

            bool found = false;
            foreach (var entry in _lines)
            {
                if (entry.Class == VegaLineClass.Lesson && string.Equals(entry.Line.Key, key, StringComparison.Ordinal))
                {
                    entry.Since = now;
                    found = true;
                }
            }

            return found;
        }

        /// <summary>Drops every lesson that has waited <see cref="LessonExpirySeconds"/> or longer by <paramref name="now"/>, unseen
        /// (its <c>shown</c> callback does not run); returns how many went. Story, step and hint lines never expire. Called every
        /// frame, so it allocates nothing.</summary>
        public int Expire(double now)
        {
            int dropped = 0;
            for (int i = _lines.Count - 1; i >= 0; i--)
            {
                var entry = _lines[i];
                if (entry.Class == VegaLineClass.Lesson && now - entry.Since >= LessonExpirySeconds)
                {
                    _lines.RemoveAt(i);
                    dropped++;
                }
            }

            return dropped;
        }

        /// <summary>Before the prologue has started: when the first waiting line is not a prologue page but a page waits further
        /// back (the join's beat catch-up arrives in front of the prologue), the pages move to the front — a stable partition,
        /// both groups in their own order. Returns whether anything moved.</summary>
        public bool PromotePrologue()
        {
            if (_lines.Count == 0 || _lines[0].Line.Prologue || !HasPrologue)
            {
                return false;
            }

            var rest = _lines.FindAll(e => !e.Line.Prologue);
            _lines.RemoveAll(e => !e.Line.Prologue);
            _lines.AddRange(rest);
            return true;
        }

        /// <summary>Takes the next line for the screen and runs its <c>shown</c> callback (the line comes up now).</summary>
        public bool TryDequeue(out VegaQueuedLine line)
        {
            if (_lines.Count == 0)
            {
                line = default;
                return false;
            }

            var entry = _lines[0];
            _lines.RemoveAt(0);
            line = entry.Line;
            entry.Shown?.Invoke();
            return true;
        }

        /// <summary>The prologue's skip (#754): drops the prologue pages at the front; returns how many went.</summary>
        public int DropLeadingPrologue()
        {
            int dropped = 0;
            while (_lines.Count > 0 && _lines[0].Line.Prologue)
            {
                _lines.RemoveAt(0);
                dropped++;
            }

            return dropped;
        }

        /// <summary>Takes back every waiting line with this locale key (#2461, <c>VegaPanel.WithdrawLine</c>); returns how many went.</summary>
        public int Withdraw(string? key)
            => string.IsNullOrEmpty(key) ? 0 : _lines.RemoveAll(e => string.Equals(e.Line.Key, key, StringComparison.Ordinal));

        /// <summary>Removes every waiting line of <paramref name="packId"/>, keeping the rest in order (#2499: a skipped pack's
        /// lines teach what the player just said they know); returns how many went.</summary>
        public int DropPack(string? packId)
            => string.IsNullOrEmpty(packId) ? 0 : _lines.RemoveAll(e => string.Equals(e.Line.PackId, packId, StringComparison.Ordinal));

        /// <summary>Drops every waiting line unseen (an unattended capture run).</summary>
        public void Clear() => _lines.Clear();

        private sealed class Entry
        {
            public Entry(VegaQueuedLine line, VegaLineClass lineClass, double since, Action? shown)
            {
                Line = line;
                Class = lineClass;
                Since = since;
                Shown = shown;
            }

            public VegaQueuedLine Line { get; }

            public VegaLineClass Class { get; }

            /// <summary>When the line was queued — for a lesson, when its situation last asked for it.</summary>
            public double Since { get; set; }

            public Action? Shown { get; }
        }
    }
}
