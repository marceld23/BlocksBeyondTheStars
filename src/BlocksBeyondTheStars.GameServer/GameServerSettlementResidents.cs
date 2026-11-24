// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Settlement residents per bed (#1887, Marcel 2026-09-14: "every resident has a bed"). The beds of a settlement's
/// stamped layout ARE its people, capped per size; the posts — vendor, quartermaster, gardener, craftsman, innkeeper —
/// are staffed by residents in that order, each sleeping in the free bed nearest to their post; everyone else takes the
/// remaining beds and idles at the settlers' spots nearest to home. The routine (#1867) does the rest: the post by day,
/// a tavern chair (else the nearest chair) in the evening, the own bed at night. A settlement without beds still has its
/// vendor and quartermaster; the G.D.S. guardians stay extra (machines, no bed). Existing worlds keep their blocks —
/// the residents simply follow the beds that stand there.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>How far around a tavern marker the evening seats are collected.</summary>
    private const int TavernSeatReach = 8;

    /// <summary>How far around a resident's bed a chair is looked for when the settlement has no tavern.</summary>
    private const int HomeSeatReach = 6;

    /// <summary>The most residents a settlement of a tier houses (Marcel 2026-09-14).</summary>
    internal static int ResidentCap(string tier) => tier switch
    {
        "hamlet" => 6,
        "village" => 10,
        "town" => 20,
        "city" => 32,
        CityGenerator.Tier => 80,
        _ => 10,
    };

    /// <summary>The post markers a resident staffs, in the order they are staffed (the services first).</summary>
    private static readonly (string Marker, string Role, string Job)[] ResidentPosts =
    {
        ("vendor", "vendor", "vendor"),
        ("mission_board", "quartermaster", "quartermaster"),
        ("greenhouse", "settler", "gardener"),
        (SettlementGenerator.WorkshopMarker, "settler", "craftsman"),
        (SettlementGenerator.TavernMarker, "settler", "innkeeper"),
    };

    /// <summary>Spawns the residents and guardians of one inhabited settlement.</summary>
    private void SpawnSettlementResidents(SettlementInstance settlement, System.Random rng)
    {
        string settlementTheme = SettlementTrade(settlement);
        int vendorIndex = 0;
        BeginAuthoredCasting(settlement.Name); // #1150: at most one authored face per place

        var posts = new List<(string Role, string Job, Vector3f Pos)>();
        foreach (var (marker, role, job) in ResidentPosts)
        {
            foreach (var (type, pos) in settlement.Markers)
            {
                if (type == marker)
                {
                    posts.Add((role, job, pos));
                }
            }
        }

        var spots = settlement.Markers.Where(m => m.Type == "npc").Select(m => m.Pos).ToList();
        var beds = SettlementBeds(settlement);
        var childBeds = TakeChildBeds(settlement, beds); // the lively places: a child's bed houses no grown-up
        var seats = TavernSeats(settlement);
        int services = posts.Count(p => p.Job is "vendor" or "quartermaster");
        int count = System.Math.Max(System.Math.Min(beds.Count, ResidentCap(settlement.Tier)), services);
        var freeBeds = new List<Vector3i>(beds);
        int seatCursor = 0;
        int bedlessResidents = 0; // #2598: the fallback slot ordinal of a resident with neither post nor bed
        var plainResidents = new List<ServerNpc>(); // the lively places: the people without a post (the night watch is two of them)

        for (int i = 0; i < count; i++)
        {
            bool hasPost = i < posts.Count;
            string role = hasPost ? posts[i].Role : "settler";
            string job = hasPost ? posts[i].Job : string.Empty;
            Vector3i? bed = null;
            Vector3f home;
            if (hasPost)
            {
                home = posts[i].Pos;
                bed = TakeNearestBed(freeBeds, home);
            }
            else
            {
                bed = freeBeds.Count > 0 ? freeBeds[0] : null;
                if (bed != null)
                {
                    freeBeds.RemoveAt(0);
                }

                var anchor = bed is { } b ? new Vector3f(b.X + 0.5f, b.Y + 0.5f, b.Z + 0.5f) : settlement.Markers.Count > 0 ? settlement.Markers[0].Pos : default;
                home = spots.Count > 0 ? spots.OrderBy(s => WrapDistSq(s, anchor)).First() : posts.Count > 0 ? posts[0].Pos : anchor;
            }

            string npcTheme = role == "vendor"
                ? SettlementVendorThemeFor(settlement.Name, SettlementPlaceKey(settlement), vendorIndex++, settlementTheme, _meta.Description.TerrainGeneration) // #2698
                : settlementTheme;
            bool robotic = npcTheme == "researchers" && rng.Next(100) < 60; // most research staff are service androids — but not all (#711)

            // Feet on top of the floor block (markers sit centred in the air cell above it), never below the settlement.
            var standing = new Vector3f(home.X, (float)System.Math.Floor(System.Math.Max(settlement.Min.Y + 1f, home.Y)), home.Z);
            var npc = MakeNpc(role, npcTheme, robotic, standing, rng);
            npc.Settlement = settlement.Name;
            npc.Job = job is "vendor" or "quartermaster" or "craftsman" or "innkeeper" or "gardener" ? job : string.Empty;
            npc.Held = ResidentTool(npc.Job); // the lively places: the village gardener and craftsman hold their tool too
            npc.Work = standing;
            npc.HasWork = true;
            SetNpcBed(npc, bed); // #1887: the bed is assigned; the seat below
            SetNpcSeat(npc, EveningSeatFor(settlement, seats, ref seatCursor, bed, hasPost ? null : npc));
            MarkFurnitureLooked(npc); // #2655: kept while they stand; it looks again only when furniture comes or goes

            if (role == "quartermaster")
            {
                npc.Name = SettlementGiverName(settlement); // the mission-giver's name matches its missions (item 13)
            }

            // #2598: the villager's own memory key, from their place in the layout (post cell, else bed cell).
            npc.PersonKey = SettlementPersonKey(settlement, npc, hasPost ? posts[i].Pos : null, bed, ref bedlessResidents);
            ApplyAuthoredCharacter(npc, "settlement", settlement.Name); // #1128: a pack face may claim this slot
            npc.RoutineEnabled = true; // #1867: villagers keep a daily routine
            _npcs.Add(npc);
            if (!hasPost && npc.CharacterId.Length == 0)
            {
                plainResidents.Add(npc);
            }
        }

        AssignNightWatch(settlement, plainResidents);
        AssignHouseholdPets(plainResidents); // after the watch: its people sleep by day

        // The professions (2026-09): one extra resident per profession post, after everyone above and with a generator of
        // their own — a settlement without these posts (every settlement of an older world) spawns exactly the people it
        // always did.
        SpawnProfessionResidents(settlement, freeBeds, seats, ref seatCursor);
        SpawnChildren(settlement, childBeds, spots); // … and the children last, from a generator of their own

        // The G.D.S. guardians (#1793): machines at their posts, never asleep, no bed.
        foreach (var (type, pos) in settlement.Markers)
        {
            if (type != GuardPostMarker)
            {
                continue;
            }

            var standing = new Vector3f(pos.X, (float)System.Math.Floor(System.Math.Max(settlement.Min.Y + 1f, pos.Y)), pos.Z);
            var guardian = MakeNpc("guardian", settlementTheme, robotic: true, standing, rng);
            guardian.Settlement = settlement.Name;
            DressGuardian(guardian);
            ApplyAuthoredCharacter(guardian, "settlement", settlement.Name);
            guardian.RoutineEnabled = false;
            _npcs.Add(guardian);
        }
    }

    /// <summary>The head cells of every bed in a settlement's stamped layout, in world space and scan order — but for the
    /// taverns' guest beds (#2986): those are the travellers', so they neither house a resident nor raise the count.</summary>
    private static List<Vector3i> SettlementBeds(SettlementInstance settlement, ushort bedId)
    {
        var beds = new List<Vector3i>();
        var s = settlement.Layout;
        if (s is null || bedId == 0)
        {
            return beds;
        }

        var guest = new HashSet<Vector3i>(settlement.Markers
            .Where(m => m.Type == SettlementGenerator.GuestBedMarker)
            .Select(m => m.Pos.ToBlock()));
        for (int x = 0; x < s.Width; x++)
            for (int z = 0; z < s.Length; z++)
                for (int y = 0; y < s.Height; y++)
                {
                    var cell = new Vector3i(settlement.Min.X + x, settlement.GroundY + y, settlement.Min.Z + z);
                    if (s.Get(x, y, z) == bedId && ShapeCode.ShapeOf(s.GetShape(x, y, z)) != (int)BlockShape.BedFoot && !guest.Contains(cell))
                    {
                        beds.Add(cell);
                    }
                }

        return beds;
    }

    private List<Vector3i> SettlementBeds(SettlementInstance settlement)
        => SettlementBeds(settlement, _content.GetBlock(BedBlock)?.NumericId.Value ?? 0);

    private Vector3i? TakeNearestBed(List<Vector3i> freeBeds, Vector3f from)
    {
        if (freeBeds.Count == 0)
        {
            return null;
        }

        int best = 0;
        double bestD = double.MaxValue;
        for (int i = 0; i < freeBeds.Count; i++)
        {
            double d = WrapDistSq(from, freeBeds[i]) + System.Math.Abs(freeBeds[i].Y - from.Y) * 4.0;
            if (d < bestD)
            {
                bestD = d;
                best = i;
            }
        }

        var bed = freeBeds[best];
        freeBeds.RemoveAt(best);
        return bed;
    }

    /// <summary>Every chair and bench within reach of the settlement's tavern markers (from its layout), no duplicates.</summary>
    private static List<Vector3i> TavernSeats(SettlementInstance settlement)
    {
        var seats = new List<Vector3i>();
        var s = settlement.Layout;
        if (s is null)
        {
            return seats;
        }

        var seen = new HashSet<Vector3i>();
        foreach (var (type, pos) in settlement.Markers)
        {
            if (type != SettlementGenerator.TavernMarker)
            {
                continue;
            }

            int lx = (int)System.Math.Floor(pos.X) - settlement.Min.X;
            int ly = (int)System.Math.Floor(pos.Y) - settlement.GroundY;
            int lz = (int)System.Math.Floor(pos.Z) - settlement.Min.Z;
            for (int dx = -TavernSeatReach; dx <= TavernSeatReach; dx++)
                for (int dz = -TavernSeatReach; dz <= TavernSeatReach; dz++)
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        int x = lx + dx, y = ly + dy, z = lz + dz;
                        if (!s.InBounds(x, y, z) || s.Get(x, y, z) == 0 || !FurnitureShapes.IsSeat(ShapeCode.ShapeOf(s.GetShape(x, y, z))))
                        {
                            continue;
                        }

                        var c = new Vector3i(settlement.Min.X + x, settlement.GroundY + y, settlement.Min.Z + z);
                        if (seen.Add(c))
                        {
                            seats.Add(c);
                        }
                    }
        }

        return seats;
    }

    /// <summary>A resident's evening seat (#2122): the next free tavern seat, else the nearest free chair by its bed, else
    /// none (the evening is rested standing) — never a seat another resident already has. The tavern cursor used to wrap,
    /// so a settlement with more residents than tavern chairs seated two people on one.</summary>
    private Vector3i? EveningSeatFor(SettlementInstance settlement, List<Vector3i> tavernSeats, ref int cursor, Vector3i? bed,
        ServerNpc? benchSitter = null)
    {
        if (NextFreeSeat(tavernSeats, ref cursor) is { } tavern)
        {
            return tavern;
        }

        // The lively places: once the tavern is full, the next ones spend the evening on a bench of the plaza (in dry
        // weather — the routine sends a bench sitter home when it rains); only then the chair at home.
        if (benchSitter is not null && FreePlazaBench(settlement) is { } bench)
        {
            benchSitter.BenchSeat = true;
            return bench;
        }

        return bed is { } own ? NearestLayoutSeat(settlement, own) : null;
    }

    /// <summary>A bench of the settlement's street furniture nobody has as a seat yet: the seat block beside a bench
    /// marker (the marker is the cell in front of it), or null — every settlement without such markers.</summary>
    private Vector3i? FreePlazaBench(SettlementInstance settlement)
    {
        var s = settlement.Layout;
        if (s is null)
        {
            return null;
        }

        foreach (var (type, pos) in settlement.Markers)
        {
            if (type != SettlementPlanBaker.BenchMarker)
            {
                continue;
            }

            var front = pos.ToBlock();
            int lx = WorldConstants.WrapDeltaX(front.X - settlement.Min.X, _world.Circumference), ly = front.Y - settlement.GroundY, lz = front.Z - settlement.Min.Z;
            foreach (var (dx, dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                int x = lx + dx, z = lz + dz;
                if (!s.InBounds(x, ly, z) || s.Get(x, ly, z) == 0 || ShapeCode.ShapeOf(s.GetShape(x, ly, z)) != (int)BlockShape.Bench)
                {
                    continue;
                }

                var cell = new Vector3i(front.X + dx, front.Y, front.Z + dz);
                if (!SeatClaimed(cell))
                {
                    return cell;
                }
            }
        }

        return null;
    }

    /// <summary>The unclaimed chair nearest to a bed in the settlement's layout, within <see cref="HomeSeatReach"/>; null
    /// when none.</summary>
    private Vector3i? NearestLayoutSeat(SettlementInstance settlement, Vector3i bed)
    {
        var s = settlement.Layout;
        if (s is null)
        {
            return null;
        }

        int lx = bed.X - settlement.Min.X, ly = bed.Y - settlement.GroundY, lz = bed.Z - settlement.Min.Z;
        Vector3i? best = null;
        int bestD = int.MaxValue;
        for (int dx = -HomeSeatReach; dx <= HomeSeatReach; dx++)
            for (int dz = -HomeSeatReach; dz <= HomeSeatReach; dz++)
            {
                int x = lx + dx, z = lz + dz;
                if (!s.InBounds(x, ly, z) || s.Get(x, ly, z) == 0 || !FurnitureShapes.IsSeat(ShapeCode.ShapeOf(s.GetShape(x, ly, z))))
                {
                    continue;
                }

                int d = dx * dx + dz * dz;
                if (d >= bestD)
                {
                    continue;
                }

                var cell = new Vector3i(settlement.Min.X + x, settlement.GroundY + ly, settlement.Min.Z + z);
                if (!SeatClaimed(cell))
                {
                    bestD = d;
                    best = cell;
                }
            }

        return best;
    }

    /// <summary>Test seam (#1887): per settlement its tier, the count of its grown-ups' beds (a child's marked bed is not
    /// one of them) and the residents (role, job, bed) the server keeps — the children among them, role "child".</summary>
    public IReadOnlyList<(string Name, string Tier, int Beds, IReadOnlyList<(string Role, string Job, Vector3i? Bed, Vector3f Home)> Residents)> SettlementResidentsForTest
        => _settlements.Where(s => !s.Ruined).Select(s => (s.Name, s.Tier, GrownUpBedCount(s),
            (IReadOnlyList<(string, string, Vector3i?, Vector3f)>)_npcs.Where(n => n.Settlement == s.Name && n.Role != "guardian")
                .Select(n => (n.Role, n.Job, n.Bed, n.Home)).ToList())).ToList();

    private int GrownUpBedCount(SettlementInstance s)
    {
        var beds = SettlementBeds(s);
        TakeChildBeds(s, beds);
        return beds.Count;
    }

    /// <summary>Test seam (#1887): the resident cap of a tier.</summary>
    public static int ResidentCapForTest(string tier) => ResidentCap(tier);

    /// <summary>Test seam (the lively places): everybody whose evening seat is a bench of the plaza — the seat, whether the
    /// settlement's layout has a bench block there and whether one of its bench markers stands right beside it.</summary>
    public IReadOnlyList<(int Id, string Settlement, Vector3i Seat, bool IsBench, bool ByABenchMarker)> BenchSittersForTest
        => _npcs.Where(n => n.BenchSeat && n.Seat.HasValue).Select(n =>
        {
            var seat = n.Seat!.Value;
            int circ = _world.Circumference;
            var place = _settlements.FirstOrDefault(s => s.Name == n.Settlement);
            int lx = place is null ? 0 : WorldConstants.WrapDeltaX(seat.X - place.Min.X, circ), ly = place is null ? 0 : seat.Y - place.GroundY,
                lz = place is null ? 0 : seat.Z - place.Min.Z;
            bool bench = place?.Layout is { } layout && layout.InBounds(lx, ly, lz) && layout.Get(lx, ly, lz) != 0
                && ShapeCode.ShapeOf(layout.GetShape(lx, ly, lz)) == (int)BlockShape.Bench;
            bool marked = place is not null && place.Markers.Any(m =>
                m.Type == SettlementPlanBaker.BenchMarker && m.Pos.ToBlock() is var front && front.Y == seat.Y
                && System.Math.Abs(WorldConstants.WrapDeltaX(front.X - seat.X, circ)) + System.Math.Abs(front.Z - seat.Z) == 1);
            return (n.Id, n.Settlement, seat, bench, marked);
        }).ToList();

    /// <summary>Test seam (the lively places): a villager gets a plaza bench as its evening seat, as the spawner hands it
    /// out once the tavern is full.</summary>
    public void GiveBenchSeatForTest(int npcId, Vector3i bench)
    {
        if (_npcs.FirstOrDefault(n => n.Id == npcId) is { } npc)
        {
            SetNpcSeat(npc, bench);
            npc.BenchSeat = true;
        }
    }
}
