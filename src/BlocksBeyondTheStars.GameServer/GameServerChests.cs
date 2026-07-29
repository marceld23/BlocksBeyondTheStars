// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Scatters standalone <b>treasure chests</b> on a body's surface — rare lootable caches that are NOT part
/// of any structure (settlement / wreck / vault). They reuse the existing structure-loot container flow: a
/// chest is a one-time <see cref="Persistence.StoredContainer"/> spawned via <c>SpawnStructureLoot</c> and
/// recorded in <c>WorldMetadata.GeneratedLoot</c>, so once looted it never re-spawns. Deterministic from the
/// world seed (re-derived every session); idempotent across reloads.
/// </summary>
public sealed partial class GameServer
{
    private void StampChests()
    {
        var planet = _world.Planet;
        if (planet.Void)
        {
            return; // ships / stations have no surface to scatter on
        }

        long cSeed = _meta.Seed ^ WorldGenerator.StableHash("chest:" + _world.LocationId);
        var rng = new System.Random(unchecked((int)(cSeed ^ (cSeed >> 32))));

        // Rare: most bodies get none, some one, a few two.
        double r = rng.NextDouble();
        int count = r < 0.6 ? 0 : r < 0.88 ? 1 : 2;
        if (count == 0)
        {
            return;
        }

        int circ = _world.Circumference;
        int pad0X = _landingPads.Count > 0 ? _landingPads[0].CenterX : 0;
        int pad0Z = _landingPads.Count > 0 ? _landingPads[0].CenterZ : 0;

        int placed = 0;
        for (int i = 0; i < count; i++)
        {
            bool done = false;

            // A spot well away from the landing zone, on dry land, clear of any settlement/ruin footprint.
            for (int attempt = 0; attempt < 24; attempt++)
            {
                double ang = rng.NextDouble() * System.Math.PI * 2.0;
                int dist = 60 + rng.Next(0, 360);
                int x = WorldConstants.WrapX(pad0X + (int)System.Math.Round(System.Math.Cos(ang) * dist), circ);
                int z = pad0Z + (int)System.Math.Round(System.Math.Sin(ang) * dist);

                if (OverlapsAnySettlement(x, z, 2)
                    || _generator.IsSurfaceWater(planet, x, z)
                    || _generator.IsSurfaceLava(planet, x, z))
                {
                    continue;
                }

                int y = _generator.SurfaceHeight(planet, x, z) + 1;
                SpawnStructureLoot("chest", "chest", new Vector3f(x, y, z), rng);
                placed++;
                done = true;
                break;
            }

            if (done || !_worlds.Active.VirginAtLoad)
            {
                // Legacy worlds keep the frozen behaviour bit-for-bit: the fallback's extra loot-roll
                // consumption would shift the next chest's position off its GeneratedLoot key and
                // duplicate it. The guarantee applies to worlds created from now on.
                continue;
            }

            // Terminal fallback (#586): the rolled chest must exist. Deterministic longitude walk; a wet
            // column floats the salvage capsule on the water line, only lava stays excluded. This runs
            // AFTER the 24 rolled attempts, so worlds where they used to succeed are byte-identical.
            for (int step = 0; step < circ / 16; step++)
            {
                int x = WorldConstants.WrapX(pad0X + 60 + step * 16, circ);
                int z = pad0Z + (step % 5 - 2) * 24;
                if (OverlapsAnySettlement(x, z, 2) || _generator.IsSurfaceLava(planet, x, z))
                {
                    continue;
                }

                int y = _generator.TryGetWaterSurface(planet, x, z, out int waterTop, out _)
                    ? waterTop + 1
                    : _generator.SurfaceHeight(planet, x, z) + 1;
                SpawnStructureLoot("chest", "chest", new Vector3f(x, y, z), rng);
                placed++;
                break;
            }
        }

        ReportStamp("chest", count, placed);
        if (placed > 0)
        {
            _log.Info($"Scattered {placed} treasure chest(s) on '{_world.LocationId}'.");
        }
    }
}
