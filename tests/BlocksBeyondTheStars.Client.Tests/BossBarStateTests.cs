// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client.Music;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3901: the one "active boss" the HUD's bar, the boss music and the flight view read — merged from the guardian giant's
/// own status (which must behave exactly as before) and the generic <see cref="BossStatus"/> of the escort cruiser: it
/// appears, follows the hull, the shield and the phase, ends (defeated, or jumped away), times out when the statuses stop,
/// and with two bosses at once the one of the player's scene keeps the bar.
/// </summary>
public sealed class BossBarStateTests
{
    private const string Boss = "e-77";
    private const string Site = "planet_a#0";

    private static BossStatus Space(float health = 1f, float shield = 1f, byte phase = EscortCruiserRules.PhaseNormal,
        bool active = true, bool defeated = false, string id = Boss)
        => new BossStatus
        {
            Id = id,
            NameKey = EscortCruiserRules.NameKey,
            Health = health,
            Shield = shield,
            Phase = phase,
            Source = BossSources.Space,
            Active = active,
            Defeated = defeated,
        };

    private static GuardianGiantStatus Giant(float hull = 400f, bool rage = false, GuardianGiantMove move = GuardianGiantMove.Walk,
        string site = Site)
        => new GuardianGiantStatus
        {
            SiteId = site,
            EntityId = "gg-" + site,
            Move = (byte)move,
            Hull = hull,
            HullMax = 600f,
            Rage = rage,
        };

    [Fact]
    public void NoBoss_NoBar()
    {
        var state = new BossBarState();
        Assert.False(state.TryGetBar(new GuardianGiantFight(), 5f, inFlight: true, out _));
        Assert.False(state.TryGetBar(null, 5f, inFlight: false, out _));
        Assert.False(state.SpaceBossFighting(5f));
        Assert.Equal(0, state.PhaseOf(Boss, 5f));
        state.Receive(null, 5f);
        state.Receive(new BossStatus { Id = string.Empty, Active = true }, 5f);
        Assert.False(state.TryGetBar(null, 5f, inFlight: true, out _));
    }

    [Fact]
    public void ASpaceBoss_Appears_WithItsNameItsHullAndItsShield()
    {
        var state = new BossBarState();
        state.Receive(Space(), 10f);

        Assert.True(state.TryGetBar(null, 10f, inFlight: true, out var bar));
        Assert.Equal(Boss, bar.Id);
        Assert.Equal("enemy.escort_cruiser.name", bar.NameKey);
        Assert.Equal(BossSources.Space, bar.Source);
        Assert.Equal(1f, bar.Health);
        Assert.Equal(1f, bar.Shield);
        Assert.Equal(EscortCruiserRules.PhaseNormal, bar.Phase);
        Assert.False(bar.Enraged);
        Assert.True(state.IsSpaceBoss(Boss, 10f));
        Assert.False(state.IsSpaceBoss("e-78", 10f));
        Assert.True(state.SpaceBossFighting(10f));
        Assert.True(state.SpeaksFor(Boss, 10f));
    }

    [Fact]
    public void TheBar_FollowsTheStatuses_AndThePhaseNeverGoesBack()
    {
        var state = new BossBarState();
        state.Receive(Space(), 10f);
        state.Receive(Space(health: 0.8f, shield: 0.25f), 10.5f);
        Assert.True(state.TryGetBar(null, 10.6f, inFlight: true, out var bar));
        Assert.Equal(0.8f, bar.Health, 4);
        Assert.Equal(0.25f, bar.Shield, 4);

        state.Receive(Space(health: 0.49f, shield: 0f, phase: EscortCruiserRules.PhaseEnraged), 11f);
        Assert.True(state.TryGetBar(null, 11f, inFlight: true, out bar));
        Assert.True(bar.Enraged);
        Assert.Equal(0f, bar.Shield);
        Assert.Equal(EscortCruiserRules.PhaseEnraged, state.PhaseOf(Boss, 11f));

        state.Receive(Space(health: 0.4f, shield: 0f, phase: EscortCruiserRules.PhaseNormal), 11.5f); // a status out of order
        Assert.Equal(EscortCruiserRules.PhaseEnraged, state.PhaseOf(Boss, 11.5f));

        state.Receive(Space(health: 7f, shield: -3f), 12f); // nonsense on the wire stays inside the bar
        Assert.True(state.TryGetBar(null, 12f, inFlight: true, out bar));
        Assert.Equal(1f, bar.Health);
        Assert.Equal(0f, bar.Shield);
    }

