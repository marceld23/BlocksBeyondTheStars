// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Linq;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The world notices your base (#1120, stage 2): a settler NPC moves in once a base carries enough
/// machines — known to the owner from day one — and never before. No NPC ever damages a block (the settler
/// only exists; there is no block-touching code path at all).
/// </summary>
public sealed class BaseLifeTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public BaseLifeTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_baselife_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private SvGameServer Start(out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "baselife"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var server = new SvGameServer(new ServerConfig
        {
            WorldName = "baselife",
            Seed = 4242,
            StartPlanet = "rocky",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = false,
            PlaceWrecks = false,
        }, _content, st, repo);
        server.Start();
        return server;
    }

    [Fact]
    public void ASettlerMovesIn_OnceTheBaseHasMachines_AndIsKnownToTheOwner()
    {
        var server = Start(out var repo);
        using (repo)
        {
            var owner = server.AddLocalPlayer("Homesteader");
            var feet = owner.State.Position;
            var core = new Vector3i((int)Math.Floor(feet.X) + 3, (int)Math.Floor(feet.Y) + 4, (int)Math.Floor(feet.Z));
            server.PlaceBaseForTest(owner, core);
            int baseId = server.BaseSnapshots.Single(b => b.OwnerId == owner.State.PlayerId).Id;

            // A bare claim attracts nobody.
            server.ScanBaseLifeForTest();
            Assert.Null(server.BaseSettlerForTest(baseId));
            Assert.DoesNotContain(server.NpcSnapshots, n => n.Role == "settler");

            // Three machines make it a home — the settler moves in, known to the owner from day one.
            var workbench = _content.GetBlock("workbench")!.NumericId;
            server.World.SetBlock(new Vector3i(core.X + 1, core.Y, core.Z), workbench, 0, 0, 0, "Homesteader");
            server.World.SetBlock(new Vector3i(core.X + 2, core.Y, core.Z), workbench, 0, 0, 0, "Homesteader");
            server.World.SetBlock(new Vector3i(core.X + 1, core.Y, core.Z + 1), _content.GetBlock("forge")!.NumericId, 0, 0, 0, "Homesteader");

            server.ScanBaseLifeForTest();
            int? settlerId = server.BaseSettlerForTest(baseId);
            Assert.NotNull(settlerId);
            Assert.Contains(server.NpcSnapshots, n => n.Id == settlerId!.Value && n.Role == "settler");
            Assert.Contains(owner.State.NpcMemory.Values, r => r.Role == "settler" && r.Value >= 10);

            // Running the scan again never doubles the settler.
            server.ScanBaseLifeForTest();
            Assert.Equal(1, server.NpcSnapshots.Count(n => n.Role == "settler"));
        }
    }
}
