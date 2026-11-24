// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Test seams of the creature system: spawn, summon and inspect creatures and their spawn rules from a test.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Spawns a wild creature of the first roster species (or <paramref name="speciesId"/>) at an
    /// exact position, bypassing the spawn habitat/ship checks. Test-only — lets a test plant a creature
    /// inside a parked ship to prove it is evicted. Returns the new creature's id.</summary>
    public string SpawnCreatureAtForTest(Vector3f at, string? speciesId = null)
    {
        SpawnCreature(speciesId is null ? _speciesRoster[0] : _speciesById[speciesId], at);
        return _creatures[^1].Id;
    }

    /// <summary>Runs the placement-time eviction sweep directly (no tick) so a test can isolate it.</summary>
    public void EvictWildlifeFromShipsForTest() => EvictWildlifeFromShips();

    /// <summary>Test-only: the creature exactly as the next creature broadcast would carry it — lets a test
    /// pin the wire's motion flags (airborne / perched / glide) to the simulated state (#1368).</summary>
    public NetCreature NetCreatureForTest(string id) => ToNetCreature(_creatures.First(x => x.Id == id));

    /// <summary>Test-only: the movement profile the active world's creature tick steps a species with — the all-zero
    /// default (speed 0: the animal stands still) when the world has none for the id.</summary>
    public LocomotionProfile LocomotionProfileForTest(string speciesId) => ProfileFor(speciesId);

    /// <summary>Test-only: puts a creature's locomotion controller into a roam PAUSE for <paramref name="seconds"/>
    /// (as if it had rolled one), so a test can trigger the behaviour a pause drives — a flier landing to
    /// rest (#1332) — without waiting for the seeded roll.</summary>
    public void PauseCreatureForTest(string id, float seconds)
    {
        var c = _creatures.First(x => x.Id == id);
        c.Loco.Initialized = true;
        c.Loco.Mode = MoveMode.Pause;
        c.Loco.ModeTimer = seconds;
        c.Loco.Speed = 0f;
    }

    private int _spawnAttemptsForTest;

    /// <summary>Test-only: how many times the spawner has been asked to place something since start — the
    /// #1717 gate must stop asking once the world holds the hard cap.</summary>
    public int SpawnAttemptsForTest => _spawnAttemptsForTest;

    /// <summary>Test-only: the UNCLAMPED population model for this many surface players (#1717).</summary>
    public int WorldCreatureCapForTest(int players) => WorldCreatureCap(players);

    /// <summary>Test-only: the water probe (#1718) — the column it would seat an aquatic spawn in, or null.</summary>
    public (int X, int Z, int Top, int Bed)? WaterColumnNearForTest(int x, int z)
        => TryFindWaterColumnNear(x, z, out int wx, out int wz, out int top, out int bed) ? (wx, wz, top, bed) : null;

    /// <summary>Test-only (#4017): the column the spawner would seat <paramref name="speciesId"/> in near a spot — its depth band
    /// and its sea rule applied — or null.</summary>
    public (int X, int Z, int Top, int Bed)? SpawnWaterNearForTest(string speciesId, int x, int z)
        => TryFindSpawnWaterNear(_speciesById[speciesId], x, z, out int wx, out int wz, out int top, out int bed) ? (wx, wz, top, bed) : null;

    /// <summary>Test-only: places the herd of a species around a spot exactly as the spawner does after the
    /// leader stands (#1718), against a cap of the hard cap.</summary>
    public void SpawnGroupAroundForTest(string speciesId, int x, int z)
        => SpawnGroupAround(_speciesById[speciesId], x, z, CreatureHardCap);

    /// <summary>Test-only: the cave-floor probe (#1719) at a column, −1 when it finds no open cave.</summary>
    public int CaveFloorForTest(int x, int z) => FindCaveFloorY(x, z, _generator.SurfaceHeight(_world.Planet, x, z));

    /// <summary>Test seam (#1862): the generator's surface height for a column — no chunk is loaded or generated,
    /// so a test can size a large build above the terrain without streaming a hundred chunk columns first.</summary>
    public int SurfaceHeightForTest(int x, int z) => _generator.SurfaceHeight(_world.Planet, x, z);

    /// <summary>Test seam (#2226): a fresh generator with this server's seed and galaxy-wide settings and no world
    /// mode. A test configures it for one body and compares it with what the server's shared generator answers
    /// while that body's world is the active one.</summary>
    public BlocksBeyondTheStars.WorldGeneration.WorldGenerator FreshGeneratorForTest() => _generator.CreateSibling();

    /// <summary>The spawner's full reject list for the first roster species at a spot (#1314 seam).</summary>
    public bool SpawnSpotClearForTest(Vector3f at)
    {
        int x = (int)System.Math.Floor(at.X), z = (int)System.Math.Floor(at.Z);
        return SpawnSpotClear(_speciesRoster[0], at, x, z, _generator.SurfaceHeight(_world.Planet, x, z));
    }

    /// <summary>#1747 seam: the spawner's full reject list for a synthetic cave dweller at a spot.</summary>
    public bool CaveSpawnSpotClearForTest(Vector3f at)
    {
        int x = (int)System.Math.Floor(at.X), z = (int)System.Math.Floor(at.Z);
        var sp = new CreatureSpecies { Id = "test_cave", Habitat = CreatureHabitat.Cave };
        return SpawnSpotClear(sp, at, x, z, _generator.SurfaceHeight(_world.Planet, x, z));
    }

    /// <summary>The per-species share of this world's live cap for one player on foot (#1325 seam).</summary>
    public int SpeciesShareForTest() => SpeciesShare(System.Math.Min(WorldCreatureCap(1), CreatureHardCap));
}
