// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #2665: the creature motion stream, real <see cref="NetworkClient"/> against the real server over the ENCODED wire (the
/// path a socket takes, so the new message must be registered and round-trip). The 2 Hz creature list alone aliased every
/// quick move into straight segments; nearby animals now arrive every second tick with their velocity and heading.
/// </summary>
public sealed class CreatureMotionStreamTests
{
    private static GameContent LoadContent() => ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir());

    [Fact]
    public void ANearbyAnimal_StreamsEverySecondTick_AFarOneRidesOnlyTheList()
    {
        using var h = new ClientServerHarness(LoadContent(), c => c.StartPlanet = "jungle", passObjects: false);
        var frames = new List<CreatureMotionFrame>();
        var lists = new List<CreatureList>();
        h.Client.CreatureMotionReceived += frames.Add;
        h.Client.CreaturesReceived += lists.Add;
        h.Join("Watcher");
        Assert.NotNull(h.JoinAccepted);
        var session = h.Server.Sessions.Values.Single(s => s.State.PlayerId == h.JoinAccepted!.PlayerId);
        session.State.AboardShip = false;
        var at = session.State.Position;

        string near = h.Server.SpawnCreatureAtForTest(new Vector3f(at.X + 3f, at.Y, at.Z));
        string far = h.Server.SpawnCreatureAtForTest(new Vector3f(at.X + 60f, at.Y, at.Z));
        frames.Clear();
        lists.Clear();
        for (int i = 0; i < 20; i++)
        {
            session.State.Position = at; // hold the player still: the radius test is about the animals
            h.Tick(0.1);
        }

        // Every second 0.1 s tick → ~10 frames in 2 s, each naming the near animal and never the far one.
        Assert.InRange(frames.Count, 8, 11);
        foreach (var f in frames)
        {
            Assert.Contains(near, f.Ids);
            Assert.DoesNotContain(far, f.Ids);
            Assert.Equal(f.Ids.Length * CreatureMotionFrame.Stride, f.Data.Length);
        }

        for (int i = 1; i < frames.Count; i++)
        {
            Assert.Equal(0.2, frames[i].ServerTime - frames[i - 1].ServerTime, 3);
        }

        // The packed position is the server's (to the centimetre), and the list carries the clock and the velocity fields.
        var live = h.Server.Creatures.First(c => c.Id == near);
        var lastFrame = frames[^1];
        int k = Array.IndexOf(lastFrame.Ids, near) * CreatureMotionFrame.Stride;
        var lastList = lists.Last();
        Assert.True(lastList.ServerTime > 0);
        if (h.Server.Creatures.Any(c => c.Id == far))
        {
            Assert.Contains(lastList.Creatures, c => c.Id == far); // out of the stream's radius, still on the list
        }
        if (Math.Abs(lastFrame.ServerTime - lastList.ServerTime) < 1e-9)
        {
            var listed = lastList.Creatures.Single(c => c.Id == near);
            Assert.Equal(listed.X, CreatureMotionFrame.FromFixed(lastFrame.Data[k], CreatureMotionFrame.PositionScale), 2);
            Assert.Equal(listed.VelX, CreatureMotionFrame.FromFixed(lastFrame.Data[k + 3], CreatureMotionFrame.VelocityScale), 2);
        }

        Assert.True(Math.Abs(live.Position.X - CreatureMotionFrame.FromFixed(lastFrame.Data[k], CreatureMotionFrame.PositionScale)) < 1.5f,
            "the streamed position is nowhere near the animal");
    }

    [Fact]
    public void TickVelocity_IsTheLastTicksMove_AndATeleportReadsAsStanding()
    {
        var v = default(TickVelocity);
        v.Observe(new Vector3f(0.4f, 64, 10), 1.0, 1000);
        Assert.Equal(0f, v.Velocity.X);

        v.Observe(new Vector3f(0.6f, 64, 10), 1.1, 1000);
        Assert.Equal(2f, v.Velocity.X, 3);

        v.Observe(new Vector3f(0.6f, 64, 10), 1.1, 1000); // the same tick sampled twice keeps the result
        Assert.Equal(2f, v.Velocity.X, 3);

        v.Observe(new Vector3f(999.9f, 64, 10), 1.2, 1000); // across the seam: −0.7 the short way, not +999.3
        Assert.Equal(-7f, v.Velocity.X, 2);

        v.Observe(new Vector3f(500f, 64, 10), 1.3, 1000); // a relocation, not a run
        Assert.Equal(0f, v.Velocity.X);
    }
}
