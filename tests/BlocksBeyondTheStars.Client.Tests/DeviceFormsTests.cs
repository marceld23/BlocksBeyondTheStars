// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Text;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using Xunit.Abstractions;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// Device forms (#3936): the forms devices are drawn with inside their cube. Every form of the table must
/// stay inside the cell, be free of overlaps, stand on the floor with nothing floating, keep to the freeform budget and
/// name only slots and tiles that exist; the turn that puts the form's front on the cube's front picture is pinned here.
/// </summary>
public sealed class DeviceFormsTests
{
    private readonly ITestOutputHelper _out;

    public DeviceFormsTests(ITestOutputHelper output) => _out = output;

    public static IEnumerable<object[]> FormKeys() => DeviceForms.Table.Keys.OrderBy(k => k, StringComparer.Ordinal).Select(k => new object[] { k });

    [Theory]
    [MemberData(nameof(FormKeys))]
    public void EveryForm_IsValid(string key)
    {
        var form = DeviceForms.Table[key];
        Assert.Equal(key, form.Key);
        Assert.Empty(DeviceForms.Problems(form));
        Assert.InRange(form.Boxes.Count, 3, 14);
    }

    [Theory]
    [MemberData(nameof(FormKeys))]
    public void EveryForm_DrawsARealBlock_AndNamesTilesThatExist(string key)
    {
        var content = ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir());
        Assert.NotNull(content.GetBlock(key));
        string res = Path.Combine(ClientTestPaths.RepoRoot(), "client", "Assets", "Resources");
        foreach (string tile in DeviceForms.Table[key].TileKeys())
        {
            bool exists = content.GetBlock(tile) != null
                || File.Exists(Path.Combine(res, "textures", tile + ".bytes"));
            Assert.True(exists, $"{key}: tile '{tile}' is neither a block nor a bundled texture");
        }
    }

    [Theory]
    [InlineData("workbench")]   // workshop
    [InlineData("heal_tank")]   // medbay
    [InlineData("data_cache")]  // cockpit and console of the layout ships
    [InlineData("ship_helm")]   // cockpit of a ship the player built
    public void StationMarkers_KeepTheirWholeTopAtFullHeight(string key)
        => Assert.True(DeviceForms.KeepsFullTop(DeviceForms.Table[key]), key + " carries a station's decor on its top");

    [Fact]
    public void Validation_FindsWhatIsWrong()
    {
        var own = new[] { DeviceFormSlot.Own() };
        DeviceForm F(params DeviceFormBox[] boxes) => new("x", boxes, own);
        Assert.Contains(DeviceForms.Problems(F(new DeviceFormBox(0, 0, 0, 9, 1, 1, 1))), p => p.Contains("leaves the cell"));
        Assert.Contains(DeviceForms.Problems(F(new DeviceFormBox(0, 0, 0, 0, 1, 1, 1))), p => p.Contains("no size"));
        Assert.Contains(DeviceForms.Problems(F(new DeviceFormBox(0, 0, 0, 4, 4, 4, 1), new DeviceFormBox(3, 3, 3, 8, 8, 8, 1))),
            p => p.Contains("overlap"));
        Assert.Contains(DeviceForms.Problems(F(new DeviceFormBox(0, 0, 0, 8, 1, 8, 2))), p => p.Contains("slot 2"));
        Assert.Contains(DeviceForms.Problems(F(new DeviceFormBox(0, 1, 0, 8, 2, 8, 1))), p => p.Contains("floor"));
        Assert.Contains(DeviceForms.Problems(F(new DeviceFormBox(0, 0, 0, 8, 1, 8, 1), new DeviceFormBox(0, 3, 0, 8, 4, 8, 1))),
            p => p.Contains("floats"));
        // Touching only along an edge holds nothing.
        Assert.Contains(DeviceForms.Problems(F(new DeviceFormBox(0, 0, 0, 4, 4, 4, 1), new DeviceFormBox(4, 4, 0, 8, 8, 4, 1))),
            p => p.Contains("floats"));
        var empty = new DeviceForm("x", new[] { new DeviceFormBox(0, 0, 0, 8, 8, 8, 1) },
            new[] { DeviceFormSlot.All(DeviceFormTile.Slice(" ")) });
        Assert.Contains(DeviceForms.Problems(empty), p => p.Contains("empty tile key"));
        var many = Enumerable.Range(0, 49).Select(i => new DeviceFormBox(i % 8, 0, i / 8, (i % 8) + 1, 1, (i / 8) + 1, 1)).ToArray();
        Assert.Contains(DeviceForms.Problems(F(many)), p => p.Contains("more than 48"));
    }

    [Theory]
    [MemberData(nameof(FormKeys))]
    public void FromVoxels_ReadsATableFormBack(string key)
    {
        var form = DeviceForms.Table[key];
        string voxels = Rasterise(form);
        var back = DeviceForms.FromVoxels(key, voxels, form.Slots.ToArray());
        Assert.Equal(voxels, Rasterise(back));
        Assert.Empty(DeviceForms.Problems(back));
        Assert.True(back.Boxes.Count <= DeviceForms.MaxBoxes);
    }

    [Fact]
    public void FromVoxels_TakesTheSmallGridAndRefusesOtherLengths()
    {
        var cells = new char[64];
        for (int i = 0; i < cells.Length; i++)
        {
            int y = i / 16;
            cells[i] = y == 0 ? '2' : y == 1 ? '1' : '0'; // a slot-2 floor layer under a slot-1 layer
        }

        var form = DeviceForms.FromVoxels("x", new string(cells), DeviceFormSlot.Own(), DeviceFormSlot.Own());
        Assert.Equal(2, form.Boxes.Count);
        Assert.Contains(form.Boxes, b => b.Slot == 2 && b.Y0 == 0 && b.Y1 == 2 && b.X1 == 8 && b.Z1 == 8);
        Assert.Contains(form.Boxes, b => b.Slot == 1 && b.Y0 == 2 && b.Y1 == 4);
        Assert.Throws<ArgumentException>(() => DeviceForms.FromVoxels("x", "123"));
    }

    [Fact]
    public void Turns_ComeBackAfterFour_AndStayInTheCell()
    {
        var b = new DeviceFormBox(1, 2, 0, 3, 5, 6, 1);
        Assert.Equal(b, DeviceFormFaces.Turn(b, 4));
        for (int yaw = 0; yaw < 4; yaw++)
        {
            var t = DeviceFormFaces.Turn(b, yaw);
            Assert.True(t.X0 >= 0 && t.X1 <= 8 && t.Z0 >= 0 && t.Z1 <= 8 && t.X1 > t.X0 && t.Z1 > t.Z0);
            Assert.Equal((b.X1 - b.X0) * (b.Z1 - b.Z0), (t.X1 - t.X0) * (t.Z1 - t.Z0));
        }

        for (int dir = 0; dir < 6; dir++)
        {
            Assert.Equal(dir, DeviceFormFaces.TurnDir(dir, 4));
        }
    }

    [Theory]
    [InlineData(5)] // −Z
    [InlineData(2)] // +X
    [InlineData(4)] // +Z
    [InlineData(3)] // −X
    public void TheFormsFront_LandsOnTheCubesFront(int front)
    {
        // A plate across the form's front: after the turn its front face must lie on the cube face that shows the
        // front picture, and be the only face there.
        var form = new DeviceForm("x", new[] { new DeviceFormBox(0, 0, 0, 8, 8, 1, 1) }, new[] { DeviceFormSlot.Own() });
        var quads = DeviceFormFaces.Build(form, DeviceFormFaces.YawForFront(front));
        var fronts = quads.Where(q => q.Side == FaceSide.Front).ToList();
        Assert.Single(fronts);
        Assert.Equal(front, fronts[0].Dir);
        Assert.True(fronts[0].Boundary);
    }

    [Fact]
    public void AFaceUnderAnotherBox_IsLeftOut()
    {
        // The tank's column top lies under its cap and its foot on the base plate: neither is drawn.
        var tank = DeviceForms.Table["heal_tank"];
        var quads = DeviceFormFaces.Build(tank, 0);
        Assert.DoesNotContain(quads, q => q.Dir == 0 && q.Y0 == 7 && q.X0 == 2 && q.X1 == 6);
        Assert.DoesNotContain(quads, q => q.Dir == 1 && q.Y0 == 1 && q.X0 == 2 && q.X1 == 6);
        Assert.Contains(quads, q => q.Dir == 0 && q.Y0 == 8 && q.Boundary); // the cap's top stays
    }

    [Fact]
    public void TriangleCounts_StayWithinTwiceTheBoxBudget()
    {
        var report = new StringBuilder();
        int worst = 0;
        foreach (var form in DeviceForms.Table.Values.OrderBy(f => f.Key, StringComparer.Ordinal))
        {
            int faces = DeviceFormFaces.Build(form, 0).Count;
            Assert.Equal(faces, DeviceFormFaces.Build(form, 3).Count);
            report.AppendLine($"{form.Key}: {form.Boxes.Count} boxes, {faces} faces, {faces * 2} triangles at most");
            worst = Math.Max(worst, faces * 2);
        }

        _out.WriteLine(report.ToString());
        Assert.InRange(worst, 1, 12 * 14);
    }

    private static string Rasterise(DeviceForm form)
    {
        var cells = Enumerable.Repeat('0', 512).ToArray();
        foreach (var b in form.Boxes)
        {
            for (int y = b.Y0; y < b.Y1; y++)
            {
                for (int z = b.Z0; z < b.Z1; z++)
                {
                    for (int x = b.X0; x < b.X1; x++)
                    {
                        cells[CustomShape.IndexOf(x, y, z, 8)] = "0123456789abcdef"[b.Slot];
                    }
                }
            }
        }

        return new string(cells);
    }
}
