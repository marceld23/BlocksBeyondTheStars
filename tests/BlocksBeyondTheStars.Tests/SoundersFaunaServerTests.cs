// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Sounders;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>A live jungle world, a stone pad over its highest block and a player on foot — the stage of the Soaniri tests.</summary>
internal static class SoundersStage
{
    public static SvGameServer Started(string root, string name, GameContent content, out SqliteWorldRepository repo,
        Action<ServerConfig>? tune = null)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(root, name));
        var config = new ServerConfig
        {
            WorldName = name,
            Seed = 4242,
            StartPlanet = "jungle",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
        };
        tune?.Invoke(config);
        var server = new SvGameServer(config, content, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        return server;
    }

    /// <summary>A flat stone pad of half-width <paramref name="r"/>, ten cells over the highest block around (crowns included),
    /// so nothing natural stands over it or lights it. Returns the pad's Y (feet stand at Y + 1).</summary>
    public static int Pad(SvGameServer server, GameContent content, int cx, int cz, int r)
    {
        int top = int.MinValue;
        for (int dx = -r - 4; dx <= r + 4; dx += 2)
        {
            for (int dz = -r - 4; dz <= r + 4; dz += 2)
            {
                for (int y = 220; y > -200; y--)
                {
                    if (!server.World.GetBlock(new Vector3i(cx + dx, y, cz + dz)).IsAir)
                    {
                        top = Math.Max(top, y);
                        break;
                    }
                }
            }
        }

        int padY = top + 10;
        var stone = content.GetBlock("stone")!.NumericId;
        for (int dx = -r; dx <= r; dx++)
        {
            for (int dz = -r; dz <= r; dz++)
            {
                server.World.SetBlock(new Vector3i(cx + dx, padY, cz + dz), stone);
            }
        }

        return padY;
    }

    /// <summary>A stone roof at <paramref name="y"/> over x ∈ [x0, x1], z ∈ [z0, z1].</summary>
    public static void Roof(SvGameServer server, GameContent content, int x0, int x1, int z0, int z1, int y)
    {
        var stone = content.GetBlock("stone")!.NumericId;
        for (int x = x0; x <= x1; x++)
        {
            for (int z = z0; z <= z1; z++)
            {
                server.World.SetBlock(new Vector3i(x, y, z), stone);
            }
        }
    }

    /// <summary>Every rolled species of the world turns passive, so no wild biter wanders into a measurement.</summary>
    public static void Pacify(SvGameServer server)
    {
        foreach (var sp in server.SpeciesRoster.Where(s => !SoundersFauna.IsSoundersPlan(s.BodyPlan)))
        {
            sp.Temperament = Shared.Definitions.CreatureTemperament.Passive;
            sp.AttackDamage = 0f;
        }
    }

    public static PlayerSession OnFoot(SvGameServer server, string name, Vector3f at)
    {
        var p = server.AddLocalPlayer(name);
        p.State.AboardShip = false;
        p.State.Position = at;
        return p;
    }

    public static void Delete(string root)
    {
        try
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
        catch
        {
            // best effort
        }
    }
}

/// <summary>
/// The creatures of Soaniri on a live server (#3359) and the Sounders as NPCs (#3355): the spawner's caps (one Soundrak, a few
/// Qualis), the Quali nobody can touch, the Soundrak's hostility from the start (#3424: a warning scream, then the attack on a
/// nearby player on a fresh world; a player who backs away is let go; Peaceful only hears it) and its calm, an authored species
/// joining a roster for a story beat, the scan's words, and a Sounder NPC whose look rides on the NPC wire.
/// </summary>
public sealed class SoundersFaunaServerTests : IDisposable
{
    private const int Cx = 300, Cz = 300;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_sounders_" + Guid.NewGuid().ToString("N"));
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    public void Dispose() => SoundersStage.Delete(_root);

