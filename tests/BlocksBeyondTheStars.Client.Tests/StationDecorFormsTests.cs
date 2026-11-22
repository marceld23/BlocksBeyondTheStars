// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Linq;
using System.Numerics;
using Xunit;
using static BlocksBeyondTheStars.Client.StationDecorForms;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The station decor's forms (<see cref="StationDecorForms"/>) stay in the cell above their marker block, stand on
/// what is under them, step down toward the pilot on +Z and turn their screens to him.
/// </summary>
public sealed class StationDecorFormsTests
{
    private static readonly Vector3[] Corners =
        (from x in new[] { -0.5f, 0.5f } from y in new[] { -0.5f, 0.5f } from z in new[] { -0.5f, 0.5f } select new Vector3(x, y, z)).ToArray();

    private static float Lowest(FormBox b) => Corners.Min(c => PointToCell(b, c).Y);

    private static float Highest(FormBox b) => Corners.Max(c => PointToCell(b, c).Y);

    private static FormBox Only(Kind kind, Surface surface) => BoxesOf(kind).Single(b => b.Surface == surface);

    [Theory]
    [InlineData(Kind.Cockpit, 1f)]
    [InlineData(Kind.Terminal, 1f)]
    [InlineData(Kind.Medbay, 1.55f)] // the heal capsule has always reached y 1.5; its cap sits on it
    public void EveryBoxStaysInItsCell(Kind kind, float top)
    {
        foreach (var b in BoxesOf(kind))
        {
            foreach (var c in Corners)
            {
                var p = PointToCell(b, c);
                Assert.InRange(p.X, -0.5f, 0.5f);
                Assert.InRange(p.Z, -0.5f, 0.5f);
                Assert.InRange(p.Y, -0.01f, top);
            }
        }
    }

    [Theory]
    [InlineData(Kind.Cockpit)]
    [InlineData(Kind.Terminal)]
    [InlineData(Kind.Medbay)]
    public void TheFormStandsOnTheMarkerTop(Kind kind)
        => Assert.Equal(0f, BoxesOf(kind).Min(Lowest), 4);

    [Theory]
    [InlineData(Kind.Cockpit, 0.5f)]
    [InlineData(Kind.Terminal, 0.375f)]
    public void TheTiltedFrameRestsOnTheBlockBehindTheDeck(Kind kind, float restY)
    {
        var frame = BoxesOf(kind).Single(b => b.Surface == Surface.Housing && b.TiltX != 0f);
        Assert.InRange(Lowest(frame), restY - 0.01f, restY);
    }

    [Theory]
    [InlineData(Kind.Cockpit)]
    [InlineData(Kind.Terminal)]
    public void TheScreenFacesThePilotOnPlusZAndLeansBack(Kind kind)
    {
        var screen = Only(kind, Surface.Screen);
        var n = NormalToCell(screen, new Vector3(0f, 0f, 1f));
        Assert.True(n.Z > 0.9f, $"screen normal {n}");
        Assert.True(n.Y > 0.1f, $"screen normal {n}");

        // It lies on the frame's front face: the frame's centre, out along that normal by the two half depths.
        var frame = BoxesOf(kind).Single(b => b.Surface == Surface.Housing && b.TiltX == screen.TiltX);
        float along = Vector3.Dot(screen.Centre - frame.Centre, n);
        Assert.Equal((frame.Size.Z + screen.Size.Z) * 0.5f, along, 2);
        Assert.True(screen.Size.X < frame.Size.X && screen.Size.Y < frame.Size.Y);
    }

    [Fact]
    public void TheCockpitDeckStepsDownTowardThePilot()
    {
        var decks = BoxesOf(Kind.Cockpit).Where(b => b.Surface == Surface.Deck).OrderBy(b => b.Centre.Z).ToArray();
        Assert.Equal(2, decks.Length);
        var high = decks[0];
        var low = decks[1];
        Assert.True(Highest(low) < Highest(high), "the deck nearer the pilot is the lower one");
        var frame = BoxesOf(Kind.Cockpit).Single(b => b.Surface == Surface.Housing && b.TiltX != 0f);
        Assert.True(frame.Centre.Z < high.Centre.Z, "the screen frame stands behind the decks");
        Assert.True(Highest(high) < Highest(frame));
    }

    [Theory]
    [InlineData(Kind.Cockpit)]
    [InlineData(Kind.Terminal)]
    [InlineData(Kind.Medbay)]
    public void HousingAndDeckBoxesAreSixteenthsOfABlock(Kind kind)
    {
        foreach (var b in BoxesOf(kind).Where(b => b.Surface != Surface.Screen))
        {
            foreach (float s in new[] { b.Size.X, b.Size.Y, b.Size.Z })
            {
                float sixteenths = s * 16f;
                Assert.True(Math.Abs(sixteenths - MathF.Round(sixteenths)) < 1e-4f || Math.Abs((s * 32f) - MathF.Round(s * 32f)) < 1e-4f, $"size {s}");
            }
        }
    }

