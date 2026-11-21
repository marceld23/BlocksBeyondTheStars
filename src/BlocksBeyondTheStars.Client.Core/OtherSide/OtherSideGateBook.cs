// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Networking.Messages;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>One rune gate the client draws a light over.</summary>
    public readonly struct OtherSideGateMark
    {
        public OtherSideGateMark(float x, float y, float z, bool cracked, bool entry)
        {
            X = x;
            Y = y;
            Z = z;
            Cracked = cracked;
            Entry = entry;
        }

        /// <summary>The gate's centre, world coordinates.</summary>
        public float X { get; }

        /// <summary>See <see cref="X"/>.</summary>
        public float Y { get; }

        /// <summary>See <see cref="X"/>.</summary>
        public float Z { get; }

        /// <summary>A crack runs through it.</summary>
        public bool Cracked { get; }

        /// <summary>The gate the player came through.</summary>
        public bool Entry { get; }
    }

    /// <summary>
    /// The rune gates around the player while over there (#3906), as the server last listed them
    /// (<see cref="OtherSideGates"/>): a small fixed book the gate lights are drawn from. The server paces the list; the
    /// client only keeps it. At most <see cref="Capacity"/> gates are kept (the server sends fewer), nearest first.
    /// </summary>
    public sealed class OtherSideGateBook
    {
        /// <summary>The most gates the book keeps — and the size of the client's pool of lights.</summary>
        public const int Capacity = 32;

        private readonly OtherSideGateMark[] _gates = new OtherSideGateMark[Capacity];

        /// <summary>How many gates are listed.</summary>
        public int Count { get; private set; }

        /// <summary>Moves whenever the list was replaced or cleared.</summary>
        public int Version { get; private set; }

        /// <summary>The gate at <paramref name="index"/> (0 … <see cref="Count"/> − 1).</summary>
        public OtherSideGateMark this[int index] => _gates[index];

        /// <summary>Replaces the list with the server's (null and empty lists clear it).</summary>
        public void Apply(OtherSideGates? m)
        {
            Version++;
            Count = 0;
            var list = m?.Gates;
            if (list is null)
            {
                return;
            }

            for (int i = 0; i < list.Length && Count < Capacity; i++)
            {
                var g = list[i];
                if (g is null || !IsFinite(g.X) || !IsFinite(g.Y) || !IsFinite(g.Z))
                {
                    continue; // a broken entry draws nothing
                }

                _gates[Count++] = new OtherSideGateMark(g.X, g.Y, g.Z, g.Cracked, g.Entry);
            }
        }

        /// <summary>Forgets every gate.</summary>
        public void Clear()
        {
            if (Count == 0)
            {
                return;
            }

            Version++;
            Count = 0;
        }

        private static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
    }

    /// <summary>
    /// The numbers of the other side's look (#3906) that are worth testing without Unity: how far the grade goes, how a
    /// gate's light fades in, widens with distance and breathes.
    /// </summary>
    public static class OtherSideLook
    {
        /// <summary>The colour grade's saturation at full level (URP's scale): greyscale.</summary>
        public const float GreySaturation = -100f;

        /// <summary>Contrast added at full level (URP's scale) — white, grey and black read apart without colour.</summary>
        public const float ContrastLift = 14f;

        /// <summary>A gate light's height in blocks.</summary>
        public const float PillarHeight = 150f;

        /// <summary>A gate light's width in blocks where the player stands next to it.</summary>
        public const float PillarWidth = 2.6f;

        /// <summary>The saturation the grade writes: its own value on our side, greyscale over there.</summary>
        public static float Saturation(float graded, float level)
            => graded + ((GreySaturation - graded) * Clamp01(level));

        /// <summary>The contrast the grade writes: a little more over there.</summary>
        public static float Contrast(float graded, float level)
        {
            float v = graded + (ContrastLift * Clamp01(level));
            return v < -100f ? -100f : v > 100f ? 100f : v;
        }

        /// <summary>How wide a gate's light is drawn at <paramref name="distance"/> blocks: it grows with the distance, so
        /// a far gate stays a clear line instead of thinning to a pixel (up to six times its width).</summary>
        public static float PillarWidthAt(float distance)
        {
            float k = distance / 70f;
            return PillarWidth * (k < 1f ? 1f : k > 6f ? 6f : k);
        }

        /// <summary>A gate light's strength 0..1: the look's blend, the entry gate a little brighter and breathing slowly
        /// (steady with reduced motion), every other gate calm.</summary>
        public static float PillarStrength(float look, bool entry, double time, bool reducedMotion)
        {
            float level = Clamp01(look);
            if (!entry)
            {
                return level * 0.62f;
            }

            float wave = reducedMotion ? 1f : 0.82f + (0.18f * (float)Math.Sin(time * 2.2));
            return level * wave;
        }

        private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
    }
}
