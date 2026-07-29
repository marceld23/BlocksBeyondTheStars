// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The named half of the admin teleport: <c>/tp village2</c> instead of <c>/tp 812 71 -1904</c>. Targets are
/// addressed by kind + 1-based number (never by the procedural name), resolve on the CURRENT body only, and
/// sit under the same <c>CheatsAllowed</c> + admin-role gate as the coordinate teleport they extend — the
/// cross-body jump stays fleet-admin <c>/goto</c> (issue #487).
/// </summary>
public sealed class AdminNamedTeleportTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public AdminNamedTeleportTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_tpnamed_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private ServerConfig Config(long seed, string world, bool cheats) => new()
    {
        WorldName = world,
        Seed = seed,
        StartPlanet = "jungle", // hospitable ⇒ inhabited settlements actually get stamped
        AutoSaveIntervalMinutes = 9999,
        PlaceSettlements = true,
        PlaceStarterShip = true,
        PlaceWrecks = false,
        PlaceChests = false,
        PlaceRuins = false,
        PlaceVaults = false,
        PlaceDataCubes = false,
        PlaceBanditCamps = false,
        PlaceMonuments = false,
        PlaceFactories = false,
        Rules = new GameRules { AdminCheats = cheats, AllowCheatsInSurvival = cheats },
    };

    private SvGameServer Started(long seed, string world, bool cheats, out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var server = new SvGameServer(Config(seed, world, cheats), _content, st, repo);
        server.Start();
        return server;
    }

    /// <summary>A world that actually has an inhabited settlement, with the first player joined (⇒ WorldAdmin).
    /// Seed-search pattern shared with the settlement/NPC-hint suites: settlement counts are seeded, so a
    /// fixed seed would make the whole suite hostage to an unrelated worldgen tweak.</summary>
    private SvGameServer StartedWithVillage(bool cheats, out SqliteWorldRepository repo, out PlayerSession admin)
    {
        for (long seed = 1; seed <= 200; seed++)
        {
            var server = Started(seed, $"tp{seed}_{cheats}", cheats, out repo);
            if (server.HasSettlement && server.InhabitedSettlementCount > 0)
            {
                admin = server.AddLocalPlayer("Creator", "en");
                return server;
            }

            repo.Dispose();
        }

        throw new Xunit.Sdk.XunitException("No world with an inhabited settlement across 200 seeds.");
    }

    private static void Tp(SvGameServer server, PlayerSession session, string argument)
        => server.HandleForTest(session, new AdminCommandIntent { Command = "teleport_to_named", StringArg = argument });

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A save file still held open by a disposed-late repo must not fail the suite.
        }
    }

    [Fact]
    [Trait("Category", "Slow")]
    public void Village_Resolves_ByKind_AndMovesTheAdmin()
    {
        var server = StartedWithVillage(cheats: true, out var repo, out var admin);
        using var _ = repo;

        var targets = server.TeleportTargetsForTest(admin.State.PlayerId);
        var village = targets.First(t => t.Kind == "village" && t.Number == 1);

        admin.State.Position = new Vector3f(0, 500, 0);
        Tp(server, admin, "village");

        Assert.Equal(village.Position, admin.State.Position);
    }

    /// <summary>The two spellings of the number have to agree — "village2" is just "village 2" without the
    /// space, and both have to mean the same place.</summary>
    [Fact]
    [Trait("Category", "Slow")]
    public void SuffixedAndSpacedNumbers_ResolveToTheSameTarget()
    {
        var server = StartedWithVillage(cheats: true, out var repo, out var admin);
        using var _ = repo;

        int pads = server.TeleportTargetsForTest(admin.State.PlayerId).Count(t => t.Kind == "pad");
        Assert.True(pads >= 1, "a body always has at least one landing pad");

        Tp(server, admin, "pad1");
        var viaSuffix = admin.State.Position;

        admin.State.Position = new Vector3f(0, 500, 0);
        Tp(server, admin, "pad 1");

        Assert.Equal(viaSuffix, admin.State.Position);
    }

    /// <summary>Aliases exist so the word on the map and the word you type are never two different things.</summary>
    [Fact]
    [Trait("Category", "Slow")]
    public void SettlementAlias_ResolvesLikeVillage()
    {
        var server = StartedWithVillage(cheats: true, out var repo, out var admin);
        using var _ = repo;

        Tp(server, admin, "village");
        var viaVillage = admin.State.Position;

        admin.State.Position = new Vector3f(0, 500, 0);
        Tp(server, admin, "settlements");

        Assert.Equal(viaVillage, admin.State.Position);
    }

    [Fact]
    [Trait("Category", "Slow")]
    public void Ship_ResolvesToTheOwnParkedShip()
    {
        var server = StartedWithVillage(cheats: true, out var repo, out var admin);
        using var _ = repo;

        var ship = server.TeleportTargetsForTest(admin.State.PlayerId).Single(t => t.Kind == "ship");

        admin.State.Position = new Vector3f(0, 500, 0);
        Tp(server, admin, "ship");

        // The medbay heal tank — the same spot the craftable suit teleporter recalls to.
        Assert.Equal(ship.Position, admin.State.Position);
        Assert.Equal(server.HealTank, admin.State.Position);
    }

    /// <summary>An out-of-range number and an unknown word both have to refuse rather than land the admin
    /// somewhere arbitrary — silently snapping to #1 would be worse than saying no.</summary>
    [Fact]
    [Trait("Category", "Slow")]
    public void UnknownWord_OrNumberOutOfRange_DoesNotMoveTheAdmin()
    {
        var server = StartedWithVillage(cheats: true, out var repo, out var admin);
        using var _ = repo;

        var parked = new Vector3f(1, 500, 2);

        admin.State.Position = parked;
        Tp(server, admin, "nonsense");
        Assert.Equal(parked, admin.State.Position);

        admin.State.Position = parked;
        Tp(server, admin, "village99");
        Assert.Equal(parked, admin.State.Position);
    }

    /// <summary>The listing form must never move anyone — it is a query.</summary>
    [Fact]
    [Trait("Category", "Slow")]
    public void EmptyArgument_ListsWithoutMoving()
    {
        var server = StartedWithVillage(cheats: true, out var repo, out var admin);
        using var _ = repo;

        var parked = new Vector3f(3, 500, 4);
        admin.State.Position = parked;
        Tp(server, admin, string.Empty);

        Assert.Equal(parked, admin.State.Position);
    }

    /// <summary>Same gate as /tp X Y Z: naming the destination must not become a way around the world's
    /// "cheats off" setting.</summary>
    [Fact]
    [Trait("Category", "Slow")]
    public void Rejected_WhenCheatsAreDisabled()
    {
        var server = StartedWithVillage(cheats: false, out var repo, out var admin);
        using var _ = repo;

        var parked = new Vector3f(5, 500, 6);
        admin.State.Position = parked;
        Tp(server, admin, "village");

        Assert.Equal(parked, admin.State.Position);
    }

    [Fact]
    [Trait("Category", "Slow")]
    public void Rejected_ForANonAdminPlayer()
    {
        var server = StartedWithVillage(cheats: true, out var repo, out var admin);
        using var _ = repo;
        Assert.Equal(PlayerRole.WorldAdmin, admin.State.Role); // the first joiner owns the world…

        var guest = server.AddLocalPlayer("Guest", "en");      // …the second is an ordinary player
        Assert.False(guest.State.IsAdmin);

        var parked = new Vector3f(7, 500, 8);
        guest.State.Position = parked;
        Tp(server, guest, "village");

        Assert.Equal(parked, guest.State.Position);
    }

    /// <summary>Numbering is per kind and 1-based with no gaps — that is the whole addressing scheme, so it
    /// is worth pinning rather than trusting the enumeration order that produced it.</summary>
    [Fact]
    [Trait("Category", "Slow")]
    public void Numbering_IsPerKind_AndGapless()
    {
        var server = StartedWithVillage(cheats: true, out var repo, out var admin);
        using var _ = repo;

        foreach (var group in server.TeleportTargetsForTest(admin.State.PlayerId).GroupBy(t => t.Kind))
        {
            Assert.Equal(Enumerable.Range(1, group.Count()), group.Select(t => t.Number).OrderBy(n => n));
        }
    }
}
