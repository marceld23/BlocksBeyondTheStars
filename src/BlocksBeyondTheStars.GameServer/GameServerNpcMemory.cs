// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// NPC memory (item 14): each NPC remembers a player's interactions (dialog / trade / mission accepted) as a
/// relationship score plus a capped log of the most recent ones. Stored on the player (keyed by a stable NPC
/// key = location + role) so it persists, and read back by item 15's dialog backend. No dialog system yet, so
/// only trade + mission-accept are recorded today; "Dialog" is supported in the model for item 15.
/// </summary>
public sealed partial class GameServer
{
    private const int NpcMemoryLog = 10;

    private static int InteractionWeight(NpcInteractionKind kind) => kind switch
    {
        NpcInteractionKind.MissionAccepted => 3,
        NpcInteractionKind.Trade => 2,
        _ => 1, // Dialog
    };

    /// <summary>A stable NPC key for memory: the board's location id + the role, so the same vendor/quartermaster
    /// is recognised across reloads even though the runtime <c>ServerNpc.Id</c> is not stable.</summary>
    private static string NpcKey(string locationKey, string role) => locationKey + ":" + role;

    // A settlement's location id is SettlementLocationKey (GameServerSettlementKeys.cs, #2693): body + name, not the name alone.

    private static string StationLocationKey(string stationId) => $"station_{(uint)WorldGenerator.StableHash(stationId) % 100000u}";

    /// <summary>A base resident's memory key as minted from a base id (#1262, #1865): the PERSON in a slot, whatever job they
    /// hold — the founding settler is <c>base_&lt;id&gt;:settler</c>, the residents the beds brought are
    /// <c>base_&lt;id&gt;#&lt;slot&gt;:settler</c> (a base NAME can change — keying by its hash minted a new settler on every
    /// rename). A resident who takes over the trading post stays the same acquaintance. The id is a load-order number that
    /// shifts when an older core goes (#2716), so this only mints the key a resident is pinned with
    /// (<c>BaseResidentRecord.Key</c>); readers go through <see cref="BaseResidentKey(ServerBase, int)"/>.</summary>
    private static string BaseResidentIdKey(string idPart, int slot)
        => slot == 0 ? NpcKey("base_" + idPart, "settler") : NpcKey($"base_{idPart}#{slot}", "settler");

    /// <summary>The board location id from a board mission id: the kind and the number (<c>settle_123_4</c>,
    /// <c>settle_123_bounty_camp_2</c>, <c>station_9_m3</c> → <c>settle_123</c> / <c>station_9</c>). Cut at the SECOND
    /// underscore — the last one left a camp bounty's quartermaster filed under <c>settle_123_bounty</c> (#2693).</summary>
    private static string LocationKeyOfMission(string missionId)
    {
        int kind = missionId.IndexOf('_');
        int number = kind > 0 ? missionId.IndexOf('_', kind + 1) : -1;
        if (number > 0)
        {
            return missionId[..number];
        }

        int i = missionId.LastIndexOf('_');
        return i > 0 ? missionId[..i] : missionId;
    }

    /// <summary>Records an interaction in an NPC's memory of this player (item 14): raises the relationship by a
    /// per-kind weight and appends to the capped last-N log. Persists with the player. A passing guest at the base
    /// (<c>guest:&lt;name&gt;</c>, #2602) is nobody to remember — the roster stays clean.</summary>
    private void RecordNpcInteraction(PlayerState player, string npcKey, string npcName, string npcRole, NpcInteractionKind kind, string place = "")
    {
        if (string.IsNullOrEmpty(npcKey) || npcKey.StartsWith(GuestKeyPrefix, System.StringComparison.Ordinal))
        {
            return;
        }

        // #2598: a villager's first entry starts at the floor of the old shared standing (plus the first-meeting lift).
        var rel = EnsureRelationship(player, npcKey, npcName, npcRole);
        rel.LastSeenUtc = WallUtcNowIso(); // #2595: "when did they last see me" — welcome-back reads it

        if (!string.IsNullOrEmpty(npcName))
        {
            rel.Name = npcName; // keep the remembered name/role current
        }

        if (!string.IsNullOrEmpty(npcRole))
        {
            rel.Role = npcRole;
        }

        if (!string.IsNullOrEmpty(place))
        {
            rel.Place = place; // #1118: the "People you know" list shows where they live
        }

        rel.Value = System.Math.Clamp(rel.Value + InteractionWeight(kind), -100, 100);
        rel.Log.Add(new NpcInteraction { Kind = kind });
        if (rel.Log.Count > NpcMemoryLog)
        {
            rel.Log.RemoveRange(0, rel.Log.Count - NpcMemoryLog); // keep only the most recent
        }
    }

