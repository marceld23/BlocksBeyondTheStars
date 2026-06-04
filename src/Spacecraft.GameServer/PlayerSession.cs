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

    /// <summary>The celestial-body id of the world this player is currently in. With multi-world, each
    /// player can be on a different body; the server sets <c>_worlds.Active</c> to this before handling the
    /// player's messages / streaming their chunks. Empty until the join places them.</summary>
    public string CurrentLocationId { get; set; } = string.Empty;

    /// <summary>Environment.TickCount of the last accepted chat line (rate limiting).</summary>
    public int LastChatTick { get; set; }

    /// <summary>Chunks already streamed to this client, to avoid resending.</summary>
    public HashSet<ChunkCoord> SentChunks { get; } = new();

    /// <summary>Short rolling history of the player's recent state (for /bump diagnostics).</summary>
    public List<BumpSample> History { get; } = new();
    public double SinceHistorySample;

    // Avatar colours (packed 0xRRGGBB) relayed to other players. Sensible defaults until set.
    public int SkinColor { get; set; } = 0xD9AE8C;
    public int TorsoColor { get; set; } = 0x3372CC;
    public int ArmColor { get; set; } = 0x3372CC;
    public int LegColor { get; set; } = 0x40404F;

    public PlayerSession(int connectionId, PlayerState state)
    {
        ConnectionId = connectionId;
        State = state;
    }
}
