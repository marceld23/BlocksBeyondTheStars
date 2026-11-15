// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Client.Core;

/// <summary>What the flight view does in one frame of a docking's hand-over (<see cref="DockHandoff"/>).</summary>
public enum DockHandoffStep
{
    /// <summary>Nothing this frame: the approach still flies, the doors are closing, or the server's answer is awaited.</summary>
    Wait,

    /// <summary>Close the hangar doors over the view (the transition director's <c>CoverDocking</c>, #3142).</summary>
    CloseDoors,

    /// <summary>Send the board intent now. Without the doors the loading veil goes up with it (B34).</summary>
    SendBoard,

    /// <summary>The server never confirmed the docking (it refused, or did not answer): hand the flight — or the spacewalk —
    /// back and open the doors.</summary>
    GiveUp,
}

/// <summary>
/// The end of a docking — the dock approach flown from the helm (#1917) or E at a station's hull on a spacewalk: the hangar
/// doors close over the view (#3142), the board intent goes out once they are shut, and a docking the server does not
/// confirm in time hands the controls back. The flight view keeps one instance and starts every docking with
/// <see cref="Begin"/>, so nothing of an earlier docking carries over.
/// <para>
/// Justus' black screen (2026-11-15, "pressed E on a station in space and was simply gone"): the flight view kept "the doors
/// are closing" in a field that only the helm's dock approach cleared. After one docking from the helm it stayed set, so a
/// later docking from a spacewalk waited for doors nobody had asked to close: the board intent never went out, the give-up
/// only counted once it had, and the flight view's fade held the screen black for good — while the server, never asked,
/// still had the player floating on the spacewalk. Here every wait has a clock: the doors get <see cref="DoorsGiveUp"/>
/// seconds to shut, the server <see cref="AnswerWaitCovered"/> (behind the doors) or <see cref="AnswerWait"/> to answer.
/// </para>
/// Unity-free so the rule is covered by plain .NET tests (<c>DockHandoffTests</c>); presentation only — the server decides
/// whether the player boards.
/// </summary>
public sealed class DockHandoff
{
    /// <summary>Seconds the doors may take to shut before the board intent goes out anyway (they close in 0.9 s).</summary>
    public const float DoorsGiveUp = 3f;

    /// <summary>Seconds to wait for the server behind the doors — a cold station may take a moment to build.</summary>
    public const float AnswerWaitCovered = 8f;

    /// <summary>Seconds to wait for the server without the doors (behind the loading veil).</summary>
    public const float AnswerWait = 2.5f;

    private float _doorsFor;

    /// <summary>True from <see cref="Begin"/> until the docking was given up (or <see cref="Reset"/>).</summary>
    public bool Active { get; private set; }

    /// <summary>The docking started on a spacewalk: the suit floated to the hull, the parked ship stays where it is, and a
    /// refused docking goes back to the spacewalk. Kept after <see cref="DockHandoffStep.GiveUp"/> so the caller can read it.</summary>
    public bool FromSpacewalk { get; private set; }

    /// <summary>The doors were asked to close.</summary>
    public bool DoorsClosing { get; private set; }

    /// <summary>The board intent went out.</summary>
    public bool Sent { get; private set; }

    /// <summary>Seconds since the board intent went out.</summary>
    public float Waited { get; private set; }

    /// <summary>Starts a docking from a clean state — whatever an earlier docking left behind.</summary>
    public void Begin(bool fromSpacewalk)
    {
        Active = true;
        FromSpacewalk = fromSpacewalk;
        DoorsClosing = false;
        Sent = false;
        Waited = 0f;
        _doorsFor = 0f;
    }

    /// <summary>No docking under way (the flight view opened, or the docking is over).</summary>
    public void Reset()
    {
        Begin(fromSpacewalk: false);
        Active = false;
    }

    /// <summary>
    /// Advances the hand-over one frame and says what to do. <paramref name="approachDone"/>: the dock approach has played
    /// (always true on a spacewalk, which skips the fly-in). <paramref name="withDoors"/>: a transition director closes the
    /// hangar doors (#3142) — without one the intent goes out at once behind the loading veil. <paramref name="doorsShut"/>:
    /// the doors are fully shut.
    /// </summary>
    public DockHandoffStep Tick(float dt, bool approachDone, bool withDoors, bool doorsShut)
    {
        if (!Active || !approachDone)
        {
            return DockHandoffStep.Wait;
        }

        float step = dt > 0f ? dt : 0f;
        if (!Sent)
        {
            if (!withDoors)
            {
                Sent = true;
                return DockHandoffStep.SendBoard;
            }

            if (!DoorsClosing)
            {
                DoorsClosing = true;
                return DockHandoffStep.CloseDoors;
            }

            _doorsFor += step;
            if (!doorsShut && _doorsFor < DoorsGiveUp)
            {
                return DockHandoffStep.Wait;
            }

            Sent = true;
            return DockHandoffStep.SendBoard;
        }

        Waited += step;
        if (Waited <= (withDoors ? AnswerWaitCovered : AnswerWait))
        {
            return DockHandoffStep.Wait;
        }

        Active = false;
        return DockHandoffStep.GiveUp;
    }
}
