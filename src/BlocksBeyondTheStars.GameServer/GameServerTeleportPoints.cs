// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The teleporter's places (#2994, #2995): besides the ship and an ally, a teleporter jumps to the fixed places of the body
/// the player stands on — the player's own and allied <b>bases</b>, <b>beam pads</b> and <b>radio beacons</b>, and
/// <b>map markers</b> (own, or shared by an ally) — and the Quantum Teleporter to a free point: typed <b>coordinates</b> or
/// the map <b>waypoint</b>.
/// <para>The client only names the place (a kind and a canonical cell, or a marker id); the server finds it, checks that it
/// may be used and works out where to stand: beside a base core or a beacon, on top of a beam pad, on the surface column of
/// a marker or a free point (climbing out of buildings like <c>/tp</c>). A marker or a free point over lava is refused. A
/// marker is a point the player picked on the map, so the plain suit teleporter only reaches markers on ground the player
/// has explored (the planet map's remembered cells) — anywhere else is the Quantum Teleporter's free jump. The device
/// gates are the teleporter's own (<see cref="TeleporterReady"/>, <see cref="TeleportRules"/>).</para>
/// </summary>
public sealed partial class GameServer
{
    private void HandleTeleportToPoint(PlayerSession session, TeleportToPointIntent intent)
        => TeleportToPoint(session.State.PlayerId, intent);

    /// <summary>Jumps the player to the place <paramref name="intent"/> names, when every gate holds.</summary>
    public void TeleportToPoint(string playerId, TeleportToPointIntent intent)
    {
        var session = FindSessionByPlayerId(playerId);
        if (session is null || intent is null)
        {
            return;
        }

        Serve(session);
        string kind = intent.Kind ?? string.Empty;
        if (!TeleporterReady(session, kind))
        {
            return;
        }

        if (_world.Planet.Void)
        {
            Reject(session, "teleport", "@srv.tp.not_in_space"); // a station or a ship interior has no places to jump to
            return;
        }

        _markerJumpIsFree = false;
        if (!TryResolveTeleportPoint(session, intent, out var arrival, out string reason, out string refusal))
        {
            Reject(session, "teleport", refusal);
            return;
        }

        // A marker on ground the player never saw is a free point (the Quantum Teleporter's) — and costs like one.
        string priceKind = _markerJumpIsFree ? TeleportRules.KindCoords : kind;
        _markerJumpIsFree = false;
        var p = session.State;
        if (p.SuitEnergy < TeleportRules.EnergyCostOf(priceKind))
        {
            Reject(session, "teleport", "@srv.tp.no_energy");
            return;
        }

        p.SuitEnergy -= TeleportRules.EnergyCostOf(priceKind);
        _teleportCooldown[playerId] = TeleportRules.CooldownOf(priceKind);
        p.Position = arrival;

        // The far place's ground goes out ahead of the snap (#1449), which rides the RespawnNotice channel (#414 M7).
        StreamFootingNow(session, arrival);
        Send(session, new RespawnNotice { X = arrival.X, Y = arrival.Y, Z = arrival.Z, Reason = reason });
        SendPlayerState(session);
        UpdateAboard(session); // a place on a parked ship's deck flips the aboard state now, not on the next move
    }

    /// <summary>Finds the place and the spot to stand on, or the refusal token.</summary>
    private bool TryResolveTeleportPoint(PlayerSession session, TeleportToPointIntent intent, out Vector3f arrival,
        out string reason, out string refusal)
    {
        arrival = default;
        reason = "@srv.tp.done";
        refusal = "@srv.tp.bad_target";
        if (!float.IsFinite(intent.X) || !float.IsFinite(intent.Y) || !float.IsFinite(intent.Z))
        {
            return false;
        }

        string me = session.State.PlayerId;
        var cell = WorldConstants.CanonicalBlock(
            new Vector3i((int)Math.Floor(intent.X), (int)Math.Floor(intent.Y), (int)Math.Floor(intent.Z)), _world.Circumference);
        switch (intent.Kind)
        {
            case TeleportRules.KindBase:
                return TryBaseTeleportPoint(me, cell, out arrival, ref reason, ref refusal);
            case TeleportRules.KindBeam:
                return TryBeamTeleportPoint(me, cell, out arrival, ref reason, ref refusal);
            case TeleportRules.KindBeacon:
                return TryBeaconTeleportPoint(me, cell, out arrival, ref reason, ref refusal);
            case TeleportRules.KindMarker:
                return TryMarkerTeleportPoint(session, intent.TargetId, out arrival, ref reason, ref refusal);
            case TeleportRules.KindCoords:
            case TeleportRules.KindWaypoint:
                return TryFreeTeleportPoint(intent.X, intent.Z, out arrival, ref refusal);
            default:
                return false;
        }
    }

