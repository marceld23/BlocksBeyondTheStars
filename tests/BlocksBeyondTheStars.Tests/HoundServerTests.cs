// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The Schuppenhund on a live server (#3326): it bites in chunks of 10 — once per bite interval, nothing in between — and not
/// at all with planet enemies off; the spawner keeps one about at a time; the scan names how hard it bites and that it barks;
/// the wire carries its ears, muzzle, teeth and claws; <c>/hound</c> adds it to a roster without one, and VEGA warns.
/// </summary>
public sealed class HoundServerTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public HoundServerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_hound_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
            // best effort
        }
    }

    private SvGameServer Started(out SqliteWorldRepository repo, bool enemiesOff = false)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "hound"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = "hound",
            Seed = 4242,
            StartPlanet = "jungle",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
        };
        if (enemiesOff)
        {
            config.Rules.PlanetEnemies = AlienActivity.Off;
        }

        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    /// <summary>Turns roster slot 0 into this world's hound (awake around the clock, so the test never meets a sleeper).</summary>
    private CreatureSpecies ForceHound(SvGameServer server)
    {
        var hound = WorldGeneration.CreatureGenerator.GenerateScaleHound(_content.GetPlanet("bone_desert")!, 77);
        foreach (var other in server.SpeciesRoster.Skip(1))
        {
            other.Temperament = CreatureTemperament.Passive; // no other biter wanders into the measurement
            other.AttackDamage = 0f;
        }

        var sp = server.SpeciesRoster.First();
        sp.Habitat = CreatureHabitat.Land;
        sp.BodyPlan = CreatureBodyPlan.Hound;
        sp.Temperament = CreatureTemperament.Aggressive;
        sp.Activity = CreatureActivity.Cathemeral;
        sp.Size = hound.Size;
        sp.Speed = hound.Speed;
        sp.Legs = 4;
        sp.BodySegments = 2;
        sp.HasWings = false;
        sp.HasGasSac = false;
        sp.SocialGroupSize = 1;
        sp.LocoStyle = LocomotionStyle.Prowler;
        sp.EarStyle = hound.EarStyle;
        sp.SnoutLength = hound.SnoutLength;
        sp.HasClaws = true;
        sp.HasTeeth = true;
        sp.BiteDamage = HoundRules.BiteDamage;
        sp.BiteInterval = 1.5f;
        sp.AttackDamage = sp.BiteDamage / sp.BiteInterval;
        return sp;
    }

    private static int SurfaceTopY(SvGameServer server, int x, int z)
    {
        for (int y = 200; y > -200; y--)
        {
            if (!server.World.GetBlock(new Vector3i(x, y, z)).IsAir)
            {
                return y;
            }
        }

        return 0;
    }

    /// <summary>A flat stone pad well above the terrain, so the body checks see open ground.</summary>
    private int BuildPad(SvGameServer server, int cx, int cz, int r)
    {
        int top = int.MinValue;
        for (int dx = -r - 4; dx <= r + 4; dx++)
        {
            for (int dz = -r - 4; dz <= r + 4; dz++)
            {
                top = Math.Max(top, SurfaceTopY(server, cx + dx, cz + dz));
            }
        }

        int padY = top + 8;
        var stone = _content.GetBlock("stone")!.NumericId;
        for (int dx = -r; dx <= r; dx++)
        {
            for (int dz = -r; dz <= r; dz++)
            {
                server.World.SetBlock(new Vector3i(cx + dx, padY, cz + dz), stone);
            }
        }

        return padY;
    }

    /// <summary>A hound beside the player on a pad; ticks the server for <paramref name="seconds"/> and returns every health
    /// drop per tick.</summary>
    private static List<float> RunNextToHound(SvGameServer server, CreatureSpecies sp, BlocksBeyondTheStars.GameServer.PlayerSession p,
        int padY, int cx, int cz, double seconds)
    {
        p.State.AboardShip = false;
        p.State.Position = new Vector3f(cx + 2.5f, padY + 1, cz + 0.5f);
        server.SpawnCreatureAtForTest(new Vector3f(cx + 0.5f, padY + 1, cz + 0.5f), sp.Id);
        var drops = new List<float>();
        for (int i = 0; i < (int)Math.Round(seconds / 0.1); i++)
        {
            float before = p.State.Health;
            p.State.Position = new Vector3f(cx + 2.5f, padY + 1, cz + 0.5f); // hold the player in reach
            server.TickForTest(0.1);
            drops.Add(before - p.State.Health);
        }

        return drops;
    }

    [Fact]
    public void AHound_BitesInChunksOfTen_OncePerInterval()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var sp = ForceHound(server);
            var p = server.AddLocalPlayer("Jafar");
            const int cx = 300, cz = 300;
            int padY = BuildPad(server, cx, cz, 12);

            var drops = RunNextToHound(server, sp, p, padY, cx, cz, 3.2);

            // Health regenerates a little every tick, so a tick with a bite reads 10 less that tick's regeneration.
            string log = string.Join(" ", drops.Select(d => d.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)));
            var bites = drops.Select((d, i) => (d, i)).Where(x => x.d >= 9.5f).ToList();
            Assert.True(bites.Count is >= 2 and <= 3, "bites at t ≈ 0, 1.5, 3.0 — drops: " + log);
            Assert.All(bites, b => Assert.InRange(b.d, 9.5f, 10.05f));
            Assert.True(drops.Where(d => d is > 0f and < 9.5f).Sum() < 0.5f, "nothing in between, no per-second drain: " + log);
            for (int k = 1; k < bites.Count; k++)
            {
                Assert.InRange(bites[k].i - bites[k - 1].i, 14, 16); // 1.5 s apart at 0.1 s ticks
            }
        }
    }

    [Fact]
    public void WithPlanetEnemiesOff_AHoundNeverBites()
    {
        var server = Started(out var repo, enemiesOff: true);
        using (repo)
        {
            var sp = ForceHound(server);
            var p = server.AddLocalPlayer("Jafar");
            const int cx = 300, cz = 300;
            int padY = BuildPad(server, cx, cz, 12);

            var drops = RunNextToHound(server, sp, p, padY, cx, cz, 3.2);

            Assert.True(drops.Sum() < 0.5f, $"the family presets switch every bite off: lost {drops.Sum():0.0}");
        }
    }

    [Fact]
    public void TheSpawner_KeepsOneHoundAboutAtATime()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var sp = ForceHound(server);
            Assert.False(server.HoundAlreadyAbout(sp));
            server.SpawnCreatureAtForTest(new Vector3f(300.5f, 120, 300.5f), sp.Id);
            Assert.True(server.HoundAlreadyAbout(sp));

            var other = server.SpeciesRoster.Skip(1).First();
            Assert.False(server.HoundAlreadyAbout(other)); // only the hound lives alone this way
        }
    }

    [Fact]
    public void TheScan_SaysHowHardItBites_AndThatItBarks_AndTheWireCarriesItsLooks()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var sp = ForceHound(server);
            server.AddLocalPlayer("Jafar");
            var result = server.ScanSubject("Jafar", "creature", sp.Id);
            Assert.Contains("ui.scan.body.hound", result.TraitKeys);
            Assert.Contains("ui.scan.voice.bark", result.TraitKeys);

            string id = server.SpawnCreatureAtForTest(new Vector3f(300.5f, 120, 300.5f), sp.Id);
            var net = server.NetCreatureForTest(id);
            Assert.Equal("Hound", net.BodyPlan);
            Assert.Equal(sp.EarStyle.ToString(), net.EarStyle);
            Assert.NotEqual("None", net.EarStyle);
            Assert.Equal(sp.SnoutLength, net.SnoutLength);
            Assert.True(net.HasClaws);
            Assert.True(net.HasTeeth);
        }
    }

    [Fact]
    public void SummonHound_AddsTheSpeciesToARosterWithoutOne_PlacesItNearTheAdmin_AndVegaWarns()
    {
        var server = Started(out var repo);
        using (repo)
        {
            Assert.DoesNotContain(server.SpeciesRoster, HoundRules.IsHound); // the jungle hosts none
            var p = server.AddLocalPlayer("Admin");
            p.State.AboardShip = false;
            var (cx, cz) = DryGround.Find(server, 26);
            int padY = BuildPad(server, cx, cz, 30);
            p.State.Position = new Vector3f(cx + 0.5f, padY + 1, cz + 0.5f);
            int before = server.SpeciesRoster.Count;

            server.SummonHoundForTest("Admin");

            var species = Assert.Single(server.SpeciesRoster, HoundRules.IsHound);
            Assert.Equal(HoundRules.SpeciesName, species.Name);
            Assert.Equal(before + 1, server.SpeciesRoster.Count);
            var live = Assert.Single(server.Creatures, c => c.SpeciesId == species.Id);
            float dx = live.Position.X - p.State.Position.X, dz = live.Position.Z - p.State.Position.Z;
            Assert.InRange((float)Math.Sqrt(dx * dx + dz * dz), 12f, 24f);

            // The summon stands it on the generator's ground, which may lie under the test pad: step next to it to be seen.
            Assert.False(p.Vega.HoundSighted);
            p.State.Position = new Vector3f(live.Position.X + 8f, live.Position.Y, live.Position.Z);
            for (int i = 0; i < 20 && !p.Vega.HoundSighted; i++)
            {
                server.TickForTest(0.1);
            }

            var now = server.Creatures.First(c => c.SpeciesId == species.Id).Position;
            Assert.True(p.Vega.HoundSighted, $"VEGA warns the first time a hound comes near (hound at {now}, player at {p.State.Position})");
        }
    }
}
