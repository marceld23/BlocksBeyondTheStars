// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Whole-build share codes (#1117): the BBTS1-B code round-trips a region losslessly (keys, shapes, dye),
/// hostile/tampered codes simply fail to decode, a paste pays materials from the inventory and respects
/// base protection, and the code for a normal small build stays clipboard-friendly.
/// </summary>
public sealed class BlueprintTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public BlueprintTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_blueprint_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    // ---- The code itself (no server) -----------------------------------------------------------

    private static BlueprintCell[] SampleCells(int sx, int sy, int sz)
    {
        var cells = new BlueprintCell[sx * sy * sz];
        cells[BlueprintCode.CellIndex(0, 0, 0, sy, sz)] = new BlueprintCell { Key = "concrete", Shape = ShapeCode.Pack(2, 1, 0), Tint = 0x3366CC, Glow = 0 };
        cells[BlueprintCode.CellIndex(1, 0, 0, sy, sz)] = new BlueprintCell { Key = "torch", Shape = 0, Tint = 0, Glow = 0 };
        cells[BlueprintCode.CellIndex(0, 1, 1, sy, sz)] = new BlueprintCell { Key = "glass", Shape = 0, Tint = 0, Glow = 0x112233 };
        return cells;
    }

    [Fact]
    public void Code_RoundTrips_KeysShapesAndDye()
    {
        var cells = SampleCells(2, 2, 2);
        string code = BlueprintCode.Encode(2, 2, 2, "Marcel", "Hut", cells);
        Assert.StartsWith("BBTS1-B-", code);

        Assert.True(BlueprintCode.TryDecode(code, out int sx, out int sy, out int sz, out string author, out string name, out var back));
        Assert.Equal((2, 2, 2), (sx, sy, sz));
        Assert.Equal("Marcel", author);
        Assert.Equal("Hut", name);
        for (int i = 0; i < cells.Length; i++)
        {
            Assert.Equal(cells[i].Key, back[i].Key);
            Assert.Equal(cells[i].Shape, back[i].Shape);
            Assert.Equal(cells[i].Tint, back[i].Tint);
            Assert.Equal(cells[i].Glow, back[i].Glow);
        }
    }

    [Fact]
    public void Code_StripsSaveLocalDesignBits_FromShapes()
    {
        var cells = new BlueprintCell[1];
        int withDesign = ShapeCode.WithDesign(ShapeCode.Pack(3, 2, 0), 7); // paint-design ids are save-local
        cells[0] = new BlueprintCell { Key = "concrete", Shape = withDesign };

        string code = BlueprintCode.Encode(1, 1, 1, "a", "n", cells);
        Assert.True(BlueprintCode.TryDecode(code, out _, out _, out _, out _, out _, out var back));
        Assert.Equal(ShapeCode.Pack(3, 2, 0), back[0].Shape);
    }

    [Fact]
    public void Code_RejectsTamperedAndOversizedInput()
    {
        Assert.False(BlueprintCode.TryDecode("BBTS1-B-not base64!!", out _, out _, out _, out _, out _, out _));
        Assert.False(BlueprintCode.TryDecode("BBTS1-F-" + Convert.ToBase64String(new byte[] { 1, 2, 3 }), out _, out _, out _, out _, out _, out _));
        Assert.False(BlueprintCode.TryDecode(ShareCode.Encode("B", Convert.ToBase64String(new byte[] { 99, 1, 1, 1 }), "x"), out _, out _, out _, out _, out _, out _)); // wrong version
        Assert.False(BlueprintCode.TryDecode(ShareCode.Encode("B", Convert.ToBase64String(new byte[BlueprintCode.MaxPayloadBytes + 1]), "x"), out _, out _, out _, out _, out _, out _));
        Assert.Equal(string.Empty, BlueprintCode.Encode(17, 1, 1, "a", "n", new BlueprintCell[17])); // over the edge cap
    }

    [Fact]
    public void Code_ForASmallBuild_StaysClipboardFriendly()
    {
        // An 8×4×8 shed with three materials — the everyday case must stay easy to paste anywhere.
        const int sx = 8, sy = 4, sz = 8;
        var cells = new BlueprintCell[sx * sy * sz];
        for (int x = 0; x < sx; x++)
            for (int z = 0; z < sz; z++)
            {
                cells[BlueprintCode.CellIndex(x, 0, z, sy, sz)] = new BlueprintCell { Key = "concrete" };
                cells[BlueprintCode.CellIndex(x, 3, z, sy, sz)] = new BlueprintCell { Key = "glass" };
            }

        string code = BlueprintCode.Encode(sx, sy, sz, "Marcel", "Shed", cells);
        Assert.InRange(code.Length, 1, 1500);
    }

    // ---- Copy → paste on a live server ---------------------------------------------------------

    private ServerConfig Config() => new()
    {
        WorldName = "blueprint",
        Seed = 777,
        StartPlanet = "rocky",
        AutoSaveIntervalMinutes = 9999,
        ViewDistanceChunks = 1,
        MaxPlayers = 4,
        PlaceStarterShip = false,
    };

    private static LoopbackLink NewLink(out LoopbackLink link)
    {
        link = new LoopbackLink();
        return link;
    }

    private static void JoinAndDrain(SvGameServer server, LoopbackClientTransport client, string name)
    {
        client.Connect("loopback", 0);
        client.Send(NetCodec.Encode(new JoinRequest { PlayerName = name }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        client.Poll();
    }

    private static List<T> Capture<T>(LoopbackClientTransport client) where T : class
    {
        var messages = new List<T>();
        client.PayloadReceived += payload =>
        {
            if (NetCodec.Decode(payload) is T m)
            {
                messages.Add(m);
            }
        };
        return messages;
    }

    [Fact]
    public void CopyThenPaste_RebuildsTheRegion_AndPaysMaterials()
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "blueprint"));
        using var serverTransport = new LoopbackServerTransport(NewLink(out var link));
        using var client = new LoopbackClientTransport(link);
        var codes = Capture<BuildCodeResult>(client);
        var results = Capture<BuildPasteResult>(client);
        var server = new SvGameServer(Config(), _content, serverTransport, repo);
        server.Start();
        JoinAndDrain(server, client, "Builder");

        var session = server.Sessions[1];
        var p = session.State.Position;
        int bx = (int)Math.Floor(p.X) + 4, by = (int)Math.Floor(p.Y) + 6, bz = (int)Math.Floor(p.Z);

        // Build a 2×1×1 original in the air near the player: a shaped, dyed block + a plain one.
        var concrete = _content.GetBlock("concrete")!;
        int shape = ShapeCode.Pack(2, 1, 0);
        server.World.SetBlock(new Vector3i(bx, by, bz), concrete.NumericId, 0x3366CC, 0, shape, "Builder");
        server.World.SetBlock(new Vector3i(bx + 1, by, bz), concrete.NumericId, 0, 0, 0, "Builder");

        client.Send(NetCodec.Encode(new CopyBuildIntent { X1 = bx, Y1 = by, Z1 = bz, X2 = bx + 1, Y2 = by, Z2 = bz, Name = "Pair" }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        client.Poll();
        var code = Assert.Single(codes);
        Assert.True(code.Success, code.Reason);

        // Paste two blocks over: enough concrete for exactly the two cells.
        session.State.Inventory.Add("concrete", 2, 99);
        int px = bx + 4;
        client.Send(NetCodec.Encode(new PasteBuildIntent { Code = code.Code, X = px, Y = by, Z = bz }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        client.Poll();

        var result = Assert.Single(results);
        Assert.True(result.Success, result.Reason);
        Assert.Equal(2, result.Placed);
        Assert.Equal("Builder", result.Author);
        Assert.Equal(0, session.State.Inventory.CountOf("concrete")); // both blocks were paid for

        Assert.Equal(concrete.NumericId, server.World.GetBlock(new Vector3i(px, by, bz)));
        Assert.Equal(shape, server.World.GetShape(new Vector3i(px, by, bz)));
        Assert.Equal((0x3366CC, 0), server.World.GetModifier(new Vector3i(px, by, bz)));
        Assert.Equal(concrete.NumericId, server.World.GetBlock(new Vector3i(px + 1, by, bz)));

        // Pasting again immediately trips the cooldown, and without materials nothing places anyway.
        results.Clear();
        client.Send(NetCodec.Encode(new PasteBuildIntent { Code = code.Code, X = px + 8, Y = by, Z = bz }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        client.Poll();
        Assert.False(Assert.Single(results).Success);
    }

    [Fact]
    public void Paste_RespectsAnotherPlayersBaseProtection()
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "blueprint"));
        using var serverTransport = new LoopbackServerTransport(NewLink(out var link));
        using var client = new LoopbackClientTransport(link);
        var results = Capture<BuildPasteResult>(client);
        var server = new SvGameServer(Config(), _content, serverTransport, repo);
        server.Start();
        JoinAndDrain(server, client, "Builder");

        var session = server.Sessions[1];
        session.State.Role = Shared.State.PlayerRole.Player; // the first join is the host/admin — admins bypass protection on purpose
        var p = session.State.Position;
        int bx = (int)Math.Floor(p.X) + 4, by = (int)Math.Floor(p.Y) + 6, bz = (int)Math.Floor(p.Z);

        // Someone ELSE's base zone covers the paste target.
        var owner = server.AddLocalPlayer("Owner");
        server.PlaceBaseForTest(owner, new Vector3i(bx, by, bz));

        var cells = new BlueprintCell[] { new() { Key = "concrete" } };
        string code = BlueprintCode.Encode(1, 1, 1, "Builder", "Cube", cells);
        session.State.Inventory.Add("concrete", 1, 99);

        client.Send(NetCodec.Encode(new PasteBuildIntent { Code = code, X = bx, Y = by, Z = bz }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        client.Poll();

        var result = Assert.Single(results);
        Assert.False(result.Success);
        Assert.Equal(1, result.SkippedProtected);
        Assert.Equal(1, session.State.Inventory.CountOf("concrete")); // nothing was paid for a refused cell
    }
}
