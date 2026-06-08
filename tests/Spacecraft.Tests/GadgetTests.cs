using System.IO;
using Spacecraft.Networking.Transport;
using Spacecraft.Persistence;
using Spacecraft.Shared.Configuration;
using Spacecraft.Shared.Content;
using Spacecraft.Shared.Geometry;
using Xunit;
using SvGameServer = Spacecraft.GameServer.GameServer;

namespace Spacecraft.Tests;

/// <summary>Right-click gadgets (item 36): the field medkit area heal, the stasis projector, the terrain
/// blaster — all reusable, suit-energy + cooldown gated.</summary>
public sealed class GadgetTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public GadgetTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "spacecraft_gadget_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Started(out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "gadget"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = "gadget", Seed = 3, StartPlanet = "rocky",
            AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false,
        };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    [Fact]
    public void FieldMedkit_HealsTheUserAndNearbyAllies_ButNotTheFarAway()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var user = server.AddLocalPlayer("Medic");
            var near = server.AddLocalPlayer("Buddy");
            var far = server.AddLocalPlayer("Stranger");

            user.State.Inventory.Add("field_medkit", 1, 1);
            user.State.Health = 40f;
            near.State.Health = 50f;
            far.State.Health = 50f;

            // Place the user + nearby ally together; the stranger well outside the heal radius (6 blocks).
            user.State.Position = new Vector3f(0f, 64f, 0f);
            near.State.Position = new Vector3f(2f, 64f, 1f);
            far.State.Position = new Vector3f(40f, 64f, 0f);

            server.UseGadgetForTest("Medic", "field_medkit", user.State.Position);

            Assert.True(user.State.Health > 40f, "the user heals themselves");
            Assert.True(near.State.Health > 50f, "a nearby ally is healed");
            Assert.Equal(50f, far.State.Health);                 // out of range — untouched
            Assert.True(user.State.SuitEnergy < 100f);           // use cost suit energy
            Assert.True(server.GadgetCooldownForTest("Medic", "field_medkit") > 0); // now on cooldown
        }
    }

    [Fact]
    public void TerrainBlaster_ClearsASphereOfTerrain_WithNoLoot()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Demo");
            p.State.AboardShip = false;
            p.State.Inventory.Add("terrain_blaster", 1, 1);

            var stone = _content.GetBlock("stone")!.NumericId;
            var center = new Vector3i(12, 40, 12);
            for (int dx = -2; dx <= 2; dx++)
            for (int dy = -2; dy <= 2; dy++)
            for (int dz = -2; dz <= 2; dz++)
            {
                server.World.SetBlock(new Vector3i(center.X + dx, center.Y + dy, center.Z + dz), stone);
            }

            int stoneBefore = p.State.Inventory.CountOf("stone");

            server.UseGadgetForTest("Demo", "terrain_blaster",
                new Vector3f(center.X + 0.5f, center.Y + 0.5f, center.Z + 0.5f));

            Assert.True(server.World.GetBlock(center).IsAir, "the blast clears the centre");
            Assert.True(server.World.GetBlock(new Vector3i(center.X + 1, center.Y, center.Z)).IsAir);
            Assert.Equal(stoneBefore, p.State.Inventory.CountOf("stone")); // a clearing blast yields no loot
        }
    }

    [Fact]
    public void FieldMedkit_DoesNothing_WithoutTheGadget()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var user = server.AddLocalPlayer("NoKit");
            user.State.Health = 30f;
            server.UseGadgetForTest("NoKit", "field_medkit", user.State.Position);
            Assert.Equal(30f, user.State.Health); // no gadget in inventory → rejected, no heal
        }
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { /* best effort */ }
    }
}
