// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// NPCs are startled by a weapon fired or swung nearby (#3769 — Justus: "when you shoot in a station, NPCs are startled,
/// raise their hands, open their mouth wide and call out … 'Watch out', 'Not here', 'Are you related to the bounty hunter
/// Layex?'").
/// <list type="bullet">
/// <item><b>The trigger</b> is the player's own cosmetic weapon effect (<see cref="FxIntent"/>, kind Shot or Melee with a
/// weapon in the hand) — the only thing the server hears of a shot or swing that hits nothing. NPCs cannot be hit, and
/// nothing here changes that.</item>
/// <item><b>Who</b>: the nearest awake NPCs within <see cref="StartleRange"/> of the shooter, on foot, at most
/// <see cref="StartleMaxNpcs"/> per bout — never a guardian, a sleeper, one in a conversation, away on a visit, or one
/// that is hostile to the shooter.</item>
/// <item><b>What</b>: a speech bubble of kind <see cref="NpcGreetingKinds.Alert"/> with the gesture
/// <see cref="NpcGestures.HandsUp"/> (both hands up, the mouth wide open), heard by every player nearby; the NPC stands
/// still and looks at the shooter for a moment. One line of the pool names the shooter.</item>
/// <item><b>The tone is startled and funny, never afraid</b> (the game has no fear-driven mechanics): indignant call-outs,
/// no cry for help, nobody runs away, and a few seconds later everybody carries on.</item>
/// <item><b>The pace</b>: one bout per shooter every <see cref="StartleShooterGapSeconds"/>, one per place every
/// <see cref="StartlePlaceGapSeconds"/> (whoever shoots), one call-out per NPC every <see cref="StartleNpcGapSeconds"/>
/// — a burst of fire is one fright, not a chorus.</item>
/// </list>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>How far from the shooter an NPC is startled, in blocks.</summary>
    internal const float StartleRange = 12f;

    /// <summary>How far the call-out is heard (the villager chatter's reach).</summary>
    internal const float StartleHeardRange = 24f;

    /// <summary>At most this many NPCs call out at one shot or swing — the nearest ones.</summary>
    internal const int StartleMaxNpcs = 3;

    /// <summary>Seconds before the same shooter startles anybody again.</summary>
    internal const double StartleShooterGapSeconds = 5.0;

    /// <summary>Seconds before the same NPC calls out again.</summary>
    internal const double StartleNpcGapSeconds = 25.0;

    /// <summary>How long a startled NPC stands still and looks at the shooter.</summary>
    internal const double StartleAttendSeconds = 3.0;

    /// <summary>The call-outs: <c>npc.startle.1</c> … <c>npc.startle.8</c>; <c>{0}</c> is the shooter's name.</summary>
    internal const int StartleLineCount = 8;

    /// <summary>Seconds before anybody startles the people of the same place again — whoever shoots. Without it a
    /// crowded square answered every shooter's every bout in turn, a chorus that never ended under steady fire.</summary>
    internal const double StartlePlaceGapSeconds = 12.0;

    private readonly Dictionary<string, double> _startleShooterReadyAt = new(System.StringComparer.Ordinal);
    private readonly List<(string World, Vector3f At, double Until)> _startlePlaces = new();
    private readonly List<(ServerNpc Npc, double DistSq)> _startleCandidates = new();
    private int _startleBouts;

    /// <summary>Whether a relayed weapon effect is one that startles: a shot or a swing of a WEAPON — never the fist, a
    /// tool or a block in the hand. The item decides for a shot too: a mining beam or a drill "fired" at something sends
    /// the same effect kind as a pistol, and tools startle nobody.</summary>
    internal bool StartlingEffect(byte kind, string itemKey)
        => (kind == FxActionKinds.Shot || kind == FxActionKinds.Melee)
           && _content.GetItem(itemKey)?.Tool?.Kind == ToolKind.Weapon;

    /// <summary>Whether the people around <paramref name="at"/> were startled a moment ago (by anybody); forgets the
    /// places whose rest is over.</summary>
    private bool StartledPlaceRests(Vector3f at)
    {
        string world = _world.LocationId;
        double rangeSq = (double)StartleRange * StartleRange;
        bool rests = false;
        for (int i = _startlePlaces.Count - 1; i >= 0; i--)
        {
            var place = _startlePlaces[i];
            if (_uptime >= place.Until)
            {
                _startlePlaces.RemoveAt(i);
            }
            else if (place.World == world && WrapDistSq(at, place.At) <= rangeSq)
            {
                rests = true;
            }
        }

        return rests;
    }

    /// <summary>Whether an NPC can be startled at all: awake, not a G.D.S. guardian, not away visiting a base, not busy in
    /// a talk with a player.</summary>
    private bool MayBeStartled(ServerNpc npc)
        => npc.Pose != 2 && npc.Role != "guardian" && !npc.IsChild && !npc.VisitingAway && !NpcInPlayerDialog(npc); // nobody frightens a child

    /// <summary>A player on the surface fired or swung a weapon at <paramref name="at"/> (from <see cref="HandleFxIntent"/>,
    /// after its checks): the nearest NPCs around call out. Returns how many did.</summary>
    internal int StartleNpcsNear(PlayerSession shooter, Vector3f at)
    {
        var p = shooter.State;
        if (_npcs.Count == 0 || !OnFootHere(shooter)
            || (_startleShooterReadyAt.TryGetValue(p.PlayerId, out double ready) && _uptime < ready)
            || StartledPlaceRests(at))
        {
            return 0;
        }

        _startleCandidates.Clear();
        double rangeSq = (double)StartleRange * StartleRange;
        foreach (var npc in _npcs)
        {
            double d = WrapDistSq(at, npc.Pos);
            if (d > rangeSq || !MayBeStartled(npc) || _uptime < npc.StartleReadyAt
                || RelationshipTier(RelationshipValue(p, NpcKeyForNpc(shooter, npc))) == "hostile")
            {
                continue;
            }

            _startleCandidates.Add((npc, d));
        }

        if (_startleCandidates.Count == 0)
        {
            return 0;
        }

        _startleCandidates.Sort((a, b) => a.DistSq.CompareTo(b.DistSq));
        _startleShooterReadyAt[p.PlayerId] = _uptime + StartleShooterGapSeconds;
        int bout = _startleBouts++;
        int called = 0;
        for (int i = 0; i < _startleCandidates.Count && called < StartleMaxNpcs; i++)
        {
            var npc = _startleCandidates[i].Npc;
            string key = StartleLineKey(bout, called);
            int id = BroadcastNpcLine(npc, StartleHeardRange, s => Localize(s.Locale, key).Replace("{0}", p.Name),
                NpcGreetingKinds.Alert, NpcGestures.HandsUp);
            if (id == 0)
            {
                continue;
            }

            // On the NPC itself, not in a table by its number: the numbers start again on every world, and a table
            // would hand one NPC's rest to a stranger with the same number (and never forget anybody).
            npc.StartleReadyAt = _uptime + StartleNpcGapSeconds;
            npc.StartledUntil = _uptime + StartleAttendSeconds; // stands still on a route too (the NPC tick)
            npc.AttendUntil = System.Math.Max(npc.AttendUntil, _uptime + StartleAttendSeconds);
            npc.AttendPos = p.Position;
            called++;
        }

        if (called > 0)
        {
            _startlePlaces.Add((_world.LocationId, at, _uptime + StartlePlaceGapSeconds));
        }

        return called;
    }

    /// <summary>The call-out of the <paramref name="index"/>-th NPC of a bout: the lines take turns from bout to bout, and
    /// two NPCs of one bout never say the same.</summary>
    internal static string StartleLineKey(int bout, int index)
        => "npc.startle." + ((((bout * 3) + index) % StartleLineCount) + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Test seam: a player's weapon effect as the dispatch would hand it over.</summary>
    internal void FxIntentForTest(PlayerSession session, FxIntent intent) => HandleFxIntent(session, intent);
}
