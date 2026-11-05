// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #2743: footsteps on a parked ship's floor sounded like rock — the ship is its own object (ship-as-object), the world's
/// grid read air under the player and the cue fell through to rock. What the player stands on now reads the ship's cell
/// first and the world's behind it (the footsteps, the sneak edge-stop and the ladder check share it).
/// </summary>
public sealed class FootstepSurfaceTests
{
    /// <summary>Two sparse grids, the parked ship's cells and the world's; a missing cell is air.</summary>
    private sealed class Grid
    {
        private readonly Dictionary<(int, int, int), string> _ship = new();
        private readonly Dictionary<(int, int, int), string> _world = new();

        public Grid Ship(int x, int y, int z, string key)
        {
            _ship[(x, y, z)] = key;
            return this;
        }

        public Grid World(int x, int y, int z, string key)
        {
            _world[(x, y, z)] = key;
            return this;
        }

        public string? ShipKey(int x, int y, int z) => _ship.TryGetValue((x, y, z), out var k) ? k : null;

        public string? WorldKey(int x, int y, int z) => _world.TryGetValue((x, y, z), out var k) ? k : "air";

        /// <summary>The cue for a player whose feet stand at the top of cell (0, 0, 0) — the y = 0 cell is underfoot.</summary>
        public string CueStandingOnCellZero() => FootstepSurface.CueUnderFeet(ShipKey, WorldKey, 0.5f, 1.0f, 0.5f);
    }

    [Fact]
    public void AShipsSteelFloor_OverWorldAir_SoundsLikeMetal()
    {
        var grid = new Grid().Ship(0, 0, 0, "steel_floor");
        Assert.Equal("step_metal", grid.CueStandingOnCellZero());
    }

    [Fact]
    public void NoShipCell_OverGrass_SoundsLikeGrass()
    {
        var grid = new Grid().World(0, 0, 0, "grass").Ship(0, 3, 0, "steel_floor"); // a ship's deck far overhead
        Assert.Equal("step_grass", grid.CueStandingOnCellZero());
    }

    [Fact]
    public void AShipsWall_OverSand_TheShipWins()
    {
        var grid = new Grid().Ship(0, 0, 0, "iron_wall").World(0, 0, 0, "sand");
        Assert.Equal("step_metal", grid.CueStandingOnCellZero());
        Assert.Equal("iron_wall", FootstepSurface.KeyAt(grid.ShipKey, grid.WorldKey, 0, 0, 0));
    }

    [Fact]
    public void NothingUnderfoot_SoundsLikeRock()
    {
        var grid = new Grid();
        Assert.Equal("step_rock", grid.CueStandingOnCellZero());
        Assert.Equal("step_rock", FootstepSurface.CueUnderFeet(null, null, 0.5f, 1f, 0.5f));
    }

    [Fact]
    public void AShipCellReportedAsAir_FallsThroughToTheWorld()
    {
        var grid = new Grid().Ship(0, 0, 0, "air").World(0, 0, 0, "snow");
        Assert.Equal("step_snow", grid.CueStandingOnCellZero());
    }

    [Fact]
    public void TheSneakEdgeStopsProbe_FindsAShipsRoof_OverTheVoid()
    {
        // The edge-stop probes half a block and 1.2 blocks under a point ahead: on a ship's roof over open air, the ship's
        // cell is the ground (the world alone read air and cancelled every crouched step).
        var grid = new Grid().Ship(3, 9, -2, "metal_panel");
        Assert.Equal("metal_panel", FootstepSurface.KeyAtPoint(grid.ShipKey, grid.WorldKey, 3.4f, 10f - 0.5f, -1.6f));
        Assert.Equal("air", FootstepSurface.KeyAtPoint(grid.ShipKey, grid.WorldKey, 4.6f, 10f - 0.5f, -1.6f));

        // Negative coordinates floor, not truncate.
        Assert.Equal("metal_panel", FootstepSurface.KeyAtPoint(grid.ShipKey, grid.WorldKey, 3.01f, 9.99f, -1.01f));
    }

    [Fact]
    public void ALadderFurnishingAShip_IsFound()
    {
        var grid = new Grid().Ship(2, 5, 2, "ladder").World(2, 5, 2, "air");
        Assert.Equal("ladder", FootstepSurface.KeyAtPoint(grid.ShipKey, grid.WorldKey, 2.5f, 5.3f, 2.5f));
    }

    [Theory]
    [InlineData("steel_floor", "step_metal")]
    [InlineData("metal_panel", "step_metal")]
    [InlineData("iron_wall", "step_metal")]
    [InlineData("sand", "step_sand")]
    [InlineData("red_sand", "step_sand")]
    [InlineData("grass", "step_grass")]
    [InlineData("alien_grass", "step_grass")]
    [InlineData("snow", "step_snow")]
    [InlineData("ice", "step_snow")]
    [InlineData("stone", "step_rock")]
    [InlineData("dirt", "step_rock")]
    [InlineData("", "step_rock")]
    [InlineData(null, "step_rock")]
    public void TheKeyHeuristic_KeepsTheOldSurfaces(string? key, string cue)
        => Assert.Equal(cue, FootstepSurface.CueFor(key));

