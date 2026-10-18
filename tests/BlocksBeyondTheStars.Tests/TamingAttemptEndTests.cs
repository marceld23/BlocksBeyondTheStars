// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #2530: the client's taming panel holds the cursor and pauses the controls while an attempt is open, so an attempt
/// must never end SILENTLY. When its animal dies or wanders out of reach, the server ends the attempt on the next creature
/// tick and tells the player (a failed <see cref="TameResult"/>), which closes the panel and frees the controls.
/// </summary>
public sealed class TamingAttemptEndTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public TamingAttemptEndTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_tameend_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Started(RecordingTransport transport, out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "tameend"));
        var config = new ServerConfig
        {
            WorldName = "tameend",
            Seed = 4242,
            StartPlanet = "jungle", // "many" fauna
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            World = { TerrainGeneration = 0 }, // the classic relief, as in CreatureTamingTests
        };
        var server = new SvGameServer(config, _content, transport, repo);
        server.Start();
        return server;
    }

    /// <summary>A ranger standing on a wild creature with an open taming attempt on it; returns that creature.</summary>
    private static CombatEntity RangerTaming(SvGameServer server, out PlayerSession ranger)
    {
        ranger = server.AddLocalPlayer("Ranger");
        ranger.State.AboardShip = false;
        ranger.State.Position = new Vector3f(0, 64, 0);
        ranger.State.Inventory.Add("creature_translator", 1, 1);
        server.Tick(6.0);

        var creature = server.Creatures.First(c => c.OwnerId.Length == 0);
        ranger.State.Position = creature.Position;
        server.TameDecodeForTest("Ranger");
        Assert.False(string.IsNullOrEmpty(server.TameCurrentNeedForTest("Ranger")), "the decode should open an attempt");
        return server.Creatures.First(c => c.Id == server.TameTargetForTest("Ranger"));
    }

    private static List<TameResult> ResultsTo(RecordingTransport t, PlayerSession who)
        => t.Sent.Where(s => s.Conn == who.ConnectionId).Select(s => s.Msg).OfType<TameResult>().ToList();

    [Fact]
    public void ACreatureThatDies_EndsTheAttempt_AndThePlayerIsTold()
    {
        var transport = new RecordingTransport();
        var server = Started(transport, out var repo);
        using (repo)
        {
            var creature = RangerTaming(server, out var ranger);
            transport.Sent.Clear();

            creature.Hull = 0f;
            server.Tick(0.1);

            Assert.Equal(-1, server.TameTrustForTest("Ranger")); // no attempt left
            var result = Assert.Single(ResultsTo(transport, ranger));
            Assert.False(result.Success);
            Assert.Equal(creature.Id, result.CreatureId);
            Assert.Equal("creature.tame.msg.gone", result.MessageKey);
        }
    }

    [Fact]
    public void ACreatureOutOfReach_EndsTheAttempt_AndThePlayerIsTold()
    {
        var transport = new RecordingTransport();
        var server = Started(transport, out var repo);
        using (repo)
        {
            var creature = RangerTaming(server, out var ranger);
            transport.Sent.Clear();

            var at = creature.Position;
            ranger.State.Position = new Vector3f(at.X + 40f, at.Y, at.Z); // far past the 12-block reach
            server.Tick(0.1);

            Assert.Equal(-1, server.TameTrustForTest("Ranger"));
            Assert.False(Assert.Single(ResultsTo(transport, ranger)).Success);
        }
    }

    [Fact]
    public void ACreatureStillBesideThePlayer_KeepsTheAttemptOpen()
    {
        var transport = new RecordingTransport();
        var server = Started(transport, out var repo);
        using (repo)
        {
            var creature = RangerTaming(server, out var ranger);
            transport.Sent.Clear();

            ranger.State.Position = creature.Position;
            server.Tick(0.1);

            Assert.Equal(0, server.TameTrustForTest("Ranger")); // still open, nothing earned yet
            Assert.Empty(ResultsTo(transport, ranger));
        }
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // best effort — a still-open SQLite handle on Windows
        }
    }
}
