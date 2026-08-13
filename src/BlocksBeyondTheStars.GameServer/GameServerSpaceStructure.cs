// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.WorldGeneration;

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

    /// <summary>Per-cell dye/glow colour (0xRRGGBB each; 0 = none) for cells that carry one. Authored ship
    /// designs (the ship editor) populate these; plain hulls leave them empty.</summary>
    public Dictionary<Vector3i, (int Tint, int Glow)> Mods { get; } = new();

    /// <summary>Per-cell packed shape + orientation (<c>ShapeCode.Pack</c>; 0 = cube) for shaped cells.</summary>
    public Dictionary<Vector3i, int> Shapes { get; } = new();

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
            Mods.Remove(pos);
            Shapes.Remove(pos);
        }
        else
        {
            Cells[pos] = block;
        }
    }

    /// <summary>Sets a block together with its per-cell dye/glow + shape (authored ship cells).</summary>
    public void Set(Vector3i pos, BlockId block, int tint, int glow, int shape)
    {
        Set(pos, block);
        if (block.IsAir)
        {
            return;
        }

        if (tint != 0 || glow != 0)
        {
            Mods[pos] = (tint, glow);
        }
        else
        {
            Mods.Remove(pos);
        }

        if (shape != 0)
        {
            Shapes[pos] = shape;
        }
        else
        {
            Shapes.Remove(pos);
        }
    }

    public BlockId Get(Vector3i pos) => Cells.TryGetValue(pos, out var b) ? b : BlockId.Air;
}

public sealed partial class GameServer
{
    /// <summary>S5: how close the suit must be to a static structure (asteroid/station) to mine/build on it —
    /// a coarse anti-grief range so you can't edit a body across the flight zone.</summary>
    private const float StructureEditRange = 40f;

    /// <summary>#685: per-cell EVA mining progress on voxel structures (asteroid ore), keyed by structure id +
    /// structure-local cell — the structure-space analogue of <c>_miningProgress</c>. An entry clears when its
    /// cell breaks or its structure is removed; a changed block id restarts from zero (same guard as the world
    /// path, B52).</summary>
    private readonly Dictionary<(string Id, Vector3i Cell), (ushort Block, float Progress)> _structureMiningProgress = new();

    /// <summary>Builds the served player's ship as a voxel <see cref="SpaceStructure"/> from its editor design
    /// (item 20, S1). Mirrors the block mapping the ground stamp uses (<see cref="StampShipLayout"/>) but writes
    /// into a standalone sparse grid instead of the planet world. Hatch/door cells render as holes. Ships with
    /// no designed layout fall back to a hollow hull box derived from the design's interior dimensions.</summary>
    private SpaceStructure BuildShipStructure(string ownerId)
        => _ship.IsCustom
            ? BuildCustomShipStructure("ship:" + ownerId, ownerId, _ship, commissioned: true, applyEdits: true)
            : BuildShipStructureFrom("ship:" + ownerId, ownerId,
                _content.GetShip(_ship.ShipType) ?? _content.GetShip("starter"), persistEdits: true);

    /// <summary>Builds a peaceful NPC trader's ship as a voxel structure straight from a ship-type key — no
    /// player owner, no persisted edits. Reuses the exact player-ship voxel pipeline so a trader renders 1:1
    /// as the real in-game ship of that type; new ship types in <c>data/ships.json</c> are picked up
    /// automatically (the NPC selection enumerates <see cref="GameContent.Ships"/>).</summary>
    private SpaceStructure BuildNpcShipStructure(string structureId, string shipTypeKey)
        => BuildShipStructureFrom(structureId, string.Empty,
            _content.GetShip(shipTypeKey) ?? _content.GetShip("starter"), persistEdits: false);

