// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Named map markers + ping (#1217) on the server: the facade of <see cref="MarkerSystem"/> (ADR 0019), which owns the
/// marker verbs, the live pings and their rate limit, and the shared-marker index (#1293). This partial keeps the members
/// sibling partials, the crew host, the dispatch table, the tick and the tests call — same names, same signatures, each
/// one line delegating — and the explicit <see cref="IMarkerHost"/> implementation.
/// </summary>
public sealed partial class GameServer : IMarkerHost
{
    private void HandleMarkerAction(PlayerSession session, MarkerActionIntent intent) => _markerSystem.HandleMarkerAction(session, intent);

    /// <summary>A ping the SERVER raises for a player (#1668): the spot it chose for something of theirs — the
    /// vehicle it parked beside the ship when the inventory was full. Same lifetime and audience as a player's
    /// own ping, no rate limit (it is not a key press).</summary>
    private void RaisePingAt(PlayerSession session, Vector3f at) => _markerSystem.RaisePingAt(session, at);

    /// <summary>Expires pings (registered in the environment tick). Cheap: no work while nothing is live.</summary>
    private void TickMarkerPings(double dt) => _markerSystem.TickMarkerPings(dt);

    /// <summary>Everything ONE player should see on their current world: their own markers, the shared markers
    /// of players they are allied or crewed with on the same body (online or not), and the live pings of that
    /// circle.</summary>
    private MarkerList MarkersFor(PlayerSession viewer) => _markerSystem.MarkersFor(viewer);

    private void SendMarkers(PlayerSession session) => _markerSystem.SendMarkers(session);

    /// <summary>Re-sends the marker view to each of the given players that is online — the audience of a
    /// shared marker just changed (alliance formed/dissolved, crew membership moved, #1293).</summary>
    private void RefreshMarkersFor(IEnumerable<string> playerIds) => _markerSystem.RefreshMarkersFor(playerIds);

    /// <summary>A player joined: their loaded state is the truth for their shared markers.</summary>
    private void OnMarkerOwnerJoined(PlayerSession session) => _markerSystem.OnMarkerOwnerJoined(session);

    /// <summary>A player left a world (travel, respawn elsewhere, or disconnect): their pings go with them (#1293). Call
    /// AFTER the session has moved (or been removed) so the leaver is not counted among the remaining players.</summary>
    private void OnMarkerOwnerLeftWorld(PlayerSession session, string oldLocationId, bool disconnected)
        => _markerSystem.OnMarkerOwnerLeftWorld(session, oldLocationId, disconnected);

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

    // IMarkerHost: each member delegates to the server's own member of the same name (never to the interface member,
    // which would recurse).
    string? IMarkerHost.ScreenPlayerName(PlayerSession session, string clean, string surface) => ScreenPlayerName(session, clean, surface);

    bool IMarkerHost.AreAllied(string a, string b) => AreAllied(a, b);

    void IMarkerHost.AddStoryMarkers(PlayerSession viewer, string loc, List<NetMarker> into) => AddStoryMarkers(viewer, loc, into);

    void IMarkerHost.AddWishMarkers(PlayerSession viewer, string loc, List<NetMarker> into) => AddWishMarkers(viewer, loc, into);

    void IMarkerHost.NoteStoryMarkersSent(PlayerSession s) => NoteStoryMarkersSent(s);

    Dictionary<int, string> IMarkerHost.StoryMarkersSent => _storyMarkersSent;
}