    /// <summary>Records that the player took a mission from a board's quartermaster (item 14).</summary>
    private void RecordMissionAccepted(PlayerState player, string missionId, string giverName)
    {
        // #1865: a base board's quartermaster is one of the base's residents — the job goes on that person's memory.
        if (IsBaseMission(missionId) && BaseResidentWithJob(player, "quartermaster") is { } qm)
        {
            RecordNpcInteraction(player, BaseResidentKeyOf(qm), qm.Name, "quartermaster", NpcInteractionKind.MissionAccepted, qm.Settlement);
            return;
        }

        RecordNpcInteraction(player, NpcKey(MemoryLocationOfBoardMission(missionId), "quartermaster"), giverName, "quartermaster", NpcInteractionKind.MissionAccepted, NpcPlaceFor(player));
    }

    /// <summary>Records a barter the player just made at a settlement/station vendor (item 14); no-op aboard ship.</summary>
    private void RecordVendorTrade(PlayerState player)
    {
        string locationKey;
        var vendor = NearestNpc(player, "vendor");
        if (NearBaseVendor(player) && BaseResidentWithJob(player, "vendor") is { } resident)
        {
            // #1865: the trader at home is a resident — the barter goes on that person's memory.
            RecordNpcInteraction(player, BaseResidentKeyOf(resident), resident.Name, "vendor", NpcInteractionKind.Trade, resident.Settlement);
            return;
        }

        if (NearSettlementVendor(player))
        {
            locationKey = SettlementLocationKey(vendor?.Settlement ?? string.Empty);
        }
        else if (NearSpaceStationVendor(player) && _boardedStation.TryGetValue(player.PlayerId, out var stationId))
        {
            locationKey = StationLocationKey(stationId);
        }
        else
        {
            return; // traded at the ship's own console — not an NPC vendor
        }

        // An authored character behind the counter (#1128) banks the trade on their GLOBAL memory; a trading profession
        // (doctor, grocer …) on their own person key (#2598) — only the classic stall keeps the shared vendor key.
        string key = vendor is { CharacterId.Length: > 0 } ? "char:" + vendor.CharacterId
            : vendor is { PersonKey.Length: > 0 } && NearSettlementVendor(player) ? vendor.PersonKey
            : NpcKey(locationKey, "vendor");
        RecordNpcInteraction(player, key, vendor?.Name ?? string.Empty, "vendor", NpcInteractionKind.Trade, NpcPlaceFor(player));
    }

    /// <summary>The nearest live NPC with the given role to a player (same world), or null.</summary>
    private ServerNpc? NearestNpc(PlayerState player, string role)
    {
        ServerNpc? best = null;
        double bestSq = double.MaxValue;
        foreach (var n in _npcs)
        {
            if (n.Role != role)
            {
                continue;
            }

            double d = WrapDistSq(player.Position, n.Pos);
            if (d < bestSq)
            {
                bestSq = d;
                best = n;
            }
        }

        return best;
    }

    /// <summary>An NPC's relationship with a player (item 15 input) — score + recent log + name/role; null if none.</summary>
    public NpcRelationship? NpcRelationshipFor(string playerId, string npcKey)
        => FindSessionByPlayerId(playerId) is { } s && s.State.NpcMemory.TryGetValue(npcKey, out var rel) ? rel : null;
}
