using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>A hand-designed structure template is faithfully turned into a station/settlement
/// structure (blocks become voxels, markers become interaction points).</summary>
public sealed class StructureTemplateTests
{
    private static GameContent Content() => ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    [Fact]
    public void StationTemplate_BuildsVoxelsAndMarkers()
    {
        var c = Content();
        ushort hull = c.GetBlock("iron_wall")!.NumericId.Value;

        var t = new StructureTemplate
        {
            Key = "test_station", Name = "Test", Tier = "small", Kind = "station",
            Width = 6, Height = 4, Length = 6,
            Cells = new List<TemplateCell>
            {
                new() { X = 1, Y = 0, Z = 1, Kind = "block", Id = "iron_wall" },
                new() { X = 4, Y = 0, Z = 4, Kind = "block", Id = "glass" },
                new() { X = 3, Y = 1, Z = 3, Kind = "marker", Id = "heal_tank" },
            },
        };

        var s = StationGenerator.FromTemplate(t, c);

        Assert.Equal(6, s.Width);
        Assert.Equal(hull, s.Get(1, 0, 1));      // block placed
        Assert.Equal(0, s.Get(0, 0, 0));         // empty stays air
        Assert.Contains(s.Markers, m => m.Type == "heal_tank");
        Assert.Contains(s.Markers, m => m.Type == "vendor");        // guaranteed essentials
        Assert.Contains(s.Markers, m => m.Type == "mission_board");
    }

    [Fact]
    public void SettlementTemplate_BuildsVoxelsAndMarkers()
    {
        var c = Content();
        ushort stone = c.GetBlock("stone")!.NumericId.Value;

        var t = new StructureTemplate
        {
            Key = "test_town", Name = "Test", Tier = "village", Kind = "settlement",
            Width = 5, Height = 4, Length = 5,
            Cells = new List<TemplateCell>
            {
                new() { X = 0, Y = 0, Z = 0, Kind = "block", Id = "stone" },
                new() { X = 2, Y = 1, Z = 2, Kind = "marker", Id = "npc" },
                new() { X = 1, Y = 1, Z = 1, Kind = "marker", Id = "vendor" },
            },
        };

        var s = SettlementGenerator.FromTemplate(t, c);

        Assert.Equal("village", s.Tier);
        Assert.False(s.Ruined);
        Assert.Equal(stone, s.Get(0, 0, 0));
        Assert.Contains(s.Markers, m => m.Type == "npc");
        Assert.Contains(s.Markers, m => m.Type == "vendor");
    }
}
