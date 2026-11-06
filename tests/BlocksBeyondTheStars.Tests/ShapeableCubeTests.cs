// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Content;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3287: every cube material a player can hold can be re-formed (stairs, slabs, …). The wooden planks of #3181
/// were added without joining the curated shape list, so "wooden stairs" only existed with bark on them. This
/// guard makes the next new block fail loudly instead: a placeable, solid building/terrain block is either
/// shapeable or named here with the reason it keeps its own form.
/// </summary>
public sealed class ShapeableCubeTests
{
    private static readonly Dictionary<string, string> KeepsItsOwnForm = new(StringComparer.Ordinal)
    {
        ["lava"] = "a glowing fluid",
        ["tar"] = "keeps its fluid look",
        ["stairs"] = "already a staircase prop",
        ["crate"] = "a container with its own picture",
        ["wood_crate"] = "a container with its own picture",
        ["transport_crate"] = "a container with its own picture",
        ["gift_box"] = "a present that opens",
        ["rug"] = "a flat picture prop",
        ["flower_pot"] = "a planter prop",
        ["broken_machine"] = "a machine front",
        ["gaming_pc"] = "a machine front",
        ["gaming_monitor"] = "a machine front",
        ["gaming_keyboard"] = "a prop",
        ["gaming_mouse"] = "a prop",
        ["paul_leaf"] = "foliage",
        ["paul_petals"] = "foliage",
        ["crystal_conduit"] = "a power conduit",
        ["engine_nozzle"] = "a ship engine part",
        ["phase_block"] = "switches between solid and open",
    };

    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    [Fact]
    public void EveryPlaceableCubeMaterial_IsShapeable_UnlessItKeepsItsOwnForm()
    {
        var placeable = _content.Items.Values
            .Where(i => !string.IsNullOrEmpty(i.PlacesBlock))
            .Select(i => i.PlacesBlock!)
            .ToHashSet(StringComparer.Ordinal);

        var missing = _content.Blocks.Values
            .Where(b => b.Category is "building" or "terrain")
            .Where(b => b.Solid && !b.Liquid && placeable.Contains(b.Key))
            .Where(b => !b.Shapeable && !KeepsItsOwnForm.ContainsKey(b.Key))
            .Select(b => b.Key)
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();

        Assert.True(missing.Count == 0,
            "These cube materials cannot be shaped — add them to GameContent.TintableDefaults, or to KeepsItsOwnForm here "
            + "with the reason: " + string.Join(", ", missing));
    }

    [Theory]
    [InlineData("wood_planks")]
    [InlineData("wood_planks_light")]
    [InlineData("wood_planks_mahogany")]
    [InlineData("iron_block")]
    [InlineData("lead_block")]
    [InlineData("obsidian")]
    [InlineData("ice")]
    [InlineData("glass")]
    [InlineData("glass_clear")]
    public void TheMaterialsOfTheReport_AreShapeable(string key)
    {
        Assert.True(_content.GetBlock(key)!.Shapeable, key + " should take a form (#3287)");
    }

    [Fact]
    public void TheExceptions_AreStillRealBlocks()
    {
        // A stale exception would silently hide a renamed block from the guard above.
        foreach (var key in KeepsItsOwnForm.Keys)
        {
            Assert.NotNull(_content.GetBlock(key));
        }
    }
}
