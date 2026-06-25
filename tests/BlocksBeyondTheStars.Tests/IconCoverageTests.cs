// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Task 4 asset coverage: every pickup-able item and buildable ship module must resolve to a real
/// icon, mirroring the client's IconResolver order — a block-backed material reuses its in-game atlas
/// tile, everything else needs a generated <c>Resources/icons/item_&lt;key&gt;.png</c>. This guards
/// against an item shipping with only the crude procedural-glyph fallback.
/// </summary>
public sealed class IconCoverageTests
{
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private bool HasGeneratedIcon(string key)
        => File.Exists(Path.Combine(TestPaths.ClientIconsDir(), $"item_{key}.png"));

    /// <summary>A material backed by a block surfaces that block's atlas tile, so it needs no PNG.</summary>
    private bool IsBlockBacked(string key)
    {
        var item = _content.GetItem(key);
        if (item == null)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(item.PlacesBlock))
        {
            return true;
        }

        return _content.GetBlock(key) != null;
    }

    [Fact]
    public void EveryItem_ResolvesToAnIcon_BlockTileOrGeneratedPng()
    {
        var missing = _content.Items.Keys
            .Where(key => !IsBlockBacked(key) && !HasGeneratedIcon(key))
            .OrderBy(k => k)
            .ToList();

        Assert.True(missing.Count == 0, "Items without any icon (need item_<key>.png): " + string.Join(", ", missing));
    }

    [Fact]
    public void EveryShipModule_HasAGeneratedIcon()
    {
        var missing = _content.ShipModules.Keys
            .Where(key => !HasGeneratedIcon(key))
            .OrderBy(k => k)
            .ToList();

        Assert.True(missing.Count == 0, "Ship modules without an icon (need item_<key>.png): " + string.Join(", ", missing));
    }

    [Fact]
    public void ToxicConsumables_AreFlaggedForTheGreenTint()
    {
        // The green poison tint keys off ConsumeHealth < 0; make sure the toxic items still carry that.
        var toxic = new[] { "toxic_gland", "toxic_berries" };
        foreach (var key in toxic)
        {
            var def = _content.GetItem(key);
            Assert.NotNull(def);
            Assert.True(def!.ConsumeHealth < 0f, $"{key} must have negative ConsumeHealth to read as toxic.");
        }
    }
}
