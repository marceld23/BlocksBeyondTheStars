// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.Tests.Helpers;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3639–#3641: the civilization framework's pure parts — the checks of a civilization file and its templates, the home
/// selection (seed-pure over the fixed systems, per-system caps, never two civilizations on one body), the count roll, the
/// air pocket cell kind through the compact template body, and the ocean guarantee of a generation-43 galaxy.
/// </summary>
public sealed class CivilizationFrameworkTests
{
    private static readonly GameContent Shipped = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    internal static CivilizationDefinition Civ(string key, string[]? types = null, string[]? atmospheres = null, int min = 1, int max = 1,
        int perSystem = 1, string start = CivilizationRules.StartAllowed, bool underwater = false) => new()
        {
            Key = key,
            NameKey = "civ." + key + ".name",
            Scope = new CivilizationScope
            {
                Count = new List<int> { min, max },
                PerSystem = perSystem,
                PlanetTypes = (types ?? System.Array.Empty<string>()).ToList(),
                Atmospheres = (atmospheres ?? System.Array.Empty<string>()).ToList(),
                Conditions = new List<string> { CivilizationRules.ConditionNotAsteroid, CivilizationRules.ConditionNotGas, CivilizationRules.ConditionNotVoid },
                StartSystem = start,
                Underwater = underwater,
            },
            Sites = new List<CivilizationSitePool> { new() { Pool = CivilizationRules.PoolGiant } },
        };

    private static GameContent ContentWith(params CivilizationDefinition[] civs)
    {
        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        content.SetCivilizations(civs, System.Array.Empty<StructureTemplate>());
        return content;
    }

    /// <summary>A galaxy of <paramref name="systems"/> systems, each with planets of the given types (ids <c>sysN-pM</c>).</summary>
    private static Galaxy GalaxyOf(int systems, params string[] typesPerSystem)
    {
        var galaxy = new Galaxy();
        for (int s = 0; s < systems; s++)
        {
            var system = new StarSystem { Id = "sys" + s };
            for (int p = 0; p < typesPerSystem.Length; p++)
            {
                system.Bodies.Add(new CelestialBody { Id = $"sys{s}-p{p}", Kind = CelestialKind.Planet, PlanetType = typesPerSystem[p], SystemId = system.Id });
            }

            galaxy.Systems.Add(system);
        }

        return galaxy;
    }

    [Fact]
    public void TheShippedCivilizations_AreFramework_AndTheirFilesAreFine()
    {
        var gt = Shipped.GetCivilization("grosstechniker");
        var lb = Shipped.GetCivilization("labyrinth_builders");
        Assert.NotNull(gt);
        Assert.NotNull(lb);
        Assert.True(gt!.IsFramework && gt.Scope.Underwater);
        Assert.Equal(WorldDescription.CivilizationsGeneration, gt.MinTerrainGeneration);
        Assert.Equal(new[] { "ocean", "rainbow_sea" }, gt.Scope.PlanetTypes);
        Assert.Equal(new[] { "toxic", "none" }, lb!.Scope.Atmospheres);
        Assert.Equal((1, 3), CivilizationRules.RangeOf(lb.Scope.Count));
        Assert.Contains(StructureTypeRules.Landmark, gt.HomeBody.Suppress);
        Assert.Contains(StructureTypeRules.GiantWreck, lb.HomeBody.Suppress);
    }

    [Fact]
    public void ABrokenCivilization_IsDropped_NeverTheLoad()
    {
        var warnings = new List<string>();
        var broken = Civ("broken");
        broken.Scope.Atmospheres.Add("lava");
        broken.HomeBody.Suppress.Add("settlement");
        broken.Materials["@wall"] = "no_such_block";
        broken.Vega.Add(new CivilizationVegaLine { Trigger = CivilizationRules.VegaBlock, Key = "x" });
        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        content.SetCivilizations(new[] { broken, Civ("fine") }, System.Array.Empty<StructureTemplate>(), warnings.Add);
        Assert.Null(content.GetCivilization("broken"));
        Assert.NotNull(content.GetCivilization("fine"));
        string warning = Assert.Single(warnings, w => w.Contains("'broken'", System.StringComparison.Ordinal));
        Assert.Contains("lava", warning, System.StringComparison.Ordinal);
        Assert.Contains("settlement", warning, System.StringComparison.Ordinal);
        Assert.Contains("no_such_block", warning, System.StringComparison.Ordinal);
        Assert.Contains("needs a value", warning, System.StringComparison.Ordinal);
    }

