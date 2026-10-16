// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The size of a star-map body as the SERVER sized it (#2691). From terrain generation 26 a body's circumference is
    /// seeded by the world, so the client no longer recomputes it in every view: the server sends each body's value
    /// (<see cref="NetBody.Circumference"/>) and every view that sizes a body — the orbit sphere, the sky disc, the map
    /// bake, the landing-pad map — reads it here. Only an older server sends none (0); it sized its worlds with the
    /// classic formula, so that is the fallback.
    /// </summary>
    public static class BodySize
    {
        /// <summary>Maps a NetBody's string kind + planet type to a size class (the server's
        /// <see cref="WorldConstants.SizeClassFor"/> on the wire strings).</summary>
        public static WorldConstants.WorldSizeClass ClassOf(string? kind, string? planetType)
            => WorldConstants.IsAsteroidType(planetType) ? WorldConstants.WorldSizeClass.Asteroid
             : kind == "Moon" ? WorldConstants.WorldSizeClass.Moon
             : WorldConstants.WorldSizeClass.Planet;

        /// <summary>The walkable circumference of <paramref name="body"/>: the server's value, else (an older server) the
        /// classic size from its id, class and archetype bias.</summary>
        public static int CircumferenceOf(NetBody body)
            => CircumferenceOf(body, body.Id, body.Kind, body.PlanetType);

        /// <summary>The walkable circumference of a body that may be missing from the star map: the server's value when
        /// <paramref name="body"/> carries one, else the classic size keyed by <paramref name="fallbackId"/> (or the body's
        /// id) in the class of <paramref name="kind"/> / <paramref name="planetType"/> — the exact numbers every view
        /// computed before #2691, so an older server's worlds keep matching.</summary>
        public static int CircumferenceOf(NetBody? body, string fallbackId, string? kind, string? planetType)
            => CircumferenceOf(body, fallbackId, kind, planetType, currentWorldCircumference: 0);

        /// <summary>As <see cref="CircumferenceOf(NetBody?, string, string?, string?)"/> for a fallback that IS the world
        /// the player is on: when the star map has no size for it, <paramref name="currentWorldCircumference"/> — the
        /// server's own value for that world (the environment message) — beats the classic formula, which a generation-26
        /// server no longer uses. 0 (unknown) falls through to the classic size.</summary>
        public static int CircumferenceOf(NetBody? body, string fallbackId, string? kind, string? planetType, int currentWorldCircumference)
        {
            if (body != null && body.Circumference > 0)
            {
                return body.Circumference;
            }

            if (currentWorldCircumference > 0)
            {
                return currentWorldCircumference;
            }

            string key = !string.IsNullOrEmpty(body?.Id) ? body!.Id : fallbackId ?? string.Empty;
            return WorldConstants.CircumferenceFor(key, ClassOf(kind, planetType), body?.SizeBias ?? 0f);
        }
    }
}