    [Theory]
    [InlineData(true)]  // it was defeated
    [InlineData(false)] // it jumped away
    public void TheLastStatus_EndsTheFight_AndSaysHow(bool defeated)
    {
        var state = new BossBarState();
        state.Receive(Space(health: 0.1f, shield: 0f, phase: EscortCruiserRules.PhaseEnraged), 10f);
        state.Receive(Space(health: 0f, shield: 0f, active: false, defeated: defeated), 10.4f);

        Assert.False(state.TryGetBar(null, 10.4f, inFlight: true, out _));
        Assert.False(state.SpaceBossFighting(10.4f));
        Assert.False(state.IsSpaceBoss(Boss, 10.4f));
        Assert.Equal(0, state.PhaseOf(Boss, 10.4f));
        Assert.True(state.SpeaksFor(Boss, 10.4f)); // no small health bar pops up over the wreck / the jump flash
        Assert.Equal(1, state.EndCount);
        Assert.Equal(Boss, state.LastEndedId);
        Assert.Equal(defeated, state.LastEndDefeated);

        state.Receive(Space(health: 0.1f), 10.5f); // a late status of the same boss
        Assert.False(state.TryGetBar(null, 10.5f, inFlight: true, out _));
        state.Receive(Space(active: false, defeated: !defeated), 10.6f); // …and a second end changes nothing
        Assert.Equal(1, state.EndCount);
        Assert.Equal(defeated, state.LastEndDefeated);
    }

    [Fact]
    public void WithNoStatusForFourSeconds_TheBarGoes_AndANewStatusBringsItBack()
    {
        var state = new BossBarState();
        state.Receive(Space(health: 0.6f), 10f);
        Assert.True(state.TryGetBar(null, 10f + BossBarState.SilentSeconds - 0.1f, inFlight: true, out _));
        Assert.False(state.TryGetBar(null, 10f + BossBarState.SilentSeconds + 0.1f, inFlight: true, out _)); // the pilot left the flight
        Assert.False(state.SpaceBossFighting(10f + BossBarState.SilentSeconds + 0.1f));
        Assert.False(state.SpeaksFor(Boss, 10f + BossBarState.SilentSeconds + 0.1f));

        state.Receive(Space(health: 0.5f), 16f); // back in its flight: it never ended
        Assert.True(state.TryGetBar(null, 16f, inFlight: true, out var bar));
        Assert.Equal(0.5f, bar.Health, 4);
    }

    [Fact]
    public void WhenTheFlightCloses_TheSpaceBossesBarGoesAtOnce_AndTheGiantsStays()
    {
        var giants = new GuardianGiantFight();
        var state = new BossBarState();
        state.Receive(Space(health: 0.6f), 10f);
        giants.Receive(Giant(), 10f);

        state.Leave(BossSources.Space); // landed, jumped away, the ship lost
        Assert.False(state.SpaceBossFighting(10.1f));
        Assert.False(state.IsSpaceBoss(Boss, 10.1f));
        Assert.False(state.TryGetBar(null, 10.1f, inFlight: false, out _));
        Assert.True(state.TryGetBar(giants, 10.1f, inFlight: false, out var bar));
        Assert.Equal(BossSources.Giant, bar.Source);

        state.Receive(Space(health: 0.55f), 11f); // still in its flight after all (walking inside the ship): it is back
        Assert.True(state.TryGetBar(null, 11f, inFlight: true, out bar));
        Assert.Equal(0.55f, bar.Health, 4);
    }

    [Fact]
    public void AnEndedBoss_IsForgotten_SoItsIdCanFightAgainMuchLater()
    {
        var state = new BossBarState();
        state.Receive(Space(), 10f);
        state.Receive(Space(active: false, defeated: true), 11f);
        state.Receive(Space(health: 0.9f), 11f + BossBarState.ForgetSeconds + 1f);
        Assert.True(state.TryGetBar(null, 11f + BossBarState.ForgetSeconds + 1f, inFlight: true, out var bar));
        Assert.Equal(0.9f, bar.Health, 4);
    }

