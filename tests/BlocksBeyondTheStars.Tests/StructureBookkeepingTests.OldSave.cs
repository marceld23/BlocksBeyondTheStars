// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.State;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3510: a save written BEFORE the structure pipeline, simulated — a generation-37 world booted with today's build, then
/// stripped of everything the pipeline added to its metadata (the pinned counts, the new record kinds, the record fields Tier,
/// Ruined, Design and the footprint size, the Guardian core's stamped mark). Its next load must find what stands: no chest
/// twice, the backfilled records equal to the ones the first stamp wrote, and the Guardian core chamber not written over
/// what a player changed in it.
/// </summary>
public sealed partial class StructureBookkeepingTests
{
    /// <summary>The record kinds #3513 introduced — a save from before has none of them.</summary>
    private static readonly string[] NewRecordKinds =
    {
        SvGameServer.RuinRecordKind, SvGameServer.MonumentRecordKind, SvGameServer.TextTabletRecordKind, SvGameServer.ChestRecordKind,
        SvGameServer.DataCubeRecordKind, SvGameServer.GuardianCoreRecordKind,
    };

    [Fact]
    public void AnOldSave_LoadsWithoutADuplicateChest_AndBackfillsTheRecordsOfWhatStands()
    {
        const string name = "oldsave";
        (List<string> Loot, List<string> Containers, List<string> Records, List<string> Settlements) Look(SvGameServer s)
            => (s.Metadata.GeneratedLoot.Where(k => k.StartsWith("chest:", StringComparison.Ordinal)).OrderBy(k => k, StringComparer.Ordinal).ToList(),
                s.Containers.Select(c => $"{c.Id}@{c.Position}").OrderBy(c => c, StringComparer.Ordinal).ToList(),
                Records(s),
                SettlementVerdicts(s).Select(v => v.ToString()).ToList());

        var first = Boot(name, "meadowlands", 20261112L, Gen37, Look);
        Assert.Equal(2, first.Loot.Count); // the premise: this body rolls two chests (the placement golden's world)
        Assert.Contains(first.Records, r => r.StartsWith(SvGameServer.ChestRecordKind + ":", StringComparison.Ordinal));
        Assert.NotEmpty(first.Settlements);

        EditMetadata(name, StripToAnOldSave);

        var again = Boot(name, "meadowlands", 20261112L, Gen37, Look);
        Assert.Equal(first.Loot, again.Loot);             // no third cache, no chest twice
        Assert.Equal(first.Containers, again.Containers);
        Assert.Equal(first.Records, again.Records);       // the backfilled records are the stamped structures
        Assert.Equal(first.Settlements, again.Settlements);

        // And it stays put: a further load reads the backfilled records back.
        Assert.Equal(first.Records, Boot(name, "meadowlands", 20261112L, Gen37, Records));
    }

    [Fact]
    public void AnOldSave_DoesNotWriteTheGuardianCoreChamberOverWhatAPlayerChanged()
    {
        const string name = "oldcore";
        var stone = Content.GetBlock("stone")!.NumericId;
        Vector3i core = default, inside = default;
        string location = string.Empty;
        Boot(name, "rocky", 9, Gen37, s =>
        {
            core = s.LoadGuardianCoreForTest();
            location = s.ActiveLocationId;
            inside = new Vector3i(core.X + 3, core.Y, core.Z);
            s.World.SetBlock(core, BlockId.Air, owner: "tester"); // a player mines the core cell …
            s.World.SetBlock(inside, stone, owner: "tester");     // … and builds inside
        });

        // Before #3513 the chamber had no record and no stamped mark (an in-memory flag guarded it).
        EditMetadata(name, meta =>
        {
            StripToAnOldSave(meta);
            meta.StampedFeatures.RemoveAll(f => f.EndsWith("|guardiancore", StringComparison.Ordinal));
        });

        Boot(name, "rocky", 9, Gen37, s =>
        {
            Assert.Equal(core, s.LoadGuardianCoreForTest());
            Assert.True(s.HasCoreChamberForTest);
            Assert.True(s.World.GetBlock(core).IsAir);            // not written again …
            Assert.Equal(stone, s.World.GetBlock(inside));        // … over what the player built
            Assert.True(s.FeatureStampedForTest("guardiancore")); // and marked, so it never is
            Assert.Single(s.PlacementRecordsForTest, r => r.Kind == SvGameServer.GuardianCoreRecordKind && r.Placed);
        });
    }

    /// <summary>Strips what the structure pipeline added to a save's metadata: the pinned counts, the records of the new kinds
    /// and the new record fields.</summary>
    private static void StripToAnOldSave(WorldMetadata meta)
    {
        meta.StructureCounts.Clear();
        meta.Placements.RemoveAll(r => NewRecordKinds.Contains(r.Kind));
        foreach (var r in meta.Placements)
        {
            (r.Tier, r.Ruined, r.Design, r.Width, r.Length) = (null, null, null, 0, 0);
        }
    }

    /// <summary>The records of the new kinds, the settlements and the wreck — placement, seat and template — as text in a stable
    /// order. A backfilled monument, ruin or text tablet says seat <c>legacy</c> (how it was seated is not known any more; its
    /// place is what replays), so their seat is left out.</summary>
    private static List<string> Records(SvGameServer s)
        => s.PlacementRecordsForTest
            .Where(r => NewRecordKinds.Contains(r.Kind) || r.Kind is "settlement" or "wreck")
            .OrderBy(r => r.Kind, StringComparer.Ordinal).ThenBy(r => r.Index)
            .Select(r => $"{r.Kind}:{r.Index}:{r.Placed}:{r.X},{r.GroundY},{r.Z}:"
                + (r.Kind is SvGameServer.MonumentRecordKind or SvGameServer.RuinRecordKind or SvGameServer.TextTabletRecordKind ? "-" : r.Seat)
                + $":{r.Template}")
            .ToList();
}
