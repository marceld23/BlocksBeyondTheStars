// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client.Core;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The end of a docking (#1917, #3142) — Justus' black screen (2026-11-15): "pressed E on a station in space and was simply
/// gone". He had boarded a giant wreck (by all signs docked from the helm), left it on a spacewalk and pressed E at a
/// hull. The flight view's "doors closing" flag was still set from the helm's docking, so the spacewalk's docking waited
/// for doors nobody had asked to close: no board intent, no give-up, and the flight view's fade held the screen black
/// while the server still had him floating. Every docking now starts its hand-over afresh, and every wait has a clock.
/// </summary>
public sealed class DockHandoffTests
{
    private const float Frame = 1f / 60f;

    /// <summary>Plays a docking to its board intent with the doors, as the flight view drives it: the approach first, then
    /// the doors close and shut, then the intent. Returns the steps seen, in order, without the waits.</summary>
    private static string Dock(DockHandoff handoff, bool fromSpacewalk, float approachSeconds)
    {
        handoff.Begin(fromSpacewalk);
        string seen = string.Empty;
        float clock = 0f;
        bool doorsShut = false;
        float doorsClosingFor = -1f;
        for (int i = 0; i < 600 && !handoff.Sent; i++)
        {
            clock += Frame;
            var step = handoff.Tick(Frame, approachDone: clock >= approachSeconds, withDoors: true, doorsShut: doorsShut);
            if (step == DockHandoffStep.CloseDoors)
            {
                doorsClosingFor = 0f;
            }

            if (doorsClosingFor >= 0f)
            {
                doorsClosingFor += Frame;
                doorsShut = doorsClosingFor >= 0.9f; // the hangar doors take 0.9 s
            }

            if (step != DockHandoffStep.Wait)
            {
                seen += step + " ";
            }
        }

        return seen.Trim();
    }

    [Fact]
    public void ADockingFromTheHelm_ThenOneFromASpacewalk_BothSendTheirBoardIntent()
    {
        // The report's order: dock the wreck at the helm (boarded — the server closed the flight with the doors still
        // "closing"), fall out on a spacewalk, E at the hull. The second docking has no approach to fly.
        var handoff = new DockHandoff();
        Assert.Equal("CloseDoors SendBoard", Dock(handoff, fromSpacewalk: false, approachSeconds: 1.2f));
        Assert.True(handoff.DoorsClosing, "the helm's docking leaves its doors closed behind it");

        Assert.Equal("CloseDoors SendBoard", Dock(handoff, fromSpacewalk: true, approachSeconds: 0f));
        Assert.True(handoff.FromSpacewalk);
    }

    [Fact]
    public void BeginningADocking_ForgetsEverythingTheLastOneLeft()
    {
        // The old flow cleared "doors closing" on the helm's path only, so a spacewalk's docking inherited the helm's flag and
        // waited for doors that were open again long ago (the doors' "shut" never came). Begin clears every field.
        var handoff = new DockHandoff();
        handoff.Begin(fromSpacewalk: false);
        handoff.Tick(Frame, true, withDoors: true, doorsShut: false);              // doors closing
        handoff.Tick(Frame, true, withDoors: true, doorsShut: true);               // intent sent
        handoff.Tick(5f, true, withDoors: true, doorsShut: true);                  // waiting for the answer
        Assert.True(handoff.DoorsClosing && handoff.Sent && handoff.Waited > 0f);

        handoff.Begin(fromSpacewalk: true);
        Assert.True(handoff.Active && handoff.FromSpacewalk);
        Assert.False(handoff.DoorsClosing || handoff.Sent);
        Assert.Equal(0f, handoff.Waited);
        Assert.Equal(DockHandoffStep.CloseDoors, handoff.Tick(Frame, approachDone: true, withDoors: true, doorsShut: false));
    }

    [Fact]
    public void DoorsThatNeverShut_StillSendTheIntent_AfterTheirGiveUp()
    {
        var handoff = new DockHandoff();
        handoff.Begin(fromSpacewalk: true);
        Assert.Equal(DockHandoffStep.CloseDoors, handoff.Tick(Frame, true, withDoors: true, doorsShut: false));

        float clock = 0f;
        DockHandoffStep step = DockHandoffStep.Wait;
        while (step == DockHandoffStep.Wait && clock < 10f)
        {
            clock += Frame;
            step = handoff.Tick(Frame, true, withDoors: true, doorsShut: false);
        }

        Assert.Equal(DockHandoffStep.SendBoard, step);
        Assert.InRange(clock, DockHandoff.DoorsGiveUp - 0.05f, DockHandoff.DoorsGiveUp + 0.05f);
    }

    [Fact]
    public void ARefusedDocking_GivesUpBehindTheDoors_AndRemembersItCameFromASpacewalk()
    {
        var handoff = new DockHandoff();
        handoff.Begin(fromSpacewalk: true);
        handoff.Tick(Frame, true, withDoors: true, doorsShut: false);
        Assert.Equal(DockHandoffStep.SendBoard, handoff.Tick(Frame, true, withDoors: true, doorsShut: true));

        float clock = 0f;
        DockHandoffStep step = DockHandoffStep.Wait;
        while (step == DockHandoffStep.Wait && clock < 20f)
        {
            clock += Frame;
            step = handoff.Tick(Frame, true, withDoors: true, doorsShut: true);
        }

        Assert.Equal(DockHandoffStep.GiveUp, step);
        Assert.InRange(clock, DockHandoff.AnswerWaitCovered, DockHandoff.AnswerWaitCovered + 0.05f);
        Assert.False(handoff.Active);
        Assert.True(handoff.FromSpacewalk, "the flight view hands the spacewalk back, not the helm");
        Assert.Equal(DockHandoffStep.Wait, handoff.Tick(Frame, true, true, true)); // given up once — nothing more
    }

    [Fact]
    public void WithoutTheDoors_TheIntentGoesAtOnce_AndTheAnswerWaitIsShort()
    {
        var handoff = new DockHandoff();
        handoff.Begin(fromSpacewalk: false);
        Assert.Equal(DockHandoffStep.Wait, handoff.Tick(Frame, approachDone: false, withDoors: false, doorsShut: true));
        Assert.Equal(DockHandoffStep.SendBoard, handoff.Tick(Frame, approachDone: true, withDoors: false, doorsShut: true));

        float clock = 0f;
        DockHandoffStep step = DockHandoffStep.Wait;
        while (step == DockHandoffStep.Wait && clock < 10f)
        {
            clock += Frame;
            step = handoff.Tick(Frame, true, withDoors: false, doorsShut: true);
        }

        Assert.Equal(DockHandoffStep.GiveUp, step);
        Assert.InRange(clock, DockHandoff.AnswerWait, DockHandoff.AnswerWait + 0.05f);
    }

    [Fact]
    public void NothingHappens_BeforeTheApproachHasPlayed_OrWithoutADocking()
    {
        var idle = new DockHandoff();
        Assert.Equal(DockHandoffStep.Wait, idle.Tick(Frame, true, true, true)); // never begun

        var handoff = new DockHandoff();
        handoff.Begin(fromSpacewalk: false);
        for (int i = 0; i < 600; i++)
        {
            Assert.Equal(DockHandoffStep.Wait, handoff.Tick(Frame, approachDone: false, withDoors: true, doorsShut: false));
        }

        handoff.Reset(); // the flight view opened again
        Assert.False(handoff.Active);
        Assert.False(handoff.DoorsClosing);
        Assert.Equal(DockHandoffStep.Wait, handoff.Tick(Frame, true, true, true));
    }
}
