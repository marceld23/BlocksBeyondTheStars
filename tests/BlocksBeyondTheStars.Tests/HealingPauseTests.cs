// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.IO;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The pause of the body's own healing after a hit (#3794): on a breathable world health comes back at 2 a second, and
/// it used to come back in the middle of a fight too — an attacker that dealt no more than that never moved the health
/// bar. A hit (damage armour works against) now makes the healing wait <see cref="NaturalHealing.PauseAfterHitSeconds"/>;
/// damage of the surroundings does not.
/// </summary>
public sealed class HealingPauseTests : IDisposable
{
    private const int Cx = 300, Cz = 300;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_healpause_" + Guid.NewGuid().ToString("N"));
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    public void Dispose() => SoundersStage.Delete(_root);

    /// <summary>A player on foot on a pad of a breathable world, at half health, with no animal that bites.</summary>
    private (SvGameServer Server, SqliteWorldRepository Repo, PlayerSession Player) Stage(string name)
    {
        var server = SoundersStage.Started(_root, name, _content, out var repo);
        SoundersStage.Pacify(server);
        int padY = SoundersStage.Pad(server, _content, Cx, Cz, 12);
        var p = SoundersStage.OnFoot(server, "Kid", new Vector3f(Cx + 0.5f, padY + 1, Cz + 0.5f));
        server.TickForTest(0.1);
        p.State.Health = 50f;
        return (server, repo, p);
    }

    private static void Tick(SvGameServer server, double seconds)
    {
        for (int i = 0; i < (int)Math.Round(seconds * 10.0); i++)
        {
            server.TickForTest(0.1);
        }
    }

    [Fact]
    public void TheRule_WaitsFiveSeconds_AndALaterHitNeverShortensARunningWait()
    {
        Assert.Equal(5.0, NaturalHealing.PauseAfterHitSeconds); // "a few seconds" — the maintainer's decision
        Assert.False(NaturalHealing.Paused(now: 10.0, pausedUntil: 0.0)); // never hit
        Assert.True(NaturalHealing.Paused(10.0, 12.0));
        Assert.False(NaturalHealing.Paused(12.0, 12.0)); // the wait is over at its moment
        Assert.Equal(15.0, NaturalHealing.PauseUntil(now: 10.0, pausedUntil: 0.0));
        Assert.Equal(15.0, NaturalHealing.PauseUntil(10.0, 12.0)); // a second hit starts the wait again
        Assert.Equal(40.0, NaturalHealing.PauseUntil(10.0, 40.0)); // … but never ends one early
    }

    [Fact]
    public void AHit_MakesTheHealingWait_ThenItGoesOnAtTheOldRate()
    {
        var (server, repo, p) = Stage("wait");
        using (repo)
        {
            Tick(server, 1.0);
            Assert.InRange(p.State.Health, 51.5f, 52.5f); // the stage heals: 2 a second in breathable air
            Assert.False(server.HealingPausedForTest(p.State.PlayerId));

            float lost = server.HitForTest(p.State.PlayerId, 10f);
            Assert.Equal(10f, lost, 3);
            float afterHit = p.State.Health;
            Assert.True(server.HealingPausedForTest(p.State.PlayerId));

            Tick(server, 4.5);
            Assert.Equal(afterHit, p.State.Health, 3); // not one point came back inside the wait

            Tick(server, 1.5); // the wait ran out a second ago
            Assert.False(server.HealingPausedForTest(p.State.PlayerId));
            Assert.InRange(p.State.Health, afterHit + 1.0f, afterHit + 2.6f); // and it heals at 2 a second again
        }
    }

    [Fact]
    public void AnotherHitInsideTheWait_StartsItAgain()
    {
        var (server, repo, p) = Stage("again");
        using (repo)
        {
            server.HitForTest(p.State.PlayerId, 5f);
            Tick(server, 3.0);
            server.HitForTest(p.State.PlayerId, 5f);
            float afterSecond = p.State.Health;

            Tick(server, 3.0); // six seconds after the first hit, three after the second
            Assert.Equal(afterSecond, p.State.Health, 3);

            Tick(server, 3.0);
            Assert.True(p.State.Health > afterSecond + 1f, "the wait counted from the last hit");
        }
    }

    [Fact]
    public void AWeakAttacker_NowLowersTheHealthBar()
    {
        var (server, repo, p) = Stage("weak");
        using (repo)
        {
            // 2 health a second in ticks — exactly what the healing gave back, so the bar used to stand still.
            float before = p.State.Health;
            for (int i = 0; i < 50; i++)
            {
                server.HitForTest(p.State.PlayerId, 0.2f);
                server.TickForTest(0.1);
            }

            Assert.InRange(p.State.Health, before - 10.5f, before - 9.5f);
        }
    }

    [Fact]
    public void DamageOfTheSurroundings_DoesNotPauseTheHealing()
    {
        var (server, repo, p) = Stage("surroundings");
        using (repo)
        {
            server.HurtForTest(p.State.PlayerId, 10f); // thin air, cold, hunger: not what armour works against
            float after = p.State.Health;
            Assert.False(server.HealingPausedForTest(p.State.PlayerId));

            Tick(server, 1.0);
            Assert.InRange(p.State.Health, after + 1.5f, after + 2.5f);
        }
    }

    [Fact]
    public void AHitThatTookNothing_DoesNotPause()
    {
        var (server, repo, p) = Stage("nothing");
        using (repo)
        {
            p.State.GodMode = true; // nothing hurts: no health lost, nothing to recover from
            Assert.Equal(0f, server.HitForTest(p.State.PlayerId, 10f));
            Assert.False(server.HealingPausedForTest(p.State.PlayerId));
        }
    }
}
