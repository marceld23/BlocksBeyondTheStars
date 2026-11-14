// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit.Abstractions;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3626 / #3627: the landmarks' torches stood straight in the air in front of their walls — the scripts stamped bare
/// <c>torch</c> cells, and a light without a mount descriptor is drawn as the old upright rosette. The landmark toolkit's
/// mount pass (tools/landmarks/lightmount.py, run by <c>Design.write</c>) now gives every torch and lantern the wall /
/// floor / ceiling mount of #2683, and the scripts put their torches beside the walls instead of into them. These tests
/// read the shipped files: every light carries a mount whose block holds it (a torch on a wall wherever a wall is
/// beside it), nobody's light is buried, and the mount survives the landmark's quarter turn and its ruin pass.
/// </summary>
public sealed class LandmarkLightMountTests
{
    private static readonly Lazy<GameContent> Shipped = new(() => ContentLoader.LoadFromDirectory(TestPaths.DataDir()));

    /// <summary>The framework's samples are pinned byte for byte as they shipped (StructureGeneratorGoldenTests), so they keep
    /// their unmounted lights — the pass is switched off for them in tools/gen_landmarks.py.</summary>
    private static readonly HashSet<string> PinnedSamples = new(StringComparer.Ordinal) { "sample_tower", "sample_mine", "sample_store" };

    private readonly ITestOutputHelper _output;

    public LandmarkLightMountTests(ITestOutputHelper output) => _output = output;

    private static IEnumerable<StructureTemplate> MountedLandmarks()
        => Shipped.Value.Landmarks.Where(t => !PinnedSamples.Contains(t.Key));

    /// <summary>The template's plain cells by position (blocks and cavities) and its marker cells.</summary>
    private static (Dictionary<(int, int, int), TemplateBody.Plain> Cells, HashSet<(int, int, int)> Markers) Read(StructureTemplate t)
    {
        var cells = new Dictionary<(int, int, int), TemplateBody.Plain>();
        TemplateBody.ForEachPlain(t, p => cells[(p.X, p.Y, p.Z)] = p);
        var markers = new HashSet<(int, int, int)>(t.Cells.Where(c => c.Kind == "marker").Select(c => (c.X, c.Y, c.Z)));
        return (cells, markers);
    }

    /// <summary>Whether the template cell at <paramref name="p"/> holds a light: a block that <see cref="PropShapes.HoldsLight"/>
    /// says so of, or — below the ground row — an empty cell that is neither a cavity nor a marker's: the planet's rock.</summary>
    private static bool Holds(StructureTemplate t, Dictionary<(int, int, int), TemplateBody.Plain> cells, HashSet<(int, int, int)> markers,
        (int X, int Y, int Z) p)
    {
        if (cells.TryGetValue(p, out var c))
        {
            return !c.Cavity && PropShapes.HoldsLight(Shipped.Value.GetBlock(c.Id));
        }

        return p.Y < t.GroundRow && !markers.Contains(p);
    }

    private static (int, int, int) Holder((int X, int Y, int Z) p, int face)
    {
        var (dx, dy, dz) = ShapeCode.FaceDirection(face);
        return (p.X - dx, p.Y - dy, p.Z - dz);
    }

    [Fact]
    public void EveryTorchAndLanternOfAShippedLandmark_HangsOnWhatHoldsIt_AWallFirstForATorch()
    {
        int lights = 0;
        var torchesOnWalls = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var t in MountedLandmarks())
        {
            var (cells, markers) = Read(t);
            foreach (var (p, c) in cells.Where(kv => !kv.Value.Cavity && PropShapes.IsLightMountKey(kv.Value.Id)))
            {
                lights++;
                string where = $"{t.Key}: {c.Id} at {p}";
                int mount = PropShapes.LightMountOf(c.Id, c.Shape);
                Assert.True(mount >= 0, $"{where} carries no mount (shape {c.Shape}) — run tools/gen_landmarks.py");
                Assert.True(Holds(t, cells, markers, Holder(p, mount)), $"{where} is fixed by face {mount} to a cell that holds no light");
                var walls = ShapeCode.WallFaces.Where(f => Holds(t, cells, markers, Holder(p, f))).ToList();
                if (!PropShapes.IsLanternKey(c.Id))
                {
                    Assert.True(mount != ShapeCode.UpPlusY || walls.Count == 0, $"{where} stands on the floor beside a wall ({string.Join(",", walls)})");
                    torchesOnWalls[t.Key] = torchesOnWalls.GetValueOrDefault(t.Key) + (mount >= 2 ? 1 : 0);
                }

                Assert.False(Enumerable.Range(0, 6).All(f => Holds(t, cells, markers, Holder(p, f))), $"{where} is buried on all six sides");
            }
        }

