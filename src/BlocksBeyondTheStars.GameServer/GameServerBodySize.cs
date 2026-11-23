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
/// <para>#3969: all of it reads the body's <see cref="BodyProfile"/> — the fixed values of a fixed system's body (Sol),
/// else today's rolls, bit for bit.</para>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>#3969: the fixed values of every body of a fixed or pack system in the galaxy, by body id — filled when such a
    /// system is appended, emptied when the galaxy is rebuilt. Looked up by location id, so a world, a pad or a station's
    /// edits resolve their profile without walking the galaxy.</summary>
    private readonly Dictionary<string, FixedBodyDefinition> _fixedBodies = new(StringComparer.Ordinal);

    /// <summary>The profile of <paramref name="locationId"/> with the given size class and archetype bias in this save (its
    /// seed and terrain generation): the body's fixed values when it has them, else the rolls.</summary>
    private BodyProfile BodyProfileOf(string locationId, WorldConstants.WorldSizeClass sizeClass, float sizeBias)
        => BodyProfile.Resolve(_fixedBodies.TryGetValue(locationId, out var fixedValues) ? fixedValues : null,
            locationId, sizeClass, sizeBias, _meta.Seed, _meta.Description.TerrainGeneration);

    /// <summary>The profile of a galaxy body in this save — its size class from its kind and planet type, its archetype bias
    /// (#549), its fixed values.</summary>
    private BodyProfile BodyProfileOf(CelestialBody body)
        => BodyProfile.Resolve(body.Fixed, body.Id, WorldConstants.SizeClassFor(body.Kind, body.PlanetType ?? string.Empty),
            body.SizeBias, _meta.Seed, _meta.Description.TerrainGeneration);

    /// <summary>The walkable circumference of <paramref name="locationId"/> with the given size class and archetype bias,
    /// in this save (its seed and terrain generation).</summary>
    private int WorldCircumferenceOf(string locationId, WorldConstants.WorldSizeClass sizeClass, float sizeBias)
        => BodyProfileOf(locationId, sizeClass, sizeBias).Circumference;

    /// <summary>The walkable circumference of a galaxy body in this save — its size class from its kind and planet type,
    /// its archetype bias (#549).</summary>
    private int WorldCircumferenceOf(CelestialBody body) => BodyProfileOf(body).Circumference;

    /// <summary>#2697: the gravity multiplier of <paramref name="locationId"/> with the given size class in this save (its
    /// seed and terrain generation) — <see cref="BodyGravity.FactorFor"/>, or a fixed body's own. The loaded world's
    /// environment (<c>InitWeather</c>) and the ship scanner's overview card both read it here, so the card always shows the
    /// gravity you land in; the client takes it from <c>WorldEnvironment.GravityFactor</c> and never recomputes it.</summary>
    private float WorldGravityOf(string locationId, WorldConstants.WorldSizeClass sizeClass)
        => BodyProfileOf(locationId, sizeClass, 0f).GravityFactor;

    /// <summary>#3969: the drawn diameter of a galaxy body in the flight view — the one the client draws it at
    /// (<c>NetBody.VisibleDiameter</c>, else its circumference), so a server-flown ship meets the same sphere.</summary>
    private float VisibleDiameterOf(CelestialBody body) => BodyProfileOf(body).VisibleDiameter;

    /// <summary>#3969: registers the fixed values of a just-appended fixed or pack system's bodies.</summary>
    private void RegisterFixedBodies(StarSystem system)
    {
        foreach (var body in system.Bodies)
        {
            if (body.Fixed is { } values)
            {
                _fixedBodies[body.Id] = values;
            }
        }
    }
}
