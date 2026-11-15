// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Client.Showcase;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3705: the shell's one fixed motif — the corvette from its shipped layout in front of a "varied" world with clouds. The
/// menu, the intro film and the poster draw it from these numbers; the tests pin that the ship is the real corvette (the
/// server's cell mapping, the floor guarantee, no doorway blocks) and that the planet is a world with air, clouds, land and
/// sea — a generator change that turns it into an ocean or a desert shows up here, not first on the poster.
/// </summary>
public sealed class ShowcaseMotifTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir());

    [Fact]
    public void ShipDesign_IsTheCorvetteLayout_MappedLikeTheServerHull()
    {
        var layout = Content.GetShipLayout(Content.Ships[ShowcaseMotif.ShipType].Layout)!;
        var design = ShowcaseMotif.ShipDesign(Content);

        Assert.NotNull(design);
        Assert.Equal(design!.Block.Length, design.X.Length);
        Assert.Equal((layout.Width, layout.Height, layout.Length), (design.Width, design.Height, design.Length));
        Assert.All(design.Block, b => Assert.NotEqual((ushort)0, b));

        var cells = Enumerable.Range(0, design.Block.Length).ToDictionary(i => (design.X[i], design.Y[i], design.Z[i]), i => design.Block[i]);
        Assert.Equal(cells.Count, design.Block.Length); // no cell twice

        // Every layout block cell is there as the server maps it; doorways and hatches stay open.
        var palette = ShipCellPalette.From(Content);
        foreach (var cell in layout.Cells)
        {
            var mapped = ShipCellBlocks.Map(cell, palette, Content);
            var key = (cell.X, cell.Y, cell.Z);
            if (mapped.Role is ShipCellRole.Door or ShipCellRole.Opening)
            {
                Assert.False(cells.ContainsKey(key) && cell.Y != 0, $"doorway cell {key} must stay open");
                continue;
            }

            Assert.Equal(mapped.Block.Value, cells[key]);
        }

        // The engines are the real nozzle block (the plumes find them), the cockpit screen is clear glass.
        ushort nozzle = Content.GetBlock(ShipCellBlocks.EngineNozzleBlock)!.NumericId.Value;
        Assert.Contains(nozzle, design.Block);
        Assert.Contains(Content.GetBlock("glass_clear")!.NumericId.Value, design.Block);
    }

    [Fact]
    public void ShipDesign_HasTheFloorGuarantee()
    {
        var layout = Content.GetShipLayout(Content.Ships[ShowcaseMotif.ShipType].Layout)!;
        var design = ShowcaseMotif.ShipDesign(Content)!;
        var floor = new HashSet<(int, int)>();
        for (int i = 0; i < design.Block.Length; i++)
        {
            if (design.Y[i] == 0)
            {
                floor.Add((design.X[i], design.Z[i]));
            }
        }

        foreach (var column in ShipCellBlocks.FloorFootprint(layout))
        {
            Assert.Contains(column, floor);
        }
    }

    [Fact]
    public void Planet_IsAVariedWorld_WithAirAndClouds()
    {
        var planet = Content.GetPlanet(ShowcaseMotif.PlanetType);
        Assert.NotNull(planet);

        var look = OrbitLook.For(planet, ShowcaseMotif.WorldSeed, ShowcaseMotif.BodyId, ShowcaseMotif.Generation);
        Assert.True(look.HasAir);
        Assert.True(look.HasClouds);
        Assert.True(look.Breathable);
        Assert.Equal(WorldDescription.CurrentTerrainGeneration, ShowcaseMotif.Generation);
        Assert.InRange(ShowcaseMotif.Circumference, 4000, 16000);
    }

    [Fact]
    public void Planet_Map_ShowsLandAndSea()
    {
        var colors = new Dictionary<ushort, int>();
        foreach (ushort id in PlanetMapBakeJob.GroundBlockIds(Content, ShowcaseMotif.PlanetType))
        {
            colors[id] = 0x6E8C3C;
        }

        var map = new PlanetMapBakeJob(new PlanetMapRequest
        {
            Content = Content,
            WorldSeed = ShowcaseMotif.WorldSeed,
            BodyId = ShowcaseMotif.BodyId,
            FloraKey = ShowcaseMotif.FloraKey,
            PlanetTypeKey = ShowcaseMotif.PlanetType,
            Circumference = ShowcaseMotif.Circumference,
            Width = 96,
            Height = 48,
            Continents = ShowcaseMotif.Continents,
            Generation = ShowcaseMotif.Generation,
            GroundColors = colors,
        }).Run();

        int sea = map.Flags.Count(f => (f & PlanetMapData.FlagSea) != 0);
        double share = sea / (double)map.Flags.Length;
        Assert.InRange(share, 0.15, 0.85); // the poster needs both: seas to read as a world, land to read as a home
    }
}
