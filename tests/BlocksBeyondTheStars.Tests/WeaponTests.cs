using System.Linq;
using BlocksBeyondTheStars.Networking.Transport;
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
/// Craftable player weapons: melee (short reach, high damage) and ranged (long reach, draws suit
/// energy). They flow through the shared AttackEntity path — the held weapon's range gates reach
/// and its damage/energy decide the hit.
/// </summary>
public sealed class WeaponTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public WeaponTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_weapon_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Started(out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "weapon"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = "weapon",
            Seed = 4242,
            StartPlanet = "jungle",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
        };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    private static void Equip(PlayerState p, string weapon)
    {
        p.Inventory.SetSlot(0, new ItemStack(weapon, 1));
        p.SelectedHotbarSlot = 0;
    }

    [Fact]
    public void Weapons_HaveExpectedToolStats()
    {
        var machete = _content.GetItem("machete")!;
        Assert.Equal(ToolKind.Weapon, machete.Tool!.Kind);
        Assert.True(machete.Tool.Damage > 0f);
        Assert.True(machete.Tool.Range is > 0f and < 6f); // melee = short reach

        var laser = _content.GetItem("laser_pistol")!;
        Assert.Equal(ToolKind.Weapon, laser.Tool!.Kind);
        Assert.True(laser.Tool.Range >= 20f);    // ranged
        Assert.True(laser.Tool.EnergyPerUse > 0f); // energy weapon
    }

    [Fact]
    public void RangedWeapon_HitsBeyondMeleeReach_ButMeleeCannot()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Gunner");
            p.State.AboardShip = false;
            p.State.Position = new Vector3f(0, 64, 0);
            p.State.SuitEnergy = 100f;

            server.Tick(6.0);
            var creature = server.Creatures.First();
            creature.Position = new Vector3f(0, 64, 12); // 12 blocks away — out of melee range

            // Melee can't reach that far → no damage, creature intact.
            Equip(p.State, "machete");
            float maxHull = creature.HullMax;
            server.AttackEntity("Gunner", creature.Id);
            Assert.Contains(server.Creatures, c => c.Id == creature.Id);
            Assert.Equal(maxHull, server.Creatures.First(c => c.Id == creature.Id).Hull);

            // Ranged reaches it; a few shots kill it and suit energy is spent.
            Equip(p.State, "laser_pistol");
            for (int i = 0; i < 4 && server.Creatures.Any(c => c.Id == creature.Id); i++)
            {
                server.AttackEntity("Gunner", creature.Id);
            }

            Assert.DoesNotContain(server.Creatures, c => c.Id == creature.Id);
            Assert.True(p.State.SuitEnergy < 100f, "Energy weapon should consume suit energy.");
        }
    }

    [Fact]
    public void Machete_HitsCreature_WithinDefaultReach_EvenBeyondItsShortRange()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Slasher");
            p.State.AboardShip = false;
            p.State.Position = new Vector3f(0, 64, 0);

            server.Tick(6.0);
            var creature = server.Creatures.First();
            creature.Position = new Vector3f(0, 64, 5); // 5 blocks: past the machete's 3.5 range, within the 6 swing reach
            Equip(p.State, "machete");

            float before = creature.Hull;
            server.AttackEntity("Slasher", creature.Id);

            // Equipping a melee weapon must never make you worse than fists: the swing lands within the
            // default reach even though the machete's own range is short.
            bool hit = server.Creatures.All(c => c.Id != creature.Id)
                       || server.Creatures.First(c => c.Id == creature.Id).Hull < before;
            Assert.True(hit, "The machete should hit a creature within the default swing reach.");
        }
    }

    [Fact]
    public void EnergyWeapon_RejectsFireWhenSuitEnergyEmpty()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Gunner");
            p.State.AboardShip = false;
            p.State.Position = new Vector3f(0, 64, 0);

            server.Tick(6.0);
            var creature = server.Creatures.First();
            creature.Position = new Vector3f(0, 64, 5); // within laser range
            float maxHull = creature.HullMax;

            Equip(p.State, "laser_pistol");
            p.State.SuitEnergy = 0.5f; // less than the laser's per-shot cost (1.0)
            server.AttackEntity("Gunner", creature.Id);

            Assert.Contains(server.Creatures, c => c.Id == creature.Id);
            Assert.Equal(maxHull, server.Creatures.First(c => c.Id == creature.Id).Hull);
            Assert.Equal(0.5f, p.State.SuitEnergy); // nothing spent
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
