// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Globalization;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The keys of a settlement and the rolls of every mission board (#2688, #2693).
/// <para><b>Place keys.</b> A settlement's key (<c>settle_&lt;n&gt;</c>) names its board's mission ids, its people's NPC
/// memory, its villagers' wishes and the place a chain step was taken at. It used to hash the settlement NAME only — and
/// names come from about 45 combinations and are unique per body only, so two "Karth Village" on two moons shared one
/// board, the same mission ids and the same relationships. The key now hashes body and name, salted with the world seed,
/// into nine digits (<see cref="SettlementKeyFloor"/>..999 999 999); the old key had at most five, so the two never meet
/// and an old key is recognisable by its shape (<see cref="IsLegacySettlementPlace"/>). Station keys (<c>station_&lt;n&gt;</c>)
/// were unique already (a station id is a body id) and stay as they are.</para>
/// <para><b>Board rolls.</b> A board coins its jobs per slot from a seeded <see cref="System.Random"/>. The seed used to be
/// the low bits of the plain FNV hash of "board:stream:slot" — neighbouring slots differ there by multiples of 435, and a
/// seeded Random is affine in its seed, so the first draws of consecutive slots formed an arithmetic sequence (a board
/// repeated after four jobs in 59 % of the cases) — and the key held no world seed, so a board was the same in every
/// world. New rolls seed from <see cref="StableHashing.Seed32(string, long)"/> with the world seed.</para>
/// <para><b>What a player already holds keeps its definition.</b> A board job is not stored: it is coined again from its
/// id after every load. So the new rolls run on NEW ids — a settlement board under its new place key, a station or base
/// board on its own <see cref="MixedRollsInfix"/> stream next to the old one — and the old ids keep the old rolls
/// (<c>legacy: true</c>). An old board offers nothing new; it only re-coins what a player still holds there and finishes
/// a big order they started (<see cref="EnsureLegacyBoardStreams"/>).</para>
/// <para><b>Migration.</b> On world entry, everything the player's save files under an old settlement key moves to the key
/// of the settlement of that name on this body (<see cref="MigrateLegacySettlementKeys"/>). The old key cannot say which
/// body it meant: when two bodies carry the same name, the first of them the player enters after the update inherits it.
/// Moved, not copied — a copy would list the same person twice under "People you know" and cannot split one held wish or
/// one running chain between two villages.</para>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The smallest number of a body-aware settlement key — nine digits, so it can never equal an old key (≤ 99 999).</summary>
    private const uint SettlementKeyFloor = 100_000_000u;

    /// <summary>How many body-aware settlement keys there are (100 000 000 … 999 999 999).</summary>
    private const int SettlementKeySpan = 900_000_000;

    /// <summary>Old board keys have at most this many digits (the old hash was taken mod 100 000).</summary>
    private const int LegacySettlementKeyDigits = 5;

    private const string SettlementKeyPrefix = "settle_";

    /// <summary>#2688: the id stream of a station or base board's new jobs (<c>station_&lt;n&gt;_m3</c>, <c>…_ms0</c>,
    /// <c>…_mc0</c>) — next to the old stream, whose ids players may hold. Settlement boards need none: their place key
    /// itself is new (#2693).</summary>
    internal const string MixedRollsInfix = "m";

    /// <summary>The pre-#2693 place key of a settlement: its name only. Read for held missions and the migration — never
    /// written for anything new.</summary>
    internal static string LegacySettlementLocationKey(string settlementName)
        => SettlementKeyPrefix + ((uint)WorldGenerator.StableHash(settlementName ?? string.Empty) % 100000u).ToString(CultureInfo.InvariantCulture);

    /// <summary>#2693: the place key of the settlement <paramref name="settlementName"/> on the body
    /// <paramref name="bodyId"/> in the world <paramref name="worldSeed"/> — the same name on two bodies gives two keys.</summary>
    internal static string SettlementLocationKeyFor(string bodyId, string settlementName, long worldSeed)
    {
        ulong mixed = StableHashing.Mixed("settlement:" + bodyId + "|" + settlementName, worldSeed);
        uint n = SettlementKeyFloor + (uint)StableHashing.Index(mixed, SettlementKeySpan);
        return SettlementKeyPrefix + n.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>The place key of a settlement of the ACTIVE world (matches the <c>settle_&lt;n&gt;</c> mission prefix), so
    /// each settlement keeps its own board and NPC memory — also against a namesake on another body (#2693).</summary>
    private string SettlementLocationKey(string settlementName)
        => SettlementLocationKeyFor(_world.LocationId, settlementName ?? string.Empty, _meta.Seed);

    /// <summary>The mission-id prefix of a settlement board of the active world (<c>settle_&lt;n&gt;_</c>).</summary>
    private string SettlementBoardPrefix(string settlementName) => SettlementLocationKey(settlementName) + "_";

    /// <summary>The pre-#2693 mission-id prefix of a settlement board — the ids players took before the update.</summary>
    private static string LegacySettlementBoardPrefix(string settlementName) => LegacySettlementLocationKey(settlementName) + "_";

    /// <summary>A station board's mission-id prefix (<c>station_&lt;n&gt;_</c>) — the station's place key, unchanged.</summary>
    private static string StationBoardPrefix(string stationId) => StationLocationKey(stationId) + "_";

    /// <summary>Whether a place key is an old, name-only settlement key (<c>settle_</c> + one to five digits).</summary>
    internal static bool IsLegacySettlementPlace(string? place)
        => place is not null && place.StartsWith(SettlementKeyPrefix, StringComparison.Ordinal)
           && LegacyPlaceEnd(place, SettlementKeyPrefix.Length) == place.Length;

    /// <summary>The end of an old settlement number that starts at <paramref name="start"/> (one to five digits), or -1.</summary>
    private static int LegacyPlaceEnd(string s, int start)
    {
        int i = start;
        while (i < s.Length && i - start <= LegacySettlementKeyDigits && s[i] >= '0' && s[i] <= '9')
        {
            i++;
        }

        int digits = i - start;
        return digits is >= 1 and <= LegacySettlementKeyDigits ? i : -1;
    }

    /// <summary>
    /// The rng of one board roll (#2688): <paramref name="stream"/> is "mission" / "buildmission" / "scanmission" /
    /// "bigorder" / "bounty", <paramref name="part"/> the slot, order or camp. A <paramref name="legacy"/> roll keeps the
    /// historical seeding bit for bit — a job a player holds is coined again from it after every load; every other roll
    /// seeds from the well-mixed hash with the world seed, so consecutive slots are unrelated and boards differ per world.
    /// </summary>
    internal static System.Random BoardRng(string boardKey, string stream, string part, bool legacy, long worldSeed)
    {
        string key = boardKey + ":" + stream + ":" + part;
        return legacy
            ? new System.Random(unchecked((int)WorldGenerator.StableHash(key)))
            : new System.Random(StableHashing.Seed32(key, worldSeed));
    }

    /// <summary><see cref="BoardRng(string, string, string, bool, long)"/> for a numbered slot or order.</summary>
    internal static System.Random BoardRng(string boardKey, string stream, int slot, bool legacy, long worldSeed)
        => BoardRng(boardKey, stream, slot.ToString(CultureInfo.InvariantCulture), legacy, worldSeed);

    /// <summary>The board rng of this world (see the static form).</summary>
    private System.Random BoardRng(string boardKey, string stream, int slot, bool legacy)
        => BoardRng(boardKey, stream, slot, legacy, _meta.Seed);

    /// <summary>
    /// #2688/#2693: what a player took from a board before it moved to its new ids — re-coined with the old rolls so it
    /// can still be shown and turned in (a held gather, build or survey job; a big order they started, both of its
    /// steps). Offers nothing new. Cheap when the player holds nothing there.
    /// </summary>
    private void EnsureLegacyBoardStreams(PlayerState player, string legacyPrefix, string legacyBoardKey, HashSet<string> idSet,
        string giverName, HashSet<string> currentBoardIds, bool station, bool withBuild)
    {
        if (!player.Missions.Any(m => m.MissionId.StartsWith(legacyPrefix, StringComparison.Ordinal)))
        {
            return;
        }

        EnsureBoardWindow(player, legacyPrefix, legacyBoardKey, idSet, giverName, currentBoardIds, BoardSlotKind.Gather, window: 0, legacy: true);
        if (withBuild)
        {
            EnsureBoardWindow(player, legacyPrefix + "b", legacyBoardKey, idSet, giverName, currentBoardIds, BoardSlotKind.Build, window: 0, legacy: true);
        }

        EnsureBoardWindow(player, legacyPrefix + "s", legacyBoardKey, idSet, giverName, currentBoardIds,
            station ? BoardSlotKind.StationScan : BoardSlotKind.Scan, window: 0, legacy: true);
        EnsureBigOrders(player, legacyPrefix, legacyBoardKey, idSet, giverName, currentBoardIds, station, legacy: true);
    }

    /// <summary>The NPC-memory place of a board mission id (<c>settle_123_4</c> → <c>settle_123</c>): an old settlement id
    /// a player still holds names the body-aware key of its settlement on this world (#2693).</summary>
    private string MemoryLocationOfBoardMission(string missionId)
    {
        string loc = LocationKeyOfMission(missionId);
        if (!IsLegacySettlementPlace(loc))
        {
            return loc;
        }

        foreach (var s in _settlements)
        {
            if (!string.IsNullOrEmpty(s.Name) && LegacySettlementLocationKey(s.Name) == loc)
            {
                return SettlementLocationKey(s.Name);
            }
        }

        return loc;
    }

    // ---------------- Migration (#2693) ----------------

    /// <summary>
    /// Once per world entry (cheap when there is nothing old): moves what the player's save files under an old,
    /// name-only settlement key onto the key of the settlement of that name on this body — the NPC memory (the shared
    /// role entries and every villager's person key), the #2598 migration marker, the wish ledger with its held wishes,
    /// and the place a chain step was taken at. Board jobs a player holds keep their old ids and rolls (see
    /// <see cref="EnsureLegacyBoardStreams"/>). An old key whose name is not on this body waits for that body.
    /// </summary>
    private void MigrateLegacySettlementKeys(PlayerSession session)
    {
        var p = session.State;
        if (!HasLegacySettlementKeys(p))
        {
            return;
        }

        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var s in _settlements)
        {
            if (!s.Ruined && !string.IsNullOrEmpty(s.Name))
            {
                map.TryAdd(LegacySettlementLocationKey(s.Name), SettlementLocationKey(s.Name));
            }
        }

        if (map.Count == 0)
        {
            return;
        }

        bool changed = MoveLegacyMemory(p, map);
        changed |= MoveLegacyMilestones(p, map);
        changed |= MoveLegacyWishes(p, map);
        changed |= MoveLegacyMissionPlaces(p, map);
        if (changed)
        {
            _repo.SavePlayer(p);
            _log.Info($"Moved '{p.Name}''s settlement memory onto the body-aware keys of '{_world.LocationId}' (#2693).");
        }
    }

    /// <summary>The old settlement place a memory key starts with (<c>settle_123:vendor</c>, <c>settle_123#bed@…</c>), or null.</summary>
    private static string? LegacyPlaceOfMemoryKey(string key)
    {
        if (!key.StartsWith(SettlementKeyPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        int end = LegacyPlaceEnd(key, SettlementKeyPrefix.Length);
        return end > 0 && (end == key.Length || key[end] is ':' or '#') ? key.Substring(0, end) : null;
    }

    /// <summary>The old settlement place a wish id names (<c>wish_123_…</c> → <c>settle_123</c>), or null.</summary>
    private static string? LegacyPlaceOfWishId(string missionId)
    {
        if (!IsWishMission(missionId))
        {
            return null;
        }

        int end = LegacyPlaceEnd(missionId, WishMissionPrefix.Length);
        return end > 0 && end < missionId.Length && missionId[end] == '_'
            ? SettlementKeyPrefix + missionId.Substring(WishMissionPrefix.Length, end - WishMissionPrefix.Length)
            : null;
    }

    /// <summary>Whether anything in the save still names an old settlement key — the allocation-free fast path.</summary>
    private static bool HasLegacySettlementKeys(PlayerState p)
    {
        foreach (string key in p.NpcMemory.Keys)
        {
            if (LegacyPlaceOfMemoryKey(key) is not null)
            {
                return true;
            }
        }

        foreach (string key in p.Wishes.Keys)
        {
            if (IsLegacySettlementPlace(key))
            {
                return true;
            }
        }

        foreach (var m in p.Missions)
        {
            if (IsLegacySettlementPlace(m.AcceptedFrom) || LegacyPlaceOfWishId(m.MissionId) is not null)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Re-files the memory entries of the mapped old places under the new ones. A key already filed under the new
    /// place (only possible when the player met someone there before this migration ran) keeps the warmer of the two.</summary>
    private static bool MoveLegacyMemory(PlayerState p, Dictionary<string, string> map)
    {
        List<(string Old, string New)>? moves = null;
        foreach (string key in p.NpcMemory.Keys)
        {
            if (LegacyPlaceOfMemoryKey(key) is { } place && map.TryGetValue(place, out var target))
            {
                (moves ??= new()).Add((key, target + key.Substring(place.Length)));
            }
        }

        if (moves is null)
        {
            return false;
        }

        foreach (var (oldKey, newKey) in moves)
        {
            var rel = p.NpcMemory[oldKey];
            p.NpcMemory.Remove(oldKey);
            if (!p.NpcMemory.TryGetValue(newKey, out var current) || rel.Value > current.Value)
            {
                p.NpcMemory[newKey] = rel;
            }
        }

        return true;
    }

    /// <summary>The #2598 "old shared entries became people" marker follows its place, so that migration does not run twice.</summary>
    private static bool MoveLegacyMilestones(PlayerState p, Dictionary<string, string> map)
    {
        bool changed = false;
        foreach (var (oldPlace, newPlace) in map)
        {
            if (p.Milestones.Remove(NpcMigrationMilestonePrefix + oldPlace))
            {
                p.Milestones.Add(NpcMigrationMilestonePrefix + newPlace);
                changed = true;
            }
        }

        return changed;
    }

    /// <summary>A place's wish ledger moves with it — its villagers' person keys included (#2601).</summary>
    private static bool MoveLegacyWishes(PlayerState p, Dictionary<string, string> map)
    {
        bool changed = false;
        foreach (var (oldPlace, newPlace) in map)
        {
            if (!p.Wishes.TryGetValue(oldPlace, out var ledger) || p.Wishes.ContainsKey(newPlace))
            {
                continue;
            }

            p.Wishes.Remove(oldPlace);
            foreach (var lane in ledger.Lanes)
            {
                lane.Giver = RePlace(lane.Giver, oldPlace, newPlace);
                lane.LastGiver = RePlace(lane.LastGiver, oldPlace, newPlace);
            }

            p.Wishes[newPlace] = ledger;
            changed = true;
        }

        return changed;
    }

    /// <summary>A person key of <paramref name="oldPlace"/> re-filed under <paramref name="newPlace"/> (anything else unchanged).</summary>
    private static string RePlace(string key, string oldPlace, string newPlace)
        => LegacyPlaceOfMemoryKey(key) == oldPlace ? newPlace + key.Substring(oldPlace.Length) : key;

    /// <summary>The place a chain step was taken at follows its settlement; a held wish is renamed onto the new place
    /// (its stored definition with it), because the wish lanes find their held wish by its id.</summary>
    private bool MoveLegacyMissionPlaces(PlayerState p, Dictionary<string, string> map)
    {
        bool changed = false;
        foreach (var pr in p.Missions)
        {
            if (IsLegacySettlementPlace(pr.AcceptedFrom) && map.TryGetValue(pr.AcceptedFrom, out var place))
            {
                pr.AcceptedFrom = place;
                changed = true;
            }

            if (LegacyPlaceOfWishId(pr.MissionId) is { } wishPlace && map.TryGetValue(wishPlace, out var newPlace))
            {
                string oldId = pr.MissionId;
                string newId = WishMissionPrefix + newPlace.Substring(SettlementKeyPrefix.Length)
                    + oldId.Substring(WishMissionPrefix.Length + wishPlace.Length - SettlementKeyPrefix.Length);
                pr.MissionId = newId;
                if (_missionDefs.Remove(oldId, out var def))
                {
                    def.Id = newId;
                    _missionDefs[newId] = def;
                    _repo.DeleteMission(oldId);
                    _repo.SaveMission(def);
                }

                changed = true;
            }
        }

        return changed;
    }

    // ---------------- Test seams ----------------

    /// <summary>Test seam (#2693): the pre-#2693 name-only key of a settlement (the body-aware one of the active world is
    /// <see cref="SettlementLocationKeyForTest"/>).</summary>
    public static string LegacySettlementPlaceKeyForTest(string settlementName) => LegacySettlementLocationKey(settlementName);

    /// <summary>Test seam (#2693): runs the world-entry key migration for a player now.</summary>
    public void MigrateLegacySettlementKeysForTest(string playerId)
    {
        if (FindSessionByPlayerId(playerId) is { } s)
        {
            MigrateLegacySettlementKeys(s);
        }
    }
}
