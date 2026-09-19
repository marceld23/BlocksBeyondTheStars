// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.Textures;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// World textures (#1958): an admin's textures for everyone in the save. Pins the payload codec (exact size, hard
/// stop while inflating), the server's gatekeeping (rule, role, key, animation, alpha rule, caps, pacing), the
/// paged join list and the three repositories.
/// </summary>
public sealed class WorldTextureTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public WorldTextureTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_worldtex_" + Guid.NewGuid().ToString("N"));
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

    private static byte[] Frame(byte value, byte alpha = 255)
    {
        var raw = new byte[TextureTiles.BytesPerFrame];
        for (int i = 0; i < raw.Length; i += 4)
        {
            raw[i] = value;
            raw[i + 1] = (byte)(value / 2);
            raw[i + 2] = (byte)(i % 251); // not perfectly flat, so deflate has something to do
            raw[i + 3] = alpha;
        }

        return raw;
    }

    private static string Data(params byte[][] frames) => WorldTextureCodec.Encode(frames);

    // ── Codec ───────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Codec_RoundTripsFramesExactly()
    {
        var frames = new[] { Frame(10), Frame(200), Frame(90) };

        Assert.True(WorldTextureCodec.TryDecode(WorldTextureCodec.Encode(frames), 3, out var back));

        Assert.Equal(3, back.Length);
        for (int i = 0; i < 3; i++)
        {
            Assert.Equal(frames[i], back[i]);
        }
    }

    [Fact]
    public void Codec_RejectsAnythingThatIsNotExactlyTheAnnouncedSize()
    {
        string two = Data(Frame(1), Frame(2));

        Assert.False(WorldTextureCodec.TryDecode(two, 1, out _));  // longer than announced
        Assert.False(WorldTextureCodec.TryDecode(two, 3, out _));  // shorter than announced
        Assert.False(WorldTextureCodec.TryDecode(two, 0, out _));
        Assert.False(WorldTextureCodec.TryDecode(two, TextureTiles.MaxFrames + 1, out _));
        Assert.False(WorldTextureCodec.TryDecode(string.Empty, 1, out _));
        Assert.False(WorldTextureCodec.TryDecode(null, 1, out _));
        Assert.False(WorldTextureCodec.TryDecode("not base64 !!", 1, out _));
        Assert.False(WorldTextureCodec.TryDecode(Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5 }), 1, out _)); // not deflate
    }

    [Fact]
    public void Codec_StopsABomb_BeforeItInflates()
    {
        // 64 MB of zeros deflate to a few dozen KB — far more pixels than any texture. The decoder must refuse it
        // by size, without ever allocating it.
        var zeros = new byte[TextureTiles.BytesPerFrame];
        var many = Enumerable.Repeat(zeros, 4096).ToArray();
        string bomb = WorldTextureCodec.Encode(many);

        Assert.True(bomb.Length < WorldTextureCodec.MaxEncodedLength(TextureTiles.MaxFrames)); // it would pass a length check alone
        Assert.False(WorldTextureCodec.TryDecode(bomb, TextureTiles.MaxFrames, out _));
    }

    // ── Server ──────────────────────────────────────────────────────────────────────────────────

    private (SvGameServer Server, LoopbackClientTransport Client, SqliteWorldRepository Repo, List<object> Inbox) Start(
        string world, GameRules? rules = null, bool admin = true)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        var link = new LoopbackLink();
        var st = new LoopbackServerTransport(link);
        var client = new LoopbackClientTransport(link);
        var inbox = new List<object>();
        client.PayloadReceived += payload =>
        {
            if (NetCodec.Decode(payload) is { } message)
            {
                inbox.Add(message);
            }
        };
        var config = new ServerConfig { WorldName = world, Seed = 1, AutoSaveIntervalMinutes = 9999, Rules = rules ?? new GameRules() };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        client.Connect("loopback", 0);
        client.Send(NetCodec.Encode(new JoinRequest { PlayerName = "Painter" }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        server.Sessions[1].State.Role = admin ? PlayerRole.Admin : PlayerRole.Player;
        client.Poll();
        return (server, client, repo, inbox);
    }

    private static void Publish(LoopbackClientTransport client, SvGameServer server, string key, int frames, int fps, string data)
    {
        client.Send(NetCodec.Encode(new PublishWorldTextureIntent { Key = key, Frames = frames, Fps = fps, Data = data }),
            DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        client.Poll();
    }

    [Fact]
    public void AnAdminPublishes_TheSaveKeepsIt_AndEveryClientHearsOfIt()
    {
        var (server, client, repo, inbox) = Start("wt1");
        inbox.Clear();

        Publish(client, server, "stone", 1, 0, Data(Frame(120)));

        var stored = repo.ListWorldTextures().Single();
        Assert.Equal("stone", stored.Key);
        Assert.Equal(1, stored.Frames);
        Assert.Equal("Painter", stored.OwnerName);
        var pushed = inbox.OfType<WorldTextureData>().Single().Texture;
        Assert.Equal("stone", pushed.Key);
        Assert.True(WorldTextureCodec.TryDecode(pushed.Data, 1, out _));
    }

    [Fact]
    public void ASeeThroughBlockTile_LeavesTheServerOpaque()
    {
        var (server, client, repo, _) = Start("wt2");

        Publish(client, server, "stone", 1, 0, Data(Frame(120, alpha: 0))); // an x-ray attempt

        var stored = repo.ListWorldTextures().Single();
        Assert.True(WorldTextureCodec.TryDecode(stored.Data, 1, out var pixels));
        Assert.Equal(0, TextureTiles.CountSeeThrough(pixels[0]));
    }

    [Fact]
    public void ACutoutTile_KeepsItsAlpha()
    {
        var (server, client, repo, _) = Start("wt3");

        Publish(client, server, "flora_fern", 1, 0, Data(Frame(80, alpha: 0)));

        var stored = repo.ListWorldTextures().Single();
        Assert.True(WorldTextureCodec.TryDecode(stored.Data, 1, out var pixels));
        Assert.Equal(TextureTiles.Size * TextureTiles.Size, TextureTiles.CountSeeThrough(pixels[0]));
    }

    [Fact]
    public void OnlyAdminsPublish()
    {
        var (server, client, repo, inbox) = Start("wt4", admin: false);
        inbox.Clear();

        Publish(client, server, "stone", 1, 0, Data(Frame(120)));

        Assert.Empty(repo.ListWorldTextures());
        Assert.Equal("@srv.worldtex.admin_only", inbox.OfType<ActionRejected>().Single().Reason);
    }

    [Fact]
    public void TheWorldRule_SwitchesPublishingOff()
    {
        var (server, client, repo, inbox) = Start("wt5", new GameRules { WorldTextures = false });
        inbox.Clear();

        Publish(client, server, "stone", 1, 0, Data(Frame(120)));

        Assert.Empty(repo.ListWorldTextures());
        Assert.Equal("@srv.worldtex.off", inbox.OfType<ActionRejected>().Single().Reason);
    }

    [Theory]
    [InlineData("no_such_block_anywhere", "@srv.worldtex.bad_key")]
    [InlineData("../stone", "@srv.worldtex.bad_key")]
    [InlineData("Stone", "@srv.worldtex.bad_key")]
    public void UnknownOrMalformedKeys_AreRefused(string key, string reason)
    {
        var (server, client, repo, inbox) = Start("wt6_" + key.Length + "_" + (int)key[0]);
        inbox.Clear();

        Publish(client, server, key, 1, 0, Data(Frame(1)));

        Assert.Empty(repo.ListWorldTextures());
        Assert.Equal(reason, inbox.OfType<ActionRejected>().Single().Reason);
    }

    [Fact]
    public void NonBlockFamilies_ArePublishable_ButNeverAnimated()
    {
        var (server, client, repo, inbox) = Start("wt7");

        Publish(client, server, "creature_fur", 1, 0, Data(Frame(60)));
        Assert.Single(repo.ListWorldTextures());

        server.Tick(3.0); // past the 2 s pacing
        inbox.Clear();
        Publish(client, server, "creature_hide", 2, 8, Data(Frame(60), Frame(61)));
        Assert.Single(repo.ListWorldTextures());
        Assert.Equal("@srv.worldtex.bad_animation", inbox.OfType<ActionRejected>().Single().Reason);
    }

    [Theory]
    [InlineData(2, 5)]  // not an offered speed
    [InlineData(9, 8)]  // too many frames
    [InlineData(0, 0)]
    public void BadAnimations_AreRefused(int frames, int fps)
    {
        var (server, client, repo, inbox) = Start("wt8_" + frames + "_" + fps);
        inbox.Clear();

        Publish(client, server, "campfire", frames, fps, Data(Frame(1)));

        Assert.Empty(repo.ListWorldTextures());
        Assert.Equal("@srv.worldtex.bad_animation", inbox.OfType<ActionRejected>().Single().Reason);
    }

    [Fact]
    public void DataThatDoesNotMatchTheFrameCount_IsRefused()
    {
        var (server, client, repo, inbox) = Start("wt9");
        inbox.Clear();

        Publish(client, server, "campfire", 4, 8, Data(Frame(1), Frame(2))); // announces 4, carries 2

        Assert.Empty(repo.ListWorldTextures());
        Assert.Equal("@srv.worldtex.bad_data", inbox.OfType<ActionRejected>().Single().Reason);
    }

    [Fact]
    public void Publishing_IsPaced_AndPublishingAgainReplaces()
    {
        var (server, client, repo, inbox) = Start("wt10");
        Publish(client, server, "stone", 1, 0, Data(Frame(10)));
        inbox.Clear();

        Publish(client, server, "dirt", 1, 0, Data(Frame(20))); // inside the 2 s window
        Assert.Equal("@srv.worldtex.too_fast", inbox.OfType<ActionRejected>().Single().Reason);
        Assert.Single(repo.ListWorldTextures());

        server.Tick(3.0);
        Publish(client, server, "stone", 1, 0, Data(Frame(99)));
        var stored = repo.ListWorldTextures().Single();
        Assert.True(WorldTextureCodec.TryDecode(stored.Data, 1, out var pixels));
        Assert.Equal(99, pixels[0][0]);
    }

    [Fact]
    public void Removing_BringsTheOfficialTextureBack()
    {
        var (server, client, repo, inbox) = Start("wt11");
        Publish(client, server, "stone", 1, 0, Data(Frame(10)));
        inbox.Clear();

        client.Send(NetCodec.Encode(new RemoveWorldTextureIntent { Key = "stone" }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        client.Poll();

        Assert.Empty(repo.ListWorldTextures());
        var wipe = inbox.OfType<WorldTextureData>().Single().Texture;
        Assert.Equal("stone", wipe.Key);
        Assert.Equal(string.Empty, wipe.Data);
    }

    [Fact]
    public void AnimatedTextures_ShareASlotBudget()
    {
        var (server, client, repo, inbox) = Start("wt12");
        string eight = Data(Enumerable.Range(0, 8).Select(i => Frame((byte)i)).ToArray());
        var blocks = _content.Blocks.Keys.OrderBy(k => k, StringComparer.Ordinal)
            .Take((WorldTextureCodec.AnimSlotBudget / 8) + 1).ToList();

        foreach (string key in blocks)
        {
            server.Tick(3.0);
            Publish(client, server, key, 8, 8, eight);
        }

        Assert.Equal(WorldTextureCodec.AnimSlotBudget / 8, repo.ListWorldTextures().Count);
        Assert.Contains(inbox.OfType<ActionRejected>(), r => r.Reason == "@srv.worldtex.anim_limit");
    }

    [Fact]
    public void AJoiningClient_GetsTheTextures_AndAlwaysAFinalPage()
    {
        var (server, client, repo, _) = Start("wt13");
        Publish(client, server, "stone", 1, 0, Data(Frame(10)));
        server.Tick(3.0);
        Publish(client, server, "dirt", 1, 0, Data(Frame(20)));

        // A second server over the same save: the registry restores, a newcomer gets it paged.
        var link = new LoopbackLink();
        var server2 = new SvGameServer(
            new ServerConfig { WorldName = "wt13", Seed = 1, AutoSaveIntervalMinutes = 9999, Rules = new GameRules() },
            _content, new LoopbackServerTransport(link), repo);
        server2.Start();
        var client2 = new LoopbackClientTransport(link);
        var inbox = new List<object>();
        client2.PayloadReceived += payload => { if (NetCodec.Decode(payload) is { } m) { inbox.Add(m); } };
        client2.Connect("loopback", 0);
        client2.Send(NetCodec.Encode(new JoinRequest { PlayerName = "Newcomer" }), DeliveryMode.ReliableOrdered);
        for (int i = 0; i < 4; i++)
        {
            server2.Tick(0.1);
            client2.Poll();
        }

        var pages = inbox.OfType<WorldTextureList>().ToList();
        Assert.NotEmpty(pages);
        Assert.True(pages[0].Reset);
        Assert.True(pages[pages.Count - 1].Final);
        Assert.Equal(new[] { "dirt", "stone" }, pages.SelectMany(p => p.Textures).Select(t => t.Key).OrderBy(k => k).ToArray());
    }

    [Fact]
    public void AWorldWithoutTextures_StillSendsOneEmptyFinalPage()
    {
        var (_, _, _, inbox) = Start("wt14");

        var page = inbox.OfType<WorldTextureList>().Single();
        Assert.True(page.Reset);
        Assert.True(page.Final);
        Assert.Empty(page.Textures);
    }

    [Fact]
    public void Pages_StayFarBelowTheMessageCap_OnTheJsonPath()
    {
        string eight = Data(Enumerable.Range(0, 8).Select(i => Frame((byte)(i * 31))).ToArray());
        var textures = Enumerable.Range(0, 40)
            .Select(i => new NetWorldTexture { Key = "k" + i, Frames = 8, Fps = 8, Data = eight, Owner = "x" }).ToList();

        var pages = SvGameServer.BuildWorldTexturePages(textures);

        Assert.True(pages.Count > 1);
        Assert.Equal(40, pages.Sum(p => p.Textures.Length));
        Assert.True(pages[0].Reset && pages[^1].Final);
        Assert.Single(pages.Where(p => p.Reset));
        Assert.Single(pages.Where(p => p.Final));
        foreach (var page in pages)
        {
            int json = NetCodec.EncodeJson(page).Length; // the uncompressed browser path is the worst case
            Assert.True(json < NetCodec.MaxJsonPayloadBytes / 2, $"page {page.Page} is {json} bytes on the JSON path");
        }
    }

    [Fact]
    public void SwitchingTheRuleOff_TellsEveryClientToDropThem()
    {
        var (server, client, _, inbox) = Start("wt15");
        Publish(client, server, "stone", 1, 0, Data(Frame(10)));
        inbox.Clear();

        client.Send(NetCodec.Encode(new SetWorldRulesIntent { WorldTextures = "Off" }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        server.Tick(0.1);
        client.Poll();

        var page = inbox.OfType<WorldTextureList>().Single();
        Assert.True(page.Reset && page.Final);
        Assert.Empty(page.Textures);
        Assert.Equal("Off", inbox.OfType<ServerRules>().Last().WorldTextures);
    }

    [Fact]
    public void TextureWipe_RemovesByKey_ByPlayer_OrAll()
    {
        var (server, client, repo, _) = Start("wt16");
        Publish(client, server, "stone", 1, 0, Data(Frame(10)));
        server.Tick(3.0);
        Publish(client, server, "dirt", 1, 0, Data(Frame(20)));

        client.Send(NetCodec.Encode(new AdminCommandIntent { Command = "texturewipe", StringArg = "stone" }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        Assert.Equal("dirt", repo.ListWorldTextures().Single().Key);

        client.Send(NetCodec.Encode(new AdminCommandIntent { Command = "texturewipe", StringArg = "Painter" }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        Assert.Empty(repo.ListWorldTextures());
    }

    // ── Repositories ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheMemorySnapshot_CarriesWorldTextures_AndOldSnapshotsReadAsNone()
    {
        var repo = new MemoryWorldRepository(new SaveGamePaths(_root, "wt_mem_a"));
        repo.SaveWorldTexture(new StoredWorldTexture { Key = "stone", Frames = 1, Data = Data(Frame(5)), OwnerName = "A", CreatedUnix = 7 });

        byte[] blob = repo.ExportSnapshotBlob();
        var restored = new MemoryWorldRepository(new SaveGamePaths(_root, "wt_mem_b"));
        restored.ImportSnapshotBlob(blob);
        Assert.Equal("stone", restored.ListWorldTextures().Single().Key);
        Assert.Equal(7, restored.ListWorldTextures().Single().CreatedUnix);

        restored.DeleteWorldTexture("stone");
        Assert.Empty(restored.ListWorldTextures());

        // A blob written before the feature has no such list — it must load as "no world textures", and an import
        // over a repository that already holds some must not keep the old ones.
        var old = new MemoryWorldRepository(new SaveGamePaths(_root, "wt_mem_c"));
        old.SaveWorldTexture(new StoredWorldTexture { Key = "dirt", Frames = 1, Data = "x" });
        old.ImportSnapshotBlob(WithoutProperty(blob, "WorldTextures"));
        Assert.Empty(old.ListWorldTextures());
    }

    private static byte[] WithoutProperty(byte[] gzipJson, string property)
    {
        using var input = new MemoryStream(gzipJson);
        using var gunzip = new System.IO.Compression.GZipStream(input, System.IO.Compression.CompressionMode.Decompress);
        var node = System.Text.Json.Nodes.JsonNode.Parse(gunzip)!.AsObject();
        Assert.True(node.Remove(property), $"the snapshot has no '{property}' property to remove");
        using var output = new MemoryStream();
        using (var gzip = new System.IO.Compression.GZipStream(output, System.IO.Compression.CompressionLevel.Fastest, leaveOpen: true))
        {
            using var writer = new System.Text.Json.Utf8JsonWriter(gzip);
            node.WriteTo(writer);
        }

        return output.ToArray();
    }

    [Fact]
    public void Sqlite_ReplacesByKey()
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "wt_sql"));
        repo.Initialize();
        repo.SaveWorldTexture(new StoredWorldTexture { Key = "stone", Frames = 1, Data = "a", OwnerName = "A" });
        repo.SaveWorldTexture(new StoredWorldTexture { Key = "stone", Frames = 2, Fps = 8, Data = "b", OwnerName = "B" });

        var one = repo.ListWorldTextures().Single();
        Assert.Equal(2, one.Frames);
        Assert.Equal("b", one.Data);
        Assert.Equal("B", one.OwnerName);
    }
}
