// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.World;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// A device drawn as a form remembers the way it was placed whenever its form is not the same from all four sides. A
/// device without a stored front turns its form toward the first open side (<see cref="CubeFacing.DeriveFront"/>), so it
/// would face away from the player who placed it and could turn when someone builds beside it. Such a device declares
/// <c>facing</c> in data/blocks.json: the server stores the front at placement and the form turns to it. A form that a
/// quarter turn maps onto itself — the same cells with the same slots, and the same tile on its front faces as on its
/// sides — needs none: no derived front can be told apart from any other.
/// </summary>
public sealed class DeviceFormFrontTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir());

    public static IEnumerable<object[]> FormKeys() => DeviceForms.Table.Keys.OrderBy(k => k, StringComparer.Ordinal).Select(k => new object[] { k });

    [Theory]
    [MemberData(nameof(FormKeys))]
    public void AFormThatIsNotTheSameFromAllSides_RemembersItsFront(string key)
    {
        var def = Content.GetBlock(key);
        Assert.NotNull(def);
        if (def!.Facing == null)
        {
            Assert.True(SameFromAllFourSides(DeviceForms.Table[key]),
                $"{key}: its form differs between its sides, so it needs \"facing\" in data/blocks.json");
        }
    }

    [Theory]
    [InlineData("workbench")]        // drawers and legs set back under the worktop's front edge
    [InlineData("crate")]            // the skids run from front to back
    [InlineData("wood_crate")]
    [InlineData("transport_crate")]
    [InlineData("thumper")]          // the guide columns stand left and right
    public void TheDevicesWithADirection_TurnToThePlayerWhoPlacesThem(string key)
    {
        Assert.False(SameFromAllFourSides(DeviceForms.Table[key]));
        Assert.Equal(CubeFacing.Toward, Content.GetBlock(key)!.Facing);
    }

    [Theory]
    [InlineData("heal_tank")]
    [InlineData("clone_tank")]
    [InlineData("capsule_tank")]
    [InlineData("capsule_tank_open")]
    [InlineData("algae_tank")]
    [InlineData("power_relay")]
    [InlineData("hydro_tray")]
    [InlineData("stretcher")]
    [InlineData("radio_beacon")]
    [InlineData("decontaminator")]
    [InlineData("machine_block")]
    [InlineData("data_cache")]
    [InlineData("ship_helm")]
    public void TheDevicesAlikeOnAllSides_KeepNoFront(string key)
    {
        Assert.True(SameFromAllFourSides(DeviceForms.Table[key]));
        Assert.Null(Content.GetBlock(key)!.Facing);
    }

    /// <summary>True when a quarter turn maps the form onto itself, cell by cell and slot by slot, and no slot dresses its
    /// front faces differently from its side faces.</summary>
    private static bool SameFromAllFourSides(DeviceForm form)
    {
        foreach (var slot in form.Slots)
        {
            if (!SameTile(slot.Front, slot.Side))
            {
                return false;
            }
        }

        return Cells(form, 0).SetEquals(Cells(form, 1));
    }

    private static bool SameTile(DeviceFormTile a, DeviceFormTile b)
        => a.Key == b.Key && a.Mode == b.Mode && a.U0 == b.U0 && a.V0 == b.V0 && a.U1 == b.U1 && a.V1 == b.V1;

    /// <summary>Every eighth-cell the form fills after <paramref name="yaw"/> quarter turns, with the slot that dresses it.</summary>
    private static HashSet<(int X, int Y, int Z, int Slot)> Cells(DeviceForm form, int yaw)
    {
        var cells = new HashSet<(int X, int Y, int Z, int Slot)>();
        foreach (var box in form.Boxes)
        {
            var b = DeviceFormFaces.Turn(box, yaw);
            for (int x = b.X0; x < b.X1; x++)
            {
                for (int y = b.Y0; y < b.Y1; y++)
                {
                    for (int z = b.Z0; z < b.Z1; z++)
                    {
                        cells.Add((x, y, z, b.Slot));
                    }
                }
            }
        }

        return cells;
    }
}