    private SpaceStructure BuildShipStructureFrom(string structureId, string ownerId, ShipDefinition? design, bool persistEdits)
    {
        var s = new SpaceStructure { Id = structureId, Kind = "ship", OwnerId = ownerId };

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
                    case "door_energy": // ship doors all register as energy doors anyway (item 35)
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
                // Authored dye/glow/shape ride along (the ship editor can tint + shape + orient any block).
                s.Set(p, _content.GetBlock(cell.Id)?.NumericId ?? wall, cell.Tint, cell.Glow, cell.Shape);
            }

            // Guarantee a flush, solid floor across the footprint (fills layout gaps) so the player never
            // falls out of the walkable interior — mirrors the old stamped-ship floor guarantee. Only
            // columns the hull actually encloses (a floor or roof cell) get filled: a non-rectangular plan
            // (the hammerhead's T-shape) must not grow floating floor tiles in the notches of its bounding
            // rect, and an exterior attachment (a nav light on a flank) must not grow one under itself.
            var footprint = new HashSet<(int X, int Z)>();
            foreach (var cell in layout.Cells)
            {
                if ((cell.Y == 0 || cell.Y == layout.Height) &&
                    cell.X >= 0 && cell.X < layout.Width && cell.Z >= 0 && cell.Z < layout.Length)
                {
                    footprint.Add((cell.X, cell.Z));
                }
            }

            for (int fx = 0; fx < layout.Width; fx++)
                for (int fz = 0; fz < layout.Length; fz++)
                {
                    var fp = new Vector3i(fx, 0, fz);
                    if (footprint.Contains((fx, fz)) && s.Get(fp).IsAir)
                    {
                        s.Set(fp, wall);
                    }
                }

            FinishShipStructure(s, persistEdits);
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

                    // Rear hatch opening (a slide door fills it, see DoorCells below). THREE tall like every
                    // settlement doorway: the player capsule is 1.88 m and the grounded step-up sweep
                    // (stepOffset 0.6) needs that much headroom on top — a 2-tall hatch jammed the walk-out
                    // against the lintel until the player jumped (#211). Very low hulls keep a 2-tall hatch
                    // rather than cutting into the roof rim.
                    int doorTop = System.Math.Min(3, height - 1);
                    bool door = z == 0 && (x == halfX - 1 || x == halfX || x == halfX + 1) && y >= 1 && y <= doorTop;
                    if (door)
                    {
                        continue;
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

        // Keep the two rows just inside the hatch (z=1,2) free of station blocks: stations stamp as solid,
        // knee-high marker blocks, and medbay/quarters in the rear corners (z=1) flanked the 3-wide doorway,
        // leaving a single 1-wide lane the 0.86-wide player capsule snagged on unless dead-centred (#211).
        int rearRow = System.Math.Min(halfZ + 1, halfZ * 2 - 2); // deeper in the cabin, clear of the lab/console row
        BoxStation("medbay", 1, rearRow);
        BoxStation("cockpit", halfX, halfZ * 2 - 1);
        BoxStation("workshop", halfX * 2 - 1, halfZ);
        BoxStation("cargo", 1, halfZ);
        BoxStation("quarters", halfX * 2 - 1, rearRow);
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

            // Rear engine nozzles (dark), just behind the rear wall at the TRUE corners (x=0 / x=2·halfX).
            // They must stay clear of the 3-wide rear hatch gap (x = halfX-1 .. halfX+1): at halfX∓1 they sat
            // inside the doorway on a 5-wide hull, pinching the exit to a single centre lane the player had to
            // hit exactly or jump (#181, #211).
            s.Set(new Vector3i(sgn < 0 ? 0 : halfX * 2, 1, -1), dark);

            // Wingtip nav lights: red to port (-X), green to starboard (+X).
            s.Set(new Vector3i(sgn < 0 ? -2 : halfX * 2 + 2, wingY, halfZ), sgn < 0 ? lightR : lightG);
        }

        // Raised glass cockpit canopy on top toward the front.
        s.Set(new Vector3i(cx, height + 1, halfZ * 2 - 1), glass);
        s.Set(new Vector3i(cx, height + 1, halfZ * 2 - 2), glass);

