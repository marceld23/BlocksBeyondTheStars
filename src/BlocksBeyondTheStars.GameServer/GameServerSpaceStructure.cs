using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// A small voxel structure floating in a space instance (item 20, "build in space"). Stage 1 (S1) seeds the
/// first one from the player's own ship design (the ship-editor voxel cells), so the flight view can render
/// the ship as a real 1:1 voxel mesh instead of the hand-built cube model. Later stages add player-built
/// stations and voxel asteroids.
///
/// It is its own tiny block grid — no world generation, no longitude wrap — held sparsely (only non-air
/// cells), plus a position in the flight scene. S1 only renders it (the player's own ship rides the live ship
/// pose, so its <see cref="Position"/> is informational); free-space block edits + collision arrive in S2.
/// </summary>
public sealed class SpaceStructure
{
    public string Id { get; set; } = string.Empty;
    public string Kind { get; set; } = "ship"; // ship | station | asteroid
    public string OwnerId { get; set; } = string.Empty;

    /// <summary>Display name (player-built stations) — shown on the star map + as the dock contact.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>True once a player station has been commissioned (has an airlock + min size) — it is then a
    /// boardable body on the star map + persisted (item 20 S4).</summary>
    public bool Boardable { get; set; }

    /// <summary>Position in the flight scene. For the owner's own ship the live ship pose overrides this.</summary>
    public Vector3f Position { get; set; }

    /// <summary>Design bounding-box size in blocks (used to centre the mesh on the ship pivot).</summary>
    public int Width { get; set; }
    public int Height { get; set; }
    public int Length { get; set; }

    /// <summary>The sparse block grid — only non-air cells are stored.</summary>
    public Dictionary<Vector3i, BlockId> Cells { get; } = new();

    /// <summary>The design-derived cells (hull/glass/lights/engines/station markers) snapshotted BEFORE the
    /// player's persisted edits apply. On-foot edits (landed ship + ship interior) may never mine these —
    /// the hull is not damageable and modules are not removable; only player-added blocks come out again.
    /// (The space EVA keeps its existing hull-mining rules for repairs.)</summary>
    public HashSet<Vector3i> Baseline { get; } = new();

    /// <summary>Interior station markers (medbay/cockpit/workshop/…) in structure-local cells.</summary>
    public List<(string Type, Vector3i Cell)> StationCells { get; } = new();

    /// <summary>Doorway base cells (sci-fi slide doors fill these openings) in structure-local coords.</summary>
    public List<Vector3i> DoorCells { get; } = new();

    /// <summary>The medbay heal-tank cell (respawn point), if the design carries one.</summary>
    public Vector3i? MedbayCell { get; set; }

    public void Set(Vector3i pos, BlockId block)
    {
        if (block.IsAir)
        {
            Cells.Remove(pos);
        }
        else
        {
            Cells[pos] = block;
        }
    }

    public BlockId Get(Vector3i pos) => Cells.TryGetValue(pos, out var b) ? b : BlockId.Air;
}

public sealed partial class GameServer
{
    /// <summary>S5: how close the suit must be to a static structure (asteroid/station) to mine/build on it —
    /// a coarse anti-grief range so you can't edit a body across the flight zone.</summary>
    private const float StructureEditRange = 40f;

