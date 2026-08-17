// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Player-built ships (#948/#949/#950): lay a <b>ship keel</b> anywhere on a planet surface, build a hull
/// onto it block by block (a construction-site structure OBJECT, the on-foot sibling of the EVA station
/// build), then <b>commission</b> it at its helm. Commissioning validates the build (size cap, exactly one
/// helm, at least one engine, a door, an airtight interior) and turns it into a normal fleet ship whose
/// flight stats derive from its geometry (mass vs. engines) instead of <c>data/ships.json</c>.
///
/// The hull's source of truth is <see cref="ShipState.BuiltCells"/> — a serialized structure-local cell
/// blob persisted with the fleet — so the landed object, the flight-view structure, the repair reference
/// and the derived stats are all rebuilt from the same save row. Door items become <b>door cells</b>
/// (an opening filled by the server-authoritative slide door), never solid cells: the client meshes and
/// collides every non-air structure cell, so a door block would wall the builder in.
/// </summary>
public sealed partial class GameServer
{
    private const string ShipCoreBlock = "ship_core";
    private const string ShipHelmBlock = "ship_helm";
    private const string ShipEngineBlock = "ship_engine";

    /// <summary>Hard size cap of a self-built hull (issue #950): 15×15 footprint, 15 high — it also keeps
    /// every buildable ship inside the landing-pad clearance (pad radius 8 → 17 blocks across).</summary>
    private const int CustomShipMaxFootprint = 15;
    private const int CustomShipMaxHeight = 15;

    /// <summary>Minimum solid cells to commission (the station analogue is 12; a ship needs a bit more
    /// hull to read as one).</summary>
    private const int CustomShipMinBlocks = 20;

    /// <summary>Minimum enclosed interior air cells: enough for the pilot to stand inside.</summary>
    private const int CustomShipMinInteriorAir = 4;

    /// <summary>LandedShips key of a player's construction site (their parked ship keeps the plain id).</summary>
    private static string ConstructionKey(string playerId) => "build:" + playerId;

    /// <summary>Structure id of a player's construction site ("ship:&lt;pid&gt;" stays the parked ship).</summary>
    private static string ConstructionStructureId(string playerId) => "shipyard:" + playerId;

    private static bool IsConstructionKey(string landedKey) => landedKey.StartsWith("build:", System.StringComparison.Ordinal);

    // ---------------- Fleet lookup ----------------

    /// <summary>The player's un-commissioned self-built ship (at most one exists at a time), or null.</summary>
    private (string Id, ShipState Ship)? UnderConstructionShip(PlayerSession session)
    {
        foreach (var (id, ship) in session.Ships)
        {
            if (ship.IsCustom && !ship.Commissioned)
            {
                return (id, ship);
            }
        }

        return null;
    }

    // ---------------- Cell blob helpers ----------------

    /// <summary>Parses a <see cref="ShipState.BuiltCells"/> blob into a mutable cell map (same
    /// "x:y:z:blockId;…" format the player stations persist).</summary>
    private static Dictionary<Vector3i, BlockId> ParseCustomCells(string blob)
    {
        var cells = new Dictionary<Vector3i, BlockId>();
        if (string.IsNullOrEmpty(blob))
        {
            return cells;
        }

        foreach (var cell in blob.Split(';'))
        {
            var p = cell.Split(':');
            if (p.Length == 4
                && int.TryParse(p[0], out var x) && int.TryParse(p[1], out var y)
                && int.TryParse(p[2], out var z) && ushort.TryParse(p[3], out var b))
            {
                cells[new Vector3i(x, y, z)] = new BlockId(b);
            }
        }

        return cells;
    }

    /// <summary>True when the block id is one of the door items (they become door CELLS, never solid cells).</summary>
    private bool IsDoorBlockId(BlockId block)
        => _content.BlockById(block) is { } def && IsDoorBlock(def.Key);

    private bool IsBlockId(BlockId block, string key)
        => _content.GetBlock(key) is { } def && def.NumericId.Value == block.Value;

