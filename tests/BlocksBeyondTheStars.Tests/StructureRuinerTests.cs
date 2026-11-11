// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Tests.Helpers;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The landmark builder and the shared ruin pass (#3486, #3487): an intact authored structure in, a ruin out — the
/// same ruin for the same seed, a different one for another, nothing left floating, the kept cells and the floor
/// under a marker untouched, and the marker rules of a ruin.
/// </summary>
public sealed class StructureRuinerTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private static ushort Id(string key) => Content.GetBlock(key)!.NumericId.Value;

    private static LandmarkLayout Build(StructureTemplate t, long seed, int turns = 0, bool? ruin = null)
        => LandmarkGenerator.Build(t, seed, turns, Content, "grass", ruin);

    private static IEnumerable<(int X, int Y, int Z)> Cells(SettlementStructure s)
    {
        for (int x = 0; x < s.Width; x++)
        {
            for (int y = 0; y < s.Height; y++)
            {
                for (int z = 0; z < s.Length; z++)
                {
                    yield return (x, y, z);
                }
            }
        }
    }

    private static int Solid(SettlementStructure s) => Cells(s).Count(c => s.Get(c.X, c.Y, c.Z) != 0);

    private static ulong Fingerprint(LandmarkLayout l)
    {
        ulong h = 1469598103934665603UL;
        var s = l.Structure;
        foreach (var (x, y, z) in Cells(s))
        {
            h = (h ^ s.Get(x, y, z)) * 1099511628211UL;
            h = (h ^ (ulong)(uint)s.GetShape(x, y, z)) * 1099511628211UL;
            h = (h ^ (l.IsCavity(x, y, z) ? 1UL : 0UL)) * 1099511628211UL;
        }

        return h;
    }

    /// <summary>The solid cells above the ground row that do not hang together with it (faces and edges).</summary>
    private static int Floating(LandmarkLayout l, IEnumerable<(int X, int Y, int Z)> extraAnchors)
    {
        var s = l.Structure;
        var held = new HashSet<(int, int, int)>();
        var open = new Stack<(int X, int Y, int Z)>();
        foreach (var c in Cells(s).Where(c => c.Y <= l.GroundRow && s.Get(c.X, c.Y, c.Z) != 0).Concat(extraAnchors))
        {
            if (s.Get(c.X, c.Y, c.Z) != 0 && held.Add(c))
            {
                open.Push(c);
            }
        }

        while (open.Count > 0)
        {
            var (x, y, z) = open.Pop();
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        int axes = System.Math.Abs(dx) + System.Math.Abs(dy) + System.Math.Abs(dz);
                        var n = (x + dx, y + dy, z + dz);
                        if ((axes == 1 || axes == 2) && s.InBounds(n.Item1, n.Item2, n.Item3)
                            && s.Get(n.Item1, n.Item2, n.Item3) != 0 && held.Add(n))
                        {
                            open.Push(n);
                        }
                    }
                }
            }
        }

        return Cells(s).Count(c => s.Get(c.X, c.Y, c.Z) != 0 && !held.Contains(c));
    }

    [Fact]
    public void Intact_IsTheTemplate_BlockForBlock()
    {
        var t = LandmarkShapes.Tower(ruin: false);
        var l = Build(t, seed: 1);
        Assert.False(l.Ruined);
        Assert.Equal(t.Cells.Count(c => c.Kind == "block"), Solid(l.Structure));
        Assert.Equal(Id("ladder"), l.Structure.Get(1, 5, 1));
        Assert.Equal(Id("glass"), l.Structure.Get(2, 4, 0));
        Assert.Equal(4, l.Structure.Markers.Count); // an intact landmark keeps every marker, the resident too
        Assert.Equal("sound=amb_sounder;radius=60;volume=0.8", l.Structure.Markers.First(m => m.Type == LandmarkRules.SoundMarker).Data);
        Assert.Equal(Fingerprint(l), Fingerprint(Build(t, seed: 99))); // the seed only matters to a ruin
    }

    [Fact]
    public void CompactAndListedBodies_BuildTheSameLandmark()
    {
        var listed = Build(LandmarkShapes.Tower(), seed: 7);
        var compact = Build(LandmarkShapes.Compacted(LandmarkShapes.Tower()), seed: 7);
        Assert.Equal(Fingerprint(listed), Fingerprint(compact));
    }

    [Fact]
    public void Ruin_IsDeterministic_AndDiffersBetweenSeeds()
    {
        var t = LandmarkShapes.Tower();
        Assert.Equal(Fingerprint(Build(t, 20261111)), Fingerprint(Build(t, 20261111)));

        var prints = new HashSet<ulong>();
        var counts = new List<int>();
        for (long seed = 1; seed <= 12; seed++)
        {
            var l = Build(t, seed);
            prints.Add(Fingerprint(l));
            counts.Add(Solid(l.Structure));
        }

        Assert.Equal(12, prints.Count); // no two alike
        Assert.True(counts.Max() - counts.Min() > 40, $"the ruins hardly differ in size: {counts.Min()}..{counts.Max()}");
    }

    [Theory]
    [InlineData(LandmarkRules.ProfileMasonry)]
    [InlineData(LandmarkRules.ProfileLattice)]
    public void Ruin_LeavesNothingFloating_AndKeepsTheGroundCourse(string profile)
    {
        var t = profile == LandmarkRules.ProfileLattice ? LandmarkShapes.Lattice() : LandmarkShapes.Tower();
        int intact = Solid(Build(t, 1, ruin: false).Structure);
        for (long seed = 1; seed <= 10; seed++)
        {
            var l = Build(t, seed);
            var s = l.Structure;
            Assert.Equal(0, Floating(l, Enumerable.Empty<(int, int, int)>()));

            // Rows 0 and 1 are the footprint: whatever the template built there still stands (rubble may be added).
            foreach (var c in t.Cells.Where(c => c.Kind == "block" && c.Y <= 1 && c.Id is "stone" or "iron_wall"))
            {
                Assert.NotEqual(0, s.Get(c.X, c.Y, c.Z));
            }

            double share = Solid(s) / (double)intact;
            Assert.InRange(share, 0.25, 0.97); // a ruin, not a pile and not the intact building
        }
    }

    [Fact]
    public void Ruin_Strength_DecidesHowMuchStands()
    {
        double Share(double strength)
        {
            var t = LandmarkShapes.Tower();
            t.RuinStrength = strength;
            int intact = Solid(Build(t, 1, ruin: false).Structure);
            return Enumerable.Range(1, 8).Average(seed => Solid(Build(t, seed).Structure) / (double)intact);
        }

        double light = Share(0.15), heavy = Share(0.95);
        Assert.True(light > heavy + 0.15, $"light {light:F2} vs heavy {heavy:F2}");
        Assert.True(light > 0.75, $"a light decay left only {light:F2}");
        Assert.True(heavy < 0.7, $"a heavy decay left {heavy:F2}");
    }

    [Fact]
    public void Ruin_KeepsKeptCells_AndSeatsEveryMarkerOnWhatIsLeft()
    {
        var t = LandmarkShapes.Tower();
        t.RuinStrength = 1.0;
        var kept = new List<(int X, int Y, int Z)>();
        for (int y = 33; y < 40; y++)
        {
            kept.Add((7, y, 0)); // a strip of wall right at the top, where the decay is worst
        }

        foreach (var cell in t.Cells.Where(c => c.Kind == "block" && kept.Contains((c.X, c.Y, c.Z))))
        {
            cell.Keep = true;
        }

        for (long seed = 1; seed <= 10; seed++)
        {
            var l = Build(t, seed);
            var s = l.Structure;
            Assert.All(kept, c => Assert.NotEqual(0, s.Get(c.X, c.Y, c.Z)));
            Assert.Equal(0, Floating(l, kept)); // kept cells hold what hangs on them and need no support themselves

            foreach (var m in s.Markers)
            {
                Assert.NotEqual(0, s.Get(m.LocalPos.X, m.LocalPos.Y - 1, m.LocalPos.Z)); // it stands on something
                Assert.Equal(0, s.Get(m.LocalPos.X, m.LocalPos.Y, m.LocalPos.Z));       // its cell
                Assert.Equal(0, s.Get(m.LocalPos.X, m.LocalPos.Y + 1, m.LocalPos.Z));   // head room
            }
        }

        // At full strength the first floor goes somewhere: the den that stood on it comes down with it.
        int den = t.Cells.First(c => c.Id == LandmarkRules.DenMarker).Y;
        Assert.Contains(Enumerable.Range(1, 30), seed =>
            Build(t, seed).Structure.Markers.First(m => m.Type == LandmarkRules.DenMarker).LocalPos.Y < den);
    }

    [Fact]
    public void Ruin_KeepsOnlyWhatAVisitorFinds()
    {
        var l = Build(LandmarkShapes.Tower(), 5);
        Assert.Equal(
            new[] { LandmarkRules.ChestMarker, LandmarkRules.DenMarker, LandmarkRules.SoundMarker },
            l.Structure.Markers.Select(m => m.Type).OrderBy(m => m, System.StringComparer.Ordinal));
        Assert.DoesNotContain(l.Structure.Markers, m => m.Type == "npc");
    }

    [Fact]
    public void Ruin_SmashesGlass_KillsLights_AndNeverErodesALadder()
    {
        var t = LandmarkShapes.Tower();
        var intact = Build(t, 1, ruin: false).Structure;
        int glass = Cells(intact).Count(c => intact.Get(c.X, c.Y, c.Z) == Id("glass"));
        int torches = Cells(intact).Count(c => intact.Get(c.X, c.Y, c.Z) == Id("torch"));
        int glassLeft = 0, torchesLeft = 0, torchesOff = 0;
        for (long seed = 1; seed <= 10; seed++)
        {
            var s = Build(t, seed).Structure;
            glassLeft += Cells(s).Count(c => s.Get(c.X, c.Y, c.Z) == Id("glass"));
            torchesLeft += Cells(s).Count(c => s.Get(c.X, c.Y, c.Z) == Id("torch"));
            torchesOff += Cells(s).Count(c => s.Get(c.X, c.Y, c.Z) == Id("torch_off"));

            // The ladder's foot (rows 1..3, behind the footprint courses) is never bitten out.
            for (int y = 1; y <= 3; y++)
            {
                Assert.Equal(Id("ladder"), s.Get(1, y, 1));
            }
        }

        Assert.True(glassLeft < glass * 10 * 0.6, $"{glassLeft} of {glass * 10} panes survived");
        Assert.True(torchesLeft < torches * 10 * 0.5, $"{torchesLeft} of {torches * 10} torches still burn");
        Assert.True(torchesOff > 0, "a dead torch becomes its switched-off twin");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Turns_TurnBlocksMarkersAndTheRuin_TheSameWay(int turns)
    {
        var t = LandmarkShapes.Tower(size: 12, height: 17, ruin: false);
        t.Width = 20; // an oblong box, so a wrong swap of width and length shows
        var plain = Build(t, 3).Structure;
        var turned = Build(t, 3, turns).Structure;
        Assert.Equal((turns & 1) == 1 ? plain.Length : plain.Width, turned.Width);
        Assert.Equal(Solid(plain), Solid(turned));

        foreach (var (x, y, z) in Cells(plain))
        {
            var (tx, tz) = LandmarkGenerator.Turn(x, z, plain.Width, plain.Length, turns);
            Assert.Equal(plain.Get(x, y, z), turned.Get(tx, y, tz));
        }

        foreach (var m in plain.Markers)
        {
            var (tx, tz) = LandmarkGenerator.Turn(m.LocalPos.X, m.LocalPos.Z, plain.Width, plain.Length, turns);
            Assert.Contains(turned.Markers, o => o.Type == m.Type && o.LocalPos.Equals(new Vector3i(tx, m.LocalPos.Y, tz)));
        }
    }

    [Fact]
    public void Mine_KeepsItsCavities_CavesInDeepDown_AndLeavesTheWayInOpen()
    {
        var t = LandmarkShapes.Mine();
        var intact = Build(t, 1, ruin: false);
        Assert.Equal(t.GroundRow, intact.GroundRow);
        Assert.Equal(t.Cells.Count(c => c.Kind == LandmarkRules.CavityKind), intact.CavityCount);
        Assert.True(intact.IsCavity(4, t.GroundRow, 4)); // the shaft's mouth in the hut floor
        Assert.True(intact.IsCavity(34, 5, 8));          // the cavern

        int filled = 0;
        for (long seed = 1; seed <= 10; seed++)
        {
            var l = Build(t, seed);
            Assert.True(l.CavityCount <= intact.CavityCount);
            filled += intact.CavityCount - l.CavityCount;

            // The shaft is open from the hut down to five rows under the ground row, and the chest's cell is free.
            for (int y = t.GroundRow - 4; y <= t.GroundRow; y++)
            {
                Assert.True(l.IsCavity(4, y, 4), $"seed {seed}: the shaft is blocked at row {y}");
            }

            var chest = l.Structure.Markers.Single(m => m.Type == LandmarkRules.ChestMarker);
            Assert.True(l.IsCavity(chest.LocalPos.X, chest.LocalPos.Y, chest.LocalPos.Z));

            // A filled cell is rock and no cavity; a cavity is never a block.
            foreach (var (x, y, z) in Cells(l.Structure))
            {
                Assert.False(l.IsCavity(x, y, z) && l.Structure.Get(x, y, z) != 0);
            }
        }

        Assert.True(filled > 0, "no tunnel caved in over ten seeds");
    }
}
