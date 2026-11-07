// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.Sounders;
using BlocksBeyondTheStars.Shared.State;
using Microsoft.Data.Sqlite;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The story Soundi (#3361): the Sounders pack grants it after the sing duel — a travelling companion of kind "soundi", one
/// per player, outside the six-pet cap, with a fixed bond, named by the player with "Muffin" suggested. It follows to every
/// world, is never released (it waits on board, #3344), keeps its kind across a restart and sings now and then.
/// </summary>
public sealed class StoryCompanionTests : IDisposable
{
    private static readonly Vector3i Centre = new(300, 150, 300);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_story_companion_" + Guid.NewGuid().ToString("N"));
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    private readonly List<SqliteWorldRepository> _repos = new();

    public void Dispose()
    {
        foreach (var repo in _repos)
        {
            repo.Dispose();
        }

        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // best-effort temp cleanup
        }
        catch (UnauthorizedAccessException)
        {
            // best-effort temp cleanup
        }
    }

    private SvGameServer Server(string name, RecordingTransport transport)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        _repos.Add(repo);
        var config = new ServerConfig
        {
            WorldName = name,
            Seed = 4242,
            StartPlanet = "rocky",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = false,
            PlaceWrecks = false,
            ViewDistanceChunks = 1,
        };
        var server = new SvGameServer(config, _content, transport, repo);
        server.Start();
        var stone = _content.GetBlock("stone")!.NumericId;
        for (int x = -6; x <= 6; x++)
        {
            for (int z = -6; z <= 6; z++)
            {
                server.World.SetBlock(new Vector3i(Centre.X + x, Centre.Y - 1, Centre.Z + z), stone);
                for (int y = 0; y < 3; y++)
                {
                    server.World.SetBlock(new Vector3i(Centre.X + x, Centre.Y + y, Centre.Z + z), BlockId.Air);
                }
            }
        }

        return server;
    }

    private (SvGameServer Server, RecordingTransport Transport, PlayerSession Owner) Started(string name)
    {
        var transport = new RecordingTransport();
        var server = Server(name, transport);
        var owner = server.AddLocalPlayer("Ada");
        owner.State.AboardShip = false;
        owner.State.Position = new Vector3f(Centre.X + 0.5f, Centre.Y, Centre.Z + 0.5f);
        return (server, transport, owner);
    }

    private static TamedCreature Grant(SvGameServer server)
        => server.GrantStoryCompanionForTest("Ada", TamedCreature.SoundiKind, SoundersIds.Soundi, "Muffin")!;

    private static CombatEntity? SoundiEntity(SvGameServer server, TamedCreature tc)
        => server.CompanionEntitiesForTest("Ada").FirstOrDefault(c => c.CompanionId == tc.Id);

    [Fact]
    public void TheGrant_CreatesATravellingSoundi_NamedMuffin_AndOpensTheNamePrompt()
    {
        var (server, transport, owner) = Started("story_grant");

        var tc = Grant(server);

        Assert.Same(tc, Assert.Single(owner.State.TamedCreatures));
        Assert.Equal(TamedCreature.SoundiKind, tc.Kind);
        Assert.True(tc.Roams);
        Assert.True(tc.Following);
        Assert.Equal(100, tc.Bond);
        Assert.Equal("Muffin", tc.Name);
        Assert.Equal("au_" + SoundersIds.Soundi, tc.SpeciesId); // the wild Soundi's id — the client draws the authored look
        Assert.Equal(tc.SpeciesId, tc.Species.Id);
        Assert.Equal(0, tc.Species.Eyes); // the Soundi has no eyes, whatever the data set holds

        var prompt = transport.To<TameResult>(owner).Last();
        Assert.True(prompt.Success);
        Assert.Equal(tc.Id, prompt.CompanionId);
        Assert.Equal("Muffin", prompt.SuggestedName);
        Assert.Equal("creature.tame.msg.story", prompt.MessageKey);

        var live = SoundiEntity(server, tc);
        Assert.NotNull(live);
        Assert.True(live!.CompanionRoams);
        var listed = transport.To<CompanionList>(owner).Last().Companions.Single();
        Assert.True(listed.Roams);
        Assert.Equal(TamedCreature.SoundiKind, listed.Kind);
    }

    [Fact]
    public void TheSoundisLook_ComesFromItsAuthoredRecord_OffItsHomeWorldToo()
    {
        if (!_content.AuthoredCreatures.TryGetValue(SoundersIds.Soundi, out var record))
        {
            return; // a data set without the record: the code-built stand-in (pinned by the grant test above)
        }

        var (server, _, _) = Started("story_record");

        var tc = Grant(server); // on a rocky world: no Soundi lives here, the record is built as on Soaniri

        Assert.Equal(record.ColorRgb, tc.Species.ColorRgb);
        Assert.Equal(record.BellyRgb, tc.Species.BellyRgb);
        Assert.Equal(record.Hide, tc.Species.Hide);
        Assert.True(tc.Species.DuelTame); // the record's flags travel along (inert on a companion)
        Assert.False(string.IsNullOrEmpty(tc.Species.LookPlanet));
    }

    [Fact]
    public void OneSoundiPerPlayer_AndNeverTheRobotsOrRudolphosKind()
    {
        var (server, _, owner) = Started("story_once");
        Assert.NotNull(Grant(server));

        Assert.Null(server.GrantStoryCompanionForTest("Ada", TamedCreature.SoundiKind, SoundersIds.Soundi, "Keks"));
        Assert.Null(server.GrantStoryCompanionForTest("Ada", TamedCreature.RobotKind, SoundersIds.Soundi, "Robo"));
        Assert.Null(server.GrantStoryCompanionForTest("Ada", TamedCreature.ReindeerKind, SoundersIds.Soundi, "Rudi"));
        Assert.Equal("Muffin", Assert.Single(owner.State.TamedCreatures).Name);
    }

    [Fact]
    public void TheSoundi_FollowsItsFriendToAnyWorld_AndTakesNoPetsPlace()
    {
        var (server, _, owner) = Started("story_roams");
        var species = server.SpeciesRoster.First();
        for (int i = 0; i < 6; i++)
        {
            owner.State.TamedCreatures.Add(new TamedCreature
            {
                Id = "pet" + i,
                HomeBodyId = server.ActiveLocationId,
                Name = "Pet " + i,
                SpeciesId = species.Id,
                Species = species,
                SizeScale = 1f,
                Bond = 50,
            });
        }

        server.ReconcileCompanionsForTest();
        var tc = Grant(server);
        tc.HomeBodyId = "some-other-body"; // granted on another world
        server.ReconcileCompanionsForTest();

        Assert.Equal(7, server.CompanionEntitiesForTest("Ada").Count); // six pets and the Soundi beside them
        Assert.NotNull(SoundiEntity(server, tc));
    }

    [Fact]
    public void Release_PutsTheSoundiOnBoard_ItIsNeverLost()
    {
        var (server, transport, owner) = Started("story_release");
        var tc = Grant(server);

        server.ReleaseCompanionForTest("Ada", tc.Id);
        server.ReconcileCompanionsForTest();

        Assert.Same(tc, Assert.Single(owner.State.TamedCreatures));
        Assert.False(tc.Following);
        Assert.Null(SoundiEntity(server, tc));
        Assert.Contains("@srv.companion.on_board:Muffin", transport.TextsTo(owner));

        server.SetCompanionFollowForTest("Ada", tc.Id, following: true);
        Assert.NotNull(SoundiEntity(server, tc));
    }

    [Fact]
    public void TheSoundi_SingsNowAndThen()
    {
        var (server, transport, _) = Started("story_sings");
        Grant(server);

        server.TickStoryCompanionsForTest(); // arms the song clock
        server.AdvanceUptimeForTest(200);
        server.TickStoryCompanionsForTest();

        var song = Assert.Single(transport.Messages().OfType<SoundFx>(), s => s.SoundId.StartsWith(SvGameServer.SoundiCallPrefix, StringComparison.Ordinal));
        int tune = int.Parse(song.SoundId.Substring(SvGameServer.SoundiCallPrefix.Length), System.Globalization.CultureInfo.InvariantCulture);
        Assert.InRange(tune, 1, SvGameServer.SoundiCallCount);
        Assert.Equal(2, song.SoundId.Length - SvGameServer.SoundiCallPrefix.Length); // soundi_call_07, two digits
    }

    /// <summary>#3314's lesson through the real save: after a restart the Soundi is still a roaming story companion, its bond
    /// never fades on the join that charges the days away, and it appears on the join world at once.</summary>
    [Fact]
    public void AfterARestart_MuffinIsStillASoundi_WithAFullBond_BesideItsFriend()
    {
        var (server, _, _) = Started("story_restart");
        var before = Grant(server);
        before.LastFedUtc = 1;
        before.TamedAtUtc = 1; // "never fed" for half a century — an animal's bond would sink to the floor
        before.HomeBodyId = "some-other-body";
        server.Stop();
        _repos[0].Dispose();
        _repos.Clear();
        SqliteConnection.ClearAllPools();

        var again = Server("story_restart", new RecordingTransport());
        again.HandlePayloadForTest(91, NetCodec.Encode(new JoinRequest { ContentFingerprint = TestJoin.Fingerprint, PlayerName = "Ada" }));

        var tc = Assert.Single(again.Sessions[91].State.TamedCreatures);
        Assert.Equal(TamedCreature.SoundiKind, tc.Kind);
        Assert.True(tc.Roams);
        Assert.True(tc.Following);
        Assert.Equal(100, tc.Bond);
        Assert.Equal("Muffin", tc.Name);
        Assert.Contains(again.CompanionEntitiesForTest("Ada"), c => c.CompanionId == tc.Id);
    }
}
