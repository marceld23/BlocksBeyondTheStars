// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>An intact door of a derelict (#2632): the base cell of its three-high opening and the axis its leaf runs
/// along (true = along X). The server hangs it as a door record — airtight, so the room behind it stays closed.</summary>
public readonly struct DerelictDoor
{
    public readonly Vector3i Cell;
    public readonly bool AxisX;

    public DerelictDoor(Vector3i cell, bool axisX)
    {
        Cell = cell;
        AxisX = axisX;
    }
}

/// <summary>
/// A derelict station's layout (#2632): the decayed voxel grid plus the spots the later phases build on. All
/// coordinates are structure-local cells (0 … Width-1 etc.); the server stamps the grid like a player station's build.
/// <list type="bullet">
/// <item><see cref="CrateSpots"/> — feet cells on a deck for the old supply crates (#2633). Empty floor, nothing placed.</item>
/// <item><see cref="SpawnerSpot"/> — the cell in the middle of a room of decent size (on its deck) where the guardian
/// bay (<c>guardian_spawner</c>, #2635) stands, its front toward the arrival.</item>
/// <item><see cref="TraderPosts"/> — the broken, unstaffed trading posts: plain <c>station_vendor</c> blocks.</item>
/// <item><see cref="Arrival"/> — where a boarder is set down: the deck of the command room (a beta station's hub).</item>
/// </list>
/// </summary>
public sealed class DerelictLayout
{
    private readonly ushort[] _blocks;
    private readonly Dictionary<int, (int Tint, int Glow)> _mods;
    private readonly Dictionary<int, int> _shapes;

    internal DerelictLayout(int width, int height, int length, string sizeTier, string source, ushort[] blocks,
        Dictionary<int, (int Tint, int Glow)> mods, Dictionary<int, int> shapes)
    {
        Width = width;
        Height = height;
        Length = length;
        SizeTier = sizeTier;
        Source = source;
        _blocks = blocks;
        _mods = mods;
        _shapes = shapes;
    }

    public int Width { get; }
    public int Height { get; }
    public int Length { get; }

    /// <summary>The station tier the layout was built at (small / medium / large, or a template's own tier).</summary>
    public string SizeTier { get; }

    /// <summary>Where the layout came from: <c>beta</c> (<see cref="DerelictGenerator.BetaSource"/> — every new derelict
    /// since #2674, the wreck of a beta station), or for a derelict a save stored before that: <c>procedural:&lt;tier&gt;</c>
    /// (the module generator) or <c>template:&lt;key&gt;</c> (one of the classic hand-built stations).</summary>
    public string Source { get; }

    /// <summary>True for the wreck of a beta station (#2674).</summary>
    public bool IsBeta => Source == DerelictGenerator.BetaSource;

    public IReadOnlyList<DerelictDoor> Doors { get; internal set; } = Array.Empty<DerelictDoor>();
    public Vector3i Arrival { get; internal set; }

    /// <summary>True when the arrival sits in a pocket the hull closes completely, judged by the station air rule
    /// (airtight full cubes and doors) — geometry only: no derelict breathes since #2675 (dead life support).</summary>
    public bool ArrivalSealed { get; internal set; }

    public IReadOnlyList<Vector3i> CrateSpots { get; internal set; } = Array.Empty<Vector3i>();
    public Vector3i? SpawnerSpot { get; internal set; }
    public IReadOnlyList<Vector3i> TraderPosts { get; internal set; } = Array.Empty<Vector3i>();

    /// <summary>Rooms of the layout, and how many of them still hold their air / are open to space after the decay.</summary>
    public int RoomCount { get; internal set; }
    public int SealedRoomCount { get; internal set; }
    public int BreachedRoomCount { get; internal set; }

    public bool InBounds(int x, int y, int z) => x >= 0 && y >= 0 && z >= 0 && x < Width && y < Height && z < Length;

    public ushort Get(int x, int y, int z) => InBounds(x, y, z) ? _blocks[Index(x, y, z)] : (ushort)0;

    public (int Tint, int Glow) GetModifier(int x, int y, int z)
        => InBounds(x, y, z) && _mods.TryGetValue(Index(x, y, z), out var m) ? m : (0, 0);

    public int GetShape(int x, int y, int z) => InBounds(x, y, z) && _shapes.TryGetValue(Index(x, y, z), out var s) ? s : 0;

    private int Index(int x, int y, int z) => ((x * Height) + y) * Length + z;
}

/// <summary>
/// Builds a derelict station (#2632, #2674). Since #2674 every new derelict is the wreck of a <b>beta station</b> — the
/// boxy shape every NPC station showed in flight until v2026.9.9 (<see cref="BetaStationGenerator"/>): <see cref="Generate"/>.
/// A derelict a save already stored (entered or edited before #2674) keeps the layout it was built from — one of the
/// procedural module stations (<see cref="StationGenerator.Generate"/>) or a classic hand-built template
/// (<c>data/station_templates.json</c>, the complete, non-kit ones): <see cref="GenerateLegacy"/>, which must keep
/// producing exactly what it always did. Both run the same decay pass, deterministic from the seed:
/// <list type="number">
/// <item>splits the interior into rooms (the doorways are the cuts) and keeps the command room (the arrival) closed;
/// the hangar loses its force field and stands open; other rooms are breached by chance — a torn hole in an outer wall,
/// floor or roof (falling out of it is the point, #2636: a real spacewalk) — never in a way that opens a closed room.
/// No room breathes since #2675 — the life support is dead — but the closed rooms keep their doors and a warm emergency
/// light. (The legacy path still patches a classic template's hall shut at the end, as it did when the arrival held
/// air, so a stored derelict's layout stays the same.)</item>
/// <item>hangs an intact door into every doorway with a closed room on one side (one column walled up — a station door
/// is one block wide), and leaves the doorways between two breached rooms open;</item>
/// <item>tears off random outer panels and greebles, scorches the plating (carbon) — heavily round the breaches —
/// scatters scrap on the decks, turns the lights off in the breached rooms (warm emergency light in the closed ones),
/// and removes every plant, data cache and force field;</item>
/// <item>turns each vendor stall into a broken trading post: a plain <c>station_vendor</c> block in scorch and scrap.
/// No marker is produced — the server never staffs or trades at it;</item>
/// <item>a beta wreck on top: one solar wing half torn off with a ragged, scorched edge and a few of its cells
/// drifting, the mast snapped (its top with the dead beacon floating just beside the stump), rusted patches on the
/// hull and a few loose debris blocks near it.</item>
/// </list>
/// </summary>
public static class DerelictGenerator
{
    /// <summary>Share of derelicts built from a classic template instead of the module generator (legacy path).</summary>
    public const double TemplateShare = 0.2;

    /// <summary>The <see cref="DerelictLayout.Source"/> of a beta-station derelict (#2674).</summary>
    public const string BetaSource = "beta";

    private const int MinRoomCells = 8;

    /// <summary>Generates a new derelict (#2674): the wreck of a beta station.</summary>
    public static DerelictLayout Generate(long seed, GameContent content)
    {
        var rng = new Random(unchecked((int)(seed ^ (seed >> 32)) ^ (int)WorldGenerator.StableHash("derelict-beta")));
        var st = BetaStationGenerator.Generate(seed ^ WorldGenerator.StableHash("derelict-beta-station"), content);
        return Decay(st, content, rng, BetaStationGenerator.SizeTier, BetaSource, hall: false, beta: true);
    }

