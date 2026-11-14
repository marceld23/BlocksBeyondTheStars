// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Tests.Helpers;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Revision 3 of the shared ruin pass (#3609), the giant wrecks: the profile <c>crash</c> (a hull lying on a planet: anchored on
/// the buried keel, rubble on the ground, overgrowth only with air), the hull at revision 3 in space (every big piece stays),
/// collapse that scales, hull breaks, glass in patches, dead lights, frozen water — and the landmark profiles untouched.
/// </summary>
public sealed class StructureRuinerCrashTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    /// <summary>A long hull (120 × 24 × 32: long enough to break) and a short one (48 × 24 × 32: it never breaks).</summary>
    private static readonly GiantShipTestHulls.Hull Long = GiantShipTestHulls.Build(120, 24, 32);

    private static ushort Id(string key) => Content.GetBlock(key)!.NumericId.Value;

    private static RuinOptions Options(bool air = false) => new()
    {
        Overgrowth = air,
        FreezeWater = !air,
        RubblePalette = new[] { new RuinWeightedBlock("scrap_metal", 2), new RuinWeightedBlock("carbon", 1) },
        Scorch = new RuinScorch { Block = "carbon", NearBreachChance = 0.3, ElsewhereChance = 0.02 },
    };

    private static (VoxelGrid Intact, VoxelGrid Ruin) Run(GiantShipTestHulls.Hull hull, string profile, long seed, double strength = 0,
        RuinOptions? options = null, int revision = StructureRuiner.GiantRevision)
    {
        var (intact, _) = GiantShipTestHulls.Grid(Content, hull);
        var (g, markers) = GiantShipTestHulls.Grid(Content, hull);
        StructureRuiner.Run(g, Content, seed, profile, strength, hull.Ground, markers, "grass", revision, options ?? Options());
        return (intact, g);
    }

    private static double ShareRemoved(VoxelGrid intact, VoxelGrid ruin)
    {
        int solid = 0, gone = 0;
        for (int i = 0; i < intact.Blocks.Length; i++)
        {
            solid += intact.Blocks[i] != 0 ? 1 : 0;
            gone += intact.Blocks[i] != 0 && ruin.Blocks[i] == 0 ? 1 : 0;
        }

        return (double)gone / solid;
    }

    [Fact]
    public void Revision3_IsDeterministic_AndDiffersBySeed()
    {
        foreach (string profile in new[] { LandmarkRules.ProfileCrash, LandmarkRules.ProfileHull })
        {
            ulong a = RuinerTestGrids.Hash(Content, Run(Long, profile, 5).Ruin);
            Assert.Equal(a, RuinerTestGrids.Hash(Content, Run(Long, profile, 5).Ruin));
            Assert.NotEqual(a, RuinerTestGrids.Hash(Content, Run(Long, profile, 6).Ruin));
            Assert.Equal(a, RuinerTestGrids.Hash(Content, Run(Long, profile, 5, revision: 99).Ruin)); // a newer build's record
        }

        // The hull at revisions 1 and 2 stays what it was; the crash profile is new and runs revision 3 whatever is asked.
        Assert.NotEqual(RuinerTestGrids.Hash(Content, Run(Long, LandmarkRules.ProfileHull, 5).Ruin),
            RuinerTestGrids.Hash(Content, Run(Long, LandmarkRules.ProfileHull, 5, revision: 2).Ruin));
        Assert.Equal(RuinerTestGrids.Hash(Content, Run(Long, LandmarkRules.ProfileHull, 5, revision: 1).Ruin),
            RuinerTestGrids.Hash(Content, Run(Long, LandmarkRules.ProfileHull, 5, revision: 2).Ruin));
        Assert.Equal(RuinerTestGrids.Hash(Content, Run(Long, LandmarkRules.ProfileCrash, 5, revision: 1).Ruin),
            RuinerTestGrids.Hash(Content, Run(Long, LandmarkRules.ProfileCrash, 5).Ruin));
    }

    /// <summary>Revision 3 changes the giant profiles only: every landmark profile builds what revision 2 builds.</summary>
    [Theory]
    [InlineData(LandmarkRules.ProfileMasonry)]
    [InlineData(LandmarkRules.ProfileLattice)]
    [InlineData(LandmarkRules.ProfileUnderground)]
    public void Revision3_BuildsRevision2_ForEveryLandmarkProfile(string profile)
    {
        foreach (long seed in new long[] { 1, 20261114 })
        {
            foreach (var make in new System.Func<RuinerTestGrids.Case>[]
                     {
                         () => RuinerTestGrids.Soup(Content),
                         () => RuinerTestGrids.FromTemplate(Content, LandmarkShapes.Lattice()),
                         () => RuinerTestGrids.FromTemplate(Content, LandmarkShapes.Tower()),
                     })
            {
                var two = make();
                var three = make();
                StructureRuiner.Run(two.Grid, Content, seed, profile, 0.7, two.GroundRow, two.Markers, "grass", 2, new RuinOptions());
                StructureRuiner.Run(three.Grid, Content, seed, profile, 0.7, three.GroundRow, three.Markers, "grass", 3, new RuinOptions());
                Assert.Equal(RuinerTestGrids.Hash(Content, two.Grid), RuinerTestGrids.Hash(Content, three.Grid));
            }
        }
    }

    [Fact]
    public void Crash_LeavesNothingAdrift_KeepsTheKeel_AndLeavesRubbleOnTheGround()
    {
        ushort scrap = Id("scrap_metal"), carbon = Id("carbon");
        for (long seed = 1; seed <= 4; seed++)
        {
            var (intact, g) = Run(Long, LandmarkRules.ProfileCrash, seed);
            foreach (var piece in RuinerTestGrids.Pieces(g))
            {
                Assert.True(piece.Any(c => c.Y <= Long.Ground || g.Keep[g.Index(c.X, c.Y, c.Z)]),
                    $"seed {seed}: a piece of {piece.Count} hangs on nothing");
            }

            // The kept ladder shaft is untouched; nothing of the buried keel is taken (a scorch mark may darken it).
            Assert.All(RuinerTestGrids.Cells(g).Where(c => g.Keep[g.Index(c.X, c.Y, c.Z)]),
                c => Assert.Equal(intact.Get(c.X, c.Y, c.Z), g.Get(c.X, c.Y, c.Z)));
            Assert.All(RuinerTestGrids.Cells(g).Where(c => c.Y <= Long.Ground && intact.Solid(c.X, c.Y, c.Z)),
                c => Assert.True(g.Solid(c.X, c.Y, c.Z), $"seed {seed}: the keel lost {c}"));

            // Rubble lies where nothing stood before, low on the ground or on a floor near it.
            var rubble = RuinerTestGrids.Cells(g).Where(c => intact.Get(c.X, c.Y, c.Z) == 0 && g.Solid(c.X, c.Y, c.Z)).ToList();
            Assert.NotEmpty(rubble);
            Assert.All(rubble, c => Assert.True(c.Y >= Long.Ground && c.Y <= Long.Ground + 3, $"rubble at row {c.Y}"));
            Assert.All(rubble, c => Assert.Contains(g.Get(c.X, c.Y, c.Z), new[] { scrap, carbon }));
        }
    }

    [Fact]
    public void Crash_Overgrows_OnlyWithAir()
    {
        ushort flora = Content.GetBlock(SettlementGenerator.BiomeFloraKey("grass"))!.NumericId.Value;
        int grown = 0;
        for (long seed = 1; seed <= 3; seed++)
        {
            Assert.DoesNotContain(flora, Run(Long, LandmarkRules.ProfileCrash, seed, options: Options(air: false)).Ruin.Blocks);
            grown += Run(Long, LandmarkRules.ProfileCrash, seed, options: Options(air: true)).Ruin.Blocks.Count(b => b == flora);
        }

        Assert.True(grown > 0, "no plant grew through a wreck on a body with air");
    }

    /// <summary>A long hull breaks across its long axis: above the buried keel it falls apart into big sections that the keel
    /// still holds on a planet.</summary>
    [Fact]
    public void Crash_LongHull_BreaksIntoSections_ThatStayOnTheGround()
    {
        int broken = 0;
        for (long seed = 1; seed <= 6; seed++)
        {
            var (_, g) = Run(Long, LandmarkRules.ProfileCrash, seed);
            var above = RuinerTestGrids.Clone(g);
            for (int i = 0; i < above.Blocks.Length; i++)
            {
                if ((i % (above.H * above.L)) / above.L <= Long.Ground + 3)
                {
                    above.Blocks[i] = 0; // look above the keel and the rubble's reach (scrap may lie in the gap of a break)
                }
            }

            int sections = RuinerTestGrids.Pieces(above).Count(p => p.Count >= 1000);
            broken += sections >= 2 ? 1 : 0;
        }

        Assert.True(broken >= 4, $"only {broken} of 6 long hulls broke into sections");
    }

    /// <summary>In space every piece of at least <see cref="StructureRuiner.MinPieceCells"/> blocks stays (revision 2 kept only
    /// the largest), the crumbs go, and nothing moves.</summary>
    [Fact]
    public void Space_KeepsEveryBigPiece_DropsTheCrumbs_AndMovesNothing()
    {
        int several = 0;
        for (long seed = 1; seed <= 6; seed++)
        {
            var (intact, g) = Run(Long, LandmarkRules.ProfileHull, seed);
            var pieces = RuinerTestGrids.Pieces(g);
            int largest = pieces.Max(p => p.Count);
            Assert.All(pieces, p => Assert.True(p.Count >= StructureRuiner.MinPieceCells || p.Count == largest
                || p.Any(c => g.Keep[g.Index(c.X, c.Y, c.Z)]), $"seed {seed}: a crumb of {p.Count} stayed"));
            several += pieces.Count(p => p.Count >= StructureRuiner.MinPieceCells) >= 2 ? 1 : 0;

            // Nothing new lies anywhere: every block left stood there before (scorched, switched off or frozen).
            Assert.All(RuinerTestGrids.Cells(g).Where(c => g.Solid(c.X, c.Y, c.Z)), c => Assert.True(intact.Solid(c.X, c.Y, c.Z)));
        }

        Assert.True(several >= 4, $"only {several} of 6 hulls drift as several big pieces");
    }

    [Fact]
    public void GlassIsSmashedInPatches_AndEveryLightIsDead()
    {
        ushort glass = Id("glass"), light = Id("light_white"), lightOff = Id("light_white_off");
        foreach (string profile in new[] { LandmarkRules.ProfileCrash, LandmarkRules.ProfileHull })
        {
            var (intact, g) = Run(Long, profile, 3);
            var panes = RuinerTestGrids.Cells(intact).Where(c => intact.Get(c.X, c.Y, c.Z) == glass && c.Y > Long.Ground).ToList();
            bool Smashed((int X, int Y, int Z) c) => g.Get(c.X, c.Y, c.Z) != glass;
            double share = panes.Count(Smashed) / (double)panes.Count;
            Assert.InRange(share, 0.25, 0.7);

            // Coherent patches: next to a smashed pane, a pane is smashed far more often than on average.
            var set = panes.ToHashSet();
            var pairs = panes.Where(Smashed).SelectMany(c => new[] { (c.X + 1, c.Y, c.Z), (c.X, c.Y, c.Z + 1), (c.X, c.Y + 1, c.Z) })
                .Where(set.Contains).ToList();
            Assert.True(pairs.Count(Smashed) / (double)pairs.Count > share + 0.15, $"{profile}: the smashed glass is a speckle");

            Assert.DoesNotContain(light, g.Blocks);
            Assert.Contains(lightOff, g.Blocks);
        }
    }

    [Fact]
    public void WaterFreezes_OnlyWhenAsked_AndIsNeverScorched()
    {
        ushort water = Id("water"), ice = Id("ice");
        var (_, frozen) = Run(Long, LandmarkRules.ProfileCrash, 2, options: Options(air: false));
        Assert.DoesNotContain(water, frozen.Blocks);
        Assert.Contains(ice, frozen.Blocks);

        var (intact, wet) = Run(Long, LandmarkRules.ProfileCrash, 2, options: Options(air: true));
        Assert.DoesNotContain(ice, wet.Blocks);
        Assert.Contains(water, wet.Blocks);
        Assert.All(RuinerTestGrids.Cells(intact).Where(c => intact.Get(c.X, c.Y, c.Z) == water),
            c => Assert.Contains(wet.Get(c.X, c.Y, c.Z), new ushort[] { 0, water })); // gone or still water, never carbon

        // An option of every revision, off by default: a landmark's pass freezes nothing unless asked.
        var soup = RuinerTestGrids.Soup(Content);
        soup.Grid.Set(5, 12, 5, water);
        soup.Grid.Keep[soup.Grid.Index(5, 12, 5)] = true;
        StructureRuiner.Run(soup.Grid, Content, 1, LandmarkRules.ProfileMasonry, 0.5, soup.GroundRow, soup.Markers, "grass", 1,
            new RuinOptions { FreezeWater = true });
        Assert.Equal(ice, soup.Grid.Get(5, 12, 5)); // a kept cell freezes too: the cold is no decay
    }

    /// <summary>From a distance a giant wreck reads as plating with a few big breaches, not moth-eaten: almost no lone one-cell
    /// hole in its outer surface, and its scorch lies in patches round the breaches — a modest share, hardly a lone speck.</summary>
    [Fact]
    public void Revision3_HasNoSpeckle_HolesAndScorchAreCoherent()
    {
        ushort carbon = Id("carbon");
        foreach (string profile in new[] { LandmarkRules.ProfileCrash, LandmarkRules.ProfileHull })
        {
            for (long seed = 1; seed <= 3; seed++)
            {
                var (intact, g) = Run(Long, profile, seed);
                var outer = OuterSurface(intact).Where(c => c.Y > Long.Ground).ToList();
                bool Gone((int X, int Y, int Z) c) => intact.Solid(c.X, c.Y, c.Z) && !g.Solid(c.X, c.Y, c.Z);
                bool Burnt((int X, int Y, int Z) c) => g.Get(c.X, c.Y, c.Z) == carbon && intact.Get(c.X, c.Y, c.Z) != carbon;
                var set = outer.ToHashSet();
                int lone = outer.Count(c => Gone(c) && Faces(c).Where(set.Contains).All(n => !Gone(n)));
                Assert.True(lone < 0.02 * outer.Count, $"{profile} seed {seed}: {lone} lone holes in {outer.Count} outer cells");

                var left = outer.Where(c => !Gone(c)).ToList();
                var burnt = left.Where(Burnt).ToList();
                Assert.InRange(burnt.Count / (double)left.Count, 0.02, 0.25);
                int specks = burnt.Count(c => Around(c).All(n => !Burnt(n) && !Gone(n)));
                Assert.True(specks < 0.05 * burnt.Count, $"{profile} seed {seed}: {specks} of {burnt.Count} scorch marks are lone specks");
            }
        }
    }

    private static IEnumerable<(int X, int Y, int Z)> Faces((int X, int Y, int Z) c)
    {
        yield return (c.X + 1, c.Y, c.Z);
        yield return (c.X - 1, c.Y, c.Z);
        yield return (c.X, c.Y + 1, c.Z);
        yield return (c.X, c.Y - 1, c.Z);
        yield return (c.X, c.Y, c.Z + 1);
        yield return (c.X, c.Y, c.Z - 1);
    }

    private static IEnumerable<(int X, int Y, int Z)> Around((int X, int Y, int Z) c)
    {
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    if (dx != 0 || dy != 0 || dz != 0)
                    {
                        yield return (c.X + dx, c.Y + dy, c.Z + dz);
                    }
                }
            }
        }
    }

    /// <summary>The blocks of a grid that touch the air outside it (a flood of the empty cells from the box's faces).</summary>
    private static List<(int X, int Y, int Z)> OuterSurface(VoxelGrid g)
    {
        var outside = new HashSet<(int, int, int)>();
        var open = new Stack<(int X, int Y, int Z)>();
        foreach (var c in RuinerTestGrids.Cells(g).Where(c => !g.Solid(c.X, c.Y, c.Z)
                     && (c.X == 0 || c.Y == 0 || c.Z == 0 || c.X == g.W - 1 || c.Y == g.H - 1 || c.Z == g.L - 1)))
        {
            outside.Add(c);
            open.Push(c);
        }

        while (open.Count > 0)
        {
            foreach (var n in Faces(open.Pop()).Where(n => g.In(n.X, n.Y, n.Z) && !g.Solid(n.X, n.Y, n.Z) && outside.Add(n)))
            {
                open.Push(n);
            }
        }

        return RuinerTestGrids.Cells(g).Where(c => g.Solid(c.X, c.Y, c.Z)
            && Faces(c).Any(n => !g.In(n.X, n.Y, n.Z) || outside.Contains(n))).ToList();
    }

    /// <summary>The size the brief names: one pass over a 256 × 80 × 100 hull completes and takes a share in range (no clock).</summary>
    [Fact]
    public void GiantHull_256x80x100_CompletesWithAShareInRange()
    {
        var big = GiantShipTestHulls.Build(256, 80, 100);
        foreach (string profile in new[] { LandmarkRules.ProfileCrash, LandmarkRules.ProfileHull })
        {
            var (intact, g) = Run(big, profile, 11);
            Assert.InRange(ShareRemoved(intact, g), 0.12, 0.35);
        }
    }

    [Fact]
    public void Strength_ScalesTheDecay()
    {
        foreach (string profile in new[] { LandmarkRules.ProfileCrash, LandmarkRules.ProfileHull })
        {
            var (weakIntact, weakRuin) = Run(Long, profile, 4, 0.1);
            var (strongIntact, strongRuin) = Run(Long, profile, 4, 1.0);
            double weak = ShareRemoved(weakIntact, weakRuin), strong = ShareRemoved(strongIntact, strongRuin);
            Assert.True(strong > weak + 0.05, $"{profile}: strength 1 took {strong:P0}, strength 0.1 {weak:P0}");
        }
    }

    [Fact]
    public void Crash_IsAGiantProfile_NotALandmarkOne()
    {
        Assert.Contains(LandmarkRules.ProfileCrash, LandmarkRules.AllProfiles);
        Assert.DoesNotContain(LandmarkRules.ProfileCrash, LandmarkRules.Profiles);
        var landmark = new StructureTemplate { Key = "x", Width = 4, Height = 4, Length = 4, Landmark = true, RuinProfile = "crash" };
        Assert.Equal(LandmarkRules.ProfileMasonry, LandmarkRules.ProfileOf(landmark));
        Assert.Contains(LandmarkRules.Problems(landmark), p => p.Contains("giant ship", System.StringComparison.Ordinal));
        Assert.Equal(LandmarkRules.ProfileHull, LandmarkRules.GiantProfileOf(new RuinZone { Profile = "crash" }, space: true));
        Assert.Equal(LandmarkRules.ProfileHull, LandmarkRules.GiantProfileOf(new RuinZone { Profile = "Hull" }, space: false));
        Assert.Equal(LandmarkRules.ProfileLattice, LandmarkRules.GiantProfileOf(new RuinZone { Profile = "lattice" }, space: false));
        Assert.Equal(LandmarkRules.ProfileCrash, LandmarkRules.GiantProfileOf(new RuinZone(), space: false));
        Assert.Equal(LandmarkRules.ProfileCrash, LandmarkRules.GiantProfileOf(new RuinZone { Profile = "masonry" }, space: false));
        Assert.Equal(StructureRuiner.GiantRevision, GiantShipGenerator.RuinRevision);
        Assert.True(StructureRuiner.GiantRevision > StructureRuiner.Revision);
    }

    /// <summary>The revision-3 pins (#3609): the pass straight over the synthetic long hull, both giant profiles, two seeds and two
    /// strengths. A change of revision 3 that moves these moves every giant wreck that stands — give it a new revision.</summary>
    [Fact]
    public void Revision3_MatchesThePins()
    {
        var cases = new List<(string, ulong)>();
        foreach (long seed in new long[] { 1, 20261114 })
        {
            foreach (double strength in new[] { 0.0, 1.0 })
            {
                foreach (string profile in new[] { LandmarkRules.ProfileCrash, LandmarkRules.ProfileHull })
                {
                    string name = $"{profile}:{seed}:{strength:0}";
                    cases.Add((name, RuinerTestGrids.Hash(Content, Run(Long, profile, seed, strength).Ruin)));
                }
            }
        }

        StructureGoldens.AssertPinned("ruin pass revision 3", Pins, cases);
    }

    private static readonly Dictionary<string, string> Pins = new()
    {
        ["crash:1:0"] = "76ec639d27600c4d",
        ["hull:1:0"] = "481a4cfab5c97f8c",
        ["crash:1:1"] = "b80fcc68a52318fa",
        ["hull:1:1"] = "a4204394a03d2fde",
        ["crash:20261114:0"] = "adf0c599fc8bdb70",
        ["hull:20261114:0"] = "ca490cba359b5866",
        ["crash:20261114:1"] = "d6349d105ad76619",
        ["hull:20261114:1"] = "14c9ead40a597bfe",
    };
}
