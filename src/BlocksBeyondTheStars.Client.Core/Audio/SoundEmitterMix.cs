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
        /// <summary>At most this many emitters play at once (four since the lively places: a village has a tavern, a
        /// workshop, a well and a market within earshot of one another).</summary>
        public const int MaxVoices = 4;

        /// <summary>The local day fractions the time gates use — the hours a place keeps (the villagers' routine).</summary>
        public const float DayFrom = 0.24f;
        public const float EveningFrom = 0.62f;
        public const float NightFrom = 0.80f;

        /// <summary>How long (in day fraction) a gate takes to open or close, so a tavern's murmur swells and fades.</summary>
        public const float GateRamp = 0.02f;

        /// <summary>
        /// How much of an emitter's level is left at a time of day, 0..1: 1 inside its hours (<paramref name="when"/>, the
        /// wire's <c>SoundEmitterTimes</c>: 1 day, 2 evening, 3 night, 4 day and evening), easing in and out over
        /// <see cref="GateRamp"/>; always 1 for "always" (0) and for a negative <paramref name="timeOfDay"/> (unknown).
        /// </summary>
        public static float TimeGate(byte when, float timeOfDay)
        {
            if (when == 0 || timeOfDay < 0f)
            {
                return 1f;
            }

            float t = timeOfDay - (float)Math.Floor(timeOfDay);
            switch (when)
            {
                case 1: return Window(t, DayFrom, EveningFrom);
                case 2: return Window(t, EveningFrom, NightFrom);
                case 3: return Math.Max(Window(t, NightFrom, 1f + GateRamp), Window(t, -GateRamp, DayFrom));
                case 4: return Window(t, DayFrom, NightFrom);
                default: return 1f; // a value this client does not know: play
            }
        }

        private static float Window(float t, float from, float to)
        {
            if (t <= from - GateRamp || t >= to + GateRamp)
            {
                return 0f;
            }

            float up = Math.Min(1f, (t - (from - GateRamp)) / GateRamp);
            float down = Math.Min(1f, ((to + GateRamp) - t) / GateRamp);
            return Math.Max(0f, Math.Min(up, down));
        }

        /// <summary>The share of an emitter's radius, measured from the source, within which it plays at its full volume.</summary>
        public const float FullWithin = 0.12f;

        /// <summary>An emitter as the mix sees it: where it stands (scene space), its sound and its reach.</summary>
        public readonly struct Emitter
        {
            public Emitter(float x, float y, float z, string sound, float radius, float volume, byte when = 0)
            {
                X = x;
                Y = y;
                Z = z;
                Sound = sound ?? string.Empty;
                Radius = radius;
                Volume = volume;
                When = when;
            }

            /// <summary>When it plays (the wire's <c>SoundEmitterTimes</c>; 0 = always).</summary>
            public byte When { get; }

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
        /// <paramref name="picks"/> (cleared first). <paramref name="timeOfDay"/> is the local day fraction there (negative
        /// = unknown): an emitter outside its hours (<see cref="TimeGate"/>) is silent and takes no voice.
        /// </summary>
        public static void Pick(IReadOnlyList<Emitter> emitters, float lx, float ly, float lz, List<Voice> picks, float timeOfDay = -1f)
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
                float level = Level((float)Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz)), e.Radius, e.Volume) * TimeGate(e.When, timeOfDay);
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
