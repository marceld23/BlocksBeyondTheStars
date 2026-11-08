// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3421: <c>/ressaw</c> (admin verb <c>goto_ancient</c>, cheats on) — into the nearest buried G.D.S. ruin on any planet.
/// On a body with a ruin it drops the admin inside, on dry floor beside the diving-suit crate; elsewhere it predicts the bodies
/// with a ruin from the seed alone (the boot stamp's own rule), travels to the nearest and drops the admin inside after the
/// landing. <c>/ressaw 2</c> takes the second, <c>/ressaw list</c> names them; a world older than generation 35 has none.
/// </summary>
public sealed class AncientRuinAdminTests : IDisposable
{
    private const long CitySeed = 20261107;
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_ressaw_" + Guid.NewGuid().ToString("N"));

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

    private SvGameServer Start(string planet, RecordingTransport t, out SqliteWorldRepository repo, long seed = CitySeed,
        int generation = WorldDescription.CurrentTerrainGeneration, bool cheats = true, bool settlements = true)
    {
        string name = $"ressaw_{planet}_{generation}_{seed}_{cheats}";
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        var config = new ServerConfig
        {
            WorldName = name,
            Seed = seed,
            StartPlanet = planet,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = settlements,
            Rules = cheats ? new GameRules { AdminCheats = true, AllowCheatsInSurvival = true } : new GameRules { AdminCheats = false },
        };
        config.World.TerrainGeneration = generation;
        var server = new SvGameServer(config, Content, t, repo);
        server.Start();
        return server;
    }

    private static PlayerSession Admin(SvGameServer server)
    {
        var admin = server.AddLocalPlayer("Marcel", "en");
        admin.State.Role = PlayerRole.WorldAdmin;
        admin.State.AboardShip = false;
        return admin;
    }

    /// <summary>Runs <c>/ressaw [arg]</c> and returns what the server sent the admin because of it.</summary>
    private static List<object> Ressaw(SvGameServer server, RecordingTransport t, PlayerSession admin, string? arg = null)
    {
        int before = t.Sent.Count;
        server.HandleForTest(admin, new AdminCommandIntent { Command = "goto_ancient", StringArg = arg });
        return t.Sent.Skip(before).Where(s => s.Conn == admin.ConnectionId).Select(s => s.Msg).ToList();
    }

    private static List<string> Refusals(List<object> sent)
        => sent.OfType<ActionRejected>().Where(r => r.Action == "admin").Select(r => r.Reason).ToList();

    private static List<string> Lines(List<object> sent) => sent.OfType<ServerMessage>().Select(m => m.Text).ToList();

    /// <summary>The admin stands inside the ruin's box, on solid dry floor, with air for feet and head.</summary>
    private static void AssertInside(SvGameServer server, SvGameServer.AncientRuinSite site, Vector3f pos)
    {
        int x = (int)Math.Floor(pos.X), y = (int)Math.Floor(pos.Y), z = (int)Math.Floor(pos.Z);
        int dx = WorldConstants.WrapDeltaX(x - site.Origin.X, server.World.Circumference);
        Assert.True(dx >= 0 && dx < site.Width && z >= site.Origin.Z && z < site.Origin.Z + site.Length
                    && y > site.Origin.Y && y < site.Origin.Y + site.Height,
            $"{pos} is not inside the ruin at {site.Origin} ({site.Width} × {site.Height} × {site.Length})");
        var floor = Content.BlockById(server.World.GetBlock(new Vector3i(x, y - 1, z)));
        Assert.True(floor is { Solid: true, Liquid: false }, $"no dry floor under {pos}: {floor?.Key ?? "air"}");
        Assert.True(server.World.GetBlock(new Vector3i(x, y, z)).IsAir && server.World.GetBlock(new Vector3i(x, y + 1, z)).IsAir,
            $"no room to stand at {pos}");
    }

    /// <summary><c>/ressaw n</c> to candidate n: the admin lands on its body, whose stamp decided the predicted slot — placed
    /// (the admin inside its G.D.S. ruin; true) or skipped (the answer says so, and the body is off the list; false).</summary>
    private static bool VisitCandidate(SvGameServer server, RecordingTransport t, PlayerSession admin, int n)
    {
        var c = server.AncientRuinCandidatesForTest(admin)[n - 1];
        var sent = Ressaw(server, t, admin, n.ToString(CultureInfo.InvariantCulture));

        Assert.Equal(c.BodyId, admin.CurrentLocationId);
        var record = Assert.Single(server.PlacementRecordsForTest, r => r.Kind == SvGameServer.UndergroundKind && r.Index == c.Slot);
        if (!record.Placed)
        {
            Assert.StartsWith("@srv.ancient.seat_failed:", Assert.Single(Refusals(sent)), StringComparison.Ordinal);
            Assert.DoesNotContain(server.AncientRuinCandidatesForTest(admin), x => x.BodyId == c.BodyId && x.Slot == c.Slot);
            return false;
        }

        Assert.Empty(Refusals(sent));
        var site = Assert.Single(server.AncientRuinsForTest, s => s.Slot == c.Slot);
        Assert.True(site.Gds, $"{c.BodyId}: the predicted G.D.S. ruin is a {site.Source}");
        AssertInside(server, site, admin.State.Position);
        return true;
    }

