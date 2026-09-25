// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
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
/// The arachnid on a live server (#2009): an ambusher sits still until a player comes within
/// <see cref="ArachnidRules.LurkRange"/> and then rushes; the scanner reads the plan and warns of the ambush; VEGA points
/// the first one out; <c>/arachnid</c> adds the species to a roster that has none and places one near the admin.
/// </summary>
public sealed class ArachnidServerTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public ArachnidServerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_arachnid_" + Guid.NewGuid().ToString("N"));
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

    private SvGameServer Started(out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "arachnid"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = "arachnid",
            Seed = 4242,
            StartPlanet = "jungle",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
        };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    /// <summary>Forces roster slot 0 into an always-awake, solitary arachnid of the wanted temper.</summary>
    private static CreatureSpecies ForceArachnid(SvGameServer server, CreatureTemperament temperament)
    {
        var sp = server.SpeciesRoster.First();
        sp.Habitat = CreatureHabitat.Land;
        sp.BodyPlan = CreatureBodyPlan.Arachnid;
        sp.HeadShape = CreatureHeadShape.Pyramid;
        sp.Legs = 8;
        sp.BodySegments = 2;
        sp.Size = 3.3f;
        sp.Speed = 3f;
        sp.Temperament = temperament;
        sp.LocoStyle = LocomotionStyle.Prowler;
        sp.Activity = CreatureActivity.Cathemeral;
        sp.SocialGroupSize = 1;
        sp.HasWings = false;
        sp.HasGasSac = false;
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

    private static int MaxTopY(SvGameServer server, int cx, int cz, int r)
    {
        int max = int.MinValue;
        for (int dx = -r; dx <= r; dx++)
        {
            for (int dz = -r; dz <= r; dz++)
            {
                max = Math.Max(max, SurfaceTopY(server, cx + dx, cz + dz));
            }
        }

        return max;
    }

    /// <summary>A flat stone pad well above the terrain, so the body checks see open ground.</summary>
    private int BuildPad(SvGameServer server, int cx, int cz, int r)
    {
        int padY = MaxTopY(server, cx, cz, r + 4) + 8;
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

    private static float Dist(Vector3f a, Vector3f b)
    {
        float dx = a.X - b.X, dz = a.Z - b.Z;
        return (float)Math.Sqrt(dx * dx + dz * dz);
    }

    [Theory]
    [InlineData(CreatureTemperament.Aggressive)]
    [InlineData(CreatureTemperament.Territorial)]
    public void Ambusher_SitsStill_UntilAPlayerComesClose_ThenRushes(CreatureTemperament temperament)
    {
        var server = Started(out var repo);
        using (repo)
        {
            var sp = ForceArachnid(server, temperament);
            var p = server.AddLocalPlayer("Bait");
            p.State.AboardShip = false;
            const int cx = 300, cz = 300;
            int padY = BuildPad(server, cx, cz, 16);

            // The player 12 blocks away — past the lurk range, inside the aggro range a hunter would otherwise use.
            p.State.Position = new Vector3f(cx + 12.5f, padY + 1, cz + 0.5f);
            string id = server.SpawnCreatureAtForTest(new Vector3f(cx + 0.5f, padY + 1, cz + 0.5f), sp.Id);
            var start = server.Creatures.First(c => c.Id == id).Position;

            for (int i = 0; i < 40; i++)
            {
                server.TickForTest(0.1);
            }

            var still = server.Creatures.First(c => c.Id == id);
            Assert.True(Dist(still.Position, start) < 0.05f, $"an ambusher does not roam: moved {Dist(still.Position, start):0.00}");
            Assert.True(server.NetCreatureForTest(id).Lurking, "the wire carries the wait, so the client holds the crouch");
            Assert.Equal(0.0, server.ProvokeTimerForTest(id));

            // The player steps inside the lurk range: the wait ends, the creature is provoked and closes in.
            p.State.Position = new Vector3f(cx + 4.5f, padY + 1, cz + 0.5f);
            for (int i = 0; i < 30; i++)
            {
                server.TickForTest(0.1);
            }

            var rushing = server.Creatures.First(c => c.Id == id);
            Assert.False(server.NetCreatureForTest(id).Lurking);
            Assert.True(server.ProvokeTimerForTest(id) > 0, "inside the lurk range it is provoked");
            Assert.True(Dist(rushing.Position, p.State.Position) < Dist(start, p.State.Position) - 0.5f,
                $"it rushes the player: {Dist(start, p.State.Position):0.0} → {Dist(rushing.Position, p.State.Position):0.0}");
            Assert.True(p.ArachnidSighted, "VEGA pointed the arachnid out to the player who came near it");
        }
    }

    [Fact]
    public void APassiveArachnid_DoesNotLurk()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var sp = ForceArachnid(server, CreatureTemperament.Passive);
            var p = server.AddLocalPlayer("Bait");
            p.State.AboardShip = false;
            const int cx = 300, cz = 300;
            int padY = BuildPad(server, cx, cz, 16);
            p.State.Position = new Vector3f(cx + 12.5f, padY + 1, cz + 0.5f);
            string id = server.SpawnCreatureAtForTest(new Vector3f(cx + 0.5f, padY + 1, cz + 0.5f), sp.Id);

            for (int i = 0; i < 20; i++)
            {
                server.TickForTest(0.1);
            }

            Assert.False(server.NetCreatureForTest(id).Lurking);
            Assert.Equal("Arachnid", server.NetCreatureForTest(id).BodyPlan);
            Assert.Equal("Pyramid", server.NetCreatureForTest(id).HeadShape);
        }
    }

    [Fact]
    public void Scan_ReadsEightLegged_AndWarnsOfTheAmbush()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var sp = ForceArachnid(server, CreatureTemperament.Territorial);
            server.AddLocalPlayer("Justus");
            var result = server.ScanSubject("Justus", "creature", sp.Id);
            Assert.Contains("ui.scan.body.arachnid", result.TraitKeys);
            Assert.Contains("ui.scan.body.ambush", result.TraitKeys);

            sp.Temperament = CreatureTemperament.Passive;
            var calm = server.ScanSubject("Justus", "creature", sp.Id);
            Assert.Contains("ui.scan.body.arachnid", calm.TraitKeys);
            Assert.DoesNotContain("ui.scan.body.ambush", calm.TraitKeys);
        }
    }

    [Fact]
    public void SummonArachnid_AddsTheSpeciesToARosterWithoutOne_AndPlacesItNearTheAdmin()
    {
        var server = Started(out var repo);
        using (repo)
        {
            foreach (var sp in server.SpeciesRoster.Where(s => s.BodyPlan == CreatureBodyPlan.Arachnid))
            {
                sp.BodyPlan = CreatureBodyPlan.Standard; // start from a roster that has none, whatever the seed rolled
            }

            var p = server.AddLocalPlayer("Admin");
            p.State.AboardShip = false;
            const int cx = 300, cz = 300;
            int padY = BuildPad(server, cx, cz, 30);
            p.State.Position = new Vector3f(cx + 0.5f, padY + 1, cz + 0.5f);
            int before = server.SpeciesRoster.Count;

            server.SummonArachnidForTest("Admin");

            var species = Assert.Single(server.SpeciesRoster, s => s.BodyPlan == CreatureBodyPlan.Arachnid);
            Assert.Equal(before + 1, server.SpeciesRoster.Count);
            var live = Assert.Single(server.Creatures, c => c.SpeciesId == species.Id);
            float d = Dist(live.Position, p.State.Position);
            Assert.InRange(d, 10f, 24f);

            // A second summon reuses the roster's species instead of adding another.
            server.SummonArachnidForTest("Admin");
            Assert.Equal(before + 1, server.SpeciesRoster.Count);
            Assert.Equal(2, server.Creatures.Count(c => c.SpeciesId == species.Id));
        }
    }
}
