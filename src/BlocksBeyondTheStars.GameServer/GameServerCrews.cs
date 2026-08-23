// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Crews (#1216): a named group of up to <see cref="CrewMaxMembers"/> players layered on the pairwise alliance
/// primitive. Membership implies being allied with every other member — <c>AreAllied</c> answers
/// "pairwise OR same crew" — but the crew edges live HERE, in their own structures, so dissolving a manual
/// alliance never cuts crew access and leaving the crew never cuts a manual alliance.
///
/// <para>Design constraints, all deliberate: a player is in at most ONE crew (a second crew would make the
/// shared-access question ambiguous and the roster unreadable); invites go only to ONLINE players and there are
/// no join codes (kid safety — nobody can be pulled into a group by a string pasted from outside the game);
/// roles are owner-or-member only (the owner invites / kicks / renames / disbands; anyone may leave; an owner
/// leaving hands the crew to its oldest member). The crew graph is server-wide and persisted like the alliance
/// graph; pending invites are transient and cleared on disconnect, like alliance requests.</para>
/// </summary>
public sealed partial class GameServer
{
    private const int CrewMaxMembers = 8;
    private const int CrewNameMaxLength = 24;

    /// <summary>A crew on the server (persisted via <see cref="StoredCrew"/> + <see cref="StoredCrewMember"/>).</summary>
    internal sealed class ServerCrew
    {
        public string Id = string.Empty;
        public string Name = string.Empty;
        public string OwnerId = string.Empty;
        public string CreatedUtc = string.Empty;

        /// <summary>Member player id → ISO-8601 join timestamp (oldest member inherits an abandoned crew).</summary>
        public readonly Dictionary<string, string> Members = new();
    }

    private readonly Dictionary<string, ServerCrew> _crews = new();          // crewId → crew
    private readonly Dictionary<string, string> _crewByPlayer = new();       // playerId → crewId (≤ one crew)
    private readonly HashSet<(string CrewId, string To)> _pendingCrewInvites = new();

    /// <summary>True while the two players are members of the same crew — the crew half of <c>AreAllied</c>.</summary>
    public bool SameCrew(string a, string b)
        => !string.IsNullOrEmpty(a) && !string.IsNullOrEmpty(b) && a != b
           && _crewByPlayer.TryGetValue(a, out var ca) && _crewByPlayer.TryGetValue(b, out var cb) && ca == cb;

    private ServerCrew? CrewOf(string playerId)
        => _crewByPlayer.TryGetValue(playerId, out var id) && _crews.TryGetValue(id, out var c) ? c : null;

    /// <summary>Crew states (id/name/owner/members) for tests + inspection.</summary>
    public IReadOnlyList<(string Id, string Name, string OwnerId, string[] Members)> CrewSnapshots
        => _crews.Values.Select(c => (c.Id, c.Name, c.OwnerId, c.Members.Keys.OrderBy(m => m, StringComparer.Ordinal).ToArray())).ToList();

    /// <summary>Loads the persisted crews at server start (server-wide, like the alliance graph). Idempotent.
    /// A member row pointing at a missing crew is dropped; a crew whose owner row went missing is healed by
    /// promoting its oldest member, exactly like an owner leaving.</summary>
    private void LoadAllCrews()
    {
        _crews.Clear();
        _crewByPlayer.Clear();
        foreach (var sc in _repo.ListCrews())
        {
            _crews[sc.CrewId] = new ServerCrew { Id = sc.CrewId, Name = sc.Name, OwnerId = sc.OwnerId, CreatedUtc = sc.CreatedUtc };
        }

        foreach (var m in _repo.ListCrewMembers())
        {
            if (_crews.TryGetValue(m.CrewId, out var crew) && !_crewByPlayer.ContainsKey(m.PlayerId))
            {
                crew.Members[m.PlayerId] = m.JoinedUtc;
                _crewByPlayer[m.PlayerId] = m.CrewId;
            }
        }

        // Defensive healing: an empty crew (all member rows lost) is deleted; a crew whose owner is not a
        // member anymore hands ownership to the oldest member.
        foreach (var crew in _crews.Values.ToList())
        {
            if (crew.Members.Count == 0)
            {
                _crews.Remove(crew.Id);
                _repo.DeleteCrew(crew.Id);
                continue;
            }

            if (!crew.Members.ContainsKey(crew.OwnerId))
            {
                crew.OwnerId = OldestMember(crew);
                _repo.SaveCrew(ToStoredCrew(crew));
            }
        }

        if (_crews.Count > 0)
        {
            _log.Info($"Loaded {_crews.Count} crew(s).");
        }
    }

