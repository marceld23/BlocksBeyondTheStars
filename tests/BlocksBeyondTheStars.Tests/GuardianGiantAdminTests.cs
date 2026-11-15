// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3676: the guardian giants' admin tools — <c>/giant guardian</c> lays a giant that can wake about thirty blocks in front of
/// the admin (its feet toward them, its record written), and <c>/tp giant [n]</c> stands the admin 12–16 blocks from one of
/// this body's giants, named with its state, never inside the body; a body without one says so.
/// </summary>
public sealed class GuardianGiantAdminTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_giantadmin_" + Guid.NewGuid().ToString("N"));
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    private readonly List<SqliteWorldRepository> _repos = new();

    public void Dispose()
    {
        foreach (var repo in _repos)
        {
            repo.Dispose();
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch { /* best-effort temp cleanup */ }
    }

    private SvGameServer Started(string world, RecordingTransport transport)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        _repos.Add(repo);
        var config = new ServerConfig
        {
            WorldName = world,
            Seed = 9,
            StartPlanet = "rocky",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = false,
            PlaceWrecks = false,
            PlaceBanditCamps = false,
            ViewDistanceChunks = 1,
        };
        config.Rules.AdminCheats = true;
        config.Rules.AllowCheatsInSurvival = true;
        config.Rules.PlanetEnemies = AlienActivity.Normal;
        config.Rules.Bandits = AlienActivity.Off;
        config.Rules.CreatureAbundance = AlienActivity.Off;
        var server = new SvGameServer(config, _content, transport, repo);
        server.Start();
        return server;
    }

    private static PlayerSession Admin(SvGameServer server)
    {
        var p = server.AddLocalPlayer("Marcel", "en");
        p.State.Role = PlayerRole.WorldAdmin;
        p.State.AboardShip = false;
        return p;
    }

    private static List<object> Run(SvGameServer server, RecordingTransport transport, PlayerSession admin, string command, string arg)
    {
        int before = transport.Sent.Count;
        server.HandleForTest(admin, new AdminCommandIntent { Command = command, StringArg = arg });
        return transport.Sent.Skip(before).Where(s => s.Conn == admin.ConnectionId).Select(s => s.Msg).ToList();
    }

    /// <summary>Flat, dry spots around the start (the seed's start is an island), nearest first.</summary>
    private static IEnumerable<(int X, int Z)> FlatSpots(SvGameServer server, PlayerSession p)
    {
        int px = (int)p.State.Position.X, pz = (int)p.State.Position.Z;
        return from r in Enumerable.Range(3, 12)
               from a in Enumerable.Range(0, 16)
               let x = px + (int)Math.Round(Math.Cos(a * Math.PI / 8) * r * 16)
               let z = pz + (int)Math.Round(Math.Sin(a * Math.PI / 8) * r * 16)
               where server.HunterSpotSpreadForTest(x, z) is >= 0 and <= 1
               select (x, z);
    }

    private string KeyAt(SvGameServer server, Vector3i cell) => _content.BlockById(server.World.GetBlock(cell))?.Key ?? "air";

    [Fact]
    public void GiantGuardian_LaysAGiantThatCanWake_ThirtyBlocksAhead_FeetTowardTheAdmin()
    {
        var transport = new RecordingTransport();
        var server = Started("lay", transport);
        var admin = Admin(server);
        GuardianGiantRecord? rec = null;
        foreach (var (x, z) in FlatSpots(server, admin).Take(20))
        {
            admin.State.Position = new Vector3f(x + 0.5f, server.GroundFeetForTest(x, z - 30), z - 29.5f);
            admin.State.Yaw = 0f; // looking toward +Z
            var answer = Run(server, transport, admin, "summon_giant", "guardian");
            if (answer.OfType<ServerMessage>().Any(m => m.Text == "@srv.giant.laid"))
            {
                rec = Assert.Single(server.Metadata.GuardianGiants.Values);
                break;
            }

            Assert.Contains(answer.OfType<ActionRejected>(), r => r.Reason == "@srv.giant.no_spot");
        }

        Assert.NotNull(rec);
        Assert.True(rec!.CanWake);
        Assert.Equal(GuardianGiantRecord.Dormant, rec.State);
        Assert.True(rec.Index >= SvGameServer.GuardianGiantAdminIndex);
        Assert.Equal(server.Metadata.ActiveLocationId, rec.LocationId);
        Assert.Equal(2, rec.Facing); // the head lies away from the admin (+Z), the feet toward them
        float dx = rec.X - admin.State.Position.X, dz = rec.Z - admin.State.Position.Z;
        Assert.InRange(Math.Sqrt((dx * dx) + (dz * dz)), 20.0, 40.0);
        foreach (var c in GuardianGiantBody.Lying(rec.Seed, lit: false, rec.Facing))
        {
            Assert.Equal(c.Block, KeyAt(server, new Vector3i(rec.X + c.Dx, rec.Y + c.Dy, rec.Z + c.Dz))); // whole, lights out
        }
    }

    [Fact]
    public void TpGiant_StandsBesideTheGiant_NamedWithItsState_AndSaysWhenThereIsNone()
    {
        var transport = new RecordingTransport();
        var server = Started("tp", transport);
        var admin = Admin(server);
        Assert.Contains(Run(server, transport, admin, "teleport_to_named", "giant").OfType<ActionRejected>(), r => r.Reason == "@srv.tp.no_giant");

        var (sx, sz) = FlatSpots(server, admin).First();
        var rec = server.LayGuardianGiantForTest(sx, sz, facing: 1, canWake: false);
        var target = Assert.Single(server.TeleportTargetsForTest(admin.State.PlayerId), t => t.Kind == "giant");
        Assert.Equal("giant1 'Guardian giant 1 · lying'", target.Label);
        var list = Run(server, transport, admin, "teleport_to_named", string.Empty).OfType<ServerMessage>().Select(m => m.Text).ToList();
        Assert.Contains(list, t => t.StartsWith("/tp giant1 · " + target.Label, StringComparison.Ordinal));

        var (cx, cz) = GuardianGiantBody.LyingCentre(rec.Facing);
        var body = GuardianGiantBody.Lying(rec.Seed, lit: false, rec.Facing).Select(c => new Vector3i(rec.X + c.Dx, rec.Y + c.Dy, rec.Z + c.Dz)).ToHashSet();
        foreach (string word in new[] { "giant", "giant1", "giants", "Gigant 1", "guardiangiant" })
        {
            admin.State.Position = new Vector3f(0f, 500f, 0f);
            Run(server, transport, admin, "teleport_to_named", word);
            Assert.Equal(target.Position, admin.State.Position);
        }

        var at = admin.State.Position;
        float dx = at.X - (rec.X + cx), dz = at.Z - (rec.Z + cz);
        Assert.InRange(Math.Sqrt((dx * dx) + (dz * dz)), 11.0, 17.0);
        var feet = new Vector3i((int)Math.Floor(at.X), (int)Math.Floor(at.Y), (int)Math.Floor(at.Z));
        Assert.DoesNotContain(feet, body);
        Assert.Equal("air", KeyAt(server, feet));
    }
}
