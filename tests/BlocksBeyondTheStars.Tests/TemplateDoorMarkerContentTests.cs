// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.World;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #1982: a door in a shipped SETTLEMENT template is a MARKER on the doorway's floor with a walkable opening over
/// it — the server hangs a door on markers only. Eight templates used to carry the door as a block cell, which
/// stamped as a solid door-textured wall; these keep that from coming back and pin that every door marker stands
/// where the shared door rule finds a wall beside it.
/// <para>A STATION template may carry a door block, but only in its outer hull: there it is the airtight airlock
/// block the station air model counts as sealed, never a door somebody walks through.</para>
/// </summary>
public sealed class TemplateDoorMarkerContentTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    [Fact]
    public void NoSettlementTemplateCarriesADoorAsABlock()
    {
        var offenders = Content.SettlementTemplates
            .SelectMany(t => t.Cells.Where(c => c.Kind != "marker" && DoorBlocks.IsDoorBlock(c.Id)).Select(c => $"{t.Key} ({c.X},{c.Y},{c.Z}) {c.Id}"))
            .ToList();
        Assert.True(offenders.Count == 0, "door BLOCK cells (should be markers):\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void EverySettlementDoorMarkerHasAWallBesideItAndRoomToWalkThrough()
    {
        var offenders = new List<string>();
        foreach (var t in Content.SettlementTemplates)
        {
            var solid = new HashSet<(int, int, int)>(t.Cells.Where(c => c.Kind != "marker").Select(c => (c.X, c.Y, c.Z)));
            bool Solid(int x, int y, int z) => solid.Contains((x, y, z));
            foreach (var m in t.Cells.Where(c => c.Kind == "marker" && DoorBlocks.IsDoorMarker(c.Id)))
            {
                var fit = DoorProbe.Measure(Solid, m.X, m.Y, m.Z);
                if (!fit.HasJamb)
                {
                    offenders.Add($"{t.Key} ({m.X},{m.Y},{m.Z}) {m.Id}: no wall beside it");
                }

                if (Solid(m.X, m.Y + 1, m.Z))
                {
                    offenders.Add($"{t.Key} ({m.X},{m.Y},{m.Z}) {m.Id}: the cell above is wall — nobody fits through");
                }
            }
        }

        Assert.True(offenders.Count == 0, "door markers the server would hang wrong:\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void AStationDoorBlockIsAnAirlockInTheOuterHullOnly()
    {
        var offenders = new List<string>();
        foreach (var t in Content.StationTemplates)
        {
            foreach (var c in t.Cells.Where(c => c.Kind != "marker" && DoorBlocks.IsDoorBlock(c.Id)))
            {
                bool onHull = c.X == 0 || c.X == t.Width - 1 || c.Z == 0 || c.Z == t.Length - 1;
                if (!onHull)
                {
                    offenders.Add($"{t.Key} ({c.X},{c.Y},{c.Z}) {c.Id}");
                }
            }
        }

        Assert.True(offenders.Count == 0, "door BLOCK cells inside a station (a wall, not an airlock — use a door marker):\n" + string.Join("\n", offenders));
    }
}
