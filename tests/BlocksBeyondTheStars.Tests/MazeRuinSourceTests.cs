// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using BlocksBeyondTheStars.WorldGeneration.Sources;
using Xunit;
using Xunit.Abstractions;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3645: the Labyrinth Builders' maze ruin source (<c>maze_ruin</c>) — the palette of passage widths, a maze whose every
/// cell is reachable from every entrance, loops, the entrances in the outer wall, the plaza with its statue and chest, the
/// statues and chests of the halls and dead ends, the kept wall rows that survive the ruin pass, the block parameters, the
/// seed purity and the goldens per size.
/// </summary>
public sealed partial class MazeRuinSourceTests
{
    private const string Key = "maze_ruin";

    private static readonly Lazy<GameContent> LazyContent = new(() => ContentLoader.LoadFromDirectory(TestPaths.DataDir()));

    private static readonly long[] Seeds = { 1, 20261114, -987_654_321_987L, 0x5DEECE66DL };

    private readonly ITestOutputHelper _out;

    public MazeRuinSourceTests(ITestOutputHelper output) => _out = output;

    private static GameContent Content => LazyContent.Value;

    public static TheoryData<string> SizeKeys() => new() { MazeRuinPlan.Small, MazeRuinPlan.Medium, MazeRuinPlan.Large };

    private static BuiltStructure Build(long seed, params (string Key, string Value)[] values)
        => StructureSources.Get(Key)!.Build(new StructureBuildRequest(Content, seed)
        {
            Params = values.ToDictionary(v => v.Key, v => v.Value, StringComparer.Ordinal),
        });

    /// <summary>The plan a build with the default braid and hall count stands on.</summary>
    private static MazeRuinPlan Plan(long seed, string size = MazeRuinPlan.Medium, int braid = 15, int halls = 3)
        => MazeRuinPlan.Create(seed, size, braid, halls);

    private static ushort Id(string block) => Content.GetBlock(block)!.NumericId.Value;

    /// <summary>A column a visitor walks through: paving in the floor row, air above it.</summary>
    private static bool Walkable(BuiltStructure b, int x, int z) => b.Get(x, 0, z) != 0 && b.Get(x, 1, z) == 0;

    [Theory]
    [MemberData(nameof(SizeKeys))]
    public void Widths_ComeFromThePalette_NarrowAlleysAndWideAvenuesBoth(string size)
    {
        int[] allowed = { 2, 3, 4, 5, 6, 8, 9, 10 };
        foreach (long seed in Seeds)
        {
            var plan = Plan(seed, size);
            var widths = plan.WidthX.Concat(plan.WidthZ).ToList();
            Assert.Equal(1, plan.N % 2);
            Assert.All(widths, w => Assert.Contains(w, allowed));
            Assert.Contains(widths, w => w <= 3);
            Assert.Contains(widths, w => w >= 8);
            Assert.True(plan.WidthX[plan.N / 2] >= 8 && plan.WidthZ[plan.N / 2] >= 8, "the plaza's middle column and row are wide");

            int target = MazeRuinPlan.FootprintFor(size);
            Assert.InRange(plan.Width, target * 9 / 10, target * 11 / 10);
            Assert.InRange(plan.Length, target * 9 / 10, target * 11 / 10);
            var b = Build(seed, ("size", size));
            Assert.Equal((plan.Width, plan.Length), (b.Width, b.Length));
        }
    }

