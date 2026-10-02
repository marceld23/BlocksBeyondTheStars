// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>A body's atmosphere as seen from orbit (#2170).</summary>
    public readonly struct OrbitAtmosphere
    {
        public OrbitAtmosphere(bool hasAir, float cloudDensity, int cloudRgb, int skyRgb, bool breathable)
        {
            HasAir = hasAir;
            CloudDensity = cloudDensity;
            CloudRgb = cloudRgb;
            SkyRgb = skyRgb;
            Breathable = breathable;
        }

        /// <summary>The body has air (breathable or toxic) — a haze rim and, with clouds, a cloud shell.</summary>
        public bool HasAir { get; }

        /// <summary>The planet's base cloud cover 0..1 (0 on airless bodies) — exactly the surface's value.</summary>
        public float CloudDensity { get; }

        /// <summary>The world's own cloud tint (0xRRGGBB).</summary>
        public int CloudRgb { get; }

        /// <summary>The world's own daytime sky hue (0xRRGGBB) — the colour of its atmosphere rim.</summary>
        public int SkyRgb { get; }

        /// <summary>Breathable air (a denser, brighter rim than a toxic one).</summary>
        public bool Breathable { get; }

        /// <summary>True when a cloud shell is drawn at all.</summary>
        public bool HasClouds => HasAir && CloudDensity > 0.001f;
    }

    /// <summary>
    /// The orbit look of a body's atmosphere from DATA (#2170): the same rules the server applies when the world is
    /// loaded — no clouds on airless and space-sky bodies, the type's <c>cloudDensity</c> otherwise, and the per-world
    /// sky and cloud tints of <see cref="AtmosphereTints"/>. The flight view used a hard-coded table that knew 15 of
    /// 51 types: ocean, meadow and boreal worlds flew past without a single cloud, and the airless crystal world wore
    /// a cloud shell it never has on the ground.
    /// </summary>
    public static class OrbitLook
    {
        /// <summary>The atmosphere of a body (by its location id, the key every per-world look is seeded with).</summary>
        public static OrbitAtmosphere For(PlanetType? planet, long worldSeed, string? bodyId)
        {
            if (planet is null || planet.Void)
            {
                return new OrbitAtmosphere(false, 0f, AtmosphereTints.DefaultCloudRgb, 0x8CBFF2, false);
            }

            bool airless = planet.IsAirless || planet.SpaceSky;
            int sky = AtmosphereTints.SkyRgb(worldSeed, bodyId, planet);
            int cloud = AtmosphereTints.CloudRgb(worldSeed, bodyId, planet, sky);
            float density = airless ? 0f : (float)Math.Clamp(planet.CloudDensity, 0.0, 1.0);
            bool breathable = string.Equals(planet.Atmosphere, "breathable", StringComparison.OrdinalIgnoreCase);
            return new OrbitAtmosphere(!airless, density, cloud, sky, breathable);
        }
    }
}
