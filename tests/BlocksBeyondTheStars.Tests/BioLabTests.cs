// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Bio;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.State;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;
using SvSession = BlocksBeyondTheStars.GameServer.PlayerSession;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The bio lab through the real server (#2201–#2209): a harvest also yields a sample, the lab analyses and mixes, a
/// preparation starts an effect that acts on vitals and combat, a tool is changed and stays in its tier, the clone
/// tank grows an animal of another world and crosses two, and a bred plant grows, is harvested and comes back.
/// </summary>
public sealed class BioLabTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public BioLabTests()
    {
        _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bbts_bio_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        try
        {
            System.IO.Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // best effort
        }
    }

    private SvGameServer NewServer(string planet = "jungle", string world = "bio")
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        var config = new ServerConfig { WorldName = world, Seed = 77, StartPlanet = planet, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        var server = new SvGameServer(config, _content, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        return server;
    }

    /// <summary>A player high up in the air (empty cells, everything in reach) with the given items.</summary>
    private static SvSession Player(SvGameServer server, string name, params string[] items)
    {
        var p = server.AddLocalPlayer(name);
        p.State.AboardShip = false;
        p.State.Position = new Vector3f(0, 200, 0);
        p.State.SuitEnergy = 100f;
        for (int i = 0; i < p.State.Inventory.SlotCount; i++)
        {
            p.State.Inventory.SetSlot(i, null); // no starter kit: a test counts exactly what it hands out and harvests
        }

        foreach (string key in items)
        {
            Assert.Equal(0, p.State.Inventory.Add(key, 1, 1));
        }

        return p;
    }

    private BlockId Block(string key) => _content.GetBlock(key)!.NumericId;

    private void PutLab(SvGameServer server) => server.World.SetBlock(new Vector3i(2, 200, 0), Block(BioItems.Lab));

    private static void Ticks(SvGameServer server, double seconds, double step = 0.5)
    {
        for (double t = 0; t < seconds; t += step)
        {
            server.TickForTest(step);
        }
    }

    private static int Samples(SvSession p, uint seed, bool mineral = false)
        => p.State.SampleCase.CountOf(ItemKey.WithSeed(mineral ? BioItems.MineralSample : BioItems.Sample, seed));

    private static string? FirstItem(SvSession p, string baseKey)
        => p.State.Inventory.Slots.FirstOrDefault(s => s is { IsEmpty: false } && ItemKey.Base(s.Item) == baseKey)?.Item;

    // ---------------- Samples ----------------

    [Fact]
    public void AHarvest_AlsoYieldsASample_AndTheYieldIsWhatItAlwaysWas()
    {
        var server = NewServer();
        var p = Player(server, "Botanist");
        var host = new Vector3i(3, 199, 0);
        var cell = new Vector3i(3, 200, 0);
        server.World.SetBlock(host, Block("dirt"));
        server.World.SetBlock(cell, Block("flora_bush"));

        server.MineBlock("Botanist", cell.X, cell.Y, cell.Z);

        // The bush still gives its fibre and its berries (the poisonous twin on a world where it is toxic).
        Assert.Equal(1, p.State.Inventory.CountOf("plant_fiber"));
        Assert.Equal(2, p.State.Inventory.CountOf("berries") + p.State.Inventory.CountOf("toxic_berries"));

        // And one sample of its species sits in the sample case — not in the backpack.
        var sample = Assert.Single(p.State.SampleCase.Slots.Where(s => s is { IsEmpty: false }));
        Assert.Equal(BioItems.Sample, ItemKey.Base(sample!.Item));
        Assert.Equal(1, sample.Count);
        Assert.DoesNotContain(p.State.Inventory.Slots, s => s is { IsEmpty: false } && ItemKey.Base(s.Item) == BioItems.Sample);

        var entry = server.BioEntryForTest(ItemKey.Seed(sample.Item));
        Assert.NotNull(entry);
        Assert.Equal(BioKind.Plant, entry!.Kind);
        Assert.Equal("flora_bush", entry.Flora?.BodyBlock);
        Assert.Equal(server.ActiveLocationId, entry.OriginBodyId);
        Assert.NotNull(server.BioProfileForTest(entry.Seed));
    }

    [Fact]
    public void ADefeatedAnimal_YieldsASample_AndSoDoesTheSamplerWithoutHarm()
    {
        var server = NewServer();
        var p = Player(server, "Ranger", BioItems.Sampler);
        var sp = server.SpeciesRoster.First(s => !s.Hostile && !s.IsGiant);
        uint seed = server.BioSeedForTest(sp);

        // The sampler: a sample, and the animal lives.
        string alive = server.SpawnCreatureAtForTest(new Vector3f(1, 200, 0), sp.Id);
        server.UseGadgetForTest("Ranger", BioItems.Sampler, new Vector3f(1, 200, 0));
        Assert.Equal(1, Samples(p, seed));
        Assert.Contains(server.Creatures, c => c.Id == alive);

        // The same animal gives no second sample for a few minutes.
        Ticks(server, 3.0);
        p.State.Position = server.Creatures.First(c => c.Id == alive).Position;
        server.UseGadgetForTest("Ranger", BioItems.Sampler, p.State.Position);
        Assert.Equal(1, Samples(p, seed));

        // Defeating one yields a sample with its loot.
        var target = server.Creatures.First(c => c.Id == alive);
        p.State.Position = target.Position;
        for (int i = 0; i < 12 && server.Creatures.Any(c => c.Id == alive); i++)
        {
            server.AttackEntity("Ranger", alive);
        }

        Assert.DoesNotContain(server.Creatures, c => c.Id == alive);
        Assert.Equal(2, Samples(p, seed));
        Assert.Equal(BioKind.Animal, server.BioEntryForTest(seed)!.Kind);
    }

    [Fact]
    public void TheSampler_RefusesAHostileAnimal_UnlessItIsFrozen()
    {
        var server = NewServer();
        var p = Player(server, "Ranger", BioItems.Sampler);
        var hostile = server.SpeciesRoster.FirstOrDefault(s => s.Hostile && !s.IsGiant);
        if (hostile is null)
        {
            return; // this world rolled no hostile species
        }

        p.State.GodMode = true; // it would bite
        server.SpawnCreatureAtForTest(new Vector3f(1, 200, 0), hostile.Id);
        server.UseGadgetForTest("Ranger", BioItems.Sampler, new Vector3f(1, 200, 0));
        Assert.Equal(0, Samples(p, server.BioSeedForTest(hostile)));
    }

    [Fact]
    public void Ore_YieldsAMineralSample_OnlyFromADepositTheWorldMade()
    {
        var server = NewServer();
        var p = Player(server, "Miner", "basic_drill");
        Assert.Equal(0, p.State.Inventory.Add("crystal", 4, 1024));
        server.World.SetBlock(new Vector3i(1, 200, 0), Block("iron_ore"));

        server.MineBlock("Miner", 1, 200, 0);

        Assert.Equal(1, p.State.Inventory.CountOf("iron_ore")); // the ore itself is plain and stacks as ever
        var sample = Assert.Single(p.State.SampleCase.Slots.Where(s => s is { IsEmpty: false }));
        Assert.Equal(BioItems.MineralSample, ItemKey.Base(sample!.Item));
        var deposit = server.BioEntryForTest(ItemKey.Seed(sample.Item))!;
        Assert.Equal(BioKind.Mineral, deposit.Kind);
        Assert.Equal("iron_ore", deposit.MaterialItem);

        // A crystal a player set down is no deposit: mining it gives the crystal back and no sample.
        server.PlaceBlock("Miner", 1, 201, 0, "crystal");
        Assert.Equal("crystal", _content.BlockById(server.World.GetBlock(new Vector3i(1, 201, 0)))?.Key);
        server.MineBlock("Miner", 1, 201, 0);
        Assert.Single(p.State.SampleCase.Slots.Where(s => s is { IsEmpty: false }));
    }

    // ---------------- The lab ----------------

    [Fact]
    public void Analysing_NeedsALab_UsesOneSample_AndPaysKnowledgeOnce()
    {
        var server = NewServer();
        var p = Player(server, "Scholar");
        uint seed = server.GiveFloraSampleForTest(p, "flora_bush", 3);
        var analyse = new BioLabIntent { Action = BioLabIntent.Analyse, Sample = seed };

        server.BioLabForTest(p, analyse); // no lab in reach
        Assert.False(server.BioAnalysedForTest("Scholar", seed));
        Assert.Equal(3, Samples(p, seed));

        PutLab(server);
        int before = p.State.KnowledgePoints;
        server.BioLabForTest(p, analyse);
        Assert.True(server.BioAnalysedForTest("Scholar", seed));
        Assert.Equal(2, Samples(p, seed));
        int gained = p.State.KnowledgePoints - before;
        Assert.True(gained >= 2);

        server.BioLabForTest(p, analyse); // already known: nothing is used up, nothing is paid
        Assert.Equal(2, Samples(p, seed));
        Assert.Equal(before + gained, p.State.KnowledgePoints);
    }

    [Fact]
    public void AnExtract_NeedsNoBlueprint_TheFullMixerDoes()
    {
        var server = NewServer();
        var p = Player(server, "Scholar");
        PutLab(server);
        uint seed = server.GiveFloraSampleForTest(p, "flora_fern", 4);
        Assert.Equal(0, p.State.Inventory.Add("water", 4, 1024));

        // One sample alone: an extract, as an injector.
        server.BioLabForTest(p, new BioLabIntent { Action = BioLabIntent.Mix, Sample = seed });
        string? extract = FirstItem(p, "prep_injector");
        Assert.NotNull(extract);
        Assert.Equal(3, Samples(p, seed));
        Assert.Equal(server.BioProfileForTest(seed)!.Effect, BioItems.CompoundOf(extract)!.Effect);

        // A carrier is the full mixer: refused until Synthesis is researched, and nothing is used up.
        var withCarrier = new BioLabIntent { Action = BioLabIntent.Mix, Sample = seed, Carrier = "plant_fiber" };
        Assert.Equal(0, p.State.Inventory.Add("plant_fiber", 2, 1024));
        server.BioLabForTest(p, withCarrier);
        Assert.Null(FirstItem(p, "prep_gel"));
        Assert.Equal(3, Samples(p, seed));

        p.State.UnlockedBlueprints.Add(BioItems.SynthesisBlueprint);
        server.BioLabForTest(p, withCarrier);
        string? gel = FirstItem(p, "prep_gel");
        Assert.NotNull(gel);
        Assert.Equal(1, p.State.Inventory.CountOf("plant_fiber"));
        Assert.Equal(2, Samples(p, seed));
        Assert.True(BioItems.CompoundOf(gel)!.Level <= BioItems.CompoundOf(extract)!.Level);
        Assert.True(BioItems.CompoundOf(gel)!.DurationSeconds > BioItems.CompoundOf(extract)!.DurationSeconds);
    }

    // ---------------- Effects ----------------

    private static string Prep(BioEffect effect, int level, int seconds = 120, BioForm form = BioForm.Injector)
        => BioItems.PreparationItem(new Compound { Form = form, Effect = effect, Level = level, DurationSeconds = seconds });

    [Fact]
    public void APreparation_StartsItsEffect_AWeakerRepeatAndAFourthAreRefused()
    {
        var server = NewServer();
        var p = Player(server, "Taster", Prep(BioEffect.Speed, 8), Prep(BioEffect.Speed, 3), Prep(BioEffect.Jump, 5),
            Prep(BioEffect.Grip, 5), Prep(BioEffect.Mining, 5));

        server.ConsumeItem("Taster", Prep(BioEffect.Speed, 8));
        var running = Assert.Single(p.State.Effects);
        Assert.Equal(BioEffect.Speed, running.Effect);
        Assert.Equal(120f, running.SecondsLeft, 1);
        Assert.Equal(0, p.State.Inventory.CountOf(Prep(BioEffect.Speed, 8)));

        server.ConsumeItem("Taster", Prep(BioEffect.Speed, 3)); // weaker: refused, kept
        Assert.Single(p.State.Effects);
        Assert.Equal(1, p.State.Inventory.CountOf(Prep(BioEffect.Speed, 3)));

        server.ConsumeItem("Taster", Prep(BioEffect.Jump, 5));
        server.ConsumeItem("Taster", Prep(BioEffect.Grip, 5));
        Assert.Equal(3, p.State.Effects.Count);

        server.ConsumeItem("Taster", Prep(BioEffect.Mining, 5)); // a fourth: refused, kept
        Assert.Equal(3, p.State.Effects.Count);
        Assert.Equal(1, p.State.Inventory.CountOf(Prep(BioEffect.Mining, 5)));

        Ticks(server, 121.0);
        Assert.Empty(p.State.Effects); // it ends on time
    }

    [Fact]
    public void ACoating_IsNotTaken()
    {
        var server = NewServer();
        string coating = Prep(BioEffect.Strength, 8, form: BioForm.Coating);
        var p = Player(server, "Taster", coating);
        server.ConsumeItem("Taster", coating);
        Assert.Empty(p.State.Effects);
        Assert.Equal(1, p.State.Inventory.CountOf(coating));
    }

    [Fact]
    public void Effects_ActOnMining_Harvest_Hunger_AndHostiles()
    {
        var server = NewServer();
        var p = Player(server, "Tester");

        // Mining: coral rock (hardness 1.2) takes two hits by hand — one with a strong mining effect.
        var rock = new Vector3i(1, 200, 0);
        server.World.SetBlock(rock, Block("coral_rock"));
        server.MineBlockOnce("Tester", rock.X, rock.Y, rock.Z);
        Assert.False(server.World.GetBlock(rock).IsAir);
        server.World.SetBlock(new Vector3i(1, 201, 0), Block("coral_rock"));
        server.StartEffectForTest(p, BioEffect.Mining, 15, 600);
        server.MineBlockOnce("Tester", 1, 201, 0);
        Assert.True(server.World.GetBlock(new Vector3i(1, 201, 0)).IsAir);

        // Gathering: a harvested plant yields more.
        server.StartEffectForTest(p, BioEffect.Gathering, 15, 600);
        server.World.SetBlock(new Vector3i(3, 199, 0), Block("dirt"));
        server.World.SetBlock(new Vector3i(3, 200, 0), Block("flora_fern"));
        server.MineBlock("Tester", 3, 200, 0);
        Assert.Equal(2 + 2, p.State.Inventory.CountOf("plant_fiber")); // the fern's two, and two more

        // Stealth: hostiles do not notice the player while it lasts.
        Assert.False(p.State.IgnoredByHostiles);
        server.StartEffectForTest(p, BioEffect.Stealth, 5, 30);
        Assert.True(p.State.IgnoredByHostiles);

        // Hunger: satiety slows the drain, a "hungry" side effect speeds it up.
        var q = Player(server, "Other");
        var r = Player(server, "Third");
        server.StartEffectForTest(q, BioEffect.Satiety, 15, 600);
        server.StartEffectForTest(r, BioEffect.Jump, 5, 600, BioSideEffect.Hungry, 3);
        foreach (var s in new[] { p, q, r })
        {
            s.State.Hunger = 100f;
        }

        Ticks(server, 30.0);
        Assert.True(q.State.Hunger > p.State.Hunger, "satiety must slow the drain");
        Assert.True(r.State.Hunger < p.State.Hunger, "a hungry side effect must speed it up");
    }

    [Fact]
    public void TheShield_TakesAHitFirst_AndGoesWithItsEffect()
    {
        var server = NewServer();
        var p = Player(server, "Faller", Prep(BioEffect.Shield, 15, seconds: 60));
        server.ConsumeItem("Faller", Prep(BioEffect.Shield, 15, seconds: 60));
        Assert.Equal(30f, p.State.Shield, 1);

        p.State.Health = 100f;
        server.FallDamageForTest("Faller", 17f); // a little over the safe landing speed: about 13 damage
        Assert.Equal(100f, p.State.Health, 1);
        Assert.True(p.State.Shield < 30f && p.State.Shield > 0f);

        Ticks(server, 61.0);
        Assert.Equal(0f, p.State.Shield);
    }

    // ---------------- Changing tools and gear ----------------

    [Fact]
    public void AChangedDrill_MinesHarder_StaysInItsTier_AndWashesOff()
    {
        var server = NewServer();
        var p = Player(server, "Smith", "titanium_drill");
        PutLab(server);
        uint tungsten = server.GiveMineralSampleForTest(p, "tungsten_ore", "tungsten_ore", 2);
        var change = new BioLabIntent { Action = BioLabIntent.Change, TargetItem = "titanium_drill", Stabiliser = tungsten };

        server.BioLabForTest(p, change); // needs the blueprint
        Assert.Equal(1, p.State.Inventory.CountOf("titanium_drill"));

        p.State.UnlockedBlueprints.Add(BioItems.TuningBlueprint);
        server.BioLabForTest(p, change);
        string changed = p.State.Inventory.Slots[0]!.Item; // in place: the drill stays in its slot
        Assert.StartsWith("titanium_drill#u", changed);
        Assert.Equal(1, Samples(p, tungsten, mineral: true));

        var plain = _content.GetItem("titanium_drill")!.Tool!;
        var tool = server.ActiveToolForTest(p);
        Assert.True(tool.MiningPower > plain.MiningPower);
        Assert.True(tool.MiningPower < _content.GetItem("diamond_drill")!.Tool!.MiningPower);
        Assert.Equal(plain.Tier, tool.Tier);
        Assert.Equal(plain.MiningRadius, tool.MiningRadius);

        server.BioLabForTest(p, new BioLabIntent { Action = BioLabIntent.WashOff, TargetItem = changed });
        Assert.Equal("titanium_drill", p.State.Inventory.Slots[0]!.Item);
        Assert.Equal(plain.MiningPower, server.ActiveToolForTest(p).MiningPower);
    }

    [Fact]
    public void APlainMaterial_WorksToo_WithoutAnOrigin()
    {
        var server = NewServer();
        var p = Player(server, "Smith", "titanium_drill");
        PutLab(server);
        p.State.UnlockedBlueprints.Add(BioItems.TuningBlueprint);
        Assert.Equal(0, p.State.Inventory.Add("steel", 2, 1024));

        server.BioLabForTest(p, new BioLabIntent { Action = BioLabIntent.Change, TargetItem = "titanium_drill", MaterialItem = "steel" });

        Assert.StartsWith("titanium_drill#u", p.State.Inventory.Slots[0]!.Item);
        Assert.Equal(1, p.State.Inventory.CountOf("steel"));
    }

    [Fact]
    public void ChangedGear_StillWorksWhenWorn_AndAnUpgradeRecipeTakesIt()
    {
        var server = NewServer();
        var p = Player(server, "Climber");
        string gloves = new ItemMods(ModStat.Grip, 2, ModStat.None, 0, ModStat.None, 0).ApplyTo("climbing_gloves");
        Assert.Equal(0, p.State.Inventory.Add(gloves, 1, 1));
        Assert.Equal(0, p.State.Inventory.Add("titanium_plate", 2, 1024));
        Assert.Equal(0, p.State.Inventory.Add("iron_plate", 2, 1024));
        p.State.UnlockedBlueprints.Add("climbing_claws");
        server.World.SetBlock(new Vector3i(1, 200, 0), Block("workbench"));

        server.Craft("Climber", "climbing_claws");

        Assert.Equal(1, p.State.Inventory.CountOf("climbing_claws"));
        Assert.Equal(0, p.State.Inventory.CountOf(gloves));
    }

    // ---------------- The clone tank ----------------

    private SvSession TankOwner(SvGameServer server)
    {
        var p = Player(server, "Keeper", "clone_tank");
        Assert.Equal(0, p.State.Inventory.Add("matter_dust", 64, 1024));
        server.PlaceBlock("Keeper", 1, 200, 0, "clone_tank");
        return p;
    }

    private static readonly Vector3i Tank = new(1, 200, 0);

    [Fact]
    public void TheTank_GrowsAnAnimalOfAnotherWorld_FromASample()
    {
        var server = NewServer();
        var p = TankOwner(server);
        var sp = server.SpeciesRoster.First(s => !s.Hostile && !s.IsGiant && s.Habitat is CreatureHabitat.Land or CreatureHabitat.Air);
        uint seed = server.RegisterForeignCreatureForTest(sp, "elsewhere");
        Assert.True(server.GiveSampleForTest(p, seed, 2));
        string choice = SvGameServer.TankChoiceForTest(seed);

        Assert.Contains(server.CloneableSpeciesFor("Keeper"), c => c.SpeciesId == choice);

        server.SetCrystalDeviceForTest(p, Tank, action: 2, mode: 0, config: "sp=" + choice);
        server.SetCrystalDeviceForTest(p, Tank, action: 1);
        Assert.True(server.CrystalDeviceOutput(Tank));
        Assert.Equal(1, Samples(p, seed));                               // one sample …
        Assert.Equal(62, p.State.Inventory.CountOf("matter_dust"));     // … and two matter dust, no bait

        Ticks(server, CrystalNetRules.CloneGrowSeconds + 1.0);
        var clone = server.Creatures.FirstOrDefault(c => c.CloneOf.Length > 0);
        Assert.NotNull(clone);
        Assert.StartsWith("gx", clone!.SpeciesId);                      // a guest of this world
        Assert.False(clone.Hostile);
    }

    [Fact]
    public void TheTank_NeverGrowsAHostileAnimal_AndAWaterAnimalNeedsWater()
    {
        var server = NewServer();
        var p = TankOwner(server);

        var hostile = server.SpeciesRoster.FirstOrDefault(s => s.Hostile && !s.IsGiant);
        if (hostile is not null)
        {
            uint seed = server.RegisterForeignCreatureForTest(hostile, "elsewhere");
            server.GiveSampleForTest(p, seed, 1);
            server.SetCrystalDeviceForTest(p, Tank, action: 2, mode: 0, config: "sp=" + SvGameServer.TankChoiceForTest(seed));
            server.SetCrystalDeviceForTest(p, Tank, action: 1);
            Assert.False(server.CrystalDeviceOutput(Tank));
            Assert.Equal(1, Samples(p, seed));
        }

        var swimmer = server.SpeciesRoster.FirstOrDefault(s => !s.Hostile && !s.IsGiant && s.Habitat == CreatureHabitat.Water);
        if (swimmer is not null)
        {
            uint seed = server.RegisterForeignCreatureForTest(swimmer, "elsewhere");
            server.GiveSampleForTest(p, seed, 1);
            server.SetCrystalDeviceForTest(p, Tank, action: 2, mode: 0, config: "sp=" + SvGameServer.TankChoiceForTest(seed));
            server.SetCrystalDeviceForTest(p, Tank, action: 1); // up in the air: no water anywhere near
            Assert.False(server.CrystalDeviceOutput(Tank));
            Assert.Equal(1, Samples(p, seed));
        }
    }

    [Fact]
    public void ACross_IsTheSameChildEveryTime_NeverHostile_AndComesWithASample()
    {
        var server = NewServer();
        var p = TankOwner(server);
        var parents = server.SpeciesRoster.Where(s => !s.IsGiant && s.Habitat is CreatureHabitat.Land or CreatureHabitat.Air or CreatureHabitat.Cave).Take(2).ToList();
        Assert.Equal(2, parents.Count);
        uint a = server.GiveCreatureSampleForTest(p, parents[0], 2);
        uint b = server.GiveCreatureSampleForTest(p, parents[1], 2);
        uint childSeed = BioHash.CrossSeed(a, b);
        string config = "sp=" + parents[0].Id + ";x=" + parents[1].Id;

        server.SetCrystalDeviceForTest(p, Tank, action: 2, mode: 0, config: config);
        server.SetCrystalDeviceForTest(p, Tank, action: 1); // needs the Crossing blueprint
        Assert.False(server.CrystalDeviceOutput(Tank));

        p.State.UnlockedBlueprints.Add(BioItems.CrossingBlueprint);
        server.SetCrystalDeviceForTest(p, Tank, action: 1);
        Assert.True(server.CrystalDeviceOutput(Tank));
        Assert.Equal(1, Samples(p, a));
        Assert.Equal(1, Samples(p, b));
        Assert.Equal(60, p.State.Inventory.CountOf("matter_dust")); // four for a cross

        Ticks(server, CrystalNetRules.CloneGrowSeconds + 1.0);
        var child = server.BioEntryForTest(childSeed);
        Assert.NotNull(child);
        Assert.Equal(1, child!.Generation);
        Assert.False(child.Creature!.Hostile);
        Assert.Equal(0f, child.Creature.AttackDamage);
        Assert.Contains(child.Creature.BodyPlan, new[] { parents[0].BodyPlan, parents[1].BodyPlan });
        Assert.Contains(server.Creatures, c => c.CloneOf.Length > 0 && c.SpeciesId.StartsWith("gx", StringComparison.Ordinal));
        Assert.Equal(1, Samples(p, childSeed)); // a sample of the new species, to clone and cross again
        Assert.NotNull(server.BioProfileForTest(childSeed));

        // The other way round is the very same child.
        Assert.Same(child, server.CrossForTest(b, a));
    }

    [Fact]
    public void EveryPairOfBodyPlans_GivesABodyOfOneParent_ThatIsNeverHostile()
    {
        var plans = Enum.GetValues(typeof(CreatureBodyPlan)).Cast<CreatureBodyPlan>()
            .Where(plan => plan is not (CreatureBodyPlan.Colossus or CreatureBodyPlan.Sandworm or CreatureBodyPlan.Leviathan or CreatureBodyPlan.SkyGiant))
            .ToList();
        CreatureSpecies Of(CreatureBodyPlan plan, int i) => new()
        {
            Id = "sp" + i,
            Name = "Parent " + plan,
            BodyPlan = plan,
            Habitat = plan is CreatureBodyPlan.Medusa or CreatureBodyPlan.Ray ? CreatureHabitat.Air : CreatureHabitat.Land,
            Temperament = i % 2 == 0 ? CreatureTemperament.Aggressive : CreatureTemperament.Skittish,
            AttackDamage = i % 2 == 0 ? 6f : 0f,
            Size = 0.6f + i * 0.15f,
            Speed = 1.5f + i * 0.2f,
            Legs = plan == CreatureBodyPlan.Arachnid ? 8 : plan == CreatureBodyPlan.Worm ? 0 : 4,
            VoiceSeed = 1000 + i,
        };

        for (int i = 0; i < plans.Count; i++)
        {
            for (int j = i + 1; j < plans.Count; j++)
            {
                var a = Of(plans[i], i);
                var b = Of(plans[j], j);
                for (uint seed = 1; seed <= 6; seed++)
                {
                    var child = SvGameServer.CrossCreaturesForTest(a, b, seed * 7919u);
                    Assert.Contains(child.BodyPlan, new[] { a.BodyPlan, b.BodyPlan });
                    Assert.False(child.Hostile);
                    Assert.Equal(0f, child.AttackDamage);
                    Assert.InRange(child.Size, 0.5f, 2.2f);
                    Assert.InRange(child.Speed, 1.5f, 4f);
                    Assert.Equal(1, child.SocialGroupSize);
                    Assert.False(child.IsGiant);
                    // Plan and habitat come from the same parent, so the body can always be built and moved.
                    Assert.Equal(child.BodyPlan == a.BodyPlan ? a.Habitat : b.Habitat, child.Habitat);
                }
            }
        }
    }

    // ---------------- Bred plants ----------------

    [Fact]
    public void ASeedling_Grows_IsHarvested_AndComesBackAsTheSamePlant()
    {
        var server = NewServer();
        var p = Player(server, "Gardener");
        PutLab(server);
        uint seed = server.GiveFloraSampleForTest(p, "flora_bush", 2);

        server.BioLabForTest(p, new BioLabIntent { Action = BioLabIntent.Seedling, Sample = seed });
        string seedling = ItemKey.WithSeed(BioItems.Seedling, seed);
        Assert.Equal(1, p.State.Inventory.CountOf(seedling));
        Assert.Equal(1, Samples(p, seed));

        var cell = new Vector3i(-2, 200, 0);
        server.World.SetBlock(new Vector3i(-2, 199, 0), Block("dirt"));
        server.PlaceBlock("Gardener", cell.X, cell.Y, cell.Z, seedling);

        Assert.Equal(FloraForm.BlockKey, _content.BlockById(server.World.GetBlock(cell))?.Key);
        Assert.Equal(seed, server.BredSeedAtForTest(cell));
        var (tint, form) = server.World.GetModifier(cell);
        Assert.True(FloraForm.IsForm(form));
        Assert.Equal("flora_bush", FloraForm.BodyBlock(form));
        Assert.Equal(FloraForm.LayoutPlain, FloraForm.Layout(form));

        // A harvest yields what the bush yields, and a sample of the species.
        server.MineBlock("Gardener", cell.X, cell.Y, cell.Z);
        Assert.True(server.World.GetBlock(cell).IsAir);
        Assert.Equal(1, p.State.Inventory.CountOf("plant_fiber"));
        Assert.Equal(2, Samples(p, seed));

        // It grows back — more slowly than a wild plant (the weather moves the clock a little) — as the same plant.
        Ticks(server, 20.0, 1.0);
        Assert.True(server.World.GetBlock(cell).IsAir);
        for (int i = 0; i < 600 && server.World.GetBlock(cell).IsAir; i++)
        {
            server.TickForTest(1.0);
        }

        Assert.Equal(FloraForm.BlockKey, _content.BlockById(server.World.GetBlock(cell))?.Key);
        Assert.Equal((tint, form), server.World.GetModifier(cell));
    }

    [Fact]
    public void ASeedling_NeedsCleanSoil_OrATray_OrAPot()
    {
        var server = NewServer();
        var p = Player(server, "Gardener");
        uint seed = server.GiveFloraSampleForTest(p, "flora_fern", 1);
        string seedling = ItemKey.WithSeed(BioItems.Seedling, seed);
        Assert.Equal(0, p.State.Inventory.Add(seedling, 4, 64));

        string Planted(int x, string host)
        {
            server.World.SetBlock(new Vector3i(x, 199, 2), Block(host));
            server.PlaceBlock("Gardener", x, 200, 2, seedling);
            return _content.BlockById(server.World.GetBlock(new Vector3i(x, 200, 2)))?.Key ?? "air";
        }

        Assert.Equal("air", Planted(-1, "tainted_soil"));
        Assert.Equal("air", Planted(0, "steel_floor"));
        Assert.Equal(FloraForm.BlockKey, Planted(1, "hydro_tray"));
        Assert.Equal(FloraForm.BlockKey, Planted(2, "flower_pot"));
        Assert.Equal(2, p.State.Inventory.CountOf(seedling)); // only the two that grew were used up
    }

    [Fact]
    public void ASeedling_NeedsAir()
    {
        var server = NewServer("crystal", "airless"); // an airless world
        var p = Player(server, "Gardener");
        uint seed = server.GiveFloraSampleForTest(p, "flora_fern", 1);
        string seedling = ItemKey.WithSeed(BioItems.Seedling, seed);
        Assert.Equal(0, p.State.Inventory.Add(seedling, 1, 64));
        server.World.SetBlock(new Vector3i(1, 199, 2), Block("dirt"));

        server.PlaceBlock("Gardener", 1, 200, 2, seedling);

        Assert.True(server.World.GetBlock(new Vector3i(1, 200, 2)).IsAir);
        Assert.Equal(1, p.State.Inventory.CountOf(seedling));
    }

    [Fact]
    public void APlantCross_FromTheTank_IsANewPlant()
    {
        var server = NewServer();
        var p = TankOwner(server);
        p.State.UnlockedBlueprints.Add(BioItems.CrossingBlueprint);
        uint bush = server.GiveFloraSampleForTest(p, "flora_bush", 1);
        uint fern = server.GiveFloraSampleForTest(p, "flora_fern", 1);
        uint childSeed = BioHash.CrossSeed(bush, fern);

        server.SetCrystalDeviceForTest(p, Tank, action: 2, mode: 0,
            config: "sp=" + SvGameServer.TankChoiceForTest(bush) + ";x=" + SvGameServer.TankChoiceForTest(fern));
        server.SetCrystalDeviceForTest(p, Tank, action: 1);
        Assert.True(server.CrystalDeviceOutput(Tank));
        Ticks(server, CrystalNetRules.CloneGrowSeconds + 1.0);

        var child = server.BioEntryForTest(childSeed)!;
        Assert.Equal(BioKind.Plant, child.Kind);
        Assert.NotEqual(FloraForm.LayoutPlain, child.Flora!.Layout);
        Assert.NotEqual(child.Flora.BodyBlock, child.Flora.CrownBlock);
        Assert.Equal(2, Samples(p, childSeed));          // two samples of the new plant
        Assert.Empty(server.Creatures.Where(c => c.CloneOf.Length > 0)); // no animal came out of it
    }

    // ---------------- Persistence ----------------

    [Fact]
    public void SampleCase_Research_Register_Effects_AndBredPlants_SurviveARestart()
    {
        uint seed;
        var cell = new Vector3i(-2, 200, 0);
        {
            var server = NewServer(world: "keep");
            var p = Player(server, "Keeper");
            PutLab(server);
            seed = server.GiveFloraSampleForTest(p, "flora_bush", 4);
            server.BioLabForTest(p, new BioLabIntent { Action = BioLabIntent.Analyse, Sample = seed });
            server.BioLabForTest(p, new BioLabIntent { Action = BioLabIntent.Seedling, Sample = seed });
            server.World.SetBlock(new Vector3i(-2, 199, 0), Block("dirt"));
            server.PlaceBlock("Keeper", cell.X, cell.Y, cell.Z, ItemKey.WithSeed(BioItems.Seedling, seed));
            server.StartEffectForTest(p, BioEffect.Speed, 9, 300, BioSideEffect.Hungry, 1);
            p.State.Shield = 12f;
            Assert.Equal(2, Samples(p, seed));
            server.SaveAllForTest();
            server.Stop();
        }

        {
            var server = NewServer(world: "keep");
            var p = server.AddLocalPlayer("Keeper");
            Assert.Equal(2, Samples(p, seed));
            Assert.True(server.BioAnalysedForTest("Keeper", seed));
            Assert.NotNull(server.BioEntryForTest(seed));
            var effect = Assert.Single(p.State.Effects);
            Assert.Equal(BioEffect.Speed, effect.Effect);
            Assert.Equal(9, effect.Level);
            Assert.Equal(BioSideEffect.Hungry, effect.Side);
            Assert.Equal(12f, p.State.Shield);
            Assert.Equal(seed, server.BredSeedAtForTest(cell));
            Assert.Equal(FloraForm.BlockKey, _content.BlockById(server.World.GetBlock(cell))?.Key);
        }
    }

    [Fact]
    public void AnOlderPlayerRecord_LoadsWithAnEmptyCaseAndNoEffects()
    {
        var legacy = new PlayerSnapshot { Id = "Old", Name = "Old" };
        legacy.SampleCase = null!;
        legacy.Effects = null!;
        var state = StateMapper.FromSnapshot(legacy);
        Assert.Equal(BioRules.SampleCaseSlots, state.SampleCase.SlotCount);
        Assert.Empty(state.Effects);
        Assert.Equal(0f, state.Shield);

        state.SampleCase.Add(ItemKey.WithSeed(BioItems.Sample, 0xABCDEF01), 3, BioRules.SampleStack);
        state.Effects.Add(new ActiveEffect { Effect = BioEffect.Jump, Level = 4, SecondsLeft = 33f });
        var back = StateMapper.FromSnapshot(StateMapper.ToSnapshot(state));
        Assert.Equal(3, back.SampleCase.CountOf(ItemKey.WithSeed(BioItems.Sample, 0xABCDEF01)));
        Assert.Equal(33f, Assert.Single(back.Effects).SecondsLeft);
    }
}