    /// <summary>#3239: the five new surfaces, and the old heuristic's slips — an ore clanked like metal for its "iron", a
    /// sandstone crunched like a beach, a "device_eye" squeaked like snow for its "ice".</summary>
    [Theory]
    [InlineData("water", "step_water")]
    [InlineData("wood_planks", "step_wood")]
    [InlineData("wood_planks_mahogany", "step_wood")]
    [InlineData("wood_log", "step_wood")]
    [InlineData("giant_log", "step_wood")]
    [InlineData("wood_crate", "step_wood")]
    [InlineData("crate", "step_wood")]
    [InlineData("trapdoor", "step_wood")]
    [InlineData("door_wood", "step_wood")]
    [InlineData("tree_leaves", "step_leaves")]
    [InlineData("pine_needles", "step_leaves")]
    [InlineData("palm_frond", "step_leaves")]
    [InlineData("bridge_deck", "step_grate")]
    [InlineData("lift_platform", "step_grate")]
    [InlineData("crystal", "step_crystal")]
    [InlineData("crystal_conduit", "step_crystal")]
    [InlineData("glass", "step_crystal")]
    [InlineData("obsidian", "step_crystal")]
    [InlineData("cargo_floor", "step_metal")]
    [InlineData("transport_crate", "step_metal")]
    [InlineData("copper_block", "step_metal")]
    [InlineData("logic_block", "step_metal")]
    [InlineData("scrap_pile", "step_metal")]
    [InlineData("iron_ore", "step_rock")]
    [InlineData("tainted_iron_ore", "step_rock")]
    [InlineData("moss_stone", "step_rock")]
    [InlineData("sandstone", "step_rock")]
    [InlineData("device_eye", "step_rock")]
    [InlineData("black_sand", "step_sand")]
    [InlineData("salt", "step_sand")]
    public void TheKeyHeuristic_KnowsTheNewSurfaces(string key, string cue)
        => Assert.Equal(cue, FootstepSurface.CueFor(key));

    /// <summary>#3239: what covers the ground at the feet colours the step — the plants there have no collider, so the cell
    /// under the feet alone is always the soil beneath them, and shallow water is the cell at the feet, not under them.</summary>
    [Fact]
    public void TheCoverAtTheFeet_ColoursTheStep_OverTheFloorBelow()
    {
        var wading = new Grid().World(0, 0, 0, "sand").World(0, 1, 0, "water");
        Assert.Equal("step_water", wading.CueStandingOnCellZero());

        var mossy = new Grid().World(0, 0, 0, "dirt").World(0, 1, 0, "flora_moss");
        Assert.Equal("step_leaves", mossy.CueStandingOnCellZero());

        var tuft = new Grid().World(0, 0, 0, "dirt").World(0, 1, 0, "flora_grasstuft");
        Assert.Equal("step_grass", tuft.CueStandingOnCellZero());

        var flower = new Grid().World(0, 0, 0, "snow").World(0, 1, 0, "flora_flower");
        Assert.Equal("step_snow", flower.CueStandingOnCellZero()); // a flower adds nothing: the floor speaks

        Assert.Null(FootstepSurface.CoverCueFor("air"));
        Assert.Null(FootstepSurface.CoverCueFor(null));
        Assert.Null(FootstepSurface.CoverCueFor("water_spout")); // a machine, not water
    }

    [Fact]
    public void ATake_FallsBackToTakeOne_AndAMissingSurfaceToRock()
    {
        var bundled = new HashSet<string> { "step_wood", "step_wood_2", "step_rock", "step_rock_2", "step_rock_3" };
        Assert.Equal("step_wood", FootstepSurface.TakeClip("step_wood", 1, bundled.Contains));
        Assert.Equal("step_wood_2", FootstepSurface.TakeClip("step_wood", 2, bundled.Contains));
        Assert.Equal("step_wood", FootstepSurface.TakeClip("step_wood", 3, bundled.Contains)); // take 3 is missing
        Assert.Equal("step_rock_3", FootstepSurface.TakeClip("step_leaves", 3, bundled.Contains)); // no leaves at all
        Assert.Equal("step_grate_3", FootstepSurface.TakeClip("step_grate", 3, null)); // no lookup: trust the name
        Assert.Equal("step_rock", FootstepSurface.TakeClip("", 1, null));
    }

    /// <summary>The Unity half, guarded as source text (the Unity scripts cannot load headless): the footsteps, the sneak
    /// edge-stop and the ladder check read the ship first; a monorail wagon's floor is metal; the fluid probes keep the
    /// world alone.</summary>
    [Fact]
    public void ThePlayerController_ReadsTheShipFirst_ForStepsTheEdgeStopAndLadders()
    {
        string player = ClientTestPaths.PlayerControllerSources();
        string steps = Body(player, "private string SurfaceStep()");
        Assert.Contains("ShipOrWorldKeyAt(", steps);
        Assert.Contains("FootstepSurface.MetalCue", steps);
        Assert.DoesNotContain("Game.World.GetBlock(", steps);
        Assert.Contains("ShipOrWorldKeyAt(", Body(player, "private bool GroundAhead("));
        Assert.DoesNotContain("BlockKeyAt(", Body(player, "private bool GroundAhead("));
        Assert.Contains("ShipOrWorldKeyAt(", Body(player, "private bool OnLadder()"));
        Assert.Contains("FootstepSurface.KeyAtPoint(", Body(player, "private string ShipOrWorldKeyAt("));
        Assert.Contains("FootstepSurface.CueFor(key)", player); // the climb cue's StepCueFor delegates
    }

    private static string Body(string src, string signature)
    {
        int start = src.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"'{signature}' not found");
        int end = src.IndexOf("/// <summary>", start, StringComparison.Ordinal);
        return end < 0 ? src.Substring(start) : src.Substring(start, end - start);
    }
}
