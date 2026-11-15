// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.Tests.Helpers;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>Shared pieces of the civilization world tests (#3642): small test-made civilizations — so nothing depends on the
/// shipped ones or on their blocks — and the server config. Their words borrow the shipped civilizations' locale keys, so the
/// tests can read the English texts.</summary>
internal static class CivilizationTestWorlds
{
    /// <summary>The land people: one hall on the start body (every meadow body of the start system is a home).</summary>
    public const string Land = "test_maze";

    /// <summary>The sea people: one sunken house on the start body (every body of its planet type in the start system is a home).</summary>
    public const string Sea = "test_deep";

    /// <summary>The far people: at home on an ocean body of another system, never on the start body.</summary>
    public const string Far = "test_far";

    /// <summary>The land people's VEGA lines, in their order: first, inside, the iron (a prefix), the glass, the statue — and a
    /// line for a prop pool no marker names, which never comes.</summary>
    public static readonly CivilizationVegaLine[] LandLines =
    {
        new() { Trigger = CivilizationRules.VegaFirst, Key = "vega.civ.labyrinth_builders.first" },
        new() { Trigger = CivilizationRules.VegaInside, Key = "vega.civ.labyrinth_builders.inside" },
        new() { Trigger = CivilizationRules.VegaBlock, Value = "iron_*", Key = "vega.civ.grosstechniker.mural" },
        new() { Trigger = CivilizationRules.VegaBlock, Value = "glass", Key = "vega.civ.grosstechniker.fluid" },
        new() { Trigger = CivilizationRules.VegaProp, Value = "statue", Key = "vega.civ.labyrinth_builders.statue" },
        new() { Trigger = CivilizationRules.VegaProp, Value = "statue_large", Key = "vega.civ.labyrinth_builders.centre" },
    };

    /// <summary>The shipped content with only the land and the far people (and, when asked, the glass tagged as the land
    /// people's).</summary>
    public static GameContent Content(bool tagGlass = false)
    {
        var content = With(new[] { LandPeople(), FarPeople() }, new[] { Hall() });
        if (tagGlass)
        {
            content.GetBlock("glass")!.Civilization = Land;
        }

        return content;
    }

    /// <summary>The shipped content with only the sea people, at home in the start system's bodies of <paramref name="planet"/>
    /// (a breathable meadow world has sea enough — and starts far quicker than an ocean world).</summary>
    public static GameContent SeaContent(string planet) => With(new[] { SeaPeople(planet) }, new[] { SunkenHouse() });

    private static GameContent With(CivilizationDefinition[] civilizations, StructureTemplate[] templates)
    {
        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        var warnings = new List<string>();
        content.SetCivilizations(civilizations, templates, warnings.Add);
        Assert.True(content.Civilizations.Count == civilizations.Length && content.CivilizationTemplates.Count == templates.Length,
            string.Join("\n", warnings));
        return content;
    }

    /// <summary>A small test world: no starter ship, no settlements, no landmark, no giant wreck.</summary>
    public static ServerConfig Config(string name, string planet, int generation = WorldDescription.CivilizationsGeneration)
    {
        var config = new ServerConfig
        {
            WorldName = name,
            Seed = 20261114,
            StartPlanet = planet,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = false,
            PlaceLandmarks = false,
            GiantWreckChance = 0.0,
        };
        config.World.TerrainGeneration = generation;
        return config;
    }

    private static CivilizationDefinition LandPeople() => new()
    {
        Key = Land,
        NameKey = "civ.labyrinth_builders.name",
        Scope = new CivilizationScope
        {
            Count = new List<int> { CivilizationRules.MaxHomes },
            PerSystem = 0,
            PlanetTypes = new List<string> { "meadowlands" },
            StartSystem = CivilizationRules.StartOnly,
        },
        Sites = new List<CivilizationSitePool> { new() { Pool = CivilizationRules.PoolGiant, NameKey = "civ.labyrinth_builders.site.maze" } },
        Vega = new List<CivilizationVegaLine>(LandLines),
        Codex = new CivilizationCodex { TitleKey = "civ.labyrinth_builders.codex.title", TextKey = "civ.labyrinth_builders.codex.text" },
        Rumours = new List<string> { "npc.rumour.civ.labyrinth_builders.1" },
    };