    /// <summary>Extents of a cell map (min/max corners). False when empty.</summary>
    private static bool CellBounds(Dictionary<Vector3i, BlockId> cells, out Vector3i min, out Vector3i max)
    {
        min = max = default;
        bool any = false;
        foreach (var c in cells.Keys)
        {
            if (!any)
            {
                min = max = c;
                any = true;
                continue;
            }

            min = new Vector3i(System.Math.Min(min.X, c.X), System.Math.Min(min.Y, c.Y), System.Math.Min(min.Z, c.Z));
            max = new Vector3i(System.Math.Max(max.X, c.X), System.Math.Max(max.Y, c.Y), System.Math.Max(max.Z, c.Z));
        }

        return any;
    }

    /// <summary>Re-anchors a cell map so its minimum corner is (0,0,0) and shifts the world origin by the
    /// same amount — building toward -X/-Z grows the hull without moving it in the world.</summary>
    private static Dictionary<Vector3i, BlockId> NormalizeCustomCells(Dictionary<Vector3i, BlockId> cells, ref Vector3i origin)
    {
        if (!CellBounds(cells, out var min, out _) || (min.X == 0 && min.Y == 0 && min.Z == 0))
        {
            return cells;
        }

        var shifted = new Dictionary<Vector3i, BlockId>(cells.Count);
        foreach (var (c, b) in cells)
        {
            shifted[new Vector3i(c.X - min.X, c.Y - min.Y, c.Z - min.Z)] = b;
        }

        origin = new Vector3i(origin.X + min.X, origin.Y + min.Y, origin.Z + min.Z);
        return shifted;
    }

    // ---------------- Structure building ----------------

    /// <summary>Builds a self-built ship's voxel structure from its persisted cell blob — the custom-ship
    /// sibling of <see cref="BuildShipStructureFrom"/>. Door entries become door cells (openings), the helm
    /// becomes the interaction anchor ("shipyard" to commission while under construction, "cockpit" once
    /// commissioned — the same star-map/travel station authored ships carry). No decorative passes: the
    /// player IS the designer, their lights and floors stay exactly as built.</summary>
    /// <param name="commissioned">Names the helm anchor: "shipyard" (commission prompt) vs "cockpit".</param>
    /// <param name="applyEdits">Apply the per-cell damage deltas on top of the blob (live commissioned
    /// structures). The repair reference passes false: it wants the pristine design.</param>
    private SpaceStructure BuildCustomShipStructure(string structureId, string ownerId, ShipState ship, bool commissioned, bool applyEdits = false)
    {
        var s = new SpaceStructure { Id = structureId, Kind = "ship", OwnerId = ownerId };
        var cells = ParseCustomCells(ship.BuiltCells);
        var zero = default(Vector3i);
        cells = NormalizeCustomCells(cells, ref zero); // defensive — edits keep the blob normalized

        foreach (var (pos, block) in cells)
        {
            if (_content.BlockById(block) is { } doorDef && IsDoorBlock(doorDef.Key))
            {
                // A server-authoritative door fills the opening — and it keeps the KIND of the door block
                // the player built in, so a wooden/hinge door still swings by hand with E (#1021).
                s.DoorCells.Add(pos);
                s.DoorKinds[pos] = DoorKindForBlock(doorDef.Key);
                continue;
            }

            s.Set(pos, block);
            if (IsBlockId(block, ShipHelmBlock))
            {
                s.StationCells.Add((commissioned ? "cockpit" : "shipyard", pos));
            }
        }

        if (CellBounds(cells, out _, out var max))
        {
            s.Width = max.X + 1;
            s.Height = max.Y + 1;
            s.Length = max.Z + 1;
        }

        // The blob IS the design: snapshot it as the repair baseline, then (for the live structure) apply
        // the per-cell damage deltas on top (EVA hull holes, the wreck carve) exactly like authored ships.
        s.Baseline.Clear();
        s.Baseline.UnionWith(s.Cells.Keys);
        if (applyEdits)
        {
            ApplyPersistedShipEdits(s);
        }

        return s;
    }

    // ---------------- Geometry-derived stats (#949) ----------------

    /// <summary>Flight/combat stats derived from a self-built hull: hull HP grows with the cell count, and
    /// speed/handling come from engine thrust vs. hull mass (clamped into the authored ships' band, with a
    /// floor so even an engine-poor brick limps home). Deterministic integer/cell math only.</summary>
    internal static (float HullMax, float FlightSpeed, float Handling) CustomShipStats(int cells, int engines)
    {
        float hull = System.Math.Clamp(40f + cells * 0.5f, 60f, 320f);
        float thrust = engines * 1f / System.Math.Max(1f, cells / 24f); // 1 engine carries ~24 cells at full ratio
        float speed = System.Math.Clamp(0.4f + thrust * 0.6f, 0.4f, 1.8f);
        float handling = System.Math.Clamp(0.4f + thrust * 0.5f, 0.4f, 1.7f);
        return (hull, speed, handling);
    }