    /// <summary>Generates a derelict the way it was done before #2674 — for a derelict whose build a save already stored,
    /// so its layout (arrival, crate spots, guardian bay, flight position) matches that build. <paramref name="proceduralOnly"/>
    /// forces a medium module station (it was the guaranteed derelict near the start). Never change what this produces.</summary>
    public static DerelictLayout GenerateLegacy(long seed, GameContent content, bool proceduralOnly = false)
    {
        var rng = new Random(unchecked((int)(seed ^ (seed >> 32)) ^ (int)WorldGenerator.StableHash("derelict-layout")));

        // ---- 1. The source station ----
        var classics = new List<StructureTemplate>();
        foreach (var t in content.StationTemplates)
        {
            if (!t.IsModule && t.Width > 0 && t.Height > 0 && t.Length > 0 && t.Cells.Count > 0)
            {
                classics.Add(t);
            }
        }

        classics.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
        double sourceRoll = rng.NextDouble();
        double tierRoll = rng.NextDouble();
        bool useTemplate = !proceduralOnly && classics.Count > 0 && sourceRoll < TemplateShare;
        StationStructure st;
        string tier;
        string source;
        if (useTemplate)
        {
            int total = 0;
            foreach (var t in classics)
            {
                total += Math.Max(1, t.Weight);
            }

            int pick = (int)(tierRoll * total);
            var chosen = classics[classics.Count - 1];
            foreach (var t in classics)
            {
                pick -= Math.Max(1, t.Weight);
                if (pick < 0)
                {
                    chosen = t;
                    break;
                }
            }

            st = StationGenerator.FromTemplate(chosen, content);
            tier = string.IsNullOrWhiteSpace(chosen.Tier) ? "medium" : chosen.Tier;
            source = "template:" + chosen.Key;
        }
        else
        {
            tier = proceduralOnly ? "medium" : tierRoll < 0.35 ? "small" : tierRoll < 0.8 ? "medium" : "large";
            st = StationGenerator.Generate(tier, seed ^ WorldGenerator.StableHash("derelict-station"), content);
            source = "procedural:" + tier;
        }

        return Decay(st, content, rng, tier, source, hall: useTemplate, beta: false);
    }

