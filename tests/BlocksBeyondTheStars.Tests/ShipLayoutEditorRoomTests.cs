// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The ship editor can load every shipped ship as a starting point (#1394): each layout referenced from
/// <c>data/ships.json</c> must fit the editor's build room after the interior-origin shift (#1397), and its
/// station ids must be ones the editor palette offers — otherwise a load would silently drop cells (#1398).
/// </summary>
public sealed class ShipLayoutEditorRoomTests
{
    /// <summary>The station ids the ship editor's palette offers (ShipEditor.BuildPalette), mirrored here so a
    /// layout that introduces a new station type fails loudly instead of losing the cell on load.</summary>
    private static readonly HashSet<string> EditorStations = new(StringComparer.Ordinal)
    {
        "cockpit", "reactor", "life_support", "workshop", "medbay", "quarters", "cargo", "hangar", "console",
        "ship_laser_basic", "ship_cannon_1",
    };

    [Fact]
    public void EveryShippedLayout_FitsTheEditorRoom_AndUsesPaletteStations()
    {
        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        int checkedLayouts = 0;
        foreach (var ship in content.Ships.Values)
        {
            var layout = content.GetShipLayout(ship.Layout);
            if (layout == null)
            {
                continue; // the starter ship is a code-built box and has no layout
            }

            checkedLayouts++;
            foreach (var cell in layout.Cells)
            {
                Assert.True(ShipLayout.FitsEditorRoom(cell.X, cell.Y, cell.Z),
                    $"{layout.Key}: cell ({cell.X},{cell.Y},{cell.Z}) is outside the editor's build room.");
                if (cell.Kind == "station")
                {
                    Assert.True(EditorStations.Contains(cell.Id), $"{layout.Key}: station '{cell.Id}' is not in the ship editor palette.");
                }
            }

            // The interior frame the editor shows must be inside the room too (origin + dims).
            Assert.True(ShipLayout.FitsEditorRoom(layout.Width - 1, layout.Height, layout.Length - 1),
                $"{layout.Key}: interior {layout.Width}×{layout.Height}×{layout.Length} exceeds the editor room.");
        }

        Assert.True(checkedLayouts >= 7, $"expected the seven shipped layouts, found {checkedLayouts}");
    }

    [Fact]
    public void FitsEditorRoom_RejectsCellsBeyondTheOriginMargin()
    {
        Assert.True(ShipLayout.FitsEditorRoom(-ShipLayout.EditorOriginX, 0, -ShipLayout.EditorOriginZ));
        Assert.False(ShipLayout.FitsEditorRoom(-ShipLayout.EditorOriginX - 1, 0, 0));
        Assert.False(ShipLayout.FitsEditorRoom(0, -1, 0));
        Assert.False(ShipLayout.FitsEditorRoom(ShipLayout.EditorRoomWidth - ShipLayout.EditorOriginX, 0, 0));
        Assert.True(ShipLayout.FitsEditorRoom(ShipLayout.EditorRoomWidth - ShipLayout.EditorOriginX - 1, ShipLayout.EditorRoomHeight - 1, 0));
    }
}