    private bool TryBaseTeleportPoint(string me, Vector3i cell, out Vector3f arrival, ref string reason, ref string refusal)
    {
        arrival = default;
        var b = _bases.FirstOrDefault(x => x.Planet == _world.LocationId && x.Cell == cell);
        if (b is null)
        {
            refusal = "@srv.tp.place_gone";
            return false;
        }

        if (!OwnOrAllied(b.OwnerId, me) && !IsAdminPlayer(me))
        {
            refusal = "@srv.tp.place_not_allied";
            return false;
        }

        arrival = ArrivalBeside(b.Cell);
        reason = PlaceReason(b.Name);
        return true;
    }

    private bool TryBeamTeleportPoint(string me, Vector3i cell, out Vector3f arrival, ref string reason, ref string refusal)
    {
        arrival = default;
        var pad = _beams.FirstOrDefault(x => x.Cell == cell);
        if (pad is null)
        {
            refusal = "@srv.tp.place_gone";
            return false;
        }

        if (!CanUseBeam(pad, me))
        {
            refusal = "@srv.tp.place_not_allied";
            return false;
        }

        arrival = BeamArrivalSpot(pad.Cell);
        reason = PlaceReason(pad.Name);
        return true;
    }

    private bool TryBeaconTeleportPoint(string me, Vector3i cell, out Vector3f arrival, ref string reason, ref string refusal)
    {
        arrival = default;
        var beacon = _beacons.FirstOrDefault(x => WorldConstants.CanonicalBlock(x.Pos.ToBlock(), _world.Circumference) == cell);
        if (beacon is null)
        {
            refusal = "@srv.tp.place_gone";
            return false;
        }

        if (!OwnOrAllied(beacon.OwnerId, me) && !IsAdminPlayer(me))
        {
            refusal = "@srv.tp.place_not_allied";
            return false;
        }

        arrival = ArrivalBeside(cell);
        reason = PlaceReason(beacon.Label);
        return true;
    }

    private bool TryMarkerTeleportPoint(PlayerSession session, string id, out Vector3f arrival, ref string reason, ref string refusal)
    {
        arrival = default;
        if (_markerSystem.FindJumpMarker(session, id ?? string.Empty) is not { } marker)
        {
            refusal = "@srv.tp.place_gone";
            return false;
        }

        int x = (int)Math.Floor(WorldConstants.WrapX((double)marker.X, _world.Circumference));
        int z = (int)Math.Floor(WorldConstants.WrapZ((double)marker.Z, _world.Circumference));
        if (!ExploredByPlayer(session, x, z))
        {
            if (!session.State.Inventory.Has(TeleportRules.QuantumTeleporterItem, 1))
            {
                refusal = "@srv.tp.marker_unexplored"; // a marker set on unknown ground is a free point: the Quantum Teleporter's
                return false;
            }

            _markerJumpIsFree = true;
        }

        arrival = SurfaceSpot(x, z);
        if (UnsafeArrival(arrival, x, z))
        {
            refusal = "@srv.tp.unsafe";
            return false;
        }

        reason = PlaceReason(marker.Label);
        return true;
    }

    /// <summary>Whether the planet map's explored cell of this column is set for the player (#1113 — the server marks it as
    /// the terrain streams in).</summary>
    private bool ExploredByPlayer(PlayerSession session, int x, int z)
    {
        var chunk = WorldConstants.CanonicalChunk(WorldConstants.WorldToChunk(new Vector3i(x, 0, z)), _world.Circumference);
        int index = ExploredMap.CellIndex(chunk.X, chunk.Z, _world.Circumference);
        return session.State.ExploredCells.TryGetValue(_world.LocationId, out var map) && ExploredMap.GetBit(map, index);
    }