    [Fact]
    public void ALostCivilizationsTemplate_RefusesResidentsAndDens_AndANamesItsPool()
    {
        var civ = Civ("lost_test");
        var good = new StructureTemplate { Key = "ok", Civilization = "lost_test", CivPool = CivilizationRules.PoolGiant, Width = 2, Height = 2, Length = 2 };
        good.Cells.Add(new TemplateCell { X = 0, Y = 0, Z = 0, Kind = "block", Id = "stone" });
        good.Cells.Add(new TemplateCell { X = 1, Y = 0, Z = 1, Kind = "marker", Id = LandmarkRules.ChestMarker });
        good.Cells.Add(new TemplateCell { X = 1, Y = 1, Z = 1, Kind = "marker", Id = CivilizationRules.PropMarker, Config = "pool=bits" });
        Assert.Empty(CivilizationRules.TemplateProblems(good, civ));

        var den = new StructureTemplate { Key = "den", Civilization = "lost_test", CivPool = CivilizationRules.PoolSmall, Width = 2, Height = 2, Length = 2 };
        den.Cells.Add(new TemplateCell { X = 0, Y = 0, Z = 0, Kind = "marker", Id = LandmarkRules.DenMarker });
        Assert.Contains(CivilizationRules.TemplateProblems(den, civ), p => p.Contains("nobody home", System.StringComparison.Ordinal));

        var noPool = new StructureTemplate { Key = "np", Civilization = "lost_test", CivPool = "tower", Width = 1, Height = 1, Length = 1 };
        Assert.Contains(CivilizationRules.TemplateProblems(noPool, civ), p => p.Contains("civPool", System.StringComparison.Ordinal));
        Assert.Contains(CivilizationRules.TemplateProblems(good, null), p => p.Contains("unknown civilization", System.StringComparison.Ordinal));
    }

    [Fact]
    public void TheSeabedSeat_IsACivilizationTemplatesAlone()
    {
        var civTemplate = new StructureTemplate { Key = "a", Civilization = "x", Seat = LandmarkRules.SeatSeabed, Width = 1, Height = 1, Length = 1 };
        var landmark = new StructureTemplate { Key = "b", Seat = LandmarkRules.SeatSeabed, Width = 1, Height = 1, Length = 1 };
        Assert.DoesNotContain(LandmarkRules.Problems(civTemplate), p => p.Contains("seat", System.StringComparison.Ordinal));
        Assert.Contains(LandmarkRules.Problems(landmark), p => p.Contains("seat", System.StringComparison.Ordinal));
    }

    [Fact]
    public void AnAirPocket_SurvivesTheCompactBody_AsACavityThatIsAir()
    {
        var t = new StructureTemplate { Key = "bell", Width = 3, Height = 3, Length = 1 };
        t.Cells.Add(new TemplateCell { X = 0, Y = 0, Z = 0, Kind = "block", Id = "stone" });
        t.Cells.Add(new TemplateCell { X = 1, Y = 1, Z = 0, Kind = CivilizationRules.AirKind });
        t.Cells.Add(new TemplateCell { X = 2, Y = 1, Z = 0, Kind = LandmarkRules.CavityKind });
        TemplateBody.Compact(t);
        Assert.Contains(t.Palette, p => p.Kind == CivilizationRules.AirKind);
        var plain = new List<TemplateBody.Plain>();
        TemplateBody.ForEachPlain(t, plain.Add);
        var air = Assert.Single(plain, p => p.Air);
        Assert.True(air.Cavity);
        Assert.Equal((1, 1), (air.X, air.Y));
        Assert.False(Assert.Single(plain, p => p.X == 2).Air);

        TemplateBody.Expand(t);
        Assert.Contains(t.Cells, c => c.Kind == CivilizationRules.AirKind && c.X == 1 && c.Y == 1);
        Assert.Contains(t.Cells, c => c.Kind == LandmarkRules.CavityKind && c.X == 2);
    }

    [Fact]
    public void Homes_AreSeedPure_OnlyInTheFixedSystems_OnePerSystem_NeverShared()
    {
        var sea = Civ("sea", types: new[] { "ocean" }, min: 3, max: 3);
        var meadow = Civ("meadow", types: new[] { "ocean", "meadowlands" }, min: 4, max: 4);
        var content = ContentWith(sea, meadow);
        var galaxy = GalaxyOf(8, "ocean", "meadowlands");
        var a = CivilizationHomes.Pick(galaxy, content, 42, fixedSystems: 6, WorldDescription.CivilizationsGeneration, "sys0");
        var b = CivilizationHomes.Pick(galaxy, content, 42, fixedSystems: 6, WorldDescription.CivilizationsGeneration, "sys0");
        Assert.Equal(a["sea"], b["sea"]);
        Assert.Equal(a["meadow"], b["meadow"]);
        Assert.Equal(3, a["sea"].Count);
        Assert.Equal(4, a["meadow"].Count);
        var all = a["sea"].Concat(a["meadow"]).ToList();
        Assert.Equal(all.Count, all.Distinct().Count()); // never two civilizations on one body
        Assert.All(all, id => Assert.False(id.StartsWith("sys6", System.StringComparison.Ordinal) || id.StartsWith("sys7", System.StringComparison.Ordinal)));
        Assert.Equal(a["sea"].Count, a["sea"].Select(id => id.Split('-')[0]).Distinct().Count()); // one per system

        // Growth appends systems: the homes of the fixed prefix never move.
        var grown = GalaxyOf(12, "ocean", "meadowlands");
        Assert.Equal(a["sea"], CivilizationHomes.Pick(grown, content, 42, 6, WorldDescription.CivilizationsGeneration, "sys0")["sea"]);
        Assert.NotEqual(a["sea"], CivilizationHomes.Pick(galaxy, content, 43, 6, WorldDescription.CivilizationsGeneration, "sys0")["sea"]);
    }

