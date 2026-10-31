// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// How a season looks (#3042, Justus: "in autumn the leaves change colour, in summer it is hot, in spring the flowers bloom
    /// and buds open"). Unity-free, so it is unit-tested; <c>Sky</c> eases toward these targets and hands them to the shaders
    /// as <c>_Sc_Season</c> (no chunk is re-meshed). Every change runs over the first part of its season, so the year turns
    /// gradually rather than at a stroke.
    /// <list type="bullet">
    /// <item><b>Autumn</b> (<see cref="Target"/>.Autumn): the leaves turn amber and red over the first third and stay so;
    /// in winter the last of it fades.</item>
    /// <item><b>Winter</b> (.Winter): the plants pale; with the air below <see cref="FrostBelowC"/> °C a light frost lies on
    /// the tops (<see cref="Frost"/>). Spring melts it over its first quarter.</item>
    /// <item><b>Spring</b> (.Bloom): the blossoms open from a tenth into the season and are in full bloom until its end.</item>
    /// <item><b>Grade</b>: summer and autumn warm the picture, winter cools and greys it, spring is a touch more colourful.</item>
    /// </list>
    /// </summary>
    public static class SeasonLook
    {
        /// <summary>Air temperature (°C) below which a winter frost dusts the tops.</summary>
        public const float FrostBelowC = 3f;

        /// <summary>The frost's strength on the snow channel (the winter festivals use 0.55; a frost is lighter).</summary>
        public const float FrostAmount = 0.3f;

        /// <summary>The shader targets for a season at <paramref name="progress"/> (0..1) into it: autumn leaves, winter
        /// paleness, spring bloom — each 0..1. A world without seasons is all zero.</summary>
        public static (float Autumn, float Winter, float Bloom) Target(string? season, float progress)
        {
            float p = Math.Max(0f, Math.Min(1f, progress));
            return season switch
            {
                PlanetSeasons.Autumn => (Smooth(0f, 0.35f, p), 0f, 0f),
                PlanetSeasons.Winter => (0.3f * (1f - Smooth(0f, 0.3f, p)), Smooth(0f, 0.3f, p), 0f),
                PlanetSeasons.Spring => (0f, 1f - Smooth(0f, 0.25f, p), Smooth(0.1f, 0.4f, p)),
                PlanetSeasons.Summer => (0f, 0f, 0.4f * (1f - Smooth(0f, 0.3f, p))),
                _ => (0f, 0f, 0f),
            };
        }

        /// <summary>Whether a winter frost lies on the tops: winter, and the air below <see cref="FrostBelowC"/> °C.</summary>
        public static bool Frost(string? season, float temperatureC)
            => season == PlanetSeasons.Winter && temperatureC < FrostBelowC;

        /// <summary>The season's pull on the colour grade: warmth (−1 cool … +1 warm, the share of the warm/cool tint) and a
        /// saturation factor.</summary>
        public static (float Warmth, float Saturation) Grade(string? season) => season switch
        {
            PlanetSeasons.Summer => (0.25f, 1.06f),
            PlanetSeasons.Autumn => (0.3f, 1.04f),
            PlanetSeasons.Winter => (-0.35f, 0.88f),
            PlanetSeasons.Spring => (0f, 1.08f),
            _ => (0f, 1f),
        };

        private static float Smooth(float a, float b, float x)
        {
            float t = Math.Max(0f, Math.Min(1f, (x - a) / (b - a)));
            return t * t * (3f - (2f * t));
        }
    }
}
