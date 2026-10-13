// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Bio;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Props that need something to stand on (#2532): "When I mine a plant and the block below it, the seed should
/// disappear because there's no block under it!" A plant, a sapling, a torch or a lantern used to float above a
/// dug-out hole for good — the host rule existed only at placement and at regrow. Now the support hook every
/// air-making path calls (<see cref="OnSupportRemoved"/>) also looks at the props the vacated cell carried:
/// <list type="bullet">
/// <item>a <b>standing</b> plant above it and a <b>hanging</b> plant (#1759) below it pop — but only when the host
/// cell is now air or a fluid. A host that merely turned into another block (grass → dirt) never pops anything, so
/// a meadow survives its ground changing; a lily whose host IS water stays on water.</item>
/// <item><b>wild flora just vanishes</b>; a <b>sapling</b> or a <b>bred plant</b> — the player's investment — drops
/// its item on the ground (the seedling keeps its species), and a <b>fruit</b> under a mined leaf drops its harvest
/// (a kid clearing a crown must not lose the fruit).</item>
/// <item>a <b>torch or lantern</b> (lit or switched off by the Crystal Net) standing on the cell pops and drops its
/// item — unless a wall beside it or a block above it can hold it: the light props store no mount direction, so a
/// torch with a wall next to it is read as wall-mounted and stays.</item>
/// <item>a <b>pending regrow</b> of a harvested plant that rooted in the vacated cell is cancelled — the server would
/// never regrow it into the air anyway, and the client's growing sprout goes with the ground.</item>
/// </list>
/// A pop is a removal like any other: the air is persisted as the cell's edit, broadcast, written back into a player
/// station's grid, the neighbouring water flows in, and the hook recurses (a hanging vine below a hanging vine).
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The torch and the lantern, lit or switched off by the Crystal Net (#2520) — the light props that stand on
    /// a block.</summary>
    private static bool IsStandingLightKey(string key) => key is "torch" or "lantern" or "torch_off" or "lantern_off";

    // While the Crystal Net ticks, its beats walk the net's cells (machines, actuators) and may remove blocks on the way —
    // an auto-drill's bite, a bridge deck pulled in, a piston retracting. A prop popped then could be a wired lamp, whose
    // unregistering would change the cell table under the running loop; so the hosts wait here until the tick is over.
    private int _propPopDeferral;
    private readonly List<Vector3i> _deferredPropHosts = new();

    /// <summary>Runs <paramref name="action"/> with prop pops held back, then pops what lost its hold meanwhile.</summary>
    private void WithPropPopsDeferred(System.Action action)
    {
        _propPopDeferral++;
        try
        {
            action();
        }
        finally
        {
            _propPopDeferral--;
        }

        if (_propPopDeferral > 0 || _deferredPropHosts.Count == 0)
        {
            return;
        }

        var hosts = _deferredPropHosts.ToList();
        _deferredPropHosts.Clear();
        foreach (var host in hosts)
        {
            OnPropSupportRemoved(host); // re-reads the cell: a deck pushed out again meanwhile holds its prop
        }
    }

    /// <summary>#2532: the props <paramref name="host"/> carried lose their hold when it is now air or a fluid — the
    /// standing plant, torch or lantern above it and the hanging plant below it.</summary>
    private void OnPropSupportRemoved(Vector3i host)
    {
        if (_crystalFrame is not null)
        {
            return; // a ship's own net moved one of ITS cells (bridge, piston): ship-local coordinates, not this world's
        }

        if (_propPopDeferral > 0)
        {
            _deferredPropHosts.Add(host); // mid crystal beat: a pop could unregister a wired lamp under a running loop
            return;
        }

        var hostId = _world.GetBlock(host);
        if (!(hostId.IsAir || IsFluid(hostId.Value) || IsLiquid(hostId)))
        {
            return; // the cell holds a block again (or never lost it): every prop on it keeps standing
        }

        PopIfUnsupported(new Vector3i(host.X, host.Y + 1, host.Z), hostId, hanging: false);
        PopIfUnsupported(new Vector3i(host.X, host.Y - 1, host.Z), hostId, hanging: true);
    }

    /// <summary>Pops the prop in <paramref name="cell"/> when it rooted in the lost host — a standing one above the
    /// host (<paramref name="hanging"/> false) or a hanging plant below it — or cancels the pending regrow of a plant
    /// that did.</summary>
    private void PopIfUnsupported(Vector3i cell, BlockId hostId, bool hanging)
    {
        if (!WithinBuildHeight(cell.Y) || InShipInterior(cell))
        {
            return; // a parked ship's furnishing is never disturbed (the granular rule)
        }

        var id = _world.GetBlock(cell);
        if (id.IsAir)
        {
            CancelOrphanedRegrow(cell, hostId, hanging);
            return;
        }

        if (IsFlora(id.Value))
        {
            if (_floraHangingIds.Contains(id.Value) != hanging || FloraRootsIn(id.Value, hostId))
            {
                return; // roots on the other side, or in this very fluid (a lily on its pond)
            }

            PopProp(cell, id, PlantDrops(cell, id));
        }
        else if (!hanging && _world.Definition(id) is { } def && IsStandingLightKey(def.Key) && !HeldBySide(cell))
        {
            PopProp(cell, id, LightDrops(cell, def));
        }
    }

    /// <summary>True when a fluid host is the plant's own ground (a lily pad roots in water) — the one case where a
    /// fluid under a plant is no lost support.</summary>
    private bool FloraRootsIn(ushort floraId, BlockId hostId)
        => !hostId.IsAir && _floraHostIds.TryGetValue(floraId, out var hosts) && hosts.Contains(hostId.Value);

    /// <summary>What a popped plant leaves on the ground: a sapling its own item, a bred plant its seedling (its species
    /// rides along, so it can be planted again), a fruit its harvest, other wild flora nothing.</summary>
    private List<ItemAmount> PlantDrops(Vector3i cell, BlockId id)
    {
        if (IsSapling(id.Value))
        {
            return _world.Definition(id) is { } def ? def.Drops.Select(d => new ItemAmount(d.Item, d.Count)).ToList() : new List<ItemAmount>();
        }

        if (IsBredPlant(id.Value) && BredSeedAt(cell) is var seed and not 0)
        {
            return new List<ItemAmount> { new(ItemKey.WithSeed(BioItems.Seedling, seed), 1) };
        }

        // A fruit (#2038, FiFi's berries too) falls with its leaf instead of vanishing — a kid must not lose the fruit
        // they cleared the crown for. It drops what picking it gives: the poisonous twin where the species is toxic.
        if (_fruitIds.Contains(id.Value) && _world.Definition(id) is { } fruit)
        {
            bool toxic = _floraSpeciesByBlock.TryGetValue(id.Value, out var sp) && sp.Toxic;
            return fruit.Drops.Select(d => new ItemAmount(toxic ? ToxicCounterpart(d.Item) : d.Item, d.Count)).ToList();
        }

        return new List<ItemAmount>();
    }

    /// <summary>A popped torch or lantern drops what mining it gives — in its dye and glow when the item places this
    /// very block (the break path's round trip); an unlit one drops the lit item, as mining it does.</summary>
    private List<ItemAmount> LightDrops(Vector3i cell, BlockDefinition def)
    {
        var (tint, glow) = _world.GetModifier(cell);
        var drops = new List<ItemAmount>();
        foreach (var d in def.Drops)
        {
            string item = d.Item;
            if ((tint != 0 || glow != 0) && _content.GetItem(item)?.PlacesBlock == def.Key)
            {
                item = ItemKey.Compose(item, tint, glow);
            }

            drops.Add(new ItemAmount(item, d.Count));
        }

        return drops;
    }

    /// <summary>A light prop with a wall beside it or a block over it counts as mounted there — the cross-billboard
    /// stores no mount direction, so the conservative reading keeps every torch a wall could be holding.</summary>
    private bool HeldBySide(Vector3i cell)
    {
        bool Holds(int dx, int dy, int dz)
            => IsCollidingBlock(_world.GetBlock(new Vector3i(cell.X + dx, cell.Y + dy, cell.Z + dz)), fluidsPass: true, foliagePasses: false);

        return Holds(1, 0, 0) || Holds(-1, 0, 0) || Holds(0, 0, 1) || Holds(0, 0, -1) || Holds(0, 1, 0);
    }

    /// <summary>Removes an unsupported prop the way a break removes a block: the air is the cell's edit (persisted by
    /// <c>SetBlock</c>), every client hears it, a player station's grid follows, the plant's growth and species records
    /// go, a Crystal Net lamp leaves the net, the drops spill onto the ground, neighbouring fluid flows into the freed
    /// cell and whatever stood on or hung from the prop follows it.</summary>
    private void PopProp(Vector3i cell, BlockId id, List<ItemAmount> drops)
    {
        var def = _world.Definition(id);
        _world.SetBlock(cell, BlockId.Air);
        _miningProgress.Remove(cell);
        OnCrystalBlockRemoved(cell, def); // a lamp wired into the Crystal Net leaves it (#2046)
        BroadcastToWorld(new BlockChanged { X = cell.X, Y = cell.Y, Z = cell.Z, Block = BlockId.AirValue });
        WriteBackStationCell(cell, BlockId.Air); // #1481/#1857: the station's grid mirrors what stands there
        if (IsFlora(id.Value))
        {
            ForgetFloraRegrow(cell); // a sapling's growth clock (#1774) — nothing grows in the air
            if (IsBredPlant(id.Value))
            {
                ForgetBredPlant(cell); // #2209: its species record goes; the seedling in the drop carries it on
            }
        }

        if (drops.Count > 0)
        {
            SpillToGround(cell, drops); // after the clear, so the packet settles where the prop fell
        }

        if (HasFluidNeighbor(cell))
        {
            OnFluidRemoved(cell); // the pond beside the plant flows into its cell
        }

        OnSupportRemoved(cell); // whatever stood on it or hung from it follows (and loose sand above settles)
    }

    /// <summary>A harvested plant waiting to regrow in <paramref name="cell"/> has lost the ground it would root in: the
    /// timer goes (the server would never regrow it into the air) — and a bred plant's species record with it, as
    /// <see cref="StepFlora"/> forgets one whose soil is gone. A sapling's timer belongs to a standing sapling and is
    /// handled by its pop.</summary>
    private void CancelOrphanedRegrow(Vector3i cell, BlockId hostId, bool hanging)
    {
        var key = WorldConstants.CanonicalBlock(cell, _world.Circumference);
        if (!_floraRegrow.TryGetValue(key, out var regrow) || IsSapling(regrow.FloraId)
            || _floraHangingIds.Contains(regrow.FloraId) != hanging || FloraRootsIn(regrow.FloraId, hostId))
        {
            return;
        }

        ForgetFloraRegrow(key);
        if (IsBredPlant(regrow.FloraId))
        {
            ForgetBredPlant(key);
        }
    }

    /// <summary>Test seam (#2532): runs the support hook on a cell as an air-making path would (a direct
    /// <c>World.SetBlock</c> in a test stands in for a change that wakes nothing).</summary>
    public void SupportRemovedForTest(int x, int y, int z) => OnSupportRemoved(new Vector3i(x, y, z));
}
