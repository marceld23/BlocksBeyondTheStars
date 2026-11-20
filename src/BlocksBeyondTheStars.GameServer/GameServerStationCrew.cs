// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The crew of a kit station (#1874, Marcel 2026-09-13: one cabin per crew member). Every <c>cabin</c> marker is
/// one resident with its own bed; the posts — vendor, mission board, greenhouse, hangar, medbay, canteen — are
/// staffed by residents in marker order, residents without a post stroll the arrival hall by day. The routine
/// (#1867) does the rest: the post by day, a seat in the canteen or bar (<c>lounge</c> markers) in the evening,
/// the own bed at night. No filler crew: the cabins ARE the crew. Players may walk into the cabins.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>How far around a cabin marker the resident's own bed and chair are searched.</summary>
    private const int CabinFurnitureReach = 4;

    /// <summary>How far around a lounge marker the evening seats are collected.</summary>
    private const int LoungeSeatReach = 8;

    /// <summary>The name-plate label of a station's crew member who keeps no shop (#3768).</summary>
    internal const string StationCrewNameKey = "npc.role.station_crew";

    /// <summary>The label of the residents of a traders settlement who keep no shop (#3768): plain settlers.</summary>
    internal const string SettlerNameKey = "npc.role.settler";

    /// <summary>
    /// The name-plate label an NPC takes from its place's theme (#3768 — Justus: "above this NPC it says trader, and I can
    /// only talk to him"). A place's theme picks its people's clothes; as a label, <c>npc.theme.traders</c> ("Trader", in
    /// German the same word as the vendor's) promised a shop on everybody at a station and in a traders settlement. Only an
    /// NPC of the vendor role trades, so the traders theme labels its other people settlers; every other theme is a trade
    /// nobody mistakes for a shop (settler, miner, researcher).
    /// </summary>
    internal static string ThemeNameKey(string theme)
        => theme == "traders" ? SettlerNameKey : "npc.theme." + theme;

    /// <summary>A station's crew member who keeps no shop and gives no jobs is "Crew" (#3768). Called right after
    /// <see cref="MakeNpc"/>; a profession or an authored character applied afterwards still names itself.</summary>
    private static void LabelStationCrew(ServerNpc npc)
    {
        if (npc.Role == "settler")
        {
            npc.NameKey = StationCrewNameKey;
        }
    }

    /// <summary>Test seam (#3768): the role and the name-plate label key of every NPC of the active world.</summary>
    public IReadOnlyList<(string Role, string NameKey)> NpcLabelsForTest() => _npcs.Select(n => (n.Role, n.NameKey)).ToList();

    private void SpawnKitStationCrew(BoardableStation station)
    {
        _stationCrewSpotsTaken.Clear();
        var rng = new System.Random(unchecked((int)(_meta.Seed ^ WorldGenerator.StableHash("station-npc:" + station.Id))));
        BeginAuthoredCasting(station.Id); // #1150: at most one authored face per place

        var cabins = station.Markers.Where(m => m.Type == "cabin").ToList();
        var posts = new List<(string Role, Vector3f Pos, NpcProfession? Profession, string Marker)>();
        foreach (var (type, pos) in station.Markers)
        {
            var profession = NpcProfessions.ByMarker(type); // an author's profession post (2026-09)
            string? role = type switch
            {
                "vendor" => "vendor",
                "mission_board" => "quartermaster",
                "greenhouse" or "hangar" or "heal_tank" or "lounge" => "settler",
                _ => profession?.Role,
            };
            if (role != null)
            {
                posts.Add((role, pos, profession, type));
            }
        }

        // 2026-09: on a station with profession rooms the vendors and the quartermaster are staffed first, then the
        // professions, then the settler posts (hangar, medbay, greenhouse, lounges) — each profession room brings its keeper's
        // cabin, but its post might otherwise fall behind a settler post past the last cabin. A station without profession
        // posts (every station of an older world) keeps its exact order.
        // The lively places: a station with a purpose staffs its stalls first as well — a trade station's four trades are
        // only all aboard when every stall has its trader.
        if (posts.Any(post => post.Profession != null) || StationPurposeOf(station.Id).Length > 0)
        {
            posts = posts.Where(post => post.Profession == null && post.Role is "vendor" or "quartermaster")
                .Concat(posts.Where(post => post.Profession != null))
                .Concat(posts.Where(post => post.Profession == null && post.Role is not ("vendor" or "quartermaster")))
                .ToList();
        }

        var seats = LoungeSeats(station);
        string crewTheme = StationCrewTheme(station.Id); // the lively places: the purpose's trade ("traders" without one)
        int vendorIndex = 0, seatCursor = 0, added = 0;
        var withoutPost = new List<ServerNpc>(); // the lively places: one of them walks the posts by day
        for (int i = 0; i < cabins.Count; i++)
        {
            var (_, cabinPos) = cabins[i];
            bool hasPost = i < posts.Count;
            string role = hasPost ? posts[i].Role : "settler";
            var profession = hasPost ? posts[i].Profession : null;
            string theme = profession is { Trades: true } ? profession.Theme
                : role == "vendor" ? KitStationVendorTheme(station.Id, vendorIndex++) : crewTheme;
            // Research staff are androids — the vendor of that trade, and on a station without a purpose everybody of it;
            // on a research station the crew only wears the trade's colours.
            bool robotic = (StationPurposes.LookOf(theme) == "researchers" && (role != "settler" || crewTheme == "traders"))
                || (role == "settler" && rng.NextDouble() < 0.3);

            // The cabin spot is where the resident rests at night (#1775: a standable cell that holds air); the crew
            // starts its day at the post (the hall for those without one), so a boarder meets a staffed station at once.
            var cabinSpot = StationCrewSpot(station, cabinPos, rng, jitter: 0);
            var work = hasPost
                ? StationCrewSpot(station, posts[i].Pos, rng, jitter: 0)
                : StationCrewSpot(station, station.Spawn, rng, jitter: 2);
            var npc = MakeNpc(role, theme, robotic, work, rng);
            LabelStationCrew(npc); // #3768: "Crew", not "Trader" — a profession below still names its own job
            npc.Rest = cabinSpot;
            npc.Work = work;
            npc.HasWork = true;
            npc.RoutineEnabled = true;
            if (role == "quartermaster")
            {
                npc.Name = StationGiverName(station.Id); // the mission-giver's name matches its missions (item 13)
            }

            if (profession != null)
            {
                ApplyProfession(npc, profession);
            }
            else if (hasPost)
            {
                npc.Job = role is "vendor" or "quartermaster" ? role : string.Empty; // the post leash + nameplate
                if (posts[i].Marker == "hangar")
                {
                    npc.Held = "npc_hammer"; // the lively places: the dock hand works on the deck where one can see it
                }

                ApplyAuthoredCharacter(npc, "station", station.Id); // #1128: a pack face may claim this slot
            }
            else
            {
                npc.Size = 0.9f + (float)rng.NextDouble() * 0.22f;
                withoutPost.Add(npc);
            }

            _npcs.Add(npc);
            npc.CabinBed = true; // #2655: the own bed (and the cabin chair as the fallback seat) — in the cabin, now and later
            SearchNpcFurniture(npc);
            // The evening in the canteen / bar — a free seat only (#2122: the wrapping cursor seated two crew members
            // on one chair); once the lounges are full the cabin chair stays the seat.
            if (NextFreeSeat(seats, ref seatCursor, npc) is { } lounge)
            {
                SetNpcSeat(npc, lounge);
            }

            added++;
        }

        AssignServiceRounds(withoutPost);
        if (added > 0)
        {
            _log.Info($"Spawned {added} crew NPCs at station '{station.Name}' ({cabins.Count} cabins, {posts.Count} posts).");
        }
    }

    /// <summary>Every chair and bench around the station's lounge markers, nearest first per lounge, no duplicates.</summary>
    private List<Vector3i> LoungeSeats(BoardableStation station)
    {
        var seats = new List<Vector3i>();
        var seen = new HashSet<Vector3i>();
        foreach (var (type, pos) in station.Markers)
        {
            if (type != "lounge")
            {
                continue;
            }

            var centre = pos.ToBlock();
            var found = new List<(double D, Vector3i C)>();
            for (int dx = -LoungeSeatReach; dx <= LoungeSeatReach; dx++)
                for (int dz = -LoungeSeatReach; dz <= LoungeSeatReach; dz++)
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        var c = new Vector3i(centre.X + dx, centre.Y + dy, centre.Z + dz);
                        var id = _world.GetBlockIfLoaded(c);
                        if (id.IsAir || !FurnitureShapes.IsSeat(ShapeCode.ShapeOf(_world.GetShape(c))) || seen.Contains(c))
                        {
                            continue;
                        }

                        found.Add((dx * dx + dz * dz + dy * dy * 4, c));
                    }

            found.Sort((a, b) => a.D != b.D ? a.D.CompareTo(b.D) : a.C.X != b.C.X ? a.C.X.CompareTo(b.C.X) : a.C.Z.CompareTo(b.C.Z));
            foreach (var (_, c) in found)
            {
                if (seen.Add(c))
                {
                    seats.Add(c);
                }
            }
        }

        return seats;
    }

    private static StationKitRecord ToRecord(StationComposition composition)
    {
        var rec = new StationKitRecord
        {
            Kit = composition.KitKey,
            Seed = composition.Seed,
            Revision = StationKitRecord.CurrentRevision,
            Exterior = new StationKitExteriorRecord { SolarWings = composition.SolarWings, Antennas = composition.Antennas, Domes = composition.Domes },
            Purpose = composition.Purpose,
            Colour = composition.Colour,
        };
        foreach (var m in composition.Modules)
        {
            rec.Modules.Add(new StationKitModuleRecord { Key = m.Key, X = m.X, Y = m.Y, Z = m.Z, Turns = m.Turns });
        }

        return rec;
    }

    private static StationComposition FromRecord(StationKitRecord rec)
    {
        var composition = new StationComposition
        {
            KitKey = rec.Kit,
            Seed = rec.Seed,
            SolarWings = rec.Exterior?.SolarWings ?? 0,
            Antennas = rec.Exterior?.Antennas ?? 0,
            Domes = rec.Exterior?.Domes ?? 0,
            Purpose = rec.Purpose,
            Colour = rec.Colour,
        };
        foreach (var m in rec.Modules)
        {
            composition.Modules.Add(new PlacedKitModule(m.Key, m.X, m.Y, m.Z, m.Turns));
        }

        return composition;
    }

    /// <summary>Test seam (#1874): the pinned kit key of a station ("" when it is not a kit station).</summary>
    public string StationKitForTest(string stationId)
        => _meta.StationTemplates.TryGetValue(stationId, out var key) && key.StartsWith("kit:", System.StringComparison.Ordinal) ? key.Substring(4) : string.Empty;
}
