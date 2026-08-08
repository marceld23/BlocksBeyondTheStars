// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The player-designed block form FORMAT (#842): the micro-voxel bitmap, its validation, the greedy box
/// merge the renderer and the server's budget check share, and the custom half of the shape descriptor.
/// Deliberately free of server/Unity setup — this is the layer everything else builds on.
/// </summary>
public sealed class CustomShapeTests
{
    private static string Empty(int grid) => new string('0', grid * grid * grid);

    /// <summary>A bitmap with the given micro cells filled ('1'), everything else empty.</summary>
    private static string With(int grid, params (int X, int Y, int Z)[] cells)
    {
        var chars = Empty(grid).ToCharArray();
        foreach (var (x, y, z) in cells)
        {
            chars[CustomShape.IndexOf(x, y, z, grid)] = '1';
        }

        return new string(chars);
    }

    [Theory]
    [InlineData(CustomShape.GridSmall)]
    [InlineData(CustomShape.GridLarge)]
    public void GridOf_ReadsTheSideLengthFromTheStringLength(int grid)
    {
        Assert.Equal(grid, CustomShape.GridOf(Empty(grid)));
    }

    [Fact]
    public void GridOf_RejectsAnyOtherLength()
    {
        Assert.Equal(0, CustomShape.GridOf(null));
        Assert.Equal(0, CustomShape.GridOf(string.Empty));
        Assert.Equal(0, CustomShape.GridOf(new string('0', 100)));
    }

    [Fact]
    public void IsValidVoxels_AcceptsAFormWithFilledAndEmptyCells()
    {
        Assert.True(CustomShape.IsValidVoxels(With(CustomShape.GridSmall, (0, 0, 0))));
        Assert.True(CustomShape.IsValidVoxels(With(CustomShape.GridLarge, (3, 4, 5), (3, 4, 6))));
    }

    [Fact]
    public void IsValidVoxels_RejectsWrongLengthAndNonHex()
    {
        Assert.False(CustomShape.IsValidVoxels(new string('1', 63)));
        var bad = With(CustomShape.GridSmall, (0, 0, 0)).ToCharArray();
        bad[5] = 'x';
        Assert.False(CustomShape.IsValidVoxels(new string(bad)));
        bad[5] = 'A'; // uppercase: the wire form is normalized before validation, so this is malformed here
        Assert.False(CustomShape.IsValidVoxels(new string(bad)));
    }

    [Fact]
    public void IsValidVoxels_RejectsNothingAndRejectsAPlainCube()
    {
        Assert.False(CustomShape.IsValidVoxels(Empty(CustomShape.GridSmall)));           // nothing at all
        Assert.False(CustomShape.IsValidVoxels(new string('1', CustomShape.SmallChars))); // that IS a cube
    }

    [Fact]
    public void IsValidVoxels_AcceptsReservedPaletteValuesAsFilled()
    {
        // 2..f are reserved for a later per-micro-cell tint; today they must simply read as "filled" so a
        // form authored by a newer client never fails validation on an older server.
        var chars = Empty(CustomShape.GridSmall).ToCharArray();
        chars[0] = 'f';
        string voxels = new string(chars);
        Assert.True(CustomShape.IsValidVoxels(voxels));
        Assert.True(CustomShape.IsFilled(voxels, 0, 0, 0, CustomShape.GridSmall));
    }

    [Fact]
    public void IsFilled_TreatsOutOfRangeAsEmpty()
    {
        string voxels = With(CustomShape.GridSmall, (0, 0, 0));
        Assert.True(CustomShape.IsFilled(voxels, 0, 0, 0, CustomShape.GridSmall));
        Assert.False(CustomShape.IsFilled(voxels, -1, 0, 0, CustomShape.GridSmall));
        Assert.False(CustomShape.IsFilled(voxels, CustomShape.GridSmall, 0, 0, CustomShape.GridSmall));
    }