    [Fact]
    public void TheMedbayFormLeavesRoomForTheCapsule()
    {
        // The capsule: radius 0.275 about the vertical axis, from y 0 to 1.5. The collar bars stand outside that radius.
        foreach (var b in BoxesOf(Kind.Medbay).Where(b => b.Centre.Y > 0.125f && b.Centre.Y < 0.5f))
        {
            float inner = Math.Max(Math.Abs(b.Centre.X) - (b.Size.X * 0.5f), Math.Abs(b.Centre.Z) - (b.Size.Z * 0.5f));
            Assert.True(inner >= 0.275f, $"collar bar inner face at {inner}");
        }

        // The cap covers the capsule's tip.
        Assert.True(BoxesOf(Kind.Medbay).Max(Highest) >= 1.5f);
    }

    [Theory]
    [InlineData(Kind.Cockpit)]
    [InlineData(Kind.Terminal)]
    public void TheScreenShowsTheWholeTileOnItsFrontFaceOnly(Kind kind)
    {
        var screen = Only(kind, Surface.Screen);
        var flat = FlatCube(screen);
        Assert.Equal(1, Enumerable.Range(0, 6).Count(f => IsScreenFace(screen, flat, f)));
        var front = Enumerable.Range(0, 24).Where(i => flat.Normals[(i * 3) + 2] > 0.5f).ToArray();
        Assert.Equal(4, front.Length);
        Assert.Equal(0f, front.Min(i => flat.Uvs[i * 2]), 4);
        Assert.Equal(1f, front.Max(i => flat.Uvs[i * 2]), 4);
        Assert.Equal(0f, front.Min(i => flat.Uvs[(i * 2) + 1]), 4);
        Assert.Equal(1f, front.Max(i => flat.Uvs[(i * 2) + 1]), 4);

        // Seen from +Z the viewer's right is −X: the corner at −X carries u = 1, and the top v = 1 — not mirrored.
        int right = front.OrderBy(i => flat.Positions[i * 3]).First();
        Assert.Equal(1f, flat.Uvs[right * 2], 4);
        int top = front.OrderBy(i => flat.Positions[(i * 3) + 1]).Last();
        Assert.Equal(1f, flat.Uvs[(top * 2) + 1], 4);

        // Its other faces are mapped by size like the housing (they wear the dark metal).
        var side = Enumerable.Range(0, 24).Where(i => flat.Normals[i * 3] > 0.5f).ToArray();
        Assert.Equal(screen.Size.Z, side.Max(i => flat.Uvs[i * 2]) - side.Min(i => flat.Uvs[i * 2]), 4);
    }

    [Theory]
    [InlineData(Kind.Cockpit)]
    [InlineData(Kind.Terminal)]
    [InlineData(Kind.Medbay)]
    public void HousingAndDeckFacesAreMappedBySizeFromAnEdge(Kind kind)
    {
        foreach (var box in BoxesOf(kind).Where(b => b.Surface != Surface.Screen))
        {
            var flat = FlatCube(box);
            for (int face = 0; face < 6; face++)
            {
                var idx = Enumerable.Range(face * 4, 4).ToArray();
                var n = new Vector3(flat.Normals[face * 12], flat.Normals[(face * 12) + 1], flat.Normals[(face * 12) + 2]);
                float uSpan = idx.Max(i => flat.Uvs[i * 2]), vSpan = idx.Max(i => flat.Uvs[(i * 2) + 1]);
                Assert.Equal(0f, idx.Min(i => flat.Uvs[i * 2]), 4);
                Assert.Equal(0f, idx.Min(i => flat.Uvs[(i * 2) + 1]), 4);

                // A face shows one tile per block of its size: the two face spans are the box's two other sides.
                float across = Math.Abs(n.X) > 0.5f ? box.Size.Z : box.Size.X;
                float up = Math.Abs(n.Y) > 0.5f ? box.Size.Z : box.Size.Y;
                Assert.Equal(across, uSpan, 4);
                Assert.Equal(up, vSpan, 4);
            }
        }
    }

    [Theory]
    [InlineData(Kind.Cockpit)]
    [InlineData(Kind.Terminal)]
    public void DecksShowWholeButtonsOfTwoEighths(Kind kind)
    {
        foreach (var deck in BoxesOf(kind).Where(b => b.Surface == Surface.Deck))
        {
            Assert.Equal(0f, (deck.Size.X * 4f) - MathF.Round(deck.Size.X * 4f), 4);
            Assert.Equal(0f, (deck.Size.Z * 4f) - MathF.Round(deck.Size.Z * 4f), 4);
        }
    }

    [Fact]
    public void TheGlowScreenIsGreyStretchedAndFullyGlowing()
    {
        var tile = new byte[64 * 64 * 4];
        for (int o = 0; o < tile.Length; o += 4)
        {
            tile[o] = 10;
            tile[o + 1] = (byte)(o % 256 < 128 ? 30 : 140);
            tile[o + 2] = 160;
            tile[o + 3] = 255;
        }

        var glow = GlowScreenPixels(tile, 0.2f)!;
        Assert.Equal(tile.Length, glow.Length);
        Assert.Equal(255, glow.Where((_, i) => i % 4 == 0).Max(b => (int)b));
        Assert.True(glow.Where((_, i) => i % 4 == 0).Min(b => (int)b) >= 51);
        Assert.All(glow.Where((_, i) => i % 4 == 3), a => Assert.Equal(0, a));
        Assert.Null(GlowScreenPixels(null, 0.2f));
    }
}
