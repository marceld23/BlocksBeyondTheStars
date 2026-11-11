// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The mix of the world sound emitters (#3494) — Unity-free so the headless tests can drive it. An emitter is a
    /// place that plays a loop which gets louder as the listener comes closer (a statue that sings across a valley):
    /// silent at its radius, rising smoothly, and at its own volume within <see cref="FullWithin"/> of the radius from
    /// the source. A strict budget: only the <see cref="MaxVoices"/> loudest emitters play, each on its own pooled
    /// voice, and never two with the same sound (two statues singing the same loop would only phase).
    /// </summary>
    public static class SoundEmitterMix
    {
        /// <summary>At most this many emitters play at once.</summary>
        public const int MaxVoices = 3;

        /// <summary>The share of an emitter's radius, measured from the source, within which it plays at its full volume.</summary>
        public const float FullWithin = 0.12f;

        /// <summary>An emitter as the mix sees it: where it stands (scene space), its sound and its reach.</summary>
        public readonly struct Emitter
        {
            public Emitter(float x, float y, float z, string sound, float radius, float volume)
            {
                X = x;
                Y = y;
                Z = z;
                Sound = sound ?? string.Empty;
                Radius = radius;
                Volume = volume;
            }

            public float X { get; }

            public float Y { get; }

            public float Z { get; }

            public string Sound { get; }

            public float Radius { get; }

            public float Volume { get; }
        }

        /// <summary>One pick of <see cref="Pick"/>: the emitter's index and the level it plays at (0..1).</summary>
        public readonly struct Voice
        {
            public Voice(int emitter, float level)
            {
                Emitter = emitter;
                Level = level;
            }

            public int Emitter { get; }

            public float Level { get; }
        }

        /// <summary>
        /// The level of an emitter at <paramref name="distance"/> blocks: 0 at and beyond <paramref name="radius"/>,
        /// <paramref name="volume"/> within <see cref="FullWithin"/> of it, and a smooth ease between — so the sound
        /// swells while walking up instead of switching on.
        /// </summary>
        public static float Level(float distance, float radius, float volume)
        {
            if (radius <= 0f || volume <= 0f || distance >= radius)
            {
                return 0f;
            }

            float full = radius * FullWithin;
            if (distance <= full)
            {
                return Math.Min(1f, volume);
            }

            float t = 1f - ((distance - full) / (radius - full)); // 0 at the rim, 1 at the full ring
            return Math.Min(1f, volume) * t * t * (3f - (2f * t));
        }

        /// <summary>
        /// Picks the emitters to play for a listener at (<paramref name="lx"/>, <paramref name="ly"/>,
        /// <paramref name="lz"/>): the loudest first, at most <see cref="MaxVoices"/>, one per sound. Fills
        /// <paramref name="picks"/> (cleared first).
        /// </summary>
        public static void Pick(IReadOnlyList<Emitter> emitters, float lx, float ly, float lz, List<Voice> picks)
        {
            picks.Clear();
            for (int i = 0; i < emitters.Count; i++)
            {
                var e = emitters[i];
                if (e.Sound.Length == 0)
                {
                    continue;
                }

                float dx = e.X - lx, dy = e.Y - ly, dz = e.Z - lz;
                float level = Level((float)Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz)), e.Radius, e.Volume);
                if (level > 0f)
                {
                    picks.Add(new Voice(i, level));
                }
            }

            // Loudest first; a tie keeps the list order, so the pick never flickers between two equal emitters.
            picks.Sort((a, b) => a.Level != b.Level ? b.Level.CompareTo(a.Level) : a.Emitter.CompareTo(b.Emitter));
            for (int i = 0; i < picks.Count; i++)
            {
                for (int j = picks.Count - 1; j > i; j--)
                {
                    if (emitters[picks[j].Emitter].Sound == emitters[picks[i].Emitter].Sound)
                    {
                        picks.RemoveAt(j);
                    }
                }
            }

            if (picks.Count > MaxVoices)
            {
                picks.RemoveRange(MaxVoices, picks.Count - MaxVoices);
            }
        }
    }
}