    [Fact]
    public void Merge_SingleCell_IsOneUnitBox()
    {
        var boxes = CustomShape.Merge(With(CustomShape.GridSmall, (1, 2, 3)));
        var box = Assert.Single(boxes);
        Assert.Equal((1, 2, 3, 2, 3, 4), (box.X0, box.Y0, box.Z0, box.X1, box.Y1, box.Z1));
        Assert.Equal(CustomShape.GridSmall, box.Grid);
    }

    [Fact]
    public void Merge_FullGrid_CollapsesToOneBox()
    {
        // Not a legal FORM (that is a cube), but the merge itself must still reach the optimum — this is the
        // property that keeps a big blobby form cheap.
        var boxes = CustomShape.Merge(new string('1', CustomShape.LargeChars));
        var box = Assert.Single(boxes);
        Assert.Equal((0, 0, 0), (box.X0, box.Y0, box.Z0));
        Assert.Equal((CustomShape.GridLarge, CustomShape.GridLarge, CustomShape.GridLarge), (box.X1, box.Y1, box.Z1));
    }

    [Fact]
    public void Merge_GrowsAlongXThenZThenY()
    {
        // A 2×2×2 corner block: one box, not eight.
        var cells = new List<(int, int, int)>();
        for (int y = 0; y < 2; y++)
        {
            for (int z = 0; z < 2; z++)
            {
                for (int x = 0; x < 2; x++)
                {
                    cells.Add((x, y, z));
                }
            }
        }

        var boxes = CustomShape.Merge(With(CustomShape.GridSmall, cells.ToArray()));
        var box = Assert.Single(boxes);
        Assert.Equal((2, 2, 2), (box.X1, box.Y1, box.Z1));
    }

    [Fact]
    public void Merge_IsDeterministic()
    {
        string voxels = With(CustomShape.GridLarge, (0, 0, 0), (1, 0, 0), (0, 1, 0), (7, 7, 7), (3, 3, 3), (4, 3, 3));
        var a = CustomShape.Merge(voxels);
        var b = CustomShape.Merge(voxels);
        Assert.Equal(a, b);
    }

    [Fact]
    public void Merge_CoversEveryFilledCellExactlyOnce()
    {
        string voxels = With(CustomShape.GridSmall, (0, 0, 0), (1, 0, 0), (2, 0, 0), (0, 1, 0), (3, 3, 3), (3, 3, 2));
        var covered = new HashSet<(int, int, int)>();
        foreach (var box in CustomShape.Merge(voxels))
        {
            for (int y = box.Y0; y < box.Y1; y++)
            {
                for (int z = box.Z0; z < box.Z1; z++)
                {
                    for (int x = box.X0; x < box.X1; x++)
                    {
                        Assert.True(CustomShape.IsFilled(voxels, x, y, z, CustomShape.GridSmall), "box covers an empty cell");
                        Assert.True(covered.Add((x, y, z)), "cell covered twice");
                    }
                }
            }
        }

        int filled = voxels.Count(c => c != '0');
        Assert.Equal(filled, covered.Count);
    }

    [Fact]
    public void FitsBudget_AcceptsABlobAndRejectsACheckerboard()
    {
        // A solid-ish blob merges to a handful of boxes …
        var blob = new List<(int, int, int)>();
        for (int y = 0; y < 3; y++)
        {
            for (int z = 0; z < 3; z++)
            {
                for (int x = 0; x < 3; x++)
                {
                    blob.Add((x, y, z));
                }
            }
        }

        Assert.True(CustomShape.FitsBudget(With(CustomShape.GridSmall, blob.ToArray())));

        // … while a checkerboard is the worst case the budget exists to keep out of the collider.
        var chars = Empty(CustomShape.GridLarge).ToCharArray();
        for (int y = 0; y < CustomShape.GridLarge; y++)
        {
            for (int z = 0; z < CustomShape.GridLarge; z++)
            {
                for (int x = 0; x < CustomShape.GridLarge; x++)
                {
                    if ((x + y + z) % 2 == 0)
                    {
                        chars[CustomShape.IndexOf(x, y, z, CustomShape.GridLarge)] = '1';
                    }
                }
            }
        }

        string checkerboard = new string(chars);
        Assert.True(CustomShape.IsValidVoxels(checkerboard));
        Assert.False(CustomShape.FitsBudget(checkerboard));
        Assert.True(CustomShape.Merge(checkerboard).Count > CustomShape.MaxBoxes);
    }

