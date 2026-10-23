// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;
using UnityEngine.UI;
using static BlocksBeyondTheStars.Client.ModelKit;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The spacewalk (EVA): suit flight with voxel collision, building and mining on the own ship, asteroids and
    /// stations, the door ghost and the aim highlight, and climbing back aboard or docking from the suit.
    /// </summary>
    public sealed partial class SpaceView
    {
        /// <summary>Server reports the player is now on an EVA (stepped out the ship's airlock): set up the
        /// first-person 6-DOF suit float next to the parked ship. The ship stays where it is.</summary>
        private void BeginEvaMode()
        {
            if (_ship == null)
            {
                return;
            }

            var rot = _ship.transform.localRotation;
            _evaYaw = _yaw;
            _evaPitch = _pitch;

            // Start the suit clearly OUTSIDE + behind the hull, facing it, so you actually see your ship. The
            // voxel ship is full-size (1:1) on an EVA, so the offset scales with its length (the old fixed -3.4
            // was tuned for the small cube model and left you spawning inside the big voxel hull → "no ship").
            // #2703: measured from the hull box's stern face, so the suit starts inside the board reach on every
            // design (the centre-based 6 left the starter ship's suit 6 behind its stern, past the 5 of reach).
            float backZ = _shipCells != null && _shipCells.Count > 0 ? _shipHalf.z + 4f : 3.4f;
            _evaPos = _ship.transform.localPosition + rot * new Vector3(0f, -1f, -backZ);

            // #2636: fell out of a derelict through its hull — the suit starts where it left the hull (the server sent the
            // spot with this flight), looking the way the player looked aboard; the ship waits further off.
            if (Game.TakeSpaceEvaStart(out var suitAt, out var suitYaw))
            {
                _evaPos = suitAt;
                _evaYaw = suitYaw;
                _evaPitch = 0f;
            }

            if (_evaPos.magnitude > FlightClamp)
            {
                _evaPos = _evaPos.normalized * FlightClamp;
            }

            _eva = true;
            ClearTargetLock(sound: false); // #2277: the target lock belongs to the helm
            ClientAudio.Instance?.Cue("scan_ping"); // a soft suit blip as the airlock cycles
        }

        /// <summary>Free-floating suit control while on EVA: mouse look + WASD + Space/Ctrl up/down (6-DOF).
        /// Float up to the parked ship (or a station) and press E to board — no take-off animation.</summary>
        private void UpdateEva()
        {
            if (_ship == null)
            {
                return;
            }

            // Report the suit's pose so the other players in this instance see us floating (the server tags
            // it as an EVA from our InEva state). Sent BEFORE the menu early-out below — behind an open panel
            // this is the client's only payload, and the server drops sessions silent for 90 s (#964/#1008).
            _moveSendTimer -= Time.deltaTime;
            if (_moveSendTimer <= 0f)
            {
                _moveSendTimer = 0.08f;
                Game.Network?.SendShipMove(_evaPos, _evaYaw);
            }

            // #2724: whom K docks with and whether the docked friend's ship floats within reach — measured from the suit.
            UpdateVisit(_evaPos);
            _interactGoesOver = false;
            _interactAsksPartner = false;

            // Menu, destruction prompt, chat box or any focused text field: hold still (#1858). #2724: so does a visit on
            // its way to the friend's ship behind the veil.
            if (!FlightInputAllowed || _visitWait >= 0f)
            {
                return;
            }

            // Free-look.
            _evaYaw += InputMap.LookX() * LookSpeed;
            _evaPitch = Mathf.Clamp(_evaPitch - InputMap.LookY() * LookSpeed, -89f, 89f);
            var rot = Quaternion.Euler(_evaPitch, _evaYaw, 0f);

            // 6-DOF thrust: WASD in the look plane, Space/Ctrl for world up/down.
            float fwd = InputMap.MoveY();
            float strafe = InputMap.MoveX();
            float lift = (InputMap.JumpHeld() ? 1f : 0f)
                       - ((InputMap.CrouchHeld()) ? 1f : 0f);
            Vector3 dir = rot * (Vector3.forward * fwd + Vector3.right * strafe) + Vector3.up * lift;
            Vector3 delta = dir * (EvaSpeed * Time.deltaTime);

            // Don't float through the ship or an asteroid. With voxel structures present (S2/S3) collide against
            // their block grids — so you can drift right up to a hull/rock — otherwise fall back to a sphere keep-out.
            if (HasVoxelStructures())
            {
                _evaPos = ResolveEvaVoxelMove(_evaPos, delta);
            }
            else
            {
                _evaPos += delta;
                Vector3 toShip = _evaPos - _ship.transform.localPosition;
                float shipDist = toShip.magnitude;
                if (shipDist < ShipKeepOut && shipDist > 0.0001f)
                {
                    _evaPos = _ship.transform.localPosition + toShip / shipDist * ShipKeepOut;
                }
            }

            // Stay inside the flight bounds and don't drift into a body — slide along its keep-out shell.
            if (_evaPos.magnitude > FlightClamp)
            {
                _evaPos = _evaPos.normalized * FlightClamp;
            }

            foreach (var ob in _keepOut)
            {
                Vector3 d = _evaPos - ob.Pos;
                float dist = d.magnitude;
                if (dist < ob.Radius && dist > 0.0001f)
                {
                    _evaPos = ob.Pos + d / dist * ob.Radius;
                }
            }

            // Hotbar select (number keys + scroll) so you can pick the block/tool to build with out here.
            UpdateEvaHotbarSelect();

            // Aim at the ship's voxel grid and build/mine on it (S2).
            UpdateEvaBuild();

            // item 20 S4: deploy a station core (B) to start a player-built station.
            if (!Game.MenuOpen && InputMap.Down(InputAction.EvaDeployStation))
            {
                Game.Network?.SendDeployStationCore();
            }

            // Board targets: the parked ship, or the nearest station in range — EvaBoardChoice picks which one E boards.
            _evaShipDist = EvaShipHullDistance(_evaPos);
            _evaNearShip = _evaShipDist <= EvaShipBoardReach;

            _nearStationId = null;
            _nearStationSq = float.MaxValue;
            var space = Game.Space;
            if (space != null)
            {
                float best = BoardRange;
                foreach (var e in space.Entities)
                {
                    if (e.Kind != "SpaceStation")
                    {
                        continue;
                    }

                    float dist = StationDistance(e, _evaPos); // #1917: to the hull, like the server
                    if (dist < best)
                    {
                        best = dist;
                        _nearStationId = e.Id;
                        _nearStationName = e.Name;
                        _boardTargetPos = new Vector3(e.X, e.Y, e.Z);
                        _nearStationSq = dist * dist;
                    }
                }
            }

            var evaChoice = EvaBoardChoice();
            _interactGoesOver = evaChoice == EvaBoardTarget.PartnerShip; // #2724: the Actions list offers "go over" then
            if (InputMap.Down(InputAction.Interact))
            {
                switch (evaChoice)
                {
                    case EvaBoardTarget.Station:
                        DockStationFromEva();
                        break;
                    case EvaBoardTarget.Ship:
                        BoardShipFromEva();
                        break;
                    case EvaBoardTarget.PartnerShip:
                        BeginVisit(); // #2724: into the docked friend's ship
                        break;
                }
            }
        }

        private enum EvaBoardTarget { None, Ship, Station, PartnerShip }

        /// <summary>
        /// What E boards on a spacewalk — the key and the HUD prompt both ask here, so they never disagree (#2703). The
        /// crosshair decides first: on the near station's cells (a door #2607, a wall, the deck behind a breach #2636) E
        /// docks that station, and the server sees the suit beside the door and lets the player in on its inside; on the
        /// own hull it boards the ship. Otherwise the own ship wins whenever it is in reach, a station only when it is not.
        /// A station used to win whenever its hull box was nearer than the ship's CENTRE, so at the own hatch beside a
        /// station (a derelict's box is large) E docked the station instead of taking the player home.
        /// #2724: docked with a friend whose ship floats parked within reach, that ship is the third target — after the own
        /// ship, and before a station unless the station's hull is nearer.
        /// </summary>
        private EvaBoardTarget EvaBoardChoice()
        {
            if (_evaHasAim && _evaAimStructId != null)
            {
                if (_nearStationId != null && _evaAimStructId == _nearStationId)
                {
                    return EvaBoardTarget.Station;
                }

                if (_evaAimStructId == _shipStructureId)
                {
                    return EvaBoardTarget.Ship; // the aim reaches a little past the board reach — looking at the hull is enough
                }
            }

            if (_evaNearShip)
            {
                return EvaBoardTarget.Ship;
            }

            if (_visit.Prompt == BlocksBeyondTheStars.Client.Core.ShipVisitPrompt.GoOver
                && (_nearStationId == null || _visit.Distance * _visit.Distance <= _nearStationSq))
            {
                return EvaBoardTarget.PartnerShip;
            }

            return _nearStationId != null ? EvaBoardTarget.Station : EvaBoardTarget.None;
        }

        /// <summary>
        /// What E does at the helm — the cruise twin of <see cref="EvaBoardChoice"/> (#2934). The key (UpdateCruise, right
        /// after it sets the visit flags) and the board prompt (LateUpdate) both ask here, each from the fields as they stand
        /// at that moment, so the prompt never offers what the key would not do. The rule is
        /// <see cref="BlocksBeyondTheStars.Client.Core.CruiseInteractRules.Choose"/>.
        /// </summary>
        private BlocksBeyondTheStars.Client.Core.CruiseInteractTarget CruiseInteractChoice()
            => BlocksBeyondTheStars.Client.Core.CruiseInteractRules.Choose(_nearWormholeId != null, _interactGoesOver,
                _interactAsksPartner, _nearStationId != null, _nearStationSq, _landTargetId != null, _landTargetSq,
                _visit.Prompt == BlocksBeyondTheStars.Client.Core.ShipVisitPrompt.PartnerFlying);

        /// <summary>#2703: the suit's distance to the own ship's hull — to its voxel box in the ship's design frame (so the
        /// ship's heading counts), the way a station is measured to its box (#1917). The cube fallback ship has no cells: it
        /// is measured to its keep-out shell.</summary>
        private float EvaShipHullDistance(Vector3 suit)
        {
            if (_shipCells == null || _shipCells.Count == 0)
            {
                return Mathf.Max(0f, (_ship.transform.localPosition - suit).magnitude - ShipKeepOut);
            }

            Vector3 d = ToDesign(_ship.transform, _shipCentre, suit) - _shipCentre;
            return new Vector3(
                Mathf.Max(0f, Mathf.Abs(d.x) - _shipHalf.x),
                Mathf.Max(0f, Mathf.Abs(d.y) - _shipHalf.y),
                Mathf.Max(0f, Mathf.Abs(d.z) - _shipHalf.z)).magnitude;
        }

        // ---------------- item 20 S2/S3: free-space build/mine + voxel collision (ship + asteroids) ----------------

        /// <summary>Resolves a structure id to its transform + voxel grid (the own ship, or an asteroid body), or
        /// returns null if it isn't present/built.</summary>
        private Transform StructTransform(string id, out Dictionary<Vector3i, BlockId> cells, out Vector3 centre)
        {
            if (id == _shipStructureId && _shipCells != null && _ship != null)
            {
                cells = _shipCells; centre = _shipCentre; return _ship.transform;
            }

            if (_structs.TryGetValue(id, out var vs) && vs.Root != null)
            {
                cells = vs.Cells; centre = vs.Centre; return vs.Root.transform;
            }

            cells = null; centre = Vector3.zero; return null;
        }

        /// <summary>Root-local point → a structure's design-grid coords (axis-aligned with its voxels), and back.</summary>
        private Vector3 ToDesign(Transform t, Vector3 centre, Vector3 rootPoint)
            => t.InverseTransformPoint(_root.transform.TransformPoint(rootPoint)) + centre;

        private Vector3 FromDesign(Transform t, Vector3 centre, Vector3 designPoint)
            => _root.transform.InverseTransformPoint(t.TransformPoint(designPoint - centre));

        private static bool CellsBlock(Dictionary<Vector3i, BlockId> cells, Vector3 designPos)
        {
            int x0 = Mathf.FloorToInt(designPos.x - SuitRadius), x1 = Mathf.FloorToInt(designPos.x + SuitRadius);
            int y0 = Mathf.FloorToInt(designPos.y - SuitRadius), y1 = Mathf.FloorToInt(designPos.y + SuitRadius);
            int z0 = Mathf.FloorToInt(designPos.z - SuitRadius), z1 = Mathf.FloorToInt(designPos.z + SuitRadius);
            for (int x = x0; x <= x1; x++)
            for (int y = y0; y <= y1; y++)
            for (int z = z0; z <= z1; z++)
            {
                if (cells.ContainsKey(new Vector3i(x, y, z)))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>True if a suit-sized sphere at this root-local point overlaps a solid cell of ANY structure.</summary>
        private bool SuitBlockedWorld(Vector3 rootPos)
        {
            if (_shipCells != null && _shipCells.Count > 0 && _ship != null
                && CellsBlock(_shipCells, ToDesign(_ship.transform, _shipCentre, rootPos)))
            {
                return true;
            }

            foreach (var vs in _structs.Values)
            {
                if (vs.Root != null && vs.Cells != null && vs.Cells.Count > 0
                    && CellsBlock(vs.Cells, ToDesign(vs.Root.transform, vs.Centre, rootPos)))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>True if any voxel structure (ship or asteroid) is present to collide/build against.</summary>
        private bool HasVoxelStructures()
            => (_shipCells != null && _shipCells.Count > 0) || _structs.Count > 0;

        /// <summary>Moves the suit by <paramref name="delta"/> (root-local) but stops it passing through any
        /// structure: resolves each world axis so it slides along the voxels instead of penetrating.</summary>
        private Vector3 ResolveEvaVoxelMove(Vector3 evaPos, Vector3 delta)
        {
            Vector3 p = evaPos;
            var tryX = new Vector3(p.x + delta.x, p.y, p.z);
            if (!SuitBlockedWorld(tryX)) { p.x = tryX.x; }
            var tryY = new Vector3(p.x, p.y + delta.y, p.z);
            if (!SuitBlockedWorld(tryY)) { p.y = tryY.y; }
            var tryZ = new Vector3(p.x, p.y, p.z + delta.z);
            if (!SuitBlockedWorld(tryZ)) { p.z = tryZ.z; }
            return p;
        }

        /// <summary>Ray-marches one structure's voxel grid from the suit's eye (a design-space DDA mirroring the
        /// on-foot AimBlock). Returns the hit cell, the empty cell before it, and the hit distance. #2607: the ray also
        /// stops on <paramref name="doors"/> (the cells the structure's built doors fill — air in the grid), so a door hung
        /// in space can be aimed at and picked up again with the mining button.</summary>
        private bool RayMarchCells(Transform t, Dictionary<Vector3i, BlockId> cells, Vector3 centre,
            Vector3 rootOrigin, Vector3 rootDir, out Vector3Int hitCell, out Vector3Int placeCell, out float dist,
            HashSet<Vector3i> doors = null)
        {
            hitCell = default; placeCell = default; dist = 0f;
            Vector3 o = ToDesign(t, centre, rootOrigin);
            Vector3 dir = t.InverseTransformDirection(_root.transform.TransformDirection(rootDir)).normalized;

            int x = Mathf.FloorToInt(o.x), y = Mathf.FloorToInt(o.y), z = Mathf.FloorToInt(o.z);
            int px = x, py = y, pz = z;
            int sx = dir.x >= 0 ? 1 : -1, sy = dir.y >= 0 ? 1 : -1, sz = dir.z >= 0 ? 1 : -1;
            float invx = Mathf.Abs(dir.x) > 1e-6f ? 1f / Mathf.Abs(dir.x) : float.PositiveInfinity;
            float invy = Mathf.Abs(dir.y) > 1e-6f ? 1f / Mathf.Abs(dir.y) : float.PositiveInfinity;
            float invz = Mathf.Abs(dir.z) > 1e-6f ? 1f / Mathf.Abs(dir.z) : float.PositiveInfinity;
            float tMaxX = float.IsInfinity(invx) ? float.PositiveInfinity : (dir.x > 0 ? (x + 1 - o.x) : (o.x - x)) * invx;
            float tMaxY = float.IsInfinity(invy) ? float.PositiveInfinity : (dir.y > 0 ? (y + 1 - o.y) : (o.y - y)) * invy;
            float tMaxZ = float.IsInfinity(invz) ? float.PositiveInfinity : (dir.z > 0 ? (z + 1 - o.z) : (o.z - z)) * invz;

            float tt = 0f;
            for (int i = 0; i < 80 && tt <= EvaReach; i++)
            {
                var cell = new Vector3i(x, y, z);
                if (cells.ContainsKey(cell) || (doors != null && doors.Contains(cell)))
                {
                    hitCell = new Vector3Int(x, y, z);
                    placeCell = new Vector3Int(px, py, pz);
                    dist = tt;
                    return true;
                }

                px = x; py = y; pz = z;
                if (tMaxX <= tMaxY && tMaxX <= tMaxZ) { x += sx; tt = tMaxX; tMaxX += invx; }
                else if (tMaxY <= tMaxZ) { y += sy; tt = tMaxY; tMaxY += invy; }
                else { z += sz; tt = tMaxZ; tMaxZ += invz; }
            }

            return false;
        }

        /// <summary>Aims at the nearest voxel structure (ship or asteroid) the suit is looking at.</summary>
        private bool AimVoxel(out string structId, out Vector3Int hitCell, out Vector3Int placeCell)
        {
            structId = null; hitCell = default; placeCell = default;
            if (_root == null)
            {
                return false;
            }

            Vector3 rootDir = Quaternion.Euler(_evaPitch, _evaYaw, 0f) * Vector3.forward;
            float bestT = float.MaxValue;
            string bestId = null;
            Vector3Int bestHit = default, bestPlace = default;

            if (_shipCells != null && _ship != null && _shipCells.Count > 0
                && RayMarchCells(_ship.transform, _shipCells, _shipCentre, _evaPos, rootDir, out var sh, out var sp, out var sd, _shipDoorCells)
                && sd < bestT)
            {
                bestT = sd; bestId = _shipStructureId; bestHit = sh; bestPlace = sp;
            }

            foreach (var kv in _structs)
            {
                var vs = kv.Value;
                if (vs.Root == null || vs.Cells == null || vs.Cells.Count == 0)
                {
                    continue;
                }

                if (RayMarchCells(vs.Root.transform, vs.Cells, vs.Centre, _evaPos, rootDir, out var hc, out var pc, out var d, vs.DoorCells)
                    && d < bestT)
                {
                    bestT = d; bestId = kv.Key; bestHit = hc; bestPlace = pc;
                }
            }

            if (bestId == null)
            {
                return false;
            }

            structId = bestId; hitCell = bestHit; placeCell = bestPlace;
            return true;
        }

        /// <summary>EVA aim + build/mine: highlights the targeted cell; LMB mines it (ship hull or asteroid ore),
        /// RMB places the held block in the empty cell before it. Server-authoritative — it validates + broadcasts.</summary>
        /// <summary>Lets the EVA player pick the active hotbar slot (1–9 keys or scroll), mirroring the on-foot
        /// controls — so they can choose which block/tool to build with while floating in space.</summary>
        private void UpdateEvaHotbarSelect()
        {
            const int slots = 9;
            for (int i = 0; i < slots; i++)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1 + i) || Input.GetKeyDown(KeyCode.Keypad1 + i))
                {
                    SelectEvaSlot(i);
                }
            }

            float scroll = InputMap.HotbarScroll();
            if (scroll > 0f) { SelectEvaSlot((Game.SelectedHotbarSlot + slots - 1) % slots); }
            else if (scroll < 0f) { SelectEvaSlot((Game.SelectedHotbarSlot + 1) % slots); }
        }

        private void SelectEvaSlot(int slot)
        {
            if (slot == Game.SelectedHotbarSlot) { return; }
            Game.Network?.SendSelectHotbar(slot); // #2525: the network client keeps the slot Game.SelectedHotbarSlot reads
        }

        private void UpdateEvaBuild()
        {
            _evaHasAim = AimVoxel(out _evaAimStructId, out _evaAimHit, out _evaAimPlace);
            UpdateAimHighlight();
            UpdateEvaDoorGhost();

            if (Game.MenuOpen || !_evaHasAim || string.IsNullOrEmpty(_evaAimStructId))
            {
                return;
            }

            if (InputMap.PrimaryHeld())
            {
                // #685: mining is hold-to-repeat at the on-foot drill pace — the server accumulates hardness
                // progress per hit on asteroid ore, so a single click no longer pops a block instantly.
                if (Time.time >= _nextEvaMine)
                {
                    _nextEvaMine = Time.time + EvaMinePace;
                    Game.Network?.SendStructureEdit(_evaAimStructId, _evaAimHit.x, _evaAimHit.y, _evaAimHit.z, mine: true);
                }
            }
            else if (InputMap.SecondaryDown())
            {
                string item = Game.ItemInSlot(Game.SelectedHotbarSlot) ?? string.Empty;
                var def = string.IsNullOrEmpty(item) ? null : Game.Content?.GetItem(item);
                if (def != null && !string.IsNullOrEmpty(def.PlacesBlock))
                {
                    Game.Network?.SendStructureEdit(_evaAimStructId, _evaAimPlace.x, _evaAimPlace.y, _evaAimPlace.z, mine: false, item);
                }
            }
        }

        /// <summary>#2119: while a door block is held on a spacewalk and the suit aims at the own ship, the door it will
        /// hang shows in the cell — turned the way the server will turn it (jambs decide, else it faces the suit), so a
        /// door never looks like a block before it is placed. #2607: the same at a player station the suit may build on
        /// (its owner's, an ally's) — a station's grid is the flight frame, so the suit's own heading turns the door — and
        /// an energy door's preview shows its blue field, the way the closed door looks out here.</summary>
        private void UpdateEvaDoorGhost()
        {
            string doorKind = null;
            Transform t = null;
            Dictionary<Vector3i, BlockId> cells = null;
            Vector3 centre = Vector3.zero;
            float yawInGrid = 0f;
            if (_evaHasAim && !Game.MenuOpen)
            {
                string item = Game.ItemInSlot(Game.SelectedHotbarSlot);
                string placed = string.IsNullOrEmpty(item) ? null : Game.Content?.GetItem(item)?.PlacesBlock;
                doorKind = DoorBlocks.IsDoorBlock(placed) ? DoorBlocks.KindForBlock(placed) : null;
                if (doorKind != null && _evaAimStructId == _shipStructureId && _shipCells != null && _ship != null)
                {
                    t = _ship.transform;
                    cells = _shipCells;
                    centre = _shipCentre;
                    yawInGrid = _evaYaw - _yaw;
                }
                else if (doorKind != null && _evaAimStructId != null && _structs.TryGetValue(_evaAimStructId, out var vs)
                         && vs.IsStation && vs.Root != null && vs.Cells != null && MayBuildOnStation(vs))
                {
                    t = vs.Root.transform;
                    cells = vs.Cells;
                    centre = vs.Centre;
                    yawInGrid = _evaYaw;
                }
            }

            if (doorKind == null || t == null)
            {
                _evaDoorGhost?.Hide();
                return;
            }

            var cell = _evaAimPlace;
            var grid = cells;
            bool axisX = DoorProbe.AxisForPlacedDoor(
                (x, y, z) => grid.TryGetValue(new Vector3i(x, y, z), out var b) && !b.IsAir,
                cell.x, cell.y, cell.z, yawInGrid);
            _evaDoorGhost ??= new PlacementGhost();
            _evaDoorGhost.ShowDoorAt(t.TransformPoint(new Vector3(cell.x + 0.5f, cell.y, cell.z + 0.5f) - centre), t.rotation, doorKind, axisX,
                withField: SpaceDoors.ShowsFieldInSpace(doorKind));
        }

        /// <summary>#2607: whether the local player may build onto this station from a spacewalk — its owner or an ally,
        /// the server's own rule (an ownerless, generated station hull takes no build at all).</summary>
        private bool MayBuildOnStation(VoxStruct vs)
        {
            var allies = Game.Alliances?.Allies;
            var allyIds = new List<string>();
            if (allies != null)
            {
                foreach (var a in allies)
                {
                    allyIds.Add(a.PartnerId);
                }
            }

            return SpaceDoors.MayBuildOnStation(vs.OwnerId, Game.LocalPlayerId, allyIds);
        }

        /// <summary>Shows a small glowing marker on the cell the suit is aiming at (build/mine feedback).</summary>
        private void UpdateAimHighlight()
        {
            Transform t = null;
            Vector3 centre = Vector3.zero;
            if (_evaHasAim)
            {
                t = StructTransform(_evaAimStructId, out _, out centre);
            }

            if (!_evaHasAim || _root == null || t == null)
            {
                if (_aimHighlight != null) { _aimHighlight.SetActive(false); }
                return;
            }

            if (_aimHighlight == null)
            {
                _aimHighlightMat = Unlit(new Color(0.3f, 0.95f, 1f));
                _aimHighlight = Cube("AimMarker", _root.transform, Vector3.zero, Vector3.one * 0.22f, _aimHighlightMat);
            }

            // #685: warm the marker toward orange as the server reports mining progress on the aimed cell, so
            // multi-hit ore visibly works toward breaking. A report older than a second is stale (the aim moved
            // on, or the cell broke) and falls back to the idle cyan.
            float frac = 0f;
            if (_evaMineProgressStructId == _evaAimStructId && _evaMineProgressCell == _evaAimHit
                && Time.time - _evaMineProgressAt < 1f)
            {
                frac = _evaMineProgressFraction;
            }

            if (_aimHighlightMat != null)
            {
                _aimHighlightMat.color = Color.Lerp(new Color(0.3f, 0.95f, 1f), new Color(1f, 0.55f, 0.15f), frac);
            }

            _aimHighlight.SetActive(true);
            var cellCentre = new Vector3(_evaAimHit.x + 0.5f, _evaAimHit.y + 0.5f, _evaAimHit.z + 0.5f);
            _aimHighlight.transform.localPosition = FromDesign(t, centre, cellCentre);
        }

        /// <summary>Climbs back in through the airlock from an EVA — into the ship's walkable interior (on
        /// foot), the way you came out. From there the helm takes you back to flying.</summary>
        private void BoardShipFromEva()
        {
            _eva = false;
            _enteringInterior = true;       // tear the flight view down at once (no landing descent)
            Game.BeginWorldTransition();     // veil immediately so the flight view doesn't flash first (B34)
            Game.Network?.SendEnterShip();  // server ends the EVA and loads the ship interior
            ClientAudio.Instance?.Cue("scan_ping");
        }

        /// <summary>Docks the nearby station directly from an EVA: ends the spacewalk and reuses the normal
        /// station-board teardown (the dock-approach is already "done" since you floated over on foot).</summary>
        private void DockStationFromEva()
        {
            // Keep InEva set on the server until BoardStation runs, so it knows the ship stayed floating
            // (undocking returns you to the float). The server clears InEva as it docks you.
            _eva = false;
            _phase = Phase.Boarding;
            _boardPath.Clear();
            _boardDuration = BoardDuration;
            _seq = BoardDuration;     // skip the fly-in: you're already at the hull on foot
            _boardSent = false;
            _boardWait = 0f;
            _boardTargetId = _nearStationId;
            _boardStartPos = _ship.transform.localPosition;
        }
    }
}
