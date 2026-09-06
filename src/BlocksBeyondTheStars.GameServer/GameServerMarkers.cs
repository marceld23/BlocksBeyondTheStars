// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Named map markers + ping (#1217). Markers are per player per world (cap
/// <see cref="MarkerMaxPerWorld"/>), persisted on the owner's player blob, and — when flagged shared — shown
/// to players the owner is allied or crewed with while both stand on the same body. Shared markers stay visible
/// while their owner is OFFLINE (#1293): the family meeting point must not vanish when the kid logs off. They
/// are served from a server-side RAM index (<see cref="_sharedMarkers"/>, body → owner → markers) seeded once
/// from the persisted player store and then kept current on every marker edit, join and disconnect — so no read
/// hits the database on the hot path. The view is re-sent whenever the audience changes: alliance formed or
/// dissolved, crew joined/left/kicked/disbanded, and when a player leaves a world (their pings go with them).
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

    /// <summary>Shared markers of EVERY known player, online or not: body id → owner id → that owner's shared
    /// markers on the body (#1293). Seeded lazily from the player store (<see cref="EnsureSharedMarkerIndex"/>),
    /// then maintained by <see cref="IndexSharedMarkers"/> on every marker edit, join and disconnect.</summary>
    private readonly Dictionary<string, Dictionary<string, List<PlayerMarker>>> _sharedMarkers = new();
    private bool _sharedMarkersSeeded;

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
        IndexSharedMarkers(p);
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
        IndexSharedMarkers(p);
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

    /// <summary>A ping the SERVER raises for a player (#1668): the spot it chose for something of theirs — the
    /// vehicle it parked beside the ship when the inventory was full. Same lifetime and audience as a player's
    /// own ping, no rate limit (it is not a key press).</summary>
    private void RaisePingAt(PlayerSession session, Vector3f at)
    {
        string loc = session.CurrentLocationId;
        if (string.IsNullOrEmpty(loc))
        {
            return;
        }

        _pings.Add(new ServerPing
        {
            OwnerId = session.State.PlayerId,
            LocationId = loc,
            X = at.X,
            Y = at.Y,
            Z = at.Z,
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

    /// <summary>Seeds <see cref="_sharedMarkers"/> from the persisted player store, once per server lifetime.
    /// Runs on the first marker read (i.e. the first join), so a big fleet world pays the cost exactly once.
    /// Online players are indexed from their live state, which wins over a stored blob. A corrupted blob is
    /// skipped, not fatal — markers are not worth refusing a join over.</summary>
    private void EnsureSharedMarkerIndex()
    {
        if (_sharedMarkersSeeded)
        {
            return;
        }

        _sharedMarkersSeeded = true;
        int offlineOwners = 0;
        foreach (var id in _repo.ListPlayerIds())
        {
            if (FindSessionByPlayerId(id) is not null)
            {
                continue; // indexed from the live state below
            }

            try
            {
                if (_repo.LoadPlayer(id) is { } stored && stored.Markers.Any(m => m.Shared))
                {
                    IndexSharedMarkers(stored);
                    offlineOwners++;
                }
            }
            catch (InvalidDataException ex)
            {
                _log.Warn($"Marker index: skipped player '{id}' (unreadable save: {ex.Message}).");
            }
        }

        foreach (var s in _sessions.Values)
        {
            if (s.Joined)
            {
                IndexSharedMarkers(s.State);
            }
        }

        if (offlineOwners > 0)
        {
            _log.Info($"Marker index: {offlineOwners} offline player(s) with shared markers.");
        }
    }

    /// <summary>(Re)indexes one player's shared markers: drops every entry of theirs, then adds the current
    /// shared set per body. Called on marker set/remove, join and disconnect — never per tick.</summary>
    private void IndexSharedMarkers(PlayerState p)
    {
        string owner = p.PlayerId;
        foreach (var kv in _sharedMarkers.ToList())
        {
            kv.Value.Remove(owner);
            if (kv.Value.Count == 0)
            {
                _sharedMarkers.Remove(kv.Key);
            }
        }

        foreach (var m in p.Markers)
        {
            if (!m.Shared || string.IsNullOrEmpty(m.LocationId))
            {
                continue;
            }

            if (!_sharedMarkers.TryGetValue(m.LocationId, out var byOwner))
            {
                _sharedMarkers[m.LocationId] = byOwner = new Dictionary<string, List<PlayerMarker>>();
            }

            if (!byOwner.TryGetValue(owner, out var list))
            {
                byOwner[owner] = list = new List<PlayerMarker>();
            }

            list.Add(m);
        }
    }

    /// <summary>Everything ONE player should see on their current world: their own markers, the shared markers
    /// of players they are allied or crewed with on the same body (online or not), and the live pings of that
    /// circle.</summary>
    private MarkerList MarkersFor(PlayerSession viewer)
    {
        string me = viewer.State.PlayerId;
        string loc = viewer.CurrentLocationId;
        var result = new List<NetMarker>();
        if (string.IsNullOrEmpty(loc))
        {
            return new MarkerList();
        }

        EnsureSharedMarkerIndex();

        foreach (var m in viewer.State.Markers)
        {
            if (m.LocationId == loc)
            {
                result.Add(ToNetMarker(me, m));
            }
        }

        if (_sharedMarkers.TryGetValue(loc, out var byOwner))
        {
            foreach (var kv in byOwner)
            {
                string owner = kv.Key;
                if (owner == me || !AreAllied(me, owner))
                {
                    continue;
                }

                foreach (var m in kv.Value)
                {
                    result.Add(ToNetMarker(owner, m));
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

    private static NetMarker ToNetMarker(string owner, PlayerMarker m) => new()
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
    };

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

    /// <summary>Re-sends the marker view to each of the given players that is online — the audience of a
    /// shared marker just changed (alliance formed/dissolved, crew membership moved, #1293). Called from the
    /// alliance and crew files; duplicates in the list are harmless.</summary>
    private void RefreshMarkersFor(IEnumerable<string> playerIds)
    {
        foreach (var id in playerIds.Distinct())
        {
            if (FindSessionByPlayerId(id) is { } s && s.Joined)
            {
                SendMarkers(s);
            }
        }
    }

    /// <summary>A player joined: their loaded state is the truth for their shared markers (the seed may have
    /// read an older blob, or none at all for a brand-new player). No-op before the first seed — the seed
    /// itself indexes every joined session.</summary>
    private void OnMarkerOwnerJoined(PlayerSession session)
    {
        if (_sharedMarkersSeeded)
        {
            IndexSharedMarkers(session.State);
        }
    }

    /// <summary>A player left a world (travel, respawn elsewhere, or disconnect): their pings are a shout that
    /// stops with them, so drop them and re-send the view to everyone still on that world (#1293). Their SHARED
    /// markers stay — the index carries them while the owner is away; on a disconnect it is refreshed from the
    /// state that was just saved. Call AFTER the session has moved (or been removed) so the leaver is not
    /// counted among the remaining players.</summary>
    private void OnMarkerOwnerLeftWorld(PlayerSession session, string oldLocationId, bool disconnected)
    {
        string me = session.State.PlayerId;
        if (disconnected)
        {
            _nextPingAt.Remove(me);
            if (_sharedMarkersSeeded)
            {
                IndexSharedMarkers(session.State);
            }
        }

        int dropped = _pings.RemoveAll(p => p.OwnerId == me);
        if (string.IsNullOrEmpty(oldLocationId) || (dropped == 0 && !disconnected))
        {
            return; // a plain world switch with no live ping changes nothing for the players left behind
        }

        foreach (var s in _sessions.Values)
        {
            if (s.Joined && s != session && s.CurrentLocationId == oldLocationId)
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
