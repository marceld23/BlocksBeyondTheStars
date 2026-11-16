// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
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
/// The fixes of the bio lab audit (#3724–#3730): a melee weapon has no range to change; a mining preparation bites on an
/// asteroid's or a derelict's blocks as on a planet's; a coupled second effect that finds no room is named to the player;
/// taking an equal preparation never shortens the one that runs; a lit Ressaw lamp covers a night sight the player drank
/// and gives back what is left of it; a fighting robber lets go of a player under a stealth preparation; and the hunter
/// robot fights with the values of the very item it was given, a lab change included.
/// </summary>
public sealed class BioLabAuditFixTests : IDisposable
{
    private const string Hull = "derelict:audit-test-0";
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    private static readonly Vector3f AtTheLab = new(0, 200, 0);
    private static readonly Vector3f HullSpot = new(0f, 300f, -400f);
    private static readonly Vector3i RobotFloor = new(300, 150, 300);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_bioaudit_" + Guid.NewGuid().ToString("N"));
    private readonly List<SqliteWorldRepository> _repos = new();

    public void Dispose()
    {
        foreach (var repo in _repos)
        {
            repo.Dispose();
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // a locked temp file is no test failure
        }
    }

    // ---------------- Harness ----------------

    private SvGameServer NewServer(string world, string planet, RecordingTransport? transport = null, Action<ServerConfig>? tune = null)
        => NewServer(world, planet, out _, transport, tune);

