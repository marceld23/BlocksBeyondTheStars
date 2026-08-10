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
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Player-painted block designs (#817): the design id rides in the shape descriptor's previously-unused
/// bits 11+ (the up-face precedent), the bitmap lives ONCE in the save-global paint_design registry.
/// These tests pin the bit packing (old descriptors must read "unpainted"), the registry persistence in
/// SQLite + the WebGL memory snapshot, and the server intent path: validation, dedup, clearing, and the
/// solid-blocks-only rule.
/// </summary>
public sealed class PaintDesignTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public PaintDesignTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_paint_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // best-effort temp cleanup
        }
    }

    private static string ValidPixels(char fill = '3')
        => new string(fill, 1024); // 32×32, one hex char per pixel

    // ── ShapeCode design bits ────────────────────────────────────────────────────────────────────

    [Fact]
    public void DesignBits_RoundTrip_AndPreserveTheRestOfTheDescriptor()
    {
        int packed = ShapeCode.Pack(9 /* Panel */, 3, 4);
        int painted = ShapeCode.WithDesign(packed, 12345);

        Assert.Equal(12345, ShapeCode.DesignOf(painted));
        Assert.Equal(9, ShapeCode.ShapeOf(painted));
        Assert.Equal(3, ShapeCode.OrientationOf(painted));
        Assert.Equal(4, ShapeCode.UpFaceOf(painted));

        // Stripping the design restores the original descriptor exactly — the mined drop keeps only the form.
        Assert.Equal(packed, ShapeCode.WithoutDesign(painted));
    }

    [Fact]
    public void OldDescriptors_ReadAsUnpainted()
    {
        // Every descriptor written before this feature has bits 11+ = 0 → design 0 = no paint. That IS the
        // migration (same trick as the up-face field).
        Assert.Equal(0, ShapeCode.DesignOf(ShapeCode.Pack(BlockShape.Slab, 2)));
        Assert.Equal(0, ShapeCode.DesignOf(0));
    }

    [Fact]
    public void PaintedPlainCube_IsStillACube()
    {
        // Face culling, airtightness and the shaped-block mesher branch all key on IsCube — a painted
        // full cube must stay a cube for every one of them.
        int paintedCube = ShapeCode.WithDesign(0, 7);
        Assert.True(ShapeCode.IsCube(paintedCube));
        Assert.NotEqual(0, paintedCube); // …but the descriptor is non-zero, so ChunkData keeps the entry alive
    }

    // ── Registry persistence ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void Sqlite_SaveListDelete_RoundTrips()
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "sq"));
        repo.Initialize();

        repo.SavePaintDesign(new StoredPaintDesign { Id = 1, OwnerId = "p1", Pixels = ValidPixels() });
        repo.SavePaintDesign(new StoredPaintDesign { Id = 2, OwnerId = "p2", Pixels = ValidPixels('a') });
        repo.SavePaintDesign(new StoredPaintDesign { Id = 1, OwnerId = "p1", Pixels = string.Empty }); // tombstone upsert

        var designs = repo.ListPaintDesigns();
        Assert.Equal(2, designs.Count);
        Assert.Equal(string.Empty, designs.Single(d => d.Id == 1).Pixels);
        Assert.Equal(ValidPixels('a'), designs.Single(d => d.Id == 2).Pixels);

        repo.DeletePaintDesign(2);
        Assert.Single(repo.ListPaintDesigns());

        repo.SavePaintReport(new StoredPaintReport
        {
            ReporterId = "p3",
            OwnerId = "p2",
            DesignId = 2,
            Planet = "sys0-p1",
            X = 1,
            Y = 2,
            Z = 3,
            CreatedUnix = 42,
        });
        var reports = repo.ListPaintReports();
        Assert.Single(reports);
        Assert.Equal("p3", reports[0].ReporterId);
        Assert.Equal(2, reports[0].DesignId);
    }

    [Fact]
    public void MemorySnapshot_CarriesDesignsAndReports()
    {
        var repo = new MemoryWorldRepository(new SaveGamePaths(_root, "mem"));
        repo.Initialize();
        repo.SavePaintDesign(new StoredPaintDesign { Id = 5, OwnerId = "p1", Pixels = ValidPixels() });
        repo.SavePaintReport(new StoredPaintReport { ReporterId = "p2", OwnerId = "p1", DesignId = 5, Planet = "w", CreatedUnix = 7 });

        var blob = repo.ExportSnapshotBlob();
        var restored = new MemoryWorldRepository(new SaveGamePaths(_root, "mem2"));
        restored.Initialize();
        restored.ImportSnapshotBlob(blob);

        Assert.Equal(ValidPixels(), restored.ListPaintDesigns().Single(d => d.Id == 5).Pixels);
        Assert.Equal(7, restored.ListPaintReports().Single().CreatedUnix);
    }

    [Fact]
    public void BlockEditShapeColumn_CarriesTheDesignBits_Unchanged()
    {
        // The whole point of the bit-packing: a painted cell persists through the EXISTING shape column,
        // no new storage per block.
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "bits"));
        repo.Initialize();

        int painted = ShapeCode.WithDesign(ShapeCode.Pack(BlockShape.Panel, 1, 2), 99);
        var pos = new Vector3i(10, 64, 10);
        repo.SetBlock("sys0-p1", pos, 5, shape: painted);

        var edit = repo.LoadChunkEdits("sys0-p1", WorldConstants.WorldToChunk(pos))
            .Single(e => e.WorldPosition.Equals(pos));
        Assert.Equal(painted, edit.Shape);
        Assert.Equal(99, ShapeCode.DesignOf(edit.Shape));
    }

    // ── Server intent path ───────────────────────────────────────────────────────────────────────

    private (SvGameServer Server, LoopbackClientTransport Client, SqliteWorldRepository Repo) Start(string world)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        var link = new LoopbackLink();
        var st = new LoopbackServerTransport(link);
        var client = new LoopbackClientTransport(link);
        var config = new ServerConfig { WorldName = world, Seed = 1, AutoSaveIntervalMinutes = 9999, Rules = new GameRules() };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        client.Connect("loopback", 0);
        client.Send(NetCodec.Encode(new JoinRequest { PlayerName = "Painter" }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        return (server, client, repo);
    }

    /// <summary>Puts a stone block near the player and returns its cell (reach-checked by the handler).</summary>
    private Vector3i PlaceCanvasBlock(SvGameServer server)
    {
        var stone = _content.GetBlock("stone")!.NumericId;
        var pos = new Vector3i(5, 300, 0);
        server.World.SetBlock(pos, stone);
        server.Sessions[1].State.Position = new Vector3f(5.5f, 301f, 0.5f);
        return pos;
    }

    [Fact]
    public void Paint_RegistersDesign_AndStampsTheShapeBits()
    {
        var (server, client, repo) = Start("paint1");
        var pos = PlaceCanvasBlock(server);

        client.Send(NetCodec.Encode(new PaintBlockIntent { X = pos.X, Y = pos.Y, Z = pos.Z, Pixels = ValidPixels() }),
            DeliveryMode.ReliableOrdered);
        server.Tick(0.1);

        int design = ShapeCode.DesignOf(server.World.GetShape(pos));
        Assert.NotEqual(0, design);
        var stored = repo.ListPaintDesigns().Single();
        Assert.Equal(design, stored.Id);
        Assert.Equal(ValidPixels(), stored.Pixels);
        Assert.Equal(server.Sessions[1].State.PlayerId, stored.OwnerId);
    }

    [Fact]
    public void SamePixels_ReuseTheSameDesignId()
    {
        var (server, client, _) = Start("paint2");
        var pos = PlaceCanvasBlock(server);
        var stone = _content.GetBlock("stone")!.NumericId;
        var pos2 = new Vector3i(6, 300, 0);
        server.World.SetBlock(pos2, stone);

        client.Send(NetCodec.Encode(new PaintBlockIntent { X = pos.X, Y = pos.Y, Z = pos.Z, Pixels = ValidPixels() }),
            DeliveryMode.ReliableOrdered);
        server.Tick(3.0); // paints are throttled to one per 2 s — pass the window before the second one
        client.Send(NetCodec.Encode(new PaintBlockIntent { X = pos2.X, Y = pos2.Y, Z = pos2.Z, Pixels = ValidPixels() }),
            DeliveryMode.ReliableOrdered);
        server.Tick(0.1);

        int d1 = ShapeCode.DesignOf(server.World.GetShape(pos));
        int d2 = ShapeCode.DesignOf(server.World.GetShape(pos2));
        Assert.NotEqual(0, d1);
        Assert.Equal(d1, d2); // dedup: one registry row however many blocks carry the motif
    }

    [Fact]
    public void EmptyPixels_ClearThePaint_ButKeepTheShape()
    {
        var (server, client, _) = Start("paint3");
        var pos = PlaceCanvasBlock(server);
        int shaped = ShapeCode.Pack(BlockShape.Panel, 1, 0);
        server.World.SetBlock(pos, server.World.GetBlock(pos), 0, 0, shaped);

        client.Send(NetCodec.Encode(new PaintBlockIntent { X = pos.X, Y = pos.Y, Z = pos.Z, Pixels = ValidPixels() }),
            DeliveryMode.ReliableOrdered);
        server.Tick(3.0);
        Assert.NotEqual(0, ShapeCode.DesignOf(server.World.GetShape(pos)));

        client.Send(NetCodec.Encode(new PaintBlockIntent { X = pos.X, Y = pos.Y, Z = pos.Z, Pixels = string.Empty }),
            DeliveryMode.ReliableOrdered);
        server.Tick(0.1);

        Assert.Equal(shaped, server.World.GetShape(pos)); // paint gone, the panel form + orientation stay
    }

    [Theory]
    [InlineData("xyz")]                     // wrong length
    [InlineData("g")]                       // wrong length (a legal symbol since #899, but still one char)
    public void MalformedPixels_AreDropped(string bad)
    {
        var (server, client, repo) = Start("paint4");
        var pos = PlaceCanvasBlock(server);

        client.Send(NetCodec.Encode(new PaintBlockIntent { X = pos.X, Y = pos.Y, Z = pos.Z, Pixels = bad }),
            DeliveryMode.ReliableOrdered);
        server.Tick(0.1);

        Assert.Equal(0, ShapeCode.DesignOf(server.World.GetShape(pos)));
        Assert.Empty(repo.ListPaintDesigns());
    }

    [Fact]
    public void OffAlphabet_FullLengthPixels_AreDropped()
    {
        var (server, client, repo) = Start("paint5");
        var pos = PlaceCanvasBlock(server);

        // 'z' is past the end of the base32 palette alphabet (0-9a-v) — reserved, never a colour.
        client.Send(NetCodec.Encode(new PaintBlockIntent { X = pos.X, Y = pos.Y, Z = pos.Z, Pixels = new string('z', 1024) }),
            DeliveryMode.ReliableOrdered);
        server.Tick(0.1);

        Assert.Equal(0, ShapeCode.DesignOf(server.World.GetShape(pos)));
        Assert.Empty(repo.ListPaintDesigns());
    }

    /// <summary>The palette widened from 16 to 32 colours (#899): a design drawn with the new symbols must
    /// register like any other, or the extra colours would look right in the editor and vanish on the block.</summary>
    [Fact]
    public void WidenedPaletteSymbols_AreAccepted()
    {
        var (server, client, repo) = Start("paint6");
        var pos = PlaceCanvasBlock(server);

        string pixels = new string('v', 1024); // 'v' = palette index 31, the last slot of the base32 alphabet
        client.Send(NetCodec.Encode(new PaintBlockIntent { X = pos.X, Y = pos.Y, Z = pos.Z, Pixels = pixels }),
            DeliveryMode.ReliableOrdered);
        server.Tick(0.1);

        Assert.NotEqual(0, ShapeCode.DesignOf(server.World.GetShape(pos)));
        Assert.Equal(pixels, repo.ListPaintDesigns().Single().Pixels);
    }

    [Fact]
    public void AirCell_CannotBePainted()
    {
        var (server, client, repo) = Start("paint6");
        PlaceCanvasBlock(server);
        var air = new Vector3i(5, 302, 0); // above the stone — empty

        client.Send(NetCodec.Encode(new PaintBlockIntent { X = air.X, Y = air.Y, Z = air.Z, Pixels = ValidPixels() }),
            DeliveryMode.ReliableOrdered);
        server.Tick(0.1);

        Assert.Empty(repo.ListPaintDesigns());
    }

    [Fact]
    public void Paint_SurvivesSaveAndReload()
    {
        var (server, client, repo) = Start("paint7");
        var pos = PlaceCanvasBlock(server);

        client.Send(NetCodec.Encode(new PaintBlockIntent { X = pos.X, Y = pos.Y, Z = pos.Z, Pixels = ValidPixels('b') }),
            DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        int design = ShapeCode.DesignOf(server.World.GetShape(pos));
        Assert.NotEqual(0, design);

        // A second server over the SAME repo: the registry restores at Start, the cell restores via the
        // ordinary block-edit replay — nothing paint-specific in the chunk path.
        var link2 = new LoopbackLink();
        var server2 = new SvGameServer(
            new ServerConfig { WorldName = "paint7", Seed = 1, AutoSaveIntervalMinutes = 9999, Rules = new GameRules() },
            _content, new LoopbackServerTransport(link2), repo);
        server2.Start();

        Assert.Equal(design, ShapeCode.DesignOf(server2.World.GetShape(pos)));
        Assert.Equal(ValidPixels('b'), repo.ListPaintDesigns().Single(d => d.Id == design).Pixels);
    }
}
