// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Player alliances: two players form a mutual, pairwise alliance (not transitive — A↔B and B↔C does not imply
/// A↔C). Allies co-own each other's space stations and planet bases — they may build/mine/use/board them as if
/// they owned them (see the <c>AreAllied</c> checks in the station/base access paths) — and they cannot harm one
/// another, even on a PVP server. The player ship is deliberately excluded: an alliance grants no ship access.
///
/// Forming needs the other player's confirmation (a request/accept handshake, like ship docking); dissolving is
/// one-sided. The alliance graph is server-wide and persisted (mirrors the planet-base load at server start);
/// pending requests are transient (in-memory) and cleared on disconnect, like docking requests.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Symmetric alliance graph: each player id maps to the set of player ids it is allied with.</summary>
    private readonly Dictionary<string, HashSet<string>> _allies = new();

    /// <summary>"allied since" timestamps, keyed by the normalised player-id pair (for the menu display).</summary>
    private readonly Dictionary<string, string> _allianceFormed = new();

    /// <summary>Outstanding alliance proposals awaiting the target's accept/decline: (requester, target) pairs.</summary>
    private readonly HashSet<(string From, string To)> _pendingAlliance = new();

    /// <summary>True while the two players are in an alliance (order-independent). The one predicate every
    /// station/base access + non-aggression guard funnels through. Since #1216 a shared CREW also allies its
    /// members — the crew edges live in their own structures (<see cref="SameCrew"/>), so dissolving a manual
    /// pairwise alliance never cuts crew access and leaving the crew never cuts a manual alliance.</summary>
    public bool AreAllied(string a, string b)
        => PairAllied(a, b) || SameCrew(a, b);

    /// <summary>The manual pairwise half of <see cref="AreAllied"/> (the persisted handshake graph alone).</summary>
    public bool PairAllied(string a, string b)
        => !string.IsNullOrEmpty(a) && !string.IsNullOrEmpty(b) && a != b
           && _allies.TryGetValue(a, out var set) && set.Contains(b);

    /// <summary>Alliance pairs (a, b, formedUtc) for tests + inspection.</summary>
    public IReadOnlyList<(string A, string B, string Formed)> AllianceSnapshots
        => _allianceFormed.Select(kv =>
        {
            int sep = kv.Key.IndexOf('\0');
            return (kv.Key.Substring(0, sep), kv.Key.Substring(sep + 1), kv.Value);
        }).ToList();

    /// <summary>Loads the persisted alliance graph at server start (server-wide, like planet bases). Idempotent.</summary>
    private void LoadAllAlliances()
    {
        _allies.Clear();
        _allianceFormed.Clear();
        foreach (var sa in _repo.ListAlliances())
        {
            AddAllyEdge(sa.PlayerA, sa.PlayerB);
            _allianceFormed[PairKey(sa.PlayerA, sa.PlayerB)] = sa.FormedUtc;
        }

        if (_allies.Count > 0)
        {
            _log.Info($"Loaded alliances for {_allies.Count} player(s).");
        }
    }

    private void AddAllyEdge(string a, string b)
    {
        if (!_allies.TryGetValue(a, out var sa))
        {
            _allies[a] = sa = new HashSet<string>();
        }

        if (!_allies.TryGetValue(b, out var sb))
        {
            _allies[b] = sb = new HashSet<string>();
        }

        sa.Add(b);
        sb.Add(a);
    }

    private void RemoveAllyEdge(string a, string b)
    {
        if (_allies.TryGetValue(a, out var sa))
        {
            sa.Remove(b);
            if (sa.Count == 0)
            {
                _allies.Remove(a);
            }
        }

        if (_allies.TryGetValue(b, out var sb))
        {
            sb.Remove(a);
            if (sb.Count == 0)
            {
                _allies.Remove(b);
            }
        }
    }

    /// <summary>Order-independent pair key (id-A \0 id-B) for the formed-timestamp map.</summary>
    private static string PairKey(string x, string y)
        => string.CompareOrdinal(x, y) <= 0 ? x + '\0' + y : y + '\0' + x;

    // ---------------- Handshake ----------------

    /// <summary>A player proposes an alliance to another. Validates, records a pending request and notifies the
    /// target, who must confirm with <see cref="RespondAlliance"/>.</summary>
    public void RequestAlliance(string fromId, string toId)
    {
        var fromSession = FindSessionByPlayerId(fromId);

        if (string.IsNullOrEmpty(toId) || fromId == toId)
        {
            RejectAlliance(fromSession, "@srv.ally.bad_target");
            return;
        }

        // Crew mates may still form a MANUAL alliance on top (it survives either of them leaving the crew),
        // so only an existing pairwise alliance blocks the request.
        if (PairAllied(fromId, toId))
        {
            RejectAlliance(fromSession, "@srv.ally.already");
            return;
        }

        var toSession = FindSessionByPlayerId(toId);
        if (toSession is null)
        {
            RejectAlliance(fromSession, "@srv.ally.offline");
            return;
        }

        // The other side already asked me → treat my "request back" as an accept (mutual agreement).
        if (_pendingAlliance.Contains((toId, fromId)))
        {
            RespondAlliance(fromId, toId, accept: true);
            return;
        }

        if (!_pendingAlliance.Add((fromId, toId)))
        {
            RejectAlliance(fromSession, "@srv.ally.pending");
            return;
        }

        Send(toSession, new AllianceRequestNotice
        {
            RequesterId = fromId,
            RequesterName = fromSession?.State.Name ?? fromId,
        });
        RefreshAllianceRoster(fromId);
        RefreshAllianceRoster(toId);
    }

    /// <summary>The request target accepts or declines a pending proposal from <paramref name="fromId"/>.
    /// Accepting establishes the mutual alliance; declining drops the request and notifies the requester.</summary>
    public void RespondAlliance(string toId, string fromId, bool accept)
    {
        if (!_pendingAlliance.Remove((fromId, toId)))
        {
            return; // no matching pending request (already answered / withdrawn)
        }

        if (!accept)
        {
            var requester = FindSessionByPlayerId(fromId);
            if (requester is not null)
            {
                Send(requester, new ServerMessage { Text = "@srv.ally.declined" });
            }

            RefreshAllianceRoster(fromId);
            RefreshAllianceRoster(toId);
            return;
        }

        EstablishAlliance(fromId, toId);
    }

    private void EstablishAlliance(string a, string b)
    {
        AddAllyEdge(a, b);
        string formed = System.DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
        _allianceFormed[PairKey(a, b)] = formed;
        _repo.SaveAlliance(new StoredAlliance { PlayerA = a, PlayerB = b, FormedUtc = formed });

        // Any reverse pending request is now moot.
        _pendingAlliance.Remove((a, b));
        _pendingAlliance.Remove((b, a));

        NotifyAlliance(a, b, "@srv.ally.formed");
        NotifyAlliance(b, a, "@srv.ally.formed");
        RaiseGameEvent(FindSessionByPlayerId(a), Shared.Story.GameEventKeys.AllyJoined, Shared.Story.GameEventKeys.AllyAlliance); // #2482: the "Together" course
        RaiseGameEvent(FindSessionByPlayerId(b), Shared.Story.GameEventKeys.AllyJoined, Shared.Story.GameEventKeys.AllyAlliance);
        RefreshAllianceRoster(a);
        RefreshAllianceRoster(b);
        RefreshMarkersFor(new[] { a, b }); // each now sees the other's shared markers (#1293)
    }

    /// <summary>Either party dissolves an existing alliance. Both sides lose the shared access immediately —
    /// unless they are also crew mates: the crew edge is not touched here (leave the crew to cut that).</summary>
    public void DissolveAlliance(string actorId, string partnerId)
    {
        if (!PairAllied(actorId, partnerId))
        {
            return;
        }

        RemoveAllyEdge(actorId, partnerId);
        _allianceFormed.Remove(PairKey(actorId, partnerId));
        _repo.DeleteAlliance(actorId, partnerId);

        NotifyAlliance(actorId, partnerId, "@srv.ally.ended");
        NotifyAlliance(partnerId, actorId, "@srv.ally.ended");
        RefreshAllianceRoster(actorId);
        RefreshAllianceRoster(partnerId);
        RefreshMarkersFor(new[] { actorId, partnerId }); // the other's shared markers drop off the map (#1293)
    }

    /// <summary>Drops every pending request involving a disconnecting player and refreshes the rosters of the
    /// still-online allies / counterparts (so their "online" flags + pending lists update). Established
    /// alliances persist across the disconnect — only the transient requests are cleared.</summary>
    private void ClearAlliancePending(string playerId)
    {
        var stale = _pendingAlliance.Where(p => p.From == playerId || p.To == playerId).ToList();
        foreach (var pair in stale)
        {
            _pendingAlliance.Remove(pair);
            string other = pair.From == playerId ? pair.To : pair.From;
            RefreshAllianceRoster(other);
        }

        // Let online allies see the partner drop offline in their roster.
        if (_allies.TryGetValue(playerId, out var set))
        {
            foreach (var ally in set)
            {
                RefreshAllianceRoster(ally);
            }
        }
    }

    // ---------------- Roster + notices ----------------

    /// <summary>Builds and sends one player's full alliance roster (confirmed allies + pending both ways).</summary>
    private void SendAllianceList(PlayerSession session)
    {
        string me = session.State.PlayerId;

        // Manual pairwise allies, then crew mates who are NOT also pairwise-allied — flagged Crew so the
        // client can hide the "End" button (crew access is left through the Crew view, not dissolved here),
        // while everything that reads the roster (ally beam, teleporter) simply keeps working.
        var entries = new List<NetAlliance>();
        if (_allies.TryGetValue(me, out var set))
        {
            foreach (var id in set)
            {
                var s = FindSessionByPlayerId(id);
                entries.Add(new NetAlliance
                {
                    PartnerId = id,
                    PartnerName = s?.State.Name ?? id, // PlayerId == display name for offline players
                    FormedUtc = _allianceFormed.GetValueOrDefault(PairKey(me, id), string.Empty),
                    Online = s is not null,
                });
            }
        }

        if (CrewOf(me) is { } crew)
        {
            foreach (var id in crew.Members.Keys)
            {
                if (id == me || entries.Any(e => e.PartnerId == id))
                {
                    continue;
                }

                var s = FindSessionByPlayerId(id);
                entries.Add(new NetAlliance
                {
                    PartnerId = id,
                    PartnerName = s?.State.Name ?? id,
                    FormedUtc = crew.Members[id],
                    Online = s is not null,
                    Crew = true,
                });
            }
        }

        NetAlliance[] allies = entries.OrderBy(a => a.PartnerName, System.StringComparer.OrdinalIgnoreCase).ToArray();

        NetAllianceRequest[] incoming = _pendingAlliance
            .Where(p => p.To == me)
            .Select(p => new NetAllianceRequest { PlayerId = p.From, PlayerName = NameOf(p.From) })
            .ToArray();

        NetAllianceRequest[] outgoing = _pendingAlliance
            .Where(p => p.From == me)
            .Select(p => new NetAllianceRequest { PlayerId = p.To, PlayerName = NameOf(p.To) })
            .ToArray();

        Send(session, new AllianceList { Allies = allies, Incoming = incoming, Outgoing = outgoing });
    }

    /// <summary>Re-sends the roster to a player if they are online (no-op otherwise). Every alliance and crew change ends here,
    /// so on a hidden star map the shared chart (#3997) goes out with it.</summary>
    private void RefreshAllianceRoster(string playerId)
    {
        if (FindSessionByPlayerId(playerId) is { } session)
        {
            SendAllianceList(session);
            RefreshSharedCharts(playerId);
        }
    }

    private void NotifyAlliance(string playerId, string partnerId, string verb)
    {
        if (FindSessionByPlayerId(playerId) is { } session)
        {
            Send(session, new ServerMessage { Text = verb + ":" + NameOf(partnerId) });
        }
    }

    private string NameOf(string playerId) => FindSessionByPlayerId(playerId)?.State.Name ?? playerId;

    private void RejectAlliance(PlayerSession? session, string reason)
    {
        if (session is not null)
        {
            Reject(session, "alliance", reason);
        }
    }

    // ---------------- Intent handlers (dispatched from OnPayload) ----------------

    private void HandleRequestAllianceList(PlayerSession session) => SendAllianceList(session);

    private void HandleRequestAlliance(PlayerSession session, RequestAllianceIntent intent)
        => RequestAlliance(session.State.PlayerId, intent.TargetPlayerId);

    private void HandleAllianceResponse(PlayerSession session, AllianceResponseIntent intent)
        => RespondAlliance(session.State.PlayerId, intent.RequesterId, intent.Accept);

    private void HandleDissolveAlliance(PlayerSession session, DissolveAllianceIntent intent)
        => DissolveAlliance(session.State.PlayerId, intent.PartnerId);

    // ---------------- Test / util entrypoints ----------------

    /// <summary>Test hook: propose an alliance as the given player (mirrors the request intent).</summary>
    public void RequestAllianceForTest(string fromId, string toId) => RequestAlliance(fromId, toId);

    /// <summary>Test hook: answer a pending request as the target (mirrors the response intent).</summary>
    public void RespondAllianceForTest(string toId, string fromId, bool accept) => RespondAlliance(toId, fromId, accept);

    /// <summary>Test hook: dissolve an alliance as the given player (mirrors the dissolve intent).</summary>
    public void DissolveAllianceForTest(string actorId, string partnerId) => DissolveAlliance(actorId, partnerId);
}
