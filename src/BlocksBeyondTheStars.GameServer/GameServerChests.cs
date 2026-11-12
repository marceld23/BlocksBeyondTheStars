// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Scatters standalone <b>treasure chests</b> on a body's surface — rare lootable caches that are NOT part
/// of any structure (settlement / wreck / vault). They reuse the existing structure-loot container flow: a
/// chest is a one-time <see cref="Persistence.StoredContainer"/> spawned via <c>SpawnStructureLoot</c> and
/// recorded in <c>WorldMetadata.GeneratedLoot</c>, so once looted it never re-spawns. Deterministic from the
/// world seed (re-derived every session); idempotent across reloads. Since #3513 every chest also has a placement record
/// (kind <c>chest</c>: the cell it stands in), and a reload walks the body stream exactly as the first spawn did.
/// </summary>
public sealed partial class GameServer
{
    private void StampChests()
    {
        var planet = _world.Planet;
        if (!StructureConditionsHold(StructureTypeRules.Chest, planet))
        {
            return; // ships / stations have no surface to scatter on (the kind's shipped condition, #3517)
        }

        long cSeed = _meta.Seed ^ WorldGenerator.StableHash("chest:" + _world.LocationId);
        var rng = new System.Random(unchecked((int)(cSeed ^ (cSeed >> 32))));

        // Rare: most bodies get none, some one, a few two.
        double r = rng.NextDouble();
        int count = PinnedCount(ChestRecordKind, StructureType(StructureTypeRules.Chest).CountBelow(r)); // #3513: drawn as always, pinned at the first run; #3517: [0.6, 0.88]
        if (count == 0)
        {
            return;
        }

        int circ = _world.Circumference;
        int pad0X = _landingPads.Count > 0 ? _landingPads[0].CenterX : 0;
        int pad0Z = _landingPads.Count > 0 ? _landingPads[0].CenterZ : 0;
        bool seaIceIsWet = SeaIceIsWet; // #2738: from generation 27 a frozen sea is no dry land for a chest either

        int placed = 0;
        for (int i = 0; i < count; i++)
        {
            // The rolled spot — its 24 attempts draw from the body stream on every load (the next chest's spot follows them).
            var spot = RollChestSpot(rng, pad0X, pad0Z, circ, seaIceIsWet);
            string seat = "rolled";
            var rec = FindPlacementRecord(ChestRecordKind, i);
            if (rec is null && spot is null && _worlds.Active.VirginAtLoad)
            {
                // Terminal fallback (#586): the rolled chest must exist. It runs AFTER the 24 rolled attempts (and draws
                // nothing), so worlds where they used to succeed are byte-identical. Legacy worlds keep the frozen behaviour
                // bit-for-bit — the guarantee applies to worlds created from #586 on.
                spot = ChestFallbackSpot(pad0X, pad0Z, circ, seaIceIsWet);
                seat = "fallback";
            }

            // #3513: the chest's cell is recorded at its first spawn, and a recorded chest stands where its record says.
            if (rec is null)
            {
                if (spot is { } cell)
                {
                    RecordPlacement(ChestRecordKind, i, cell, cell.Y, onIsland: false, seat, "chest_" + i);
                }
                else
                {
                    RecordPlacementSkip(ChestRecordKind, i);
                }
            }
            else
            {
                spot = rec.Placed ? new Vector3i(rec.X, rec.GroundY, rec.Z) : null;
            }

            if (spot is { } at)
            {
                RegisterFootprint(StructureTypeRules.Chest, (at.X, at.Z, 1, 1)); // #3514
                // #3513: a chest whose cache exists already still rolls its loot (and drops it) — the loot and the spots share
                // the body stream, and skipping those draws on a reload moved the next chest to a new spot: a second chest.
                SpawnStructureLoot("chest", "chest", new Vector3f(at.X, at.Y, at.Z), rng, replayDraws: true);
                placed++;
            }
        }

        SavePlacementRecords();
        ReportStamp("chest", count, placed);
        if (placed > 0)
        {
            _log.Info($"Scattered {placed} treasure chest(s) on '{_world.LocationId}'.");
        }
    }

    /// <summary>The first of 24 rolled spots well away from the landing zone, on dry land, clear of any settlement/ruin
    /// footprint and of sea ice (#2738) — the chest's cell (one above the ground), or null when every attempt failed. Every
    /// attempt draws two numbers from <paramref name="rng"/>.</summary>
    private Vector3i? RollChestSpot(System.Random rng, int pad0X, int pad0Z, int circ, bool seaIceIsWet)
    {
        var planet = _world.Planet;
        for (int attempt = 0; attempt < 24; attempt++)
        {
            double ang = rng.NextDouble() * System.Math.PI * 2.0;
            int dist = 60 + rng.Next(0, 360);
            int x = WorldConstants.WrapX(pad0X + (int)System.Math.Round(System.Math.Cos(ang) * dist), circ);
            int z = pad0Z + (int)System.Math.Round(System.Math.Sin(ang) * dist);

            if (KeepClearBlocked(StructureTypeRules.Chest, x, z, 2) // #3514: generation 38: the registry
                || _generator.IsSurfaceWater(planet, x, z)
                || _generator.IsSurfaceLava(planet, x, z)
                || (seaIceIsWet && _generator.SurfaceIceThickness(planet, x, z) > 0)) // not buried under sea ice
            {
                continue;
            }

            return new Vector3i(x, _generator.SurfaceHeight(planet, x, z) + 1, z);
        }

        return null;
    }

    /// <summary>The fallback spot (#586): a deterministic longitude walk; a wet column floats the salvage capsule on the
    /// water line (on a frozen sea it rests on the ice, #2738), only lava stays excluded. Draws nothing.</summary>
    private Vector3i? ChestFallbackSpot(int pad0X, int pad0Z, int circ, bool seaIceIsWet)
    {
        var planet = _world.Planet;
        for (int step = 0; step < circ / 16; step++)
        {
            int x = WorldConstants.WrapX(pad0X + 60 + step * 16, circ);
            int z = pad0Z + (step % 5 - 2) * 24;
            if (KeepClearBlocked(StructureTypeRules.Chest, x, z, 2) || _generator.IsSurfaceLava(planet, x, z))
            {
                continue;
            }

            int y = seaIceIsWet && _generator.TryGetIceSurface(planet, x, z, out int iceTop) ? iceTop + 1
                : _generator.TryGetWaterSurface(planet, x, z, out int waterTop, out _) ? waterTop + 1
                : _generator.SurfaceHeight(planet, x, z) + 1;
            return new Vector3i(x, y, z);
        }

        return null;
    }
}
