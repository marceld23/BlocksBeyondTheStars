// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Messages;
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
/// The fallen (#3765 — Justus, 2026-11-17: "a death means you can look at the world but nothing else; in multiplayer the
/// others can bring you back"): the death rule of a Hard Survival world, <see cref="DeathPenalty.Revive"/>, on a live
/// server. A player who dies on foot watches instead of waking up at the ship; a team-mate's medkit at the place of death
/// brings them back; alone they may start again at the ship after a minute, and at once when everybody has fallen. Every
/// other world keeps its death exactly as it was.
/// </summary>
public sealed class FallenTests : IDisposable
{
    private const int Cx = 300, Cz = 300;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_fallen_" + Guid.NewGuid().ToString("N"));
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    public void Dispose() => SoundersStage.Delete(_root);

    private (SvGameServer Server, SqliteWorldRepository Repo, int PadY) World(string name, DeathPenalty penalty = DeathPenalty.Revive)
    {
        var server = SoundersStage.Started(_root, name, _content, out var repo, c => c.Rules.DeathPenalty = penalty);
        SoundersStage.Pacify(server);
        int padY = SoundersStage.Pad(server, _content, Cx, Cz, 20);
        return (server, repo, padY);
    }

    private static PlayerSession Player(SvGameServer server, string name, int padY, float dx)
    {
        var p = SoundersStage.OnFoot(server, name, new Vector3f(Cx + 0.5f + dx, padY + 1, Cz + 0.5f));
        p.State.RespawnPoint = new Vector3f(Cx + 0.5f, padY + 1, Cz + 15.5f); // "the ship"
        return p;
    }

    // ---- the rules -------------------------------------------------------------------------------------------------

    [Fact]
    public void WhenARestartIsAllowed_IsOneRule()
    {
        Assert.Equal(FallenRules.WaitForTeam, FallenRules.RestartIn(someoneStands: true, everybodyFell: false, fallenFor: 500));
        Assert.Equal((float)FallenRules.AloneSeconds, FallenRules.RestartIn(false, false, 0));
        Assert.Equal(20f, FallenRules.RestartIn(false, false, FallenRules.AloneSeconds - 20), 3);
        Assert.Equal(0f, FallenRules.RestartIn(false, false, FallenRules.AloneSeconds + 5));
        Assert.Equal(0f, FallenRules.RestartIn(someoneStands: true, everybodyFell: true, fallenFor: 0)); // everybody fell once: it stays allowed
        Assert.Equal(60.0, FallenRules.AloneSeconds); // the maintainer's number
    }

    [Fact]
    public void TheFallen_MayMoveTheirCameraAndTalk_NothingElse()
    {
        Assert.True(SvGameServer.FallenMayHandleForTest(new MoveIntent()));
        Assert.True(SvGameServer.FallenMayHandleForTest(new ChatIntent()));
        Assert.True(SvGameServer.FallenMayHandleForTest(new RespawnChoiceIntent())); // the wish to start again — checked by its handler
        Assert.False(SvGameServer.FallenMayHandleForTest(new MineBlockIntent()));    // unlike an observing admin
        Assert.False(SvGameServer.FallenMayHandleForTest(new AttackEntityIntent()));
        Assert.False(SvGameServer.FallenMayHandleForTest(new CarryBlockIntent()));
        Assert.False(SvGameServer.FallenMayHandleForTest(new ThrowFoodIntent()));
    }

    // ---- other worlds ----------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(DeathPenalty.Light)]
    [InlineData(DeathPenalty.Hard)]
    public void InEveryOtherWorld_ADeathWakesUpAtTheShip_AsBefore(DeathPenalty penalty)
    {
        var (server, repo, padY) = World("plain" + penalty, penalty);
        using (repo)
        {
            var p = Player(server, "Kid", padY, 0f);
            server.KillPlayerForTest(p.State.PlayerId);

            Assert.False(server.IsFallenForTest(p.State.PlayerId));
            Assert.True(p.State.AboardShip);
            Assert.Equal(p.State.RespawnPoint, p.State.Position);
            Assert.False(p.State.GodMode);
            Assert.Equal("explorer", server.WorldModeForTest());
        }
    }

    // ---- falling ---------------------------------------------------------------------------------------------------

