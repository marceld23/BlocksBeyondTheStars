// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #2972 on the wire: the real client hears whom it may hit (<see cref="PvpTargets"/>) and its attack on
/// <c>player:&lt;id&gt;</c> reaches the real server, which hurts that player — through the real codec, so every new message
/// is registered.
/// </summary>
public sealed class PvpClientTests
{
    [Fact]
    public void TheClient_HearsWhomItMayHit_AndItsAttackHurtsThatPlayer()
    {
        var content = ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir());
        using var h = new ClientServerHarness(content, c =>
        {
            c.StartPlanet = "rocky";
            c.Rules.Pvp = PvpMode.On;
            c.Rules.PlanetEnemies = AlienActivity.Off;
            c.Rules.Bandits = AlienActivity.Off;
            c.Rules.CreatureAbundance = AlienActivity.Off;
        }, passObjects: false);
        var heard = new List<string>();
        // A local test player shares the harness's connection, so the client hears Jonah's list too — look for its own.
        h.Client.PvpTargetsReceived += m => heard.Add(string.Join(",", m.PlayerIds));
        h.Join("Justus");
        Assert.NotNull(h.JoinAccepted);

        var me = h.Server.Sessions.Values.Single(s => s.State.PlayerId == h.JoinAccepted!.PlayerId);
        var other = h.Server.AddLocalPlayer("Jonah");
        var at = me.State.RespawnPoint;
        foreach (var (s, dx) in new[] { (me, 0f), (other, 2f) })
        {
            s.State.AboardShip = false;
            s.State.Position = new Vector3f(at.X + 30f + dx, at.Y + 40f, at.Z + 30f);
            h.Server.ClearPvpProtectionForTest(s.State.PlayerId);
        }

        h.Server.TickPvpForTest();
        Assert.True(h.PumpUntil(() => heard.Contains("Jonah")), "the client never heard it may hit Jonah: [" + string.Join(" / ", heard) + "]");

        h.Client.SendAttackEntity(PvpRules.TargetId("Jonah"), new Vector3f(1f, 0f, 0f));
        Assert.True(h.PumpUntil(() => other.State.Health < 100f), "the attack never hurt Jonah");
    }
}
