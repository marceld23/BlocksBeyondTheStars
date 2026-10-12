// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>One button of a VEGA line (#2499, <see cref="ShipAiLine.Choices"/>): its label, and what picking it does — an
    /// ordinary <see cref="StoryPackIntent"/>, or nothing at all for <see cref="ShipAiChoices.Continue"/> (the line just ends).</summary>
    public sealed class VegaChoice
    {
        /// <summary>The plain continue a line without a usable first button falls back to: no label (the panel shows its
        /// usual "Continue"), no intent.</summary>
        public static readonly VegaChoice PlainContinue = new VegaChoice(string.Empty, ShipAiChoices.Continue, string.Empty, string.Empty);

        public VegaChoice(string labelKey, string action, string packId, string chapterId)
        {
            LabelKey = labelKey ?? string.Empty;
            Action = action ?? string.Empty;
            PackId = packId ?? string.Empty;
            ChapterId = chapterId ?? string.Empty;
        }

        /// <summary>The button's locale key (empty on <see cref="PlainContinue"/>).</summary>
        public string LabelKey { get; }

        /// <summary><see cref="ShipAiChoices.Continue"/> or a <see cref="StoryPackActions"/> word.</summary>
        public string Action { get; }

        public string PackId { get; }

        public string ChapterId { get; }

        /// <summary>Picking it sends a <see cref="StoryPackIntent"/> (anything but a plain continue).</summary>
        public bool SendsIntent => Action != ShipAiChoices.Continue;

        /// <summary>Picking it skips the pack's training — the lines of that pack still waiting in the panel are dropped
        /// (<see cref="VegaQueuedLine.DropPack"/>): they teach what the player just said they know.</summary>
        public bool DropsPackLines => Action == StoryPackActions.SkipPack && PackId.Length > 0;
    }

    /// <summary>
    /// The buttons of one VEGA line (#2499) as the panel draws them: the <see cref="Primary"/> replaces the continue pill (the
    /// continue control — N, the pad's VEGA button, NEXT, a click or a tap — picks it), the <see cref="Secondary"/> sits
    /// where the prologue's skip pill is and takes a deliberate click, tap or a held continue control (never Esc). The
    /// server's order is the role: an entry this client cannot read keeps its place empty — a secondary never moves up to
    /// become the reflex answer (a "skip" must never land on N because the button before it was unknown).
    /// </summary>
    public sealed class VegaChoiceSet
    {
        /// <summary>A plain line: no buttons.</summary>
        public static readonly VegaChoiceSet None = new VegaChoiceSet(VegaChoice.PlainContinue, null);

        /// <summary>The words this client acts on. A newer server's word is ignored (that button is not drawn).</summary>
        private static readonly HashSet<string> KnownActions = new HashSet<string>(StringComparer.Ordinal)
        {
            ShipAiChoices.Continue,
            StoryPackActions.SkipPack,
            StoryPackActions.StartChapter,
            StoryPackActions.DeclineChapter,
        };

        private VegaChoiceSet(VegaChoice primary, VegaChoice? secondary)
        {
            Primary = primary;
            Secondary = secondary;
        }

        /// <summary>The continue control's button — <see cref="VegaChoice.PlainContinue"/> when the line has none usable.</summary>
        public VegaChoice Primary { get; }

        /// <summary>The deliberate button, or null.</summary>
        public VegaChoice? Secondary { get; }

        /// <summary>Whether the line shows any button of its own (a labelled primary or a secondary).</summary>
        public bool HasAny => Primary.LabelKey.Length > 0 || Secondary != null;

        /// <summary>Reads <see cref="ShipAiLine.Choices"/>: the first entry is the primary, the second the secondary, the rest
        /// is ignored. An entry is used only when it decodes, its action is one this client knows, a pack action names its
        /// pack and a chapter action its chapter.</summary>
        public static VegaChoiceSet Parse(IReadOnlyList<string>? wire)
        {
            if (wire is null || wire.Count == 0)
            {
                return None;
            }

            var primary = Read(wire[0]) ?? VegaChoice.PlainContinue;
            var secondary = wire.Count > 1 ? Read(wire[1]) : null;
            return primary.LabelKey.Length == 0 && secondary is null ? None : new VegaChoiceSet(primary, secondary);
        }

        private static VegaChoice? Read(string? entry)
        {
            if (!ShipAiChoices.TryDecode(entry, out string label, out string action, out string pack, out string chapter)
                || !KnownActions.Contains(action))
            {
                return null;
            }

            bool chapterAction = action == StoryPackActions.StartChapter || action == StoryPackActions.DeclineChapter;
            if ((action != ShipAiChoices.Continue && pack.Length == 0) || (chapterAction && chapter.Length == 0))
            {
                return null;
            }

            return new VegaChoice(label, action, pack, chapter);
        }
    }

    /// <summary>What the player picked on a line with buttons this frame.</summary>
    public enum VegaChoicePick
    {
        None,
        Primary,
        Secondary,
    }

    /// <summary>
    /// The continue control on a line with buttons (#2499): a press picks the primary when it is let go; held for
    /// <see cref="HoldSeconds"/> it picks the secondary instead — the deliberate route for a keyboard on foot (the cursor is
    /// captured there, the pill cannot be clicked) and for a pad (every stock pad button already has a job: A jump, B cancel,
    /// X use, Y view, LB/RB place/mine, Back VEGA, Start menu, L3 the action list, R3 slot actions — so the secondary rides
    /// on a long press of the pad's VEGA button). Without a secondary the press picks the primary at once. Only a press that
    /// starts on a revealed line counts: the press that finished the typing does not arm it. Unity-free (VegaPanel feeds it
    /// the frame's input), so the timing is covered by plain .NET tests.
    /// </summary>
    public sealed class VegaChoiceInput
    {
        /// <summary>How long the continue control is held to pick the secondary button.</summary>
        public const float HoldSeconds = 0.8f;

        private bool _armed;

        /// <summary>How long the current press has been held (0 when none is armed).</summary>
        public float HeldSeconds { get; private set; }

        /// <summary>A press is armed (held, not yet decided).</summary>
        public bool Holding => _armed;

        /// <summary>0…1 of the hold towards the secondary — the panel fills the secondary pill with it.</summary>
        public float HoldProgress => _armed ? Math.Min(1f, HeldSeconds / HoldSeconds) : 0f;

        /// <summary>Forgets a press in progress (a new page, the panel hidden or folded, input captured).</summary>
        public void Reset()
        {
            _armed = false;
            HeldSeconds = 0f;
        }

        /// <summary>One frame: <paramref name="down"/> = the continue control went down this frame, <paramref name="held"/> =
        /// it is down now. Returns the pick of this frame, if any.</summary>
        public VegaChoicePick Tick(bool down, bool held, float deltaSeconds, bool hasSecondary)
        {
            if (down && !_armed)
            {
                if (!hasSecondary || !held)
                {
                    Reset();
                    return VegaChoicePick.Primary; // nothing to hold for — or a press without a hold (the action list's "continue")
                }

                _armed = true;
                HeldSeconds = 0f;
                return VegaChoicePick.None;
            }

            if (!_armed)
            {
                return VegaChoicePick.None;
            }

            if (!held)
            {
                Reset();
                return VegaChoicePick.Primary; // let go before the hold: the primary
            }

            if (deltaSeconds > 0f && !float.IsNaN(deltaSeconds) && !float.IsInfinity(deltaSeconds))
            {
                HeldSeconds += deltaSeconds;
            }

            if (HeldSeconds >= HoldSeconds)
            {
                Reset();
                return VegaChoicePick.Secondary;
            }

            return VegaChoicePick.None;
        }
    }

    /// <summary>The texts on the two pills (#2499) — <paramref name="control"/> is the continue control for the device in
    /// hand ("[N]", the pad glyph), empty on touch (the pill itself is tapped).</summary>
    public static class VegaChoiceLabels
    {
        /// <summary>"Let's go · [N]" — or the label alone without a control.</summary>
        public static string Primary(string? label, string? control)
            => string.IsNullOrEmpty(control) ? label ?? string.Empty : (label ?? string.Empty) + " · " + control;

        /// <summary>"Hold [N]: I know the game — skip" through <paramref name="holdTemplate"/> (<c>ui.vega.choice.hold</c>,
        /// {0} = the control, {1} = the label) — or the label alone without a control. A broken template falls back to the
        /// label (a stray brace in a translation must never blank the button).</summary>
        public static string Secondary(string? label, string? holdTemplate, string? control)
        {
            string text = label ?? string.Empty;
            if (string.IsNullOrEmpty(control) || string.IsNullOrEmpty(holdTemplate))
            {
                return text;
            }

            try
            {
                return string.Format(System.Globalization.CultureInfo.InvariantCulture, holdTemplate, control, text);
            }
            catch (FormatException)
            {
                return text;
            }
        }
    }

    /// <summary>A line waiting in VEGA's speech queue (#2499: it now keeps its pack and its buttons — a skip drops the
    /// skipped pack's waiting lines, and the buttons show when the line comes up).</summary>
    public readonly struct VegaQueuedLine
    {
        public VegaQueuedLine(string text, bool prologue, string key, string packId = "", VegaChoiceSet? choices = null)
        {
            Text = text ?? string.Empty;
            Prologue = prologue;
            Key = key ?? string.Empty;
            PackId = packId ?? string.Empty;
            Choices = choices ?? VegaChoiceSet.None;
        }

        /// <summary>The composed text (keys resolved, slots filled).</summary>
        public string Text { get; }

        /// <summary>A prologue page (Kind 4) of an older server.</summary>
        public bool Prologue { get; }

        /// <summary>The locale key (empty for a client-side reminder) — <c>WithdrawLine</c> finds a line by it.</summary>
        public string Key { get; }

        /// <summary>The story pack the line belongs to (<see cref="ShipAiLine.PackId"/>; empty = VEGA's own).</summary>
        public string PackId { get; }

        /// <summary>Its buttons (<see cref="VegaChoiceSet.None"/> = a plain line).</summary>
        public VegaChoiceSet Choices { get; }

        /// <summary>Removes every waiting line of <paramref name="packId"/>, keeping the rest in order; returns how many went.</summary>
        public static int DropPack(Queue<VegaQueuedLine> queue, string packId)
        {
            if (queue is null || queue.Count == 0 || string.IsNullOrEmpty(packId))
            {
                return 0;
            }

            int before = queue.Count;
            var keep = new List<VegaQueuedLine>(before);
            while (queue.Count > 0)
            {
                var item = queue.Dequeue();
                if (!string.Equals(item.PackId, packId, StringComparison.Ordinal))
                {
                    keep.Add(item);
                }
            }

            foreach (var item in keep)
            {
                queue.Enqueue(item);
            }

            return before - keep.Count;
        }
    }
}
