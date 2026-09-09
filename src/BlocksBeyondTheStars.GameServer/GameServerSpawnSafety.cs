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
        // #1681: the heal tank is the right answer only while it is somewhere a body can be. A second hull
        // parked over this one makes the tank part of the trap, and returning it every second is what turned
        // "two ships on one pad" into a player who could not get out at all.
        if (ownShip.Placed && !InsideForeignHull(playerId, ownShip.HealTank))
        {
            return ownShip.HealTank;
        }

        var pad = FindSessionByPlayerId(playerId) is { } s ? PlayerPad(s)
            : (_landingPads.Count > 0 ? _landingPads[0] : null);
        int px = pad?.CenterX ?? 0, pz = pad?.CenterZ ?? 0;
        int surfaceY = PadSurfaceY(px, pz); // real ground over the pad, never the generated surface alone (#1318)
        return new Vector3f(px + 0.5f, surfaceY + 2f, pz + 0.5f);
    }

    /// <summary>True if this position lies inside a parked hull that belongs to someone else (#1681).
    /// Construction sites do not count: an open keel frame is meant to be walked into.</summary>
    private bool InsideForeignHull(string playerId, Vector3f p)
    {
        foreach (var (key, rec) in _worlds.Active.LandedShips)
        {
            if (!rec.Placed || IsConstructionKey(key) || key == playerId)
            {
                continue;
            }

            if (LandedBoundsContain(rec, p))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// True when the player is standing where their OWN parked hull and someone else's overlap (#1681) — the
    /// signature of two ships stamped on one pad. Deliberately not "inside a foreign hull": visiting another
    /// player's ship is normal, and only the overlap is unambiguously a place a body cannot be.
    /// </summary>
    private bool WedgedInOverlappingHulls(PlayerSession s)
    {
        var own = _worlds.Active.LandedFor(s.State.PlayerId);
        return own.Placed
            && LandedBoundsContain(own, s.State.Position)
            && InsideForeignHull(s.State.PlayerId, s.State.Position);
    }

    /// <summary>
    /// True when a position has no body space inside a parked hull — feet cell and head cell both filled by
    /// ship blocks (#1709).
    /// <para>
    /// The block rescue above cannot see this at all: a hull is a placed OBJECT, so <see cref="IsEntombed"/>
    /// reads air from the world grid and reports a perfectly safe spot. <see cref="WedgedInOverlappingHulls"/>
    /// cannot see it either — it asks for TWO hulls, because visiting someone else's ship is normal and only
    /// the overlap of two is unambiguous. Between them they missed the simplest trap of all: a player sealed
    /// inside a SINGLE hull, their own. A child in the school club fell, respawned at his heal tank inside the
    /// hull, and could not get out — the hull is indestructible, so he could not even dig. He had to abandon
    /// the session.
    /// </para>
    /// Being inside your own ship stays normal: the test is "no body space in this cell", not "inside a hull".
    /// </summary>
    private bool SealedInHull(Vector3f p)
    {
        if (!float.IsFinite(p.X) || !float.IsFinite(p.Y) || !float.IsFinite(p.Z))
        {
            return false; // non-finite is the void guard's business
        }

        var cell = new Vector3i(
            (int)System.Math.Floor(p.X), (int)System.Math.Floor(p.Y), (int)System.Math.Floor(p.Z));

        foreach (var (key, rec) in _worlds.Active.LandedShips)
        {
            // A construction site is an open keel frame — meant to be walked into, and its cells are not a trap.
            if (!rec.Placed || IsConstructionKey(key) || !LandedBoundsContain(rec, p))
            {
                continue;
            }

            var feet = rec.ToLocal(cell, _world.Circumference);
            var head = new Vector3i(feet.X, feet.Y + 1, feet.Z);
            if (HullCellBlocksBody(rec, feet) && HullCellBlocksBody(rec, head))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether one structure-local cell of a parked hull is a cell a player's body cannot occupy
    /// (#1709). Mirrors <see cref="IsBodyBlockingCell"/>, but reads the ship's sparse cell grid instead of the
    /// world: a missing key is the hull's own air (corridors, cabins), which is where players belong.</summary>
    private bool HullCellBlocksBody(LandedShip rec, Vector3i local)
    {
        if (!rec.Structure.Cells.TryGetValue(local, out var id) || id.IsAir)
        {
            return false;
        }

        var def = _content.BlockById(id);
        if (def == null)
        {
            return true; // unknown id → treat as blocking, same safe default as the world-side predicate
        }

        return def.Solid && def.Category != "flora";
    }

    /// <summary>Open ground beside the player's pad, outside every parked hull (#1681), or null when the whole
    /// band around the pad is blocked — then the rescue leaves them be rather than teleporting them somewhere
    /// worse.</summary>
    private Vector3f? FreeSpotOutsideHulls(PlayerSession s)
        => NearestStandableSpotOutsidePad(PlayerPad(s), s.State.Position, WedgedRescueRingMin, WedgedRescueRingMax);

    /// <summary>How far outside the pad rim the wedged rescue looks for open ground (#1681) — far enough to
    /// clear any hull centred on the pad, near enough that the player is still at their own landing site.</summary>
    private const int WedgedRescueRingMin = 2;
    private const int WedgedRescueRingMax = 12;

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
            if (InSpace(p.PlayerId))
            {
                continue;
            }

            if (InStation(p.PlayerId))
            {
                RescueDriftingBoarder(s); // #1485: a boarder lost far beyond the station's gravity volume comes back to the pad
                continue;
            }

            // Sealed inside blocks at runtime — terrain stamped over someone, or a spawn that landed in rock.
            // Lift them straight up out of the column; only a column with no gap at all resorts to the pad,
            // because a full teleport across the map for what may be a moment of clipping would be worse.
            if (IsEntombed(p.Position))
            {
                // #1708: never send a second rescue while OUR OWN last one is still in flight. A RespawnNotice
                // is a hard snap on the client: it re-arms the settle freeze (_settling, _settleTimer = 0),
                // which holds the body with no gravity and no WASD and only releases after an 8 s grace. At
                // 1 Hz the grace never elapsed, so the freeze was re-armed a second before it could lift and
                // the player sat there permanently — "kann mich nicht bewegen, nur drehen", with "Stabilisiere
                // Position …" on screen and the rescue toast above it.
                // Both halves of the condition matter. AwaitingSpawnAdopt alone is NOT enough: a join sets it
                // too, so gating on it would have silenced the FIRST rescue of anyone who logs in sealed in —
                // and someone entombed may never send the movement report that clears it, which would leave
                // them stuck forever by the guard meant to protect them. EntombedRescueSpot is set only by the
                // rescue below, so together they mean exactly "we placed them and the client has not arrived
                // yet". A player still sealed in after adoption is rescued again on the next tick.
                if (s.AwaitingSpawnAdopt && s.EntombedRescueSpot is not null)
                {
                    continue;
                }

                var freed = DigOutUpwards(p.Position) ?? SafeSpawnPoint(p.PlayerId);
                // #1367: when the pad fallback is walled in as well (a build higher than the touchdown scan over
                // the pad), the rescue used to fire every second — teleport, toast and chat line — into the same
                // blocked spot for as long as the player stayed sealed in. One rescue per entombment episode: a
                // destination that is itself entombed and the same as last time is left alone until the player
                // (or the world) changes something; a new destination is a new rescue.
                if (s.EntombedRescueSpot is { } last && last.ToBlock() == freed.ToBlock() && IsEntombed(freed))
                {
                    continue;
                }

                s.EntombedRescueSpot = freed;
                p.Position = freed;
                s.AwaitingSpawnAdopt = true; // #865: the client's stale stream must not drag them back in
                _log.Warn($"Player '{p.Name}' was sealed inside blocks; moved to {freed}.");
                Send(s, new RespawnNotice { X = freed.X, Y = freed.Y, Z = freed.Z, Reason = "@srv.misc.dug_out" });
                // #1318: the HUD toast is overwritten by the next message (a landing sends several), so the
                // rescue "flashed too briefly to read" — mirror it into the chat scrollback as plain text.
                Send(s, new ServerMessage { Text = Localize(s.Locale, "srv.misc.dug_out") });
                SendPlayerState(s);
                continue;
            }

            // #1681: hulls are placed OBJECTS, not world blocks, so IsEntombed above cannot see them at all.
            // A player standing where two hulls overlap is walled in by geometry the block rescue is blind to.
            // #1709: so is a player sealed inside a SINGLE hull — feet and head cell both ship blocks. That is
            // the trap a fallen player lands in when the heal tank sits behind hull geometry, and because the
            // hull is indestructible it is the one trap the player cannot dig out of.
            if (WedgedInOverlappingHulls(s) || SealedInHull(p.Position))
            {
                if (FreeSpotOutsideHulls(s) is { } outside)
                {
                    s.EntombedRescueSpot = null;
                    p.Position = outside;
                    p.AboardShip = false; // stepped out of the hull, onto open ground
                    s.AwaitingSpawnAdopt = true;
                    _log.Warn($"Player '{p.Name}' had no body space inside a parked hull; moved to {outside}.");
                    Send(s, new RespawnNotice { X = outside.X, Y = outside.Y, Z = outside.Z, Reason = "@srv.misc.freed_from_hull" });
                    Send(s, new ServerMessage { Text = Localize(s.Locale, "srv.misc.freed_from_hull") });
                    SendPlayerState(s);
                }

                continue;
            }

            s.EntombedRescueSpot = null; // out in the open again — the episode is over

            if (!IsInVoid(p.Position))
            {
                continue;
            }

            var safe = SafeSpawnPoint(p.PlayerId);
            p.Position = safe;
            s.AwaitingSpawnAdopt = true; // #865: the client's stale stream must not drag them back down
            _log.Warn($"Player '{p.Name}' fell into the void; recovered to {safe}.");
            Send(s, new RespawnNotice { X = safe.X, Y = safe.Y, Z = safe.Z, Reason = "@srv.misc.fall_recovered" });
            Send(s, new ServerMessage { Text = Localize(s.Locale, "srv.misc.fall_recovered") }); // readable in chat too (#1318)
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

    /// <summary>Test entrypoint (#1709): whether a position has no body space inside a parked hull.</summary>
    public bool SealedInHullForTest(Vector3f pos) => SealedInHull(pos);

    /// <summary>Test/diagnostic (#1709): a world cell inside this player's parked hull where a body has no
    /// room — feet and head cell both ship blocks — or null when the design has no such spot.</summary>
    public Vector3f? SealedHullSpotForTest(string playerId)
    {
        var rec = _worlds.Active.LandedFor(playerId);
        if (!rec.Placed)
        {
            return null;
        }

        foreach (var local in rec.Structure.Cells.Keys)
        {
            var head = new Vector3i(local.X, local.Y + 1, local.Z);
            if (!HullCellBlocksBody(rec, local) || !HullCellBlocksBody(rec, head))
            {
                continue;
            }

            var spot = new Vector3f(
                rec.Origin.X + local.X + 0.5f, rec.Origin.Y + local.Y, rec.Origin.Z + local.Z + 0.5f);
            if (SealedInHull(spot))
            {
                return spot;
            }
        }

        return null;
    }

    /// <summary>Test entrypoint: run the join-time spawn-safety guard for a player session.</summary>
    public void EnsureSafeSpawnForTest(PlayerSession session) => EnsureSafeSpawn(session);
}