    private static CivilizationDefinition SeaPeople(string planet) => new()
    {
        Key = Sea,
        NameKey = "civ.grosstechniker.name",
        Scope = new CivilizationScope
        {
            Count = new List<int> { CivilizationRules.MaxHomes },
            PerSystem = 0,
            PlanetTypes = new List<string> { planet },
            StartSystem = CivilizationRules.StartOnly,
            Underwater = true,
        },
        Sites = new List<CivilizationSitePool> { new() { Pool = CivilizationRules.PoolGiant, NameKey = "civ.grosstechniker.site.city" } },
        Vega = new List<CivilizationVegaLine> { new() { Trigger = CivilizationRules.VegaAir, Key = "vega.civ.grosstechniker.air" } },
    };

    private static CivilizationDefinition FarPeople() => new()
    {
        Key = Far,
        NameKey = "civ.grosstechniker.name",
        Scope = new CivilizationScope
        {
            Count = new List<int> { 1 },
            PerSystem = 1,
            PlanetTypes = new List<string> { "ocean" },
            StartSystem = CivilizationRules.StartNever,
        },
        Guarantee = new CivilizationGuarantee { PlanetTypes = new List<string> { "ocean" } },
        Sites = new List<CivilizationSitePool> { new() { Pool = CivilizationRules.PoolGiant } },
        Rumours = new List<string> { "npc.rumour.civ.grosstechniker.1", "npc.rumour.civ.grosstechniker.2" },
    };

    /// <summary>The land people's hall, 30 × 6 × 12 as authored, never a ruin: a stone floor, one iron wall in a corner, one
    /// glass block in the far corner and a statue marker (no prop of that pool exists, so the marker stays bare).</summary>
    private static StructureTemplate Hall()
    {
        var t = new StructureTemplate
        {
            Key = "test_maze_hall",
            Name = "Test Hall",
            Kind = CivilizationRules.TemplateKind,
            Civilization = Land,
            CivPool = CivilizationRules.PoolGiant,
            Ruin = LandmarkRules.RuinNever,
            Width = 30,
            Height = 6,
            Length = 12,
        };
        for (int x = 0; x < 30; x++)
        {
            for (int z = 0; z < 12; z++)
            {
                t.Cells.Add(LandmarkShapes.Block(x, 0, z, "stone"));
            }
        }

        t.Cells.Add(LandmarkShapes.Block(1, 1, 1, "iron_wall"));
        t.Cells.Add(LandmarkShapes.Block(28, 1, 10, "glass"));
        t.Cells.Add(LandmarkShapes.Marker(6, 1, 10, CivilizationRules.PropMarker, "pool=statue"));
        return t;
    }

    /// <summary>The sea people's house, 9 × 5 × 9: a stone box around one big air pocket (a diving bell).</summary>
    private static StructureTemplate SunkenHouse()
    {
        var t = new StructureTemplate
        {
            Key = "test_deep_house",
            Name = "Test House",
            Kind = CivilizationRules.TemplateKind,
            Civilization = Sea,
            CivPool = CivilizationRules.PoolGiant,
            Ruin = LandmarkRules.RuinNever,
            Width = 9,
            Height = 5,
            Length = 9,
        };
        for (int x = 0; x < 9; x++)
        {
            for (int y = 0; y < 5; y++)
            {
                for (int z = 0; z < 9; z++)
                {
                    bool shell = x == 0 || z == 0 || x == 8 || z == 8 || y == 0 || y == 4;
                    t.Cells.Add(shell
                        ? LandmarkShapes.Block(x, y, z, "stone")
                        : new TemplateCell { X = x, Y = y, Z = z, Kind = CivilizationRules.AirKind });
                }
            }
        }

        return t;
    }

    /// <summary>A player position at a structure-local cell of the site (feet in that cell, X wrapped).</summary>
    public static Vector3f At(SvGameServer server, SvGameServer.LandmarkInstance site, int x, int y, int z)
        => new(WorldConstants.WrapX(site.Origin.X + x, server.World.Circumference) + 0.5f, site.Origin.Y + y, site.Origin.Z + z + 0.5f);

    /// <summary>The first local cell of the site's layout that holds <paramref name="block"/>.</summary>
    public static Vector3i CellOf(GameContent content, SvGameServer.LandmarkInstance site, string block)
    {
        ushort id = content.GetBlock(block)!.NumericId.Value;
        foreach (var c in GiantWreckTestWorlds.AllCells(site.Structure))
        {
            if (site.Structure.Get(c.X, c.Y, c.Z) == id)
            {
                return c;
            }
        }

        throw new Xunit.Sdk.XunitException($"the site holds no {block}");
    }
}
