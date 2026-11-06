// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>An sRGB colour, 0..1 per channel, for the Unity-free atmosphere rules (<see cref="MistLook"/>).</summary>
    public readonly struct MistRgb
    {
        public MistRgb(float r, float g, float b)
        {
            R = r;
            G = g;
            B = b;
        }

        public float R { get; }

        public float G { get; }

        public float B { get; }

        /// <summary>Perceived brightness (Rec. 601 weights).</summary>
        public float Luma => (0.299f * R) + (0.587f * G) + (0.114f * B);

        /// <summary>A colour packed 0xRRGGBB, as the environment message sends it.</summary>
        public static MistRgb FromPacked(int rgb)
            => new MistRgb(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);

        public static MistRgb Lerp(MistRgb a, MistRgb b, float t)
        {
            t = MistLook.Clamp01(t);
            return new MistRgb(a.R + ((b.R - a.R) * t), a.G + ((b.G - a.G) * t), a.B + ((b.B - a.B) * t));
        }

        public MistRgb Scale(float s) => new MistRgb(R * s, G * s, B * s);

        public MistRgb Times(MistRgb o) => new MistRgb(R * o.R, G * o.G, B * o.B);

        /// <summary>Each channel at least the floor's.</summary>
        public MistRgb AtLeast(MistRgb floor) => new MistRgb(Math.Max(R, floor.R), Math.Max(G, floor.G), Math.Max(B, floor.B));

        /// <summary>The hue alone: the brightest channel scaled to 1 (white for black).</summary>
        public MistRgb Hue()
        {
            float m = Math.Max(R, Math.Max(G, B));
            return m > 0.001f ? Scale(1f / m) : new MistRgb(1f, 1f, 1f);
        }
    }

    /// <summary>A weather's colour for the haze (#3270) and how strongly it tints the sky (the distance haze and the camera
    /// background follow the sky) and the mist.</summary>
    public readonly struct HazeTint
    {
        public HazeTint(MistRgb color, float skyWeight, float mistWeight)
        {
            Color = color;
            SkyWeight = skyWeight;
            MistWeight = mistWeight;
        }

        /// <summary>No tint.</summary>
        public static HazeTint None => new HazeTint(new MistRgb(1f, 1f, 1f), 0f, 0f);

        public MistRgb Color { get; }

        /// <summary>0..1 how far the sky (and with it the distance haze) moves toward <see cref="Color"/>.</summary>
        public float SkyWeight { get; }

        /// <summary>0..1 how far the mist and the cloud band move toward <see cref="Color"/>.</summary>
        public float MistWeight { get; }
    }

    /// <summary>
    /// The mist of a world as Unity-free rules (#3270–#3272): the height fog's strength, floor and falloff (#2406, now from
    /// the world's <c>MistAmount</c> instead of a biome switch — the server's value already carries the air density), the
    /// wisps that tear it into banks drifting with the wind, the lit colours of the mist and the cloud band, the cloud band
    /// itself and the colour a fog, dust or ion-storm episode gives the haze. <c>Sky.Mist.cs</c> turns them into the shader
    /// globals; the shader (<c>AtmosphereCommon.hlsl</c>) repeats the few per-pixel formulas, and
    /// <c>MistLookTests</c> pins the shared constants against its source. Purely visual: nothing here touches gameplay.
    /// </summary>
    public static class MistLook
    {
        /// <summary>The wisps' bank threshold on the even (uniform) noise sheets: below <see cref="WispLow"/> is a gap (30 % of
        /// the area at full strength), above <see cref="WispHigh"/> a full bank (shader: <c>BBTS_WISP_LOW</c> /
        /// <c>BBTS_WISP_HIGH</c>). Symmetric about 0.5, so the banks hold the mist the smooth blanket had.</summary>
        public const float WispLow = 0.3f, WispHigh = 0.7f;

        /// <summary>How much the wisp pattern shifts per metre of height (in metres across the ground), so a bank's upper
        /// part does not sit exactly over its foot (shader: <c>BBTS_WISP_SHEAR</c>).</summary>
        public const float WispShearX = 0.35f, WispShearZ = -0.2f;

        /// <summary>Seconds for one full morph between the two noise sheets — the banks slowly change shape.</summary>
        public const float MorphPeriod = 150f;

        /// <summary>The metres right in front of the camera the cloud band never fogs (shader: <c>BBTS_BAND_CLEAR</c>).</summary>
        public const float BandClearMetres = 6f;

        /// <summary>The band's extinction per metre: <see cref="BandExtinctionBase"/> + <see cref="BandExtinctionCover"/> ×
        /// cover (shader: <c>BBTS_BAND_K0</c> / <c>BBTS_BAND_K1</c>).</summary>
        public const float BandExtinctionBase = 0.04f, BandExtinctionCover = 0.03f;

        /// <summary>The band's soft edges: this share of its thickness at the base and at the top.</summary>
        public const float BandEdgeShare = 0.25f;

        /// <summary>The band's noise holes: world blocks per tile of the green sheet, drifting with the cloud shadows' wind
        /// offset (shader: <c>BBTS_BAND_HOLE_TILE</c>).</summary>
        public const float BandHoleTile = 96f;

        /// <summary>Inside a full cloud the nearest this-many metres always stay readable (at most half hazed) — the kids'
        /// minimum visibility.</summary>
        public const float MinVisibleMetres = 15f;

        /// <summary>The daylight share the mist and the cloud band keep at night.</summary>
        public const float NightLight = 0.14f;

        /// <summary>The night's mist: a dark blue-grey, and the floor nothing falls below (never black).</summary>
        public static readonly MistRgb NightMist = new MistRgb(0.09f, 0.105f, 0.15f);

        public static readonly MistRgb NightFloor = new MistRgb(0.035f, 0.04f, 0.055f);

        // The weather colours WeatherFx3D used to give Unity's fog (#3270: they never reached the custom shaders).
        private static readonly MistRgb FogGrey = new MistRgb(0.80f, 0.83f, 0.86f);
        private static readonly MistRgb IonBlue = new MistRgb(0.42f, 0.55f, 0.78f);
        private static readonly MistRgb AshSmoke = new MistRgb(0.34f, 0.26f, 0.22f);
        private static readonly MistRgb AcidGreen = new MistRgb(0.40f, 0.52f, 0.32f);
        private static readonly MistRgb WhiteOut = new MistRgb(0.74f, 0.78f, 0.84f);
        private static readonly MistRgb StormGrey = new MistRgb(0.45f, 0.50f, 0.58f);

        /// <summary>The mist's time of day: dawn mist (and a little less at dusk), some through the night, almost none at noon.
        /// <paramref name="timeOfDay"/> is the local 0..1 day fraction, <paramref name="day"/> the 0..1 sun-height daylight.</summary>
        public static float TimeFactor(float timeOfDay, float day)
        {
            float dawn = Math.Max(0f, 1f - (Math.Abs(timeOfDay - 0.27f) * 8f)) + (0.7f * Math.Max(0f, 1f - (Math.Abs(timeOfDay - 0.73f) * 8f)));
            return Math.Max(0.3f + (0.7f * Clamp01(dawn)), (1f - Clamp01(day)) * 0.6f);
        }

        /// <summary>Fog weather thickens the mist, rain a little, a storm's wind blows it away.</summary>
        public static float WeatherFactor(string? weather)
        {
            switch (weather ?? string.Empty)
            {
                case "fog":
                case "ground_fog":
                    return 1.6f;
                case "rain":
                case "drizzle":
                    return 1.2f;
                case "storm":
                case "gale":
                case "blizzard":
                    return 0.7f;
                default:
                    return 1f;
            }
        }

        /// <summary>Whether the weather is one of the two fog states.</summary>
        public static bool IsFogWeather(string? weather) => weather == "fog" || weather == "ground_fog";

        /// <summary>0..1 how strong the height fog is right now: the world's mist amount (0 on airless worlds) × the time of day
        /// × the weather.</summary>
        public static float HeightFogStrength(float mistAmount, float timeOfDay, float day, string? weather)
            => Clamp01(Clamp01(mistAmount) * TimeFactor(timeOfDay, day) * WeatherFactor(weather));

        /// <summary>Seconds of one slow swell of a persistent haze (<see cref="PersistentMist"/>).</summary>
        public const float SwellPeriod = 240f;

        /// <summary>The persistent haze's share of its floor at its thinnest (it swells back to the full floor).</summary>
        public const float SwellLow = 0.65f;

        /// <summary>The nearest the distance haze closes in at a full persistent haze, as a share of the render distance.</summary>
        public const float PersistentFarShare = 0.25f;

        /// <summary>0..1 the mist that never burns off (#3269 follow-up, the gas giants: <c>WorldEnvironment.MistFloor</c>):
        /// the world's amount × its floor, swelling and thinning slowly over the world clock (two incommensurate waves,
        /// <see cref="SwellLow"/>..1 of the floor) — sometimes less dense, never clear. 0 on a world without a floor.</summary>
        public static float PersistentMist(float mistAmount, float mistFloor, float worldTime)
        {
            if (mistFloor <= 0f || mistAmount <= 0f)
            {
                return 0f;
            }

            double t = worldTime * (2.0 * Math.PI / SwellPeriod);
            float wave = 0.5f + (0.5f * (float)((0.6 * Math.Sin(t)) + (0.4 * Math.Sin((t * 2.37) + 1.3))));
            float swell = SwellLow + ((1f - SwellLow) * Clamp01(wave));
            return Clamp01(Clamp01(mistAmount) * Clamp01(mistFloor) * swell);
        }

        /// <summary>The height fog with a persistent haze under it: never below <paramref name="persistent"/>, whatever the
        /// hour and the weather.</summary>
        public static float HeightFogStrength(float mistAmount, float timeOfDay, float day, string? weather, float persistent)
            => Math.Max(HeightFogStrength(mistAmount, timeOfDay, day, weather), Clamp01(persistent));

        /// <summary>Where the distance haze completes under a persistent haze: pulled in toward <see cref="PersistentFarShare"/>
        /// of <paramref name="renderDist"/> as the haze thickens; unchanged without one.</summary>
        public static float PersistentFar(float far, float renderDist, float persistent)
        {
            float p = Clamp01(persistent);
            return p <= 0f ? far : Math.Min(far, renderDist * (1f - ((1f - PersistentFarShare) * p)));
        }

        /// <summary>The world Y the mist is densest at: a little above the probe's valley floor, higher in fog weather.</summary>
        public static float FogFloor(float probeFloor, string? weather) => probeFloor + 1.5f + (IsFogWeather(weather) ? 3f : 0f);

        /// <summary>1/metres the density falls per metre above the floor — thicker mist reaches higher.</summary>
        public static float Falloff(float strength) => 1f / (6f + (6f * Clamp01(strength)));

        /// <summary>0..1 how torn the mist is (0 = the smooth blanket of #2406, exactly).</summary>
        public static float WispStrength(float wispiness, bool enabled) => enabled ? Clamp01(wispiness) : 0f;

        /// <summary>The wisp noise's frequency (1/metres): a calm world's banks are ~110 m apart, a torn one's ~50 m.</summary>
        public static float WispFrequency(float wispiness) => 1f / (110f - (60f * Clamp01(wispiness)));

        /// <summary>0..1 the morph between the two noise sheets at world time <paramref name="worldTime"/> (seconds).</summary>
        public static float Morph(float worldTime) => 0.5f + (0.5f * (float)Math.Sin(worldTime * (2.0 * Math.PI / MorphPeriod)));

        /// <summary>Mixing two independent sheets flattens the noise in between; this restores its contrast.</summary>
        public static float MorphContrast(float morph)
        {
            float m = Clamp01(morph);
            return 1f / (float)Math.Sqrt((m * m) + ((1f - m) * (1f - m)));
        }

        /// <summary>The wisp factor at one point (the shader's <c>BbtsWispAt</c>): 0..2 times the smooth density — 0 in a gap,
        /// 2 in a bank, about 1 on average — from the two even sheets' values there (green and blue of the noise texture).
        /// Strength 0 returns exactly 1.</summary>
        public static float WispFactor(float strength, float sheetA, float sheetB, float morph, float contrast)
        {
            float n = 0.5f + ((sheetA + ((sheetB - sheetA) * morph) - 0.5f) * contrast);
            float bank = SmoothStep(WispLow, WispHigh, n) * 2f;
            return 1f + ((bank - 1f) * Clamp01(strength));
        }

        /// <summary>The colour the height fog takes, lit: the world's own mist colour (0xRRGGBB) under the daylight — a bright,
        /// friendly tint by day, a dark blue-grey at night, never black and never glowing white — or, for −1 (no colour of its
        /// own), simply the sky colour, as every world had before #3271.</summary>
        public static MistRgb LitMist(int mistColor, MistRgb sky, MistRgb sun, float day, float weatherIntensity)
            => mistColor < 0 ? sky : Lit(MistRgb.FromPacked(mistColor), sky, sun, day, weatherIntensity);

        /// <summary>The cloud band's colour (#3272): the world's cloud colour lit like the mist, leaning a little toward the
        /// world's own mist colour when it has one, so standing in a cloud matches the valley mist.</summary>
        public static MistRgb LitCloud(int cloudColor, int mistColor, MistRgb sky, MistRgb sun, float day, float weatherIntensity)
        {
            MistRgb own = MistRgb.FromPacked(cloudColor);
            if (mistColor >= 0)
            {
                own = MistRgb.Lerp(own, MistRgb.FromPacked(mistColor), 0.3f);
            }

            return Lit(own, sky, sun, day, weatherIntensity);
        }

        private static MistRgb Lit(MistRgb own, MistRgb sky, MistRgb sun, float day, float weatherIntensity)
        {
            float d = Clamp01(day);
            float dayLit = d * (2f - d);
            float light = (NightLight + ((1f - NightLight) * dayLit)) * (1f - (0.18f * Clamp01(weatherIntensity)));
            MistRgb starTint = MistRgb.Lerp(new MistRgb(1f, 1f, 1f), sun.Hue(), 0.25f);
            MistRgb lit = own.Times(starTint).Scale(light);
            lit = MistRgb.Lerp(lit, sky, 0.25f);                    // the sky's own light: dusk glow, the night's blue
            lit = MistRgb.Lerp(lit, NightMist, (1f - dayLit) * 0.5f); // night: a dark blue-grey
            return lit.AtLeast(NightFloor);
        }

        /// <summary>The colour a weather gives the haze (#3270) — what <c>WeatherFx3D</c> used to give Unity's fog: soft grey
        /// murk in fog (the valley mist only in ground fog, so a hilltop still looks out over it), the ground's grit in a
        /// gale or a sandstorm (<paramref name="grit"/>), a charged blue in an ion storm, and in a storm the colour of what
        /// falls (smoky ash, acrid green acid, a white-out in snow, grey-blue rain). Scaled by the 0..1 intensity.</summary>
        public static HazeTint WeatherTint(string? weather, string? precipitation, float intensity, MistRgb grit)
        {
            float s = Clamp01(intensity);
            string precip = precipitation ?? string.Empty;
            if (precip == "sandstorm" || precip == "dust")
            {
                return new HazeTint(grit, 0.7f * s, 0.7f * s);
            }

            switch (weather ?? string.Empty)
            {
                case "fog":
                    return new HazeTint(FogGrey, 0.6f * s, 0.8f * s);
                case "ground_fog":
                    return new HazeTint(FogGrey, 0f, 0.6f * s);
                case "ion_storm":
                    return new HazeTint(IonBlue, 0.45f * s, 0.5f * s);
                case "storm":
                case "toxic_storm":
                case "blizzard":
                    float sky = weather == "blizzard" ? 0.55f : 0.35f;
                    return new HazeTint(StormColor(precip), sky * s, 0.5f * s);
                default:
                    return HazeTint.None;
            }
        }

        private static MistRgb StormColor(string precip)
        {
            switch (precip)
            {
                case "ash":
                    return AshSmoke;
                case "acid":
                    return AcidGreen;
                case "snow":
                case "sleet":
                case "hail":
                    return WhiteOut;
                default:
                    return StormGrey;
            }
        }

        /// <summary>Moves <paramref name="color"/> toward the tint's hue by <paramref name="weight"/>, keeping its brightness:
        /// a fog by day reads grey, the same fog at night a dark grey — a tint never makes the night glow.</summary>
        public static MistRgb ApplyTint(MistRgb color, MistRgb tint, float weight)
        {
            if (weight <= 0f)
            {
                return color;
            }

            float scale = color.Luma / Math.Max(tint.Luma, 1e-3f);
            return MistRgb.Lerp(color, tint.Scale(scale), weight);
        }

        /// <summary>0..1 how much of the world's cloud band is there right now: <see cref="CloudBandRules.Cover"/> when the
        /// world has a band and the player's "Cloud fog" switch is on, else 0.</summary>
        public static float BandCover(string? weather, float intensity, float cloudDensity, float baseY, float topY, bool enabled)
            => enabled && CloudBandRules.HasBand(baseY, topY) ? CloudBandRules.Cover(weather, intensity, cloudDensity) : 0f;

        /// <summary>The thickness of the band's soft edges (metres): a quarter of the band, never more than half of it.</summary>
        public static float BandEdge(float baseY, float topY) => Math.Max(0.5f, BandEdgeShare * (topY - baseY));

        /// <summary>0..1 the band's density at height <paramref name="y"/> (the shader's <c>BbtsBandDensity</c>): a trapezoid,
        /// 0 at the base and the top, 1 through the middle. 0 for a world without a band.</summary>
        public static float BandDensityAt(float y, float baseY, float topY)
        {
            if (!CloudBandRules.HasBand(baseY, topY))
            {
                return 0f;
            }

            float e = BandEdge(baseY, topY);
            return Clamp01((y - baseY) / e) * Clamp01((topY - y) / e);
        }

        /// <summary>0..1 how much the band fills a spot at noise value <paramref name="noise"/> (the even green sheet) and cover
        /// <paramref name="cover"/>: about the share <paramref name="cover"/> of the area is filled — a full cover closes the
        /// band completely, a patchy one leaves holes, 0 leaves nothing.</summary>
        public static float BandFill(float noise, float cover)
        {
            float t = 1.1f - (1.2f * Clamp01(cover));
            return SmoothStep(t - 0.08f, t + 0.08f, noise);
        }

        /// <summary>The band's extinction per metre at <paramref name="cover"/>.</summary>
        public static float BandExtinction(float cover) => BandExtinctionBase + (BandExtinctionCover * Clamp01(cover));

        /// <summary>0..1 how hazed a surface <paramref name="metres"/> away looks with the camera inside a filled band at
        /// <paramref name="cover"/> (the shader's band term on a level ray through the middle of the band): clear for the
        /// first <see cref="BandClearMetres"/>, about half at <see cref="MinVisibleMetres"/>, nearly white by ~40 m.</summary>
        public static float BandAmount(float metres, float cover)
            => 1f - (float)Math.Exp(-Math.Max(metres - BandClearMetres, 0f) * BandExtinction(cover));

        public static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;

        private static float SmoothStep(float edge0, float edge1, float x)
        {
            float t = Clamp01((x - edge0) / (edge1 - edge0));
            return t * t * (3f - (2f * t));
        }
    }
}
