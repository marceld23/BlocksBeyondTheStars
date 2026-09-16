// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
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

/// <summary>Valuma's shapeshifter (2026-09): one Sreekmakra per world, disguised as a land animal of the roster with three
/// times its health; it changes shape while nobody watches, turns on a player who kills one of its shape's kind or hits
/// it, breaks its disguise at zero health, and its defeat opens the Codex entry, counts the achievement and keeps the next
/// one away for days. With planet enemies off it only reveals itself and flees. The scanner reads the disguise as an
/// anomaly; a long stay darkens the mood.</summary>
public sealed class SreekmakraTests : IDisposable
{
    private const string TrueForm = "au_sreekmakra";
    private readonly string _root;
    private readonly GameContent _content;

    public SreekmakraTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_sreek_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer NewServer(string world, Action<ServerConfig>? tune = null)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        var link = new LoopbackLink();
        var st = new LoopbackServerTransport(link);
        var client = new LoopbackClientTransport(link);
        var config = new ServerConfig { WorldName = world, Seed = 77, StartPlanet = "valuma", AutoSaveIntervalMinutes = 9999 };
        tune?.Invoke(config);
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        client.Connect("loopback", 0);
        client.Send(NetCodec.Encode(new JoinRequest { PlayerName = "Justus" }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        return server;
    }

    private static void TickSeconds(SvGameServer server, double seconds)
    {
        for (double t = 0; t < seconds; t += 0.1)
        {
            server.Tick(0.1);
        }
    }

    /// <summary>A server whose player stands on foot in the open with the shapeshifter placed somewhere around them.</summary>
    private SvGameServer WithSreekmakra(string world, Action<ServerConfig>? tune = null)
    {
        var server = NewServer(world, tune);
        var p = server.Sessions[1].State;
        p.AboardShip = false;
        p.Position = new Vector3f(300.5f, 90f, 300.5f);
        for (int i = 0; i < 20 && server.SreekmakraForTest() is null; i++)
        {
            server.TickSreekmakraForTest();
        }

        Assert.NotNull(server.SreekmakraForTest());
        return server;
    }

    private static void Hit(SvGameServer server, string playerId, string creatureId, Func<bool> until, int max = 400)
    {
        for (int i = 0; i < max && !until(); i++)
        {
            var c = server.Creatures.FirstOrDefault(x => x.Id == creatureId);
            if (c is null)
            {
                return;
            }

            server.Sessions[1].State.Position = c.Position;
            server.AttackEntity(playerId, creatureId);
        }
    }

    [Fact]
    public void OneShapeshifter_WearsALandAnimal_WithThreeTimesItsHealth_AndNeverSpawnsAsAnAnimal()
    {
        var server = WithSreekmakra("disguise");
        var s = server.SreekmakraForTest()!.Value;
        Assert.DoesNotContain("au_", s.SpeciesId);
        Assert.False(s.Revealed);
        Assert.Equal(s.HullMax, s.Hull);
        Assert.True(s.HullMax >= 3f * 5f);

        TickSeconds(server, 40); // the world fills with animals around the player
        Assert.DoesNotContain(server.Creatures, c => c.SpeciesId == TrueForm);
        Assert.Single(server.Creatures, c => c.Id == s.Id);
        server.TickSreekmakraForTest();
        Assert.Equal(s.Id, server.SreekmakraForTest()!.Value.Id); // still the one — no second shapeshifter

        // Unobserved (the player stands 40+ blocks away) it takes another shape.
        server.ShiftSreekmakraForTest();
        var shifted = server.SreekmakraForTest()!.Value;
        Assert.NotEqual(s.SpeciesId, shifted.SpeciesId);
        Assert.DoesNotContain("au_", shifted.SpeciesId);
    }

    [Fact]
    public void KillingAnAnimalOfItsShape_TurnsItOnTheKiller_AndTheScannerReadsAnAnomaly()
    {
        var server = WithSreekmakra("grudge");
        var p = server.Sessions[1].State;
        var s = server.SreekmakraForTest()!.Value;
        var sreek = server.Creatures.First(c => c.Id == s.Id);

        // The scanner, right beside it: the disguise fools the eye, not the instrument.
        p.Position = new Vector3f(sreek.Position.X + 3f, sreek.Position.Y, sreek.Position.Z);
        Assert.True(server.SreekmakraAnomalyForTest(p.PlayerId, s.SpeciesId));
        Assert.Equal("ui.scan.threat.anomaly", server.ScanSubject(p.PlayerId, "creature", s.SpeciesId).ThreatKey);
        p.Position = new Vector3f(sreek.Position.X + 60f, sreek.Position.Y, sreek.Position.Z);
        Assert.False(server.SreekmakraAnomalyForTest(p.PlayerId, s.SpeciesId));

        // Kill an ordinary animal of the shape it wears.
        string victim = server.SpawnCreatureAtForTest(new Vector3f(sreek.Position.X + 50f, sreek.Position.Y, sreek.Position.Z), s.SpeciesId);
        Hit(server, p.PlayerId, victim, () => server.Creatures.All(c => c.Id != victim));
        Assert.DoesNotContain(server.Creatures, c => c.Id == victim);

        var angry = server.SreekmakraForTest()!.Value;
        Assert.Equal(p.PlayerId, angry.TargetPlayerId);
        Assert.False(angry.Revealed); // it attacks in the shape it wears
        Assert.True(server.NetCreatureForTest(s.Id).Hostile);
    }

    [Fact]
    public void AtZeroItsDisguiseBreaks_AndItsDefeat_OpensTheCodex_CountsTheAchievement_AndKeepsTheNextAway()
    {
        var server = WithSreekmakra("unmasked");
        var p = server.Sessions[1].State;
        string id = server.SreekmakraForTest()!.Value.Id;

        Hit(server, p.PlayerId, id, () => server.SreekmakraForTest() is { Revealed: true });
        var revealed = server.SreekmakraForTest()!.Value;
        Assert.True(revealed.Revealed);
        Assert.Equal(TrueForm, revealed.SpeciesId);
        Assert.Equal(90f, revealed.HullMax);
        Assert.Equal(p.PlayerId, revealed.TargetPlayerId);

        Hit(server, p.PlayerId, id, () => server.SreekmakraForTest() is null);
        Assert.Null(server.SreekmakraForTest());
        Assert.Contains("creature:" + TrueForm, p.Scanned);
        Assert.Equal(1, p.AchievementCounters.GetValueOrDefault("defeat:sreekmakra"));
        Assert.True(server.SreekmakraBackAtForTest() > DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 60);

        server.TickSreekmakraForTest();
        Assert.Null(server.SreekmakraForTest()); // days before the next one
    }

    [Fact]
    public void WithPlanetEnemiesOff_ItOnlyRevealsItself_AndFlees()
    {
        var server = WithSreekmakra("peaceful", c => c.Rules.PlanetEnemies = AlienActivity.Off);
        var p = server.Sessions[1].State;
        var s = server.SreekmakraForTest()!.Value;
        var sreek = server.Creatures.First(c => c.Id == s.Id);
        string victim = server.SpawnCreatureAtForTest(new Vector3f(sreek.Position.X + 50f, sreek.Position.Y, sreek.Position.Z), s.SpeciesId);
        Hit(server, p.PlayerId, victim, () => server.Creatures.All(c => c.Id != victim));

        var fled = server.SreekmakraForTest()!.Value;
        Assert.True(fled.Revealed);
        Assert.True(fled.Fleeing);
        Assert.Equal(string.Empty, fled.TargetPlayerId);
        Assert.False(server.NetCreatureForTest(s.Id).Hostile);

        TickSeconds(server, 27);
        server.TickSreekmakraForTest();
        Assert.DoesNotContain(server.Creatures, c => c.Id == s.Id);
        Assert.True(server.SreekmakraBackAtForTest() > DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    }

    /// <summary>Runs the translator ritual on the nearest wild creature with the right answer every step.</summary>
    private static void Tame(SvGameServer server, string playerId, Func<bool> done)
    {
        var p = server.Sessions[1].State;
        foreach (var bait in new[] { "forage_bait", "meat_bait", "nectar_lure" })
        {
            p.Inventory.Add(bait, 20, 99);
        }

        for (int i = 0; i < 24 && !done(); i++)
        {
            server.TameDecodeForTest(playerId);
            string need = server.TameCurrentNeedForTest(playerId);
            if (need.Length == 0)
            {
                return;
            }

            server.TameRespondForTest(playerId, need);
        }
    }

    [Fact]
    public void TamingItsDisguise_BondsTheShapeshifter_InItsTrueForm()
    {
        // #1926: "It also works if you tame it directly."
        var server = WithSreekmakra("tame-direct");
        var p = server.Sessions[1].State;
        var s = server.SreekmakraForTest()!.Value;
        p.Position = server.Creatures.First(c => c.Id == s.Id).Position;

        Tame(server, p.PlayerId, () => p.TamedCreatures.Count > 0);

        var pet = Assert.Single(p.TamedCreatures);
        Assert.Equal(TrueForm, pet.SpeciesId);
        Assert.Null(server.SreekmakraForTest()); // it left the herd
        Assert.Contains(server.Creatures, c => c.SpeciesId == TrueForm && c.OwnerId == p.PlayerId);
        Assert.Contains("creature:" + TrueForm, p.Scanned);
        Assert.Equal(1, p.AchievementCounters.GetValueOrDefault("tame:sreekmakra"));
        Assert.True(server.SreekmakraBackAtForTest() > DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 60);
    }

    [Fact]
    public void TamingAnAnimalOfItsShape_BringsTheShapeshifterAlong_BesideThePlayer()
    {
        // #1926: "If you tame an animal of the kind the Sreekmakra has turned into, you get the animal AND the Sreekmakra."
        var server = WithSreekmakra("tame-shape");
        var p = server.Sessions[1].State;
        var s = server.SreekmakraForTest()!.Value;
        var sreek = server.Creatures.First(c => c.Id == s.Id);
        string animal = server.SpawnCreatureAtForTest(new Vector3f(sreek.Position.X + 50f, sreek.Position.Y, sreek.Position.Z), s.SpeciesId);
        p.Position = server.Creatures.First(c => c.Id == animal).Position;

        Tame(server, p.PlayerId, () => p.TamedCreatures.Count > 0);

        Assert.Equal(2, p.TamedCreatures.Count);
        Assert.Contains(p.TamedCreatures, t => t.SpeciesId == s.SpeciesId);
        Assert.Contains(p.TamedCreatures, t => t.SpeciesId == TrueForm);
        Assert.Null(server.SreekmakraForTest());
        var follower = server.Creatures.Single(c => c.SpeciesId == TrueForm && c.OwnerId == p.PlayerId);
        float dx = follower.Position.X - p.Position.X, dz = follower.Position.Z - p.Position.Z;
        Assert.True(dx * dx + dz * dz < 10f * 10f, "it appears beside the player, not back where it grazed");
    }

    [Fact]
    public void ItsTrueForm_CannotBeTamed()
    {
        // #1926: "You can only tame it while it wears a shape."
        var server = WithSreekmakra("tame-revealed");
        var p = server.Sessions[1].State;
        string id = server.SreekmakraForTest()!.Value.Id;
        Hit(server, p.PlayerId, id, () => server.SreekmakraForTest() is { Revealed: true });
        Assert.True(server.SreekmakraForTest()!.Value.Revealed);
        p.Position = server.Creatures.First(c => c.Id == id).Position;

        Tame(server, p.PlayerId, () => p.TamedCreatures.Count > 0);

        Assert.Empty(p.TamedCreatures);
        Assert.NotNull(server.SreekmakraForTest());
    }

    [Fact]
    public void TheScanner_ReadsTheTrueName_OfExactlyTheAimedDisguise()
    {
        // #1926: "If you scan it while it is disguised, the scanner shows its true name, Sreekmakra, not the animal."
        var server = WithSreekmakra("scan-name");
        var p = server.Sessions[1].State;
        var s = server.SreekmakraForTest()!.Value;
        var sreek = server.Creatures.First(c => c.Id == s.Id);
        p.Position = new Vector3f(sreek.Position.X + 3f, sreek.Position.Y, sreek.Position.Z);

        var disguise = server.ScanSubject(p.PlayerId, "creature", s.SpeciesId, s.Id);
        Assert.Equal("ui.scan.threat.anomaly", disguise.ThreatKey);
        Assert.Equal(TrueForm, disguise.SubjectKey);
        Assert.StartsWith("Sreekmakra", disguise.Subject);
        Assert.Contains("creature:" + TrueForm, p.Scanned);

        // A real animal of the same kind standing even closer is just that animal.
        string animal = server.SpawnCreatureAtForTest(new Vector3f(p.Position.X + 1f, p.Position.Y, p.Position.Z), s.SpeciesId);
        var plain = server.ScanSubject(p.PlayerId, "creature", s.SpeciesId, animal);
        Assert.NotEqual("ui.scan.threat.anomaly", plain.ThreatKey);
        Assert.Equal(s.SpeciesId, plain.SubjectKey);
    }

    [Fact]
    public void ALongStay_BringsTheWatchedLine_ThenTheFog_AndLeavingResetsIt()
    {
        var server = NewServer("mood");
        var session = server.Sessions[1];
        server.TickSreekmakraForTest(); // the clock starts on this world
        server.SetValumaMoodSecondsForTest(session.State.PlayerId, 20 * 60 - 1);
        server.TickSreekmakraForTest();
        Assert.True(session.MoodWatchedTold);
        Assert.False(session.MoodUneasy);

        server.SetValumaMoodSecondsForTest(session.State.PlayerId, 35 * 60 - 1);
        server.TickSreekmakraForTest();
        Assert.True(session.MoodUneasy);

        session.MoodLocationId = "somewhere-else"; // as after a flight to another body
        server.TickSreekmakraForTest();
        Assert.False(session.MoodUneasy);
        Assert.False(session.MoodWatchedTold);
        Assert.True(session.MoodSeconds <= 1.0);
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