        _output.WriteLine($"{lights} lights; torches on walls: {string.Join(", ", torchesOnWalls.Select(kv => $"{kv.Key} {kv.Value}"))}");
        foreach (string key in new[] { "old_mine", "eifelnyde", "porta_nigra", "portasseum", "golden_bird_gate" })
        {
            Assert.True(torchesOnWalls.GetValueOrDefault(key) > 0, $"{key} has no torch on a wall");
        }
    }

    /// <summary>The layout the stamp writes (turned, ruined or not) keeps every light's mount, turned with the landmark, and
    /// the turned mount still points at a block that holds it. The ruin pass's light swap (torch → torch_off) keeps the
    /// descriptor and erosion never takes a shaped cell; a light whose block fell falls with it (LandmarkLights) instead of
    /// leaning out of thin air.</summary>
    [Theory]
    [InlineData("old_mine", 1)]
    [InlineData("porta_nigra", 3)]
    [InlineData("golden_bird_gate", 2)]
    public void TheStampedLayout_KeepsEveryMount_TurnedWithTheLandmark(string key, int turns)
    {
        var content = Shipped.Value;
        var t = content.LandmarkByKey(key)!;
        int templateLights = 0;
        TemplateBody.ForEachPlain(t, p => templateLights += !p.Cavity && PropShapes.IsLightMountKey(p.Id) ? 1 : 0);
        foreach (bool ruin in new[] { false, true })
        {
            var layout = LandmarkGenerator.Build(t, 20261114, turns, content, "grass", ruin);
            var s = layout.Structure;
            int lights = 0;
            for (int x = 0; x < s.Width; x++)
            {
                for (int y = 0; y < s.Height; y++)
                {
                    for (int z = 0; z < s.Length; z++)
                    {
                        var def = content.BlockById(new Shared.Primitives.BlockId(s.Get(x, y, z)));
                        if (def is null || !PropShapes.IsLightMountKey(def.Key))
                        {
                            continue;
                        }

                        lights++;
                        int mount = PropShapes.LightMountOf(def.Key, s.GetShape(x, y, z));
                        Assert.True(mount >= 0, $"{key} t{turns}: {def.Key} at {(x, y, z)} lost its mount ({s.GetShape(x, y, z)})");
                        Assert.True(HeldInLayout(layout, Holder((x, y, z), mount)),
                            $"{key} t{turns} {(ruin ? "ruin" : "intact")}: {def.Key} at {(x, y, z)} faces {mount}, but nothing holds it");
                    }
                }
            }

            _output.WriteLine($"{key} t{turns} {(ruin ? "ruin" : "intact")}: {lights} of {templateLights} lights");
            Assert.True(ruin ? lights > 0 : lights == templateLights, $"{key}: {lights} lights in the layout, {templateLights} in the template");
        }
    }

    /// <summary>Whether the layout cell holds a light: a block that does, or — below the ground row, inside or outside the
    /// grid — rock the stamp leaves standing (no cavity carved there).</summary>
    private static bool HeldInLayout(LandmarkLayout layout, (int X, int Y, int Z) p)
    {
        var s = layout.Structure;
        bool inside = p.X >= 0 && p.Y >= 0 && p.Z >= 0 && p.X < s.Width && p.Y < s.Height && p.Z < s.Length;
        ushort b = inside ? s.Get(p.X, p.Y, p.Z) : (ushort)0;
        if (b != 0)
        {
            return PropShapes.HoldsLight(Shipped.Value.BlockById(new Shared.Primitives.BlockId(b)));
        }

        return p.Y < layout.GroundRow && !(inside && layout.IsCavity(p.X, p.Y, p.Z));
    }
}