    /// <summary>Derived stats of a specific self-built ship (from its persisted blob).</summary>
    private (float HullMax, float FlightSpeed, float Handling) CustomShipStatsFor(ShipState ship)
    {
        var cells = ParseCustomCells(ship.BuiltCells);
        int engines = cells.Values.Count(b => IsBlockId(b, ShipEngineBlock));
        return CustomShipStats(cells.Count, engines);
    }

    // ---------------- Keel placement (construction start) ----------------

    /// <summary>Handles placing the <c>ship_core</c> item: instead of a world block it founds a new
    /// self-built ship — a fleet entry (un-commissioned) plus a 1-cell construction-site structure anchored
    /// at the cell. Runs after the generic place validation (reach, pads, protections, ship interiors), so
    /// only the keel-specific rules and the material cost live here.</summary>
    private void HandleShipCorePlace(PlayerSession session, Vector3i pos, string itemKey)
    {
        var p = session.State;

        // Surface bodies only (same rule as founding a base): no keels inside stations or ship interiors.
        var hereBody = _galaxy?.FindBody(_world.LocationId);
        if (hereBody is null
            || (hereBody.Kind != CelestialKind.Planet && hereBody.Kind != CelestialKind.Moon && hereBody.Kind != CelestialKind.AsteroidField))
        {
            Reject(session, "place", "@srv.ship.core_ground");
            return;
        }

        // One construction site at a time — the blob, the site object and the commissioning flow are all
        // per-player singletons by design.
        if (UnderConstructionShip(session) is not null)
        {
            Reject(session, "place", "@srv.ship.construction_here");
            return;
        }

        // The keel needs solid ground under it — a floating keel would commission a floating staircase.
        var below = _world.GetBlock(new Vector3i(pos.X, pos.Y - 1, pos.Z));
        if (below.IsAir || IsFluid(below.Value))
        {
            Reject(session, "place", "@srv.ship.core_ground");
            return;
        }

        var core = _content.GetBlock(ShipCoreBlock)?.NumericId ?? BlockId.Air;
        if (core.IsAir)
        {
            Reject(session, "place", "@srv.place.unknown_block");
            return;
        }

        bool free = !Rules.CraftingCostsMaterials || p.InstantBuild;
        var pool = new MaterialPool(_content, p, _ship);
        if (!free)
        {
            if (pool.Count(itemKey) < 1)
            {
                Reject(session, "place", "@srv.place.no_block");
                return;
            }

            pool.Remove(new[] { new ItemAmount(itemKey, 1) });
            SendInventory(session);
        }

        // The new fleet entry: geometry lives in the blob, stats derive from it, nothing in ships.json.
        string id = "built_custom_" + session.Ships.Count;
        while (session.Ships.ContainsKey(id))
        {
            id = "built_custom_" + (session.Ships.Count + id.Length); // collision-proof without extra state
        }

        session.Ships[id] = new ShipState
        {
            ShipType = ShipState.CustomShipType,
            Commissioned = false,
            BuiltCells = "0:0:0:" + core.Value,
            BuildLocationId = _world.LocationId,
            BuildX = pos.X,
            BuildY = pos.Y,
            BuildZ = pos.Z,
            CurrentLocationId = _world.LocationId,
            Hull = 0f,
        };

        PersistFleet(session);
        BroadcastOwnedShips();
        EnsureConstructionSite(session);
        Send(session, new ServerMessage { Text = "@srv.ship.core_placed" });
        _log.Info($"Ship keel laid by {p.Name} at ({pos.X},{pos.Y},{pos.Z}) on {_world.LocationId} (fleet id {id}).");
    }

    // ---------------- Construction-site object lifecycle ----------------

    /// <summary>The player's construction-site record on the active world, or null when none is placed.</summary>
    private LandedShip? ConstructionFor(string playerId)
        => _worlds.Active.LandedShips.TryGetValue(ConstructionKey(playerId), out var rec) && rec.Placed ? rec : null;

