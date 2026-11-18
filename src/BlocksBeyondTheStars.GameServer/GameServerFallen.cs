// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The fallen (#3765, the death rule of a Hard Survival world — <see cref="DeathPenalty.Revive"/>, rules in
/// <see cref="FallenRules"/>). A player who dies on foot on a world does not wake up at the ship. Their things stay at
/// the place of death (the salvage capsule, as with every death penalty from Normal up), and they <b>watch</b>: they fly
/// freely, nothing hurts them, no enemy sees them, the other players do not see them, and every intent except moving
/// and talking is dropped.
/// <para>Deliberately NOT the admins' observer mode (<c>ObserverSystem</c>): that one gives up the parked ship and the
/// landing pad, hides the player from every list, logs as staff and may remove blocks. The fallen keep their ship and
/// their place in the world; only the client is told the same thing (<c>PlayerStateUpdate.Spectating</c>), so it flies
/// with the observer's camera.</para>
/// <para>Ways back: another player's <b>medkit</b> at the place of death (they stand up there, at the medkit's health);
/// nobody else left standing — alone after <see cref="FallenRules.AloneSeconds"/>, or everybody fallen — lets the player
/// start again at the ship (the client asks with the death screen's choice intent; the server checks). A death in
/// flight, on a spacewalk, in a ship or on a station, and a defeat by another player, respawn as everywhere.</para>
/// <para>Nothing of the state is saved: god mode and flight are session flags, and a leaving or crashed player is put
/// back at the ship (<see cref="FallenMarker"/> survives a crash and is undone on the next join).</para>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Milestone a fallen player carries, so a save written meanwhile is recognised on the next join.</summary>
    internal const string FallenMarker = "fallen:pending";

    private double _nextFallenCheckAt;
    private double _nextFallenBeaconAt;

    /// <summary>Called from <c>RespawnPlayer</c> once the things are in the capsule: true if the player fell instead of
    /// respawning.</summary>
    private bool FallInsteadOfRespawn(PlayerSession session, string reason, bool salvaged, bool sameWorld)
    {
        if (Rules.DeathPenalty != DeathPenalty.Revive || session.Respawn.SparedByPvp || !sameWorld || session.Spectating)
        {
            return false;
        }

        var p = session.State;
        var r = session.Respawn;
        r.Fallen = true;
        r.FallenAt = p.Position;
        r.FallenSince = _uptime;
        r.FallenReason = reason;
        r.FallenSalvaged = salvaged;
        r.FallenRestartNow = false;
        r.FallenToldRestartIn = float.NaN;
        p.Milestones.Add(FallenMarker);

        // Watching: nothing hurts, nothing hunts, the body flies. The hands are empty.
        p.GodMode = true;
        p.Fly = true;
        p.Health = 100f;
        p.Gliding = false;
        p.RopeAnchor = null;
        p.Seated = false;
        p.SeatCell = null;
        session.CarryingBlock = false;
        ReleaseDrivenVehicle(p);
        LeaveTrainSilently(p);

        TellFallen(session, firstTime: true);
        SendInventory(session); // the capsule emptied the pockets
        SendPlayerState(session);
        _repo.SavePlayer(p);
        foreach (var s in _sessions.Values)
        {
            if (s.Joined && !ReferenceEquals(s, session))
            {
                Send(s, new ServerMessage { Text = Localize(s.Locale, "srv.fallen.announce").Replace("{player}", p.Name) });
            }
        }

        _log.Info($"Player '{p.Name}' fell ({reason}) — watching until revived (salvage={salvaged}).");
        return true;
    }

    /// <summary>True if no player is left who could bring a fallen one back: nobody joined is standing (admins who only
    /// observe do not count).</summary>
    private bool NobodyStands()
    {
        foreach (var s in _sessions.Values)
        {
            if (s.Joined && !s.Spectating && !s.Respawn.Fallen)
            {
                return false;
            }
        }

        return true;
    }

    private int FallenCount()
    {
        int n = 0;
        foreach (var s in _sessions.Values)
        {
            if (s.Joined && s.Respawn.Fallen)
            {
                n++;
            }
        }

        return n;
    }

    /// <summary>Seconds until this fallen player may start again at the ship (<see cref="FallenRules.RestartIn"/>).</summary>
    private float FallenRestartIn(PlayerSession session)
        => FallenRules.RestartIn(!NobodyStands(), session.Respawn.FallenRestartNow, _uptime - session.Respawn.FallenSince);

    /// <summary>Tells the fallen player's client where it stands: watching, and when (if ever) the ship is an option.</summary>
    private void TellFallen(PlayerSession session, bool firstTime = false)
    {
        var r = session.Respawn;
        r.FallenToldRestartIn = FallenRestartIn(session);
        Send(session, new RespawnOptions
        {
            Reason = r.FallenReason,
            SalvageCapsuleDropped = r.FallenSalvaged,
            Fallen = true,
            FallenAgain = !firstTime,
            RestartInSeconds = r.FallenToldRestartIn,
        });
    }

    /// <summary>Once a tick: who may start again, and the light at each place somebody fell. Never moves a player itself —
    /// a restart is the player's own intent (<see cref="RestartFallenAtShip"/>), so it runs with that player's world and
    /// ship in hand.</summary>
    private void TickFallen()
    {
        if (_uptime < _nextFallenCheckAt)
        {
            return;
        }

        _nextFallenCheckAt = _uptime + 0.5;
        int fallen = FallenCount();
        if (fallen == 0)
        {
            return;
        }

        bool nobodyStands = NobodyStands();
        bool beacon = _uptime >= _nextFallenBeaconAt;
        if (beacon)
        {
            _nextFallenBeaconAt = _uptime + FallenRules.BeaconSeconds;
        }

        foreach (var s in _sessions.Values)
        {
            if (!s.Joined || !s.Respawn.Fallen)
            {
                continue;
            }

            if (nobodyStands && fallen >= 2)
            {
                s.Respawn.FallenRestartNow = true; // everybody fell: all of them start again (it stays so once said)
            }

            // The client counts the alone-seconds down itself; it is told again only when the answer changes kind.
            float now = FallenRestartIn(s);
            float told = s.Respawn.FallenToldRestartIn;
            if (float.IsNaN(told) || (now < 0f) != (told < 0f) || (now == 0f) != (told == 0f))
            {
                TellFallen(s);
            }

            if (beacon)
            {
                var at = s.Respawn.FallenAt;
                var fx = new WorldFx { Kind = "signal_ping", X = at.X, Y = at.Y + 0.6f, Z = at.Z, Strength = 0.6f };
                foreach (var other in _sessions.Values)
                {
                    if (other.Joined && other.CurrentLocationId == s.CurrentLocationId)
                    {
                        Send(other, fx);
                    }
                }
            }
        }
    }

    /// <summary>The fallen player asks to start again at the ship (the death screen's choice intent). Refused — silently,
    /// the client shows no button — while somebody could still bring them back.</summary>
    private void RestartFallenAtShip(PlayerSession session)
    {
        if (!session.Respawn.Fallen || FallenRestartIn(session) != 0f)
        {
            return;
        }

        var r = session.Respawn;
        EndFallen(session);
        r.FallenRestarting = true;
        try
        {
            CompleteRespawn(session, "@srv.fallen.restarted", r.FallenSalvaged, sameWorld: true, useCustomSpawn: false);
        }
        finally
        {
            r.FallenRestarting = false;
        }
    }

    /// <summary>A medkit pulse of <paramref name="medic"/>: every player who fell within the pulse's radius stands up
    /// again, at the place they fell. Returns how many.</summary>
    private int ReviveFallenNear(PlayerSession medic)
    {
        int revived = 0;
        foreach (var s in JoinedInActiveWorld().ToList())
        {
            if (!s.Respawn.Fallen || ReferenceEquals(s, medic)
                || WrapDistSq(medic.State.Position, s.Respawn.FallenAt) > MedkitRadius * MedkitRadius)
            {
                continue;
            }

            ReviveFallen(s, medic);
            revived++;
        }

        return revived;
    }

    /// <summary>True if a medkit pulse of <paramref name="medic"/> would bring somebody back.</summary>
    private bool FallenInMedkitReach(PlayerSession medic)
    {
        if (Rules.DeathPenalty != DeathPenalty.Revive)
        {
            return false;
        }

        foreach (var s in JoinedInActiveWorld())
        {
            if (s.Respawn.Fallen && !ReferenceEquals(s, medic)
                && WrapDistSq(medic.State.Position, s.Respawn.FallenAt) <= MedkitRadius * MedkitRadius)
            {
                return true;
            }
        }

        return false;
    }

    private void ReviveFallen(PlayerSession session, PlayerSession medic)
    {
        var p = session.State;
        var at = session.Respawn.FallenAt;
        EndFallen(session);
        p.Position = at;
        session.AwaitingSpawnAdopt = true; // ignore the watcher's pose until the client has snapped back (#865)
        p.Health = MedkitHealAmount;
        p.Oxygen = MaxOxygen(p);
        p.Hunger = System.Math.Max(p.Hunger, FallenRules.RevivedHunger);
        Send(session, new RespawnNotice { X = at.X, Y = at.Y, Z = at.Z, Reason = "@srv.fallen.revived", Died = false });
        SendPlayerState(session);
        Send(medic, new ServerMessage { Text = Localize(medic.Locale, "srv.fallen.you_revived").Replace("{player}", p.Name) });
        _repo.SavePlayer(p);
        _log.Info($"Player '{p.Name}' was revived by '{medic.State.Name}'.");
    }

    /// <summary>Ends the watching: the session flags go, the body can be hurt and walks again.</summary>
    private void EndFallen(PlayerSession session)
    {
        var p = session.State;
        session.Respawn.Fallen = false;
        session.Respawn.FallenRestartNow = false;
        p.GodMode = false;
        p.Fly = false;
        p.Milestones.Remove(FallenMarker);
    }

    /// <summary>A fallen player leaves: the save puts them at the ship, alive — leaving is no way to stand up where they
    /// fell, and nobody may come back hanging in the air.</summary>
    private void FallenOnLeave(PlayerSession session)
    {
        if (session.Respawn.Fallen)
        {
            EndFallen(session);
            PutBackAtShip(session.State);
        }
    }

    /// <summary>A save written while its player was watching (a crash): undone when the player is loaded.</summary>
    private static void UndoFallenInSave(PlayerState p)
    {
        if (p.Milestones.Remove(FallenMarker))
        {
            PutBackAtShip(p);
        }
    }

    private static void PutBackAtShip(PlayerState p)
    {
        p.Position = p.RespawnPoint;
        p.AboardShip = true;
        p.Health = 100f;
    }

    /// <summary>The intents a fallen player may still send: moving the watching camera, talking, the far view and the
    /// star map to look at, asking for help — and the wish to start again.</summary>
    private static bool FallenMayHandle(object message) => message switch
    {
        MoveIntent or ChatIntent or RespawnChoiceIntent or SelectHotbarIntent or BumpReport => true,
        RequestStarMap or FarTerrainTileRequest => true,
        SetAppearanceIntent or SetFaceIntent => true,
        CallAdminIntent or PlayerReportIntent or DirectChatIntent => true,
        _ => false,
    };

    /// <summary>Test seam: the intents a fallen player may send.</summary>
    public static bool FallenMayHandleForTest(object message) => FallenMayHandle(message);

    /// <summary>Test seam: the marker a fallen player's save carries.</summary>
    public const string FallenMarkerForTest = FallenMarker;

    /// <summary>Test seam: the leave step of a watching player.</summary>
    public void FallenOnLeaveForTest(string playerId)
    {
        if (FindSessionByPlayerId(playerId) is { } s)
        {
            FallenOnLeave(s);
        }
    }

    /// <summary>Test seam: what loading a player does to a save written while they watched.</summary>
    public static void UndoFallenInSaveForTest(PlayerState p) => UndoFallenInSave(p);

    /// <summary>Test seam: has this player fallen (watching)?</summary>
    public bool IsFallenForTest(string playerId) => FindSessionByPlayerId(playerId)?.Respawn.Fallen ?? false;

    /// <summary>Test seam: seconds until this fallen player may start again at the ship (-1: a team-mate still stands).</summary>
    public float FallenRestartInForTest(string playerId)
        => FindSessionByPlayerId(playerId) is { } s ? FallenRestartIn(s) : float.NaN;

    /// <summary>Test seam: the fallen player's wish to start again at the ship, as the client sends it.</summary>
    public void RestartFallenForTest(string playerId)
    {
        if (FindSessionByPlayerId(playerId) is { } s)
        {
            Serve(s);
            HandleRespawnChoice(s, new RespawnChoiceIntent());
        }
    }

    /// <summary>Test seam: the player dies of <paramref name="reason"/> where they stand.</summary>
    public void KillPlayerForTest(string playerId, string reason = "@srv.death.fall")
    {
        if (FindSessionByPlayerId(playerId) is { } s)
        {
            Serve(s);
            s.State.Health = 0f;
            RespawnPlayer(s, reason);
        }
    }

    /// <summary>Test seam: a medkit pulse of this player (no cooldown, no energy).</summary>
    public int MedkitPulseForTest(string playerId)
    {
        if (FindSessionByPlayerId(playerId) is not { } s)
        {
            return 0;
        }

        Serve(s);
        UseFieldMedkit(s);
        return FallenCount();
    }
}
