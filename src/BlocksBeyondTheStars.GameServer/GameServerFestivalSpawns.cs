// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Festivals;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The festivals' deposit layer (#3006, epic #3000): while a festival with a <c>spawn</c> block is on (the harvest festivals'
/// pumpkins), its block appears on existing worlds at runtime — the weather snow's pattern, not world generation, so a
/// world's natural blocks never change under the player and old saves get the festival too.
/// <para>Everything is bounded: one pass every <see cref="FestivalSpawnInterval"/> s, at most <c>perPass</c> cells near a
/// player on foot, never more than <c>cap</c> per world, loaded chunks only, breathable worlds only, never inside a base
/// zone or a ship, only in the air cell on one of the spawn's host blocks under the open sky. Every placement is broadcast
/// and written to the <c>weather_deposit</c> table (told apart from snow by its block), so a restart keeps the ledger.</para>
/// <para>Cleanup: when no festival with that block is on, the pass removes a few of its own deposits per pass — only cells
/// that still hold the festival block; a player's own pumpkin is never touched. A harvested deposit leaves the ledger and
/// never regrows (<see cref="ForgetFestivalDeposit"/>): the harvest stays harvested.</para>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Seconds between two festival spawn/cleanup passes on a world.</summary>
    private const double FestivalSpawnInterval = 3.0;

    /// <summary>Cells the cleanup pass removes per pass.</summary>
    private const int FestivalCleanupPerPass = 12;

    private readonly Random _festivalSpawnRng = new(0x5F3A);

    /// <summary>The spawn of the primary festival that is on, or null (none on, or none of the active festivals spawns).</summary>
    private FestivalSpawn? ActiveFestivalSpawn()
    {
        foreach (string key in ActiveFestivals)
        {
            var spawn = _content.FindFestival(key)?.Spawn;
            if (spawn is not null && !string.IsNullOrEmpty(spawn.Block))
            {
                return spawn;
            }
        }

        return null;
    }

    /// <summary>Per-world tick step: lays down the festival's block near players, or clears its own deposits when the
    /// festival is over.</summary>
    private void TickFestivalSpawns(double dt)
    {
        var record = _worlds.Active;
        record.SinceFestivalDeposit += dt;
        if (record.SinceFestivalDeposit < FestivalSpawnInterval)
        {
            return;
        }

        record.SinceFestivalDeposit = 0;
        RunFestivalSpawnPass();
    }

    /// <summary>One spawn/cleanup pass on the active world (the test seam runs it directly).</summary>
    private void RunFestivalSpawnPass()
    {
        var deposits = _worlds.Active.FestivalDeposits;
        var spawn = ActiveFestivalSpawn();
        ushort block = spawn is null ? (ushort)0 : _content.GetBlock(spawn.Block)?.NumericId.Value ?? (ushort)0;
        bool spawning = block != 0 && BodyBreathable(_world.LocationId) && _content.GetPlanet(_worlds.Active.PlanetType)?.Void != true;

        if (!spawning && deposits.Count == 0)
        {
            return; // nothing to lay down or clear — no empty transaction
        }

        _repo.RunInTransaction(() =>
        {
            if (spawning)
            {
                PlaceFestivalDeposits(spawn!, block, deposits);
            }

            ClearFestivalDeposits(deposits, keep: spawning ? block : (ushort)0);
        });
    }

    private void PlaceFestivalDeposits(FestivalSpawn spawn, ushort block, Dictionary<Vector3i, ushort> deposits)
    {
        int cap = Math.Max(0, spawn.Cap);
        int perPass = Math.Max(1, spawn.PerPass);
        int radius = Math.Max(2, spawn.Radius);
        if (deposits.Count >= cap)
        {
            return;
        }

        var hosts = new HashSet<ushort>();
        foreach (string host in spawn.Hosts ?? Array.Empty<string>())
        {
            if (_content.GetBlock(host) is { } def)
            {
                hosts.Add(def.NumericId.Value);
            }
        }

        if (hosts.Count == 0)
        {
            return;
        }

        int placed = 0;
        foreach (var session in JoinedInActiveWorld())
        {
            if (placed >= perPass || deposits.Count >= cap)
            {
                break;
            }

            var p = session.State;
            if (p.InEva || p.AboardShip)
            {
                continue;
            }

            for (int attempt = 0; attempt < perPass * 3 && placed < perPass && deposits.Count < cap; attempt++)
            {
                int x = (int)Math.Floor(p.Position.X) + _festivalSpawnRng.Next(-radius, radius + 1);
                int z = (int)Math.Floor(p.Position.Z) + _festivalSpawnRng.Next(-radius, radius + 1);
                if (!TryFindFestivalSpawnCell(x, (int)Math.Round(p.Position.Y), z, hosts, out var cell) || deposits.ContainsKey(cell))
                {
                    continue;
                }

                _world.SetBlock(cell, new BlockId(block));
                BroadcastToWorld(new BlockChanged { X = cell.X, Y = cell.Y, Z = cell.Z, Block = block }); // clients only learn of edits this way
                deposits[cell] = block;
                _repo.SaveWeatherDeposit(_world.LocationId, cell, block, 0);
                placed++;
            }
        }
    }

    /// <summary>The air cell on a host block below the player's level in this column, under the open sky, outside every
    /// base zone and ship. Reads only loaded chunks — the pass never generates.</summary>
    private bool TryFindFestivalSpawnCell(int x, int fromY, int z, HashSet<ushort> hosts, out Vector3i cell)
    {
        cell = default;
        for (int y = fromY + 8; y > fromY - 12; y--)
        {
            var here = _world.GetBlockIfLoaded(new Vector3i(x, y, z));
            if (here.Value == 0)
            {
                continue;
            }

            if (!hosts.Contains(here.Value))
            {
                return false; // the first solid thing in the column is not a host — no pumpkin on a roof, a road or the water
            }

            var above = new Vector3i(x, y + 1, z);
            if (_world.GetBlockIfLoaded(above).Value != 0 || !SkyExposed(above) || InAnyBaseZone(above) || InShipInterior(above))
            {
                return false;
            }

            cell = above;
            return true;
        }

        return false;
    }

    /// <summary>Removes a few of the layer's own deposits that do not hold the block a festival on right now spawns — the
    /// cells still holding that block are cleared to air; cells the player changed just leave the ledger.</summary>
    private void ClearFestivalDeposits(Dictionary<Vector3i, ushort> deposits, ushort keep)
    {
        if (deposits.Count == 0)
        {
            return;
        }

        var doomed = new List<Vector3i>(FestivalCleanupPerPass);
        foreach (var (cell, block) in deposits)
        {
            if (block == keep)
            {
                continue;
            }

            doomed.Add(cell);
            if (doomed.Count >= FestivalCleanupPerPass)
            {
                break;
            }
        }

        foreach (var cell in doomed)
        {
            ushort block = deposits[cell];
            deposits.Remove(cell);
            _repo.DeleteWeatherDeposit(_world.LocationId, cell);
            if (_world.GetBlockIfLoaded(cell).Value == block)
            {
                _world.SetBlock(cell, BlockId.Air);
                BroadcastToWorld(new BlockChanged { X = cell.X, Y = cell.Y, Z = cell.Z, Block = 0 });
            }
        }
    }

    /// <summary>A festival deposit the player harvested leaves the ledger (true) — nothing regrows there. False for any
    /// other cell, which the regrow queue handles as always.</summary>
    private bool ForgetFestivalDeposit(Vector3i pos)
    {
        var deposits = _worlds.Active.FestivalDeposits;
        if (!deposits.Remove(pos))
        {
            return false;
        }

        _repo.DeleteWeatherDeposit(_world.LocationId, pos);
        return true;
    }

    /// <summary>Test seam: one spawn/cleanup pass on the active world, right now.</summary>
    public void RunFestivalSpawnPassForTest() => RunFestivalSpawnPass();

    /// <summary>Test seam: the festival deposit cells of the active world.</summary>
    public IReadOnlyCollection<Vector3i> FestivalDepositCellsForTest => _worlds.Active.FestivalDeposits.Keys;

    /// <summary>Test seam: whether this cell is a festival deposit.</summary>
    public bool IsFestivalDepositForTest(Vector3i cell) => _worlds.Active.FestivalDeposits.ContainsKey(cell);
}
