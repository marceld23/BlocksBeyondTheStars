// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The admin view's Players tab (#3505): who plays and where — every joined player on every body, once a second, only to
/// admins whose panel is open — and the jump to a player or a place. Observers are listed (flagged) only to an admin who may
/// see them (<see cref="SeesObservers"/>). Following a player is client-side: the observer keeps itself behind the followed
/// avatar, whose presence it receives on the same body.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Seconds between two roster sends (and two idle-clock samples).</summary>
    private const double AdminRosterInterval = 1.0;

    /// <summary>How far a player must move between two samples to count as active (blocks).</summary>
    private const float AdminIdleMoveThreshold = 0.25f;

    private double _adminRosterTimer;

    /// <summary>Real-time seconds for the admin view (the chat clock: it keeps running while a paused world holds the
    /// simulation clock, and tests pin it with <see cref="SetChatClockForTest"/>).</summary>
    private double AdminClockSeconds => ChatClockMs / 1000.0;

    /// <summary>Once a second: samples who moved (the roster's idle column) and sends the roster to every open panel.</summary>
    private void TickAdminView(double deltaSeconds)
    {
        _adminRosterTimer += deltaSeconds;
        if (_adminRosterTimer < AdminRosterInterval)
        {
            return;
        }

        _adminRosterTimer = 0;
        double now = AdminClockSeconds;
        foreach (var s in _sessions.Values)
        {
            if (!s.Joined)
            {
                continue;
            }

            var pos = s.State.Position;
            var seen = s.Admin.IdleSeenAt;
            float dx = pos.X - seen.X, dy = pos.Y - seen.Y, dz = pos.Z - seen.Z;
            if (s.Admin.LastMovedAt < 0 || (dx * dx) + (dy * dy) + (dz * dz) > AdminIdleMoveThreshold * AdminIdleMoveThreshold)
            {
                s.Admin.LastMovedAt = now;
                s.Admin.IdleSeenAt = pos;
            }
        }

        foreach (var viewer in _sessions.Values)
        {
            if (viewer.Joined && viewer.Admin.PanelOpen && MayUseAdminView(viewer))
            {
                Send(viewer, BuildAdminRoster(viewer));
            }
        }
    }

    /// <summary>The roster as <paramref name="viewer"/> may see it, sorted by body, then name — for the visible variant of an
    /// official hosted world's owner the body only, no position (<see cref="SeesPositions"/>).</summary>
    private AdminRoster BuildAdminRoster(PlayerSession viewer)
    {
        bool seesObservers = SeesObservers(viewer);
        bool withPosition = SeesPositions(viewer);
        double now = AdminClockSeconds;
        var rows = new List<NetAdminPlayer>();
        foreach (var s in _sessions.Values)
        {
            if (s.Joined && (!s.Spectating || seesObservers || ReferenceEquals(s, viewer)))
            {
                rows.Add(AdminRosterRow(s, now, withPosition));
            }
        }

        return new AdminRoster
        {
            Players = rows.OrderBy(r => r.LocationName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToArray(),
        };
    }

    private NetAdminPlayer AdminRosterRow(PlayerSession s, double now, bool withPosition)
    {
        var p = s.State;
        bool inDuel = _duelPartner.TryGetValue(p.PlayerId, out var partner);
        return new NetAdminPlayer
        {
            PlayerId = p.PlayerId,
            Name = p.Name ?? string.Empty,
            Role = p.Role.ToString(),
            FleetAdmin = s.IsFleetAdmin,
            LocationId = s.CurrentLocationId,
            LocationName = ResolveLocationBody(s.CurrentLocationId)?.Name ?? s.CurrentLocationId,
            HasPosition = withPosition,
            X = withPosition ? p.Position.X : 0f,
            Y = withPosition ? p.Position.Y : 0f,
            Z = withPosition ? p.Position.Z : 0f,
            Health = p.Health,
            State = AdminPlayerState(s),
            InDuel = inDuel,
            DuelWith = inDuel ? FindSessionByPlayerId(partner!)?.State.Name ?? partner! : string.Empty,
            Protected = PvpGuarded(s),
            Silenced = ChatMutedUntil(s) > _uptime,
            IdleSeconds = s.Admin.LastMovedAt < 0 ? 0 : (int)Math.Max(0, now - s.Admin.LastMovedAt),
        };
    }

    /// <summary>What the player is doing, one of <see cref="AdminPlayerStates"/>.</summary>
    private string AdminPlayerState(PlayerSession s)
    {
        var p = s.State;
        if (s.Spectating)
        {
            return AdminPlayerStates.Observer;
        }

        if (p.Health <= 0f || s.Respawn.ChoicePending)
        {
            return AdminPlayerStates.Dead;
        }

        if (InSpace(p.PlayerId))
        {
            return AdminPlayerStates.InSpace;
        }

        if (InStation(p.PlayerId))
        {
            return AdminPlayerStates.Station;
        }

        if (p.AboardShip || _inShipInterior.ContainsKey(p.PlayerId))
        {
            return AdminPlayerStates.Aboard;
        }

        if (p.InSpeeder.Length > 0)
        {
            return AdminPlayerStates.Vehicle;
        }

        if (p.InTrain.Length > 0)
        {
            return AdminPlayerStates.Train;
        }

        return p.InEva ? AdminPlayerStates.Eva : p.Fly ? AdminPlayerStates.Flying : AdminPlayerStates.OnFoot;
    }

    // ---------------- Jump (#3505) ----------------

    /// <summary>How far behind and above the player a jump arrives (blocks).</summary>
    private const float AdminJumpBehind = 4f;

    private const float AdminJumpAbove = 4f;

    /// <summary>An invisible admin jumps to a player — a free spot behind and above them; another body is travelled to
    /// silently with the admin bypass — or to a place (a log row, a report).</summary>
    private void HandleAdminJump(PlayerSession session, AdminJumpIntent intent)
    {
        if (session.Admin.Mode != AdminModes.Invisible || !session.Spectating || !MayUseAdminView(session))
        {
            Reject(session, "admin", "@srv.admin.jump_invisible_only");
            return;
        }

        if (string.IsNullOrEmpty(intent.PlayerId))
        {
            if (!intent.HasPosition || !float.IsFinite(intent.X) || !float.IsFinite(intent.Y) || !float.IsFinite(intent.Z))
            {
                Reject(session, "admin", "@srv.obs.unknown_destination");
                return;
            }

            string body = string.IsNullOrEmpty(intent.LocationId) ? session.CurrentLocationId : intent.LocationId;
            _observerSystem.GotoPosition(session, body, new Vector3f(intent.X, intent.Y + 2f, intent.Z), body);
            return;
        }

        var target = FindSessionByPlayerId(intent.PlayerId);
        if (target is null || !target.Joined || ReferenceEquals(target, session) || (target.Spectating && !SeesObservers(session)))
        {
            Reject(session, "admin", "@srv.obs.unknown_player");
            return;
        }

        if (InSpace(target.State.PlayerId))
        {
            Reject(session, "admin", "@srv.admin.jump_target_in_space:" + (target.State.Name ?? "?"));
            return;
        }

        // Another body is travelled to first (silently), so the free spot is looked up in the world the admin arrives in.
        string name = target.State.Name ?? "?";
        if (!_observerSystem.TravelForGoto(session, target.CurrentLocationId))
        {
            return;
        }

        _observerSystem.GotoPosition(session, target.CurrentLocationId, AdminJumpSpot(target), name);
    }

    /// <summary>A spot about four blocks behind and four above <paramref name="target"/> (by their facing), moved up to the
    /// first free two-block-high cell (an observer flies without collision, but leaving admin mode must never start inside a
    /// wall, and the view from inside a hill shows nothing).</summary>
    private Vector3f AdminJumpSpot(PlayerSession target)
    {
        var p = target.State;
        double yaw = p.Yaw * Math.PI / 180.0; // 0 = +Z forward, 90 = +X (the client's convention)
        float x = p.Position.X - (float)(Math.Sin(yaw) * AdminJumpBehind);
        float z = p.Position.Z - (float)(Math.Cos(yaw) * AdminJumpBehind);
        float y = p.Position.Y + AdminJumpAbove;
        if (!SetActiveWorld(target.CurrentLocationId))
        {
            return new Vector3f(x, y, z);
        }

        int bx = (int)Math.Floor(x), bz = (int)Math.Floor(z);
        return FreeStandingY(bx, (int)Math.Floor(y), bz, 12) is { } by ? new Vector3f(bx + 0.5f, by, bz + 0.5f) : new Vector3f(x, y, z);
    }

    /// <summary>The lowest cell from <paramref name="by"/> up (at most <paramref name="range"/> cells) with two free cells to
    /// stand in, in the ACTIVE world; null when there is none.</summary>
    private int? FreeStandingY(int bx, int by, int bz, int range)
    {
        for (int y = by; y < by + range; y++)
        {
            if (!IsBodyBlockingCell(bx, y, bz) && !IsBodyBlockingCell(bx, y + 1, bz))
            {
                return y;
            }
        }

        return null;
    }

    // ---------------- Test seams ----------------

    /// <summary>Test seam: the roster as this admin would receive it now.</summary>
    internal AdminRoster AdminRosterForTest(PlayerSession viewer) => BuildAdminRoster(viewer);

    /// <summary>Test seam: one pass of the 1 Hz admin view tick.</summary>
    internal void TickAdminViewForTest() => TickAdminView(AdminRosterInterval);

    /// <summary>Test seam: a jump as if the client sent <see cref="AdminJumpIntent"/>.</summary>
    internal void AdminJumpForTest(PlayerSession session, AdminJumpIntent intent) => HandleAdminJump(session, intent);
}
