// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Sounders;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// A mended crack answers on the real side (#3909) — once per gate and world, and only at a gate the world itself carries
/// (<see cref="IsWorldGate"/>):
/// <list type="bullet">
/// <item><b>The singing plants bloom</b>: <see cref="CrackBloomMin"/>–<see cref="CrackBloomMax"/> chime reeds, bell
/// blooms and echo flowers appear on free natural ground around the gate, as ordinary block changes nobody owns. Never
/// in a base zone, a protected structure, a settlement or on anything a player built — fewer, or none, when there is no
/// room. (The harp vine hangs from obsidian and has no place on open ground.)</item>
/// <item><b>A relic cache refills</b>: the nearest relic cache of a monument at the gate that was emptied (an emptied
/// cache is gone) stands there again with ordinary relic finds (the rare Sounder finds of a first fill are not rolled
/// again; the story-loot rule applies to it as to every relic cache).</item>
/// <item><b>The Lights leave a gift</b> for every player who took part: two crystals and an obsidian, into the backpack
/// only. Without room it waits, like everything the Sounders' story hands over (<see cref="HandSoundersItems"/>): the
/// due gift is a milestone (<see cref="CrackGiftPrefix"/>) until it is packed.</item>
/// </list>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>A mended world gate grows at least this many singing plants on the real side (room permitting) …</summary>
    internal const int CrackBloomMin = 3;

    /// <summary>… and at most this many.</summary>
    internal const int CrackBloomMax = 5;

    /// <summary>The plants bloom between these distances from the gate's middle (blocks).</summary>
    private const float CrackBloomNear = 3f;

    /// <summary>See <see cref="CrackBloomNear"/>.</summary>
    private const float CrackBloomFar = 11f;

    /// <summary>A monument's relic cache this close to the gate may refill (blocks).</summary>
    private const float CrackRelicRange = 40f;

    /// <summary>Per-player milestone of a gift of the Lights that is still to be packed:
    /// <c>snd:crack_gift:&lt;crack key&gt;</c>. Removed when the gift is in the backpack.</summary>
    internal const string CrackGiftPrefix = SoundersIds.MilestonePrefix + "crack_gift:";

    /// <summary>What the Lights leave: two crystals and an obsidian.</summary>
    private static readonly string[] CrackGift = { "crystal", "crystal", "obsidian" };

    /// <summary>The singing plants that bloom on open ground.</summary>
    private static readonly string[] CrackBloomPlants = { FloraCatalog.ChimeReedKey, FloraCatalog.BellBloomKey, FloraCatalog.EchoFlowerKey };

    /// <summary>The players a gift is due to (filled at a mend, and once per player and server run from their milestones).</summary>
    private readonly HashSet<string> _crackGiftDue = new(StringComparer.Ordinal);
    private readonly HashSet<string> _crackGiftChecked = new(StringComparer.Ordinal);

    /// <summary>The real side's answer to a mended crack of a world gate (see the type's summary).</summary>
    private void BloomRealSide(Vector3f gate, string key, List<PlayerSession> menders)
    {
        int plants = BloomSingingPlants(gate, key);
        bool cache = RefillRelicCacheNear(gate, key);
        foreach (var mender in menders)
        {
            mender.State.Milestones.Add(CrackGiftPrefix + key);
            _crackGiftDue.Add(mender.State.PlayerId);
            _repo.SavePlayer(mender.State);
            if (plants > 0)
            {
                Send(mender, new ServerMessage { Text = "@srv.sounders.crack_bloom" });
            }
        }

        _log.Info($"The real side answered the mend at ({gate.X:0},{gate.Y:0},{gate.Z:0}): {plants} plant(s), relic cache refilled: {cache}.");
    }

    // ---------------- The singing plants ----------------

    /// <summary>Plants the bloom around the gate; returns how many plants found room.</summary>
    private int BloomSingingPlants(Vector3f gate, string key)
    {
        var plants = CrackBloomPlants.Select(k => _content.GetBlock(k)).Where(b => b is not null).Select(b => b!.NumericId).ToList();
        if (plants.Count == 0)
        {
            return 0;
        }

        long seed = WorldGenerator.StableHash("bloom:" + key);
        var rng = new Random(unchecked((int)(seed ^ (seed >> 32))));
        int want = CrackBloomMin + rng.Next(CrackBloomMax - CrackBloomMin + 1);
        int placed = 0;
        for (int attempt = 0; attempt < 60 && placed < want; attempt++)
        {
            double angle = rng.NextDouble() * Math.PI * 2.0;
            double dist = CrackBloomNear + (rng.NextDouble() * (CrackBloomFar - CrackBloomNear));
            int x = (int)Math.Floor(gate.X + (Math.Cos(angle) * dist));
            int z = (int)Math.Floor(gate.Z + (Math.Sin(angle) * dist));
            var plant = plants[rng.Next(plants.Count)];
            if (BloomCell(x, (int)Math.Floor(gate.Y), z) is not { } cell)
            {
                continue;
            }

            _world.SetBlock(cell, plant); // nobody's: the world grew it
            BroadcastToWorld(new BlockChanged { X = cell.X, Y = cell.Y, Z = cell.Z, Block = plant.Value });
            placed++;
        }

        return placed;
    }

    /// <summary>The cell a plant may grow in at the column (<paramref name="x"/>, <paramref name="z"/>): the topmost open
    /// cell near the gate's height that stands on natural ground and may be planted (<see cref="BloomAllowed"/>). Null
    /// when the column offers none. Loaded chunks only.</summary>
    private Vector3i? BloomCell(int x, int y0, int z)
    {
        for (int y = y0 + 5; y >= y0 - 6; y--)
        {
            var cell = new Vector3i(x, y, z);
            var ground = new Vector3i(x, y - 1, z);
            if (!_world.GetBlockIfLoaded(cell).IsAir || !_world.GetBlockIfLoaded(new Vector3i(x, y + 1, z)).IsAir)
            {
                continue;
            }

            var below = _world.GetBlockIfLoaded(ground);
            if (below.IsAir)
            {
                continue; // still above the ground
            }

            // The first surface from above decides: nothing grows under a roof or inside a wall.
            return _content.BlockById(below) is { Category: "terrain", Solid: true } && !IsFluid(below.Value) && BloomAllowed(cell, ground)
                ? cell
                : null;
        }

        return null;
    }

    /// <summary>Whether the world may grow a plant here by itself: outside every base zone, protected structure and
    /// settlement, and neither the cell nor its ground was last touched by a player.</summary>
    private bool BloomAllowed(Vector3i cell, Vector3i ground)
        => !InAnyBaseZone(cell) && !IsProtectedStructureCell(cell) && !IsSettlementBlock(cell)
           && !IsPlayerBuilt(cell) && !IsPlayerBuilt(ground);

    private bool IsPlayerBuilt(Vector3i cell)
        => _repo.GetBlockAttribution(_world.LocationId, Shared.World.WorldConstants.CanonicalBlock(cell, _world.Circumference)) is { Owner.Length: > 0 };

    // ---------------- The relic cache ----------------

    /// <summary>
    /// Brings back the nearest relic cache of a monument within <see cref="CrackRelicRange"/> of the gate that was emptied
    /// (the loot ledger knows the cache; its container is gone once it is empty), with ordinary relic finds rolled from the
    /// crack's key. A cache that still stands is left alone. True when one came back.
    /// </summary>
    private bool RefillRelicCacheNear(Vector3f gate, string key)
    {
        const string Prefix = "monument:relic_cache:";
        (string LootKey, Vector3i Cell, double DistSq)? best = null;
        foreach (string lootKey in _meta.GeneratedLoot)
        {
            if (!lootKey.StartsWith(Prefix, StringComparison.Ordinal) || !TryParseCell(lootKey.Substring(Prefix.Length), out var cell))
            {
                continue;
            }

            // The ledger's keys carry no world: the cell must lie in a monument of THIS world, right at the gate.
            double distSq = WrapDistSq(gate, cell);
            if (distSq <= CrackRelicRange * CrackRelicRange && (best is null || distSq < best.Value.DistSq)
                && InMonument(Shared.World.WorldConstants.CanonicalBlock(cell, _world.Circumference))
                && !_containers.Any(c => c.Id == LootContainerId(lootKey)))
            {
                best = (lootKey, cell, distSq);
            }
        }

        if (best is not { } found)
        {
            return false;
        }

        long seed = WorldGenerator.StableHash("refill:" + key);
        var items = BuildStructureLoot("relic_cache", new Random(unchecked((int)(seed ^ (seed >> 32)))));
        if (items.Count == 0)
        {
            return false;
        }

        AddContainer(new StoredContainer
        {
            Id = LootContainerId(found.LootKey),
            Planet = _world.LocationId,
            Kind = "salvage",
            Position = found.Cell,
            Items = items,
        });
        return true;
    }

    /// <summary>The container id <c>SpawnStructureLoot</c> gives the cache of a loot-ledger key.</summary>
    private static string LootContainerId(string lootKey) => "loot_" + lootKey.Replace(':', '_');

    private static bool TryParseCell(string text, out Vector3i cell)
    {
        cell = default;
        string[] parts = text.Split(':');
        if (parts.Length != 3
            || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int x)
            || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int y)
            || !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int z))
        {
            return false;
        }

        cell = new Vector3i(x, y, z);
        return true;
    }

    // ---------------- The gift of the Lights ----------------

    /// <summary>
    /// Packs the gifts of the Lights that are due to <paramref name="s"/> (from the 1 Hz story pass) — one gift per mended
    /// crack, each whole or not at all. A gift without room waits in <paramref name="pass"/> (VEGA asks for room, the next
    /// pass tries again). True when at least one gift went into the backpack; the caller saves and tells.
    /// </summary>
    private bool HandCrackGifts(PlayerSession s, SoundersCatchUpPass pass)
    {
        string id = s.State.PlayerId;
        if (_crackGiftChecked.Add(id) && s.State.Milestones.Any(k => k.StartsWith(CrackGiftPrefix, StringComparison.Ordinal)))
        {
            _crackGiftDue.Add(id); // a gift that was still waiting when the player left, or the server stopped
        }

        if (!_crackGiftDue.Contains(id))
        {
            return false;
        }

        bool handed = false;
        var names = new List<string>();
        foreach (string due in s.State.Milestones.Where(k => k.StartsWith(CrackGiftPrefix, StringComparison.Ordinal)).ToList())
        {
            if (!HandSoundersItems(s, pass, names, CrackGift))
            {
                break; // no room: this one and the ones after it wait
            }

            s.State.Milestones.Remove(due);
            handed = true;
        }

        if (!s.State.Milestones.Any(k => k.StartsWith(CrackGiftPrefix, StringComparison.Ordinal)))
        {
            _crackGiftDue.Remove(id);
        }

        return handed;
    }

    // ---------------- Test seams ----------------

    /// <summary>Test seam: whether a gift of the Lights still waits for room in this player's backpack.</summary>
    public bool CrackGiftWaitsForTest(PlayerSession s)
        => s.State.Milestones.Any(k => k.StartsWith(CrackGiftPrefix, StringComparison.Ordinal));
}