    /// <summary>(Re-)places the player's construction site on the active world when their un-commissioned
    /// ship was founded here — called wherever the parked ship is (re-)placed, so join/landing/respawn all
    /// bring the half-built hull back. Idempotent.</summary>
    private void EnsureConstructionSite(PlayerSession session)
    {
        if (UnderConstructionShip(session) is not { } uc || uc.Ship.BuildLocationId != _world.LocationId)
        {
            return;
        }

        var s = BuildCustomShipStructure(ConstructionStructureId(session.State.PlayerId), session.State.PlayerId, uc.Ship, commissioned: false);
        if (s.Cells.Count == 0 && s.DoorCells.Count == 0)
        {
            return;
        }

        var rec = _worlds.Active.LandedFor(ConstructionKey(session.State.PlayerId));
        rec.Structure = s;
        rec.Origin = new Vector3i(uc.Ship.BuildX, uc.Ship.BuildY, uc.Ship.BuildZ);
        DeriveLandedAnchors(rec);
        rec.Placed = true;

        BroadcastToWorld(LandedShipMessage(ConstructionKey(session.State.PlayerId), rec, removed: false));
        RegisterDoors();
        SendShipStations(session);
    }

    /// <summary>Removes the player's construction-site object from the active world (logout, commissioning,
    /// full dismantle). The build itself stays safe in the fleet save; only the world object despawns.</summary>
    private void RemoveConstructionSite(PlayerSession session)
    {
        if (!_worlds.Active.LandedShips.TryGetValue(ConstructionKey(session.State.PlayerId), out var rec) || !rec.Placed)
        {
            return;
        }

        rec.Placed = false;
        BroadcastToWorld(new LandedShipState
        {
            PlayerId = ConstructionKey(session.State.PlayerId),
            StructureId = ConstructionStructureId(session.State.PlayerId),
            Removed = true,
        });
        RegisterDoors();
    }

    /// <summary>True when a world position lies inside the player-built construction volume — keeps world
    /// blocks out of the site the same way <see cref="ShipInteriorContains"/> guards parked ships (the site
    /// itself is deliberately NOT "aboard": an open frame must not hand out ship life support).</summary>
    private bool ConstructionContains(Vector3f p)
    {
        foreach (var (key, rec) in _worlds.Active.LandedShips)
        {
            if (!IsConstructionKey(key) || !rec.Placed)
            {
                continue;
            }

            var s = rec.Structure;
            double dx = WorldConstants.WrapDeltaX(p.X - rec.Origin.X, _world.Circumference);
            if (dx >= 0 && dx <= s.Width
                && p.Y >= rec.Origin.Y && p.Y <= rec.Origin.Y + s.Height + 1
                && p.Z >= rec.Origin.Z && p.Z <= rec.Origin.Z + s.Length)
            {
                return true;
            }
        }

        return false;
    }

    // ---------------- Construction edits (place/mine on the site) ----------------

