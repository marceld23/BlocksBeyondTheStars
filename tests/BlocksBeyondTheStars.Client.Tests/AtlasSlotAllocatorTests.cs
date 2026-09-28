// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The slot bands of the block texture atlas and the allocator that deals slots inside a band (#1952). The one
/// hard rule: the frames of an animated texture lie in ONE atlas row, because the shader walks them by adding a
/// tile width to U — a run that wrapped into the next row would show an unrelated tile.
/// </summary>
public sealed class AtlasSlotAllocatorTests
{
    [Fact]
    public void Bands_TileTheAtlasWithoutGapsOrOverlap()
    {
        Assert.Equal(AtlasBands.BlockEnd, AtlasBands.ExtraStart);
        Assert.Equal(AtlasBands.ExtraEnd, AtlasBands.DynamicStart);
        Assert.Equal(AtlasBands.DynamicEnd, AtlasBands.DerivedStart);
        Assert.True(AtlasBands.DerivedStart < AtlasBands.Cols * AtlasBands.Rows);
    }

    [Fact]
    public void BlockBand_IsWhatContentValidationEnforces()
    {
        // Shared cannot reference the client, so the number lives twice — this keeps the two in step.
        Assert.Equal(AtlasBands.BlockEnd, GameContent.AtlasTileCapacity);
    }

    [Fact]
    public void DerivedBand_HoldsTodaysVariantsAndLogCaps()
    {
        // 8 ground + 8 flora blocks × 2 variants, plus the two log end-grain tiles.
        Assert.True(AtlasBands.Cols * AtlasBands.Rows - AtlasBands.DerivedStart >= (16 * 2) + 2);
    }

    [Fact]
    public void SingleSlots_AreDealtInOrder()
    {
        var band = new AtlasSlotAllocator(AtlasBands.ExtraStart, AtlasBands.ExtraEnd);

        Assert.Equal(400, band.Allocate());
        Assert.Equal(401, band.Allocate());
        Assert.Equal(AtlasBands.ExtraEnd - AtlasBands.ExtraStart - 2, band.Free);
    }

    [Fact]
    public void ARun_NeverCrossesARowEnd()
    {
        var band = new AtlasSlotAllocator(AtlasBands.DynamicStart, AtlasBands.DynamicEnd);

        // Fill row 16 (slots 512..543) up to its last three slots, then ask for a strip of eight.
        Assert.Equal(512, band.Allocate(29));
        int strip = band.Allocate(8);

        Assert.Equal(544, strip); // the next row — not 541, which would wrap
        Assert.Equal(strip / AtlasBands.Cols, (strip + 7) / AtlasBands.Cols);
        // The three slots left at the row end are still usable for a shorter run.
        Assert.Equal(541, band.Allocate(3));
    }

    [Fact]
    public void ReleasedRuns_AreReused_AndClearFreesEverything()
    {
        var band = new AtlasSlotAllocator(AtlasBands.DynamicStart, AtlasBands.DynamicEnd);
        int a = band.Allocate(4);
        int b = band.Allocate(4);
        band.Release(a, 4);

        Assert.Equal(a, band.Allocate(4));
        Assert.NotEqual(a, b);

        band.Clear();
        Assert.Equal(AtlasBands.DynamicEnd - AtlasBands.DynamicStart, band.Free);
    }

    [Fact]
    public void AFullBand_AnswersMinusOne_InsteadOfLeavingTheBand()
    {
        var band = new AtlasSlotAllocator(992, 996); // four slots at the start of a row

        Assert.Equal(992, band.Allocate(4));
        Assert.Equal(-1, band.Allocate());
        Assert.Equal(-1, band.Allocate(0));
        Assert.Equal(-1, band.Allocate(AtlasBands.Cols + 1)); // longer than a row can ever be
    }

    [Fact]
    public void TheDynamicBand_FitsTheWorldTextureBudget()
    {
        // World textures may use up to 478 slots in total (frames count); every strip of eight must find a row.
        var band = new AtlasSlotAllocator(AtlasBands.DynamicStart, AtlasBands.DynamicEnd);
        int strips = 0;
        while (band.Allocate(8) >= 0)
        {
            strips++;
        }

        Assert.True(strips >= 56, $"only {strips} eight-frame strips fit the dynamic band");
    }

    [Fact]
    public void FaceTiles_AreDealtFromTheExtrasBand_InKeyOrder()
    {
        // #2124: the atlas paints the face tiles and the mesher maps faces onto them; both deal the slots from the same
        // key list, so the order must be a pure function of the keys (duplicates and empties collapse).
        var slots = FaceTileBand.Deal(new[] { "face_b", "face_a", "", "face_b", "face_c" }, out var overflow);

        Assert.Empty(overflow);
        Assert.Equal(AtlasBands.ExtraStart, slots["face_a"]);
        Assert.Equal(AtlasBands.ExtraStart + 1, slots["face_b"]);
        Assert.Equal(AtlasBands.ExtraStart + 2, slots["face_c"]);
        Assert.Equal(3, slots.Count);
    }

    [Fact]
    public void FaceTiles_ThatDoNotFit_AreReportedNotDropped()
    {
        var keys = Enumerable.Range(0, FaceTileBand.Capacity + 3).Select(i => $"face_{i:D4}").ToList();
        var slots = FaceTileBand.Deal(keys, out var overflow);

        Assert.Equal(FaceTileBand.Capacity, slots.Count);
        Assert.Equal(3, overflow.Count);
        Assert.All(slots.Values, s => Assert.InRange(s, AtlasBands.ExtraStart, AtlasBands.ExtraEnd - 1));
    }

    [Fact]
    public void ShippedFaceTiles_FitTheExtrasBand()
    {
        // The content a player runs must never overflow the band: an overflowing face would silently show its block's
        // own tile (the atlas logs an error, but only in the player's log).
        var content = ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir());
        var keys = BlockFaceTextures.TextureOnlyKeys(content.Blocks.Values, key => content.GetBlock(key) != null);
        FaceTileBand.Deal(keys, out var overflow);

        Assert.NotEmpty(keys);
        Assert.Empty(overflow);
    }
}
