// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.World;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The devices whose form inside the cube is not the same from all four sides — the workbench, the three crates and the
/// thumper — remember the way they were placed: they declare <c>"facing": "toward"</c>, so the server stores their front
/// (<see cref="CubeFacing"/>) and the form turns to the player who placed them instead of to the first open side.
/// <para>
/// A front changes which slot a cube face reads: the front face reads <c>front</c>, the other three <c>side</c>, where a
/// cube without a front reads <c>side</c> on all four. Without care that would put the workbench's top picture (its own
/// tile) onto its front. These tests restate the tile every face of the plain cube showed before the front was stored —
/// the cube still dresses icons, held blocks, painted devices, the far hull and the forms' own faces — and hold each face
/// to it for every front the block can have.
/// </para>
/// </summary>
public sealed class DeviceFrontTilesTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    /// <summary>Today's tiles: key, top, bottom, the four sides (a null slot reads as the block's own tile).</summary>
    public static TheoryData<string, string, string, string> TodaysTiles => new()
    {
        { "workbench", "workbench", "metal_panel", "face_workbench_side" },
        { "crate", "face_crate_top", "face_crate_top", "crate" },
        { "wood_crate", "face_wood_crate_top", "face_wood_crate_top", "wood_crate" },
        { "transport_crate", "face_transport_crate_top", "face_transport_crate_top", "transport_crate" },
        { "thumper", "face_industrial_top", "metal_panel", "thumper" },
    };

    [Theory]
    [MemberData(nameof(TodaysTiles))]
    public void TheStoredFront_KeepsEveryCubeFaceAsItWas(string key, string top, string bottom, string side)
    {
        var def = Content.GetBlock(key);
        Assert.NotNull(def);
        Assert.Equal(CubeFacing.Toward, def!.Facing);

        // Mesher face order: +Y, −Y, +X, −X, +Z, −Z.
        string[] expected = { top, bottom, side, side, side, side };
        Assert.Equal(expected, Shown(def, -1)); // a cell without a stored front: placed before, or by a layout
        for (int front = 2; front <= 5; front++)
        {
            Assert.Equal(expected, Shown(def, front));
        }
    }

    /// <summary>The tile key each cube face shows for a front (-1 = none), the way the client's cube table resolves it.</summary>
    private static string[] Shown(BlockDefinition def, int front)
    {
        var shown = new string[6];
        for (int face = 0; face < shown.Length; face++)
        {
            var slot = BlockFaceTextures.Resolve(def.Faces, ShapePart.Body, CubeFacing.SideOf(face, front));
            shown[face] = slot?.Tile ?? def.Key;
        }

        return shown;
    }
}