    private SvGameServer NewServer(string world, string planet, out SqliteWorldRepository repo, RecordingTransport? transport = null,
        Action<ServerConfig>? tune = null)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        _repos.Add(repo);
        var config = new ServerConfig
        {
            WorldName = world,
            Seed = 77,
            StartPlanet = planet,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = false,
            PlaceWrecks = false,
            PlaceBanditCamps = false,
            ViewDistanceChunks = 1,
        };
        config.Rules.PlanetEnemies = AlienActivity.Off;
        config.Rules.AggressiveAliens = AlienActivity.Off;
        config.Rules.CreatureAbundance = AlienActivity.Off;
        tune?.Invoke(config);
        var server = new SvGameServer(config, Content, transport ?? new RecordingTransport(), repo);
        server.Start();
        return server;
    }

    /// <summary>A player on foot at <paramref name="at"/> with an empty backpack and one of each given item.</summary>
    private static PlayerSession Player(SvGameServer server, string name, Vector3f at, string locale = "en", params string[] items)
    {
        var p = server.AddLocalPlayer(name, locale);
        p.State.AboardShip = false;
        p.State.Position = at;
        p.State.SuitEnergy = 100f;
        for (int i = 0; i < p.State.Inventory.SlotCount; i++)
        {
            p.State.Inventory.SetSlot(i, null);
        }

        foreach (string key in items)
        {
            Assert.Equal(0, p.State.Inventory.Add(key, 1, 1));
        }

        return p;
    }

    private static BlockId Block(string key) => Content.GetBlock(key)!.NumericId;

    private static void TickSeconds(SvGameServer server, PlayerSession p, double seconds)
    {
        var at = p.State.Position;
        for (double t = 0; t < seconds - 1e-6; t += 0.1)
        {
            p.State.Position = at; // nothing may nudge the player away
            server.TickForTest(0.1);
        }
    }

    private static MaterialProfile Material(int purity, params (MatTrait Trait, int Level)[] traits)
    {
        var m = new MaterialProfile { Purity = purity };
        foreach (var (trait, level) in traits)
        {
            m.Levels[(int)trait] = level;
        }

        return m;
    }

    private static string Prep(BioEffect effect, int level, int seconds = 120, BioEffect secondary = BioEffect.None, int secondaryLevel = 0)
        => BioItems.PreparationItem(new Compound
        {
            Form = BioForm.Injector,
            Effect = effect,
            Level = level,
            DurationSeconds = seconds,
            Secondary = secondary,
            SecondaryLevel = secondaryLevel,
        });

    private static ActiveEffect? Running(PlayerSession p, BioEffect effect)
        => p.State.Effects.Find(e => e.Effect == effect && e.SecondsLeft > 0f);

    /// <summary>The bio lab's notices a player got (the <c>@srv.bio.*</c> server messages).</summary>
    private static List<string> BioNotices(RecordingTransport transport, PlayerSession who)
        => transport.TextsTo(who).Where(t => t.StartsWith("@srv.bio.", StringComparison.Ordinal)).ToList();

    private static bool UsesRange(ItemMods mods)
        => mods.First == ModStat.Range || mods.Second == ModStat.Range || mods.Drawback == ModStat.Range;

    // ---------------- #3724: the range of a melee weapon ----------------

    [Fact]
    public void AMeleeWeapon_HasNoRangeToChange_AndAGunHas()
    {
        var magnetic = Material(5, (MatTrait.Magnetic, 3), (MatTrait.Hard, 1));
        var unstable = Material(5, (MatTrait.Hard, 3), (MatTrait.Unstable, 2));
        var sight = new Compound { Form = BioForm.Coating, Effect = BioEffect.NightSight, Level = 12 }; // a coating that adds range
        var strength = new Compound { Form = BioForm.Coating, Effect = BioEffect.Strength, Level = 12 };
        int blades = 0, guns = 0;
        foreach (var item in Content.Items.Values.Where(i => i.Tool is { Kind: ToolKind.Weapon }))
        {
            var tool = item.Tool!;
            ItemMods Change(MaterialProfile material, Compound coating)
                => ItemModRules.Compute(true, tool.CooldownSeconds > 0f, ItemModRules.HasChangeableRange(tool), tool.EnergyPerUse > 0f, material, coating);

            var pulled = Change(magnetic, sight);
            var shaky = Change(unstable, strength);
            if (tool.Range <= MeleeRules.MeleeReach)
            {
                blades++;
                Assert.False(ItemModRules.HasChangeableRange(tool), item.Key);
                Assert.False(UsesRange(pulled), $"{item.Key}: a magnetic material and a range coating must not change a range that never acts");
                Assert.False(UsesRange(shaky), $"{item.Key}: an unstable material must not pay with a range that never acts");
                Assert.False(shaky.IsEmpty, item.Key);
                if (tool.CooldownSeconds > 0f)
                {
                    Assert.Equal(ModStat.Cooldown, shaky.Drawback); // the big change still has its price — a real one
                }
            }
            else
            {
                guns++;
                Assert.True(ItemModRules.HasChangeableRange(tool), item.Key);
                Assert.Equal(ModStat.Range, pulled.First);
                Assert.Equal(ModStat.Range, shaky.Drawback);
            }
        }

        Assert.True(blades >= 5 && guns >= 3, $"the shipped data has blades ({blades}) and guns ({guns})");
        Assert.False(ItemModRules.HasChangeableRange(Content.GetItem("basic_drill")!.Tool)); // a drill has no range at all
        Assert.False(ItemModRules.HasChangeableRange(null));
    }

    [Fact]
    public void TheLab_PutsNoRangeOnABlade_ButStillOnAGun()
    {
        var server = NewServer("blade_range", "jungle");
        var p = Player(server, "Smith", AtTheLab, "en", "machete", "gauss_pistol");
        server.World.SetBlock(new Vector3i(2, 200, 0), Block(BioItems.Lab));
        p.State.UnlockedBlueprints.Add(BioItems.TuningBlueprint);
        Assert.Equal(0, p.State.Inventory.Add("neodymium", 4, 1024)); // magnetic 3, heavy 1

        server.BioLabForTest(p, new BioLabIntent { Action = BioLabIntent.Change, TargetItem = "machete", MaterialItem = "neodymium" });
        server.BioLabForTest(p, new BioLabIntent { Action = BioLabIntent.Change, TargetItem = "gauss_pistol", MaterialItem = "neodymium" });

        var blade = ItemMods.Of(p.State.Inventory.Slots[0]!.Item);
        Assert.False(blade.IsEmpty);     // the heavy trait still means something to it …
        Assert.False(UsesRange(blade));  // … the magnetic one does not
        Assert.Equal(ModStat.Power, blade.First);
        var gun = ItemMods.Of(p.State.Inventory.Slots[1]!.Item);
        Assert.Equal(ModStat.Range, gun.First);
        Assert.Equal(2, p.State.Inventory.CountOf("neodymium"));
    }

    // ---------------- #3725: the mining preparation on a space structure ----------------

    [Fact]
    public void AMiningPreparation_BitesHarderOnADerelictsHull_AsOnAPlanet()
    {
        var transport = new RecordingTransport();
        var server = NewServer("hull_mining", "jungle", transport, c => c.Rules.FreeSpaceFlight = true);
        var p = server.AddLocalPlayer("Miner");
        var wall = Block("iron_wall");
        var cells = new Dictionary<Vector3i, BlockId>();
        for (int x = 0; x < 8; x++)
        {
            for (int y = 0; y < 6; y++)
            {
                for (int z = 0; z < 8; z++)
                {
                    bool shell = x == 0 || x == 7 || y == 0 || y == 5 || z == 0 || z == 7;
                    bool entrance = z == 0 && (x == 3 || x == 4) && y >= 1 && y <= 3;
                    if (shell && !entrance)
                    {
                        cells[new Vector3i(x, y, z)] = wall;
                    }
                }
            }
        }

        server.RegisterDerivedStationForTest(Hull, "Test Hull", cells, new Vector3i(3, 1, 2), HullSpot);
        server.EnterSpace("Miner");
        server.ShowDerivedStationForTest("Miner", Hull);
        p.State.Inventory.SetSlot(0, new ItemStack("basic_drill", 1)); // power 1 against a hardness of several: no hit breaks it
        p.State.SelectedHotbarSlot = 0;
        p.State.InEva = true;
        server.ShipMove("Miner", HullSpot.X, HullSpot.Y + 6f, HullSpot.Z);

        float Hit(Vector3i cell)
        {
            transport.Sent.Clear();
            server.HandleStructureEditForTest("Miner", new StructureEditIntent { StructureId = Hull, X = cell.X, Y = cell.Y, Z = cell.Z, Mine = true });
            Assert.Empty(transport.To<ActionRejected>(p));
            return Assert.Single(transport.To<StructureMiningProgress>(p)).Fraction;
        }

        float plain = Hit(new Vector3i(1, 5, 1));
        server.StartEffectForTest(p, BioEffect.Mining, BioRules.MaxLevel, 300f);
        float factor = PlayerEffects.MiningFactor(p.State.Effects);
        float boosted = Hit(new Vector3i(2, 5, 1));

        Assert.True(factor > 1.2f, $"the top mining preparation is a real factor ({factor})");
        Assert.Equal(plain * factor, boosted, 4);
    }

    // ---------------- #3726: the coupled second effect ----------------

    [Fact]
    public void ACoupledSecondEffect_WithThreeEffectsRunning_IsNamedToThePlayer_InTheirLanguage()
    {
        var transport = new RecordingTransport();
        var server = NewServer("second_full", "jungle", transport);
        string prep = Prep(BioEffect.Strength, 8, secondary: BioEffect.Mining, secondaryLevel: 4);
        var en = Player(server, "Taster", AtTheLab, "en", prep);
        var de = Player(server, "Koster", AtTheLab, "de", prep);
        foreach (var p in new[] { en, de })
        {
            server.StartEffectForTest(p, BioEffect.Speed, 5, 300f);
            server.StartEffectForTest(p, BioEffect.Jump, 5, 300f);
            server.ConsumeItem(p.State.PlayerId, prep);

            Assert.NotNull(Running(p, BioEffect.Strength)); // the main effect starts …
            Assert.Null(Running(p, BioEffect.Mining));      // … the second finds no room …
            Assert.Equal(BioRules.MaxActiveEffects, p.State.Effects.Count);
            Assert.Equal(0, p.State.Inventory.CountOf(prep)); // … and the preparation is used up all the same
            Assert.Empty(transport.To<ActionRejected>(p));
        }

        Assert.Equal("@srv.bio.second_effect_no_room:Mining", Assert.Single(BioNotices(transport, en)));
        Assert.Equal("@srv.bio.second_effect_no_room:Abbaukraft", Assert.Single(BioNotices(transport, de)));
    }

    [Fact]
    public void ACoupledSecondEffect_UnderAStrongerOneOfItsKind_IsNamedToo_AndOneWithRoom_StartsSilently()
    {
        var transport = new RecordingTransport();
        var server = NewServer("second_weaker", "jungle", transport);
        string prep = Prep(BioEffect.Strength, 8, secondary: BioEffect.Mining, secondaryLevel: 4);
        var p = Player(server, "Taster", AtTheLab, "en", prep);
        server.StartEffectForTest(p, BioEffect.Mining, 9, 300f);
        server.ConsumeItem("Taster", prep);

        Assert.NotNull(Running(p, BioEffect.Strength));
        Assert.Equal(9, Running(p, BioEffect.Mining)!.Level); // the stronger one keeps running untouched
        Assert.Equal(0, p.State.Inventory.CountOf(prep));
        Assert.Equal("@srv.bio.second_effect_no_room:Mining", Assert.Single(BioNotices(transport, p)));

        var free = Player(server, "Lucky", AtTheLab, "en", prep);
        server.ConsumeItem("Lucky", prep);
        Assert.Equal(4, Running(free, BioEffect.Mining)!.Level);
        Assert.Empty(BioNotices(transport, free));
    }

    [Fact]
    public void TheSecondEffectNotice_IsInBothLanguages_WithItsNamePlaceholder()
    {
        foreach (string language in new[] { "en", "de" })
        {
            Assert.True(TestLocales.Load(language).TryGetValue("srv.bio.second_effect_no_room", out string? text), language);
            Assert.Contains("{name}", text, StringComparison.Ordinal);
        }
    }

    // ---------------- #3727: an equal effect never shortens the running one ----------------

    [Fact]
    public void AnEqualEffect_KeepsTheLongerTime_AStrongerOneStartsItsOwn_AWeakerOneIsRefused()
    {
        ActiveEffect Speed(int level, float seconds, BioSideEffect side = BioSideEffect.None, int sideLevel = 0, BioThermal thermal = BioThermal.Stable)
            => new() { Effect = BioEffect.Speed, Level = level, SecondsLeft = seconds, Side = side, SideLevel = sideLevel, Thermal = thermal };

        // Equal, shorter: the time that was left stays; the catch and the weather behaviour are the new one's.
        var effects = new List<ActiveEffect> { Speed(5, 300f, BioSideEffect.Hungry, 1) };
        Assert.Equal(EffectAddResult.Refreshed, PlayerEffects.Add(effects, Speed(5, 120f, BioSideEffect.Tired, 2, BioThermal.HeatSensitive)));
        var kept = Assert.Single(effects);
        Assert.Equal(300f, kept.SecondsLeft);
        Assert.Equal((BioSideEffect.Tired, 2, BioThermal.HeatSensitive), (kept.Side, kept.SideLevel, kept.Thermal));

        // Equal, longer: the new time.
        Assert.Equal(EffectAddResult.Refreshed, PlayerEffects.Add(effects, Speed(5, 400f)));
        Assert.Equal(400f, Assert.Single(effects).SecondsLeft);

        // Stronger: it replaces, with its own (shorter) time.
        Assert.Equal(EffectAddResult.Refreshed, PlayerEffects.Add(effects, Speed(8, 60f)));
        Assert.Equal((8, 60f), (effects[0].Level, effects[0].SecondsLeft));

        // Weaker: refused, nothing changes.
        Assert.Equal(EffectAddResult.Weaker, PlayerEffects.Add(effects, Speed(3, 900f)));
        Assert.Equal((8, 60f), (Assert.Single(effects).Level, effects[0].SecondsLeft));
    }

    [Fact]
    public void TakingTheSamePreparationAgain_DoesNotShortenTheRunningEffect()
    {
        var server = NewServer("equal_refresh", "jungle");
        string prep = Prep(BioEffect.Speed, 8, seconds: 120);
        var p = Player(server, "Taster", AtTheLab, "en", prep);
        server.StartEffectForTest(p, BioEffect.Speed, 8, 300f);

        server.ConsumeItem("Taster", prep);

        Assert.Equal(0, p.State.Inventory.CountOf(prep));
        Assert.Equal(300f, Running(p, BioEffect.Speed)!.SecondsLeft);
    }

    // ---------------- #3728: the Ressaw lamp and a night sight the player drank ----------------

    private static PlayerSession WithLamp(SvGameServer server, string name)
    {
        var p = Player(server, name, new Vector3f(500.5f, 150f, 500.5f));
        TestGear.Wear(p.State, "ressaw_lamp");
        return p;
    }

    [Fact]
    public void TheLamp_CoversAWeakerDrunkNightSight_AndGivesBackWhatIsLeftOfIt()
    {
        var server = NewServer("lamp_cover", "varied");
        var p = WithLamp(server, "Drinker");
        server.StartEffectForTest(p, BioEffect.NightSight, 5, 300f, BioSideEffect.Tired, 2);
        float weak = PlayerEffects.Of(p.State.Effects, BioEffect.NightSight);

        p.LampOn = true;
        TickSeconds(server, p, 1);
        Assert.Equal(1f, PlayerEffects.Of(p.State.Effects, BioEffect.NightSight), 3); // the lamp's strength
        Assert.Single(p.State.Effects);                                               // in the drunk one's place, not beside it

        TickSeconds(server, p, 30); // longer than one lamp run: renewed, and the drunk one counts down underneath
        Assert.Equal(1f, PlayerEffects.Of(p.State.Effects, BioEffect.NightSight), 3);

        p.LampOn = false;
        TickSeconds(server, p, 0.2);
        var back = Assert.Single(p.State.Effects);
        Assert.Equal(weak, PlayerEffects.Of(p.State.Effects, BioEffect.NightSight));
        Assert.Equal((5, BioSideEffect.Tired, 2), (back.Level, back.Side, back.SideLevel));
        Assert.InRange(back.SecondsLeft, 300f - 31.2f - 0.5f, 300f - 31.2f + 0.5f); // it paid for the time under the lamp
        Assert.Null(back.Under);
    }

    [Fact]
    public void ADrunkNightSight_ThatRunsOutUnderTheLamp_DoesNotComeBack()
    {
        var server = NewServer("lamp_out", "varied");
        var p = WithLamp(server, "Drinker");
        server.StartEffectForTest(p, BioEffect.NightSight, 5, 15f, BioSideEffect.Tired, 2);

        p.LampOn = true;
        TickSeconds(server, p, 20);
        Assert.Equal(1f, PlayerEffects.Of(p.State.Effects, BioEffect.NightSight), 3);
        p.LampOn = false;
        TickSeconds(server, p, 0.2);

        Assert.Empty(p.State.Effects);
    }

    [Fact]
    public void AnAsStrongDrunkNightSight_AboutToEnd_IsCoveredToo_AndItsLastSecondsComeBack()
    {
        var server = NewServer("lamp_top", "varied");
        var p = WithLamp(server, "Drinker");
        server.StartEffectForTest(p, BioEffect.NightSight, BioRules.MaxLevel, 8f, BioSideEffect.Hungry, 1);

        p.LampOn = true;
        TickSeconds(server, p, 2);
        var lamp = Assert.Single(p.State.Effects);
        Assert.Equal(BioSideEffect.None, lamp.Side); // the lamp's run took over …
        p.LampOn = false;
        TickSeconds(server, p, 0.2);

        var back = Assert.Single(p.State.Effects); // … and the drunk one's last seconds are back with its catch
        Assert.Equal(BioSideEffect.Hungry, back.Side);
        Assert.InRange(back.SecondsLeft, 5f, 6.2f);
    }

    [Fact]
    public void UnderTheLamp_ACoveredEffect_NeverMakesRoomForAFourth()
    {
        var transport = new RecordingTransport();
        var server = NewServer("lamp_three", "varied", transport);
        var p = WithLamp(server, "Busy");
        string prep = Prep(BioEffect.Strength, 5);
        Assert.Equal(0, p.State.Inventory.Add(prep, 1, 1));
        server.StartEffectForTest(p, BioEffect.Speed, 5, 300f);
        server.StartEffectForTest(p, BioEffect.Jump, 5, 300f);
        server.StartEffectForTest(p, BioEffect.NightSight, 5, 300f);

        p.LampOn = true;
        TickSeconds(server, p, 1);
        Assert.Equal(BioRules.MaxActiveEffects, p.State.Effects.Count);
        server.ConsumeItem("Busy", prep);
        Assert.Contains(transport.To<ActionRejected>(p), r => r.Reason == "@srv.bio.effect_full");
        Assert.Equal(1, p.State.Inventory.CountOf(prep));

        p.LampOn = false;
        TickSeconds(server, p, 0.2);
        Assert.Equal(BioRules.MaxActiveEffects, p.State.Effects.Count);
        Assert.Equal(5, Running(p, BioEffect.NightSight)!.Level);
    }

    [Fact]
    public void LoggingOutUnderTheLamp_KeepsTheDrunkNightSight_InTheSave()
    {
        var server = NewServer("lamp_save", "varied", out var repo);
        var p = WithLamp(server, "Drinker");
        server.StartEffectForTest(p, BioEffect.NightSight, 5, 300f, BioSideEffect.Tired, 2);
        p.LampOn = true;
        TickSeconds(server, p, 1);

        repo.SavePlayer(p.State);
        var loaded = repo.LoadPlayer(p.State.PlayerId)!;

        var saved = Assert.Single(loaded.Effects);
        Assert.Equal(SvGameServer.LampSightLevel, saved.Level); // the lamp's run, as it was lit …
        Assert.Equal(5, saved.Under?.Level);                     // … and the drunk one under it

        // The next session starts with the lamp off: its last run ends by itself, and the drunk night sight is back.
        Assert.True(PlayerEffects.Tick(loaded.Effects, 25f, temperatureC: 20f));
        var back = Assert.Single(loaded.Effects);
        Assert.Equal((5, BioSideEffect.Tired, 2), (back.Level, back.Side, back.SideLevel));
        Assert.InRange(back.SecondsLeft, 273f, 275f);
    }

    [Fact]
    public void ACoveredEffect_CountsDownInItsOwnWeather_AndTheTemperatureIsReadForIt()
    {
        ActiveEffect Sight(int level, float seconds, BioThermal thermal = BioThermal.Stable)
            => new() { Effect = BioEffect.NightSight, Level = level, SecondsLeft = seconds, Thermal = thermal };

        var drunk = Sight(5, 100f, BioThermal.HeatSensitive);
        var effects = new List<ActiveEffect> { drunk };
        PlayerEffects.Cover(effects, drunk, Sight(12, 20f));

        Assert.Same(drunk, Assert.Single(effects).Under);
        Assert.True(PlayerEffects.AnyThermal(effects)); // or the server would never look the temperature up
        Assert.False(PlayerEffects.Tick(effects, 10f, temperatureC: 80f));
        Assert.Equal(10f, effects[0].SecondsLeft, 3);                               // the cover: plain speed
        Assert.Equal(100f - 10f * BioRules.ThermalDecay, drunk.SecondsLeft, 3);     // underneath: its own weather

        Assert.True(PlayerEffects.Tick(effects, 10f, temperatureC: 20f)); // the cover runs out: the set changed …
        Assert.Same(drunk, Assert.Single(effects));                        // … and what it covered is back in its place
    }

    // ---------------- #3729: a fighting robber and a stealth preparation ----------------

    [Fact]
    public void AFightingRobber_LetsGoOfAPlayerUnderAStealthPreparation()
    {
        var server = NewServer("robber_stealth", "rocky", tune: c =>
        {
            c.Seed = 9; // the spawn of BanditTests: open ground, the robber three blocks off is in sight
            c.Rules.Bandits = AlienActivity.Normal;
        });
        var mark = server.AddLocalPlayer("Mark");
        mark.State.AboardShip = false;
        for (int i = 0; i < mark.State.Inventory.SlotCount; i++)
        {
            mark.State.Inventory.SetSlot(i, null);
        }

        mark.State.Inventory.SetSlot(0, new ItemStack("iron_ore", 100));
        server.SpawnBanditAtForTest(new Vector3f(mark.State.Position.X + 3f, mark.State.Position.Y, mark.State.Position.Z + 3f), "Mark");
        server.Tick(0.1);
        server.RespondBanditDemandForTest("Mark", comply: false);
        var robber = Assert.Single(server.Bandits);
        server.Tick(0.1);
        Assert.Equal(BanditPhase.Fighting, robber.BanditPhase); // it fights a player it can see

        server.StartEffectForTest(mark, BioEffect.Stealth, 5, 120f);
        Assert.False(mark.State.Stealthed); // not the suit's cloak: the preparation alone
        float health = mark.State.Health;
        server.Tick(0.1);

        Assert.Equal(BanditPhase.Leaving, robber.BanditPhase); // nobody left to fight: it walks off
        Assert.Equal(health, mark.State.Health);
    }

    // ---------------- #3730: the hunter robot and a changed weapon ----------------

    [Fact]
    public void TheRobot_FightsWithTheValuesOfTheVeryItemItCarries_ALabChangeIncluded()
    {
        var server = NewServer("robot_changed", "rocky");
        var stone = Block("stone");
        for (int x = -8; x <= 8; x++)
        {
            for (int z = -8; z <= 8; z++)
            {
                server.World.SetBlock(new Vector3i(RobotFloor.X + x, RobotFloor.Y - 1, RobotFloor.Z + z), stone);
                for (int y = 0; y < 11; y++)
                {
                    server.World.SetBlock(new Vector3i(RobotFloor.X + x, RobotFloor.Y + y, RobotFloor.Z + z), BlockId.Air);
                }
            }
        }

        var owner = Player(server, "Ada", new Vector3f(RobotFloor.X + 0.5f, RobotFloor.Y, RobotFloor.Z + 0.5f), "en", SvGameServer.HunterRobotItem);
        Assert.True(server.DeployRobotForTest("Ada"));
        var robot = server.RobotEntityForTest("Ada")!;
        robot.Position = new Vector3f(RobotFloor.X + 1.5f, RobotFloor.Y, RobotFloor.Z + 0.5f);
        var record = owner.State.TamedCreatures.Single();

        void Give(string item)
        {
            owner.State.Inventory.SetSlot(2, new ItemStack(item, 1));
            server.HandlePayloadForTest(owner.ConnectionId, NetCodec.Encode(new GiveCompanionGearIntent { CompanionId = record.Id, Slot = 2 }));
        }

        // A plain pistol — what every robot of an older save carries: the definition's values.
        Give("scrap_pistol");
        var plain = server.RobotWeaponStatsForTest("Ada");
        Assert.Equal(6f, plain.Damage);  // 60 % of 10 is its own shot
        Assert.Equal(16f, plain.Reach);

        // The same pistol with more power and less range from the lab: the robot keeps the full key and fights with it.
        string changed = new ItemMods(ModStat.Power, 3, ModStat.None, 0, ModStat.Range, 2).ApplyTo("scrap_pistol");
        Give(changed);
        Assert.Equal(changed, record.GearWeapon);
        Assert.Equal(changed, robot.RobotWeapon);
        var tuned = server.RobotWeaponStatsForTest("Ada");
        Assert.Equal(RobotGear.WeaponDamageShare * 10f * (1f + 3 * ToolMods.PowerPerLevel), tuned.Damage, 3);
        Assert.Equal(16f * (1f - 2 * ToolMods.RangePerLevel), tuned.Reach, 3);
        Assert.True(tuned.Damage > plain.Damage && tuned.Reach < plain.Reach);

        // The rule itself: without a key, or with a key this version cannot read, the definition's own values.
        var pistol = Content.GetItem("scrap_pistol");
        Assert.Equal(plain.Damage, RobotGear.ShotDamage(pistol, 6f));
        Assert.Equal(plain.Reach, RobotGear.Reach(pistol, 14f, "scrap_pistol#uzzzzzz"));
        Assert.Null(RobotGear.WeaponValues(Content.GetItem("basic_drill"), "basic_drill"));

        // Taken back, the item is still the changed one.
        server.HandlePayloadForTest(owner.ConnectionId, NetCodec.Encode(new TakeCompanionGearIntent { CompanionId = record.Id, Piece = RobotGear.WeaponPiece }));
        Assert.Equal(1, owner.State.Inventory.CountOf(changed));
    }
}
