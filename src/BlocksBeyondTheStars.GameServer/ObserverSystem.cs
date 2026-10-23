// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// What <see cref="ObserverSystem"/> asks of the server beyond <see cref="IServerHost"/> (ADR 0019): the world cursor
/// and the world footprint a joining player leaves (ship, landing pad), the broadcasts that announce or hide an avatar,
/// the story catch-up when observing ends, the cross-body travel and snap of <c>/goto</c>, and the galaxy the inspection
/// commands read. Implemented explicitly by <c>GameServer</c> in <c>GameServerObserver.cs</c>, each member delegating to
/// the server's own member of the same name. A wide host — observer mode touches every place a player's presence is
/// produced (ADR 0019, "wide hosts, shrink later").
/// </summary>
internal interface IObserverHost : IServerHost
{
    // The world cursor and the world footprint
    bool SetActiveWorld(string locationId);

    void SetCurrent(PlayerSession session);

    void RemoveLandedShip(PlayerSession session);

    bool ClaimPadOrReject(PlayerSession session, string bodyId, int padIndex);

    void PlaceLandedShip();

    // Messaging
    void BroadcastLandingPads(PlayerSession? always = null);

    void BroadcastFace(PlayerSession subject);

    string Localize(string localeCode, string key);

    void CheatLog(PlayerState admin, string message);

    // The story (GameServerStory*.cs)
    string? FinalBeatKey { get; }

    void CatchUpStoryLines(PlayerSession session);

    bool StoryTrackStarted(PlayerSession s);

    void DeliverResolution(PlayerSession session, bool achievement, bool live = false);

    bool TryStartStoryTrack(PlayerSession s);

    // Moving the admin
    void HandleTravel(PlayerSession session, TravelIntent intent, bool quickTravel = true, bool adminBypass = false, bool allowCurrentBody = false,
        bool adminTeleport = false);

    void SnapPlayerTo(PlayerSession session, Vector3f target, string reason);

    /// <summary>The galaxy (the server's <c>_galaxy</c>) — replaced when the server starts, so read it at use time.</summary>
    Galaxy Galaxy { get; }
}

/// <summary>
/// Fleet-admin observer mode (issue #487) and the admin inspection commands (issue #488).
///
/// <para>Invisibility here is a <b>server-side mode</b>, not the cosmetic <c>Stealthed</c> flag. Stealth only
/// asks the client not to draw an avatar — an old or modified client can simply ignore it, and it does nothing
/// about the footprint a joining player leaves in a world: a parked ship object, a claimed landing pad, a
/// broadcast descent animation, spawned fauna, a consumed player slot. An observer must leave none of that, so
/// the suppression happens where the data is produced.</para>
/// </summary>
internal sealed class ObserverSystem
{
    private readonly IObserverHost _host;
    private readonly IWorldRepository _repo;
    private readonly ServerConfig _config;

    /// <summary>Stores the references only; built once in <c>GameServer.ConstructSystems</c> (ADR 0019).</summary>
    internal ObserverSystem(IObserverHost host, IWorldRepository repo, ServerConfig config)
    {
        _host = host;
        _repo = repo;
        _config = config;
    }

    // ---------------- Entering / leaving observer mode ----------------

    /// <summary>Turns observer mode on: the world forgets the admin is there. Order matters — the ship object
    /// and the presence handshake must go out while the session is still "visible", otherwise the removal
    /// messages are themselves suppressed and every client keeps a frozen avatar and a ghost ship.</summary>
    private void EnterSpectate(PlayerSession session)
    {
        var p = session.State;

        // Give up the world footprint FIRST, while broadcasts still reach the world.
        if (_host.SetActiveWorld(session.CurrentLocationId))
        {
            _host.SetCurrent(session);
            _host.RemoveLandedShip(session);        // the parked ship object leaves with its (now invisible) owner
        }

        session.AssignedPadIndex = -1;        // release the landing pad: pads are communal and finite
        _host.BroadcastToWorld(new PlayerLeft { PlayerId = p.PlayerId }); // clients drop the avatar they can see
        _host.BroadcastLandingPads(); // the released pad is free again — everyone's map must show it (#1020)
        // Pets and deployed speeders despawn on the next reconcile tick — ReconcileCompanions/ReconcileSpeeders
        // stop counting a spectating session as present.

        session.Spectating = true;
        p.GodMode = true;                     // no damage, no drains, and creature targeting already skips this
        p.Stealthed = true;                   // belt and braces: enemy aggro checks honour it too
        p.Fly = true;
        p.AboardShip = false;                 // no ship to be aboard: cargo crafting/life support don't apply
        p.InSpeeder = string.Empty;

        _host.SendPlayerState(session);
        _host.Send(session, new ServerMessage { Text = "@srv.obs.on" });
        _host.CheatLog(p, "entered observer mode");
    }

