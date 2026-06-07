using System.Linq;
using Spacecraft.Shared.Content;
using Xunit;

namespace Spacecraft.Tests;

/// <summary>
/// Task 5 Stage 2 — knowledge-economy + ore depth-tier balance. These lock in the design intent so a later
/// data edit can't silently revert the eased data_fragment grind or push a valuable ore out of reach.
/// </summary>
public sealed class EconomyBalanceTests
{
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    // Construction-grade ores stay shallow; the rare/valuable tier sits deeper (reward deep mining + better drills).
    private static readonly string[] SurfaceTier = { "iron_ore", "copper_ore", "silicate", "carbon" };
    private static readonly string[] DeepTier =
        { "gold_ore", "silver_ore", "cobalt_ore", "uranium_ore", "platinum_ore", "tungsten_ore", "neodymium_ore" };

    [Fact]
    public void DataCache_DropsEasedFragmentYield()
    {
        // Stage 2: a found cache yields 2 fragments (was 1) so the exploration path is half the grind.
        var cache = _content.GetBlock("data_cache");
        Assert.NotNull(cache);
        int fragments = cache!.Drops.Where(d => d.Item == "data_fragment").Sum(d => d.Count);
        Assert.True(fragments >= 2, $"data_cache should drop >= 2 data_fragment, was {fragments}");
    }

    [Fact]
    public void EveryOreVein_StaysReachable_WithinItsCrust()
    {
        // No depth soft-lock: an ore's minimum depth must leave a real mining band above bedrock (the crust is
        // roughly BaseHeight blocks deep, surface down to Y=0), so it can always be reached with a basic drill.
        foreach (var planet in _content.Planets.Values.Where(p => !p.Void && p.Ores.Count > 0))
        {
            foreach (var ore in planet.Ores)
            {
                Assert.True(ore.MinDepth < ore.MaxDepth, $"{planet.Key}/{ore.Block}: minDepth >= maxDepth");
                Assert.True(ore.MinDepth <= planet.BaseHeight - 8,
                    $"{planet.Key}/{ore.Block}: minDepth {ore.MinDepth} too deep for a crust of ~{planet.BaseHeight}");
            }
        }
    }

    [Fact]
    public void ValuableOres_SitDeeperThanConstructionOres()
    {
        // The tier intent: every placement of a rare/valuable ore is below the shallow construction band.
        foreach (var planet in _content.Planets.Values.Where(p => !p.Void))
        {
            foreach (var ore in planet.Ores)
            {
                if (SurfaceTier.Contains(ore.Block))
                    Assert.True(ore.MinDepth <= 8, $"{planet.Key}/{ore.Block}: surface-tier ore too deep ({ore.MinDepth})");
                if (DeepTier.Contains(ore.Block))
                    Assert.True(ore.MinDepth >= 16, $"{planet.Key}/{ore.Block}: deep-tier ore too shallow ({ore.MinDepth})");
            }
        }
    }
}
