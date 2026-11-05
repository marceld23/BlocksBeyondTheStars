// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>The colours of wind-blown grit on one world (#3203): two sRGB colours (0..1), each grain a random mix of
    /// them. The Unity side converts them for the linear pipeline and dims them by the world light.</summary>
    public readonly struct GritLook
    {
        public GritLook(string name, float r, float g, float b, float r2, float g2, float b2)
        {
            Name = name;
            R = r;
            G = g;
            B = b;
            R2 = r2;
            G2 = g2;
            B2 = b2;
        }

        /// <summary>A short name for logs and tests ("grit", "spindrift", "rust_grit").</summary>
        public string Name { get; }

        public float R { get; }

        public float G { get; }

        public float B { get; }

        public float R2 { get; }

        public float G2 { get; }

        public float B2 { get; }
    }

    /// <summary>
    /// The rules of the falling and blown weather particles (#3203), Unity-free so the headless tests pin them: the
    /// particle shape from a style's proportions (<see cref="Shape"/>), the horizontal drive along the world's wind
    /// (<see cref="Drive"/>), and for the wind-blown grit of a gale or sandstorm its per-world colour
    /// (<see cref="LookFor"/>), the per-grain variation and the fades that keep grains from popping in and out. The
    /// Unity side is <c>WeatherFx3D</c>; the server keeps sending the precipitation forms "dust" (gale) and "sandstorm".
    /// </summary>
    public static class WindGrit
    {
        /// <summary>Metres per second of horizontal drive a full wind (1.0) adds to a style's own slant.</summary>
        public const float WindGain = 12f;

        /// <summary>A fresh grain fades in over this many seconds (grit spawns in the open air around the camera).</summary>
        public const float FadeInSeconds = 0.25f;

        /// <summary>Grains fade out over the last metres before the radius they die at.</summary>
        public const float EdgeBand = 4f;

        /// <summary>Grains closer to the camera than this start to fade, so none smears across the lens.</summary>
        public const float NearFadeStart = 1.5f;

        /// <summary>Grains this close to the camera are fully faded.</summary>
        public const float NearCut = 0.35f;

        /// <summary>Seconds the whole effect takes to come up when the weather starts.</summary>
        public const float EnvelopeInSeconds = 1.2f;

        /// <summary>Seconds the whole effect takes to die away when the weather ends (it used to vanish in one frame).</summary>
        public const float EnvelopeOutSeconds = 2.5f;

        /// <summary>Air at or below this temperature (°C) whips up snow, not dust.</summary>
        public const float FreezingAir = -8f;

        /// <summary>Plain grit — a world whose ground colour is not known yet.</summary>
        public static readonly GritLook Plain = new GritLook("grit", 0.74f, 0.68f, 0.55f, 0.84f, 0.78f, 0.64f);

        /// <summary>White spindrift — snow and ice crystals blown off a frozen world.</summary>
        public static readonly GritLook Spindrift = new GritLook("spindrift", 0.86f, 0.91f, 0.98f, 0.97f, 0.98f, 1.00f);

        private const float RustR = 0.80f, RustG = 0.44f, RustB = 0.28f;

        /// <summary>True for the precipitation forms that are blown grit rather than falling water or flakes.</summary>
        public static bool IsGrit(string? precipitation) => precipitation is "dust" or "sandstorm";

        /// <summary>The particle shape for a style's proportions (x, y, z in metres): a flake is a round billboard of
        /// <c>max(x, z) × 1.6</c>; a streak is as wide as its THIN axis and stretched along its long one. Rain keeps its
        /// long axis in y, the blown grit in z — #3203: the old rule took the widest of x and z as the width, so every
        /// grit grain drew as a one-metre square (dust 0.12 × 0.54 m now, sandstorm 0.15 × 0.45 m; rain unchanged).</summary>
        public static (float Size, float LengthScale) Shape(float x, float y, float z, bool flake)
        {
            if (flake)
            {
                return (MathF.Max(x, z) * 1.6f, 1f);
            }

            float thin = MathF.Max(0.005f, MathF.Min(x, z));
            float longest = MathF.Max(x, MathF.Max(y, z));
            return (thin * 3f, MathF.Max(1f, longest / thin * 0.5f));
        }

        /// <summary>The horizontal velocity (m/s, world X and Z) of the blown grit: along the world's wind direction — the
        /// same direction the grass sways and the cloud shadows drift — at the style's own <paramref name="baseSpeed"/>
        /// plus <see cref="WindGain"/> per unit of wind. The wind speed is applied once (the old rule squared it, because
        /// the wind vector already carried the speed).</summary>
        public static (float X, float Z) Drive(float windDirection, float windSpeed, float baseSpeed)
        {
            float speed = baseSpeed + (Clamp01(windSpeed) * WindGain);
            return (MathF.Cos(windDirection) * speed, MathF.Sin(windDirection) * speed);
        }

        /// <summary>The horizontal velocity (m/s) of everything that FALLS (rain, snow, hail, ash, spores …): along the wind
        /// direction like the grit, but with its historical strength — the style's own <paramref name="slant"/> plus
        /// <see cref="WindGain"/> × wind² (the old <c>WindVector × wind × 12</c>), so rain and snow do not blow harder than
        /// they always did. Before #3203 the slant pointed at world +X whatever the wind did.</summary>
        public static (float X, float Z) FallDrive(float windDirection, float windSpeed, float slant)
        {
            float wind = Clamp01(windSpeed);
            float speed = slant + (wind * wind * WindGain);
            return (MathF.Cos(windDirection) * speed, MathF.Sin(windDirection) * speed);
        }

        /// <summary>
        /// The grit colours for a world: white spindrift on a frozen world (a tundra flora theme, an ice planet type or
        /// air at or below <see cref="FreezingAir"/>), otherwise the colour of its ground — the surface block's average
        /// atlas colour, toned down a little and lifted out of the dark so airborne grit reads against the land —
        /// pulled toward rust on the red worlds. Without a known ground colour it is <see cref="Plain"/>.
        /// </summary>
        public static GritLook LookFor(string? planetType, string? floraTheme, float temperature,
            bool hasGround, float groundR, float groundG, float groundB)
        {
            string type = (planetType ?? string.Empty).ToLowerInvariant();
            if (IsFrozen(type, floraTheme, temperature))
            {
                return Spindrift;
            }

            float r = Plain.R, g = Plain.G, b = Plain.B;
            if (hasGround)
            {
                FromGround(groundR, groundG, groundB, out r, out g, out b);
            }

            string name = hasGround ? "ground_grit" : Plain.Name;
            if (type is "red_desert" or "badlands" or "scrapyard")
            {
                r = Lerp(r, RustR, 0.5f);
                g = Lerp(g, RustG, 0.5f);
                b = Lerp(b, RustB, 0.5f);
                name = "rust_grit";
            }

            if (!hasGround && name == Plain.Name)
            {
                return Plain;
            }

            // The second colour is a lighter, sun-bleached version of the first.
            return new GritLook(name, r, g, b, Lerp(r, 1f, 0.22f), Lerp(g, 1f, 0.22f), Lerp(b, 1f, 0.22f));
        }

        /// <summary>A grain's size multiplier (0.6..1.4, fixed per pool slot) — no two neighbouring grains alike.</summary>
        public static float GrainSize(int index) => 0.6f + (0.8f * Hash01(index, 0x51));

        /// <summary>A grain's opacity (0.3..0.8, fixed per pool slot).</summary>
        public static float GrainAlpha(int index) => 0.3f + (0.5f * Hash01(index, 0xA7));

        /// <summary>A grain's mix between the look's two colours (0..1, fixed per pool slot).</summary>
        public static float GrainMix(int index) => Hash01(index, 0x3C);

        /// <summary>A grain's visibility 0..1: it fades in over <see cref="FadeInSeconds"/> after it spawned, out over the
        /// last <see cref="EdgeBand"/> metres before <paramref name="killRadius"/>, and out when it passes within
        /// <see cref="NearFadeStart"/> of the camera.</summary>
        public static float GrainFade(float age, float distance, float killRadius)
        {
            float fadeIn = Clamp01(age / FadeInSeconds);
            float edge = Clamp01((killRadius - distance) / EdgeBand);
            float near = Clamp01((distance - NearCut) / (NearFadeStart - NearCut));
            return fadeIn * edge * near;
        }

        /// <summary>One step of the whole effect's envelope (0..1): up over <see cref="EnvelopeInSeconds"/> while the
        /// weather blows, down over <see cref="EnvelopeOutSeconds"/> once it ends.</summary>
        public static float Envelope(float current, bool active, float dt)
        {
            if (!(dt > 0f))
            {
                return Clamp01(current);
            }

            float target = active ? 1f : 0f;
            float step = dt / (active ? EnvelopeInSeconds : EnvelopeOutSeconds);
            float delta = target - current;
            return Math.Abs(delta) <= step ? target : Clamp01(current + (Math.Sign(delta) * step));
        }

        private static bool IsFrozen(string type, string? floraTheme, float temperature)
        {
            if (type is "ice" or "frozen" or "glacier" or "icecap" or "frozen_ocean" or "tundra")
            {
                return true;
            }

            return string.Equals(floraTheme, "tundra", StringComparison.OrdinalIgnoreCase) || temperature <= FreezingAir;
        }

        private static void FromGround(float gr, float gg, float gb, out float r, out float g, out float b)
        {
            gr = Clamp01(gr);
            gg = Clamp01(gg);
            gb = Clamp01(gb);
            float lum = Luma(gr, gg, gb);
            // Airborne dust is paler and greyer than the ground it came from, and a little sandier.
            r = Lerp(Lerp(gr, lum, 0.35f), Plain.R, 0.25f);
            g = Lerp(Lerp(gg, lum, 0.35f), Plain.G, 0.25f);
            b = Lerp(Lerp(gb, lum, 0.35f), Plain.B, 0.25f);

            // Keep it readable: black sand still throws a dark grey, snow-white salt a soft off-white.
            float l = Luma(r, g, b);
            float scale = l < 0.28f ? 0.28f / MathF.Max(0.01f, l) : l > 0.9f ? 0.9f / l : 1f;
            r = Clamp01(r * scale);
            g = Clamp01(g * scale);
            b = Clamp01(b * scale);
        }

        private static float Luma(float r, float g, float b) => (0.299f * r) + (0.587f * g) + (0.114f * b);

        private static float Hash01(int index, int salt)
        {
            unchecked
            {
                uint h = (uint)(index * 374761393) ^ (uint)(salt * 668265263);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFF) / 65535f;
            }
        }

        private static float Lerp(float a, float b, float t) => a + ((b - a) * t);

        private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
    }
}