    /// <summary>Turns observer mode off and hands the admin back to the world as a normal player: the ship is
    /// parked again and everyone is told about the avatar that just appeared.</summary>
    private void ExitSpectate(PlayerSession session)
    {
        var p = session.State;
        session.Spectating = false;
        p.GodMode = false;
        p.Stealthed = false;
        p.Fly = false;

        if (_host.SetActiveWorld(session.CurrentLocationId))
        {
            _host.SetCurrent(session);
            if (_config.PlaceStarterShip)
            {
                _host.ClaimPadOrReject(session, session.CurrentLocationId, -1); // take a pad again (any free one)
                _host.PlaceLandedShip();
            }

            _host.BroadcastLandingPads(session); // the re-claimed pad is taken again — everyone's map must show it (#1020)
        }

        // Re-announce: presence resumes on the next tick by itself, but the face is out-of-band and would
        // otherwise stay missing until the admin edits it.
        _host.BroadcastFace(session);
        _host.SendPlayerState(session);
        _host.Send(session, new ServerMessage { Text = "@srv.obs.off" });
        // #2478 (E23): whoever had heard the arc out (B12) before the win counts as being there for it — read before the
        // catch-up below hears any missed beat. An observer is not aboard at the win, so the ending (and its achievement)
        // reaches them when observing ends.
        bool heardFinal = _host.FinalBeatKey is { } finalBeat && p.Milestones.Contains(finalBeat);
        _host.CatchUpStoryLines(session); // the beats the story held back while observing (#2460, N1)
        if (_host.StoryTrackStarted(session))
        {
            _host.DeliverResolution(session, achievement: heardFinal);
        }

        _host.TryStartStoryTrack(session); // #2470: the start rule may hold now (observers never start a track)
        _host.CheatLog(p, "left observer mode");
    }

    /// <summary><c>/spectate [on|off]</c> — fleet admins only.</summary>
    internal void HandleSpectateCommand(PlayerSession session, string? arg)
    {
        bool on = string.IsNullOrWhiteSpace(arg)
            ? !session.Spectating
            : arg.Trim().ToLowerInvariant() is "on" or "an" or "1" or "true";

        if (on == session.Spectating)
        {
            _host.Send(session, new ServerMessage { Text = on ? "@srv.obs.already_on" : "@srv.obs.already_off" });
            return;
        }

        if (on)
        {
            EnterSpectate(session);
        }
        else
        {
            ExitSpectate(session);
        }
    }

    // ---------------- Inspection commands (issue #488) ----------------

