// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client.Core;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #2588: a player took a space anomaly for a wormhole — it was labelled "???" (what an unknown wormhole's destination
/// reads) and flying close or pressing E next to it said nothing. The anomaly is named by its kind, and an approach or an
/// E press says what it is: unscanned "not a wormhole, scan it", scanned "read already" — every approach, rate-limited.
/// </summary>
public sealed class AnomalyHintTests
{
    private const string Id = "anomaly:sol";

    private static Func<string, string> Localizer(string code)
    {
        var en = VegaLineBudgetTests.Core("en");
        var table = VegaLineBudgetTests.Core(code);
        return key => table.TryGetValue(key, out var v) ? v : en.TryGetValue(key, out var e) ? e : "[" + key + "]";
    }

    // ---- The name ------------------------------------------------------------------------------------------

    [Fact]
    public void TheRadarRim_NamesAnAnomalyByItsKind_EvenWhenAnOlderServerStillSendsQuestionMarks()
    {
        Assert.Equal("Anomaly", SpaceEncounterLabel.Rim("Anomaly", "???", Localizer("en")));
        Assert.Equal("Anomalie", SpaceEncounterLabel.Rim("Anomaly", "???", Localizer("de")));
        Assert.Equal("Anomaly", SpaceEncounterLabel.Rim("Anomaly", string.Empty, Localizer("en")));
    }

    [Fact]
    public void TheRadarRim_KeepsThePodsSos_TheWormholesKind_AndEveryOtherName()
    {
        var en = Localizer("en");
        Assert.Equal("SOS · Mira Kessel", SpaceEncounterLabel.Rim("EscapePod", "Mira Kessel", en));
        Assert.Equal("Wormhole", SpaceEncounterLabel.Rim("Wormhole", string.Empty, en));
        Assert.Equal("Rim Exchange", SpaceEncounterLabel.Rim("SpaceStation", "Rim Exchange", en));
    }

    [Fact]
    public void TheLines_ExistInEnglishAndGerman_AndNameTheWormholeAndTheScanner()
    {
        var en = Localizer("en");
        var de = Localizer("de");
        Assert.Contains("wormhole", en(AnomalyHint.UnscannedKey));
        Assert.Contains("scanner", en(AnomalyHint.UnscannedKey));
        Assert.Contains("Wurmloch", de(AnomalyHint.UnscannedKey));
        Assert.Contains("Scanner", de(AnomalyHint.UnscannedKey));
        Assert.DoesNotContain("[", en(AnomalyHint.ScannedKey));
        Assert.DoesNotContain("[", de(AnomalyHint.ScannedKey));
        Assert.Equal("Anomalie", de(AnomalyHint.NameKey));
    }

    // ---- The approach --------------------------------------------------------------------------------------

    [Fact]
    public void AnApproach_ToAnUnscannedAnomaly_SaysItIsNoWormhole_OnceWhileTheShipStaysClose()
    {
        var hint = new AnomalyHint();
        Assert.Null(hint.Approach(Id, 120f, scanned: false, now: 0f)); // far out: nothing yet
        Assert.Null(hint.Approach(Id, AnomalyHint.ApproachRange + 1f, scanned: false, now: 1f));

        Assert.Equal(AnomalyHint.UnscannedKey, hint.Approach(Id, AnomalyHint.ApproachRange - 1f, scanned: false, now: 2f));

        // Hovering next to it (well past the cooldown) does not repeat the line every frame.
        Assert.Null(hint.Approach(Id, 10f, scanned: false, now: 20f));
        Assert.Null(hint.Approach(Id, 9.7f, scanned: false, now: 60f));
    }

    [Fact]
    public void EveryApproach_SpeaksAgain_AfterTheShipFellBackBeyondTheRearmRange()
    {
        var hint = new AnomalyHint();
        Assert.Equal(AnomalyHint.UnscannedKey, hint.Approach(Id, 20f, scanned: false, now: 0f));

        // Circling on the edge of the approach range is still the same approach.
        Assert.Null(hint.Approach(Id, AnomalyHint.ApproachRange + 5f, scanned: false, now: 10f));
        Assert.Null(hint.Approach(Id, 20f, scanned: false, now: 11f));

        // Away and back: a new approach — not once per save.
        Assert.Null(hint.Approach(Id, AnomalyHint.RearmRange + 1f, scanned: false, now: 20f));
        Assert.Equal(AnomalyHint.UnscannedKey, hint.Approach(Id, 20f, scanned: false, now: 30f));
    }

    [Fact]
    public void AScannedAnomaly_SaysItWasReadAlready()
    {
        var hint = new AnomalyHint();
        Assert.Equal(AnomalyHint.ScannedKey, hint.Approach(Id, 15f, scanned: true, now: 0f));
        Assert.Equal(AnomalyHint.ScannedKey, hint.Interact(15f, scanned: true, now: 10f));
    }

    [Fact]
    public void ANewFlight_MakesTheNextLookAFreshApproach()
    {
        var hint = new AnomalyHint();
        Assert.Equal(AnomalyHint.UnscannedKey, hint.Approach(Id, 15f, scanned: false, now: 0f));
        hint.Reset();
        Assert.Equal(AnomalyHint.UnscannedKey, hint.Approach(Id, 15f, scanned: false, now: 10f));
    }

    // ---- E next to it --------------------------------------------------------------------------------------

    [Fact]
    public void PressingE_NextToAnAnomaly_RepeatsTheLine_AFewSecondsApart()
    {
        var hint = new AnomalyHint();
        Assert.Equal(AnomalyHint.UnscannedKey, hint.Interact(9.7f, scanned: false, now: 0f));
        Assert.Null(hint.Interact(9.7f, scanned: false, now: 1f)); // hammering E: the toast is still up
        Assert.Equal(AnomalyHint.UnscannedKey, hint.Interact(9.7f, scanned: false, now: AnomalyHint.CooldownSeconds + 0.1f));
    }

    [Fact]
    public void PressingE_FarFromAnyAnomaly_SaysNothing()
    {
        var hint = new AnomalyHint();
        Assert.Null(hint.Interact(float.PositiveInfinity, scanned: false, now: 0f));
        Assert.Null(hint.Interact(AnomalyHint.ApproachRange + 0.5f, scanned: false, now: 0f));
    }

    [Fact]
    public void AnApproachAndAnEPress_ShareTheCooldown()
    {
        var hint = new AnomalyHint();
        Assert.Equal(AnomalyHint.UnscannedKey, hint.Approach(Id, 20f, scanned: false, now: 0f));
        Assert.Null(hint.Interact(20f, scanned: false, now: 2f));
        Assert.Equal(AnomalyHint.UnscannedKey, hint.Interact(20f, scanned: false, now: 5f));
    }

    [Fact]
    public void OnlyAnAnomaly_IsOne()
    {
        Assert.True(AnomalyHint.IsAnomaly("Anomaly"));
        Assert.False(AnomalyHint.IsAnomaly("Wormhole"));
        Assert.False(AnomalyHint.IsAnomaly(null));
    }
}
