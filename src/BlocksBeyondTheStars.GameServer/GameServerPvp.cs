// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// On-foot PvP (#2972). <see cref="GameRules.Pvp"/> and <see cref="GameRules.FriendlyFire"/> had been world rules for a long
/// time without a single reader — players could not hurt each other at all. Now an attack on <c>player:&lt;id&gt;</c> runs the
/// same swing as an attack on a machine (cooldown, reach, line of fire, aim, suit energy, damage) once
/// <see cref="PvpRefusal"/> lets it through: the rule table (<see cref="PvpRules.Refusal"/>) plus the guards — nobody aboard a
/// ship, in a station, in space or on a spacewalk, in a vehicle, spectating, in god mode or a Sandbox, waiting to respawn,
/// or within <see cref="PvpRules.SpawnProtectionSeconds"/> of joining or of being defeated by a player. A player defeated by
/// another keeps every item whatever the death penalty says; a duel's loser is not defeated at all — the duel ends and the
/// loser gets back up. Each player's client is told whom it may hit (<see cref="PvpTargets"/>, 1 Hz on change) so its aim
/// only picks those avatars.
/// </summary>
public sealed partial class GameServer
{
    private readonly Dictionary<string, double> _pvpProtectedUntil = new();
    private readonly Dictionary<string, string> _pvpTargetsSent = new();
    private readonly Dictionary<string, string> _duelPartner = new();
    private readonly Dictionary<string, double> _duelEndsAt = new();
    private readonly Dictionary<string, (string From, double Until)> _duelRequests = new(); // keyed by the challenged player
    private readonly List<string> _pvpScratch = new();
    private double _pvpTimer;

    /// <summary>An attack on another player (the entity id was <c>player:&lt;id&gt;</c>).</summary>
    private void AttackPlayer(PlayerSession session, string targetPlayerId, Shared.Geometry.Vector3f aimDir)
    {
        if (FindSessionByPlayerId(targetPlayerId) is not { Joined: true } target || target == session)
        {
            Reject(session, "attack", "@srv.attack.no_target");
            return;
        }

        if (PvpRefusal(session, target) is { } reason)
        {
            Reject(session, "attack", reason);
            return;
        }

        var p = session.State;
        var tool = ActiveTool(p);
        bool isWeapon = tool.Kind == ToolKind.Weapon;
        if (!SwingReady(session, tool, isWeapon))
        {
            return;
        }

        float reach = isWeapon ? System.Math.Max(tool.Range, EnemyAttackReach) : EnemyAttackReach;
        if (WrapDistSq(p.Position, target.State.Position) > reach * reach)
        {
            Reject(session, "attack", "@srv.attack.out_of_reach");
            return;
        }

        if (!ValidateAim(session, new CombatEntity { Position = target.State.Position }, tool, aimDir) || !PayShot(session, tool, isWeapon))
        {
            return;
        }

        DamagePlayer(target.State, SwingDamage(p, tool, isWeapon), mitigated: true);
        NoteCombat(session); // "Back to my ship" waits out a fight between players too
        NoteCombat(target);
        target.Sync.LastHitEntityId = PvpRules.TargetId(p.PlayerId); // the HUD marker points at the attacker
        target.Sync.LastHitFrom = p.Position;
        target.Sync.LastHitSeq = target.Sync.LastHitSeq == int.MaxValue ? 1 : target.Sync.LastHitSeq + 1;
        MarkPlayerStateDirty(target);
        if (target.State.Health <= 0f)
        {
            DefeatPlayer(session, target);
        }
    }

    /// <summary>Why this attacker may not hurt this target right now — a reject token — or null. The world's rule table
    /// first, then the guards on both sides.</summary>
    private string? PvpRefusal(PlayerSession attacker, PlayerSession target)
    {
        string a = attacker.State.PlayerId, t = target.State.PlayerId;
        bool duel = _duelPartner.TryGetValue(a, out var partner) && partner == t;
        if (PvpRules.Refusal(Rules.Pvp, Rules.FriendlyFire, AreAllied(a, t), CrewOf(a)?.Id ?? string.Empty,
                CrewOf(t)?.Id ?? string.Empty, duel) is { } rule)
        {
            return rule;
        }

        return PvpGuarded(attacker) || PvpGuarded(target) || attacker.CurrentLocationId != target.CurrentLocationId
            ? PvpRules.ProtectedToken
            : null;
    }

    /// <summary>A player out of every fight between players: not on foot on a planet, spectating, protected by mode or god
    /// mode, waiting to respawn, or freshly spawned.</summary>
    private bool PvpGuarded(PlayerSession session)
    {
        var p = session.State;
        return !session.Joined || session.Spectating || p.AboardShip || p.InEva || InSpace(p.PlayerId) || InStation(p.PlayerId)
            || _inShipInterior.ContainsKey(p.PlayerId) || p.InSpeeder.Length > 0 || p.InTrain.Length > 0
            || !PlayerTakesDamage(p) || p.Health <= 0f || session.Respawn.RespawnChoiceDeadline > 0
            || (_pvpProtectedUntil.TryGetValue(p.PlayerId, out var until) && _uptime < until);
    }