    /// <summary>Place/mine one cell of the player's construction site — the under-construction sibling of
    /// <see cref="HandleLandedShipEdit"/>. No design baseline exists yet: everything is editable, the size
    /// cap is enforced instead of the design box, and every change is persisted straight into the ship's
    /// cell blob. Growing past the current bounds is allowed (the blob re-normalizes and the object
    /// re-broadcasts), so the hull can expand in every direction from the keel.</summary>
    private void HandleConstructionEdit(PlayerSession session, LandedShip rec, StructureEditIntent intent)
    {
        var p = session.State;
        if (UnderConstructionShip(session) is not { } uc)
        {
            Reject(session, "structure", "@srv.structure.none_here");
            return;
        }

        var s = rec.Structure;
        var pos = new Vector3i(intent.X, intent.Y, intent.Z);
        var cellCentre = new Vector3f(
            rec.Origin.X + pos.X + 0.5f, rec.Origin.Y + pos.Y + 0.5f, rec.Origin.Z + pos.Z + 0.5f);
        if (WrapDistSq(p.Position, cellCentre) > 10f * 10f)
        {
            Reject(session, "structure", "@srv.structure.far");
            return;
        }

        var cells = ParseCustomCells(uc.Ship.BuiltCells);

        if (intent.Mine)
        {
            if (!cells.TryGetValue(pos, out var existing) || IsDoorBlockId(existing))
            {
                // Door cells are air in the structure; the aim ray can't target them anyway.
                Reject(session, "structure", "@srv.structure.nothing");
                return;
            }

            cells.Remove(pos);
            if (_content.BlockById(existing) is { } def && def.Drops.Count > 0)
            {
                var pool = new MaterialPool(_content, p, _ship);
                BankLoot(session, pool, def.Drops);
                SendInventory(session);
            }

            if (cells.Count == 0)
            {
                // The last block came out: the construction is dismantled and the fleet entry goes with it
                // (the persisted ship row is orphaned — RestoreFleet only loads ids in the fleet index).
                session.Ships.Remove(uc.Id);
                PersistFleet(session);
                BroadcastOwnedShips();
                RemoveConstructionSite(session);
                Send(session, new ServerMessage { Text = "@srv.ship.core_removed" });
                return;
            }

            CommitCustomShipCells(session, uc.Ship, rec, commissioned: false, cells, pos, BlockId.AirValue);
            return;
        }

        // ---- place ----
        var item = _content.GetItem(intent.ItemKey);
        if (item is null || string.IsNullOrEmpty(item.PlacesBlock))
        {
            Reject(session, "structure", "@srv.place.not_placeable");
            return;
        }

        var blockDef = _content.GetBlock(item.PlacesBlock!);
        if (blockDef is null)
        {
            Reject(session, "structure", "@srv.place.unknown_block");
            return;
        }

        if (cells.ContainsKey(pos))
        {
            Reject(session, "structure", "@srv.place.not_empty");
            return;
        }

        // The hull rests ON the keel's ground level — no digging the ship into the terrain.
        if (pos.Y < 0)
        {
            Reject(session, "structure", "@srv.ship.ground_level");
            return;
        }

        // Attached to the existing build (6-neighbourhood over the blob, doors included — a lintel above a
        // door opening is legal), so no floating shards.
        bool attached = cells.ContainsKey(new Vector3i(pos.X + 1, pos.Y, pos.Z))
            || cells.ContainsKey(new Vector3i(pos.X - 1, pos.Y, pos.Z))
            || cells.ContainsKey(new Vector3i(pos.X, pos.Y + 1, pos.Z))
            || cells.ContainsKey(new Vector3i(pos.X, pos.Y - 1, pos.Z))
            || cells.ContainsKey(new Vector3i(pos.X, pos.Y, pos.Z + 1))
            || cells.ContainsKey(new Vector3i(pos.X, pos.Y, pos.Z - 1));
        if (!attached)
        {
            Reject(session, "structure", "@srv.structure.no_anchor");
            return;
        }

        // Size cap (issue #950): the extents including the new cell must stay within 15×15×15.
        var probe = new Dictionary<Vector3i, BlockId>(cells) { [pos] = blockDef.NumericId };
        CellBounds(probe, out var min, out var max);
        if (max.X - min.X + 1 > CustomShipMaxFootprint
            || max.Z - min.Z + 1 > CustomShipMaxFootprint
            || max.Y - min.Y + 1 > CustomShipMaxHeight)
        {
            Reject(session, "structure", "@srv.ship.too_big");
            return;
        }

        // Exactly one helm per ship — the commissioning anchor must be unambiguous.
        if (blockDef.Key == ShipHelmBlock && cells.Values.Any(b => IsBlockId(b, ShipHelmBlock)))
        {
            Reject(session, "structure", "@srv.ship.one_helm");
            return;
        }

        bool free = !Rules.CraftingCostsMaterials || p.InstantBuild;
        var buildPool = new MaterialPool(_content, p, _ship);
        if (!free)
        {
            if (buildPool.Count(intent.ItemKey) < 1)
            {
                Reject(session, "structure", "@srv.place.no_block");
                return;
            }

            buildPool.Remove(new[] { new ItemAmount(intent.ItemKey, 1) });
            SendInventory(session);
        }

        cells[pos] = blockDef.NumericId;
        CommitCustomShipCells(session, uc.Ship, rec, commissioned: false, cells, pos,
            IsDoorBlock(blockDef.Key) ? BlockId.AirValue : blockDef.NumericId.Value);
    }