    /// <summary>Every player this world knows: the live sessions plus every persisted record. Returns the live
    /// session where there is one, so coordinates are current rather than "as of the last save".</summary>
    private List<(PlayerState State, bool Online, bool Observing)> AllKnownPlayers()
    {
        var byId = new Dictionary<string, (PlayerState, bool, bool)>(StringComparer.OrdinalIgnoreCase);

        foreach (var id in _repo.ListPlayerIds())
        {
            if (_repo.LoadPlayer(id) is { } stored)
            {
                byId[id] = (stored, false, false);
            }
        }

        foreach (var s in _host.Sessions.Values)
        {
            if (s.Joined)
            {
                byId[s.State.PlayerId] = (s.State, true, s.Spectating);
            }
        }

        return byId.Values
            .OrderByDescending(e => e.Item2)
            .ThenBy(e => e.Item1.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Human-readable body label ("Kepler-2 (sys0-p1)"), falling back to the raw id.</summary>
    private string BodyLabel(string bodyId)
    {
        if (string.IsNullOrEmpty(bodyId))
        {
            return "-";
        }

        var body = _host.Galaxy?.FindBody(bodyId);
        return body is null ? bodyId : $"{body.Name} ({bodyId})";
    }

    private static string Coords(Vector3f p)
        => string.Format(CultureInfo.InvariantCulture, "{0:0}/{1:0}/{2:0}", p.X, p.Y, p.Z);

    private static string Coords(Vector3i p)
        => string.Format(CultureInfo.InvariantCulture, "{0}/{1}/{2}", p.X, p.Y, p.Z);

    /// <summary>Trims an ISO timestamp to "2026-07-26 14:03" for the console; "-" when never recorded.</summary>
    private static string ShortTime(string iso)
        => DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t)
            ? t.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
            : "-";

    /// <summary><c>/players</c> — who is on this world, and where everyone was last seen.</summary>
    internal void AdminListPlayers(PlayerSession session)
    {
        var all = AllKnownPlayers();
        if (all.Count == 0)
        {
            _host.Send(session, new ServerMessage { Text = "@srv.obs.no_players" });
            return;
        }

        _host.Send(session, new ServerMessage
        {
            Text = _host.Localize(session.Locale, "srv.obs.players_header").Replace("{count}", all.Count.ToString()),
        });

        foreach (var (state, online, observing) in all)
        {
            string status = observing
                ? _host.Localize(session.Locale, "srv.obs.observing")
                : online ? "online" : ShortTime(state.LastSeenUtc); // "online" reads the same in both languages

            _host.Send(session, new ServerMessage
            {
                Text = $"{state.Name} [{state.Role}]"
                       // A per-player mode override (#1121) is worth a glance in the roster; the silent
                       // default (playing the world's mode) stays silent.
                       + (state.ModeOverride == Shared.Configuration.PlayerModeOverride.None
                           ? string.Empty : $" [{state.ModeOverride}]")
                       + $" · {BodyLabel(state.CurrentLocationId)} · "
                       + $"{Coords(state.Position)} · {status}",
            });
        }
    }

    /// <summary><c>/where &lt;name&gt;</c> — one player's body, position and last-seen time. Works for offline
    /// players: the position is persisted per player, so the save already knows where they stopped.</summary>
    internal void AdminWhere(PlayerSession session, string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            _host.Reject(session, "admin", "@srv.obs.usage_where");
            return;
        }

        var match = AllKnownPlayers()
            .FirstOrDefault(e => string.Equals(e.State.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
        if (match.State is null)
        {
            _host.Reject(session, "admin", "@srv.obs.unknown_player");
            return;
        }

        var s = match.State;
        _host.Send(session, new ServerMessage
        {
            Text = $"{s.Name}: {BodyLabel(s.CurrentLocationId)} · {Coords(s.Position)} · "
                   + (match.Online
                       ? "online"
                       : _host.Localize(session.Locale, "srv.obs.last_seen").Replace("{time}", ShortTime(s.LastSeenUtc))),
        });
        _host.Send(session, new ServerMessage { Text = $"/goto {s.CurrentLocationId} {Coords(s.Position).Replace('/', ' ')}" });
    }

    /// <summary>One listed structure: what it is, who owns it, where, and the command to jump there.</summary>
    internal readonly record struct BuildEntry(string Kind, string Name, string Owner, string Body, Vector3i Cell);

    /// <summary>Every player-made structure the save knows about, across all bodies. Read from the repository
    /// rather than the in-memory world state: bases/beacons/beams are per-world and only the resident world's
    /// are loaded, but an admin asking "what has been built" means everywhere, not just here.</summary>
    internal List<BuildEntry> AllBuilds()
    {
        var list = new List<BuildEntry>();

        foreach (var b in _repo.ListAllBases())
        {
            list.Add(new BuildEntry("base", b.Name, b.OwnerId, b.Planet, new Vector3i(b.X, b.Y, b.Z)));
        }

        foreach (var b in _repo.ListAllBeacons())
        {
            list.Add(new BuildEntry("beacon", b.Label, b.OwnerId, b.Planet, new Vector3i(b.X, b.Y, b.Z)));
        }

        foreach (var b in _repo.ListAllBeams())
        {
            list.Add(new BuildEntry("beam", b.Name, b.OwnerId, b.Planet, new Vector3i(b.X, b.Y, b.Z)));
        }

        foreach (var st in _repo.ListSpaceStructures())
        {
            if (GameServer.IsDerelictId(st.Id))
            {
                continue; // #2634: a derelict's stored edits are nobody's build
            }

            list.Add(new BuildEntry("station", st.Name, st.OwnerId, st.Location,
                new Vector3i((int)st.PosX, (int)st.PosY, (int)st.PosZ)));
        }

        return list.OrderBy(e => e.Owner, StringComparer.OrdinalIgnoreCase).ThenBy(e => e.Kind).ToList();
    }

    /// <summary><c>/builds [name]</c> — named structures with owner + coordinates, optionally for one player.
    /// Unnamed building activity (a house with no base core) has no registry row and is found through the
    /// block-edit hotspots on the fleet admin panel instead (issue #489).</summary>
    internal void AdminListBuilds(PlayerSession session, string? ownerFilter)
    {
        var builds = AllBuilds();
        if (!string.IsNullOrWhiteSpace(ownerFilter))
        {
            string who = ownerFilter.Trim();
            builds = builds.Where(b => string.Equals(b.Owner, who, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        if (builds.Count == 0)
        {
            _host.Send(session, new ServerMessage { Text = "@srv.obs.no_builds" });
            return;
        }

        _host.Send(session, new ServerMessage
        {
            Text = _host.Localize(session.Locale, "srv.obs.builds_header").Replace("{count}", builds.Count.ToString()),
        });

        // The console is a chat log, so cap the burst: a world with hundreds of beacons would otherwise
        // scroll the admin's own screen into uselessness (and trip nothing, since this is server→client).
        const int maxLines = 40;
        foreach (var b in builds.Take(maxLines))
        {
            string label = string.IsNullOrWhiteSpace(b.Name) ? _host.Localize(session.Locale, "srv.obs.unnamed") : b.Name;
            _host.Send(session, new ServerMessage
            {
                Text = $"{b.Kind}: {label} · {b.Owner} · {BodyLabel(b.Body)} · /goto {b.Body} {b.Cell.X} {b.Cell.Y} {b.Cell.Z}",
            });
        }

        if (builds.Count > maxLines)
        {
            _host.Send(session, new ServerMessage
            {
                Text = _host.Localize(session.Locale, "srv.obs.builds_more")
                    .Replace("{count}", (builds.Count - maxLines).ToString()),
            });
        }
    }

    // ---------------- Jumping (issue #488) ----------------

    /// <summary><c>/goto</c> — the cross-body teleport the old <c>/tp</c> could never be: it moves the admin to
    /// another celestial body first and only then sets the position. Forms:
    /// <c>/goto &lt;player&gt;</c>, <c>/goto base|beacon|beam|station &lt;name&gt;</c>,
    /// <c>/goto &lt;bodyId&gt; &lt;x&gt; &lt;y&gt; &lt;z&gt;</c>.</summary>
    internal void AdminGoto(PlayerSession session, string? argument)
    {
        // No StringSplitOptions.TrimEntries here: this assembly also targets netstandard2.1 for the in-browser
        // singleplayer server, where that overload does not exist.
        var parts = (argument ?? string.Empty)
            .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToArray();
        if (parts.Length == 0)
        {
            _host.Reject(session, "admin", "@srv.obs.usage_goto");
            return;
        }

        // /goto <bodyId> <x> <y> <z>
        if (parts.Length == 4
            && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float gx)
            && float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float gy)
            && float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float gz))
        {
            GotoPosition(session, parts[0], new Vector3f(gx, gy, gz), parts[0]);
            return;
        }

        // /goto <kind> <name…>
        string kind = parts[0].ToLowerInvariant();
        if (parts.Length >= 2 && kind is "base" or "beacon" or "beam" or "station")
        {
            string wanted = string.Join(' ', parts.Skip(1)).Trim('"');
            var hit = AllBuilds().FirstOrDefault(b =>
                b.Kind == kind && string.Equals(b.Name, wanted, StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrEmpty(hit.Body))
            {
                _host.Reject(session, "admin", _host.Localize(session.Locale, "srv.obs.no_build_named")
                    .Replace("{kind}", kind)
                    .Replace("{name}", wanted));
                return;
            }

            // Stand next to the marker block, not inside it — a spectator noclips, but on exit that would
            // leave the admin embedded in a wall.
            GotoPosition(session, hit.Body,
                new Vector3f(hit.Cell.X + 0.5f, hit.Cell.Y + 2f, hit.Cell.Z + 0.5f), $"{kind} '{hit.Name}'");
            return;
        }

        // /goto <player>
        string playerName = string.Join(' ', parts).Trim('"');
        var match = AllKnownPlayers()
            .FirstOrDefault(e => string.Equals(e.State.Name, playerName, StringComparison.OrdinalIgnoreCase));
        if (match.State is null)
        {
            _host.Reject(session, "admin", "@srv.obs.unknown_player");
            return;
        }

        GotoPosition(session, match.State.CurrentLocationId, match.State.Position, match.State.Name);
    }

    /// <summary>Moves the admin to <paramref name="bodyId"/> (if that is not where they already are) and snaps
    /// them to <paramref name="target"/>.</summary>
    private void GotoPosition(PlayerSession session, string bodyId, Vector3f target, string what)
    {
        if (string.IsNullOrEmpty(bodyId))
        {
            _host.Reject(session, "admin", "@srv.obs.unknown_destination");
            return;
        }

        if (bodyId != session.CurrentLocationId)
        {
            if (_host.Galaxy?.FindBody(bodyId) is null)
            {
                _host.Reject(session, "admin", "@srv.obs.unknown_body");
                return;
            }

            // adminBypass skips the jump-generator requirement; the Instant Travel rule does not apply
            // because this is not the quick-travel screen.
            _host.HandleTravel(session, new TravelIntent { DestinationBodyId = bodyId }, quickTravel: false, adminBypass: true, adminTeleport: true); // #2493: booked silently
            if (session.CurrentLocationId != bodyId)
            {
                return; // travel refused (and already told the admin why)
            }
        }

        // The snap has to ride the RespawnNotice channel or the client discards it (#414 M7) — shared with
        // the named teleport in GameServerAdminTeleport.
        _host.SnapPlayerTo(session, target, "@srv.obs.jumped:" + what);
        _host.CheatLog(session.State, $"jumped to {what} on {bodyId}");
    }
}