    [Fact]
    public void BoxBudget_BoundsTheGeometryAndColliderCostPerCell()
    {
        // What the budget actually buys, in the numbers the mesher will emit: every merged box contributes
        // 6 quads = 24 vertices = 12 collider triangles, so the worst legal form costs a bounded multiple of
        // a plain cube (2 collider triangles per visible face). This is the arithmetic the 48 is chosen
        // against; the frame-time half is a playtest measurement, not something a unit test can claim.
        const int quadsPerBox = 6, vertsPerBox = 24, colliderTrisPerBox = 12;
        Assert.Equal(288, CustomShape.MaxBoxes * quadsPerBox);
        Assert.Equal(1152, CustomShape.MaxBoxes * vertsPerBox);
        Assert.Equal(576, CustomShape.MaxBoxes * colliderTrisPerBox);

        // And a form that a player is likely to build — a blobby arch — stays far below the ceiling.
        var arch = new List<(int, int, int)>();
        for (int y = 0; y < CustomShape.GridLarge; y++)
        {
            for (int z = 2; z < 6; z++)
            {
                for (int x = 0; x < CustomShape.GridLarge; x++)
                {
                    bool leg = x < 2 || x >= CustomShape.GridLarge - 2;
                    if (leg || y >= CustomShape.GridLarge - 2)
                    {
                        arch.Add((x, y, z));
                    }
                }
            }
        }

        int boxes = CustomShape.Merge(With(CustomShape.GridLarge, arch.ToArray())).Count;
        Assert.True(boxes <= 8, $"a plain arch should merge to a handful of boxes, got {boxes}");
    }

    [Fact]
    public void Silhouette_ProjectsAlongZ()
    {
        string voxels = With(CustomShape.GridSmall, (1, 2, 0), (1, 2, 3), (0, 0, 2));
        var mask = CustomShape.Silhouette(voxels, out int grid);
        Assert.Equal(CustomShape.GridSmall, grid);
        Assert.True(mask[2 * grid + 1]); // (x=1, y=2) is covered by two cells at different depths
        Assert.True(mask[0 * grid + 0]);
        Assert.False(mask[3 * grid + 3]);
    }

    [Fact]
    public void ShapeCode_CustomIdsLiveAboveTheBuiltInForms()
    {
        Assert.Equal(ShapeCode.Count, ShapeCode.FirstCustom);
        Assert.False(ShapeCode.IsCustomShape(ShapeCode.Count - 1));
        Assert.True(ShapeCode.IsCustomShape(ShapeCode.FirstCustom));
        Assert.True(ShapeCode.IsCustomShape(ShapeCode.LastCustom));
        Assert.False(ShapeCode.IsCustomShape(ShapeCode.LastCustom + 1));
        Assert.Equal(45, ShapeCode.MaxCustomShapes);
    }

    [Fact]
    public void ShapeCode_CustomFormSurvivesPackingWithOrientation()
    {
        int d = ShapeCode.Pack(ShapeCode.LastCustom, 3, 4);
        Assert.Equal(ShapeCode.LastCustom, ShapeCode.ShapeOf(d));
        Assert.Equal(3, ShapeCode.OrientationOf(d));
        Assert.Equal(4, ShapeCode.UpFaceOf(d));
        Assert.True(ShapeCode.IsCustomDescriptor(d));
        Assert.False(ShapeCode.IsCube(d));

        // …and alongside a paint design, which lives in higher bits.
        int painted = ShapeCode.WithDesign(d, 1234);
        Assert.Equal(ShapeCode.LastCustom, ShapeCode.ShapeOf(painted));
        Assert.Equal(1234, ShapeCode.DesignOf(painted));
    }

