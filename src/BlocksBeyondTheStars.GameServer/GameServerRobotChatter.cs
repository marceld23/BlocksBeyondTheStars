// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.State;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The talking robot (#3394-#3396, Justus' idea "Talking robo!"): the friendly hunter robot (#3295) speaks in VEGA's panel
/// under its own name. Its lines are locale keys (<see cref="RobotChatter"/>): it is sorry for what the old network did to
/// the SPS, misses its brothers, asks the player things, and sometimes VEGA asks it something and it answers.
/// <list type="bullet">
/// <item><b>Talk (E)</b> — <see cref="TalkToCompanionIntent"/>: only the sender's own robot, switched on, on this world and
/// within reach, answers; with a short cooldown. Spoken as a story line (kind 2), so the hints mute never swallows an
/// answer the player asked for.</item>
/// <item><b>By itself</b> — every 10-20 minutes (the first 5-10 minutes after a join) while it follows its owner on foot
/// close by and neither of them is in a fight: an advisor line (kind 1), muted with VEGA's hints.</item>
/// </list>
/// The pools play without repeats until every entry was heard; the progress is persisted in the player's counters.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Seconds between two answers of the robot to E.</summary>
    internal const double RobotTalkCooldownSeconds = 4.0;

    /// <summary>The server's slack over the client's talk reach (<see cref="InteractionReach.CompanionTalk"/>): the robot and
    /// the player moved a little since the positions the server last heard.</summary>
    private const float RobotTalkReachSlack = 1f;

    /// <summary>The robot speaks by itself only this close to its owner.</summary>
    internal const float RobotAmbientRange = 16f;

    /// <summary>The robot speaks by itself only when neither it nor its owner was in a fight for this long.</summary>
    internal const double RobotAmbientQuietSeconds = 30.0;

    /// <summary>E at the own robot: it answers with its next line (or VEGA asks and it answers). Anything that is not the
    /// sender's own robot beside them is ignored silently — another player's robot just shows its nameplate.</summary>
    private void HandleTalkToCompanion(PlayerSession session, TalkToCompanionIntent intent)
    {
        if (_uptime < session.Vega.RobotTalkReadyAt || TalkableRobot(session, intent.CompanionId ?? string.Empty) is not { } tc)
        {
            return;
        }

        session.Vega.RobotTalkReadyAt = _uptime + RobotTalkCooldownSeconds;
        SpeakRobotChatter(session, tc, kind: 2);
    }

    /// <summary>The sender's robot record when its live body stands on the sender's world within talk reach (an empty id names
    /// the sender's robot); null otherwise.</summary>
    private TamedCreature? TalkableRobot(PlayerSession session, string companionId)
    {
        var p = session.State;
        if (session.CurrentLocationId != _world.LocationId || InSpace(p.PlayerId) || p.Health <= 0f)
        {
            return null;
        }

        var tc = p.TamedCreatures.FirstOrDefault(t => t.Kind == TamedCreature.RobotKind && !t.KnockedOut
            && (companionId.Length == 0 || t.Id == companionId));
        if (tc is null || LiveRobotOf(p.PlayerId, tc) is not { } robot)
        {
            return null;
        }

        float reach = InteractionReach.CompanionTalk + RobotTalkReachSlack;
        return WrapDistSq(robot.Position, p.Position) <= reach * reach ? tc : null;
    }

    /// <summary>The live body of a player's robot on the active world, or null.</summary>
    private CombatEntity? LiveRobotOf(string playerId, TamedCreature tc)
        => _creatures.FirstOrDefault(c => IsRobotEntity(c) && c.CompanionId == tc.Id && c.OwnerId == playerId);

    /// <summary>The robot's next turn: an own line, or — about one talk in three — VEGA's question followed by its answer (in
    /// that order; the panel keeps the order). Both carry the robot's name as {0}; the robot's line names it as the speaker.
    /// The pool's progress goes to the player's counters.</summary>
    private void SpeakRobotChatter(PlayerSession session, TamedCreature tc, byte kind)
    {
        var counters = session.State.AchievementCounters;
        counters.TryGetValue(RobotChatter.LinesHeardCounter, out int lines);
        counters.TryGetValue(RobotChatter.ExchangesHeardCounter, out int exchanges);
        var (exchange, n) = RobotChatter.Next(session.State.PlayerId, lines, exchanges);
        string name = CompanionDisplayName(tc);
        if (exchange)
        {
            SendVegaLine(session, RobotChatter.QuestionKey(n), kind, name); // VEGA's own line: no speaker
            SendRobotLine(session, RobotChatter.AnswerKey(n), kind, name);
            counters[RobotChatter.ExchangesHeardCounter] = exchanges + 1;
        }
        else
        {
            SendRobotLine(session, RobotChatter.LineKey(n), kind, name);
            counters[RobotChatter.LinesHeardCounter] = lines + 1;
        }
    }

    /// <summary>A line the robot speaks: VEGA's panel shows its name (and its light blue) instead of "VEGA".</summary>
    private void SendRobotLine(PlayerSession session, string lineKey, byte kind, string name)
        => SendVegaWithObjective(session, new ShipAiLine
        {
            LineKey = lineKey,
            LineArg = name,
            Kind = kind,
            Speaker = RobotChatter.SpeakerId,
            SpeakerName = name,
        });

    // ---------------- By itself (#3396) ----------------

    /// <summary>The 1 Hz pass of one player (<c>TickShipAi</c>): arms the first ambient line after the join, and once it is due
    /// the robot speaks at the next good moment (<see cref="AmbientRobot"/>) — then the next gap is rolled. Muted with VEGA's
    /// hints: a muted player hears nothing, and the line waits.</summary>
    private void TickRobotChatterFor(PlayerSession session)
    {
        var vega = session.Vega;
        string playerId = session.State.PlayerId;
        if (double.IsNaN(vega.RobotAmbientDueAt))
        {
            vega.RobotAmbientDueAt = _uptime + RobotChatter.FirstAmbientDelay(playerId);
            return;
        }

        if (_uptime < vega.RobotAmbientDueAt || vega.VegaHintsMuted || AmbientRobot(session) is not { } tc)
        {
            return;
        }

        vega.RobotAmbientDueAt = _uptime + RobotChatter.AmbientInterval(playerId, vega.RobotAmbientSpoken);
        vega.RobotAmbientSpoken++;
        SpeakRobotChatter(session, tc, kind: 1);
    }

    /// <summary>The player's robot when this is a moment for it to speak by itself: the owner on foot on this world (not
    /// aboard, in space, on a station, in a ship interior or dead), the robot following within
    /// <see cref="RobotAmbientRange"/>, and neither in a fight for <see cref="RobotAmbientQuietSeconds"/>.</summary>
    private TamedCreature? AmbientRobot(PlayerSession session)
    {
        var p = session.State;
        if (p.AboardShip || p.InEva || p.Health <= 0f || session.Respawn.ChoicePending || session.CurrentLocationId != _world.LocationId
            || InSpace(p.PlayerId) || InStation(p.PlayerId) || ShipInteriorContains(p.Position)
            || GateClock - session.LastCombatAt < RobotAmbientQuietSeconds)
        {
            return null;
        }

        var tc = p.TamedCreatures.FirstOrDefault(t => t.Kind == TamedCreature.RobotKind && !t.KnockedOut && t.Following);
        if (tc is null || LiveRobotOf(p.PlayerId, tc) is not { } robot || _uptime - robot.RobotLastFightAt < RobotAmbientQuietSeconds)
        {
            return null;
        }

        return WrapDistSq(robot.Position, p.Position) <= RobotAmbientRange * RobotAmbientRange ? tc : null;
    }

    // ---------------- Test seams ----------------

    /// <summary>Test seam: runs the robot's ambient pass for a player right now (the 1 Hz gate of <c>TickShipAi</c> aside).</summary>
    public void TickRobotChatterForTest(string playerId)
    {
        if (FindSessionByPlayerId(playerId) is { } session)
        {
            TickRobotChatterFor(session);
        }
    }
}
