// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The boat (#1215): the water <c>Kind</c> of the speeder system. Everything that is the same — deploy from the
/// gadget path, board, dismount, pack up, hull damage, persistence in the player blob, reconciliation — stays in
/// <c>GameServerSpeeders.cs</c> and is keyed on the item's <see cref="Shared.Definitions.VehicleProperties"/>.
/// This file holds only what differs: the boat needs a water column to launch into, it never drains a cell, and
/// a driver who reports it "ashore" for long enough is set back onto the last water pose (the client owns the
/// floating physics and grounds it itself; the server rule is the lenient safety net, exactly like the
/// speeder's collision reports are the client's and the hull damage is the server's).
/// </summary>
public sealed partial class GameServer
{
    // --- balance ---
    private const float BoatFloatAboveWaterline = 0.3f;   // deploy Y above the top water cell
    private const int BoatLaunchMinAhead = 2;             // scan for water this many blocks in front of the player …
    private const int BoatLaunchMaxAhead = 5;             // … up to this far, …
    private const int BoatLaunchSideways = 3;             // … and this far to either side (the issue's ±3)
    private const int BoatLaunchDown = 6;                 // waterline may sit this far below the player's feet (a bank)
    private const int BoatLaunchUp = 3;                   // … or this far above (standing in a dip beside a pool)
    private const int BoatHeadroom = 2;                   // air cells required above the waterline
    private const int BoatAshoreReports = 30;             // consecutive "no water under the hull" move reports (~3 s @10 Hz) before the snap-back
    private const int BoatHullProbeDepth = 2;             // water counts if it sits within this many cells below the driver's feet

    /// <summary>Vehicle kind of a persisted record — empty (pre-#1215 save) reads as a speeder.</summary>
    private static string VehicleKind(DeployedSpeeder rec) => string.IsNullOrEmpty(rec.Kind) ? "speeder" : rec.Kind;

    private static bool IsBoat(DeployedSpeeder rec) => VehicleKind(rec) == "boat";

    /// <summary>The item that packing this vehicle up returns (the item that deployed it).</summary>
    private static string VehicleItem(DeployedSpeeder rec) => IsBoat(rec) ? "boat" : "speeder";

    /// <summary>A server message key in the vehicle's own wording: <c>@srv.boat.&lt;key&gt;</c> for a boat,
    /// <c>@srv.speeder.&lt;key&gt;</c> otherwise ("That isn't your boat" instead of "…speeder").</summary>
    private static string VehicleMsg(DeployedSpeeder rec, string key) => (IsBoat(rec) ? "@srv.boat." : "@srv.speeder.") + key;

    /// <summary>True when the start planet is a water world (the <c>ocean</c> type: water abundance ≥ 1), where a
    /// fresh pilot is handed a boat on first join — a shore with no way across it would be a bad first hour.</summary>
    private bool StartBodyIsWaterWorld
        => (_content.GetPlanet(_meta.DefaultPlanetType)?.WaterAbundance ?? 0.0) >= 1.0;

    private bool IsWaterCell(Vector3i cell) => _waterId != 0 && _world.GetBlock(cell).Value == _waterId;

    /// <summary>Finds the waterline in the column (x, z): the highest water cell within the search window that has
    /// <see cref="BoatHeadroom"/> air cells above it. Returns the Y of that cell's top face.</summary>
    private bool TryFindWaterline(int x, int z, int nearY, out float waterlineY)
    {
        for (int y = nearY + BoatLaunchUp; y >= nearY - BoatLaunchDown; y--)
        {
            if (!IsWaterCell(new Vector3i(x, y, z)))
            {
                continue;
            }

            bool clear = true;
            for (int h = 1; h <= BoatHeadroom && clear; h++)
            {
                clear = _world.GetBlock(new Vector3i(x, y + h, z)).IsAir;
            }

            if (clear)
            {
                waterlineY = y + 1f;
                return true;
            }
        }

        waterlineY = 0f;
        return false;
    }