    /// <summary>The target went down by the attacker's hand. In a duel between the two that only ends the duel; otherwise
    /// the world hears who won, and the target respawns keeping every item, protected for a moment.</summary>
    private void DefeatPlayer(PlayerSession attacker, PlayerSession target)
    {
        string a = attacker.State.PlayerId, t = target.State.PlayerId;
        if (_duelPartner.TryGetValue(a, out var partner) && partner == t)
        {
            target.State.Health = PvpRules.DuelLoserHealth;
            EndDuel(a, t, winner: attacker.State.Name);
            MarkPlayerStateDirty(target);
            return;
        }

        foreach (var s in JoinedInActiveWorld())
        {
            Send(s, new ServerMessage
            {
                Text = Localize(s.Locale, "srv.pvp.defeated").Replace("{attacker}", attacker.State.Name).Replace("{target}", target.State.Name),
            });
        }

        target.Respawn.SparedByPvp = true; // a defeat by a player never costs an item
        try
        {
            RespawnPlayer(target, PvpRules.DefeatedYouToken + ":" + attacker.State.Name);
        }
        finally
        {
            target.Respawn.SparedByPvp = false;
        }

        _pvpProtectedUntil[t] = _uptime + PvpRules.SpawnProtectionSeconds;
    }

    // ---------------- World rules ----------------

    /// <summary>The world admin's live edit of the PvP mode and friendly fire (from <see cref="SetWorldRulesIntent"/>).</summary>
    private void ApplyPvpRules(SetWorldRulesIntent intent)
    {
        if (!string.IsNullOrEmpty(intent.Pvp) && System.Enum.TryParse<PvpMode>(intent.Pvp, ignoreCase: true, out var mode))
        {
            Rules.Pvp = mode;
            if (mode == PvpMode.Off)
            {
                foreach (var (a, b) in new List<KeyValuePair<string, string>>(_duelPartner))
                {
                    if (_duelPartner.ContainsKey(a))
                    {
                        EndDuel(a, b, winner: string.Empty); // no fights at all any more
                    }
                }
            }
        }

        ApplyOnOff(intent.FriendlyFire, v => Rules.FriendlyFire = v);
        _pvpTimer = 1.0; // every player's target list follows on the next tick
    }

    /// <summary>A live-edit toggle: "On"/"Off" sets it, empty leaves it.</summary>
    private static void ApplyOnOff(string value, System.Action<bool> set)
    {
        if (!string.IsNullOrEmpty(value))
        {
            set(value.Equals("On", System.StringComparison.OrdinalIgnoreCase));
        }
    }

    // ---------------- Duels ----------------

    private void HandleDuel(PlayerSession session, DuelIntent intent)
    {
        string me = session.State.PlayerId;
        string other = intent.TargetPlayerId ?? string.Empty;
        switch ((intent.Action ?? string.Empty).ToLowerInvariant())
        {
            case "challenge":
                ChallengeDuel(session, other);
                break;
            case "accept":
                if (_duelRequests.TryGetValue(me, out var req) && req.From == other && _uptime < req.Until
                    && FindSessionByPlayerId(other) is { Joined: true } challenger && !_duelPartner.ContainsKey(other) && !_duelPartner.ContainsKey(me))
                {
                    _duelRequests.Remove(me);
                    _duelPartner[me] = other;
                    _duelPartner[other] = me;
                    _duelEndsAt[me] = _duelEndsAt[other] = _uptime + PvpRules.DuelMaxSeconds;
                    Send(session, new DuelNotice { Kind = "started", Other = other });
                    Send(challenger, new DuelNotice { Kind = "started", Other = me });
                    _pvpTimer = 1.0; // the target lists follow at once
                }
                else
                {
                    Reject(session, "duel", "@srv.pvp.duel_gone");
                }

                break;
            case "decline":
                if (_duelRequests.TryGetValue(me, out var declined) && declined.From == other)
                {
                    _duelRequests.Remove(me);
                    if (FindSessionByPlayerId(other) is { } from)
                    {
                        Send(from, new DuelNotice { Kind = "declined", Other = me });
                    }
                }

                break;
            case "end":
                if (_duelPartner.TryGetValue(me, out var partner))
                {
                    EndDuel(me, partner, winner: string.Empty);
                }

                break;
        }
    }

    private void ChallengeDuel(PlayerSession session, string other)
    {
        string me = session.State.PlayerId;
        if (Rules.Pvp == PvpMode.Off)
        {
            Reject(session, "duel", PvpRules.OffToken);
            return;
        }

        if (other == me || FindSessionByPlayerId(other) is not { Joined: true } target || target.Spectating)
        {
            Reject(session, "duel", "@srv.pvp.duel_gone");
            return;
        }

        if (_duelPartner.ContainsKey(me) || _duelPartner.ContainsKey(other))
        {
            Reject(session, "duel", "@srv.pvp.duel_busy");
            return;
        }

        _duelRequests[other] = (me, _uptime + PvpRules.DuelRequestSeconds);
        Send(target, new DuelNotice { Kind = "request", Other = me });
        Send(session, new ServerMessage { Text = "@srv.pvp.duel_sent:" + target.State.Name });
    }

