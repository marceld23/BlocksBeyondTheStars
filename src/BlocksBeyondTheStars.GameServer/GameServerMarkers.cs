// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.State;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Named map markers + ping (#1217). Markers are per player per world (cap
/// <see cref="MarkerMaxPerWorld"/>), persisted on the owner's player blob, and — when flagged shared — shown
/// to players the owner is allied or crewed with while both stand on the same body. Because shared markers are
/// read straight off the owner's live <c>PlayerState</c>, sharing needs the owner ONLINE — which matches what
/// the feature is for ("come to me, the copper is here"), keeps every read out of the database, and means an
/// offline player's map presence disappears with them.
///
/// <para>Pings are the transient cousin: a "look here" pulse at the crosshair with a
/// <see cref="PingTtlSeconds"/>-second lifetime and a per-player rate limit, visible to the same audience,
/// never persisted. Labels go through the same sanitize + content screen as beacon labels.</para>
/// </summary>
public sealed partial class GameServer
{
    private const int MarkerMaxPerWorld = 8;
    private const int MarkerIconCount = 8;   // flag, home, ore, danger, water, star, heart, question
    private const int MarkerColorCount = 6;  // the shared marker palette (star-map colour marks, #613)
    private const double PingTtlSeconds = 30;
    private const double PingRateSeconds = 5;

    /// <summary>A live ping (RAM only — a ping is a shout, not a record).</summary>
    internal sealed class ServerPing
    {
        public string OwnerId = string.Empty;
        public string LocationId = string.Empty;
        public float X;
        public float Y;
        public float Z;
        public double ExpiresAt;
    }

    private readonly List<ServerPing> _pings = new();
    private readonly Dictionary<string, double> _nextPingAt = new();

    // ---------------------------------------------------------------------------------------------
    // The one intent envelope.
    // ---------------------------------------------------------------------------------------------

    private void HandleMarkerAction(PlayerSession session, MarkerActionIntent intent)
    {
        switch (intent.Kind)
        {
            case "set": SetMarker(session, intent); break;
            case "remove": RemoveMarker(session, intent.Id); break;
            case "ping": RaisePing(session, intent); break;
            case "list": SendMarkers(session); break;
            default: break; // unknown verb from a newer client — ignore
        }
    }

    private void SetMarker(PlayerSession session, MarkerActionIntent intent)
    {
        if (!float.IsFinite(intent.X) || !float.IsFinite(intent.Y) || !float.IsFinite(intent.Z))
        {
            return;
        }

        string loc = session.CurrentLocationId;
        if (string.IsNullOrEmpty(loc) || InSpace(session.State.PlayerId))
        {
            Reject(session, "marker", "@srv.marker.surface_only");
            return;
        }

        // The beacon label rules verbatim: strip + clamp, then the #1221 content screen. A refused label
        // refuses the marker (nothing exists yet — unlike a beacon, whose block is already placed).
        if (ScreenPlayerName(session, SanitizeBeaconLabel(intent.Label), "marker") is not { } label)
        {
            return;
        }

        var p = session.State;
        var existing = string.IsNullOrEmpty(intent.Id) ? null : p.Markers.FirstOrDefault(m => m.Id == intent.Id);
        if (existing is null)
        {
            if (p.Markers.Count(m => m.LocationId == loc) >= MarkerMaxPerWorld)
            {
                Reject(session, "marker", "@srv.marker.full");
                return;
            }

            existing = new PlayerMarker
            {
                Id = "mk" + Guid.NewGuid().ToString("N").Substring(0, 12),
                LocationId = loc,
                CreatedUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            };
            p.Markers.Add(existing);
        }
        else if (existing.LocationId != loc)
        {
            Reject(session, "marker", "@srv.marker.other_world"); // edit it where it lives
            return;
        }

        existing.X = intent.X;
        existing.Y = intent.Y;
        existing.Z = intent.Z;
        existing.Label = label;
        existing.Icon = Math.Clamp(intent.Icon, 0, MarkerIconCount - 1);
        existing.Color = Math.Clamp(intent.Color, 0, MarkerColorCount - 1);
        existing.Shared = intent.Shared;
        _repo.SavePlayer(p);
        BroadcastMarkersOn(loc);
    }

    private void RemoveMarker(PlayerSession session, string id)
    {
        var p = session.State;
        var marker = p.Markers.FirstOrDefault(m => m.Id == id);
        if (marker is null)
        {
            return;
        }

        p.Markers.Remove(marker);
        _repo.SavePlayer(p);
        BroadcastMarkersOn(marker.LocationId);
    }

    private void RaisePing(PlayerSession session, MarkerActionIntent intent)
    {
        if (!float.IsFinite(intent.X) || !float.IsFinite(intent.Y) || !float.IsFinite(intent.Z))
        {
            return;
        }

        string me = session.State.PlayerId;
        string loc = session.CurrentLocationId;
        if (string.IsNullOrEmpty(loc) || InSpace(me))
        {
            return;
        }

        if (_uptime < _nextPingAt.GetValueOrDefault(me))
        {
            return; // rate limit — silently, a spammed ping key must not spam rejects either
        }

        _nextPingAt[me] = _uptime + PingRateSeconds;
        _pings.Add(new ServerPing
        {
            OwnerId = me,
            LocationId = loc,
            X = intent.X,
            Y = intent.Y,
            Z = intent.Z,
            ExpiresAt = _uptime + PingTtlSeconds,
        });
        BroadcastMarkersOn(loc);
    }