    /// <summary>Builds the served player's ship as a voxel <see cref="SpaceStructure"/> from its editor design
    /// (item 20, S1). Mirrors the block mapping the ground stamp uses (<see cref="StampShipLayout"/>) but writes
    /// into a standalone sparse grid instead of the planet world. Hatch/door cells render as holes. Ships with
    /// no designed layout fall back to a hollow hull box derived from the design's interior dimensions.</summary>
    private SpaceStructure BuildShipStructure(string ownerId)
    {
        var design = _content.GetShip(_ship.ShipType) ?? _content.GetShip("starter");
        var s = new SpaceStructure { Id = "ship:" + ownerId, Kind = "ship", OwnerId = ownerId };

        var wall = _content.GetBlock("iron_wall")?.NumericId ?? BlockId.Air;
        var glass = _content.GetBlock("glass")?.NumericId ?? wall;
        var dark = _content.GetBlock("carbon")?.NumericId ?? _content.GetBlock("basalt")?.NumericId ?? wall;
        var lightW = _content.GetBlock("light_white")?.NumericId ?? glass;
        var lightR = _content.GetBlock("light_red")?.NumericId ?? lightW;
        var lightG = _content.GetBlock("light_green")?.NumericId ?? lightW;
        if (wall.IsAir)
        {
            return s; // no hull block in content → an empty structure (client keeps the cube model)
        }

        var layout = _content.GetShipLayout(design?.Layout);
        if (layout != null && layout.Cells.Count > 0)
        {
            s.Width = layout.Width;
            s.Height = layout.Height;
            s.Length = layout.Length;
            foreach (var cell in layout.Cells)
            {
                var p = new Vector3i(cell.X, cell.Y, cell.Z);

                // Station tiles: an interior interaction marker block + the gameplay anchor (ship-as-object:
                // the structure IS the walkable ship everywhere now, so the stations live in it).
                if (cell.Kind == "station")
                {
                    s.Set(p, _content.GetBlock(StationBlockKey(cell.Id))?.NumericId ?? wall);
                    s.StationCells.Add((cell.Id, p));
                    if (cell.Id == "medbay")
                    {
                        s.MedbayCell = p;
                    }

                    continue;
                }

                switch (cell.Id)
                {
                    case "hatch":
                        continue; // an open entry renders as a hole
                    case "door_slide":
                    case "door_hinge":
                        s.DoorCells.Add(p); // a server-authoritative slide door fills this opening
                        continue;
                    case "glass": s.Set(p, glass); continue;
                    case "light":
                    case "headlight": s.Set(p, lightW); continue;
                    case "light_red": s.Set(p, lightR); continue;
                    case "light_green": s.Set(p, lightG); continue;
                    case "engine": s.Set(p, dark); continue;
                }

                // Any block key (iron_wall, carbon cargo, …) renders as that block; unknown ids fall back to hull.
                s.Set(p, _content.GetBlock(cell.Id)?.NumericId ?? wall);
            }

            // Guarantee a flush, solid floor across the footprint (fills layout gaps) so the player never
            // falls out of the walkable interior — mirrors the old stamped-ship floor guarantee.
            for (int fx = 0; fx < layout.Width; fx++)
            for (int fz = 0; fz < layout.Length; fz++)
            {
                var fp = new Vector3i(fx, 0, fz);
                if (s.Get(fp).IsAir)
                {
                    s.Set(fp, wall);
                }
            }

            FinishShipStructure(s);
            return s;
        }

        // No designed layout → a simple hollow hull box from the design's interior dims (matches StampShip's
        // box: a shell with a 3-wide rear hatch hole and a front window band).
        int halfX = System.Math.Max(2, (design?.InteriorWidth ?? 5) / 2);
        int halfZ = System.Math.Max(2, (design?.InteriorLength ?? 7) / 2);
        int height = System.Math.Max(3, design?.Height ?? 4);
        s.Width = halfX * 2 + 1;
        s.Height = height + 1;
        s.Length = halfZ * 2 + 1;
        for (int x = 0; x <= halfX * 2; x++)
        for (int y = 0; y <= height; y++)
        for (int z = 0; z <= halfZ * 2; z++)
        {
            bool shell = x == 0 || x == halfX * 2 || y == 0 || y == height || z == 0 || z == halfZ * 2;
            if (!shell)
            {
                continue; // hollow interior
            }

            bool door = z == 0 && (x == halfX - 1 || x == halfX || x == halfX + 1) && (y == 1 || y == 2);
            if (door)
            {
                continue; // rear hatch opening (a slide door fills it, see DoorCells below)
            }

            // Window panes at eye height: a band along the front (+Z) and both side walls (matching the
            // old stamped box ship), so the cabin has proper windows to see out of.
            bool frontWin = z == halfZ * 2 && y == 2 && x > 0 && x < halfX * 2;
            bool sideWin = (x == 0 || x == halfX * 2) && y == 2 && z > 0 && z < halfZ * 2;
            s.Set(new Vector3i(x, y, z), frontWin || sideWin ? glass : wall);
        }

        // The rear hatch gets a real door (server-authoritative slide door at the opening's centre column).
        s.DoorCells.Add(new Vector3i(halfX, 1, 0));

        // Interior dressing (ported from the old stamped box ship): emissive ceiling panels down the roof
        // centre + cyan wall light strips above the window band, so the cabin reads as a lit sci-fi interior.
        var ceilingLight = _content.GetBlock("data_cache")?.NumericId ?? glass;
        for (int zc = 1; zc <= halfZ * 2 - 1; zc += 2)
        {
            s.Set(new Vector3i(halfX, height, zc), ceilingLight);
        }

        var stripCyan = _content.GetBlock("strip_light_cyan")?.NumericId ?? BlockId.Air;
        if (!stripCyan.IsAir && height >= 4)
        {
            for (int zc = 1; zc <= halfZ * 2 - 1; zc += 2)
            {
                s.Set(new Vector3i(0, 3, zc), stripCyan);
                s.Set(new Vector3i(halfX * 2, 3, zc), stripCyan);
            }
        }

        // Interior station markers on the floor (same placement as the old stamped box ship: corners + walls,
        // kept inside the shell). NOTE: the box ship's heal-tank/respawn stays at the CABIN CENTRE (MedbayCell
        // unset → the placement falls back to the centre), matching the old stamp's spawn point.
        void BoxStation(string type, int x, int z)
        {
            var cell = new Vector3i(x, 1, z);
            s.Set(cell, _content.GetBlock(StationBlockKey(type))?.NumericId ?? wall);
            s.StationCells.Add((type, cell));
        }

        BoxStation("medbay", 1, 1);
        BoxStation("cockpit", halfX, halfZ * 2 - 1);
        BoxStation("workshop", halfX * 2 - 1, halfZ);
        BoxStation("cargo", 1, halfZ);
        BoxStation("quarters", halfX * 2 - 1, 1);
        BoxStation("lab", 1, halfZ * 2 - 1);
        BoxStation("console", halfX * 2 - 1, halfZ * 2 - 1);

        // Exterior silhouette so the box reads as a SHIP from outside (bug fix): side wings, rear engine
        // nozzles, nav lights at the wingtips, and a raised glass cockpit canopy toward the front. (Cells may
        // be negative / beyond the hull box — the structure grid + client mesher handle that.)
        int wingY = System.Math.Max(1, height / 2);
        int cx = halfX;
        for (int sgn = -1; sgn <= 1; sgn += 2)
        {
            for (int w = 1; w <= 2; w++) // span outward from each side wall
            {
                for (int zc = halfZ - 1; zc <= halfZ + 1; zc++) // a short chord around the middle
                {
                    int wx = sgn < 0 ? -w : halfX * 2 + w;
                    s.Set(new Vector3i(wx, wingY, zc), wall);
                }
            }

            // Rear engine nozzles (dark), just behind the rear wall at the corners.
            s.Set(new Vector3i(sgn < 0 ? 1 : halfX * 2 - 1, 1, -1), dark);

            // Wingtip nav lights: red to port (-X), green to starboard (+X).
            s.Set(new Vector3i(sgn < 0 ? -2 : halfX * 2 + 2, wingY, halfZ), sgn < 0 ? lightR : lightG);
        }

        // Raised glass cockpit canopy on top toward the front.
        s.Set(new Vector3i(cx, height + 1, halfZ * 2 - 1), glass);
        s.Set(new Vector3i(cx, height + 1, halfZ * 2 - 2), glass);

        FinishShipStructure(s);
        return s;
    }

