// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client.Feedback;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #4067: the Station / Village editor recognises a build that is still a shipped station or town template, cell for cell —
/// the twin of the Ship Designer's check (#4065). Every shipped template matches itself (also moved as a whole); one changed
/// block, sign text or device setting is the player's own build.
/// </summary>
public sealed class ShippedTemplateMatchTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir());

    private static IEnumerable<StructureTemplate> WithCells(IReadOnlyList<StructureTemplate> pool) => pool.Where(t => t.Cells.Count > 0);

    private static StructureTemplate AStation() => WithCells(Content.StationTemplates).OrderBy(t => t.Key, StringComparer.Ordinal).First();

    [Fact]
    public void EveryShippedTemplate_IsFoundAsItself()
    {
        int checkedTemplates = 0;
        foreach (var pool in new[] { Content.StationTemplates, Content.SettlementTemplates })
        {
            foreach (var t in WithCells(pool))
            {
                var cells = ShippedTemplateMatch.Of(t);
                string found = ShippedTemplateMatch.Find(cells, pool);
                Assert.False(string.IsNullOrEmpty(found), $"{t.Key} was not found in its own pool");

                // Two shipped templates may share a build; the first in key order is named, and it is the same build.
                var twin = pool.First(p => p.Key == found);
                Assert.True(CreationCell.SameBuild(cells, ShippedTemplateMatch.Of(twin)), $"{t.Key} matched {found}, a different build");
                checkedTemplates++;
            }
        }

        Assert.True(checkedTemplates >= 10, $"only {checkedTemplates} shipped templates with cells");
    }

    [Fact]
    public void ATemplate_MovedAsAWhole_IsStillThatTemplate()
    {
        var t = AStation();
        var moved = ShippedTemplateMatch.Of(t).Select(c => c.Shifted(-5, -2, 7)).ToList();
        Assert.Equal(t.Key, ShippedTemplateMatch.Find(moved, Content.StationTemplates));
    }

    [Fact]
    public void ANewSignText_OrAnotherDeviceSetting_MakesItTheirOwn()
    {
        var t = AStation();
        var cells = ShippedTemplateMatch.Of(t);
        var c = t.Cells[0];
        cells[0] = ShippedTemplateMatch.Cell(c.X, c.Y, c.Z, c.Kind, c.Id, c.Tint, c.Glow, c.Shape, c.Port, c.Mode, c.Config, c.Label + "Hello", c.Keep);
        Assert.Equal(string.Empty, ShippedTemplateMatch.Find(cells, Content.StationTemplates));

        cells = ShippedTemplateMatch.Of(t);
        cells[0] = ShippedTemplateMatch.Cell(c.X, c.Y, c.Z, c.Kind, c.Id, c.Tint, c.Glow, c.Shape, c.Port, c.Mode + 1, c.Config, c.Label, c.Keep);
        Assert.Equal(string.Empty, ShippedTemplateMatch.Find(cells, Content.StationTemplates));

        cells = ShippedTemplateMatch.Of(t);
        cells[0] = ShippedTemplateMatch.Cell(c.X, c.Y, c.Z, c.Kind, c.Id, c.Tint, c.Glow, c.Shape, c.Port, c.Mode, c.Config, c.Label, !c.Keep);
        Assert.Equal(string.Empty, ShippedTemplateMatch.Find(cells, Content.StationTemplates));
    }

    [Fact]
    public void OneAddedOrRemovedBlock_MakesItTheirOwn()
    {
        var t = AStation();
        var added = ShippedTemplateMatch.Of(t);
        int top = added.Max(c => c.Y);
        added.Add(ShippedTemplateMatch.Cell(0, top + 1, 0, "block", "iron_wall", 0, 0, 0, null, 0, null, null, false));
        Assert.Equal(string.Empty, ShippedTemplateMatch.Find(added, Content.StationTemplates));

        var removed = ShippedTemplateMatch.Of(t);
        removed.RemoveAt(removed.Count - 1);
        Assert.Equal(string.Empty, ShippedTemplateMatch.Find(removed, Content.StationTemplates));
    }

    [Fact]
    public void NothingBuilt_OrNoPool_IsNoMatch()
    {
        Assert.Equal(string.Empty, ShippedTemplateMatch.Find(new List<CreationCell>(), Content.StationTemplates));
        Assert.Equal(string.Empty, ShippedTemplateMatch.Find(ShippedTemplateMatch.Of(AStation()), null));
    }

    [Fact]
    public void AShipCell_HasNoDetail_AndATemplateCellWithoutSettingsIsNotAShipCell()
    {
        var ship = new CreationCell(1, 2, 3, "block", "iron_wall", 0, 0, 0);
        var template = ShippedTemplateMatch.Cell(1, 2, 3, "block", "iron_wall", 0, 0, 0, null, 0, null, null, false);
        Assert.Equal(string.Empty, ship.Detail);
        Assert.NotEqual(ship, template); // the two editors never compare across kinds; their detail shapes differ on purpose
        Assert.Equal(template, ShippedTemplateMatch.Cell(1, 2, 3, null, "iron_wall", 0, 0, 0, string.Empty, 0, string.Empty, string.Empty, false));
    }
}