    /// <summary>The decay pass over a source station (see the class summary). <paramref name="hall"/>: the source is a
    /// classic template's one hall; <paramref name="beta"/>: the source is a beta station — only then do the beta-only
    /// steps run (the beta wreck damage, the doorway fronts kept clear, the guardian bay out of the open hangar), so the
    /// legacy path draws exactly the random numbers it always drew.</summary>
    private static DerelictLayout Decay(StationStructure st, GameContent content, Random rng, string tier, string source, bool hall, bool beta)
    {
        bool useTemplate = hall;
        var grid = new Grid(st);
        var ids = new Ids(content);
        grid.RemoveFlora(content); // nothing grows any more — and the rooms below are judged without the planters

        // ---- 2. Inside / outside ----
        bool[] exterior = grid.FloodExterior();

        // ---- 3. Doorways (the module generator's cuts; a template's airlock is a door block in its wall) ----
        // A cut lies in a module wall plane: an X-facing wall sits at a module's first or last X column. Judged from the
        // module grid, never from the cells around the cut — a planter or a crate beside a doorway fooled that. A beta
        // station names the wall's axis on the marker itself ("x" / "z", #2674).
        var wallX = new HashSet<int>();
        foreach (var m in st.Modules)
        {
            wallX.Add(m.Origin.X);
            wallX.Add(m.Origin.X + st.RoomW - 1);
        }

        var doorways = new List<Doorway>();
        var doorwayCell = new HashSet<Vector3i>();
        foreach (var m in st.Markers)
        {
            if (m.Type != "door_slide")
            {
                continue;
            }

            var dc = m.LocalPos;
            bool xWall = m.Data == "x" || (m.Data != "z" && wallX.Contains(dc.X));
            var other = xWall ? new Vector3i(dc.X, dc.Y, dc.Z + 1) : new Vector3i(dc.X + 1, dc.Y, dc.Z);
            var d = new Doorway(dc, other, xWall);
            doorways.Add(d);
            foreach (var c in d.Cells())
            {
                doorwayCell.Add(c);
            }
        }

        // ---- 4. Rooms ----
        int[] room = grid.LabelRooms(exterior, doorwayCell, out var roomCells);
        if (useTemplate || roomCells.Count == 0)
        {
            // A classic template is one hall, and it was never built airtight (its stations breathed regardless): the
            // fill runs out of a gap and finds no room, or only a pocket under a dome. Its hall is judged by its roof and
            // deck instead — and since it is the arrival, its gaps are patched shut at the end (SealArrivalPocket, #2636).
            grid.LabelInnerAir(room, roomCells);
        }

        int roomCount = roomCells.Count;

        // The arrival: the generator's spawn marker (the hub's deck), else the middle of the largest room.
        Vector3i arrival = default;
        bool haveArrival = false;
        foreach (var m in st.Markers)
        {
            if (m.Type == "spawn" && grid.InBounds(m.LocalPos.X, m.LocalPos.Y, m.LocalPos.Z))
            {
                arrival = m.LocalPos;
                haveArrival = true;
                break;
            }
        }

        int largest = -1;
        for (int r = 0; r < roomCount; r++)
        {
            if (largest < 0 || roomCells[r].Count > roomCells[largest].Count)
            {
                largest = r;
            }
        }

        if (!haveArrival && largest >= 0)
        {
            arrival = grid.StandableNearCentroid(roomCells[largest], null) ?? roomCells[largest][0];
            haveArrival = true;
        }

        int arrivalRoom = haveArrival ? grid.RoomAt(room, arrival) : -1;
        if (arrivalRoom < 0 && largest >= 0)
        {
            arrivalRoom = largest;
        }

        // ---- 5. Which rooms stay closed (no derelict breathes since #2675, but a closed room keeps its door and light) ----
        var fieldRoom = new bool[roomCount];
        for (int r = 0; r < roomCount; r++)
        {
            foreach (var c in roomCells[r])
            {
                if (grid.TouchesBlock(c, ids.Field))
                {
                    fieldRoom[r] = true;
                    break;
                }
            }
        }

        var breached = new bool[roomCount];
        for (int r = 0; r < roomCount; r++)
        {
            double roll = rng.NextDouble();
            if (r == arrivalRoom)
            {
                // The arrival room stays closed, whatever the source (#2636: a boarder never arrives at a hole).
                // A classic template's hall that leaked from the start (leakyHall) is patched shut at the end
                // (SealArrivalPocket) instead of torn open — its roll is still drawn, so the seeded sequence stays put.
                breached[r] = false;
            }
            else
            {
                breached[r] = fieldRoom[r] || roll < 0.6;
            }
        }

        if (roomCount >= 2 && Array.IndexOf(breached, true) < 0)
        {
            for (int r = 0; r < roomCount; r++)
            {
                if (r != arrivalRoom)
                {
                    breached[r] = true;
                    break;
                }
            }
        }

        // A room with nothing to tear open (an inner room with no outer face) stays closed after all; a closed room
        // protects more walls, so settle that until nothing changes.
        var candidates = new List<Vector3i>[roomCount];
        HashSet<Vector3i> protectedCells;
        bool changed;
        do
        {
            changed = false;
            protectedCells = ProtectedCells(roomCells, breached, doorways);
            for (int r = 0; r < roomCount; r++)
            {
                candidates[r] = new List<Vector3i>();
                if (!breached[r] || fieldRoom[r])
                {
                    continue;
                }

                candidates[r] = grid.BreachCandidates(roomCells[r], exterior, protectedCells);
                if (candidates[r].Count == 0)
                {
                    breached[r] = false;
                    changed = true;
                }
            }
        }
        while (changed);

        // ---- 6. Doors ----
        var doors = new List<DerelictDoor>();
        var doorColumn = new HashSet<Vector3i>();
        foreach (var d in doorways)
        {
            int a = grid.RoomAt(room, d.Side(-1)), b = grid.RoomAt(room, d.Side(+1));
            bool sealedSide = a < 0 || b < 0 || !breached[a] || !breached[b];
            if (!sealedSide)
            {
                continue; // between two breached rooms: the door is long gone
            }

            for (int dy = 0; dy < 3; dy++)
            {
                grid.Set(d.Marker.X, d.Marker.Y + dy, d.Marker.Z, ids.Hull); // a station door is one block wide
                doorColumn.Add(new Vector3i(d.Other.X, d.Other.Y + dy, d.Other.Z));
            }

            doors.Add(new DerelictDoor(d.Other, axisX: !d.XWall));
        }

        protectedCells.UnionWith(doorColumn);
        if (arrivalRoom >= 0 && !breached[arrivalRoom])
        {
            // The arrival column is cleared later (step 11): its deck and walls hold the sealed room even if furniture
            // stood there while the rooms were judged.
            for (int dy = -1; dy <= 2; dy++)
            {
                var c = new Vector3i(arrival.X, arrival.Y + dy, arrival.Z);
                protectedCells.Add(c);
                protectedCells.UnionWith(Neighbours6(c));
            }
        }

        // ---- 7. The breaches ----
        var breachCentres = new List<Vector3i>();
        for (int r = 0; r < roomCount; r++)
        {
            if (!breached[r] || fieldRoom[r] || candidates[r].Count == 0)
            {
                continue;
            }

            var centre = candidates[r][rng.Next(candidates[r].Count)];
            breachCentres.Add(centre);
            double radius = 1.3 + rng.NextDouble() * 1.2;
            grid.Carve(centre, radius, protectedCells, rng);
        }

        // ---- 8. Strip what no longer works, light what still does ----
        var sealedAir = new HashSet<Vector3i>();
        for (int r = 0; r < roomCount; r++)
        {
            if (!breached[r])
            {
                foreach (var c in roomCells[r])
                {
                    sealedAir.Add(c);
                }
            }
        }

        grid.Strip(ids, sealedAir);

        // ---- 9. Torn panels and scorch ----
        grid.TearPanels(exterior, protectedCells, rng);
        grid.Scorch(ids, exterior, breachCentres, rng);

        // ---- 9b. The beta wreck (#2674): torn wing, snapped mast, dead beacon, rust, loose debris ----
        if (beta)
        {
            grid.BetaDamage(ids, exterior, rng);
        }

        // ---- 10. The broken trading posts ----
        var posts = new List<Vector3i>();
        var reserved = new HashSet<Vector3i>(doorwayCell) { arrival, new Vector3i(arrival.X, arrival.Y + 1, arrival.Z) };
        if (beta)
        {
            // A beta station's arms are narrow tubes: nothing (scrap, a crate) may stand right in front of a doorway.
            foreach (var d in doorways)
            {
                foreach (int sign in new[] { -1, 1 })
                {
                    var front = d.Side(sign);
                    var frontOther = d.XWall ? new Vector3i(front.X, front.Y, front.Z + 1) : new Vector3i(front.X + 1, front.Y, front.Z);
                    reserved.Add(front);
                    reserved.Add(frontOther);
                }
            }
        }
        foreach (var m in st.Markers)
        {
            if (m.Type != "vendor" || posts.Count >= 3 || ids.Vendor == 0)
            {
                continue;
            }

            var p = m.LocalPos;
            if (!grid.InBounds(p.X, p.Y, p.Z) || reserved.Contains(p) || posts.Contains(p))
            {
                continue;
            }

            grid.Set(p.X, p.Y, p.Z, ids.Vendor);
            posts.Add(p);
            reserved.Add(p);
            grid.ScorchAt(p.X, p.Y - 1, p.Z, ids);
            for (int i = 0; i < 2; i++)
            {
                grid.ScorchAt(p.X + rng.Next(-1, 2), p.Y - 1, p.Z + rng.Next(-1, 2), ids);
            }

            var side = Neighbours4(p)[rng.Next(4)];
            if (grid.Standable(side.X, side.Y, side.Z) && !reserved.Contains(side) && ids.ScrapPile != 0)
            {
                grid.Set(side.X, side.Y, side.Z, ids.ScrapPile); // what fell off the old stall
                reserved.Add(side);
            }
        }

        // ---- 11. The arrival stands ----
        grid.Set(arrival.X, arrival.Y, arrival.Z, 0);
        grid.Set(arrival.X, arrival.Y + 1, arrival.Z, 0);
        if (!grid.Solid(arrival.X, arrival.Y - 1, arrival.Z))
        {
            grid.Set(arrival.X, arrival.Y - 1, arrival.Z, ids.Hull);
        }

        // ---- 12. Scrap on the decks ----
        for (int r = 0; r < roomCount; r++)
        {
            int pieces = breached[r] ? 2 + rng.Next(3) : rng.Next(2);
            var spots = grid.StandableCells(roomCells[r], reserved, nearArrival: arrival, minArrivalDistance: 2);
            for (int i = 0; i < pieces && spots.Count > 0; i++)
            {
                var c = spots[rng.Next(spots.Count)];
                spots.Remove(c);
                ushort scrap = rng.NextDouble() < 0.5 ? ids.ScrapMetal : ids.ScrapPile;
                if (scrap != 0)
                {
                    grid.Set(c.X, c.Y, c.Z, scrap);
                    reserved.Add(c);
                }
            }
        }

        // ---- 13. Crate spots (#2633) and the spawner spot (#2635) ----
        var crates = new List<Vector3i>();
        var roomOrder = new List<int>();
        for (int r = 0; r < roomCount; r++)
        {
            roomOrder.Add(r);
        }

        roomOrder.Sort((a, b) => breached[a] == breached[b] ? a.CompareTo(b) : breached[a] ? 1 : -1); // sealed rooms first

        // Only rooms a boarder walks into hold crates and the spawner — a module behind a doorway, the arrival room, a
        // template's hall — never the closed pocket under a command dome.
        var walkable = new bool[roomCount];
        foreach (var d in doorways)
        {
            foreach (int sign in new[] { -1, 1 })
            {
                int r = grid.RoomAt(room, d.Side(sign));
                if (r >= 0)
                {
                    walkable[r] = true;
                }
            }
        }

        if (arrivalRoom >= 0)
        {
            walkable[arrivalRoom] = true;
        }

        int crateTarget = Math.Max(3, Math.Min(6, roomCount + 1));
        for (int pass = 0; pass < 4 && crates.Count < crateTarget; pass++)
        {
            foreach (int r in roomOrder)
            {
                if (crates.Count >= crateTarget || !walkable[r])
                {
                    continue;
                }

                var spots = grid.StandableCells(roomCells[r], reserved, nearArrival: arrival, minArrivalDistance: 2);
                OnTheDeck(spots, reserved); // a crate stands on the floor too — never on a scrap pile or another crate
                spots.RemoveAll(c => (pass < 3 && !grid.AgainstWall(c)) || TooClose(c, crates, 2)); // the last pass takes the middle too
                if (spots.Count == 0)
                {
                    continue;
                }

                var spot = spots[rng.Next(spots.Count)];
                crates.Add(spot);
                reserved.Add(spot);
            }
        }

        Vector3i? spawner = null;
        int spawnerRoom = -1;
        for (int r = 0; r < roomCount; r++)
        {
            bool openHangar = beta && fieldRoom[r]; // #2674: never in the hangar — its mouth stands open to space
            if (r != arrivalRoom && walkable[r] && !openHangar && (spawnerRoom < 0 || roomCells[r].Count > roomCells[spawnerRoom].Count))
            {
                spawnerRoom = r;
            }
        }

        if (spawnerRoom < 0)
        {
            spawnerRoom = arrivalRoom;
        }

        if (spawnerRoom >= 0)
        {
            // As far from the arrival as the room allows: four blocks, else two, else any free deck cell.
            foreach (int keepAway in new[] { 4, 2, 0 })
            {
                var spots = grid.StandableCells(roomCells[spawnerRoom], reserved, nearArrival: arrival, minArrivalDistance: keepAway);
                OnTheDeck(spots, reserved); // #2635: the bay stands on the floor, never on a scrap pile or a crate
                spawner = grid.StandableNearCentroid(roomCells[spawnerRoom], spots);
                if (spawner is not null)
                {
                    break;
                }
            }
        }

        if (spawner is { } bay && ids.GuardianBay != 0)
        {
            // #2635: the guardian bay stands on the spot, its glowing front turned toward the arrival — the way a boarder
            // comes in. The server releases the derelict's guardians beside it until somebody mines it.
            grid.Set(bay.X, bay.Y, bay.Z, ids.GuardianBay);
            grid.SetShape(bay.X, bay.Y, bay.Z, CubeFacing.Pack(FaceToward(bay, arrival)));
            reserved.Add(bay);
        }

        // ---- 14. The verdict, as the server's air rule will see it ----
        var doorCells = new HashSet<Vector3i>();
        foreach (var d in doors)
        {
            for (int dy = 0; dy < 3; dy++)
            {
                doorCells.Add(new Vector3i(d.Cell.X, d.Cell.Y + dy, d.Cell.Z));
            }
        }

        // #2636: a classic template's hall was never built airtight, so the legacy path patches the arrival's pocket shut
        // here — after every block the decay writes, judged by the server's own air rule (airtight full cubes and doors).
        // Since #2675 no derelict breathes, so a beta station needs no patch (its hub is closed by construction); the legacy
        // path keeps it, so a stored derelict's layout stays exactly what its build was made from.
        if (!beta && !grid.PocketSealed(arrival, doorCells, ids))
        {
            grid.SealArrivalPocket(arrival, doorCells, ids);
        }

        int sealedRooms = 0, openRooms = 0;
        for (int r = 0; r < roomCount; r++)
        {
            var probe = grid.FirstAir(roomCells[r], doorCells);
            if (probe is null)
            {
                continue;
            }

            if (grid.PocketSealed(probe.Value, doorCells, ids))
            {
                sealedRooms++;
            }
            else
            {
                openRooms++;
            }
        }

        var layout = grid.ToLayout(tier, source);
        layout.Doors = doors;
        layout.Arrival = arrival;
        layout.ArrivalSealed = grid.PocketSealed(arrival, doorCells, ids);
        layout.CrateSpots = crates;
        layout.SpawnerSpot = spawner;
        layout.TraderPosts = posts;
        layout.RoomCount = roomCount;
        layout.SealedRoomCount = sealedRooms;
        layout.BreachedRoomCount = openRooms;
        return layout;
    }