        FinishShipStructure(s, persistEdits);
        return s;
    }

    /// <summary>Common ship-structure finish: paints the per-room floor accents, hangs the per-room ceiling
    /// lamps, snapshots the protected design baseline (hull + modules — never minable on foot), then (for a
    /// player's own ship) applies the player's persisted edits on top (added blocks; in-space EVA hull
    /// repairs/removals). NPC trader ships pass <paramref name="persistEdits"/> = false: they have no owner
    /// and no per-cell deltas to load.</summary>
    private void FinishShipStructure(SpaceStructure s, bool persistEdits = true)
    {
        PaintStructureAccents(s);
        PlaceInteriorLights(s);
        s.Baseline.Clear();
        s.Baseline.UnionWith(s.Cells.Keys);
        if (persistEdits)
        {
            ApplyPersistedShipEdits(s);
        }
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

    /// <summary>Interior lighting pass: hangs a ceiling lamp over every station marker, so each room of a
    /// multi-room ship carries its own light source. Before this, NO authored layout held a single interior
    /// light — every light cell in the layouts is an exterior nav light — and a room only looked lit where
    /// that nav-light glow happened to bleed in through a window (glass passes the mesher's propagated block
    /// light), which is why the Hammerhead's bridge was lit and its rear compartments were not (#776).
    /// Station cells are the ship's room anchors (the accent pass above keys off the same list), so this
    /// covers every authored layout, the code-box starter hull and any ship a player builds in the editor,
    /// without touching the hand-tuned layout JSON. Only ever fills AIR: hull, cargo and stations stay put.</summary>
    private void PlaceInteriorLights(SpaceStructure s)
    {
        if (_content.GetBlock("light_white") is not { } lamp)
        {
            return;
        }

        const int MaxRoomHeight = 8; // bounds the ceiling scan on an open-topped or malformed hull

        foreach (var (_, cell) in s.StationCells)
        {
            // Scan up to this room's ceiling and hang the lamp in the air cell right below it. Scanning beats
            // using the structure height: it stays correct under a low wing, a raised canopy or a second deck.
            int y = cell.Y;
            while (y < cell.Y + MaxRoomHeight && s.Get(new Vector3i(cell.X, y + 1, cell.Z)).IsAir)
            {
                y++;
            }

            // Needs real headroom: the player capsule is 1.88 m, so a lamp at the station's own head height
            // would sit in the walkway. A room that low simply keeps the flat indoor fill instead.
            var lampCell = new Vector3i(cell.X, y, cell.Z);
            if (y <= cell.Y + 1 || !s.Get(lampCell).IsAir)
            {
                continue;
            }

            s.Set(lampCell, lamp.NumericId);
        }
    }

    /// <summary>Re-applies the player's persisted hull edits (item 20 S4 durable save) on top of the
    /// freshly rebuilt ship voxel baseline, so mined-out / built-on cells survive a server restart and
    /// re-entry into space — and, ship-as-object, carry into the landed ship + walkable interior too.
    /// Only player deltas are stored (mirrors the per-cell planet block-edit model), keeping it
    /// lightweight. An edit setting a cell to air is honoured via <see cref="SpaceStructure.Set"/>.</summary>
    private void ApplyPersistedShipEdits(SpaceStructure s)
    {
        foreach (var edit in _repo.LoadStructureEdits(StructureEditStoreId(s)))
        {
            s.Set(edit.WorldPosition, new BlockId(edit.Block));
        }
    }

    /// <summary>Persistence id of a ship structure's per-cell deltas. Historically all of a player's ships
    /// shared one delta set under the runtime structure id ("ship:&lt;pid&gt;") — switching ships re-applied
    /// the same edits onto a different design. Deltas are now scoped per SHIP: the fleet's "default" ship
    /// keeps the legacy id (old saves keep their edits), every other ship gets its own
    /// "ship:&lt;pid&gt;#&lt;shipId&gt;" row.</summary>
    private string StructureEditStoreId(SpaceStructure s)
    {
        if (s.Kind != "ship" || string.IsNullOrEmpty(s.OwnerId))
        {
            return s.Id;
        }

        string shipId = FindSessionByPlayerId(s.OwnerId)?.ActiveShipId ?? ShipId;
        return shipId == ShipId ? s.Id : s.Id + "#" + shipId;
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
            Reject(session, "structure", "@srv.structure.eva_only");
            return;
        }

        if (!instance.Structures.TryGetValue(intent.StructureId, out var s))
        {
            Reject(session, "structure", "@srv.structure.none");
            return;
        }

        // S2/S3: you may mine your OWN ship or any asteroid; placing is only on your own ship. Other players'
        // ships + game stations stay protected (S5).
        bool isAsteroid = s.Kind == "asteroid";
        bool isOwn = s.OwnerId == p.PlayerId; // your own ship or your own station
        // Allies co-own each other's STATIONS (build/mine the floating structure), but never each other's ship —
        // the ship stays owner-only by design, so the alliance grant is scoped to station structures.
        bool isAlliedStation = !isOwn && s.Kind == "station" && AreAllied(s.OwnerId, p.PlayerId);
        if (!isAsteroid && !isOwn && !isAlliedStation)
        {
            Reject(session, "structure", "@srv.structure.own_only");
            return;
        }

        // S5 hardening: a static structure (asteroid/station) has a real world position — require the suit to be
        // near it, so you can't mine/build across the whole zone. (The own ship rides the pilot, so skip it.)
        if (s.Kind != "ship")
        {
            var suit = PilotPositionIn(instance, p.PlayerId); // #994: range from THIS pilot's suit/ship
            float ex = suit.X - s.Position.X, ey = suit.Y - s.Position.Y, ez = suit.Z - s.Position.Z;
            if (ex * ex + ey * ey + ez * ez > StructureEditRange * StructureEditRange)
            {
                Reject(session, "structure", "@srv.structure.too_far");
                return;
            }
        }

        var pos = new Vector3i(intent.X, intent.Y, intent.Z);
        if (intent.Mine)
        {
            // Ship MODULES (station markers) are never removable — not even on an EVA hull pass.
            if (s.Kind == "ship" && s.StationCells.Any(sc => sc.Cell == pos))
            {
                Reject(session, "structure", "@srv.structure.module_fixed");
                return;
            }

            var existing = s.Get(pos);
            if (existing.IsAir)
            {
                Reject(session, "structure", "@srv.structure.nothing");
                return;
            }

            var def = _content.BlockById(existing);

            // #685: asteroid ore obeys the same rules as planet mining — tool gating + hardness accumulated
            // over several hits (a single click used to pop ANY block bare-handed, titanium included). Own-ship
            // and station editing stays instant by design: that is construction UX, not resource gathering.
            if (isAsteroid)
            {
                if (def is null || !def.Mineable)
                {
                    Reject(session, "structure", "@srv.mine.not_mineable");
                    return;
                }

                var tool = ActiveTool(p);
                if (!ToolCanMine(tool, def))
                {
                    Reject(session, "structure", "@srv.mine.wrong_tool");
                    return;
                }

                // Powered drills draw suit energy per swing on asteroid ore too (#796) — same rule as
                // planet mining. Own-ship and station editing below stays free: construction, not mining.
                if (tool.EnergyPerUse > 0f)
                {
                    if (p.SuitEnergy < tool.EnergyPerUse)
                    {
                        Reject(session, "structure", "@no_energy");
                        return;
                    }

                    p.SuitEnergy -= tool.EnergyPerUse;
                    SendPlayerState(session);
                }

                float hardness = System.Math.Max(0.2f, def.Hardness);
                float power = tool.MiningPower > 0f ? tool.MiningPower : 1f;
                float prior = _structureMiningProgress.TryGetValue((s.Id, pos), out var prev) && prev.Block == existing.Value
                    ? prev.Progress
                    : 0f;
                float progress = prior + power;
                if (progress + 0.0001f < hardness)
                {
                    _structureMiningProgress[(s.Id, pos)] = (existing.Value, progress);
                    Send(session, new StructureMiningProgress
                    {
                        StructureId = s.Id,
                        X = pos.X,
                        Y = pos.Y,
                        Z = pos.Z,
                        Fraction = progress / hardness,
                    });
                    return;
                }
            }

            // #685 (mirrors #600): make sure the drops fit BEFORE clearing the cell — the cell used to be
            // cleared first, so a full inventory silently ate the ore. Refusing the break is the lossless
            // option; accumulated progress stays banked, so the block falls on the next hit once there is room.
            var pool = new MaterialPool(_content, p, _ship);
            if (def is { Drops.Count: > 0 } && !pool.CanFit(def.Drops))
            {
                Reject(session, "structure", "@inventory_full");
                return;
            }

            _structureMiningProgress.Remove((s.Id, pos));
            s.Set(pos, BlockId.Air);

            // item 20 S4 durable save: a hull cell the owner mined out persists as a per-cell delta (only
            // player changes are stored), so the edit survives a server restart + re-entry into space.
            if (s.Kind == "ship")
            {
                _repo.SetStructureBlock(StructureEditStoreId(s), pos, BlockId.AirValue);
            }

            // Bank the mined block's drops (ore from asteroids; rebuild materials from a ship hull) — the
            // capacity was checked above, so nothing is lost here.
            if (def is { Drops.Count: > 0 })
            {
                BankLoot(session, pool, def.Drops);
                SendInventory(session);
            }

            BroadcastToInstance(instance, new StructureBlockChanged
            {
                StructureId = s.Id,
                X = pos.X,
                Y = pos.Y,
                Z = pos.Z,
                Block = BlockId.AirValue,
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
            Reject(session, "structure", "@srv.structure.no_asteroid");
            return;
        }

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

        if (!s.Get(pos).IsAir)
        {
            Reject(session, "structure", "@srv.place.not_empty");
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

        s.Set(pos, blockDef.NumericId);

        // item 20 S4 durable save: a hull cell the owner built persists as a per-cell delta (own ship only;
        // a station's whole build is persisted via PersistStation below).
        if (s.Kind == "ship")
        {
            _repo.SetStructureBlock(StructureEditStoreId(s), pos, blockDef.NumericId.Value);
        }

        BroadcastToInstance(instance, new StructureBlockChanged
        {
            StructureId = s.Id,
            X = pos.X,
            Y = pos.Y,
            Z = pos.Z,
            Block = blockDef.NumericId.Value,
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

        // The player's construction site (#948) has its own ruleset: no design baseline yet, the size cap
        // replaces the design box, and every change persists into the ship's cell blob.
        if (intent.StructureId == ConstructionStructureId(p.PlayerId))
        {
            if (ConstructionFor(p.PlayerId) is { } build)
            {
                HandleConstructionEdit(session, build, intent);
            }
            else
            {
                Reject(session, "structure", "@srv.structure.none_here");
            }

            return;
        }

        var rec = _worlds.Active.LandedFor(p.PlayerId);
        if (!rec.Placed || rec.Structure.Id != intent.StructureId)
        {
            Reject(session, "structure", "@srv.structure.none_here");
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
            Reject(session, "structure", "@srv.structure.far");
            return;
        }

        if (intent.Mine)
        {
            if (s.Baseline.Contains(pos))
            {
                // A self-built ship stays the player's own design: mining a hull cell is a DESIGN change
                // that leaves the persisted blob (repair never rebuilds it), not damage (#948).
                if (_ship.IsCustom)
                {
                    HandleCustomShipMine(session, rec, pos);
                    return;
                }

                Reject(session, "structure", s.StationCells.Any(sc => sc.Cell == pos)
                    ? "@srv.structure.module_fixed"
                    : "@srv.structure.hull_protected");
                return;
            }

            var existing = s.Get(pos);
            if (existing.IsAir)
            {
                Reject(session, "structure", "@srv.structure.nothing");
                return;
            }

            s.Set(pos, BlockId.Air);
            _repo.SetStructureBlock(StructureEditStoreId(s), pos, BlockId.AirValue);

            if (_content.BlockById(existing) is { } def && def.Drops.Count > 0)
            {
                var pool = new MaterialPool(_content, p, _ship);
                BankLoot(session, pool, def.Drops); // cell already cleared — warn if the drop cannot be stored
                SendInventory(session);
            }

            BroadcastToWorld(new StructureBlockChanged
            {
                StructureId = s.Id,
                X = pos.X,
                Y = pos.Y,
                Z = pos.Z,
                Block = BlockId.AirValue,
            });
            return;
        }

        // Place: only into free space INSIDE the ship bounds, attached to something (no floating junk).
        if (pos.X < 0 || pos.X >= s.Width || pos.Y < 0 || pos.Y > s.Height || pos.Z < 0 || pos.Z >= s.Length)
        {
            Reject(session, "structure", "@srv.structure.inside_only");
            return;
        }

        if (!s.Get(pos).IsAir)
        {
            Reject(session, "structure", "@srv.place.not_empty");
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
            Reject(session, "structure", "@srv.structure.no_anchor");
            return;
        }

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

        // A self-built ship keeps exactly one helm (the unambiguous commissioning/cockpit anchor, #950) —
        // checked before any material is consumed.
        if (_ship.IsCustom && blockDef.Key == ShipHelmBlock
            && ParseCustomCells(_ship.BuiltCells).Values.Any(b => IsBlockId(b, ShipHelmBlock)))
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

        // A self-built ship's on-foot placement is a DESIGN change: it goes into the persisted cell blob
        // (doors become door cells, engines change the derived stats), not into the damage-delta store.
        if (_ship.IsCustom)
        {
            var cells = ParseCustomCells(_ship.BuiltCells);
            cells[pos] = blockDef.NumericId;
            CommitCustomShipCells(session, _ship, rec, commissioned: true, cells, pos,
                IsDoorBlock(blockDef.Key) ? BlockId.AirValue : blockDef.NumericId.Value);
            return;
        }

        s.Set(pos, blockDef.NumericId);
        _repo.SetStructureBlock(StructureEditStoreId(s), pos, blockDef.NumericId.Value);
        BroadcastToWorld(new StructureBlockChanged
        {
            StructureId = s.Id,
            X = pos.X,
            Y = pos.Y,
            Z = pos.Z,
            Block = blockDef.NumericId.Value,
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

    /// <summary>Test/inspection: the block id at a cell of ANY structure (by id) across the space instances —
    /// for asserting asteroid family compositions (#687). Air if there is no such structure/cell.</summary>
    public ushort StructureCellForTest(string structureId, int x, int y, int z)
    {
        foreach (var inst in _spaceInstances.Values)
        {
            if (inst.Structures.TryGetValue(structureId, out var s))
            {
                return s.Get(new Vector3i(x, y, z)).Value;
            }
        }

        return BlockId.AirValue;
    }

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
    private void SendShipDesign(PlayerSession session, SpaceStructure s, string? kindOverride = null)
    {
        int n = s.Cells.Count;
        var xs = new int[n];
        var ys = new int[n];
        var zs = new int[n];
        var bs = new ushort[n];
        // Modifier arrays are emitted only when the design actually carries dye/glow/shape (authored ships),
        // so plain hulls stay as compact as before.
        bool anyMods = s.Mods.Count > 0 || s.Shapes.Count > 0;
        var tints = anyMods ? new int[n] : System.Array.Empty<int>();
        var glows = anyMods ? new int[n] : System.Array.Empty<int>();
        var shapes = anyMods ? new int[n] : System.Array.Empty<int>();
        int i = 0;
        foreach (var kv in s.Cells)
        {
            xs[i] = kv.Key.X;
            ys[i] = kv.Key.Y;
            zs[i] = kv.Key.Z;
            bs[i] = kv.Value.Value;
            if (anyMods)
            {
                if (s.Mods.TryGetValue(kv.Key, out var m)) { tints[i] = m.Tint; glows[i] = m.Glow; }
                if (s.Shapes.TryGetValue(kv.Key, out var sh)) { shapes[i] = sh; }
            }

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
            Tint = tints,
            Glow = glows,
            Shape = shapes,
        });
    }

    // ---------------- item 20 S3 + #687: voxel ore asteroids ----------------

    private const int AsteroidVoxelRadius = 2; // the classic rock: a ~5-block rough sphere of ore

    /// <summary>#687: the mineable space rocks roll a seeded FAMILY (which minerals they carry). Weights make
    /// stony rocks common and crystal ones rare; icy rocks are water-ice deposits. Mirrors the landable
    /// asteroid families (#515).</summary>
    private static readonly (string Family, int Weight)[] AsteroidFamilies =
    {
        ("stony", 40),
        ("metallic", 25),
        ("icy", 20),
        ("carbonaceous", 10),
        ("crystalline", 5),
    };

    /// <summary>#687: the size roll (voxel sphere radius) — pebbles are common, big boulders rare.</summary>
    private static readonly (int Radius, int Weight)[] AsteroidSizes =
    {
        (1, 45),
        (2, 40),
        (3, 15),
    };

    /// <summary>Small deterministic xorshift (#687): the asteroid roll must be identical across restarts and
    /// platforms, so no <c>Random</c>, no <c>string.GetHashCode</c> (randomized per process) and no
    /// trig-derived floats (libm differs between Windows and Linux).</summary>
    private static uint NextAsteroidRand(ref uint state)
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return state;
    }

    private static T PickWeighted<T>((T Value, int Weight)[] table, ref uint state)
    {
        int total = table.Sum(e => e.Weight);
        int roll = (int)(NextAsteroidRand(ref state) % (uint)total);
        foreach (var (value, weight) in table)
        {
            roll -= weight;
            if (roll < 0)
            {
                return value;
            }
        }

        return table[^1].Value;
    }

    /// <summary>Builds a small voxel ore body — a rough sphere of the family's minerals around its core — for
    /// an in-space asteroid (item 20 S3; families + sizes #687). The structure rides at
    /// <paramref name="worldPos"/>.</summary>
    private SpaceStructure MakeAsteroidStructure(string id, Vector3f worldPos, string family, int r)
    {
        var stone = _content.GetBlock("stone")?.NumericId ?? BlockId.Air;
        BlockId B(string key) => _content.GetBlock(key)?.NumericId ?? stone;

        // Per family: the core mineral + the shell mix, indexed by the same deterministic vein pattern for
        // every family ((x+y+z) parity). The "metallic" row is the classic pre-#687 rock, byte-identical.
        var (core, vein, even, odd) = family switch
        {
            "metallic" => (B("titanium_ore"), B("copper_ore"), stone, B("iron_ore")),
            "icy" => (stone, stone, B("ice"), B("ice")), // a rocky heart under hand-mineable water ice
            "carbonaceous" => (B("carbon"), stone, B("carbon"), B("carbon")),
            "crystalline" => (B("crystal"), B("crystal"), stone, stone),
            _ => (B("iron_ore"), B("iron_ore"), stone, stone), // stony: iron veins in plain rock
        };

        var s = new SpaceStructure { Id = id, Kind = "asteroid", OwnerId = string.Empty, Position = worldPos };
        int rSq = r * r;
        int coreSq = r <= 1 ? 0 : r == 2 ? 1 : 2; // the core grows a little with the rock
        for (int x = -r; x <= r; x++)
            for (int y = -r; y <= r; y++)
                for (int z = -r; z <= r; z++)
                {
                    int dSq = x * x + y * y + z * z;
                    if (dSq > rSq)
                    {
                        continue; // carve to a rough sphere
                    }

                    BlockId block;
                    if (dSq <= coreSq) { block = core; }
                    else if (((x + y + z) & 3) == 0) { block = vein; }
                    else if (((x + y + z) & 1) == 0) { block = even; }
                    else { block = odd; }

                    s.Set(new Vector3i(x, y, z), block);
                }

        s.Width = s.Height = s.Length = r * 2 + 1;
        return s;
    }

    /// <summary>#687: what a shot-down asteroid bursts into, by family — bigger rocks pay out more. (EVA
    /// mining ignores this and yields per-block drops instead, #685.)</summary>
    private static List<ItemAmount> AsteroidLoot(string family, int radius)
    {
        bool big = radius >= 3;
        return family switch
        {
            "icy" => new() { new ItemAmount("ice", big ? 10 : 6) },
            "carbonaceous" => new() { new ItemAmount("carbon", big ? 8 : 5) },
            "crystalline" => new() { new ItemAmount("crystal", big ? 5 : 3) },
            "stony" => new() { new ItemAmount("iron_ore", big ? 7 : 4) },
            _ => new()
            {
                new ItemAmount("iron_ore", big ? 8 : 5),
                new ItemAmount("titanium_ore", big ? 4 : 2),
            },
        };
    }

    /// <summary>Spawns one asteroid: a combat entity (for ship targeting/firing + respawn accounting) paired with
    /// a voxel ore structure of the same id (for rendering + EVA mining) — item 20 S3. The entity's hull tracks
    /// its block count so laser fire carves the rock down as it depletes. #687: the family/size roll is seeded
    /// from the world's saved seed + instance id + spawn <paramref name="ordinal"/>, so the same world always
    /// grows the same rocks (#719: the SAVED seed, not the launch config's — those differ on name-seeded worlds);
    /// ordinal 0 is pinned to the classic metallic r=2 rock so every field guarantees one titanium core
    /// (mirrors the start-planet ring pin).</summary>
    private void SpawnAsteroid(SpaceInstance instance, Vector3f pos, int ordinal, bool broadcast)
    {
        string family = "metallic";
        int radius = AsteroidVoxelRadius;
        if (ordinal > 0)
        {
            long h = WorldGenerator.StableHash(instance.Id);
            uint state = (uint)(h ^ (h >> 32) ^ (_meta.Seed * 397L) ^ (ordinal * 668265263L)) | 1u;
            family = PickWeighted(AsteroidFamilies, ref state);
            radius = PickWeighted(AsteroidSizes, ref state);
        }

        var entity = new CombatEntity
        {
            Id = NextEntityId(),
            Kind = CombatEntityKind.Asteroid,
            Hostile = false,
            AsteroidTier = 0, // voxel asteroids don't split — they carve + deplete
            Position = pos,
        };
        foreach (var drop in AsteroidLoot(family, radius))
        {
            entity.Loot.Add(drop);
        }

        var s = MakeAsteroidStructure(entity.Id, pos, family, radius);
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
                StructureId = s.Id,
                X = c.X,
                Y = c.Y,
                Z = c.Z,
                Block = BlockId.AirValue,
            });
        }
    }

    /// <summary>Removes a depleted/destroyed asteroid's voxel structure + tells clients to drop its mesh (item 20
    /// S3). The paired combat entity removal + loot are handled by the caller.</summary>
    private void RemoveAsteroidStructure(SpaceInstance instance, string id)
    {
        if (instance.Structures.Remove(id))
        {
            // #685: drop any banked mining progress with the rock, so a respawned structure that happens to
            // reuse the id never inherits half-mined cells.
            foreach (var key in _structureMiningProgress.Keys.Where(k => k.Id == id).ToList())
            {
                _structureMiningProgress.Remove(key);
            }

            BroadcastToInstance(instance, new SpaceEntityDestroyed { Id = id });
        }
    }
}