    [Fact]
    public void IsPlaceableShape_NeedsTheRegistryForCustomIds()
    {
        Assert.True(ShapeCode.IsPlaceableShape((int)BlockShape.Ramp, null));
        Assert.False(ShapeCode.IsPlaceableShape(ShapeCode.FirstCustom, null));
        Assert.False(ShapeCode.IsPlaceableShape(ShapeCode.FirstCustom, _ => false));
        Assert.True(ShapeCode.IsPlaceableShape(ShapeCode.FirstCustom, id => id == ShapeCode.FirstCustom));
        Assert.False(ShapeCode.IsPlaceableShape(ShapeCode.LastCustom + 1, _ => true));
    }

    // ── share codes (#846) ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void ShareCode_RoundTripsAFormWithItsName()
    {
        string voxels = With(CustomShape.GridLarge, (1, 1, 1), (2, 1, 1));
        string code = ShareCode.EncodeForm(voxels, "Justus' Bogen");

        Assert.True(ShareCode.TryDecodeForm(code, out string back, out string name));
        Assert.Equal(voxels, back);
        Assert.Equal("Justus' Bogen", name);
    }

    [Fact]
    public void ShareCode_SurvivesWhitespaceAroundIt()
    {
        // Codes travel through chat windows and forum posts, so they arrive with stray spaces or newlines.
        string code = ShareCode.EncodeForm(With(CustomShape.GridSmall, (0, 0, 0)), "A");
        Assert.True(ShareCode.TryDecodeForm("  " + code + "\n", out _, out _));
    }

    [Fact]
    public void ShareCode_RejectsGarbageAndTheWrongKind()
    {
        Assert.False(ShareCode.TryDecodeForm(null, out _, out _));
        Assert.False(ShareCode.TryDecodeForm(string.Empty, out _, out _));
        Assert.False(ShareCode.TryDecodeForm("hello", out _, out _));
        Assert.False(ShareCode.TryDecodeForm("BBTS1-F-not-base64!!", out _, out _));

        // A design code must not decode as a form and vice versa.
        string design = ShareCode.EncodeDesign(new string('3', 1024), "Muster");
        Assert.False(ShareCode.TryDecodeForm(design, out _, out _));
        Assert.True(ShareCode.TryDecodeDesign(design, 1024, out string pixels, out string name));
        Assert.Equal(1024, pixels.Length);
        Assert.Equal("Muster", name);
    }

    [Fact]
    public void ShareCode_AppliesTheSameValidationTheServerWould()
    {
        // An imported form must be one the game can actually render: over-budget and malformed payloads are
        // refused at the door, so the registry never sees them.
        var chars = Empty(CustomShape.GridLarge).ToCharArray();
        for (int y = 0; y < CustomShape.GridLarge; y++)
        {
            for (int z = 0; z < CustomShape.GridLarge; z++)
            {
                for (int x = 0; x < CustomShape.GridLarge; x++)
                {
                    if ((x + y + z) % 2 == 0)
                    {
                        chars[CustomShape.IndexOf(x, y, z, CustomShape.GridLarge)] = '1';
                    }
                }
            }
        }

        string overBudget = ShareCode.Encode(ShareCode.KindForm, new string(chars), "Zu fein");
        Assert.False(ShareCode.TryDecodeForm(overBudget, out _, out _));

        string malformed = ShareCode.Encode(ShareCode.KindForm, "zzzz", "Kaputt");
        Assert.False(ShareCode.TryDecodeForm(malformed, out _, out _));

        // …and a design code with the wrong bitmap length is refused too.
        Assert.False(ShareCode.TryDecodeDesign(ShareCode.EncodeDesign(new string('3', 64), "x"), 1024, out _, out _));
    }

    [Fact]
    public void ItemKey_CarriesACustomFormLikeAnyOtherShape()
    {
        string key = ItemKey.Compose("stone", 0, 0, ShapeCode.LastCustom);
        Assert.Equal(ShapeCode.LastCustom, ItemKey.Shape(key));
        Assert.Equal("stone", ItemKey.Base(key));

        string dyed = ItemKey.Compose("stone", 0x3f6fb0, 0, ShapeCode.FirstCustom);
        Assert.Equal(ShapeCode.FirstCustom, ItemKey.Shape(dyed));
        Assert.Equal(0x3f6fb0, ItemKey.Tint(dyed));
    }
}
