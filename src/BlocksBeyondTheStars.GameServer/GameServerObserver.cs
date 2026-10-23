// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Fleet-admin observer mode (issue #487) and the admin inspection commands (issue #488) on the server: the facade of
/// <see cref="ObserverSystem"/> (ADR 0019), which owns entering and leaving observer mode and <c>/players</c>,
/// <c>/where</c>, <c>/builds</c> and <c>/goto</c>. This partial keeps the player count, the fleet-admin check and the
/// observer intent filter the join path and the dispatch read, the members the admin command table and sibling partials
/// call — same names, same signatures, each one line delegating — the test seams, and the explicit
/// <see cref="IObserverHost"/> implementation.
/// </summary>
public sealed partial class GameServer : IObserverHost
{
    /// <summary>Players that count against <see cref="Shared.Configuration.ServerConfig.MaxPlayers"/> and that
    /// the control plane reports as "online". Observers are excluded: they are staff, not players, and a full
    /// world is exactly when an operator is most likely to need to look at it.</summary>
    private int JoinedPlayerCount()
    {
        int n = 0;
        foreach (var s in _sessions.Values)
        {
            if (s.Joined && !s.Spectating)
            {
                n++;
            }
        }

        return n;
    }

    /// <summary>ISO-8601 UTC "now", the format <see cref="PlayerState.LastSeenUtc"/> stores.</summary>
    private static string UtcNowIso() => DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);

    /// <summary>Whether <paramref name="name"/> is a configured fleet-admin name. Case-insensitive (#495),
    /// matching how the hosted join token compares names — a silent case mismatch would deny the elevation
    /// with no error anywhere to see.</summary>
    private bool IsFleetAdminName(string name)
        => _config.FleetAdminPlayers.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Which client intents an observer may still act on. Movement and pure read/query traffic pass;
    /// mining passes because removing an offensive build is the one in-world moderation lever we have (bans are
    /// account-level and world stop/delete is a sledgehammer). Everything else is dropped.</summary>
    private static bool SpectatorMayHandle(object message) => message switch
    {
        MoveIntent or SelectHotbarIntent or ChatIntent or AdminCommandIntent or BumpReport => true,
        MineBlockIntent => true, // moderation: remove an offensive build (logged via CheatLog)
        RequestStarMap or RequestMissions or RequestCompanionsIntent or RequestLandingPadsIntent => true,
        FarTerrainTileRequest => true, // read-only: the observer's far view (#1821)
        SetAppearanceIntent or SetFaceIntent or SetBodyPaintIntent or SetToolLookIntent => true, // cosmetic, and never broadcast while spectating anyway
        _ => false,
    };

    // ---------------- Test seams ----------------

    /// <summary>Runs one admin command for a session without a socket, so tests can exercise the observer and
    /// inspection commands through the real handler (role gate included).</summary>
    public void HandleForTest(PlayerSession session, AdminCommandIntent command)
        => HandleAdminCommand(session, command);

    /// <summary>Exposes the observer intent filter so a test can pin down exactly what stays possible while
    /// observing — the read-only boundary is a security property, not an implementation detail.</summary>
    public static bool SpectatorMayHandleForTest(object message) => SpectatorMayHandle(message);

    /// <summary>Persists everything now (tests that assert on offline players need the save written).</summary>
    public void SaveAllForTest() => SaveAll();

    // ---------------- Observer mode and inspection commands (ObserverSystem) ----------------

    private void HandleSpectateCommand(PlayerSession session, string? arg) => _observerSystem.HandleSpectateCommand(session, arg);

    private void AdminListPlayers(PlayerSession session) => _observerSystem.AdminListPlayers(session);

    private void AdminWhere(PlayerSession session, string? name) => _observerSystem.AdminWhere(session, name);

    private List<ObserverSystem.BuildEntry> AllBuilds() => _observerSystem.AllBuilds();

    private void AdminListBuilds(PlayerSession session, string? ownerFilter) => _observerSystem.AdminListBuilds(session, ownerFilter);

    private void AdminGoto(PlayerSession session, string? argument) => _observerSystem.AdminGoto(session, argument);

    // IObserverHost: each member delegates to the server's own member of the same name (never to the interface member,
    // which would recurse).
    bool IObserverHost.SetActiveWorld(string locationId) => SetActiveWorld(locationId);

    void IObserverHost.SetCurrent(PlayerSession session) => SetCurrent(session);

    void IObserverHost.RemoveLandedShip(PlayerSession session) => RemoveLandedShip(session);

    bool IObserverHost.ClaimPadOrReject(PlayerSession session, string bodyId, int padIndex) => ClaimPadOrReject(session, bodyId, padIndex);

    void IObserverHost.PlaceLandedShip() => PlaceLandedShip();

    void IObserverHost.BroadcastLandingPads(PlayerSession? always) => BroadcastLandingPads(always);

    void IObserverHost.BroadcastFace(PlayerSession subject) => BroadcastFace(subject);

    string IObserverHost.Localize(string localeCode, string key) => Localize(localeCode, key);

    void IObserverHost.CheatLog(PlayerState admin, string message) => CheatLog(admin, message);

    string? IObserverHost.FinalBeatKey => FinalBeatKey;

    void IObserverHost.CatchUpStoryLines(PlayerSession session) => CatchUpStoryLines(session);

    bool IObserverHost.StoryTrackStarted(PlayerSession s) => StoryTrackStarted(s);

    void IObserverHost.DeliverResolution(PlayerSession session, bool achievement, bool live) => DeliverResolution(session, achievement, live);

    bool IObserverHost.TryStartStoryTrack(PlayerSession s) => TryStartStoryTrack(s);

    void IObserverHost.HandleTravel(PlayerSession session, TravelIntent intent, bool quickTravel, bool adminBypass, bool allowCurrentBody,
        bool adminTeleport) => HandleTravel(session, intent, quickTravel, adminBypass, allowCurrentBody, adminTeleport);

    void IObserverHost.SnapPlayerTo(PlayerSession session, Vector3f target, string reason) => SnapPlayerTo(session, target, reason);

    Galaxy IObserverHost.Galaxy => _galaxy;
}
