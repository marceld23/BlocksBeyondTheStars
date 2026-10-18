// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Floating space salvage (#1475, persisted since #2748): the <see cref="CombatEntityKind.ResourceDrop"/> entities a
/// space instance holds — tractor-beam loot waiting to be pulled in, and the overflow a full hold left floating at the
/// rock. Space drops never expire, so they must outlive both the instance and the server process:
/// <list type="bullet">
/// <item>An instance teardown (last pilot landed, ship disabled, a walkabout or a quit leaving no parked ship) stashes
/// its drops by instance id; the next time that space is created they are re-added.</item>
/// <item>The stash plus the drops of every live instance are written as one named blob
/// (<see cref="FloatingSalvageBlob"/>) inside <see cref="SaveAll"/>'s transaction — atomic with the players' cargo —
/// and a teardown refreshes its own instance's entry at once.</item>
/// <item>On boot the blob is read back into the stash with FRESH entity ids (the id counter restarts at 1 every
/// boot, so persisted ids could collide).</item>
/// </list>
/// Each instance keeps at most <see cref="MaxFloatingSalvagePerInstance"/> drops; beyond that the oldest are merged
/// into the next one, so nothing is lost and nothing piles up forever. The blob is additive: an older save simply has
/// none (no save-version bump).
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The named blob holding the floating salvage: instance id → drops (position + loot).</summary>
    private const string FloatingSalvageBlob = "space:floating_salvage";

    /// <summary>The most drops one instance keeps across a teardown or a restart; the oldest merge into the next.</summary>
    internal const int MaxFloatingSalvagePerInstance = 48;

    /// <summary>Salvage drops of torn-down instances, keyed by instance id, re-added when that space is created again.</summary>
    private readonly Dictionary<string, List<CombatEntity>> _floatingSalvage = new();

    /// <summary>What the blob holds per instance, as last written or loaded. A teardown refreshes only its own entry
    /// (the others stay what the last save wrote, consistent with the cargo that save wrote); a save refreshes all.</summary>
    private readonly SortedDictionary<string, List<FloatingSalvageRecord>> _floatingSalvageOnDisk = new(StringComparer.Ordinal);

    /// <summary>The blob as last written or loaded (canonical form) — an unchanged blob is not written again.</summary>
    private string _floatingSalvageJson = "{}";

    /// <summary>One persisted drop.</summary>
    private sealed class FloatingSalvageRecord
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public List<ItemAmount> Loot { get; set; } = new();
    }

    /// <summary>Removes an instance nobody flies in any more: its floating salvage is stashed (and written), a carve
    /// ledger changed during the flight is persisted (#2354), and the instance goes. Every teardown runs through here.</summary>
    private void TearDownSpaceInstance(SpaceInstance instance)
    {
        StashFloatingSalvage(instance); // #1475: uncollected ore outlives the flight
        PersistSpaceSalvageLedger();    // #2354: a half-carved wreck keeps its state across the teardown
        _spaceInstances.Remove(instance.Id);
        FlushFloatingSalvage(instance.Id); // #2748: …and a server restart
    }

    private static IEnumerable<CombatEntity> FloatingDropsOf(SpaceInstance instance)
        => instance.Entities.Where(e => e.Kind == CombatEntityKind.ResourceDrop && e.Loot.Count > 0);

    private void StashFloatingSalvage(SpaceInstance instance)
    {
        var drops = FloatingDropsOf(instance).ToList();
        if (drops.Count == 0)
        {
            _floatingSalvage.Remove(instance.Id);
            return;
        }

        CapFloatingSalvage(drops, d => d.Loot, (d, loot) => d.Loot = loot);
        _floatingSalvage[instance.Id] = drops;
    }

    private void RestoreFloatingSalvage(SpaceInstance instance)
    {
        if (_floatingSalvage.Remove(instance.Id, out var drops))
        {
            instance.Entities.AddRange(drops);
        }
    }

    /// <summary>Boot (registries pass): reads the persisted floating salvage into the stash, each drop with a fresh
    /// entity id. Unknown items (content removed since) and broken entries are skipped; an unreadable blob is logged
    /// and treated as empty.</summary>
    private void LoadFloatingSalvage()
    {
        _floatingSalvage.Clear();
        _floatingSalvageOnDisk.Clear();
        _floatingSalvageJson = "{}";
        if (_repo.LoadNamedBlob(FloatingSalvageBlob) is not { Length: > 0 } json)
        {
            return;
        }

        Dictionary<string, List<FloatingSalvageRecord>>? stored;
        try
        {
            stored = JsonSerializer.Deserialize<Dictionary<string, List<FloatingSalvageRecord>>>(json);
        }
        catch (JsonException ex)
        {
            _log.Warn($"The floating space salvage could not be read ({ex.Message}); starting without it.");
            return;
        }

        int restored = 0;
        foreach (var (instanceId, records) in stored ?? new Dictionary<string, List<FloatingSalvageRecord>>())
        {
            if (string.IsNullOrEmpty(instanceId) || records is null)
            {
                continue;
            }

            var drops = new List<CombatEntity>();
            foreach (var record in records)
            {
                if (record is null || !float.IsFinite(record.X) || !float.IsFinite(record.Y) || !float.IsFinite(record.Z))
                {
                    continue;
                }

                var loot = (record.Loot ?? new List<ItemAmount>())
                    .Where(l => l is { Count: > 0 } && !string.IsNullOrEmpty(l.Item) && _content.GetItem(l.Item) is not null)
                    .Select(l => new ItemAmount(l.Item, l.Count))
                    .ToList();
                if (loot.Count > 0)
                {
                    drops.Add(NewFloatingDrop(new Vector3f(record.X, record.Y, record.Z), loot));
                }
            }

            if (drops.Count == 0)
            {
                continue;
            }

            CapFloatingSalvage(drops, d => d.Loot, (d, loot) => d.Loot = loot);
            _floatingSalvage[instanceId] = drops;
            SetFloatingSalvageOnDisk(instanceId, drops);
            restored += drops.Count;
        }

        _floatingSalvageJson = JsonSerializer.Serialize(_floatingSalvageOnDisk);
        if (restored > 0)
        {
            _log.Info($"Restored {restored} floating salvage drop(s) in {_floatingSalvage.Count} space instance(s).");
        }
    }

    /// <summary>Writes the floating salvage blob when it changed. With <paramref name="instanceId"/> (a teardown) only
    /// that instance's entry is refreshed from its stash; without (a save, inside <see cref="SaveAll"/>'s transaction)
    /// every stash and every live instance's drops are captured.</summary>
    private void FlushFloatingSalvage(string? instanceId = null)
    {
        if (instanceId is null)
        {
            _floatingSalvageOnDisk.Clear();
            foreach (var (id, drops) in _floatingSalvage)
            {
                SetFloatingSalvageOnDisk(id, drops);
            }

            foreach (var instance in _spaceInstances.Values)
            {
                SetFloatingSalvageOnDisk(instance.Id, FloatingDropsOf(instance));
            }
        }
        else
        {
            _floatingSalvageOnDisk.Remove(instanceId);
            if (_floatingSalvage.TryGetValue(instanceId, out var drops))
            {
                SetFloatingSalvageOnDisk(instanceId, drops);
            }
        }

        string json = JsonSerializer.Serialize(_floatingSalvageOnDisk);
        if (json == _floatingSalvageJson)
        {
            return;
        }

        _repo.SaveNamedBlob(FloatingSalvageBlob, json);
        _floatingSalvageJson = json;
    }

    /// <summary>Adds <paramref name="drops"/> to an instance's on-disk entry (after anything already there), capped.</summary>
    private void SetFloatingSalvageOnDisk(string instanceId, IEnumerable<CombatEntity> drops)
    {
        // A non-finite position would make the serializer throw inside SaveAll's transaction — such a drop is not kept.
        var records = new List<FloatingSalvageRecord>();
        foreach (var d in drops)
        {
            if (float.IsFinite(d.Position.X) && float.IsFinite(d.Position.Y) && float.IsFinite(d.Position.Z))
            {
                records.Add(new FloatingSalvageRecord
                {
                    X = d.Position.X,
                    Y = d.Position.Y,
                    Z = d.Position.Z,
                    Loot = d.Loot.Select(l => new ItemAmount(l.Item, l.Count)).ToList(),
                });
            }
        }

        if (records.Count == 0)
        {
            return;
        }

        if (_floatingSalvageOnDisk.TryGetValue(instanceId, out var existing))
        {
            existing.AddRange(records);
            records = existing;
        }

        CapFloatingSalvage(records, r => r.Loot, (r, loot) => r.Loot = loot);
        _floatingSalvageOnDisk[instanceId] = records;
    }

    private CombatEntity NewFloatingDrop(Vector3f at, List<ItemAmount> loot) => new()
    {
        Id = NextEntityId(),
        Kind = CombatEntityKind.ResourceDrop,
        Hostile = false,
        Hull = 1f,
        HullMax = 1f,
        Position = at,
        Loot = loot,
    };

    /// <summary>Keeps at most <see cref="MaxFloatingSalvagePerInstance"/> drops (oldest first in the list): the oldest
    /// is merged into the next one until the list fits — the loot survives, only the pile of drops shrinks.</summary>
    private static void CapFloatingSalvage<T>(List<T> drops, Func<T, List<ItemAmount>> lootOf, Action<T, List<ItemAmount>> setLoot)
    {
        while (drops.Count > MaxFloatingSalvagePerInstance)
        {
            var oldest = drops[0];
            drops.RemoveAt(0);
            var merged = new List<ItemAmount>();
            foreach (var l in lootOf(oldest).Concat(lootOf(drops[0])))
            {
                if (merged.Find(m => m.Item == l.Item) is { } same)
                {
                    same.Count += l.Count;
                }
                else
                {
                    merged.Add(new ItemAmount(l.Item, l.Count));
                }
            }

            setLoot(drops[0], merged);
        }
    }

    /// <summary>Test/inspection: salvage drops parked for a torn-down instance (#1475).</summary>
    public int FloatingSalvageParkedForTest(string instanceId)
        => _floatingSalvage.TryGetValue(instanceId, out var drops) ? drops.Count : 0;

    /// <summary>Test seam (#2748): a salvage drop floating in the pilot's flight instance, as a tractor-beam kill or a
    /// full hold leaves one.</summary>
    public CombatEntity AddFloatingSalvageForTest(string playerId, Vector3f at, string item, int count)
    {
        if (!_playerInstance.TryGetValue(playerId, out var id) || !_spaceInstances.TryGetValue(id, out var instance))
        {
            throw new InvalidOperationException($"'{playerId}' is not in space.");
        }

        var drop = NewFloatingDrop(at, new List<ItemAmount> { new(item, count) });
        instance.Entities.Add(drop);
        return drop;
    }
}
