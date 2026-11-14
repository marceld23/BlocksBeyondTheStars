// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The clouds of the landing and departure cover lit like the planet's own (#3635). The cover (<c>TransitionCover</c>)
    /// used to draw the destination's cloud colour at its daytime base — the same function and inputs as the server
    /// (<see cref="OrbitLook"/>), but unlit: a landing at night or into a storm showed bright clouds that turned dark on
    /// arrival. Now one rule lights the dome clouds (<c>Clouds</c>), the cloud sea (<c>CloudSea</c>) and the cover:
    /// <see cref="DeckColour"/> — the cloud colour (with a quarter of the world's mist colour for the sea and the cover),
    /// 35 % bright at midnight up to full at noon, darkened by the weather's cloud deck, a touch of the star's colour by day
    /// and more at low sun. The day factor is the sky's (<see cref="DayOf"/>) at the local time of the landing point
    /// (<see cref="LocalTime"/>, the rule of <see cref="PlanetSpin"/>). Pure, so the view code only gathers the inputs.
    /// </summary>
    public static class CoverLook
    {
        /// <summary>The share of the world's own mist colour in the cover's clouds — the cloud sea's (#3272).</summary>
        public const float MistShare = 0.25f;

        /// <summary>The local time (0..1) at map longitude <paramref name="longitude"/> (x / circumference) of a body whose
        /// clock reads <paramref name="timeOfDay"/>: the time plus the longitude, wrapped (the surface sky and the pad map's
        /// day/night band use the same rule).</summary>
        public static double LocalTime(double timeOfDay, double longitude)
        {
            double t = timeOfDay + longitude;
            return t - Math.Floor(t);
        }

        /// <summary>The sky's day curve: 0 at midnight, 0.5 at sunrise and sunset, 1 at noon — the sun height
        /// <c>sin((t − 0.25)·2π)</c> mapped to 0..1 (Sky, Clouds and CloudSea read the same).</summary>
        public static float DayOf(double localTime)
        {
            double t = localTime - Math.Floor(localTime);
            return Clamp01((float)(Math.Sin((t - 0.25) * 2.0 * Math.PI) * 0.5 + 0.5));
        }

        /// <summary>A cloud deck's lit colour: <paramref name="cloudRgb"/> (0xRRGGBB) with <paramref name="mistShare"/> of the
        /// world's mist colour mixed in when it has one (<paramref name="mistRgb"/> ≥ 0), (0.35 + 0.65 · day) bright times the
        /// weather's <paramref name="darken"/> (<see cref="CloudDeck.Target"/>), then 10 % (noon) to 35 % (midnight) toward the
        /// star's colour <paramref name="sunRgb"/>.</summary>
        public static MistRgb DeckColour(int cloudRgb, int mistRgb, float mistShare, int sunRgb, float day, float darken)
        {
            MistRgb own = MistRgb.FromPacked(cloudRgb);
            if (mistRgb >= 0 && mistShare > 0f)
            {
                own = MistRgb.Lerp(own, MistRgb.FromPacked(mistRgb), mistShare);
            }

            float d = Clamp01(day);
            MistRgb lit = own.Scale((0.35f + (0.65f * d)) * darken);
            return MistRgb.Lerp(lit, lit.Times(MistRgb.FromPacked(sunRgb)), 0.1f + (0.25f * (1f - d)));
        }

        /// <summary>The sky behind the cover's clouds (the wash under them): the world's <paramref name="skyRgb"/> by day,
        /// dimmed by the weather, easing into the night's dark blue-grey (<see cref="MistLook.NightMist"/>) as the light goes —
        /// never below the night floor.</summary>
        public static MistRgb SkyColour(int skyRgb, float day, float darken)
        {
            float d = Clamp01(day);
            float dayLit = d * (2f - d);
            return MistRgb.Lerp(MistLook.NightMist, MistRgb.FromPacked(skyRgb).Scale(darken), dayLit).AtLeast(MistLook.NightFloor);
        }

        /// <summary>The cover over a body with air: its clouds (<see cref="DeckColour"/> with <see cref="MistShare"/> of the
        /// mist) and the sky behind them, for the hour <paramref name="day"/> at the landing point and the body's weather state
        /// <paramref name="weather"/> (null or "clear" = fair).</summary>
        public static (MistRgb Cloud, MistRgb Sky) For(OrbitAtmosphere atmosphere, int mistRgb, int sunRgb, float day, string? weather)
        {
            float darken = CloudDeck.Target(weather, atmosphere.CloudDensity).Darken;
            return (DeckColour(atmosphere.CloudRgb, mistRgb, MistShare, sunRgb, day, darken), SkyColour(atmosphere.SkyRgb, day, darken));
        }

        private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
    }
}
