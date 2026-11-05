// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3245 distant fauna: the client only hears about the animals near it, so the server tells it the species of the world it
/// is in — once per world entry, never per tick — and the client lets those species call from far away. The roster carries
/// exactly the voice traits of each species, so a distant call is the voice the animal has up close.
/// </summary>
public sealed class FaunaRosterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_faunaroster_" + Guid.NewGuid().ToString("N"));
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    private readonly List<SqliteWorldRepository> _repos = new();

    public void Dispose()
    {
        foreach (var repo in _repos)
        {
            repo.Dispose();
        }

        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private SvGameServer NewServer(string name, IServerTransport transport, Action<ServerConfig>? configure = null)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        _repos.Add(repo);
        var config = new ServerConfig
        {
            WorldName = name,
            Seed = 1,
            StartPlanet = "rocky",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = false,
            PlaceWrecks = false,
            PlaceBanditCamps = false,
            ViewDistanceChunks = 1,
        };
        configure?.Invoke(config);
        var server = new SvGameServer(config, _content, transport, repo);
        server.Start();
        return server;
    }

    private static List<FaunaRoster> Rosters(List<(int Conn, object Msg)> sent, int conn)
        => sent.Where(x => x.Conn == conn).Select(x => x.Msg).OfType<FaunaRoster>().ToList();

    private static int IndexOf<T>(List<(int Conn, object Msg)> sent, int conn)
        => sent.FindIndex(x => x.Conn == conn && x.Msg is T);

    /// <summary>The roster is the world's species table, in order, with every voice trait and the waking hours.</summary>
    private static void AssertMatches(IReadOnlyList<CreatureSpecies> expected, FaunaRoster roster)
    {
        Assert.Equal(expected.Select(s => s.Id), roster.Species.Select(s => s.SpeciesId));
        for (int i = 0; i < expected.Count; i++)
        {
            var sp = expected[i];
            var net = roster.Species[i];
            Assert.Equal(sp.VoiceSeed, net.VoiceSeed);
            Assert.Equal(sp.Activity.ToString(), net.Activity);
            var near = VoiceTraits.From(sp);
            var far = new VoiceTraits(net.Habitat, net.Temperament, net.BodyPlan, net.Size, net.Legs, net.Eyes, net.Horns,
                net.BodySegments, net.Tentacles, net.HasGasSac, net.Glows);
            Assert.Equal((near.Habitat, near.Temperament, near.BodyPlan, near.Size, near.Legs, near.Eyes),
                (far.Habitat, far.Temperament, far.BodyPlan, far.Size, far.Legs, far.Eyes));
            Assert.Equal((near.Horns, near.BodySegments, near.Tentacles, near.HasGasSac, near.Glows),
                (far.Horns, far.BodySegments, far.Tentacles, far.HasGasSac, far.Glows));
        }
    }

    [Fact]
    public void AJoiningPlayer_IsToldTheWorldsSpecies_Once_AfterTheEnvironment()
    {
        var transport = new DrivenTransport();
        var server = NewServer("roster_join", transport);
        Assert.NotEmpty(server.SpeciesRoster); // a rocky world has fauna — otherwise this test would prove nothing

        transport.Connect(1);
        transport.Receive(1, new JoinRequest { ContentFingerprint = TestJoin.Fingerprint, PlayerName = "Justus", ViewDistanceChunks = 1 });

        AssertMatches(server.SpeciesRoster, Assert.Single(Rosters(transport.Sent, 1)));
        Assert.True(IndexOf<FaunaRoster>(transport.Sent, 1) > IndexOf<JoinAccepted>(transport.Sent, 1));
        Assert.True(IndexOf<FaunaRoster>(transport.Sent, 1) > IndexOf<WorldEnvironment>(transport.Sent, 1));

        // The environment heartbeat goes on every few seconds; the roster does not ride along with it.
        for (int i = 0; i < 30; i++)
        {
            server.Tick(0.5);
        }

        Assert.True(transport.Sent.Count(x => x.Conn == 1 && x.Msg is WorldEnvironment) > 1, "the heartbeat should have run");
        Assert.Single(Rosters(transport.Sent, 1));
        server.Stop();
    }

    [Fact]
    public void ATravel_TellsTheNewWorldsSpecies_AndTheShipInteriorHasNone()
    {
        var transport = new RecordingTransport();
        var server = NewServer("roster_travel", transport, c => c.Rules.FreeSpaceFlight = true);
        var p = server.AddLocalPlayer("Justus");
        string home = p.CurrentLocationId;
        var homeBody = server.Galaxy.FindBody(home)!;
        var other = server.Galaxy.AllBodies().First(b => b.Id != home && b.SystemId == homeBody.SystemId
            && b.Kind is CelestialKind.Planet or CelestialKind.Moon && !string.IsNullOrEmpty(b.PlanetType)
            && _content.GetPlanet(b.PlanetType!)?.CreatureAbundance is "few" or "many");

        transport.Sent.Clear();
        server.Travel("Justus", other.Id);
        Assert.Equal(other.Id, p.CurrentLocationId);
        Assert.NotEmpty(server.SpeciesRoster);
        AssertMatches(server.SpeciesRoster, Assert.Single(Rosters(transport.Sent, p.ConnectionId)));
        Assert.True(IndexOf<FaunaRoster>(transport.Sent, p.ConnectionId) > IndexOf<WorldReset>(transport.Sent, p.ConnectionId));

        // Up into space and inside the ship: a world without animals, so an empty roster replaces the planet's.
        transport.Sent.Clear();
        server.EnterSpace("Justus");
        server.EnterShipInterior("Justus");
        Assert.True(server.InShipInterior("Justus"));
        Assert.Empty(Assert.Single(Rosters(transport.Sent, p.ConnectionId)).Species);
        server.Stop();
    }

    [Fact]
    public void WithTheCreatureRuleOff_TheRosterIsEmpty()
    {
        var transport = new DrivenTransport();
        var server = NewServer("roster_off", transport, c => c.Rules.CreatureAbundance = AlienActivity.Off);

        transport.Connect(1);
        transport.Receive(1, new JoinRequest { ContentFingerprint = TestJoin.Fingerprint, PlayerName = "Justus", ViewDistanceChunks = 1 });

        Assert.Empty(Assert.Single(Rosters(transport.Sent, 1)).Species); // no animal lives here, so none calls from afar
        server.Stop();
    }

    [Fact]
    public void TheRoster_SurvivesTheCodec()
    {
        var roster = new FaunaRoster
        {
            Species =
            {
                new NetFaunaSpecies
                {
                    SpeciesId = "sp3", VoiceSeed = -1234567, Habitat = "Amphibian", Temperament = "Skittish", BodyPlan = "Medusa",
                    Size = 1.75f, Legs = 6, Eyes = 3, Horns = 2, BodySegments = 4, Tentacles = 8, HasGasSac = true, Glows = true,
                    Activity = "Crepuscular",
                },
            },
        };

        var back = Assert.IsType<FaunaRoster>(NetCodec.Decode(NetCodec.Encode(roster)));
        var sp = Assert.Single(back.Species);
        Assert.Equal(("sp3", -1234567, "Amphibian", "Skittish", "Medusa", 1.75f, "Crepuscular"),
            (sp.SpeciesId, sp.VoiceSeed, sp.Habitat, sp.Temperament, sp.BodyPlan, sp.Size, sp.Activity));
        Assert.Equal((6, 3, 2, 4, 8, true, true), (sp.Legs, sp.Eyes, sp.Horns, sp.BodySegments, sp.Tentacles, sp.HasGasSac, sp.Glows));
    }
}
