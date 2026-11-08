// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>A d-pad direction (the melody bar's pad keys).</summary>
    public enum PadDirection
    {
        None = 0,
        Up,
        Right,
        Down,
        Left,
    }

    /// <summary>
    /// The melody bar's keys (#3346), one place for every device: the number keys 1–8 are the tones C … C' (while the bar
    /// is open they no longer switch the hotbar); on a pad the d-pad plays tones 1–4 (up, right, down, left — clockwise,
    /// like the pillars around a shrine) and the same with a shoulder button held plays 5–8; touch has eight buttons, one per
    /// tone. Unity-free, so the mapping is pinned by tests.
    /// </summary>
    public static class MelodyBarInput
    {
        /// <summary>No tone.</summary>
        public const int NoTone = -1;

        /// <summary>The tones on the bar.</summary>
        public const int Tones = 8;

        /// <summary>The tone a number key plays: 1 → 0 (C) … 8 → 7 (C'); anything else (0, 9) none.</summary>
        public static int ToneForDigit(int digit) => digit >= 1 && digit <= Tones ? digit - 1 : NoTone;

        /// <summary>The number key that plays <paramref name="tone"/> (the bar's key caption).</summary>
        public static int DigitForTone(int tone) => tone >= 0 && tone < Tones ? tone + 1 : 0;

        /// <summary>The tone a d-pad press plays: up 0, right 1, down 2, left 3 — with a shoulder held 4 … 7.</summary>
        public static int ToneForPad(PadDirection direction, bool shoulderHeld)
        {
            int baseTone = direction switch
            {
                PadDirection.Up => 0,
                PadDirection.Right => 1,
                PadDirection.Down => 2,
                PadDirection.Left => 3,
                _ => NoTone,
            };
            return baseTone < 0 ? NoTone : baseTone + (shoulderHeld ? 4 : 0);
        }

        /// <summary>The pad keys that play <paramref name="tone"/>: its d-pad direction and whether a shoulder is held.</summary>
        public static (PadDirection Direction, bool Shoulder) PadForTone(int tone)
        {
            if (tone < 0 || tone >= Tones)
            {
                return (PadDirection.None, false);
            }

            var direction = (tone % 4) switch
            {
                0 => PadDirection.Up,
                1 => PadDirection.Right,
                2 => PadDirection.Down,
                _ => PadDirection.Left,
            };
            return (direction, tone >= 4);
        }

        /// <summary>The direction of the d-pad's axes (x right, y up), past <paramref name="threshold"/>; the stronger axis
        /// wins a diagonal.</summary>
        public static PadDirection DirectionOf(float x, float y, float threshold = MelodyPadReader.Threshold)
        {
            float ax = Math.Abs(x), ay = Math.Abs(y);
            if (ax < threshold && ay < threshold)
            {
                return PadDirection.None;
            }

            if (ay >= ax)
            {
                return y > 0 ? PadDirection.Up : PadDirection.Down;
            }

            return x > 0 ? PadDirection.Right : PadDirection.Left;
        }

        /// <summary>The key token of the hotbar's segment in the keyboard controls line (<c>ui.hud.hint</c>).</summary>
        public const string HotbarToken = "{key:Hotbar}";

        /// <summary>
        /// #3431: the keyboard controls line while the bar is open — the segment that names the hotbar keys ("1-9 Hotbar",
        /// the one holding <see cref="HotbarToken"/>) drops out, because 1–8 play the tones then; every other segment stays,
        /// in its order. Found by its token, not by its words, so it works in every language; a template without the token
        /// comes back unchanged.
        /// </summary>
        public static string WithoutHotbarSegment(string? template)
        {
            if (string.IsNullOrEmpty(template) || template!.IndexOf(HotbarToken, StringComparison.Ordinal) < 0)
            {
                return template ?? string.Empty;
            }

            var kept = new List<string>();
            foreach (string segment in template.Split(new[] { PadControlNames.Separator }, StringSplitOptions.None))
            {
                if (segment.IndexOf(HotbarToken, StringComparison.Ordinal) < 0)
                {
                    kept.Add(segment);
                }
            }

            return string.Join(PadControlNames.Separator, kept);
        }
    }

    /// <summary>
    /// Turns the pad's raw d-pad axes into tone PRESSES for the melody bar: a tone sounds once when a direction is pushed,
    /// not every frame it is held; letting go (or moving to another direction) arms the next press.
    /// </summary>
    public sealed class MelodyPadReader
    {
        /// <summary>How far an axis must be pushed to count.</summary>
        public const float Threshold = 0.5f;

        private PadDirection _held;

        /// <summary>One frame: the tone pressed this frame, or <see cref="MelodyBarInput.NoTone"/>.</summary>
        public int Update(float dpadX, float dpadY, bool shoulderHeld)
        {
            var now = MelodyBarInput.DirectionOf(dpadX, dpadY);
            if (now == _held)
            {
                return MelodyBarInput.NoTone;
            }

            _held = now;
            return MelodyBarInput.ToneForPad(now, shoulderHeld);
        }

        /// <summary>Forgets a held direction (the bar closed).</summary>
        public void Reset() => _held = PadDirection.None;
    }
}
