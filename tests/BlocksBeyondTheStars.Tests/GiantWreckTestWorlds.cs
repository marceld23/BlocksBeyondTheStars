// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>Shared pieces of the giant-wreck server tests (#3610, #3611): the content with a rollable sample hull, the server
/// config, and the questions every test asks of a stamped wreck.</summary>
internal static class GiantWreckTestWorlds
{
    /// <summary>The shipped content with the sample hull as the ONLY giant ship, made rollable (the shipped file is pin-only) —
    /// so a test never depends on which real ships are in <c>data/giant_ships/</c>.</summary>
    public static GameContent ContentWithHulk(out StructureTemplate hulk)
    {
        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        hulk = content.GetGiantShip("sample_hulk") ?? throw new Xunit.Sdk.XunitException("data/giant_ships/sample_hulk.json is missing");
        hulk.PinOnly = false;
        content.SetGiantShips(new[] { hulk });
        Assert.Single(content.GiantShips);
        return content;
    }

    /// <summary>A small test world: no starter ship, no settlements, no landmark; the giant-wreck chance pinned.</summary>
    public static ServerConfig Config(string name, string planet, int generation, double chance, bool cheats = false)
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
            GiantWreckChance = chance,
            Rules = cheats ? new GameRules { AdminCheats = true, AllowCheatsInSurvival = true } : new GameRules(),
        };
        config.World.TerrainGeneration = generation;
        return config;
    }

    public static StructureOverlay Decode(byte[] blob)
    {
        Assert.True(StructureOverlay.TryDecode(blob, out var overlay));
        return overlay;
    }

    /// <summary>The block edits of the store that lie on a cell of the overlay.</summary>
    public static List<BlockEdit> EditsOn(IWorldRepository repo, string location, StructureOverlay overlay)
    {
        var cells = new HashSet<Vector3i>(overlay.Cells().Select(c => c.Cell));
        return overlay.Chunks.SelectMany(c => repo.LoadChunkEdits(location, c.Coord)).Where(e => cells.Contains(e.WorldPosition)).ToList();
    }

    /// <summary>The canonical world cell of a structure-local cell.</summary>
    public static Vector3i At(SvGameServer server, SvGameServer.LandmarkInstance wreck, Vector3i local)
        => WorldConstants.CanonicalBlock(wreck.Cell(local), server.World.Circumference);

    public static IEnumerable<Vector3i> AllCells(SettlementStructure s)
    {
        for (int x = 0; x < s.Width; x++)
        {
            for (int y = 0; y < s.Height; y++)
            {
                for (int z = 0; z < s.Length; z++)
                {
                    yield return new Vector3i(x, y, z);
                }
            }
        }
    }

    /// <summary>Every block of the wreck's layout, in index order (a replay must derive the very same).</summary>
    public static ushort[] LayoutCells(SvGameServer.LandmarkInstance wreck)
        => AllCells(wreck.Structure).Select(c => wreck.Structure.Get(c.X, c.Y, c.Z)).ToArray();

    /// <summary>The layout's cells that hold a block, from the ground row up, away from the chests (a crate and a hidden
    /// stone stand there).</summary>
    public static IEnumerable<Vector3i> SolidCells(SvGameServer.LandmarkInstance wreck)
    {
        var chests = wreck.Structure.Markers.Where(m => m.Type == LandmarkRules.ChestMarker).Select(m => m.LocalPos).ToList();
        return AllCells(wreck.Structure).Where(c => c.Y >= wreck.Layout.GroundRow && wreck.Structure.Get(c.X, c.Y, c.Z) != 0
            && !chests.Any(m => m.X == c.X && m.Z == c.Z && (m.Y == c.Y || m.Y + 1 == c.Y)));
    }

    /// <summary>Every block the generator kept stands in the world — the hull, the tree, the pool (or its ice): the stamp
    /// writes the layout as it is.</summary>
    public static void AssertStandsAsBuilt(SvGameServer server, SvGameServer.LandmarkInstance wreck)
    {
        int n = 0;
        foreach (var c in SolidCells(wreck))
        {
            ushort want = wreck.Structure.Get(c.X, c.Y, c.Z);
            ushort got = server.World.GetBlock(At(server, wreck, c)).Value;
            Assert.True(want == got, $"cell {c.X},{c.Y},{c.Z} of the wreck holds {got}, the layout says {want}");
            n++;
        }

        Assert.True(n > 0, "the wreck has no block above its ground row");
    }

    /// <summary>The wreck's chests (the landmark chest path, its own container prefix): id, cell and content.</summary>
    public static List<(string Id, Vector3i Cell, string Items)> ChestsIn(SvGameServer server, SvGameServer.LandmarkInstance wreck)
        => server.Containers
            .Where(c => c.Id.StartsWith(SvGameServer.GiantWreckChestContainerPrefix, System.StringComparison.Ordinal)
                        && wreck.Structure.Markers.Any(m => m.Type == LandmarkRules.ChestMarker && At(server, wreck, m.LocalPos).Equals(c.Position)))
            .OrderBy(c => c.Id, System.StringComparer.Ordinal)
            .Select(c => (c.Id, c.Position, string.Join(",", c.Items.Select(i => i.Item + "x" + i.Count))))
            .ToList();
}