    [Theory]
    [MemberData(nameof(SizeKeys))]
    public void EveryCell_IsReachableFromEveryEntrance(string size)
    {
        foreach (long seed in Seeds.Take(2))
        {
            var plan = Plan(seed, size);
            var b = Build(seed, ("size", size));
            Assert.InRange(plan.Entrances.Count, 2, 4);
            foreach (var e in plan.Entrances)
            {
                var reached = FloodFrom(b, EntranceMouth(plan, e));
                for (int j = 0; j < plan.N; j++)
                {
                    for (int i = 0; i < plan.N; i++)
                    {
                        int x = plan.StartX[i] + (plan.WidthX[i] / 2), z = plan.StartZ[j] + (plan.WidthZ[j] / 2);
                        Assert.True(reached[(x * b.Length) + z], $"{size}/{seed}: cell ({i},{j}) cannot be reached from entrance {e.Side}:{e.Index}");
                    }
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(SizeKeys))]
    public void Entrances_LieInTheOuterWall_OnDifferentSides(string size)
    {
        foreach (long seed in Seeds)
        {
            var plan = Plan(seed, size);
            var b = Build(seed, ("size", size));
            Assert.Equal(plan.Entrances.Count, plan.Entrances.Select(e => e.Side).Distinct().Count());
            var mouths = new HashSet<(int, int)>();
            foreach (var e in plan.Entrances)
            {
                Assert.InRange(e.Index, 1, plan.N - 2); // never a corner cell
                foreach (var (x, z) in EntranceSpan(plan, e))
                {
                    Assert.True(Walkable(b, x, z), $"{size}/{seed}: the entrance {e.Side}:{e.Index} is blocked at ({x},{z})");
                    mouths.Add((x, z));
                }
            }

            // Every other cell of the outer ring is wall.
            foreach (var (x, z) in Ring(b.Width, b.Length).Where(c => !mouths.Contains(c)))
            {
                Assert.False(Walkable(b, x, z), $"{size}/{seed}: the outer wall has a gap at ({x},{z})");
            }
        }
    }

    [Fact]
    public void Braid_OpensDeadEndsIntoLoops_OverTheSamePerfectMaze()
    {
        foreach (long seed in Seeds)
        {
            var tree = Plan(seed, braid: 0, halls: 0);
            var braided = Plan(seed, braid: 15, halls: 0);
            var wide = Plan(seed, braid: 100, halls: 0);
            int cells = tree.N * tree.N;
            Assert.True(tree.OpenPassages() > cells - 1, "the plaza alone already closes loops");
            Assert.True(braided.OpenPassages() > tree.OpenPassages(), "a braid of 15 % opens dead ends");
            Assert.True(wide.OpenPassages() > braided.OpenPassages());
            Assert.True(DeadEnds(wide) < DeadEnds(braided) && DeadEnds(braided) < DeadEnds(tree));
            Assert.True(Plan(seed).OpenPassages() > cells - 1, "the default maze has loops");
            for (int j = 0; j < tree.N; j++)
            {
                for (int i = 0; i < tree.N; i++)
                {
                    for (int dir = MazeRuinPlan.North; dir <= MazeRuinPlan.West; dir++)
                    {
                        Assert.True(!tree.IsOpen(i, j, dir) || braided.IsOpen(i, j, dir), "the braid only opens walls");
                    }
                }
            }
        }
    }

    [Fact]
    public void Halls_AreMerged2x2Rooms_UpToTheCountAsked()
    {
        foreach (long seed in Seeds)
        {
            Assert.Empty(Plan(seed, halls: 0).Halls);
            var plan = Plan(seed, MazeRuinPlan.Large, halls: 6);
            Assert.InRange(plan.Halls.Count, 1, 6);
            var b = Build(seed, ("size", MazeRuinPlan.Large), ("halls", "6"));
            for (int k = 0; k < plan.Halls.Count; k++)
            {
                var (i, j) = plan.Halls[k];
                Assert.Equal(MazeRuinPlan.FirstHallGroup + k, plan.Group(i + 1, j + 1));
                var (x0, z0, x1, z1) = plan.Box(i, j, i + 1, j + 1);
                for (int x = x0; x <= x1; x++)
                {
                    for (int z = z0; z <= z1; z++)
                    {
                        Assert.True(Walkable(b, x, z), $"hall {k} has a wall at ({x},{z})");
                    }
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(SizeKeys))]
    public void ThePlaza_IsOpen_WithTheBigStatueAndTheRichChest(string size)
    {
        foreach (long seed in Seeds.Take(2))
        {
            var plan = Plan(seed, size);
            var b = Build(seed, ("size", size));
            var (x0, z0, x1, z1) = plan.PlazaBox();
            Assert.True(x1 - x0 >= 15 && z1 - z0 >= 15, "the plaza has room for the big statue");
            for (int x = x0; x <= x1; x++)
            {
                for (int z = z0; z <= z1; z++)
                {
                    Assert.True(Walkable(b, x, z), $"{size}/{seed}: the plaza has a wall at ({x},{z})");
                }
            }

            var big = Assert.Single(b.Markers, m => m.Data == "pool=" + MazeRuinGenerator.LargeStatuePool);
            Assert.Equal((CivilizationRules.PropMarker, (x0 + x1) / 2, (z0 + z1) / 2), (big.Type, big.X, big.Z));
            var rich = Assert.Single(b.Markers, m => m.Data == "table=" + MazeRuinGenerator.RichTable);
            Assert.Equal(LandmarkRules.ChestMarker, rich.Type);
            Assert.InRange(rich.X, x0, x1);
            Assert.InRange(rich.Z, z0, z1);
        }
    }

    [Fact]
    public void StatuesAndChests_StandInTheHallsAndDeadEnds_OnTheOpenFloor()
    {
        int hidden = 0;
        foreach (long seed in Seeds)
        {
            var plan = Plan(seed);
            var b = Build(seed);
            Assert.All(b.Markers, m => Assert.True(m.Y == 1 && Walkable(b, m.X, m.Z), $"{seed}: marker {m} does not stand on the floor"));
            Assert.Equal(b.Markers.Count, b.Markers.Select(m => (m.X, m.Z)).Distinct().Count());
            var statues = b.Markers.Where(m => m.Type == CivilizationRules.PropMarker && m.Data == "pool=" + MazeRuinGenerator.StatuePool).ToList();
            Assert.True(statues.Count > plan.Halls.Count, $"{seed}: the dead ends carry statues besides the halls");
            var chests = b.Markers.Where(m => m.Type == LandmarkRules.ChestMarker && m.Data.StartsWith("table=" + MazeRuinGenerator.CommonTable, StringComparison.Ordinal)).ToList();
            Assert.NotEmpty(chests);
            Assert.All(chests, c => Assert.True(plan.IsDeadEnd(plan.CellX(c.X), plan.CellZ(c.Z)), $"{seed}: chest {c} is not in a dead end"));
            hidden += chests.Count(c => c.Data.EndsWith(";chance=" + MazeRuinGenerator.HiddenChestChance, StringComparison.Ordinal));

            // No statues asked: only the plaza's big one stands.
            var none = Build(seed, ("statues", "0"));
            Assert.Single(none.Markers, m => m.Type == CivilizationRules.PropMarker);
        }

        Assert.True(hidden > 0, "some dead-end chests are there only by chance");
    }

    [Fact]
    public void TheLowestWallRows_AreKept_AndNothingElse()
    {
        var b = Build(Seeds[1], ("size", MazeRuinPlan.Small));
        int walls = 0;
        for (int x = 0; x < b.Width; x++)
        {
            for (int z = 0; z < b.Length; z++)
            {
                bool wall = b.Get(x, 1, z) != 0;
                walls += wall ? 1 : 0;
                for (int y = 0; y < b.Height; y++)
                {
                    bool kept = wall && y >= 1 && y <= MazeRuinGenerator.KeepRows;
                    Assert.True(kept == b.IsKept(x, y, z), $"({x},{y},{z}) kept {b.IsKept(x, y, z)}, wanted {kept}");
                }
            }
        }

        Assert.Equal(walls * MazeRuinGenerator.KeepRows, b.Keep.Count);
    }

    [Fact]
    public void TheRuinPass_LeavesTheKeptRowsAndTheChestsStanding()
    {
        var entry = new StructureTemplate
        {
            Key = "test_maze",
            Landmark = true,
            Source = Key,
            SourceParams = "size=S",
            Ruin = LandmarkRules.RuinAlways,
            RuinProfile = LandmarkRules.ProfileMasonry,
            RuinStrength = 0.45,
        };
        foreach (long seed in Seeds.Take(2))
        {
            var built = SourceTemplates.Build(entry, Content, seed, "stone", null, WorldDescription.CurrentTerrainGeneration, out string failure);
            Assert.True(built is not null, failure);
            var layout = BuiltConverters.ToLandmarkLayout(built!, entry, seed, Content, "stone", LandmarkGenerator.RuinRevision);
            var s = layout.Structure;
            Assert.True(layout.Ruined);
            int high = 0, highLeft = 0;
            for (int x = 0; x < built!.Width; x++)
            {
                for (int y = 0; y < built.Height; y++)
                {
                    for (int z = 0; z < built.Length; z++)
                    {
                        if (built.IsKept(x, y, z))
                        {
                            Assert.True(s.Get(x, y, z) != 0, $"{seed}: the kept cell ({x},{y},{z}) fell");
                        }
                        else if (y > MazeRuinGenerator.KeepRows && built.Get(x, y, z) != 0)
                        {
                            high++;
                            highLeft += s.Get(x, y, z) != 0 ? 1 : 0;
                        }
                    }
                }
            }

            Assert.True(highLeft < high * 0.95, $"{seed}: the ruin pass took almost nothing ({highLeft} of {high} upper wall cells stand)");
            Assert.Equal(built.Markers.Count(m => m.Type == LandmarkRules.ChestMarker), s.Markers.Count(m => m.Type == LandmarkRules.ChestMarker));
        }
    }

    [Fact]
    public void TheBlocks_FollowTheParameters_AndAnUnknownKeyBuildsWithTheDefault()
    {
        var plain = Build(Seeds[0], ("size", MazeRuinPlan.Small));
        var custom = Build(Seeds[0], ("size", MazeRuinPlan.Small), ("wall", "stone"), ("accent", "gold_block"), ("floor", "sand"));
        var unknown = Build(Seeds[0], ("size", MazeRuinPlan.Small), ("wall", "no_such_block"));
        var plan = Plan(Seeds[0], MazeRuinPlan.Small);
        int lineX = plan.StartX[0] + plan.WidthX[0]; // the first inner wall line
        int z = plan.StartZ[0];
        Assert.Equal(Id(MazeRuinSource.DefaultFloor), plain.Get(plan.StartX[0], 0, z));
        Assert.Equal(Id("sand"), custom.Get(plan.StartX[0], 0, z));
        Assert.Equal(Id("stone"), custom.Get(0, 1, 0));
        Assert.Equal(Id(MazeRuinSource.DefaultWall), plain.Get(0, 1, 0));
        Assert.Equal(plain.ContentHash(), unknown.ContentHash());
        Assert.Contains(Id(MazeRuinSource.DefaultAccent), plain.Blocks);
        Assert.Contains(Id("gold_block"), custom.Blocks);
        Assert.Equal(plain.Get(lineX, 1, z) == 0, custom.Get(lineX, 1, z) == 0);
    }

    [Fact]
    public void TheWalls_StandAboutTheHeightAsked_WithPillarsAndABand()
    {
        var b = Build(Seeds[2], ("size", MazeRuinPlan.Small), ("height", "12"));
        var tops = new Dictionary<int, int>();
        int accentAtBand = 0, wallsOverBand = 0, band = 12 - 3;
        for (int x = 0; x < b.Width; x++)
        {
            for (int z = 0; z < b.Length; z++)
            {
                int top = -1;
                for (int y = b.Height - 1; y >= 1 && top < 0; y--)
                {
                    top = b.Get(x, y, z) != 0 ? y : -1;
                }

                if (top < 1)
                {
                    continue;
                }

                tops[top] = tops.TryGetValue(top, out int n) ? n + 1 : 1;
                wallsOverBand += top > band ? 1 : 0;
                accentAtBand += top > band && b.Get(x, band, z) == Id(MazeRuinSource.DefaultAccent) ? 1 : 0;
            }
        }

        Assert.All(tops.Keys, t => Assert.InRange(t, 11, 12 + MazeRuinGenerator.PillarRise + 2));
        Assert.True(tops.Keys.Count(t => t <= 13) == 3, "the wall sections stand 11, 12 and 13 high");
        Assert.Contains(tops.Keys, t => t >= 12 + MazeRuinGenerator.PillarRise);
        Assert.Equal(wallsOverBand, accentAtBand);
    }

    [Fact]
    public void Builds_AreSeedPure_AndHeightAndStatuesMoveNothingElse()
    {
        foreach (long seed in Seeds)
        {
            var a = Build(seed);
            var again = Build(seed);
            Assert.Equal(a.ContentHash(), again.ContentHash());
            Assert.Equal(a.Keep.OrderBy(i => i), again.Keep.OrderBy(i => i));
            Assert.Equal(a.Markers, again.Markers);
            Assert.NotEqual(a.ContentHash(), Build(seed + 1).ContentHash());

            var lower = Build(seed, ("height", "14"));
            var fewer = Build(seed, ("statues", "0"));
            Assert.Equal(a.Markers, lower.Markers);
            Assert.Equal(a.Blocks, fewer.Blocks);
            for (int x = 0; x < a.Width; x++)
            {
                for (int z = 0; z < a.Length; z++)
                {
                    Assert.Equal(Walkable(a, x, z), Walkable(lower, x, z));
                }
            }
        }
    }

    [Fact]
    public void AParamLine_AsACivilizationWritesIt_Parses()
    {
        var source = StructureSources.Get(Key)!;
        Assert.True(StructureSources.TryParseParams(source, Content, "size=L;braid=20;wall=basalt;floor=deepslate", out var values, out string error), error);
        Assert.Equal(MazeRuinPlan.Large, values["size"]);
        Assert.Equal("20", values["braid"]);
        Assert.Equal("size=L;braid=20", StructureSources.FormatParams(source, Content, values));
        Assert.False(StructureSources.TryParseParams(source, Content, "size=XL;height=99", out _, out error));
        Assert.Contains("size", error, StringComparison.Ordinal);
        Assert.Contains("height", error, StringComparison.Ordinal);
        Assert.Equal(StructureSourceKind.Surface, source.Kind);
        Assert.False(source.SupportsIntact);
    }

    [Fact]
    public void TopView_OfASmallMaze()
    {
        var b = Build(Seeds[1], ("size", MazeRuinPlan.Small));
        string view = TopView(b);
        _out.WriteLine(view);
        Assert.Equal(b.Length, view.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    private static int DeadEnds(MazeRuinPlan plan)
    {
        int n = 0;
        for (int j = 0; j < plan.N; j++)
        {
            for (int i = 0; i < plan.N; i++)
            {
                n += plan.IsDeadEnd(i, j) ? 1 : 0;
            }
        }

        return n;
    }

    /// <summary>The outer-ring columns an entrance opens (its cell's whole width, through the whole outer wall).</summary>
    private static IEnumerable<(int X, int Z)> EntranceSpan(MazeRuinPlan plan, MazeEntrance e)
    {
        bool alongX = e.Side is MazeRuinPlan.North or MazeRuinPlan.South;
        int start = alongX ? plan.StartX[e.Index] : plan.StartZ[e.Index];
        int width = alongX ? plan.WidthX[e.Index] : plan.WidthZ[e.Index];
        for (int k = start; k < start + width; k++)
        {
            for (int d = 0; d < MazeRuinPlan.OuterWall; d++)
            {
                yield return e.Side switch
                {
                    MazeRuinPlan.North => (k, d),
                    MazeRuinPlan.South => (k, plan.Length - 1 - d),
                    MazeRuinPlan.East => (plan.Width - 1 - d, k),
                    _ => (d, k),
                };
            }
        }
    }

    /// <summary>The outermost column of an entrance's middle.</summary>
    private static (int X, int Z) EntranceMouth(MazeRuinPlan plan, MazeEntrance e)
    {
        var span = EntranceSpan(plan, e).Where((_, k) => k % MazeRuinPlan.OuterWall == 0).ToList();
        return span[span.Count / 2];
    }

    /// <summary>Every column of the outer ring (the outermost line of the footprint).</summary>
    private static IEnumerable<(int X, int Z)> Ring(int w, int l)
    {
        for (int x = 0; x < w; x++)
        {
            yield return (x, 0);
            yield return (x, l - 1);
        }

        for (int z = 1; z < l - 1; z++)
        {
            yield return (0, z);
            yield return (w - 1, z);
        }
    }

    /// <summary>The walkable columns reached from <paramref name="start"/> (index <c>x · length + z</c>).</summary>
    private static bool[] FloodFrom(BuiltStructure b, (int X, int Z) start)
    {
        var reached = new bool[b.Width * b.Length];
        var queue = new Queue<(int X, int Z)>();
        Assert.True(Walkable(b, start.X, start.Z), $"the entrance mouth {start} is blocked");
        reached[(start.X * b.Length) + start.Z] = true;
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            var (x, z) = queue.Dequeue();
            foreach (var (nx, nz) in new[] { (x + 1, z), (x - 1, z), (x, z + 1), (x, z - 1) })
            {
                if (nx >= 0 && nz >= 0 && nx < b.Width && nz < b.Length && !reached[(nx * b.Length) + nz] && Walkable(b, nx, nz))
                {
                    reached[(nx * b.Length) + nz] = true;
                    queue.Enqueue((nx, nz));
                }
            }
        }

        return reached;
    }

    /// <summary>An ASCII top view for eyeballing: <c>#</c> wall, <c>I</c> pillar, <c>.</c> floor, <c>S</c> a statue,
    /// <c>B</c> the big statue, <c>C</c> a chest, <c>c</c> a chest there by chance.</summary>
    internal static string TopView(BuiltStructure b)
    {
        var marks = new Dictionary<(int, int), char>();
        foreach (var m in b.Markers)
        {
            marks[(m.X, m.Z)] = m.Data == "pool=" + MazeRuinGenerator.LargeStatuePool ? 'B'
                : m.Type == CivilizationRules.PropMarker ? 'S'
                : m.Data.Contains("chance=", StringComparison.Ordinal) ? 'c' : 'C';
        }

        var sb = new StringBuilder();
        int pillar = b.Height - MazeRuinGenerator.PillarRise;
        for (int z = 0; z < b.Length; z++)
        {
            for (int x = 0; x < b.Width; x++)
            {
                sb.Append(marks.TryGetValue((x, z), out char c) ? c
                    : b.Get(x, 1, z) == 0 ? '.'
                    : Enumerable.Range(pillar, MazeRuinGenerator.PillarRise).Any(y => b.Get(x, y, z) != 0) ? 'I' : '#');
            }

            sb.Append('\n');
        }

        return sb.ToString();
    }
}
