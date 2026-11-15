// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.Tests.Helpers;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3607, #3608: the rules of a giant ship template (<see cref="GiantShipRules.Problems"/> — one test per problem), the
/// <c>giant_wreck</c> placement rule, terrain generation 42, the <see cref="WorldDescription.GiantWrecks"/> world option and
/// <see cref="ServerConfig.GiantWreckChance"/>.
/// </summary>
public sealed class GiantShipRulesTests
{
    private static readonly HashSet<string> KnownBlocks = new(StringComparer.Ordinal) { "iron_wall", "glass", "ladder", "water" };

    private static List<string> Problems(StructureTemplate t)
        => GiantShipRules.Problems(t, KnownBlocks.Contains, id => id is not ("ladder" or "water"));

    private static StructureTemplate Ship() => GiantShipTemplateTests.Ship();

    private static void AssertOneProblem(StructureTemplate t, string fragment)
    {
        var problems = Problems(t);
        Assert.True(problems.Count == 1 && problems[0].Contains(fragment, StringComparison.Ordinal),
            $"expected one problem with '{fragment}', got: {string.Join(" / ", problems)}");
    }

    [Fact]
    public void AValidShip_HasNoProblem_InEitherBodyForm()
    {
        Assert.Empty(Problems(Ship()));
        Assert.Empty(Problems(LandmarkShapes.Compacted(Ship())));
    }

    [Theory]
    [InlineData(0, 4, 5)]
    [InlineData(6, 0, 5)]
    [InlineData(6, 4, -1)]
    public void ASizeOfZero_IsAProblem(int width, int height, int length)
    {
        var t = Ship();
        (t.Width, t.Height, t.Length) = (width, height, length);
        Assert.Contains(Problems(t), p => p.Contains("no size", StringComparison.Ordinal));
    }

    [Fact]
    public void MoreThan256OnAnAxis_IsAProblem_256IsFine()
    {
        var t = Ship();
        t.Width = GiantShipRules.MaxSize;
        Assert.Empty(Problems(t));
        t.Height = GiantShipRules.MaxSize + 1;
        AssertOneProblem(t, "larger than 256");
    }

    [Fact]
    public void NoBody_IsAProblem()
    {
        var t = Ship();
        t.Cells.RemoveAll(c => c.Kind == "block");
        Assert.Contains(Problems(t), p => p.Contains("no body", StringComparison.Ordinal));
    }

    [Fact]
    public void AnUnknownBlock_OrAMaterialToken_IsAProblem()
    {
        var t = Ship();
        t.Cells.Add(LandmarkShapes.Block(0, 1, 0, "no_such_block"));
        t.Cells.Add(LandmarkShapes.Block(5, 1, 4, MaterialTokens.Wall));
        AssertOneProblem(t, "no_such_block");
        Assert.Contains(MaterialTokens.Wall, Problems(t)[0], StringComparison.Ordinal);
    }

    [Fact]
    public void ABlockOutsideTheBox_IsAProblem()
    {
        var t = Ship();
        t.Cells.Add(LandmarkShapes.Block(6, 0, 0, "iron_wall"));
        AssertOneProblem(t, "outside the structure's box");
    }

