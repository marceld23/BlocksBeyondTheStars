// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Docking: the docking visit (#2724: dock, undock, go over to the friend's ship) and the dock approach that flies
    /// the ship into a station's hangar mouth (#1917).
    /// </summary>
    public sealed partial class SpaceView
    {
        // ---------------- #2724: the docking visit (dock, undock, go over to the friend's ship) ----------------

        /// <summary>Reads this frame's docking picture from <paramref name="from"/> (the ship at the helm, the suit on a
        /// spacewalk): K's nearest pilot and the visit prompt (<see cref="BlocksBeyondTheStars.Client.Core.ShipVisitRules"/>).
        /// Allocation-free — it runs every cruise and EVA frame.</summary>
        private void UpdateVisit(Vector3 from)
        {
            var players = Game.Space?.Players;
            var dock = Game.Dock;
            _dockPilot = Game.Docked ? null
                : BlocksBeyondTheStars.Client.Core.ShipVisitRules.NearestDockPilot(players, from.x, from.y, from.z);
            _visit = BlocksBeyondTheStars.Client.Core.ShipVisitRules.Evaluate(Game.VisitsSupported, dock != null && dock.Docked,
                dock?.Partner, players, from.x, from.y, from.z);
        }

        /// <summary>Goes over to the docked partner's ship (#2724): the same descent-less step as walking into the own ship
        /// (veil at once, B34), but the server places us in the partner's interior as a guest. A refusal lifts the veil again
        /// (<see cref="UpdatePendingVisit"/>). The suit stays a suit until the server says otherwise, so a refused visit from a
        /// spacewalk floats on where it was.</summary>
        private void BeginVisit()
        {
            if (Game.Network == null || string.IsNullOrEmpty(_visit.PartnerId))
            {
                return;
            }

            _enteringInterior = true;     // the server closes this flight: tear the view down at once (no landing descent)
            _visitWait = 0f;
            _visitRefusalsSeen = Game.VisitRefusals;
            Game.BeginWorldTransition();  // veil immediately — the flight view must not flash before the friend's ship (B34)
            Game.Network.SendVisitShip(_visit.PartnerId);
            ClientAudio.Instance?.Cue("scan_ping");
        }

        /// <summary>Follows a visit on its way (#2724): the server's refusal (or no answer within the overlay's confirm time)
        /// hands the controls back and lifts the veil; an accepted visit closes the flight, and the teardown takes over.</summary>
        private void UpdatePendingVisit()
        {
            if (_visitWait < 0f || !Game.InSpace)
            {
                return;
            }

            _visitWait += Time.deltaTime;
            bool refused = Game.VisitRefusals != _visitRefusalsSeen;
            if (refused || _visitWait > 2.5f)
            {
                _visitWait = -1f;
                _enteringInterior = false; // a later landing plays its descent again
                if (refused)
                {
                    Game.CancelWorldTransition(); // the reason is on the toast — show it at once, not after the timeout
                }
            }
        }

        private void ResetVisit()
        {
            _visit = default;
            _dockPilot = null;
            _interactGoesOver = false;
            _interactAsksPartner = false;
            _visitWait = -1f;
        }

        /// <summary>
        /// #1917: plans the dock approach into a station's hangar — out to the point in front of the mouth, then in. When
        /// the hull's box stands between the ship and that point, the path climbs over it (or dives under it, when the ship
        /// is below the station). A station without a known mouth keeps the classic short fly-in (an empty path).
        /// </summary>
        private void PlanDockApproach()
        {
            _lastDockedStationId = _boardTargetId; // #3142: undocking slides out of this station's hangar
            _boardTargetName = _nearStationName ?? string.Empty;
            _dockDoorsClosing = false;
            _dockCamFrom = Camera != null ? Camera.transform.localPosition : Vector3.zero;
            _dockCamFromRot = Camera != null ? Camera.transform.localRotation : Quaternion.identity;
            _boardPath.Clear();
            _boardDuration = BoardDuration;
            if (_ship == null || _boardTargetId == null || !_structs.TryGetValue(_boardTargetId, out var vs) || !vs.HasDock
                || StationApproachPoint(_boardTargetId, DockApproachStandOff) is not { } approach)
            {
                return;
            }

            Vector3 start = _ship.transform.localPosition;
            Vector3 mouth = vs.Dock + vs.DockOut * DockMouthStandOff;
            var box = vs.Half + Vector3.one * 4f;
            _boardPath.Add(start);
            if (SegmentHitsBox(start, approach, vs.Pos, box))
            {
                bool inFootprint = Mathf.Abs(start.x - vs.Pos.x) <= box.x && Mathf.Abs(start.z - vs.Pos.z) <= box.z;
                bool under = inFootprint && start.y < vs.Pos.y;
                float pass = under ? vs.Pos.y - box.y - 8f : vs.Pos.y + box.y + 8f;
                _boardPath.Add(new Vector3(start.x, under ? Mathf.Min(start.y, pass) : Mathf.Max(start.y, pass), start.z));
                _boardPath.Add(new Vector3(approach.x, pass, approach.z));
            }

            _boardPath.Add(approach);
            _boardPath.Add(mouth);

            float length = 0f;
            for (int i = 1; i < _boardPath.Count; i++)
            {
                length += Vector3.Distance(_boardPath[i - 1], _boardPath[i]);
            }

            _boardDuration = Mathf.Clamp(length / DockFlySpeed, BoardDuration, DockApproachMaxSeconds);
        }

        /// <summary>True when the segment a→b passes through the box of half size <paramref name="half"/> around <paramref name="centre"/>.</summary>
        private static bool SegmentHitsBox(Vector3 a, Vector3 b, Vector3 centre, Vector3 half)
        {
            Vector3 min = centre - half, max = centre + half, d = b - a;
            float t0 = 0f, t1 = 1f;
            for (int axis = 0; axis < 3; axis++)
            {
                if (Mathf.Abs(d[axis]) < 1e-5f)
                {
                    if (a[axis] < min[axis] || a[axis] > max[axis])
                    {
                        return false;
                    }

                    continue;
                }

                float inv = 1f / d[axis];
                float near = (min[axis] - a[axis]) * inv, far = (max[axis] - a[axis]) * inv;
                if (near > far)
                {
                    (near, far) = (far, near);
                }

                t0 = Mathf.Max(t0, near);
                t1 = Mathf.Min(t1, far);
                if (t0 > t1)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>The point <paramref name="distance"/> along the planned dock path, and the direction of travel there.</summary>
        private Vector3 AlongBoardPath(float distance, out Vector3 heading)
        {
            heading = Vector3.forward;
            for (int i = 1; i < _boardPath.Count; i++)
            {
                Vector3 a = _boardPath[i - 1], b = _boardPath[i];
                float seg = Vector3.Distance(a, b);
                if (seg > 0.0001f)
                {
                    heading = (b - a) / seg;
                }

                if (distance <= seg || i == _boardPath.Count - 1)
                {
                    return seg > 0.0001f ? Vector3.Lerp(a, b, Mathf.Clamp01(distance / seg)) : b;
                }

                distance -= seg;
            }

            return _boardPath.Count > 0 ? _boardPath[_boardPath.Count - 1] : Vector3.zero;
        }

        /// <summary>#1917: moves the ship from <paramref name="from"/> toward <paramref name="to"/> but not into a station
        /// hull: each axis is tried on its own, so the ship slides along the hull instead of stopping dead. A ship already
        /// overlapping a hull (one that just arrived around it) is let through, so it can always fly out.</summary>
        private Vector3 ResolveStationHullMove(Vector3 from, Vector3 to)
        {
            if (_structs.Count == 0 || ShipBlockedByStation(from))
            {
                return to;
            }

            Vector3 p = from;
            if (!ShipBlockedByStation(new Vector3(to.x, p.y, p.z))) { p.x = to.x; }
            if (!ShipBlockedByStation(new Vector3(p.x, to.y, p.z))) { p.y = to.y; }
            if (!ShipBlockedByStation(new Vector3(p.x, p.y, to.z))) { p.z = to.z; }
            return p;
        }

        /// <summary>True if a ship-sized sphere at this root-local point overlaps a block of a station hull.</summary>
        private bool ShipBlockedByStation(Vector3 rootPos)
        {
            foreach (var vs in _structs.Values)
            {
                if (!vs.IsStation || vs.Root == null || vs.Cells == null || vs.Cells.Count == 0)
                {
                    continue;
                }

                Vector3 d = rootPos - vs.Pos;
                float reach = ShipHullRadius + 1f;
                if (Mathf.Abs(d.x) > vs.Half.x + reach || Mathf.Abs(d.y) > vs.Half.y + reach || Mathf.Abs(d.z) > vs.Half.z + reach)
                {
                    continue;
                }

                Vector3 p = ToDesign(vs.Root.transform, vs.Centre, rootPos);
                int x0 = Mathf.FloorToInt(p.x - ShipHullRadius), x1 = Mathf.FloorToInt(p.x + ShipHullRadius);
                int y0 = Mathf.FloorToInt(p.y - ShipHullRadius), y1 = Mathf.FloorToInt(p.y + ShipHullRadius);
                int z0 = Mathf.FloorToInt(p.z - ShipHullRadius), z1 = Mathf.FloorToInt(p.z + ShipHullRadius);
                for (int x = x0; x <= x1; x++)
                for (int y = y0; y <= y1; y++)
                for (int z = z0; z <= z1; z++)
                {
                    if (vs.Cells.ContainsKey(new Vector3i(x, y, z)))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>Flies the ship in to dock with the station + fades out, then sends the board intent. With a known
        /// hangar mouth (#1917) the ship follows the planned approach and noses into the hangar. A safety timeout reverts
        /// to cruise if the server never confirms (so it can't hang on black).</summary>
        private void UpdateBoarding()
        {
            _seq += Time.deltaTime;
            float t = Mathf.Clamp01(_seq / _boardDuration);
            float ease = 1f - (1f - t) * (1f - t);
            if (_ship != null && _boardPath.Count >= 2)
            {
                float total = 0f;
                for (int i = 1; i < _boardPath.Count; i++)
                {
                    total += Vector3.Distance(_boardPath[i - 1], _boardPath[i]);
                }

                float smooth = t * t * (3f - 2f * t);
                _ship.transform.localPosition = AlongBoardPath(smooth * total, out var heading);
                if (heading.sqrMagnitude > 0.001f)
                {
                    var look = Quaternion.LookRotation(heading, Vector3.up);
                    _ship.transform.localRotation = Quaternion.Slerp(_ship.transform.localRotation, look, Time.deltaTime * 5f);
                }
            }
            else if (_ship != null)
            {
                var dock = Vector3.Lerp(_boardTargetPos, _boardStartPos, 0.14f); // stop just short of the hull
                _ship.transform.localPosition = Vector3.Lerp(_boardStartPos, dock, ease);
                var dir = _boardTargetPos - _ship.transform.localPosition;
                if (dir.sqrMagnitude > 0.001f)
                {
                    var look = Quaternion.LookRotation(dir.normalized, Vector3.up);
                    _ship.transform.localRotation = Quaternion.Slerp(_ship.transform.localRotation, look, Time.deltaTime * 4f);
                }
            }

            DockGuideLights();
            FinishDockApproach();
        }

        /// <summary>The end of the dock approach: the hangar doors close over the mouth (#3142) and, once they are shut, the
        /// board intent goes out — without a transition director the old veil and the intent at once. A refusal (or no
        /// answer) hands the flight back and opens the doors again.</summary>
        private void FinishDockApproach()
        {
            if (_seq >= _boardDuration && !_boardSent)
            {
                if (Transitions == null)
                {
                    _boardSent = true;
                    Game.BeginWorldTransition(); // veil now (the approach has played) so the station doesn't flash (B34)
                    Game.Network?.SendBoardStation(_boardTargetId);
                }
                else if (!_dockDoorsClosing)
                {
                    _dockDoorsClosing = true; // #3142: the hangar doors close over the mouth; the station loads behind them
                    Transitions.CoverDocking(_boardTargetName, 0.9f);
                }
                else if (Transitions.CoverShut)
                {
                    _boardSent = true;
                    Game.Network?.SendBoardStation(_boardTargetId);
                }
            }

            if (_boardSent)
            {
                _boardWait += Time.deltaTime;
                // The server didn't confirm (e.g. rejected) — recover instead of hanging. Behind the doors (#3142) a cold
                // station may take a moment to build on the server, so they wait longer before opening on the flight.
                if (_boardWait > (Transitions != null ? 8f : 2.5f))
                {
                    _phase = Phase.Cruise;
                    _seq = 0f;
                    _boardSent = false;
                    Transitions?.CancelArrival();
                }
            }
        }

        private string _boardTargetName = string.Empty;
        private bool _dockDoorsClosing;
        private Vector3 _dockCamFrom;
        private Quaternion _dockCamFromRot = Quaternion.identity;
        private float _dockLightClock;

        /// <summary>#3142: the dock approach seen from beside the hangar mouth — the camera glides there from the chase and
        /// watches the ship line up and nose in. False (the usual chase) for a station without a known mouth.</summary>
        private bool PlaceDockCamera()
        {
            if (_ship == null || _boardTargetId == null || !_structs.TryGetValue(_boardTargetId, out var vs) || !vs.HasDock)
            {
                return false;
            }

            var outward = vs.DockOut.sqrMagnitude > 1e-4f ? vs.DockOut.normalized : Vector3.forward;
            var side = Vector3.Cross(Vector3.up, outward);
            side = side.sqrMagnitude > 1e-3f ? side.normalized : Vector3.right;
            var spot = vs.Dock + outward * 18f + side * 9f + Vector3.up * 3.5f;
            var sp = _ship.transform.localPosition;
            float k = Smooth(0f, 1.4f, _seq);
            var pos = Vector3.Lerp(_dockCamFrom, spot, k);
            var look = sp - pos;
            Camera.transform.localPosition = pos;
            Camera.transform.localRotation = Quaternion.Slerp(_dockCamFromRot, look.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(look, Vector3.up) : _dockCamFromRot, k);
            return true;
        }

        /// <summary>#3142: guide lights racing along the approach into the hangar mouth, in pairs either side.</summary>
        private void DockGuideLights()
        {
            if (_root == null || _boardTargetId == null || !_structs.TryGetValue(_boardTargetId, out var vs) || !vs.HasDock)
            {
                return;
            }

            _dockLightClock += Time.deltaTime;
            if (_dockLightClock < 0.09f)
            {
                return;
            }

            _dockLightClock = 0f;
            var outward = vs.DockOut.sqrMagnitude > 1e-4f ? vs.DockOut.normalized : Vector3.forward;
            var side = Vector3.Cross(Vector3.up, outward);
            side = side.sqrMagnitude > 1e-3f ? side.normalized : Vector3.right;
            int step = Mathf.FloorToInt(Time.time * 11f) % 6;
            var along = vs.Dock + outward * (15f - step * 3f) + Vector3.down * 2.2f;
            var color = new Color(0.35f, 1f, 0.85f);
            FxKit.Flash(_root.transform.TransformPoint(along + side * 4f), color, 1.1f, 0.22f);
            FxKit.Flash(_root.transform.TransformPoint(along - side * 4f), color, 1.1f, 0.22f);
        }
    }
}
