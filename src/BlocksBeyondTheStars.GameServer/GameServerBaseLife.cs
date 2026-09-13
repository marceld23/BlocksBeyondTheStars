// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The world notices your base (#1120, stages 1–2): trader ships prefer landing on worlds with a founded
/// base (stage 1, see <c>PickLandableBody</c>) and hail the base owner over the radio when they set down;
/// a settler NPC moves in once a base carries enough machines (stage 2) — they greet, count as a KNOWN
/// acquaintance, and appear in the "People you know" roster. No NPC ever damages a block.
/// <para><b>Residents (#1865):</b> every bed inside the base (core zone, sealed room, walled yard or closed room —
/// see <see cref="BaseIndex"/>) brings one more resident, up to <see cref="BaseResidentCap"/>: 1 + beds. Slot 0 is
/// the founding settler (today's identity and memory key); slots 1..4 are the people the beds brought. Each
/// resident takes a bed in slot order (the newest sleeps rough until one more bed stands), a chair or bench for
/// the evening, and a job derived from what the base holds (#1868): the trading post and the mission board are
/// staffed first, then a guard for a walled base, a gardener for crops, a craftsman for a workbench.</para>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Machines (block category "machine", the base core itself excluded) a base needs before a
    /// settler moves in — a bare marker post is a claim, not a home.</summary>
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

    private double _nextBaseLifeAt;
    private int _baseLifeCursor;

    /// <summary>Round-robin base scan (Guard-registered): keeps each base's residents in step with its beds, posts
    /// and workshops, and removes them again when their base was dissolved.</summary>
    private void TickBaseLife()
    {
        if (_uptime < _nextBaseLifeAt)
        {
            return;
        }

        _nextBaseLifeAt = _uptime + BaseLifeScanInterval;
        SweepDissolvedBaseResidents();

        var here = _bases.Where(b => b.Planet == _world.LocationId).ToList();
        if (here.Count == 0)
        {
            return;
        }

        UpdateBaseResidents(here[_baseLifeCursor++ % here.Count]);
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
    }

    /// <summary>Brings one base's residents in step with the base: spawns the missing slots, sends the surplus away,
    /// and re-assigns beds, seats, jobs and posts.</summary>
    private void UpdateBaseResidents(ServerBase b)
    {
        var idx = RefreshBaseIndex(b);
        if (!_baseResidents.TryGetValue(b.Id, out var list))
        {
            _baseResidents[b.Id] = list = new List<BaseResident>();
        }

        // A world switch cleared the NPC list: a stale mapping must not block the respawn (#1152).
        list.RemoveAll(r => r.WorldId == _world.LocationId && !_npcs.Any(n => n.Id == r.NpcId && n.BaseId == b.Id));

        // The first settler needs the machines; once a base has one, beds alone bring the rest (and a machine
        // mined for a moment never sends the founding settler away).
        bool founded = list.Any(r => r.Slot == 0);
        int desired = founded || CountBaseMachines(b) >= BaseSettlerMachineCount
            ? System.Math.Min(BaseResidentCap, 1 + idx.BedHeads.Count)
            : 0;

        bool changed = false;
        foreach (var r in list.Where(r => r.Slot >= desired).ToList())
        {
            _npcs.RemoveAll(n => n.Id == r.NpcId && n.BaseId == b.Id); // a bed went — its sleeper moves on
            list.Remove(r);
            changed = true;
        }

        for (int slot = 0; slot < desired; slot++)
        {
            if (!list.Any(r => r.Slot == slot))
            {
                var npc = SpawnBaseResident(b, slot);
                list.Add(new BaseResident { Slot = slot, WorldId = b.Planet, NpcId = npc.Id });
                changed = true;
            }
        }

        list.Sort((p, q) => p.Slot.CompareTo(q.Slot));
        changed |= AssignBaseResidents(b, idx, list);
        if (changed)
        {
            BroadcastNpcs();
        }
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
    /// Hands out beds, seats, jobs and posts to a base's residents (#1865/#1868). Deterministic: residents in slot
    /// order take the beds and seats in the index's order and the jobs in priority order — trading post, mission
    /// board, guard (a walled base or a sentry post), gardener (crops, trays, saplings), craftsman (workbench,
    /// forge). Returns whether anything the client sees changed.
    /// </summary>
    private bool AssignBaseResidents(ServerBase b, BaseIndex idx, List<BaseResident> list)
    {
        var residents = ResidentNpcs(b);
        bool changed = false;

        // --- jobs, in priority order ---
        var patrol = residents.Count > 0 ? GuardPatrolFor(b) : null;
        var jobs = new List<string>();
        if (idx.VendorPosts.Count > 0)
        {
            jobs.Add("vendor");
        }

        if (idx.Boards.Count > 0)
        {
            jobs.Add("quartermaster");
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

        var takenHomes = new HashSet<Vector3i>();
        var takenSeats = new HashSet<Vector3i>();
        string boardKey = BaseBoardKey(b);
        for (int i = 0; i < residents.Count; i++)
        {
            var npc = residents[i];
            string job = i < jobs.Count ? jobs[i] : string.Empty;
            changed |= ApplyResidentJob(b, npc, job, boardKey);

            // --- bed + resting spot ---
            Vector3i? bed = i < idx.BedHeads.Count ? idx.BedHeads[i] : null;
            Vector3f? rest = bed is { } head ? BedSideSpot(head, takenHomes) : null;
            rest ??= ResidentHomeNear(b.Cell, takenHomes);
            takenHomes.Add(rest.Value.ToBlock());
            if (!System.Nullable.Equals(npc.Bed, bed))
            {
                npc.Bed = bed;
                npc.PhaseCheckedAt = double.NegativeInfinity; // re-think where to sleep
            }

            npc.Rest = rest.Value;

            // --- the evening seat: the nearest free one within reach of the resting spot ---
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
            }

            if (!System.Nullable.Equals(npc.Seat, seat))
            {
                npc.Seat = seat;
                npc.PhaseCheckedAt = double.NegativeInfinity;
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
        string role = job is "vendor" or "quartermaster" ? job : "settler";
        npc.NameKey = job switch
        {
            "vendor" => "npc.role.vendor",
            "quartermaster" => "npc.role.quartermaster",
            "guard" => "npc.role.guard",
            "gardener" => "npc.role.gardener",
            "craftsman" => "npc.role.craftsman",
            _ => "npc.theme.settlers",
        };
        npc.Theme = job == "vendor" ? SettlementTradeFor(boardKey) : "settlers";
        npc.Held = job switch
        {
            "gardener" => "npc_hoe",
            "craftsman" => "npc_hammer",
            "guard" => "blade",
            _ => string.Empty,
        };
        npc.SiteCursor = 0;
        npc.SiteUntil = 0;
        npc.PhaseCheckedAt = double.NegativeInfinity;

        if (npc.Role != role)
        {
            npc.Role = role;

            // The resident stays the same person (the memory key is the slot), only what they do changed.
            if (FindSessionByPlayerId(b.OwnerId) is { } owner
                && owner.State.NpcMemory.TryGetValue(BaseResidentKey(b.Id, npc.BaseSlot), out var rel))
            {
                rel.Role = role;
                SendNpcStandings(owner);
            }
        }

        return true;
    }

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
            _ => null,
        };

        if (npc.Job == "guard" && patrol is { Count: > 0 })
        {
            return patrol[0];
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
    /// "Free" is judged the way the NPC WALKS (<see cref="IsCollidingCell"/>, fluids are a wall) and not only
    /// the way a body is entombed (<see cref="IsBodyBlockingCell"/>, fluids pass): the two disagreed on water,
    /// so a shore base homed its settler on the seabed under two water cells — a place the leash could never
    /// walk them out of, and that the re-home below kept calling "still a place to stand" (#1658).</summary>
    private Vector3f? StandableSpot(int x, int y, int z)
    {
        if (!WithinBuildHeight(y) || !IsBodyBlockingCell(x, y - 1, z) || IsBodyBlockingCell(x, y, z) || IsBodyBlockingCell(x, y + 1, z)
            || IsCollidingCell(x, y, z) || IsCollidingCell(x, y + 1, z))
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
            for (int slot = 0; slot < BaseResidentCap; slot++)
            {
                if (owner.State.NpcMemory.TryGetValue(BaseResidentKey(b.Id, slot), out var rel))
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

            string key = BaseSettlerKey(b.Id);
            string legacy = NpcKey(SettlementLocationKey(b.Name), "settler");
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

    /// <summary>Machine-category blocks inside the base zone (base_core itself excluded).</summary>
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

    /// <summary>A resident moves in — a deterministic look and name per (base, slot) (slot 0 keeps the founding
    /// settler's seed), KNOWN to the owner from day one (the plan's "counts as a known NPC"), announced over the
    /// owner's radio.</summary>
    private ServerNpc SpawnBaseResident(ServerBase b, int slot)
    {
        string seed = slot == 0 ? "base-settler:" + b.Id : $"base-settler:{b.Id}:{slot}";
        var rng = new System.Random(unchecked((int)WorldGenerator.StableHash(seed)));
        var home = ResidentHomeNear(b.Cell, new HashSet<Vector3i>(ResidentNpcs(b).Select(n => n.Home.ToBlock())));
        var npc = MakeNpc("settler", "settlers", robotic: false, home, rng);
        npc.Settlement = b.Name; // display name for greetings/dialogs — the memory key is the base ID (#1262)
        npc.BaseId = b.Id;
        npc.BaseSlot = slot;
        npc.Rest = home;
        npc.RoutineEnabled = true; // #1867
        npc.Leash = ResidentLeash;
        _npcs.Add(npc);

        string npcKey = BaseResidentKey(b.Id, slot);
        if (FindSessionByPlayerId(b.OwnerId) is { Joined: true } owner)
        {
            // The plan says the settler "counts as a known NPC": seed the acquaintance so the nameplate
            // shows a stage and the roster lists them right away.
            if (!owner.State.NpcMemory.ContainsKey(npcKey))
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
            TryNpcRadioCall(owner, npcKey, npc.Name, b.Name, b.Planet,
                slot == 0 ? "settler:" + b.Id : $"settler:{b.Id}:{slot}", "npc.call.settler", string.Empty, isMission: false);
            _repo.SavePlayer(owner.State);
        }

        return npc;
    }

    /// <summary>Stage 1's hail (#1120): a trader just set down on a body — base owners there get a call
    /// (no acquaintance required; the trader is advertising).</summary>
    private void NpcRadioOnTraderLanded(string bodyId, string traderName)
    {
        string bodyName = _galaxy?.FindBody(bodyId)?.Name ?? bodyId;
        foreach (var b in _bases)
        {
            if (b.Planet != bodyId || FindSessionByPlayerId(b.OwnerId) is not { Joined: true } owner)
            {
                continue;
            }

            TryNpcRadioCall(owner, npcKey: string.Empty, traderName, bodyName, bodyId,
                "trader:" + bodyId, "npc.call.trader", bodyName, isMission: false, requireKnown: false);
        }
    }

    /// <summary>Test seam: run one base-life scan for every base on the active world right now.</summary>
    public void ScanBaseLifeForTest()
    {
        _nextBaseLifeAt = 0;
        for (int i = 0; i < System.Math.Max(1, _bases.Count); i++)
        {
            TickBaseLife();
            _nextBaseLifeAt = 0;
        }
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
}
