// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #2162: engine plumes sat on every ship's rear door because the client never found the engines. The cluster finder
/// turns a hull's nozzle cells into one exhaust per ENGINE (neighbouring nozzle cells are one engine), placed on its
/// open rear faces.
/// </summary>
public sealed class ShipExhaustsTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir());

    /// <summary>The structure cells of an authored layout as the server stamps them: every cell but the doorway
    /// markers is solid, the layout's "engine" element is the nozzle block.</summary>
    private static (List<(int X, int Y, int Z)> Engines, HashSet<(int X, int Y, int Z)> Solid) Layout(string key)
    {
        var layout = Content.GetShipLayout(key)!;
        var solid = layout.Cells
            .Where(c => c.Id is not ("door_slide" or "door_hinge" or "door_energy" or "hatch"))
            .Select(c => (c.X, c.Y, c.Z))
            .ToHashSet();
        var engines = layout.Cells.Where(c => c.Id == "engine").Select(c => (c.X, c.Y, c.Z)).ToList();
        return (engines, solid);
    }

    [Fact]
    public void Hammerhead_TwoBigEngines_AndTwoSmallNozzles_AllAtTheStern()
    {
        var (engines, solid) = Layout("ship_hammerhead");

        var exhausts = ShipExhausts.Find(engines, (x, y, z) => solid.Contains((x, y, z)));

        Assert.Equal(4, exhausts.Count);
        // The two 2×2 engines first (biggest first), each centred on its block: x 4..5 → 5.0, x 8..9 → 9.0, y 1..2 → 2.0.
        Assert.Equal(4, exhausts[0].Faces);
        Assert.Equal(5f, exhausts[0].X, 3);
        Assert.Equal(2f, exhausts[0].Y, 3);
        Assert.Equal(4, exhausts[1].Faces);
        Assert.Equal(9f, exhausts[1].X, 3);
        Assert.Equal(2f, exhausts[1].Size, 3);
        // Then the single nozzles at the outer corners.
        Assert.Equal(new[] { 2.5f, 11.5f }, exhausts.Skip(2).Select(e => e.X).ToArray());
        Assert.All(exhausts, e => Assert.Equal(-1f, e.Z)); // the rear face of the z = −1 nozzle row
    }

    [Fact]
    public void EveryAuthoredShip_HasEngines_AndNoneOnTheRearDoor()
    {
        // The bug in one line: no exhaust may sit in front of a rear doorway column.
        int ships = 0;
        foreach (var ship in Content.Ships.Values)
        {
            if (Content.GetShipLayout(ship.Layout) is not { } layout)
            {
                continue;
            }

            ships++;
            var (engines, solid) = Layout(layout.Key);
            var exhausts = ShipExhausts.Find(engines, (x, y, z) => solid.Contains((x, y, z)));
            Assert.True(exhausts.Count > 0, $"{layout.Key}: no engine exhaust found");

            var rearDoors = layout.Cells.Where(c => c.Id.StartsWith("door_", System.StringComparison.Ordinal) && c.Z == 0).ToList();
            foreach (var door in rearDoors)
            {
                Assert.DoesNotContain(exhausts, e => e.X > door.X && e.X < door.X + 1 && e.Y > door.Y && e.Y < door.Y + 1);
            }
        }

        Assert.True(ships > 0);
    }

    [Fact]
    public void StarterBox_TwoCornerNozzles_TwoExhausts()
    {
        // The box ship stamps its two nozzles at the rear corners (x = 0 and x = 2·halfX, y = 1, z = −1) — a 5-wide hull.
        var engines = new List<(int X, int Y, int Z)> { (0, 1, -1), (4, 1, -1) };
        var hull = new HashSet<(int X, int Y, int Z)>(engines);
        for (int x = 0; x <= 4; x++)
        {
            for (int y = 0; y <= 4; y++)
            {
                hull.Add((x, y, 0)); // the rear wall in front of the nozzles
            }
        }

        var exhausts = ShipExhausts.Find(engines, (x, y, z) => hull.Contains((x, y, z)));

        Assert.Equal(2, exhausts.Count);
        Assert.Equal(new[] { 0.5f, 4.5f }, exhausts.Select(e => e.X).ToArray());
        Assert.All(exhausts, e => Assert.Equal(1.5f, e.Y));
        Assert.All(exhausts, e => Assert.Equal(1f, e.Size));
    }

    [Fact]
    public void BuriedOrForwardEngine_BreathesNoExhaust()
    {
        // A nozzle with a block behind it (built into the hull, or the nose half of a forward-facing pod) has no plume.
        var engines = new List<(int X, int Y, int Z)> { (3, 1, 5), (3, 1, 6) };
        var solid = new HashSet<(int X, int Y, int Z)>(engines) { (3, 1, 4) };

        Assert.Empty(ShipExhausts.Find(engines, (x, y, z) => solid.Contains((x, y, z))));
    }

    [Fact]
    public void ManyEngines_AreCapped_BiggestFirst()
    {
        var engines = Enumerable.Range(0, 10).Select(i => (i * 2, 1, -1)).ToList();
        engines.Add((40, 1, -1));
        engines.Add((41, 1, -1)); // one 2-wide engine among singles

        var exhausts = ShipExhausts.Find(engines, (_, _, _) => false, max: 4);

        Assert.Equal(4, exhausts.Count);
        Assert.Equal(2, exhausts[0].Faces);
        Assert.Equal(41f, exhausts[0].X, 3);
    }

    [Theory]
    [InlineData("engine_nozzle", true)]
    [InlineData("ship_engine", true)]
    [InlineData("carbon", false)]
    [InlineData(null, false)]
    public void IsEngineBlock_KnowsTheNozzleBlocks(string? key, bool expected)
        => Assert.Equal(expected, ShipExhausts.IsEngineBlock(key));
}
