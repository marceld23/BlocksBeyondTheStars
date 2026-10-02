// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Weather;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>A body's weather projected onto its planet map, one entry per map pixel (#2175).</summary>
    public sealed class WeatherMapPixels
    {
        public int Width;
        public int Height;

        /// <summary>The true weather at the pixel (what the surface reports there) — index into
        /// <see cref="WeatherMapProjector.States"/>.</summary>
        public byte[] State = Array.Empty<byte>();

        /// <summary>The weather the views DRAW at the pixel: the true one, except inside a front's widened visual band
        /// (#2175 — fronts are only 1–4 map pixels wide, so they are drawn wider, feathered).</summary>
        public byte[] Visual = Array.Empty<byte>();

        /// <summary>What falls at the pixel under its drawn weather — index into <see cref="WeatherMapProjector.Precipitations"/>.</summary>
        public byte[] Precip = Array.Empty<byte>();

        /// <summary>Cloud cover 0..1 the views draw at the pixel (the surface sky's rule, fronts feathered).</summary>
        public float[] Cover = Array.Empty<float>();

        public void Resize(int width, int height)
        {
            if (width == Width && height == Height && Cover.Length == width * height)
            {
                return;
            }

            Width = width;
            Height = height;
            int n = width * height;
            State = new byte[n];
            Visual = new byte[n];
            Precip = new byte[n];
            Cover = new float[n];
        }
    }

    /// <summary>
    /// Projects a body's live weather onto its baked planet map (#2175): every pixel runs the SAME per-position formula
    /// the server applies on the surface (<see cref="WeatherProjection"/>: the world episode + the biome's offset +
    /// fronts + the summit rule), so the cloud shell over a region and the weather you land in there agree. Pure
    /// integer/float maths over the map — run once per weather snapshot (or every couple of seconds for the planet
    /// you are near, as fronts drift), never per frame.
    /// </summary>
    public static class WeatherMapProjector
    {
        /// <summary>Minimum VISUAL half-width of a front as a share of the circumference (#2175): a front's true band
        /// (140–520 blocks) is 1–4 pixels on a 96-pixel map, so the views draw at least 4 % of the planet, feathered.
        /// The pad weather and the surface stay on the true width.</summary>
        public const double VisualFrontHalfWidthShare = 0.02;

        /// <summary>Every weather state, in a fixed order (the pixel indices point here).</summary>
        public static readonly string[] States = BuildStates();

        /// <summary>Every precipitation form, in a fixed order.</summary>
        public static readonly string[] Precipitations =
        {
            "none", "rain", "drizzle", "snow", "hail", "sleet", "ash", "sandstorm", "acid", "dust", "meteor", "spores",
        };

        private static string[] BuildStates()
        {
            var list = new List<string>();
            foreach (var d in WeatherCatalog.Ladder)
            {
                list.Add(d.Key);
            }

            foreach (var d in WeatherCatalog.Events)
            {
                list.Add(d.Key);
            }

            return list.ToArray();
        }

        /// <summary>The index of a state in <see cref="States"/> (0 = clear for an unknown key).</summary>
        public static byte StateIndex(string? state)
        {
            for (int i = 0; i < States.Length; i++)
            {
                if (string.Equals(States[i], state, StringComparison.Ordinal))
                {
                    return (byte)i;
                }
            }

            return 0;
        }

        private static byte PrecipIndex(string? precip)
        {
            for (int i = 0; i < Precipitations.Length; i++)
            {
                if (string.Equals(Precipitations[i], precip, StringComparison.Ordinal))
                {
                    return (byte)i;
                }
            }

            return 0;
        }

        /// <summary>Projects the weather onto <paramref name="map"/> into <paramref name="output"/> (resized to the map).
        /// <paramref name="cloudDensity"/> is the planet's base cover (0 on airless bodies: no clouds at all).</summary>
        public static void Project(PlanetMapData map, in WeatherEpisode episode, IReadOnlyList<WeatherFrontState>? fronts,
            long worldSeed, double systemTimeDays, PlanetType? planet, float cloudDensity, string? episodePrecip,
            WeatherMapPixels output)
        {
            if (map is null || output is null)
            {
                return;
            }

            output.Resize(map.Width, map.Height);
            bool airless = planet is null || planet.IsAirless || planet.SpaceSky;
            double cloudLine = WeatherProjection.CloudLineY(planet);
            bool ladder = WeatherCatalog.Find(episode.State)?.IsLadder ?? true;
            int circ = Math.Max(1, map.Circumference);
            double visualMin = circ * VisualFrontHalfWidthShare;

            // Per-biome offsets are constant across the map: one lookup per biome index.
            var offsets = new int[256];
            for (int b = 0; b < offsets.Length; b++)
            {
                offsets[b] = WeatherProjection.BiomeOffset(worldSeed, b, systemTimeDays);
            }

            for (int x = 0; x < map.Width; x++)
            {
                int wx = map.WorldX(x);
                int trueBoost = ladder ? WeatherProjection.FrontBoost(fronts, wx, circ) : 0;
                var (visualBoost, weight) = ladder ? VisualFront(fronts, wx, circ, visualMin) : (0, 0f);

                for (int y = 0; y < map.Height; y++)
                {
                    int i = y * map.Width + x;
                    int offset = ladder ? offsets[map.Biomes[i]] : 0;
                    bool summit = ladder && map.Heights[i] + 1 > cloudLine;
                    var (state, _) = WeatherProjection.At(episode, offset, trueBoost, summit);
                    string drawn = state;
                    float cover = airless ? 0f : WeatherLook.Cover(state, cloudDensity);
                    if (weight > 0f && visualBoost > trueBoost)
                    {
                        var (wide, _) = WeatherProjection.At(episode, offset, visualBoost, summit);
                        float wideCover = airless ? 0f : WeatherLook.Cover(wide, cloudDensity);
                        cover += (wideCover - cover) * weight;
                        if (weight >= 0.5f)
                        {
                            drawn = wide;
                        }
                    }

                    float temp = map.Temperatures[i] + (WeatherCatalog.Find(drawn)?.TempDelta ?? 0f);
                    output.State[i] = StateIndex(state);
                    output.Visual[i] = StateIndex(drawn);
                    output.Precip[i] = PrecipIndex(WeatherProjection.Precipitation(drawn, planet, temp, episodePrecip));
                    output.Cover[i] = cover;
                }
            }
        }

        /// <summary>The strongest front whose WIDENED band covers <paramref name="x"/> and how strongly (1 inside the
        /// true width, feathering to 0 at the visual edge).</summary>
        public static (int Boost, float Weight) VisualFront(IReadOnlyList<WeatherFrontState>? fronts, double x, int circumference, double minHalfWidth)
        {
            if (fronts is null)
            {
                return (0, 0f);
            }

            int bestBoost = 0;
            float bestWeight = 0f;
            double circ = Math.Max(1, circumference);
            for (int i = 0; i < fronts.Count; i++)
            {
                var f = fronts[i];
                double d = Math.Abs(x - f.CenterX);
                d = Math.Min(d, circ - d);
                double outer = Math.Max(f.HalfWidth, minHalfWidth);
                if (d > outer)
                {
                    continue;
                }

                float w = d <= f.HalfWidth ? 1f : (float)(1.0 - (d - f.HalfWidth) / Math.Max(1.0, outer - f.HalfWidth));
                w = w * w * (3f - 2f * w); // smooth feather
                if (f.Boost > bestBoost || (f.Boost == bestBoost && w > bestWeight))
                {
                    bestBoost = f.Boost;
                    bestWeight = w;
                }
            }

            return (bestBoost, bestWeight);
        }
    }
}
