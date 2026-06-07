using Spacecraft.Shared.State;
using Spacecraft.WorldGeneration;

namespace Spacecraft.GameServer;

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

    /// <summary>The settlement board's stable location id (matches the settle_&lt;hash&gt; mission prefix).</summary>
    private string SettlementLocationKey() => $"settle_{(uint)WorldGenerator.StableHash(_settlementName) % 100000u}";

    private static string StationLocationKey(string stationId) => $"station_{(uint)WorldGenerator.StableHash(stationId) % 100000u}";

    /// <summary>The board location id from a board mission id (strips the trailing _&lt;slot&gt;).</summary>
    private static string LocationKeyOfMission(string missionId)
    {
        int i = missionId.LastIndexOf('_');
        return i > 0 ? missionId[..i] : missionId;
    }

    /// <summary>Records an interaction in an NPC's memory of this player (item 14): raises the relationship by a
    /// per-kind weight and appends to the capped last-N log. Persists with the player.</summary>
    private void RecordNpcInteraction(PlayerState player, string npcKey, string npcName, string npcRole, NpcInteractionKind kind)
    {
        if (string.IsNullOrEmpty(npcKey))
        {
            return;
        }

        if (!player.NpcMemory.TryGetValue(npcKey, out var rel))
        {
            rel = new NpcRelationship { Name = npcName, Role = npcRole };
            player.NpcMemory[npcKey] = rel;
        }

        if (!string.IsNullOrEmpty(npcName))
        {
            rel.Name = npcName; // keep the remembered name/role current
        }

        if (!string.IsNullOrEmpty(npcRole))
        {
            rel.Role = npcRole;
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
        => RecordNpcInteraction(player, NpcKey(LocationKeyOfMission(missionId), "quartermaster"), giverName, "quartermaster", NpcInteractionKind.MissionAccepted);

    /// <summary>Records a barter the player just made at a settlement/station vendor (item 14); no-op aboard ship.</summary>
    private void RecordVendorTrade(PlayerState player)
    {
        string locationKey;
        if (NearSettlementVendor(player))
        {
            locationKey = SettlementLocationKey();
        }
        else if (NearSpaceStationVendor(player) && _boardedStation.TryGetValue(player.PlayerId, out var stationId))
        {
            locationKey = StationLocationKey(stationId);
        }
        else
        {
            return; // traded at the ship's own console — not an NPC vendor
        }

        var vendor = NearestNpc(player, "vendor");
        RecordNpcInteraction(player, NpcKey(locationKey, "vendor"), vendor?.Name ?? string.Empty, "vendor", NpcInteractionKind.Trade);
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
