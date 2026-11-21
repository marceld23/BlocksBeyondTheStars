// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client.Feel;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3882–#3886 (Marcel, 2026-11-21: "NPCs and characters look wooden and stiff from outside — the NPCs' walk, for
/// example"): the humanoid figure's walk curves (<see cref="HumanGait"/>). A stroll is 0.7 blocks/s — the rig's walk blend
/// <c>moving</c> = 0.7 / 3.2 ≈ 0.22.
/// </summary>
public sealed class HumanGaitTests
{
    private const float Stroll = 0.7f / 3.2f;

    private static IEnumerable<float> Phases(int n = 200) => Enumerable.Range(0, n).Select(i => i / (float)n);

    [Fact]
    public void TheKnee_FoldsInTheSwing_NotWithTheLegAtItsRearmost()
    {
        // The old curve: max(0, sin φ) — its peak was the leg's rearmost point (Hip = +1, toe-off), the swing came forward
        // straight: a stilt walk. Now the toe-off is already bending and the deepest fold comes in the swing.
        float peakAt = Phases().OrderByDescending(p => HumanGait.Knee(p, Stroll, 0f)).First();
        Assert.InRange(peakAt, 0.6f, 0.76f);
        Assert.True(HumanGait.Knee(0.5f, Stroll, 0f) > 10f, "the knee is already bending at toe-off");
        Assert.True(HumanGait.Knee(0.25f, Stroll, 0f) < 2f, "the standing leg is straight in mid-stance");
        Assert.True(HumanGait.Knee(0f, Stroll, 0f) < 2f, "the leg lands nearly straight");
        Assert.InRange(HumanGait.Knee(0.12f, Stroll, 0f), 4f, 15f); // the give after the heel strike
        Assert.All(Phases(), p => Assert.True(HumanGait.Knee(p, 1f, 1f) >= 0f, "a knee only ever bends one way"));
    }

    [Fact]
    public void AStroll_BendsTheKneeClearly_ARunDeeper()
    {
        // A stroll used to peak at about 18° — between two boxes of one colour that reads as no knee at all.
        float stroll = Phases().Max(p => HumanGait.Knee(p, Stroll, 0f));
        float walk = Phases().Max(p => HumanGait.Knee(p, 1f, 0f));
        float run = Phases().Max(p => HumanGait.Knee(p, 1f, 1f));
        Assert.InRange(stroll, 38f, 50f);
        Assert.True(walk > stroll && run > walk + 20f);

        // Standing still nothing folds: the swing's fold would be marching on the spot.
        Assert.All(Phases(), p => Assert.Equal(0f, HumanGait.Knee(p, 0f, 0f)));
    }

    [Fact]
    public void TheElbow_BendsOnTheArmsForwardSwing()
    {
        // The old curve bent the elbow on the arm's BACK swing.
        Assert.True(HumanGait.Elbow(1f, Stroll, 0f, 3f) > HumanGait.Elbow(0f, Stroll, 0f, 3f) + 8f);
        Assert.Equal(HumanGait.Elbow(0f, Stroll, 0f, 3f), HumanGait.Elbow(-0.5f, Stroll, 0f, 3f)); // behind the body: the rest bend
        Assert.InRange(HumanGait.Elbow(0.5f, 1f, 1f, 3f), 60f, 100f);                              // a sprinter's pumping arm
    }

    [Fact]
    public void TheFoot_LandsOnItsHeel_StandsFlat_AndPushesOff()
    {
        Assert.True(HumanGait.FootPitch(0f, 1f, 0f) < -8f, "toes up at the heel strike");
        Assert.All(new[] { 0.15f, 0.25f, 0.35f }, p => Assert.Equal(0f, HumanGait.FootPitch(p, 1f, 0f), 3));
        Assert.True(HumanGait.FootPitch(0.58f, 1f, 0f) > 18f, "the heel lifts at push-off");
        Assert.True(HumanGait.FootPitch(0.58f, Stroll, 0f) > 10f, "a stroll still rolls off the foot");
        Assert.Equal(0f, HumanGait.FootPitch(0.58f, 0f, 0f)); // standing: flat
    }