    /// <summary>Common ship-structure finish: paints the per-room floor accents, snapshots the protected
    /// design baseline (hull + modules — never minable on foot), then applies the player's persisted edits
    /// on top (added blocks; in-space EVA hull repairs/removals).</summary>
    private void FinishShipStructure(SpaceStructure s)
    {
        PaintStructureAccents(s);
        s.Baseline.Clear();
        s.Baseline.UnionWith(s.Cells.Keys);
        ApplyPersistedShipEdits(s);
    }

    /// <summary>Room-identity pass (ship-as-object port of the stamped PaintStationAccents): a 3×3 accent
    /// pad in the floor layer under each station marker so the rooms read at a glance. Only recolours
    /// existing solid floor cells, never air.</summary>
    private void PaintStructureAccents(SpaceStructure s)
    {
        foreach (var (type, cell) in s.StationCells)
        {
            string? accentKey = type switch
            {
                "medbay" => "medbay_panel",
                "lab" or "cockpit" or "console" => "lab_panel",
                "cargo" => "cargo_floor",
                "workshop" => "engine_panel",
                "quarters" => "metal_panel",
                _ => null,
            };

            if (accentKey == null || _content.GetBlock(accentKey) is not { } accent)
            {
                continue;
            }

            for (int x = cell.X - 1; x <= cell.X + 1; x++)
            for (int z = cell.Z - 1; z <= cell.Z + 1; z++)
            {
                var p = new Vector3i(x, cell.Y - 1, z);
                if (!s.Get(p).IsAir)
                {
                    s.Set(p, accent.NumericId);
                }
            }
        }
    }

