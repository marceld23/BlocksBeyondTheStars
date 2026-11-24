// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Shared.World;

/// <summary>
/// Turns a checked <see cref="FixedSystemDefinition"/> into the <see cref="StarSystem"/> the galaxy holds (#3969) — the
/// one builder of every hand-made system with bodies (Sol, a story pack's system). Pure: the same definition and map spot
/// give the same system at every start.
/// </summary>
public static class FixedSystemBuilder
{
    /// <summary>
    /// The system of <paramref name="def"/> at the map spot (<paramref name="mapX"/>, <paramref name="mapY"/>). Each body
    /// sits at its orbit radius and angle around the star (or around its parent), carries its definition
    /// (<see cref="CelestialBody.Fixed"/>, read by <see cref="BodyProfile"/>) and its ring. <paramref name="nameOf"/> turns a
    /// name key and its fallback into the stored name (the server's default language); a body without a name takes the
    /// system's.
    /// </summary>
    public static StarSystem Build(FixedSystemDefinition def, StarSystemOrigin origin, float mapX, float mapY,
        Func<string, string, string> nameOf)
    {
        string systemName = nameOf(def.NameKey, string.IsNullOrEmpty(def.Name) ? def.Id : def.Name);
        var system = new StarSystem { Id = def.Id, Origin = origin, Name = systemName, MapX = mapX, MapY = mapY, Fixed = def };
        var placed = new Dictionary<string, CelestialBody>(StringComparer.Ordinal);
        foreach (var b in def.Bodies)
        {
            placed.TryGetValue(b.Parent ?? string.Empty, out var parent);
            float angle = b.OrbitAngle * MathF.PI / 180f;
            float dx = b.OrbitRadius * MathF.Cos(angle), dz = b.OrbitRadius * MathF.Sin(angle);
            string id = FixedSystemDefinition.BodyIdOf(def.Id, b.Id);
            var body = new CelestialBody
            {
                Id = id,
                Name = nameOf(b.NameKey, string.IsNullOrEmpty(b.Name) ? systemName : b.Name),
                Kind = FixedSystemRules.KindOf(b.Kind) ?? CelestialKind.Planet,
                PlanetType = b.PlanetType,
                SystemId = def.Id,
                SystemX = parent is null ? dx : parent.SystemX + dx,
                SystemZ = parent is null ? dz : parent.SystemZ + dz,
                OrbitPeriodDays = b.OrbitPeriodDays,
                ParentId = parent?.Id ?? string.Empty,
                RingSeed = RingSeedOf(b, id),
                Fixed = b,
            };
            placed[b.Id] = body;
            system.Bodies.Add(body);
        }

        return system;
    }

    /// <summary>A body's ring: its own seed, else — with <see cref="FixedBodyDefinition.Ring"/> — one from its id, else none.</summary>
    public static int RingSeedOf(FixedBodyDefinition body, string bodyId)
        => body.RingSeed != 0 ? body.RingSeed
            : body.Ring ? 1 + StableHashing.Index("fixed-ring:" + bodyId, 999_999)
            : 0;
}
