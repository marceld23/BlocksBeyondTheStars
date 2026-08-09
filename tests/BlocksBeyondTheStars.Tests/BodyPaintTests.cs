// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.State;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Avatar body paint (#874): per-part pixel paintings (torso/arms/legs/helmet), the face's siblings.
/// The server treats the payload as opaque but must bound it hard — it is persisted and rebroadcast —
/// so validation accepts exactly the per-part expected length in hex, shares the face's 2 s appearance
/// rate limit, and the strings must survive the player-snapshot round trip.
/// </summary>
public sealed class BodyPaintTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public BodyPaintTests()
    {
        _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bbts_bodypaint_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer NewServer(out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "bodypaint"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig { WorldName = "bodypaint", Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    private static string ValidPixels(int part) => new string('7', BodyPaint.ExpectedLength(part));

    [Fact]
    public void Messages_AreRegisteredWithNetCodec_AndRoundTrip()
    {
        // Registration guard: an unregistered message would throw on Encode (and silently never send).
        var intent = Assert.IsType<SetBodyPaintIntent>(NetCodec.Decode(NetCodec.Encode(
            new SetBodyPaintIntent { Part = BodyPaint.Legs, Pixels = "abc" })));
        Assert.Equal(BodyPaint.Legs, intent.Part);
        Assert.Equal("abc", intent.Pixels);

        var relay = Assert.IsType<PlayerBodyPaint>(NetCodec.Decode(NetCodec.Encode(
            new PlayerBodyPaint { PlayerId = "p1", Part = BodyPaint.Helmet, Pixels = "0f0f" })));
        Assert.Equal("p1", relay.PlayerId);
        Assert.Equal(BodyPaint.Helmet, relay.Part);
        Assert.Equal("0f0f", relay.Pixels);
    }

    [Fact]
    public void ValidPainting_IsStored_PerPart_AndEmptyClears()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Painter");

            for (int part = 0; part < BodyPaint.PartCount; part++)
            {
                server.TickForTest(3.0); // step past the shared appearance rate limit between parts
                server.SetBodyPaintForTest(p, part, ValidPixels(part));
                Assert.Equal(ValidPixels(part), p.State.GetBodyPaint(part));
            }

            server.TickForTest(3.0);
            server.SetBodyPaintForTest(p, BodyPaint.Torso, string.Empty);
            Assert.Equal(string.Empty, p.State.TorsoPixels);
            Assert.Equal(ValidPixels(BodyPaint.Arms), p.State.ArmPixels); // other parts untouched
        }
    }

    [Fact]
    public void MalformedPayloads_AreDropped()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Painter");

            // Wrong length (the face's 1024 is NOT a legal torso payload — torso needs 4 chunks).
            server.SetBodyPaintForTest(p, BodyPaint.Torso, new string('7', 1024));
            Assert.Equal(string.Empty, p.State.TorsoPixels);

            // Right length, non-hex charset.
            server.SetBodyPaintForTest(p, BodyPaint.Torso, new string('x', BodyPaint.ExpectedLength(BodyPaint.Torso)));
            Assert.Equal(string.Empty, p.State.TorsoPixels);

            // Unknown part indices must not crash or store anything.
            server.SetBodyPaintForTest(p, -1, ValidPixels(BodyPaint.Torso));
            server.SetBodyPaintForTest(p, BodyPaint.PartCount, ValidPixels(BodyPaint.Torso));
            for (int part = 0; part < BodyPaint.PartCount; part++)
            {
                Assert.Equal(string.Empty, p.State.GetBodyPaint(part));
            }
        }
    }

    [Fact]
    public void AppearanceRateLimit_IsShared_AcrossParts()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Painter");

            server.SetBodyPaintForTest(p, BodyPaint.Torso, ValidPixels(BodyPaint.Torso));
            Assert.Equal(ValidPixels(BodyPaint.Torso), p.State.TorsoPixels);

            // Immediately painting ANOTHER part must hit the same 2 s window (no per-part loophole).
            server.SetBodyPaintForTest(p, BodyPaint.Legs, ValidPixels(BodyPaint.Legs));
            Assert.Equal(string.Empty, p.State.LegPixels);

            server.TickForTest(3.0);
            server.SetBodyPaintForTest(p, BodyPaint.Legs, ValidPixels(BodyPaint.Legs));
            Assert.Equal(ValidPixels(BodyPaint.Legs), p.State.LegPixels);
        }
    }

    [Fact]
    public void Paintings_SurviveThePlayerSnapshotRoundTrip()
    {
        var state = new PlayerState
        {
            PlayerId = "p1",
            Name = "Painter",
            TorsoPixels = ValidPixels(BodyPaint.Torso),
            ArmPixels = ValidPixels(BodyPaint.Arms),
            LegPixels = ValidPixels(BodyPaint.Legs),
            HelmetPixels = ValidPixels(BodyPaint.Helmet),
        };

        var restored = StateMapper.FromSnapshot(StateMapper.ToSnapshot(state));

        for (int part = 0; part < BodyPaint.PartCount; part++)
        {
            Assert.Equal(state.GetBodyPaint(part), restored.GetBodyPaint(part));
        }
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (System.IO.Directory.Exists(_root)) System.IO.Directory.Delete(_root, recursive: true);
        }
        catch { }
    }
}