    [Fact]
    public void AClockThatWentBack_StartsAfresh()
    {
        var state = new BossBarState();
        state.Receive(Space(active: false, defeated: true), 500f); // the old world's boss fell
        Assert.False(state.TryGetBar(null, 3f, inFlight: true, out _));
        state.Receive(Space(health: 0.7f), 3f); // a new world, its clock starts over
        Assert.True(state.TryGetBar(null, 3f, inFlight: true, out var bar));
        Assert.Equal(0.7f, bar.Health, 4);

        state.Clear();
        Assert.False(state.TryGetBar(null, 3f, inFlight: true, out _));
    }

    [Fact]
    public void TheGiantsBar_IsWhatItWas()
    {
        var giants = new GuardianGiantFight();
        var state = new BossBarState();
        giants.Receive(Giant(hull: 450f), 10f);

        Assert.True(state.TryGetBar(giants, 10f, inFlight: false, out var bar));
        Assert.Equal(BossBarState.GiantNameKey, bar.NameKey);
        Assert.Equal("enemy.guardian_giant.name", bar.NameKey); // the key the bar always showed
        Assert.Equal(Site, bar.Id);
        Assert.Equal(BossSources.Giant, bar.Source);
        Assert.Equal(giants.BossBar(10f)!.HullFraction, bar.Health);
        Assert.Equal(0.75f, bar.Health, 4);
        Assert.Equal(0f, bar.Shield); // a giant has no shield: no strip
        Assert.Equal(GuardianGiantRules.RageAt, bar.RageAt); // the mark where its rage begins
        Assert.False(bar.Enraged);
        Assert.False(state.SpaceBossFighting(10f)); // a giant never starts the space fight's music rule

        giants.Receive(Giant(hull: 200f, rage: true), 11f);
        Assert.True(state.TryGetBar(giants, 11f, inFlight: false, out bar));
        Assert.True(bar.Enraged);

        // It hides exactly when the giant's own rule hides it: no hull left, the fight over, or no status for 3 s.
        Assert.False(state.TryGetBar(giants, 11f + GuardianGiantFight.StaleSeconds + 0.1f, inFlight: false, out _));
        giants.Receive(Giant(hull: 0f, move: GuardianGiantMove.Collapse), 20f);
        Assert.False(state.TryGetBar(giants, 20f, inFlight: false, out _));
        giants.Receive(Giant(hull: 200f, move: GuardianGiantMove.None), 21f);
        Assert.False(state.TryGetBar(giants, 21f, inFlight: false, out _));
    }

    [Fact]
    public void OfTwoGiants_TheOneHeardOfLast_AsBefore()
    {
        var giants = new GuardianGiantFight();
        var state = new BossBarState();
        giants.Receive(Giant(hull: 500f, site: "a#0"), 10f);
        giants.Receive(Giant(hull: 300f, site: "b#0"), 10.5f);
        Assert.True(state.TryGetBar(giants, 10.6f, inFlight: false, out var bar));
        Assert.Equal("b#0", bar.Id);
        Assert.Equal(giants.BossBar(10.6f)!.SiteId, bar.Id);
    }

    [Fact]
    public void AGiantAndACruiserAtOnce_TheBossOfThePlayersSceneKeepsTheBar()
    {
        var giants = new GuardianGiantFight();
        var state = new BossBarState();
        giants.Receive(Giant(), 10f);
        state.Receive(Space(health: 0.9f), 10.2f);

        Assert.True(state.TryGetBar(giants, 10.3f, inFlight: true, out var bar));
        Assert.Equal(BossSources.Space, bar.Source); // in flight: the cruiser
        Assert.True(state.TryGetBar(giants, 10.3f, inFlight: false, out bar));
        Assert.Equal(BossSources.Giant, bar.Source); // on foot: the giant

        // Whoever was heard of last makes no difference — the bar does not flicker with every packet.
        giants.Receive(Giant(), 10.8f);
        Assert.True(state.TryGetBar(giants, 10.9f, inFlight: true, out bar));
        Assert.Equal(BossSources.Space, bar.Source);
        state.Receive(Space(health: 0.85f), 11f);
        Assert.True(state.TryGetBar(giants, 11f, inFlight: false, out bar));
        Assert.Equal(BossSources.Giant, bar.Source);

        // With only one of them left, that one has the bar wherever the player is.
        state.Receive(Space(active: false, defeated: true), 11.2f);
        Assert.True(state.TryGetBar(giants, 11.3f, inFlight: true, out bar));
        Assert.Equal(BossSources.Giant, bar.Source);
    }

