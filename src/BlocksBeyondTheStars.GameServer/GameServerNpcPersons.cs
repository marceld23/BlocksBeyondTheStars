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
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Friends with names (#2598) and "they remember when they last saw you" (#2595). Every villager is a PERSON with a
/// memory key of their own — <c>settle_&lt;hash&gt;#&lt;slot&gt;:&lt;role&gt;</c>, the slot being their bed or post cell
/// in the settlement layout — instead of all settlers (and all traders) sharing one role key. The classic vendor and
/// the quartermaster keep their role keys (board, market, chains and radio hang on them).
/// <para>The old shared standing becomes a FLOOR (<see cref="RelationshipValue"/>): a villager without an entry of
/// their own reads min(old role value, 39) — nobody drops to stranger, but "friend" is earned per person. On world
/// entry the player's old entries are migrated once per settlement: the villager the entry was named after (the one in
/// "People you know") inherits it in full, the rest of the old entry becomes "&lt;place&gt; · Villagers".</para>
/// <para>Gains are tuned so a child is "known" within one session and a friend after a few: talking to a person pays
/// every 10 min (role keys keep their hour), the first meeting +3, a passing hello +1 once per session up to 39, the
/// festival +6, a bandit camp cleared nearby +2. A friend hands over a small present at most every half hour of world
/// playtime (<c>data/village_life.json</c>).</para>
/// <para>Also the wall clock behind "welcome back": a seam (<see cref="WallUtcNow"/>), the per-NPC
/// <see cref="NpcRelationship.LastSeenUtc"/> stamp, and the helpers the greeting code asks.</para>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The highest standing the old shared role entry lends a villager (#2598): "known", never "friend".</summary>
    internal const int LegacyStandingFloorCap = 39;

    /// <summary>The standing a villager must have of their own to be a friend (the "trusted" tier).</summary>
    private const int FriendStanding = 40;

    /// <summary>Meeting a villager for the first time lifts the standing to at least this (#2598).</summary>
    private const int FirstMeetBonus = 3;

    /// <summary>A repeatable dialogue with a PERSON pays its relationship reward at most this often (#2598) — role
    /// keys keep <see cref="DialogRewardCooldownSeconds"/>.</summary>
    private const double PersonDialogRewardCooldownSeconds = 600.0;

    /// <summary>A villager friend hands over a present at most this often, in seconds of world playtime (#2598, E8).</summary>
    internal const double FriendGiftCooldownSeconds = 1800.0;

    /// <summary>Standing every festival attendee gains (#2598, C6) — for <see cref="AddStanding"/>.</summary>
    internal const int FestivalStandingBonus = 6;

    /// <summary>Standing the villagers who already know the player gain when a bandit camp near them is cleared (#2598).</summary>
    internal const int CampClearedStandingBonus = 2;

    /// <summary>A known NPC (or the player's own resident) who has not seen the player for this long greets them as
    /// "welcome back" (#2595, E3).</summary>
    internal const double NpcWelcomeBackHours = 18.0;

    /// <summary>The engine dialogue a friend's present is handed over in (#2598); its role never matches an NPC, it is
    /// picked only by <see cref="FriendGiftDue"/>.</summary>
    internal const string FriendGiftDialogKey = "friend_gift";

    /// <summary>The dialogue consequence that hands a friend's present over (#2598).</summary>
    internal const string FriendGiftConsequence = "friendgift";

    /// <summary>The role of a migrated old shared settler entry — "&lt;place&gt; · Villagers" in the roster (#2598).</summary>
    internal const string VillagersRole = "villagers";

    /// <summary>"npcmig:&lt;settle key&gt;" — the player's old entries of that settlement were migrated (#2598).</summary>
    private const string NpcMigrationMilestonePrefix = "npcmig:";

    private readonly Random _friendGiftRng = new(0x6F17E5);

    /// <summary>Set by a consequence that must REPLACE the choice's authored response (a friend's present that does not
    /// fit the bag, #2598) — read and cleared by <c>HandleNpcDialogChoice</c>, like <c>_dialogExtraText</c>.</summary>
    private string _dialogResponseOverride = string.Empty;

    private static readonly char[] SlotJobEnd = { '@', '#' };
    private static readonly char[] BaseIdEnd = { '#', ':' };

    // ---------------- Wall clock (#2595) ----------------

    private DateTime? _wallClockForTest; // test seam: the wall clock "last online" and "welcome back" read

    /// <summary>The wall-clock UTC now that "last seen" stamps and the welcome-back check use — a seam, like
    /// <c>BackupUtcNow</c>, so a test can step 19 hours ahead.</summary>
    private DateTime WallUtcNow => _wallClockForTest ?? DateTime.UtcNow;

    /// <summary><see cref="WallUtcNow"/> as the ISO-8601 round-trip text the save stores.</summary>
    private string WallUtcNowIso() => WallUtcNow.ToString("o", CultureInfo.InvariantCulture);

    /// <summary>Test seam (#2595): pins the wall clock (null = the real clock).</summary>
    public void SetWallClockForTest(DateTime? utcNow) => _wallClockForTest = utcNow;

    private static bool TryParseUtc(string iso, out DateTime utc)
        => DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out utc);

    // ---------------- Keys ----------------

    /// <summary>Whether a memory key is a settlement villager's own person key (<c>settle_…#slot:role</c>). Base
    /// residents (<c>base_…#slot</c>) are persons too, but they have no shared role entry to borrow a floor from.</summary>
    internal static bool IsPersonKey(string key)
    {
        if (string.IsNullOrEmpty(key) || !key.StartsWith("settle_", StringComparison.Ordinal))
        {
            return false;
        }

        int colon = key.LastIndexOf(':');
        int hash = key.IndexOf('#');
        return hash > 0 && colon > hash;
    }

    /// <summary>The place part of a memory key (#2598): cut at the role's ':' and at the slot's '#' —
    /// <c>settle_123#bed@4.1.7:settler</c> → <c>settle_123</c>, <c>settle_123:vendor</c> → <c>settle_123</c>. What a
    /// dialogue-given chain step binds to (<c>GrantMissionFromDialog</c>).</summary>
    internal static string PlaceOfNpcKey(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return string.Empty;
        }

        int colon = key.LastIndexOf(':');
        string place = colon >= 0 ? key.Substring(0, colon) : key;
        int hash = place.IndexOf('#');
        return hash >= 0 ? place.Substring(0, hash) : place;
    }

    /// <summary>The role part of a memory key (after the last ':'), "" when it has none.</summary>
    private static string RoleOfNpcKey(string key)
    {
        int colon = key.LastIndexOf(':');
        return colon >= 0 ? key.Substring(colon + 1) : string.Empty;
    }

    /// <summary>The old shared role key a villager's person key falls back to for its floor (#2598).</summary>
    private static string LegacyKeyOfPerson(string personKey) => NpcKey(PlaceOfNpcKey(personKey), RoleOfNpcKey(personKey));

    /// <summary>The job a person key names in its slot (<c>…#innkeeper@3.1.9:settler</c> → "innkeeper"); "" for a
    /// settler's bed slot and every non-person key.</summary>
    internal static string JobOfPersonKey(string key)
    {
        if (!IsPersonKey(key))
        {
            return string.Empty;
        }

        int hash = key.IndexOf('#');
        string slot = key.Substring(hash + 1, key.LastIndexOf(':') - hash - 1);
        int end = slot.IndexOfAny(SlotJobEnd);
        string job = end >= 0 ? slot.Substring(0, end) : slot;
        return job is "bed" or "settler" ? string.Empty : job;
    }

    /// <summary>
    /// The person key of a freshly spawned settlement resident (#2598): <c>settle_&lt;hash&gt;#&lt;slot&gt;:&lt;role&gt;</c>.
    /// The slot is where the resident belongs in the settlement's layout — their post marker cell for a post holder or a
    /// profession (<c>gardener@x.y.z</c>, <c>doctor@x.y.z</c>), else their bed's head cell (<c>bed@x.y.z</c>), both
    /// relative to the layout origin; a resident with neither gets <c>settler#n</c>. Survives changes to the resident cap
    /// or post order. The classic vendor, the quartermaster and the G.D.S. guardians keep the role key ("").
    /// </summary>
    private string SettlementPersonKey(SettlementInstance settlement, ServerNpc npc, Vector3f? post, Vector3i? bed, ref int fallbackOrdinal)
    {
        if (npc.Job is "vendor" or "quartermaster" || npc.Role is "guardian" or "quartermaster" || string.IsNullOrEmpty(settlement.Name))
        {
            return string.Empty;
        }

        string job = string.IsNullOrEmpty(npc.Job) ? "settler" : npc.Job;
        string slot = post is { } p
            ? job + "@" + LayoutCellText(settlement, new Vector3i((int)Math.Floor(p.X), (int)Math.Floor(p.Y), (int)Math.Floor(p.Z)))
            : bed is { } b
                ? "bed@" + LayoutCellText(settlement, b)
                : job + "#" + (++fallbackOrdinal).ToString(CultureInfo.InvariantCulture);

        string place = SettlementLocationKey(settlement.Name) + "#" + slot;
        string key = place + ":" + npc.Role;
        for (int n = 2; _npcs.Any(o => o.PersonKey == key); n++)
        {
            key = place + "#" + n.ToString(CultureInfo.InvariantCulture) + ":" + npc.Role; // two posts on one cell: never share a memory
        }

        return key;
    }

    /// <summary>A world cell relative to a settlement's layout origin (Min.X, GroundY, Min.Z), the short way round the
    /// east–west seam, as "x.y.z".</summary>
    private string LayoutCellText(SettlementInstance s, Vector3i cell)
    {
        int x = WorldConstants.WrapDeltaX(cell.X - s.Min.X, _world.Circumference);
        int y = cell.Y - s.GroundY;
        int z = cell.Z - s.Min.Z;
        return x.ToString(CultureInfo.InvariantCulture) + "." + y.ToString(CultureInfo.InvariantCulture) + "."
            + z.ToString(CultureInfo.InvariantCulture);
    }

    // ---------------- Standing ----------------

    /// <summary>
    /// The player's standing with an NPC key (#2598) — what EVERY reader asks (nameplate stages, dialogue gates, the
    /// greeting tier, hints, story threads, chain stages, radio calls): the key's own entry; for a villager without one,
    /// the floor min(old shared role value, <see cref="LegacyStandingFloorCap"/>); else 0.
    /// </summary>
    internal static int RelationshipValue(PlayerState p, string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return 0;
        }

        if (p.NpcMemory.TryGetValue(key, out var own))
        {
            return own.Value;
        }

        return IsPersonKey(key) && p.NpcMemory.TryGetValue(LegacyKeyOfPerson(key), out var legacy)
            ? Math.Min(legacy.Value, LegacyStandingFloorCap)
            : 0;
    }

    /// <summary>The player's own entry for an NPC key, created when missing (#2598): a villager's new entry starts at
    /// their floor, and a first meeting lifts it to at least <see cref="FirstMeetBonus"/> (never below the floor, so a
    /// villager already known through the old shared entry gets nothing extra).</summary>
    private static NpcRelationship EnsureRelationship(PlayerState p, string key, string name, string role)
    {
        if (p.NpcMemory.TryGetValue(key, out var rel))
        {
            return rel;
        }

        int seed = RelationshipValue(p, key);
        if (IsPersonKey(key) || IsWildernessPersonKey(key)) // #2641/#2646: a lodge resident or a wanderer is a person to meet too
        {
            seed = Math.Max(seed, Math.Min(seed + FirstMeetBonus, FirstMeetBonus));
        }

        rel = new NpcRelationship { Name = name, Role = role, Value = seed };
        p.NpcMemory[key] = rel;
        return rel;
    }

    /// <summary>Name, role and home of the live NPC behind a memory key (person key or settlement role key), for an
    /// entry created without an interaction (a festival, a cleared camp). Empty when no such NPC is spawned.</summary>
    private (string Name, string Role, string Place) DescribeNpcKey(string key)
    {
        foreach (var n in _npcs)
        {
            if (IsPersonKey(key) || IsWildernessPersonKey(key) ? n.PersonKey == key
                : n.PersonKey.Length == 0 && n.BaseId == 0 && n.CharacterId.Length == 0 && n.Settlement.Length > 0
                  && NpcKey(SettlementLocationKey(n.Settlement), n.Role) == key)
            {
                return (n.Name, n.Role, n.Settlement);
            }
        }

        return (string.Empty, RoleOfNpcKey(key), string.Empty);
    }

    /// <summary>
    /// Raises (or lowers) the player's standing with an NPC key by <paramref name="delta"/>, clamped like every other
    /// gain (#2598) — for the festival (<see cref="FestivalStandingBonus"/> per attendee) and other bonuses outside a
    /// conversation. A villager without an entry gets one, seeded from the floor. <paramref name="reason"/> is logged
    /// when the stage changes. The caller saves the player and refreshes the standings.
    /// </summary>
    internal void AddStanding(PlayerState p, string key, int delta, string reason)
    {
        if (string.IsNullOrEmpty(key) || delta == 0)
        {
            return;
        }

        var (name, role, place) = DescribeNpcKey(key);
        var rel = EnsureRelationship(p, key, name, role);
        if (rel.Place.Length == 0 && place.Length > 0)
        {
            rel.Place = place;
        }

        string before = RelationshipTier(rel.Value);
        rel.Value = Math.Clamp(rel.Value + delta, -100, 100);
        string after = RelationshipTier(rel.Value);
        if (before != after)
        {
            _log.Info($"'{p.Name}' and {(rel.Name.Length > 0 ? rel.Name : key)}: {before} → {after} ({reason}).");
        }
    }

    /// <summary>Raises the standing of every villager of a settlement the player ALREADY has an entry with (#2598) — a
    /// bandit camp near them cleared (<see cref="CampClearedStandingBonus"/>). Strangers stay strangers. Returns how many
    /// villagers were glad. The caller saves the player and refreshes the standings.</summary>
    internal int AddStandingToKnownVillagers(PlayerState p, string settlementName, int delta, string reason)
    {
        if (string.IsNullOrEmpty(settlementName))
        {
            return 0;
        }

        string loc = SettlementLocationKey(settlementName);
        var keys = p.NpcMemory.Keys.Where(k => IsPersonKey(k) && PlaceOfNpcKey(k) == loc).ToList();
        foreach (string key in keys)
        {
            AddStanding(p, key, delta, reason);
        }

        return keys.Count;
    }

    /// <summary>
    /// A passing hello's standing (#2598): +1 once per session per villager the player has already met (standing above
    /// 0), and only below <see cref="LegacyStandingFloorCap"/> — a hello helps a child become "known", it never makes a
    /// friend. Person keys only (vendors and quartermasters keep their role keys). Sends the nameplate stages when the
    /// tier moved; does NOT stamp "seen" (check <see cref="NpcWelcomeBackDue"/> first, then <see cref="StampNpcSeen"/>).
    /// Returns true when the +1 was paid. The caller saves (or leaves it to the autosave).
    /// </summary>
    internal bool TryHelloStanding(PlayerSession session, string personKey)
    {
        if (!IsPersonKey(personKey) && !IsWildernessPersonKey(personKey)) // #2641/#2646: lodge residents and wanderers are people too
        {
            return false;
        }

        int value = RelationshipValue(session.State, personKey);
        if (value <= 0 || value >= LegacyStandingFloorCap || !session.HelloStandingPaid.Add(personKey))
        {
            return false;
        }

        string before = RelationshipTier(value);
        AddStanding(session.State, personKey, 1, "hello"); // no "seen" stamp here: the caller stamps after its welcome check
        if (RelationshipTier(RelationshipValue(session.State, personKey)) != before)
        {
            SendNpcStandings(session);
        }

        return true;
    }

    // ---------------- Welcome back (#2595) ----------------

    /// <summary>Stamps "this NPC saw the player just now" (#2595) on an EXISTING entry — never creates one (a passing
    /// stranger is no acquaintance). Call it AFTER <see cref="NpcWelcomeBackDue"/>, which reads the previous stamp.</summary>
    internal void StampNpcSeen(PlayerState p, string npcKey)
    {
        if (!string.IsNullOrEmpty(npcKey) && p.NpcMemory.TryGetValue(npcKey, out var rel))
        {
            rel.LastSeenUtc = WallUtcNowIso();
        }
    }

    /// <summary>
    /// Whether an NPC greets the player as "welcome back" (#2595): the player is known/trusted to them (or this is the
    /// player's own base resident) and the NPC last saw them at least <see cref="NpcWelcomeBackHours"/> ago. An empty
    /// stamp (a memory from before the stamp existed) is NOT due — it is only stamped, so the update brings no flood of
    /// welcomes. Pure read: stamp with <see cref="StampNpcSeen"/> afterwards.
    /// </summary>
    internal bool NpcWelcomeBackDue(PlayerState p, string npcKey)
    {
        if (string.IsNullOrEmpty(npcKey) || !p.NpcMemory.TryGetValue(npcKey, out var rel)
            || string.IsNullOrEmpty(rel.LastSeenUtc) || !TryParseUtc(rel.LastSeenUtc, out var last))
        {
            return false;
        }

        if (RelationshipTier(rel.Value) is not ("known" or "trusted") && !IsOwnBaseResidentKey(p, npcKey))
        {
            return false;
        }

        return (WallUtcNow - last).TotalHours >= NpcWelcomeBackHours;
    }

    /// <summary>Whole days since the NPC behind <paramref name="npcKey"/> last saw the player (#2595) — the AI's
    /// "AwayDays"; 0 when unknown or less than a day.</summary>
    internal int NpcDaysAway(PlayerState p, string npcKey)
        => p.NpcMemory.TryGetValue(npcKey, out var rel) && TryParseUtc(rel.LastSeenUtc, out var last)
            ? Math.Max(0, (int)(WallUtcNow - last).TotalDays)
            : 0;

    /// <summary>Whether a memory key is one of the player's own base residents (<c>base_&lt;id&gt;…</c> of a base they own).</summary>
    private bool IsOwnBaseResidentKey(PlayerState p, string key)
    {
        if (!key.StartsWith("base_", StringComparison.Ordinal))
        {
            return false;
        }

        int end = key.IndexOfAny(BaseIdEnd, 5);
        string digits = end > 5 ? key.Substring(5, end - 5) : key.Substring(5);
        return int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out int baseId)
            && _bases.Any(b => b.Id == baseId && b.OwnerId == p.PlayerId);
    }

    // ---------------- Migration (#2598, E7) ----------------

    /// <summary>
    /// Once per player and settlement on world entry: the old shared entries become people. The villager the old
    /// settler entry is named after (the one "People you know" showed) inherits its full value and log; the old entry
    /// itself becomes "&lt;place&gt; · Villagers", capped at <see cref="LegacyStandingFloorCap"/> — it lives on as the
    /// floor for everyone else. A trading profession named in the old vendor entry inherits that one the same way (the
    /// vendor entry stays the classic vendor's and gets their name back). Cheap when there is nothing to do. Runs after
    /// the #2693 move of the old name-only keys onto this body's keys, so it always works on the current keys.
    /// </summary>
    private void MigrateSettlementMemory(PlayerSession session)
    {
        MigrateLegacySettlementKeys(session); // #2693: name-only settlement keys → this body's keys (memory, wishes, chain places)
        var p = session.State;
        if (p.NpcMemory.Count == 0)
        {
            return;
        }

        bool changed = false;
        foreach (var s in _settlements)
        {
            if (s.Ruined || string.IsNullOrEmpty(s.Name))
            {
                continue;
            }

            string loc = SettlementLocationKey(s.Name);
            string milestone = NpcMigrationMilestonePrefix + loc;
            bool hasSettlers = p.NpcMemory.TryGetValue(NpcKey(loc, "settler"), out var settlers);
            bool hasVendor = p.NpcMemory.TryGetValue(NpcKey(loc, "vendor"), out var vendor);
            if ((!hasSettlers && !hasVendor) || p.Milestones.Contains(milestone))
            {
                continue;
            }

            if (settlers is not null && settlers.Role != VillagersRole)
            {
                InheritLegacyEntry(p, s, settlers, "settler");
                settlers.Role = VillagersRole;
                if (settlers.Place.Length == 0)
                {
                    settlers.Place = s.Name;
                }

                settlers.Name = settlers.Place;
                settlers.Value = Math.Min(settlers.Value, LegacyStandingFloorCap);
            }

            if (vendor is not null && InheritLegacyEntry(p, s, vendor, "vendor")
                && _npcs.FirstOrDefault(n => n.Settlement == s.Name && n.Job == "vendor" && n.BaseId == 0
                    && n.CharacterId.Length == 0) is { } classic)
            {
                vendor.Name = classic.Name; // the trades went to the profession; the role key is the stall's again
            }

            p.Milestones.Add(milestone);
            changed = true;
        }

        if (changed)
        {
            _repo.SavePlayer(p);
        }
    }

    /// <summary>The villager of a settlement whose name an old shared entry carries inherits it (#2598): full value
    /// (never less than they already have) and the log. False when nobody of that role carries the name.</summary>
    private bool InheritLegacyEntry(PlayerState p, SettlementInstance s, NpcRelationship legacy, string role)
    {
        if (string.IsNullOrEmpty(legacy.Name))
        {
            return false;
        }

        var person = _npcs.FirstOrDefault(n => n.Settlement == s.Name && n.Role == role && n.PersonKey.Length > 0
            && n.CharacterId.Length == 0 && n.Name == legacy.Name);
        if (person is null)
        {
            return false;
        }

        if (p.NpcMemory.TryGetValue(person.PersonKey, out var own))
        {
            own.Value = Math.Max(own.Value, legacy.Value);
            return true;
        }

        p.NpcMemory[person.PersonKey] = new NpcRelationship
        {
            Name = person.Name,
            Role = person.Role,
            Place = legacy.Place.Length > 0 ? legacy.Place : s.Name,
            Value = legacy.Value,
            Log = legacy.Log.Select(i => new NpcInteraction { Kind = i.Kind }).ToList(),
            LastSeenUtc = legacy.LastSeenUtc ?? string.Empty,
        };
        return true;
    }

    // ---------------- Friend gift (#2598, E8) ----------------

    /// <summary>Whether talking to this NPC opens the friend's present (#2598): a settlement villager (not a trader,
    /// quartermaster, base resident or authored character) who is the player's friend on their OWN entry — the floor of
    /// an old shared standing never counts — with a present in the pool and the half hour of playtime since the last
    /// one gone by.</summary>
    private bool FriendGiftDue(PlayerSession session, ServerNpc npc, string npcKey)
    {
        if (npc.BaseId != 0 || npc.CharacterId.Length > 0 || npc.Settlement.Length == 0 || npc.Role != "settler"
            || npc.PersonKey.Length == 0 || npcKey != npc.PersonKey || _content.VillageLife.FriendGifts.Count == 0
            || !session.State.NpcMemory.TryGetValue(npcKey, out var rel) || rel.Value < FriendStanding)
        {
            return false;
        }

        return rel.LastGiftAt <= 0 || _meta.CumulativePlaytimeSeconds - rel.LastGiftAt >= FriendGiftCooldownSeconds;
    }

    /// <summary>The engine's friend-present dialogue, or null when the data lacks it.</summary>
    private DialogDefinition? FriendGiftDialog()
        => _content.Dialogs.FirstOrDefault(d => string.Equals(d.Key, FriendGiftDialogKey, StringComparison.Ordinal));

    /// <summary>The <c>friendgift</c> consequence (#2598): picks a present from the pool, hands it over and appends the
    /// item line ("+3 Berries", always authored). A full bag gets "next time!" instead — and the cooldown is NOT spent,
    /// so the friend offers again on the next talk.</summary>
    private void GiveFriendGift(PlayerSession session, string npcKey)
    {
        var p = session.State;
        if (!p.NpcMemory.TryGetValue(npcKey, out var rel) || rel.Value < FriendStanding
            || (rel.LastGiftAt > 0 && _meta.CumulativePlaytimeSeconds - rel.LastGiftAt < FriendGiftCooldownSeconds))
        {
            return; // the server's own check again — a present is never handed out twice per cooldown
        }

        var pool = _content.VillageLife.FriendGifts.Where(g => g.Count > 0 && _content.GetItem(g.Item) is not null).ToList();
        if (pool.Count == 0)
        {
            return;
        }

        var gift = pool[_friendGiftRng.Next(pool.Count)];
        Serve(session);
        var bag = new MaterialPool(_content, p, _ship);
        if (!bag.CanFit(new[] { gift }))
        {
            _dialogResponseOverride = Localize(session.Locale, "dlg.friend_gift.full");
            return;
        }

        bag.Add(gift.Item, gift.Count);
        SendInventory(session);
        rel.LastGiftAt = Math.Max(1.0, _meta.CumulativePlaytimeSeconds); // 0 reads "never" — a gift at playtime 0 still counts
        string itemName = LocalizedName(session.Locale, _content.GetItem(gift.Item)?.NameKey, gift.Item);
        _dialogExtraText = "\n\n+" + gift.Count.ToString(CultureInfo.InvariantCulture) + " " + itemName;
    }

    // ---------------- Radio story threads ----------------

    /// <summary>The roles whose acquaintances may call a story thread in, in the order the radio scan asks them.</summary>
    private static readonly string[] ThreadCallRoles = { "quartermaster", "vendor", "settler" };

    /// <summary>The memory keys of one settlement's people of a role the player knows (#2598) — the shared role key
    /// (still the quartermaster's and the classic vendor's; a settler one only before its migration) and every
    /// villager's own person key of that role. A snapshot, so a call may save the player while the scan walks it.</summary>
    private static List<string> ThreadCallKeys(PlayerState p, string locationKey, string role)
    {
        var keys = new List<string>();
        string roleKey = NpcKey(locationKey, role);
        if (p.NpcMemory.TryGetValue(roleKey, out var shared) && shared.Role != VillagersRole)
        {
            keys.Add(roleKey);
        }

        foreach (string key in p.NpcMemory.Keys)
        {
            if (IsPersonKey(key) && PlaceOfNpcKey(key) == locationKey && RoleOfNpcKey(key) == role)
            {
                keys.Add(key);
            }
        }

        return keys;
    }

    // ---------------- Roster ----------------

    /// <summary>The roster's role label for an entry (#2598): a villager's job when it has a name of its own
    /// (<c>npc.role.innkeeper</c>, <c>npc.role.doctor</c>), else the stored role (<c>npc.role.villagers</c> for a migrated
    /// shared entry); an entry without a role reads "vendor", as it always did.</summary>
    private string KnownNpcRoleKey(string key, NpcRelationship rel)
    {
        string job = JobOfPersonKey(key);
        if (job.Length > 0 && EnglishHas("npc.role." + job))
        {
            return "npc.role." + job;
        }

        return "npc.role." + (string.IsNullOrEmpty(rel.Role) ? "vendor" : rel.Role);
    }

    /// <summary>Whether the English table (which every other locale falls back to) has a key.</summary>
    private bool EnglishHas(string key)
    {
        var locale = Shared.Localization.GameLocale.English;
        if (!_localizers.TryGetValue(locale, out var loc))
        {
            loc = _content.CreateLocalizer(locale);
            _localizers[locale] = loc;
        }

        return loc.Has(key);
    }

    /// <summary>The "People you know" list a player is sent (#1118, #2598): everyone with a standing, friendliest first.</summary>
    private NetKnownNpc[] KnownNpcEntries(PlayerState p)
        => p.NpcMemory
            .Where(e => e.Value.Value != 0 && !string.IsNullOrEmpty(e.Value.Name))
            .OrderByDescending(e => e.Value.Value)
            .Take(64)
            .Select(e => new NetKnownNpc
            {
                Name = e.Value.Name,
                RoleKey = KnownNpcRoleKey(e.Key, e.Value),
                StageKey = NpcStageKey(e.Value.Value),
                Place = e.Value.Place,
                Standing = e.Value.Value,
            })
            .ToArray();

    // ---------------- Test hooks ----------------

    /// <summary>Test/inspection (#2598): every settlement resident of the active world with their person key ("" =
    /// role key), in spawn order.</summary>
    public IReadOnlyList<(int Id, string Settlement, string Name, string Role, string Job, string PersonKey)> PersonKeysForTest
        => _npcs.Where(n => n.Settlement.Length > 0 && n.BaseId == 0)
            .Select(n => (n.Id, n.Settlement, n.Name, n.Role, n.Job, n.PersonKey)).ToList();

    /// <summary>Test seam (#2598): the memory key a player's talk to this NPC uses.</summary>
    public string? NpcKeyOfNpcForTest(string playerId, int npcId)
        => FindSessionByPlayerId(playerId) is { } s && _npcs.FirstOrDefault(n => n.Id == npcId) is { } npc ? NpcKeyForNpc(s, npc) : null;

    /// <summary>Test seam (#2598): the standing every reader sees (own entry, else the villager floor).</summary>
    public int RelationshipValueForTest(string playerId, string key)
        => FindSessionByPlayerId(playerId) is { } s ? RelationshipValue(s.State, key) : 0;

    /// <summary>Test seam (#2598): the nameplate stage key this player sees over an NPC ("" = stranger, not listed).</summary>
    public string NpcStageForTest(string playerId, int npcId)
    {
        if (FindSessionByPlayerId(playerId) is not { } s || _npcs.FirstOrDefault(n => n.Id == npcId) is not { } npc)
        {
            return string.Empty;
        }

        int value = RelationshipValue(s.State, NpcKeyForNpc(s, npc));
        return RelationshipTier(value) == "stranger" ? string.Empty : NpcStageKey(value);
    }

    /// <summary>Test seam (#2598): runs the world-entry migration of the player's old shared entries now.</summary>
    public void MigrateNpcMemoryForTest(string playerId)
    {
        if (FindSessionByPlayerId(playerId) is { } s)
        {
            MigrateSettlementMemory(s);
        }
    }

    /// <summary>Test seam (#2598): the "People you know" list the player would be sent.</summary>
    public IReadOnlyList<NetKnownNpc> KnownNpcsForTest(string playerId)
        => FindSessionByPlayerId(playerId) is { } s ? KnownNpcEntries(s.State) : Array.Empty<NetKnownNpc>();

    /// <summary>Test seam (#2598): a passing hello's standing, as the passing greeting pays it.</summary>
    public bool TryHelloStandingForTest(string playerId, string personKey)
        => FindSessionByPlayerId(playerId) is { } s && TryHelloStanding(s, personKey);

    /// <summary>Test seam (#2598): <see cref="AddStanding"/>.</summary>
    public void AddStandingForTest(string playerId, string key, int delta)
    {
        if (FindSessionByPlayerId(playerId) is { } s)
        {
            AddStanding(s.State, key, delta, "test");
        }
    }

    /// <summary>Test seam (#2598): <see cref="AddStandingToKnownVillagers"/>.</summary>
    public int AddStandingToKnownVillagersForTest(string playerId, string settlementName, int delta)
        => FindSessionByPlayerId(playerId) is { } s ? AddStandingToKnownVillagers(s.State, settlementName, delta, "test") : 0;

    /// <summary>Test seam (#2598): the friend-present cooldown in seconds of world playtime.</summary>
    public static double FriendGiftCooldownSecondsForTest => FriendGiftCooldownSeconds;

    /// <summary>Test seam (#2598): <see cref="PlaceOfNpcKey"/>.</summary>
    public static string PlaceOfNpcKeyForTest(string key) => PlaceOfNpcKey(key);

    /// <summary>Test seam (#2598): <see cref="IsPersonKey"/>.</summary>
    public static bool IsPersonKeyForTest(string key) => IsPersonKey(key);

    /// <summary>Test seam (#2598): <see cref="JobOfPersonKey"/>.</summary>
    public static string JobOfPersonKeyForTest(string key) => JobOfPersonKey(key);

    /// <summary>Test seam (#2595): whether the NPC behind a key would greet the player as "welcome back" now.</summary>
    public bool NpcWelcomeBackDueForTest(string playerId, string npcKey)
        => FindSessionByPlayerId(playerId) is { } s && NpcWelcomeBackDue(s.State, npcKey);

    /// <summary>Test seam (#2595): stamps "seen just now" on an existing entry.</summary>
    public void StampNpcSeenForTest(string playerId, string npcKey)
    {
        if (FindSessionByPlayerId(playerId) is { } s)
        {
            StampNpcSeen(s.State, npcKey);
        }
    }
}
