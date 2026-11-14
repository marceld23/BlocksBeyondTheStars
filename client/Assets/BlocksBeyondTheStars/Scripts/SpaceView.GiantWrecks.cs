// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client.Core;
using BlocksBeyondTheStars.Networking.Messages;
using UnityEngine;
using NVector3 = System.Numerics.Vector3;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// #3615: a giant ship wreck in flight is a derelict of its own family — every derelict rule already holds for it — but it
    /// is up to 256 blocks a side, so its centre lies far inside the hull. The ranges and the approach that the flight view
    /// used to take from a station's centre are measured from the hull's box for it instead (<see cref="HullApproach"/>; the
    /// server measures scan range and VEGA's approach the same way): the autopilot and a chart waypoint stop in front of the
    /// hull instead of flying into it, the scanner reaches it from beside its hull, and the target brackets frame its size.
    /// Only while the flight view holds the wreck's voxel hull; before that, the centre as for every station.
    /// </summary>
    public sealed partial class SpaceView
    {
        /// <summary>How far in front of a giant wreck's hull box the autopilot and a waypoint stop the ship.</summary>
        private const float GiantHullStandOff = 24f;

        /// <summary>The arrival radius around that point — the ship ends within 40 of the hull, well inside the board range.</summary>
        private const float GiantHullArrive = 16f;

        /// <summary>Where the autopilot or a chart waypoint flies the ship for a station contact: in front of a known hangar
        /// mouth (#1917), in front of a giant wreck's hull box (#3615), else to the centre into dock range.</summary>
        private void StationFlightTarget(NetCombatEntity e, Vector3 from, out Vector3 target, out float arriveSq)
        {
            if (StationApproachPoint(e.Id, DockApproachStandOff) is { } approach)
            {
                target = approach; // #1917: in front of the hangar mouth
                arriveSq = DockApproachArrive * DockApproachArrive;
                return;
            }

            if (GiantWreckText.IsGiantWreck(e.Id) && TryHeldHullBox(e.Id, out var centre, out var half))
            {
                target = FromNumerics(HullApproach.StandOffPoint(ToNumerics(centre), ToNumerics(half), ToNumerics(from), GiantHullStandOff));
                arriveSq = GiantHullArrive * GiantHullArrive;
                return;
            }

            target = new Vector3(e.X, e.Y, e.Z);
            arriveSq = BoardRange * BoardRange * 0.64f; // well inside dock range before handing over
        }

        /// <summary>The distance the scanner's range is checked against: to a giant wreck's hull box (the server's rule), else
        /// the <paramref name="centreDistance"/> every other contact uses.</summary>
        private float ScanRangeDistance(NetCombatEntity e, Vector3 shipPos, float centreDistance) => HullDistance(e, shipPos, centreDistance);

        /// <summary>How far a contact is from a root-local point: to a giant wreck's hull box while the flight view holds its
        /// hull, else <paramref name="centreDistance"/> (the radar's readout and the scanner's range read this).</summary>
        public float HullDistance(NetCombatEntity e, Vector3 from, float centreDistance)
            => e != null && GiantWreckText.IsGiantWreck(e.Id) && TryHeldHullBox(e.Id, out var centre, out var half)
                ? HullApproach.BoxDistance(ToNumerics(centre), ToNumerics(half), ToNumerics(from))
                : centreDistance;

        /// <summary>A giant wreck's size for the brackets and the aim cone: the half diagonal of its hull's box, or
        /// <paramref name="fallback"/> while its hull is not here (and for every other station).</summary>
        private float GiantWreckRadius(NetCombatEntity e, float fallback)
            => GiantWreckText.IsGiantWreck(e.Id) && TryHeldHullBox(e.Id, out _, out var half) ? Mathf.Max(fallback, half.magnitude) : fallback;

        /// <summary>The box of a station hull the flight view holds (root-local centre and half size).</summary>
        private bool TryHeldHullBox(string id, out Vector3 centre, out Vector3 half)
        {
            if (id != null && _structs.TryGetValue(id, out var vs) && vs.IsStation && vs.Cells != null && vs.Cells.Count > 0)
            {
                centre = vs.Pos;
                half = vs.Half;
                return true;
            }

            centre = half = Vector3.zero;
            return false;
        }

        // ToNumerics lives with the rock keep-out (SpaceView.Structures.cs); this is its way back.
        private static Vector3 FromNumerics(NVector3 v) => new Vector3(v.X, v.Y, v.Z);
    }
}
