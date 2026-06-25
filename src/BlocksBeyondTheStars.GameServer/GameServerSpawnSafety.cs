// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Keeps players out of the bottomless void. The world has no bedrock floor (Y is unbounded), so a player
/// who ends up below the terrain with nothing under them falls forever — and because their position is
/// persisted and restored verbatim on the next join, a single fall can poison a save so every launch drops
/// them again. Two guards close that loop: <see cref="EnsureSafeSpawn"/> validates a player's position when
/// they join (self-healing a poisoned save), and <see cref="TickVoidRescue"/> recovers anyone caught
/// plummeting at runtime before that fall can be saved.
/// </summary>
public sealed partial class GameServer
{
    private const int VoidBelowSurface = 16; // only "void" once a player is this far under the terrain surface
    private const int VoidProbeDepth = 24;   // …with no solid block within this many blocks below them
    private const double VoidRescueInterval = 1.0; // how often the runtime void check runs (seconds)

    private double _sinceVoidCheck;

    /// <summary>True if there's a solid block within <paramref name="depth"/> blocks below the position —
    /// something to stand on (terrain, a cave floor, the ship's deck). Reads generate the column as needed.</summary>
    private bool HasGroundWithin(Vector3f pos, int depth)
    {
        int x = (int)System.Math.Floor(pos.X);
        int z = (int)System.Math.Floor(pos.Z);
        int y0 = (int)System.Math.Floor(pos.Y);
        for (int dy = 0; dy <= depth; dy++)
        {
            if (!_world.GetBlock(new Vector3i(x, y0 - dy, z)).IsAir)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True if a position is in the bottomless void: well below the terrain surface of its own
    /// column and with nothing solid to land on. Positions on/near the surface, on the ship, or on a cave
    /// floor are never "void".</summary>
    private bool IsInVoid(Vector3f pos)
    {
        if (!float.IsFinite(pos.X) || !float.IsFinite(pos.Y) || !float.IsFinite(pos.Z))
        {
            return true;
        }

        int surface = _generator.SurfaceHeight(_world.Planet,
            (int)System.Math.Floor(pos.X), (int)System.Math.Floor(pos.Z));
        if (pos.Y >= surface - VoidBelowSurface)
        {
            return false; // at/above the terrain (or standing on the ship/in a building)
        }

        return !HasGroundWithin(pos, VoidProbeDepth);
    }

    /// <summary>A safe place to stand in the active world: the ship's heal-tank if a ship is parked, else
    /// the landing-zone surface.</summary>
    private Vector3f SafeSpawnPoint(string playerId)
    {
        if (_shipPlaced)
        {
            return _healTank;
        }

        var pad = FindSessionByPlayerId(playerId) is { } s ? PlayerPad(s)
            : (_landingPads.Count > 0 ? _landingPads[0] : null);
        int px = pad?.CenterX ?? 0, pz = pad?.CenterZ ?? 0;
        int surfaceY = _generator.SurfaceHeight(_world.Planet, px, pz);
        return new Vector3f(px + 0.5f, surfaceY + 2f, pz + 0.5f);
    }

    /// <summary>Validates a joining player's position. If it's in the void — e.g. a position persisted
    /// mid-fall and restored on load — snap them (and a poisoned respawn point) back to a safe spawn, so a
    /// bad save self-heals instead of dropping them forever. No-op while in space / aboard a station.</summary>
    private void EnsureSafeSpawn(PlayerSession session)
    {
        var p = session.State;
        if (InSpace(p.PlayerId) || InStation(p.PlayerId))
        {
            return; // floating in a space instance / aboard a station — "ground below" doesn't apply
        }

        if (IsUnsafeSurfaceSpawn(p.Position))
        {
            var safe = SafeSpawnPoint(p.PlayerId);
            _log.Warn($"Player '{p.Name}' loaded at an unsafe position {p.Position}; respawning at {safe}.");
            p.Position = safe;
        }

        if (IsUnsafeSurfaceSpawn(p.RespawnPoint))
        {
            p.RespawnPoint = SafeSpawnPoint(p.PlayerId);
        }
    }

    /// <summary>Unsafe to load a SURFACE player at: in the bottomless void below the terrain, OR far ABOVE it.
    /// A position persisted from a space / EVA / ship-interior session can sit well above the planet surface
    /// (the flight scene is thousands of units up); restoring it drops the player out of the sky onto an empty
    /// planet (it reads as "falling through space, then stuck above the ground with no ship"). A normal surface
    /// join is just above the surface, so a wildly high position is rescued to the ship/pad too.</summary>
    private bool IsUnsafeSurfaceSpawn(Vector3f pos)
    {
        if (IsInVoid(pos))
        {
            return true;
        }

        int surface = _generator.SurfaceHeight(_world.Planet,
            (int)System.Math.Floor(pos.X), (int)System.Math.Floor(pos.Z));
        return float.IsFinite(pos.Y) && pos.Y > surface + 40; // far above the terrain → a stale space/flight pose
    }

    /// <summary>Belt-and-braces for <see cref="EnsureSafeSpawn"/>: rescues any surface player who is
    /// plummeting through the void at runtime (teleporting them to a safe spawn), so a live fall can never
    /// be persisted and re-poison the save. Throttled to once per <see cref="VoidRescueInterval"/>.</summary>
    private void TickVoidRescue(double dt)
    {
        _sinceVoidCheck += dt;
        if (_sinceVoidCheck < VoidRescueInterval)
        {
            return;
        }

        _sinceVoidCheck = 0;

        foreach (var s in JoinedInActiveWorld())
        {
            var p = s.State;
            if (InSpace(p.PlayerId) || InStation(p.PlayerId) || !IsInVoid(p.Position))
            {
                continue;
            }

            var safe = SafeSpawnPoint(p.PlayerId);
            p.Position = safe;
            _log.Warn($"Player '{p.Name}' fell into the void; recovered to {safe}.");
            Send(s, new RespawnNotice { X = safe.X, Y = safe.Y, Z = safe.Z, Reason = "Recovered from a fall." });
            SendPlayerState(s);
        }
    }

    /// <summary>Test entrypoint: run the runtime void rescue for the active world immediately.</summary>
    public void RunVoidRescueForTest()
    {
        _sinceVoidCheck = VoidRescueInterval;
        TickVoidRescue(0);
    }

    /// <summary>Test entrypoint: whether a position is in the bottomless void of the active world.</summary>
    public bool IsInVoidForTest(Vector3f pos) => IsInVoid(pos);

    /// <summary>Test entrypoint: run the join-time spawn-safety guard for a player session.</summary>
    public void EnsureSafeSpawnForTest(PlayerSession session) => EnsureSafeSpawn(session);
}