    private static string OldestMember(ServerCrew crew)
        => crew.Members.OrderBy(kv => kv.Value, StringComparer.Ordinal).ThenBy(kv => kv.Key, StringComparer.Ordinal).First().Key;

    private static StoredCrew ToStoredCrew(ServerCrew c)
        => new() { CrewId = c.Id, Name = c.Name, OwnerId = c.OwnerId, CreatedUtc = c.CreatedUtc };

    // ---------------------------------------------------------------------------------------------
    // The one intent envelope.
    // ---------------------------------------------------------------------------------------------

    private void HandleCrewAction(PlayerSession session, CrewActionIntent intent)
    {
        switch (intent.Kind)
        {
            case "create": CreateCrew(session, intent.Name); break;
            case "invite": InviteToCrew(session, intent.TargetPlayerId); break;
            case "accept": RespondCrewInvite(session, intent.Name, accept: true); break;
            case "decline": RespondCrewInvite(session, intent.Name, accept: false); break;
            case "leave": LeaveCrew(session); break;
            case "kick": KickFromCrew(session, intent.TargetPlayerId); break;
            case "rename": RenameCrew(session, intent.Name); break;
            case "disband": DisbandCrew(session); break;
            case "list": SendCrewList(session); break;
            default: break; // unknown verb from a newer client — ignore
        }
    }

    private void CreateCrew(PlayerSession session, string rawName)
    {
        string me = session.State.PlayerId;
        if (CrewOf(me) is not null)
        {
            Reject(session, "crew", "@srv.crew.already_in");
            return;
        }

        string sanitized = SanitizeCrewName(rawName);
        if (sanitized.Length == 0)
        {
            Reject(session, "crew", "@srv.crew.name_needed");
            return;
        }

        if (ScreenPlayerName(session, sanitized, "crew") is not { } name)
        {
            return; // refused by the content screen (#1221) — the player has been told
        }

        var crew = new ServerCrew
        {
            Id = "cw" + Guid.NewGuid().ToString("N").Substring(0, 12),
            Name = name,
            OwnerId = me,
            CreatedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
        };
        crew.Members[me] = crew.CreatedUtc;
        _crews[crew.Id] = crew;
        _crewByPlayer[me] = crew.Id;
        _repo.SaveCrew(ToStoredCrew(crew));
        _repo.SaveCrewMember(new StoredCrewMember { CrewId = crew.Id, PlayerId = me, JoinedUtc = crew.CreatedUtc });

        Send(session, new ServerMessage { Text = "@srv.crew.created:" + crew.Name });
        SendCrewList(session);
    }

    private void InviteToCrew(PlayerSession session, string targetId)
    {
        string me = session.State.PlayerId;
        var crew = CrewOf(me);
        if (crew is null || crew.OwnerId != me)
        {
            Reject(session, "crew", crew is null ? "@srv.crew.none" : "@srv.crew.owner_only");
            return;
        }

        if (string.IsNullOrEmpty(targetId) || targetId == me)
        {
            Reject(session, "crew", "@srv.crew.bad_target");
            return;
        }

        if (crew.Members.Count >= CrewMaxMembers)
        {
            Reject(session, "crew", "@srv.crew.full");
            return;
        }

        // Online only, by design: an invite is a face-to-face gesture, not a mailbox item — and the target
        // must be able to say no right away.
        var target = FindSessionByPlayerId(targetId);
        if (target is null)
        {
            Reject(session, "crew", "@srv.crew.offline");
            return;
        }

        if (CrewOf(targetId) is not null)
        {
            Reject(session, "crew", "@srv.crew.target_taken");
            return;
        }

        if (!_pendingCrewInvites.Add((crew.Id, targetId)))
        {
            Reject(session, "crew", "@srv.crew.pending");
            return;
        }

        Send(target, new CrewInviteNotice { CrewId = crew.Id, CrewName = crew.Name, FromName = session.State.Name });
        SendCrewList(target);
        Send(session, new ServerMessage { Text = "@srv.crew.invited:" + target.State.Name });
    }