    [Fact]
    public void AnAuthoredSpecies_JoinsTheRosterForAStoryBeat_AndSpawns()
    {
        var server = SoundersStage.Started(_root, "join", _content, out var repo);
        using (repo)
        {
            Assert.DoesNotContain(server.SpeciesRoster, s => s.Id == SoundersFauna.SpeciesIdOf(SoundersIds.Soundi)); // the jungle lists none
            int before = server.SpeciesRoster.Count;
            var sp = server.EnsureAuthoredSpeciesForTest(SoundersIds.Soundi)!;
            Assert.Same(sp, server.EnsureAuthoredSpeciesForTest(SoundersIds.Soundi)); // once
            Assert.Equal(before + 1, server.SpeciesRoster.Count);
            Assert.Null(server.EnsureAuthoredSpeciesForTest("no_such_creature"));

            string? id = server.SpawnAuthoredCreatureForTest(SoundersIds.Soundi, new Vector3f(Cx + 0.5f, 120f, Cz + 0.5f));
            var net = server.NetCreatureForTest(id!);
            Assert.Equal("Soundi", net.BodyPlan);
            Assert.Equal(0, net.Eyes);
            Assert.Equal(SoundiRules.Fur, net.ColorRgb);
            Assert.Equal(SoundiRules.Mint, net.BellyRgb);
            Assert.Equal("Soundi", net.Name);
        }
    }

    [Fact]
    public void TheSpawner_KeepsOneSoundrak_AndAFewQualis_AboutAtATime()
    {
        var server = SoundersStage.Started(_root, "caps", _content, out var repo);
        using (repo)
        {
            var rak = server.EnsureAuthoredSpeciesForTest(SoundersIds.Soundrak)!;
            var quali = server.EnsureAuthoredSpeciesForTest(SoundersIds.Quali)!;
            var soundi = server.EnsureAuthoredSpeciesForTest(SoundersIds.Soundi)!;
            var at = new Vector3f(Cx + 0.5f, 140f, Cz + 0.5f);

            Assert.False(server.SoundersCapReachedForTest(rak.Id));
            server.SpawnCreatureAtForTest(at, rak.Id);
            Assert.True(server.SoundersCapReachedForTest(rak.Id));

            for (int i = 0; i < QualiRules.MaxWildAtOnce; i++)
            {
                Assert.False(server.SoundersCapReachedForTest(quali.Id));
                server.SpawnCreatureAtForTest(at, quali.Id);
            }

            Assert.True(server.SoundersCapReachedForTest(quali.Id));
            for (int i = 0; i < 8; i++)
            {
                server.SpawnCreatureAtForTest(at, soundi.Id);
            }

            Assert.False(server.SoundersCapReachedForTest(soundi.Id)); // a Soundi pack has no cap of its own
        }
    }

    [Fact]
    public void AQuali_CannotBeHit()
    {
        var server = SoundersStage.Started(_root, "quali", _content, out var repo);
        using (repo)
        {
            var sp = server.EnsureAuthoredSpeciesForTest(SoundersIds.Quali)!;
            int padY = SoundersStage.Pad(server, _content, Cx, Cz, 6);
            var p = SoundersStage.OnFoot(server, "Kid", new Vector3f(Cx + 0.5f, padY + 1, Cz + 0.5f));
            string id = server.SpawnCreatureAtForTest(new Vector3f(Cx + 1.5f, padY + 1, Cz + 0.5f), sp.Id);
            float hull = server.Creatures.First(c => c.Id == id).Hull;

            for (int i = 0; i < 5; i++)
            {
                server.AttackEntity(p.State.PlayerId, id, 1f, 0f, 0f);
                server.TickForTest(0.6);
            }

            Assert.Equal(hull, server.Creatures.First(c => c.Id == id).Hull);
            Assert.False(server.NetCreatureForTest(id).Hostile); // never hostile
        }
    }

    /// <summary>A Soundrak on a fresh jungle pad (no story progress anywhere) and a player on foot <paramref name="away"/> blocks
    /// east of it; every rolled species is pacified so only the Soundrak can bite.</summary>
    private (SvGameServer Server, SqliteWorldRepository Repo, PlayerSession Player, CombatEntity Rak, int PadY) RakStage(
        string name, float away, Action<ServerConfig>? tune = null)
    {
        var server = SoundersStage.Started(_root, name, _content, out var repo, tune);
        SoundersStage.Pacify(server);
        var sp = server.EnsureAuthoredSpeciesForTest(SoundersIds.Soundrak)!;
        int padY = SoundersStage.Pad(server, _content, Cx, Cz, 22);
        var p = SoundersStage.OnFoot(server, "Kid", new Vector3f(Cx + 0.5f + away, padY + 1, Cz + 0.5f));
        string id = server.SpawnCreatureAtForTest(new Vector3f(Cx + 0.5f, padY + 1, Cz + 0.5f), sp.Id);
        return (server, repo, p, server.Creatures.First(c => c.Id == id), padY);
    }