    [Fact]
    public void AGroundRowOutsideTheStructure_IsAProblem()
    {
        var t = Ship();
        t.GroundRow = 3;
        Assert.Empty(Problems(t));
        t.GroundRow = 4;
        AssertOneProblem(t, "ground row");
        t.GroundRow = -1;
        AssertOneProblem(t, "ground row");
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    [InlineData(double.NaN)]
    public void ARuinStrengthOutside0To1_IsAProblem(double strength)
    {
        var t = Ship();
        t.RuinStrength = strength;
        AssertOneProblem(t, "ruin strength");
    }

    [Fact]
    public void TheStyle_IsEmptyOrAlien()
    {
        var t = Ship();
        t.Style = GiantShipRules.StyleAlien;
        Assert.Empty(Problems(t));
        t.Style = "steampunk";
        AssertOneProblem(t, "unknown style 'steampunk'");
    }

    [Fact]
    public void RuinZones_TakeCrashOrHull_InsideTheBox_WithAStrengthIn0To1()
    {
        var t = Ship();
        t.RuinZones.Add(new RuinZone { X0 = 0, Y0 = 0, Z0 = 0, X1 = 5, Y1 = 3, Z1 = 4, Profile = LandmarkRules.ProfileCrash, Strength = 1 });
        t.RuinZones.Add(new RuinZone { X1 = 2, Y1 = 2, Z1 = 2, Profile = LandmarkRules.ProfileHull });
        t.RuinZones.Add(new RuinZone { X1 = 1, Y1 = 1, Z1 = 1 }); // empty = the setting's own profile
        t.RuinZones.Add(new RuinZone { X1 = 1, Y1 = 3, Z1 = 1, Profile = LandmarkRules.ProfileLattice }); // a mast
        Assert.Empty(Problems(t));

        var masonry = Ship();
        masonry.RuinZones.Add(new RuinZone { X1 = 1, Y1 = 1, Z1 = 1, Profile = LandmarkRules.ProfileMasonry });
        AssertOneProblem(masonry, "'masonry'");

        var outside = Ship();
        outside.RuinZones.Add(new RuinZone { X1 = 6, Y1 = 1, Z1 = 1 });
        AssertOneProblem(outside, "reaches outside");

        var strong = Ship();
        strong.RuinZones.Add(new RuinZone { X1 = 1, Y1 = 1, Z1 = 1, Strength = 2 });
        AssertOneProblem(strong, "strength");

        var many = Ship();
        for (int i = 0; i <= LandmarkRules.MaxRuinZones; i++)
        {
            many.RuinZones.Add(new RuinZone { X1 = 1, Y1 = 1, Z1 = 1 });
        }

        AssertOneProblem(many, "more than 16 ruin zones");
    }

    [Fact]
    public void Markers_AreKnown_InsideTheBox_AndADoorwayNamesItsAxis()
    {
        var known = Ship();
        known.Cells.Add(LandmarkShapes.Marker(1, 1, 1, GiantShipRules.DoorMarker, "axis=x"));
        known.Cells.Add(LandmarkShapes.Marker(3, 1, 1, GiantShipRules.DoorMarker, "axis=z"));
        known.Cells.Add(LandmarkShapes.Marker(4, 1, 1, LandmarkRules.ChestMarker, "table=ruin_common;hidden=1"));
        known.Cells.Add(LandmarkShapes.Marker(4, 2, 3, LandmarkRules.SoundMarker, "sound=amb_wind"));
        known.Cells.Add(LandmarkShapes.Marker(5, 1, 4, "data_terminal"));
        Assert.Empty(Problems(known));

        foreach (string nobodyHome in new[] { "npc", "vendor", LandmarkRules.DenMarker })
        {
            var t = Ship();
            t.Cells.Add(LandmarkShapes.Marker(1, 1, 1, nobodyHome));
            AssertOneProblem(t, "not a giant ship marker");
        }

        var outside = Ship();
        outside.Cells.Add(LandmarkShapes.Marker(1, 9, 1, LandmarkRules.ChestMarker));
        AssertOneProblem(outside, "lies outside");

        var door = Ship();
        door.Cells.Add(LandmarkShapes.Marker(1, 1, 1, GiantShipRules.DoorMarker, "axis=y"));
        AssertOneProblem(door, "needs axis=x");
        door.Cells[^1].Config = string.Empty;
        AssertOneProblem(door, "needs axis=x");
    }

    [Fact]
    public void NoEntrance_IsAProblem()
    {
        var t = Ship();
        t.Cells.RemoveAll(c => c.Id == GiantShipRules.EntranceMarker);
        AssertOneProblem(t, "no 'entrance' marker");
    }

    [Fact]
    public void AnEntrance_NeedsASolidFloor_AndTwoFreeCellsAbove()
    {
        // Ship(): the entrance at (2, 1, 2) on the iron floor.
        var noFloor = Ship();
        noFloor.Cells.RemoveAll(c => c.Kind == "block" && c.X == 2 && c.Z == 2);
        AssertOneProblem(noFloor, "the entrance at (2,1,2)");

        var cavityFloor = Ship();
        cavityFloor.Cells.Add(LandmarkShapes.Cavity(2, 0, 2)); // written after the floor block: this cell is air
        AssertOneProblem(cavityFloor, "the entrance at (2,1,2)");

        var waterFloor = Ship();
        waterFloor.Cells.Add(LandmarkShapes.Block(2, 0, 2, "water"));
        AssertOneProblem(waterFloor, "the entrance at (2,1,2)");

        var lowRoof = Ship();
        lowRoof.Cells.Add(LandmarkShapes.Block(2, 2, 2, "iron_wall"));
        AssertOneProblem(lowRoof, "two free cells of headroom");

        var ladder = Ship(); // a passable block in the headroom is fine
        ladder.Cells.Add(LandmarkShapes.Block(2, 1, 2, "ladder"));
        ladder.Cells.Add(LandmarkShapes.Block(2, 2, 2, "ladder"));
        Assert.Empty(Problems(ladder));

        var bottom = Ship(); // an entrance on the bottom row stands on nothing
        bottom.Cells.Add(LandmarkShapes.Marker(4, 0, 4, GiantShipRules.EntranceMarker));
        AssertOneProblem(bottom, "the entrance at (4,0,4)");

        // Without a solidity test every block counts as solid: the ladder headroom is then a wall.
        Assert.Contains(GiantShipRules.Problems(ladder, KnownBlocks.Contains), p => p.Contains("headroom", StringComparison.Ordinal));
    }

    [Fact]
    public void AGiantShipIsNeverAModule_Underground_AGeneratorEntry_OrALandmark()
    {
        var module = Ship();
        module.Role = "house";
        AssertOneProblem(module, "kit module");

        var underground = Ship();
        underground.Underground = true;
        AssertOneProblem(underground, "underground");

        var source = Ship();
        source.Source = "monument";
        AssertOneProblem(source, "generator entry");

        var landmark = Ship();
        landmark.Landmark = true;
        AssertOneProblem(landmark, "landmark");
    }

    [Fact]
    public void AnUnknownProtectionWord_AndANegativeGeneration_AreProblems()
    {
        var t = Ship();
        t.Protection = LandmarkRules.ProtectionOpen;
        Assert.Empty(Problems(t));
        t.Protection = "locked";
        AssertOneProblem(t, "unknown protection 'locked'");

        var young = Ship();
        young.MinTerrainGeneration = -1;
        AssertOneProblem(young, "minimum terrain generation");
    }

    // ---- the placement rule (#3608) ---------------------------------------------------------------------------------

    [Fact]
    public void TheGiantWreckRule_IsGeneration42_AboutEvery20thPlanet_AirlessAndAsteroidsAllowed()
    {
        var rule = StructureTypeRules.Defaults.Get(StructureTypeRules.GiantWreck);

        Assert.True(rule.Enabled);
        Assert.Equal(WorldDescription.GiantWrecksGeneration, rule.MinGeneration);
        Assert.Equal(GiantShipRules.Chance, rule.Chance);
        Assert.Equal(GiantShipRules.StructureKind, rule.Whitelist);
        Assert.Equal(new[] { StructureTypeRules.ConditionNotVoid, StructureTypeRules.ConditionNotGas }, rule.Conditions);
        Assert.Equal(24, rule.MarginFrom(StructureTypeRules.Pad, 6));
        Assert.Equal(10, rule.MarginFrom(StructureTypeRules.WreckSite, 6));
        Assert.Equal(16, rule.MarginFrom(StructureTypeRules.Landmark, 6));
        Assert.Equal((16, 24, 24), (rule.Grade.Radius, rule.Grade.MaxCut, rule.Grade.MaxFill));
        Assert.Empty(rule.Site.Altitude);

        var airlessMoon = new PlanetType { Key = "moon", Atmosphere = "none" };
        Assert.True(StructureTypeRules.ConditionsHold(rule, airlessMoon, asteroid: true));
        Assert.True(StructureTypeRules.ConditionsHold(rule, new PlanetType { Atmosphere = "breathable" }, asteroid: false));
        Assert.False(StructureTypeRules.ConditionsHold(rule, new PlanetType { Void = true }, asteroid: false));

        var whitelisted = new PlanetType { Key = "titas", RestrictStructures = true, AllowedStructures = { GiantShipRules.StructureKind } };
        Assert.True(StructureTypeRules.AllowedOn(rule, whitelisted, restricted: true));
        Assert.False(StructureTypeRules.AllowedOn(rule, new PlanetType { RestrictStructures = true }, restricted: true));
    }

    [Fact]
    public void TheGiantWreckRule_CannotBeLoweredBelowGeneration42_ByTheFile()
    {
        var warnings = new List<string>();
        var rules = StructureTypeRules.Parse("""{ "giant_wreck": { "minGeneration": 41, "chance": 0.5 } }""", warnings.Add);

        Assert.Equal(WorldDescription.GiantWrecksGeneration, rules.Get(StructureTypeRules.GiantWreck).MinGeneration);
        Assert.Contains(warnings, w => w.Contains("'giant_wreck'", StringComparison.Ordinal));
    }

    // ---- terrain generation 42, the world option, the server config -------------------------------------------------

    [Fact]
    public void NewWorlds_AreCreatedAtGeneration42OrLater()
    {
        Assert.Equal(42, WorldDescription.GiantWrecksGeneration);
        Assert.True(WorldDescription.CurrentTerrainGeneration >= WorldDescription.GiantWrecksGeneration); // #3639: 43 since the civilizations
        Assert.True(WorldDescription.GrownSettlementsGeneration < WorldDescription.GiantWrecksGeneration);
        Assert.Equal(WorldDescription.CurrentTerrainGeneration, new ServerConfig().World.TerrainGeneration);
    }

    [Fact]
    public void TheWorldOption_DefaultsToNormal_ReadsTheCommandLine_AndAnOldSaveReadsNormal()
    {
        var config = new ServerConfig();
        Assert.Equal(Frequency.Normal, config.World.GiantWrecks);
        Assert.Contains("giant-wrecks", config.ApplyCommandLine(new[] { "--giant-wrecks", "off" }));
        Assert.Equal(Frequency.Off, config.World.GiantWrecks);
        Assert.Contains("giant-wrecks", config.ApplyCommandLine(new[] { "--giant-wrecks", "Frequent" }));
        Assert.Equal(Frequency.Frequent, config.World.GiantWrecks);

        var legacy = System.Text.Json.JsonSerializer.Deserialize<WorldDescription>("""{ "Derelicts": 1 }""")!;
        Assert.Equal(Frequency.Normal, legacy.GiantWrecks);

        var stored = new WorldDescription { GiantWrecks = Frequency.Rare };
        var back = System.Text.Json.JsonSerializer.Deserialize<WorldDescription>(System.Text.Json.JsonSerializer.Serialize(stored))!;
        Assert.Equal(Frequency.Rare, back.GiantWrecks);
    }

    [Fact]
    public void GiantWreckChance_IsUnsetByDefault_AndAStoredValueIsKept()
    {
        Assert.Null(new ServerConfig().GiantWreckChance);
        Assert.True(new ServerConfig().PlaceGiantWrecks);

        // A new key: nothing ever wrote a code default into server.json, so even 0.05 is the operator's choice.
        Assert.Equal(0.05, ServerConfig.FromJson("""{ "GiantWreckChance": 0.05 }""").GiantWreckChance);
        Assert.Equal(1.0, ServerConfig.FromJson(new ServerConfig { GiantWreckChance = 1.0 }.ToJson()).GiantWreckChance);
        Assert.Null(ServerConfig.FromJson("{}").GiantWreckChance);
        Assert.False(ServerConfig.FromJson("""{ "PlaceGiantWrecks": false }""").PlaceGiantWrecks);
    }
}
