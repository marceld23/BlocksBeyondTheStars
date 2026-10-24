// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Missions;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.Story;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Personal wishes (#2601, epic #2589 "the village lives"): a real, named villager asks THIS player for a small favour —
/// bring me five berries, show me a crystal (you keep it), check on the animals (scan one). A wish is a system mission
/// with the id <c>wish_&lt;place&gt;_&lt;player&gt;_&lt;lane&gt;_&lt;n&gt;</c> (never a board id), not repeatable and not story
/// progress (<see cref="MissionDefinition.CountsForStory"/> false).
/// <para><b>Who and what.</b> Plain settlers, gardeners, craftsmen and innkeepers of an inhabited settlement have wishes
/// (no traders, quartermasters, professions, guardians, authored characters or base residents). A hamlet has one lane, a
/// larger village two: each lane holds one wish at a time. The lane's wish is coined deterministically from the world
/// seed, the place, the player and the lane's own counter — the villager by a hash ranking within the lane's share of the
/// villagers (never the other lane's villager, never the one whose wish was fulfilled last), the template from
/// <c>data/wishes.json</c> by the giver's job, the lane's tier (a first wish is easy) and what this world can grant (no
/// berries without berry plants, no firewood without trees). An open offer therefore stays the same until it is
/// fulfilled. Wishes are per player — siblings get different wishes and nobody "steals" one.</para>
/// <para><b>The flow uses no new intent.</b> E at the villager (before any other dialogue, after the sleepy mumble): an
/// offer opens a synthetic dialogue ("Sure, I'll help!" / "Maybe later."); accepting puts the wish into the mission log
/// (persisted like a bounty definition); E again with everything in the bag hands it over — thanks, the present and +5
/// standing; with only part of it the villager says how far along the player is ("no hurry!"). The Missions tab never
/// offers a wish, never accepts one and turns one in only next to the villager. A full bag keeps the wish open. No
/// timers: a wish never expires.</para>
/// <para><b>Markers.</b> The per-player <see cref="NpcStandingList"/> carries the villagers with an offer ("!") and with a
/// wish ready to hand over ("?"); a 1 Hz check (from the VEGA advisor tick) re-sends it when a held wish becomes ready
/// and says so once. An older client ignores the fields — talking still works. #2617: while a villager visits the
/// player's base as a neighbour, its markers sit on the guest there, and that guest takes the held wish (the hand-over or
/// the reminder) — a "?" always means "talk to me and it is handed over".</para>
/// <para><b>Where (#2714).</b> A held wish remembers the giver's village (<see cref="WishLane.Village"/>, its body and centre,
/// persisted with the lane): the Missions tab says "Tomas lives in Testville" and carries the place
/// (<see cref="NetMission.PlaceName"/>), the ready toast and the tab's refusal name it, and the player's marker view holds a
/// village marker (<see cref="NetMarker.Wish"/>, one per village) while a wish from there is open and they are on its body —
/// the planet map and the HUD compass point the way. A wish held from before #2714 learns its village the next time the
/// player is on that body (the 1 Hz check).</para>
/// <para><b>AI (hosted worlds).</b> The request (<c>wish_ask</c>) and the thanks (<c>wish_thanks</c>) may be re-worded in
/// the villager's own voice through the shared <c>/npc-lines</c> path — prefetched (the ask when the player stands in
/// the village with an offer, the thanks when a wish turns ready), never swapped into an open panel; the facts travel as
/// <c>{count}</c>/<c>{item}</c> tokens and the exact line ("You need: 5× Berries", "Tomas gives you: 3× Berry Seed") is
/// always authored and appended by the server.</para>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The id prefix of every wish mission — not a board prefix (<c>settle_/station_/home_</c>).</summary>
    internal const string WishMissionPrefix = "wish_";

    /// <summary>Standing a fulfilled wish brings with the villager who wished it.</summary>
    internal const int WishStandingBonus = 5;

    /// <summary>The synthetic dialogue key of a wish offer (<see cref="ActiveDialogForTest"/> reads it).</summary>
    internal const string WishDialogKey = "wish";

    /// <summary>The dialogue consequence family of a wish offer: <c>wish:accept</c> / <c>wish:later</c>.</summary>
    internal const string WishConsequence = "wish";

    /// <summary>#2714: the id prefix of a wish village's map marker (<c>wish:&lt;place key&gt;</c>).</summary>
    internal const string WishMarkerIdPrefix = "wish:";

    /// <summary>#2714: the wish village marker's icon (home) and palette colour (yellow, the gold of the "!"/"?") — what an
    /// older client draws for it.</summary>
    private const int WishMarkerIcon = 1;
    private const int WishMarkerColor = 2;

    /// <summary>Most characters an AI re-wording of a villager's request may take.</summary>
    private const int WishAskMaxChars = 160;

    /// <summary>Most characters an AI re-wording of a villager's thanks may take.</summary>
    private const int WishThanksMaxChars = 120;

    /// <summary>The authored wording of a wish offer: one node, two answers (the prompt text is built per player).</summary>
    private static readonly DialogDefinition WishDialog = new()
    {
        Key = WishDialogKey,
        Role = "_wish", // never matches a live NPC in PickDialog — only TryWishConversation opens it
        Nodes =
        {
            new DialogNode
            {
                PromptKey = "dlg.wish.fact",
                Choices =
                {
                    new DialogChoice { TextKey = "dlg.wish.c_yes", ResponseKey = "dlg.wish.r_yes", Consequence = WishConsequence + ":accept" },
                    new DialogChoice { TextKey = "dlg.wish.c_later", ResponseKey = "dlg.wish.r_later", Consequence = WishConsequence + ":later" },
                },
            },
        },
    };

    private static readonly string[] WishAskRequiredTokens = { AiLineTokens.Count, AiLineTokens.Item };
    private static readonly string[] WishThanksRequiredTokens = { AiLineTokens.Item };

    /// <summary>Set while a talk hands a wish over — the villager is right there, so the tab's "next to the giver" gate
    /// is skipped and no extra thank-you bubble goes out (the panel says it).</summary>
    private bool _wishTurnInByTalk;

    private string? _wishTemplateForTest;      // test seam: every coined wish uses this template
    private bool _wishNoPlantsForTest;         // test seam: the world grows no plants and no trees
    private bool _wishToxicPlantsForTest;      // test seam: every plant species is poisonous

    /// <summary>One lane of a place for one player: its id, the villager, the template and whether the player holds it.</summary>
    private sealed class WishLanePlan
    {
        public int Lane;
        public int N;
        public string MissionId = string.Empty;
        public string GiverKey = string.Empty;
        public ServerNpc? Giver;
        public WishTemplate? Template;
        public int Count;
        public MissionProgress? Held;
        public MissionDefinition? Def;
    }

    /// <summary>Whether a mission id is a villager's wish.</summary>
    internal static bool IsWishMission(string? missionId)
        => missionId is not null && missionId.StartsWith(WishMissionPrefix, StringComparison.Ordinal);

    /// <summary>Whether this NPC may have a wish at all: a settlement villager with a person key — a plain settler, a
    /// gardener, a craftsman or an innkeeper; never a trader, a quartermaster, a profession, a guardian, an authored
    /// character or a base resident.</summary>
    private static bool WishEligible(ServerNpc npc)
        => npc.Role == "settler" && npc.BaseId == 0 && npc.CharacterId.Length == 0 && npc.Settlement.Length > 0
           && npc.PersonKey.Length > 0 && Array.IndexOf(WishTemplate.EligibleJobs, npc.Job) >= 0;

    /// <summary>Whether a villager asks for a NEW wish right now: not while celebrating at the festival (#2604 — everybody
    /// is busy cheering) and not while away visiting a base (#2602 — hidden at home). Their lane keeps them (the coin does
    /// not move on to somebody else); the "!" and the offer simply wait. A held wish is still handed over.</summary>
    private static bool WishOfferPaused(ServerNpc npc) => npc.Phase == NpcPhase.Festival || npc.VisitingAway;

    /// <summary>A hamlet's villagers have one wish at a time, every larger place two.</summary>
    private static int WishLaneCount(SettlementInstance s) => s.Tier == "hamlet" ? 1 : 2;

    /// <summary>The wish mission id: <c>wish_&lt;place hash&gt;_&lt;player hash&gt;_&lt;lane&gt;_&lt;n&gt;</c>.</summary>
    private static string WishMissionId(string placeKey, string playerId, int lane, int n)
    {
        string place = placeKey.StartsWith("settle_", StringComparison.Ordinal) ? placeKey.Substring(7) : placeKey;
        uint player = unchecked((uint)WorldGenerator.StableHash("wishplayer:" + playerId));
        return WishMissionPrefix + place + "_" + player.ToString("x8", CultureInfo.InvariantCulture) + "_"
            + lane.ToString(CultureInfo.InvariantCulture) + "_" + n.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>The place key and lane a wish id names (false for anything else).</summary>
    private static bool TryParseWishId(string missionId, out string placeKey, out int lane)
    {
        placeKey = string.Empty;
        lane = -1;
        if (!IsWishMission(missionId))
        {
            return false;
        }

        var parts = missionId.Split('_');
        if (parts.Length != 5 || !int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out lane))
        {
            return false;
        }

        placeKey = "settle_" + parts[1];
        return true;
    }

    /// <summary>A 32-bit hash over the world seed and the given parts — the coin behind every wish choice.</summary>
    private uint WishHash(string text) => unchecked((uint)WorldGenerator.StableHash(_meta.Seed.ToString(CultureInfo.InvariantCulture) + ":wish:" + text));

    // ---------------- What the world can grant ----------------

    /// <summary>The template keys this world can grant right now (#2601): plants that drop the item (a poisonous species
    /// does not count when the item has a poisonous twin), trees, mycelium ground, animals.</summary>
    private HashSet<string> WishFeasibleTemplates()
    {
        var feasible = new HashSet<string>(StringComparer.Ordinal);
        var planet = _content.GetPlanet(_worlds.Active.PlanetType);
        foreach (var t in _content.Wishes)
        {
            bool ok = t.Requires switch
            {
                WishTemplate.RequiresFloraDrop => WishFloraDrops(t.Item),
                WishTemplate.RequiresTrees => !_wishNoPlantsForTest && _treeSpeciesByBlock.Count > 0
                    && planet is not null && (planet.TreeDensity ?? 0.012) > 0.0,
                WishTemplate.RequiresMycelium => planet is not null && (planet.SurfaceBlock == "mycelium"
                    || planet.Biomes.Any(b => b.SurfaceBlock == "mycelium")),
                WishTemplate.RequiresCreatures => _speciesRoster.Any(sp => sp.Id != SreekmakraSpeciesId),
                _ => true,
            };
            if (ok)
            {
                feasible.Add(t.Key);
            }
        }

        return feasible;
    }

    /// <summary>Whether a plant growing on this world drops <paramref name="item"/> — a poisonous species only counts when
    /// its drop has no poisonous twin (fibre is fibre, toxic berries are no present).</summary>
    private bool WishFloraDrops(string item)
    {
        if (_wishNoPlantsForTest)
        {
            return false;
        }

        bool twin = ToxicCounterpart(item) != item;
        foreach (var fs in _floraSpeciesByBlock.Values)
        {
            if (!fs.Active || ((fs.Toxic || _wishToxicPlantsForTest) && twin))
            {
                continue;
            }

            if (_content.GetBlock(fs.BlockKey) is { } block && block.Drops.Any(d => d.Item == item))
            {
                return true;
            }
        }

        return false;
    }

    // ---------------- Coining the lanes ----------------

    /// <summary>The eligible villagers of the active world per settlement name, in spawn order, one per person key (a
    /// visitor borrowing a villager's key never counts twice).</summary>
    private Dictionary<string, List<ServerNpc>> WishVillagers()
    {
        var bySettlement = new Dictionary<string, List<ServerNpc>>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var npc in _npcs)
        {
            if (!WishEligible(npc) || !seen.Add(npc.PersonKey))
            {
                continue;
            }

            if (!bySettlement.TryGetValue(npc.Settlement, out var list))
            {
                list = new List<ServerNpc>();
                bySettlement[npc.Settlement] = list;
            }

            list.Add(npc);
        }

        return bySettlement;
    }

    /// <summary>
    /// The player's wish lanes in one settlement. A held lane keeps its villager and its persisted definition (an orphaned
    /// row — a definition lost from the store — is coined again for the same villager). Every other lane is coined from
    /// its OWN state only (its counter, its last giver and template) — so an offer never changes because the player
    /// fulfilled the other lane's wish: the villagers are split between the lanes by a hash (a lane borrows from the
    /// other's share only when its own share has nobody left), and a lane's template ignores the other lanes (two
    /// villagers may now and then wish the same thing). Pure: nothing is stored, nothing is sent.
    /// </summary>
    private List<WishLanePlan> PlanWishes(PlayerState p, SettlementInstance s, IReadOnlyList<ServerNpc> villagers, HashSet<string> feasible)
    {
        var plans = new List<WishLanePlan>();
        string place = SettlementLocationKey(s.Name);
        p.Wishes.TryGetValue(place, out var ledger);
        int laneCount = WishLaneCount(s);
        var takenGivers = new HashSet<string>(StringComparer.Ordinal);

        // Held lanes first: their villager and template are fixed.
        for (int lane = 0; lane < laneCount; lane++)
        {
            var ln = ledger is not null && lane < ledger.Lanes.Count ? ledger.Lanes[lane] : null;
            int n = ln?.N ?? 0;
            var plan = new WishLanePlan { Lane = lane, N = n, MissionId = WishMissionId(place, p.PlayerId, lane, n) };
            plans.Add(plan);
            var pr = p.Missions.FirstOrDefault(m => m.MissionId == plan.MissionId && m.Status != MissionStatus.TurnedIn);
            if (pr is null || ln is null || ln.Giver.Length == 0)
            {
                continue;
            }

            plan.Held = pr;
            plan.GiverKey = ln.Giver;
            plan.Giver = villagers.FirstOrDefault(v => v.PersonKey == ln.Giver);
            plan.Def = GetMissionDef(plan.MissionId);
            if (plan.Def is null && plan.Giver is not null
                && CoinWishLane(p, place, plan, ln, laneCount, new[] { plan.Giver }, feasible, takenGivers))
            {
                plan.Def = NewWishDef(plan); // the definition went missing — the same villager wishes it again
                _missionDefs[plan.MissionId] = plan.Def;
                _repo.SaveMission(plan.Def);
            }

            plan.Template = plan.Def is null ? null : WishTemplateOf(plan.Def);
            plan.Count = plan.Def?.Objectives.FirstOrDefault()?.Required ?? 0;
            takenGivers.Add(plan.GiverKey);
        }

        // Every open lane from its own share first (the shares are disjoint, so these never disturb each other); only then
        // a lane whose share has nobody left borrows from what remains.
        foreach (bool ownShareOnly in new[] { true, false })
        {
            foreach (var plan in plans)
            {
                if (plan.Held is not null || plan.Template is not null)
                {
                    continue;
                }

                var ln = ledger is not null && plan.Lane < ledger.Lanes.Count ? ledger.Lanes[plan.Lane] : null;
                if (CoinWishLane(p, place, plan, ln, laneCount, villagers, feasible, takenGivers, ownShareOnly))
                {
                    takenGivers.Add(plan.GiverKey);
                }
            }
        }

        return plans;
    }

    /// <summary>Coins one lane's wish: the best-ranked villager (a hash over seed, place, player, lane and counter) of the
    /// lane's own share who is not the lane's last giver; else (unless <paramref name="ownShareOnly"/>) anybody not taken
    /// by another lane; the last giver only when nobody else is left — the first one with a template that fits. False when
    /// nobody can wish anything here.</summary>
    private bool CoinWishLane(PlayerState p, string place, WishLanePlan plan, WishLane? ln, int laneCount,
        IReadOnlyList<ServerNpc> villagers, HashSet<string> feasible, HashSet<string> takenGivers, bool ownShareOnly = false)
    {
        string coin = place + ":" + p.PlayerId + ":" + plan.Lane.ToString(CultureInfo.InvariantCulture) + ":"
            + plan.N.ToString(CultureInfo.InvariantCulture);
        string lastGiver = ln?.LastGiver ?? string.Empty;
        var free = villagers.Where(v => !takenGivers.Contains(v.PersonKey))
            .OrderBy(v => WishHash(coin + ":giver:" + v.PersonKey))
            .ThenBy(v => v.PersonKey, StringComparer.Ordinal)
            .ToList();
        bool OwnShare(ServerNpc v) => laneCount <= 1
            || WishHash(place + ":" + p.PlayerId + ":share:" + v.PersonKey) % (uint)laneCount == (uint)plan.Lane;

        // 0: the lane's own share, 1: anybody else, 2: the last giver again (all without another lane's villager).
        for (int pass = 0; pass < (ownShareOnly ? 1 : 3); pass++)
        {
            foreach (var v in free)
            {
                bool last = v.PersonKey == lastGiver;
                bool fits = pass switch
                {
                    0 => !last && OwnShare(v),
                    1 => !last && !OwnShare(v),
                    _ => last,
                };
                if (!fits || PickWishTemplate(coin, v.Job, plan.N, feasible, ln?.LastTemplate ?? string.Empty) is not { } tpl)
                {
                    continue;
                }

                plan.Giver = v;
                plan.GiverKey = v.PersonKey;
                plan.Template = tpl;
                plan.Count = tpl.Jitter > 0
                    ? Math.Max(1, tpl.Count + (int)(WishHash(coin + ":count:" + tpl.Key) % (uint)(2 * tpl.Jitter + 1)) - tpl.Jitter)
                    : Math.Max(1, tpl.Count);
                plan.Def = NewWishDef(plan);
                return true;
            }
        }

        return false;
    }

    /// <summary>The template a villager of <paramref name="job"/> wishes: feasible here, open to the job, within the tier the
    /// lane reached (tier T after T−1 fulfilled wishes of the lane — every lane starts easy), preferably not the lane's
    /// last one. Ranked by hash, so it is stable.</summary>
    private WishTemplate? PickWishTemplate(string coin, string job, int laneDone, HashSet<string> feasible, string lastTemplate)
    {
        if (_wishTemplateForTest is not null)
        {
            return _content.Wishes.FirstOrDefault(t => t.Key == _wishTemplateForTest);
        }

        var pool = _content.Wishes.Where(t => feasible.Contains(t.Key) && t.Tier <= laneDone + 1
                && (t.Jobs.Count == 0 || t.Jobs.Contains(job, StringComparer.Ordinal)))
            .OrderBy(t => WishHash(coin + ":tpl:" + t.Key))
            .ThenBy(t => t.Key, StringComparer.Ordinal)
            .ToList();
        return pool.FirstOrDefault(t => t.Key != lastTemplate) ?? pool.FirstOrDefault();
    }

    /// <summary>The mission definition of a coined lane — a system mission with the villager as giver, never story
    /// progress, never repeatable.</summary>
    private static MissionDefinition NewWishDef(WishLanePlan plan)
    {
        var tpl = plan.Template!;
        var def = new MissionDefinition
        {
            Id = plan.MissionId,
            Source = MissionSource.System,
            NameKey = "wish.title",
            DescriptionKey = tpl.AskKey,
            GiverName = plan.Giver?.Name ?? string.Empty,
            Objectives = { new MissionObjective { Type = tpl.ObjectiveType, Target = tpl.Item, TargetGroup = tpl.Group, Required = plan.Count } },
            KnowledgeReward = tpl.Knowledge,
            Repeatable = false,
            Active = true,
            CountsForStory = false,
        };
        foreach (var g in tpl.Gift)
        {
            def.Rewards.Add(new ItemAmount(g.Item, g.Count));
        }

        return def;
    }

    /// <summary>#2958: what a Collect / Deliver objective counts and takes — its target, its group included. A wish coined
    /// before the groups existed (saved without <see cref="MissionObjective.TargetGroup"/>) takes the group of the template
    /// it came from, so an old firewood wish takes pale logs too.</summary>
    private ItemAmount ObjectiveAmount(MissionDefinition def, MissionObjective obj)
    {
        var need = obj.AsItemAmount();
        if (need.Group.Length == 0 && def.NameKey == "wish.title" && WishTemplateOf(def) is { } tpl && tpl.Item == obj.Target)
        {
            need.Group = tpl.Group;
        }

        return need;
    }

    /// <summary>The template a wish definition was coined from (its description key), or null when it left the data.</summary>
    private WishTemplate? WishTemplateOf(MissionDefinition def)
        => _content.Wishes.FirstOrDefault(t => t.AskKey == def.DescriptionKey);

    /// <summary>The plans of the settlement an NPC lives in, for this player — null when the NPC has no wishes, or (with
    /// <paramref name="playerThere"/>) the player is not in its settlement (a talk with a villager visiting elsewhere is
    /// no wish talk).</summary>
    private List<WishLanePlan>? WishPlansAround(PlayerSession session, ServerNpc npc, bool playerThere, out SettlementInstance? settlement)
    {
        settlement = null;
        if (_content.Wishes.Count == 0 || !WishEligible(npc))
        {
            return null;
        }

        settlement = _settlements.FirstOrDefault(s => !s.Ruined && s.Name == npc.Settlement);
        if (settlement is null || (playerThere && !PlayerInSettlement(session.State, settlement))
            || !WishVillagers().TryGetValue(settlement.Name, out var villagers))
        {
            return null;
        }

        return PlanWishes(session.State, settlement, villagers, WishFeasibleTemplates());
    }

    // ---------------- Progress ----------------

    /// <summary>How far the player is with a held wish: the items in reach (inventory + cargo aboard) for bring/show, the
    /// scans counted for scan.</summary>
    private int WishHave(PlayerSession session, MissionDefinition def, MissionProgress pr, MaterialPool? pool = null)
    {
        var obj = def.Objectives.FirstOrDefault();
        if (obj is null)
        {
            return 0;
        }

        return obj.Type is MissionObjectiveType.Collect or MissionObjectiveType.Deliver
            ? (pool ?? new MaterialPool(_content, session.State, ShipOf(session))).Count(ObjectiveAmount(def, obj)) // #2958: its group
            : pr.ObjectiveProgress.Count > 0 ? pr.ObjectiveProgress[0] : 0;
    }

    /// <summary>Whether a held wish can be handed over now.</summary>
    private bool WishReady(PlayerSession session, MissionDefinition def, MissionProgress pr, MaterialPool? pool = null)
        => def.Objectives.Count > 0 && WishHave(session, def, pr, pool) >= def.Objectives[0].Required;

    // ---------------- Talking ----------------

    /// <summary>#2617: the villager a wish talk is about — a neighbour visiting the base (a guest with the villager's person
    /// key) stands for its villager, who is hidden at home meanwhile; anyone else is themselves. Null for a guest without
    /// a villager (the merchant, the traveller).</summary>
    private ServerNpc? WishPersonOf(ServerNpc npc)
    {
        if (!IsGuestJob(npc.Job))
        {
            return npc;
        }

        var guest = _baseGuests.FirstOrDefault(g => g.NpcId == npc.Id);
        return guest is { SourceNpcId: > 0 } ? _npcs.FirstOrDefault(n => n.Id == guest.SourceNpcId) : null;
    }

    /// <summary>#2617: the NPC the players SEE for a villager — the villager, or while it is away visiting a base the guest
    /// walking around there in its place (null when neither is on the world). The wish markers sit on this one.</summary>
    private ServerNpc? WishFaceOf(ServerNpc villager)
    {
        if (!villager.VisitingAway)
        {
            return villager;
        }

        var guest = _baseGuests.FirstOrDefault(g => g.SourceNpcId == villager.Id);
        return guest is null ? null : _npcs.FirstOrDefault(n => n.Id == guest.NpcId);
    }

    /// <summary>#2617: the visible NPC with a person key — never a villager hidden at home while it visits a base (its guest
    /// carries the same key and is the one standing there).</summary>
    private ServerNpc? VisiblePersonByKey(string personKey)
        => personKey.Length == 0 ? null : _npcs.FirstOrDefault(n => n.PersonKey == personKey && !n.VisitingAway);

    /// <summary>
    /// The wish part of an E-talk (#2601), asked by <c>HandleTalkToNpc</c> after the sleepy check and before any other
    /// dialogue: a held wish that is complete is handed over (thanks + present, +5 standing); an incomplete one gets a
    /// "you have 3 of 5 — no hurry!" (again only once the progress changed — a second press talks normally); an open
    /// offer opens the wish dialogue unless the player said "maybe later" this session. Returns true when the talk was
    /// handled here. The welcome-back bubble (#2595) goes out before the panel, as on the ordinary dialogue path.
    /// <para>#2617: a neighbour visiting the base (<see cref="WishPersonOf"/>) handles its villager's HELD wish the same way
    /// — the hand-over with present and standing, or the reminder — so the "?" over the guest means what it says; an open
    /// offer there is the guest's own "Can I help you with anything?" (<c>guest:wish</c>).</para>
    /// </summary>
    private bool TryWishConversation(PlayerSession session, ServerNpc npc, string npcKey, bool welcomeBack)
    {
        bool guest = IsGuestJob(npc.Job);
        if (WishPersonOf(npc) is not { } person)
        {
            return false;
        }

        var plans = WishPlansAround(session, person, playerThere: !guest, out _);
        var plan = plans?.FirstOrDefault(pl => pl.GiverKey.Length > 0 && pl.GiverKey == person.PersonKey);
        if (plan?.Template is null || (guest && plan.Held is null))
        {
            return false;
        }

        string locale = session.Locale;
        if (plan.Held is not null && plan.Def is not null)
        {
            Serve(session); // the cargo of THIS player's ship counts while aboard
            var pool = new MaterialPool(_content, session.State, _ship);
            if (WishReady(session, plan.Def, plan.Held, pool))
            {
                if (welcomeBack)
                {
                    SendPassingGreeting(session, npc, npcKey, welcomeBack: true);
                }

                return HandOverWishByTalk(session, npc, plan, pool);
            }

            int have = WishHave(session, plan.Def, plan.Held, pool);
            if (session.Npc.WishReminded.TryGetValue(plan.MissionId, out int said) && said == have)
            {
                return false; // nothing new since the last reminder — an ordinary chat this time
            }

            session.Npc.WishReminded[plan.MissionId] = have;
            if (welcomeBack)
            {
                SendPassingGreeting(session, npc, npcKey, welcomeBack: true);
            }

            string remindKey = plan.Template.Kind == WishTemplate.KindScan ? "dlg.wish.remind_scan" : "dlg.wish.remind";
            Send(session, new NpcDialogState
            {
                NpcId = npc.Id,
                Name = npc.Name,
                Text = FillWishText(Localize(locale, remindKey), plan.Count, WishItemName(locale, plan.Template))
                    .Replace("{have}", have.ToString(CultureInfo.InvariantCulture)),
                End = true,
            });
            return true;
        }

        if (plan.Held is not null || session.Npc.DeclinedWishGivers.Contains(npc.PersonKey) || WishOfferPaused(npc))
        {
            return false; // "maybe later" — the ordinary chat until the next visit (the "!" stays); a festival goes first
        }

        if (welcomeBack)
        {
            SendPassingGreeting(session, npc, npcKey, welcomeBack: true);
        }

        var node = WishDialog.Nodes[0];
        string text = WishAskText(session, npc, plan, requestAi: true) + "\n\n" + WishFactLine(locale, plan.Template, plan.Count);
        StartNpcConversation(session, npc, WishDialog); // #2616
        Send(session, new NpcDialogState
        {
            NpcId = npc.Id,
            Name = npc.Name,
            Text = text,
            Choices = node.Choices.Select(c => Localize(locale, c.TextKey)).ToArray(),
            End = false,
        });
        return true;
    }

    /// <summary>Hands a complete wish over in conversation: a full bag keeps the wish ("make a little room"), else the
    /// ordinary turn-in runs (items used up for "bring me", kept for "show me"), and the villager thanks the player with
    /// the present line appended.</summary>
    private bool HandOverWishByTalk(PlayerSession session, ServerNpc npc, WishLanePlan plan, MaterialPool pool)
    {
        string locale = session.Locale;
        if (!pool.CanFit(plan.Def!.Rewards))
        {
            Send(session, new NpcDialogState { NpcId = npc.Id, Name = npc.Name, Text = Localize(locale, "dlg.wish.full"), End = true });
            return true;
        }

        string thanks = WishThanksText(session, npc, plan);
        string gift = WishGiftLine(locale, plan.Def, npc.Name);
        bool done;
        _wishTurnInByTalk = true;
        try
        {
            done = HandleTurnInMission(session, plan.MissionId);
        }
        finally
        {
            _wishTurnInByTalk = false;
        }

        if (!done)
        {
            return false;
        }

        Send(session, new NpcDialogState { NpcId = npc.Id, Name = npc.Name, Text = thanks + "\n\n" + gift, End = true });
        return true;
    }

    /// <summary>The <c>wish:accept</c> / <c>wish:later</c> dialogue consequences. Accepting re-plans the place (nothing
    /// is trusted from the open panel) and grants the lane this villager holds; "later" sets the offer aside for the
    /// session.</summary>
    private void ApplyWishConsequence(PlayerSession session, ServerNpc npc, string npcKey, string what)
    {
        if (what == "later")
        {
            session.Npc.DeclinedWishGivers.Add(npc.PersonKey);
            return;
        }

        if (what == "accept")
        {
            GrantWish(session, npc, npcKey, answerWhenReady: true);
        }
    }

    /// <summary>Puts the villager's wish into the player's mission log — persisted (definition in the mission store,
    /// progress with the player), the giver remembered in the lane, the acceptance recorded as an interaction.
    /// <paramref name="answerWhenReady"/>: accepted with everything already in the bag (a new player starts with berries),
    /// the villager's answer says so (<c>_dialogResponseOverride</c>) and the next talk hands it over — in the village, or
    /// (#2617) to the neighbour still visiting the base. Without it the 1 Hz check's "bring it over!" toast tells it.</summary>
    private bool GrantWish(PlayerSession session, ServerNpc npc, string npcKey, bool answerWhenReady = false)
    {
        var p = session.State;
        var plans = WishPlansAround(session, npc, playerThere: false, out var settlement);
        var plan = plans?.FirstOrDefault(pl => pl.Held is null && pl.GiverKey == npc.PersonKey && pl.Def is not null);
        if (plan is null || settlement is null || p.Missions.Any(m => m.MissionId == plan.MissionId))
        {
            return false;
        }

        var def = plan.Def!;
        _missionDefs[def.Id] = def;
        _repo.SaveMission(def);
        var progress = NewMissionProgress(p, def, string.Empty);
        p.Missions.Add(progress);

        string place = SettlementLocationKey(settlement.Name);
        if (!p.Wishes.TryGetValue(place, out var ledger))
        {
            ledger = new WishLedger();
            p.Wishes[place] = ledger;
        }

        while (ledger.Lanes.Count <= plan.Lane)
        {
            ledger.Lanes.Add(new WishLane());
        }

        ledger.Lanes[plan.Lane].N = plan.N;
        ledger.Lanes[plan.Lane].Giver = npc.PersonKey;
        SetWishVillage(ledger.Lanes[plan.Lane], settlement); // #2714: where it is handed over — the tab, the toasts, the map

        // Accepted with everything already in the bag: the answer says "you already have it all" (the "?" follows with the
        // markers the dialogue sends), so the 1 Hz check's toast stays quiet right after the yes.
        if (answerWhenReady && WishReady(session, def, progress))
        {
            session.Npc.WishReady.Add(def.Id);
            _dialogResponseOverride = Localize(session.Locale, "dlg.wish.r_yes_ready");
        }

        RecordNpcInteraction(p, npcKey, npc.Name, npc.Role, NpcInteractionKind.MissionAccepted, settlement.Name);
        Send(session, MissionSucceeded(session, def, MissionOutcomes.Accepted));
        SendMissionList(session);
        SendMarkers(session); // #2714: the village goes on the map
        RaiseGameEvent(session, GameEventKeys.MissionAccepted, def.Id);
        _log.Info($"Player '{p.Name}' took a wish ('{plan.Template?.Key}') from {npc.Name} in {settlement.Name}.");
        return true;
    }

    /// <summary>
    /// After a wish was turned in (<c>HandleTurnInMission</c>, by talk or at the tab next to the villager): the lane moves
    /// on (counter +1, the next wish goes to somebody else and asks for something else), +<see cref="WishStandingBonus"/>
    /// standing with the villager, the definition leaves the store, the markers refresh. At the tab the villager also
    /// says thank you in a bubble — #2617: the one the player sees (a neighbour visiting the base, never the villager
    /// hidden at home meanwhile).
    /// </summary>
    private void OnWishTurnedIn(PlayerSession session, MissionDefinition def)
    {
        var p = session.State;
        string giverKey = string.Empty;
        if (TryParseWishId(def.Id, out string place, out int lane) && p.Wishes.TryGetValue(place, out var ledger)
            && lane < ledger.Lanes.Count)
        {
            var ln = ledger.Lanes[lane];
            giverKey = ln.Giver;
            ln.LastGiver = ln.Giver;
            ln.Giver = string.Empty;
            ClearWishVillage(ln); // #2714: the village marker goes with the wish
            ln.LastTemplate = WishTemplateOf(def)?.Key ?? string.Empty;
            ln.N++;
            ledger.Done++;
        }

        if (giverKey.Length > 0)
        {
            AddStanding(p, giverKey, WishStandingBonus, "wish");
            if (!_wishTurnInByTalk && VisiblePersonByKey(giverKey) is { } giver
                && WishTemplateOf(def) is { } tpl)
            {
                var plan = new WishLanePlan { MissionId = def.Id, GiverKey = giverKey, Giver = giver, Template = tpl, Count = def.Objectives.FirstOrDefault()?.Required ?? 0, Def = def };
                Send(session, new NpcGreeting
                {
                    NpcId = giver.Id,
                    Name = giver.Name,
                    Role = giver.Role,
                    Text = WishThanksText(session, giver, plan),
                    GreetingId = NextGreetingId(),
                    Kind = NpcGreetingKinds.Addressed,
                });
            }
        }

        _missionDefs.Remove(def.Id);
        _repo.DeleteMission(def.Id);
        session.Npc.WishReady.Remove(def.Id);
        session.Npc.WishReminded.Remove(def.Id);
        SendNpcStandings(session);
        SendMarkers(session); // #2714: the village marker is gone (another wish from there keeps it)
    }

    /// <summary>The tab's turn-in gate (#2601): a wish is handed over in person — the villager must be within greeting
    /// reach (#2617: the one standing there — a neighbour visiting the base counts, the villager hidden at home meanwhile
    /// does not). <paramref name="giverName"/> names them for the refusal.</summary>
    private bool WishGiverNear(PlayerSession session, string missionId, out string giverName)
    {
        giverName = GetMissionDef(missionId)?.GiverName ?? string.Empty;
        var p = session.State;
        if (!TryParseWishId(missionId, out string place, out int lane) || !p.Wishes.TryGetValue(place, out var ledger)
            || lane >= ledger.Lanes.Count || ledger.Lanes[lane].Giver.Length == 0)
        {
            return false;
        }

        string key = ledger.Lanes[lane].Giver;
        return _npcs.Any(n => n.PersonKey == key && !n.VisitingAway && WrapDistSq(p.Position, n.Pos) <= NpcGreetRange * NpcGreetRange);
    }

    // ---------------- Markers + the 1 Hz check ----------------

    /// <summary>The villagers of the active world with an offer ("!") and with a held wish the player can hand over now
    /// ("?") for this player — filled into every <see cref="NpcStandingList"/>. Also warms the AI re-wording of the
    /// offers in the settlement the player stands in. #2617: a villager away visiting a base is marked on the guest the
    /// players see there (<see cref="WishFaceOf"/>) — never on its hidden id; talking to that guest takes the "?" wish
    /// and its "Can I help you with anything?" the "!" one.</summary>
    private void WishMarksFor(PlayerSession session, List<int> offers, List<int> ready)
    {
        if (_content.Wishes.Count == 0 || _settlements.Count == 0)
        {
            return;
        }

        var villagersBySettlement = WishVillagers();
        if (villagersBySettlement.Count == 0)
        {
            return;
        }

        var feasible = WishFeasibleTemplates();
        var pool = new MaterialPool(_content, session.State, ShipOf(session));
        foreach (var s in _settlements)
        {
            if (s.Ruined || string.IsNullOrEmpty(s.Name) || !villagersBySettlement.TryGetValue(s.Name, out var villagers))
            {
                continue;
            }

            bool here = AiLinesEnabled && PlayerInSettlement(session.State, s);
            foreach (var plan in PlanWishes(session.State, s, villagers, feasible))
            {
                if (plan.Giver is null || plan.Template is null)
                {
                    continue;
                }

                if (WishFaceOf(plan.Giver) is not { } face)
                {
                    continue; // nobody to see — no marker
                }

                if (plan.Held is null)
                {
                    if (plan.Giver.Phase == NpcPhase.Festival)
                    {
                        continue; // celebrating — no new "!" until they are back to their day
                    }

                    offers.Add(face.Id); // #2617: away visiting the base → on the guest, whose "Can I help?" takes it
                    if (here && !plan.Giver.VisitingAway)
                    {
                        WishAskText(session, plan.Giver, plan, requestAi: true); // prefetch — the next talk may show it
                    }
                }
                else if (plan.Def is not null && WishReady(session, plan.Def, plan.Held, pool))
                {
                    ready.Add(face.Id);
                }
            }
        }
    }

    /// <summary>
    /// The 1 Hz wish check of one joined player (from the VEGA advisor tick — no tick of its own): when a held wish turns
    /// ready the markers are re-sent and the player hears it once ("You have everything Tomas wished for — bring it
    /// over!"), and the villager's thanks is warmed for the AI; when the player walks into another settlement with AI on,
    /// the markers (and with them the offers' AI re-wording) are refreshed, and so they are when a festival starts or ends
    /// where the player stands. Cheap: a box test per settlement, one pass over the NPCs and over the mission list.
    /// </summary>
    private void TickWishesFor(PlayerSession session)
    {
        if (_content.Wishes.Count == 0 || session.Spectating)
        {
            return;
        }

        var p = session.State;
        bool refresh = false;
        SettlementInstance? hereSettlement = null;
        foreach (var s in _settlements)
        {
            if (!s.Ruined && !string.IsNullOrEmpty(s.Name) && PlayerInSettlement(p, s))
            {
                hereSettlement = s;
                break;
            }
        }

        string here = hereSettlement is null ? string.Empty : SettlementLocationKey(hereSettlement.Name);
        if (AiLinesEnabled && here != session.Npc.WishPlaceKey)
        {
            session.Npc.WishPlaceKey = here;
            refresh = here.Length > 0;
        }

        // A festival starting or ending in the village the player stands in pauses / brings back the "!" (#2604).
        bool celebrating = hereSettlement is not null
            && _npcs.Any(n => n.Phase == NpcPhase.Festival && n.Settlement == hereSettlement.Name);
        if (celebrating != session.Npc.WishFestivalHere)
        {
            session.Npc.WishFestivalHere = celebrating;
            refresh = true;
        }

        var readyNow = new HashSet<string>(StringComparer.Ordinal);
        MaterialPool? pool = null;
        foreach (var pr in p.Missions)
        {
            if (!IsWishMission(pr.MissionId) || pr.Status == MissionStatus.TurnedIn || GetMissionDef(pr.MissionId) is not { } def)
            {
                continue;
            }

            pool ??= new MaterialPool(_content, p, ShipOf(session));
            if (!WishReady(session, def, pr, pool))
            {
                continue;
            }

            readyNow.Add(pr.MissionId);
            if (!session.Npc.WishReady.Contains(pr.MissionId))
            {
                Send(session, new ServerMessage { Text = WishReadyText(session, def) });
                WarmWishThanks(session, def);
            }
        }

        if (!readyNow.SetEquals(session.Npc.WishReady))
        {
            session.Npc.WishReady = readyNow;
            refresh = true;
        }

        if (refresh)
        {
            SendNpcStandings(session);
        }

        if (LearnWishVillages(session))
        {
            SendMissionList(session); // #2714: a wish held from before learnt its village — the tab and the map say it now
            SendMarkers(session);
        }
    }

    // ---------------- Where (#2714) ----------------

    /// <summary>#2714: the lane of a wish the player holds (by the wish id), or null.</summary>
    private static WishLane? HeldWishLane(PlayerState p, string missionId)
        => TryParseWishId(missionId, out string place, out int lane) && p.Wishes.TryGetValue(place, out var ledger)
           && lane < ledger.Lanes.Count && ledger.Lanes[lane].Giver.Length > 0
            ? ledger.Lanes[lane]
            : null;

    /// <summary>#2714: the village a held wish is handed over in ("" = not known yet).</summary>
    private static string WishVillageOf(PlayerState p, string missionId) => HeldWishLane(p, missionId)?.Village ?? string.Empty;

    /// <summary>#2714: the map marker id of a wish's village (one marker per village).</summary>
    private static string WishMarkerId(string placeKey) => WishMarkerIdPrefix + placeKey;

    /// <summary>#2714: the Missions tab's place of a held wish — the village's name and its marker's id, both "" while the
    /// village is not known.</summary>
    private static (string Name, string MarkerId) WishPlaceOf(PlayerState p, string missionId)
        => TryParseWishId(missionId, out string place, out _) && WishVillageOf(p, missionId) is { Length: > 0 } village
            ? (village, WishMarkerId(place))
            : (string.Empty, string.Empty);

    /// <summary>#2714: remembers the giver's village of a held wish — on the active world, where it was granted.</summary>
    private void SetWishVillage(WishLane lane, SettlementInstance village)
    {
        var at = SettlementCenter(village);
        lane.Village = village.Name;
        lane.VillageLocationId = _world.LocationId;
        lane.VillageX = at.X;
        lane.VillageY = at.Y;
        lane.VillageZ = at.Z;
    }

    /// <summary>#2714: forgets the village of a lane whose wish was handed over.</summary>
    private static void ClearWishVillage(WishLane lane)
    {
        lane.Village = string.Empty;
        lane.VillageLocationId = string.Empty;
        lane.VillageX = 0f;
        lane.VillageY = 0f;
        lane.VillageZ = 0f;
    }

    /// <summary>#2714: a wish held from before #2714 has no village yet — fill it in from the settlement of its place on the
    /// active world (the player's: run by the 1 Hz check). True when anything was learnt. Cheap: nothing to do once every
    /// held wish knows its village; a held wish from another body only compares the cached place keys.</summary>
    private bool LearnWishVillages(PlayerSession session)
    {
        bool learnt = false;
        foreach (var (place, ledger) in session.State.Wishes)
        {
            foreach (var lane in ledger.Lanes)
            {
                if (lane.Giver.Length == 0 || lane.Village.Length > 0)
                {
                    continue;
                }

                if (_settlements.FirstOrDefault(s => !string.IsNullOrEmpty(s.Name) && SettlementPlaceKey(s) == place) is { } village)
                {
                    SetWishVillage(lane, village);
                    learnt = true;
                }
            }
        }

        return learnt;
    }

    /// <summary>#2714: the village markers of the viewer's held wishes on <paramref name="loc"/> (their own only, one per
    /// village) — added to their marker view by <see cref="MarkersFor"/>. Flagged <see cref="NetMarker.Wish"/>; an older
    /// client draws an ordinary named marker (home icon, yellow) labelled with the village.</summary>
    private void AddWishMarkers(PlayerSession viewer, string loc, List<NetMarker> into)
    {
        var p = viewer.State;
        if (p.Wishes.Count == 0)
        {
            return;
        }

        HashSet<string>? placed = null;
        foreach (var pr in p.Missions)
        {
            if (!IsWishMission(pr.MissionId) || pr.Status == MissionStatus.TurnedIn
                || !TryParseWishId(pr.MissionId, out string place, out _)
                || HeldWishLane(p, pr.MissionId) is not { Village.Length: > 0 } lane
                || !string.Equals(lane.VillageLocationId, loc, StringComparison.Ordinal)
                || !(placed ??= new HashSet<string>(StringComparer.Ordinal)).Add(place))
            {
                continue;
            }

            into.Add(new NetMarker
            {
                Id = WishMarkerId(place),
                OwnerId = p.PlayerId,
                X = lane.VillageX,
                Y = lane.VillageY,
                Z = lane.VillageZ,
                Label = lane.Village,
                Icon = WishMarkerIcon,
                Color = WishMarkerColor,
                Wish = true,
            });
        }
    }

    /// <summary>#2713: the toast when a held wish turns ready — "… bring it to Testville!" when its village is known, else the
    /// plain "bring it over!" token.</summary>
    private string WishReadyText(PlayerSession session, MissionDefinition def)
        => WishVillageOf(session.State, def.Id) is { Length: > 0 } village
            ? Localize(session.Locale, "srv.wish.ready_village").Replace("{name}", def.GiverName).Replace("{village}", village)
            : "@srv.wish.ready:" + def.GiverName;

    /// <summary>#2713: the Missions tab's refusal away from the giver — "Take it to Tomas in person — in Testville!" when the
    /// village is known, else the plain token.</summary>
    private string WishReturnReason(PlayerSession session, string missionId, string giverName)
        => WishVillageOf(session.State, missionId) is { Length: > 0 } village
            ? Localize(session.Locale, "srv.wish.return_village").Replace("{name}", giverName).Replace("{village}", village)
            : "@srv.wish.return_giver:" + giverName;

    /// <summary>Starts the AI thanks of a wish that just turned ready, so the hand-over can show it (a no-op without AI or
    /// when the villager is not on this world).</summary>
    private void WarmWishThanks(PlayerSession session, MissionDefinition def)
    {
        if (!AiLinesEnabled || !TryParseWishId(def.Id, out string place, out int lane)
            || !session.State.Wishes.TryGetValue(place, out var ledger) || lane >= ledger.Lanes.Count
            || WishTemplateOf(def) is not { } tpl)
        {
            return;
        }

        string giverKey = ledger.Lanes[lane].Giver;
        if (_npcs.FirstOrDefault(n => n.PersonKey == giverKey) is not { } giver)
        {
            return;
        }

        var plan = new WishLanePlan { MissionId = def.Id, GiverKey = giverKey, Giver = giver, Template = tpl, Count = def.Objectives.FirstOrDefault()?.Required ?? 0, Def = def };
        WishThanksText(session, giver, plan, requestAi: true);
    }

    // ---------------- Texts ----------------

    /// <summary>The display name of what a template asks for: the item's name, or "animal" for a creature scan.</summary>
    private string WishItemName(string locale, WishTemplate tpl)
        => tpl.Kind == WishTemplate.KindScan
            ? Localize(locale, "wish.target.creature")
            : LocalizedName(locale, _content.GetItem(tpl.Item)?.NameKey, tpl.Item);

    private static string FillWishText(string text, int count, string item)
        => text.Replace("{count}", count.ToString(CultureInfo.InvariantCulture)).Replace("{item}", item);

    /// <summary>The always-authored fact line under a request: "You need: 5× Berries" (show: "Show me: … — you keep it!",
    /// scan: "Scan with your scanner: 1× animal").</summary>
    private string WishFactLine(string locale, WishTemplate tpl, int count)
    {
        string key = tpl.Kind switch
        {
            WishTemplate.KindShow => "dlg.wish.fact_show",
            WishTemplate.KindScan => "dlg.wish.fact_scan",
            _ => "dlg.wish.fact",
        };
        return FillWishText(Localize(locale, key), count, WishItemName(locale, tpl));
    }

    /// <summary>The present line after the thanks: "Tomas gives you: 3× Berry Seed, 2 knowledge points".</summary>
    private string WishGiftLine(string locale, MissionDefinition def, string giverName)
    {
        var parts = new List<string>();
        foreach (var r in def.Rewards)
        {
            parts.Add(r.Count.ToString(CultureInfo.InvariantCulture) + "× "
                + LocalizedName(locale, _content.GetItem(r.Item)?.NameKey, r.Item));
        }

        if (def.KnowledgeReward > 0)
        {
            parts.Add(Localize(locale, "dlg.wish.knowledge").Replace("{count}", def.KnowledgeReward.ToString(CultureInfo.InvariantCulture)));
        }

        return Localize(locale, "dlg.wish.gift").Replace("{npc}", giverName).Replace("{gift}", string.Join(", ", parts));
    }

    /// <summary>The Missions tab's title of a wish: "A wish from Tomas".</summary>
    private string WishTitle(string locale, MissionDefinition def)
        => Localize(locale, "wish.title").Replace("{npc}", def.GiverName);

    /// <summary>The Missions tab's description of a held wish: the villager's request (a cached AI re-wording when there
    /// is one, never requested from here) plus the fact line.</summary>
    private string WishDescription(PlayerSession session, MissionDefinition def)
    {
        if (WishTemplateOf(def) is not { } tpl)
        {
            return string.Empty;
        }

        int count = def.Objectives.FirstOrDefault()?.Required ?? tpl.Count;
        string giverKey = string.Empty;
        if (TryParseWishId(def.Id, out string place, out int lane) && session.State.Wishes.TryGetValue(place, out var ledger)
            && lane < ledger.Lanes.Count)
        {
            giverKey = ledger.Lanes[lane].Giver;
        }

        var giver = giverKey.Length > 0 ? _npcs.FirstOrDefault(n => n.PersonKey == giverKey) : null;
        var plan = new WishLanePlan { MissionId = def.Id, GiverKey = giverKey, Giver = giver, Template = tpl, Count = count, Def = def };
        string text = WishAskText(session, giver, plan, requestAi: false) + "\n\n" + WishFactLine(session.Locale, tpl, count);

        // #2713: and where to find the villager — "Tomas lives in Testville — visit them there when you're ready!"
        return WishVillageOf(session.State, def.Id) is { Length: > 0 } village
            ? text + "\n\n" + Localize(session.Locale, "wish.where").Replace("{npc}", def.GiverName).Replace("{village}", village)
            : text;
    }

    /// <summary>The villager's request: an AI re-wording in their voice when one is cached (purpose <c>wish_ask</c>,
    /// keyed by template, persona and language — never by player), else the authored <c>wish.&lt;key&gt;.ask</c>; tokens
    /// filled for this player. With <paramref name="requestAi"/> a miss starts the request for next time. The variant is
    /// picked by the wish id, so the panel and the Missions tab read the same.</summary>
    private string WishAskText(PlayerSession session, ServerNpc? npc, WishLanePlan plan, bool requestAi)
        => WishLine(session, npc, plan, NpcLinePurposes.WishAsk, plan.Template!.AskKey, WishAskRequiredTokens, WishAskMaxChars, requestAi);

    /// <summary>The villager's thanks — like <see cref="WishAskText"/>, purpose <c>wish_thanks</c>.</summary>
    private string WishThanksText(PlayerSession session, ServerNpc? npc, WishLanePlan plan, bool requestAi = false)
        => WishLine(session, npc, plan, NpcLinePurposes.WishThanks, plan.Template!.ThanksKey, WishThanksRequiredTokens, WishThanksMaxChars, requestAi);

    private string WishLine(PlayerSession session, ServerNpc? npc, WishLanePlan plan, string purpose, string textKey,
        IReadOnlyCollection<string> required, int maxChars, bool requestAi)
    {
        var tpl = plan.Template!;
        string locale = session.Locale;
        string item = WishItemName(locale, tpl);
        string raw = Localize(locale, textKey);
        string authored = FillWishText(raw, plan.Count, item);
        if (!AiLinesEnabled || npc is null || plan.GiverKey.Length == 0)
        {
            return authored;
        }

        string giverKey = plan.GiverKey;
        string cacheKey = AiLineCacheKey(purpose, tpl.Key, PersonaIndexFor(giverKey, npc.IsRobot, npc.Role), locale, string.Empty);

        // What is cached NOW is what shows — read before asking, so an answer can only ever reach the NEXT text.
        var cached = CachedAiLines(cacheKey, unchecked((int)WorldGenerator.StableHash(plan.MissionId)));
        if (requestAi)
        {
            AiLinesFor(cacheKey, AiLinePriority.Personal, () => WishLinesRequest(purpose, locale, npc, giverKey, tpl, raw, maxChars), required);
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [AiLineTokens.Count] = plan.Count.ToString(CultureInfo.InvariantCulture),
            [AiLineTokens.Item] = item,
        };
        return cached is { Length: > 0 } && FillAiLine(cached[0], session, values) is { Length: > 0 } text ? text : authored;
    }

    /// <summary>The <c>/npc-lines</c> request of a wish line: one line in the villager's own voice, the facts as tokens
    /// (<c>{count}</c>, <c>{item}</c> — the server fills them per player), the authored line as the meaning anchor.</summary>
    private static NpcLinesRequest WishLinesRequest(string purpose, string locale, ServerNpc npc, string giverKey, WishTemplate tpl,
        string authored, int maxChars)
    {
        bool ask = purpose == NpcLinePurposes.WishAsk;
        string fact = tpl.Kind switch
        {
            WishTemplate.KindShow => ask
                ? "The speaker would love to see {count} {item}; the player only shows them and keeps them."
                : "The player just showed the speaker {count} {item} and keeps them.",
            WishTemplate.KindScan => ask
                ? "The speaker asks the player to scan {count} {item} with the scanner to see the animals are well."
                : "The player scanned {count} {item}, so the speaker knows the animals are well.",
            _ => ask
                ? "The speaker asks the player to bring {count} {item}."
                : "The player just brought the speaker {count} {item}.",
        };
        return new NpcLinesRequest
        {
            Purpose = purpose,
            Language = locale,
            Place = "a small village",
            Theme = npc.Theme,
            Situation = ask
                ? "a villager asks the player in person for a small favour; no hurry, no deadline"
                : "the villager thanks the player warmly for a fulfilled wish",
            Speakers =
            {
                new NpcLinesSpeaker
                {
                    Role = npc.Role,
                    Job = npc.Job,
                    IsRobot = npc.IsRobot,
                    Persona = PersonaFor(giverKey, npc.Theme, npc.IsRobot, npc.Role),
                },
            },
            TopicHint = tpl.Reason.Length > 0 ? tpl.Reason : tpl.Key,
            Facts = { fact },
            Tokens = { AiLineTokens.Count, AiLineTokens.Item, AiLineTokens.Player },
            Authored = { authored },
            LineCount = 1,
            MaxChars = maxChars,
        };
    }

    // ---------------- Test hooks ----------------

    /// <summary>Test seam (#2601): every coined wish uses this template (null = the normal pick) — feasibility, job and
    /// tier are ignored for it.</summary>
    public void SetWishTemplateForTest(string? templateKey) => _wishTemplateForTest = templateKey;

    /// <summary>Test seam (#2601): pretends the world grows no plants and no trees (<paramref name="noPlants"/>) or only
    /// poisonous ones (<paramref name="toxicPlants"/>).</summary>
    public void SetWishFloraForTest(bool noPlants, bool toxicPlants)
    {
        _wishNoPlantsForTest = noPlants;
        _wishToxicPlantsForTest = toxicPlants;
    }

    /// <summary>Test seam (#2601): the template keys this world can grant.</summary>
    public IReadOnlyList<string> WishFeasibleTemplatesForTest() => WishFeasibleTemplates().OrderBy(k => k, StringComparer.Ordinal).ToList();

    /// <summary>Test seam (#2601): a settlement's tier (hamlet = one wish lane).</summary>
    public void SetSettlementTierForTest(string settlementName, string tier)
    {
        foreach (var s in _settlements.Where(s => s.Name == settlementName))
        {
            s.Tier = tier;
        }
    }

    /// <summary>Test seam (#2601): the wish lanes of every settlement on the active world for a player — a joined player's
    /// own state, else a fresh player of that id (what a new player would be offered).</summary>
    public IReadOnlyList<(string Settlement, int Lane, string MissionId, int GiverId, string GiverKey, string Template, int Count, bool Held)> WishLanesForTest(string playerId)
    {
        var state = FindSessionByPlayerId(playerId)?.State ?? new PlayerState { PlayerId = playerId, Name = playerId };
        var result = new List<(string, int, string, int, string, string, int, bool)>();
        var villagersBySettlement = WishVillagers();
        var feasible = WishFeasibleTemplates();
        foreach (var s in _settlements)
        {
            if (s.Ruined || string.IsNullOrEmpty(s.Name) || !villagersBySettlement.TryGetValue(s.Name, out var villagers))
            {
                continue;
            }

            foreach (var plan in PlanWishes(state, s, villagers, feasible))
            {
                if (plan.Giver is not null && plan.Template is not null)
                {
                    result.Add((s.Name, plan.Lane, plan.MissionId, plan.Giver.Id, plan.GiverKey, plan.Template.Key, plan.Count, plan.Held is not null));
                }
            }
        }

        return result;
    }

    /// <summary>Test seam (#2601): the NPC ids that have a wish for this player right now (offered or held) — tests about
    /// other dialogues talk to somebody else.</summary>
    public IReadOnlyCollection<int> WishGiverIdsForTest(string playerId)
        => WishLanesForTest(playerId).Select(l => l.GiverId).ToHashSet();

    /// <summary>Test seam (#2601): the markers the player's next standings message carries.</summary>
    public (int[] Offers, int[] Ready) WishMarksForTest(string playerId)
    {
        var offers = new List<int>();
        var ready = new List<int>();
        if (FindSessionByPlayerId(playerId) is { } s)
        {
            WishMarksFor(s, offers, ready);
        }

        return (offers.ToArray(), ready.ToArray());
    }

    /// <summary>Test seam (#2601): the player's held wish ids.</summary>
    public IReadOnlyList<string> HeldWishesForTest(string playerId)
        => FindSessionByPlayerId(playerId)?.State.Missions.Where(m => IsWishMission(m.MissionId) && m.Status != MissionStatus.TurnedIn)
            .Select(m => m.MissionId).ToList() ?? new List<string>();

    /// <summary>Test seam (#2601): runs the player's 1 Hz wish check now.</summary>
    public void TickWishesForTest(string playerId)
    {
        if (FindSessionByPlayerId(playerId) is { } s)
        {
            TickWishesFor(s);
        }
    }

    /// <summary>Test seam (#2601): sends the player's standings (with the wish markers) now.</summary>
    public void SendNpcStandingsForTest(string playerId)
    {
        if (FindSessionByPlayerId(playerId) is { } s)
        {
            SendNpcStandings(s);
        }
    }

    /// <summary>Test seam (#2601): puts an NPC into (or out of) the festival phase, as a running festival does.</summary>
    public void SetNpcCelebratingForTest(int npcId, bool celebrating)
    {
        if (_npcs.FirstOrDefault(n => n.Id == npcId) is { } npc)
        {
            npc.Phase = celebrating ? NpcPhase.Festival : NpcPhase.Day;
        }
    }

    /// <summary>Test seam (#2601): whether a mission id is a wish.</summary>
    public static bool IsWishMissionForTest(string missionId) => IsWishMission(missionId);

    /// <summary>Test seam (#2714): where a held wish is handed over, as stored with it ("" = none / not known).</summary>
    public (string Village, string LocationId, Vector3f At) WishVillageForTest(string playerId, string missionId)
        => FindSessionByPlayerId(playerId)?.State is { } p && HeldWishLane(p, missionId) is { } lane
            ? (lane.Village, lane.VillageLocationId, new Vector3f(lane.VillageX, lane.VillageY, lane.VillageZ))
            : (string.Empty, string.Empty, default);

    /// <summary>Test seam (#2714): the wish village markers the player sees now.</summary>
    internal IReadOnlyList<NetMarker> WishMarkersForTest(string playerId)
        => FindSessionByPlayerId(playerId) is { } s
            ? MarkersFor(s).Markers.Where(m => m.Wish).ToList()
            : new List<NetMarker>();

    /// <summary>Test seam (#2714): forgets the village of every held wish, as a save from before #2714 has it.</summary>
    public void ForgetWishVillagesForTest(string playerId)
    {
        foreach (var lane in FindSessionByPlayerId(playerId)?.State.Wishes.Values.SelectMany(l => l.Lanes) ?? Enumerable.Empty<WishLane>())
        {
            ClearWishVillage(lane);
        }
    }

    /// <summary>Test seam (#2714): a settlement's centre — where its wish marker sits.</summary>
    public Vector3f SettlementCenterForTest(string settlementName)
        => _settlements.FirstOrDefault(s => s.Name == settlementName) is { } s ? SettlementCenter(s) : default;
}