    [Fact]
    public void WithoutCheats_RessawIsRefused_AndNobodyMoves()
    {
        var t = new RecordingTransport();
        var server = Start("rocky", t, out var repo, cheats: false, settlements: false);
        using (repo)
        {
            var admin = Admin(server);
            var before = admin.State.Position;
            string body = admin.CurrentLocationId;

            var sent = Ressaw(server, t, admin);

            Assert.Equal("@srv.admin.cheats_disabled", Assert.Single(Refusals(sent)));
            Assert.Empty(sent.OfType<RespawnNotice>());
            Assert.Equal(before, admin.State.Position);
            Assert.Equal(body, admin.CurrentLocationId);
        }
    }

    [Fact]
    public void AWorldOfGeneration34_AnswersThatItHasNoRuins()
    {
        var t = new RecordingTransport();
        var server = Start("rocky", t, out var repo, generation: WorldDescription.AncientRuinsGeneration - 1, settlements: false);
        using (repo)
        {
            var admin = Admin(server);
            var before = admin.State.Position;

            Assert.Equal("@srv.ancient.too_old", Assert.Single(Refusals(Ressaw(server, t, admin))));
            Assert.Equal("@srv.ancient.too_old", Assert.Single(Refusals(Ressaw(server, t, admin, "list"))));
            Assert.Equal("@srv.ancient.too_old", Assert.Single(Refusals(Ressaw(server, t, admin, "2"))));
            Assert.Equal(before, admin.State.Position);
        }
    }

    [Fact]
    public void OnTheCityWorld_TheAdminLandsInsideTheRuin_OnDryFloorBesideTheSuitCrate()
    {
        var t = new RecordingTransport();
        var server = Start("gds_desert", t, out var repo);
        using (repo)
        {
            var admin = Admin(server);
            string body = admin.CurrentLocationId;
            var site = server.AncientRuinForTest!;
            Assert.True(site.Gds);

            var sent = Ressaw(server, t, admin);

            Assert.Empty(Refusals(sent));
            Assert.Equal(body, admin.CurrentLocationId); // no travel: the ruin is right here
            var snap = Assert.Single(sent.OfType<RespawnNotice>());
            Assert.Equal("@srv.ancient.dropped:" + server.Galaxy.FindBody(body)!.Name, snap.Reason);
            var pos = admin.State.Position;
            Assert.Equal(new Vector3f(snap.X, snap.Y, snap.Z), pos);
            AssertInside(server, site, pos);
            var crate = site.Caches[0];
            int dx = WorldConstants.WrapDeltaX((int)Math.Floor(pos.X) - crate.X, server.World.Circumference);
            Assert.True(Math.Abs(dx) <= 3 && Math.Abs((int)Math.Floor(pos.Z) - crate.Z) <= 3, $"{pos} is not beside the suit crate {crate}");
            Assert.False(admin.State.AboardShip);
        }
    }

    [Fact]
    public void RessawList_NamesTheRuinsNearestFirst_AndBadNumbersAreAnswered()
    {
        var t = new RecordingTransport();
        var server = Start("gds_desert", t, out var repo);
        using (repo)
        {
            var admin = Admin(server);
            var candidates = server.AncientRuinCandidatesForTest(admin);
            Assert.NotEmpty(candidates);
            Assert.Equal((admin.CurrentLocationId, SvGameServer.UndergroundCitySlot), (candidates[0].BodyId, candidates[0].Slot));
            AssertNearestFirst(server, admin.CurrentLocationId, candidates);

            var lines = Lines(Ressaw(server, t, admin, "list"));
            int shown = Math.Min(candidates.Count, 24);
            Assert.Equal(shown + 1 + (candidates.Count > shown ? 1 : 0), lines.Count);
            Assert.Equal($"— Buried G.D.S. ruins ({candidates.Count}) —", lines[0]);
            Assert.StartsWith($"/ressaw 1 · {server.Galaxy.FindBody(admin.CurrentLocationId)!.Name} · ", lines[1], StringComparison.Ordinal);
            Assert.EndsWith(" · under the city (glass hatch) · you are here", lines[1], StringComparison.Ordinal);
            for (int n = 2; n <= shown; n++)
            {
                var c = candidates[n - 1];
                Assert.StartsWith($"/ressaw {n} · {server.Galaxy.FindBody(c.BodyId)!.Name} · ", lines[n], StringComparison.Ordinal);
                Assert.EndsWith(c.Slot == SvGameServer.UndergroundCitySlot ? "under the city (glass hatch)" : "under a vent tower", lines[n], StringComparison.Ordinal);
            }

            Assert.Equal("@srv.ancient.usage", Assert.Single(Refusals(Ressaw(server, t, admin, "soon"))));
            Assert.Equal("@srv.ancient.usage", Assert.Single(Refusals(Ressaw(server, t, admin, "0"))));
            string range = Assert.Single(Refusals(Ressaw(server, t, admin, (candidates.Count + 1).ToString(CultureInfo.InvariantCulture))));
            Assert.Equal($"Only {candidates.Count} G.D.S. ruins are known — /ressaw 1 to /ressaw {candidates.Count}.", range);
        }
    }

