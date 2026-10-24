// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Wildlife on ground the server has loaded (#2960).
/// <para>The problem: a player who joined or landed got <see cref="PopulateCreaturesNear"/> inline. That ran before the
/// first streaming pass, so no chunk of the spawn ring was loaded yet. Every reject rule in <see cref="SpawnSpotClear"/>
/// reads real blocks, and an unloaded chunk reads as air, so this first batch of animals was placed without looking:</para>
/// <list type="bullet">
/// <item>a floor the player built did not count (#2521);</item>
/// <item>a walled yard did not count (#1315);</item>
/// <item>a wall in the way of a big body did not count;</item>
/// <item>the feet fell back to the generator surface, which is exactly the top of a paved yard.</item>
/// </list>
/// <para>During play the ring is loaded and the same rules hold. That is why only the first animals of a session stood on a
/// player's spaceport. Two changes fix it:</para>
/// <list type="bullet">
/// <item>Join and landing only mark the first batch as due (<see cref="QueueFaunaSeed"/>). The creature tick places it once
/// the ground of every ring slot is loaded, or after <see cref="FaunaSeedTimeout"/> seconds at the latest
/// (<see cref="TickFaunaSeeds"/>). This runs before the tick's early return for "everybody is aboard", so a player still in
/// the cockpit also arrives to a living world.</item>
/// <item>Before it judges a column, the spawner loads that column's surface chunks (<see cref="LoadSpawnGround"/>). That is
/// one chunk, two at a chunk border. The streamer sends them anyway, and during play they are already loaded. So a batch
/// that timed out, or a spawn in a tick before the first streaming pass, still reads real blocks there.</item>
/// </list>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The longest the first batch of animals waits for the ring to stream in (a slow first visit, a tiny view
    /// distance). Past it, the batch is placed with what has loaded; <see cref="LoadSpawnGround"/> loads the rest.</summary>
    private const double FaunaSeedTimeout = 6.0;

    /// <summary>Marks the first batch of animals as due for a player who just joined or landed.</summary>
    private static void QueueFaunaSeed(PlayerSession session)
    {
        session.FaunaSeedDue = true;
        session.FaunaSeedWaited = 0;
    }

    /// <summary>Places every due first batch on the active world whose ring has streamed in (or whose wait ran out).
    /// Nothing to place on a void or lifeless world, or for a player in space: the mark is simply dropped. True when an
    /// animal was added (the caller re-broadcasts).</summary>
    private bool TickFaunaSeeds(double dt)
    {
        bool spawned = false;
        foreach (var s in JoinedInActiveWorld())
        {
            if (!s.FaunaSeedDue)
            {
                continue;
            }

            s.FaunaSeedWaited += dt;
            bool nothingToSeed = _world.Planet.Void || _speciesRoster.Length == 0 || InSpace(s.State.PlayerId);
            if (!nothingToSeed && s.FaunaSeedWaited < FaunaSeedTimeout && !SpawnRingResident(s.State.Position))
            {
                continue; // the ground around the player is still streaming in
            }

            s.FaunaSeedDue = false;
            if (nothingToSeed)
            {
                continue;
            }

            int before = _creatures.Count;
            PopulateCreaturesNear(s.State, CreatureCapPerPlayer);
            spawned |= _creatures.Count != before;
        }

        return spawned;
    }

    /// <summary>Whether the surface chunks of every spawn-ring slot around a position are loaded. These are the chunks a
    /// land placement reads first, and the walls of a yard stand in them too.</summary>
    private bool SpawnRingResident(Vector3f at)
    {
        int cx = (int)System.Math.Floor(at.X);
        int cz = (int)System.Math.Floor(at.Z);
        foreach (var (dx, dz) in SpawnRing)
        {
            int x = cx + dx, z = cz + dz;
            int surface = _generator.SurfaceHeight(_world.Planet, x, z);
            if (!_world.IsChunkLoaded(WorldConstants.WorldToChunk(new Vector3i(x, surface, z)))
                || !_world.IsChunkLoaded(WorldConstants.WorldToChunk(new Vector3i(x, surface + 1, z))))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Loads the surface chunks of a spawn column before the spawner judges it: the chunk of the generator surface
    /// and the chunk of the feet cell above it (the same chunk, except at a chunk border). During play they are already
    /// loaded and this is a lookup. Cave spots keep their own probe, which does not load chunks (#1719).</summary>
    private void LoadSpawnGround(int x, int z, int surface)
    {
        for (int y = surface; y <= surface + 1; y++)
        {
            if (WithinBuildHeight(y))
            {
                _world.GetOrLoadChunk(WorldConstants.WorldToChunk(new Vector3i(x, y, z)));
            }
        }
    }

    /// <summary>Test seam (#2960): whether this player's first batch of animals is still waiting for the ground.</summary>
    public bool FaunaSeedDueForTest(string playerId)
    {
        foreach (var s in _sessions.Values)
        {
            if (s.State.PlayerId == playerId)
            {
                return s.FaunaSeedDue;
            }
        }

        return false;
    }
}
