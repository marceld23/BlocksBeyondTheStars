// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client.Feedback;
using BlocksBeyondTheStars.Shared.Content;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #4064, #4065: the Ship Designer recognises a build that is still a shipped ship, block for block — the first ship
/// submission was the shipped Hammerhead with only its stats changed. Every shipped ship matches itself (also moved as a
/// whole); one painted, added or removed block, or another shape, is the player's own build.
/// </summary>
public sealed class ShippedShipMatchTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir());

    private static List<CreationCell> Hammerhead() => ShippedShipMatch.Of(Content.GetShipLayout(Content.Ships["hammerhead"].Layout)!);

    [Fact]
    public void EveryShippedShip_IsFoundAsItself()
    {
        int checkedShips = 0;
        foreach (var (key, def) in Content.Ships)
        {
            var layout = Content.GetShipLayout(def.Layout);
            if (layout is null)
            {
                continue; // the starter box is built in code
            }

            Assert.Equal(key, ShippedShipMatch.Find(ShippedShipMatch.Of(layout), Content));
            checkedShips++;
        }

        Assert.True(checkedShips >= 5, $"only {checkedShips} shipped ships with a layout");
    }

    [Fact]
    public void TheHammerhead_MovedAsAWhole_IsStillTheHammerhead()
    {
        var moved = Hammerhead().Select(c => c.Shifted(-3, -1, 2)).ToList();
        Assert.Equal("hammerhead", ShippedShipMatch.Find(moved, Content));
    }

    [Fact]
    public void OnePaintedBlock_MakesItTheirOwn()
    {
        var cells = Hammerhead();
        var c = cells[0];
        cells[0] = new CreationCell(c.X, c.Y, c.Z, c.Kind, c.Id, 0xFF8800, c.Glow, c.Shape);
        Assert.Equal(string.Empty, ShippedShipMatch.Find(cells, Content));
    }

    [Fact]
    public void OneMovedBlock_OrAnotherShape_MakesItTheirOwn()
    {
        var cells = Hammerhead();
        int top = cells.Max(c => c.Y);
        var c0 = cells[0];
        cells[0] = new CreationCell(c0.X, top + 1, c0.Z, c0.Kind, c0.Id, c0.Tint, c0.Glow, c0.Shape); // same count, new place
        Assert.Equal(string.Empty, ShippedShipMatch.Find(cells, Content));

        cells = Hammerhead();
        var c1 = cells[1];
        cells[1] = new CreationCell(c1.X, c1.Y, c1.Z, c1.Kind, c1.Id, c1.Tint, c1.Glow, c1.Shape + 1);
        Assert.Equal(string.Empty, ShippedShipMatch.Find(cells, Content));
    }

    [Fact]
    public void OneAddedOrRemovedBlock_MakesItTheirOwn()
    {
        var added = Hammerhead();
        int top = added.Max(c => c.Y);
        added.Add(new CreationCell(0, top + 1, 0, "block", "iron_wall", 0, 0, 0));
        Assert.Equal(string.Empty, ShippedShipMatch.Find(added, Content));

        var removed = Hammerhead();
        removed.RemoveAt(removed.Count - 1);
        Assert.Equal(string.Empty, ShippedShipMatch.Find(removed, Content));
    }

    [Fact]
    public void NothingBuilt_OrNoContent_IsNoMatch()
    {
        Assert.Equal(string.Empty, ShippedShipMatch.Find(new List<CreationCell>(), Content));
        Assert.Equal(string.Empty, ShippedShipMatch.Find(Hammerhead(), null));
    }

    [Fact]
    public void AnEmptyKind_ReadsAsBlock_LikeTheLayoutLoader()
    {
        var a = new List<CreationCell> { new(1, 2, 3, null, "iron_wall", 0, 0, 0) };
        var b = new List<CreationCell> { new(5, 5, 5, "block", "iron_wall", 0, 0, 0) };
        Assert.True(ShippedShipMatch.SameBuild(a, b));
        Assert.False(ShippedShipMatch.SameBuild(a, new List<CreationCell> { new(1, 2, 3, "station", "iron_wall", 0, 0, 0) }));
        Assert.Equal(a[0], new CreationCell(1, 2, 3, string.Empty, "iron_wall", 0, 0, 0));
        Assert.True(a[0] != b[0]);
    }
}
