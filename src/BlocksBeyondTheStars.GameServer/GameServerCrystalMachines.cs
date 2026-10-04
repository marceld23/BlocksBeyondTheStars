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
            case CrystalDeviceKind.DrillLaser:
                DrillLaserStep(c);
                break;
            case CrystalDeviceKind.RailStop:
                DepartTrainsAtStop(c.Cell); // #2113: the signal departs the train halted here
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
            case CrystalDeviceKind.FlowerPot: // #2261: a pot harvests like a tray
                HarvestHydroTray(c);
                break;
            case CrystalDeviceKind.LiftStop:
            case CrystalDeviceKind.LiftMotor:
                LiftSignal(c); // #2266
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
                case CrystalDeviceKind.DrillLaser when held && _uptime >= c.NextBeat:
                    DrillLaserStep(c);
                    break;
                case CrystalDeviceKind.CloneTank:
                    CloneTankBeat(c, held);
                    break;
                case CrystalDeviceKind.BridgeMotor:
                    BridgeMotorBeat(c, held); // #2265: one deck block per step, out while ON, back in while OFF
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
        // #2252: the partner is a CELL — a device id is handed out afresh on every load.
        ServerCrystalCell? receiver = CrystalPairCell(sender.Config) is { } at && CrystalNet.Cells.TryGetValue(at, out var r)
            && r.Kind == CrystalDeviceKind.MatterReceiver && !r.Inert ? r : null;
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

    /// <summary>Test seam: the matter receivers a sender's owner may pair with, as (cell, label) — #2252: a pair names the
    /// partner's cell.</summary>
    public IReadOnlyList<(Vector3i Cell, string Label)> CrystalReceiversFor(string playerId)
        => CrystalNet.Cells.Values.Where(d => d.Kind == CrystalDeviceKind.MatterReceiver && !d.Inert && CanConfigureCrystal(d, playerId, false))
            .Select(d => (d.Cell, d.Label)).ToList();

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

    // ------------------------------------------------------------------------------------------------------
    // Drill laser (#2108): a 1×1 shaft straight down from the device's own column, one block per beat.
    // ------------------------------------------------------------------------------------------------------

    /// <summary>One beat of a drill laser. Unlike the auto-drill it LOADS the chunk it aims at (a shaft goes far below
    /// the rows streamed around a player, and an unloaded cell must never read as air and be skipped); it passes air
    /// (a cave) without a beat; it banks ore and oil — in "only ore" mode the rock is vaporised, in "everything" mode
    /// it goes into the crate too; and it stops for good at water or lava (in the shaft or beside it), at bedrock or
    /// anything its tier cannot cut, at a protected cell, or at its maximum depth. The depth reached lives in the
    /// cell's config (<c>depth=</c>), so a reload resumes where it stopped.</summary>
    private void DrillLaserStep(ServerCrystalCell laser)
    {
        laser.NextBeat = _uptime + CrystalNetRules.DrillLaserBeat;
        if (_drillBlocksThisTick >= CrystalNetRules.MaxDrillBlocksPerTick)
        {
            laser.NextBeat = _uptime + CrystalNetRules.LogicBeatSeconds; // the world's budget for this tick is spent — try next beat
            return;
        }

        var crate = AdjacentCrystalCrate(laser.Cell, out _);
        if (crate is null)
        {
            SetCrystalBlocked(laser, true);
            return;
        }

        int depth = int.TryParse(CrystalConfigValue(laser.Config, "depth"), out int saved) ? System.Math.Max(0, saved) : 0;
        bool onlyOre = (AutoDrillMode)laser.Mode == AutoDrillMode.OnlyOre;
        var tool = new ToolProperties { Kind = ToolKind.Drill, Tier = CrystalNetRules.DrillLaserToolTier };
        int scanned = 0;
        while (depth < CrystalNetRules.DrillLaserDepth && scanned < 16)
        {
            var target = new Vector3i(laser.Cell.X, laser.Cell.Y - 1 - depth, laser.Cell.Z);
            scanned++;
            var id = _world.GetBlock(target); // loads / generates on purpose (#2108) — never "air because unloaded"
            if (id.IsAir)
            {
                depth++; // a cave: the beam passes through
                continue;
            }

            var def = _content.BlockById(id);
            if (def is null || IsFluid(id.Value) || (!def.Mineable && !def.Liquid) || (!def.Liquid && !MiningRules.ToolCanMine(tool, def))
                || DrillCellProtected(target, id, laser.OwnerId))
            {
                DrillLaserStop(laser, depth);
                return;
            }

            // Never open a fluid into the shaft: a cell with water or lava beside it ends the dig.
            foreach (var face in CrystalNetRules.Faces)
            {
                if (IsFluid(_world.GetBlock(target + face).Value))
                {
                    DrillLaserStop(laser, depth);
                    return;
                }
            }

            bool bank = !onlyOre || def.Category == "ore" || def.Liquid;
            var drops = new List<ItemAmount>();
            if (bank && !NpcCrateHasRoom(crate, def.Drops))
            {
                DrillLaserStop(laser, depth); // the crate is full: it resumes from the same cell once emptied and started again
                return;
            }

            BreakBlockCore(null, laser.OwnerId, target, def, null, (item, count) =>
            {
                if (bank)
                {
                    drops.Add(new ItemAmount(item, count));
                }
            });
            _drillBlocksThisTick++;
            depth++;
            laser.Config = CrystalConfigWith(laser.Config, "depth", depth.ToString(System.Globalization.CultureInfo.InvariantCulture));
            SaveCrystalCell(laser);
            if (drops.Count > 0 && !NpcDepositToContainer(crate, drops))
            {
                SpillToGround(target, drops, creatureLoot: false); // the dry run said yes; a composed key can still refuse
            }

            // The beam: from the device straight down to the cell it just cut (Radius carries the length for the client).
            BroadcastToWorld(new WorldFx { Kind = "laser", X = target.X + 0.5f, Y = target.Y + 0.5f, Z = target.Z + 0.5f, Strength = 0.3f, Radius = depth });
            SetCrystalBlocked(laser, false);
            return;
        }

        if (depth >= CrystalNetRules.DrillLaserDepth)
        {
            DrillLaserStop(laser, depth); // done: the shaft is as deep as it goes
        }
    }

    /// <summary>The laser halts: the depth reached is kept, the status light turns amber (a Device Eye reads it).</summary>
    private void DrillLaserStop(ServerCrystalCell laser, int depth)
    {
        string kept = depth.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (CrystalConfigValue(laser.Config, "depth") != kept)
        {
            laser.Config = CrystalConfigWith(laser.Config, "depth", kept);
            SaveCrystalCell(laser);
        }

        SetCrystalBlocked(laser, true);
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

            // #2097: scanned ON THIS WORLD (species ids repeat across planets, the scan ledger does not know where) — the
            // per-body record, or for a scan made before it existed the first-scan site — or tamed here.
            string here = _world.LocationId + ":" + sp.Id;
            bool scannedHere = p.ScannedCreatureSites.Contains(here)
                || (p.ScannedWhere.TryGetValue("creature:" + sp.Id, out var site) && site is not null && site.BodyId == _world.LocationId);
            if (scannedHere || p.TamedSpecies.Contains(here))
            {
                result.Add((sp.Id, sp.Name));
            }
        }

        AppendSampleChoices(p, result); // #2207: and whatever the owner holds a sample of — species of other worlds too
        return result;
    }

    private void CloneTankStart(ServerCrystalCell tank, PlayerSession? by)
    {
        if (_world.Planet.Void)
        {
            return;
        }

        if (tank.Inert)
        {
            // #2214: a tank over the cap exists and does nothing — it takes no price and starts no job. Whoever
            // presses start hears the line the tank got when it was placed.
            if (by is not null)
            {
                SendVegaLine(by, "vega.sys.crystal_cap", 3);
            }

            return;
        }

        if (CrystalConfigValue(tank.Config, "growing") == "1")
        {
            return;
        }

        string? spId = CrystalConfigValue(tank.Config, "sp");

        // #2207/#2208: a sample as the species, or a second species as the partner of a cross, is the samples' path.
        string? partner = CrystalConfigValue(tank.Config, TankPartnerKey);
        if (spId is not null && (spId.StartsWith(TankSamplePrefix, StringComparison.Ordinal) || !string.IsNullOrEmpty(partner)))
        {
            BioTankStart(tank, by, spId, partner);
            return;
        }

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

        // #2207: and a cap for the whole world, whoever owns the tanks — clones are never pruned while their tank stands.
        if (LivingClonesInWorld() >= CrystalNetRules.MaxLivingClonesPerWorld)
        {
            if (by is not null)
            {
                Reject(by, "crystal", "@srv.crystal.clone_world_cap");
            }

            return;
        }

        // The price: one bait of the species' preference and two matter dust, from the crate beside the tank or
        // from the pocket of whoever pressed start. #2214: nothing at all in a free game mode — the owner's mode
        // counts, exactly as on the samples' path.
        var owner = FindSessionByPlayerId(tank.OwnerId);
        if (owner is null || Rules.CraftingCostsMaterialsFor(owner.State.ModeOverride))
        {
            string bait = PreferredBait(spId);
            var price = new[] { new ItemAmount(bait, 1), new ItemAmount("matter_dust", 2) };
            if (!TakeFromCrateOrPocket(tank, by, price))
            {
                if (by is not null)
                {
                    Reject(by, "crystal", "@srv.crystal.clone_price." + bait); // #2096: one line per bait, no raw item key
                }

                return;
            }
        }

        // #2214: the tank remembers what it was started on, as it does on the samples' path. The species setting stays
        // the player's to change while the tank grows — the animal that was paid for comes out all the same.
        tank.Config = CrystalConfigWith(tank.Config, TankGrowKey, spId);
        StartCloneGrowth(tank);
    }

    /// <summary>The tank starts growing: the wait, the light, the bubbling. #2214: the device list goes out with the
    /// new state, so a menu that is open on the tank can follow — marked outright here, not only through the light's
    /// change (which marked it before, too).</summary>
    private void StartCloneGrowth(ServerCrystalCell tank)
    {
        tank.Config = CrystalConfigWith(tank.Config, "growing", "1");
        tank.NextBeat = _uptime + CrystalNetRules.CloneGrowSeconds;
        tank.WaitTold = false;
        SaveCrystalCell(tank);
        SetCrystalBlocked(tank, true); // ON while growing

        // #2214: a start inside the "clone ready" pulse of the job before finds the light already on, so the line
        // above changes nothing — and the pulse, still armed, would switch the light off half a second later, for
        // the whole new job. The light is the job's now: the pulse of the last one is over.
        tank.PulseUntil = 0;
        CrystalNet.DeviceListDirty = true;
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

        string? grow = CrystalConfigValue(tank.Config, TankGrowKey);
        if (!string.IsNullOrEmpty(grow) && grow!.StartsWith(TankSamplePrefix, StringComparison.Ordinal))
        {
            // #2207/#2208: started on a sample — a guest clone, or a cross and a sample of the new species.
            if (!BioTankFinish(tank, grow!))
            {
                // The owner is away, or the owner's sample case has no room (#2214): what the tank made waits in it.
                tank.NextBeat = _uptime + CrystalNetRules.CloneHandOverRetrySeconds;
                return;
            }
        }
        else
        {
            // A native species: the one the tank was STARTED on (#2214), not what its setting names by now — a player
            // who picks another species while it grows must neither swap the animal nor lose the job. A job that an
            // older build started carries no "grow" and reads the setting, as it always did.
            bool started = !string.IsNullOrEmpty(grow);
            string? spId = started ? grow : CrystalConfigValue(tank.Config, "sp");
            if (spId is not null && _speciesById.TryGetValue(spId, out var sp) && !sp.Hostile)
            {
                ReleaseClone(tank, sp);
            }

            if (started)
            {
                tank.Config = CrystalConfigWith(tank.Config, TankGrowKey, string.Empty);
            }
        }

        tank.Config = CrystalConfigWith(tank.Config, "growing", "0");
        tank.WaitTold = false;
        SaveCrystalCell(tank);
        SetCrystalBlocked(tank, false);
        PulseCrystalCell(tank); // "clone ready" — one pulse for a chime or a door
        CrystalNet.DeviceListDirty = true; // #2214: what the tank holds and clones now goes out — an open menu can follow
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
        RefreshTankClones(tank); // #2214: the tank remembers every living clone with its species; the caller saves the row
        CrystalNet.CreatureListDirtyHint = true;
    }

    private static string CloneTankKey(Vector3i cell) => cell.X + "," + cell.Y + "," + cell.Z;

    /// <summary>The clones an owner's tanks hold on this world: the animals that live, and (#2214) the clones a tank's
    /// list names that are not beside it yet — they come back on the world's first beat, so they count against the cap.</summary>
    private int LivingClonesOf(string ownerId)
    {
        var tanks = new HashSet<string>();
        int waiting = 0;
        foreach (var c in CrystalNet.Cells.Values)
        {
            if (c.Kind == CrystalDeviceKind.CloneTank && c.OwnerId == ownerId)
            {
                tanks.Add(CloneTankKey(c.Cell));
                waiting += c.CloneWaiting?.Count ?? 0;
            }
        }

        return waiting + _creatures.Count(c => c.CloneOf.Length > 0 && tanks.Contains(c.CloneOf));
    }

    // ---- The tank's clone list (#2214) ----
    // A clone is never persisted as an entity: its tank's config lists it. One entry per clone, each with its own
    // species, so a tank that grew a wolf, then a cross, then a plant brings back exactly the animals that were
    // alive — whatever its species setting names by then. An entry leaves the list when its animal is gone (defeated,
    // tamed, burnt) or can never be an animal again. Between the load of a tank's row and the first beat of its world
    // the listed clones WAIT (ServerCrystalCell.CloneWaiting): they are not beside the tank yet, and they are its
    // clones all the same — they count against the caps and stay in the row. Nothing waits longer than that (#2226:
    // every world reads its own species table, so a native id can always be named on its own world).

    /// <summary>The config key of a tank's clone list: the species of every clone, separated by commas — a native
    /// species id, or <c>g:&lt;seed&gt;</c> for a guest from a sample. The living ones come first, in the order they
    /// were released; the ones that wait follow.</summary>
    private const string TankClonesKey = "cl";

    /// <summary>The keys of a tank's config that are the server's to write: what it grows and which clones it holds.
    /// A client <c>configure</c> can neither set nor drop them.</summary>
    private static readonly string[] TankOwnedKeys = { "growing", "clones", TankGrowKey, TankClonesKey };

    /// <summary>The clones a tank's config lists, one entry per clone. A row written before the list existed carries
    /// a counter and one species (<c>clones=N;sp=X</c>) — that reads as N clones of X.</summary>
    private static List<string> TankClones(string config)
    {
        var result = new List<string>();
        if (CrystalConfigValue(config, TankClonesKey) is { } list)
        {
            foreach (string token in list.Split(','))
            {
                if (token.Length > 0)
                {
                    result.Add(token);
                }
            }

            return result;
        }

        string? sp = CrystalConfigValue(config, "sp");
        int count = Math.Min(CrystalNetRules.MaxLivingClonesPerOwner, CrystalConfigInt(config, "clones", 0));
        for (int i = 0; i < count && !string.IsNullOrEmpty(sp); i++)
        {
            result.Add(sp!);
        }

        return result;
    }

    /// <summary>How a clone's species stands in its tank's list: a guest by its sample choice, a native by its id.</summary>
    private static string CloneToken(string speciesId)
        => GuestSeed(speciesId) is { } seed ? TankChoice(seed) : speciesId;

    /// <summary>A tank as it is registered — placed, or rebuilt from its row. An older row gets its list (see
    /// <see cref="TankClones"/>); the clones a row lists all wait until <see cref="RespawnCrystalClones"/> brings them
    /// back. A tank that was growing when its world was left starts its wait over: the deadline is runtime state, and
    /// "growing" alone would release at once on the first beat after the return.</summary>
    private void InitCloneTank(ServerCrystalCell tank)
    {
        tank.CloneTag = CloneTankKey(tank.Cell);
        var clones = TankClones(tank.Config);
        tank.CloneCount = 0;
        tank.CloneWaiting = clones.Count > 0 ? clones : null;
        if (clones.Count > 0 && CrystalConfigValue(tank.Config, TankClonesKey) is null)
        {
            tank.Config = CrystalConfigWith(
                CrystalConfigWith(tank.Config, TankClonesKey, string.Join(",", clones)),
                "clones", clones.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (!tank.Inert && CrystalConfigValue(tank.Config, "growing") == "1")
        {
            tank.NextBeat = _uptime + CrystalNetRules.CloneGrowSeconds;
            tank.Output = true; // ON while growing, as when it was started
        }
    }

    /// <summary>Brings a tank's list in line with its clones: those that live, then those that wait (see
    /// <see cref="ServerCrystalCell.CloneWaiting"/>). Returns whether the config changed (the caller saves the row).</summary>
    private bool RefreshTankClones(ServerCrystalCell tank)
    {
        var listed = new List<string>();
        foreach (var c in _creatures)
        {
            if (c.CloneOf.Length > 0 && c.CloneOf == tank.CloneTag)
            {
                listed.Add(CloneToken(c.SpeciesId));
            }
        }

        tank.CloneCount = listed.Count;
        if (tank.CloneWaiting is { } waiting)
        {
            listed.AddRange(waiting); // not beside the tank yet, but still this tank's clones
        }

        string list = string.Join(",", listed);
        if ((CrystalConfigValue(tank.Config, TankClonesKey) ?? string.Empty) == list)
        {
            return false;
        }

        tank.Config = CrystalConfigWith(
            CrystalConfigWith(tank.Config, TankClonesKey, list),
            "clones", listed.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)); // the counter older builds read
        return true;
    }

    private void SyncTankClones(ServerCrystalCell tank)
    {
        if (RefreshTankClones(tank))
        {
            SaveCrystalCell(tank);
        }
    }

    /// <summary>A clone left the world for good — it was defeated, burnt, brought down by a sentry post, or tamed and
    /// is a companion now: its tank forgets
    /// it, so it does not stand beside the tank again after the next reload. Called once the animal has left the
    /// creature list; a no-op for anything that is no clone.</summary>
    private void ForgetClone(CombatEntity creature)
    {
        if (creature.CloneOf.Length == 0)
        {
            return;
        }

        foreach (var tank in CrystalNet.Cells.Values)
        {
            if (tank.Kind == CrystalDeviceKind.CloneTank && tank.CloneTag == creature.CloneOf)
            {
                SyncTankClones(tank);
                break;
            }
        }
    }

    /// <summary>The sensor beat's look at one clone tank — two comparisons, nothing allocated while nothing changed:
    /// <list type="bullet">
    /// <item><b>What its owner may pick.</b> The choices ride in the device list, and that only goes out when the net
    /// is marked dirty. A sample gained or spent (a harvest, the sampler, the lab, this tank) or a new scan marks
    /// nothing — so the tank compares a signature of its owner's choices with the one it last saw.</item>
    /// <item><b>Which of its clones live.</b> A clone lost in a way that passes <see cref="ForgetClone"/> by (a pen
    /// built around a sleeper, any removal without its own hook) leaves the list all the same. The clones that wait
    /// are no part of this count: they have not been beside the tank in this residency yet, so nothing here can have
    /// lost them.</item>
    /// </list></summary>
    private void WatchCloneTank(ServerCrystalCell tank)
    {
        int stamp = CloneChoiceStamp(tank.OwnerId);
        if (stamp != tank.ChoiceStamp)
        {
            tank.ChoiceStamp = stamp;
            CrystalNet.DeviceListDirty = true;
        }

        int living = 0;
        foreach (var c in _creatures)
        {
            if (c.CloneOf.Length > 0 && c.CloneOf == tank.CloneTag)
            {
                living++;
            }
        }

        if (living != tank.CloneCount)
        {
            SyncTankClones(tank);
        }
    }

    /// <summary>On world activation the tanks' clones come back as wild animals beside their tank (#2057): they are
    /// listed in the tank's config, never persisted as entities. #2214: each comes back as the species it was grown
    /// as. #2226: the species table is this world's own, so every entry either names an animal or never will again —
    /// none waits past this call. Called after <c>LoadCrystalNet</c> once the roster exists.</summary>
    private void RespawnCrystalClones()
    {
        if (_world.Planet.Void)
        {
            return;
        }

        foreach (var tank in CrystalNet.Cells.Values)
        {
            if (tank.Kind != CrystalDeviceKind.CloneTank || tank.CloneWaiting is not { Count: > 0 } listed)
            {
                continue;
            }

            string key = CloneTankKey(tank.Cell);
            var alive = _creatures.Where(c => c.CloneOf == key).Select(c => CloneToken(c.SpeciesId)).ToList();
            var at = new Vector3f(tank.Cell.X + 0.5f, tank.Cell.Y + 1f, tank.Cell.Z + 0.5f);
            int kept = 0;
            foreach (string token in listed)
            {
                if (kept >= CrystalNetRules.MaxLivingClonesPerOwner)
                {
                    break;
                }

                if (alive.Remove(token))
                {
                    kept++; // already beside its tank
                    continue;
                }

                if (ReturningClone(token) is { } sp)
                {
                    SpawnCreature(sp, CompanionSpotNear(sp, NextEntityId(), at));
                    _creatures[^1].CloneOf = key;
                    kept++;
                }
            }

            tank.CloneWaiting = null; // every listed clone is beside its tank now, or has left the list
            SyncTankClones(tank);
        }
    }

    /// <summary>The animal a listed clone comes back as, or null for an entry that can never be an animal again: the
    /// register lost its species, it names a plant (an older row whose species setting was a plant), it is hostile, or
    /// this world's roster does not know the id. Such an entry leaves the list. A native id is read from this world's
    /// own table (#2226), so it never names another world's animal.</summary>
    private CreatureSpecies? ReturningClone(string token)
    {
        CreatureSpecies? sp;
        if (token.StartsWith(TankSamplePrefix, StringComparison.Ordinal))
        {
            sp = GuestSpeciesOf(TankEntry(token)); // a guest lives in the save's register, whatever world this is
        }
        else
        {
            _speciesById.TryGetValue(token, out sp);
        }

        return sp is null || sp.Hostile ? null : sp;
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
