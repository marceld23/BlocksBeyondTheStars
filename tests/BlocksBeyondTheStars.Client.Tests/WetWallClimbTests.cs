// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3827 (a young player's idea): "in the rain you only get about three blocks up a wall and then slip off — only on
/// stone and other things that get slick, not on earth or grass; ladders do not count; and only without a roof."
/// The rule of which blocks turn slick (<see cref="ClimbSurfaces.SlickWhenWet"/>, with the data's <c>wetGrip</c>), what
/// wet costs the grip (<see cref="ClimbGrip.WetFactor"/>), and the Unity wiring as source text (the
/// <see cref="ClientQuirksWiringTests"/> pattern).
/// </summary>
public sealed class WetWallClimbTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir());

    private static string Script(string file)
        => File.ReadAllText(Path.Combine(ClientTestPaths.RepoRoot(), "client", "Assets", "BlocksBeyondTheStars", "Scripts", file));

    /// <summary>How many blocks a rested climber gets straight up a wall before the grip is spent.</summary>
    private static float BlocksUpFromFull(float gravity, ClimbSurface surface, float gear, bool wet)
    {
        var stamina = new BodyStamina();
        float height = 0f;
        const float dt = 1f / 120f;
        for (int i = 0; i < 120 * 120 && !stamina.Exhausted; i++)
        {
            height += ClimbGrip.UpSpeed * stamina.SpeedFactor * dt;
            ClimbGrip.Drain(stamina, dt, ClimbMotion.Up, gravity, surface, gear, wet);
        }

        return height;
    }

    [Fact]
    public void OnAWetWall_ARestedClimberGetsAboutThreeBlocksUp()
    {
        float dry = BlocksUpFromFull(1f, ClimbSurface.Normal, 0f, wet: false);
        float wet = BlocksUpFromFull(1f, ClimbSurface.Normal, 0f, wet: true);
        Assert.InRange(wet, 2.7f, 3.3f); // "about three"
        Assert.Equal(dry / ClimbGrip.WetFactor, wet, 1);
        Assert.True(dry > 15f, "a dry wall still lets a rested climber up a cliff");

        // Heavier worlds give less, lighter ones more — the grip's own gravity rule.
        Assert.True(BlocksUpFromFull(1.5f, ClimbSurface.Normal, 0f, wet: true) < wet);
        Assert.True(BlocksUpFromFull(0.5f, ClimbSurface.Normal, 0f, wet: true) > wet);
    }

    [Fact]
    public void WetOnlyTouchesAPlainHold_AndHangingTiresToo()
    {
        // A loose wall (sand, snow) keeps its own factor in the rain; no hold stays no hold.
        Assert.Equal(
            ClimbGrip.DrainPerSecond(ClimbMotion.Up, 1f, ClimbSurface.Slippery, 0f),
            ClimbGrip.DrainPerSecond(ClimbMotion.Up, 1f, ClimbSurface.Slippery, 0f, wet: true));
        Assert.Equal(
            ClimbGrip.DrainPerSecond(ClimbMotion.Up, 1f, ClimbSurface.Normal, 0f) * ClimbGrip.WetFactor,
            ClimbGrip.DrainPerSecond(ClimbMotion.Up, 1f, ClimbSurface.Normal, 0f, wet: true), 5);
        Assert.Equal(
            ClimbGrip.SecondsFromFull(ClimbMotion.Hang, 1f, ClimbSurface.Normal, 0f) / ClimbGrip.WetFactor,
            ClimbGrip.SecondsFromFull(ClimbMotion.Hang, 1f, ClimbSurface.Normal, 0f, wet: true), 3);
        Assert.True(ClimbGrip.WetFactor > ClimbGrip.SlipperyFactor, "wet rock is worse than sand");

        // Gloves still take their share of the drain on a wet wall.
        Assert.True(ClimbGrip.DrainPerSecond(ClimbMotion.Up, 1f, ClimbSurface.Normal, 0.3f, wet: true)
                    < ClimbGrip.DrainPerSecond(ClimbMotion.Up, 1f, ClimbSurface.Normal, 0f, wet: true));
    }

    [Theory]
    [InlineData("rain", true)]
    [InlineData("drizzle", true)]
    [InlineData("acid", true)]
    [InlineData("snow", false)]
    [InlineData("hail", false)]
    [InlineData("sleet", false)]
    [InlineData("dust", false)]
    [InlineData("ash", false)]
    [InlineData("none", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void OnlyWhatIsWater_WetsAWall(string? precipitation, bool wets) => Assert.Equal(wets, ClimbGrip.RainWets(precipitation));

    [Theory]
    [InlineData("stone", true)]
    [InlineData("granite", true)]
    [InlineData("basalt", true)]
    [InlineData("moss_stone", true)]      // wet moss on rock: slick
    [InlineData("iron_ore", true)]
    [InlineData("steel_wall", true)]
    [InlineData("iron_block", true)]
    [InlineData("concrete", true)]
    [InlineData("dirt", false)]
    [InlineData("grass", false)]
    [InlineData("mud", false)]
    [InlineData("wood_log", false)]
    [InlineData("wood_planks", false)]
    [InlineData("giant_log", false)]
    [InlineData("sand", false)]           // loose: its own rule (slippery, wet or dry)
    [InlineData("snow", false)]
    [InlineData("ice", false)]            // icy: holds only claws
    [InlineData("glass", false)]          // no hold at all
    [InlineData("ladder", false)]         // "ladders do not count"
    [InlineData("water", false)]
    [InlineData("tree_leaves", false)]    // plants keep their hold
    public void WhichBlocksTurnSlickInTheRain(string key, bool slick)
        => Assert.Equal(slick, ClimbSurfaces.SlickWhenWet(Content.GetBlock(key)));

    [Fact]
    public void EveryBlockResolves_AndOnlyAPlainHoldCanBeSlick()
    {
        Assert.False(ClimbSurfaces.SlickWhenWet(null));
        int slick = 0, rough = 0;
        foreach (var def in Content.Blocks.Values)
        {
            bool wet = ClimbSurfaces.SlickWhenWet(def);
            if (wet)
            {
                slick++;
                Assert.True(ClimbSurfaces.Of(def) == ClimbSurface.Normal, def.Key + " is slick when wet without a plain hold");
                Assert.False(def.WetGrip, def.Key);
            }
            else if (def.WetGrip)
            {
                rough++;
                // The data's mark only means something on a block that could be slick at all.
                Assert.True(ClimbSurfaces.Of(def) == ClimbSurface.Normal, def.Key + " is marked wetGrip but gives no plain hold");
            }
        }

        Assert.True(slick > 100, "rock, ore and metal are most of the world: " + slick);
        Assert.InRange(rough, 25, 60); // soil, grass, wood, soft things
    }

    [Fact]
    public void TheClimb_AsksTheRainTheRoofTheClawsAndTheBlock()
    {
        string climb = Script("PlayerController.Climb.cs");
        Assert.Contains("ClimbGrip.RainWets(Game.Environment?.Precipitation)", climb);
        Assert.Contains("!Game.ExposedToSky", climb);                                   // a roof keeps the wall dry
        Assert.Contains("surface != ClimbSurface.Normal || _climbGearIce", climb);      // claws hold on a wet wall
        Assert.Contains("ClimbWetSlickAt(pos, ClimbProbe.HandHeight) || ClimbWetSlickAt(pos, ClimbProbe.KneeHeight)", climb);
        Assert.Contains("collides && ClimbSurfaces.SlickWhenWet(def)", climb);
        Assert.Contains("ClimbGrip.Drain(_stamina, dt, motion, _gFactor, surface, _climbGearGrip, _climbWet);", climb);
        Assert.Contains("Game.Localizer.Get(\"ui.hud.climb_wet\")", climb);             // the slip says why

        // The cell the wet check reads is the cell the hold was read from.
        Assert.Equal((3, 11, -2), ClimbProbe.WallCell(2.5f, 10.2f, -1.5f, 1, 0, ClimbProbe.HandHeight));
        Assert.Equal((2, 10, -3), ClimbProbe.WallCell(2.5f, 10.2f, -1.5f, 0, -1, ClimbProbe.KneeHeight));

        foreach (string code in new[] { "en", "de" })
        {
            var table = ContentLoader.ParseLocaleTable(File.ReadAllText(Path.Combine(ClientTestPaths.DataDir(), "locales", code + ".json")));
            Assert.True(table.TryGetValue("ui.hud.climb_wet", out string? text) && !string.IsNullOrWhiteSpace(text), code);
        }
    }
}
