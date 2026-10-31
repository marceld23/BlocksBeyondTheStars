// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Justus' wishes of 2026-10-30, the creature and planet parts: a name tag names your own companion for good (#3035) and only
/// yours; companion names lose control characters (#3034); every planet with air has four seasons that warm and cool it and
/// slow or speed the plants (#3041); and the system clock they run on survives a restart.
/// </summary>
public sealed class NameTagAndSeasonsTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;
    private readonly List<SqliteWorldRepository> _repos = new();

    public NameTagAndSeasonsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_tagseason_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        foreach (var r in _repos)
        {
            r.Dispose();
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_root, true);
        }
        catch (IOException)
        {
            // best effort
        }
        catch (UnauthorizedAccessException)
        {
            // best effort
        }
    }

    private SvGameServer NewServer(string name, RecordingTransport transport, string startPlanet = "rocky")
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        _repos.Add(repo);
        var server = new SvGameServer(new ServerConfig
        {
            WorldName = name,
            Seed = 3,
            StartPlanet = startPlanet,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
        }, _content, transport, repo);
        server.Start();
        return server;
    }

    private static (PlayerSession Keeper, TamedCreature Pet) KeeperWithPet(SvGameServer server)
    {
        var keeper = server.AddLocalPlayer("Keeper");
        keeper.State.AboardShip = false;
        var sp = server.SpeciesRoster.First();
        var pet = new TamedCreature { Id = "c1", HomeBodyId = server.ActiveLocationId, Name = "Flöckchen", SpeciesId = sp.Id, Species = sp, SizeScale = 1f, Bond = 50 };
        keeper.State.TamedCreatures.Add(pet);
        server.SpawnCompanionsForTest(keeper.State.PlayerId);
        return (keeper, pet);
    }

    private static IEnumerable<string> MessagesTo(RecordingTransport t, PlayerSession who)
        => t.Sent.Where(s => s.Conn == who.ConnectionId).Select(s => s.Msg).OfType<ServerMessage>().Select(m => m.Text);

    private static void UseTag(SvGameServer server, PlayerSession p, string id, string name)
        => server.HandlePayloadForTest(p.ConnectionId, BlocksBeyondTheStars.Networking.NetCodec.Encode(
            new SetCompanionNameIntent { CompanionId = id, Name = name, UseNameTag = true }));

    // ---------------- #3035 name tag ----------------

    [Fact]
    public void ANameTag_OnMyCompanion_NamesIt_TakesOneTag_AndMakesItAHologram()
    {
        var t = new RecordingTransport();
        var server = NewServer("tag_own", t);
        var (keeper, pet) = KeeperWithPet(server);
        keeper.State.Inventory.Add(SvGameServer.NameTagItem, 2, 16);
        var creature = server.CompanionEntitiesForTest(keeper.State.PlayerId).Single();

        UseTag(server, keeper, creature.Id, "Blitz"); // the client sends the aimed creature's id

        Assert.Equal("Blitz", pet.Name);
        Assert.True(pet.NameTag);
        Assert.True(creature.NameTag);
        Assert.Equal(1, keeper.State.Inventory.CountOf(SvGameServer.NameTagItem));
        Assert.Contains("@creature.nametag.applied:Blitz", MessagesTo(t, keeper));

        UseTag(server, keeper, "c1", "Donner"); // it wears one already: a rename, no second tag
        Assert.Equal("Donner", pet.Name);
        Assert.Equal(1, keeper.State.Inventory.CountOf(SvGameServer.NameTagItem));
    }

    [Fact]
    public void WithoutATag_OrOnSomeoneElsesAnimal_NothingChanges()
    {
        var t = new RecordingTransport();
        var server = NewServer("tag_refused", t);
        var (keeper, pet) = KeeperWithPet(server);

        UseTag(server, keeper, "c1", "Blitz");
        Assert.Equal("Flöckchen", pet.Name);
        Assert.False(pet.NameTag);
        Assert.Contains("@creature.nametag.none", MessagesTo(t, keeper));

        var other = server.AddLocalPlayer("Other");
        other.State.Inventory.Add(SvGameServer.NameTagItem, 1, 16);
        UseTag(server, other, server.CompanionEntitiesForTest(keeper.State.PlayerId).Single().Id, "Mine");
        Assert.Equal("Flöckchen", pet.Name);
        Assert.Equal(1, other.State.Inventory.CountOf(SvGameServer.NameTagItem));
        Assert.Contains("@creature.nametag.wild", MessagesTo(t, other));
    }

    [Fact]
    public void ANameTag_IsAHandRecipe_AndTheTagSurvivesTheSave()
    {
        Assert.NotNull(_content.GetItem(SvGameServer.NameTagItem));
        Assert.Contains(_content.Recipes.Values, r => r.Outputs.Any(o => o.Item == SvGameServer.NameTagItem));

        var t = new RecordingTransport();
        var server = NewServer("tag_save", t);
        var (keeper, _) = KeeperWithPet(server);
        keeper.State.Inventory.Add(SvGameServer.NameTagItem, 1, 16);
        UseTag(server, keeper, "c1", "Blitz");
        server.SaveAllForTest();

        var reloaded = _repos[0].LoadPlayer(keeper.State.PlayerId);
        Assert.NotNull(reloaded);
        Assert.True(reloaded!.TamedCreatures.Single().NameTag);
    }

    // ---------------- #3034 control characters ----------------

    [Fact]
    public void ACompanionName_LosesItsControlCharacters()
    {
        var t = new RecordingTransport();
        var server = NewServer("tag_ctrl", t);
        var (keeper, pet) = KeeperWithPet(server);

        server.RenameCompanionForTest(keeper.State.PlayerId, "c1", "Bl\u0007itz\n");

        Assert.Equal("Bl itz", pet.Name); // a control character becomes a space, like in a base name; the line break is trimmed
        Assert.DoesNotContain(pet.Name, char.IsControl);
    }

    // ---------------- #3041 seasons ----------------

    [Fact]
    public void AYear_RunsSpringSummerAutumnWinter_AndBackToSpring()
    {
        const double dayLength = 600.0; // one planet day = one system day
        var seen = new List<string>();
        for (int day = 0; day < 20; day++)
        {
            double pos = PlanetSeasons.YearPosition(day + 0.5, dayLength, phase: 0.0);
            seen.Add(PlanetSeasons.SeasonAt(pos));
        }

        Assert.Equal(new[] { "spring", "summer", "autumn", "winter" }, seen.Distinct());
        Assert.All(seen.Take(5), s => Assert.Equal("spring", s)); // five planet days a season
        Assert.Equal("spring", PlanetSeasons.SeasonAt(PlanetSeasons.YearPosition(20.5, dayLength, 0.0))); // the year turns
    }

    [Fact]
    public void Summer_IsWarm_Winter_IsCold_SpringAndAutumnInBetween()
    {
        Assert.Equal(8.0, PlanetSeasons.TemperatureOffset(1.5, 8.0), 6);  // mid-summer
        Assert.Equal(-8.0, PlanetSeasons.TemperatureOffset(3.5, 8.0), 6); // mid-winter
        Assert.Equal(0.0, PlanetSeasons.TemperatureOffset(0.5, 8.0), 6);  // mid-spring
        Assert.Equal(0.0, PlanetSeasons.TemperatureOffset(2.5, 8.0), 6);  // mid-autumn
        Assert.True(PlanetSeasons.GrowthFactor("winter") < 1.0);
        Assert.True(PlanetSeasons.GrowthFactor("spring") > 1.0);
        Assert.Equal(1.0, PlanetSeasons.GrowthFactor(string.Empty));
    }

    [Fact]
    public void OnlyWorldsWithAir_HaveSeasons()
    {
        Assert.True(PlanetSeasons.HasSeasons(_content.GetPlanet("varied")));
        Assert.False(PlanetSeasons.HasSeasons(_content.GetPlanet("ship_interior"))); // a cabin
        Assert.All(_content.Planets.Values.Where(p => string.Equals(p.Atmosphere, "none", StringComparison.OrdinalIgnoreCase)),
            p => Assert.False(PlanetSeasons.HasSeasons(p)));
    }

    [Fact]
    public void TheServer_NamesTheSeason_AndItsWarmthMovesWithTheYear()
    {
        var server = NewServer("season_env", new RecordingTransport(), startPlanet: "varied");
        server.AddLocalPlayer("Walker");
        var warmths = new List<double>();
        var seasons = new HashSet<string>();
        for (int i = 0; i < 8; i++)
        {
            server.SetSystemTimeDaysForTest(i * 2.6); // a varied planet day is longer than a system day
            var s = server.ActiveSeasonForTest;
            seasons.Add(s.Season);
            warmths.Add(s.Warmth);
        }

        Assert.DoesNotContain(string.Empty, seasons);
        Assert.True(seasons.Count >= 2, "the season changes over the year");
        Assert.True(warmths.Max() - warmths.Min() > 4.0, "summer and winter differ in warmth");
    }

    [Fact]
    public void TheSystemClock_IsSaved_SoARestartKeepsTheSeason()
    {
        var server = NewServer("season_clock", new RecordingTransport(), startPlanet: "varied");
        server.SetSystemTimeDaysForTest(123.25);
        server.Tick(0.05);
        server.SaveAllForTest();

        var meta = _repos[0].LoadMetadata();
        Assert.NotNull(meta);
        Assert.True(meta!.SystemTimeDays >= 123.25, "the clock went into the save");
    }
}