    /// <summary>Re-applies the player's persisted hull edits (item 20 S4 durable save) on top of the
    /// freshly rebuilt ship voxel baseline, so mined-out / built-on cells survive a server restart and
    /// re-entry into space — and, ship-as-object, carry into the landed ship + walkable interior too.
    /// Only player deltas are stored (mirrors the per-cell planet block-edit model), keeping it
    /// Raspberry-Pi-friendly. An edit setting a cell to air is honoured via <see cref="SpaceStructure.Set"/>.</summary>
    private void ApplyPersistedShipEdits(SpaceStructure s)
    {
        foreach (var edit in _repo.LoadStructureEdits(s.Id))
        {
            s.Set(edit.WorldPosition, new BlockId(edit.Block));
        }
    }

    /// <summary>Places or mines one cell on a voxel structure during an EVA spacewalk (item 20 S2) — the
    /// free-space analogue of <see cref="HandlePlace"/>/<see cref="HandleMine"/>, scoped to the structure's own
    /// sparse grid. S2 only lets you edit your OWN ship (other ships + game stations are protected in S5), and
    /// trusts the client's voxel ray-march for aim/reach (server-side reach is S5 hardening). Edits live in the
    /// instance's structure (persisted across re-entry while you stay in space; durable save is S4).</summary>
    private void HandleStructureEdit(PlayerSession session, StructureEditIntent intent)
    {
        var p = session.State;
        if (!_playerInstance.TryGetValue(p.PlayerId, out var iid) || !_spaceInstances.TryGetValue(iid, out var instance))
        {
            // Not in space → on foot: edit YOUR parked ship (landed world / walkable ship interior).
            HandleLandedShipEdit(session, intent);
            return;
        }

        if (!p.InEva)
        {
            Reject(session, "structure", "Step outside (EVA) to build in space.");
            return;
        }

        if (!instance.Structures.TryGetValue(intent.StructureId, out var s))
        {
            Reject(session, "structure", "No such structure.");
            return;
        }

        // S2/S3: you may mine your OWN ship or any asteroid; placing is only on your own ship. Other players'
        // ships + game stations stay protected (S5).
        bool isAsteroid = s.Kind == "asteroid";
        bool isOwn = s.OwnerId == p.PlayerId; // your own ship or your own station
        if (!isAsteroid && !isOwn)
        {
            Reject(session, "structure", "You can only modify your own ship or station.");
            return;
        }

        // S5 hardening: a static structure (asteroid/station) has a real world position — require the suit to be
        // near it, so you can't mine/build across the whole zone. (The own ship rides the pilot, so skip it.)
        if (s.Kind != "ship")
        {
            var suit = instance.ShipPosition;
            float ex = suit.X - s.Position.X, ey = suit.Y - s.Position.Y, ez = suit.Z - s.Position.Z;
            if (ex * ex + ey * ey + ez * ez > StructureEditRange * StructureEditRange)
            {
                Reject(session, "structure", "Too far from the structure.");
                return;
            }
        }

        var pos = new Vector3i(intent.X, intent.Y, intent.Z);
        if (intent.Mine)
        {
            // Ship MODULES (station markers) are never removable — not even on an EVA hull pass.
            if (s.Kind == "ship" && s.StationCells.Any(sc => sc.Cell == pos))
            {
                Reject(session, "structure", "Ship modules cannot be removed.");
                return;
            }

            var existing = s.Get(pos);
            if (existing.IsAir)
            {
                Reject(session, "structure", "Nothing to mine there.");
                return;
            }

            s.Set(pos, BlockId.Air);

            // item 20 S4 durable save: a hull cell the owner mined out persists as a per-cell delta (only
            // player changes are stored), so the edit survives a server restart + re-entry into space.
            if (s.Kind == "ship")
            {
                _repo.SetStructureBlock(s.Id, pos, BlockId.AirValue);
            }

            // Bank the mined block's drops (ore from asteroids; rebuild materials from a ship hull).
            if (_content.BlockById(existing) is { } def && def.Drops.Count > 0)
            {
                var pool = new MaterialPool(_content, p, _ship);
                foreach (var drop in def.Drops)
                {
                    pool.Add(drop.Item, drop.Count);
                }

                SendInventory(session);
            }

            BroadcastToInstance(instance, new StructureBlockChanged
            {
                StructureId = s.Id, X = pos.X, Y = pos.Y, Z = pos.Z, Block = BlockId.AirValue,
            });

            // A fully mined-out asteroid is gone — remove its body + paired entity (the field respawns later).
            if (isAsteroid && s.Cells.Count == 0)
            {
                instance.Entities.RemoveAll(e => e.Id == s.Id);
                RemoveAsteroidStructure(instance, s.Id);
                BroadcastSpaceState(instance);
            }
            else if (isAsteroid && instance.Entities.FirstOrDefault(e => e.Id == s.Id) is { } ent)
            {
                ent.Hull = ent.HullMax = s.Cells.Count; // keep the shoot-path hull in step with mined blocks
            }

            return;
        }

        if (isAsteroid)
        {
            Reject(session, "structure", "You can't build on an asteroid.");
            return;
        }

        var item = _content.GetItem(intent.ItemKey);
        if (item is null || string.IsNullOrEmpty(item.PlacesBlock))
        {
            Reject(session, "structure", "Item cannot be placed.");
            return;
        }

        var blockDef = _content.GetBlock(item.PlacesBlock!);
        if (blockDef is null)
        {
            Reject(session, "structure", "Unknown block for item.");
            return;
        }

        if (!s.Get(pos).IsAir)
        {
            Reject(session, "structure", "Target is not empty.");
            return;
        }

        bool free = !Rules.CraftingCostsMaterials || p.InstantBuild;
        var buildPool = new MaterialPool(_content, p, _ship);
        if (!free)
        {
            if (buildPool.Count(intent.ItemKey) < 1)
            {
                Reject(session, "structure", "You do not have that block.");
                return;
            }

            buildPool.Remove(new[] { new ItemAmount(intent.ItemKey, 1) });
            SendInventory(session);
        }

        s.Set(pos, blockDef.NumericId);

        // item 20 S4 durable save: a hull cell the owner built persists as a per-cell delta (own ship only;
        // a station's whole build is persisted via PersistStation below).
        if (s.Kind == "ship")
        {
            _repo.SetStructureBlock(s.Id, pos, blockDef.NumericId.Value);
        }

        BroadcastToInstance(instance, new StructureBlockChanged
        {
            StructureId = s.Id, X = pos.X, Y = pos.Y, Z = pos.Z, Block = blockDef.NumericId.Value,
        });

        // item 20 S4: building a hull + airlock around a station core commissions it (boardable + on the map);
        // keep an already-commissioned station's persisted build up to date.
        if (s.Kind == "station")
        {
            if (!s.Boardable)
            {
                TryCommissionStation(instance, s, session);
            }
            else
            {
                PersistStation(instance, s);
            }
        }
    }