    /// <summary>Picks the launch spot for a boat: the nearest water column in front of the player (2–5 blocks
    /// ahead, up to 3 to either side) whose waterline lies within reach of the player's feet. Nearest-ahead wins,
    /// then the smallest sideways offset, so the boat appears where the player is looking.</summary>
    private bool TryFindBoatLaunch(PlayerState p, out Vector3f at)
    {
        double yawRad = p.Yaw * Math.PI / 180.0;
        double fx = Math.Sin(yawRad);
        double fz = Math.Cos(yawRad);
        double rx = fz;   // right-hand perpendicular
        double rz = -fx;
        int circ = _world.Circumference;
        int feetY = (int)Math.Floor(p.Position.Y);

        for (int ahead = BoatLaunchMinAhead; ahead <= BoatLaunchMaxAhead; ahead++)
        {
            for (int side = 0; side <= BoatLaunchSideways; side++)
            {
                foreach (int sign in side == 0 ? new[] { 0 } : new[] { -1, 1 })
                {
                    double wx = p.Position.X + fx * ahead + rx * side * sign;
                    double wz = p.Position.Z + fz * ahead + rz * side * sign;
                    int cx = (int)Math.Floor(WorldConstants.WrapX(wx, circ));
                    int cz = (int)Math.Floor(WorldConstants.WrapZ(wz, circ));
                    if (TryFindWaterline(cx, cz, feetY, out float waterline))
                    {
                        at = new Vector3f(cx + 0.5f, waterline + BoatFloatAboveWaterline, cz + 0.5f);
                        return true;
                    }
                }
            }
        }

        at = default;
        return false;
    }

    /// <summary>Whether a driven boat at this pose still has water under its hull — judged ONLY in loaded chunks.
    /// An unloaded cell reads as air, and far water is the #987 LOD case, so "not loaded" must mean "don't judge",
    /// never "ashore". Returns null when it cannot tell.</summary>
    private bool? BoatOverWater(Vector3f pos)
    {
        int x = (int)Math.Floor(pos.X);
        int z = (int)Math.Floor(pos.Z);
        int feet = (int)Math.Floor(pos.Y);
        var probe = WorldConstants.CanonicalBlock(new Vector3i(x, feet, z), _world.Circumference);
        if (!_world.IsChunkLoaded(WorldConstants.WorldToChunk(probe)))
        {
            return null;
        }

        for (int dy = 0; dy <= BoatHullProbeDepth; dy++)
        {
            if (_world.GetBlockIfLoaded(new Vector3i(x, feet - dy, z)).Value == _waterId && _waterId != 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Per move report while driving a boat: remember the last pose that had water under the hull; after
    /// <see cref="BoatAshoreReports"/> consecutive dry reports set the driver back onto it. Lenient by design — the
    /// client grounds a beached boat itself (speed decays, it nudges back); this only catches a client that keeps
    /// driving over land anyway, and it never damages anything.</summary>
    private void TickBoatAshore(PlayerSession session, ServerSpeeder s)
    {
        var p = session.State;
        bool? wet = BoatOverWater(p.Position);
        if (wet != false)
        {
            // Only a pose JUDGED wet is a good pose to come back to. An unloaded probe chunk is "don't judge":
            // it neither counts as ashore nor becomes the snap-back target (#1301) — the previous water pose
            // stands, or none is known yet and the rule stays disarmed until the boat is seen floating.
            if (wet == true)
            {
                s.LastWaterPos = p.Position;
                s.HasWaterPos = true;
            }

            s.AshoreReports = 0;
            return;
        }

        if (!s.HasWaterPos)
        {
            return; // no water pose known yet (boarded a boat that was itself beached) — nothing sane to snap to
        }

        if (++s.AshoreReports < BoatAshoreReports)
        {
            return;
        }

        s.AshoreReports = 0;
        p.Position = s.LastWaterPos;
        s.Rec.X = p.Position.X;
        s.Rec.Y = p.Position.Y;
        s.Rec.Z = p.Position.Z;
        s.LastDriverPos = p.Position;
        session.AwaitingSpawnAdopt = true; // #865: the client keeps streaming its ashore pose for a beat — it must not drag the boat back
        SendPlayerState(session);
        // The plain "the server moved you" the client already understands (it snaps the body, #beam); no FX.
        Send(session, new BeamTeleported { X = p.Position.X, Y = p.Position.Y, Z = p.Position.Z });
        Send(session, new ServerMessage { Text = "@srv.boat.aground" });
    }

    // ---------------------------------------------------------------------------------------------
    // Test hooks.
    // ---------------------------------------------------------------------------------------------

    /// <summary>Test/util: the persisted vehicle kind of a live vehicle ("speeder" / "boat"), or "" if unknown.</summary>
    public string VehicleKindForTest(string vehicleId)
        => _speeders.Find(v => v.Id == vehicleId) is { } s ? VehicleKind(s.Rec) : string.Empty;

    /// <summary>Test/util: the last pose a live boat was judged to be floating at (the ashore snap-back target),
    /// and whether one is known at all.</summary>
    public (bool Known, Vector3f Pos) BoatWaterPosForTest(string vehicleId)
        => _speeders.Find(v => v.Id == vehicleId) is { } s ? (s.HasWaterPos, s.LastWaterPos) : (false, default);

    /// <summary>Test/util: whether the start planet hands out a boat on first join.</summary>
    public bool StartBodyIsWaterWorldForTest => StartBodyIsWaterWorld;
}
