// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The world notices your base (#1120, stages 1–2): trader ships prefer landing on worlds with a founded
/// base (stage 1, see <c>PickLandableBody</c>) and hail the base owner over the radio when they set down;
/// a settler NPC moves in once a base holds a bed or a few machines anywhere inside it (stage 2, #2677) — they greet,
/// count as a KNOWN acquaintance, and appear in the "People you know" roster. No NPC ever damages a block.
/// <para><b>Residents (#1865):</b> every bed inside the base (core zone, sealed room, walled yard or closed room —
/// see <see cref="BaseIndex"/>) brings one more resident, up to <see cref="BaseResidentCap"/>: 1 + beds. Slot 0 is
/// the founding settler (today's identity and memory key); slots 1..4 are the people the beds brought. A job comes from
/// what the base holds (#1868): the trading post and the mission board first, then the professions, a guard for a walled
/// base, a gardener for crops, a craftsman for a workbench.</para>
/// <para><b>Beds and jobs belong to someone (#2656):</b> a resident keeps its bed while the bed stands and its job while
/// the post stands; a new bed or post goes to someone without one — the newest resident sleeps rough until one more bed
/// stands. When a bed goes, its own sleeper moves out (never the founder), and a rebuilt bed brings the same person back
/// (the lowest free slot). The assignment is saved per base (<c>WorldMetadata.BaseResidents</c>) and survives a reload.
/// A leak never evicts anyone: a bed or post that counted as inside keeps counting while it stands (#2653,
/// <see cref="BaseIndex"/>).</para>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Machines (block category "machine", the base core itself excluded) inside a base that bring its first
    /// settler — a bare marker post is a claim, not a home. Since #2677 they count anywhere inside the base (the rule beds
    /// use), and a single bed inside is enough on its own (<see cref="BaseEarnsFirstSettler"/>).</summary>
    private const int BaseSettlerMachineCount = 3;

    /// <summary>How often ONE base is (re)checked — round-robins over the bases on the active world.</summary>
    private const double BaseLifeScanInterval = 10.0;

    /// <summary>Most residents a base holds (Marcel 2026-09-13: 1 + one per bed, max five).</summary>
    private const int BaseResidentCap = 5;

    /// <summary>How far from its resting spot a resident looks for a chair or bench.</summary>
    private const int ResidentSeatReach = 10;

    /// <summary>One resident of a base: its slot and the NPC standing for it on the base's world (transient — NPCs
    /// are per-world and respawn via the scan; the world id keeps the sweep from touching same-numbered NPCs of
    /// other worlds, #1152).</summary>
    internal sealed class BaseResident
    {
        public int Slot;
        public string WorldId = string.Empty;
        public int NpcId;
    }

    private readonly Dictionary<int, List<BaseResident>> _baseResidents = new();

    /// <summary>#2718: per world (location id) — when its next base scan is due and its round-robin cursor.</summary>
    private readonly Dictionary<string, double> _nextBaseLifeAt = new(System.StringComparer.Ordinal);
    private readonly Dictionary<string, int> _baseLifeCursor = new(System.StringComparer.Ordinal);

    /// <summary>#2718: the uptime of the tick that ran the last base scan — at most one base is scanned per server tick,
    /// whatever the number of occupied worlds (a second world due in the same tick goes next tick).</summary>
    private double _baseLifeScannedAt = double.NegativeInfinity;

    /// <summary>Round-robin base scan of the active world (Guard-registered in the per-world tick loop): keeps each base's
    /// residents in step with its beds, posts and workshops, and removes them again when their base was dissolved. #2718:
    /// every occupied world keeps its own timer and cursor — the scan used to run once per tick after the world loop, so
    /// only the last-ticked world's bases ever got residents.</summary>
    private void TickBaseLife()
    {
        string loc = _world.LocationId;
        if ((_nextBaseLifeAt.TryGetValue(loc, out double due) && _uptime < due) || _uptime <= _baseLifeScannedAt)
        {
            return;
        }

        _baseLifeScannedAt = _uptime;
        _nextBaseLifeAt[loc] = _uptime + BaseLifeScanInterval;
        SweepDissolvedBaseResidents();

        var here = _bases.Where(b => b.Planet == loc).ToList();
        if (here.Count == 0)
        {
            return;
        }

        int cursor = _baseLifeCursor.TryGetValue(loc, out int c) ? c : 0;
        _baseLifeCursor[loc] = cursor + 1;
        UpdateBaseResidents(here[cursor % here.Count]);
    }

    /// <summary>A dissolved base takes its residents with it — but only ever on their own world: NPC ids restart at 1
    /// per world, so a blind remove-by-id could delete an unrelated NPC elsewhere (#1152).</summary>
    private void SweepDissolvedBaseResidents()
    {
        foreach (var (baseId, list) in _baseResidents.ToList())
        {
            if (_bases.Any(b => b.Id == baseId))
            {
                continue;
            }

            int removed = 0;
            foreach (var r in list.Where(r => r.WorldId == _world.LocationId).ToList())
            {
                removed += _npcs.RemoveAll(n => n.Id == r.NpcId && n.BaseId == baseId);
                list.Remove(r);
            }

            _baseMarkers.RemoveAll(m => m.BaseId == baseId);
            if (list.Count == 0)
            {
                _baseResidents.Remove(baseId);
            }

            if (removed > 0)
            {
                BroadcastNpcs();
            }
        }

        // #2656: a dissolved base's saved residents go with it (every base of the save is in _bases, whatever its world).
        if (_meta.BaseResidents.Count > 0)
        {
            var standing = new HashSet<string>(_bases.Select(BaseBoardKey), System.StringComparer.Ordinal);
            var stale = _meta.BaseResidents.Keys.Where(k => !standing.Contains(k)).ToList();
            if (stale.Count > 0)
            {
                foreach (var key in stale)
                {
                    foreach (var r in _meta.BaseResidents[key])
                    {
                        RetireResident(key, r); // #2716: a later base with the same id never gets these people
                    }

                    _meta.BaseResidents.Remove(key);
                }

                _repo.SaveMetadata(_meta);
            }
        }
    }

    /// <summary>
    /// Brings one base's residents in step with the base (#1865, #2656). Who lives there is the base's saved plan
    /// (<c>WorldMetadata.BaseResidents</c>, slot → bed and job), which this scan updates and saves when it changes — so a
    /// reload or a return to the planet brings back the same people in the same beds at the same jobs.
    /// <list type="number">
    /// <item>A resident whose bed is gone (or no longer counts) lost it in this scan.</item>
    /// <item>The count: 1 + one per bed, at most <see cref="BaseResidentCap"/>, once the base is founded. The surplus moves
    /// out — first a resident who lost the bed in this scan, else the bedless resident with the highest slot, never the
    /// founding settler (slot 0) — and the owner hears who left (#2715).</item>
    /// <item>Whoever is missing moves in at the lowest free slot. Look, name and memory follow from the slot, so a rebuilt
    /// bed brings back the person who left; a resident's identity is pinned in the plan once it moves in (#2716,
    /// <see cref="PinResidentIdentity"/>), so the base id shifting after a restart never makes them someone else.</item>
    /// <item>Jobs stay with their resident while the post stands; a job nobody holds goes, in priority order, to a resident
    /// without one (<see cref="AssignBaseJobs"/>).</item>
    /// <item>Beds stay with their sleeper; free beds go to the bedless (<see cref="AssignBaseBeds"/>) — whoever lost theirs
    /// in this scan first (a bed moved is still that person's), then the longest-bedless.</item>
    /// </list>
    /// </summary>
    private void UpdateBaseResidents(ServerBase b)
    {
        var idx = RefreshBaseIndex(b);
        if (!_baseResidents.TryGetValue(b.Id, out var list))
        {
            _baseResidents[b.Id] = list = new List<BaseResident>();
        }

        // A world switch cleared the NPC list: a stale mapping must not block the respawn (#1152).
        list.RemoveAll(r => r.WorldId == _world.LocationId && !_npcs.Any(n => n.Id == r.NpcId && n.BaseId == b.Id));

        string key = BaseBoardKey(b);
        var plan = _meta.BaseResidents.TryGetValue(key, out var saved) ? saved : new List<BaseResidentRecord>();
        string savedSignature = BasePlanSignature(plan);
        int residentsBefore = plan.Count; // the saved list is changed in place below
        var live = new Dictionary<int, ServerNpc>();
        foreach (var npc in ResidentNpcs(b))
        {
            live[npc.BaseSlot] = npc;
            if (!plan.Any(r => r.Slot == npc.BaseSlot))
            {
                plan.Add(new BaseResidentRecord { Slot = npc.BaseSlot, Bed = BedCellKey(npc.Bed), Job = npc.Job }); // alive before the plan knew them
            }
        }

        // 1. A bed that is gone — mined, moved, or no longer inside — was lost in this scan.
        var beds = new HashSet<Vector3i>(idx.BedHeads);
        var lostBed = new HashSet<int>();
        var heldBeds = new HashSet<Vector3i>();
        foreach (var r in plan.OrderBy(r => r.Slot))
        {
            if (!TryParseBedCell(r.Bed, out var cell))
            {
                r.Bed = string.Empty;
            }
            else if (!beds.Contains(cell))
            {
                r.Bed = string.Empty;
                lostBed.Add(r.Slot);
            }
            else if (!heldBeds.Add(cell))
            {
                r.Bed = string.Empty; // two on one bed (a hand-edited save): the lower slot keeps it
            }
        }

        // 2. How many live here. The first settler comes for a bed or a few machines anywhere inside the base (#2677); once
        // a base has one, beds alone bring the rest (and a machine mined for a moment never sends the founding settler away —
        // slot 0 is saved with the plan).
        bool founded = plan.Any(r => r.Slot == 0);
        int desired = founded || BaseEarnsFirstSettler(b, idx)
            ? System.Math.Min(BaseResidentCap, 1 + idx.BedHeads.Count)
            : 0;

        bool changed = false;
        while (plan.Count > desired)
        {
            var leaving = plan.Where(r => r.Slot != 0 && lostBed.Contains(r.Slot)).OrderByDescending(r => r.Slot).FirstOrDefault()
                ?? plan.Where(r => r.Slot != 0 && r.Bed.Length == 0).OrderByDescending(r => r.Slot).FirstOrDefault()
                ?? plan.OrderByDescending(r => r.Slot).First();
            plan.Remove(leaving);
            string leaverName = string.Empty;
            if (live.TryGetValue(leaving.Slot, out var gone))
            {
                leaverName = gone.Name;
                _npcs.RemoveAll(n => n.Id == gone.Id && n.BaseId == b.Id); // a bed went — its sleeper moves on
                live.Remove(leaving.Slot);
            }

            list.RemoveAll(r => r.Slot == leaving.Slot && r.WorldId == _world.LocationId);
            changed = true;
            TellResidentMovedOut(b, leaving, leaverName);
            RetireResident(key, leaving); // #2716: a rebuilt bed brings this person back, nobody else gets their key
        }

        // 3. Moving in: the lowest free slot.
        while (plan.Count < desired)
        {
            int slot = 0;
            while (plan.Any(r => r.Slot == slot))
            {
                slot++;
            }

            plan.Add(new BaseResidentRecord { Slot = slot });
        }

        // #2716: whoever has no pinned identity yet — a newcomer, or a resident alive before the plan knew them — gets one now.
        // A brand-new plan goes into the metadata at once: every key reader in this scan (the spawn's standings, a job's
        // role on the owner's memory) looks the pins up there — before, they fell back to the id-minted key, which may be
        // another base's resident's. SaveBasePlan below still writes the save only when the plan changed.
        foreach (var r in plan)
        {
            PinResidentIdentity(b, r);
        }

        if (plan.Count > 0)
        {
            _meta.BaseResidents[key] = plan;
        }

        plan.Sort((p, q) => p.Slot.CompareTo(q.Slot));
        var patrol = plan.Count > 0 ? GuardPatrolFor(b) : null;

        // An index built over unloaded chunks cannot tell a post that went from a room it cannot see (nor a walled round from
        // open ground): it keeps the jobs the residents hold until the area is loaded and the index rebuilt.
        AssignBaseJobs(plan, BaseJobs(idx, patrol), provisional: !idx.BuiltLoaded);
        AssignBaseBeds(b, idx, plan, live, lostBed, patrol);
        foreach (var r in plan)
        {
            if (BaseJobPost(idx, r.Job) is { } post)
            {
                r.Post = BedCellKey(post);
            }
            else if (idx.BuiltLoaded || r.Job.Length == 0)
            {
                r.Post = string.Empty;
            }
        }

        // 4. The people: whoever has no NPC on this world yet — a newcomer, or a resident after a reload or a return.
        foreach (var r in plan)
        {
            if (!live.ContainsKey(r.Slot))
            {
                var npc = SpawnBaseResident(b, r);
                list.Add(new BaseResident { Slot = r.Slot, WorldId = b.Planet, NpcId = npc.Id });
                live[r.Slot] = npc;
                changed = true;
            }
        }

        list.Sort((p, q) => p.Slot.CompareTo(q.Slot));
        changed |= AssignBaseResidents(b, idx, plan, live, patrol);
        SaveBasePlan(key, plan, savedSignature);
        if (changed)
        {
            BroadcastNpcs();
        }

        if (plan.Count != residentsBefore)
        {
            BroadcastBasesOn(b.Planet); // #2679: the core's readout shows "residents n/5"
        }
    }

    /// <summary>#2677: whether a base without its founding settler earns one now — a bed inside the base, or
    /// <see cref="BaseSettlerMachineCount"/> machines anywhere inside it (<see cref="BaseMachinesInside"/>).</summary>
    private bool BaseEarnsFirstSettler(ServerBase b, BaseIndex idx)
        => idx.BedHeads.Count > 0 || BaseMachinesInside(b, idx) >= BaseSettlerMachineCount;

    /// <summary>How many residents a base's saved plan holds (the founding settler included) — any world.</summary>
    private int BaseResidentCount(ServerBase b)
        => _meta.BaseResidents.TryGetValue(BaseBoardKey(b), out var plan) ? plan.Count : 0;

    /// <summary>
    /// #2679: a bed was placed. When it stands in or near a base of the player's (owned or allied, within the base's reach)
    /// the base is rescanned at once — the bed is counted, a resident may move in — and the player hears what it did:
    /// it counts (residents n/5), it founded the base (the first settlers move in), the base is full, or it stands outside
    /// the base (with what "inside" means, and that nobody lives there yet when that is so). A bed far from every base of
    /// theirs, aboard a parked ship or on a station is a bed to sleep in — nothing is said.
    /// </summary>
    private void OnBaseBedPlaced(PlayerSession session, Vector3i head)
    {
        if (_world.Planet?.Void == true || IsPlayerStationWorld(_world.LocationId)
            || ShipInteriorContains(new Vector3f(head.X + 0.5f, head.Y + 0.5f, head.Z + 0.5f)))
        {
            return;
        }

        string player = session.State.PlayerId;
        var cell = WorldConstants.CanonicalBlock(head, _world.Circumference);
        var b = _bases.FirstOrDefault(x => x.Planet == _world.LocationId && x.OwnerId == player && WithinBaseWallReach(x, cell))
            ?? _bases.FirstOrDefault(x => x.Planet == _world.LocationId && AreAllied(x.OwnerId, player) && WithinBaseWallReach(x, cell));
        if (b is null)
        {
            return;
        }

        bool wasFounded = BaseResidentCount(b) > 0;
        if (_baseIndex.TryGetValue(b.Id, out var stale))
        {
            stale.Dirty = true;
        }

        UpdateBaseResidents(b);
        var idx = RefreshBaseIndex(b);
        int residents = BaseResidentCount(b);
        string key = !idx.BedHeads.Contains(cell) ? residents > 0 ? "srv.base.bed_outside" : "srv.base.bed_outside_no_settler"
            : !wasFounded ? "srv.base.bed_founds"
            : 1 + idx.BedHeads.Count > BaseResidentCap ? "srv.base.bed_full"
            : "srv.base.bed_counts";

        // Resolved here in the player's language: the line carries two numbers, one "@srv.*:arg" token only one.
        string text = Localize(session.Locale, key)
            .Replace("{n}", residents.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Replace("{max}", BaseResidentCap.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Send(session, new ServerMessage { Text = text });
    }

    /// <summary>The jobs a base offers, in priority order (#1868): trading post, mission board, the professions' posts,
    /// guard (a walled base or a sentry post), gardener (crops, trays, saplings), craftsman (workbench, forge).</summary>
    private static List<string> BaseJobs(BaseIndex idx, List<Vector3f>? patrol)
    {
        var jobs = new List<string>();
        if (idx.VendorPosts.Count > 0)
        {
            jobs.Add("vendor");
        }

        if (idx.Boards.Count > 0)
        {
            jobs.Add("quartermaster");
        }

        // The professions (2026-09): a post the player built is a job they asked for — right after the two classic posts.
        foreach (var profession in NpcProfessions.All)
        {
            if (idx.ProfessionPosts.TryGetValue(profession.Job, out var professionPosts) && professionPosts.Count > 0)
            {
                jobs.Add(profession.Job);
            }
        }

        if (patrol != null || idx.SentryPosts.Count > 0)
        {
            jobs.Add("guard");
        }

        if (idx.Crops.Count + idx.Trays.Count + idx.Saplings.Count > 0)
        {
            jobs.Add("gardener");
        }

        if (idx.Workbenches.Count + idx.Forges.Count > 0)
        {
            jobs.Add("craftsman");
        }

        return jobs;
    }

    /// <summary>#2656: a job stays with its resident while its post stands; a job nobody holds goes, in priority order, to
    /// the resident without a job with the lowest slot. A new post never takes anybody off the job they have. A
    /// <paramref name="provisional"/> index (built over unloaded chunks, <c>BaseIndex.BuiltLoaded</c> false) hands out no new
    /// job and keeps a held job it cannot judge: one without a saved post cell (a guard on a walled round, whose walls may
    /// lie in unloaded chunks). A job with a post cell is judged as always — that cell counts as inside while its block
    /// stands (<c>BaseIndex.StickyInside</c>), so a job whose post is missing really lost it.</summary>
    private static void AssignBaseJobs(List<BaseResidentRecord> plan, List<string> jobs, bool provisional = false)
    {
        var open = new HashSet<string>(jobs, System.StringComparer.Ordinal);
        var held = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (var r in plan)
        {
            if (r.Job.Length == 0)
            {
                continue;
            }

            bool cannotJudge = provisional && r.Post.Length == 0;
            if (!held.Add(r.Job) || (!open.Remove(r.Job) && !cannotJudge))
            {
                r.Job = string.Empty; // its post went (or a second holder in a hand-edited save)
            }
        }

        if (provisional)
        {
            return;
        }

        foreach (var job in jobs)
        {
            if (!open.Contains(job))
            {
                continue;
            }

            var jobless = plan.FirstOrDefault(r => r.Job.Length == 0);
            if (jobless is null)
            {
                break;
            }

            jobless.Job = job;
            open.Remove(job);
        }
    }

    /// <summary>#2656: free beds of the base go to the bedless — first those who lost theirs in this scan (a moved bed stays
    /// with its sleeper), then the longest-bedless (<c>ServerNpc.BedlessSince</c>; a newcomer queues from now), then by slot —
    /// each taking the best free bed for its work spot (<see cref="BetterBed"/>). A bed another NPC of the world holds is not
    /// free.</summary>
    private void AssignBaseBeds(ServerBase b, BaseIndex idx, List<BaseResidentRecord> plan, Dictionary<int, ServerNpc> live,
        HashSet<int> lostBed, List<Vector3f>? patrol)
    {
        int circ = _world.Circumference;
        var held = new HashSet<Vector3i>();
        foreach (var r in plan)
        {
            if (TryParseBedCell(r.Bed, out var c))
            {
                held.Add(c);
            }
        }

        foreach (var n in _npcs)
        {
            if (n.BaseId != b.Id && n.Bed is { } other)
            {
                held.Add(WorldConstants.CanonicalBlock(other, circ));
            }
        }

        var free = idx.BedHeads.Where(c => !held.Contains(c)).ToList();
        if (free.Count == 0)
        {
            return;
        }

        var queue = plan.Where(r => r.Bed.Length == 0)
            .OrderBy(r => lostBed.Contains(r.Slot) ? 0 : 1)
            .ThenBy(r => live.TryGetValue(r.Slot, out var n) ? n.BedlessSince : _uptime)
            .ThenBy(r => r.Slot)
            .ToList();
        foreach (var r in queue)
        {
            if (free.Count == 0)
            {
                break;
            }

            var anchor = BaseJobAnchor(b, idx, r.Job, patrol);
            if (BestBedFor(anchor, Above(anchor), free) is { } best)
            {
                r.Bed = BedCellKey(best);
                free.Remove(best);
            }
        }
    }

    /// <summary>Where a resident with <paramref name="job"/> works — its post block, workbench, first garden site, first
    /// patrol stop or sentry post — or the core for a settler without one: the anchor its bed is chosen by.</summary>
    private static Vector3i BaseJobAnchor(ServerBase b, BaseIndex idx, string job, List<Vector3f>? patrol)
    {
        Vector3i? anchor = job switch
        {
            "gardener" => GardenSites(idx).Select(s => (Vector3i?)s).FirstOrDefault(),
            "guard" when patrol is { Count: > 0 } => patrol[0].ToBlock(),
            _ => BaseJobPost(idx, job),
        };

        return anchor ?? b.Cell;
    }

    /// <summary>The indexed block a job is tied to (<c>BaseResidentRecord.Post</c>): the trading post, the board, the
    /// workbench or forge, the profession's post, the first crop / sapling / tray, the sentry post; null for a guard on a
    /// walled round or no job.</summary>
    private static Vector3i? BaseJobPost(BaseIndex idx, string job) => job switch
    {
        "vendor" when idx.VendorPosts.Count > 0 => idx.VendorPosts[0],
        "quartermaster" when idx.Boards.Count > 0 => idx.Boards[0],
        "craftsman" when idx.Workbenches.Count > 0 => idx.Workbenches[0],
        "craftsman" when idx.Forges.Count > 0 => idx.Forges[0],
        "gardener" when idx.Crops.Count > 0 => idx.Crops[0],
        "gardener" when idx.Saplings.Count > 0 => idx.Saplings[0],
        "gardener" when idx.Trays.Count > 0 => idx.Trays[0],
        "guard" when idx.SentryPosts.Count > 0 => idx.SentryPosts[0],
        "" => null,
        _ when idx.ProfessionPosts.TryGetValue(job, out var posts) && posts.Count > 0 => posts[0],
        _ => null,
    };

    /// <summary>Writes a base's plan back to the metadata (and the save) when it changed; an empty plan leaves no entry.</summary>
    private void SaveBasePlan(string key, List<BaseResidentRecord> plan, string savedSignature)
    {
        bool stored = _meta.BaseResidents.ContainsKey(key);
        if (BasePlanSignature(plan) == savedSignature && stored == plan.Count > 0)
        {
            return;
        }

        if (plan.Count == 0)
        {
            _meta.BaseResidents.Remove(key);
        }
        else
        {
            _meta.BaseResidents[key] = plan;
        }

        _repo.SaveMetadata(_meta);
    }

    private static string BasePlanSignature(List<BaseResidentRecord> plan)
        => string.Join(";", plan.OrderBy(r => r.Slot).Select(r => r.Slot.ToString(System.Globalization.CultureInfo.InvariantCulture) + "|" + r.Bed + "|" + r.Job + "|" + r.Post
            + "|" + r.Seed + "|" + r.Key + "|" + r.Face.ToString(System.Globalization.CultureInfo.InvariantCulture)));

    /// <summary>A bed cell as the plan stores it: <c>"x,y,z"</c>, or empty for none.</summary>
    private static string BedCellKey(Vector3i? cell)
        => cell is { } c ? string.Join(",", c.X.ToString(System.Globalization.CultureInfo.InvariantCulture),
            c.Y.ToString(System.Globalization.CultureInfo.InvariantCulture), c.Z.ToString(System.Globalization.CultureInfo.InvariantCulture)) : string.Empty;

    private static bool TryParseBedCell(string text, out Vector3i cell)
    {
        cell = default;
        var parts = text.Split(',');
        if (parts.Length != 3
            || !int.TryParse(parts[0], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int x)
            || !int.TryParse(parts[1], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int y)
            || !int.TryParse(parts[2], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int z))
        {
            return false;
        }

        cell = new Vector3i(x, y, z);
        return true;
    }

    /// <summary>The live NPCs of a base's residents on the active world, in slot order.</summary>
    private List<ServerNpc> ResidentNpcs(ServerBase b)
    {
        var result = new List<ServerNpc>();
        if (!_baseResidents.TryGetValue(b.Id, out var list))
        {
            return result;
        }

        foreach (var r in list)
        {
            if (r.WorldId == _world.LocationId && _npcs.FirstOrDefault(n => n.Id == r.NpcId && n.BaseId == b.Id) is { } npc)
            {
                result.Add(npc);
            }
        }

        return result;
    }

    /// <summary>
    /// Applies a base's plan to its residents' NPCs (#1865/#1868/#2656), in slot order: the job (role, nameplate, what
    /// they carry), the bed the plan gives them and a resting spot beside it (or near the core), the evening seat — kept
    /// while it stands inside the base, else the nearest free one within <see cref="ResidentSeatReach"/> of the resting
    /// spot — the work spot and the post markers. Returns whether anything the client sees changed.
    /// </summary>
    private bool AssignBaseResidents(ServerBase b, BaseIndex idx, List<BaseResidentRecord> plan, Dictionary<int, ServerNpc> live,
        List<Vector3f>? patrol)
    {
        var residents = plan.Select(r => live[r.Slot]).ToList();
        bool changed = false;
        int circ = _world.Circumference;

        // Seats are kept like beds (#2655): a seat that still stands inside the base stays its sitter's; a seat another
        // NPC of this world holds is not free.
        var seats = new HashSet<Vector3i>(idx.Seats);
        var takenSeats = new HashSet<Vector3i>();
        foreach (var n in _npcs)
        {
            if (n.BaseId != b.Id && n.Seat is { } other)
            {
                takenSeats.Add(WorldConstants.CanonicalBlock(other, circ));
            }
        }

        foreach (var npc in residents)
        {
            if (npc.Seat is { } s && seats.Contains(WorldConstants.CanonicalBlock(s, circ)) && takenSeats.Add(WorldConstants.CanonicalBlock(s, circ)))
            {
                continue;
            }

            if (npc.Seat is not null)
            {
                SetNpcSeat(npc, null);
                npc.PhaseCheckedAt = double.NegativeInfinity;
            }
        }

        var takenHomes = new HashSet<Vector3i>();
        string boardKey = BaseBoardKey(b);
        for (int i = 0; i < residents.Count; i++)
        {
            var npc = residents[i];
            changed |= ApplyResidentJob(b, npc, plan[i].Job, boardKey);

            // --- bed + resting spot ---
            Vector3i? bed = TryParseBedCell(plan[i].Bed, out var head) ? head : null;
            Vector3f? rest = bed is { } h ? BedSideSpot(h, takenHomes) : null;
            rest ??= ResidentHomeNear(b.Cell, takenHomes);
            takenHomes.Add(rest.Value.ToBlock());
            if (!System.Nullable.Equals(npc.Bed, bed))
            {
                SetNpcBed(npc, bed);
                npc.PhaseCheckedAt = double.NegativeInfinity; // re-think where to sleep
            }

            npc.Rest = rest.Value;

            // --- the evening seat: kept, or the nearest free one within reach of the resting spot ---
            if (npc.Seat is null)
            {
                Vector3i? seat = null;
                double bestSq = ResidentSeatReach * ResidentSeatReach;
                foreach (var s in idx.Seats)
                {
                    double d = WrapDistSq(npc.Rest, s);
                    if (d <= bestSq && !takenSeats.Contains(s))
                    {
                        bestSq = d;
                        seat = s;
                    }
                }

                if (seat is { } taken)
                {
                    takenSeats.Add(taken);
                    SetNpcSeat(npc, taken);
                    npc.PhaseCheckedAt = double.NegativeInfinity;
                }
            }

            // --- the work spot ---
            var work = ResidentWorkSpot(npc, idx, patrol, takenHomes) ?? npc.Rest;
            if (!npc.HasWork || !npc.Work.Equals(work))
            {
                npc.Work = work;
                npc.HasWork = true;
                npc.PhaseCheckedAt = double.NegativeInfinity;
            }

            npc.Patrol = npc.Job == "guard" ? patrol : null;

            // #1248/#1658: a resident whose idle spot got built over or flooded moves out of it at once.
            if (npc.Pose == 0 && npc.Goal is null)
            {
                var hc = npc.Home.ToBlock();
                if (StandableSpot(hc.X, hc.Y, hc.Z) is null)
                {
                    npc.Home = npc.Rest;
                    npc.Pos = npc.Rest;
                    npc.Path = null;
                    changed = true;
                }
            }
        }

        changed |= RefreshBaseMarkers(b, idx, residents);
        return changed;
    }

    /// <summary>Gives a resident its job: the role the roster and the dialogues know, the nameplate's role key, the
    /// trade profession and what it carries. Returns whether the client-visible state changed.</summary>
    private bool ApplyResidentJob(ServerBase b, ServerNpc npc, string job, string boardKey)
    {
        if (npc.Job == job && npc.NameKey.Length > 0)
        {
            return false;
        }

        npc.Job = job;
        var profession = NpcProfessions.ByJob(job);
        string role = job is "vendor" or "quartermaster" ? job : profession?.Role ?? "settler";
        npc.NameKey = ResidentNameKey(job);
        // A base vendor's trade comes from the board key (body + cell), which is per place already — no change in #2698.
        npc.Theme = job == "vendor" ? ClassicTradeFor(boardKey) : profession is { Trades: true } ? profession.Theme : "settlers";
        npc.Held = job switch
        {
            "gardener" => "npc_hoe",
            "craftsman" => "npc_hammer",
            "guard" => "blade",
            _ => profession?.Held ?? string.Empty,
        };
        npc.SiteCursor = 0;
        npc.SiteUntil = 0;
        npc.PhaseCheckedAt = double.NegativeInfinity;

        if (npc.Role != role)
        {
            npc.Role = role;

            // The resident stays the same person (the memory key is the slot), only what they do changed.
            if (FindSessionByPlayerId(b.OwnerId) is { } owner
                && owner.State.NpcMemory.TryGetValue(BaseResidentKey(b, npc.BaseSlot), out var rel))
            {
                rel.Role = role;
                SendNpcStandings(owner);
            }
        }

        return true;
    }

    /// <summary>The nameplate's role key of a resident with <paramref name="job"/> (also the resident list's, #2717).</summary>
    private static string ResidentNameKey(string job) => job switch
    {
        "vendor" => "npc.role.vendor",
        "quartermaster" => "npc.role.quartermaster",
        "guard" => "npc.role.guard",
        "gardener" => "npc.role.gardener",
        "craftsman" => "npc.role.craftsman",
        _ => NpcProfessions.ByJob(job)?.NameKey ?? "npc.theme.settlers",
    };

    /// <summary>Where a resident works by day (#1868): beside its post, its workbench or its first garden site, on
    /// its patrol; null for a settler without a job (it idles at its resting spot).</summary>
    private Vector3f? ResidentWorkSpot(ServerNpc npc, BaseIndex idx, List<Vector3f>? patrol, HashSet<Vector3i> taken)
    {
        Vector3i? anchor = npc.Job switch
        {
            "vendor" when idx.VendorPosts.Count > 0 => idx.VendorPosts[0],
            "quartermaster" when idx.Boards.Count > 0 => idx.Boards[0],
            "craftsman" when idx.Workbenches.Count > 0 => idx.Workbenches[0],
            "craftsman" when idx.Forges.Count > 0 => idx.Forges[0],
            "gardener" => GardenSites(idx).Select(s => (Vector3i?)s).FirstOrDefault(),
            _ when idx.ProfessionPosts.TryGetValue(npc.Job, out var professionPosts) && professionPosts.Count > 0 => professionPosts[0],
            _ => null,
        };

        if (npc.Job == "guard" && patrol is { Count: > 0 })
        {
            return patrol[0];
        }

        if (NpcProfessions.ByJob(npc.Job) is { WorksOutside: true } && OutsideBaseWorkSpot(npc) is { } quarry)
        {
            return quarry; // the blockfarmer (2026-09) quarries beyond the walls
        }

        return anchor is { } a ? SpotBeside(a, taken) : null;
    }

    /// <summary>Where a resident rests beside its bed: a standable cell next to the head, then next to the foot.</summary>
    private Vector3f? BedSideSpot(Vector3i head, HashSet<Vector3i> taken)
    {
        var (fx, fz) = FurnitureShapes.TryBedPartnerOffset(_world.GetShape(head), out int dx, out int dz) ? (dx, dz) : (0, 0);
        var cells = new List<Vector3i>
        {
            new(head.X + fz, head.Y, head.Z - fx),
            new(head.X - fz, head.Y, head.Z + fx),
            new(head.X + fx + fz, head.Y, head.Z + fz - fx),
            new(head.X + fx - fz, head.Y, head.Z + fz + fx),
            new(head.X - fx, head.Y, head.Z - fz),
        };

        foreach (var c in cells)
        {
            if (!taken.Contains(c) && StandableSpot(c.X, c.Y, c.Z) is { } spot)
            {
                return spot;
            }
        }

        return SpotBeside(head, taken);
    }

    /// <summary>A standable cell beside a block (its four sides on its own level, then one down, then one up).</summary>
    private Vector3f? SpotBeside(Vector3i block, HashSet<Vector3i> taken)
    {
        foreach (int dy in new[] { 0, -1, 1 })
        {
            foreach (var (dx, dz) in WallFillDirs)
            {
                var c = new Vector3i(block.X + dx, block.Y + dy, block.Z + dz);
                if (!taken.Contains(c) && StandableSpot(c.X, c.Y, c.Z) is { } spot)
                {
                    return spot;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Where a base resident without a bed lives. The first version put them at a fixed core+(2, 1, 2) with no look
    /// at what stood there, so an owner who had built a wall, a machine or a stair on that spot got a settler
    /// permanently wedged inside it (#1248, a player report). Try the classic spot first (existing bases keep their
    /// settler where it was when it is free), then ring outwards through the base zone for the nearest column with
    /// two air cells over a floor that is not inside a parked ship and not another resident's spot.
    /// </summary>
    private Vector3f ResidentHomeNear(Vector3i core, HashSet<Vector3i> taken)
    {
        var legacy = new Vector3f(core.X + 2.5f, core.Y + 1f, core.Z + 2.5f);
        var classicCell = new Vector3i(core.X + 2, core.Y + 1, core.Z + 2);
        if (!taken.Contains(classicCell) && StandableSpot(classicCell.X, classicCell.Y, classicCell.Z) is { } classic)
        {
            return classic;
        }

        for (int r = 1; r <= BaseProtectionRadius; r++)
            for (int dx = -r; dx <= r; dx++)
                for (int dz = -r; dz <= r; dz++)
                {
                    if (System.Math.Max(System.Math.Abs(dx), System.Math.Abs(dz)) != r || (dx == 0 && dz == 0))
                    {
                        continue; // ring r only — inner rings were already searched
                    }

                    // Feet from two above the core down to two below it: a raised floor, a slope or a dug-out
                    // yard all count, a basement further down does not (the settler should be seen).
                    for (int y = core.Y + 2; y >= core.Y - 2; y--)
                    {
                        var c = new Vector3i(core.X + dx, y, core.Z + dz);
                        if (!taken.Contains(c) && StandableSpot(c.X, c.Y, c.Z) is { } spot)
                        {
                            return spot;
                        }
                    }
                }

        return legacy;
    }

    /// <summary>Where the base settler lives when nothing else is known (kept for the trader/visitor helpers).</summary>
    private Vector3f SettlerHomeNear(Vector3i core) => ResidentHomeNear(core, new HashSet<Vector3i>());

    /// <summary>The feet position for a cell a human-sized NPC can stand in: a blocking floor under two
    /// free cells, outside every parked ship's hull (nobody moves into the owner's cockpit); null otherwise.
    /// "Free" is judged the way the NPC WALKS (<see cref="NpcBodyBlockedAt"/>, fluids are a wall) and not only
    /// the way a body is entombed (<see cref="IsBodyBlockingCell"/>, fluids pass): the two disagreed on water,
    /// so a shore base homed its settler on the seabed under two water cells — a place the leash could never
    /// walk them out of, and that the re-home below kept calling "still a place to stand" (#1658). The floor is a
    /// real one (#1895): never the top of a table, a crate or a fence; a rug or a floor panel in the feet cell is
    /// walked through and carries the feet itself.</summary>
    private Vector3f? StandableSpot(int x, int y, int z)
    {
        if (!WithinBuildHeight(y) || NpcBodyBlockedAt(x, y, z) || NpcBodyBlockedAt(x, y + 1, z))
        {
            return null;
        }

        bool carried = (IsBodyBlockingCell(x, y, z) && NpcFootingAt(x, y, z) == NpcFooting.FloorPlate)
            || (IsBodyBlockingCell(x, y - 1, z) && NpcFootingAt(x, y - 1, z) == NpcFooting.Floor);
        if (!carried)
        {
            return null;
        }

        var feet = new Vector3f(x + 0.5f, y, z + 0.5f);
        return ShipInteriorContains(new Vector3f(feet.X, y + 0.5f, feet.Z)) ? null : feet;
    }

    /// <summary>
    /// The standable spot nearest <paramref name="near"/> on the square rings <paramref name="ringMin"/> …
    /// <paramref name="ringMax"/> blocks outside a pad's rim, or null when the whole band is blocked. Never on
    /// the pad itself (that is the reserved landing volume) and never inside a parked hull — <see
    /// cref="StandableSpot"/> rejects ship interiors. Shared by the vehicle recall (#1661) and the
    /// wedged-in-a-hull rescue (#1681), which both need "put this down beside the ship, in the open".
    /// </summary>
    private Vector3f? NearestStandableSpotOutsidePad(LandingPad pad, Vector3f near, int ringMin, int ringMax)
    {
        int refY = PadSurfaceY(pad.CenterX, pad.CenterZ);
        Vector3f? best = null;
        double bestSq = double.MaxValue;
        for (int r = pad.Radius + ringMin; r <= pad.Radius + ringMax; r++)
            for (int dx = -r; dx <= r; dx++)
                for (int dz = -r; dz <= r; dz++)
                {
                    if (System.Math.Max(System.Math.Abs(dx), System.Math.Abs(dz)) != r)
                    {
                        continue;
                    }

                    for (int y = refY + 3; y >= refY - 3; y--)
                    {
                        if (StandableSpot(pad.CenterX + dx, y, pad.CenterZ + dz) is { } spot)
                        {
                            double d = WrapDistSq(near, spot);
                            if (d < bestSq)
                            {
                                bestSq = d;
                                best = spot;
                            }

                            break;
                        }
                    }
                }

        return best;
    }

    /// <summary>Renaming a base keeps its residents (#1262): the live NPCs' display settlement and the owner's roster
    /// entries follow the new name. Before this the scan compared the NPC's settlement to the base name, saw "no
    /// settler" after a rename and spawned a second one under a fresh name-hash key.</summary>
    private void RenameBaseSettler(ServerBase b, string newName)
    {
        foreach (var npc in _npcs)
        {
            if (npc.BaseId == b.Id)
            {
                npc.Settlement = newName;
            }
        }

        if (FindSessionByPlayerId(b.OwnerId) is { } owner)
        {
            // #2716: the plan's people by their pinned keys (a base without a plan yet: the keys its id mints).
            var keys = _meta.BaseResidents.TryGetValue(BaseBoardKey(b), out var plan)
                ? plan.Select(r => BaseResidentKey(b, r))
                : Enumerable.Range(0, BaseResidentCap).Select(slot => BaseResidentKey(b, slot));
            foreach (string key in keys)
            {
                if (owner.State.NpcMemory.TryGetValue(key, out var rel))
                {
                    rel.Place = newName;
                }
            }
        }
    }

    /// <summary>Pre-#1262 saves keyed the base settler by a hash of the base NAME, so every rename minted a
    /// fresh entry — "my settler is listed three times". Moves the entry for the current name onto the
    /// rename-proof base-id key and drops the stale name-keyed copies of the same settler.</summary>
    private void MigrateBaseSettlerMemory(PlayerSession session)
    {
        var mem = session.State.NpcMemory;
        bool changed = false;
        foreach (var b in _bases)
        {
            if (b.OwnerId != session.State.PlayerId)
            {
                continue;
            }

            string key = BaseResidentKey(b, 0); // #2716: the founding settler's pinned key
            string legacy = NpcKey(LegacySettlementLocationKey(b.Name), "settler"); // the pre-#1262 key hashed the base name alone
            if (!mem.ContainsKey(key) && mem.TryGetValue(legacy, out var rel))
            {
                mem.Remove(legacy);
                rel.Place = b.Name;
                mem[key] = rel;
                changed = true;
            }
        }

        // Stale name-keyed copies of a settler we now know by base id: same coined name (the settler's
        // look and name are seeded from the base id, so every duplicate carried the same name).
        var known = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (var (key, rel) in mem)
        {
            if (key.StartsWith("base_", System.StringComparison.Ordinal) && rel.Role == "settler")
            {
                known.Add(rel.Name);
            }
        }

        foreach (var stale in mem.Where(kv => kv.Key.StartsWith("settle_", System.StringComparison.Ordinal)
                     && kv.Value.Role == "settler" && known.Contains(kv.Value.Name)).Select(kv => kv.Key).ToList())
        {
            mem.Remove(stale);
            changed = true;
        }

        if (changed)
        {
            _repo.SavePlayer(session.State);
        }
    }

    /// <summary>Test entrypoint for the pre-#1262 memory migration.</summary>
    public void MigrateBaseSettlerMemoryForTest(PlayerSession session) => MigrateBaseSettlerMemory(session);

    /// <summary>Machine-category blocks inside the 17³ core zone, read from the voxels (base_core itself excluded) — the
    /// core-zone part of <see cref="BaseMachinesInside"/>.</summary>
    private int CountBaseMachines(ServerBase b)
    {
        int count = 0;
        int r = BaseProtectionRadius;
        for (int x = -r; x <= r; x++)
            for (int y = -r; y <= r; y++)
                for (int z = -r; z <= r; z++)
                {
                    var pos = new Vector3i(b.Cell.X + x, b.Cell.Y + y, b.Cell.Z + z);
                    if (!WithinBuildHeight(pos.Y))
                    {
                        continue;
                    }

                    var block = _world.GetBlock(WorldConstants.CanonicalBlock(pos, _world.Circumference));
                    if (block.IsAir)
                    {
                        continue;
                    }

                    var def = _content.BlockById(block);
                    if (def is { Category: "machine" } && def.Key != "base_core")
                    {
                        count++;
                    }
                }

        return count;
    }

    /// <summary>A resident moves in — the look, name and face of the person pinned in the plan record (#2716; minted from
    /// the base and the slot when they first moved in), KNOWN to the owner from day one (the plan's "counts as a known
    /// NPC"), announced over the owner's radio — or by VEGA when the radio does not reach (#2596). Only a NEW resident is
    /// announced: residents are respawned after every world load, and the call used to ring again each time. The founding
    /// settler's news says that every bed inside the base brings one more resident (#2715) — without beds only they live
    /// there, and nothing told the player so.</summary>
    private ServerNpc SpawnBaseResident(ServerBase b, BaseResidentRecord r)
    {
        PinResidentIdentity(b, r); // normally done by the scan already
        int slot = r.Slot;
        var rng = new System.Random(unchecked((int)WorldGenerator.StableHash(r.Seed)));
        var home = ResidentHomeNear(b.Cell, new HashSet<Vector3i>(ResidentNpcs(b).Select(n => n.Home.ToBlock())));
        var npc = MakeNpc("settler", "settlers", robotic: false, home, rng);
        npc.Settlement = b.Name; // display name for greetings/dialogs — the memory key is pinned in the plan (#1262, #2716)
        npc.BaseId = b.Id;
        npc.BaseSlot = slot;
        npc.Rest = home;
        npc.RoutineEnabled = true; // #1867
        npc.Leash = ResidentLeash;

        // #2716: the face the client draws came from the runtime NPC id, which follows the spawn order — pin the first one.
        if (r.Face == 0)
        {
            r.Face = ClientFaceSeed(npc.Id, npc.Name);
        }

        npc.FaceSeed = r.Face;
        _npcs.Add(npc);

        string npcKey = r.Key;
        if (FindSessionByPlayerId(b.OwnerId) is { Joined: true } owner)
        {
            // The plan says the settler "counts as a known NPC": seed the acquaintance so the nameplate
            // shows a stage and the roster lists them right away.
            bool isNew = !owner.State.NpcMemory.ContainsKey(npcKey); // #2596: a respawn after a reload is no news
            if (isNew)
            {
                owner.State.NpcMemory[npcKey] = new NpcRelationship
                {
                    Name = npc.Name,
                    Role = "settler",
                    Place = b.Name,
                    Value = 10, // the "known" threshold
                };
            }

            SendNpcStandings(owner);
            string callKey = "settler:" + npcKey;
            if (isNew && !CallSettlerMovedIn(owner, b, npc, npcKey, callKey))
            {
                // #2596: no radio in reach (or its cadence) — VEGA passes the news on instead of losing it.
                if (slot == 0)
                {
                    RelayBaseNewsViaVega(owner, callKey, "vega.news.settler_first", new[] { npc.Name, b.Name, InvariantText(BaseResidentCap) });
                }
                else
                {
                    RelayBaseNewsViaVega(owner, callKey, "vega.news.settler", new[] { npc.Name, b.Name });
                }
            }

            _repo.SavePlayer(owner.State);
        }

        return npc;
    }

    /// <summary>The move-in radio call; the founding settler adds that every bed inside the base brings one more (#2715).</summary>
    private bool CallSettlerMovedIn(PlayerSession owner, ServerBase b, ServerNpc npc, string npcKey, string callKey)
    {
        if (npc.BaseSlot != 0)
        {
            return TryNpcRadioCall(owner, npcKey, npc.Name, b.Name, b.Planet, callKey, "npc.call.settler", string.Empty, isMission: false);
        }

        string text = Localize(owner.Locale, "npc.call.settler") + " "
            + Localize(owner.Locale, "npc.call.settler_beds").Replace("{max}", InvariantText(BaseResidentCap));
        return TryNpcRadioCallText(owner, npcKey, npc.Name, b.Name, b.Planet, callKey, text, isMission: false);
    }

    /// <summary>#2715: a resident moved out because their bed no longer counts — the owner hears who left (the founding
    /// settler never leaves). The name is the live NPC's, else the one the owner remembers.</summary>
    private void TellResidentMovedOut(ServerBase b, BaseResidentRecord leaving, string name)
    {
        if (leaving.Slot == 0 || FindSessionByPlayerId(b.OwnerId) is not { Joined: true } owner)
        {
            return;
        }

        if (name.Length == 0 && owner.State.NpcMemory.TryGetValue(BaseResidentKey(b, leaving), out var rel))
        {
            name = rel.Name;
        }

        if (name.Length > 0)
        {
            Send(owner, new ServerMessage { Text = "@srv.base.resident_left:" + name });
        }
    }

    // ---------------- #2716: pinned identities ----------------

    private static string InvariantText(int value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The seed of a resident's look and name as minted from a base id part and the slot — the founding settler's
    /// is <c>base-settler:&lt;id&gt;</c>, the others' <c>base-settler:&lt;id&gt;:&lt;slot&gt;</c> (what they always were).</summary>
    private static string BaseResidentSeed(string idPart, int slot)
        => slot == 0 ? "base-settler:" + idPart : "base-settler:" + idPart + ":" + InvariantText(slot);

    /// <summary>
    /// #2716: pins a resident's identity — the seed of their look and name, their memory key and their face — unless it is
    /// pinned already. Base ids are load-order numbers that shift after a restart once an older core went, so a pinned
    /// resident never reads the id again: they keep name, face and every player's memory of them.
    /// <list type="bullet">
    /// <item>Someone who left this base from this slot (<see cref="WorldMetadata.RetiredBaseResidents"/>: a bed that went, or
    /// the core put back on its cell) comes back as themselves.</item>
    /// <item>Else the identity is minted from today's base id. A key that a resident of a standing base holds, or that a
    /// resident who left any base had (a later base may get the same id), is not minted again: the id part becomes
    /// <c>&lt;id&gt;~&lt;n&gt;</c>. Within the <see cref="RetiredBaseResident.MaxKept"/> departed residents a save remembers,
    /// two people never share a memory; a base dissolved before this pin existed left nothing to remember.</item>
    /// </list>
    /// Returns whether anything was pinned.
    /// </summary>
    private bool PinResidentIdentity(ServerBase b, BaseResidentRecord r)
    {
        if (r.Key.Length > 0 && r.Seed.Length > 0)
        {
            return false;
        }

        string home = BaseBoardKey(b);
        int back = r.Key.Length == 0 && r.Seed.Length == 0
            ? _meta.RetiredBaseResidents.FindLastIndex(x => x.Base == home && x.Slot == r.Slot && x.Key.Length > 0)
            : -1;
        if (back >= 0)
        {
            var returning = _meta.RetiredBaseResidents[back];
            _meta.RetiredBaseResidents.RemoveAt(back);
            if (!ResidentKeyTaken(returning.Key))
            {
                r.Key = returning.Key;
                r.Seed = returning.Seed;
                r.Face = returning.Face;
                return true;
            }
        }

        string idPart = InvariantText(b.Id);
        if (r.Key.Length == 0)
        {
            for (int n = 2; ResidentKeyTaken(BaseResidentIdKey(idPart, r.Slot)); n++)
            {
                idPart = InvariantText(b.Id) + "~" + InvariantText(n);
            }

            r.Key = BaseResidentIdKey(idPart, r.Slot);
        }

        if (r.Seed.Length == 0)
        {
            r.Seed = BaseResidentSeed(idPart, r.Slot);
        }

        return true;
    }

    /// <summary>Whether <paramref name="key"/> is somebody's: pinned by a resident of a standing base's plan, or the key of a
    /// resident who left (<see cref="WorldMetadata.RetiredBaseResidents"/>).</summary>
    private bool ResidentKeyTaken(string key)
    {
        foreach (var plan in _meta.BaseResidents.Values)
        {
            if (plan.Any(x => x.Key == key))
            {
                return true;
            }
        }

        return _meta.RetiredBaseResidents.Any(x => x.Key == key);
    }

    /// <summary>#2716: remembers a resident who left <paramref name="baseKey"/> — a bed that went, or a dissolved base — so
    /// their key is never minted for anybody else and the same slot of the same base brings them back. Keeps the newest
    /// <see cref="RetiredBaseResident.MaxKept"/>; an unpinned record (nothing to remember) is skipped.</summary>
    private void RetireResident(string baseKey, BaseResidentRecord r)
    {
        if (r.Key.Length == 0)
        {
            return;
        }

        var retired = _meta.RetiredBaseResidents;
        retired.RemoveAll(x => x.Key == r.Key);
        retired.Add(new RetiredBaseResident { Base = baseKey, Slot = r.Slot, Seed = r.Seed, Key = r.Key, Face = r.Face });
        if (retired.Count > RetiredBaseResident.MaxKept)
        {
            retired.RemoveRange(0, retired.Count - RetiredBaseResident.MaxKept);
        }
    }

    /// <summary>#2716: run once at load, right after the bases got their load-order ids — every saved resident without a
    /// pinned identity (a save from before the pin) is pinned from this load's ids, which are the ids they were made with,
    /// so they keep their faces, names and memories even when an older core goes later. Bases on worlds nobody visits are
    /// pinned too: their ids could shift before their first scan.</summary>
    private void PinBaseResidentIdentities()
    {
        bool changed = false;
        foreach (var b in _bases)
        {
            if (_meta.BaseResidents.TryGetValue(BaseBoardKey(b), out var plan))
            {
                foreach (var r in plan.OrderBy(x => x.Slot))
                {
                    changed |= PinResidentIdentity(b, r);
                }
            }
        }

        if (changed)
        {
            _repo.SaveMetadata(_meta);
        }
    }

    /// <summary>The plan record of a base's resident in a slot, or null (no plan yet, or nobody in that slot).</summary>
    private BaseResidentRecord? ResidentRecord(ServerBase b, int slot)
        => _meta.BaseResidents.TryGetValue(BaseBoardKey(b), out var plan) ? plan.FirstOrDefault(r => r.Slot == slot) : null;

    /// <summary>The memory key a base's resident in a slot is filed under (#2716): pinned in the plan, else minted from
    /// today's base id (a base whose plan was not written yet).</summary>
    private string BaseResidentKey(ServerBase b, int slot)
        => ResidentRecord(b, slot) is { } r ? BaseResidentKey(b, r) : BaseResidentIdKey(InvariantText(b.Id), slot);

    /// <summary>The memory key of a plan record: its pinned key, else minted from today's base id.</summary>
    private static string BaseResidentKey(ServerBase b, BaseResidentRecord r)
        => r.Key.Length > 0 ? r.Key : BaseResidentIdKey(InvariantText(b.Id), r.Slot);

    /// <summary>The memory key of a live base resident — what <see cref="NpcKeyForNpc"/>, the trades and the board missions
    /// file it under.</summary>
    private string BaseResidentKeyOf(ServerNpc npc)
        => _bases.FirstOrDefault(x => x.Id == npc.BaseId) is { } b
            ? BaseResidentKey(b, npc.BaseSlot)
            : BaseResidentIdKey(InvariantText(npc.BaseId), npc.BaseSlot);

    /// <summary>Stage 1's hail (#1120): a trader just set down on a body — base owners there get a call
    /// (no acquaintance required; the trader is advertising). #2596: an owner the radio does not reach, but who is in
    /// the same star system, hears it from VEGA instead (an advisor line — the hints switch mutes it). #2603:
    /// <paramref name="hailFor"/> may supply a ready line for an owner (the cached AI hail of a planet-side arrival) — null
    /// keeps the authored <c>npc.call.trader</c>.</summary>
    private void NpcRadioOnTraderLanded(string bodyId, string traderName, System.Func<PlayerSession, string?>? hailFor = null)
    {
        var body = _galaxy?.FindBody(bodyId);
        string bodyName = body?.Name ?? bodyId;
        foreach (var b in _bases)
        {
            if (b.Planet != bodyId || FindSessionByPlayerId(b.OwnerId) is not { Joined: true } owner)
            {
                continue;
            }

            string callKey = "trader:" + bodyId;
            string? hail = hailFor?.Invoke(owner);
            bool called = hail is { Length: > 0 }
                ? TryNpcRadioCallText(owner, npcKey: string.Empty, traderName, bodyName, bodyId, callKey, hail, isMission: false, requireKnown: false)
                : TryNpcRadioCall(owner, npcKey: string.Empty, traderName, bodyName, bodyId,
                    callKey, "npc.call.trader", bodyName, isMission: false, requireKnown: false);
            if (!called && body?.SystemId is { } system && _galaxy?.FindBody(owner.CurrentLocationId)?.SystemId == system)
            {
                RelayBaseNewsViaVega(owner, callKey, "vega.news.trader", new[] { bodyName }, kind: 1);
            }
        }
    }

    /// <summary>Test seam (#2596): a trader set down on <paramref name="bodyId"/> — the base owners' hail.</summary>
    public void NpcRadioOnTraderLandedForTest(string bodyId, string traderName) => NpcRadioOnTraderLanded(bodyId, traderName);

    /// <summary>Test seam: run one base-life scan for every base on the active world right now.</summary>
    public void ScanBaseLifeForTest()
    {
        for (int i = 0; i < System.Math.Max(1, _bases.Count); i++)
        {
            _nextBaseLifeAt.Remove(_world.LocationId);
            _baseLifeScannedAt = double.NegativeInfinity;
            TickBaseLife();
        }

        _nextBaseLifeAt.Remove(_world.LocationId); // as before: the next tick may scan again at once
        _baseLifeScannedAt = double.NegativeInfinity;
    }

    /// <summary>Test seam: the founding settler's NPC id for a base, or null when none moved in yet.</summary>
    public int? BaseSettlerForTest(int baseId)
        => _baseResidents.TryGetValue(baseId, out var list) && list.FirstOrDefault(r => r.Slot == 0) is { } r ? r.NpcId : null;

    /// <summary>Test seam (#1865): a base's residents on the active world — slot, NPC id, role, job, bed, seat.</summary>
    public IReadOnlyList<(int Slot, int NpcId, string Role, string Job, Vector3i? Bed, Vector3i? Seat)> BaseResidentsForTest(int baseId)
    {
        var b = _bases.FirstOrDefault(x => x.Id == baseId);
        return b is null
            ? new List<(int, int, string, string, Vector3i?, Vector3i?)>()
            : ResidentNpcs(b).Select(n => (n.BaseSlot, n.Id, n.Role, n.Job, n.Bed, n.Seat)).ToList();
    }

    /// <summary>Test seam (#2656): a base's residents on the active world by slot with their names (a slot is a person).</summary>
    public IReadOnlyList<(int Slot, string Name)> BaseResidentNamesForTest(int baseId)
    {
        var b = _bases.FirstOrDefault(x => x.Id == baseId);
        return b is null ? new List<(int, string)>() : ResidentNpcs(b).Select(n => (n.BaseSlot, n.Name)).ToList();
    }

    /// <summary>Test seam (#2716): a base's residents on the active world by slot with the face seed the client draws.</summary>
    public IReadOnlyList<(int Slot, int Face)> BaseResidentFacesForTest(int baseId)
    {
        var b = _bases.FirstOrDefault(x => x.Id == baseId);
        return b is null
            ? new List<(int, int)>()
            : ResidentNpcs(b).Select(n => (n.BaseSlot, n.FaceSeed != 0 ? n.FaceSeed : ClientFaceSeed(n.Id, n.Name))).ToList();
    }
}
