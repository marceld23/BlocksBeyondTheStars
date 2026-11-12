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
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Sounders;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// A started Soaniri (#3358), shared by the read-only checks of <see cref="SoaniriSitesTests"/>: the three sites are stamped
/// once on the start world.
/// </summary>
public sealed class SoaniriWorldFixture : IDisposable
{
    public SoaniriWorldFixture()
    {
        Root = Path.Combine(Path.GetTempPath(), "bbts_soaniri_fx_" + Guid.NewGuid().ToString("N"));
        Repo = new SqliteWorldRepository(new SaveGamePaths(Root, "soaniri"));
        Server = SoaniriSitesTests.StartOn(Repo, "soaniri", 3358, new RecordingTransport());
    }

    public string Root { get; }

    public SqliteWorldRepository Repo { get; }

    public SvGameServer Server { get; }

    public void Dispose()
    {
        Repo.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // a SQLite handle may linger on Windows; the temp folder is cleaned by the OS
        }
    }
}

/// <summary>
/// The server side of Soaniri (#3358) and its singing plants (#3367): the village Mirun with its Sounder villagers and the
/// Eldest, the ruin city Ornasa with three sealed sound chambers and the bell-bloom trail, the Choir Tower with its eight pipes
/// as the melody site and pack site <c>choir_tower</c> — stamped once, re-derived from the records, deterministic — and the
/// plants as puzzle helpers: the echo flower repeats, a phrase of the Song of Return opens bell blooms, the trail opens its
/// chamber and solves <c>bloom_trail</c>.
/// </summary>
public sealed class SoaniriSitesTests : IClassFixture<SoaniriWorldFixture>, IDisposable
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    private readonly SoaniriWorldFixture _fx;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_soaniri_" + Guid.NewGuid().ToString("N"));
    private readonly List<SqliteWorldRepository> _repos = new();

    public SoaniriSitesTests(SoaniriWorldFixture fx) => _fx = fx;

    public void Dispose()
    {
        foreach (var repo in _repos)
        {
            repo.Dispose();
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // best-effort temp cleanup (also: nothing was written)
        }
    }

    internal static SvGameServer StartOn(SqliteWorldRepository repo, string world, long seed, RecordingTransport transport)
    {
        var config = new ServerConfig
        {
            WorldName = world,
            Seed = seed,
            StartPlanet = SoundersIds.PlanetType, // the server makes the galaxy's first planet a Soaniri — a test world
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            World = { TerrainGeneration = WorldDescription.CurrentTerrainGeneration },
        };
        var server = new SvGameServer(config, Content, transport, repo);
        server.Start();
        Assert.Equal(SoundersIds.PlanetType, server.World.Planet.Key);
        return server;
    }

    private SvGameServer Started(string world, long seed, out RecordingTransport transport)
    {
        transport = new RecordingTransport();
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        _repos.Add(repo);
        return StartOn(repo, world, seed, transport);
    }

    private static string KeyAt(SvGameServer server, Vector3i cell) => Content.BlockById(server.World.GetBlock(cell))?.Key ?? "air";

    private static (string Key, int X, int FloorY, int Z) Site(SvGameServer server, string key)
        => server.SounderSitesForTest().Single(s => s.Key == key);

    private static PlayerSession OnFoot(SvGameServer server, string name, Vector3i near)
    {
        var s = server.AddLocalPlayer(name);
        s.State.AboardShip = false;
        s.State.Position = new Vector3f(near.X + 0.5f, near.Y + 0.2f, near.Z + 0.5f);
        return s;
    }

    private static void Play(SvGameServer server, PlayerSession who, string word)
    {
        foreach (int tone in SounderAlphabet.TonesOnly(SounderAlphabet.Encode(word)))
        {
            server.HearSingingPlants(who, tone, 0);
        }
    }

    // ---------------- the sites (read-only, shared world) ----------------

    [Fact]
    public void ASounderWorld_HasMirunOrnasaAndTheChoirTower_ApartAndClearOfThePads()
    {
        var server = _fx.Server;
        var sites = server.SounderSitesForTest();
        Assert.Equal(new[] { SvGameServer.MirunSiteKey, SvGameServer.OrnasaSiteKey, SvGameServer.ChoirTowerSiteKey }, sites.Select(s => s.Key));
        int circ = server.World.Circumference;
        double Dist(int ax, int az, int bx, int bz)
            => Math.Sqrt(Math.Pow(WorldConstants.WrapDeltaX(ax - bx, circ), 2) + Math.Pow(WorldConstants.WrapDeltaZ(az - bz, circ), 2));
        for (int i = 0; i < sites.Count; i++)
        {
            var spec = SvGameServer.SounderSites[i];
            var pad = server.LandingPadForTest(0);
            double fromPad = Dist(sites[i].X, sites[i].Z, pad.X, pad.Z);
            Assert.InRange(fromPad, spec.MinDistance - 1, (spec.MaxDistance * 3) + 1); // the second round reaches three times as far
            for (int j = 0; j < i; j++)
            {
                Assert.True(Dist(sites[i].X, sites[i].Z, sites[j].X, sites[j].Z) >= spec.Radius + SvGameServer.SounderSites[j].Radius + 10);
            }
        }

        Assert.Equal(new[] { "mirun", "ornasa", "choir_tower" }, server.StampReportForTest.Where(r => r.Kind.StartsWith("sounder:", StringComparison.Ordinal))
            .Select(r => r.Kind.Substring(8)));
        Assert.All(server.StampReportForTest.Where(r => r.Kind.StartsWith("sounder:", StringComparison.Ordinal)), r => Assert.Equal(1, r.Placed));
    }

    [Fact]
    public void TheChoirTower_CarriesEightPipes_ThatAreTheMelodySite_AndThePackSite()
    {
        var server = _fx.Server;
        var tower = Site(server, SvGameServer.ChoirTowerSiteKey);
        var pipes = SvGameServer.ChoirPipes(tower.X, tower.FloorY, tower.Z);
        var site = server.FindMelodySite(server.World.LocationId, SvGameServer.ChoirTowerSiteKey);
        Assert.NotNull(site);
        int circ = server.World.Circumference;
        for (int i = 0; i < 8; i++)
        {
            Assert.Equal(WorldConstants.CanonicalBlock(pipes[i], circ), site!.Pillars[i]);
            Assert.Equal(i, MelodySiteGeometry.Sector(pipes[i].X - tower.X, pipes[i].Z - tower.Z)); // clockwise from north
            int stem = SounderSiteLayout.ChoirPipeStem(i);
            Assert.InRange(stem, 0, 3); // the crystal is within a pillar's reach of its base
            Assert.Equal("resonance_crystal", KeyAt(server, new Vector3i(pipes[i].X, pipes[i].Y + stem, pipes[i].Z)));
            Assert.NotNull(server.MelodySiteAt(new Vector3i(pipes[i].X, pipes[i].Y + stem, pipes[i].Z), out int pillar));
            Assert.Equal(i, pillar); // the crystal on top strikes its pipe's tone
        }

        var player = OnFoot(server, "Climber", new Vector3i(tower.X, SvGameServer.ChoirPlatformY(tower.FloorY) + 1, tower.Z + 3));
        var marker = server.PackRevealTargetForTest(player, "pack_site:" + SvGameServer.ChoirTowerSiteKey);
        Assert.NotNull(marker);
        Assert.Equal(SvGameServer.ChoirPlatformY(tower.FloorY) + 2f, marker!.Value.Y);
        Assert.NotNull(server.PackRevealTargetForTest(player, "pack_site:" + SvGameServer.MirunSiteKey));
        Assert.NotNull(server.PackRevealTargetForTest(player, "pack_site:" + SvGameServer.OrnasaSiteKey));

        // The tower: a doorway in the south, the central column all the way up, the barren ring around it.
        Assert.Equal("air", KeyAt(server, new Vector3i(tower.X, tower.FloorY + 1, tower.Z - 4)));
        Assert.Equal("carved_obsidian", KeyAt(server, new Vector3i(tower.X + 4, tower.FloorY + 3, tower.Z)));
        Assert.Equal("carved_obsidian", KeyAt(server, new Vector3i(tower.X, tower.FloorY + SounderSiteLayout.ChoirTowerHeight, tower.Z)));
        int ringGround = server.World.Generator.SurfaceHeight(server.World.Planet, tower.X + 15, tower.Z);
        Assert.NotEqual("chime_moss", KeyAt(server, new Vector3i(tower.X + 15, ringGround, tower.Z)));
    }

    [Fact]
    public void Mirun_IsHomeToSounders_AndTheEldest()
    {
        var villagers = _fx.Server.MirunVillagersForTest();
        Assert.Equal(7, villagers.Length);
        Assert.All(villagers, v => Assert.Equal(SoundersIds.Sounder, v.Look));
        Assert.Single(villagers, v => v.CharacterId == SvGameServer.EldestCharacterId);
        Assert.All(villagers, v => Assert.False(string.IsNullOrWhiteSpace(v.Name)));
    }

    [Fact]
    public void MirunsVillagers_WearTheSounderSkinAndSash_AndTheEldestIsTheTallest()
    {
        // #3449: they were dressed as settlers — a human skin, a settler outfit as the sash, a human size.
        var villagers = _fx.Server.MirunVillagersForTest();
        Assert.All(villagers, v => Assert.Contains(v.Skin, SounderRules.SkinTones));
        Assert.All(villagers, v => Assert.Contains(v.Sash, SounderRules.SashTones));
        Assert.All(villagers, v => Assert.InRange(v.Size, SounderRules.MinSize, SounderRules.MaxSize));
        var eldest = villagers.Single(v => v.CharacterId == SvGameServer.EldestCharacterId);
        Assert.Equal(SounderRules.MaxSize, eldest.Size);
        Assert.All(villagers.Where(v => v.CharacterId != SvGameServer.EldestCharacterId), v => Assert.True(v.Size < eldest.Size, v.Name));
    }

    [Fact]
    public void Ornasa_HasThreeSealedChambers_AndAClosedBloomTrailOnMoss()
    {
        var server = _fx.Server;
        var chambers = server.OrnasaChambers;
        Assert.Equal(3, chambers.Count);
        foreach (var chamber in chambers)
        {
            Assert.All(chamber.Seal, c => Assert.Equal("carved_obsidian", KeyAt(server, c)));
            Assert.Equal("air", KeyAt(server, chamber.Inside));
            Assert.Same(chamber, server.OrnasaChamberAtSeal(chamber.Seal[0]));
        }

        var trail = server.BloomTrailForTest;
        Assert.Equal(SounderSiteLayout.BloomTrailLength, trail.Count);
        foreach (var cell in trail)
        {
            Assert.Equal(FloraCatalog.BellBloomKey, KeyAt(server, cell));
            Assert.Equal("chime_moss", KeyAt(server, new Vector3i(cell.X, cell.Y - 1, cell.Z)));
        }
    }

    [Fact]
    public void Soaniri_HasManyRuneMonuments_ClearOfItsSites()
    {
        var server = _fx.Server;
        var monuments = server.MonumentsForTest();
        Assert.InRange(monuments.Count, 4, 6);
        int circ = server.World.Circumference;
        foreach (var site in server.SounderSitesForTest())
        {
            int radius = SvGameServer.SounderSites.Single(s => s.Key == site.Key).Radius;
            foreach (var m in monuments)
            {
                double d = Math.Sqrt(Math.Pow(WorldConstants.WrapDeltaX((int)m.Center.X - site.X, circ), 2) + Math.Pow(m.Center.Z - site.Z, 2));
                Assert.True(d > radius, $"monument {m.Archetype} inside {site.Key}");
            }
        }
    }

    // ---------------- stamped once, deterministic ----------------

    [Fact]
    public void TheSites_AreStampedOnce_ReloadFromTheirRecords_AndAreDeterministic()
    {
        string root = Path.Combine(_root, "reload");
        IReadOnlyList<(string Key, int X, int FloorY, int Z)> first;
        Vector3i pipeCrystal;
        using (var repo = new SqliteWorldRepository(new SaveGamePaths(root, "soaniri_reload")))
        {
            var server = StartOn(repo, "soaniri_reload", 3358, new RecordingTransport());
            first = server.SounderSitesForTest();
            var tower = first.Single(s => s.Key == SvGameServer.ChoirTowerSiteKey);
            var pipe = SvGameServer.ChoirPipes(tower.X, tower.FloorY, tower.Z)[7];
            pipeCrystal = new Vector3i(pipe.X, pipe.Y + SounderSiteLayout.ChoirPipeStem(7), pipe.Z);
            server.World.SetBlock(pipeCrystal, Content.GetBlock("rune_stone")!.NumericId); // a player swapped a pipe's crystal
            server.Stop();
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        using (var repo = new SqliteWorldRepository(new SaveGamePaths(root, "soaniri_reload")))
        {
            var server = StartOn(repo, "soaniri_reload", 3358, new RecordingTransport());
            Assert.Equal(first, server.SounderSitesForTest());
            Assert.Equal("rune_stone", KeyAt(server, pipeCrystal)); // written once: the reload did not stamp over the edit
            Assert.DoesNotContain(server.StampReportForTest, r => r.Kind.StartsWith("sounder:", StringComparison.Ordinal));
            Assert.NotNull(server.FindMelodySite(server.World.LocationId, SvGameServer.ChoirTowerSiteKey)); // re-registered
            Assert.Equal(7, server.MirunVillagersForTest().Length); // respawned
            server.Stop();
        }

        Assert.Equal(first, _fx.Server.SounderSitesForTest()); // another save of the same seed: the same places
    }

    [Fact]
    public void AnotherWorld_StampsNoSounderSite()
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "varied"));
        _repos.Add(repo);
        var config = new ServerConfig { WorldName = "varied", Seed = 3358, StartPlanet = "varied", AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        var server = new SvGameServer(config, Content, new RecordingTransport(), repo);
        server.Start();
        Assert.Empty(server.SounderSitesForTest());
        Assert.Empty(server.OrnasaChambers);
        Assert.Empty(server.MirunVillagersForTest());
    }

    [Fact]
    public void TheSounderSiteKind_SwitchedOffInTheDataFile_StampsNoSite()
    {
        // #3517: structure_types.json's sounder_site.enabled is read like every kind's switch.
        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        content.SetStructureTypes(StructureTypeRules.Parse("""{ "sounder_site": { "enabled": false } }"""));
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "snd_off"));
        _repos.Add(repo);
        var config = new ServerConfig
        {
            WorldName = "snd_off",
            Seed = 3358,
            StartPlanet = SoundersIds.PlanetType,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            World = { TerrainGeneration = WorldDescription.CurrentTerrainGeneration },
        };
        var server = new SvGameServer(config, content, new RecordingTransport(), repo);
        server.Start();
        Assert.Equal(SoundersIds.PlanetType, server.World.Planet.Key);
        Assert.Empty(server.SounderSitesForTest());
        Assert.Empty(server.MirunVillagersForTest());
    }

    /// <summary>#3393: "/sounders tower" moves the admin through the snap channel — a bare state update is dropped by the
    /// client, so the command did nothing in the game.</summary>
    [Fact]
    public void TheTowerAdminCommand_SnapsTheAdminToTheChoirTower()
    {
        var server = Started("snd_tower_snap", 3358, out var transport);
        var tower = Site(server, "choir_tower");
        var admin = OnFoot(server, "Ada", new Vector3i(tower.X + 30, tower.FloorY, tower.Z));
        transport.Sent.Clear();

        Assert.Equal("On the Choir Tower.", server.SoundersAdminForTest(admin, "tower"));

        var snap = transport.To<RespawnNotice>(admin).Last();
        Assert.Equal((admin.State.Position.X, admin.State.Position.Y, admin.State.Position.Z), (snap.X, snap.Y, snap.Z));
        float dx = snap.X - tower.X, dz = snap.Z - tower.Z;
        Assert.True(dx * dx + dz * dz <= 6f * 6f, $"at the tower ({tower.X}, {tower.Z}), not at ({snap.X}, {snap.Z})");
    }

    /// <summary>#3448: "/biped" and "/arachnid" roll a species into the roster at runtime when the world has none — on Soaniri
    /// it wears the planet's fauna palette like every rolled species of the roster, not a colour from any hue.</summary>
    [Fact]
    public void AdminSummonedSpecies_WearTheFaunaPalette()
    {
        var server = Started("snd_admin_fauna", 3448, out _);
        foreach (var sp in server.SpeciesRoster.Where(s => s.BodyPlan is CreatureBodyPlan.Biped or CreatureBodyPlan.Arachnid))
        {
            sp.BodyPlan = CreatureBodyPlan.Standard; // a roster without either, whatever the seed rolled: the summons roll new ones
        }

        var admin = OnFoot(server, "Ada", new Vector3i(0, 120, 0));
        int before = server.SpeciesRoster.Count;
        server.SummonBipedForTest(admin.State.PlayerId); // placing them may find no room — the species joins the roster anyway
        server.SummonArachnidForTest(admin.State.PlayerId);

        var added = server.SpeciesRoster.Skip(before).ToList();
        Assert.Equal(new[] { CreatureBodyPlan.Biped, CreatureBodyPlan.Arachnid }, added.Select(s => s.BodyPlan));
        var palette = FaunaTints.PaletteOf(Content.GetPlanet(SoundersIds.PlanetType));
        Assert.NotEmpty(palette);
        Assert.All(added, sp => Assert.Equal(FaunaTints.CoatFor(sp.VoiceSeed, palette), (sp.ColorRgb, sp.BellyRgb)));
    }
}
