// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Story;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3900/#3901 end to end: the real NetworkClient against the real in-process server while an escort cruiser is in the
/// flight. What the flight view and the HUD build on must hold on the wire — the boss status arrives before the state
/// that carries the cruiser (so the model is built as the boss at first sight), the client's <see cref="BossBarState"/>
/// shows and ends the bar from the real messages, and the mega laser's charge and shot carry the style and the points
/// the aiming line and the beam are drawn from.
/// </summary>
[Trait("Suite", "ClientCore")]
public sealed class EscortCruiserClientTests
{
    private const string Cannon = "ship_cannon_1";

    private static GameContent LoadContent() => ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir());

    /// <summary>What the Unity client keeps of a flight with a boss, fed by the real client's events.</summary>
    private sealed class Flight
    {
        public string Id = string.Empty;
        public SpaceState? Last;
        public float Clock;
        public readonly BossBarState Bar = new BossBarState();
        public readonly List<BossStatus> Statuses = new List<BossStatus>();
        public readonly List<SpaceHeavyShot> Shots = new List<SpaceHeavyShot>();
        public readonly List<SpaceWarpFx> Warps = new List<SpaceWarpFx>();

        /// <summary>The boss was already named by a status when the first state with its entity arrived.</summary>
        public bool? NamedBeforeSeen;
    }

    private static ClientServerHarness NewHarness() => new ClientServerHarness(LoadContent(), c =>
    {
        c.Rules.FreeSpaceFlight = true;
        c.Rules.SpaceCombat = SpaceCombatMode.PvE;
        c.Rules.ShipWeapons = ShipWeaponMode.NpcsOnly;
        c.Rules.SpaceNpcEnemies = AlienActivity.Normal;
        c.Rules.AlienUfos = AlienActivity.Off;
        c.Rules.Bandits = AlienActivity.Off;
        c.Rules.StoryId = StoryRegistry.NoneStoryId;
    });

    private static Flight Launch(ClientServerHarness h)
    {
        var flight = new Flight();
        h.Client.BossStatusReceived += s =>
        {
            flight.Statuses.Add(s);
            flight.Bar.Receive(s, flight.Clock);
        };
        h.Client.SpaceHeavyShotReceived += flight.Shots.Add;
        h.Client.SpaceWarpReceived += flight.Warps.Add;
        h.Client.SpaceStateReceived += s =>
        {
            flight.Last = s;
            var cruiser = s.Entities.FirstOrDefault(e => e.Kind == EscortCruiserLook.Kind);
            if (cruiser != null && flight.NamedBeforeSeen == null)
            {
                flight.NamedBeforeSeen = flight.Bar.IsSpaceBoss(cruiser.Id, flight.Clock);
            }
        };
        h.Join("Pilot");
        flight.Id = h.JoinAccepted!.PlayerId;
        h.Server.Sessions.Values.First(s => s.State.PlayerId == flight.Id).State.AboardShip = true;
        h.Server.Ship.Modules.Add(Cannon);
        h.Server.EnterSpace(flight.Id);
        Assert.True(h.PumpUntil(() => flight.Last != null, 10), "the client should receive its space state");
        h.Server.ShipMove(flight.Id, 0f, 0f, 0f);
        return flight;
    }

    private static bool Pump(ClientServerHarness h, Flight flight, Func<bool> until, int maxTicks = 60, double dt = 0.1)
    {
        for (int i = 0; i < maxTicks && !until(); i++)
        {
            h.Tick(dt);
            flight.Clock += (float)dt;
        }

        return until();
    }

    private static CombatEntity SpawnBoss(ClientServerHarness h, Flight flight, float x)
    {
        var boss = h.Server.SpawnEscortCruiserForTest(flight.Id, at: new Vector3f(x, 0f, 0f));
        Assert.NotNull(boss);
        boss!.Boss!.Speed = 0.001f; // it stands where the test put it
        return boss;
    }

    [Fact]
    public void TheWarpIn_NamesTheBossBeforeItsEntityArrives_AndTheBarShowsIt()
    {
        using var h = NewHarness();
        var flight = Launch(h);
        var boss = SpawnBoss(h, flight, 200f);
        boss.DamagePerSecond = 0f;
        boss.HeavyDamage = 0f;

        Assert.True(Pump(h, flight, () => flight.Last!.Entities.Any(e => e.Id == boss.Id)), "the cruiser should reach the client");
        Assert.True(flight.NamedBeforeSeen, "the boss status comes before the state that carries the cruiser");

        var entity = Assert.Single(flight.Last!.Entities, e => e.Id == boss.Id);
        Assert.Equal(EscortCruiserLook.Kind, entity.Kind);
        Assert.Equal(EscortCruiserRules.ModelScale, entity.Scale);
        Assert.True(EscortCruiserLook.IsEscortCruiser(entity.Kind, entity.Scale, flight.Bar.IsSpaceBoss(entity.Id, flight.Clock)));
        Assert.True(EscortCruiserLook.IsEscortCruiser(entity.Kind, entity.Scale, namedByBossStatus: false)); // …and by its size alone

        Assert.True(flight.Bar.TryGetBar(null, flight.Clock, inFlight: true, out var bar));
        Assert.Equal(boss.Id, bar.Id);
        Assert.Equal(EscortCruiserRules.NameKey, bar.NameKey);
        Assert.Equal(BossSources.Space, bar.Source);
        Assert.Equal(1f, bar.Health);
        Assert.True(bar.Shield > 0.99f, "it arrives with its full shield");
        Assert.False(bar.Enraged);
        Assert.True(flight.Bar.SpaceBossFighting(flight.Clock)); // the boss music's cue

        var warp = Assert.Single(flight.Warps, w => w.Style == EscortCruiserRules.WarpStyle);
        Assert.True(warp.Arriving);

        // The statuses keep coming about once a second, so the bar never times out while the pilot stays.
        int heard = flight.Statuses.Count;
        Assert.True(Pump(h, flight, () => flight.Statuses.Count >= heard + 3, 60), "the status beat goes on");
        Assert.True(flight.Bar.TryGetBar(null, flight.Clock, inFlight: true, out _));
    }

    [Fact]
    public void TheMegaLaser_ArrivesWithItsStyle_ItsOriginAndItsAim()
    {
        using var h = NewHarness();
        var flight = Launch(h);
        var boss = SpawnBoss(h, flight, 60f);
        boss.HeavyContact = true;
        boss.HeavyNextAt = 0.0001; // its next mega laser is due at once

        Assert.True(Pump(h, flight, () => flight.Shots.Count > 0, 40), "the charge should be announced");
        var charge = flight.Shots[0];
        Assert.Equal(SpaceCombatRules.HeavyPhaseCharge, charge.Phase);
        Assert.True(MegaLaserAim.IsMegaLaser(charge.Style));
        Assert.Equal(boss.Id, charge.EntityId);
        Assert.Equal(flight.Id, charge.TargetPlayerId);
        Assert.InRange(charge.Seconds, 2f, 4f); // a longer warning than the classic heavy shot's
        Assert.True(charge.Seconds > SpaceCombatRules.HeavyChargeSeconds);
        Assert.Equal(60f, charge.OriginX, 0); // the cruiser's centre — the client adds the barrel's offset
        Assert.Equal(0f, charge.TargetX, 0);  // the pilot's ship
        Assert.True(charge.Range > 60f, "the pilot is inside its reach");

        Assert.True(Pump(h, flight, () => flight.Shots.Count > 1, 60), "it fires when the charge is over");
        var shot = flight.Shots[1];
        Assert.True(shot.Phase == SpaceCombatRules.HeavyPhaseHit || shot.Phase == SpaceCombatRules.HeavyPhaseMiss);
        Assert.True(MegaLaserAim.IsMegaLaser(shot.Style));
        Assert.Equal(boss.Id, shot.EntityId);
    }

    [Fact]
    public void TheBar_FollowsTheFight_TurnsEnraged_AndEndsWhenTheBossFalls()
    {
        using var h = NewHarness();
        var flight = Launch(h);
        var boss = SpawnBoss(h, flight, 30f);
        boss.DamagePerSecond = 0f;
        boss.HeavyDamage = 0f;
        boss.Shield = 0f;
        Assert.True(Pump(h, flight, () => flight.Bar.TryGetBar(null, flight.Clock, true, out _)), "the bar should show");

        boss.Hull = boss.HullMax * 0.4f; // below the share it enrages at
        Assert.True(Pump(h, flight, () => flight.Bar.PhaseOf(boss.Id, flight.Clock) == EscortCruiserRules.PhaseEnraged),
            "the second phase should reach the client");
        Assert.True(flight.Bar.TryGetBar(null, flight.Clock, true, out var bar));
        Assert.True(bar.Enraged);
        Assert.Equal(0.4f, bar.Health, 2);
        Assert.Equal(0f, bar.Shield);

        boss.Hull = 1f;
        h.Server.FireWeapon(flight.Id, Cannon, boss.Id);
        Assert.True(Pump(h, flight, () => flight.Bar.EndCount > 0), "the last status should reach the client");
        Assert.True(flight.Bar.LastEndDefeated);
        Assert.Equal(boss.Id, flight.Bar.LastEndedId);
        Assert.False(flight.Bar.TryGetBar(null, flight.Clock, true, out _));
        Assert.False(flight.Bar.SpaceBossFighting(flight.Clock)); // the music ends
        Assert.True(Pump(h, flight, () => flight.Last!.Entities.All(e => e.Id != boss.Id)), "the cruiser leaves the state");
    }
}
