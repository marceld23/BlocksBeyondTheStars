using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>Fire (item 30): lava ignites flora/wood, fire spreads + burns down to ash, water extinguishes it.</summary>
public sealed class FireTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public FireTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_fire_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Started(out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "fire"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig { WorldName = "fire", Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    private ushort Id(string key) => _content.GetBlock(key)!.NumericId.Value;

    private static void Tick(SvGameServer server, int steps)
    {
        for (int i = 0; i < steps; i++)
        {
            server.Tick(0.25);
        }
    }

    [Fact]
    public void Fire_BlocksAndItems_Exist()
    {
        Assert.NotNull(_content.GetBlock("fire"));
        Assert.NotNull(_content.GetBlock("ash"));
        Assert.False(_content.GetBlock("fire")!.Solid);     // fire is non-solid (walk through it)
        Assert.False(_content.GetBlock("fire")!.Mineable);  // can't be mined
        Assert.True(_content.GetBlock("fire")!.Emission > 0); // glows
    }

    [Fact]
    public void Ignited_Flora_BurnsDownToAsh()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var pos = new Vector3i(120, 80, 120);
            server.World.SetBlock(pos, _content.GetBlock("flora_bush")!.NumericId);
            server.IgniteForTest(pos.X, pos.Y, pos.Z);
            Assert.Equal(Id("fire"), server.World.GetBlock(pos).Value); // caught fire

            Tick(server, 20); // ~5 s — past the burn time
            Assert.Equal(Id("ash"), server.World.GetBlock(pos).Value); // burned out to ash
        }
    }

    [Fact]
    public void Fire_SpreadsToAdjacentFlammable()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var a = new Vector3i(140, 80, 140);
            var b = new Vector3i(141, 80, 140);
            server.World.SetBlock(a, _content.GetBlock("tree_leaves")!.NumericId);
            server.World.SetBlock(b, _content.GetBlock("tree_leaves")!.NumericId);

            server.IgniteForTest(a.X, a.Y, a.Z);
            Tick(server, 1); // one fire tick → it ignites its flammable neighbour
            Assert.Equal(Id("fire"), server.World.GetBlock(b).Value);
        }
    }

    [Fact]
    public void Water_ExtinguishesFire()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var pos = new Vector3i(160, 80, 160);
            server.World.SetBlock(pos, _content.GetBlock("flora_fern")!.NumericId);
            server.IgniteForTest(pos.X, pos.Y, pos.Z);
            Assert.Equal(Id("fire"), server.World.GetBlock(pos).Value);

            // A water block right next to the flame douses it back to air (not ash).
            server.World.SetBlock(new Vector3i(pos.X + 1, pos.Y, pos.Z), _content.GetBlock("water")!.NumericId);
            Tick(server, 1);
            Assert.True(server.World.GetBlock(pos).IsAir, "water should extinguish fire to air");
        }
    }

    [Fact]
    public void Lava_IgnitesAdjacentFlora()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var flora = new Vector3i(180, 80, 180);
            server.World.SetBlock(flora, _content.GetBlock("flora_vine")!.NumericId);
            server.PlaceFluidSource("lava", flora.X + 1, flora.Y, flora.Z); // lava right beside it

            Tick(server, 4); // the active lava ignites its flammable neighbour
            var v = server.World.GetBlock(flora).Value;
            Assert.True(v == Id("fire") || v == Id("ash"), $"lava should ignite the flora (got {v})");
        }
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch { }
    }
}