    /// <summary>Persists an edited custom-ship cell map (construction site OR the commissioned parked ship)
    /// and syncs the world object: normalizes the blob, updates the structure incrementally when the bounds
    /// held, or rebuilds + re-broadcasts the whole object when the hull grew/shrank (origin shifts
    /// included). Anchors (helm prompt, doors) refresh either way; a commissioned ship also re-derives its
    /// geometry stats (an engine came out → it flies slower, #949).</summary>
    private void CommitCustomShipCells(
        PlayerSession session, ShipState ship, LandedShip rec, bool commissioned,
        Dictionary<Vector3i, BlockId> cells, Vector3i editedCell, ushort editedBlock)
    {
        string playerId = session.State.PlayerId;
        var origin = rec.Origin;
        var normalized = NormalizeCustomCells(cells, ref origin);
        bool originShifted = origin.X != rec.Origin.X || origin.Y != rec.Origin.Y || origin.Z != rec.Origin.Z;

        ship.BuiltCells = SerializeCells(normalized);
        if (!commissioned)
        {
            ship.BuildX = origin.X;
            ship.BuildY = origin.Y;
            ship.BuildZ = origin.Z;
        }

        PersistFleet(session);

        var rebuilt = BuildCustomShipStructure(rec.Structure.Id, playerId, ship, commissioned, applyEdits: commissioned);
        bool boundsChanged = originShifted
            || rebuilt.Width != rec.Structure.Width
            || rebuilt.Height != rec.Structure.Height
            || rebuilt.Length != rec.Structure.Length;

        rec.Structure = rebuilt;
        rec.Origin = origin;
        DeriveLandedAnchors(rec);

        if (boundsChanged)
        {
            // Bounds moved (growth or dismantling an edge): replace the whole object client-side — the hull
            // is at most 15³ cells, so the full state is small and the client re-meshes per change anyway.
            BroadcastToWorld(LandedShipMessage(commissioned ? playerId : ConstructionKey(playerId), rec, removed: false));
        }
        else
        {
            BroadcastToWorld(new StructureBlockChanged
            {
                StructureId = rec.Structure.Id,
                X = editedCell.X,
                Y = editedCell.Y,
                Z = editedCell.Z,
                Block = editedBlock,
            });
        }

        RegisterDoors();
        SendShipStations(session);

        if (commissioned)
        {
            RecomputeShipCombatStats();
            SendShipCombatStatus(session);
            BroadcastOwnedShips(); // derived speed/handling ride the fleet message (#949)
        }
    }

    // ---------------- Commissioned-ship edits (blob-backed design changes) ----------------

    /// <summary>On-foot mining on the player's COMMISSIONED self-built ship: a design change, not damage —
    /// the cell leaves the persisted blob (so repair never rebuilds it) and the drops come back. The very
    /// last cell is refused; dismantling a commissioned ship entirely would strand its fleet entry.</summary>
    private void HandleCustomShipMine(PlayerSession session, LandedShip rec, Vector3i pos)
    {
        var ship = _ship;
        var cells = ParseCustomCells(ship.BuiltCells);
        if (!cells.TryGetValue(pos, out var existing))
        {
            Reject(session, "structure", "@srv.structure.nothing");
            return;
        }

        if (cells.Count <= 1)
        {
            Reject(session, "structure", "@srv.structure.hull_protected");
            return;
        }

        cells.Remove(pos);
        if (_content.BlockById(existing) is { } def && def.Drops.Count > 0)
        {
            var pool = new MaterialPool(_content, session.State, ship);
            BankLoot(session, pool, def.Drops);
            SendInventory(session);
        }

        CommitCustomShipCells(session, ship, rec, commissioned: true, cells, pos, BlockId.AirValue);
    }

    // ---------------- Commissioning (#950) ----------------

    /// <summary>The first launch-blocking problem of a self-built hull as a locale key, or null when it is
    /// flight-worthy. Shared by the commissioning interaction and the launch gate (a commissioned ship that
    /// was edited back into an invalid state is grounded again with the same message).</summary>
    private string? CustomShipLaunchProblem(ShipState ship)
    {
        var cells = ParseCustomCells(ship.BuiltCells);
        int solid = cells.Count(kv => !IsDoorBlockId(kv.Value));
        if (solid < CustomShipMinBlocks)
        {
            return "@srv.ship.too_small:" + CustomShipMinBlocks;
        }

        if (!CellBounds(cells, out var min, out var max)
            || max.X - min.X + 1 > CustomShipMaxFootprint
            || max.Z - min.Z + 1 > CustomShipMaxFootprint
            || max.Y - min.Y + 1 > CustomShipMaxHeight)
        {
            return "@srv.ship.too_big";
        }

        if (cells.Values.Count(b => IsBlockId(b, ShipHelmBlock)) != 1)
        {
            return "@srv.ship.need_helm";
        }

        if (!cells.Values.Any(b => IsBlockId(b, ShipEngineBlock)))
        {
            return "@srv.ship.need_engine";
        }

        if (!cells.Values.Any(IsDoorBlockId))
        {
            return "@srv.ship.need_door";
        }

        return AirtightProblem(ship);
    }