    private void RespondCrewInvite(PlayerSession session, string crewId, bool accept)
    {
        string me = session.State.PlayerId;
        if (!_pendingCrewInvites.Remove((crewId, me)))
        {
            return; // no matching invite (withdrawn / owner left / already answered)
        }

        if (!_crews.TryGetValue(crewId, out var crew))
        {
            return; // crew disbanded while the invite was open
        }

        if (!accept)
        {
            if (FindSessionByPlayerId(crew.OwnerId) is { } owner)
            {
                Send(owner, new ServerMessage { Text = "@srv.crew.declined:" + session.State.Name });
            }

            SendCrewList(session);
            return;
        }

        if (CrewOf(me) is not null)
        {
            Reject(session, "crew", "@srv.crew.already_in");
            return;
        }

        if (crew.Members.Count >= CrewMaxMembers)
        {
            Reject(session, "crew", "@srv.crew.full");
            return;
        }

        string joined = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
        crew.Members[me] = joined;
        _crewByPlayer[me] = crew.Id;
        _repo.SaveCrewMember(new StoredCrewMember { CrewId = crew.Id, PlayerId = me, JoinedUtc = joined });

        NotifyCrew(crew, "@srv.crew.joined:" + session.State.Name);
        RefreshCrewAndAllianceRosters(crew);
    }

    private void LeaveCrew(PlayerSession session)
    {
        string me = session.State.PlayerId;
        var crew = CrewOf(me);
        if (crew is null)
        {
            Reject(session, "crew", "@srv.crew.none");
            return;
        }

        RemoveMember(crew, me);

        if (crew.Members.Count == 0)
        {
            DropCrew(crew);
            Send(session, new ServerMessage { Text = "@srv.crew.disbanded:" + crew.Name });
            SendCrewList(session);
            return;
        }

        if (crew.OwnerId == me)
        {
            // The owner walked away: the crew belongs to whoever has been aboard longest.
            crew.OwnerId = OldestMember(crew);
            _repo.SaveCrew(ToStoredCrew(crew));
            NotifyCrew(crew, "@srv.crew.new_owner:" + NameOf(crew.OwnerId));
        }

        NotifyCrew(crew, "@srv.crew.left:" + session.State.Name);
        SendCrewList(session);
        RefreshAllianceRoster(me);
        RefreshCrewAndAllianceRosters(crew);
    }

    private void KickFromCrew(PlayerSession session, string targetId)
    {
        string me = session.State.PlayerId;
        var crew = CrewOf(me);
        if (crew is null || crew.OwnerId != me)
        {
            Reject(session, "crew", crew is null ? "@srv.crew.none" : "@srv.crew.owner_only");
            return;
        }

        if (targetId == me || !crew.Members.ContainsKey(targetId))
        {
            Reject(session, "crew", "@srv.crew.bad_target");
            return;
        }

        RemoveMember(crew, targetId);
        if (FindSessionByPlayerId(targetId) is { } target)
        {
            Send(target, new ServerMessage { Text = "@srv.crew.kicked:" + crew.Name });
            SendCrewList(target);
            RefreshAllianceRoster(targetId);
        }

        NotifyCrew(crew, "@srv.crew.left:" + NameOf(targetId));
        RefreshCrewAndAllianceRosters(crew);
    }

    private void RenameCrew(PlayerSession session, string rawName)
    {
        string me = session.State.PlayerId;
        var crew = CrewOf(me);
        if (crew is null || crew.OwnerId != me)
        {
            Reject(session, "crew", crew is null ? "@srv.crew.none" : "@srv.crew.owner_only");
            return;
        }

        string sanitized = SanitizeCrewName(rawName);
        if (sanitized.Length == 0)
        {
            Reject(session, "crew", "@srv.crew.name_needed");
            return;
        }

        if (ScreenPlayerName(session, sanitized, "crew") is not { } name)
        {
            return; // refused by the content screen (#1221)
        }

        crew.Name = name;
        _repo.SaveCrew(ToStoredCrew(crew));
        NotifyCrew(crew, "@srv.crew.renamed:" + name);
        RefreshCrewAndAllianceRosters(crew);
    }

    private void DisbandCrew(PlayerSession session)
    {
        string me = session.State.PlayerId;
        var crew = CrewOf(me);
        if (crew is null || crew.OwnerId != me)
        {
            Reject(session, "crew", crew is null ? "@srv.crew.none" : "@srv.crew.owner_only");
            return;
        }

        var members = crew.Members.Keys.ToList();
        foreach (var m in members)
        {
            RemoveMember(crew, m);
        }

        DropCrew(crew);
        foreach (var m in members)
        {
            if (FindSessionByPlayerId(m) is { } s)
            {
                Send(s, new ServerMessage { Text = "@srv.crew.disbanded:" + crew.Name });
                SendCrewList(s);
                RefreshAllianceRoster(m);
            }
        }
    }