    [Fact]
    public void OfTwoSpaceBosses_TheFightHeardOfFirstKeepsTheBar()
    {
        var state = new BossBarState();
        state.Receive(Space(health: 0.5f, id: "e-2"), 10f);
        state.Receive(Space(health: 0.9f, id: "e-1"), 10.5f);
        state.Receive(Space(health: 0.45f, id: "e-2"), 11f);
        Assert.True(state.TryGetBar(null, 11f, inFlight: true, out var bar));
        Assert.Equal("e-2", bar.Id);

        state.Receive(Space(active: false, defeated: true, id: "e-2"), 11.5f);
        Assert.True(state.TryGetBar(null, 11.5f, inFlight: true, out bar));
        Assert.Equal("e-1", bar.Id);
        Assert.True(state.SpaceBossFighting(11.5f));
    }

    [Fact]
    public void TheRageMark_ComesFromTheEnemyData()
    {
        var content = ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir());
        float share = BossBarState.EnrageShare(content, EscortCruiserRules.NameKey);
        Assert.Equal(content.Enemy(EnemyCatalog.EscortCruiser).Tier(EnemyTiers.Normal).EnrageAtHull, share);
        Assert.InRange(share, 0.05f, 0.95f);
        Assert.Equal(0f, BossBarState.EnrageShare(content, "enemy.space_drone.name")); // no boss: no mark
        Assert.Equal(0f, BossBarState.EnrageShare(content, "enemy.nobody.name"));
        Assert.Equal(0f, BossBarState.EnrageShare(null, EscortCruiserRules.NameKey));
        Assert.Equal(0f, BossBarState.EnrageShare(content, null));
    }

    [Fact]
    public void TheBossTrack_PlaysForAGiantOnFoot_AndForASpaceBossInFlight_NeverOverTheFinale()
    {
        Assert.Equal("music_boss_gauntlet", BossMusicRules.Track);

        // The giant's rule as it was: on foot, outside the ship.
        Assert.True(BossMusicRules.Plays(finaleStaged: false, inSpace: false, aboard: false, giantFighting: true, spaceBossFighting: false));
        Assert.False(BossMusicRules.Plays(finaleStaged: false, inSpace: false, aboard: true, giantFighting: true, spaceBossFighting: false));
        Assert.False(BossMusicRules.Plays(finaleStaged: false, inSpace: true, aboard: false, giantFighting: true, spaceBossFighting: false));

        // The space boss: in the flight, while it fights.
        Assert.True(BossMusicRules.Plays(finaleStaged: false, inSpace: true, aboard: false, giantFighting: false, spaceBossFighting: true));
        Assert.False(BossMusicRules.Plays(finaleStaged: false, inSpace: true, aboard: false, giantFighting: false, spaceBossFighting: false));
        Assert.False(BossMusicRules.Plays(finaleStaged: false, inSpace: false, aboard: false, giantFighting: false, spaceBossFighting: true));

        // The finale's own music wins, whatever fights.
        Assert.False(BossMusicRules.Plays(finaleStaged: true, inSpace: true, aboard: false, giantFighting: true, spaceBossFighting: true));
        Assert.False(BossMusicRules.Plays(finaleStaged: true, inSpace: false, aboard: false, giantFighting: true, spaceBossFighting: true));
    }

    [Fact]
    public void TheMusicStops_WhenTheFightEnds_OrThePilotLeft()
    {
        var state = new BossBarState();
        state.Receive(Space(), 10f);
        Assert.True(BossMusicRules.Plays(false, true, false, false, state.SpaceBossFighting(10.5f)));
        Assert.False(BossMusicRules.Plays(false, true, false, false, state.SpaceBossFighting(10f + BossBarState.SilentSeconds + 0.5f)));
        state.Receive(Space(), 20f);
        state.Receive(Space(active: false, defeated: false), 20.5f);
        Assert.False(BossMusicRules.Plays(false, true, false, false, state.SpaceBossFighting(20.5f)));
    }
}