    /// <summary>A spot nobody may be put down on: above the build height, or standing on lava.</summary>
    private bool UnsafeArrival(Vector3f arrival, int x, int z)
        => arrival.Y > MaxBuildY - 2 || IsLavaCell(x, (int)Math.Floor(arrival.Y) - 1, z);

    /// <summary>A free point (#2995): wrapped to the canonical range, the surface found by the server, never over lava.</summary>
    private bool TryFreeTeleportPoint(float wx, float wz, out Vector3f arrival, ref string refusal)
    {
        int x = (int)Math.Floor(WorldConstants.WrapX((double)wx, _world.Circumference));
        int z = (int)Math.Floor(WorldConstants.WrapZ((double)wz, _world.Circumference));
        arrival = SurfaceSpot(x, z);
        if (UnsafeArrival(arrival, x, z))
        {
            refusal = "@srv.tp.unsafe";
            return false;
        }

        return true;
    }

    /// <summary>The arrival toast: "Teleported to {name}", or the plain one for a place without a name.</summary>
    private static string PlaceReason(string? name)
        => string.IsNullOrWhiteSpace(name) ? "@srv.tp.done" : "@srv.tp.to:" + name;

    private bool IsAdminPlayer(string playerId) => FindSessionByPlayerId(playerId)?.State.IsAdmin ?? false;

    private bool IsLavaCell(int x, int y, int z)
    {
        ushort lava = _content.GetBlock("lava")?.NumericId.Value ?? 0;
        return lava != 0 && _world.GetBlock(new Vector3i(x, y, z)).Value == lava;
    }

    /// <summary>
    /// A standing spot beside a block (a base core, a beacon): the eight neighbouring columns are tried, each from one block
    /// above the block's level down to two below, for a floor with two clear cells over it — so the arrival never lands in
    /// the block or in a wall next to it. With nothing free around it the player stands on top of it (the beam pad's rule).
    /// </summary>
    /// <summary>Set by the marker resolution for the jump being served: the marker lies on unexplored ground, so the jump is a
    /// Quantum free jump and pays its price (single-threaded tick; reset right after use).</summary>
    private bool _markerJumpIsFree;

    private static readonly (int Dx, int Dz)[] ArrivalNeighbours = { (0, 1), (1, 0), (0, -1), (-1, 0), (1, 1), (1, -1), (-1, -1), (-1, 1) };

    private Vector3f ArrivalBeside(Vector3i block)
    {
        foreach (var (dx, dz) in ArrivalNeighbours)
        {
            int x = block.X + dx, z = block.Z + dz;
            for (int y = block.Y + 1; y >= block.Y - 2; y--)
            {
                if (IsBodyBlockingCell(x, y - 1, z) && !IsBodyBlockingCell(x, y, z) && !IsBodyBlockingCell(x, y + 1, z))
                {
                    // canonical, so a core at the seam never puts the player on the far side of the number line
                    return new Vector3f(WorldConstants.WrapX(x, _world.Circumference) + 0.5f, y, WorldConstants.WrapZ(z, _world.Circumference) + 0.5f);
                }
            }
        }

        return BeamArrivalSpot(block);
    }

    /// <summary>Test seam: a teleporter jump to a place, as the intent would ask for it.</summary>
    public void TeleportToPointForTest(PlayerSession session, string kind, float x, float y, float z, string targetId = "")
        => TeleportToPoint(session.State.PlayerId, new TeleportToPointIntent { Kind = kind, X = x, Y = y, Z = z, TargetId = targetId });

    /// <summary>Test seam: marks the planet map's explored cell of a column for a player, as streaming the terrain would.</summary>
    public void MarkExploredForTest(PlayerSession session, int x, int z)
    {
        Serve(session);
        MarkExploredCell(session, WorldConstants.CanonicalChunk(WorldConstants.WorldToChunk(new Vector3i(x, 0, z)), _world.Circumference));
    }

    /// <summary>Test seam: resets a player's teleporter cooldown.</summary>
    public void ResetTeleportCooldownForTest(string playerId) => _teleportCooldown[playerId] = 0;
}
