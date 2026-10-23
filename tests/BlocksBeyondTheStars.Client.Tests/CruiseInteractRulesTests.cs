// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client.Core;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #2934: at the helm, E and the board prompt answer one rule — fly through a wormhole, go over to the docked friend's
/// ship or ask the friend at the helm, dock the station or land on the body (whichever is closer, a tie docks the
/// station), and otherwise just say that the friend is flying. Before, the flight view wrote that rule twice (the E
/// handler and the prompt); the last test replays both former copies against the rule in every state they could see.
/// </summary>
public sealed class CruiseInteractRulesTests
{
    private const float Near = 100f;  // squared distances: 10 m
    private const float Far = 400f;   // 20 m

    [Theory]
    // wormhole, goesOver, asks, station, stationSq, body, bodySq, flying → expected
    [InlineData(false, false, false, false, 0f, false, 0f, false, CruiseInteractTarget.None)]
    [InlineData(false, false, false, true, Near, false, 0f, false, CruiseInteractTarget.Station)]
    [InlineData(false, false, false, false, 0f, true, Near, false, CruiseInteractTarget.Body)]
    [InlineData(false, false, false, true, Near, true, Far, false, CruiseInteractTarget.Station)]   // station closer
    [InlineData(false, false, false, true, Far, true, Near, false, CruiseInteractTarget.Body)]      // body closer
    [InlineData(false, false, false, true, Near, true, Near, false, CruiseInteractTarget.Station)]  // a tie docks the station
    [InlineData(true, false, false, false, 0f, false, 0f, false, CruiseInteractTarget.Wormhole)]
    [InlineData(true, true, false, true, Near, true, Near, false, CruiseInteractTarget.Wormhole)]   // the rift wins over all
    [InlineData(true, false, true, false, 0f, false, 0f, true, CruiseInteractTarget.Wormhole)]      // ... over asking too
    [InlineData(false, true, false, false, 0f, false, 0f, false, CruiseInteractTarget.GoOver)]
    [InlineData(false, true, false, true, Far, true, Far, false, CruiseInteractTarget.GoOver)]      // the friend's ship is nearer
    [InlineData(false, false, true, false, 0f, false, 0f, true, CruiseInteractTarget.AskPartner)]
    [InlineData(false, false, false, false, 0f, false, 0f, true, CruiseInteractTarget.PartnerFlying)] // out of reach: info only
    [InlineData(false, false, false, true, Near, false, 0f, true, CruiseInteractTarget.Station)]   // "is flying" only with nothing else
    [InlineData(false, false, false, false, 0f, true, Near, true, CruiseInteractTarget.Body)]
    public void Choose_PicksWhatEDoes(bool wormhole, bool goesOver, bool asks, bool station, float stationSq, bool body,
        float bodySq, bool flying, CruiseInteractTarget expected)
    {
        Assert.Equal(expected, CruiseInteractRules.Choose(wormhole, goesOver, asks, station, stationSq, body, bodySq, flying));
    }

    /// <summary>
    /// Every combination of the inputs, against transcriptions of the two copies the rule replaced: the board prompt
    /// (SpaceView.Hud.cs LateUpdate) must show the same line, and E (SpaceView.Flight.cs UpdateCruise) must do the same
    /// thing. The only states skipped are the ones the flight view never produces: it sets "asks the partner" only while
    /// the friend flies with no station and no body in range, in the same frame as those fields.
    /// </summary>
    [Fact]
    public void Choose_MatchesBothFormerCopies_InEveryStateTheFlightViewProduces()
    {
        float[] squares = { Near, Far };
        int checkedStates = 0;
        for (int bits = 0; bits < 64; bits++)
        {
            bool wormhole = (bits & 1) != 0;
            bool goesOver = (bits & 2) != 0;
            bool asks = (bits & 4) != 0;
            bool station = (bits & 8) != 0;
            bool body = (bits & 16) != 0;
            bool flying = (bits & 32) != 0;
            if (asks && (!flying || station || body))
            {
                continue;
            }

            foreach (float stationSq in squares)
            {
                foreach (float bodySq in squares)
                {
                    var target = CruiseInteractRules.Choose(wormhole, goesOver, asks, station, stationSq, body, bodySq, flying);
                    string state = $"wormhole={wormhole} goesOver={goesOver} asks={asks} station={station}@{stationSq} body={body}@{bodySq} flying={flying}";
                    Assert.True(FormerPrompt(wormhole, goesOver, asks, station, stationSq, body, bodySq, flying) == target,
                        $"prompt differs for {state}");
                    Assert.True(FormerKey(wormhole, goesOver, asks, station, stationSq, body, bodySq) == KeyOf(target),
                        $"E differs for {state}");
                    checkedStates++;
                }
            }
        }

        Assert.Equal(144, checkedStates); // 64 input combinations less the 28 the view never produces, times four distance pairs
    }

    /// <summary>What E did, as the former handler branched.</summary>
    private enum KeyAction { WormholeTransit, Visit, Dock, PadMap, AnomalyHint }

    /// <summary>The board line the former prompt showed (each later line overwrote the text; the board was visible when
    /// any of them showed) — <see cref="CruiseInteractTarget.None"/> for a hidden board.</summary>
    private static CruiseInteractTarget FormerPrompt(bool wormhole, bool goesOver, bool asks, bool haveStation, float stationSq,
        bool haveBody, float bodySq, bool flyingPrompt)
    {
        var shown = CruiseInteractTarget.None;
        bool stationCloser = haveStation && (!haveBody || stationSq <= bodySq);
        bool showStation = haveStation && stationCloser;
        bool showBody = haveBody && !stationCloser;
        if (showStation)
        {
            shown = CruiseInteractTarget.Station;
        }
        else if (showBody)
        {
            shown = CruiseInteractTarget.Body;
        }

        bool flying = flyingPrompt && !showStation && !showBody;
        if (goesOver || flying)
        {
            shown = goesOver ? CruiseInteractTarget.GoOver
                : asks ? CruiseInteractTarget.AskPartner
                : CruiseInteractTarget.PartnerFlying;
        }

        if (wormhole)
        {
            shown = CruiseInteractTarget.Wormhole;
        }

        return shown;
    }

    /// <summary>What the former E handler did on a press.</summary>
    private static KeyAction FormerKey(bool wormhole, bool goesOver, bool asks, bool haveStation, float stationSq, bool haveBody,
        float bodySq)
    {
        if (wormhole)
        {
            return KeyAction.WormholeTransit;
        }

        bool stationCloser = haveStation && (!haveBody || stationSq <= bodySq);
        if (goesOver || asks)
        {
            return KeyAction.Visit;
        }

        if (stationCloser)
        {
            return KeyAction.Dock;
        }

        return haveBody ? KeyAction.PadMap : KeyAction.AnomalyHint;
    }

    /// <summary>What E does for <paramref name="target"/> since #2934 (SpaceView.Flight.cs's switch).</summary>
    private static KeyAction KeyOf(CruiseInteractTarget target) => target switch
    {
        CruiseInteractTarget.Wormhole => KeyAction.WormholeTransit,
        CruiseInteractTarget.GoOver or CruiseInteractTarget.AskPartner => KeyAction.Visit,
        CruiseInteractTarget.Station => KeyAction.Dock,
        CruiseInteractTarget.Body => KeyAction.PadMap,
        _ => KeyAction.AnomalyHint,
    };
}