    /// <summary>Same system first, then by star-map distance, then by body id (the body the admin stands on before all).</summary>
    private static void AssertNearestFirst(SvGameServer server, string here, IReadOnlyList<(string BodyId, string SystemId, int Slot)> candidates)
    {
        var home = server.Galaxy.Systems.First(s => s.Bodies.Any(b => b.Id == here));
        double Dist(string systemId)
        {
            var s = server.Galaxy.Systems.First(x => x.Id == systemId);
            double dx = s.MapX - home.MapX, dy = s.MapY - home.MapY;
            return (dx * dx) + (dy * dy);
        }

        var keys = candidates.Where(c => c.BodyId != here).Select(c => (Home: c.SystemId == home.Id ? 0 : 1, Dist: Dist(c.SystemId), c.BodyId)).ToList();
        var sorted = keys.OrderBy(k => k.Home).ThenBy(k => k.Dist).ThenBy(k => k.BodyId, StringComparer.Ordinal).ToList();
        Assert.Equal(sorted, keys);
    }

    [Fact]
    public void AcrossBodies_TheAdminLandsOnThePredictedBody_InsideItsRuin()
    {
        var t = new RecordingTransport();
        for (long seed = 1; seed <= 40; seed++)
        {
            var server = Start("jungle", t, out var repo, seed: seed);
            using (repo)
            {
                var admin = Admin(server);
                var candidates = server.AncientRuinCandidatesForTest(admin);
                if (candidates.Count == 0 || candidates[0].BodyId == admin.CurrentLocationId)
                {
                    continue;
                }

                string start = admin.CurrentLocationId;
                AssertNearestFirst(server, start, candidates);

                // /ressaw — and again while a landing finds no room (each failed body drops off the list).
                bool inside = false;
                for (int tries = 0; tries < candidates.Count && !inside; tries++)
                {
                    inside = VisitCandidate(server, t, admin, 1);
                }

                Assert.True(inside, $"seed {seed}: no predicted ruin could be entered");
                Assert.NotEqual(start, admin.CurrentLocationId);
                return;
            }
        }

        throw new Xunit.Sdk.XunitException("No galaxy with a G.D.S. ruin away from the start body in 40 seeds.");
    }

    /// <summary>The prediction is the stamp's rule: every predicted slot of a visited body carries the stamp's record for it
    /// (placed as a G.D.S. ruin, or a pinned skip where the seat found no room), and a body without a prediction carries no
    /// G.D.S. ruin.</summary>
    [Theory]
    [Trait("Category", "Slow")]
    [InlineData(3L)]
    [InlineData(8L)]
    [InlineData(21L)]
    public void ThePrediction_MatchesTheRealStamp(long seed)
    {
        var t = new RecordingTransport();
        var server = Start("jungle", t, out var repo, seed: seed);
        using (repo)
        {
            var admin = Admin(server);
            server.SetInstantTravelForTest(true);

            // The bodies of the start system (no jump drive needed): land on up to four and compare.
            var home = server.Galaxy.Systems.First(s => s.Bodies.Any(b => b.Id == admin.CurrentLocationId));
            var bodies = home.Bodies.Where(b => (b.Kind is CelestialKind.Planet or CelestialKind.Moon) && !string.IsNullOrEmpty(b.PlanetType)).Take(4);
            foreach (var body in bodies)
            {
                if (body.Id != admin.CurrentLocationId)
                {
                    Assert.True(server.QuickTravelForTest(admin.State.PlayerId, body.Id), $"seed {seed}: could not land on {body.Id}");
                }

                AssertStampMatchesPrediction(server, body.Id);
            }

            // Every predicted body further out (up to three): /ressaw n lands there, and its stamp decided the predicted slot.
            var candidates = server.AncientRuinCandidatesForTest(admin);
            Assert.NotEmpty(candidates);
            for (int n = 1; n <= Math.Min(3, candidates.Count); n++)
            {
                var c = server.AncientRuinCandidatesForTest(admin)[n - 1];
                VisitCandidate(server, t, admin, n);
                AssertStampMatchesPrediction(server, c.BodyId);
            }
        }
    }

    private static void AssertStampMatchesPrediction(SvGameServer server, string bodyId)
    {
        var predicted = server.PredictedAncientRuinSlotsForTest(bodyId);
        var records = server.PlacementRecordsForTest.Where(r => r.Kind == SvGameServer.UndergroundKind).ToList();
        foreach (int slot in predicted)
        {
            var rec = records.FirstOrDefault(r => r.Index == slot);
            Assert.True(rec != null, $"{bodyId}: slot {slot} was predicted, but the stamp left no record");
            if (rec!.Placed)
            {
                Assert.Contains(server.AncientRuinsForTest, s => s.Slot == slot && s.Gds);
            }
        }

        if (predicted.Count == 0)
        {
            Assert.DoesNotContain(server.AncientRuinsForTest, s => s.Gds);
        }
    }
}
