using Spacecraft.Shared.State;
using Spacecraft.Shared.World;

namespace Spacecraft.GameServer;

/// <summary>Per-connection server-side player session bookkeeping.</summary>
public sealed class PlayerSession
{
    public int ConnectionId { get; }
    public PlayerState State { get; }

    /// <summary>True once the player has completed the join handshake.</summary>
    public bool Joined { get; set; }

    /// <summary>Chunks already streamed to this client, to avoid resending.</summary>
    public HashSet<ChunkCoord> SentChunks { get; } = new();

    public PlayerSession(int connectionId, PlayerState state)
    {
        ConnectionId = connectionId;
        State = state;
    }
}
