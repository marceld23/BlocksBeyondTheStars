// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
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
            World = { TerrainGeneration = 0 }, // #1645: gameplay test on the classic relief — the player sits at a fixed (0, 64, 0), which generation-1 terrain may flood or bury
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

        // #2278: the glove weapons — melee reach, suit energy per hit; only the shock gloves push and daze.
        foreach (var key in new[] { "shock_gloves", "energy_gloves" })
        {
            var gloves = _content.GetItem(key)!;
            Assert.Equal(ToolKind.Weapon, gloves.Tool!.Kind);
            Assert.True(gloves.Tool.Damage > 0f, $"{key}: damage 0 would fall back to the tier default");
            Assert.True(gloves.Tool.Range is > 0f and < 6f, $"{key}: melee = short reach");
            Assert.True(gloves.Tool.EnergyPerUse > 0f, $"{key}: every hit draws suit energy");
            Assert.True(gloves.Tool.CooldownSeconds > 0f, $"{key}: a cooldown of its own");
        }

        Assert.True(_content.GetItem("shock_gloves")!.Tool!.Knockback > 0f);
        Assert.True(_content.GetItem("shock_gloves")!.Tool!.StaggerSeconds > 0f);
        Assert.Equal(0f, _content.GetItem("energy_gloves")!.Tool!.Knockback);
        Assert.Equal(0f, machete.Tool.Knockback); // every other weapon stays push-free
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
            // The weakest animal — this test is about weapon REACH, not time-to-kill, and the first
            // spawn can now be a titan (#638) that outlasts both the shot budget and the suit energy.
            var creature = server.Creatures.OrderBy(c => c.HullMax).First();
            creature.Position = new Vector3f(0, 64, 12); // 12 blocks away — out of melee range

            // Melee can't reach that far → no damage, creature intact.
            Equip(p.State, "machete");
            float maxHull = creature.HullMax;
            server.AttackEntity("Gunner", creature.Id);
            Assert.Contains(server.Creatures, c => c.Id == creature.Id);
            Assert.Equal(maxHull, server.Creatures.First(c => c.Id == creature.Id).Hull);

            // Ranged reaches it; a few shots kill it and suit energy is spent.
            Equip(p.State, "laser_pistol");
            for (int i = 0; i < 8 && server.Creatures.Any(c => c.Id == creature.Id); i++)
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

    // ---------------- #2280: the bare hand is the weakest option ----------------

    [Fact]
    public void BareHand_Punches5_AndAPunchWithinItsCooldownIsHeldBack()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Boxer");
            p.State.AboardShip = false;
            p.State.Position = new Vector3f(0, 64, 0);
            p.State.Inventory.SetSlot(0, null); // nothing in hand
            p.State.SelectedHotbarSlot = 0;

            server.Tick(6.0);
            var creature = server.Creatures.First(c => !c.IsGiant && !c.IsCompanion);
            creature.HullMax = 50f;
            creature.Hull = 50f;

            creature.Position = new Vector3f(0, 64, 3);
            server.AttackEntity("Boxer", creature.Id);
            Assert.Equal(50f - MeleeRules.FistDamage, creature.Hull, 3);

            creature.Position = new Vector3f(0, 64, 3);
            server.AttackEntity("Boxer", creature.Id); // right away — inside the 1.2 s cooldown
            Assert.Equal(50f - MeleeRules.FistDamage, creature.Hull, 3);

            server.Tick(0.5);
            Assert.Contains(server.Creatures, c => c.Id == creature.Id);
            creature.Position = new Vector3f(0, 64, 3);
            server.AttackEntity("Boxer", creature.Id); // 0.5 s after the punch — far too early, even with jitter
            Assert.Equal(50f - MeleeRules.FistDamage, creature.Hull, 3);

            // 1.15 s after the punch: 0.05 s early, inside the server's jitter slack — it lands.
            server.Tick(0.65);
            Assert.Contains(server.Creatures, c => c.Id == creature.Id);
            creature.Position = new Vector3f(0, 64, 3);
            server.AttackEntity("Boxer", creature.Id);
            Assert.Equal(50f - (2 * MeleeRules.FistDamage), creature.Hull, 3);

            server.Tick(MeleeRules.FistCooldownSeconds + 0.1);
            Assert.Contains(server.Creatures, c => c.Id == creature.Id);
            creature.Position = new Vector3f(0, 64, 3);
            float before = creature.Hull;
            server.AttackEntity("Boxer", creature.Id);
            Assert.Equal(before - MeleeRules.FistDamage, creature.Hull, 3);
        }
    }

    [Fact]
    public void BareHand_IsWeakerPerSecondThanTheMachete()
    {
        var machete = _content.GetItem("machete")!.Tool!;
        float macheteCooldown = machete.CooldownSeconds > 0f ? machete.CooldownSeconds : 1.5f;
        Assert.True(MeleeRules.FistDamage / MeleeRules.FistCooldownSeconds < machete.Damage / macheteCooldown,
            "the first crafted weapon must always beat the bare hand");

        // Even a client that punches on the very edge of the server's jitter slack stays below the machete.
        float fastestFist = MeleeRules.FistCooldownSeconds - MeleeRules.FistJitterToleranceSeconds;
        Assert.True(MeleeRules.FistDamage / fastestFist < machete.Damage / macheteCooldown,
            "the jitter slack must not make the fist outpunch the machete");
    }

    // ---------------- #2281: companions and pets cannot be attacked ----------------

    [Fact]
    public void Companions_AndPets_CannotBeAttacked_ButAWildCreatureStillCan()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var owner = server.AddLocalPlayer("Owner");
            var stranger = server.AddLocalPlayer("Stranger");
            foreach (var s in new[] { owner, stranger })
            {
                s.State.AboardShip = false;
                s.State.Inventory.SetSlot(0, null);
                s.State.SelectedHotbarSlot = 0;
            }

            owner.State.Position = new Vector3f(0, 64, 0);
            stranger.State.Position = new Vector3f(1, 64, 0);

            for (int i = 0; i < 20 && server.Creatures.Count(c => !c.IsGiant && !c.IsCompanion) < 2; i++)
            {
                server.Tick(1.0);
            }

            var wildOnes = server.Creatures.Where(c => !c.IsGiant && !c.IsCompanion).Take(2).ToList();
            Assert.Equal(2, wildOnes.Count);
            var pet = wildOnes[0];
            var wild = wildOnes[1];
            pet.OwnerId = "Owner"; // tamed by Owner
            pet.HullMax = 50f;
            pet.Hull = 50f;

            // Neither the owner nor another player can hurt it.
            foreach (var attacker in new[] { "Owner", "Stranger" })
            {
                pet.Position = new Vector3f(0, 64, 3);
                server.AttackEntity(attacker, pet.Id);
                Assert.Equal(50f, pet.Hull);
            }

            // A tamer NPC's pet neither.
            pet.OwnerId = "npc:7";
            pet.Position = new Vector3f(0, 64, 3);
            server.AttackEntity("Stranger", pet.Id);
            Assert.Equal(50f, pet.Hull);

            // A wild creature is still fair game — and the refused swings spent no cooldown.
            wild.HullMax = 50f;
            wild.Hull = 50f;
            wild.Position = new Vector3f(1, 64, 3);
            server.AttackEntity("Stranger", wild.Id);
            Assert.True(wild.Hull < 50f, "a wild creature is still hit");
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