    /// <summary>Ends the duel between the two (winner empty = nobody won) and tells both.</summary>
    private void EndDuel(string a, string b, string winner)
    {
        _duelPartner.Remove(a);
        _duelPartner.Remove(b);
        _duelEndsAt.Remove(a);
        _duelEndsAt.Remove(b);
        foreach (var (who, other) in new[] { (a, b), (b, a) })
        {
            if (FindSessionByPlayerId(who) is { } s)
            {
                Send(s, new DuelNotice { Kind = "ended", Other = other, Winner = winner });
                _pvpProtectedUntil[who] = _uptime + PvpRules.SpawnProtectionSeconds; // no instant re-fight after the bell
            }
        }

        _pvpTimer = 1.0;
    }

    // ---------------- The 1 Hz pass ----------------

    /// <summary>Once a second: a newly joined player gets the spawn protection, duels that ran out or lost a player end,
    /// stale challenges go, and every player whose list of hittable players changed is told.</summary>
    private void TickPvp(double dt)
    {
        _pvpTimer += dt;
        if (_pvpTimer < 1.0)
        {
            return;
        }

        _pvpTimer = 0;
        ExpireDuels();
        foreach (var s in _sessions.Values)
        {
            if (!s.Joined)
            {
                continue;
            }

            string id = s.State.PlayerId;
            if (!_pvpTargetsSent.ContainsKey(id) && !_pvpProtectedUntil.ContainsKey(id))
            {
                _pvpProtectedUntil[id] = _uptime + PvpRules.SpawnProtectionSeconds; // just joined
            }

            _pvpScratch.Clear();
            if (Rules.Pvp != PvpMode.Off)
            {
                foreach (var other in _sessions.Values)
                {
                    if (other != s && PvpRefusal(s, other) is null)
                    {
                        _pvpScratch.Add(other.State.PlayerId);
                    }
                }
            }

            _pvpScratch.Sort(System.StringComparer.Ordinal);
            string signature = string.Join("\n", _pvpScratch);
            if (!_pvpTargetsSent.TryGetValue(id, out var sent) || sent != signature)
            {
                _pvpTargetsSent[id] = signature;
                Send(s, new PvpTargets { PlayerIds = _pvpScratch.ToArray() });
            }
        }

        ForgetPvpOfAbsentPlayers();
    }

    private void ExpireDuels()
    {
        foreach (var (id, partner) in new List<KeyValuePair<string, string>>(_duelPartner))
        {
            bool gone = FindSessionByPlayerId(id) is not { Joined: true } || FindSessionByPlayerId(partner) is not { Joined: true };
            if (_duelPartner.ContainsKey(id) && (gone || (_duelEndsAt.TryGetValue(id, out var ends) && _uptime >= ends)))
            {
                EndDuel(id, partner, winner: string.Empty);
            }
        }

        foreach (var (challenged, req) in new List<KeyValuePair<string, (string From, double Until)>>(_duelRequests))
        {
            if (_uptime >= req.Until)
            {
                _duelRequests.Remove(challenged);
                if (FindSessionByPlayerId(req.From) is { } from)
                {
                    Send(from, new DuelNotice { Kind = "declined", Other = challenged });
                }
            }
        }
    }

    /// <summary>A player who left starts over: protection on the next join, a fresh target list.</summary>
    private void ForgetPvpOfAbsentPlayers()
    {
        foreach (var id in new List<string>(_pvpTargetsSent.Keys))
        {
            if (FindSessionByPlayerId(id) is not { Joined: true })
            {
                _pvpTargetsSent.Remove(id);
                _pvpProtectedUntil.Remove(id);
            }
        }
    }

    // ---------------- Test seams ----------------

    /// <summary>Test seam: the live world rules (the server works on its own copy, not the config's object).</summary>
    public GameRules RulesForTest => Rules;

    /// <summary>Test seam: lifts the spawn protection of a player.</summary>
    public void ClearPvpProtectionForTest(string playerId) => _pvpProtectedUntil[playerId] = 0;

    /// <summary>Test seam: runs the 1 Hz PvP pass now.</summary>
    public void TickPvpForTest() => TickPvp(1.0);

    /// <summary>Test seam: the reject token (or null) for this attacker and target.</summary>
    public string? PvpRefusalForTest(string attacker, string target)
        => FindSessionByPlayerId(attacker) is { } a && FindSessionByPlayerId(target) is { } t ? PvpRefusal(a, t) : "@srv.attack.no_target";

    /// <summary>Test seam: a duel intent from this player.</summary>
    public void DuelForTest(string playerId, string action, string other)
    {
        if (FindSessionByPlayerId(playerId) is { } s)
        {
            HandleDuel(s, new DuelIntent { Action = action, TargetPlayerId = other });
        }
    }

    /// <summary>Test seam: an attack on a player, as the dispatcher would route it.</summary>
    public void AttackPlayerForTest(string attacker, string target, Shared.Geometry.Vector3f aim = default)
    {
        if (FindSessionByPlayerId(attacker) is { } s)
        {
            Serve(s);
            AttackPlayer(s, target, aim);
        }
    }
}
