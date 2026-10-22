// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.State;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #2712: Sandbox is damage-free ("you shouldn't be able to take damage at all in Sandbox"). The report came from a
/// spacewalk in a Sandbox world — oxygen drained and health fell to 31, because the mode's "oxygen off" sat behind the
/// EVA guard. Now the Creative game mode (the new-world screen's Sandbox, or a player's own <c>/mode Player creative</c>)
/// drains no air anywhere and nothing hurts: lava, falls, hostile fire. Explorer and the Creative world mode (survival
/// rules + free flight) keep every rule.
/// </summary>
public sealed class SandboxDamageFreeTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;
    private readonly List<SqliteWorldRepository> _repos = new();

    public SandboxDamageFreeTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_sandboxsafe_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer NewServer(string name, GameMode mode, Action<GameRules>? configure = null,
        IServerTransport? transport = null, int seed = 11)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        _repos.Add(repo);
        var config = new ServerConfig
        {
            WorldName = name,
            Seed = seed,
            StartPlanet = "jungle", // breathable: the open air alone never drains oxygen here
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = false,
            PlaceWrecks = false,
            World = { TerrainGeneration = 0 }, // the classic relief — the origin column stands on dry ground
        };
        config.Rules.GameMode = mode;
        config.Rules.CreatureAbundance = AlienActivity.Off;
        configure?.Invoke(config.Rules);
        var server = new SvGameServer(config, _content, transport ?? new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        return server;
    }

    /// <summary>The first air cell over the origin column's ground.</summary>
    private static Vector3i AboveGround(SvGameServer server)
    {
        for (int y = 200; y > 8; y--)
        {
            if (!server.World.GetBlock(new Vector3i(0, y, 0)).IsAir)
            {
                return new Vector3i(0, y + 1, 0);
            }
        }

        return new Vector3i(0, 65, 0);
    }

    /// <summary>Puts a lava block at the player's feet, on the ground at the origin.</summary>
    private Vector3f StandInLava(SvGameServer server)
    {
        var feet = AboveGround(server);
        server.World.SetBlock(feet, _content.GetBlock("lava")!.NumericId);
        return new Vector3f(feet.X + 0.5f, feet.Y, feet.Z + 0.5f);
    }

    private static void Spacewalk(PlayerState p)
    {
        p.AboardShip = true; // the suit keeps the bond to the ship; InEva is what forced the drain
        p.InEva = true;
        p.Position = new Vector3f(0, 64, 0);
    }

    [Theory]
    [InlineData(GameMode.Creative, PlayerModeOverride.None, false)]
    [InlineData(GameMode.Survival, PlayerModeOverride.None, true)]
    [InlineData(GameMode.Survival, PlayerModeOverride.Creative, false)]
    [InlineData(GameMode.Creative, PlayerModeOverride.Survival, true)]
    public void TheRule_IsTheModesTwin(GameMode mode, PlayerModeOverride over, bool hurts)
    {
        var rules = new GameRules { GameMode = mode, CreativeFlight = true };
        Assert.Equal(hurts, rules.PlayerTakesDamageFor(over));
        Assert.Equal(rules.PlayerTakesDamageFor(PlayerModeOverride.None), rules.PlayerTakesDamage);
    }

    [Fact]
    public void Sandbox_Spacewalk_KeepsFullOxygenAndHealth_OverALongWalk()
    {
        var server = NewServer("sandbox_eva", GameMode.Creative);
        var kid = server.AddLocalPlayer("Kid");
        var p = kid.State;
        Spacewalk(p);
        p.Oxygen = 100f;
        p.Health = 100f;

        for (int i = 0; i < 120; i++)
        {
            server.Tick(1.0); // two minutes outside — a full Survival tank at Normal drains a third of that
        }

        Assert.True(p.InEva);
        Assert.True(p.Oxygen >= 100f, $"a Sandbox spacewalk drained the oxygen to {p.Oxygen}");
        Assert.Equal(100f, p.Health);
    }

    [Fact]
    public void Sandbox_Spacewalk_RecoversTheReportedVitals()
    {
        var server = NewServer("sandbox_eva_report", GameMode.Creative);
        var kid = server.AddLocalPlayer("Kid");
        var p = kid.State;
        Spacewalk(p);
        p.Oxygen = 66f; // the report's numbers after the spacewalk
        p.Health = 31f;

        server.Tick(2.0);

        Assert.True(p.Oxygen > 66f, $"oxygen went {p.Oxygen}, it should refill");
        Assert.True(p.Health > 31f, $"health went {p.Health}, it should heal");
    }

    [Fact]
    public void CreativeWorldMode_SurvivalPlusFlight_StillDrainsOnASpacewalk()
    {
        // The new-world screen's "Creative" is survival rules + free flight + unlocks — not Sandbox.
        var server = NewServer("creative_eva", GameMode.Survival, r => r.CreativeFlight = true);
        var pilot = server.AddLocalPlayer("Pilot");
        var p = pilot.State;
        Spacewalk(p);
        p.Oxygen = 50f;

        server.Tick(2.0);

        Assert.True(p.Oxygen < 50f, "a Creative (survival + flight) spacewalk must still spend the tank");
        Assert.True(server.PlayerTakesDamageForTest(p));
    }

    [Theory]
    [InlineData(GameMode.Creative, false)]
    [InlineData(GameMode.Survival, true)]
    public void Lava_BurnsOnlyOutsideSandbox(GameMode mode, bool burns)
    {
        var server = NewServer("lava_" + mode, mode);
        var walker = server.AddLocalPlayer("Walker");
        var p = walker.State;
        p.AboardShip = false;
        p.Position = StandInLava(server);
        p.Health = 100f;

        server.Tick(1.0);

        if (burns)
        {
            Assert.True(p.Health < 100f, "lava burns an Explorer");
        }
        else
        {
            Assert.Equal(100f, p.Health);
        }
    }

    [Theory]
    [InlineData(GameMode.Creative, false)]
    [InlineData(GameMode.Survival, true)]
    public void HardLanding_HurtsOnlyOutsideSandbox(GameMode mode, bool hurts)
    {
        // Free flight off on purpose: a flying suit never takes a fall anyway (#1838) — this is the damage gate itself.
        var server = NewServer("fall_" + mode, mode, r => r.CreativeFlight = false);
        server.SetGravityFactorForTest(1f);
        var walker = server.AddLocalPlayer("Walker");
        var p = walker.State;
        p.AboardShip = false;
        var ground = AboveGround(server);
        p.Position = new Vector3f(ground.X + 0.5f, ground.Y, ground.Z + 0.5f);
        p.Health = 100f;

        server.FallDamageForTest("Walker", 30f); // well past the safe landing speed

        Assert.Equal(hurts, p.Health < 100f);
    }

    [Theory]
    [InlineData(GameMode.Creative, false)]
    [InlineData(GameMode.Survival, true)]
    public void SpaceHostile_NeitherHuntsNorHitsASandboxPilot(GameMode mode, bool attacked)
    {
        var server = NewServer("space_" + mode, mode, r =>
        {
            r.FreeSpaceFlight = true;
            r.SpaceCombat = SpaceCombatMode.PvE;
            r.SpaceNpcEnemies = AlienActivity.Off; // only the drone below
        });
        server.AddLocalPlayer("Pilot");
        server.EnterSpace("Pilot");
        server.ShipMove("Pilot", 0f, 0f, 0f);
        var drone = new CombatEntity
        {
            Id = "drone:sandbox",
            Kind = CombatEntityKind.Drone,
            Hostile = true,
            Hull = 25f,
            HullMax = 25f,
            DamagePerSecond = 10f,
            Position = new Vector3f(12f, 0f, 0f), // inside the engage range — its aura bites at once
        };
        server.AddSpaceEntityForTest("Pilot", drone);
        float before = server.Ship.Hull + server.Ship.Shield;

        server.Tick(2.0);

        if (attacked)
        {
            Assert.True(server.Ship.Hull + server.Ship.Shield < before, "a hostile in range hits an Explorer's ship");
            Assert.Equal("Pilot", drone.ChaseTargetId);
        }
        else
        {
            Assert.Equal(before, server.Ship.Hull + server.Ship.Shield);
            Assert.Equal(string.Empty, drone.ChaseTargetId); // nobody to hunt: it keeps to its patrol
        }
    }

    [Theory]
    [InlineData(GameMode.Creative, false)]
    [InlineData(GameMode.Survival, true)]
    public void SpeederCrash_WrecksAndJoltsOnlyOutsideSandbox(GameMode mode, bool wrecked)
    {
        var server = NewServer("speeder_" + mode, mode);
        var driver = server.AddLocalPlayer("Driver");
        var p = driver.State;
        var stand = new Vector3f(10, 64, 10);
        DryStoneStage(server, stand); // a speeder only unfolds on dry ground (#1660)
        p.AboardShip = false;
        p.Position = stand;
        p.Inventory.Add("speeder", 1, 1);
        string id = server.DeploySpeederForTest("Driver");
        Assert.NotEqual(string.Empty, id);
        p.Position = server.SpeederSnapshots.Single().Pos;
        server.EnterSpeederForTest("Driver", id);
        float hull = server.SpeederSnapshots.Single().Hull;
        p.Health = 100f;

        server.ImpactSpeederForTest("Driver", id, 999f); // a smash that wrecks any Explorer's speeder
        server.DamageSpeederForTest(id, 1000f);          // and a bite or a leviathan on top

        if (wrecked)
        {
            Assert.Equal(0, server.SpeederCount);
            Assert.Empty(p.DeployedSpeeders); // the item is lost
            Assert.True(p.Health < 100f, "the crash jolts an Explorer");
        }
        else
        {
            Assert.Equal(1, server.SpeederCount);
            Assert.Equal(hull, server.SpeederSnapshots.Single().Hull);
            Assert.Single(p.DeployedSpeeders);
            Assert.Equal(100f, p.Health);
        }
    }

    /// <summary>Dry, level stone under and around the stand, with air above it.</summary>
    private void DryStoneStage(SvGameServer server, Vector3f at)
    {
        int cx = (int)Math.Floor(at.X), cy = (int)Math.Floor(at.Y), cz = (int)Math.Floor(at.Z);
        var stone = _content.GetBlock("stone")!.NumericId;
        for (int x = cx - 12; x <= cx + 12; x++)
        {
            for (int z = cz - 12; z <= cz + 12; z++)
            {
                for (int y = cy - 3; y <= cy - 1; y++)
                {
                    server.World.SetBlock(new Vector3i(x, y, z), stone);
                }

                for (int y = cy; y <= cy + 4; y++)
                {
                    server.World.SetBlock(new Vector3i(x, y, z), BlockId.Air);
                }
            }
        }
    }

    [Theory]
    [InlineData(GameMode.Creative, false, false)] // a Sandbox world
    [InlineData(GameMode.Survival, true, false)]  // /mode Kid creative in a survival world
    [InlineData(GameMode.Survival, false, true)]  // Explorer: the animal hunts and bites
    public void AnAngryAnimal_NeitherHuntsNorBitesASandboxPlayer(GameMode mode, bool creativeOverride, bool attacked)
    {
        var server = NewServer("beast_" + mode + creativeOverride, mode, r =>
        {
            r.CreatureAbundance = AlienActivity.Normal; // a roster to pick the animal from
            r.PlanetEnemies = AlienActivity.Normal;
        }, seed: 4242);
        var (session, feet) = Pad(server, "Walker");
        var p = session.State;
        if (creativeOverride)
        {
            p.ModeOverride = PlayerModeOverride.Creative;
        }

        var species = server.SpeciesRoster
            .Where(sp => CreatureMotion.ClassOf(sp) == MotionClass.Walker && !CreatureMotion.IsGiant(sp)
                && sp.BodyPlan == CreatureBodyPlan.Standard && !ArachnidRules.Lurks(sp))
            .OrderBy(sp => sp.Size)
            .First();
        species.Temperament = CreatureTemperament.Aggressive; // a hunter (a seed's roster may roll none)
        string id = server.SpawnCreatureAtForTest(new Vector3f(2.5f, feet, 0.5f), species.Id); // in reach
        var animal = server.Creatures.First(e => e.Id == id);
        animal.HullMax = animal.Hull = 200f;
        animal.DamagePerSecond = 20f;
        animal.ProvokeTimer = 60.0;       // angry — it hunts like an aggressor
        animal.AwakeOverrideTimer = 60.0; // and awake, whatever the time of day
        p.Health = 100f;

        bool chased = false;
        for (int i = 0; i < 10; i++)
        {
            server.Tick(0.1);
            chased |= animal.ChaseTimer > 0;
        }

        if (attacked)
        {
            Assert.True(chased, "an angry animal hunts an Explorer");
            Assert.True(p.Health < 100f, "and bites");
            Assert.False(double.IsNegativeInfinity(session.LastCombatAt));
        }
        else
        {
            Assert.False(chased, "nothing hunts a Sandbox player");
            Assert.Equal(100f, p.Health);
            Assert.Equal(0, session.Sync.LastHitSeq); // no hit marker
            Assert.True(double.IsNegativeInfinity(session.LastCombatAt), "no combat grace");
            Assert.DoesNotContain("in_combat", server.ReturnToShipRefusalForTest("Walker") ?? string.Empty);
        }
    }

    /// <summary>A stone pad eight blocks above every natural top around the origin, six blocks of air over it, the player
    /// standing on it at (0.5, pad + 1, 0.5). Returns the player and the pad's feet level.</summary>
    private (PlayerSession Player, int Feet) Pad(SvGameServer server, string name)
    {
        var stone = _content.GetBlock("stone")!.NumericId;
        int top = int.MinValue;
        for (int x = -14; x <= 16; x += 2)
        {
            for (int z = -8; z <= 8; z += 2)
            {
                top = Math.Max(top, AboveGroundAt(server, x, z) - 1);
            }
        }

        int padY = top + 8;
        for (int x = -10; x <= 12; x++)
        {
            for (int z = -4; z <= 4; z++)
            {
                server.World.SetBlock(new Vector3i(x, padY, z), stone);
                for (int y = padY + 1; y <= padY + 6; y++)
                {
                    server.World.SetBlock(new Vector3i(x, y, z), BlockId.Air);
                }
            }
        }

        var player = server.AddLocalPlayer(name);
        player.State.AboardShip = false;
        player.State.Position = new Vector3f(0.5f, padY + 1, 0.5f);
        return (player, padY + 1);
    }

    private static int AboveGroundAt(SvGameServer server, int x, int z)
    {
        for (int y = 200; y > -200; y--)
        {
            if (!server.World.GetBlock(new Vector3i(x, y, z)).IsAir)
            {
                return y + 1;
            }
        }

        return 1;
    }

    [Fact]
    public void ThePerPlayerCreativeOverride_IsSandboxForThatPlayerOnly()
    {
        var link = new LoopbackLink();
        using var st = new LoopbackServerTransport(link);
        using var client = new LoopbackClientTransport(link);
        var server = NewServer("mixed", GameMode.Survival, transport: st);
        client.Connect("loopback", 0);
        client.Send(NetCodec.Encode(new JoinRequest { ContentFingerprint = TestJoin.Fingerprint, PlayerName = "Papa" }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        var papa = server.Sessions.Values.First(s => s.State.Name == "Papa").State;
        var kid = server.AddLocalPlayer("Kid").State;

        // The world admin's /mode Kid creative.
        client.Send(NetCodec.Encode(new AdminCommandIntent { Command = "set_mode", TargetPlayer = "Kid", StringArg = "creative" }),
            DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        Assert.Equal(PlayerModeOverride.Creative, kid.ModeOverride);

        // Both on a spacewalk: only the parent's tank drains.
        Spacewalk(papa);
        Spacewalk(kid);
        papa.Oxygen = kid.Oxygen = 50f;
        server.Tick(2.0);
        Assert.True(papa.Oxygen < 50f, "the survival parent still spends the tank");
        Assert.True(kid.Oxygen > 50f, $"the Sandbox kid's tank went {kid.Oxygen}");

        // Both in the same lava: only the parent burns.
        papa.InEva = kid.InEva = false;
        papa.AboardShip = kid.AboardShip = false;
        var lava = StandInLava(server);
        papa.Position = kid.Position = lava;
        papa.Health = kid.Health = 100f;
        server.Tick(1.0);
        Assert.True(papa.Health < 100f, "lava burns the survival parent");
        Assert.Equal(100f, kid.Health);
    }

    public void Dispose()
    {
        try
        {
            foreach (var r in _repos)
            {
                r.Dispose();
            }

            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch { }
    }
}
