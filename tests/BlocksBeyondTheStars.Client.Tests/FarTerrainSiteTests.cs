// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.Client.FarTerrain;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// Terrain generation 40 (#3570, #3572): the client's far terrain draws the ground the server's structures reserved — the
/// site flats ride the far-terrain handshake and the client's generator levels and grades them exactly like the server's,
/// across the X seam too; and on a river world the client's valleys are the server's.
/// </summary>
public sealed class FarTerrainSiteTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir());

    private const long Seed = 777;
    private const string Body = "sys0-p2";

    private static WorldGenerator ServerSide(SiteFlat[] sites)
    {
        var gen = new WorldGenerator(Seed, Content);
        gen.SetContinentsEnabled(true);
        gen.SetLavaCoreVolcanoes(true);
        gen.SetTerrainGeneration(WorldDescription.GeographyGeneration);
        gen.SetWorldMode(WorldConstants.Circumference, cratered: false, landingPads: null, Body, siteFlats: sites);
        return gen;
    }

    [Fact]
    public void TheFarTerrain_LevelsAndGradesTheReservedGround_LikeTheServer()
    {
        var planet = Content.Planets["highland"];
        var probe = ServerSide(System.Array.Empty<SiteFlat>());
        int y = probe.SurfaceHeight(planet, 5, 300);
        var sites = new[] { new SiteFlat(-12, 290, 24, 20, y, 16, 24, 24), new SiteFlat(900, -400, 18, 18, y + 6, 12, 16, 16) };
        var server = ServerSide(sites);
        var info = new FarTerrainWorldInfo
        {
            LocationId = Body,
            PlanetType = "highland",
            Circumference = WorldConstants.Circumference,
            ContinentsEnabled = true,
            LavaCoreVolcanoes = true,
            TerrainGeneration = WorldDescription.GeographyGeneration,
            Sites = SiteFlat.Pack(sites),
        };
        var source = FarTerrainSource.Create(Content, Seed, info);
        Assert.NotNull(source);
        foreach (var s in sites)
        {
            for (int dx = -20; dx < s.Width + 20; dx += 3)
                for (int dz = -20; dz < s.Length + 20; dz += 3)
                {
                    Assert.Equal(server.SurfaceHeight(planet, s.MinX + dx, s.MinZ + dz), source!.GroundHeight(s.MinX + dx, s.MinZ + dz));
                }
        }

        // The box straddling the X seam is level on both spellings of its columns.
        int circ = WorldConstants.Circumference;
        Assert.Equal(source!.GroundHeight(-6, 300), source.GroundHeight(circ - 6, 300));
    }

    [Fact]
    public void TheFarTerrain_DrawsTheServersValleys()
    {
        var planet = Content.Planets["highland"];
        var server = ServerSide(System.Array.Empty<SiteFlat>());
        var source = FarTerrainSource.Create(Content, Seed, new FarTerrainWorldInfo
        {
            LocationId = Body,
            PlanetType = "highland",
            Circumference = WorldConstants.Circumference,
            ContinentsEnabled = true,
            LavaCoreVolcanoes = true,
            TerrainGeneration = WorldDescription.GeographyGeneration,
        });
        var field = server.RiverFieldFor(planet);
        var valleyColumns = field.ColumnsByPosition.Keys.OrderBy(k => k.X).ThenBy(k => k.Z)
            .Select(k => (X: k.X + 5, k.Z)).Where(c => server.StageDepthAt(planet, c.X, c.Z) > 0).Take(100).ToList();
        Assert.NotEmpty(valleyColumns);
        foreach (var (x, z) in valleyColumns)
        {
            Assert.Equal(server.SurfaceHeight(planet, x, z), source!.GroundHeight(x, z));
        }
    }
}
