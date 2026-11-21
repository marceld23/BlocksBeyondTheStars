// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Sounders;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.Story;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3894 — the foundation of the package "sky-lights &amp; other side" (epic #3891): the new blocks and items in the data,
/// the hunter's rare hacking chip, the game events, the capability word and the session stub the other areas build on.
/// The names are the package's contract; a rename here is a rename everywhere.
/// </summary>
public sealed class SkyLightsFoundationTests : IDisposable
{
    private static readonly Lazy<GameContent> Content = new(() => ContentLoader.LoadFromDirectory(TestPaths.DataDir()));

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_skylights_" + Guid.NewGuid().ToString("N"));
    private readonly List<SqliteWorldRepository> _repos = new();

    public void Dispose()
    {
        foreach (var repo in _repos)
        {
            repo.Dispose();
        }

        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public void TheBlueLamp_IsTheRedLampInBlue_WithItsUnlitTwin()
    {
        var c = Content.Value;
        var red = c.GetBlock("light_red")!;
        var blue = c.GetBlock("light_blue")!;
        Assert.Equal((red.Category, red.Hardness, red.RequiredTool, red.MinToolTier, red.Tintable, red.Shapeable, red.Solid, red.Mineable),
            (blue.Category, blue.Hardness, blue.RequiredTool, blue.MinToolTier, blue.Tintable, blue.Shapeable, blue.Solid, blue.Mineable));
        Assert.Equal("light_blue", Assert.Single(blue.Drops).Item);
        Assert.Equal("light_blue", c.GetItem("light_blue")!.PlacesBlock);
        Assert.Equal(c.GetItem("light_red")!.MaxStack, c.GetItem("light_blue")!.MaxStack);

        var redOff = c.GetBlock("light_red_off")!;
        var blueOff = c.GetBlock(CrystalNetRules.LightOffKey("light_blue"))!; // the Crystal Net finds the twin by its suffix
        Assert.Equal((redOff.Category, redOff.Hardness, redOff.Tintable, redOff.Solid), (blueOff.Category, blueOff.Hardness, blueOff.Tintable, blueOff.Solid));
        Assert.Equal("light_blue", Assert.Single(blueOff.Drops).Item); // a dark lamp gives the lamp back
        Assert.Equal(CrystalDeviceKind.Light, CrystalNetRules.KindOf(blue));
        Assert.Equal(CrystalDeviceKind.Light, CrystalNetRules.KindOf(blueOff));
        Assert.Equal(0, BlockLight.NaturalColorOf(blueOff));

        var redRecipe = c.Recipes["light_red"];
        var blueRecipe = c.Recipes["light_blue"];
        Assert.Equal((redRecipe.Station, redRecipe.RequiredBlueprint, redRecipe.Outputs.Single().Count),
            (blueRecipe.Station, blueRecipe.RequiredBlueprint, blueRecipe.Outputs.Single().Count));
        Assert.Equal("light_blue", blueRecipe.Outputs.Single().Item);
    }

    [Theory]
    [InlineData("sps_console", true)]
    [InlineData("capsule_tank", true)]
    [InlineData("capsule_tank_open", false)]
    [InlineData("sound_block", true)]
    public void TheFixtures_AreNeitherMinedNorMadeNorHeld(string key, bool solid)
    {
        var c = Content.Value;
        var block = c.GetBlock(key)!;
        Assert.Equal(solid, block.Solid);
        Assert.False(block.Mineable);
        Assert.Empty(block.Drops);
        Assert.Null(c.GetItem(key));
        Assert.DoesNotContain(c.Items.Values, i => i.PlacesBlock == key);
        Assert.DoesNotContain(c.Recipes.Values, r => r.Outputs.Any(o => o.Item == key));
        Assert.DoesNotContain(c.Blocks.Values, b => b.Key != key && b.Drops.Any(d => d.Item == key));
        Assert.True(block.Color.HasValue, key + " needs a colour: it wears the plain colour tile until its own tile is bundled");
    }

    [Fact]
    public void TheConsoleIsUsed_TheOpenTankIsWalkedInto_TheSoundBlockGlowsInItsOwnColour()
    {
        var c = Content.Value;
        Assert.True(c.GetBlock("sps_console")!.Usable);
        Assert.Equal("machine", c.GetBlock("sps_console")!.Category);
        Assert.True(WalkThroughBlocks.IsOpenTwin("capsule_tank_open"));
        Assert.False(WalkThroughBlocks.IsOpenTwin("capsule_tank"));

        var sound = c.GetBlock(SoundersIds.SoundBlock)!;
        Assert.True(sound.Tintable); // the server may stamp a per-voxel glow colour
        Assert.False(sound.Shapeable); // sound blocks are rigid cubes
        Assert.True(sound.Emission > 0.5f);
        Assert.Equal(0, BlockLight.NaturalColorOf(sound)); // it glows, it floods no light: 2000 of them stay cheap
        Assert.Equal(CrystalDeviceKind.None, CrystalNetRules.KindOf(sound));
        Assert.Equal(2000, OtherSideRules.SoundBlockCap);
    }

    [Fact]
    public void TheItems_AreInTheData_WithTheirRecipes()
    {
        var c = Content.Value;
        var chip = c.GetItem(EnemyCatalog.HackingChip)!;
        Assert.Equal((ItemCategory.Component, 16), (chip.Category, chip.MaxStack));
        var chipRecipe = c.Recipes["hacking_chip"];
        Assert.Equal(CraftingStation.Workshop, chipRecipe.Station);
        Assert.True(string.IsNullOrEmpty(chipRecipe.RequiredBlueprint));
        Assert.Equal(new[] { ("titanium_plate", 10), ("guardian_plate", 20) }, chipRecipe.Inputs.Select(i => (i.Item, i.Count)));

        var rusted = c.GetItem("guardian_plasma_rusted")!;
        Assert.Null(rusted.Tool); // the sleeper's gift is no weapon as it is
        var laser = c.GetItem("guardian_laser")!;
        Assert.Equal(ToolKind.Weapon, laser.Tool!.Kind);
        Assert.Equal((3, 60f, 34f, 2.0f), (laser.Tool.Tier, laser.Tool.Damage, laser.Tool.Range, laser.Tool.EnergyPerUse));
        Assert.Equal("laser", laser.Tool.Fx!.Style);
        Assert.Equal(c.GetItem("guardian_sword")!.Tool!.Fx!.Color, laser.Tool.Fx.Color); // the Guardian's red
        Assert.NotNull(laser.HeldModel);
        var laserRecipe = c.Recipes["guardian_laser"];
        Assert.Equal(CraftingStation.Workshop, laserRecipe.Station);
        Assert.True(string.IsNullOrEmpty(laserRecipe.RequiredBlueprint));
        Assert.Equal(new[] { ("guardian_plasma_rusted", 1), ("guardian_plate", 4), ("energy_cell_1", 2) }, laserRecipe.Inputs.Select(i => (i.Item, i.Count)));

        var core = c.GetItem("echo_memory_core")!;
        Assert.Equal(c.GetItem("guardian_giant_core")!.Category, core.Category);
        Assert.DoesNotContain(c.Recipes.Values, r => r.Outputs.Any(o => o.Item == "echo_memory_core" || o.Item == "guardian_plasma_rusted"));
    }

    [Fact]
    public void AHunter_CarriesAHackingChipNowAndThen()
    {
        var c = Content.Value;
        var rare = Assert.Single(c.Enemy(EnemyCatalog.Hunter).Tier(EnemyTiers.Normal).RareLoot);
        Assert.Equal((EnemyCatalog.HackingChip, 1, EnemyCatalog.HunterChipChance), (rare.Item, rare.Count, rare.Chance));
        Assert.All(c.Enemies.Where(e => e.Key != EnemyCatalog.Hunter), e => Assert.All(e.Tiers.Values, t => Assert.Empty(t.RareLoot)));
        Assert.True(rare.Falls(0.0));
        Assert.False(rare.Falls(rare.Chance));
        Assert.False(new RareDrop { Item = "x", Chance = 0f }.Falls(0.0));

        // The server's roll: the same for the same enemy at the same moment, spread over [0, 1), and about one in twelve falls.
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "chip"));
        _repos.Add(repo);
        var config = new ServerConfig { WorldName = "chip", Seed = 9, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false, ViewDistanceChunks = 1 };
        config.Rules.PlanetEnemies = AlienActivity.Off;
        var server = new SvGameServer(config, c, new RecordingTransport(), repo);
        server.Start();
        int falls = 0;
        for (int i = 0; i < 2000; i++)
        {
            double roll = server.RareLootRollForTest("e" + i, rare.Item);
            Assert.InRange(roll, 0.0, 0.9999999);
            Assert.Equal(roll, server.RareLootRollForTest("e" + i, rare.Item));
            falls += rare.Falls(roll) ? 1 : 0;
        }

        Assert.InRange(falls, 100, 230); // 8 % of 2000 = 160
        server.Stop();
    }

    [Fact]
    public void TheEvents_TheCapabilityWord_AndTheSessionStub_AreThere()
    {
        foreach (string key in new[]
                 {
                     "sps_tower:found", "sps_tower:lit", "capsule:opened", "cruiser:defeated", "cruiser:boarded", "echo:freed",
                     "otherside:crossed", "otherside:returned", "sound:shaped", "crack:mended",
                 })
        {
            Assert.Contains(key, GameEventKeys.All);
        }

        Assert.Equal("otherside1", ClientFeatures.OtherSide1);
        var session = new PlayerSession(1, new PlayerState());
        Assert.False(session.OtherSide); // nobody is over there until the other-side core says so
        Assert.False(session.SupportsOtherSide);
        session.ClientFeatures.Add(ClientFeatures.OtherSide1);
        Assert.True(session.SupportsOtherSide);
        Assert.Equal(18, WorldMetadata.CurrentSaveVersion); // one bump for all the package's blocks
    }
}
