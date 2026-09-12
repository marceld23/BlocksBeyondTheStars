// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// P6 finale server backbone: the arc completing reveals the Guardian system; the core is hacked open (a
/// channel) and then argued into shutdown via the dialogue duel — never destroyed by weapons. Winning the duel
/// pacifies the galaxy (the same one-way flag as <see cref="SvGameServer.MarkGuardianDefeatedForTest"/>).
/// </summary>
public sealed class GameServerFinaleTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public GameServerFinaleTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_finale_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Started(out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "rocky"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = "rocky",
            Seed = 4242,
            StartPlanet = "rocky",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
        };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    /// <summary>Drives the shared story to full completion (every beat revealed) via milestones.</summary>
    private static void CompleteTheArc(SvGameServer server)
    {
        for (int i = 0; i < 300 && !server.IsGuardianSystemRevealedForTest; i++)
        {
            server.RecordStoryMilestoneForTest();
        }
    }

    [Fact]
    public void Completing_the_arc_reveals_the_guardian_system_once()
    {
        var server = Started(out var repo);
        using (repo)
        {
            server.AddLocalPlayer("Pilot");
            Assert.False(server.IsGuardianSystemRevealedForTest);

            CompleteTheArc(server);

            Assert.True(server.IsGuardianSystemRevealedForTest);
            Assert.True(server.StorySnapshot.BeatsRevealed >= 13); // the whole 13-beat arc is spoken
        }
    }

    [Fact]
    public void The_core_cannot_be_hacked_before_the_system_is_revealed()
    {
        var server = Started(out var repo);
        using (repo)
        {
            server.AddLocalPlayer("Pilot");

            for (int i = 0; i < 20; i++)
            {
                server.CoreHackTickForTest("Pilot");
            }

            Assert.False(server.IsCoreHackedForTest); // no hack until the finale is in reach
        }
    }

    /// <summary>Reveals the finale, places the player on the Guardian Core, and channels the hack open.</summary>
    private static void ReachAndHackTheCore(SvGameServer server, BlocksBeyondTheStars.GameServer.PlayerSession pilot)
    {
        CompleteTheArc(server);
        pilot.CurrentLocationId = SvGameServer.GuardianCoreBodyId; // landed on the core (the hack is location-gated)
        for (int i = 0; i < 20 && !server.IsCoreHackedForTest; i++)
        {
            server.CoreHackTickForTest("Pilot");
        }
    }

    [Fact]
    public void The_hack_only_channels_while_the_player_is_at_the_core()
    {
        var server = Started(out var repo);
        using (repo)
        {
            server.AddLocalPlayer("Pilot"); // still on the home world, not the Guardian Core
            CompleteTheArc(server);

            for (int i = 0; i < 20; i++)
            {
                server.CoreHackTickForTest("Pilot");
            }

            Assert.False(server.IsCoreHackedForTest); // ticks off-core do nothing
        }
    }

    [Fact]
    public void Channelling_the_hack_opens_the_core_then_the_duel_can_begin()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Pilot");
            ReachAndHackTheCore(server, pilot);

            Assert.True(server.IsCoreHackedForTest);
            Assert.Equal(0, server.DuelNodeForTest);          // the duel opened at the first node
            Assert.False(server.StorySnapshot.Defeated);      // not won merely by hacking
        }
    }

    [Fact]
    public void A_wrong_rebuttal_does_not_advance_the_duel_and_cannot_lose_it()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Pilot");
            ReachAndHackTheCore(server, pilot);
            Assert.True(server.IsCoreHackedForTest);

            // node 0's correct rebuttal is index 0; pick a wrong one repeatedly.
            server.CoreDialogueChoiceForTest("Pilot", 1);
            server.CoreDialogueChoiceForTest("Pilot", 2);

            Assert.Equal(0, server.DuelNodeForTest);     // still stuck on the first node
            Assert.False(server.StorySnapshot.Defeated); // the duel can stall but never be lost
        }
    }

    [Fact]
    public void Winning_the_argument_duel_shuts_down_the_core_and_pacifies_the_galaxy()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Pilot");
            ReachAndHackTheCore(server, pilot);
            Assert.True(server.IsCoreHackedForTest);

            // The authored correct (contradiction) rebuttals, in order: node0=0, node1=1, node2=2, node3=0.
            server.CoreDialogueChoiceForTest("Pilot", 0);
            Assert.Equal(1, server.DuelNodeForTest);
            server.CoreDialogueChoiceForTest("Pilot", 1);
            Assert.Equal(2, server.DuelNodeForTest);
            server.CoreDialogueChoiceForTest("Pilot", 2);
            Assert.Equal(3, server.DuelNodeForTest);

            Assert.False(server.StorySnapshot.Defeated); // not yet — one node to go
            server.CoreDialogueChoiceForTest("Pilot", 0);

            Assert.True(server.StorySnapshot.Defeated);  // the core is argued into shutdown (pacification)
        }
    }

    [Fact]
    public void Revealing_the_finale_adds_a_landable_guardian_system_to_the_galaxy()
    {
        var server = Started(out var repo);
        using (repo)
        {
            server.AddLocalPlayer("Pilot");
            Assert.False(server.GalaxyHasGuardianSystemForTest); // hidden until the arc completes

            CompleteTheArc(server);

            Assert.True(server.GalaxyHasGuardianSystemForTest);   // now a jump target on the star map
            Assert.True(server.GuardianCoreIsLandableForTest);    // with a landable core body to set down on
        }
    }

    [Fact]
    public void The_finale_body_stamps_a_core_chamber_and_no_random_structures()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var center = server.LoadGuardianCoreForTest();

            Assert.True(server.HasCoreChamberForTest);           // the inner core chamber is stamped
            Assert.Empty(server.VaultEntrances);                 // and NO random vaults/structures on the finale body
        }
    }

    [Fact]
    public void The_breach_only_channels_once_the_player_has_reached_the_core_chamber()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var center = server.LoadGuardianCoreForTest();

            // Standing at the terminal → in range; far out on the surface → not.
            Assert.True(server.IsWithinCoreChamberForTest(new Vector3f(center.X, center.Y, center.Z)));
            Assert.False(server.IsWithinCoreChamberForTest(new Vector3f(center.X + 40f, center.Y + 24f, center.Z)));
        }
    }

    [Fact]
    public void The_guardian_system_fields_an_elite_gauntlet_wave()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var (count, maxHull) = server.GuardianGauntletPreviewForTest();
            Assert.True(count >= 10, "the finale gauntlet should be a dense elite wave");
            Assert.True(maxHull >= 200f, "the gauntlet should include a heavy cruiser anchor");
        }
    }

    [Fact]
    public void A_death_in_the_guardian_system_respawns_at_the_pre_finale_world()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Pilot");
            string priorWorld = pilot.CurrentLocationId; // the world they launched into the finale from
            CompleteTheArc(server);

            // Simulate having jumped INTO the finale from priorWorld.
            server.RecordFinaleReturnForTest("Pilot", priorWorld);

            // Dying with the ship parked at the core would normally respawn there — the rule redirects home.
            Assert.Equal(priorWorld, server.ResolveRespawnHomeForTest("Pilot", SvGameServer.GuardianCoreBodyId));

            // The record is consumed (one-shot): a second death there falls back to the ship's location.
            Assert.Equal(SvGameServer.GuardianCoreBodyId,
                server.ResolveRespawnHomeForTest("Pilot", SvGameServer.GuardianCoreBodyId));

            // Outside the finale the rule never fires — a normal death respawns at the ship as usual.
            server.RecordFinaleReturnForTest("Pilot", priorWorld);
            Assert.Equal(priorWorld, server.ResolveRespawnHomeForTest("Pilot", priorWorld));
        }
    }

    [Fact]
    public void A_choice_before_the_hack_does_nothing()
    {
        var server = Started(out var repo);
        using (repo)
        {
            server.AddLocalPlayer("Pilot");
            CompleteTheArc(server);

            server.CoreDialogueChoiceForTest("Pilot", 0); // duel not open yet (not hacked)

            Assert.False(server.IsCoreHackedForTest);
            Assert.False(server.StorySnapshot.Defeated);
        }
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
            // best-effort temp cleanup
        }
    }
    // ---------------- Player reports 2026-09-12: the core is breached, not dug out (#1830 / #1832 / #1838) ----------------

    [Fact]
    public void The_core_column_cannot_be_mined()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Pilot");
            var centre = server.LoadGuardianCoreForTest();
            pilot.CurrentLocationId = SvGameServer.GuardianCoreBodyId;
            pilot.State.Position = new Vector3f(centre.X + 2.5f, centre.Y + 0.5f, centre.Z + 2.5f);

            var core = new Vector3i(centre.X, centre.Y, centre.Z); // the light column at the heart of the chamber
            ushort before = server.World.GetBlock(core).Value;
            Assert.NotEqual(0, before);
            Assert.True(server.IsGuardianCoreProtectedForTest(core.X, core.Y, core.Z));

            server.MineBlock("Pilot", core.X, core.Y, core.Z); // a bare hand — the column is a 0.5-hardness light block
            Assert.Equal(before, server.World.GetBlock(core).Value);

            // The shell around it stays diggable (Route B) and so does the world beyond the chamber.
            Assert.False(server.IsGuardianCoreProtectedForTest(core.X + 5, core.Y, core.Z));
            Assert.False(server.IsGuardianCoreProtectedForTest(core.X + 8, core.Y, core.Z + 8));
        }
    }

    [Fact]
    public void The_finale_objective_beats_the_tutorial_chip_once_the_system_is_revealed()
    {
        var server = Started(out var repo);
        using (repo)
        {
            server.AddLocalPlayer("Pilot");
            Assert.StartsWith("vega.obj.", server.ObjectiveKeyForTest("Pilot")); // a fresh player is in VEGA's onboarding

            CompleteTheArc(server);
            Assert.Equal("story.obj.finale", server.ObjectiveKeyForTest("Pilot")); // …until the finale is on the map
        }
    }

    [Fact]
    public void A_flying_player_takes_no_fall_damage()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Pilot");
            pilot.State.Health = 100f;
            pilot.State.Fly = true;
            server.FallDamageForTest("Pilot", 18f); // over the safe 14 — a hard landing on foot
            Assert.Equal(100f, pilot.State.Health);

            pilot.State.Fly = false;
            server.FallDamageForTest("Pilot", 18f);
            Assert.True(pilot.State.Health < 100f, "without flight the same landing hurts");
        }
    }
}
