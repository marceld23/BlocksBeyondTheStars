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
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Friendly visitors at the player's base (#2602, decision E10). Until now the only NPCs that ever came to a base were
/// the opt-in bandit scouts — a home nobody visits is lonely. Now, about once per one and a half in-game days, a
/// friendly guest walks up to a base whose owner is at home:
/// <list type="bullet">
/// <item><b>A neighbour</b> (weight 50, needs a village on the world with a villager nobody is watching): the REAL
/// villager of the nearest settlement — same name, face, voice and person key (<see cref="ServerNpc.PersonKey"/>), so
/// the friendship, the dialogue stage and the "People you know" roster are that person's. While visiting, the villager
/// is absent from the village (<see cref="ServerNpc.VisitingAway"/>: not sent to clients, not moved, no routine, no
/// passing hello) and is back home the moment the guest leaves.</item>
/// <item><b>A travelling merchant</b> (weight 30, none while a trader ship is parked on the body): a vendor-role NPC
/// with a bit of everything (#2630: the <see cref="MarketStock.MixedTheme"/> stock, one offer from each of several trades,
/// new every day) — <see cref="MarketAvailable"/> and <see cref="ThemedMarketRefusal"/> accept barter beside it, and the
/// client's "Trade or talk?" works as at any stall.</item>
/// <item><b>A lost traveller</b> (weight 20): asks the way to the nearest village, thanks the player with a small
/// present and walks off in that direction.</item>
/// </list>
/// <para><b>When:</b> the world option <see cref="Shared.Configuration.GameRules.FriendlyVisitors"/> is on (default on,
/// every preset); the owner is on this body, on foot (not aboard, in space or observing) and within
/// <see cref="BaseGuestOwnerReach"/> blocks of the core; it is day at the base (local day fraction
/// <see cref="BaseGuestDayStart"/>–<see cref="BaseGuestDayEnd"/>); no hostile is within
/// <see cref="BaseGuestHostileRange"/> of the owner and no bandit scouts are watching. Each base has a quiet spell of
/// 1.5 in-game days (× 0.8–1.2), a first sighting arms 0.4–0.8 of it, and a due window produces a visit with
/// <see cref="BaseGuestVisitChance"/>. One guest per base. Everything is RAM only — a visit is never saved.</para>
/// <para><b>Script</b> (1 Hz, <see cref="TickBaseGuests"/>): <i>spawn</i> 30–42 blocks out (inside the NPC path box,
/// never inside walls, a sealed room, water or lava; a neighbour on the bearing of its village) → <i>arriving</i>: walks
/// with the NPC A* (doors included) to a spot within three blocks of the owner, re-targeting up to three times when the
/// owner moves; unreachable (two failed routes, a sealed room, or 90 s) → it walks to the base's edge on the owner's
/// side and the owner reads "{name} is waiting outside your door!" → <i>greeting</i> within four blocks with a line of
/// sight (<see cref="NpcGreetingKinds.Guest"/>, a wave) → <i>waiting</i> 2 minutes (the merchant 3), held while a player
/// is within six blocks, cut short after its present → <i>leaving</i> back out and despawned where nobody sees it (no
/// player within 32 blocks at the end of its walk, or none within 24 after two minutes). #2713: the owner going aboard or
/// leaving the world starts the walk home too (the NPCs keep moving for an owner watching from the ship,
/// <see cref="WatchesGuestLeave"/>); only a world that was not ticked for <see cref="BaseGuestFrozenSeconds"/> ends a visit
/// at once. A leaving neighbour tells its owner "{name} is walking home to {village}.", and its present or a wish it
/// brought along comes with "I'm heading home to {village} soon — come and see me there!".</para>
/// <para><b>Talk:</b> guests are E-talkable; <see cref="PickDialog"/> treats their <c>guest_*</c> job like a profession, so
/// only the <c>job: guest_neighbour|guest_merchant|guest_traveler</c> dialogues of <c>data/dialogs.json</c> match.
/// Consequence <c>guest:gift:&lt;item&gt;:&lt;n&gt;</c> hands a present over once per guest and player. #2617: a neighbour
/// stands for its villager in wish matters — the "?" and "!" markers sit on the guest, a held wish is handed over to it
/// (or reminded) before its own dialogue, and <c>guest:wish</c> says "you already have everything" when the bag holds it
/// (the next talk hands it over right there). Memory: the
/// neighbour is its villager (person key); the merchant and the traveller are <c>guest:&lt;name&gt;</c> keys that are
/// never written to the save (<see cref="RecordNpcInteraction"/> skips them — the roster stays clean).</para>
/// <para><b>AI</b> (hosted worlds): the greeting (<c>guest_greet</c>) and the dialogue opening (<c>guest_offer</c>, the
/// <c>dialog_open</c> path) are requested at spawn, so the 30–60 s walk-in covers the latency; the authored line is
/// used whenever no re-wording is cached.</para>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The three kinds of friendly guest (#2602).</summary>
    internal enum BaseGuestKind : byte
    {
        Neighbour,
        Merchant,
        Traveller,
    }

    /// <summary>Where a guest is in its visit.</summary>
    internal enum BaseGuestPhase : byte
    {
        Arriving,
        Waiting,
        Leaving,
    }

    /// <summary>One friendly guest at a base (RAM only; lives on its world's <see cref="LoadedWorld.BaseGuests"/>).</summary>
    internal sealed class BaseGuest
    {
        public int NpcId;
        public int BaseId;
        public string OwnerId = string.Empty;
        public BaseGuestKind Kind;
        public BaseGuestPhase Phase;
        public double PhaseSince;
        public double LastStepAt;

        /// <summary>A neighbour's real villager, hidden while it visits (0 = none).</summary>
        public int SourceNpcId;

        /// <summary>The villager's routine switch before the visit, restored when it goes home.</summary>
        public bool SourceRoutine;

        /// <summary>The neighbour's home village; the place a lost traveller asks the way to ("" = none on the world).</summary>
        public string Place = string.Empty;

        /// <summary>Bearing (radians, from the base core) the guest leaves on: its village, its destination or where it came from.</summary>
        public double LeaveBearing;

        public bool Greeted;

        /// <summary>The owner could not be reached: the guest waits at the edge of the base instead.</summary>
        public bool Outside;

        public int Retargets;
        public double RetargetAt;
        public double WaitUntil;
        public double WaitHardEnd;

        /// <summary>Uptime after which a guest that handed its present over goes home (0 = no present given yet).</summary>
        public double LeaveSoonAt;

        /// <summary>Players who already got this guest's present (once per guest and player).</summary>
        public readonly HashSet<string> Gifted = new(StringComparer.Ordinal);
    }

    /// <summary>How often the guests' script and the visit scheduler run per world.</summary>
    private const double BaseGuestTickInterval = 1.0;

    /// <summary>The owner must stand at most this far (blocks) from the base core for a visit.</summary>
    private const float BaseGuestOwnerReach = 24f;

    /// <summary>Visits happen only in daylight at the base: local day fraction from …</summary>
    private const double BaseGuestDayStart = 0.25;

    /// <summary>… to (mid-afternoon, so a guest is gone before the evening routine).</summary>
    private const double BaseGuestDayEnd = 0.60;

    /// <summary>No machine, robber or hostile animal within this many blocks of the owner.</summary>
    private const float BaseGuestHostileRange = 24f;

    /// <summary>A due visit window produces a guest this often (like the scouts' roll: not every window brings one).</summary>
    private const double BaseGuestVisitChance = 0.7;

    /// <summary>The base's quiet spell between two visits, in in-game days (× 0.8–1.2, E10: about one per 1.5 days).</summary>
    private const double BaseGuestCooldownDays = 1.5;

    /// <summary>Spawn distances from the core: outside the zone and most yards, inside the 48-block NPC path box.</summary>
    private static readonly float[] BaseGuestSpawnRadii = { 30f, 36f, 42f };

    /// <summary>Bearings tried per radius, spread from the preferred one.</summary>
    private const int BaseGuestSpawnBearings = 8;

    /// <summary>A spawn spot stays this close (Chebyshev blocks) to the owner, so the route search's box reaches them.</summary>
    private const int BaseGuestPathReach = 44;

    /// <summary>… and at least this far, so the guest is seen walking in rather than popping up beside the player.</summary>
    private const float BaseGuestMinSpawnFromOwner = 12f;

    /// <summary>The guest greets the owner within this many blocks (with a line of sight) — its walk ends within
    /// √10 blocks of the owner's cell (<see cref="GuestGoalNearOwner"/>), so the arrival always lies inside.</summary>
    private const float BaseGuestGreetRange = 4.5f;

    /// <summary>Players within this many blocks of the guest receive its greeting (each in their language).</summary>
    private const float BaseGuestGreetHearRange = 8f;

    /// <summary>An approach that has not reached the owner after this long ends at the base's edge.</summary>
    private const double BaseGuestArriveTimeout = 90.0;

    /// <summary>The owner moved this far from the guest's goal → walk to where they are now …</summary>
    private const float BaseGuestRetargetDistance = 6f;

    /// <summary>… at most this often …</summary>
    private const double BaseGuestRetargetGap = 5.0;

    /// <summary>… and at most this many times per visit.</summary>
    private const int BaseGuestMaxRetargets = 3;

    /// <summary>Failed route searches to the owner before the guest gives up and waits outside.</summary>
    private const int BaseGuestPathFailuresBeforeOutside = 2;

    /// <summary>How long a guest waits after arriving (the merchant: <see cref="BaseGuestMerchantWaitSeconds"/>).</summary>
    private const double BaseGuestWaitSeconds = 120.0;

    /// <summary>How long the travelling merchant keeps its stall open.</summary>
    private const double BaseGuestMerchantWaitSeconds = 180.0;

    /// <summary>A player this close keeps the guest waiting …</summary>
    private const float BaseGuestHoldRange = 6f;

    /// <summary>… for this much longer each time (never past twice the normal wait).</summary>
    private const double BaseGuestHoldSeconds = 15.0;

    /// <summary>A guest that handed its present over goes home after this long.</summary>
    private const double BaseGuestAfterGiftSeconds = 30.0;

    /// <summary>A leaving guest vanishes at the end of its walk only when no player is this close …</summary>
    private const float BaseGuestDespawnUnseenRange = 32f;

    /// <summary>… and in any case after <see cref="BaseGuestHardDespawnSeconds"/> with no player this close.</summary>
    private const float BaseGuestHardDespawnRange = 24f;

    /// <summary>How long a guest may be on its way out before the hard despawn applies.</summary>
    private const double BaseGuestHardDespawnSeconds = 120.0;

    /// <summary>A leaving guest that is still watched at the end of its walk walks on this far.</summary>
    private const float BaseGuestLeaveStep = 8f;

    /// <summary>A guest whose world was not ticked for this long (nobody there) is gone when it ticks again.</summary>
    private const double BaseGuestFrozenSeconds = 20.0;

    /// <summary>The travelling merchant's barter reach (blocks), like a landed trader's pilot.</summary>
    private const float BaseGuestMerchantReach = 4f;

    /// <summary>A villager is borrowed for a visit only while no player is this close to it (nobody sees them vanish).</summary>
    private const float BaseGuestSourceUnseenRange = 24f;

    /// <summary>The job prefix that marks a guest NPC (<c>guest_neighbour</c>, <c>guest_merchant</c>, <c>guest_traveler</c>).</summary>
    internal const string GuestJobPrefix = "guest_";

    /// <summary>The memory-key prefix of a merchant or traveller guest — never saved (<see cref="RecordNpcInteraction"/>).</summary>
    internal const string GuestKeyPrefix = "guest:";

    /// <summary>The dialogue consequence family of guests: <c>guest:gift:&lt;item&gt;:&lt;n&gt;</c>.</summary>
    internal const string GuestConsequence = "guest";

    /// <summary>Most characters an AI re-wording of a guest's greeting may take.</summary>
    private const int GuestGreetMaxChars = 120;

    /// <summary>Uptime of the next possible visit per base id (RAM only).</summary>
    private readonly Dictionary<int, double> _nextGuestVisitAt = new();

    /// <summary>Per-world 1 Hz throttle of <see cref="TickBaseGuests"/>.</summary>
    private readonly Dictionary<string, double> _baseGuestTickAt = new(StringComparer.Ordinal);

    private readonly Random _guestRng = new(0x6E57);
    private int _guestVisits;

    /// <summary>Test seam: the next visit picks this kind (null = the weighted roll).</summary>
    private BaseGuestKind? _guestKindForTest;

    /// <summary>The guests on the active world.</summary>
    private List<BaseGuest> _baseGuests => _worlds.Active.BaseGuests;

    /// <summary>Whether an NPC job is a guest's.</summary>
    internal static bool IsGuestJob(string? job) => job is not null && job.StartsWith(GuestJobPrefix, StringComparison.Ordinal);

    /// <summary>The job/dialogue/data name of a guest kind (the traveller keeps the issue's spelling "traveler").</summary>
    private static string GuestKindKey(BaseGuestKind kind) => kind switch
    {
        BaseGuestKind.Neighbour => "neighbour",
        BaseGuestKind.Merchant => "merchant",
        _ => "traveler",
    };

    // ---------------- Tick ----------------

    /// <summary>The per-world 1 Hz pass (Guard-registered before the NPC routine): steps every guest's script, then
    /// looks for a base that is due a visit.</summary>
    private void TickBaseGuests(double dt)
    {
        _ = dt;
        if (_world is null)
        {
            return;
        }

        string world = _world.LocationId;
        if (_baseGuestTickAt.TryGetValue(world, out double due) && _uptime < due)
        {
            return;
        }

        _baseGuestTickAt[world] = _uptime + BaseGuestTickInterval;
        StepBaseGuests();
        TryScheduleBaseGuests();
    }

    /// <summary>Rolls one visit at most per tick, for a base whose owner is at home and whose window is open.</summary>
    private void TryScheduleBaseGuests()
    {
        if (!Rules.FriendlyVisitors || _bases.Count == 0)
        {
            return;
        }

        foreach (var b in _bases)
        {
            if (b.Planet != _world.LocationId)
            {
                continue;
            }

            if (!_nextGuestVisitAt.TryGetValue(b.Id, out double next))
            {
                // First sight of this base: arm the window instead of knocking the moment it was founded.
                _nextGuestVisitAt[b.Id] = _uptime + BaseGuestCooldown() * (0.4 + _guestRng.NextDouble() * 0.4);
                continue;
            }

            if (_uptime < next || _baseGuests.Any(g => g.BaseId == b.Id) || GuestHost(b) is not { } owner)
            {
                continue; // not yet, a guest is already there, or nobody is home — the window stays armed
            }

            _nextGuestVisitAt[b.Id] = _uptime + BaseGuestCooldown() * (0.8 + _guestRng.NextDouble() * 0.4);
            if (_guestRng.NextDouble() >= BaseGuestVisitChance)
            {
                continue;
            }

            if (PickGuestKind(b) is { } kind)
            {
                SpawnBaseGuest(b, owner, kind);
            }

            return; // one visit per tick
        }
    }

    /// <summary>The quiet spell between two visits of one base: 1.5 in-game days of the active world.</summary>
    private double BaseGuestCooldown() => BaseGuestCooldownDays * Math.Max(60.0, _dayLength);

    /// <summary>The base owner if a guest may come now (see the class summary): on this body, on foot, near the core, in
    /// daylight at the base, nothing hostile near them and no scouts watching. Null otherwise.</summary>
    private PlayerSession? GuestHost(ServerBase b)
    {
        if (FindSessionByPlayerId(b.OwnerId) is not { } owner || !GuestOwnerOnFoot(owner))
        {
            return null;
        }

        var core = BaseCorePos(b);
        if (WrapDistSq(owner.State.Position, core) > BaseGuestOwnerReach * BaseGuestOwnerReach)
        {
            return null;
        }

        double day = LocalDayFraction(core);
        if (day < BaseGuestDayStart || day > BaseGuestDayEnd)
        {
            return null;
        }

        return GuestHostileNear(owner.State.Position) || ScoutsAlreadyAt(b.Id) ? null : owner;
    }

    /// <summary>The owner is joined, on the active world, on foot — not aboard, in space or observing.</summary>
    private bool GuestOwnerOnFoot(PlayerSession? owner)
        => owner is { Joined: true, Spectating: false } && owner.CurrentLocationId == _world.LocationId
           && !owner.State.AboardShip && !InSpace(owner.State.PlayerId);

    /// <summary>A machine, a bandit that is not leaving, or a hostile animal within <see cref="BaseGuestHostileRange"/>.</summary>
    private bool GuestHostileNear(Vector3f at)
    {
        double r2 = BaseGuestHostileRange * BaseGuestHostileRange;
        return _planetEnemies.Any(e => WrapDistSq(e.Position, at) <= r2)
               || _bandits.Any(b => b.BanditPhase != BanditPhase.Leaving && WrapDistSq(b.Position, at) <= r2)
               || _creatures.Any(c => c.Hostile && !c.IsCompanion && WrapDistSq(c.Position, at) <= r2);
    }

    /// <summary>The weighted guest roll: a neighbour 50 (only with a villager to borrow), a merchant 30 (none while a
    /// trader ship is parked on this body), a traveller 20.</summary>
    private BaseGuestKind? PickGuestKind(ServerBase b)
    {
        if (_guestKindForTest is { } forced)
        {
            return forced;
        }

        int neighbour = PickVisitingNeighbour(b).Source is not null ? 50 : 0;
        int merchant = _landedTraders.ContainsKey(_world.LocationId) ? 0 : 30;
        const int traveller = 20;
        int roll = _guestRng.Next(neighbour + merchant + traveller);
        return roll < neighbour ? BaseGuestKind.Neighbour
            : roll < neighbour + merchant ? BaseGuestKind.Merchant
            : BaseGuestKind.Traveller;
    }

    // ---------------- Spawn ----------------

    /// <summary>Puts a guest of <paramref name="kind"/> outside the base and sends it walking to the owner. Null when no
    /// spot fits (nothing appears inside walls, water or lava — the visit is simply skipped) or no villager can come.</summary>
    private BaseGuest? SpawnBaseGuest(ServerBase b, PlayerSession owner, BaseGuestKind kind)
    {
        ServerNpc? source = null;
        SettlementInstance? village = null;
        if (kind == BaseGuestKind.Neighbour)
        {
            (source, village) = PickVisitingNeighbour(b, owner);
            if (source is null || village is null)
            {
                return null;
            }
        }

        var core = BaseCorePos(b);
        var nearestPlace = kind == BaseGuestKind.Traveller ? NearestSettlementTo(core) : village;
        double bearing = village is not null ? BearingFrom(core, SettlementCenter(village))
            : _guestRng.NextDouble() * Math.PI * 2.0;
        if (!TryFindGuestSpawn(b, owner, bearing, out var spawn))
        {
            _log.Info($"Base guests: no open ground around base #{b.Id} — the visit is skipped.");
            return null;
        }

        // #2688: a well-mixed seed — consecutive visits (the counter) seeded neighbouring Randoms whose first draws marched in step.
        var rng = new Random(StableHashing.Seed32("guest:" + b.Id.ToString(CultureInfo.InvariantCulture) + ":"
            + (_guestVisits++).ToString(CultureInfo.InvariantCulture), _meta.Seed));
        ServerNpc npc;
        switch (kind)
        {
            case BaseGuestKind.Neighbour:
                npc = MakeNpc(source!.Role, source.Theme, source.IsRobot, spawn, rng);
                npc.Name = source.Name;
                npc.NameKey = source.NameKey;
                npc.Size = source.Size;
                npc.SkinRgb = source.SkinRgb;
                npc.OutfitRgb = source.OutfitRgb;
                npc.LegsRgb = source.LegsRgb;
                npc.Look = source.Look;
                npc.Settlement = source.Settlement;
                npc.PersonKey = source.PersonKey; // the same person: friendship, stage and roster are theirs
                npc.FaceSeed = source.FaceSeed != 0 ? source.FaceSeed : ClientFaceSeed(source.Id, source.Name);
                break;
            case BaseGuestKind.Merchant:
                // #2630: "A bit of everything!" — the mixed stock: one offer from each of several trades, new every day
                // (MarketStock.MixedOffers, the same pick on the client's vendor screen). Dressed like a trader.
                npc = MakeNpc("vendor", "traders", robotic: false, spawn, rng);
                npc.Theme = MarketStock.MixedTheme;
                npc.NameKey = "npc.role.travelling_merchant";
                npc.GuestKey = GuestKeyPrefix + npc.Name;
                break;
            default:
                npc = MakeNpc("settler", "settlers", robotic: false, spawn, rng);
                npc.NameKey = "npc.role.traveller";
                npc.GuestKey = GuestKeyPrefix + npc.Name;
                break;
        }

        npc.Job = GuestJobPrefix + GuestKindKey(kind);
        npc.RoutineEnabled = false; // the visit script owns its goals
        npc.Leash = 0.6f;
        npc.ActivityKey = "npc.activity.visiting";
        _npcs.Add(npc);

        var guest = new BaseGuest
        {
            NpcId = npc.Id,
            BaseId = b.Id,
            OwnerId = owner.State.PlayerId,
            Kind = kind,
            Phase = BaseGuestPhase.Arriving,
            PhaseSince = _uptime,
            LastStepAt = _uptime,
            Place = nearestPlace?.Name ?? string.Empty,
            LeaveBearing = kind == BaseGuestKind.Traveller ? TravellerBearing(core, nearestPlace) : bearing,
        };

        if (source is not null)
        {
            // The villager walked over: they are not in the village meanwhile (hidden, frozen, no routine).
            guest.SourceNpcId = source.Id;
            guest.SourceRoutine = source.RoutineEnabled;
            source.VisitingAway = true;
            source.RoutineEnabled = false;
            source.Goal = null;
            source.Path = null;
        }

        _baseGuests.Add(guest);
        SetGuestGoalNearOwner(guest, npc, owner, b);
        BroadcastNpcs();
        if (source is not null)
        {
            SendStandingsToWorld(); // #2617: the villager's standing badge and wish marker move onto the guest
        }

        Send(owner, new ServerMessage { Text = "@srv.guest.arriving:" + npc.Name });
        PrefetchGuestLines(guest, npc, owner);
        _log.Info($"Base guests: {GuestKindKey(kind)} '{npc.Name}' is walking to base #{b.Id} (owner '{owner.State.Name}').");
        return guest;
    }

    /// <summary>The villager a neighbour visit borrows (#2602): of the nearest inhabited settlement that has one, a
    /// settler (no post of their own beyond garden or workbench) who is awake, standing, not chatting, not in a player's
    /// dialogue, not already away — and whom no player is watching, so nobody sees them vanish from the village. With
    /// <paramref name="owner"/> given, a villager who has a wish for the owner right now comes first (the visit then
    /// brings the wish along, <c>guest:wish</c>).</summary>
    private (ServerNpc? Source, SettlementInstance? Village) PickVisitingNeighbour(ServerBase b, PlayerSession? owner = null)
    {
        var core = BaseCorePos(b);
        SettlementInstance? bestVillage = null;
        List<ServerNpc>? bestCandidates = null;
        double bestSq = double.MaxValue;
        foreach (var s in _settlements)
        {
            if (s.Ruined || string.IsNullOrEmpty(s.Name))
            {
                continue;
            }

            double d = WrapDistSq(core, SettlementCenter(s));
            if (d >= bestSq)
            {
                continue;
            }

            var candidates = _npcs.Where(n => n.Settlement == s.Name && MayVisitAsNeighbour(n)).ToList();
            if (candidates.Count == 0)
            {
                continue;
            }

            bestSq = d;
            bestVillage = s;
            bestCandidates = candidates;
        }

        if (bestCandidates is null)
        {
            return (null, null);
        }

        if (owner is not null && bestCandidates.Where(n => GuestWishPlan(owner, n) is not null).ToList() is { Count: > 0 } wishing)
        {
            bestCandidates = wishing;
        }

        return (bestCandidates[_guestRng.Next(bestCandidates.Count)], bestVillage);
    }

    /// <summary>The wish (#2601) a villager has for the player right now — an open offer, not yet taken — or null.</summary>
    private WishLanePlan? GuestWishPlan(PlayerSession session, ServerNpc villager)
        => WishPlansAround(session, villager, playerThere: false, out _)?
            .FirstOrDefault(pl => pl.Held is null && pl.Def is not null && pl.Template is not null && pl.GiverKey == villager.PersonKey);

    /// <summary>See <see cref="PickVisitingNeighbour"/>. Never someone celebrating at a running village festival (#2604) —
    /// they are in the ring around the square.</summary>
    private bool MayVisitAsNeighbour(ServerNpc n)
        => n.BaseId == 0 && n.CharacterId.Length == 0 && n.PersonKey.Length > 0 && n.Role == "settler"
           && n.Job is "" or "gardener" or "craftsman"
           && !n.VisitingAway && n.RoutineEnabled && n.Pose == 0 && n.Goal is null && n.TalkWith is null
           && (_festivalRuns.Count == 0 || FestivalPlaceOf(n) is null)
           && !NpcInPlayerDialog(n) && !AnyPlayerWithin(n.Pos, BaseGuestSourceUnseenRange);

    /// <summary>The face seed the client derives for an NPC without a fixed one (<c>NpcView</c>: id × 486187739 ^ the
    /// name's hash) — a neighbour guest carries its villager's, so it is the same face and voice at the base.</summary>
    internal static int ClientFaceSeed(int npcId, string name)
    {
        int h = 17;
        foreach (char c in name ?? string.Empty)
        {
            h = unchecked((h * 31) + c);
        }

        return unchecked((npcId * 486187739) ^ h);
    }

    /// <summary>A spawn spot (#2602): on the rings <see cref="BaseGuestSpawnRadii"/> around the core, bearings spread from
    /// <paramref name="bearing"/>; real standable ground that is not inside walls, a sealed room, water or lava
    /// (<see cref="BanditColumnRejected"/>), within the route search's reach of the owner and not right beside them.</summary>
    private bool TryFindGuestSpawn(ServerBase b, PlayerSession owner, double bearing, out Vector3f spawn)
    {
        var o = owner.State.Position;
        foreach (float radius in BaseGuestSpawnRadii)
        {
            for (int k = 0; k < BaseGuestSpawnBearings; k++)
            {
                int step = (k + 1) / 2 * (k % 2 == 1 ? 1 : -1); // 0, +1, -1, +2, -2 … — nearest the preferred bearing first
                double a = bearing + step * (Math.PI * 2.0 / BaseGuestSpawnBearings);
                int x = (int)Math.Round(b.Cell.X + Math.Cos(a) * radius);
                int z = (int)Math.Round(b.Cell.Z + Math.Sin(a) * radius);
                if (!TryGroundFeetYAt(x, z, b.Cell.Y, CreatureBodyMinHeight, CreatureWideGroundScan, out int feet)
                    || StandableSpot(x, feet, z) is not { } spot || BanditColumnRejected(x, feet, z))
                {
                    continue;
                }

                var u = Unwrapped(o, spot);
                float dx = Math.Abs(u.X - o.X), dz = Math.Abs(u.Z - o.Z);
                if (Math.Max(dx, dz) > BaseGuestPathReach || Math.Abs(u.Y - o.Y) > 7f
                    || (dx * dx) + (dz * dz) < BaseGuestMinSpawnFromOwner * BaseGuestMinSpawnFromOwner)
                {
                    continue;
                }

                spawn = spot;
                return true;
            }
        }

        spawn = default;
        return false;
    }

    /// <summary>Asks for the AI re-wordings at spawn (hosted worlds): the greeting and the dialogue opening — the walk-in
    /// covers the latency. A no-op with AI off.</summary>
    private void PrefetchGuestLines(BaseGuest g, ServerNpc npc, PlayerSession owner)
    {
        if (!AiLinesEnabled)
        {
            return;
        }

        GuestGreetAiLines(g, npc, owner, GuestGreetAuthored(g, owner.Locale));
        WarmDialogOpening(owner, npc, NpcKeyForNpc(owner, npc)); // guest_offer (DialogOpeningAiLines)
    }

    // ---------------- Script ----------------

    /// <summary>One step of every guest's visit on the active world.</summary>
    private void StepBaseGuests()
    {
        for (int i = _baseGuests.Count - 1; i >= 0; i--)
        {
            var g = _baseGuests[i];
            var npc = _npcs.FirstOrDefault(n => n.Id == g.NpcId && IsGuestJob(n.Job));
            if (npc is null)
            {
                EndBaseGuest(g, null);
                continue;
            }

            bool frozen = _uptime - g.LastStepAt > BaseGuestFrozenSeconds; // nobody was here — the visit is over
            g.LastStepAt = _uptime;
            if (frozen)
            {
                EndBaseGuest(g, npc);
                continue;
            }

            if (g.Phase == BaseGuestPhase.Leaving)
            {
                StepGuestLeaving(g, npc); // on its way home, whatever the owner does now
                continue;
            }

            var owner = FindSessionByPlayerId(g.OwnerId);
            if (!GuestOwnerOnFoot(owner))
            {
                // #2713: the owner went aboard or left the world — the guest walks home and vanishes only out of sight (or
                // after the usual timeout), instead of popping out of the yard in front of the ship's windows.
                BeginGuestLeaving(g, npc);
                continue;
            }

            var b = _bases.FirstOrDefault(x => x.Id == g.BaseId && x.Planet == _world.LocationId);
            if (b is null || !Rules.FriendlyVisitors)
            {
                BeginGuestLeaving(g, npc); // the base was dissolved or the world switched visitors off
                continue;
            }

            if (g.Phase == BaseGuestPhase.Arriving)
            {
                StepGuestArriving(g, npc, owner!, b);
            }
            else
            {
                StepGuestWaiting(g, npc, owner!);
            }
        }
    }

    /// <summary>#2713: whether a player aboard their ship still keeps the NPCs moving — while a guest of theirs walks home,
    /// so they see it go from the ship (the NPC tick otherwise freezes while nobody is out on the surface).</summary>
    private bool WatchesGuestLeave(PlayerSession s)
    {
        if (_baseGuests.Count == 0)
        {
            return false;
        }

        string me = s.State.PlayerId;
        foreach (var g in _baseGuests)
        {
            if (g.Phase == BaseGuestPhase.Leaving && g.OwnerId == me)
            {
                return true;
            }
        }

        return false;
    }

    private void StepGuestArriving(BaseGuest g, ServerNpc npc, PlayerSession owner, ServerBase b)
    {
        if (OwnerInGreetReach(npc, owner))
        {
            GreetBaseGuest(g, npc, owner);
            StartGuestWait(g, npc);
            return;
        }

        if (g.Outside)
        {
            // Walking to (or standing at) the edge: wait there once arrived — or where it stands when even that fails.
            if (npc.Goal is null || npc.PathFailures >= BaseGuestPathFailuresBeforeOutside)
            {
                StopGuestWalking(npc);
                StartGuestWait(g, npc);
            }

            return;
        }

        var ownerCell = owner.State.Position.ToBlock();
        if (npc.PathFailures >= BaseGuestPathFailuresBeforeOutside || InSealedBaseRoom(ownerCell)
            || _uptime - g.PhaseSince > BaseGuestArriveTimeout)
        {
            GoWaitOutside(g, npc, owner, b);
            return;
        }

        bool ownerMoved = npc.Goal is not { } goal
            || WrapDistSq(goal, owner.State.Position) > BaseGuestRetargetDistance * BaseGuestRetargetDistance;
        if (!ownerMoved)
        {
            return; // on the way
        }

        if (g.Retargets < BaseGuestMaxRetargets && _uptime >= g.RetargetAt)
        {
            g.Retargets++;
            SetGuestGoalNearOwner(g, npc, owner, b);
        }
        else if (npc.Goal is null)
        {
            StartGuestWait(g, npc); // arrived where the owner was; greets when they come back within reach
        }
    }

    private void StepGuestWaiting(BaseGuest g, ServerNpc npc, PlayerSession owner)
    {
        if (!g.Greeted && OwnerInGreetReach(npc, owner))
        {
            GreetBaseGuest(g, npc, owner);
        }

        if (AnyPlayerWithin(npc.Pos, BaseGuestHoldRange))
        {
            g.WaitUntil = Math.Max(g.WaitUntil, _uptime + BaseGuestHoldSeconds); // someone is talking or trading
        }

        double end = Math.Min(g.WaitUntil, g.WaitHardEnd);
        if (g.LeaveSoonAt > 0)
        {
            end = Math.Min(end, g.LeaveSoonAt);
        }

        if (_uptime >= end)
        {
            BeginGuestLeaving(g, npc);
        }
    }

    private void StepGuestLeaving(BaseGuest g, ServerNpc npc)
    {
        if (_uptime - g.PhaseSince > BaseGuestHardDespawnSeconds && !AnyPlayerWithin(npc.Pos, BaseGuestHardDespawnRange))
        {
            EndBaseGuest(g, npc);
            return;
        }

        if (npc.Goal is not null)
        {
            return; // still on the way out
        }

        if (!AnyPlayerWithin(npc.Pos, BaseGuestDespawnUnseenRange))
        {
            EndBaseGuest(g, npc); // nobody sees it go
            return;
        }

        // Still watched at the end of the walk: a few more steps outward (or stand until nobody looks).
        if (GuestSpotAlong(npc.Pos, g.LeaveBearing, BaseGuestLeaveStep) is { } further)
        {
            SetNpcGoal(npc, further, NpcArrival.None, 0.6f);
        }
    }

    /// <summary>The guest arrived at the owner (or at its waiting spot): it waits — the merchant a little longer.</summary>
    private void StartGuestWait(BaseGuest g, ServerNpc npc)
    {
        _ = npc;
        double wait = g.Kind == BaseGuestKind.Merchant ? BaseGuestMerchantWaitSeconds : BaseGuestWaitSeconds;
        g.Phase = BaseGuestPhase.Waiting;
        g.PhaseSince = _uptime;
        g.WaitUntil = _uptime + wait;
        g.WaitHardEnd = _uptime + (2 * wait);
    }

    /// <summary>Time to go: back out on the guest's bearing (its village, its destination or where it came from). #2713: a
    /// neighbour's owner — on this world, on foot or aboard — reads "{name} is walking home to {village}.", so a wish
    /// taken from them has a place to go to.</summary>
    private void BeginGuestLeaving(BaseGuest g, ServerNpc npc)
    {
        g.Phase = BaseGuestPhase.Leaving;
        g.PhaseSince = _uptime;
        npc.AttendUntil = 0;
        if (GuestLeaveSpot(g, npc) is { } spot)
        {
            SetNpcGoal(npc, spot, NpcArrival.None, 0.6f);
        }
        else
        {
            StopGuestWalking(npc);
        }

        if (g.Kind == BaseGuestKind.Neighbour && g.Place.Length > 0
            && FindSessionByPlayerId(g.OwnerId) is { Joined: true, Spectating: false } owner
            && owner.CurrentLocationId == _world.LocationId)
        {
            Send(owner, new ServerMessage
            {
                Text = Localize(owner.Locale, "srv.guest.walking_home").Replace("{name}", npc.Name).Replace("{village}", g.Place),
            });
        }
    }

    /// <summary>#2713: a neighbour's "I'm heading home to {village} soon — come and see me there!" after its present or a
    /// wish it brought along (the line starts with a blank line); "" for any other guest or without a village.</summary>
    private string GuestGoingHomeLine(BaseGuest g, string locale)
        => g.Kind == BaseGuestKind.Neighbour && g.Place.Length > 0
            ? "\n\n" + Localize(locale, "dlg.guest.going_home").Replace("{village}", g.Place)
            : string.Empty;

    /// <summary>Takes a guest out of the world and gives a borrowed villager back to its village.</summary>
    private void EndBaseGuest(BaseGuest g, ServerNpc? npc)
    {
        if (npc is not null)
        {
            _npcs.Remove(npc);
        }

        bool villagerBack = false;
        if (g.SourceNpcId != 0 && _npcs.FirstOrDefault(n => n.Id == g.SourceNpcId) is { VisitingAway: true } source)
        {
            source.VisitingAway = false;
            source.RoutineEnabled = g.SourceRoutine;
            source.PhaseCheckedAt = double.NegativeInfinity; // back home: think about the time of day again
            villagerBack = true;
        }

        _baseGuests.Remove(g);
        BroadcastNpcs();
        if (villagerBack)
        {
            SendStandingsToWorld(); // #2617: the wish marker goes back home with the villager
        }
    }

    /// <summary>#2617: re-sends the standings (with the wish markers) to every player on the active world — a neighbour's
    /// visit starting or ending moves a villager's badge and marker between its two ids.</summary>
    private void SendStandingsToWorld()
    {
        foreach (var s in JoinedInActiveWorld())
        {
            if (!s.Spectating)
            {
                SendNpcStandings(s);
            }
        }
    }

    /// <summary>The guest's greeting (#2602): kind <see cref="NpcGreetingKinds.Guest"/> with a wave, to every player within
    /// <see cref="BaseGuestGreetHearRange"/>, each in their own language with their own name. A neighbour's visit counts
    /// as today's hello with that villager (and its +1 for an acquaintance).</summary>
    private void GreetBaseGuest(BaseGuest g, ServerNpc npc, PlayerSession owner)
    {
        int id = NextGreetingId();
        double hearSq = BaseGuestGreetHearRange * BaseGuestGreetHearRange;
        foreach (var s in JoinedInActiveWorld())
        {
            if (s.Spectating || InSpace(s.State.PlayerId) || (s != owner && WrapDistSq(s.State.Position, npc.Pos) > hearSq))
            {
                continue;
            }

            Send(s, new NpcGreeting
            {
                NpcId = npc.Id,
                Name = npc.Name,
                Role = npc.Role,
                Text = GuestGreetText(g, npc, s),
                GreetingId = id,
                Kind = NpcGreetingKinds.Guest,
                Gesture = NpcGestures.Wave,
            });
            MarkPassingGreeted(npc, s.State.PlayerId);
        }

        npc.AttendUntil = _uptime + 2.5;
        npc.AttendPos = owner.State.Position;
        g.Greeted = true;
        if (g.Kind == BaseGuestKind.Neighbour && npc.PersonKey.Length > 0)
        {
            TryHelloStanding(owner, npc.PersonKey);
            StampNpcSeen(owner.State, npc.PersonKey);
        }
    }

    /// <summary>The greeting a player hears: a cached AI re-wording (hosted worlds) or the authored line, with the
    /// player's name and the place filled in.</summary>
    private string GuestGreetText(BaseGuest g, ServerNpc npc, PlayerSession s)
    {
        string raw = GuestGreetAuthored(g, s.Locale);
        var values = GuestLineValues(g, npc);
        string authored = FillAiLine(raw, s, values) ?? raw;
        var ai = GuestGreetAiLines(g, npc, s, raw);
        return ai is { Length: > 0 } && FillAiLine(ai[0], s, values) is { Length: > 0 } text ? text : authored;
    }

    /// <summary>The authored greeting of a guest kind in a language (tokens unfilled).</summary>
    private string GuestGreetAuthored(BaseGuest g, string locale) => Localize(locale, "guest.greet." + GuestKindKey(g.Kind));

    /// <summary>The token values of a guest's lines: <c>{place}</c> = the neighbour's village (else the place a traveller
    /// asks about, else the base), <c>{npc}</c> = the guest.</summary>
    private Dictionary<string, string> GuestLineValues(BaseGuest g, ServerNpc npc)
    {
        string place = g.Place;
        if (place.Length == 0 && _bases.FirstOrDefault(b => b.Id == g.BaseId) is { } home)
        {
            place = home.Name;
        }

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [AiLineTokens.Place] = place,
            [AiLineTokens.Npc] = npc.Name,
        };
    }

    /// <summary>The cached <c>guest_greet</c> variant for this guest's voice (null on a miss, which starts a request).
    /// Keyed by guest kind, persona, language and stage — never by player.</summary>
    private string[]? GuestGreetAiLines(BaseGuest g, ServerNpc npc, PlayerSession s, string authored)
    {
        if (!AiLinesEnabled)
        {
            return null;
        }

        string npcKey = NpcKeyForNpc(s, npc);
        string stage = g.Kind == BaseGuestKind.Neighbour ? RelationshipTier(RelationshipValue(s.State, npcKey)) : "stranger";
        string cacheKey = AiLineCacheKey(NpcLinePurposes.GuestGreet, GuestKindKey(g.Kind),
            PersonaIndexFor(npcKey, npc.IsRobot, npc.Role), s.Locale, stage);
        var anchors = new List<string> { authored };
        return AiLinesFor(cacheKey, AiLinePriority.Personal, () => new NpcLinesRequest
        {
            Purpose = NpcLinePurposes.GuestGreet,
            Language = s.Locale,
            Place = g.Place,
            Theme = npc.Theme,
            Stage = stage,
            Situation = g.Kind switch
            {
                BaseGuestKind.Neighbour => "a villager walked over from their village to visit the player's home base",
                BaseGuestKind.Merchant => "a travelling merchant arrives at the player's home base with goods to trade",
                _ => "a lost traveller arrives at the player's home base and wants to ask the way",
            },
            Speakers =
            {
                new NpcLinesSpeaker
                {
                    Role = npc.Role,
                    Job = npc.Job,
                    IsRobot = npc.IsRobot,
                    Persona = PersonaFor(npcKey, npc.Theme, npc.IsRobot, npc.Role),
                },
            },
            Tokens = { AiLineTokens.Player, AiLineTokens.Place },
            Authored = anchors,
            LineCount = 1,
            MaxChars = GuestGreetMaxChars,
        }, requiredTokens: AiLineText.TokensIn(anchors));
    }

    // ---------------- Goals ----------------

    /// <summary>Sends the guest to a standable spot within three blocks of the owner (the one nearest the guest); none →
    /// it waits outside instead.</summary>
    private void SetGuestGoalNearOwner(BaseGuest g, ServerNpc npc, PlayerSession owner, ServerBase b)
    {
        g.RetargetAt = _uptime + BaseGuestRetargetGap;
        if (GuestGoalNearOwner(npc, owner) is { } goal)
        {
            SetNpcGoal(npc, goal, NpcArrival.None, 0.6f);
        }
        else
        {
            GoWaitOutside(g, npc, owner, b);
        }
    }

    /// <summary>The standable spot on the rings 2–3 (then 1) around the owner nearest the guest, or null. Ring corners
    /// further than √10 blocks are skipped, so the guest always ends within greeting reach.</summary>
    private Vector3f? GuestGoalNearOwner(ServerNpc npc, PlayerSession owner)
    {
        var oc = owner.State.Position.ToBlock();
        foreach (var (rMin, rMax) in new[] { (2, 3), (1, 1) })
        {
            Vector3f? best = null;
            double bestSq = double.MaxValue;
            for (int r = rMin; r <= rMax; r++)
                for (int dx = -r; dx <= r; dx++)
                    for (int dz = -r; dz <= r; dz++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != r || (dx * dx) + (dz * dz) > 10)
                        {
                            continue;
                        }

                        for (int y = oc.Y + 1; y >= oc.Y - 2; y--)
                        {
                            if (StandableSpot(oc.X + dx, y, oc.Z + dz) is { } spot)
                            {
                                double d = WrapDistSq(npc.Pos, spot);
                                if (d < bestSq)
                                {
                                    bestSq = d;
                                    best = spot;
                                }

                                break;
                            }
                        }
                    }

            if (best is not null)
            {
                return best;
            }
        }

        return null;
    }

    /// <summary>The owner cannot be reached (#2602): the guest walks to the first open spot outside the base on the line
    /// from the owner towards itself — outside the zone, every walled yard and sealed room — and the owner is told it is
    /// waiting outside. No such spot (or no route to it either) → it waits where it stands.</summary>
    private void GoWaitOutside(BaseGuest g, ServerNpc npc, PlayerSession owner, ServerBase b)
    {
        if (g.Outside)
        {
            return;
        }

        g.Outside = true;
        if (GuestOutsideSpot(npc, owner, b) is { } spot)
        {
            SetNpcGoal(npc, spot, NpcArrival.None, 0.6f);
        }
        else
        {
            StopGuestWalking(npc);
        }

        Send(owner, new ServerMessage { Text = "@srv.guest.waiting:" + npc.Name });
    }

    /// <summary>See <see cref="GoWaitOutside"/>.</summary>
    private Vector3f? GuestOutsideSpot(ServerNpc npc, PlayerSession owner, ServerBase b)
    {
        var o = owner.State.Position;
        var to = Unwrapped(o, npc.Pos);
        float dx = to.X - o.X, dz = to.Z - o.Z;
        float len = (float)Math.Sqrt((dx * dx) + (dz * dz));
        if (len < 1f)
        {
            return null;
        }

        for (float d = 3f; d < len; d += 1.5f)
        {
            int x = (int)Math.Floor(o.X + (dx / len * d));
            int z = (int)Math.Floor(o.Z + (dz / len * d));
            int refY = (int)Math.Floor(o.Y + ((to.Y - o.Y) * d / len));
            for (int y = refY + 1; y >= refY - 3; y--) // one step up at most: never the top of a wall
            {
                if (StandableSpot(x, y, z) is not { } spot)
                {
                    continue;
                }

                var cell = new Vector3i(x, y, z);
                if (!WithinBaseZone(b.Cell, cell) && !InWalledBaseArea(cell) && !InSealedBaseRoom(cell))
                {
                    return spot;
                }

                break;
            }
        }

        return null;
    }

    /// <summary>Where a leaving guest walks: on its bearing, as far out as a standable spot within the route search's
    /// reach allows; null when nothing fits.</summary>
    private Vector3f? GuestLeaveSpot(BaseGuest g, ServerNpc npc)
    {
        foreach (float d in new[] { 36f, 30f, 24f, 18f, 12f })
        {
            if (GuestSpotAlong(npc.Pos, g.LeaveBearing, d) is { } spot)
            {
                return spot;
            }
        }

        return null;
    }

    /// <summary>Open standable ground <paramref name="distance"/> blocks from <paramref name="from"/> on a bearing.</summary>
    private Vector3f? GuestSpotAlong(Vector3f from, double bearing, float distance)
    {
        int x = (int)Math.Round(from.X + (Math.Cos(bearing) * distance));
        int z = (int)Math.Round(from.Z + (Math.Sin(bearing) * distance));
        int refY = (int)Math.Floor(from.Y);
        if (!TryGroundFeetYAt(x, z, refY, CreatureBodyMinHeight, CreatureWideGroundScan, out int feet)
            || Math.Abs(feet - refY) > 7 || BanditColumnRejected(x, feet, z))
        {
            return null;
        }

        return StandableSpot(x, feet, z);
    }

    /// <summary>Stops a guest where it stands (it strolls a little around the spot).</summary>
    private static void StopGuestWalking(ServerNpc npc)
    {
        npc.Goal = null;
        npc.Path = null;
        npc.PathIndex = 0;
        npc.Home = npc.Pos;
        npc.Leash = 0.6f;
    }

    // ---------------- Helpers ----------------

    /// <summary>Whether the owner stands within <see cref="BaseGuestGreetRange"/> of the guest with nothing between them.</summary>
    private bool OwnerInGreetReach(ServerNpc npc, PlayerSession owner)
    {
        var o = owner.State.Position;
        return WrapDistSq(npc.Pos, o) <= BaseGuestGreetRange * BaseGuestGreetRange && Math.Abs(npc.Pos.Y - o.Y) <= 2.5f
               && HasLineOfSight(npc.Pos, o);
    }

    /// <summary>Any joined player on the active world (on foot or aboard, not observing, not in space) within range.</summary>
    private bool AnyPlayerWithin(Vector3f at, float range)
    {
        double r2 = (double)range * range;
        foreach (var s in JoinedInActiveWorld())
        {
            if (!s.Spectating && !InSpace(s.State.PlayerId) && WrapDistSq(s.State.Position, at) <= r2)
            {
                return true;
            }
        }

        return false;
    }

    private static Vector3f BaseCorePos(ServerBase b) => new(b.Cell.X + 0.5f, b.Cell.Y, b.Cell.Z + 0.5f);

    private static Vector3f SettlementCenter(SettlementInstance s)
        => new((s.Min.X + s.Max.X) * 0.5f, (s.Min.Y + s.Max.Y) * 0.5f, (s.Min.Z + s.Max.Z) * 0.5f);

    /// <summary>Bearing (radians, the short way round the east–west seam) from <paramref name="from"/> to <paramref name="to"/>.</summary>
    private double BearingFrom(Vector3f from, Vector3f to)
    {
        var u = Unwrapped(from, to);
        return Math.Atan2(u.Z - from.Z, u.X - from.X);
    }

    /// <summary>The nearest inhabited settlement to a point, or null.</summary>
    private SettlementInstance? NearestSettlementTo(Vector3f at)
    {
        SettlementInstance? best = null;
        double bestSq = double.MaxValue;
        foreach (var s in _settlements)
        {
            if (s.Ruined || string.IsNullOrEmpty(s.Name))
            {
                continue;
            }

            double d = WrapDistSq(at, SettlementCenter(s));
            if (d < bestSq)
            {
                bestSq = d;
                best = s;
            }
        }

        return best;
    }

    /// <summary>Where a lost traveller heads after asking: its village — or, with none on the world, the nearest landing
    /// pad; a random bearing when there is neither.</summary>
    private double TravellerBearing(Vector3f core, SettlementInstance? village)
    {
        if (village is not null)
        {
            return BearingFrom(core, SettlementCenter(village));
        }

        Vector3f? pad = null;
        double bestSq = double.MaxValue;
        foreach (var p in _landingPads)
        {
            var c = new Vector3f(p.CenterX + 0.5f, p.CenterY, p.CenterZ + 0.5f);
            double d = WrapDistSq(core, c);
            if (d < bestSq)
            {
                bestSq = d;
                pad = c;
            }
        }

        return pad is { } at ? BearingFrom(core, at) : _guestRng.NextDouble() * Math.PI * 2.0;
    }

    /// <summary>True if the served player stands at a travelling merchant guest (#2602) — enables the barter at the base.</summary>
    private bool NearBaseGuestMerchant(Shared.State.PlayerState player)
    {
        if (_world is null)
        {
            return false;
        }

        foreach (var npc in _npcs)
        {
            if (npc.Job == GuestJobPrefix + "merchant"
                && WrapDistSq(player.Position, npc.Pos) <= BaseGuestMerchantReach * BaseGuestMerchantReach)
            {
                return true;
            }
        }

        return false;
    }

    // ---------------- Dialogue ----------------

    /// <summary>A guest dialogue's consequence (#2602): <c>guest:gift:&lt;item&gt;:&lt;n&gt;</c> hands the present over once
    /// per guest and player (a second time the answer says so; a full bag keeps it for later) and sends the guest home
    /// soon after. Unknown parts are ignored, like every other consequence.</summary>
    private void ApplyGuestConsequence(PlayerSession session, ServerNpc npc, string[] parts)
    {
        var g = _baseGuests.FirstOrDefault(x => x.NpcId == npc.Id);
        if (g is null || parts.Length < 2)
        {
            return;
        }

        if (parts[1] == "gift" && parts.Length >= 4
            && int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int count) && count > 0
            && _content.GetItem(parts[2]) is { } item)
        {
            string pid = session.State.PlayerId;
            if (g.Gifted.Contains(pid))
            {
                _dialogResponseOverride = Localize(session.Locale, "dlg.guest.gift_given");
                return;
            }

            Serve(session);
            var bag = new MaterialPool(_content, session.State, _ship);
            if (!bag.CanFit(new[] { new ItemAmount(item.Key, count) }))
            {
                _dialogResponseOverride = Localize(session.Locale, "dlg.guest.full"); // the present waits for room
                return;
            }

            bag.Add(item.Key, count);
            SendInventory(session);
            g.Gifted.Add(pid);
            g.LeaveSoonAt = _uptime + BaseGuestAfterGiftSeconds;
            _dialogExtraText = "\n\n+" + count.ToString(CultureInfo.InvariantCulture) + " "
                + LocalizedName(session.Locale, item.NameKey, item.Key)
                + GuestGoingHomeLine(g, session.Locale); // #2713: where a neighbour is off to
            return;
        }

        if (parts[1] == "wish")
        {
            // The neighbour brought a wish (#2601): asked "can I help?", the villager tells it and it goes into the
            // player's mission log. None open → a kind "no, thanks". #2617: with everything already in the bag the
            // neighbour says so ("just talk to me again") and stays — the next talk hands it over right here; otherwise
            // it heads home soon and the wish is handed over in the village (or at its next visit) — #2713: and says so,
            // "I'm heading home to Testville soon — come and see me there!".
            if (g.SourceNpcId != 0 && _npcs.FirstOrDefault(n => n.Id == g.SourceNpcId) is { } source
                && GuestWishPlan(session, source) is { Template: { } template } plan
                && GrantWish(session, source, NpcKeyForNpc(session, source), answerWhenReady: true))
            {
                string readyLine = _dialogResponseOverride; // GrantWish's "you already have everything", "" when not
                _dialogResponseOverride = string.Empty;
                _dialogExtraText = "\n\n" + WishAskText(session, source, plan, requestAi: false)
                    + "\n\n" + WishFactLine(session.Locale, template, plan.Count)
                    + (readyLine.Length > 0 ? "\n\n" + readyLine : GuestGoingHomeLine(g, session.Locale));
                if (readyLine.Length == 0)
                {
                    g.LeaveSoonAt = _uptime + BaseGuestAfterGiftSeconds;
                }
            }
            else
            {
                _dialogResponseOverride = Localize(session.Locale, "dlg.guest_neighbour.r_nowish");
            }
        }
    }

    // ---------------- Test seams ----------------

    /// <summary>Test seam (#2602): spawns a guest of <paramref name="kind"/> ("neighbour", "merchant", "traveler") at a
    /// base right now — the visit gates (the rule, the owner at home on foot, daylight, no hostiles, no scouts, one guest
    /// per base) still apply, only the cooldown and the roll are skipped. Returns the guest's NPC id, or null.</summary>
    public int? SpawnBaseGuestForTest(int baseId, string kind)
    {
        var b = _bases.FirstOrDefault(x => x.Id == baseId && x.Planet == _world.LocationId);
        if (b is null || !Rules.FriendlyVisitors || _baseGuests.Any(g => g.BaseId == baseId) || GuestHost(b) is not { } owner)
        {
            return null;
        }

        var k = kind switch
        {
            "neighbour" => BaseGuestKind.Neighbour,
            "merchant" => BaseGuestKind.Merchant,
            _ => BaseGuestKind.Traveller,
        };
        return SpawnBaseGuest(b, owner, k)?.NpcId;
    }

    /// <summary>Test seam (#2602): one visit roll with every base's window open (the 70 % roll repeated until it lands or
    /// a gate says no), optionally forcing the kind. Returns the guests now on the world.</summary>
    public int TryBaseGuestVisitForTest(string? kind = null)
    {
        _guestKindForTest = kind switch
        {
            "neighbour" => BaseGuestKind.Neighbour,
            "merchant" => BaseGuestKind.Merchant,
            "traveler" => BaseGuestKind.Traveller,
            _ => null,
        };
        try
        {
            for (int i = 0; i < 64 && _baseGuests.Count == 0; i++)
            {
                foreach (var b in _bases)
                {
                    _nextGuestVisitAt[b.Id] = 0;
                }

                TryScheduleBaseGuests();
            }
        }
        finally
        {
            _guestKindForTest = null;
        }

        return _baseGuests.Count;
    }

    /// <summary>Test seam (#2602): steps the guests' script now (skips the 1 Hz throttle).</summary>
    public void TickBaseGuestsForTest() => StepBaseGuests();

    /// <summary>Test seam (#2713): whether this player keeps the NPCs moving even while aboard (a guest of theirs walks home).</summary>
    public bool KeepsNpcsMovingForTest(string playerId) => FindSessionByPlayerId(playerId) is { } s && WatchesGuestLeave(s);

    /// <summary>Test seam (#2602): the guests on the active world.</summary>
    public IReadOnlyList<(int NpcId, string Kind, string Phase, int SourceNpcId, bool Greeted, bool Outside)> BaseGuestsForTest
        => _baseGuests.Select(g => (g.NpcId, GuestKindKey(g.Kind), g.Phase.ToString(), g.SourceNpcId, g.Greeted, g.Outside)).ToList();

    /// <summary>Test seam (#2602): ends every waiting guest's wait now (they start leaving on the next step).</summary>
    public void EndBaseGuestWaitsForTest()
    {
        foreach (var g in _baseGuests)
        {
            g.WaitUntil = _uptime;
            g.WaitHardEnd = _uptime;
        }
    }

    /// <summary>Test seam (#2602): whether a villager is away visiting a base (hidden from the village).</summary>
    public bool NpcVisitingAwayForTest(int npcId) => _npcs.FirstOrDefault(n => n.Id == npcId)?.VisitingAway ?? false;

    /// <summary>Test seam (#2602): an NPC's person key ("" = none).</summary>
    public string NpcPersonKeyForTest(int npcId) => _npcs.FirstOrDefault(n => n.Id == npcId)?.PersonKey ?? string.Empty;

    /// <summary>Test seam (#2602): whether the travelling-merchant barter reaches the player.</summary>
    public bool NearBaseGuestMerchantForTest(Shared.State.PlayerState player) => NearBaseGuestMerchant(player);

    /// <summary>Test seam (#2602): the trade theme the player's barter would use right now ("" = no vendor in reach).</summary>
    public string VendorThemeAtForTest(Shared.State.PlayerState player) => VendorThemeAt(player);
}
