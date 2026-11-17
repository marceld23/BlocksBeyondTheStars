// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.IO;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Sounders;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Sneaking past sleeping animals (#3751 — Justus, 2026-11-17: "when you sneak, sleeping animals should not notice you") on a
/// live server: a sleeping animal wakes when a standing player comes close, sleeps on while a crouching one does, wakes the
/// moment that player stands up, and one standing player is enough whoever else sneaks. Sneaking is crouching on foot — not
/// with the jetpack running, not in a speeder.
/// </summary>
public sealed class SneakingPastSleepersTests : IDisposable
{
    private const int Cx = 300, Cz = 300;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_sneak_" + Guid.NewGuid().ToString("N"));
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    public void Dispose() => SoundersStage.Delete(_root);

    /// <summary>Night on a pad: a Soundi (a day animal) asleep in the middle, a player on foot ten blocks east of it.</summary>
    private (SvGameServer Server, SqliteWorldRepository Repo, PlayerSession Player, string Sleeper, int PadY) Stage(string name)
    {
        var server = SoundersStage.Started(_root, name, _content, out var repo);
        SoundersStage.Pacify(server);
        var sp = server.EnsureAuthoredSpeciesForTest(SoundersIds.Soundi)!;
        int padY = SoundersStage.Pad(server, _content, Cx, Cz, 14);
        var p = SoundersStage.OnFoot(server, "Kid", new Vector3f(Cx + 10.5f, padY + 1, Cz + 0.5f));
        string id = server.SpawnCreatureAtForTest(new Vector3f(Cx + 0.5f, padY + 1, Cz + 0.5f), sp.Id);
        server.SetLocalDayFractionForTest(0.0, Cx);
        server.TickForTest(0.1);
        return (server, repo, p, id, padY);
    }

    /// <summary>Ticks <paramref name="seconds"/> with the player held <paramref name="away"/> blocks east of the sleeper's spot.</summary>
    private static void Hold(SvGameServer server, PlayerSession p, int padY, float away, bool crouching, double seconds)
    {
        for (int i = 0; i < (int)Math.Round(seconds * 10.0); i++)
        {
            p.State.Position = new Vector3f(Cx + 0.5f + away, padY + 1, Cz + 0.5f);
            p.State.Crouching = crouching;
            server.TickForTest(0.1);
        }
    }

    [Fact]
    public void AStandingPlayer_WakesASleeper_ByComingClose()
    {
        var (server, repo, p, id, padY) = Stage("stand");
        using (repo)
        {
            Hold(server, p, padY, 10f, crouching: false, 1.0);
            Assert.True(server.NetCreatureForTest(id).Asleep, "asleep at night while nobody is near");

            Hold(server, p, padY, 2f, crouching: false, 0.5);
            Assert.False(server.NetCreatureForTest(id).Asleep, "a player walking up to it wakes it");
        }
    }

    [Fact]
    public void ASneakingPlayer_DoesNotWakeIt_UntilTheyStandUp()
    {
        var (server, repo, p, id, padY) = Stage("sneak");
        using (repo)
        {
            Hold(server, p, padY, 2f, crouching: true, 4.0);
            Assert.True(server.NetCreatureForTest(id).Asleep, "sneaking right beside it: it sleeps on");

            Hold(server, p, padY, 2f, crouching: false, 0.5);
            Assert.False(server.NetCreatureForTest(id).Asleep, "standing up beside it wakes it");
        }
    }

    [Fact]
    public void OneStandingPlayer_IsEnough_WhoeverElseSneaks()
    {
        var (server, repo, p, id, padY) = Stage("two");
        using (repo)
        {
            var loud = SoundersStage.OnFoot(server, "Friend", new Vector3f(Cx + 0.5f, padY + 1, Cz + 10.5f));
            Hold(server, p, padY, 1.5f, crouching: true, 2.0); // the sneaker is the nearest player, the friend far off
            Assert.True(server.NetCreatureForTest(id).Asleep);

            loud.State.Position = new Vector3f(Cx + 0.5f, padY + 1, Cz + 3.5f); // the friend walks up, three blocks away
            Hold(server, p, padY, 1.5f, crouching: true, 0.5);
            Assert.False(server.NetCreatureForTest(id).Asleep, "the standing friend wakes it although the nearest player sneaks");
        }
    }

    [Fact]
    public void Sneaking_IsCrouchingOnFoot()
    {
        var (server, repo, p, _, _) = Stage("rule");
        using (repo)
        {
            var s = p.State;
            Assert.False(SvGameServer.Sneaks(s));
            s.Crouching = true;
            Assert.True(SvGameServer.Sneaks(s));
            s.Jetpacking = true;
            Assert.False(SvGameServer.Sneaks(s)); // a running jetpack is loud
            s.Jetpacking = false;
            s.InSpeeder = "speeder-1";
            Assert.False(SvGameServer.Sneaks(s)); // so is a speeder's engine
        }
    }
}