    /// <summary>Expires pings (registered in the environment tick). Cheap: no work while nothing is live.</summary>
    private void TickMarkerPings(double dt)
    {
        if (_pings.Count == 0)
        {
            return;
        }

        var expired = _pings.Where(p => p.ExpiresAt <= _uptime).ToList();
        if (expired.Count == 0)
        {
            return;
        }

        foreach (var p in expired)
        {
            _pings.Remove(p);
        }

        foreach (var loc in expired.Select(p => p.LocationId).Distinct())
        {
            BroadcastMarkersOn(loc);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Visibility + sync.
    // ---------------------------------------------------------------------------------------------

    /// <summary>Everything ONE player should see on their current world: their own markers, the shared markers
    /// of online players they are allied or crewed with on the same body, and the live pings of that circle.</summary>
    private MarkerList MarkersFor(PlayerSession viewer)
    {
        string me = viewer.State.PlayerId;
        string loc = viewer.CurrentLocationId;
        var result = new List<NetMarker>();
        if (string.IsNullOrEmpty(loc))
        {
            return new MarkerList();
        }

        foreach (var s in _sessions.Values)
        {
            if (!s.Joined || s.CurrentLocationId != loc)
            {
                continue;
            }

            string owner = s.State.PlayerId;
            bool mine = owner == me;
            if (!mine && !AreAllied(me, owner))
            {
                continue;
            }

            foreach (var m in s.State.Markers)
            {
                if (m.LocationId == loc && (mine || m.Shared))
                {
                    result.Add(new NetMarker
                    {
                        Id = m.Id,
                        OwnerId = owner,
                        X = m.X,
                        Y = m.Y,
                        Z = m.Z,
                        Label = m.Label,
                        Icon = m.Icon,
                        Color = m.Color,
                        Shared = m.Shared,
                    });
                }
            }
        }

        foreach (var ping in _pings)
        {
            if (ping.LocationId == loc && ping.ExpiresAt > _uptime
                && (ping.OwnerId == me || AreAllied(me, ping.OwnerId)))
            {
                result.Add(new NetMarker
                {
                    Id = "ping",
                    OwnerId = ping.OwnerId,
                    X = ping.X,
                    Y = ping.Y,
                    Z = ping.Z,
                    Ping = true,
                });
            }
        }

        return new MarkerList { Markers = result.ToArray() };
    }

    private void SendMarkers(PlayerSession session) => Send(session, MarkersFor(session));

    /// <summary>Re-sends the marker view to every joined player on the given body (each gets their own
    /// filtered list — visibility is per viewer, so there is no one broadcast payload).</summary>
    private void BroadcastMarkersOn(string locationId)
    {
        foreach (var s in _sessions.Values)
        {
            if (s.Joined && s.CurrentLocationId == locationId)
            {
                SendMarkers(s);
            }
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Test hooks.
    // ---------------------------------------------------------------------------------------------

    /// <summary>Test/util: create/update a marker as a player (mirrors the intent). Returns the marker count
    /// the player now has on their current world.</summary>
    public int SetMarkerForTest(string playerId, float x, float y, float z, string label = "", int icon = 0, int color = 0, bool shared = false, string id = "")
    {
        if (FindSessionByPlayerId(playerId) is { } s)
        {
            HandleMarkerAction(s, new MarkerActionIntent { Kind = "set", Id = id, X = x, Y = y, Z = z, Label = label, Icon = icon, Color = color, Shared = shared });
            return s.State.Markers.Count(m => m.LocationId == s.CurrentLocationId);
        }

        return 0;
    }

    /// <summary>Test/util: remove an own marker.</summary>
    public void RemoveMarkerForTest(string playerId, string id)
    {
        if (FindSessionByPlayerId(playerId) is { } s)
        {
            HandleMarkerAction(s, new MarkerActionIntent { Kind = "remove", Id = id });
        }
    }

    /// <summary>Test/util: raise a ping as a player.</summary>
    public void PingForTest(string playerId, float x, float y, float z)
    {
        if (FindSessionByPlayerId(playerId) is { } s)
        {
            HandleMarkerAction(s, new MarkerActionIntent { Kind = "ping", X = x, Y = y, Z = z });
        }
    }

    /// <summary>Test/util: what the given player currently sees (id/owner/label/shared/ping tuples).</summary>
    public IReadOnlyList<(string Id, string OwnerId, string Label, bool Shared, bool Ping)> VisibleMarkersForTest(string playerId)
        => FindSessionByPlayerId(playerId) is { } s
            ? MarkersFor(s).Markers.Select(m => (m.Id, m.OwnerId, m.Label, m.Shared, m.Ping)).ToList()
            : new List<(string, string, string, bool, bool)>();

    /// <summary>Test/util: run the ping-expiry tick once.</summary>
    public void TickMarkerPingsForTest() => TickMarkerPings(0);
}
