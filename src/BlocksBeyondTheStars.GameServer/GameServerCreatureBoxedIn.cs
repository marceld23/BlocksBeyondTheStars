// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// A boxed-in animal calms down (#3880, F1 report of 2026-11-21: a big skittish animal in a pond "keeps turning back and
/// forth hectically"). With every neighbouring column shut — deep water all round, a pit — two things kept it thrashing:
/// <list type="bullet">
/// <item>The detour probes (<see cref="TrySteerAround"/>, ±35°…±110°) found a "free" step that never left the animal's own
/// column — from its edge, a short step sideways stays inside — so it slid round and round the inside of its column, its
/// heading turning every tick.</item>
/// <item>With no detour at all it re-rolled a random heading each time: a fleer kept its flight's jink (±34°) on the spot and
/// a roamer pivoted from one random heading to the next.</item>
/// </list>
/// Now the eight neighbouring columns are probed for a way out (a step to each column's centre through every barrier, a
/// one-block rise allowed — columns, not points, for the reason above):
/// <list type="bullet">
/// <item><b>None open</b>: a detour inside its own column is refused, and the animal stands still and holds its heading for
/// <see cref="CreatureBoxedHoldSeconds"/> (<see cref="LocomotionState.BoxedTimer"/>); then it tries again and is boxed in
/// anew while every way is still shut.</item>
/// <item><b>One open</b> (a corner, a dead end) and no detour ahead: the open column nearest the current heading becomes the
/// turn target — a roamer's new heading, a hunter's or fleer's detour hold — and the animal turns there on the spot.</item>
/// <item>Unchanged: a detour into another column, an animal that was not trying to move (its own spot is blocked — the
/// stuck-body rescue's case) and one finishing a turn it had begun, so nothing can get stuck.</item>
/// </list>
/// The answer is kept per feet cell for <see cref="WayOutCacheSeconds"/> (<see cref="CombatEntity.WayOutCell"/>): the eight
/// probes run at most about once a second for an animal that lingers at a blocked edge, never on a free step.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>How long a boxed-in animal stands still before it tries again.</summary>
    internal const float CreatureBoxedHoldSeconds = 2f;

    /// <summary>How long the way-out answer for one feet cell holds (creature-clock seconds).</summary>
    private const double WayOutCacheSeconds = 1.0;

    /// <summary>#3880: whether a detour step stays inside the animal's own column while no neighbouring column is open — a
    /// step that only takes it round its own column.</summary>
    private bool DetourLeadsNowhere(CombatEntity c, CreatureSpecies sp, MotionClass motion, Vector3f detour, bool terrainGates)
    {
        var cur = c.Position;
        bool home = (int)System.Math.Floor(detour.X) == (int)System.Math.Floor(cur.X)
                    && (int)System.Math.Floor(detour.Z) == (int)System.Math.Floor(cur.Z);
        return home && !TryWayOut(c, sp, motion, terrainGates, out _);
    }

    /// <summary>What a creature does when its step and every detour ahead were blocked, or the only detour led nowhere
    /// (from <c>ApplyCreatureStep</c>). It slows down either way (#2668), so the way out starts with an ease-in.</summary>
    private void OnNoDetourAhead(CombatEntity c, CreatureSpecies sp, MotionClass motion, Vector3f stepped, MoveMode intent,
        in LocomotionProfile prof, double dt, bool terrainGates)
    {
        float slowed = System.Math.Max(0f, c.Loco.Speed - (2f * prof.Accel * (float)dt));
        float dx = stepped.X - c.Position.X, dz = stepped.Z - c.Position.Z;
        if ((dx * dx) + (dz * dz) <= 1e-10f)
        {
            c.Loco.ModeTimer = 0f; // not trying to move — as before: re-roll a fresh heading
            c.Loco.Speed = slowed;
            return;
        }

        if (!TryWayOut(c, sp, motion, terrainGates, out float wayOut))
        {
            c.Loco.BoxedTimer = CreatureBoxedHoldSeconds; // shut in on every side: stand calmly, try again later
            c.Loco.Speed = 0f;
            return;
        }

        bool turning = intent == MoveMode.Roam ? c.Loco.Turning : c.SteerUntil > _creatureClock;
        if (turning)
        {
            c.Loco.ModeTimer = 0f; // as before: a turn in progress is finished first (#2668)
        }
        else if (intent == MoveMode.Roam)
        {
            c.Loco.TurnTo = wayOut;
            c.Loco.Turning = true;
            c.Loco.TurnAge = 0f;
        }
        else
        {
            c.SteerHeading = wayOut;
            c.SteerUntil = _creatureClock + CreatureDetourHoldSeconds;
        }

        c.Loco.Speed = slowed;
    }

    /// <summary>The heading toward the open neighbouring column nearest the current heading, or false when all eight are
    /// shut. Kept per feet cell for <see cref="WayOutCacheSeconds"/>.</summary>
    private bool TryWayOut(CombatEntity c, CreatureSpecies sp, MotionClass motion, bool terrainGates, out float heading)
    {
        var cell = c.Position.ToBlock();
        if (c.WayOutUntil > _creatureClock && c.WayOutCell == cell)
        {
            heading = c.WayOutHeading;
            return c.WayOutOpen;
        }

        bool open = TryFindWayOut(c, sp, motion, terrainGates, out heading);
        c.WayOutCell = cell;
        c.WayOutUntil = _creatureClock + WayOutCacheSeconds;
        c.WayOutOpen = open;
        c.WayOutHeading = heading;
        return open;
    }

    /// <summary>Probes the eight neighbouring columns (see the type summary); the open one nearest the current heading wins.</summary>
    private bool TryFindWayOut(CombatEntity c, CreatureSpecies sp, MotionClass motion, bool terrainGates, out float heading)
    {
        heading = 0f;
        var cur = c.Position;
        int cx = (int)System.Math.Floor(cur.X), cz = (int)System.Math.Floor(cur.Z);
        int curFeet = (int)System.Math.Floor(cur.Y);
        float bestTurn = float.MaxValue;
        for (int ox = -1; ox <= 1; ox++)
        {
            for (int oz = -1; oz <= 1; oz++)
            {
                if (ox == 0 && oz == 0)
                {
                    continue;
                }

                var centre = new Vector3f(cx + ox + 0.5f, cur.Y, cz + oz + 0.5f);
                float h = (float)System.Math.Atan2(centre.Z - cur.Z, centre.X - cur.X);
                float turn = System.Math.Abs(WrapAngle(h - c.Loco.Heading));
                if (turn >= bestTurn || !NeighbourColumnFree(c, sp, motion, cur, centre, curFeet, terrainGates))
                {
                    continue;
                }

                bestTurn = turn;
                heading = h;
            }
        }

        return bestTurn < float.MaxValue;
    }

    /// <summary>Whether a step from <paramref name="cur"/> to a neighbouring column's centre passes every barrier — the
    /// same checks <c>ApplyCreatureStep</c> runs, a one-block rise included (the animal jumps or climbs it when it gets there).</summary>
    private bool NeighbourColumnFree(CombatEntity c, CreatureSpecies sp, MotionClass motion, Vector3f cur, Vector3f centre,
        int curFeet, bool terrainGates)
    {
        var cand = PreviewStep(sp, motion, cur, centre, out bool needsRise, out int riseFeet, out int? nextFeet);
        if (needsRise && (riseFeet - curFeet > CreatureMotion.StepUpLimit(motion)
                          || CreatureBodyBlocked(sp, new Vector3f(cur.X, riseFeet, cur.Z))))
        {
            return false;
        }

        return !StepBlocked(c, sp, motion, cur, cand, needsRise, terrainGates, nextFeet);
    }

    private static float WrapAngle(float a)
    {
        const float tau = 6.2831853f;
        a %= tau;
        if (a > 3.14159265f)
        {
            a -= tau;
        }
        else if (a < -3.14159265f)
        {
            a += tau;
        }

        return a;
    }
}
