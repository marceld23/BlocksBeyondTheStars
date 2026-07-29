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

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Fleet-admin observer mode (issue #487) and the admin inspection commands (issue #488).
///
/// <para>Invisibility here is a <b>server-side mode</b>, not the cosmetic <c>Stealthed</c> flag. Stealth only
/// asks the client not to draw an avatar — an old or modified client can simply ignore it, and it does nothing
/// about the footprint a joining player leaves in a world: a parked ship object, a claimed landing pad, a
/// broadcast descent animation, spawned fauna, a consumed player slot. An observer must leave none of that, so
/// the suppression happens where the data is produced.</para>
/// </summary>
public sealed partial class GameServer
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

    /// <summary>True when this session's client is German — server-authored text is written in the player's
    /// language (the game is bilingual DE/EN).</summary>
    private static bool De(PlayerSession session) => session.Locale == "de";

    /// <summary>Which client intents an observer may still act on. Movement and pure read/query traffic pass;
    /// mining passes because removing an offensive build is the one in-world moderation lever we have (bans are
    /// account-level and world stop/delete is a sledgehammer). Everything else is dropped.</summary>
    private static bool SpectatorMayHandle(object message) => message switch
    {
        MoveIntent or SelectHotbarIntent or ChatIntent or AdminCommandIntent or BumpReport => true,
        MineBlockIntent => true, // moderation: remove an offensive build (logged via CheatLog)
        RequestStarMap or RequestMissions or RequestCompanionsIntent or RequestLandingPadsIntent => true,
        SetAppearanceIntent or SetFaceIntent => true, // cosmetic, and never broadcast while spectating anyway
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

    // ---------------- Entering / leaving observer mode ----------------

    /// <summary>Turns observer mode on: the world forgets the admin is there. Order matters — the ship object
    /// and the presence handshake must go out while the session is still "visible", otherwise the removal
    /// messages are themselves suppressed and every client keeps a frozen avatar and a ghost ship.</summary>
    private void EnterSpectate(PlayerSession session)
    {
        var p = session.State;

        // Give up the world footprint FIRST, while broadcasts still reach the world.
        if (SetActiveWorld(session.CurrentLocationId))
        {
            SetCurrent(session);
            RemoveLandedShip(session);        // the parked ship object leaves with its (now invisible) owner
        }

        session.AssignedPadIndex = -1;        // release the landing pad: pads are communal and finite
        BroadcastToWorld(new PlayerLeft { PlayerId = p.PlayerId }); // clients drop the avatar they can see
        // Pets and deployed speeders despawn on the next reconcile tick — CompanionOwnersHere/ReconcileSpeeders
        // stop counting a spectating session as present.

        session.Spectating = true;
        p.GodMode = true;                     // no damage, no drains, and creature targeting already skips this
        p.Stealthed = true;                   // belt and braces: enemy aggro checks honour it too
        p.Fly = true;
        p.AboardShip = false;                 // no ship to be aboard: cargo crafting/life support don't apply
        p.InSpeeder = string.Empty;

        SendPlayerState(session);
        Send(session, new ServerMessage
        {
            Text = De(session)
                ? "Beobachter-Modus AN — du bist unsichtbar, unverwundbar und hinterlässt keine Spuren."
                : "Observer mode ON — you are invisible, invulnerable and leave no trace.",
        });
        CheatLog(p, "entered observer mode");
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

        if (SetActiveWorld(session.CurrentLocationId))
        {
            SetCurrent(session);
            if (_config.PlaceStarterShip)
            {
                ClaimPadOrReject(session, session.CurrentLocationId, -1); // take a pad again (any free one)
                PlaceLandedShip();
            }
        }

        // Re-announce: presence resumes on the next tick by itself, but the face is out-of-band and would
        // otherwise stay missing until the admin edits it.
        BroadcastFace(session);
        SendPlayerState(session);
        Send(session, new ServerMessage
        {
            Text = De(session) ? "Beobachter-Modus AUS." : "Observer mode OFF.",
        });
        CheatLog(p, "left observer mode");
    }

    /// <summary><c>/spectate [on|off]</c> — fleet admins only.</summary>
    private void HandleSpectateCommand(PlayerSession session, string? arg)
    {
        bool on = string.IsNullOrWhiteSpace(arg)
            ? !session.Spectating
            : arg.Trim().ToLowerInvariant() is "on" or "an" or "1" or "true";

        if (on == session.Spectating)
        {
            Send(session, new ServerMessage
            {
                Text = De(session)
                    ? $"Beobachter-Modus ist bereits {(on ? "an" : "aus")}."
                    : $"Observer mode is already {(on ? "on" : "off")}.",
            });
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

        foreach (var s in _sessions.Values)
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

        var body = _galaxy?.FindBody(bodyId);
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
    private void AdminListPlayers(PlayerSession session)
    {
        bool de = De(session);
        var all = AllKnownPlayers();
        if (all.Count == 0)
        {
            Send(session, new ServerMessage { Text = de ? "Keine Spieler bekannt." : "No players known." });
            return;
        }

        Send(session, new ServerMessage
        {
            Text = de ? $"— Spieler ({all.Count}) —" : $"— Players ({all.Count}) —",
        });

        foreach (var (state, online, observing) in all)
        {
            string status = observing
                ? (de ? "beobachtet" : "observing")
                : online ? "online" : ShortTime(state.LastSeenUtc); // "online" reads the same in both languages

            Send(session, new ServerMessage
            {
                Text = $"{state.Name} [{state.Role}] · {BodyLabel(state.CurrentLocationId)} · "
                       + $"{Coords(state.Position)} · {status}",
            });
        }
    }

    /// <summary><c>/where &lt;name&gt;</c> — one player's body, position and last-seen time. Works for offline
    /// players: the position is persisted per player, so the save already knows where they stopped.</summary>
    private void AdminWhere(PlayerSession session, string? name)
    {
        bool de = De(session);
        if (string.IsNullOrWhiteSpace(name))
        {
            Reject(session, "admin", de ? "Benutzung: /where Name" : "Usage: /where Name");
            return;
        }

        var match = AllKnownPlayers()
            .FirstOrDefault(e => string.Equals(e.State.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
        if (match.State is null)
        {
            Reject(session, "admin", de ? "Unbekannter Spieler." : "Unknown player.");
            return;
        }

        var s = match.State;
        Send(session, new ServerMessage
        {
            Text = $"{s.Name}: {BodyLabel(s.CurrentLocationId)} · {Coords(s.Position)} · "
                   + (match.Online
                       ? "online"
                       : (de ? "zuletzt " : "last seen ") + ShortTime(s.LastSeenUtc)),
        });
        Send(session, new ServerMessage { Text = $"/goto {s.CurrentLocationId} {Coords(s.Position).Replace('/', ' ')}" });
    }

    /// <summary>One listed structure: what it is, who owns it, where, and the command to jump there.</summary>
    private readonly record struct BuildEntry(string Kind, string Name, string Owner, string Body, Vector3i Cell);

    /// <summary>Every player-made structure the save knows about, across all bodies. Read from the repository
    /// rather than the in-memory world state: bases/beacons/beams are per-world and only the resident world's
    /// are loaded, but an admin asking "what has been built" means everywhere, not just here.</summary>
    private List<BuildEntry> AllBuilds()
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
            list.Add(new BuildEntry("station", st.Name, st.OwnerId, st.Location,
                new Vector3i((int)st.PosX, (int)st.PosY, (int)st.PosZ)));
        }

        return list.OrderBy(e => e.Owner, StringComparer.OrdinalIgnoreCase).ThenBy(e => e.Kind).ToList();
    }

    /// <summary><c>/builds [name]</c> — named structures with owner + coordinates, optionally for one player.
    /// Unnamed building activity (a house with no base core) has no registry row and is found through the
    /// block-edit hotspots on the fleet admin panel instead (issue #489).</summary>
    private void AdminListBuilds(PlayerSession session, string? ownerFilter)
    {
        bool de = De(session);
        var builds = AllBuilds();
        if (!string.IsNullOrWhiteSpace(ownerFilter))
        {
            string who = ownerFilter.Trim();
            builds = builds.Where(b => string.Equals(b.Owner, who, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        if (builds.Count == 0)
        {
            Send(session, new ServerMessage
            {
                Text = de ? "Keine benannten Bauten gefunden." : "No named structures found.",
            });
            return;
        }

        Send(session, new ServerMessage
        {
            Text = de ? $"— Bauten ({builds.Count}) —" : $"— Structures ({builds.Count}) —",
        });

        // The console is a chat log, so cap the burst: a world with hundreds of beacons would otherwise
        // scroll the admin's own screen into uselessness (and trip nothing, since this is server→client).
        const int maxLines = 40;
        foreach (var b in builds.Take(maxLines))
        {
            string label = string.IsNullOrWhiteSpace(b.Name) ? "(unnamed)" : b.Name;
            Send(session, new ServerMessage
            {
                Text = $"{b.Kind}: {label} · {b.Owner} · {BodyLabel(b.Body)} · /goto {b.Body} {b.Cell.X} {b.Cell.Y} {b.Cell.Z}",
            });
        }

        if (builds.Count > maxLines)
        {
            Send(session, new ServerMessage
            {
                Text = de
                    ? $"… {builds.Count - maxLines} weitere — mit /builds Name eingrenzen."
                    : $"… {builds.Count - maxLines} more — narrow it down with /builds Name.",
            });
        }
    }

    // ---------------- Jumping (issue #488) ----------------

    /// <summary><c>/goto</c> — the cross-body teleport the old <c>/tp</c> could never be: it moves the admin to
    /// another celestial body first and only then sets the position. Forms:
    /// <c>/goto &lt;player&gt;</c>, <c>/goto base|beacon|beam|station &lt;name&gt;</c>,
    /// <c>/goto &lt;bodyId&gt; &lt;x&gt; &lt;y&gt; &lt;z&gt;</c>.</summary>
    private void AdminGoto(PlayerSession session, string? argument)
    {
        bool de = De(session);
        // No StringSplitOptions.TrimEntries here: this assembly also targets netstandard2.1 for the in-browser
        // singleplayer server, where that overload does not exist.
        var parts = (argument ?? string.Empty)
            .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToArray();
        if (parts.Length == 0)
        {
            Reject(session, "admin", de
                ? "Benutzung: /goto Spieler · /goto base|beacon|beam|station Name · /goto Körper X Y Z"
                : "Usage: /goto Player · /goto base|beacon|beam|station Name · /goto Body X Y Z");
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
                Reject(session, "admin", de ? $"Kein {kind} mit dem Namen '{wanted}'." : $"No {kind} named '{wanted}'.");
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
            Reject(session, "admin", de ? "Unbekannter Spieler." : "Unknown player.");
            return;
        }

        GotoPosition(session, match.State.CurrentLocationId, match.State.Position, match.State.Name);
    }

    /// <summary>Moves the admin to <paramref name="bodyId"/> (if that is not where they already are) and snaps
    /// them to <paramref name="target"/>.</summary>
    private void GotoPosition(PlayerSession session, string bodyId, Vector3f target, string what)
    {
        bool de = De(session);
        if (string.IsNullOrEmpty(bodyId))
        {
            Reject(session, "admin", de ? "Unbekannter Zielort." : "Unknown destination.");
            return;
        }

        if (bodyId != session.CurrentLocationId)
        {
            if (_galaxy?.FindBody(bodyId) is null)
            {
                Reject(session, "admin", de ? "Unbekannter Himmelskörper." : "Unknown celestial body.");
                return;
            }

            // adminBypass skips the jump-generator requirement; the Instant Travel rule does not apply
            // because this is not the quick-travel screen.
            HandleTravel(session, new TravelIntent { DestinationBodyId = bodyId }, quickTravel: false, adminBypass: true);
            if (session.CurrentLocationId != bodyId)
            {
                return; // travel refused (and already told the admin why)
            }
        }

        // The snap has to ride the RespawnNotice channel or the client discards it (#414 M7) — shared with
        // the named teleport in GameServerAdminTeleport.
        SnapPlayerTo(session, target, de ? $"Gesprungen zu {what}." : $"Jumped to {what}.");
        CheatLog(session.State, $"jumped to {what} on {bodyId}");
    }
}
