// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// #3355: a Sounder NPC never speaks words — it hums. The living Sounders (the villagers of Mirun, the Eldest, the Young
    /// Voices) hum with <c>elder_hum_1..2</c>, the Echoes with <c>echo_hum_1..4</c>; VEGA translates. The Unity-free half of
    /// <c>NpcView</c>'s Sounder voice: the clip ids, the plan of a hum (one clip per ~40 characters of the line, at most three,
    /// never the same clip twice in a row) and the bubble's text — the hum, not the words.
    /// </summary>
    public static class SounderVoice
    {
        /// <summary>The living Sounders' hums (<c>Resources/audio/elder_hum_*</c>).</summary>
        public static readonly IReadOnlyList<string> ElderHums = new[] { "elder_hum_1", "elder_hum_2" };

        /// <summary>The Echoes' hums (<c>Resources/audio/echo_hum_*</c>).</summary>
        public static readonly IReadOnlyList<string> EchoHums = new[] { "echo_hum_1", "echo_hum_2", "echo_hum_3", "echo_hum_4" };

        /// <summary>The locale key of a humming Sounder's speech bubble.</summary>
        public const string HumKey = "npc.sounder.hum";

        /// <summary>The bubble when the locale table has no hum line.</summary>
        public const string HumFallback = "♪ ~ ♫ ~ ♪";

        /// <summary>The lowest stable per-Sounder pitch.</summary>
        public const float PitchMin = 0.94f;

        /// <summary>The highest stable per-Sounder pitch.</summary>
        public const float PitchMax = 1.06f;

        /// <summary>The hums of a living Sounder or (<paramref name="echo"/>) an Echo.</summary>
        public static IReadOnlyList<string> HumClips(bool echo) => echo ? EchoHums : ElderHums;

        /// <summary>
        /// The clip indices (0..<paramref name="clipCount"/>−1) a line of <paramref name="textLength"/> characters hums, in order:
        /// <see cref="NpcVoice.ClipCount"/> clips, never the same one twice in a row — nor the <paramref name="previous"/> one the
        /// Sounder ended its last hum with (−1 = none). Empty without clips.
        /// </summary>
        public static int[] HumPlan(int textLength, Random rng, int previous, int clipCount)
        {
            if (rng is null)
            {
                throw new ArgumentNullException(nameof(rng));
            }

            if (clipCount <= 0)
            {
                return Array.Empty<int>();
            }

            var plan = new int[NpcVoice.ClipCount(textLength)];
            int last = previous;
            for (int i = 0; i < plan.Length; i++)
            {
                last = Cutscenes.CutsceneVoice.PickClip(clipCount, last, (float)rng.NextDouble());
                plan[i] = last;
            }

            return plan;
        }

        /// <summary>A Sounder's own stable pitch for <paramref name="seed"/> (<see cref="PitchMin"/>..<see cref="PitchMax"/>) — the
        /// hums are recordings, so they stay close to their own pitch.</summary>
        public static float Pitch(int seed)
        {
            uint h = unchecked((uint)seed * 2654435761u);
            h ^= h >> 15;
            float roll = (h & 0xFFFFu) / 65535f;
            return PitchMin + (roll * (PitchMax - PitchMin));
        }
    }
}
