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

    /// <summary>Settler NPC ids per base id, with the world they live on (transient — NPCs are per-world
    /// and respawn via the scan; the world id keeps the sweep from touching same-numbered NPCs of other
    /// worlds, #1152).</summary>
    private readonly Dictionary<int, (string WorldId, int NpcId)> _baseSettlerNpcIds = new();

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

        // A dissolved base takes its settler with it — but only ever on the settler's own world: NPC ids
        // restart at 1 per world, so a blind remove-by-id could delete an unrelated NPC elsewhere (#1152).
        foreach (var (baseId, settler) in _baseSettlerNpcIds.ToList())
        {
            if (settler.WorldId != _world.LocationId)
            {
                continue; // that world isn't loaded — handled once it is active again
            }

            if (_bases.All(b => b.Id != baseId))
            {
                _baseSettlerNpcIds.Remove(baseId);
                int removed = _npcs.RemoveAll(n => n.Id == settler.NpcId && n.Role == "settler");
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
        if (HasLiveBaseSettler(candidate))
        {
            RehomeWedgedSettler(candidate); // the owner may have built over the settler's spot since
            return;
        }

        if (CountBaseMachines(candidate) < BaseSettlerMachineCount)
        {
            return;
        }

        SpawnBaseSettler(candidate);
    }

    /// <summary>
    /// Where the base settler lives. The first version put them at a fixed core+(2, 1, 2) with no look at
    /// what stood there, so an owner who had built a wall, a machine or a stair on that spot got a settler
    /// permanently wedged inside it — the leash kept walking them back into the block (#1248, a player
    /// report). Try the classic spot first (existing bases keep their settler where it was when it is free),
    /// then ring outwards through the base zone for the nearest column with two air cells over a floor that
    /// is not inside a parked ship; the classic spot is the last resort when the whole zone is built solid.
    /// </summary>
    private Vector3f SettlerHomeNear(Vector3i core)
    {
        var legacy = new Vector3f(core.X + 2.5f, core.Y + 1f, core.Z + 2.5f);
        if (StandableSpot(core.X + 2, core.Y + 1, core.Z + 2) is { } classic)
        {
            return classic;
        }

        for (int r = 1; r <= BaseProtectionRadius; r++)
            for (int dx = -r; dx <= r; dx++)
                for (int dz = -r; dz <= r; dz++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != r || (dx == 0 && dz == 0))
                    {
                        continue; // ring r only — inner rings were already searched
                    }

                    // Feet from two above the core down to two below it: a raised floor, a slope or a dug-out
                    // yard all count, a basement further down does not (the settler should be seen).
                    for (int y = core.Y + 2; y >= core.Y - 2; y--)
                    {
                        if (StandableSpot(core.X + dx, y, core.Z + dz) is { } spot)
                        {
                            return spot;
                        }
                    }
                }

        return legacy;
    }

    /// <summary>The feet position for a cell a human-sized NPC can stand in: a blocking floor under two
    /// free cells, outside every parked ship's hull (nobody moves into the owner's cockpit); null otherwise.</summary>
    private Vector3f? StandableSpot(int x, int y, int z)
    {
        if (!WithinBuildHeight(y) || !IsBodyBlockingCell(x, y - 1, z) || IsBodyBlockingCell(x, y, z) || IsBodyBlockingCell(x, y + 1, z))
        {
            return null;
        }

        var feet = new Vector3f(x + 0.5f, y, z + 0.5f);
        return ShipInteriorContains(new Vector3f(feet.X, y + 0.5f, feet.Z)) ? null : feet;
    }

    /// <summary>A settler whose home cell got built over since they moved in (#1248) is moved to the nearest
    /// free spot — otherwise the leash walks them straight back into the new wall every tick.</summary>
    private void RehomeWedgedSettler(ServerBase b)
    {
        if (!_baseSettlerNpcIds.TryGetValue(b.Id, out var s) || _npcs.FirstOrDefault(n => n.Id == s.NpcId && n.Role == "settler") is not { } npc)
        {
            return;
        }

        int hx = (int)Math.Floor(npc.Home.X), hy = (int)Math.Floor(npc.Home.Y), hz = (int)Math.Floor(npc.Home.Z);
        if (StandableSpot(hx, hy, hz) is not null)
        {
            return; // the home is still a place to stand
        }

        var home = SettlerHomeNear(b.Cell);
        if (home.Equals(npc.Home))
        {
            return; // nothing better in the zone — leave them rather than jitter every scan
        }

        npc.Home = home;
        npc.Pos = home;
        BroadcastNpcs();
    }

    /// <summary>Whether the base's settler is actually standing on the active world — a world switch clears
    /// the NPC list, so a stale mapping must not block the respawn (#1152).</summary>
    private bool HasLiveBaseSettler(ServerBase b)
        => _baseSettlerNpcIds.TryGetValue(b.Id, out var s)
            && s.WorldId == _world.LocationId
            && _npcs.Any(n => n.Id == s.NpcId && n.BaseId == b.Id && n.Role == "settler");

    /// <summary>Renaming a base keeps its settler (#1262): the live NPC's display settlement and the owner's
    /// roster entry follow the new name. Before this the scan compared the NPC's settlement to the base name,
    /// saw "no settler" after a rename and spawned a second one under a fresh name-hash key.</summary>
    private void RenameBaseSettler(ServerBase b, string newName)
    {
        foreach (var npc in _npcs)
        {
            if (npc.BaseId == b.Id)
            {
                npc.Settlement = newName;
            }
        }

        if (FindSessionByPlayerId(b.OwnerId) is { } owner
            && owner.State.NpcMemory.TryGetValue(BaseSettlerKey(b.Id), out var rel))
        {
            rel.Place = newName;
        }
    }

    /// <summary>Pre-#1262 saves keyed the base settler by a hash of the base NAME, so every rename minted a
    /// fresh entry — "my settler is listed three times". Moves the entry for the current name onto the
    /// rename-proof base-id key and drops the stale name-keyed copies of the same settler.</summary>
    private void MigrateBaseSettlerMemory(PlayerSession session)
    {
        var mem = session.State.NpcMemory;
        bool changed = false;
        foreach (var b in _bases)
        {
            if (b.OwnerId != session.State.PlayerId)
            {
                continue;
            }

            string key = BaseSettlerKey(b.Id);
            string legacy = NpcKey(SettlementLocationKey(b.Name), "settler");
            if (!mem.ContainsKey(key) && mem.TryGetValue(legacy, out var rel))
            {
                mem.Remove(legacy);
                rel.Place = b.Name;
                mem[key] = rel;
                changed = true;
            }
        }

        // Stale name-keyed copies of a settler we now know by base id: same coined name (the settler's
        // look and name are seeded from the base id, so every duplicate carried the same name).
        var known = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (key, rel) in mem)
        {
            if (key.StartsWith("base_", StringComparison.Ordinal) && rel.Role == "settler")
            {
                known.Add(rel.Name);
            }
        }

        foreach (var stale in mem.Where(kv => kv.Key.StartsWith("settle_", StringComparison.Ordinal)
                     && kv.Value.Role == "settler" && known.Contains(kv.Value.Name)).Select(kv => kv.Key).ToList())
        {
            mem.Remove(stale);
            changed = true;
        }

        if (changed)
        {
            _repo.SavePlayer(session.State);
        }
    }

    /// <summary>Test entrypoint for the pre-#1262 memory migration.</summary>
    public void MigrateBaseSettlerMemoryForTest(PlayerSession session) => MigrateBaseSettlerMemory(session);

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
        var home = SettlerHomeNear(b.Cell);
        var npc = MakeNpc("settler", "settlers", robotic: false, home, rng);
        npc.Settlement = b.Name; // display name for greetings/dialogs — the memory key is the base ID (#1262)
        npc.BaseId = b.Id;
        _npcs.Add(npc);
        BroadcastNpcs();

        string npcKey = BaseSettlerKey(b.Id);
        _baseSettlerNpcIds[b.Id] = (b.Planet, npc.Id);

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
    public int? BaseSettlerForTest(int baseId) => _baseSettlerNpcIds.TryGetValue(baseId, out var s) ? s.NpcId : null;
}
