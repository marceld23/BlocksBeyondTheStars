// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Numerics;
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using Xunit.Abstractions;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// Device forms, soft edges (#3936): soft edges on the device forms. For every form of the table and every turn the chamfered
/// surface is closed, and dropping every face on the cell's border (what the culling may do) opens it only in the border
/// planes; every polygon stays in the cell, keeps its box and slot, and the cap leaves a form hard where the chamfer would
/// more than double its triangles. Width 0 is today's faces.
/// </summary>
public sealed class DeviceFormSoftTests
{
    private const float BevelAmount = 0.06f; // ChunkMesher.BevelAmount
    private static readonly float Width = ShapeChamfer.WidthFor(ShapeChamfer.DefaultShare, DeviceForms.Grid, BevelAmount);
    private readonly ITestOutputHelper _out;

    public DeviceFormSoftTests(ITestOutputHelper output) => _out = output;

    public static IEnumerable<object[]> FormKeys() => DeviceFormsTests.FormKeys();

    [Theory]
    [MemberData(nameof(FormKeys))]
    public void EveryForm_EveryTurn_IsClosed_InsideTheCell_AndKeepsItsSlots(string key)
    {
        var form = DeviceForms.Table[key];
        for (int yaw = 0; yaw < 4; yaw++)
        {
            var soft = DeviceFormSoft.Build(form, yaw, Width);
            var facets = soft.Select(f => f.Facet).ToList();
            ShapeChamferTests.AssertClosed(facets);
            ShapeChamferTests.AssertInsideCell(facets);
            foreach (var f in soft)
            {
                Assert.InRange(f.Box, 0, form.Boxes.Count - 1);
                Assert.Equal(form.Boxes[f.Box].Slot, f.Slot);
                Assert.True(f.Dir >= 0 && (f.Dirs & (1 << f.Dir)) != 0, $"{key}: the reference face is not one it looks to");
                Assert.True(!f.Boundary || f.Flat, $"{key}: a strip marked as a boundary face");
            }
        }
    }

    [Theory]
    [MemberData(nameof(FormKeys))]
    public void DroppingTheBoundaryFaces_OpensTheSurfaceOnlyInTheBorderPlanes(string key)
    {
        var form = DeviceForms.Table[key];
        for (int yaw = 0; yaw < 4; yaw++)
        {
            var inner = DeviceFormSoft.Build(form, yaw, Width).Where(f => !f.Boundary).Select(f => f.Facet).ToList();
            foreach (var (a, b) in ShapeChamferTests.OpenSegments(inner))
            {
                Assert.True(InBorderPlane(a, b), $"{key} turn {yaw}: open edge {a} -> {b} off the cell's border");
            }
        }
    }

    [Theory]
    [MemberData(nameof(FormKeys))]
    public void BoundaryFaces_LieOnTheCellFaceTheyLookTo(string key)
    {
        var form = DeviceForms.Table[key];
        foreach (var f in DeviceFormSoft.Build(form, 1, Width).Where(f => f.Boundary))
        {
            float plane = (f.Dir & 1) == 0 ? 1f : 0f;
            foreach (var p in new[] { f.Facet.A, f.Facet.B, f.Facet.C })
            {
                float c = f.Dir < 2 ? p.Y : f.Dir < 4 ? p.X : p.Z;
                Assert.Equal(plane, c, 5);
            }
        }
    }

    [Fact]
    public void WidthZero_IsTodaysFaces()
    {
        foreach (var form in DeviceForms.Table.Values)
        {
            Assert.Null(DeviceFormSoft.BuildAllTurns(form, 0f)); // null = the mesher draws DeviceFormFaces.Build as before
        }
    }

    [Fact]
    public void StripRule_TakesTheVerticalFace_TheFrontFirst()
    {
        // In the form's frame (yaw 0): top + front → front; top + side → side; front + right side → front; two sides → +X.
        Assert.Equal(5, DeviceFormSoft.ReferenceOf((1 << 0) | (1 << 5), 0));
        Assert.Equal(2, DeviceFormSoft.ReferenceOf((1 << 0) | (1 << 2), 0));
        Assert.Equal(5, DeviceFormSoft.ReferenceOf((1 << 2) | (1 << 5), 0));
        Assert.Equal(2, DeviceFormSoft.ReferenceOf((1 << 2) | (1 << 4), 0));
        // Turned once the form's front (−Z) looks to +X (2): a top/+X strip takes +X, the turned front.
        Assert.Equal(2, DeviceFormSoft.ReferenceOf((1 << 0) | (1 << 2), 1));
        Assert.Equal(5, DeviceFormSoft.FormDir(2, 1));
    }

    [Fact]
    public void Budget_PerForm_AndTheCap()
    {
        var lines = new List<string> { "form | hard faces / tris | soft polygons / tris (turn 0) | ratio | soft?" };
        foreach (var form in DeviceForms.Table.Values.OrderBy(f => f.Key, StringComparer.Ordinal))
        {
            int hardFaces = DeviceFormFaces.Build(form, 0).Count;
            var soft = DeviceFormSoft.Build(form, 0, Width);
            int softTris = DeviceFormSoft.Triangles(soft);
            double worst = Enumerable.Range(0, 4)
                .Max(yaw => DeviceFormSoft.Triangles(DeviceFormSoft.Build(form, yaw, Width)) / (2.0 * DeviceFormFaces.Build(form, yaw).Count));
            var all = DeviceFormSoft.BuildAllTurns(form, Width);
            Assert.Equal(worst <= DeviceFormSoft.MaxTriangleRatio, all != null);
            lines.Add($"{form.Key} | {hardFaces} / {2 * hardFaces} | {soft.Count} / {softTris} | {worst:0.00} | {(all != null ? "soft" : "capped: hard")}");
        }

        foreach (string line in lines)
        {
            _out.WriteLine(line);
        }
    }

    private static bool InBorderPlane(Vector3 a, Vector3 b)
    {
        static bool Same(float u, float v, float plane) => Math.Abs(u - plane) < 1e-5f && Math.Abs(v - plane) < 1e-5f;
        return Same(a.X, b.X, 0f) || Same(a.X, b.X, 1f) || Same(a.Y, b.Y, 0f) || Same(a.Y, b.Y, 1f) || Same(a.Z, b.Z, 0f) || Same(a.Z, b.Z, 1f);
    }
}