    /// <summary>The cells no breach and no torn panel may remove: every wall cell beside the air of a room that keeps it
    /// (a 6-neighbour — the air fill moves face to face; this covers the deck under the arrival too) and the doorway
    /// frames.</summary>
    private static HashSet<Vector3i> ProtectedCells(List<List<Vector3i>> roomCells, bool[] breached, List<Doorway> doorways)
    {
        var set = new HashSet<Vector3i>();
        for (int r = 0; r < roomCells.Count; r++)
        {
            if (breached[r])
            {
                continue;
            }

            foreach (var c in roomCells[r])
            {
                set.Add(c);
                foreach (var n in Neighbours6(c))
                {
                    set.Add(n);
                }
            }
        }

        foreach (var d in doorways)
        {
            foreach (var c in d.Cells())
            {
                set.Add(c);
                foreach (var n in Neighbours6(c))
                {
                    set.Add(n);
                }
            }
        }

        return set;
    }

    private static bool TooClose(Vector3i c, List<Vector3i> others, int min)
    {
        foreach (var o in others)
        {
            if (Math.Abs(o.X - c.X) < min && Math.Abs(o.Z - c.Z) < min && Math.Abs(o.Y - c.Y) < 2)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Keeps the feet cells that stand on the room's floor (#2635): never one on top of something placed there
    /// (a scrap pile, a post — every such cell is <paramref name="reserved"/>), and only the lowest feet level of the
    /// room, so a cell on a crate or a console does not count as the deck. The room-centre pick weights height, and a
    /// cell one up sat nearer the centre than the floor — the guardian bay stood on a scrap pile, with no floor beside it
    /// for its guardians.</summary>
    private static void OnTheDeck(List<Vector3i> spots, HashSet<Vector3i> reserved)
    {
        spots.RemoveAll(c => reserved.Contains(new Vector3i(c.X, c.Y - 1, c.Z)));
        int floor = int.MaxValue;
        foreach (var c in spots)
        {
            floor = Math.Min(floor, c.Y);
        }

        spots.RemoveAll(c => c.Y != floor);
    }

    /// <summary>The horizontal cube face (2 = +X, 3 = −X, 4 = +Z, 5 = −Z — <see cref="CubeFacing.FaceAlongHeading"/>'s
    /// numbering) of <paramref name="from"/> that looks most directly at <paramref name="to"/>.</summary>
    private static int FaceToward(Vector3i from, Vector3i to)
    {
        int dx = to.X - from.X, dz = to.Z - from.Z;
        return Math.Abs(dx) >= Math.Abs(dz) ? (dx >= 0 ? 2 : 3) : (dz >= 0 ? 4 : 5);
    }

    internal static Vector3i[] Neighbours6(Vector3i c) => new[]
    {
        new Vector3i(c.X + 1, c.Y, c.Z), new Vector3i(c.X - 1, c.Y, c.Z),
        new Vector3i(c.X, c.Y + 1, c.Z), new Vector3i(c.X, c.Y - 1, c.Z),
        new Vector3i(c.X, c.Y, c.Z + 1), new Vector3i(c.X, c.Y, c.Z - 1),
    };

    private static Vector3i[] Neighbours4(Vector3i c) => new[]
    {
        new Vector3i(c.X + 1, c.Y, c.Z), new Vector3i(c.X - 1, c.Y, c.Z),
        new Vector3i(c.X, c.Y, c.Z + 1), new Vector3i(c.X, c.Y, c.Z - 1),
    };

    /// <summary>A module doorway as the generator cut it: two columns, three cells high, in an X- or Z-facing wall.</summary>
    private readonly struct Doorway
    {
        public readonly Vector3i Marker;
        public readonly Vector3i Other;
        public readonly bool XWall;

        public Doorway(Vector3i marker, Vector3i other, bool xWall)
        {
            Marker = marker;
            Other = other;
            XWall = xWall;
        }

        public IEnumerable<Vector3i> Cells()
        {
            for (int dy = 0; dy < 3; dy++)
            {
                yield return new Vector3i(Marker.X, Marker.Y + dy, Marker.Z);
                yield return new Vector3i(Other.X, Other.Y + dy, Other.Z);
            }
        }

        /// <summary>The room cell beside the doorway on one side (<paramref name="sign"/> −1 / +1 across the wall).</summary>
        public Vector3i Side(int sign) => XWall
            ? new Vector3i(Marker.X + sign, Marker.Y, Marker.Z)
            : new Vector3i(Marker.X, Marker.Y, Marker.Z + sign);
    }

    /// <summary>The block ids the decay works with (0 when a key is missing from the data).</summary>
    private sealed class Ids
    {
        public readonly ushort Hull, Glass, Field, DataCache, Vendor, Scorch, ScrapMetal, ScrapPile, LightWarm, LightOff, HealTank, GuardianBay;

        /// <summary>#2674: the beta wreck's rusted patches and its dead beacon.</summary>
        public readonly ushort Rust, BeaconOff;
        public readonly HashSet<ushort> Plating = new();

        /// <summary>#2636: the blocks the server's station air fill stops at — airtight blocks (the content's flag) plus the
        /// door blocks and the station core it adds (GameServer.IsStationAirtightCell). Only a full cube seals.</summary>
        public readonly HashSet<ushort> Airtight = new();

        public Ids(GameContent content)
        {
            foreach (var def in content.Blocks.Values)
            {
                if (def.Airtight && def.NumericId.Value != 0)
                {
                    Airtight.Add(def.NumericId.Value);
                }
            }

            foreach (var key in new[] { "door_slide", "door_hinge", "door_energy", "station_core" })
            {
                if (content.GetBlock(key) is { } door && door.NumericId.Value != 0)
                {
                    Airtight.Add(door.NumericId.Value);
                }
            }

            ushort Id(string key) => content.GetBlock(key)?.NumericId.Value ?? 0;
            Hull = Id("iron_wall");
            Glass = Id("glass");
            Field = Id("force_field");
            DataCache = Id("data_cache");
            Vendor = Id("station_vendor");
            Scorch = Id("carbon");
            ScrapMetal = Id("scrap_metal");
            ScrapPile = Id("scrap_pile");
            LightWarm = Id("strip_light_warm");
            LightOff = Id("strip_light_cyan_off");
            HealTank = Id("heal_tank");
            GuardianBay = Id("guardian_spawner"); // #2635
            Rust = Id("rusted_panel"); // #2674
            BeaconOff = Id("light_white_off");
            foreach (var key in new[] { "iron_wall", "steel_floor", "metal_panel", "cargo_floor", "lab_panel" })
            {
                ushort id = Id(key);
                if (id != 0)
                {
                    Plating.Add(id);
                }
            }
        }
    }

    /// <summary>The working voxel grid of the decay pass.</summary>
    private sealed class Grid
    {
        private readonly ushort[] _b;
        private readonly Dictionary<int, (int Tint, int Glow)> _mods = new();
        private readonly Dictionary<int, int> _shapes = new();
        public readonly int W, H, L;

        public Grid(StationStructure st)
        {
            W = st.Width;
            H = st.Height;
            L = st.Length;
            _b = new ushort[W * H * L];
            for (int x = 0; x < W; x++)
                for (int y = 0; y < H; y++)
                    for (int z = 0; z < L; z++)
                    {
                        int i = Idx(x, y, z);
                        _b[i] = st.Get(x, y, z);
                        var mod = st.GetModifier(x, y, z);
                        if (mod.Tint != 0 || mod.Glow != 0)
                        {
                            _mods[i] = mod;
                        }

                        int shape = st.GetShape(x, y, z);
                        if (shape != 0)
                        {
                            _shapes[i] = shape;
                        }
                    }
        }

        public int Idx(int x, int y, int z) => ((x * H) + y) * L + z;

        public bool InBounds(int x, int y, int z) => x >= 0 && y >= 0 && z >= 0 && x < W && y < H && z < L;

        public ushort Get(int x, int y, int z) => InBounds(x, y, z) ? _b[Idx(x, y, z)] : (ushort)0;

        public bool Solid(int x, int y, int z) => Get(x, y, z) != 0;

        public void Set(int x, int y, int z, ushort block)
        {
            if (!InBounds(x, y, z))
            {
                return;
            }

            int i = Idx(x, y, z);
            _b[i] = block;
            _mods.Remove(i);
            _shapes.Remove(i);
        }

        /// <summary>Sets a cell's shape descriptor (0 clears it) — e.g. the front of a facing cube (#2635).</summary>
        public void SetShape(int x, int y, int z, int shape)
        {
            if (!InBounds(x, y, z))
            {
                return;
            }

            int i = Idx(x, y, z);
            if (shape == 0)
            {
                _shapes.Remove(i);
            }
            else
            {
                _shapes[i] = shape;
            }
        }

        /// <summary>Feet cell with two free cells and a solid deck below.</summary>
        public bool Standable(int x, int y, int z)
            => InBounds(x, y, z) && !Solid(x, y, z) && !Solid(x, y + 1, z) && Solid(x, y - 1, z);

        public bool AgainstWall(Vector3i c)
            => Solid(c.X + 1, c.Y, c.Z) || Solid(c.X - 1, c.Y, c.Z) || Solid(c.X, c.Y, c.Z + 1) || Solid(c.X, c.Y, c.Z - 1);

        public bool TouchesBlock(Vector3i c, ushort block)
        {
            if (block == 0)
            {
                return false;
            }

            foreach (var n in Neighbours6(c))
            {
                if (Get(n.X, n.Y, n.Z) == block)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Air reachable from outside the bounding box — the space around the station.</summary>
        public bool[] FloodExterior()
        {
            var ext = new bool[W * H * L];
            var q = new Queue<Vector3i>();
            for (int x = 0; x < W; x++)
                for (int y = 0; y < H; y++)
                    for (int z = 0; z < L; z++)
                    {
                        bool edge = x == 0 || y == 0 || z == 0 || x == W - 1 || y == H - 1 || z == L - 1;
                        if (edge && !Solid(x, y, z))
                        {
                            ext[Idx(x, y, z)] = true;
                            q.Enqueue(new Vector3i(x, y, z));
                        }
                    }

            while (q.Count > 0)
            {
                var c = q.Dequeue();
                foreach (var n in Neighbours6(c))
                {
                    if (!InBounds(n.X, n.Y, n.Z) || Solid(n.X, n.Y, n.Z) || ext[Idx(n.X, n.Y, n.Z)])
                    {
                        continue;
                    }

                    ext[Idx(n.X, n.Y, n.Z)] = true;
                    q.Enqueue(n);
                }
            }

            return ext;
        }

        public bool IsExterior(bool[] exterior, int x, int y, int z) => !InBounds(x, y, z) || exterior[Idx(x, y, z)];

        /// <summary>Labels the interior air into rooms (doorway cells count as walls). Pockets smaller than a room — gaps
        /// behind furniture — get no label.</summary>
        public int[] LabelRooms(bool[] exterior, HashSet<Vector3i> doorway, out List<List<Vector3i>> rooms)
        {
            var label = new int[W * H * L];
            for (int i = 0; i < label.Length; i++)
            {
                label[i] = -1;
            }

            rooms = new List<List<Vector3i>>();
            var seen = new bool[W * H * L];
            for (int x = 0; x < W; x++)
                for (int y = 0; y < H; y++)
                    for (int z = 0; z < L; z++)
                    {
                        int i = Idx(x, y, z);
                        if (seen[i] || exterior[i] || Solid(x, y, z) || doorway.Contains(new Vector3i(x, y, z)))
                        {
                            continue;
                        }

                        var cells = new List<Vector3i>();
                        var q = new Queue<Vector3i>();
                        seen[i] = true;
                        q.Enqueue(new Vector3i(x, y, z));
                        while (q.Count > 0)
                        {
                            var c = q.Dequeue();
                            cells.Add(c);
                            foreach (var n in Neighbours6(c))
                            {
                                if (!InBounds(n.X, n.Y, n.Z))
                                {
                                    continue;
                                }

                                int ni = Idx(n.X, n.Y, n.Z);
                                if (seen[ni] || exterior[ni] || Solid(n.X, n.Y, n.Z) || doorway.Contains(n))
                                {
                                    continue;
                                }

                                seen[ni] = true;
                                q.Enqueue(n);
                            }
                        }

                        if (cells.Count < MinRoomCells)
                        {
                            continue;
                        }

                        int id = rooms.Count;
                        rooms.Add(cells);
                        foreach (var c in cells)
                        {
                            label[Idx(c.X, c.Y, c.Z)] = id;
                        }
                    }

            return label;
        }

        /// <summary>The one room of a classic template's hall, judged without the air fill (those halls were never built
        /// airtight, so the fill runs out of a gap and finds none): the largest connected body of air cells inside the
        /// bounding box that have a roof above and a deck below — so neither the space beside a spire nor a closed pocket
        /// under a dome. Labelled room 0 (all other labels cleared) when it is big enough.</summary>
        public void LabelInnerAir(int[] label, List<List<Vector3i>> rooms)
        {
            for (int i = 0; i < label.Length; i++)
            {
                label[i] = -1;
            }

            rooms.Clear();
            var inner = new HashSet<Vector3i>();
            for (int x = 1; x < W - 1; x++)
                for (int z = 1; z < L - 1; z++)
                    for (int y = 1; y < H - 1; y++)
                    {
                        if (Solid(x, y, z))
                        {
                            continue;
                        }

                        bool roof = false, deck = false;
                        for (int yy = y + 1; yy < H && !roof; yy++)
                        {
                            roof = Solid(x, yy, z);
                        }

                        for (int yy = y - 1; yy >= 0 && !deck; yy--)
                        {
                            deck = Solid(x, yy, z);
                        }

                        if (roof && deck)
                        {
                            inner.Add(new Vector3i(x, y, z));
                        }
                    }

            List<Vector3i>? best = null;
            var seen = new HashSet<Vector3i>();
            var ordered = new List<Vector3i>(inner);
            ordered.Sort(Compare);
            foreach (var start in ordered)
            {
                if (!seen.Add(start))
                {
                    continue;
                }

                var cells = new List<Vector3i>();
                var q = new Queue<Vector3i>();
                q.Enqueue(start);
                while (q.Count > 0)
                {
                    var c = q.Dequeue();
                    cells.Add(c);
                    foreach (var n in Neighbours6(c))
                    {
                        if (inner.Contains(n) && seen.Add(n))
                        {
                            q.Enqueue(n);
                        }
                    }
                }

                if (best is null || cells.Count > best.Count)
                {
                    best = cells;
                }
            }

            if (best is null || best.Count < MinRoomCells)
            {
                return;
            }

            rooms.Add(best);
            foreach (var c in best)
            {
                label[Idx(c.X, c.Y, c.Z)] = 0;
            }
        }

        /// <summary>The room a cell belongs to — the cell's own, else the nearest labelled cell within two blocks (a cell
        /// holding furniture), else −1.</summary>
        public int RoomAt(int[] room, Vector3i c)
        {
            if (InBounds(c.X, c.Y, c.Z) && room[Idx(c.X, c.Y, c.Z)] >= 0)
            {
                return room[Idx(c.X, c.Y, c.Z)];
            }

            for (int r = 1; r <= 2; r++)
                for (int dx = -r; dx <= r; dx++)
                    for (int dy = -r; dy <= r; dy++)
                        for (int dz = -r; dz <= r; dz++)
                        {
                            int x = c.X + dx, y = c.Y + dy, z = c.Z + dz;
                            if (InBounds(x, y, z) && room[Idx(x, y, z)] >= 0)
                            {
                                return room[Idx(x, y, z)];
                            }
                        }

            return -1;
        }

        /// <summary>Wall cells a breach may start at: solid, between this room's air and the space outside, and not a wall
        /// a sealed room relies on. Sorted, so the seeded pick is stable.</summary>
        public List<Vector3i> BreachCandidates(List<Vector3i> roomAir, bool[] exterior, HashSet<Vector3i> protectedCells)
        {
            var set = new HashSet<Vector3i>();
            foreach (var c in roomAir)
            {
                foreach (var n in Neighbours6(c))
                {
                    if (!Solid(n.X, n.Y, n.Z) || protectedCells.Contains(n) || set.Contains(n))
                    {
                        continue;
                    }

                    foreach (var o in Neighbours6(n))
                    {
                        if (IsExterior(exterior, o.X, o.Y, o.Z))
                        {
                            set.Add(n);
                            break;
                        }
                    }
                }
            }

            var list = new List<Vector3i>(set);
            list.Sort(Compare);
            return list;
        }

        /// <summary>Tears a rough hole around <paramref name="centre"/> (never through a protected cell).</summary>
        public void Carve(Vector3i centre, double radius, HashSet<Vector3i> protectedCells, Random rng)
        {
            int reach = (int)Math.Ceiling(radius);
            for (int dx = -reach; dx <= reach; dx++)
                for (int dy = -reach; dy <= reach; dy++)
                    for (int dz = -reach; dz <= reach; dz++)
                    {
                        var c = new Vector3i(centre.X + dx, centre.Y + dy, centre.Z + dz);
                        double r = radius - rng.NextDouble() * 0.7;
                        bool inside = (dx * dx) + (dy * dy) + (dz * dz) <= r * r || c == centre;
                        if (inside && Solid(c.X, c.Y, c.Z) && !protectedCells.Contains(c))
                        {
                            Set(c.X, c.Y, c.Z, 0);
                        }
                    }
        }

        /// <summary>Removes every plant (the planters, the hydroponic crops) — the FIRST step, so the rooms are judged with
        /// the air the plants leave behind: a planter's deck must count as the wall of its room.</summary>
        public void RemoveFlora(GameContent content)
        {
            var flora = new HashSet<ushort>();
            foreach (var def in content.Blocks.Values)
            {
                if (def.Category == "flora")
                {
                    flora.Add(def.NumericId.Value);
                }
            }

            for (int i = 0; i < _b.Length; i++)
            {
                if (_b[i] != 0 && flora.Contains(_b[i]))
                {
                    _b[i] = 0;
                    _mods.Remove(i);
                    _shapes.Remove(i);
                }
            }
        }

        /// <summary>Removes the force fields and the data caches; the lights burn warm in the sealed rooms and are dead
        /// everywhere else.</summary>
        public void Strip(Ids ids, HashSet<Vector3i> sealedAir)
        {
            for (int x = 0; x < W; x++)
                for (int y = 0; y < H; y++)
                    for (int z = 0; z < L; z++)
                    {
                        ushort b = Get(x, y, z);
                        if (b == 0)
                        {
                            continue;
                        }

                        bool lit = false;
                        foreach (var n in Neighbours6(new Vector3i(x, y, z)))
                        {
                            if (sealedAir.Contains(n))
                            {
                                lit = true;
                                break;
                            }
                        }

                        if (b == ids.Field && ids.Field != 0)
                        {
                            Set(x, y, z, lit && ids.Glass != 0 ? ids.Glass : (ushort)0); // a sealed room keeps a window
                        }
                        else if (b == ids.DataCache && ids.DataCache != 0)
                        {
                            ushort light = lit ? ids.LightWarm : ids.LightOff;
                            Set(x, y, z, light != 0 ? light : ids.Scorch != 0 ? ids.Scorch : ids.Hull);
                        }
                        else if (b == ids.HealTank && ids.HealTank != 0)
                        {
                            Set(x, y, z, ids.ScrapMetal != 0 ? ids.ScrapMetal : ids.Hull);
                        }
                    }
        }

        /// <summary>Missing outer panels: a few outward-facing cells gone, never one a sealed room relies on.</summary>
        public void TearPanels(bool[] exterior, HashSet<Vector3i> protectedCells, Random rng)
        {
            for (int x = 0; x < W; x++)
                for (int y = 0; y < H; y++)
                    for (int z = 0; z < L; z++)
                    {
                        if (!Solid(x, y, z))
                        {
                            continue;
                        }

                        var c = new Vector3i(x, y, z);
                        bool outer = false;
                        foreach (var n in Neighbours6(c))
                        {
                            if (IsExterior(exterior, n.X, n.Y, n.Z))
                            {
                                outer = true;
                                break;
                            }
                        }

                        double roll = rng.NextDouble();
                        if (outer && !protectedCells.Contains(c) && roll < 0.06)
                        {
                            Set(x, y, z, 0);
                        }
                    }
        }

        /// <summary>Scorched plating (carbon — airtight like the iron it replaces): heavy round the breaches, light on the
        /// outer hull, a little inside.</summary>
        public void Scorch(Ids ids, bool[] exterior, List<Vector3i> breachCentres, Random rng)
        {
            if (ids.Scorch == 0)
            {
                return;
            }

            for (int x = 0; x < W; x++)
                for (int y = 0; y < H; y++)
                    for (int z = 0; z < L; z++)
                    {
                        double roll = rng.NextDouble();
                        ushort b = Get(x, y, z);
                        bool glass = b == ids.Glass && b != 0;
                        if (b == 0 || (!ids.Plating.Contains(b) && !glass))
                        {
                            continue;
                        }

                        bool nearBreach = false;
                        foreach (var c in breachCentres)
                        {
                            int dx = c.X - x, dy = c.Y - y, dz = c.Z - z;
                            if ((dx * dx) + (dy * dy) + (dz * dz) <= 16)
                            {
                                nearBreach = true;
                                break;
                            }
                        }

                        bool outer = false;
                        foreach (var n in Neighbours6(new Vector3i(x, y, z)))
                        {
                            if (IsExterior(exterior, n.X, n.Y, n.Z))
                            {
                                outer = true;
                                break;
                            }
                        }

                        double chance = nearBreach ? 0.55 : glass ? 0.0 : outer ? 0.07 : 0.03;
                        if (roll < chance)
                        {
                            Set(x, y, z, ids.Scorch);
                        }
                    }
        }

        /// <summary>
        /// #2674: what makes a beta station a wreck from outside, on top of the shared decay — only ever run on a grid built
        /// from <see cref="BetaStationGenerator"/> (its centre and parts are where that generator put them):
        /// <list type="bullet">
        /// <item>one solar wing half torn off — a ragged edge row by row, the cells along it scorched, and a few of its cells
        /// drifting beyond it;</item>
        /// <item>the mast snapped a few blocks above its foot, its top (with the dead beacon) floating just beside the stump;</item>
        /// <item>rusted patches on the outer hull (a rusted panel is a full airtight cube like the iron it replaces);</item>
        /// <item>a few loose debris blocks floating near the hull.</item>
        /// </list>
        /// Everything stays on the outside: no room is opened by it.
        /// </summary>
        public void BetaDamage(Ids ids, bool[] exterior, Random rng)
        {
            var c = BetaStationGenerator.Centre;
            ushort dark = ids.Scorch != 0 ? ids.Scorch : ids.Hull;

            // The torn wing.
            int side = rng.Next(2) == 0 ? 1 : -1;
            int mid = (BetaStationGenerator.WingInner + BetaStationGenerator.WingOuter) / 2;
            int wingY = c.Y + BetaStationGenerator.WingLayer;
            for (int dz = -BetaStationGenerator.WingHalfSpan; dz <= BetaStationGenerator.WingHalfSpan; dz++)
            {
                int edge = mid + rng.Next(-2, 3);
                for (int a = edge + 1; a <= BetaStationGenerator.WingOuter; a++)
                {
                    Set(c.X + (side * a), wingY, c.Z + dz, 0);
                }

                for (int a = edge - 2; a <= edge; a++)
                {
                    double roll = rng.NextDouble();
                    int x = c.X + (side * a), z = c.Z + dz;
                    if (Solid(x, wingY, z) && roll < 0.6)
                    {
                        Set(x, wingY, z, dark); // scorched along the tear
                    }
                }
            }

            int fragments = 2 + rng.Next(3);
            for (int tries = 0; tries < 40 && fragments > 0; tries++)
            {
                int a = mid + 2 + rng.Next(BetaStationGenerator.WingOuter - mid - 1);
                int dz = rng.Next(-BetaStationGenerator.WingHalfSpan, BetaStationGenerator.WingHalfSpan + 1);
                int dy = (rng.Next(2) == 0 ? 1 : -1) * (1 + rng.Next(2));
                bool glass = rng.NextDouble() < 0.6 && ids.Glass != 0;
                var cell = new Vector3i(c.X + (side * a), wingY + dy, c.Z + dz);
                if (!Floating(cell))
                {
                    continue;
                }

                Set(cell.X, cell.Y, cell.Z, glass ? ids.Glass : dark);
                if (glass)
                {
                    SetModifier(cell.X, cell.Y, cell.Z, (StationKitExterior.SolarTint, 0)); // a drifting solar cell
                }

                fragments--;
            }

            // The snapped mast: it stands to the stump; its top floats one block above the break, one to the side.
            int stump = BetaStationGenerator.MastBottom + 2 + rng.Next(3);
            for (int dy = stump + 1; dy <= BetaStationGenerator.BeaconLayer; dy++)
            {
                Set(c.X, c.Y + dy, c.Z, 0);
            }

            var (ox, oz) = BetaStationGenerator.Arms[rng.Next(BetaStationGenerator.Arms.Length)];
            int top = stump + 2;
            for (int dy = top; dy < top + 3; dy++)
            {
                Set(c.X + ox, c.Y + dy, c.Z + oz, dark);
            }

            if (ids.BeaconOff != 0)
            {
                Set(c.X + ox, c.Y + top + 3, c.Z + oz, ids.BeaconOff); // the beacon, dead
                SetModifier(c.X + ox, c.Y + top + 3, c.Z + oz, (BetaStationGenerator.BeaconTint, 0));
            }

            // Rusted patches on the outer hull.
            if (ids.Rust != 0 && ids.Hull != 0)
            {
                bool Outer(int x, int y, int z)
                {
                    foreach (var n in Neighbours6(new Vector3i(x, y, z)))
                    {
                        if (IsExterior(exterior, n.X, n.Y, n.Z))
                        {
                            return true;
                        }
                    }

                    return false;
                }

                var outer = new List<Vector3i>();
                for (int x = 0; x < W; x++)
                    for (int y = 0; y < H; y++)
                        for (int z = 0; z < L; z++)
                        {
                            if (Get(x, y, z) == ids.Hull && Outer(x, y, z))
                            {
                                outer.Add(new Vector3i(x, y, z));
                            }
                        }

                int patches = 4 + rng.Next(4);
                for (int p = 0; p < patches && outer.Count > 0; p++)
                {
                    var centre = outer[rng.Next(outer.Count)];
                    double radius = 1.2 + rng.NextDouble() * 1.3;
                    for (int dx = -3; dx <= 3; dx++)
                        for (int dy = -3; dy <= 3; dy++)
                            for (int dz = -3; dz <= 3; dz++)
                            {
                                double roll = rng.NextDouble();
                                int x = centre.X + dx, y = centre.Y + dy, z = centre.Z + dz;
                                if ((dx * dx) + (dy * dy) + (dz * dz) <= radius * radius && roll < 0.8
                                    && Get(x, y, z) == ids.Hull && Outer(x, y, z))
                                {
                                    Set(x, y, z, ids.Rust);
                                }
                            }
                }
            }

            // Loose debris drifting near the hull.
            int debris = 3 + rng.Next(3);
            for (int tries = 0; tries < 300 && debris > 0; tries++)
            {
                var cell = new Vector3i(rng.Next(W), rng.Next(H), rng.Next(L));
                double roll = rng.NextDouble();
                if (!IsExterior(exterior, cell.X, cell.Y, cell.Z) || !Floating(cell) || !SolidWithin(cell, 4))
                {
                    continue;
                }

                ushort b = roll < 0.5 && ids.ScrapMetal != 0 ? ids.ScrapMetal : roll < 0.8 ? dark : ids.Hull;
                Set(cell.X, cell.Y, cell.Z, b);
                debris--;
            }
        }

        /// <summary>An empty cell inside the grid with nothing in any of its 26 neighbours — a block set there floats free.</summary>
        private bool Floating(Vector3i c)
        {
            if (!InBounds(c.X, c.Y, c.Z))
            {
                return false;
            }

            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        if (Solid(c.X + dx, c.Y + dy, c.Z + dz))
                        {
                            return false;
                        }
                    }

            return true;
        }

        /// <summary>Whether any block lies within <paramref name="reach"/> cells of <paramref name="c"/> (a cube).</summary>
        private bool SolidWithin(Vector3i c, int reach)
        {
            for (int dx = -reach; dx <= reach; dx++)
                for (int dy = -reach; dy <= reach; dy++)
                    for (int dz = -reach; dz <= reach; dz++)
                    {
                        if (Solid(c.X + dx, c.Y + dy, c.Z + dz))
                        {
                            return true;
                        }
                    }

            return false;
        }

        /// <summary>Sets a cell's dye/glow (both 0 clears it) — e.g. the blue of a drifting solar cell (#2674).</summary>
        public void SetModifier(int x, int y, int z, (int Tint, int Glow) mod)
        {
            if (!InBounds(x, y, z))
            {
                return;
            }

            int i = Idx(x, y, z);
            if (mod.Tint == 0 && mod.Glow == 0)
            {
                _mods.Remove(i);
            }
            else
            {
                _mods[i] = mod;
            }
        }

        public void ScorchAt(int x, int y, int z, Ids ids)
        {
            if (ids.Scorch != 0 && ids.Plating.Contains(Get(x, y, z)))
            {
                Set(x, y, z, ids.Scorch);
            }
        }

        /// <summary>The standable feet cells of a room, minus <paramref name="reserved"/> and the arrival's surroundings.
        /// Sorted, so a seeded pick is stable.</summary>
        public List<Vector3i> StandableCells(List<Vector3i> roomAir, HashSet<Vector3i> reserved, Vector3i nearArrival, int minArrivalDistance)
        {
            var list = new List<Vector3i>();
            foreach (var c in roomAir)
            {
                if (!Standable(c.X, c.Y, c.Z) || reserved.Contains(c))
                {
                    continue;
                }

                int d = Math.Max(Math.Abs(c.X - nearArrival.X), Math.Max(Math.Abs(c.Y - nearArrival.Y), Math.Abs(c.Z - nearArrival.Z)));
                if (d < minArrivalDistance)
                {
                    continue;
                }

                list.Add(c);
            }

            list.Sort(Compare);
            return list;
        }

        /// <summary>The standable cell nearest a room's centre (from <paramref name="pool"/> when given), or null.</summary>
        public Vector3i? StandableNearCentroid(List<Vector3i> roomAir, List<Vector3i>? pool)
        {
            if (roomAir.Count == 0)
            {
                return null;
            }

            double cx = 0, cy = 0, cz = 0;
            foreach (var c in roomAir)
            {
                cx += c.X;
                cy += c.Y;
                cz += c.Z;
            }

            cx /= roomAir.Count;
            cy /= roomAir.Count;
            cz /= roomAir.Count;
            Vector3i? best = null;
            double bestD = double.MaxValue;
            foreach (var c in pool ?? roomAir)
            {
                if (!Standable(c.X, c.Y, c.Z))
                {
                    continue;
                }

                double d = ((c.X - cx) * (c.X - cx)) + ((c.Y - cy) * (c.Y - cy) * 4) + ((c.Z - cz) * (c.Z - cz));
                if (d < bestD || (Math.Abs(d - bestD) < 1e-9 && best is { } b && Compare(c, b) < 0))
                {
                    bestD = d;
                    best = c;
                }
            }

            return best;
        }

        public Vector3i? FirstAir(List<Vector3i> roomAir, HashSet<Vector3i> doorCells)
        {
            foreach (var c in roomAir)
            {
                if (!Solid(c.X, c.Y, c.Z) && !doorCells.Contains(c))
                {
                    return c;
                }
            }

            return null;
        }

        /// <summary>Whether the air pocket around <paramref name="start"/> is closed — the server's air rule in small: the
        /// fill moves face to face through every cell that does not seal (air, and since #2636 also a block that is not
        /// airtight or not a full cube, as on the server), stops at sealing blocks and doors, and leaks when it leaves the
        /// bounding box.</summary>
        public bool PocketSealed(Vector3i start, HashSet<Vector3i> doorCells, Ids ids)
        {
            if (!InBounds(start.X, start.Y, start.Z) || Seals(start.X, start.Y, start.Z, ids) || doorCells.Contains(start))
            {
                return false;
            }

            var seen = new HashSet<Vector3i> { start };
            var q = new Queue<Vector3i>();
            q.Enqueue(start);
            while (q.Count > 0)
            {
                var c = q.Dequeue();
                foreach (var n in Neighbours6(c))
                {
                    if (!InBounds(n.X, n.Y, n.Z))
                    {
                        return false;
                    }

                    if (Seals(n.X, n.Y, n.Z, ids) || doorCells.Contains(n) || !seen.Add(n))
                    {
                        continue;
                    }

                    q.Enqueue(n);
                }
            }

            return true;
        }

        /// <summary>#2636: whether a cell holds air as a wall of a station pocket on the server — an airtight full cube.</summary>
        public bool Seals(int x, int y, int z, Ids ids)
        {
            ushort b = Get(x, y, z);
            return b != 0 && ids.Airtight.Contains(b)
                && (!_shapes.TryGetValue(Idx(x, y, z), out int shape) || ShapeCode.IsCube(shape));
        }

        /// <summary>
        /// #2636: patches the arrival's pocket shut — a classic template's hall was never built airtight. The pocket grows
        /// from the arrival through every cell that does not seal, as the server's air fill would, but only through the
        /// hall itself (a cell with a block somewhere above and below it, inside the bounding box, and the arrival column);
        /// every non-sealing cell it would step into beyond that — a gap in the wall, a window frame of leaky blocks, an
        /// open dock mouth, a shaped roof cell — becomes a hull cube. Sealed by construction: everything around the pocket
        /// is a sealing cube or a door. Returns how many cells were patched.
        /// </summary>
        public int SealArrivalPocket(Vector3i arrival, HashSet<Vector3i> doorCells, Ids ids)
        {
            if (ids.Hull == 0 || !InBounds(arrival.X, arrival.Y, arrival.Z))
            {
                return 0;
            }

            var head = new Vector3i(arrival.X, arrival.Y + 1, arrival.Z);
            bool Inner(Vector3i c)
            {
                if (c == arrival || c == head)
                {
                    return true;
                }

                if (c.X < 1 || c.Y < 1 || c.Z < 1 || c.X > W - 2 || c.Y > H - 2 || c.Z > L - 2)
                {
                    return false;
                }

                bool roof = false, deck = false;
                for (int y = c.Y + 1; y < H && !roof; y++)
                {
                    roof = Solid(c.X, y, c.Z);
                }

                for (int y = c.Y - 1; y >= 0 && !deck; y--)
                {
                    deck = Solid(c.X, y, c.Z);
                }

                return roof && deck;
            }

            int patched = 0;
            var pocket = new HashSet<Vector3i> { arrival };
            var q = new Queue<Vector3i>();
            q.Enqueue(arrival);
            while (q.Count > 0)
            {
                var c = q.Dequeue();
                foreach (var n in Neighbours6(c))
                {
                    if (pocket.Contains(n) || doorCells.Contains(n) || Seals(n.X, n.Y, n.Z, ids))
                    {
                        continue;
                    }

                    if (InBounds(n.X, n.Y, n.Z) && Inner(n))
                    {
                        pocket.Add(n);
                        q.Enqueue(n);
                    }
                    else if (InBounds(n.X, n.Y, n.Z))
                    {
                        Set(n.X, n.Y, n.Z, ids.Hull); // the patch: a plain hull cube where the hall leaked
                        patched++;
                    }
                }
            }

            return patched;
        }

        public DerelictLayout ToLayout(string tier, string source)
            => new(W, H, L, tier, source, _b, _mods, _shapes);

        private static int Compare(Vector3i a, Vector3i b)
        {
            int c = a.X.CompareTo(b.X);
            if (c != 0)
            {
                return c;
            }

            c = a.Y.CompareTo(b.Y);
            return c != 0 ? c : a.Z.CompareTo(b.Z);
        }
    }
}