    /// <summary>Ticks <paramref name="seconds"/> in 0.1 s steps, the player held <paramref name="away"/> blocks east of the
    /// Soundrak.</summary>
    private static void Hold(SvGameServer server, PlayerSession p, CombatEntity rak, int padY, float away, double seconds)
    {
        for (int i = 0; i < (int)Math.Round(seconds * 10.0); i++)
        {
            p.State.Position = new Vector3f(rak.Position.X + away, padY + 1, rak.Position.Z);
            server.TickForTest(0.1);
        }
    }

    [Fact]
    public void ASoundrak_OnAFreshWorld_WarnsWithAScream_ThenAttacksANearbyPlayer()
    {
        var (server, repo, p, rak, padY) = RakStage("rak_fresh", 5f); // within its reach, no story step taken
        using (repo)
        {
            float health = p.State.Health;
            Hold(server, p, rak, padY, 5f, 0.5);
            Assert.True(rak.ScreamedAt > double.NegativeInfinity, "it noticed the player and screamed its warning");
            Assert.False(server.NetCreatureForTest(rak.Id).Hostile, "the scream comes before the first attack");

            Hold(server, p, rak, padY, 5f, SoundrakRules.WarningSeconds - 1.0);
            Assert.False(server.NetCreatureForTest(rak.Id).Hostile, "the warning gives time to back away");
            Assert.Equal(health, p.State.Health);

            Hold(server, p, rak, padY, 5f, 1.5);
            Assert.True(server.NetCreatureForTest(rak.Id).Hostile, "warned and still there: it attacks");
            Hold(server, p, rak, padY, 4f, 3.0);
            Assert.True(p.State.Health < health, "the Soundrak bites");
            Assert.True(p.State.Health > health - 20f, "a fair bite, no instant kill");
        }
    }

    [Fact]
    public void ASoundrak_LetsAPlayerWhoBacksAway_Go()
    {
        var (server, repo, p, rak, padY) = RakStage("rak_back", 8f);
        using (repo)
        {
            float health = p.State.Health;
            Hold(server, p, rak, padY, 8f, 0.5);
            Assert.True(rak.ScreamedAt > double.NegativeInfinity, "the warning");
            Hold(server, p, rak, padY, SoundrakRules.NoticeRange + 3f, 6.0); // heeded: out of its notice, still in its patch
            Assert.False(server.NetCreatureForTest(rak.Id).Hostile, "a player who backs away is not attacked");
            Assert.Equal(health, p.State.Health);
        }
    }

    [Fact]
    public void ASoundrak_InPeaceful_OnlyScreams()
    {
        var (server, repo, p, rak, padY) = RakStage("rak_peace", 5f, c => c.Rules.PlanetEnemies = AlienActivity.Off);
        using (repo)
        {
            float health = p.State.Health;
            Hold(server, p, rak, padY, 5f, 6.0);
            Assert.True(rak.ScreamedAt > double.NegativeInfinity, "it still screams");
            Assert.False(server.NetCreatureForTest(rak.Id).Hostile, "but never attacks while planet enemies are off");
            Assert.Equal(health, p.State.Health);
        }
    }

    [Fact]
    public void TheSoundrak_ScreamsAtTheEdgeOfItsPatch_AttacksWhoComesClose_AndACalmHoldsIt()
    {
        var (server, repo, p, rak, padY) = RakStage("rak", 13.5f);
        using (repo)
        {
            string id = rak.Id;
            Hold(server, p, rak, padY, 13.5f, SoundrakRules.WarningSeconds + 1.0); // in its patch, beyond its notice
            Assert.True(rak.ScreamReadyAt > 0, "a visitor in its territory hears the scream");
            Assert.False(server.NetCreatureForTest(id).Hostile, "out at the edge of its patch the scream is all");

            Hold(server, p, rak, padY, 4f, 0.5); // that scream was the warning: whoever comes close now is attacked
            Assert.True(server.NetCreatureForTest(id).Hostile, "whoever comes close after the warning makes it charge");

            Assert.True(server.CalmCreaturesNearForTest(rak.Position, 12f, 60.0) >= 1);
            Assert.True(server.IsCalmedForTest(id));
            float health = p.State.Health;
            double screamAt = rak.ScreamReadyAt;
            for (int i = 0; i < 40; i++)
            {
                p.State.Position = new Vector3f(rak.Position.X + 3f, padY + 1, rak.Position.Z);
                server.TickForTest(0.1);
            }

            Assert.False(server.NetCreatureForTest(id).Hostile, "a calm Soundrak holds no grudge");
            Assert.True(p.State.Health >= health, "and bites nobody");
            Assert.Equal(screamAt, rak.ScreamReadyAt); // nor screams
        }
    }