    /// <summary>On-foot ship editing (ship-as-object): place/mine cells of YOUR parked ship — on the landed
    /// world or in the walkable ship interior. The design baseline is protected (the hull cannot be damaged,
    /// modules cannot be removed), but the player may furnish free interior space and take those own blocks
    /// out again. Edits persist as the same per-cell structure deltas the space EVA uses, so they carry
    /// across landing, flight and the interior.</summary>
    private void HandleLandedShipEdit(PlayerSession session, StructureEditIntent intent)
    {
        var p = session.State;
        var rec = _worlds.Active.LandedFor(p.PlayerId);
        if (!rec.Placed || rec.Structure.Id != intent.StructureId)
        {
            Reject(session, "structure", "No such structure here.");
            return;
        }

        var s = rec.Structure;
        var pos = new Vector3i(intent.X, intent.Y, intent.Z);

        // Reach: the player must be near the edited cell (mirrors the world dig reach, with slack for the
        // camera-ray aim).
        var cellCentre = new Vector3f(
            rec.Origin.X + pos.X + 0.5f, rec.Origin.Y + pos.Y + 0.5f, rec.Origin.Z + pos.Z + 0.5f);
        if (WrapDistSq(p.Position, cellCentre) > 10f * 10f)
        {
            Reject(session, "structure", "Too far away.");
            return;
        }

        if (intent.Mine)
        {
            if (s.Baseline.Contains(pos))
            {
                Reject(session, "structure", s.StationCells.Any(sc => sc.Cell == pos)
                    ? "Ship modules cannot be removed."
                    : "The ship hull cannot be damaged.");
                return;
            }

            var existing = s.Get(pos);
            if (existing.IsAir)
            {
                Reject(session, "structure", "Nothing to mine there.");
                return;
            }

            s.Set(pos, BlockId.Air);
            _repo.SetStructureBlock(s.Id, pos, BlockId.AirValue);

            if (_content.BlockById(existing) is { } def && def.Drops.Count > 0)
            {
                var pool = new MaterialPool(_content, p, _ship);
                foreach (var drop in def.Drops)
                {
                    pool.Add(drop.Item, drop.Count);
                }

                SendInventory(session);
            }

            BroadcastToWorld(new StructureBlockChanged
            {
                StructureId = s.Id, X = pos.X, Y = pos.Y, Z = pos.Z, Block = BlockId.AirValue,
            });
            return;
        }

        // Place: only into free space INSIDE the ship bounds, attached to something (no floating junk).
        if (pos.X < 0 || pos.X >= s.Width || pos.Y < 0 || pos.Y > s.Height || pos.Z < 0 || pos.Z >= s.Length)
        {
            Reject(session, "structure", "Only inside the ship.");
            return;
        }

        if (!s.Get(pos).IsAir)
        {
            Reject(session, "structure", "Target is not empty.");
            return;
        }

        bool attached = !s.Get(new Vector3i(pos.X + 1, pos.Y, pos.Z)).IsAir
            || !s.Get(new Vector3i(pos.X - 1, pos.Y, pos.Z)).IsAir
            || !s.Get(new Vector3i(pos.X, pos.Y + 1, pos.Z)).IsAir
            || !s.Get(new Vector3i(pos.X, pos.Y - 1, pos.Z)).IsAir
            || !s.Get(new Vector3i(pos.X, pos.Y, pos.Z + 1)).IsAir
            || !s.Get(new Vector3i(pos.X, pos.Y, pos.Z - 1)).IsAir;
        if (!attached)
        {
            Reject(session, "structure", "Nothing to attach the block to.");
            return;
        }

        var item = _content.GetItem(intent.ItemKey);
        if (item is null || string.IsNullOrEmpty(item.PlacesBlock))
        {
            Reject(session, "structure", "Item cannot be placed.");
            return;
        }

        var blockDef = _content.GetBlock(item.PlacesBlock!);
        if (blockDef is null)
        {
            Reject(session, "structure", "Unknown block for item.");
            return;
        }

        bool free = !Rules.CraftingCostsMaterials || p.InstantBuild;
        var buildPool = new MaterialPool(_content, p, _ship);
        if (!free)
        {
            if (buildPool.Count(intent.ItemKey) < 1)
            {
                Reject(session, "structure", "You do not have that block.");
                return;
            }

            buildPool.Remove(new[] { new ItemAmount(intent.ItemKey, 1) });
            SendInventory(session);
        }

        s.Set(pos, blockDef.NumericId);
        _repo.SetStructureBlock(s.Id, pos, blockDef.NumericId.Value);
        BroadcastToWorld(new StructureBlockChanged
        {
            StructureId = s.Id, X = pos.X, Y = pos.Y, Z = pos.Z, Block = blockDef.NumericId.Value,
        });
    }

