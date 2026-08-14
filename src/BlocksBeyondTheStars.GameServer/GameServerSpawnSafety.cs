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

    // Per-world (routes through the active world) — a shared field would starve the void rescue on all but one world.
    private double _sinceVoidCheck { get => _worlds.Active.SinceVoidCheck; set => _worlds.Active.SinceVoidCheck = value; }

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

    /// <summary>
    /// True when a position is sealed inside solid blocks — both the feet cell and the head cell above it
    /// block movement, so there is no standing room and no way to walk out.
    /// <para>
    /// The void guards below deliberately do NOT catch this: <see cref="HasGroundWithin"/> answers "is there
    /// something under me", and someone buried in bedrock has stone under them in abundance, so
    /// <see cref="IsInVoid"/> reports a perfectly safe position. A player reported the resulting lockout —
    /// spawned at the world-origin column 85 blocks down, motionless for the whole session, 7550 stone
    /// blocks around him and no air anywhere. Full health, so nothing ever killed him out of it either.
    /// </para>
    /// Keyed on <see cref="IsBodyBlockingCell"/> rather than "non-air" so a swimmer (water is non-solid), a
    /// ladder climber, or someone in a torch/flora cell is never mistaken for entombed.
    /// </summary>
    private bool IsEntombed(Vector3f pos)
    {
        if (!float.IsFinite(pos.X) || !float.IsFinite(pos.Y) || !float.IsFinite(pos.Z))
        {
            return false; // non-finite is the void guard's business, not ours
        }

        int x = (int)System.Math.Floor(pos.X);
        int y = (int)System.Math.Floor(pos.Y);
        int z = (int)System.Math.Floor(pos.Z);
        return IsBodyBlockingCell(x, y, z) && IsBodyBlockingCell(x, y + 1, z);
    }

    /// <summary>
    /// A cell the player's body genuinely cannot occupy — the ONLY kind that counts for the entombed rescue.
    /// Deliberately stricter than <see cref="IsSolidCell"/>: fluids are excluded (a submerged swimmer is not
    /// stuck — the 1 Hz rescue used to "dig out" every diver onto the surface the moment the water was two
    /// deep), and so is the flora category (kelp/vine strands stack into columns, have no client collider,
    /// and would re-open the same loop for anyone swimming through a kelp forest). Belt-and-braces on the
    /// fluid check: even a fluid whose data ever regains <c>solid: true</c> must not re-trap swimmers.
    /// </summary>
    private bool IsBodyBlockingCell(int x, int y, int z)
    {
        var id = _world.GetBlock(new Vector3i(x, y, z));
        if (id.IsAir || IsFluid(id.Value))
        {
            return false;
        }

        var def = _content.BlockById(id);
        if (def == null)
        {
            return true; // unknown id → treat as blocking (safe default, matches IsSolidCell)
        }

        return def.Solid && def.Category != "flora";
    }

    /// <summary>
    /// The join-time form of <see cref="IsEntombed"/>, gated on the position being BELOW the terrain surface.
    /// <para>
    /// The gate is about cost, not correctness: <see cref="IsEntombed"/> reads blocks, and a block read
    /// generates and caches the column. Probing unconditionally on every join would load the spawn chunk
    /// before the streaming pass ever runs — which is exactly what a chunk-streaming test caught. The
    /// surface height comes from the generator's noise and touches no chunk, so a normal above-ground spawn
    /// now costs nothing again. Someone sealed in by PLACED blocks above ground is left to
    /// <see cref="TickVoidRescue"/>, which frees them a second later on a world that is loaded anyway.
    /// </para>
    /// </summary>
    private bool IsEntombedOnLoad(Vector3f pos)
    {
        if (!float.IsFinite(pos.X) || !float.IsFinite(pos.Y) || !float.IsFinite(pos.Z))
        {
            return false;
        }

        int surface = _generator.SurfaceHeight(_world.Planet,
            (int)System.Math.Floor(pos.X), (int)System.Math.Floor(pos.Z));
        return pos.Y < surface && IsEntombed(pos);
    }

    /// <summary>Lifts an entombed position straight up to the first cell with standing room (feet + head
    /// clear) resting on something the body actually stands on. Returns null when the column has no such gap
    /// within <see cref="EntombedProbeHeight"/> — the caller then falls back to the ship/landing pad.
    /// Same predicate as <see cref="IsEntombed"/>, so water or a kelp cell is never offered as a floor.</summary>
    private Vector3f? DigOutUpwards(Vector3f pos)
    {
        int x = (int)System.Math.Floor(pos.X);
        int z = (int)System.Math.Floor(pos.Z);
        int y0 = (int)System.Math.Floor(pos.Y);
        for (int y = y0 + 1; y <= y0 + EntombedProbeHeight; y++)
        {
            if (!IsBodyBlockingCell(x, y, z) && !IsBodyBlockingCell(x, y + 1, z) && IsBodyBlockingCell(x, y - 1, z))
            {
                return new Vector3f(x + 0.5f, y, z + 0.5f);
            }
        }

        return null;
    }

    /// <summary>How far up we look for standing room before giving up and using the ship/landing pad.</summary>
    private const int EntombedProbeHeight = 256;

    /// <summary>A safe place to stand in the active world: the player's OWN ship's heal-tank if their ship
    /// is parked here, else the landing-zone surface. Resolved by <paramref name="playerId"/>, NOT the ship
    /// cursor — the void-rescue tick runs with the cursor on whoever was served last, and the cursor's heal
    /// tank teleported the rescued player into someone else's hull (#1020).</summary>
    private Vector3f SafeSpawnPoint(string playerId)
    {
        var ownShip = _worlds.Active.LandedFor(playerId);
        if (ownShip.Placed)
        {
            return ownShip.HealTank;
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

        // Buried in solid rock: try the cheap, local rescue first — walk straight up the column to the first
        // gap with standing room. That keeps a player who merely clipped into terrain near where they were,
        // instead of yanking them across the map. Only a column with no gap at all falls through to the pad.
        if (IsEntombedOnLoad(p.Position))
        {
            var dugOut = DigOutUpwards(p.Position);
            var to = dugOut ?? SafeSpawnPoint(p.PlayerId);
            _log.Warn($"Player '{p.Name}' loaded sealed inside blocks at {p.Position}; moved to {to}.");
            p.Position = to;
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
        if (IsInVoid(pos) || IsEntombedOnLoad(pos))
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
            if (InSpace(p.PlayerId) || InStation(p.PlayerId))
            {
                continue;
            }

            // Sealed inside blocks at runtime — terrain stamped over someone, or a spawn that landed in rock.
            // Lift them straight up out of the column; only a column with no gap at all resorts to the pad,
            // because a full teleport across the map for what may be a moment of clipping would be worse.
            if (IsEntombed(p.Position))
            {
                var freed = DigOutUpwards(p.Position) ?? SafeSpawnPoint(p.PlayerId);
                p.Position = freed;
                s.AwaitingSpawnAdopt = true; // #865: the client's stale stream must not drag them back in
                _log.Warn($"Player '{p.Name}' was sealed inside blocks; moved to {freed}.");
                Send(s, new RespawnNotice { X = freed.X, Y = freed.Y, Z = freed.Z, Reason = "@srv.misc.dug_out" });
                SendPlayerState(s);
                continue;
            }

            if (!IsInVoid(p.Position))
            {
                continue;
            }

            var safe = SafeSpawnPoint(p.PlayerId);
            p.Position = safe;
            s.AwaitingSpawnAdopt = true; // #865: the client's stale stream must not drag them back down
            _log.Warn($"Player '{p.Name}' fell into the void; recovered to {safe}.");
            Send(s, new RespawnNotice { X = safe.X, Y = safe.Y, Z = safe.Z, Reason = "@srv.misc.fall_recovered" });
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

    /// <summary>Test entrypoint: whether a position is sealed inside solid blocks.</summary>
    public bool IsEntombedForTest(Vector3f pos) => IsEntombed(pos);

    /// <summary>Test entrypoint: run the join-time spawn-safety guard for a player session.</summary>
    public void EnsureSafeSpawnForTest(PlayerSession session) => EnsureSafeSpawn(session);
}
