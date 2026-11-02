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
/// <para>#3083: and per player — one lay every <c>interval</c> seconds at most, and no more than <c>budget</c> blocks per
/// festival and year (<see cref="Shared.State.PlayerState.FestivalSpawnsLaid"/>, persisted with the player). The cap used to
/// count only the deposits standing right now, so every harvest freed a slot the next pass filled three seconds later
/// beside the player — "the pumpkins grow back", and the sixty of the Halloween task took a few minutes.</para>
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

    /// <summary>A body keeps this many blocks of room around it (#3020): no solid pumpkin grows into a player, an NPC or a
    /// companion, or right in front of their feet.</summary>
    private const int FestivalSpawnBodyClearance = 2;

    /// <summary>The first festival among <paramref name="keys"/> that has a spawn, or null.</summary>
    private FestivalDefinition? FestivalWithSpawn(IEnumerable<string> keys)
    {
        foreach (string key in keys)
        {
            var festival = _content.FindFestival(key);
            if (festival?.Spawn is { } spawn && !string.IsNullOrEmpty(spawn.Block))
            {
                return festival;
            }
        }

        return null;
    }

    /// <summary>The spawn of the world's festivals that are on, or null (none on, or none of them spawns).</summary>
    private FestivalSpawn? ActiveFestivalSpawn() => FestivalWithSpawn(ActiveFestivals)?.Spawn;

    /// <summary>#3084: whether this cell is one of the festival layer's own deposits (a present, not the world's flora).</summary>
    private bool IsFestivalDeposit(Vector3i pos)
        => _worlds.Active.FestivalDeposits.Count > 0
           && _worlds.Active.FestivalDeposits.ContainsKey(Shared.World.WorldConstants.CanonicalBlock(pos, _world.Circumference));

    /// <summary>#3083: the key of one festival occurrence in <see cref="Shared.State.PlayerState.FestivalSpawnsLaid"/> — the
    /// festival and the year (like VEGA's greeting), so next year's Halloween starts with a fresh budget.</summary>
    private string FestivalBudgetKey(string festivalKey) => $"{festivalKey}:{FestivalUtcNow.Year}";

    /// <summary>#3083: how many more festival blocks may be laid for this player in this occurrence (int.MaxValue = the data
    /// sets no budget).</summary>
    private int FestivalBudgetLeft(Shared.State.PlayerState p, FestivalDefinition festival)
    {
        int budget = festival.Spawn?.Budget ?? 0;
        if (budget <= 0)
        {
            return int.MaxValue;
        }

        p.FestivalSpawnsLaid.TryGetValue(FestivalBudgetKey(festival.Key), out int laid);
        return Math.Max(0, budget - laid);
    }

    /// <summary>#3083: books <paramref name="count"/> laid blocks on the player's budget and forgets other years' entries, so
    /// the dictionary never holds more than this year's few festivals.</summary>
    private void ChargeFestivalBudget(Shared.State.PlayerState p, FestivalDefinition festival, int count)
    {
        string key = FestivalBudgetKey(festival.Key);
        string year = ":" + FestivalUtcNow.Year;
        foreach (string stale in p.FestivalSpawnsLaid.Keys.Where(k => !k.EndsWith(year, StringComparison.Ordinal)).ToList())
        {
            p.FestivalSpawnsLaid.Remove(stale);
        }

        p.FestivalSpawnsLaid.TryGetValue(key, out int laid);
        p.FestivalSpawnsLaid[key] = laid + count;
    }

    private ushort FestivalSpawnBlock(FestivalSpawn spawn) => _content.GetBlock(spawn.Block)?.NumericId.Value ?? (ushort)0;

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

    /// <summary>One spawn/cleanup pass on the active world (the test seam runs it directly). #3021: each player gets the spawn
    /// of the festivals THEY celebrate — pumpkins grow around a player who celebrates Halloween, not around one who chose
    /// "None"; the cleanup keeps what someone here still celebrates. With nobody on the world the world's own festivals
    /// decide, so a festival that is on is not cleared just because everybody went to bed.</summary>
    private void RunFestivalSpawnPass()
    {
        var deposits = _worlds.Active.FestivalDeposits;
        bool breathable = BodyBreathable(_world.LocationId) && _content.GetPlanet(_worlds.Active.PlanetType)?.Void != true;
        var keep = new HashSet<ushort>();
        var plans = new List<(PlayerSession Session, FestivalDefinition Festival, ushort Block)>();
        var players = JoinedInActiveWorld().ToList();
        if (players.Count == 0 && ActiveFestivalSpawn() is { } worldSpawn)
        {
            keep.Add(FestivalSpawnBlock(worldSpawn));
        }

        foreach (var session in players)
        {
            if (FestivalWithSpawn(ActiveFestivalsFor(session)) is { Spawn: { } spawn } festival
                && FestivalSpawnBlock(spawn) is var block and not 0)
            {
                keep.Add(block);
                plans.Add((session, festival, block));
            }
        }

        if (!breathable)
        {
            keep.Clear();
            plans.Clear();
        }

        if (plans.Count == 0 && deposits.Count == 0)
        {
            return; // nothing to lay down or clear — no empty transaction
        }

        _repo.RunInTransaction(() =>
        {
            foreach (var (session, festival, block) in plans)
            {
                PlaceFestivalDeposits(session, festival, block, deposits);
            }

            ClearFestivalDeposits(deposits, keep);
        });
    }

    /// <summary>The fewest column probes one lay makes — with a single cell per lay over a wide radius, three probes miss a
    /// meadow too often (a miss is retried on the next pass, the player's clock is not reset).</summary>
    private const int FestivalSpawnMinAttempts = 6;

    /// <summary>Lays down this player's festival block near them: at most the spawn's <c>perPass</c> cells, once their own
    /// clock (<see cref="PlayerSession.FestivalSpawnWait"/>, the spawn's <c>interval</c>) has run down and while their budget
    /// for this festival occurrence lasts (#3083). The clock counts in passes, so the test seam's passes count too.</summary>
    private void PlaceFestivalDeposits(PlayerSession session, FestivalDefinition festival, ushort block, Dictionary<Vector3i, ushort> deposits)
    {
        var spawn = festival.Spawn!;
        var p = session.State;
        session.FestivalSpawnWait = Math.Max(0, session.FestivalSpawnWait - FestivalSpawnInterval);
        int cap = Math.Max(0, spawn.Cap);
        int perPass = Math.Min(Math.Max(1, spawn.PerPass), FestivalBudgetLeft(p, festival));
        if (session.FestivalSpawnWait > 0 || perPass <= 0 || deposits.Count >= cap || p.InEva || p.AboardShip)
        {
            return;
        }

        var hosts = FestivalHostIds(spawn);
        if (hosts.Count == 0)
        {
            return;
        }

        int radius = Math.Max(2, spawn.Radius);
        int placed = 0;
        for (int attempt = 0; attempt < Math.Max(perPass * 3, FestivalSpawnMinAttempts) && placed < perPass && deposits.Count < cap; attempt++)
        {
            int x = (int)Math.Floor(p.Position.X) + _festivalSpawnRng.Next(-radius, radius + 1);
            int z = (int)Math.Floor(p.Position.Z) + _festivalSpawnRng.Next(-radius, radius + 1);
            if (!TryFindFestivalSpawnCell(x, (int)Math.Round(p.Position.Y), z, hosts, out var found))
            {
                continue;
            }

            var cell = Shared.World.WorldConstants.CanonicalBlock(found, _world.Circumference); // the ledger keys the cell as mining sees it
            if (deposits.ContainsKey(cell))
            {
                continue;
            }

            _world.SetBlock(cell, new BlockId(block));
            BroadcastToWorld(new BlockChanged { X = cell.X, Y = cell.Y, Z = cell.Z, Block = block }); // clients only learn of edits this way
            deposits[cell] = block;
            _repo.SaveWeatherDeposit(_world.LocationId, cell, block, 0);
            placed++;
        }

        if (placed > 0)
        {
            ChargeFestivalBudget(p, festival, placed);
            session.FestivalSpawnWait = Math.Max(0, spawn.Interval); // #3083: the next one for this player after the interval
        }
    }

    /// <summary>The numeric ids of the blocks a festival spawn may stand on.</summary>
    private HashSet<ushort> FestivalHostIds(FestivalSpawn spawn)
    {
        var hosts = new HashSet<ushort>();
        foreach (string host in spawn.Hosts ?? Array.Empty<string>())
        {
            if (_content.GetBlock(host) is { } def)
            {
                hosts.Add(def.NumericId.Value);
            }
        }

        return hosts;
    }

    /// <summary>The air cell on a host block below the player's level in this column, under the open sky, outside every
    /// base zone and ship. Reads only loaded chunks — the pass never generates. #3020: and never where players built or dug
    /// (no edit in the box around the cell and its host), inside a settlement, or within <see cref="FestivalSpawnBodyClearance"/>
    /// blocks of a player, an NPC or a creature.</summary>
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
            if (_world.GetBlockIfLoaded(above).Value != 0 || !SkyExposed(above) || InAnyBaseZone(above) || InShipInterior(above)
                || NpcCellStrictlyProtected(above) || FestivalCellNearBody(above) || FestivalCellTouchesEdits(above))
            {
                return false;
            }

            cell = above;
            return true;
        }

        return false;
    }

    /// <summary>#3020: whether players changed anything in the box around the cell and its host block (their soil, a path,
    /// a dug hole) — a festival plant only grows on untouched ground. A box across the world's seam counts as touched.</summary>
    private bool FestivalCellTouchesEdits(Vector3i cell)
    {
        int circ = _world.Circumference;
        var min = Shared.World.WorldConstants.CanonicalBlock(new Vector3i(cell.X - 1, cell.Y - 2, cell.Z - 1), circ);
        var max = Shared.World.WorldConstants.CanonicalBlock(new Vector3i(cell.X + 1, cell.Y + 1, cell.Z + 1), circ);
        return min.X > max.X || min.Z > max.Z || _repo.HasPlayerBlockEdits(_world.LocationId, min, max);
    }

    /// <summary>#3020: whether a player, an NPC or a creature (companions included) stands within
    /// <see cref="FestivalSpawnBodyClearance"/> blocks of the cell.</summary>
    private bool FestivalCellNearBody(Vector3i cell)
    {
        bool Near(Vector3i feet)
        {
            int dx = Shared.World.WorldConstants.WrapDeltaX(feet.X - cell.X, _world.Circumference);
            int dz = Shared.World.WorldConstants.WrapDeltaZ(feet.Z - cell.Z, _world.Circumference);
            return Math.Abs(dx) <= FestivalSpawnBodyClearance && Math.Abs(dz) <= FestivalSpawnBodyClearance && Math.Abs(feet.Y - cell.Y) <= 3;
        }

        return JoinedInActiveWorld().Any(s => Near(s.State.Position.ToBlock()))
               || _npcs.Any(n => Near(n.Pos.ToBlock()))
               || _creatures.Any(c => Near(c.Position.ToBlock()));
    }

    /// <summary>Removes a few of the layer's own deposits that do not hold a block a festival on right now still spawns
    /// (<paramref name="keep"/>) — the cells still holding their block are cleared to air; cells the player changed just leave
    /// the ledger.</summary>
    private void ClearFestivalDeposits(Dictionary<Vector3i, ushort> deposits, HashSet<ushort> keep)
    {
        if (deposits.Count == 0)
        {
            return;
        }

        var doomed = new List<Vector3i>(FestivalCleanupPerPass);
        foreach (var (cell, block) in deposits)
        {
            if (keep.Contains(block))
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

    /// <summary>Test seam (#3084): this world's species of a flora block turns toxic, as a world's roster roll can make it. A
    /// copy replaces the entry, so a roster object other worlds may share is never changed.</summary>
    public void MakeFloraToxicForTest(string blockKey)
    {
        ushort id = _content.GetBlock(blockKey)!.NumericId.Value;
        var was = _floraSpeciesByBlock.TryGetValue(id, out var sp) ? sp : null;
        _floraSpeciesByBlock[id] = new Shared.Definitions.FloraSpecies
        {
            Id = was?.Id ?? "test",
            Name = was?.Name ?? string.Empty,
            BlockKey = blockKey,
            Toxic = true,
            Aquatic = was?.Aquatic ?? false,
            Active = was?.Active ?? true,
        };
    }
}