    /// <summary>Test hook: run an EVA structure edit (item 20 S2).</summary>
    public void HandleStructureEditForTest(string playerId, StructureEditIntent intent)
    {
        if (FindSessionByPlayerId(playerId) is { } session)
        {
            Serve(session);
            HandleStructureEdit(session, intent);
        }
    }

    /// <summary>Test/inspection: the block id at a cell of a player's ship structure in their space instance, or
    /// air if there is no such structure/cell (item 20 S2).</summary>
    public ushort StructureBlockForTest(string playerId, int x, int y, int z)
        => _playerInstance.TryGetValue(playerId, out var iid)
           && _spaceInstances.TryGetValue(iid, out var inst)
           && inst.Structures.TryGetValue("ship:" + playerId, out var s)
            ? s.Get(new Vector3i(x, y, z)).Value
            : BlockId.AirValue;

    /// <summary>Test/inspection: the number of solid cells in a structure (by id) across any space instance — for
    /// asserting asteroid carving/mining (item 20 S3). 0 if no such structure.</summary>
    public int StructureBlockCountForTest(string structureId)
    {
        foreach (var inst in _spaceInstances.Values)
        {
            if (inst.Structures.TryGetValue(structureId, out var s))
            {
                return s.Cells.Count;
            }
        }

        return 0;
    }

