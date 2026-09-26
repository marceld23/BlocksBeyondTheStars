// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.State;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The Crystal Net's machines (#2054 matter link, #2055 auto-drill, #2056 fabricator, #2057 caller + clone tank):
/// devices that DO work on a signal rather than only follow it. A rising edge (or a manual start at the block) is
/// one job; a held level keeps the machine working on its own beat. Every job is bounded — one stack, one craft,
/// one mined block — so a base full of machines costs a handful of container operations per beat.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Mined blocks so far in this tick across every drill of the world (reset by <c>TickCrystalNet</c>).</summary>
    private int _drillBlocksThisTick;

    /// <summary>One job on a machine: the rising edge of its network, or the owner pressing start at the block.</summary>
    private void TriggerCrystalMachine(ServerCrystalCell c, PlayerSession? by)
    {
        switch (c.Kind)
        {
            case CrystalDeviceKind.MatterSender:
                MatterSenderShot(c);
                break;
            case CrystalDeviceKind.Fabricator:
                FabricatorCraft(c);
                break;
            case CrystalDeviceKind.AutoDrill:
                AutoDrillStep(c);
                break;
            case CrystalDeviceKind.Caller:
                CallerPulse(c);
                break;
            case CrystalDeviceKind.CloneTank:
                CloneTankStart(c, by);
                break;
            case CrystalDeviceKind.Thumper:
                StartThumperAt(c.Cell, c.OwnerId);
                break;
            case CrystalDeviceKind.HydroTray:
                HarvestHydroTray(c);
                break;
        }
    }

    /// <summary>Machines held ON work on their own beat (2 s for the matter link and the fabricator, the tier's
    /// beat for a drill); a growing clone advances every logic beat.</summary>
    private void CrystalMachineBeat()
    {
        var state = CrystalNet;
        foreach (var c in state.Cells.Values)
        {
            if (c.Inert || c.IsConduit || c.IsGate)
            {
                continue;
            }

            bool held = c.NetId != 0 && state.Nets.TryGetValue(c.NetId, out var net) && net.Level;
            switch (c.Kind)
            {
                case CrystalDeviceKind.MatterSender when held && _uptime >= c.NextBeat:
                    c.NextBeat = _uptime + CrystalNetRules.MoveBeatSeconds;
                    MatterSenderShot(c);
                    break;
                case CrystalDeviceKind.Fabricator when held && _uptime >= c.NextBeat:
                    c.NextBeat = _uptime + CrystalNetRules.MoveBeatSeconds;
                    FabricatorCraft(c);
                    break;
                case CrystalDeviceKind.AutoDrill when held && _uptime >= c.NextBeat:
                    AutoDrillStep(c);
                    break;
                case CrystalDeviceKind.CloneTank:
                    CloneTankBeat(c, held);
                    break;
            }
        }
    }

    private void SetCrystalBlocked(ServerCrystalCell c, bool blocked)
    {
        if (c.Output != blocked)
        {
            c.Output = blocked;
            c.PulseUntil = 0;
            CrystalNet.DeviceListDirty = true;
        }
    }

    // ------------------------------------------------------------------------------------------------------
    // Matter link (#2054): a sender beams one stack from the crate beside it into the crate beside its paired
    // receiver — anywhere on this world, no conduit between them. Free per shot (Marcel's decision); the pace
    // is the cap.
    // ------------------------------------------------------------------------------------------------------

    private void MatterSenderShot(ServerCrystalCell sender)
    {
        int pairId = CrystalConfigInt(sender.Config, "pair", 0);
        var receiver = CrystalNet.Cells.Values.FirstOrDefault(d => d.Id == pairId && d.Kind == CrystalDeviceKind.MatterReceiver && !d.Inert);
        if (receiver is null || !CanConfigureCrystal(receiver, sender.OwnerId, false))
        {
            SetCrystalBlocked(sender, true);
            return;
        }

        var from = AdjacentCrystalCrate(sender.Cell, out _);
        var to = AdjacentCrystalCrate(receiver.Cell, out _);
        if (from is null || to is null || from.Id == to.Id)
        {
            SetCrystalBlocked(sender, true);
            return;
        }

        // The first stack the far crate's filter lets in, one shot's worth of it.
        ItemStack? stack = null;
        foreach (var s in from.Items)
        {
            if (!s.IsEmpty && s.Count > 0 && (to.Filter.Count == 0 || to.Filter.Contains(ItemKey.Base(s.Item))))
            {
                stack = s;
                break;
            }
        }

        if (stack is null)
        {
            SetCrystalBlocked(sender, true);
            return;
        }

        int count = Math.Min(stack.Count, CrystalNetRules.MoveStackSize);
        if (!NpcDepositToContainer(to, new[] { new ItemAmount(stack.Item, count) }))
        {
            SetCrystalBlocked(sender, true); // no room over there
            return;
        }

        stack.Count -= count;
        from.Items.RemoveAll(s => s.Count <= 0);
        _repo.SaveContainer(from);
        BroadcastContainers();
        SetCrystalBlocked(sender, false);
        PulseCrystalCell(receiver); // "something arrived" on the receiver's port

        var a = new Vector3f(sender.Cell.X + 0.5f, sender.Cell.Y + 1f, sender.Cell.Z + 0.5f);
        var b = new Vector3f(receiver.Cell.X + 0.5f, receiver.Cell.Y + 1f, receiver.Cell.Z + 0.5f);
        BroadcastToWorld(new BeamFx { FromX = a.X, FromY = a.Y, FromZ = a.Z, ToX = b.X, ToY = b.Y, ToZ = b.Z });
        BroadcastToWorld(new SoundFx { SoundId = "beam_teleport", X = a.X, Y = a.Y, Z = a.Z, SourceId = sender.Id });
        BroadcastToWorld(new SoundFx { SoundId = "beam_teleport", X = b.X, Y = b.Y, Z = b.Z, SourceId = receiver.Id });
    }

    /// <summary>Test seam: the matter receivers a sender's owner may pair with, as (device id, label).</summary>
    public IReadOnlyList<(int Id, string Label)> CrystalReceiversFor(string playerId)
        => CrystalNet.Cells.Values.Where(d => d.Kind == CrystalDeviceKind.MatterReceiver && !d.Inert && CanConfigureCrystal(d, playerId, false))
            .Select(d => (d.Id, d.Label)).ToList();

    // ------------------------------------------------------------------------------------------------------
    // Fabricator (#2056): one recipe per device, crafted from the crates beside it into the crates beside it.
    // ------------------------------------------------------------------------------------------------------

    private void FabricatorCraft(ServerCrystalCell fab)
    {
        string? key = CrystalConfigValue(fab.Config, "recipe");
        var recipe = key is null ? null : _content.GetRecipe(key);
        if (recipe is null || recipe.Station is not (CraftingStation.Workshop or CraftingStation.Hand) || recipe.MarketTheme.Length > 0)
        {
            SetCrystalBlocked(fab, true);
            return;
        }

        // Blueprint gating follows the owner, exactly like a hand craft; an owner who is away does not craft.
        var owner = FindSessionByPlayerId(fab.OwnerId);
        if (owner is null || (recipe.RequiredBlueprint is { Length: > 0 } bp && !owner.State.UnlockedBlueprints.Contains(bp)))
        {
            SetCrystalBlocked(fab, true);
            return;
        }

        var crates = new List<StoredContainer>();
        foreach (var face in CrystalNetRules.Faces)
        {
            if (ContainerAt(fab.Cell + face) is { } crate && !crates.Contains(crate))
            {
                crates.Add(crate);
            }
        }

        if (crates.Count == 0)
        {
            SetCrystalBlocked(fab, true);
            return;
        }

        // Inputs: every crate beside the fabricator counts; all or nothing.
        foreach (var need in recipe.Inputs)
        {
            int have = crates.Sum(c => c.Items.Where(s => s.Item == need.Item).Sum(s => s.Count));
            if (have < need.Count)
            {
                SetCrystalBlocked(fab, true);
                return;
            }
        }

        // Output: the first crate whose filter takes it and that has room.
        StoredContainer? outCrate = crates.FirstOrDefault(c => NpcDepositToContainer(c, recipe.Outputs));
        if (outCrate is null)
        {
            SetCrystalBlocked(fab, true);
            return;
        }

        foreach (var need in recipe.Inputs)
        {
            int left = need.Count;
            foreach (var crate in crates)
            {
                if (left <= 0)
                {
                    break;
                }

                foreach (var s in crate.Items)
                {
                    if (left <= 0)
                    {
                        break;
                    }

                    if (s.Item == need.Item)
                    {
                        int take = Math.Min(left, s.Count);
                        s.Count -= take;
                        left -= take;
                    }
                }

                crate.Items.RemoveAll(s => s.Count <= 0);
                _repo.SaveContainer(crate);
            }
        }

        BroadcastContainers();
        SetCrystalBlocked(fab, false);
        BroadcastToWorld(new SoundFx { SoundId = "fabricator_craft", X = fab.Cell.X + 0.5f, Y = fab.Cell.Y + 0.5f, Z = fab.Cell.Z + 0.5f, SourceId = fab.Id });
    }

    // ------------------------------------------------------------------------------------------------------
    // Auto-drill (#2055): a stationary quarry that mines the square below itself, layer by layer.
    // ------------------------------------------------------------------------------------------------------

    private void AutoDrillStep(ServerCrystalCell drill)
    {
        int tierIndex = CrystalNetRules.DrillTierOf(drill.BlockKey);
        if (tierIndex < 0)
        {
            return;
        }

        var tier = CrystalNetRules.DrillTiers[tierIndex];
        drill.NextBeat = _uptime + tier.Beat;
        if (_drillBlocksThisTick >= CrystalNetRules.MaxDrillBlocksPerTick)
        {
            drill.NextBeat = _uptime + CrystalNetRules.LogicBeatSeconds; // the world's budget for this tick is spent — try next beat
            return;
        }

        var crate = AdjacentCrystalCrate(drill.Cell, out _);
        if (crate is null)
        {
            SetCrystalBlocked(drill, true);
            return;
        }

        int side = tier.Radius * 2 + 1;
        int perLayer = side * side;
        int total = perLayer * tier.Depth;
        bool onlyOre = (AutoDrillMode)drill.Mode == AutoDrillMode.OnlyOre;
        var tool = new ToolProperties { Kind = ToolKind.Drill, Tier = tier.ToolTier };
        int scanned = 0;
        while (drill.Cursor < total && scanned < perLayer)
        {
            int i = drill.Cursor;
            int layer = i / perLayer;
            int rem = i % perLayer;
            var target = new Vector3i(drill.Cell.X + rem / side - tier.Radius, drill.Cell.Y - 1 - layer, drill.Cell.Z + rem % side - tier.Radius);
            drill.Cursor++;
            scanned++;

            var id = _world.GetBlockIfLoaded(target);
            var def = _content.BlockById(id);
            if (id.IsAir || def is null || IsFluid(id.Value) || !def.Mineable || !MiningRules.ToolCanMine(tool, def))
            {
                continue;
            }

            if (onlyOre && def.Category != "ore")
            {
                continue;
            }

            if (DrillCellProtected(target, id, drill.OwnerId))
            {
                continue;
            }

            // Never open a fluid: a cell with water or lava beside it stays.
            bool fluidBeside = false;
            foreach (var face in CrystalNetRules.Faces)
            {
                if (IsFluid(_world.GetBlockIfLoaded(target + face).Value))
                {
                    fluidBeside = true;
                    break;
                }
            }

            if (fluidBeside)
            {
                continue;
            }

            var drops = new List<ItemAmount>();
            if (!NpcCrateHasRoom(crate, def.Drops))
            {
                drill.Cursor--; // come back to this one once the crate has room
                SetCrystalBlocked(drill, true);
                return;
            }

            BreakBlockCore(null, drill.OwnerId, target, def, null, (item, count) => drops.Add(new ItemAmount(item, count)));
            _drillBlocksThisTick++;
            if (drops.Count > 0 && !NpcDepositToContainer(crate, drops))
            {
                // The dry run said yes; a composed key (a dyed block) can still refuse — the drop falls to the ground.
                SpillToGround(target, drops, creatureLoot: false);
            }

            BroadcastToWorld(new WorldFx { Kind = "thump", X = target.X + 0.5f, Y = target.Y + 0.5f, Z = target.Z + 0.5f, Strength = 0.2f });
            SetCrystalBlocked(drill, false);
            return;
        }

        if (drill.Cursor >= total)
        {
            SetCrystalBlocked(drill, true); // done: the volume is mined; the light stays on until the drill is re-placed
        }
    }

    /// <summary>The cells a drill may never touch: ships, settlements, stations, the Guardian core, factories, other
    /// players' bases, and anything a player placed (the owner's own floor included — a drill under a base must
    /// not eat the base).</summary>
    private bool DrillCellProtected(Vector3i p, BlockId id, string ownerId)
        => IsShipBlock(p) || IsSettlementProtected(p, id) || IsStationBlock(p) || IsGuardianCoreProtected(p)
           || IsFactoryProtected(p, ownerId, false) || IsBaseProtected(p, ownerId, false)
           || _repo.HasPlayerBlockEdits(_world.LocationId, p, p);

    /// <summary>A dry run of <see cref="NpcDepositToContainer"/>: would these items fit?</summary>
    private bool NpcCrateHasRoom(StoredContainer container, IReadOnlyList<ItemAmount> items)
    {
        if (items.Count == 0)
        {
            return true;
        }

        if (container.Filter.Count > 0 && items.Any(i => !container.Filter.Contains(ItemKey.Base(i.Item))))
        {
            return false;
        }

        bool woodBox = _world.GetBlock(container.Position).Value == (_content.GetBlock("wood_crate")?.NumericId.Value ?? 0);
        if (!woodBox)
        {
            return true;
        }

        var have = new HashSet<string>(container.Items.Where(s => !s.IsEmpty).Select(s => s.Item));
        int extra = items.Count(i => !have.Contains(i.Item));
        return have.Count + extra <= WoodCrateStackSlots;
    }


    /// <summary>Test seam: the next cell index of a drill's volume (how far it got).</summary>
    public int CrystalDrillCursor(Vector3i cell) => CrystalNet.Cells.TryGetValue(cell, out var c) ? c.Cursor : -1;

    // ------------------------------------------------------------------------------------------------------
    // Caller (#2057): a pulse calls the peaceful animals around and the owner's companions to the block.
    // ------------------------------------------------------------------------------------------------------

    private void CallerPulse(ServerCrystalCell caller)
    {
        if (_world.Planet.Void)
        {
            return;
        }

        var at = new Vector3f(caller.Cell.X + 0.5f, caller.Cell.Y + 1f, caller.Cell.Z + 0.5f);
        CrystalNet.ActiveCallers[caller.Cell] = _uptime + CrystalNetRules.CallerHoldSeconds;
        double r2 = CrystalNetRules.CallerRange * CrystalNetRules.CallerRange;
        foreach (var c in _creatures)
        {
            if (c.IsCompanion && c.OwnerId == caller.OwnerId && WrapDistSq(c.Position, at) <= r2 && _speciesById.TryGetValue(c.SpeciesId, out var sp))
            {
                c.Position = CompanionSpotNear(sp, c.Id, at); // the leash snap — a pet "comes when called"
            }
        }

        BroadcastToWorld(new SoundFx { SoundId = "caller_whistle", X = at.X, Y = at.Y, Z = at.Z, SourceId = caller.Id });
        CrystalNet.CreatureListDirtyHint = true;
    }

    /// <summary>The nearest active caller within range, for the creature tick (#2057).</summary>
    private Vector3f? NearestActiveCaller(Vector3f from, float range)
    {
        var callers = CrystalNet.ActiveCallers;
        if (callers.Count == 0)
        {
            return null;
        }

        Vector3f? best = null;
        double bestSq = range * range;
        foreach (var kv in callers)
        {
            if (_uptime >= kv.Value)
            {
                continue;
            }

            var at = new Vector3f(kv.Key.X + 0.5f, kv.Key.Y + 1f, kv.Key.Z + 0.5f);
            double d = WrapDistSq(from, at);
            if (d <= bestSq)
            {
                bestSq = d;
                best = at;
            }
        }

        return best;
    }

    // ------------------------------------------------------------------------------------------------------
    // Clone tank (#2057): grows a WILD animal of a species the owner has scanned or tamed on this world.
    // ------------------------------------------------------------------------------------------------------

    /// <summary>The species this world's roster offers a tank owner: scanned or tamed here, never hostile (kid rule).</summary>
    public IReadOnlyList<(string SpeciesId, string Name)> CloneableSpeciesFor(string playerId)
    {
        var session = FindSessionByPlayerId(playerId);
        if (session is null)
        {
            return Array.Empty<(string, string)>();
        }

        var p = session.State;
        var result = new List<(string, string)>();
        foreach (var sp in _speciesRoster)
        {
            if (sp.Hostile)
            {
                continue;
            }

            if (p.Scanned.Contains("creature:" + sp.Id) || p.TamedSpecies.Contains(_world.LocationId + ":" + sp.Id))
            {
                result.Add((sp.Id, sp.Name));
            }
        }

        return result;
    }

    private void CloneTankStart(ServerCrystalCell tank, PlayerSession? by)
    {
        if (_world.Planet.Void || CrystalConfigValue(tank.Config, "growing") == "1")
        {
            return;
        }

        string? spId = CrystalConfigValue(tank.Config, "sp");
        if (spId is null || !_speciesById.TryGetValue(spId, out var sp) || sp.Hostile)
        {
            SetCrystalBlocked(tank, false);
            return;
        }

        if (!CloneableSpeciesFor(tank.OwnerId).Any(s => s.SpeciesId == spId))
        {
            return; // not scanned / tamed by the owner (or the owner is away)
        }

        if (LivingClonesOf(tank.OwnerId) >= CrystalNetRules.MaxLivingClonesPerOwner)
        {
            if (by is not null)
            {
                Reject(by, "crystal", "@srv.crystal.clone_cap");
            }

            return;
        }

        // The price: one bait of the species' preference and two matter dust, from the crate beside the tank or
        // from the pocket of whoever pressed start.
        string bait = PreferredBait(spId);
        var price = new[] { new ItemAmount(bait, 1), new ItemAmount("matter_dust", 2) };
        if (!TakeFromCrateOrPocket(tank, by, price))
        {
            if (by is not null)
            {
                Reject(by, "crystal", "@srv.crystal.clone_price:" + bait);
            }

            return;
        }

        tank.Config = CrystalConfigWith(tank.Config, "growing", "1");
        tank.NextBeat = _uptime + CrystalNetRules.CloneGrowSeconds;
        SaveCrystalCell(tank);
        SetCrystalBlocked(tank, true); // ON while growing
        BroadcastToWorld(new SoundFx { SoundId = "clone_tank_bubble", X = tank.Cell.X + 0.5f, Y = tank.Cell.Y + 1f, Z = tank.Cell.Z + 0.5f, Loop = true, SourceId = tank.Id });
    }

    private void CloneTankBeat(ServerCrystalCell tank, bool held)
    {
        if (CrystalConfigValue(tank.Config, "growing") != "1" || _uptime < tank.NextBeat)
        {
            return;
        }

        if (tank.Mode == 1 && !held)
        {
            return; // "release on signal": wait for the level
        }

        string? spId = CrystalConfigValue(tank.Config, "sp");
        if (spId is not null && _speciesById.TryGetValue(spId, out var sp) && !sp.Hostile)
        {
            ReleaseClone(tank, sp);
        }

        tank.Config = CrystalConfigWith(tank.Config, "growing", "0");
        SaveCrystalCell(tank);
        SetCrystalBlocked(tank, false);
        PulseCrystalCell(tank); // "clone ready" — one pulse for a chime or a door
        BroadcastToWorld(new SoundFx { SoundId = "clone_tank_bubble", X = tank.Cell.X + 0.5f, Y = tank.Cell.Y + 1f, Z = tank.Cell.Z + 0.5f, Stop = true, SourceId = tank.Id });
    }

    private void ReleaseClone(ServerCrystalCell tank, CreatureSpecies sp)
    {
        var at = new Vector3f(tank.Cell.X + 0.5f, tank.Cell.Y + 1f, tank.Cell.Z + 0.5f);
        string id = NextEntityId();
        var spot = CompanionSpotNear(sp, id, at);
        SpawnCreature(sp, spot);
        var clone = _creatures[^1];
        clone.CloneOf = CloneTankKey(tank.Cell);
        int count = CrystalConfigInt(tank.Config, "clones", 0) + 1;
        tank.Config = CrystalConfigWith(tank.Config, "clones", count.ToString(System.Globalization.CultureInfo.InvariantCulture));
        CrystalNet.CreatureListDirtyHint = true;
    }

    private static string CloneTankKey(Vector3i cell) => cell.X + "," + cell.Y + "," + cell.Z;

    private int LivingClonesOf(string ownerId)
    {
        var tanks = new HashSet<string>(CrystalNet.Cells.Values.Where(c => c.Kind == CrystalDeviceKind.CloneTank && c.OwnerId == ownerId).Select(c => CloneTankKey(c.Cell)));
        return _creatures.Count(c => c.CloneOf.Length > 0 && tanks.Contains(c.CloneOf));
    }

    /// <summary>On world activation the tanks' clones come back as wild animals beside their tank (#2057): they are
    /// counted in the tank's config, never persisted as entities. Called after <c>LoadCrystalNet</c> once the roster exists.</summary>
    private void RespawnCrystalClones()
    {
        if (_world.Planet.Void)
        {
            return;
        }

        foreach (var tank in CrystalNet.Cells.Values)
        {
            if (tank.Kind != CrystalDeviceKind.CloneTank)
            {
                continue;
            }

            string? spId = CrystalConfigValue(tank.Config, "sp");
            int count = Math.Min(CrystalNetRules.MaxLivingClonesPerOwner, CrystalConfigInt(tank.Config, "clones", 0));
            if (spId is null || count <= 0 || !_speciesById.TryGetValue(spId, out var sp) || sp.Hostile)
            {
                continue;
            }

            string key = CloneTankKey(tank.Cell);
            int alive = _creatures.Count(c => c.CloneOf == key);
            for (int i = alive; i < count; i++)
            {
                var at = new Vector3f(tank.Cell.X + 0.5f, tank.Cell.Y + 1f, tank.Cell.Z + 0.5f);
                SpawnCreature(sp, CompanionSpotNear(sp, NextEntityId(), at));
                _creatures[^1].CloneOf = key;
            }
        }
    }

    /// <summary>A mined tank releases its clones into the normal wild population (they lose their tag).</summary>
    private void ReleaseClonesOfTank(Vector3i cell)
    {
        string key = CloneTankKey(cell);
        foreach (var c in _creatures)
        {
            if (c.CloneOf == key)
            {
                c.CloneOf = string.Empty;
            }
        }
    }

    /// <summary>Takes a price from the crate beside a device, else from the pocket of the player who pressed start.
    /// All or nothing.</summary>
    private bool TakeFromCrateOrPocket(ServerCrystalCell device, PlayerSession? by, IReadOnlyList<ItemAmount> price)
    {
        var crate = AdjacentCrystalCrate(device.Cell, out _);
        if (crate is not null && price.All(p => crate.Items.Where(s => s.Item == p.Item).Sum(s => s.Count) >= p.Count))
        {
            foreach (var p in price)
            {
                int left = p.Count;
                foreach (var s in crate.Items)
                {
                    if (left <= 0)
                    {
                        break;
                    }

                    if (s.Item == p.Item)
                    {
                        int take = Math.Min(left, s.Count);
                        s.Count -= take;
                        left -= take;
                    }
                }
            }

            crate.Items.RemoveAll(s => s.Count <= 0);
            _repo.SaveContainer(crate);
            BroadcastContainers();
            return true;
        }

        if (by is not null && price.All(p => by.State.Inventory.Has(p.Item, p.Count)))
        {
            foreach (var p in price)
            {
                by.State.Inventory.Remove(p.Item, p.Count);
            }

            SendInventory(by);
            return true;
        }

        return false;
    }
}
