using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Own-ship repair (docs/SHIP_REPAIR_PLAN.md). Unifies the two kinds of ship integrity the player
/// can lose but could not restore: the numeric <see cref="ShipState.Hull"/> (dented by combat, never
/// regenerates) and missing design voxel cells of the ship hull (holes carved by EVA mining). Both are
/// expressed as one material cost against the ship's <b>design reference</b> — the same idea the wreck
/// system uses with its intact mask — and paid with an existing metal item (no new resource):
/// <list type="bullet">
/// <item>Hull points are bought with <see cref="HullPlatingItem"/> at <see cref="HullPerPlate"/> per item.</item>
/// <item>Each missing design cell is refilled with its original block, charged the item that places that
/// block when one exists, else the same plating item as a fallback (so special blocks never block a repair).</item>
/// </list>
/// Surfaces: the cockpit "Repair ship" action (<c>Mode=all</c>, repairs everything affordable at once) and
/// guided field/EVA per-cell repair (<c>Mode=cell</c>). Material-only — there is no passive hull regen; the
/// free full restore on destruction (<see cref="DisableShip"/>) stays the casual safety net.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Existing common metal item used as generic hull plating (no new resource added).</summary>
    private const string HullPlatingItem = "iron_plate";

    /// <summary>Hull points restored per plating item spent.</summary>
    private const float HullPerPlate = 10f;

    private Dictionary<string, string>? _itemForBlockCache;

    /// <summary>Resolves the served player's OWN live ship voxel structure wherever it currently is — the
    /// in-space instance copy during flight/EVA (then <paramref name="instance"/> is non-null and drives the
    /// broadcast scope), else the parked/interior copy. Returns false when no live ship structure exists.</summary>
    private bool TryGetOwnShipStructure(string playerId, out SpaceStructure live, out SpaceInstance? instance)
    {
        live = null!;
        instance = null;

        if (_playerInstance.TryGetValue(playerId, out var iid)
            && _spaceInstances.TryGetValue(iid, out var inst)
            && inst.Structures.TryGetValue("ship:" + playerId, out var s))
        {
            live = s;
            instance = inst;
            return true;
        }

        var rec = _worlds.Active.LandedFor(playerId);
        if (rec.Placed && rec.Structure is { } ls)
        {
            live = ls;
            return true;
        }

        return false;
    }

    /// <summary>The pristine design of the served player's active ship (no persisted edits) — the repair
    /// reference. Its cells equal the live structure's <see cref="SpaceStructure.Baseline"/>, and the block at
    /// a baseline cell is the block that should be rebuilt there.</summary>
    private SpaceStructure OwnShipDesignReference(string ownerId)
        => BuildShipStructureFrom("ship:" + ownerId, ownerId,
            _content.GetShip(_ship.ShipType) ?? _content.GetShip("starter"), persistEdits: false);

    /// <summary>Missing design cells: baseline cells that are currently air, with the block the design wants there.</summary>
    private IEnumerable<(Vector3i Cell, BlockDefinition Block)> EnumerateShipRepairCells(SpaceStructure live, SpaceStructure design)
    {
        foreach (var cell in live.Baseline)
        {
            if (!live.Get(cell).IsAir)
            {
                continue; // still intact
            }

            var want = design.Get(cell);
            if (want.IsAir)
            {
                continue;
            }

            if (_content.BlockById(want) is { } def)
            {
                yield return (cell, def);
            }
        }
    }

    /// <summary>The inventory item that places a given block, if any (reverse of <see cref="ItemDefinition.PlacesBlock"/>).</summary>
    private string? ItemKeyForBlock(string blockKey)
    {
        if (_itemForBlockCache is null)
        {
            _itemForBlockCache = new Dictionary<string, string>();
            foreach (var item in _content.Items.Values)
            {
                if (!string.IsNullOrEmpty(item.PlacesBlock) && !_itemForBlockCache.ContainsKey(item.PlacesBlock!))
                {
                    _itemForBlockCache[item.PlacesBlock!] = item.Key;
                }
            }
        }

        return _itemForBlockCache.TryGetValue(blockKey, out var k) ? k : null;
    }

    /// <summary>The repair item for a block: its placing item when one exists, else the generic plating item.</summary>
    private string RepairItemForBlock(BlockDefinition block) => ItemKeyForBlock(block.Key) ?? HullPlatingItem;

    /// <summary>Aggregate material cost to FULLY repair the ship (missing cells + hull deficit), plus the
    /// missing-cell count. Cost is one flat list of item amounts, paid through <see cref="MaterialPool"/>.</summary>
    private (int Missing, List<ItemAmount> Cost) ComputeShipRepairCost(SpaceStructure live, SpaceStructure design)
    {
        var costMap = new Dictionary<string, int>();
        int missing = 0;
        foreach (var (_, def) in EnumerateShipRepairCells(live, design))
        {
            missing++;
            string item = RepairItemForBlock(def);
            costMap[item] = costMap.GetValueOrDefault(item) + 1;
        }

        int hullPlates = HullPlatesNeeded();
        if (hullPlates > 0)
        {
            costMap[HullPlatingItem] = costMap.GetValueOrDefault(HullPlatingItem) + hullPlates;
        }

        var cost = costMap.Select(kv => new ItemAmount(kv.Key, kv.Value)).ToList();
        return (missing, cost);
    }

    /// <summary>Plating items needed to top the hull back to its current maximum.</summary>
    private int HullPlatesNeeded()
    {
        float deficit = System.Math.Max(0f, _shipHullMax - _ship.Hull);
        return (int)System.Math.Ceiling(deficit / HullPerPlate);
    }

    /// <summary>Pushes the current own-ship repair readout so the client can offer "Repair ship".</summary>
    private void SendShipRepairStatus(PlayerSession session)
    {
        if (!TryGetOwnShipStructure(session.State.PlayerId, out var live, out _))
        {
            Send(session, new ShipRepairStatus { Hull = _ship.Hull, HullMax = _shipHullMax, NeedsRepair = false });
            return;
        }

        var design = OwnShipDesignReference(session.State.PlayerId);
        var (missing, cost) = ComputeShipRepairCost(live, design);
        bool free = !Rules.CraftingCostsMaterials || session.State.InstantBuild;
        var pool = new MaterialPool(_content, session.State, _ship);

        Send(session, new ShipRepairStatus
        {
            Hull = _ship.Hull,
            HullMax = _shipHullMax,
            MissingCells = missing,
            NeedsRepair = missing > 0 || _ship.Hull < _shipHullMax,
            CanAfford = free || pool.Has(cost),
            Needs = string.Join(",", cost.OrderByDescending(c => c.Count).Select(c => $"{c.Item}:{c.Count}")),
        });
    }

    private void HandleRepairShip(PlayerSession session, RepairShipIntent intent)
    {
        if (!TryGetOwnShipStructure(session.State.PlayerId, out var live, out var instance))
        {
            Reject(session, "ship_repair", "Your ship isn't here to repair.");
            return;
        }

        if (string.Equals(intent.Mode, "cell", System.StringComparison.OrdinalIgnoreCase))
        {
            RepairShipCell(session, live, instance, new Vector3i(intent.X, intent.Y, intent.Z));
            return;
        }

        RepairShipAll(session, live, instance);
    }

    /// <summary>Repairs everything the player can currently afford in one pass: hull first (the common combat
    /// case), then each missing design cell as far as the materials stretch. Partial when short on materials.</summary>
    private void RepairShipAll(PlayerSession session, SpaceStructure live, SpaceInstance? instance)
    {
        var p = session.State;
        var design = OwnShipDesignReference(p.PlayerId);
        var (missingBefore, _) = ComputeShipRepairCost(live, design);

        if (missingBefore == 0 && _ship.Hull >= _shipHullMax)
        {
            Send(session, new ServerMessage { Text = "Ship is fully intact — nothing to repair." });
            SendShipRepairStatus(session);
            return;
        }

        bool free = !Rules.CraftingCostsMaterials || p.InstantBuild;
        var pool = new MaterialPool(_content, p, _ship);
        float hullBefore = _ship.Hull;
        int cellsRepaired = 0;

        // Hull first.
        int hullPlates = HullPlatesNeeded();
        if (hullPlates > 0)
        {
            int afford = free ? hullPlates : System.Math.Min(hullPlates, pool.Count(HullPlatingItem));
            if (afford > 0)
            {
                if (!free)
                {
                    pool.Remove(new[] { new ItemAmount(HullPlatingItem, afford) });
                }

                _ship.Hull = System.Math.Min(_shipHullMax, _ship.Hull + afford * HullPerPlate);
            }
        }

        // Then the structural holes, as materials allow.
        foreach (var (cell, def) in EnumerateShipRepairCells(live, design).ToList())
        {
            string item = RepairItemForBlock(def);
            if (!free && pool.Count(item) < 1)
            {
                continue;
            }

            if (!free)
            {
                pool.Remove(new[] { new ItemAmount(item, 1) });
            }

            CommitShipRepairCell(session, live, instance, cell, def.NumericId);
            cellsRepaired++;
        }

        float hullGained = _ship.Hull - hullBefore;
        if (hullGained <= 0f && cellsRepaired == 0)
        {
            Send(session, new ServerMessage { Text = "Not enough materials to repair the ship." });
            SendShipRepairStatus(session);
            return;
        }

        SendInventory(session);
        SendShipCombatStatus(session);

        var (missingAfter, _) = ComputeShipRepairCost(live, design);
        string note = $"Repaired ship: +{(int)System.Math.Round(hullGained)} hull";
        if (cellsRepaired > 0)
        {
            note += $", {cellsRepaired} hull cell{(cellsRepaired == 1 ? "" : "s")}";
        }

        note += missingAfter == 0 && _ship.Hull >= _shipHullMax ? "." : " — more materials needed to finish.";
        Send(session, new ServerMessage { Text = note });
        SendShipRepairStatus(session);
    }

    /// <summary>Guided per-cell repair (field/EVA): refills one missing design cell with its original block.</summary>
    private void RepairShipCell(PlayerSession session, SpaceStructure live, SpaceInstance? instance, Vector3i cell)
    {
        var p = session.State;
        if (!live.Baseline.Contains(cell))
        {
            Reject(session, "ship_repair", "That cell is not part of the ship hull.");
            return;
        }

        if (!live.Get(cell).IsAir)
        {
            Reject(session, "ship_repair", "That hull cell is already intact.");
            return;
        }

        var design = OwnShipDesignReference(p.PlayerId);
        var want = design.Get(cell);
        if (want.IsAir || _content.BlockById(want) is not { } def)
        {
            Reject(session, "ship_repair", "Nothing to rebuild there.");
            return;
        }

        bool free = !Rules.CraftingCostsMaterials || p.InstantBuild;
        string item = RepairItemForBlock(def);
        var pool = new MaterialPool(_content, p, _ship);
        if (!free)
        {
            if (pool.Count(item) < 1)
            {
                Reject(session, "ship_repair", $"Repair needs a {item} block.");
                return;
            }

            pool.Remove(new[] { new ItemAmount(item, 1) });
        }

        CommitShipRepairCell(session, live, instance, cell, def.NumericId);
        SendInventory(session);
        SendShipRepairStatus(session);
    }

    /// <summary>Writes a repaired cell into the live structure, persists it as a per-cell delta (same durable
    /// store EVA/interior edits use) and broadcasts it to the right audience (space instance or world).</summary>
    private void CommitShipRepairCell(PlayerSession session, SpaceStructure live, SpaceInstance? instance, Vector3i cell, BlockId block)
    {
        live.Set(cell, block);
        _repo.SetStructureBlock(live.Id, cell, block.Value);

        var msg = new StructureBlockChanged { StructureId = live.Id, X = cell.X, Y = cell.Y, Z = cell.Z, Block = block.Value };
        if (instance != null)
        {
            BroadcastToInstance(instance, msg);
        }
        else
        {
            BroadcastToWorld(msg);
        }
    }

    // ---------------- Test hooks ----------------

    /// <summary>Test hook: run a repair intent for a player (serves the cursor first, like the dispatch does).</summary>
    public bool RepairShipForTest(string playerId, RepairShipIntent intent)
    {
        var session = FindSessionByPlayerId(playerId);
        if (session is null)
        {
            return false;
        }

        Serve(session);
        HandleRepairShip(session, intent);
        return true;
    }

    /// <summary>Test hook: current hull / max for a player.</summary>
    public (float Hull, float HullMax) ShipHullForTest(string playerId)
    {
        var session = FindSessionByPlayerId(playerId);
        if (session is null)
        {
            return (0f, 0f);
        }

        Serve(session);
        return (_ship.Hull, _shipHullMax);
    }

    /// <summary>Test hook: dent the hull to a chosen value (clamped to [0, max]).</summary>
    public void SetShipHullForTest(string playerId, float hull)
    {
        var session = FindSessionByPlayerId(playerId);
        if (session is null)
        {
            return;
        }

        Serve(session);
        _ship.Hull = System.Math.Max(0f, System.Math.Min(_shipHullMax, hull));
    }

    /// <summary>Test hook: number of missing design cells on the player's live ship structure (-1 if none here).</summary>
    public int ShipRepairMissingCellsForTest(string playerId)
    {
        var session = FindSessionByPlayerId(playerId);
        if (session is null)
        {
            return -1;
        }

        Serve(session);
        if (!TryGetOwnShipStructure(playerId, out var live, out _))
        {
            return -1;
        }

        return EnumerateShipRepairCells(live, OwnShipDesignReference(playerId)).Count();
    }

    /// <summary>Test hook: carve the first solid baseline cell to air (simulates an EVA-mined hull hole) and
    /// return its structure-local coordinates plus the item its repair will consume.</summary>
    public (int X, int Y, int Z, string Item) CarveFirstShipCellForTest(string playerId)
    {
        var session = FindSessionByPlayerId(playerId);
        if (session is null)
        {
            return (0, 0, 0, string.Empty);
        }

        Serve(session);
        if (!TryGetOwnShipStructure(playerId, out var live, out _))
        {
            return (0, 0, 0, string.Empty);
        }

        var cell = live.Baseline.First(c => !live.Get(c).IsAir);
        var was = live.Get(cell);
        live.Set(cell, BlockId.Air);
        _repo.SetStructureBlock(live.Id, cell, BlockId.AirValue);
        string item = _content.BlockById(was) is { } def ? RepairItemForBlock(def) : HullPlatingItem;
        return (cell.X, cell.Y, cell.Z, item);
    }
}
