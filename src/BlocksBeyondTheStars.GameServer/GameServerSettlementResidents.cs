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
        string settlementTheme = SettlementTradeFor(settlement.Name);
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
        var seats = TavernSeats(settlement);
        int services = posts.Count(p => p.Job is "vendor" or "quartermaster");
        int count = System.Math.Max(System.Math.Min(beds.Count, ResidentCap(settlement.Tier)), services);
        var freeBeds = new List<Vector3i>(beds);
        int seatCursor = 0;

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

            string npcTheme = role == "vendor" ? VendorThemeFor(settlement.Name, vendorIndex++, settlementTheme) : settlementTheme;
            bool robotic = npcTheme == "researchers" && rng.Next(100) < 60; // most research staff are service androids — but not all (#711)

            // Feet on top of the floor block (markers sit centred in the air cell above it), never below the settlement.
            var standing = new Vector3f(home.X, (float)System.Math.Floor(System.Math.Max(settlement.Min.Y + 1f, home.Y)), home.Z);
            var npc = MakeNpc(role, npcTheme, robotic, standing, rng);
            npc.Settlement = settlement.Name;
            npc.Job = job is "vendor" or "quartermaster" or "craftsman" or "innkeeper" or "gardener" ? job : string.Empty;
            npc.Work = standing;
            npc.HasWork = true;
            npc.Bed = bed;
            npc.FurnitureScanned = true; // #1887: the bed is assigned; the seat below
            if (seats.Count > 0)
            {
                npc.Seat = seats[seatCursor % seats.Count];
                seatCursor++;
            }
            else if (bed is { } own)
            {
                npc.Seat = NearestLayoutSeat(settlement, own);
            }

            if (role == "quartermaster")
            {
                npc.Name = CoinGiverName(settlement.Name); // the mission-giver's name matches its missions (item 13)
            }

            ApplyAuthoredCharacter(npc, "settlement", settlement.Name); // #1128: a pack face may claim this slot
            npc.RoutineEnabled = true; // #1867: villagers keep a daily routine
            _npcs.Add(npc);
        }

        // The professions (2026-09): one extra resident per profession post, after everyone above and with a generator of
        // their own — a settlement without these posts (every settlement of an older world) spawns exactly the people it
        // always did.
        SpawnProfessionResidents(settlement, freeBeds, seats, ref seatCursor);

        // The G.D.S. guardians (#1793): machines at their posts, never asleep, no bed.
        foreach (var (type, pos) in settlement.Markers)
        {
            if (type != "guard_post")
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

    /// <summary>The head cells of every bed in a settlement's stamped layout, in world space and scan order.</summary>
    private static List<Vector3i> SettlementBeds(SettlementInstance settlement, ushort bedId)
    {
        var beds = new List<Vector3i>();
        var s = settlement.Layout;
        if (s is null || bedId == 0)
        {
            return beds;
        }

        for (int x = 0; x < s.Width; x++)
            for (int z = 0; z < s.Length; z++)
                for (int y = 0; y < s.Height; y++)
                {
                    if (s.Get(x, y, z) == bedId && ShapeCode.ShapeOf(s.GetShape(x, y, z)) != (int)BlockShape.BedFoot)
                    {
                        beds.Add(new Vector3i(settlement.Min.X + x, settlement.GroundY + y, settlement.Min.Z + z));
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

    /// <summary>The chair nearest to a bed in the settlement's layout, within <see cref="HomeSeatReach"/>; null when none.</summary>
    private static Vector3i? NearestLayoutSeat(SettlementInstance settlement, Vector3i bed)
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
                if (d < bestD)
                {
                    bestD = d;
                    best = new Vector3i(settlement.Min.X + x, settlement.GroundY + ly, settlement.Min.Z + z);
                }
            }

        return best;
    }

    /// <summary>Test seam (#1887): per settlement its tier, bed count and the residents (role, job, bed) the server keeps.</summary>
    public IReadOnlyList<(string Name, string Tier, int Beds, IReadOnlyList<(string Role, string Job, Vector3i? Bed, Vector3f Home)> Residents)> SettlementResidentsForTest
        => _settlements.Where(s => !s.Ruined).Select(s => (s.Name, s.Tier, SettlementBeds(s).Count,
            (IReadOnlyList<(string, string, Vector3i?, Vector3f)>)_npcs.Where(n => n.Settlement == s.Name && n.Role != "guardian")
                .Select(n => (n.Role, n.Job, n.Bed, n.Home)).ToList())).ToList();

    /// <summary>Test seam (#1887): the resident cap of a tier.</summary>
    public static int ResidentCapForTest(string tier) => ResidentCap(tier);
}