    [Fact]
    public void ADeathOnFoot_Falls_TheThingsStayThere_AndThePlayerWatches()
    {
        var (server, repo, padY) = World("fall");
        using (repo)
        {
            var a = Player(server, "Kid", padY, 0f);
            Player(server, "Friend", padY, 30f);
            a.State.Inventory.SetSlot(5, new ItemStack("stone", 7));
            var spot = a.State.Position;

            server.KillPlayerForTest(a.State.PlayerId);

            Assert.True(server.IsFallenForTest(a.State.PlayerId));
            Assert.False(a.State.AboardShip);                       // not taken to the ship
            Assert.Equal(spot, a.Respawn.FallenAt);
            Assert.True(a.State.GodMode && a.State.Fly);            // nothing hurts, nothing hunts, the watcher flies
            Assert.Equal(0, a.State.Inventory.CountOf("stone"));    // the things lie at the place of death
            Assert.Contains(server.Containers, c => c.Kind == "salvage_capsule" && c.Items.Any(s => s.Item == "stone" && s.Count == 7));
            Assert.Equal(FallenRules.WaitForTeam, server.FallenRestartInForTest(a.State.PlayerId)); // a friend still stands
            Assert.Equal("hard", server.WorldModeForTest());

            // A watcher cannot die again, and asking for the ship is refused while somebody can still help.
            int capsules = server.Containers.Count(c => c.Kind == "salvage_capsule");
            server.KillPlayerForTest(a.State.PlayerId);
            server.RestartFallenForTest(a.State.PlayerId);
            server.TickForTest(1.0);
            Assert.True(server.IsFallenForTest(a.State.PlayerId));
            Assert.Equal(capsules, server.Containers.Count(c => c.Kind == "salvage_capsule"));
        }
    }

    [Fact]
    public void AFriendsMedkit_AtThePlaceOfDeath_BringsThemBack_ThereAndOnlyThere()
    {
        var (server, repo, padY) = World("revive");
        using (repo)
        {
            var a = Player(server, "Kid", padY, 0f);
            var b = Player(server, "Friend", padY, 30f);
            var spot = a.State.Position;
            a.State.Hunger = 4f;
            server.KillPlayerForTest(a.State.PlayerId);
            a.State.Position = new Vector3f(Cx + 80f, padY + 40f, Cz + 80f); // the watcher flew off

            // A pulse far from the place of death — even right beside the watching camera — does nothing.
            b.State.Position = new Vector3f(Cx + 79f, padY + 40f, Cz + 80f);
            server.MedkitPulseForTest(b.State.PlayerId);
            Assert.True(server.IsFallenForTest(a.State.PlayerId));

            // A watcher's own medkit cannot work (their intents are dropped), and nobody revives themselves.
            Assert.False(SvGameServer.FallenMayHandleForTest(new UseGadgetIntent()));

            b.State.Position = new Vector3f(spot.X + 3f, spot.Y, spot.Z);
            server.MedkitPulseForTest(b.State.PlayerId);

            Assert.False(server.IsFallenForTest(a.State.PlayerId));
            Assert.Equal(spot, a.State.Position);            // stands up where they fell
            Assert.False(a.State.AboardShip);
            Assert.False(a.State.GodMode || a.State.Fly);    // an ordinary player again
            Assert.InRange(a.State.Health, 1f, 99f);         // the medkit's health, not a full reset
            Assert.True(a.State.Hunger >= FallenRules.RevivedHunger);
            Assert.DoesNotContain(SvGameServer.FallenMarkerForTest, a.State.Milestones);
        }
    }

    // ---- nobody left to help ---------------------------------------------------------------------------------------

    [Fact]
    public void Alone_TheShipIsAnOptionAfterAMinute_NotBefore()
    {
        var (server, repo, padY) = World("alone");
        using (repo)
        {
            var a = Player(server, "Kid", padY, 0f);
            server.KillPlayerForTest(a.State.PlayerId);
            Assert.InRange(server.FallenRestartInForTest(a.State.PlayerId), 59f, 60f);

            for (int i = 0; i < 30; i++)
            {
                server.TickForTest(1.0);
            }

            server.RestartFallenForTest(a.State.PlayerId);
            Assert.True(server.IsFallenForTest(a.State.PlayerId), "half a minute is not enough");

            for (int i = 0; i < 31; i++)
            {
                server.TickForTest(1.0);
            }

            Assert.Equal(0f, server.FallenRestartInForTest(a.State.PlayerId));
            server.RestartFallenForTest(a.State.PlayerId);
            Assert.False(server.IsFallenForTest(a.State.PlayerId));
            Assert.True(a.State.AboardShip);
            Assert.Equal(a.State.RespawnPoint, a.State.Position);
            Assert.Equal(100f, a.State.Health);
            Assert.False(a.State.GodMode || a.State.Fly);
        }
    }