    /// <summary>Airtightness (#950): flood the OUTSIDE of the hull's bounding box (+1 margin) through every
    /// non-sealing cell; whatever interior air the flood cannot reach is the sealed cabin. Sealing cells are
    /// airtight full-cube blocks (<see cref="Shared.Definitions.BlockDefinition.Airtight"/> — glass counts,
    /// like base rooms) and door cells (the slide door fills its opening; base rules for energy doors).
    /// Requires a minimum sealed volume and sealed standing room at the helm.</summary>
    private string? AirtightProblem(ShipState ship)
    {
        var s = BuildCustomShipStructure("validate:" + ship.ShipType, string.Empty, ship, commissioned: false);
        var doorCells = new HashSet<Vector3i>(s.DoorCells); // any kind seals for validation (the panel fills the opening)

        bool Seals(Vector3i c)
        {
            if (doorCells.Contains(c))
            {
                return true;
            }

            var b = s.Get(c);
            return !b.IsAir && _content.BlockById(b) is { Airtight: true };
        }

        // Outside flood over the expanded box: every box-border cell seeds it.
        var outside = new HashSet<Vector3i>();
        var frontier = new Queue<Vector3i>();
        void Seed(Vector3i c)
        {
            if (!Seals(c) && outside.Add(c))
            {
                frontier.Enqueue(c);
            }
        }

        for (int x = -1; x <= s.Width; x++)
            for (int y = -1; y <= s.Height; y++)
                for (int z = -1; z <= s.Length; z++)
                {
                    if (x == -1 || x == s.Width || y == -1 || y == s.Height || z == -1 || z == s.Length)
                    {
                        Seed(new Vector3i(x, y, z));
                    }
                }

        var steps = new[]
        {
            new Vector3i(1, 0, 0), new Vector3i(-1, 0, 0), new Vector3i(0, 1, 0),
            new Vector3i(0, -1, 0), new Vector3i(0, 0, 1), new Vector3i(0, 0, -1),
        };
        while (frontier.Count > 0)
        {
            var c = frontier.Dequeue();
            foreach (var d in steps)
            {
                var n = new Vector3i(c.X + d.X, c.Y + d.Y, c.Z + d.Z);
                if (n.X < -1 || n.X > s.Width || n.Y < -1 || n.Y > s.Height || n.Z < -1 || n.Z > s.Length)
                {
                    continue;
                }

                if (!Seals(n) && outside.Add(n))
                {
                    frontier.Enqueue(n);
                }
            }
        }

        // Interior = air cells inside the bounds the outside flood never reached.
        int interior = 0;
        var interiorCells = new HashSet<Vector3i>();
        for (int x = 0; x < s.Width; x++)
            for (int y = 0; y < s.Height; y++)
                for (int z = 0; z < s.Length; z++)
                {
                    var c = new Vector3i(x, y, z);
                    if (s.Get(c).IsAir && !doorCells.Contains(c) && !outside.Contains(c))
                    {
                        interior++;
                        interiorCells.Add(c);
                    }
                }

        if (interior < CustomShipMinInteriorAir)
        {
            return "@srv.ship.not_airtight";
        }

        // The pilot must be able to stand INSIDE at the helm: some sealed air cell touches the helm block.
        var helm = s.StationCells.FirstOrDefault(sc => sc.Type is "shipyard" or "cockpit").Cell;
        bool helmReachable = steps.Any(d => interiorCells.Contains(new Vector3i(helm.X + d.X, helm.Y + d.Y, helm.Z + d.Z)));
        return helmReachable ? null : "@srv.ship.no_room_inside";
    }

