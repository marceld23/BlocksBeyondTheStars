using System.Linq;
using Spacecraft.Networking.Transport;
using Spacecraft.Persistence;
using Spacecraft.Shared.Configuration;
using Spacecraft.Shared.Content;
using Spacecraft.Shared.Definitions;
using Spacecraft.Shared.Geometry;
using Spacecraft.Shared.World;
using Spacecraft.WorldGeneration;
using Xunit;
using SvGameServer = Spacecraft.GameServer.GameServer;

namespace Spacecraft.Tests;

/// <summary>
/// Procedural surface flora (World systems): worldgen seeds it on suitable surfaces, harvesting
/// drops the species material, and a harvested plant regrows on its cell as long as its host
/// block underneath survives. Growth is bounded — one plant per host cell, never spreading.
/// </summary>
public sealed class FloraTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public FloraTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "spacecraft_flora_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Started(out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "flora"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig { WorldName = "flora", Seed = 7, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    [Fact]
    public void AirlessBodies_HaveNoFloraOrFauna()
    {
        // Landable asteroids + airless moons/planets are barren: no flora, no fauna.
        foreach (var key in new[] { "asteroid", "lava", "crystal" })
        {
            var planet = _content.GetPlanet(key);
            Assert.NotNull(planet);
            Assert.True(planet!.IsAirless, $"{key} should be airless");
            Assert.Empty(FloraGenerator.GenerateRoster(planet, 123));
            Assert.Empty(CreatureGenerator.GenerateRoster(planet, 123));
        }

        // A breathable world still teems with life.
        var jungle = _content.GetPlanet("jungle");
        Assert.NotNull(jungle);
        Assert.NotEmpty(FloraGenerator.GenerateRoster(jungle!, 123));
        Assert.NotEmpty(CreatureGenerator.GenerateRoster(jungle!, 123));
    }

    private static ushort BlockAboveSurface(WorldGenerator gen, PlanetType planet, int x, int z)
    {
        int surfaceY = gen.SurfaceHeight(planet, x, z);
        int y = surfaceY + 1; // flora grows in the air cell directly above the surface
        var coord = WorldConstants.WorldToChunk(new Vector3i(x, y, z));
        var origin = WorldConstants.ChunkOrigin(coord);
        var chunk = gen.Generate(planet, coord);
        return chunk.Get(x - origin.X, y - origin.Y, z - origin.Z).Value;
    }

    [Fact]
    public void FloraBlocksAndSeeds_Exist()
    {
        Assert.True(_content.Blocks.ContainsKey("flora_plant"));
        Assert.True(_content.Blocks.ContainsKey("flora_crystal"));
        // Normal harvest yields the species material (fibre / crystal), not seeds.
        Assert.Equal("plant_fiber", _content.GetBlock("flora_plant")!.Drops[0].Item);
        // Seeds are separate items the player can place to replant.
        Assert.True(_content.Items.ContainsKey("plant_seed"));
        Assert.True(_content.Items.ContainsKey("crystal_seed"));
        Assert.Equal("flora_plant", _content.GetItem("plant_seed")!.PlacesBlock);
    }

    /// <summary>All flora block ids in this content set (from the shared catalog).</summary>
    private HashSet<ushort> FloraIds()
    {
        var set = new HashSet<ushort>();
        foreach (var sp in Spacecraft.Shared.Definitions.FloraCatalog.All)
        {
            if (_content.GetBlock(sp.Key) is { } b)
            {
                set.Add(b.NumericId.Value);
            }
        }

        return set;
    }

    [Fact]
    public void FloraRoster_NamesEveryArchetype_IsDeterministic_AndHasToxicVariety()
    {
        var planet = _content.GetPlanet("jungle")!; // floraDensity > 0 → a real roster
        var a = FloraGenerator.GenerateRoster(planet, 4242);
        var b = FloraGenerator.GenerateRoster(planet, 4242);

        Assert.NotEmpty(a);
        Assert.Equal(a.Count, FloraCatalog.All.Count); // one species per archetype
        for (int i = 0; i < a.Count; i++)
        {
            Assert.False(string.IsNullOrWhiteSpace(a[i].Name), "every flora species is named");
            Assert.Equal(a[i].Name, b[i].Name);     // deterministic from seed + planet
            Assert.Equal(a[i].Toxic, b[i].Toxic);
            Assert.Equal(a[i].BlockKey, b[i].BlockKey);
        }

        // A world has both edible and toxic plants (not all one classification).
        Assert.Contains(a, s => s.Toxic);
        Assert.Contains(a, s => !s.Toxic);
    }

    [Fact]
    public void FloraRoster_IsEmpty_OnABarrenWorld()
    {
        var planet = _content.GetPlanet("asteroid")!; // no flora
        Assert.Empty(FloraGenerator.GenerateRoster(planet, 4242));
    }

    [Fact]
    public void FloraRoster_ActivatesASubset_ButKeepsFullCoverage()
    {
        var planet = _content.GetPlanet("jungle")!;
        var roster = FloraGenerator.GenerateRoster(planet, 4242);

        // Subsetting: a world grows some flora forms and not others.
        Assert.Contains(roster, s => s.Active);
        Assert.Contains(roster, s => !s.Active);

        // Coverage: every land host surface keeps at least one active land species (no bare biome) …
        var landHosts = FloraCatalog.All.Where(s => !s.Aquatic).SelectMany(s => s.Hosts).Distinct();
        foreach (var host in landHosts)
        {
            Assert.Contains(roster, s => s.Active && !s.Aquatic
                && FloraCatalog.All.First(c => c.Key == s.BlockKey).Hosts.Contains(host));
        }

        // … and the seas keep at least one active aquatic species.
        Assert.Contains(roster, s => s.Active && s.Aquatic);
    }

    [Fact]
    public void ToxicFlora_YieldsToxicBerries_WhileEdibleFloraYieldsNormalBerries()
    {
        // toxic_berries is a real, harmful consumable.
        var tb = _content.GetItem("toxic_berries");
        Assert.NotNull(tb);
        Assert.True(tb!.ConsumeHealth < 0, "toxic berries must harm when eaten");

        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "floratox"));
        using (repo)
        {
            var st = new LoopbackServerTransport(new LoopbackLink());
            var config = new ServerConfig
            {
                WorldName = "floratox", Seed = 7, AutoSaveIntervalMinutes = 9999,
                PlaceStarterShip = false, StartPlanet = "jungle", // a flora world
            };
            var server = new SvGameServer(config, _content, st, repo);
            server.Start();

            var sp = server.FloraSpeciesForBlock("flora_bush"); // flora_bush yields berries
            Assert.NotNull(sp);

            var p = server.AddLocalPlayer("Forager");
            p.State.Position = new Vector3f(0.5f, 132f, 0.5f);
            var pos = new Vector3i(0, 130, 0);
            server.World.SetBlock(pos, _content.GetBlock("flora_bush")!.NumericId);

            int berries = p.State.Inventory.CountOf("berries");
            int toxic = p.State.Inventory.CountOf("toxic_berries");
            server.MineBlock("Forager", pos.X, pos.Y, pos.Z);

            if (sp!.Toxic)
            {
                Assert.True(p.State.Inventory.CountOf("toxic_berries") > toxic, "a toxic plant should drop toxic berries");
                Assert.Equal(berries, p.State.Inventory.CountOf("berries"));
            }
            else
            {
                Assert.True(p.State.Inventory.CountOf("berries") > berries, "an edible plant should drop normal berries");
                Assert.Equal(toxic, p.State.Inventory.CountOf("toxic_berries"));
            }
        }
    }

    [Fact]
    public void Worldgen_SeedsFlora_OnAFloraPlanet()
    {
        var planet = _content.GetPlanet("jungle")!; // grass surface, floraDensity 0.14
        var gen = new WorldGenerator(2026, _content);
        var floraIds = FloraIds();

        int found = 0;
        for (int x = 0; x < 48; x++)
        for (int z = 0; z < 48; z++)
        {
            if (floraIds.Contains(BlockAboveSurface(gen, planet, x, z)))
            {
                found++;
            }
        }

        Assert.True(found > 0, "Expected a flora planet to seed at least one flora block across a 48x48 area.");
    }

    [Fact]
    public void Worldgen_PlacesNoFlora_OnABarrenPlanet()
    {
        var planet = _content.GetPlanet("rocky")!; // no floraDensity → barren
        var gen = new WorldGenerator(2026, _content);
        var floraIds = FloraIds();

        for (int x = 0; x < 24; x++)
        for (int z = 0; z < 24; z++)
        {
            Assert.DoesNotContain(BlockAboveSurface(gen, planet, x, z), floraIds);
        }
    }

    [Fact]
    public void CanPlantFlora_RequiresSuitableHostBelow()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var mud = _content.GetBlock("mud")!.NumericId;
            var stone = _content.GetBlock("stone")!.NumericId;

            // Plant flora needs grass/dirt/mud below.
            server.World.SetBlock(new Vector3i(50, 49, 50), mud);
            Assert.True(server.CanPlantFlora("flora_plant", 50, 50, 50));

            server.World.SetBlock(new Vector3i(52, 49, 50), stone);
            Assert.False(server.CanPlantFlora("flora_plant", 52, 50, 50)); // stone is no host for a plant

            // Crystal flora needs crystal/stone/basalt below.
            Assert.True(server.CanPlantFlora("flora_crystal", 52, 50, 50));
            Assert.False(server.CanPlantFlora("flora_crystal", 50, 50, 50)); // mud is no host for a crystal
        }
    }

    [Fact]
    public void HarvestedFlora_RegrowsWhenHostIntact()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var session = server.AddLocalPlayer("Botanist");
            session.State.Position = new Vector3f(100f, 100f, 100f);

            var mud = _content.GetBlock("mud")!.NumericId;
            var floraPlant = _content.GetBlock("flora_plant")!.NumericId;
            var host = new Vector3i(102, 99, 100);
            var cell = new Vector3i(102, 100, 100);
            server.World.SetBlock(host, mud);
            server.World.SetBlock(cell, floraPlant);

            server.MineBlock(session.State.PlayerId, cell.X, cell.Y, cell.Z);
            Assert.True(server.World.GetBlock(cell).IsAir, "Flora should be gone right after harvest.");

            server.Tick(31.0); // > FloraRegrowSeconds → regrow step
            Assert.Equal(floraPlant.Value, server.World.GetBlock(cell).Value);
        }
    }

    [Fact]
    public void HarvestedFlora_DoesNotRegrowWhenHostRemoved()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var session = server.AddLocalPlayer("Botanist");
            session.State.Position = new Vector3f(100f, 100f, 100f);

            var mud = _content.GetBlock("mud")!.NumericId;
            var floraPlant = _content.GetBlock("flora_plant")!.NumericId;
            var host = new Vector3i(102, 99, 100);
            var cell = new Vector3i(102, 100, 100);
            server.World.SetBlock(host, mud);
            server.World.SetBlock(cell, floraPlant);

            server.MineBlock(session.State.PlayerId, cell.X, cell.Y, cell.Z);
            server.MineBlock(session.State.PlayerId, host.X, host.Y, host.Z); // dig up the ground

            server.Tick(31.0);
            Assert.True(server.World.GetBlock(cell).IsAir, "Without its host the plant must not regrow.");
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
