// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;
using Xunit.Abstractions;

namespace BlocksBeyondTheStars.Tests;

/// <summary>#3586/#3587/#3590: the settlement planners — deterministic, pinned losslessly, every building on its own lot
/// with its door on a footpath to a connected road net, the plaza's functions present, the heights stepping by one block.</summary>
public sealed class SettlementPlannerTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    private static readonly List<StructureTemplate> Pool = Content.SettlementTemplates.Where(t => t.IsModule).ToList();
    private readonly ITestOutputHelper _out;

    public SettlementPlannerTests(ITestOutputHelper output) => _out = output;

    public static IEnumerable<object[]> Kits()
    {
        foreach (string tier in new[] { "hamlet", "village", "town", "city" })
        {
            yield return new object[] { $"{tier}_grown_1", tier };
            yield return new object[] { $"{tier}_grid_1", tier };
        }
    }

    /// <summary>A rolling site: slopes up to about a block per column.</summary>
    internal static int Hills(int x, int z) => 64 + (int)Math.Round((7 * Math.Sin(x / 9.0)) + (5 * Math.Cos(z / 7.0)));

    private static SettlementPlan PlanFor(string kit, string tier, long seed, bool hills = true, string form = "", int water = -1)
        => SettlementPlanner.Plan(new SettlementPlanRequest
        {
            Tier = tier,
            Seed = seed,
            Kit = Content.KitByKey(kit)!,
            Pool = Pool,
            Ground = hills ? Hills : null,
            Form = form,
            WaterSide = water,
        });

    [Theory]
    [MemberData(nameof(Kits))]
    public void Plan_IsDeterministic_AndPinsLosslessly(string kit, string tier)
    {
        for (long seed = 1; seed <= 6; seed++)
        {
            var a = PlanFor(kit, tier, seed);
            var b = PlanFor(kit, tier, seed);
            Assert.Equal(a.Serialize(), b.Serialize());
            Assert.True(SettlementPlan.TryParse(a.Serialize(), out var back));
            Assert.Equal(a.Serialize(), back.Serialize());
        }
    }

    [Theory]
    [MemberData(nameof(Kits))]
    public void Plan_HasItsServices_AndNoTwoLotsOverlap(string kit, string tier)
    {
        for (long seed = 1; seed <= 12; seed++)
        {
            var plan = PlanFor(kit, tier, seed);
            var functions = plan.Lots.Select(l => l.Function).ToList();
            Assert.Contains(StructureRoles.Market, functions);
            Assert.Contains(StructureRoles.Board, functions);
            Assert.True(plan.Lots.Count(l => l.Module.Length > 0) >= 3, $"{kit} seed {seed}: only {plan.Lots.Count} lots");
            var seen = new HashSet<(int, int)>();
            foreach (var lot in plan.Lots)
            {
                Assert.True(lot.X >= 1 && lot.Z >= 1 && lot.X + lot.W <= plan.Width - 1 && lot.Z + lot.L <= plan.Length - 1, $"{kit} seed {seed}: a lot leaves the box");
                for (int x = lot.X; x < lot.X + lot.W; x++)
                    for (int z = lot.Z; z < lot.Z + lot.L; z++)
                        Assert.True(seen.Add((x, z)), $"{kit} seed {seed}: two lots share ({x},{z})");
            }
        }
    }

    [Theory]
    [MemberData(nameof(Kits))]
    public void EveryDoor_ReachesThePlaza_OverOneConnectedRoadNet(string kit, string tier)
    {
        for (long seed = 1; seed <= 12; seed++)
        {
            var plan = PlanFor(kit, tier, seed);
            var walk = Reachable(plan);
            foreach (var lot in plan.Lots)
            {
                Assert.True(Frontage(plan, lot).Any(walk.Contains), $"{kit} seed {seed}: lot {lot.Module} at ({lot.X},{lot.Z}) has no footpath to the plaza");
            }
        }
    }

    [Theory]
    [MemberData(nameof(Kits))]
    public void Heights_StepByOneBlock_AndStayWithinTheRise(string kit, string tier)
    {
        for (long seed = 1; seed <= 12; seed++)
        {
            var plan = PlanFor(kit, tier, seed);
            Assert.True(plan.MaxHeight() <= SettlementPlanner.MaxRise, $"{kit} seed {seed}: rises {plan.MaxHeight()}");
            for (int x = 0; x < plan.Width; x++)
                for (int z = 0; z < plan.Length; z++)
                {
                    if (!SettlementPlan.IsWalkable(plan.KindAt(x, z)))
                    {
                        continue;
                    }

                    foreach (var (nx, nz) in new[] { (x + 1, z), (x, z + 1) })
                    {
                        if (SettlementPlan.IsWalkable(plan.KindAt(nx, nz)))
                        {
                            Assert.True(Math.Abs(plan.HeightAt(x, z) - plan.HeightAt(nx, nz)) <= 1,
                                $"{kit} seed {seed}: a step of {plan.HeightAt(x, z) - plan.HeightAt(nx, nz)} at ({x},{z})");
                        }
                    }
                }
        }
    }

    [Fact]
    public void FlatGround_PlansOneLevel()
    {
        var plan = PlanFor("village_grown_1", "village", 3, hills: false);
        Assert.Equal(0, plan.MaxHeight());
        Assert.Null(plan.GroundBase);
    }

    [Fact]
    public void TheBankVillage_KeepsItsHousesOffTheWaterSide()
    {
        var plan = PlanFor("village_grown_1", "village", 7, form: "bank", water: 0);
        Assert.Equal("bank", plan.Form);
        Assert.Equal("street", PlanFor("village_grown_1", "village", 7, form: "bank").Form); // no water — a street village
    }

    [Theory]
    [InlineData("street")]
    [InlineData("green")]
    [InlineData("round")]
    [InlineData("cluster")]
    [InlineData("cross")]
    public void EveryForm_Plans(string form)
    {
        var plan = PlanFor("village_grown_1", "village", 11, form: form);
        Assert.Equal(form, plan.Form);
        Assert.Contains(plan.Lots, l => l.Function == StructureRoles.Market);
        _out.WriteLine(Draw(plan));
    }

    [Theory]
    [MemberData(nameof(Kits))]
    public void Bake_CarriesTheColumnMap_ThePlaza_AndTheServices(string kit, string tier)
    {
        var plan = PlanFor(kit, tier, 5);
        var s = SettlementPlanBaker.Bake(plan, tier, ruined: false, 5, "grass", Content, Pool);
        Assert.True(s.HasColumnMap);
        Assert.Contains(s.Markers, m => m.Type == SettlementPlanBaker.PlazaMarker);
        Assert.Contains(s.Markers, m => m.Type == "vendor");
        Assert.Contains(s.Markers, m => m.Type == "mission_board");
        Assert.Contains(s.Markers, m => m.Type == SettlementPlanBaker.RoadExitMarker);
        Assert.Contains(Enumerable.Range(0, s.Width * s.Length), i => s.FloorAt(i / s.Length, i % s.Length) < 0); // not a box
        var again = SettlementPlanBaker.Bake(plan, tier, ruined: false, 5, "grass", Content, Pool);
        for (int x = 0; x < s.Width; x++)
            for (int y = 0; y < s.Height; y++)
                for (int z = 0; z < s.Length; z++)
                    Assert.Equal(s.Get(x, y, z), again.Get(x, y, z));
    }

    [Fact]
    public void Bake_TheFarms_MarkTheirFieldsPensAndCrates()
    {
        bool field = false, pen = false, box = false;
        for (long seed = 1; seed <= 10 && !(field && pen && box); seed++)
        {
            var plan = PlanFor("village_grown_1", "village", seed);
            var s = SettlementPlanBaker.Bake(plan, "village", ruined: false, seed, "grass", Content, Pool);
            field |= s.Markers.Any(m => m.Type == SettlementPlanBaker.FieldMarker && m.Data.Contains("w="));
            pen |= s.Markers.Any(m => m.Type == SettlementPlanBaker.PenMarker);
            box |= s.Markers.Any(m => m.Type == SettlementPlanBaker.PenBoxMarker);
        }

        Assert.True(field && pen && box, $"field {field}, pen {pen}, crate {box}");
    }

    [Fact]
    public void Bake_ARuin_KeepsItsBuildingsOnTheirFloors()
    {
        var plan = PlanFor("town_grown_1", "town", 9);
        var s = SettlementPlanBaker.Bake(plan, "town", ruined: true, 9, "grass", Content, Pool);
        Assert.True(s.Ruined);
        Assert.DoesNotContain(s.Markers, m => m.Type is "vendor" or "npc" or "mission_board");
        Assert.Contains(s.Markers, m => m.Type == "loot");
        foreach (var lot in plan.Lots.Where(l => l.Y > 0))
        {
            for (int y = 0; y < lot.Y; y++)
            {
                Assert.Equal(0, s.Get(lot.X + (lot.W / 2), y, lot.Z + (lot.L / 2))); // the support is gone again
            }
        }
    }

    [Fact]
    public void Draw_AFewPlans()
    {
        foreach (var (kit, tier, seed) in new[] { ("village_grown_1", "village", 21L), ("village_grown_1", "village", 6L), ("town_grid_1", "town", 21L), ("city_grown_1", "city", 21L) })
        {
            var plan = PlanFor(kit, tier, seed);
            _out.WriteLine($"{kit} — {plan.Form} — {plan.Lots.Count} lots");
            _out.WriteLine(Draw(plan));
        }
    }

    /// <summary>The columns a walker reaches from the plaza over walkable ground (and a level green).</summary>
    private static HashSet<(int, int)> Reachable(SettlementPlan plan)
    {
        bool Open(int x, int z) => SettlementPlan.IsWalkable(plan.KindAt(x, z)) || plan.KindAt(x, z) == SettlementPlan.Green;
        var seen = new HashSet<(int, int)>();
        var queue = new Queue<(int, int)>();
        queue.Enqueue((plan.PlazaX, plan.PlazaZ));
        seen.Add((plan.PlazaX, plan.PlazaZ));
        while (queue.Count > 0)
        {
            var (x, z) = queue.Dequeue();
            foreach (var n in new[] { (x + 1, z), (x - 1, z), (x, z + 1), (x, z - 1) })
            {
                if (Open(n.Item1, n.Item2) && seen.Add(n))
                {
                    queue.Enqueue(n);
                }
            }
        }

        return seen;
    }

    private static IEnumerable<(int, int)> Frontage(SettlementPlan plan, PlanLot lot)
    {
        for (int x = lot.X; x < lot.X + lot.W; x++)
        {
            yield return (x, lot.Z - 1);
            yield return (x, lot.Z + lot.L);
        }

        for (int z = lot.Z; z < lot.Z + lot.L; z++)
        {
            yield return (lot.X - 1, z);
            yield return (lot.X + lot.W, z);
        }
    }

    internal static string Draw(SettlementPlan plan)
    {
        var sb = new StringBuilder();
        for (int z = 0; z < plan.Length; z++)
        {
            for (int x = 0; x < plan.Width; x++)
            {
                var lot = plan.Lots.FirstOrDefault(l => l.Covers(x, z));
                char c = plan.KindAt(x, z) switch
                {
                    SettlementPlan.MainRoad => '=',
                    SettlementPlan.Lane => '-',
                    SettlementPlan.Footpath => ':',
                    SettlementPlan.Plaza => '#',
                    SettlementPlan.Green => '"',
                    SettlementPlan.Yard => ',',
                    SettlementPlan.LotColumn => lot is null ? '?' : lot.Function.Length > 0 ? char.ToUpperInvariant(lot.Function[0]) : 'H',
                    _ => ' ',
                };
                sb.Append(c);
            }

            sb.Append("  ");
            for (int x = 0; x < plan.Width; x++)
            {
                sb.Append(plan.KindAt(x, z) == SettlementPlan.Unclaimed ? ' ' : (char)('0' + plan.HeightAt(x, z)));
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }
}
