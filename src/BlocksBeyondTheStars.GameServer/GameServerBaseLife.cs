// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The world notices your base (#1120, stages 1–2): trader ships prefer landing on worlds with a founded
/// base (stage 1, see <c>PickLandableBody</c>) and hail the base owner over the radio when they set down;
/// a settler NPC moves in once a base carries enough machines (stage 2) — they greet, count as a KNOWN
/// acquaintance, and appear in the "People you know" roster. No NPC ever damages a block; family/peaceful
/// presets get exactly these two friendly stages (bandit scouting is a later, opt-in feature — D4/#1122).
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Machines (block category "machine", the base core itself excluded) a base needs before a
    /// settler moves in — a bare marker post is a claim, not a home.</summary>
    private const int BaseSettlerMachineCount = 3;

    /// <summary>How often ONE base is (re)checked — the scan walks the base's 17³ zone, so it round-robins.</summary>
    private const double BaseLifeScanInterval = 10.0;

    /// <summary>Settler NPC ids per base id (transient — NPCs are per-world and respawn via the scan).</summary>
    private readonly Dictionary<int, int> _baseSettlerNpcIds = new();

    private double _nextBaseLifeAt;
    private int _baseLifeCursor;

    /// <summary>Round-robin base scan (Guard-registered): spawns a settler when a base earned one, and
    /// removes the settler again when its base was dissolved.</summary>
    private void TickBaseLife()
    {
        if (_uptime < _nextBaseLifeAt)
        {
            return;
        }

        _nextBaseLifeAt = _uptime + BaseLifeScanInterval;

        // A dissolved base takes its settler with it.
        foreach (var (baseId, npcId) in _baseSettlerNpcIds.ToList())
        {
            if (_bases.All(b => b.Id != baseId))
            {
                _baseSettlerNpcIds.Remove(baseId);
                int removed = _npcs.RemoveAll(n => n.Id == npcId);
                if (removed > 0)
                {
                    BroadcastNpcs();
                }
            }
        }

        var here = _bases.Where(b => b.Planet == _world.LocationId).ToList();
        if (here.Count == 0)
        {
            return;
        }

        var candidate = here[_baseLifeCursor++ % here.Count];
        if (_baseSettlerNpcIds.ContainsKey(candidate.Id) || CountBaseMachines(candidate) < BaseSettlerMachineCount)
        {
            return;
        }

        SpawnBaseSettler(candidate);
    }

    /// <summary>Machine-category blocks inside the base zone (base_core itself excluded).</summary>
    private int CountBaseMachines(ServerBase b)
    {
        int count = 0;
        int r = BaseProtectionRadius;
        for (int x = -r; x <= r; x++)
            for (int y = -r; y <= r; y++)
                for (int z = -r; z <= r; z++)
                {
                    var pos = new Vector3i(b.Cell.X + x, b.Cell.Y + y, b.Cell.Z + z);
                    if (!WithinBuildHeight(pos.Y))
                    {
                        continue;
                    }

                    var block = _world.GetBlock(WorldConstants.CanonicalBlock(pos, _world.Circumference));
                    if (block.IsAir)
                    {
                        continue;
                    }

                    var def = _content.BlockById(block);
                    if (def is { Category: "machine" } && def.Key != "base_core")
                    {
                        count++;
                    }
                }

        return count;
    }

    /// <summary>Stage 2: a settler moves in — deterministic look per base, home beside the core, KNOWN to
    /// the owner from day one (the plan's "counts as a known NPC"), announced over the owner's radio.</summary>
    private void SpawnBaseSettler(ServerBase b)
    {
        var rng = new System.Random(unchecked((int)WorldGenerator.StableHash("base-settler:" + b.Id)));
        var home = new Vector3f(b.Cell.X + 2.5f, b.Cell.Y + 1f, b.Cell.Z + 2.5f);
        var npc = MakeNpc("settler", "settlers", robotic: false, home, rng);
        npc.Settlement = b.Name; // keys the NPC memory to this base (settle_<hash of base name>)
        _npcs.Add(npc);
        BroadcastNpcs();

        string npcKey = NpcKey(SettlementLocationKey(b.Name), "settler");
        _baseSettlerNpcIds[b.Id] = npc.Id;

        if (FindSessionByPlayerId(b.OwnerId) is { Joined: true } owner)
        {
            // The plan says the settler "counts as a known NPC": seed the acquaintance so the nameplate
            // shows a stage and the roster lists them right away.
            if (!owner.State.NpcMemory.ContainsKey(npcKey))
            {
                owner.State.NpcMemory[npcKey] = new NpcRelationship
                {
                    Name = npc.Name,
                    Role = "settler",
                    Place = b.Name,
                    Value = 10, // the "known" threshold
                };
            }

            SendNpcStandings(owner);
            TryNpcRadioCall(owner, npcKey, npc.Name, b.Name, b.Planet,
                "settler:" + b.Id, "npc.call.settler", string.Empty, isMission: false);
            _repo.SavePlayer(owner.State);
        }
    }

    /// <summary>Stage 1's hail (#1120): a trader just set down on a body — base owners there get a call
    /// (no acquaintance required; the trader is advertising).</summary>
    private void NpcRadioOnTraderLanded(string bodyId, string traderName)
    {
        string bodyName = _galaxy?.FindBody(bodyId)?.Name ?? bodyId;
        foreach (var b in _bases)
        {
            if (b.Planet != bodyId || FindSessionByPlayerId(b.OwnerId) is not { Joined: true } owner)
            {
                continue;
            }

            TryNpcRadioCall(owner, npcKey: string.Empty, traderName, bodyName, bodyId,
                "trader:" + bodyId, "npc.call.trader", bodyName, isMission: false, requireKnown: false);
        }
    }

    /// <summary>Test seam: run one base-life scan for every base on the active world right now.</summary>
    public void ScanBaseLifeForTest()
    {
        _nextBaseLifeAt = 0;
        for (int i = 0; i < System.Math.Max(1, _bases.Count); i++)
        {
            TickBaseLife();
            _nextBaseLifeAt = 0;
        }
    }

    /// <summary>Test seam: the settler NPC id for a base, or null when none moved in yet.</summary>
    public int? BaseSettlerForTest(int baseId) => _baseSettlerNpcIds.TryGetValue(baseId, out int id) ? id : null;
}