    /// <summary>Test hook: build a player's ship voxel structure (item 20 S1) with the ship cursor pointed at them.</summary>
    public SpaceStructure BuildShipStructureForTest(string playerId)
    {
        if (FindSessionByPlayerId(playerId) is { } session)
        {
            Serve(session);
            return BuildShipStructure(playerId);
        }

        return new SpaceStructure();
    }

    /// <summary>Sends a structure's voxel grid + size to a client as a <see cref="SpaceShipDesign"/> (item 20, S1).</summary>
    /// <summary><paramref name="kindOverride"/> = "ship_remote" sends ANOTHER player's ship design (the
    /// client caches it per pilot for the flight view + the landing/launch FX instead of treating it
    /// as the own ship).</summary>
    private void SendShipDesign(PlayerSession session, SpaceStructure s, string kindOverride = null)
    {
        int n = s.Cells.Count;
        var xs = new int[n];
        var ys = new int[n];
        var zs = new int[n];
        var bs = new ushort[n];
        int i = 0;
        foreach (var kv in s.Cells)
        {
            xs[i] = kv.Key.X;
            ys[i] = kv.Key.Y;
            zs[i] = kv.Key.Z;
            bs[i] = kv.Value.Value;
            i++;
        }

        Send(session, new SpaceShipDesign
        {
            Id = s.Id,
            Kind = kindOverride ?? s.Kind,
            PosX = s.Position.X,
            PosY = s.Position.Y,
            PosZ = s.Position.Z,
            Width = s.Width,
            Height = s.Height,
            Length = s.Length,
            X = xs,
            Y = ys,
            Z = zs,
            Block = bs,
        });
    }

    // ---------------- item 20 S3: voxel ore asteroids ----------------

    private const int AsteroidVoxelRadius = 2; // a ~5-block rough sphere of ore

