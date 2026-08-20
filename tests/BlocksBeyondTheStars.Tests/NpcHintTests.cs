// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// NPC treasure hints: a greeted settlement NPC can reveal the world's wreck (for anyone) or a hidden
/// treasure chest (relationship tier "known"+) as a map POI — persisted world-globally in
/// <c>WorldMetadata.RevealedPois</c> — while speaking a localized direction + distance line. Wreck and
/// chests stay OFF the POI list until revealed; a claimed wreck and a looted chest drop out again.
/// </summary>
public sealed class NpcHintTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public NpcHintTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_npchints_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private ServerConfig Config(long seed, bool wrecks, bool chests) => new()
    {
        WorldName = "w" + seed,
        Seed = seed,
        StartPlanet = "jungle", // hospitable, so inhabited settlements (and wrecks) actually appear
        AutoSaveIntervalMinutes = 9999,
        PlaceStarterShip = false,
        PlaceSettlements = true,
        PlaceWrecks = wrecks,
        PlaceChests = chests,
        PlaceRuins = false,
        PlaceVaults = false,
        PlaceDataCubes = false,
    };

    private SvGameServer Started(string saveName, long seed, bool wrecks, bool chests, out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, saveName));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var server = new SvGameServer(Config(seed, wrecks, chests), _content, st, repo);
        server.Start();
        return server;
    }

    /// <summary>Finds a seed whose world has an inhabited settlement with a vendor plus the requested
    /// features, then drops a player onto the vendor's marker (in greeting reach). Seed-search pattern
    /// shared with the settlement/wreck test suites.</summary>
    private SvGameServer StartedWithVendor(bool needWreck, bool needChest,
        out SqliteWorldRepository repo, out PlayerSession player, out long seed, out Vector3f vendorHome)
    {
        for (seed = 1; seed <= 200; seed++)
        {
            var server = Started(SaveName(seed, needWreck, needChest), seed, wrecks: needWreck, chests: needChest, out repo);
            if (server.HasSettlement && !server.SettlementRuined
                && server.NpcSnapshots.Any(n => n.Role == "vendor")
                && (!needWreck || server.HasWreck)
                && (!needChest || server.Containers.Any(c => c.Id.StartsWith("loot_chest_", StringComparison.Ordinal))))
            {
                vendorHome = server.NpcSnapshots.First(n => n.Role == "vendor").Home;
                player = server.AddLocalPlayer("Visitor", "en");
                player.State.Position = vendorHome;
                return server;
            }

            repo.Dispose();
        }

        throw new Xunit.Sdk.XunitException(
            $"No world with an inhabited vendor settlement (wreck={needWreck}, chest={needChest}) across 200 seeds.");
    }

    private static string SaveName(long seed, bool wrecks, bool chests) => $"s{seed}_{wrecks}_{chests}";

    // ---- Wreck hints ---------------------------------------------------------------------------

    [Fact]
    [Trait("Category", "Slow")]
    public void WreckHint_RevealsPoiForEveryone_SpeaksDirection_AndPersists()
    {
        var server = StartedWithVendor(needWreck: true, needChest: false, out var repo, out var p, out long seed, out _);
        string pid = p.State.PlayerId;

        // Hidden until hinted — the wreck is deliberately not on the map after stamping.
        Assert.DoesNotContain(server.PlanetPoisForTest(pid), poi => poi.Type == "wreck");

        var buddy = server.AddLocalPlayer("Buddy", "de");
        var line = server.HintLineForTest(pid, "vendor");
        Assert.False(string.IsNullOrEmpty(line));
        Assert.Contains("crashed ship", line);           // the player's locale (en) template
        Assert.Matches(@"\d+ m", line);                  // rough distance
        Assert.Contains(server.PlanetPoisForTest(pid), poi => poi.Type == "wreck" && poi.Name == server.WreckName);

        // World-global reveal: the second player's POI list carries it too.
        Assert.Contains(server.PlanetPoisForTest(buddy.State.PlayerId), poi => poi.Type == "wreck");

        // Nothing new to share on a re-ask (stranger tier ⇒ no chest secrets either).
        Assert.Equal(string.Empty, server.HintLineForTest(pid, "vendor"));

        // Persisted: a fresh server over the same save re-derives the wreck AND keeps it revealed.
        repo.Dispose();
        var reloaded = Started(SaveName(seed, wrecks: true, chests: false), seed, wrecks: true, chests: false, out var repo2);
        using (repo2)
        {
            var again = reloaded.AddLocalPlayer("Visitor", "en");
            Assert.Contains(reloaded.PlanetPoisForTest(again.State.PlayerId), poi => poi.Type == "wreck");
        }
    }

    [Fact]
    [Trait("Category", "Slow")]
    public void ClaimedWreck_DropsPoi_AndIsNotHintedAgain()
    {
        var server = StartedWithVendor(needWreck: true, needChest: false, out var repo, out var p, out _, out _);
        using (repo)
        {
            string pid = p.State.PlayerId;
            Assert.False(string.IsNullOrEmpty(server.HintLineForTest(pid, "vendor")));
            Assert.Contains(server.PlanetPoisForTest(pid), poi => poi.Type == "wreck");

            server.SetWreckClaimedForTest(true); // it's the player's ship now — no longer a "find"
            Assert.DoesNotContain(server.PlanetPoisForTest(pid), poi => poi.Type == "wreck");
        }
    }

    [Fact]
    [Trait("Category", "Slow")]
    public void NoFeaturesOnWorld_HintIsEmpty_AndNothingRevealed()
    {
        var server = StartedWithVendor(needWreck: false, needChest: false, out var repo, out var p, out _, out _);
        using (repo)
        {
            string pid = p.State.PlayerId;
            server.SetNpcRelationshipForTest(pid, "vendor", 50); // even a trusted friend has nothing LOCAL to share

            // A trusted friend now shares the galaxy's LEGENDS first (#1129) — pre-share them so this
            // test keeps probing the local wreck/chest hints, which is what it is about.
            while (!string.IsNullOrEmpty(server.UniqueSiteHintForTest(pid)))
            {
            }

            Assert.Equal(string.Empty, server.HintLineForTest(pid, "vendor"));
            Assert.DoesNotContain(server.PlanetPoisForTest(pid), poi => poi.Type is "wreck" or "treasure");
        }
    }

    // ---- Chest hints ---------------------------------------------------------------------------

    [Fact]
    [Trait("Category", "Slow")]
    public void ChestHint_IsGatedOnRelationship_AndLootingDropsThePoi()
    {
        var server = StartedWithVendor(needWreck: false, needChest: true, out var repo, out var p, out _, out var vendorHome);
        using (repo)
        {
            string pid = p.State.PlayerId;

            // A stranger gets no chest secrets.
            Assert.Equal(string.Empty, server.HintLineForTest(pid, "vendor"));
            Assert.DoesNotContain(server.PlanetPoisForTest(pid), poi => poi.Type == "treasure");

            // A known friend does — the nearest unrevealed chest appears on the map.
            server.SetNpcRelationshipForTest(pid, "vendor", 20);
            var line = server.HintLineForTest(pid, "vendor");
            Assert.False(string.IsNullOrEmpty(line));
            Assert.Contains("hidden cache", line);
            var poi = server.PlanetPoisForTest(pid).FirstOrDefault(x => x.Type == "treasure");
            Assert.NotNull(poi);

            // Loot the revealed chest — its container despawns and the marker disappears with it.
            var chest = server.Containers.First(c => c.Id.StartsWith("loot_chest_", StringComparison.Ordinal)
                && Math.Abs(c.Position.X + 0.5f - poi!.X) < 0.01f && Math.Abs(c.Position.Z + 0.5f - poi.Z) < 0.01f);
            p.State.AboardShip = false;
            p.State.Position = new Vector3f(chest.Position.X + 0.5f, chest.Position.Y + 0.5f, chest.Position.Z + 0.5f);
            server.LootContainer(pid, chest.Id);

            p.State.Position = vendorHome; // back in greeting reach
            Assert.DoesNotContain(server.PlanetPoisForTest(pid),
                x => x.Type == "treasure" && Math.Abs(x.X - poi!.X) < 0.01f && Math.Abs(x.Z - poi.Z) < 0.01f);
        }
    }

    // ---- Direction words -----------------------------------------------------------------------

    [Fact]
    public void DirectionKey_MapsAllEightSectors()
    {
        Assert.Equal("dir.n", SvGameServer.DirectionKeyForTest(0, 10));
        Assert.Equal("dir.ne", SvGameServer.DirectionKeyForTest(10, 10));
        Assert.Equal("dir.e", SvGameServer.DirectionKeyForTest(10, 0));
        Assert.Equal("dir.se", SvGameServer.DirectionKeyForTest(10, -10));
        Assert.Equal("dir.s", SvGameServer.DirectionKeyForTest(0, -10));
        Assert.Equal("dir.sw", SvGameServer.DirectionKeyForTest(-10, -10));
        Assert.Equal("dir.w", SvGameServer.DirectionKeyForTest(-10, 0));
        Assert.Equal("dir.nw", SvGameServer.DirectionKeyForTest(-10, 10));
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch { }
    }
}
