// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Bio;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The glove weapons (#2278): the shock gloves push a creature, a planet machine or a bandit away and daze it for a
/// moment — swept through the target's own collision rules, so the push stops at a wall, a cliff edge and lava, and
/// never moves a pet or a giant; the energy gloves punch hard and fast. Every arena is a stone pad floating well above the
/// natural terrain, so the edges are real cliffs and nothing natural gets in the way.
/// </summary>
public sealed class GloveWeaponTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_gloves_" + Guid.NewGuid().ToString("N"));
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    private int _worlds;

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch { /* best effort */ }
    }

    private SvGameServer Started(out SqliteWorldRepository repo, string planet = "jungle", long seed = 4242,
        System.Action<GameRules>? rules = null, int terrainGeneration = 0)
    {
        string world = "gloves_" + (++_worlds); // every server its own save
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = world,
            Seed = seed,
            StartPlanet = planet,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = false,
            PlaceWrecks = false,
            PlaceBanditCamps = false,
            World = { TerrainGeneration = terrainGeneration },
        };
        rules?.Invoke(config.Rules);
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    private const int PadMinX = -10, PadMaxX = 12, PadHalfZ = 4;

    /// <summary>A stone pad floating eight blocks above every natural top around the origin (x −10..12, z −4..4), the
    /// player standing on it at (0.5, pad + 1, 0.5) with <paramref name="weapon"/> in hand and a full suit battery.
    /// Returns the player and the pad's feet level (pad + 1).</summary>
    private (PlayerSession Player, int Feet) Pad(SvGameServer server, string weapon, string name = "Justus")
    {
        var stone = _content.GetBlock("stone")!.NumericId;
        int top = int.MinValue;
        for (int x = PadMinX - 4; x <= PadMaxX + 4; x += 2)
            for (int z = -PadHalfZ - 4; z <= PadHalfZ + 4; z += 2)
            {
                top = System.Math.Max(top, SurfaceTopY(server, x, z));
            }

        int padY = top + 8;
        for (int x = PadMinX; x <= PadMaxX; x++)
            for (int z = -PadHalfZ; z <= PadHalfZ; z++)
            {
                server.World.SetBlock(new Vector3i(x, padY, z), stone);
                for (int y = padY + 1; y <= padY + 6; y++)
                {
                    server.World.SetBlock(new Vector3i(x, y, z), BlockId.Air);
                }
            }

        var p = server.AddLocalPlayer(name);
        p.State.AboardShip = false;
        p.State.Position = new Vector3f(0.5f, padY + 1, 0.5f);
        p.State.SuitEnergy = 100f;
        p.State.Inventory.SetSlot(0, new ItemStack(weapon, 1));
        p.State.SelectedHotbarSlot = 0;
        return (p, padY + 1);
    }

    private static int SurfaceTopY(SvGameServer server, int x, int z)
    {
        for (int y = 200; y > -200; y--)
        {
            if (!server.World.GetBlock(new Vector3i(x, y, z)).IsAir)
            {
                return y;
            }
        }

        return 0;
    }

    /// <summary>The smallest ordinary walker of the world's roster (no giant, no lurking arachnid, no special body).</summary>
    private static CreatureSpecies Walker(SvGameServer server)
        => server.SpeciesRoster
            .Where(sp => CreatureMotion.ClassOf(sp) == MotionClass.Walker && !CreatureMotion.IsGiant(sp)
                && sp.BodyPlan == CreatureBodyPlan.Standard && !ArachnidRules.Lurks(sp))
            .OrderBy(sp => sp.Size)
            .First();

    /// <summary>A wild walker planted on the pad at x = <paramref name="x"/>, with a big hull so a hit never kills it.</summary>
    private static CombatEntity Animal(SvGameServer server, int feet, float x, out CreatureSpecies species)
    {
        species = Walker(server);
        string id = server.SpawnCreatureAtForTest(new Vector3f(x, feet, 0.5f), species.Id);
        var c = server.Creatures.First(e => e.Id == id);
        c.HullMax = 200f;
        c.Hull = 200f;
        return c;
    }

    private static bool BodyClear(SvGameServer server, Vector3f at)
    {
        int x = (int)System.Math.Floor(at.X), y = (int)System.Math.Floor(at.Y), z = (int)System.Math.Floor(at.Z);
        return server.World.GetBlock(new Vector3i(x, y, z)).IsAir && server.World.GetBlock(new Vector3i(x, y + 1, z)).IsAir;
    }

    // ---------------- the data and the rules ----------------

    [Fact]
    public void BothGloves_AreHeldOnBothHands_WithTheirDecidedStats()
    {
        var shock = _content.GetItem("shock_gloves")!;
        var energy = _content.GetItem("energy_gloves")!;
        Assert.True(HeldGrips.IsGloves(shock));
        Assert.True(HeldGrips.IsGloves(energy));
        Assert.False(HeldGrips.IsGloves(_content.GetItem("machete")));

        var s = shock.Tool!;
        Assert.Equal((ToolKind.Weapon, 1, 3f, 3.5f, 1.2f, 0.5f), (s.Kind, s.Tier, s.Damage, s.Range, s.CooldownSeconds, s.EnergyPerUse));
        Assert.Equal(5f, s.Knockback);
        Assert.Equal(1f, s.StaggerSeconds);
        Assert.Equal(FxStyles.ShockPush, s.Fx!.Style);

        var e = energy.Tool!;
        Assert.Equal((ToolKind.Weapon, 2, 22f, 3f, 0.5f, 0.25f), (e.Kind, e.Tier, e.Damage, e.Range, e.CooldownSeconds, e.EnergyPerUse));
        Assert.Equal(0f, e.Knockback);
        Assert.Equal(FxStyles.EnergyFist, e.Fx!.Style);
    }

    [Fact]
    public void TheGloves_AreSiblings_TheEnergyGlovesNeedTheShockBlueprint_ButNotTheShockGloves()
    {
        var energyBlueprint = _content.GetBlueprint("energy_gloves")!;
        Assert.Contains("shock_gloves", energyBlueprint.Prerequisites);
        Assert.Equal("Weapon", energyBlueprint.Category);
        Assert.Equal("Weapon", _content.GetBlueprint("shock_gloves")!.Category);

        var recipe = _content.Recipes.Values.Single(r => r.Outputs.Any(o => o.Item == "energy_gloves"));
        Assert.DoesNotContain(recipe.Inputs, i => i.Item == "shock_gloves");
        Assert.Equal(CraftingStation.Workshop, recipe.Station);
    }

    [Fact]
    public void Mass_FullForSmallAnimals_HalfAtSizeFour_ATitanStillMovesABlock_AGiantNever()
    {
        Assert.Equal(1f, KnockbackRules.CreatureMass(1f, giant: false));
        Assert.Equal(1f, KnockbackRules.CreatureMass(2f, giant: false));
        Assert.Equal(0.5f, KnockbackRules.CreatureMass(4f, giant: false), 3);
        Assert.Equal(KnockbackRules.MinCreatureMass, KnockbackRules.CreatureMass(40f, giant: false));
        Assert.Equal(0f, KnockbackRules.CreatureMass(1f, giant: true));
        Assert.True(KnockbackRules.CreatureMass(3f, false) >= KnockbackRules.CreatureMass(5f, false)); // heavier = shorter

        Assert.Equal(10, KnockbackRules.Steps(5f));
        Assert.Equal(0, KnockbackRules.Steps(0f));
        Assert.Equal(0.5f, KnockbackRules.StepLength(1.2f, 1));
        Assert.Equal(0.2f, KnockbackRules.StepLength(1.2f, 3), 3);
    }

    [Fact]
    public void Gloves_BioLabChange_KeepsAndScalesKnockback()
    {
        var shock = _content.GetItem("shock_gloves")!;
        var stronger = new ItemMods(ModStat.Power, 2, ModStat.None, 0, ModStat.Energy, 1);
        var changed = ToolMods.Effective(shock, stronger.ApplyTo("shock_gloves"))!;
        Assert.NotSame(shock.Tool, changed);
        Assert.Equal(shock.Tool!.Knockback * (1f + (2 * ToolMods.PowerPerLevel)), changed.Knockback, 3);
        Assert.Equal(shock.Tool.StaggerSeconds, changed.StaggerSeconds);

        var weaker = new ItemMods(ModStat.Cooldown, 2, ModStat.None, 0, ModStat.Power, 2);
        var slower = ToolMods.Apply(shock.Tool, weaker);
        Assert.True(slower.Knockback < shock.Tool.Knockback, "a power drawback shortens the push like the damage");
        Assert.True(slower.Knockback > 0f);
    }

    // ---------------- the push ----------------

    [Fact]
    public void ShockGloves_PushCreatureAwayFromPlayer()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var (p, feet) = Pad(server, "shock_gloves");
            var c = Animal(server, feet, 2.5f, out var sp);
            float expected = 5f * KnockbackRules.CreatureMass(sp.Size, giant: false);

            server.AttackEntity(p.State.PlayerId, c.Id);

            Assert.Equal(200f - 3f, c.Hull, 3);                // the shock gloves barely hurt
            Assert.Equal(2.5f + expected, c.Position.X, 2);   // straight away from the player, the full distance
            Assert.Equal(0.5f, c.Position.Z, 3);
            Assert.True(server.IsStaggeredForTest(c.Id), "the push dazes it");
            Assert.True(server.NetCreatureForTest(c.Id).Staggered, "the dizzy look rides on the creature list");
            Assert.Equal(100f - 0.5f, p.State.SuitEnergy, 3);

            server.Tick(1.1);
            Assert.False(server.IsStaggeredForTest(c.Id), "the daze lasts one second");
            Assert.False(server.NetCreatureForTest(c.Id).Staggered);
        }
    }

    [Fact]
    public void ShockGloves_PushStopsAtAWall_NeverInsideABlock()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var (p, feet) = Pad(server, "shock_gloves");
            var stone = _content.GetBlock("stone")!.NumericId;
            const int wallX = 6;
            for (int z = -PadHalfZ; z <= PadHalfZ; z++)
                for (int y = feet; y < feet + 3; y++)
                {
                    server.World.SetBlock(new Vector3i(wallX, y, z), stone);
                }

            var c = Animal(server, feet, 2.5f, out _);
            server.AttackEntity(p.State.PlayerId, c.Id);

            Assert.True(c.Position.X > 2.6f, $"it was pushed toward the wall (x {c.Position.X:F2})");
            Assert.True(c.Position.X < wallX, $"never through the wall (x {c.Position.X:F2})");
            Assert.True(BodyClear(server, c.Position), "never inside a block");
            Assert.False(server.CreatureStepBlockedForTest(c.Id, c.Position), "its own spot is a valid place to stand");
        }
    }

    [Fact]
    public void ShockGloves_DoNotPushOffACliffOrIntoLava()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var (p, feet) = Pad(server, "shock_gloves");

            // The pad's edge is a cliff of eight blocks and more: the push stops on the pad.
            var c = Animal(server, feet, 8.5f, out _);
            p.State.Position = new Vector3f(6.5f, feet, 0.5f);
            server.AttackEntity(p.State.PlayerId, c.Id);
            Assert.True(c.Position.X > 8.6f, "it was pushed toward the edge");
            Assert.True(c.Position.X < PadMaxX + 1, $"never over the edge (x {c.Position.X:F2})");
            server.Tick(1.5);
            Assert.True(c.Position.Y >= feet - 0.01f, $"it did not fall (y {c.Position.Y:F2}, pad feet {feet})");

            // A strip of lava across the pad: the push stops before it.
            var lava = _content.GetBlock("lava")!.NumericId;
            for (int x = 5; x <= 7; x++)
                for (int z = -PadHalfZ; z <= PadHalfZ; z++)
                {
                    server.World.SetBlock(new Vector3i(x, feet - 1, z), lava);
                }

            var d = Animal(server, feet, 2.5f, out _);
            p.State.Position = new Vector3f(0.5f, feet, 0.5f);
            server.Tick(1.3); // past the gloves' cooldown
            d.Position = new Vector3f(2.5f, feet, 0.5f);
            server.AttackEntity(p.State.PlayerId, d.Id);
            Assert.True(d.Position.X > 2.6f, "it was pushed toward the lava");
            Assert.True(d.Position.X < 5f, $"never onto the lava (x {d.Position.X:F2})");
        }
    }

    [Fact]
    public void ShockGloves_StaggeredCreatureDoesNotBite()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var (p, feet) = Pad(server, "shock_gloves");
            var c = Animal(server, feet, 2.5f, out _);
            c.DamagePerSecond = 20f;
            c.ProvokeTimer = 60.0; // angry: it would bite anyone in reach

            server.AttackEntity(p.State.PlayerId, c.Id);
            Assert.True(server.IsStaggeredForTest(c.Id));

            c.Position = new Vector3f(1.5f, feet, 0.5f); // right beside the player, dazed
            float health = p.State.Health;
            server.Tick(0.5);
            Assert.Equal(health, p.State.Health);

            // The same animal without the daze bites at once — the hold above was the daze, nothing else.
            c.StaggerUntil = 0.0;
            c.Position = new Vector3f(1.5f, feet, 0.5f);
            server.Tick(0.2);
            Assert.True(p.State.Health < health, "an undazed angry animal in reach bites");
        }
    }

    [Fact]
    public void ShockGloves_StaggeredMachineAuraIsSilent()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var (p, feet) = Pad(server, "shock_gloves");
            server.SpawnPlanetEnemyAtForTest(new Vector3f(2.5f, feet, 0.5f), CombatEntityKind.Creature, damagePerSecond: 20f);
            var robot = server.PlanetEnemies.Last();

            server.AttackEntity(p.State.PlayerId, robot.Id);
            Assert.Equal(30f - 3f, robot.Hull, 3);
            Assert.Equal(7.5f, robot.Position.X, 2); // a plain robot weighs like a small animal: five blocks
            Assert.True(server.IsStaggeredForTest(robot.Id));

            robot.Position = new Vector3f(1.5f, feet, 0.5f);
            float health = p.State.Health;
            server.Tick(0.5);
            Assert.Equal(health, p.State.Health);
            Assert.Equal(1.5f, robot.Position.X, 3); // and it did not move

            robot.StaggerUntil = 0.0;
            server.Tick(0.2);
            Assert.True(p.State.Health < health, "the aura of an undazed machine bites");
        }
    }

    [Fact]
    public void ShockGloves_PushesBandit_CountsAsRefusal()
    {
        var server = Started(out var repo, planet: "rocky", seed: 9, rules: r =>
        {
            r.PlanetEnemies = AlienActivity.Off;
            r.Bandits = AlienActivity.Normal;
        });
        using (repo)
        {
            var (p, feet) = Pad(server, "shock_gloves", name: "Mark");
            p.State.Inventory.SetSlot(1, new ItemStack("iron_ore", 100)); // something worth a hold-up
            server.SpawnBanditAtForTest(new Vector3f(3.5f, feet, 0.5f), "Mark");
            server.Tick(0.1);
            Assert.NotEqual(0, server.PendingBanditDemandIdForTest("Mark"));
            var bandit = Assert.Single(server.Bandits);
            var before = bandit.Position;

            server.AttackEntity("Mark", bandit.Id);

            Assert.True(bandit.Hostile);
            Assert.Equal(BanditPhase.Fighting, bandit.BanditPhase);
            Assert.Equal(0, server.PendingBanditDemandIdForTest("Mark")); // the push answered the hold-up
            Assert.Equal(before.X + (5f * KnockbackRules.BanditMass), bandit.Position.X, 2);
            Assert.True(server.IsStaggeredForTest(bandit.Id));

            var dazedAt = bandit.Position;
            server.Tick(0.5);
            Assert.Equal(dazedAt.X, bandit.Position.X, 3); // a dazed bandit does not come back yet
            Assert.Equal(dazedAt.Z, bandit.Position.Z, 3);
        }
    }

    [Fact]
    public void ShockGloves_StaggerImmunityWindow()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var (p, feet) = Pad(server, "shock_gloves");
            var c = Animal(server, feet, 2.5f, out _);

            server.AttackEntity(p.State.PlayerId, c.Id);
            Assert.True(server.IsStaggeredForTest(c.Id));

            server.Tick(1.3); // the daze (1 s) and the cooldown (1.2 s) are over, the immunity (1.5 s after the daze) is not
            Assert.False(server.IsStaggeredForTest(c.Id));
            c.Position = new Vector3f(2.5f, feet, 0.5f);
            server.AttackEntity(p.State.PlayerId, c.Id);
            Assert.True(c.Position.X > 2.6f, "inside the window the push still works");
            Assert.False(server.IsStaggeredForTest(c.Id), "but there is no second daze");

            server.Tick(1.3); // 2.6 s after the first daze ended: the window is over
            c.Position = new Vector3f(2.5f, feet, 0.5f);
            server.AttackEntity(p.State.PlayerId, c.Id);
            Assert.True(server.IsStaggeredForTest(c.Id), "after the window it can be dazed again");
        }
    }

    [Fact]
    public void ShockGloves_NeverPushCompanions()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var (p, feet) = Pad(server, "shock_gloves");
            var pet = Animal(server, feet, 2.5f, out _);
            pet.OwnerId = p.State.PlayerId;
            var at = pet.Position;

            server.AttackEntity(p.State.PlayerId, pet.Id);
            Assert.Equal(at, pet.Position);
            Assert.Equal(200f, pet.Hull);
            Assert.False(server.IsStaggeredForTest(pet.Id));

            // Even straight through the knockback rule, past the attack's own refusal.
            Assert.False(server.KnockbackForTest(p.State.PlayerId, pet.Id));
            pet.OwnerId = "npc:7"; // a tamer NPC's pet neither
            Assert.False(server.KnockbackForTest(p.State.PlayerId, pet.Id));
            Assert.Equal(at, pet.Position);
            Assert.False(server.IsStaggeredForTest(pet.Id));
        }
    }

    [Fact]
    public void ShockGloves_GiantIsNotMoved()
    {
        var server = Started(out var repo, planet: "salt_flats", terrainGeneration: WorldDescription.GiantsGeneration);
        using (repo)
        {
            var p = server.AddLocalPlayer("Justus");
            p.State.AboardShip = false;
            p.State.Position = new Vector3f(0.5f, SurfaceTopY(server, 0, 0) + 1, 0.5f);
            p.State.Inventory.SetSlot(0, new ItemStack("shock_gloves", 1));
            p.State.SelectedHotbarSlot = 0;
            p.State.SuitEnergy = 100f;
            server.SummonGiantForTest("Justus", "colossus");
            var giant = server.Creatures.Single(c => c.IsGiant);
            var at = giant.Position;

            Assert.False(server.KnockbackForTest("Justus", giant.Id));
            Assert.Equal(at, giant.Position);
            Assert.False(server.IsStaggeredForTest(giant.Id));
        }
    }

    [Fact]
    public void ShockGloves_NoEnergy_NoPushNoDamage()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var (p, feet) = Pad(server, "shock_gloves");
            var c = Animal(server, feet, 2.5f, out _);
            p.State.SuitEnergy = 0.2f; // less than one push (0.5)

            server.AttackEntity(p.State.PlayerId, c.Id);

            Assert.Equal(2.5f, c.Position.X, 3);
            Assert.Equal(200f, c.Hull);
            Assert.False(server.IsStaggeredForTest(c.Id));
            Assert.Equal(0.2f, p.State.SuitEnergy, 3);
        }
    }

    [Fact]
    public void EnergyGloves_DamageCooldownAndEnergy()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var (p, feet) = Pad(server, "energy_gloves");
            var c = Animal(server, feet, 2.5f, out _);

            server.AttackEntity(p.State.PlayerId, c.Id);
            Assert.Equal(200f - 22f, c.Hull, 3);
            Assert.Equal(100f - 0.25f, p.State.SuitEnergy, 3);
            Assert.Equal(2.5f, c.Position.X, 3); // the energy gloves hit, they do not push
            Assert.False(server.IsStaggeredForTest(c.Id));

            server.AttackEntity(p.State.PlayerId, c.Id); // inside the 0.5 s cooldown — held back
            Assert.Equal(200f - 22f, c.Hull, 3);
            Assert.Equal(100f - 0.25f, p.State.SuitEnergy, 3);

            server.Tick(0.6);
            c.Position = new Vector3f(2.5f, feet, 0.5f);
            server.AttackEntity(p.State.PlayerId, c.Id);
            Assert.Equal(200f - 44f, c.Hull, 3);
            Assert.Equal(100f - 0.5f, p.State.SuitEnergy, 3);
        }
    }

    [Fact]
    public void EnergyGloves_OutpunchTheMachete_ButStayBelowThePlasmaSword()
    {
        static float PerSecond(ToolProperties t) => t.Damage / t.CooldownSeconds;
        var energy = _content.GetItem("energy_gloves")!.Tool!;
        Assert.True(PerSecond(energy) > PerSecond(_content.GetItem("machete")!.Tool!));
        Assert.True(PerSecond(energy) < PerSecond(_content.GetItem("plasma_sword")!.Tool!));
        Assert.True(PerSecond(_content.GetItem("shock_gloves")!.Tool!) < PerSecond(_content.GetItem("machete")!.Tool!),
            "the shock gloves are for pushing, not for fighting");
    }
}
