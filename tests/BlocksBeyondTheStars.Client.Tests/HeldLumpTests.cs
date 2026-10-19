// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using System.Numerics;
using BlocksBeyondTheStars.Client;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>#2838: the ores (and silicate and carbon) have no icon and showed the bare fist in the hand, while the quick bar
/// shows them as their block. Now they are held as a small lump of that block, its shape fixed per item.</summary>
public sealed class HeldLumpTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir());
    private static readonly HeldItemShapes.Rgb Grey = new(0.5f, 0.5f, 0.5f);

    private static List<ItemDefinition> LumpItems() => Content.Items.Values
        .Where(i => HeldItemShapes.MiniatureFor(i, null, Content.GetBlock(i.Key)) == HeldItemShapes.Miniature.Lump)
        .OrderBy(i => i.Key, System.StringComparer.Ordinal).ToList();

    [Fact]
    public void TheOres_SilicateAndCarbon_AreHeldAsALumpOfTheirBlock()
    {
        var lumps = LumpItems();
        Assert.Equal(24, lumps.Count);
        Assert.Contains(lumps, i => i.Key == "iron_ore");
        Assert.Contains(lumps, i => i.Key == "silicate");
        Assert.Contains(lumps, i => i.Key == "carbon");
        Assert.Contains(lumps, i => i.Key == "tainted_diamond_ore");
        foreach (var item in lumps)
        {
            var block = Content.GetBlock(item.Key)!;
            Assert.Equal("ore", block.Category);
            Assert.Contains(block.Drops, d => d.Item == item.Key); // mined from the block whose tile it wears
        }
    }

    [Theory]
    [InlineData("stone")]       // places its block: the block cube
    [InlineData("basic_drill")] // a tool
    [InlineData("iron_ingot")]  // a material without a block of its key: its icon
    public void EverythingElse_KeepsItsLook(string key)
    {
        var item = Content.GetItem(key)!;
        Assert.NotEqual(HeldItemShapes.Miniature.Lump, HeldItemShapes.MiniatureFor(item, null, Content.GetBlock(key)));
        Assert.False(HeldItemShapes.IsLumpOf(item, Content.GetBlock("iron_ore")));
    }

    [Fact]
    public void WithoutTheBlock_AnOreIsStillItsIcon()
        => Assert.Equal(HeldItemShapes.Miniature.Icon, HeldItemShapes.MiniatureFor(Content.GetItem("iron_ore"), null));

    [Fact]
    public void ALump_RestsOnTheOpenHand()
        => Assert.Equal(HeldGrips.Palm, HeldHands.GripFor(Content.GetItem("iron_ore"), HeldItemShapes.LumpKind));

    [Fact]
    public void ALump_IsTheSameEveryTime_AndDiffersFromOreToOre()
    {
        string Sig(string key) => string.Join("|", HeldItemShapes.Lump(key, Grey).Select(p => $"{p.Position}/{p.Size}"));
        Assert.Equal(Sig("iron_ore"), Sig("iron_ore"));
        var signatures = LumpItems().Select(i => Sig(i.Key)).ToList();
        Assert.Equal(signatures.Count, signatures.Distinct().Count());
    }

    [Fact]
    public void EveryLump_IsAFewKnobsStuckToOneCore()
    {
        foreach (var item in LumpItems())
        {
            var parts = HeldItemShapes.Lump(item.Key, Grey);
            Assert.InRange(parts.Count, 3, 6);
            var core = parts[0];
            Assert.InRange(core.Size.X, 0.10f, 0.13f);
            Assert.Equal(HeldItemShapes.LumpZ, core.Position.Z);
            foreach (var knob in parts.Skip(1))
            {
                Assert.True(knob.Size.X < core.Size.X && knob.Size.Z < core.Size.Z, $"{item.Key}: a knob is smaller than the core");
                Assert.True(Overlaps(core, knob), $"{item.Key}: every knob is stuck into the core");
                Assert.False(Inside(knob, core), $"{item.Key}: and stands out of it");
                Assert.True(knob.Position.Y + (knob.Size.Y * 0.5f) > core.Position.Y - (core.Size.Y * 0.5f), $"{item.Key}: no knob only under it");
            }

            Assert.Empty(HeldGadgetModelTests.SharedFaces(parts)); // no z-fighting between the cubes
            Assert.All(parts, p => Assert.Equal(HeldMaterials.Stone, p.Material));
            Assert.All(parts, p => Assert.Equal(Grey.R, p.Color.R)); // the block's colour until its tile is put on
        }
    }

    [Fact]
    public void TheHand_UnderALump_NeverSharesAPlaneWithIt()
    {
        foreach (var item in LumpItems())
        {
            var parts = HeldItemShapes.Lump(item.Key, Grey);
            var min = parts.Aggregate(new Vector3(float.MaxValue), (m, p) => Vector3.Min(m, p.Position - (p.Size * 0.5f)));
            var max = parts.Aggregate(new Vector3(float.MinValue), (m, p) => Vector3.Max(m, p.Position + (p.Size * 0.5f)));
            var hand = HeldHands.For(HeldGrips.Palm, parts, min, max);
            var palm = hand.Where(b => b.Piece == HeldHands.Piece.Hand).OrderByDescending(b => b.Size.X).First();
            Assert.Equal(min.Y, palm.Position.Y + (palm.Size.Y * 0.5f), 3); // the lump rests on the palm
            foreach (var box in hand)
            {
                // The hand box is the last cube of the list: only its pairs ("i/last …") count here.
                var withHand = parts.Append(new HeldItemShapes.Part(box.Position, box.Size, Grey)).ToList();
                Assert.DoesNotContain(HeldGadgetModelTests.SharedFaces(withHand), f => f.Contains($"/{parts.Count} ", System.StringComparison.Ordinal));
            }
        }
    }

    private static bool Overlaps(HeldItemShapes.Part a, HeldItemShapes.Part b)
    {
        var d = Vector3.Abs(a.Position - b.Position);
        var r = (a.Size + b.Size) * 0.5f;
        return d.X < r.X && d.Y < r.Y && d.Z < r.Z;
    }

    private static bool Inside(HeldItemShapes.Part inner, HeldItemShapes.Part outer)
    {
        var lo = outer.Position - (outer.Size * 0.5f);
        var hi = outer.Position + (outer.Size * 0.5f);
        var a = inner.Position - (inner.Size * 0.5f);
        var b = inner.Position + (inner.Size * 0.5f);
        return a.X >= lo.X && a.Y >= lo.Y && a.Z >= lo.Z && b.X <= hi.X && b.Y <= hi.Y && b.Z <= hi.Z;
    }
}
