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

        /// <summary>#3970: the diameter the flight view draws <paramref name="body"/> at — the server's fixed drawn size
        /// (<see cref="NetBody.VisibleDiameter"/>, Sol's giants) when it sent one, else the one its walkable
        /// <paramref name="circumference"/> gives (<see cref="SystemBodyLayout.VisibleDiameter"/>, the server's own rule).</summary>
        public static float VisibleDiameterOf(NetBody? body, int circumference)
            => SystemBodyLayout.VisibleDiameter(body?.VisibleDiameter ?? 0f, circumference);

        /// <summary>#3970: the circumference a view that scales a body by its size should use (the sky disc, the sky's
        /// prominence order): the walkable one, or — when the server fixed a drawn diameter — the circumference that diameter
        /// stands for (<see cref="SystemBodyLayout.OrbitDiameter"/> inverted), so Jupiter looms in the sky as it does in
        /// flight. The world map bake keeps the walkable <see cref="CircumferenceOf(NetBody)"/>.</summary>
        public static int ApparentCircumferenceOf(NetBody body)
            => body.VisibleDiameter > 0f
                ? System.Math.Max(0, (int)((body.VisibleDiameter - SystemBodyLayout.OrbitDiameter(0)) * 220f))
                : CircumferenceOf(body);

        /// <summary>#3970: whether <paramref name="body"/> is drawn clearly bigger than its walkable size (a quarter more) — a
        /// fixed giant, which the system chart draws with the gas giant's larger dot.</summary>
        public static bool LooksGiant(NetBody body)
            => body.VisibleDiameter > SystemBodyLayout.OrbitDiameter(CircumferenceOf(body)) * 1.25f;
    }
}
