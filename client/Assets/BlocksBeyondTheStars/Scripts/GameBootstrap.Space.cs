// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.IO;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Localization;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// GameBootstrap's space flight (#2912): the flight and combat state, ship designs, landed ships, owned ships,
    /// wreck and ship repair, trade, docking and visits.
    /// </summary>
    public sealed partial class GameBootstrap
    {
        /// <summary>An optional space-flight nav waypoint (#597), set on the system chart while flying:
        /// either the id of a landable body / space station (snap-to-marker; <see cref="SpaceView"/>
        /// resolves it to a live position) or a free scene-space point. The space radar shows it and the
        /// VEGA autopilot steers to it. Cleared with the world-scoped state on travel — like the surface
        /// waypoint, its coordinates mean nothing in the next world (#592's lesson).</summary>
        public string SpaceWaypointId;
        public Vector3? SpaceWaypointPos;

        // Space flight & combat (M25).
        public ShipCombatStatus ShipCombat { get; private set; }
        public SpaceState Space { get; private set; }       // current space instance (null when not flying)
        public SpaceShipDesign ShipDesign { get; private set; } // item 20 S1: own ship as a voxel structure (flight view)

        // OTHER players' voxel ship designs (Kind "ship_remote", Id "ship:<playerId>"), cached per pilot —
        // the flight view + the landing/launch FX render their REAL ships instead of generic silhouettes.
        private readonly System.Collections.Generic.Dictionary<string, SpaceShipDesign> _remoteShipDesigns = new();

        /// <summary>The cached voxel design of another player's ship, or null when none arrived yet.</summary>
        public SpaceShipDesign RemoteShipDesignFor(string playerId)
            => !string.IsNullOrEmpty(playerId) && _remoteShipDesigns.TryGetValue(playerId, out var d) ? d : null;

        /// <summary>Pending NPC-trader warp flashes (a localized hyperspace burst). The flight view drains this
        /// each frame and plays each one — so everyone in the instance sees traders arrive/depart.</summary>
        public readonly System.Collections.Generic.List<SpaceWarpFx> PendingWarpFx = new();

        /// <summary>Ships parked on the current world as placed structure objects (ship-as-object), keyed by
        /// structure id ("ship:&lt;playerId&gt;"). <see cref="LandedShipView"/> renders them; aiming and the
        /// weather column scans query them — the hull is NOT part of the world block grid.</summary>
        public readonly System.Collections.Generic.Dictionary<string, LandedShipModel> LandedShips = new();

        /// <summary>Raised whenever a parked ship is placed, removed or one of its cells changes.</summary>
        public event System.Action LandedShipsChanged;

        /// <summary>The parked-ship SOLID cell at a world position, or air. Outputs the ship + the
        /// structure-local cell so on-foot aiming can mine/place via StructureEditIntent.</summary>
        public BlockId LandedShipBlockAt(int x, int y, int z, out LandedShipModel ship, out BlocksBeyondTheStars.Shared.Geometry.Vector3i local)
        {
            foreach (var s in LandedShips.Values)
            {
                int dx = WorldConstants.WrapDeltaX(x - s.Origin.X, Circumference);
                int dy = y - s.Origin.Y, dz = z - s.Origin.Z;
                if (dx < -3 || dy < -1 || dz < -3 || dx > s.Width + 3 || dy > s.Height + 2 || dz > s.Length + 3)
                {
                    continue; // outside the bounds (+ a silhouette margin for wings/nozzles)
                }

                var l = new BlocksBeyondTheStars.Shared.Geometry.Vector3i(dx, dy, dz);
                var b = s.Get(l);
                if (!b.IsAir)
                {
                    ship = s;
                    local = l;
                    return b;
                }
            }

            ship = null;
            local = default;
            return BlockId.Air;
        }

        /// <summary>The parked ship whose interior BOUNDS contain a world cell (for routing block placement
        /// inside the ship to a structure edit), or null.</summary>
        public LandedShipModel LandedShipBoundsAt(int x, int y, int z, out BlocksBeyondTheStars.Shared.Geometry.Vector3i local)
        {
            foreach (var s in LandedShips.Values)
            {
                int dx = WorldConstants.WrapDeltaX(x - s.Origin.X, Circumference);
                int dy = y - s.Origin.Y, dz = z - s.Origin.Z;
                if (dx >= 0 && dx < s.Width && dy >= 0 && dy <= s.Height && dz >= 0 && dz < s.Length)
                {
                    local = new BlocksBeyondTheStars.Shared.Geometry.Vector3i(dx, dy, dz);
                    return s;
                }
            }

            local = default;
            return null;
        }

        /// <summary>True when a parked ship's bounding box roofs this column above the given height — a cheap
        /// cover test so weather treats ship roofs like solid cover without scanning cells.</summary>
        public bool LandedShipCovers(int x, int y, int z)
        {
            foreach (var s in LandedShips.Values)
            {
                int dx = WorldConstants.WrapDeltaX(x - s.Origin.X, Circumference);
                if (dx >= -2 && dx <= s.Width + 2 && z >= s.Origin.Z - 2 && z <= s.Origin.Z + s.Length + 2
                    && y < s.Origin.Y + s.Height)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>True when the cell lies inside a parked ship's bounding box. A parked ship is drawn as its own model,
        /// not as world voxels, so a capture's sight lines test it here (#2765).</summary>
        public bool LandedShipBodyAt(int x, int y, int z)
        {
            foreach (var s in LandedShips.Values)
            {
                int dx = WorldConstants.WrapDeltaX(x - s.Origin.X, Circumference);
                if (dx >= 0 && dx < s.Width && z >= s.Origin.Z && z < s.Origin.Z + s.Length
                    && y >= s.Origin.Y && y < s.Origin.Y + s.Height)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>True when a parked ship's centre lies within <paramref name="halfAngle"/> degrees of the heading
        /// <paramref name="yawDeg"/> as seen from <paramref name="from"/>, and nearer than <paramref name="range"/> metres —
        /// it would stand in a capture still's frame (#2765).</summary>
        public bool LandedShipInView(Vector3 from, float yawDeg, float halfAngle, float range)
        {
            foreach (var s in LandedShips.Values)
            {
                float dx = WorldConstants.WrapDeltaX((int)(s.Origin.X + s.Width / 2f - from.x), Circumference);
                float dz = s.Origin.Z + s.Length / 2f - from.z;
                if (dx * dx + dz * dz > range * range)
                {
                    continue;
                }

                float bearing = Mathf.Atan2(dx, dz) * Mathf.Rad2Deg;
                if (Mathf.Abs(Mathf.DeltaAngle(yawDeg, bearing)) <= halfAngle)
                {
                    return true;
                }
            }

            return false;
        }

        public bool InSpace { get; private set; }
        public bool SpaceSkipLaunch { get; private set; }    // entered space already airborne (helm) → no take-off anim
        public bool SpaceAutomaticTransit { get; private set; } // #1614: this flight is an automatic landed-ship transit (take-off → [warp] → landing, hands-off)
        public string SpaceTransitDestination { get; private set; } = string.Empty; // #1614: …the body it lands on (empty: it ends in flight)
        public bool SpaceHasResume { get; private set; }     // #2118: this flight resumes where the ship floated (helm / airlock / undock to EVA)
        public Vector3 SpaceResumePos { get; private set; }  // …its position in the flight scene
        public float SpaceResumeYaw { get; private set; }    // …and its heading (degrees)

        // #2636: the suit's own start spot for this flight's EVA (fell out of a derelict's hull) — latched on entry, taken
        // once by the flight view when the spacewalk begins, so a later EVA in the same flight starts beside the ship again.
        private bool _spaceHasEvaStart;
        private Vector3 _spaceEvaStartPos;
        private float _spaceEvaStartYaw;

        /// <summary>#2636: hands out (and forgets) the suit's start spot the server sent with this flight, if any.</summary>
        public bool TakeSpaceEvaStart(out Vector3 pos, out float yaw)
        {
            pos = _spaceEvaStartPos;
            yaw = _spaceEvaStartYaw;
            bool has = _spaceHasEvaStart;
            _spaceHasEvaStart = false;
            return has;
        }

        /// <summary>The player's owned ships and which is active (M-ships).</summary>
        public NetOwnedShip[] OwnedShips { get; private set; } = System.Array.Empty<NetOwnedShip>();

        /// <summary>Repair progress of the wreck the player is standing in (null until a wreck reports it).</summary>
        public WreckRepairStatus Wreck { get; private set; }

        /// <summary>Own-ship repair readout (hull + missing hull cells), set when the cockpit reports it; null
        /// when the ship is fully intact.</summary>
        public ShipRepairStatus ShipRepair { get; private set; }

        /// <summary>The open player-to-player trade (both offers + ready states), or null when no trade is active.</summary>
        public TradeUpdate Trade { get; private set; }
        public bool TradeActive { get; private set; }

        /// <summary>A player who has asked to dock with us (awaiting our accept/decline), or empty.</summary>
        public string PendingDockFrom { get; set; } = string.Empty;

        /// <summary>A player who has asked to trade with us (awaiting our accept/decline), or empty (#981).</summary>
        public string PendingTradeFrom { get; set; } = string.Empty;

        /// <summary>Latest authoritative docking state (partner + docked flag), or null.</summary>
        public DockStatus Dock { get; private set; }

        /// <summary>True while docked with another player (<see cref="Dock"/>).</summary>
        public bool Docked => Dock != null && Dock.Docked;

        /// <summary>#2724: the server can do docking visits (<c>DockStatus.CanVisit</c>) — an older server never says so, and
        /// then no visit prompt shows (docking itself still works).</summary>
        public bool VisitsSupported => Dock != null && Dock.Docked && Dock.CanVisit;

        /// <summary>#2725: the player whose ship interior we walk in as a guest (<c>WorldReset.ShipOwnerId</c>); empty in our
        /// own interior and on every other world. Set on every world reset.</summary>
        public string VisitingHost { get; private set; } = string.Empty;

        /// <summary>#2725: true while we are a guest in a docked friend's ship — the host's stations are room decor for us,
        /// and "back to my ship" (an <c>EnterShipIntent</c>) takes us home.</summary>
        public bool Visiting => VisitingHost.Length > 0;

        /// <summary>#2724: bumped on every refused ship visit (<c>ActionRejected</c> with action "visit"), so the flight view
        /// lifts the veil it raised for the visit at once.</summary>
        public int VisitRefusals { get; private set; }

        /// <summary>Start's subscriptions for space flight, landed ships, repairs, trade and docking. Called once from
        /// <see cref="Start"/>, before the transport connects.</summary>
        private void WireSpace()
        {
            Network.ShipCombatStatusChanged += m => ShipCombat = m;
            Network.SpaceStateReceived += m =>
            {
                if (!InSpace)
                {
                    SpaceSkipLaunch = m.SkipLaunch; // latched on entry only (later updates don't re-trigger Enter)
                    SpaceHasResume = m.HasResumePose; // #2118: latched with it — the flight view places the ship there on Enter
                    SpaceResumePos = new Vector3(m.ResumeX, m.ResumeY, m.ResumeZ);
                    SpaceResumeYaw = m.ResumeYaw;
                    _spaceHasEvaStart = m.HasEvaStart; // #2636: …and the suit's own spot, when it fell out of a derelict
                    _spaceEvaStartPos = new Vector3(m.EvaStartX, m.EvaStartY, m.EvaStartZ);
                    _spaceEvaStartYaw = m.EvaStartYaw;
                    if (m.Hyperjump)
                    {
                        HyperjumpStarted?.Invoke(); // warp VFX as we arrive in flight in a new system
                    }
                    else if (m.Wormhole)
                    {
                        WormholeArrived?.Invoke(); // #2242: through a rift — the rift transit instead of the warp
                    }
                }

                // #1614: not latched — the server re-sends the state with the transit off when it ends in flight (a
                // refused landing), and the flight view hands the controls back the moment it reads that.
                SpaceAutomaticTransit = m.AutomaticTransit;
                SpaceTransitDestination = m.TransitDestinationBodyId ?? string.Empty;

                // #1565: an in-flight hyperjump lands nowhere, so no WorldReset renames the HUD — the flight
                // state names its system + anchor body itself. Same composition as OnWorldReset.
                if (!string.IsNullOrEmpty(m.BodyName))
                {
                    LocationName = string.IsNullOrEmpty(m.SystemName) ? m.BodyName : $"{m.SystemName} · {m.BodyName}";
                }

                Space = m;
                InSpace = true;
            };
            // item 20 S1/S3: voxel structures for the flight view. Only the player's OWN ship drives the _ship
            // mesh (Game.ShipDesign); asteroid bodies (Kind != "ship") are handled directly by SpaceView.
            Network.SpaceShipDesignReceived += m =>
            {
                if (m.Kind == "ship" || string.IsNullOrEmpty(m.Kind))
                {
                    ShipDesign = m;
                }
                else if (m.Kind == "ship_remote" && m.Id.StartsWith("ship:", System.StringComparison.Ordinal))
                {
                    _remoteShipDesigns[m.Id.Substring(5)] = m; // keyed by the owning player's id (incl. "npc:<id>" traders)
                }
            };
            // Peaceful NPC traders warping in/out — queue the flash for the flight view to play.
            Network.SpaceWarpReceived += m =>
            {
                if (!InSpace)
                {
                    return;
                }

                PendingWarpFx.Add(m);
                if (PendingWarpFx.Count > 24)
                {
                    PendingWarpFx.RemoveAt(0); // safety cap — never let a backlog grow
                }
            };
            // Ship-as-object: ships parked on this world arrive/leave as placed structure objects.
            Network.LandedShipReceived += m =>
            {
                if (m.Removed)
                {
                    LandedShips.Remove(m.StructureId);
                }
                else
                {
                    var ship = new LandedShipModel
                    {
                        StructureId = m.StructureId,
                        OwnerId = m.PlayerId,
                        Origin = new Vector3i(m.OriginX, m.OriginY, m.OriginZ),
                        Hull = m.Hull,
                        Width = m.Width, Height = m.Height, Length = m.Length,
                    };
                    bool hasTint = m.Tint != null && m.Tint.Length == m.Block.Length;
                    bool hasGlow = m.Glow != null && m.Glow.Length == m.Block.Length;
                    bool hasShape = m.Shape != null && m.Shape.Length == m.Block.Length;
                    for (int i = 0; i < m.Block.Length; i++)
                    {
                        var cell = new Vector3i(m.X[i], m.Y[i], m.Z[i]);
                        ship.Cells[cell] = new BlockId(m.Block[i]);
                        int tint = hasTint ? m.Tint[i] : 0, glow = hasGlow ? m.Glow[i] : 0;
                        if (tint != 0 || glow != 0) ship.Mods[cell] = (tint, glow);
                        if (hasShape && m.Shape[i] != 0) ship.Shapes[cell] = m.Shape[i];
                    }

                    LandedShips[m.StructureId] = ship;
                }

                LandedShipsChanged?.Invoke();
            };
            // A parked ship's cell changed (on-foot furnishing / repairs) — update the model + re-mesh.
            Network.StructureBlockChangedReceived += m =>
            {
                if (LandedShips.TryGetValue(m.StructureId, out var ship))
                {
                    var cell = new Vector3i(m.X, m.Y, m.Z);
                    ship.Set(cell, new BlockId(m.Block));
                    if (m.Block != 0)
                    {
                        if (m.Tint != 0 || m.Glow != 0) ship.Mods[cell] = (m.Tint, m.Glow); else ship.Mods.Remove(cell);
                        if (m.Shape != 0) ship.Shapes[cell] = m.Shape; else ship.Shapes.Remove(cell);
                    }

                    ship.DirtyCells.Add(cell); // #2255: the view re-meshes only the chunks this cell reaches
                    LandedShipsChanged?.Invoke();
                }
            };
            Network.SpaceClosed += m => { InSpace = false; Space = null; LastMessage = ServerMessageText(m.Reason); };
            Network.StationBoardedReceived += m => { LastMessage = $"Boarded {m.Name}."; CurrentStationId = m.StationId ?? string.Empty; };
            Network.OwnedShipsReceived += m => OwnedShips = m.Ships;
            Network.WreckRepairStatusChanged += m => Wreck = m.Claimed ? null : m;
            Network.ShipRepairStatusChanged += m => ShipRepair = m.NeedsRepair ? m : null;
            // An incoming trade invitation (#981) needs an answer, so it is held as state — a toast alone
            // could not be acted on. It clears the moment the trade opens or the invitation is answered.
            // The toast comes with the ServerMessage the server sends alongside it.
            Network.TradeRequested += m => PendingTradeFrom = m.Requester;
            Network.TradeUpdated += m => { Trade = m; TradeActive = true; PendingTradeFrom = string.Empty; };
            Network.TradeClosedReceived += m =>
            {
                TradeActive = false;
                Trade = null;
                PendingTradeFrom = string.Empty;
                LastMessage = m.Completed ? (Localizer?.Get("ui.trade.complete") ?? "Trade complete.") : ServerMessageText(m.Reason);
            };
            Network.DockRequested += m => { PendingDockFrom = m.Requester; LastMessage = (Localizer?.Get("ui.dock.requests_toast") ?? "{0} requests docking.").Replace("{0}", m.Requester); };
            Network.DockStatusChanged += m =>
            {
                Dock = m;
                // #2720: the same "Docked with" prefix the docked hint uses (PlayerInteractions), localized — it was English.
                LastMessage = m.Docked ? $"{Localizer?.Get("ui.dock.docked") ?? "Docked with"} {m.Partner}" : ServerMessageText(m.Reason);
            };
        }
    }
}
