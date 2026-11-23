// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The bundled tiles are drawn in the calm style, and the block atlas exempts them from the texture calm
/// (<see cref="CalmTileRule"/>). The only bundled tiles that keep the classic treatment are the data tiles that were never
/// redrawn: the 16 coat pattern masks and the 5 horn ramps. A new bundled tile is a calm tile unless it joins one of
/// those families on purpose.
/// </summary>
public sealed class CalmTileRuleTests
{
    private static string TexturesDir() => Path.Combine(ClientTestPaths.RepoRoot(), "client", "Assets", "Resources", "textures");

    [Fact]
    public void TheClassicBundledTiles_AreExactlyTheCoatPatternMasksAndHornRamps()
    {
        var keys = Directory.GetFiles(TexturesDir(), "*.bytes").Select(Path.GetFileNameWithoutExtension).ToList();
        Assert.NotEmpty(keys);

        var classic = keys.Where(k => !CalmTileRule.IsCalmBundledKey(k!)).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(21, classic.Count);
        Assert.All(classic, k => Assert.True(
            k!.StartsWith("creature_pattern_", StringComparison.Ordinal) || k.StartsWith("creature_horn_", StringComparison.Ordinal), k));
    }

    [Theory]
    [InlineData("stone", true)]
    [InlineData("creature_fur", true)]
    [InlineData("devform_metal_light", true)]
    [InlineData("creature_pattern_giraffe", false)]
    [InlineData("creature_horn_spiral", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsCalmBundledKey_FollowsTheClassicPrefixes(string? key, bool calm)
        => Assert.Equal(calm, CalmTileRule.IsCalmBundledKey(key!));
}