    private void RemoveMember(ServerCrew crew, string playerId)
    {
        crew.Members.Remove(playerId);
        _crewByPlayer.Remove(playerId);
        _repo.DeleteCrewMember(crew.Id, playerId);
    }

    private void DropCrew(ServerCrew crew)
    {
        _crews.Remove(crew.Id);
        _repo.DeleteCrew(crew.Id);
        var stale = _pendingCrewInvites.Where(p => p.CrewId == crew.Id).ToList();
        foreach (var p in stale)
        {
            _pendingCrewInvites.Remove(p);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Roster + lifecycle plumbing.
    // ---------------------------------------------------------------------------------------------

    /// <summary>Builds and sends one player's crew state (their crew, if any, plus invites addressed to them).</summary>
    private void SendCrewList(PlayerSession session)
    {
        string me = session.State.PlayerId;
        var crew = CrewOf(me);

        var invites = _pendingCrewInvites
            .Where(p => p.To == me && _crews.ContainsKey(p.CrewId))
            .Select(p =>
            {
                var c = _crews[p.CrewId];
                return new NetCrewInvite { CrewId = c.Id, CrewName = c.Name, FromName = NameOf(c.OwnerId) };
            })
            .ToArray();

        if (crew is null)
        {
            Send(session, new CrewList { Invites = invites });
            return;
        }

        var members = crew.Members
            .OrderBy(kv => kv.Value, StringComparer.Ordinal).ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => new NetCrewMember
            {
                PlayerId = kv.Key,
                Name = NameOf(kv.Key),
                Online = FindSessionByPlayerId(kv.Key) is not null,
                IsOwner = kv.Key == crew.OwnerId,
            })
            .ToArray();

        Send(session, new CrewList { CrewId = crew.Id, Name = crew.Name, OwnerId = crew.OwnerId, Members = members, Invites = invites });
    }

    private void NotifyCrew(ServerCrew crew, string text)
    {
        foreach (var m in crew.Members.Keys)
        {
            if (FindSessionByPlayerId(m) is { } s)
            {
                Send(s, new ServerMessage { Text = text });
            }
        }
    }

    /// <summary>Re-sends every online member's crew view AND alliance roster (crew members appear there as
    /// crew-flagged allies, so a membership change moves both lists).</summary>
    private void RefreshCrewAndAllianceRosters(ServerCrew crew)
    {
        foreach (var m in crew.Members.Keys)
        {
            if (FindSessionByPlayerId(m) is { } s)
            {
                SendCrewList(s);
                RefreshAllianceRoster(m);
            }
        }
    }

    /// <summary>Drops the disconnecting player's pending crew invites and lets the remaining online members see
    /// the Online flag change. Mirrors <see cref="ClearAlliancePending"/>; membership itself persists.</summary>
    private void ClearCrewPending(string playerId)
    {
        var stale = _pendingCrewInvites.Where(p => p.To == playerId).ToList();
        foreach (var p in stale)
        {
            _pendingCrewInvites.Remove(p);
        }

        if (CrewOf(playerId) is { } crew)
        {
            foreach (var m in crew.Members.Keys)
            {
                if (m != playerId && FindSessionByPlayerId(m) is { } s)
                {
                    SendCrewList(s);
                }
            }
        }
    }

    /// <summary>Trims a player-typed crew name to one short line (mirrors the beacon label rule).</summary>
    private static string SanitizeCrewName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var trimmed = StripControlChars(raw).Trim();
        return trimmed.Length > CrewNameMaxLength ? trimmed.Substring(0, CrewNameMaxLength) : trimmed;
    }

    // ---------------------------------------------------------------------------------------------
    // Test hooks (all mirror the intent envelope).
    // ---------------------------------------------------------------------------------------------

    /// <summary>Test/util: run a crew verb as a player. Returns the player's crew id afterwards ("" = none).</summary>
    public string CrewActionForTest(string playerId, string kind, string name = "", string target = "")
    {
        if (FindSessionByPlayerId(playerId) is { } s)
        {
            HandleCrewAction(s, new CrewActionIntent { Kind = kind, Name = name, TargetPlayerId = target });
        }

        return _crewByPlayer.GetValueOrDefault(playerId, string.Empty);
    }
}
