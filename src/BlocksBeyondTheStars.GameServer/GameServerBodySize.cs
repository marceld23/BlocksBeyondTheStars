// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The size of a body in THIS world (#2691) — the one place the server turns a body into a walkable circumference.
/// From terrain generation 26 the size is seeded by the world (<see cref="WorldConstants.CircumferenceFor(string,
/// WorldConstants.WorldSizeClass, float, long, int)"/>); an older save keeps its classic sizes bit for bit because the
/// save's own generation goes in. Every server path that sizes a body — loading its world, its landing pads and their
/// weather, the ambient weather of an unloaded body, a station's stored edits, the star map the client sizes its views
/// from (<c>NetBody.Circumference</c>) — goes through here, so they can never disagree. A body's gravity (#2697) is
/// derived here as well, for the same reason.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The walkable circumference of <paramref name="locationId"/> with the given size class and archetype bias,
    /// in this save (its seed and terrain generation).</summary>
    private int WorldCircumferenceOf(string locationId, WorldConstants.WorldSizeClass sizeClass, float sizeBias)
        => WorldConstants.CircumferenceFor(locationId, sizeClass, sizeBias, _meta.Seed, _meta.Description.TerrainGeneration);

    /// <summary>The walkable circumference of a galaxy body in this save — its size class from its kind and planet type,
    /// its archetype bias (#549).</summary>
    private int WorldCircumferenceOf(CelestialBody body)
        => WorldCircumferenceOf(body.Id, WorldConstants.SizeClassFor(body.Kind, body.PlanetType ?? string.Empty), body.SizeBias);

    /// <summary>#2697: the gravity multiplier of <paramref name="locationId"/> with the given size class in this save (its
    /// seed and terrain generation) — <see cref="BodyGravity.FactorFor"/>. The loaded world's environment
    /// (<c>InitWeather</c>) and the ship scanner's overview card both read it here, so the card always shows the gravity
    /// you land in; the client takes it from <c>WorldEnvironment.GravityFactor</c> and never recomputes it.</summary>
    private float WorldGravityOf(string locationId, WorldConstants.WorldSizeClass sizeClass)
        => BodyGravity.FactorFor(locationId, sizeClass, _meta.Seed, _meta.Description.TerrainGeneration);
}
