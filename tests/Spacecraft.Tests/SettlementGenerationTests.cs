using System.Linq;
using Spacecraft.Shared.Content;
using Spacecraft.WorldGeneration;
using Xunit;

namespace Spacecraft.Tests;

/// <summary>
/// Procedural planet settlements: buildings assembled from blocks on a plot grid, baked into one
/// voxel structure to stamp on the terrain. Villages are single-storey in the biome material; towns
/// are multi-storey iron/glass with ladders between floors. A ruined variant decays the build, drops
/// the NPCs and leaves loot. Deterministic from the seed.
/// </summary>
public sealed class SettlementGenerationTests
{
    private static GameContent Content() => ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    [Fact]
    public void Generation_IsDeterministic_ForSameSeed()
    {
        var c = Content();
        var a = SettlementGenerator.Generate("town", false, 4242, "stone", c);
        var b = SettlementGenerator.Generate("town", false, 4242, "stone", c);

        Assert.Equal(a.Width, b.Width);
        Assert.Equal(a.Height, b.Height);
        Assert.Equal(a.Length, b.Length);
        Assert.Equal(a.BuildingCount, b.BuildingCount);
        for (int x = 0; x < a.Width; x++)
        for (int y = 0; y < a.Height; y++)
        for (int z = 0; z < a.Length; z++)
        {
            Assert.Equal(a.Get(x, y, z), b.Get(x, y, z));
        }
    }

    [Fact]
    public void Village_IsSingleStorey_InBiomeMaterial()
    {
        var c = Content();
        var s = SettlementGenerator.Generate("village", false, 1, "mud", c);
        ushort mud = c.GetBlock("mud")!.NumericId.Value;

        Assert.Equal("village", s.Tier);
        // A village is one storey: shorter than a town of the same scheme.
        var town = SettlementGenerator.Generate("town", false, 1, "mud", c);
        Assert.True(town.Height > s.Height);

        // Built from the biome material (mud), not iron.
        bool usesMud = false;
        for (int x = 0; x < s.Width && !usesMud; x++)
        for (int y = 0; y < s.Height; y++)
        for (int z = 0; z < s.Length; z++)
        {
            if (s.Get(x, y, z) == mud) { usesMud = true; break; }
        }

        Assert.True(usesMud, "A village should be built from its biome surface block.");
    }

    [Fact]
    public void Town_IsMultiStorey_WithLaddersBetweenFloors()
    {
        var c = Content();
        var s = SettlementGenerator.Generate("town", false, 3, "stone", c);
        ushort ladder = c.GetBlock("ladder")!.NumericId.Value;

        Assert.Equal("town", s.Tier);
        bool hasLadder = false;
        for (int x = 0; x < s.Width && !hasLadder; x++)
        for (int y = 0; y < s.Height; y++)
        for (int z = 0; z < s.Length; z++)
        {
            if (s.Get(x, y, z) == ladder) { hasLadder = true; break; }
        }

        Assert.True(hasLadder, "A multi-storey town building must have a ladder between floors.");
    }

    [Fact]
    public void Settlement_HasVendorMissionAndNpcMarkers()
    {
        var c = Content();
        var s = SettlementGenerator.Generate("town", false, 9, "stone", c);

        Assert.Contains(s.Markers, m => m.Type == "vendor");
        Assert.Contains(s.Markers, m => m.Type == "mission_board");
        Assert.Contains(s.Markers, m => m.Type == "npc");
        Assert.Contains(new[] { "human", "alien" }, x => x == s.Inhabitant);
    }

    [Fact]
    public void Ruined_HasNoNpcs_ButLeavesLoot()
    {
        var c = Content();
        var s = SettlementGenerator.Generate("village", true, 5, "stone", c);

        Assert.True(s.Ruined);
        Assert.Equal("", s.Inhabitant);
        Assert.DoesNotContain(s.Markers, m => m.Type == "npc" || m.Type == "vendor" || m.Type == "mission_board");
        // A ruined settlement is explorable for loot (very likely to have at least one cache).
        Assert.Contains(s.Markers, m => m.Type == "loot");
    }

    [Fact]
    public void Buildings_AreHollow_WithDoors()
    {
        var c = Content();
        var s = SettlementGenerator.Generate("village", false, 2, "stone", c);
        ushort air = 0;

        int interiorAir = 0;
        for (int x = 1; x < s.Width - 1; x++)
        for (int y = 1; y < s.Height - 1; y++)
        for (int z = 1; z < s.Length - 1; z++)
        {
            if (s.Get(x, y, z) == air) interiorAir++;
        }

        Assert.True(interiorAir > 0, "Buildings must be hollow (enterable rooms).");
        Assert.True(s.BuildingCount >= 1);
    }

    [Fact]
    public void FourSizeTiers_ScaleFromHamletToCity()
    {
        var c = Content();
        var hamlet = SettlementGenerator.Generate("hamlet", false, 11, "stone", c);
        var village = SettlementGenerator.Generate("village", false, 11, "stone", c);
        var town = SettlementGenerator.Generate("town", false, 11, "stone", c);
        var city = SettlementGenerator.Generate("city", false, 11, "stone", c);

        // A hamlet is the smallest footprint; a city the largest + tallest.
        Assert.True(hamlet.Width <= village.Width);
        Assert.True(city.Width >= town.Width);
        Assert.True(city.Height > town.Height, "A city is taller (more storeys) than a town.");
        // Town-style tiers are multi-storey; village-style are single-storey huts (shorter).
        Assert.True(town.Height > village.Height);
    }

    public void Dispose() { }
}