    [Fact]
    public void Homes_FollowAtmosphereStartRuleAndGeneration_AndAreNoneWhenNothingFits()
    {
        var toxic = Civ("toxic", atmospheres: new[] { CivilizationRules.AtmosphereToxic, CivilizationRules.AtmosphereNone }, min: 2, max: 2,
            start: CivilizationRules.StartNever);
        var content = ContentWith(toxic, Civ("nowhere", types: new[] { "ocean" }));
        string toxicType = content.Planets.Values.First(p => p.Atmosphere == "toxic" && !p.RestrictStructures && !p.Void && !p.IsGasWorld
                                                             && !WorldConstants.IsAsteroidType(p.Key)).Key;
        var galaxy = GalaxyOf(4, "meadowlands", toxicType);
        var homes = CivilizationHomes.Pick(galaxy, content, 7, 4, WorldDescription.CivilizationsGeneration, "sys0");
        Assert.Equal(2, homes["toxic"].Count);
        Assert.All(homes["toxic"], id => Assert.EndsWith("-p1", id, System.StringComparison.Ordinal));
        Assert.DoesNotContain(homes["toxic"], id => id.StartsWith("sys0-", System.StringComparison.Ordinal));
        Assert.Empty(homes["nowhere"]);
        Assert.Empty(CivilizationHomes.Pick(galaxy, content, 7, 4, WorldDescription.CivilizationsGeneration - 1, "sys0"));
    }

    [Fact]
    public void TheCountRoll_FollowsItsWeights()
    {
        var scope = new CivilizationScope { Count = new List<int> { 1, 3 }, CountWeights = new List<double> { 0.2, 0.3, 0.5 } };
        var seen = new int[4];
        for (long seed = 0; seed < 2000; seed++)
        {
            seen[CivilizationHomes.RollCount(scope, seed, "lb")]++;
        }

        Assert.Equal(0, seen[0]);
        Assert.InRange(seen[1], 300, 500);
        Assert.InRange(seen[2], 480, 720);
        Assert.InRange(seen[3], 880, 1120);
        Assert.Equal(2, CivilizationHomes.RollCount(new CivilizationScope { Count = new List<int> { 2 } }, 1, "x"));
    }

    [Fact]
    public void AGeneration43Galaxy_GuaranteesTheCivilizationsType_AnOlderOneStaysAsItWas()
    {
        var civ = Civ("guaranteed", types: new[] { "rainbow_sea" });
        civ.Guarantee.PlanetTypes.Add("rainbow_sea");
        var content = ContentWith(civ);
        var plain = ContentWith();
        int missingBefore = 0;
        for (long seed = 1; seed <= 40; seed++)
        {
            var desc = new WorldDescription { TerrainGeneration = WorldDescription.CivilizationsGeneration };
            var without = new UniverseGenerator(seed, desc, plain).Generate();
            var with = new UniverseGenerator(seed, desc, content).Generate();
            bool Has(Galaxy g) => g.Systems.Take(desc.StarSystemCount).SelectMany(s => s.Bodies).Any(b => b.PlanetType == "rainbow_sea");
            missingBefore += Has(without) ? 0 : 1;
            Assert.True(Has(with), $"seed {seed}: no rainbow sea in the fixed systems");

            var older = new WorldDescription { TerrainGeneration = WorldDescription.CivilizationsGeneration - 1 };
            Assert.Equal(Types(new UniverseGenerator(seed, older, plain).Generate()), Types(new UniverseGenerator(seed, older, content).Generate()));
        }

        Assert.True(missingBefore > 0, "every sampled galaxy already had one — the test proves nothing");
    }

    private static List<string> Types(Galaxy g) => g.AllBodies().Select(b => b.Id + ":" + b.PlanetType).ToList();
}