    [Fact]
    public void TheScan_SaysWhatACreatureOfSoaniriIsAbout()
    {
        var server = SoundersStage.Started(_root, "scan", _content, out var repo);
        using (repo)
        {
            server.AddLocalPlayer("Kid");
            (string Key, string Body, string Voice)[] cases =
            {
                (SoundersIds.Soundi, "ui.scan.body.soundi", "ui.scan.voice.sings"),
                (SoundersIds.Soundrak, "ui.scan.body.soundrak", "ui.scan.voice.scream"),
                (SoundersIds.Quali, "ui.scan.body.quali", "ui.scan.voice.song"),
                (SoundersIds.Soundeling, "ui.scan.behaviour.carries_light", "ui.scan.voice.coo"),
            };
            foreach (var (key, body, voice) in cases)
            {
                var sp = server.EnsureAuthoredSpeciesForTest(key)!;
                var result = server.ScanSubject("Kid", "creature", sp.Id);
                Assert.Contains(body, result.TraitKeys);
                Assert.Contains(voice, result.TraitKeys);
            }
        }
    }

    [Fact]
    public void ASounderNpc_CarriesItsLookOnTheWire_AndCanLeaveAgain()
    {
        var server = SoundersStage.Started(_root, "npc", _content, out var repo);
        using (repo)
        {
            int padY = SoundersStage.Pad(server, _content, Cx, Cz, 8);
            SoundersStage.OnFoot(server, "Kid", new Vector3f(Cx + 4.5f, padY + 1, Cz + 0.5f));
            int echo = server.SpawnSounderNpcForTest(SoundersIds.Echo, new Vector3f(Cx + 0.5f, padY + 1, Cz + 0.5f), "Elora", "elora");
            int roll = server.SpawnSounderNpcForTest(SoundersIds.Sounder, new Vector3f(Cx - 2.5f, padY + 1, Cz + 0.5f), "Roll", "roll");
            for (int i = 0; i < 10; i++)
            {
                server.TickForTest(0.1); // they stroll and turn like any NPC
            }

            var e = server.NetNpcForTest(echo);
            Assert.Equal(SoundersIds.Echo, e.Look);
            Assert.Equal("Elora", e.Name);
            Assert.Equal("npc.role.sounder_echo", e.NameKey);
            Assert.Equal(SounderRules.EchoTeal, e.SkinRgb);
            Assert.InRange(e.Size, SounderRules.MinSize, SounderRules.MaxSize);

            var r = server.NetNpcForTest(roll);
            Assert.Equal(SoundersIds.Sounder, r.Look);
            Assert.Equal("npc.role.sounder", r.NameKey);
            Assert.Contains(r.SkinRgb, SounderRules.SkinTones);
            Assert.Contains(r.OutfitRgb, SounderRules.SashTones);
            Assert.False(r.IsRobot);

            int again = server.SpawnSounderNpcForTest(SoundersIds.Sounder, new Vector3f(Cx - 4.5f, padY + 1, Cz + 0.5f), "Roll", "roll");
            Assert.Equal(r.SkinRgb, server.NetNpcForTest(again).SkinRgb); // the same character, the same colours
            Assert.Equal(r.Size, server.NetNpcForTest(again).Size);

            Assert.True(server.RemoveSounderNpcForTest(echo));
            Assert.False(server.RemoveSounderNpcForTest(echo));
            Assert.DoesNotContain(server.NpcLooksForTest, n => n.Id == echo);
            Assert.Contains(server.NpcLooksForTest, n => n.Id == roll && n.Look == SoundersIds.Sounder);
        }
    }
}
