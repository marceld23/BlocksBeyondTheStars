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
    [InlineData("iron_ore", "step_metal")]
    [InlineData("sand", "step_sand")]
    [InlineData("red_sand", "step_sand")]
    [InlineData("grass", "step_grass")]
    [InlineData("snow", "step_snow")]
    [InlineData("ice", "step_snow")]
    [InlineData("stone", "step_rock")]
    [InlineData(null, "step_rock")]
    public void TheKeyHeuristic_IsTheOldOne(string? key, string cue)
        => Assert.Equal(cue, FootstepSurface.CueFor(key));

    /// <summary>The Unity half, guarded as source text (the Unity scripts cannot load headless): the footsteps, the sneak
    /// edge-stop and the ladder check read the ship first; a monorail wagon's floor is metal; the fluid probes keep the
    /// world alone.</summary>
    [Fact]
    public void ThePlayerController_ReadsTheShipFirst_ForStepsTheEdgeStopAndLadders()
    {
        string player = File.ReadAllText(Path.Combine(ClientTestPaths.RepoRoot(), "client", "Assets", "BlocksBeyondTheStars", "Scripts", "PlayerController.cs"));
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