    /// <summary>Builds a small voxel ore body (a rough sphere of iron/copper/titanium ore + stone) centred on the
    /// origin, for an in-space asteroid (item 20 S3). The structure rides at <paramref name="worldPos"/>.</summary>
    private SpaceStructure MakeAsteroidStructure(string id, Vector3f worldPos)
    {
        var iron = _content.GetBlock("iron_ore")?.NumericId ?? BlockId.Air;
        var copper = _content.GetBlock("copper_ore")?.NumericId ?? iron;
        var titanium = _content.GetBlock("titanium_ore")?.NumericId ?? iron;
        var stone = _content.GetBlock("stone")?.NumericId ?? iron;

        var s = new SpaceStructure { Id = id, Kind = "asteroid", OwnerId = string.Empty, Position = worldPos };
        int r = AsteroidVoxelRadius;
        int rSq = r * r;
        for (int x = -r; x <= r; x++)
        for (int y = -r; y <= r; y++)
        for (int z = -r; z <= r; z++)
        {
            int dSq = x * x + y * y + z * z;
            if (dSq > rSq)
            {
                continue; // carve to a rough sphere
            }

            // A titanium core, an iron/copper shell, a little stone — a deterministic veined mix.
            BlockId block;
            if (dSq <= 1) { block = titanium; }
            else if (((x + y + z) & 3) == 0) { block = copper; }
            else if (((x + y + z) & 1) == 0) { block = stone; }
            else { block = iron; }

            s.Set(new Vector3i(x, y, z), block);
        }

        s.Width = s.Height = s.Length = r * 2 + 1;
        return s;
    }

    /// <summary>Spawns one asteroid: a combat entity (for ship targeting/firing + respawn accounting) paired with
    /// a voxel ore structure of the same id (for rendering + EVA mining) — item 20 S3. The entity's hull tracks
    /// its block count so laser fire carves the rock down as it depletes.</summary>
    private void SpawnAsteroid(SpaceInstance instance, Vector3f pos, bool broadcast)
    {
        var entity = new CombatEntity
        {
            Id = NextEntityId(),
            Kind = CombatEntityKind.Asteroid,
            Hostile = false,
            AsteroidTier = 0, // voxel asteroids don't split — they carve + deplete
            Position = pos,
            Loot = { new ItemAmount("iron_ore", 5), new ItemAmount("titanium_ore", 2) },
        };

        var s = MakeAsteroidStructure(entity.Id, pos);
        entity.HullMax = entity.Hull = System.Math.Max(8, s.Cells.Count); // hull == blocks → carve maps to damage
        instance.Entities.Add(entity);
        instance.Structures[s.Id] = s;

        if (broadcast)
        {
            foreach (var pid in instance.Players)
            {
                if (FindSessionByPlayerId(pid) is { } session)
                {
                    SendShipDesign(session, s);
                }
            }

            BroadcastSpaceState(instance);
        }
    }

    /// <summary>Carves a voxel asteroid's blocks to match its remaining hull fraction after a laser hit (item 20
    /// S3) — removes the outermost blocks first so the rock visibly shrinks, broadcasting each removal.</summary>
    private void CarveAsteroidToHull(SpaceInstance instance, CombatEntity asteroid)
    {
        if (!instance.Structures.TryGetValue(asteroid.Id, out var s) || s.Cells.Count == 0)
        {
            return;
        }

        // Blocks remaining should track the hull fraction (HullMax == the original block count by construction).
        int target = asteroid.HullMax > 0f
            ? (int)System.Math.Round(asteroid.HullMax * System.Math.Max(0f, asteroid.Hull) / asteroid.HullMax)
            : 0;

        if (s.Cells.Count <= target)
        {
            return;
        }

        // Remove outermost cells first (largest distance from the centre) for a clean shrink.
        var ordered = new List<Vector3i>(s.Cells.Keys);
        ordered.Sort((a, b) => (b.X * b.X + b.Y * b.Y + b.Z * b.Z).CompareTo(a.X * a.X + a.Y * a.Y + a.Z * a.Z));
        int remove = s.Cells.Count - target;
        for (int i = 0; i < remove && i < ordered.Count; i++)
        {
            var c = ordered[i];
            s.Set(c, BlockId.Air);
            BroadcastToInstance(instance, new StructureBlockChanged
            {
                StructureId = s.Id, X = c.X, Y = c.Y, Z = c.Z, Block = BlockId.AirValue,
            });
        }
    }

    /// <summary>Removes a depleted/destroyed asteroid's voxel structure + tells clients to drop its mesh (item 20
    /// S3). The paired combat entity removal + loot are handled by the caller.</summary>
    private void RemoveAsteroidStructure(SpaceInstance instance, string id)
    {
        if (instance.Structures.Remove(id))
        {
            BroadcastToInstance(instance, new SpaceEntityDestroyed { Id = id });
        }
    }
}
