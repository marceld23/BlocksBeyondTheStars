// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Machines catch up on return (#2269). The Crystal Net only runs on a world somebody is on; a world nobody was on did not
/// tick. When it runs again, the machines that were RUNNING when it stopped — their network ON, or ON within the last
/// half minute (a clock) — are credited the gap, bounded by the world rule <c>MachineCatchUpMinutes</c> (default 60) and
/// per machine (<see cref="CrystalNetRules.CatchUpMaxDrillBlocks"/> …): the drill digs, the laser cuts, the fabricator
/// crafts, the sender sends, a tray or pot yields its harvests, a growing clone is ready. VEGA then tells each owner on the
/// world what happened while they were away.
/// <para>When: each world remembers when its net last ran (<c>WorldMetadata.CrystalLastTicked</c>, wall clock, updated
/// every tick — so a home world that kept running with nobody online is never credited twice). A gap of at least
/// <see cref="CrystalNetRules.CatchUpMinGapSeconds"/> starts a catch-up; a negative or absurd gap (a changed clock, a
/// restored backup) is clamped to nothing / the cap.</para>
/// <para>Cost: nothing while away. On return the jobs run spread over the next ticks, at most
/// <see cref="CrystalNetRules.CatchUpEditsPerTick"/> machine steps per tick, the containers broadcast once per tick; a drill
/// loads the chunks it digs (a live drill reads the loaded rows only).</para>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>A machine's catch-up: how many steps it may still take and how many did a job.</summary>
    internal sealed class CrystalCatchUpJob
    {
        public ServerCrystalCell Cell = null!;
        public int Steps;
        public int Done;
    }

    /// <summary>True while catch-up steps run: big crates fill only so far, containers broadcast once per tick.</summary>
    private bool _crystalCatchingUp;
    private bool _crystalCatchUpContainersDirty;

    /// <summary>Seconds of wall clock a test has added (the catch-up's gap is measured on the real clock otherwise).</summary>
    private double _crystalClockShiftForTest;

    /// <summary>Seconds a running tray or pot needs to ripen again (the crop's regrow) — one harvest per this while away.</summary>
    private const double CatchUpHarvestSeconds = 30.0;

    /// <summary>A running machine's network is taken as "still running" this long after it was last ON (a clock between ticks).</summary>
    private const double CrystalRanWindowSeconds = 30.0;

    /// <summary>The "running" flag goes to the row at most this often per machine (a clock must not write ten rows a second).</summary>
    private const double CrystalRanSaveSeconds = 10.0;

    private double CrystalWallClock => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0 + _crystalClockShiftForTest;

    /// <summary>Test seam: moves this server's wall clock forward (a world left alone for so long).</summary>
    public void ShiftCrystalClockForTest(double seconds) => _crystalClockShiftForTest += seconds;

    private static bool IsCatchUpMachine(CrystalDeviceKind kind) => kind is CrystalDeviceKind.AutoDrill or CrystalDeviceKind.DrillLaser
        or CrystalDeviceKind.Fabricator or CrystalDeviceKind.MatterSender or CrystalDeviceKind.HydroTray or CrystalDeviceKind.FlowerPot
        or CrystalDeviceKind.CloneTank;

    /// <summary>Every tick, before the beats: the wall clock against this world's last run. Runs on a world without cells too —
    /// a stale stamp must never credit a base built later.</summary>
    private void CrystalCatchUpClock()
    {
        var state = CrystalNet;
        double now = CrystalWallClock;
        string loc = _world.LocationId;
        if (state.LastTickedUnix <= 0)
        {
            state.LastTickedUnix = _meta.CrystalLastTicked.TryGetValue(loc, out double saved) && saved > 0 ? saved : now;
        }

        double gap = now - state.LastTickedUnix;
        state.LastTickedUnix = now;
        _meta.CrystalLastTicked[loc] = now;
        if (gap >= CrystalNetRules.CatchUpMinGapSeconds && Rules.MachineCatchUpMinutes > 0 && state.Cells.Count > 0)
        {
            StartCrystalCatchUp(Math.Min(gap, Rules.MachineCatchUpMinutes * 60.0));
        }

        state.LastTickUptime = _uptime;
    }

    /// <summary>Keeps a machine's "running" flag: ON now, or ON within the last half minute. Rate-limited to the row.</summary>
    private void TrackCrystalRunning(ServerCrystalCell c, bool level)
    {
        if (level)
        {
            c.LastOnUptime = _uptime;
        }

        bool ran = level || _uptime - c.LastOnUptime < CrystalRanWindowSeconds;
        if (ran != c.Ran && _uptime - c.RanSavedAt >= CrystalRanSaveSeconds)
        {
            c.Ran = ran;
            c.RanSavedAt = _uptime;
            c.Config = CrystalConfigWith(c.Config, "run", ran ? "1" : "0");
            SaveCrystalCell(c);
        }
    }

    /// <summary>Queues every running machine's bounded share of the gap.</summary>
    private void StartCrystalCatchUp(double credit)
    {
        var state = CrystalNet;
        state.CatchUp.Clear();
        state.CatchUpSummary.Clear();
        foreach (var c in state.Cells.Values)
        {
            if (c.Inert || !IsCatchUpMachine(c.Kind))
            {
                continue;
            }

            // Running when the world stopped: its row says so, or (a world that stayed in memory) it was ON shortly
            // before the world's last tick.
            bool ran = c.Ran || (state.LastTickUptime - c.LastOnUptime < CrystalRanWindowSeconds && c.LastOnUptime > 0);
            if (!ran)
            {
                continue;
            }

            if (c.Kind == CrystalDeviceKind.CloneTank)
            {
                if (CrystalConfigValue(c.Config, "growing") == "1" && credit >= CrystalNetRules.CloneGrowSeconds)
                {
                    c.NextBeat = _uptime; // the clone grew while you were away: it comes out on the tank's next beat
                    CountCatchUp(c.OwnerId, 4, 1);
                }

                continue;
            }

            int steps = c.Kind switch
            {
                CrystalDeviceKind.AutoDrill when CrystalNetRules.DrillTierOf(c.BlockKey) is int tier and >= 0
                    => (int)Math.Min(credit / CrystalNetRules.DrillTiers[tier].Beat, CrystalNetRules.CatchUpMaxDrillBlocks),
                CrystalDeviceKind.DrillLaser => (int)Math.Min(credit / CrystalNetRules.DrillLaserBeat, CrystalNetRules.DrillLaserDepth),
                CrystalDeviceKind.Fabricator => (int)Math.Min(credit / CrystalNetRules.MoveBeatSeconds, CrystalNetRules.CatchUpMaxCrafts),
                CrystalDeviceKind.MatterSender => (int)Math.Min(credit / CrystalNetRules.MoveBeatSeconds, CrystalNetRules.CatchUpMaxShots),
                CrystalDeviceKind.HydroTray or CrystalDeviceKind.FlowerPot => (int)Math.Min(credit / CatchUpHarvestSeconds, CrystalNetRules.CatchUpMaxHarvests),
                _ => 0,
            };
            if (steps > 0)
            {
                state.CatchUp.Add(new CrystalCatchUpJob { Cell = c, Steps = steps });
            }
        }

        if (state.CatchUp.Count == 0 && state.CatchUpSummary.Count > 0)
        {
            SendCrystalCatchUpSummary();
        }
    }

    /// <summary>Every tick: a budget of machine steps across the queued jobs, round robin; a job ends when its machine is
    /// blocked (the crate full, the pit done, the inputs used up). When all are done, VEGA tells the owners.</summary>
    private void ProcessCrystalCatchUp()
    {
        var state = CrystalNet;
        if (state.CatchUp.Count == 0)
        {
            return;
        }

        int budget = CrystalNetRules.CatchUpEditsPerTick;
        _crystalCatchingUp = true;
        try
        {
            _repo.RunInTransaction(() =>
            {
                bool any = true;
                while (budget > 0 && any)
                {
                    any = false;
                    foreach (var job in state.CatchUp)
                    {
                        if (job.Steps <= 0 || budget <= 0)
                        {
                            continue;
                        }

                        any = true;
                        budget--;
                        job.Steps--;
                        if (CrystalCatchUpStep(job.Cell))
                        {
                            job.Done++;
                        }
                        else
                        {
                            job.Steps = 0; // blocked: the rest of its share is lost, as it would have been live
                        }
                    }
                }
            });
        }
        finally
        {
            _crystalCatchingUp = false;
        }

        if (_crystalCatchUpContainersDirty)
        {
            _crystalCatchUpContainersDirty = false;
            BroadcastContainers();
        }

        if (state.CatchUp.All(j => j.Steps <= 0))
        {
            foreach (var job in state.CatchUp)
            {
                int category = job.Cell.Kind switch
                {
                    CrystalDeviceKind.AutoDrill or CrystalDeviceKind.DrillLaser => 0,
                    CrystalDeviceKind.Fabricator => 1,
                    CrystalDeviceKind.MatterSender => 2,
                    _ => 3,
                };
                CountCatchUp(job.Cell.OwnerId, category, job.Done);
            }

            state.CatchUp.Clear();
            SendCrystalCatchUpSummary();
        }
    }

    /// <summary>One job of one machine, as the live machine would do it (the drills loading what they dig).</summary>
    private bool CrystalCatchUpStep(ServerCrystalCell c) => c.Kind switch
    {
        CrystalDeviceKind.AutoDrill => AutoDrillStep(c, catchUp: true),
        CrystalDeviceKind.DrillLaser => DrillLaserStep(c, catchUp: true),
        CrystalDeviceKind.Fabricator => FabricatorCraft(c),
        CrystalDeviceKind.MatterSender => MatterSenderShot(c),
        CrystalDeviceKind.HydroTray or CrystalDeviceKind.FlowerPot => CatchUpHarvest(c),
        _ => false,
    };

    /// <summary>A tray's or pot's harvest while away: the plant standing on it yields again into a crate beside it (it grew
    /// back between the harvests); the plant stays.</summary>
    private bool CatchUpHarvest(ServerCrystalCell c)
    {
        var above = new Shared.Geometry.Vector3i(c.Cell.X, c.Cell.Y + 1, c.Cell.Z);
        var id = _world.GetBlock(above);
        var def = _content.BlockById(id);
        if (def is null || id.IsAir || !IsFlora(id.Value))
        {
            return false;
        }

        uint bredSeed = IsBredPlant(id.Value) ? BredSeedAt(above) : 0;
        var yield = bredSeed != 0 ? BredYield(bredSeed) : def.Drops;
        var crate = AdjacentCrateWithRoom(c.Cell, yield);
        return crate is not null && NpcDepositToContainer(crate, yield);
    }

    private void CountCatchUp(string owner, int category, int n)
    {
        if (n <= 0 || owner.Length == 0 || CrystalNetRules.IsWorldOwner(owner))
        {
            return;
        }

        var summary = CrystalNet.CatchUpSummary;
        if (!summary.TryGetValue(owner, out var counts))
        {
            counts = new int[5];
            summary[owner] = counts;
        }

        counts[category] += n;
    }

    /// <summary>VEGA tells each machine owner on this world what their machines did while they were away, in their language.</summary>
    private void SendCrystalCatchUpSummary()
    {
        var summary = CrystalNet.CatchUpSummary;
        string[] keys = { "srv.crystal.catchup_blocks", "srv.crystal.catchup_crafts", "srv.crystal.catchup_shots", "srv.crystal.catchup_harvests", "srv.crystal.catchup_clones" };
        foreach (var s in JoinedInActiveWorld())
        {
            if (!summary.TryGetValue(s.State.PlayerId, out var counts) || counts.All(n => n == 0))
            {
                continue;
            }

            var parts = new List<string>();
            for (int i = 0; i < keys.Length; i++)
            {
                if (counts[i] > 0)
                {
                    parts.Add(Localize(s.Locale, keys[i]).Replace("{count}", counts[i].ToString(System.Globalization.CultureInfo.InvariantCulture)));
                }
            }

            Send(s, new ServerMessage { Text = Localize(s.Locale, "srv.crystal.catchup_head") + " " + string.Join(", ", parts) + "." });
        }

        summary.Clear();
    }

    /// <summary>Test seam: whether this world still has catch-up jobs queued.</summary>
    public bool CrystalCatchUpPendingForTest => CrystalNet.CatchUp.Count > 0;
}