    [Fact]
    public void TheBody_DipsAtDoubleSupport_AsFarAsTheSpreadLegsAsk()
    {
        const float amp = 20f;
        float doubleSupport = HumanGait.BodyDrop(0f, amp, HumanGait.LegLength, 1f, 0f, 1f, 0f);
        float midStance = HumanGait.BodyDrop(0.25f, amp, HumanGait.LegLength, 1f, 0f, 1f, 0f);
        // Without the figure's own bounce the dip is exactly what keeps both feet on the floor: L (1 − cos 20°).
        Assert.Equal(HumanGait.LegLength * (1f - MathF.Cos(20f * MathF.PI / 180f)), doubleSupport, 4);
        Assert.Equal(0f, midStance, 4);

        // The bounce of its own rides on top, scaled by the figure's liveliness and its style; a sprint never sinks the
        // body by more than a hand.
        Assert.True(HumanGait.BodyDrop(0f, amp, HumanGait.LegLength, 1f, 0f, 1f, 1f) > doubleSupport);
        Assert.True(HumanGait.BodyDrop(0f, 40f, HumanGait.LegLength, 1f, 1f, 1.35f, 1f) < 0.15f);
        Assert.Equal(0f, HumanGait.BodyDrop(0f, amp, HumanGait.LegLength, 0f, 0f, 1f, 1f)); // standing still: no dip
    }

    [Fact]
    public void TheHips_SwayOverTheStandingFoot_AndTurnWithTheStride()
    {
        // Left stance (0 … 0.5): the body over the left foot (−), the left hip forward at the left heel strike (+).
        Assert.True(HumanGait.Sway(0.25f) < -0.99f);
        Assert.True(HumanGait.Sway(0.75f) > 0.99f);
        Assert.Equal(1f, HumanGait.HipTurn(0f), 4);
        Assert.Equal(-1f, HumanGait.HipTurn(0.5f), 4);
        Assert.Equal(-1f, HumanGait.Hip(0f), 4); // the heel strike is the leg furthest forward …
        Assert.Equal(1f, HumanGait.Hip(0.5f), 4); // … the toe-off furthest back
    }

    [Fact]
    public void ThePhase_MatchesTheRigsLegSwing()
    {
        // The rig swings the left leg as sin(walkPhase): Hip(Phase(φ)) must be that very swing.
        foreach (float phi in Enumerable.Range(0, 64).Select(i => i * 0.37f))
        {
            Assert.Equal(MathF.Sin(phi), HumanGait.Hip(HumanGait.Phase(phi)), 3);
        }
    }

    [Fact]
    public void EveryVillager_HasItsOwnGait_PlayersAndMachinesTheStockOne()
    {
        var stock = HumanGait.StyleFor(0);
        Assert.Equal(GaitStyle.Neutral.Cadence, stock.Cadence);
        Assert.Equal(GaitStyle.Neutral.ArmSwing, stock.ArmSwing);

        var styles = Enumerable.Range(1, 300).Select(s => HumanGait.StyleFor(s * 7919)).ToList();
        Assert.All(styles, g =>
        {
            Assert.InRange(g.Cadence, 0.9f, 1.1f);
            Assert.InRange(g.ArmSwing, 0.75f, 1.25f);
            Assert.InRange(g.ArmAsymmetry, -0.12f, 0.12f);
            Assert.InRange(g.Bounce, 0.7f, 1.35f);
            Assert.InRange(g.ArmOut, 2f, 6f);
        });
        Assert.True(styles.Select(g => g.Cadence).Distinct().Count() > 200, "a crowd, not a drill squad");
        Assert.True(styles.Count(g => g.ArmSwing > 1.1f) > 30 && styles.Count(g => g.ArmSwing < 0.9f) > 30);

        // Deterministic: the same look seed walks the same way on every client and every visit.
        Assert.Equal(HumanGait.StyleFor(4242).Bounce, HumanGait.StyleFor(4242).Bounce);
    }

    [Fact]
    public void TheIdleShift_HoldsALegForAWhile_AndMachinesAreHalfAsLively()
    {
        var style = GaitStyle.Neutral;
        var shifts = Enumerable.Range(0, 600).Select(i => HumanGait.IdleShift(i * 0.1f, style)).ToList();
        Assert.All(shifts, s => Assert.InRange(s, -1f, 1f));
        Assert.True(shifts.Count(s => s >= 1f) > 50 && shifts.Count(s => s <= -1f) > 50, "it rests on each leg in turn");

        // #3886: stiffer than a villager, no longer rigid.
        Assert.InRange(HumanGait.MachineLiveliness, 0.3f, 0.7f);
    }

    [Fact]
    public void TheBump_WrapsRoundTheStride()
    {
        Assert.Equal(1f, HumanGait.Bump(0.95f, 0.95f, 0.1f), 4);
        Assert.True(HumanGait.Bump(0.02f, 0.95f, 0.1f) > 0f, "a bump at the end of the stride reaches into its start");
        Assert.Equal(0f, HumanGait.Bump(0.5f, 0.95f, 0.1f));
    }
}