    [Fact]
    public void WhenEverybodyHasFallen_AllOfThemMayStartAgain_AtOnce()
    {
        var (server, repo, padY) = World("all");
        using (repo)
        {
            var a = Player(server, "Kid", padY, 0f);
            var b = Player(server, "Friend", padY, 6f);
            server.KillPlayerForTest(a.State.PlayerId);
            Assert.Equal(FallenRules.WaitForTeam, server.FallenRestartInForTest(a.State.PlayerId));

            server.KillPlayerForTest(b.State.PlayerId);
            server.TickForTest(1.0);
            Assert.Equal(0f, server.FallenRestartInForTest(a.State.PlayerId));
            Assert.Equal(0f, server.FallenRestartInForTest(b.State.PlayerId));

            server.RestartFallenForTest(a.State.PlayerId);
            Assert.False(server.IsFallenForTest(a.State.PlayerId));

            // The first one standing again does not lock the second one out.
            server.TickForTest(1.0);
            Assert.Equal(0f, server.FallenRestartInForTest(b.State.PlayerId));
            server.RestartFallenForTest(b.State.PlayerId);
            Assert.False(server.IsFallenForTest(b.State.PlayerId));
            Assert.True(b.State.AboardShip);
        }
    }

    // ---- leaving ---------------------------------------------------------------------------------------------------

    [Fact]
    public void AWatcherWhoLeaves_IsSavedAtTheShip_AndACrashedSaveIsUndone()
    {
        var (server, repo, padY) = World("leave");
        using (repo)
        {
            var a = Player(server, "Kid", padY, 0f);
            Player(server, "Friend", padY, 30f);
            server.KillPlayerForTest(a.State.PlayerId);
            a.State.Position = new Vector3f(Cx + 50f, padY + 60f, Cz); // high in the air
            Assert.Contains(SvGameServer.FallenMarkerForTest, a.State.Milestones);

            server.FallenOnLeaveForTest(a.State.PlayerId);
            Assert.False(a.Respawn.Fallen);
            Assert.Equal(a.State.RespawnPoint, a.State.Position);
            Assert.True(a.State.AboardShip);
            Assert.DoesNotContain(SvGameServer.FallenMarkerForTest, a.State.Milestones);

            // The same for a save that was written while its player watched and never saw the leave (a crash).
            var saved = new PlayerState
            {
                PlayerId = "crashed",
                Position = new Vector3f(1f, 300f, 1f),
                RespawnPoint = new Vector3f(9f, 70f, 9f),
                Health = 100f,
            };
            saved.Milestones.Add(SvGameServer.FallenMarkerForTest);
            SvGameServer.UndoFallenInSaveForTest(saved);
            Assert.Equal(saved.RespawnPoint, saved.Position);
            Assert.True(saved.AboardShip);
            Assert.Empty(saved.Milestones);

            var untouched = new PlayerState { PlayerId = "fine", Position = new Vector3f(1f, 300f, 1f) };
            SvGameServer.UndoFallenInSaveForTest(untouched);
            Assert.Equal(new Vector3f(1f, 300f, 1f), untouched.Position);
        }
    }

    // ---- the mode --------------------------------------------------------------------------------------------------

    [Fact]
    public void TheDeathRule_NamesTheMode_InTheWorldList()
    {
        var hard = new WorldMetadata { RulesOverride = new GameRules { DeathPenalty = DeathPenalty.Revive } };
        Assert.Equal(WorldSaveSummary.ModeHard, WorldSaveSummary.ModeOf(hard));

        var plain = new WorldMetadata { RulesOverride = new GameRules { DeathPenalty = DeathPenalty.Hard } };
        Assert.Equal(WorldSaveSummary.ModeExplorer, WorldSaveSummary.ModeOf(plain));

        var sandbox = new WorldMetadata { RulesOverride = new GameRules { GameMode = GameMode.Creative, DeathPenalty = DeathPenalty.Revive } };
        Assert.Equal(WorldSaveSummary.ModeSandbox, WorldSaveSummary.ModeOf(sandbox)); // nothing dies in a sandbox

        Assert.Equal(4, (int)DeathPenalty.Revive); // appended: the stored values of the others keep their meaning
    }
}
