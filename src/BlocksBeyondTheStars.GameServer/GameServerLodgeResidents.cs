// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The people of a lodge (#2641) — they feel like villagers: a name, a day, a job, talk, trade and friendship.
/// <list type="bullet">
/// <item><b>One per bed</b> (one or two), spawned on every load after the settlement NPCs, each with a generator seeded
/// from the world, the lodge and the slot, so the same person lives there every time. A second resident takes the
/// profession's <see cref="LodgeProfessionDefinition.Partner"/> when the world calls for it, else the same one.</item>
/// <item><b>A person to remember</b>: <c>PersonKey</c> <c>lodge:&lt;world&gt;:&lt;x&gt;:&lt;z&gt;#&lt;slot&gt;</c> (name, standing and
/// "welcome back" live in the player's memory; NPCs stay unsaved). Role <c>vendor</c> with the profession as
/// <c>Job</c> and its market theme — the profession pattern, so trade-or-talk, the vendor screen and the memory work
/// unchanged.</item>
/// <item><b>A day</b> (the routine): work around the hut (the <c>yard</c> marker is the work anchor; the job state
/// machine of GameServerNpcWork.cs does the work, by day only, standing, in a player's area), the evening on the bench by
/// the campfire, the night in bed — a night visitor finds them asleep. Two residents chat like villagers (the lodge is
/// a place, <c>l:&lt;key&gt;</c>).</item>
/// <item><b>Talk</b> (<c>data/dialogs.json</c> <c>lodge_&lt;job&gt;</c>, a friend's <c>lodge_&lt;job&gt;_friend</c>; authored
/// + <c>aiPrompt</c>): what they do out here; a tip (<see cref="LodgeTipConsequence"/>) that reveals the nearest
/// unrevealed wreck, hidden cache or bandit camp — else names the way to the nearest village; the gatherer's warning
/// about this world's poisonous plants (<see cref="LodgePoisonConsequence"/>); and for a friend a present from what is
/// really in their store crate, once per in-game day, never anything toxic (<see cref="LodgeGiftConsequence"/>).</item>
/// <item><b>Villagers know them</b>: a hint "Mira the Gatherer lives in a hut …" that reveals the lodge
/// (<see cref="TryEmitLodgeHint"/>), and the chatter topic <c>lodge_near</c> (<see cref="LodgeResidentNear"/>).</item>
/// </list>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The dialogue consequence that hands a friend a present from the store crate.</summary>
    internal const string LodgeGiftConsequence = "lodgegift";

    /// <summary>The dialogue consequence of the tip: reveals the nearest unrevealed find, else names the nearest village.</summary>
    internal const string LodgeTipConsequence = "lodgetip";

    /// <summary>The gatherer's warning about this world's poisonous plants.</summary>
    internal const string LodgePoisonConsequence = "lodgepoison";

    /// <summary>Milestone prefix of a lodge present (<c>lodge:gift:&lt;person&gt;:&lt;day&gt;</c>) — one per resident and
    /// in-game day; only the latest day per resident is kept.</summary>
    internal const string LodgeGiftMilestonePrefix = "lodge:gift:";

    /// <summary>The most of one good a present holds.</summary>
    internal const int LodgeGiftMax = 4;

    /// <summary>How far a gatherer looks for wild plants round the yard.</summary>
    private const int LodgeGatherRadius = 18;

    /// <summary>How far a woodcutter looks for trees round the yard.</summary>
    private const int LodgeChopRadius = 20;

    /// <summary>A hunter's hunting ground round the hut.</summary>
    internal const int LodgeHuntRadius = 32;

    /// <summary>How far from the player a villager's lodge hint reaches.</summary>
    private const float LodgeHintRange = 600f;

    /// <summary>The place an AI line says a lodge resident lives in.</summary>
    private const string LodgeAiPlace = "a hut out in the wild";

    // ---------------- Residents ----------------

    /// <summary>One resident per bed of the lodge, appended after the settlement NPCs (see the class summary).</summary>
    private void SpawnLodgeResidents(LodgeInstance lodge, LodgeProfessionDefinition profession, IReadOnlyDictionary<string, double> traits)
    {
        if (_npcs.Any(n => n.LodgeKey == lodge.Key))
        {
            return;
        }

        var beds = lodge.Markers.Where(m => m.Type == LodgeGenerator.MarkerBed).Select(m => m.Pos.ToBlock()).ToList();
        var rests = lodge.Markers.Where(m => m.Type == LodgeGenerator.MarkerNpc).Select(m => m.Pos).ToList();
        var seats = lodge.Markers.Where(m => m.Type == LodgeGenerator.MarkerSeat).Select(m => m.Pos.ToBlock()).ToList();
        int residents = System.Math.Max(1, beds.Count);
        var partner = _content.LodgeProfession(profession.Partner);
        bool partnerFits = partner is not null && partner.Work != LodgeRules.WorkMine && LodgeRules.WeightFor(partner, traits) > 0;

        for (int slot = 0; slot < residents; slot++)
        {
            var job = slot == 0 || !partnerFits ? profession : partner!;
            var rng = new System.Random(unchecked((int)(_meta.Seed ^ WorldGenerator.StableHash(
                "lodge-resident:" + _world.LocationId + ":" + lodge.Key + "#" + slot.ToString(CultureInfo.InvariantCulture)))));
            var restAt = slot < rests.Count ? rests[slot] : rests.Count > 0 ? rests[0] : lodge.YardSpot;
            var rest = new Vector3f(restAt.X, (float)System.Math.Floor(restAt.Y), restAt.Z);
            var npc = MakeNpc("vendor", job.Theme, robotic: false, rest, rng);
            npc.Job = job.Key;
            npc.NameKey = job.NameKey;
            npc.Held = job.Held;
            npc.Theme = job.Theme;
            npc.LodgeKey = lodge.Key;
            npc.PersonKey = LodgePersonKey(lodge, slot);
            npc.Work = lodge.YardSpot;
            npc.HasWork = true;
            SetNpcBed(npc, slot < beds.Count ? beds[slot] : null);
            SetNpcSeat(npc, slot < seats.Count ? seats[slot] : null);
            MarkFurnitureLooked(npc); // #2655: kept while they stand; a broken bed sends its sleeper looking round the hut
            npc.RoutineEnabled = true;
            npc.WorkState = new NpcWorkState { Plan = LodgeWorkPlan(lodge, job, ownYard: job.Key == profession.Key) };
            _npcs.Add(npc);
            if (slot == 0)
            {
                lodge.ResidentName = npc.Name;
            }
        }
    }

    /// <summary>A lodge resident's stable memory key: <c>lodge:&lt;world&gt;:&lt;x&gt;:&lt;z&gt;#&lt;slot&gt;</c>.</summary>
    private string LodgePersonKey(LodgeInstance lodge, int slot)
        => "lodge:" + _world.LocationId + ":" + lodge.Key + "#" + slot.ToString(CultureInfo.InvariantCulture);

    /// <summary>Whether a memory key is a lodge resident's (#2641) — a person like a villager: the first meeting lifts the
    /// standing, a talk pays every ten minutes, a passing hello helps up to "known". Never a legacy floor (no shared
    /// role entry ever existed for them).</summary>
    internal static bool IsLodgePersonKey(string key)
        => !string.IsNullOrEmpty(key) && key.StartsWith("lodge:", System.StringComparison.Ordinal) && key.IndexOf('#') > 0;

    /// <summary>What a resident works at: the yard's own cells for the lodge's profession (the plot's crops, the quarry
    /// face), the wild land round the hut for everything else; the yield goes into the store crate.</summary>
    private NpcWorkPlan LodgeWorkPlan(LodgeInstance lodge, LodgeProfessionDefinition job, bool ownYard)
    {
        System.Action<string, int> bank = (item, count) => DepositIntoLodgeStore(lodge, item, count);
        return job.Work switch
        {
            LodgeRules.WorkMine => new NpcWorkPlan
            {
                Kind = NpcWorkKind.Mine,
                Center = lodge.YardSpot,
                Radius = 8,
                FixedSites = lodge.Face.Select(f => f.Cell).ToList(), // the face only — a lodge's miner never digs elsewhere
                Bank = bank,
                ActivityKey = job.ActivityKey,
                Held = job.Held,
            },
            LodgeRules.WorkChop => new NpcWorkPlan
            {
                Kind = NpcWorkKind.Chop,
                Center = lodge.YardSpot,
                Radius = LodgeChopRadius,
                Bank = bank,
                SiteAllowed = c => !IsLodgeBlock(c), // the hut's log frame is no tree
                ActivityKey = job.ActivityKey,
                Held = job.Held,
            },
            LodgeRules.WorkHunt => new NpcWorkPlan
            {
                Kind = NpcWorkKind.Hunt,
                Center = lodge.YardSpot, // the drying frame: a standable spot (the footprint middle is the house corner)
                Radius = LodgeHuntRadius,
                Bank = bank,
                ActivityKey = job.ActivityKey,
                Held = job.Held,
            },
            _ => new NpcWorkPlan
            {
                Kind = NpcWorkKind.Gather,
                Center = lodge.YardSpot,
                Radius = LodgeGatherRadius,
                FixedSites = ownYard && lodge.Plot.Count > 0 ? lodge.Plot.ToList() : null,
                Bank = bank,
                ActivityKey = job.ActivityKey,
                Held = job.Held,
            },
        };
    }

    /// <summary>A lodge resident's job (called from <c>TickNpcJob</c> for NPCs in reach): the work state machine, by
    /// day, standing.</summary>
    private void TickLodgeResidentJob(ServerNpc npc, List<PlayerSession> targets)
    {
        if (npc.Phase != NpcPhase.Day || npc.Pose != 0 || npc.WorkState?.Plan is not { } plan)
        {
            return;
        }

        TickNpcWork(npc, plan, targets);
    }

    /// <summary>Whether a job is one of the wilderness folk's professions (a lodge resident's or a wanderer's) — or a
    /// pioneer's (#2682): each talks in its own dialogues, never the generic settler's.</summary>
    private bool IsLodgeJob(string job) => _content.LodgeProfession(job) is not null || job == PioneerJob;

    /// <summary>The lodge a resident lives at, or null.</summary>
    private LodgeInstance? LodgeOf(ServerNpc npc)
        => npc.LodgeKey.Length == 0 ? null : _lodges.FirstOrDefault(l => l.Key == npc.LodgeKey);

    // ---------------- Talk ----------------

    /// <summary>"Got anything to spare?" — a friend's present from the store crate (#2641): what is really in it (the
    /// first good that is not toxic, up to <see cref="LodgeGiftMax"/>), once per in-game day and resident; a second ask
    /// that day, an empty crate or a full bag each hear their own line and spend nothing.</summary>
    private void ApplyLodgeGift(PlayerSession session, ServerNpc npc)
    {
        var p = session.State;
        string prefix = LodgeGiftMilestonePrefix + npc.PersonKey + ":";
        string today = prefix + MarketDay.ToString(CultureInfo.InvariantCulture);
        if (p.Milestones.Contains(today))
        {
            _dialogResponseOverride = Localize(session.Locale, "dlg.lodge.r_gift_again");
            return;
        }

        // #2681: a pioneer gives from any of the crates in the room (never the cellar's box), a hut's resident from its one.
        StoredContainer? crate = null;
        ItemStack? stack = null;
        foreach (var c in LodgeOf(npc) is { } lodge ? LodgeGiftStores(lodge) : Enumerable.Empty<StoredContainer>())
        {
            stack = c.Items.FirstOrDefault(s => !s.IsEmpty && LodgeGiftable(s.Item));
            if (stack is not null)
            {
                crate = c;
                break;
            }
        }

        if (crate is null || stack is null)
        {
            _dialogResponseOverride = Localize(session.Locale, "dlg.lodge.r_gift_empty");
            return;
        }

        int n = System.Math.Min(stack.Count, LodgeGiftMax);
        Serve(session);
        var bag = new MaterialPool(_content, p, _ship);
        if (!bag.CanFit(new[] { new ItemAmount(stack.Item, n) }))
        {
            _dialogResponseOverride = Localize(session.Locale, "dlg.lodge.r_gift_full");
            return;
        }

        bag.Add(stack.Item, n);
        SendInventory(session);
        crate.Items = crate.Items
            .Select(s => s.Item == stack.Item ? new ItemStack(s.Item, s.Count - n) : s)
            .Where(s => !s.IsEmpty && s.Count > 0)
            .ToList();
        _repo.SaveContainer(crate);
        BroadcastContainers();

        p.Milestones.RemoveWhere(m => m.StartsWith(prefix, System.StringComparison.Ordinal));
        p.Milestones.Add(today);
        string itemName = LocalizedName(session.Locale, _content.GetItem(stack.Item)?.NameKey, stack.Item);
        _dialogExtraText = "\n\n+" + n.ToString(CultureInfo.InvariantCulture) + " " + itemName;
    }

    /// <summary>Whether a good may be given away: a known item that is not toxic (no <c>toxic_</c> twin, nothing that
    /// hurts to eat).</summary>
    internal bool LodgeGiftable(string item)
        => _content.GetItem(item) is { } def && !item.StartsWith("toxic_", System.StringComparison.Ordinal) && def.ConsumeHealth >= 0f;

    /// <summary>"Seen anything interesting around here?" — the existing hint path: the nearest wreck, hidden cache or
    /// bandit camp the map does not show yet is revealed and its way told; with none left, the way to the nearest
    /// village (always on the map); with nothing at all, the authored "nothing new". A hidden cache is the villagers'
    /// secret rule too (<see cref="TryEmitHint"/>): only told to a player this NPC already knows.</summary>
    private void ApplyLodgeTip(PlayerSession session, ServerNpc npc)
    {
        bool known = RelationshipTier(RelationshipValue(session.State, NpcKeyForNpc(session, npc))) is "known" or "trusted";
        string tip = LodgeTipLine(session, shareCaches: known);
        if (tip.Length > 0)
        {
            _dialogResponseOverride = tip;
        }
    }

    private string LodgeTipLine(PlayerSession session, bool shareCaches = true)
    {
        var at = session.State.Position;
        string? key = null;
        string text = string.Empty;
        float tx = 0, tz = 0;
        double best = double.MaxValue;

        void Consider(string revealKey, string textKey, float x, float z)
        {
            double d = WrapDistSq(at, new Vector3f(x, at.Y, z));
            if (d < best)
            {
                best = d;
                key = revealKey;
                text = textKey;
                tx = x;
                tz = z;
            }
        }

        string wreckKey = _world.LocationId + "|wreck";
        if (_wreckStamped && !_wreckClaimed && !_meta.RevealedPois.Contains(wreckKey))
        {
            var (wx, wz) = WreckPoiCenter();
            Consider(wreckKey, "npc.hint.wreck", wx, wz);
        }

        foreach (var cont in shareCaches ? _containers : new List<StoredContainer>())
        {
            if (cont.Id.StartsWith(ChestContainerIdPrefix, System.StringComparison.Ordinal) && !_meta.RevealedPois.Contains(ChestRevealKey(cont)))
            {
                Consider(ChestRevealKey(cont), "npc.hint.treasure", cont.Position.X + 0.5f, cont.Position.Z + 0.5f);
            }
        }

        if (BanditsActive)
        {
            foreach (var camp in _banditCamps)
            {
                string campKey = _world.LocationId + "|banditcamp:" + camp.Key;
                if (!camp.Cleared && !_meta.RevealedPois.Contains(campKey))
                {
                    Consider(campKey, "npc.hint.bandit_camp", camp.Center.X, camp.Center.Z);
                }
            }
        }

        if (key is not null)
        {
            RevealPoi(key);
            return LodgeHintText(session, text, tx, tz);
        }

        // Nothing hidden left: the way to the nearest village.
        SettlementInstance? village = null;
        foreach (var s in _settlements)
        {
            if (s.Ruined)
            {
                continue;
            }

            var c = new Vector3f((s.Min.X + s.Max.X) * 0.5f, at.Y, (s.Min.Z + s.Max.Z) * 0.5f);
            double d = WrapDistSq(at, c);
            if (d < best)
            {
                best = d;
                village = s;
            }
        }

        return village is null
            ? string.Empty
            : LodgeHintText(session, "npc.hint.settlement", (village.Min.X + village.Max.X) * 0.5f, (village.Min.Z + village.Max.Z) * 0.5f,
                name: village.Name);
    }

    /// <summary>The gatherer's warning (#2641): the names of this world's poisonous plants and trees — or that nothing round
    /// here is.</summary>
    private void ApplyLodgePoison(PlayerSession session)
    {
        var names = _floraSpeciesByBlock.Values.Where(s => s.Active && s.Toxic && !s.Aquatic).Select(s => s.Name)
            .Concat(_treeSpeciesByBlock.Values.Where(t => t.Toxic).Select(t => t.Name))
            .Where(n => !string.IsNullOrEmpty(n))
            .Distinct(System.StringComparer.Ordinal)
            .Take(4)
            .ToList();
        _dialogResponseOverride = names.Count == 0
            ? Localize(session.Locale, "dlg.lodge_gatherer.r_poison_none")
            : Localize(session.Locale, "dlg.lodge_gatherer.r_poison").Replace("{0}", string.Join(", ", names));
    }

    /// <summary>A spoken way to a place: <c>{0}</c> the rough distance (nearest 10 m), <c>{1}</c> the compass direction,
    /// <c>{name}</c> and <c>{job}</c> filled in — the hint line's rules (<c>HintLine</c>) without its format string, so a
    /// name can never break it.</summary>
    private string LodgeHintText(PlayerSession session, string textKey, float targetX, float targetZ, string name = "", string job = "")
    {
        double dx = WorldConstants.WrapDeltaX((double)(targetX - session.State.Position.X), _world.Circumference);
        double dz = WorldConstants.WrapDeltaZ((double)(targetZ - session.State.Position.Z), _world.Circumference);
        int dist = System.Math.Max(10, (int)(System.Math.Round(System.Math.Sqrt((dx * dx) + (dz * dz)) / 10.0) * 10));
        return Localize(session.Locale, textKey)
            .Replace("{0}", dist.ToString(CultureInfo.InvariantCulture))
            .Replace("{1}", Localize(session.Locale, DirectionKey(dx, dz)))
            .Replace("{name}", name)
            .Replace("{job}", job);
    }

    // ---------------- Villagers know them ----------------

    /// <summary>A villager's hint (#2641): the nearest lodge within <see cref="LodgeHintRange"/> the map does not show
    /// yet — "Mira the Gatherer lives in a hut out in the wild, about 240 m to the north" — and it goes on the map. Empty
    /// when every lodge in reach is known.</summary>
    private string TryEmitLodgeHint(PlayerSession session)
    {
        LodgeInstance? best = null;
        double bestSq = LodgeHintRange * (double)LodgeHintRange;
        foreach (var l in _lodges)
        {
            if (_meta.RevealedPois.Contains(l.RevealKey))
            {
                continue;
            }

            double d = WrapDistSq(session.State.Position, l.Center);
            if (d <= bestSq)
            {
                bestSq = d;
                best = l;
            }
        }

        if (best is null)
        {
            return string.Empty;
        }

        RevealPoi(best.RevealKey);
        if (best.Outpost is not null)
        {
            return LodgeHintText(session, "npc.hint.outpost", best.Center.X, best.Center.Z, best.ResidentName); // #2681
        }

        string job = _content.LodgeProfession(best.Profession) is { } p ? Localize(session.Locale, p.NameKey) : string.Empty;
        return LodgeHintText(session, "npc.hint.lodge", best.Center.X, best.Center.Z, best.ResidentName, job);
    }

    /// <summary>The name of the first resident of a lodge within the chatter fact range of <paramref name="at"/>, or ""
    /// (the <c>lodge_near</c> topic's <c>{npc}</c>).</summary>
    private string LodgeResidentNear(Vector3f at)
    {
        foreach (var l in _lodges)
        {
            if (l.Outpost is null // #2681: the topic talks of a hut — a pioneer's outpost is not one
                && l.ResidentName.Length > 0 && WrapDistSq(l.Center, at) <= (double)DeedSettlementRange * DeedSettlementRange)
            {
                return l.ResidentName;
            }
        }

        return string.Empty;
    }

    // ---------------- Test hooks ----------------

    /// <summary>Test/inspection (#2641): the residents of a lodge — id, name, job, person key, bed, seat and work anchor.</summary>
    public IReadOnlyList<(int Id, string Name, string Job, string PersonKey, Vector3i? Bed, Vector3i? Seat, Vector3f Work, string Held)> LodgeResidentsForTest(string key)
        => _npcs.Where(n => n.LodgeKey == key)
            .Select(n => (n.Id, n.Name, n.Job, n.PersonKey, n.Bed, n.Seat, n.Work, n.Held))
            .ToList();

    /// <summary>Test seam (#2641): the lodge hint a villager would give this player now (reveals it), "" when none.</summary>
    public string LodgeHintForTest(string playerId)
        => FindSessionByPlayerId(playerId) is { } s ? TryEmitLodgeHint(s) : string.Empty;
}