    /// <summary>Commissions the player's construction into a real, flyable fleet ship: validates the build,
    /// grants the baseline modules (life support, cockpit, reactor, a basic hold), derives the geometry
    /// stats, makes it the ACTIVE ship and re-parks it right where it was built — the old parked ship goes
    /// back into the fleet hangar until the next switch.</summary>
    private void TryCommissionShip(PlayerSession session)
    {
        var p = session.State;
        if (UnderConstructionShip(session) is not { } uc || ConstructionFor(p.PlayerId) is not { } rec)
        {
            Reject(session, "station", "@srv.station.none");
            return;
        }

        // Reach: standing at the helm (same reach every ship station uses).
        var helmStation = rec.Stations.FirstOrDefault(st => st.Type == "shipyard");
        if (helmStation.Type != "shipyard"
            || WrapDistSq(p.Position, helmStation.Pos) > ShipStationReach * ShipStationReach)
        {
            Reject(session, "station", "@srv.station.too_far");
            return;
        }

        if (CustomShipLaunchProblem(uc.Ship) is { } problem)
        {
            Reject(session, "station", problem);
            return;
        }

        // ---- flight-worthy: commission it ----
        var ship = uc.Ship;
        ship.Commissioned = true;
        ship.CurrentLocationId = _world.LocationId;
        foreach (var moduleKey in new[] { "cockpit", "reactor", "life_support", "cargo_hold_basic" })
        {
            if (!ship.HasModule(moduleKey) && _content.GetShipModule(moduleKey) is not null)
            {
                ship.Modules.Add(moduleKey);
            }
        }

        ResizeCargo(ship);

        var origin = new Vector3i(ship.BuildX, ship.BuildY, ship.BuildZ);
        ship.BuildLocationId = string.Empty;

        // The freshly built ship becomes the active one; the previously parked ship despawns (it is back in
        // the fleet, SwitchShip re-parks it on the pad later) and the construction object turns into the
        // regular parked-ship object at the very spot it was built.
        RemoveLandedShip(session);
        RemoveConstructionSite(session);
        session.ActiveShipId = uc.Id;
        RecomputeShipCombatStats();
        ship.Hull = _shipHullMax;

        var s = BuildCustomShipStructure("ship:" + p.PlayerId, p.PlayerId, ship, commissioned: true, applyEdits: true);
        var landed = _worlds.Active.LandedFor(p.PlayerId);
        landed.Structure = s;
        landed.Origin = origin;
        DeriveLandedAnchors(landed);
        landed.Placed = true;
        BroadcastToWorld(LandedShipMessage(p.PlayerId, landed, removed: false));
        RegisterDoors();

        PersistFleet(session);
        BroadcastOwnedShips();
        SendShipStations(session);
        SendDoors(session);
        SendShipCombatStatus(session);
        Send(session, new ServerMessage { Text = "@srv.ship.commissioned" });
        OnAchievementShipCommissioned(session); // "Shipwright" (#1102)
        RecordStoryMilestone("ship:first");     // the first self-built ship of the save advances the arc (#1105)
        _log.Info($"Self-built ship '{uc.Id}' of {p.Name} commissioned ({s.Cells.Count} cells).");
    }

    // ---------------- Test hooks ----------------

    /// <summary>Test hook: lay a ship keel at a world cell as the given player (bypasses aim/reach).</summary>
    public void PlaceShipCoreForTest(string playerId, int x, int y, int z)
    {
        if (FindSessionByPlayerId(playerId) is { } session)
        {
            Serve(session);
            HandleShipCorePlace(session, new Vector3i(x, y, z), ShipCoreBlock);
        }
    }

    /// <summary>Test hook: attempt commissioning the player's construction (bypasses the E press).</summary>
    public void CommissionShipForTest(string playerId)
    {
        if (FindSessionByPlayerId(playerId) is { } session)
        {
            Serve(session);
            TryCommissionShip(session);
        }
    }

    /// <summary>Test/inspection: the player's construction-site origin + structure size, or null.</summary>
    public (Vector3i Origin, Vector3i Size)? ConstructionBoundsForTest(string playerId)
        => ConstructionFor(playerId) is { } rec
            ? (rec.Origin, new Vector3i(rec.Structure.Width, rec.Structure.Height, rec.Structure.Length))
            : null;

    /// <summary>Test/inspection: derived custom-ship stats of an owned ship, or null.</summary>
    public (float HullMax, float FlightSpeed, float Handling)? CustomShipStatsForTest(string playerId, string shipId)
        => FindSessionByPlayerId(playerId) is { } s && s.Ships.TryGetValue(shipId, out var ship) && ship.IsCustom
            ? CustomShipStatsFor(ship)
            : null;
}
