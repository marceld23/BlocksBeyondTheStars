using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>Per-connection server-side player session bookkeeping.</summary>
public sealed class PlayerSession
{
    public int ConnectionId { get; }
    public PlayerState State { get; }

    /// <summary>True once the player has completed the join handshake.</summary>
    public bool Joined { get; set; }

    /// <summary>The player's UI language ("en"/"de") sent on join (item 15). Server-authored dynamic text — LLM
    /// NPC greetings — is generated in this language. Connection-scoped (not persisted); defaults to English.</summary>
    public string Locale { get; set; } = "en";

    /// <summary>The celestial-body id of the world this player is currently in. With multi-world, each
    /// player can be on a different body; the server sets <c>_worlds.Active</c> to this before handling the
    /// player's messages / streaming their chunks. Empty until the join places them. Mirrors
    /// <see cref="State"/>.<c>CurrentLocationId</c> so it is persisted (restored to the last body on load).</summary>
    public string CurrentLocationId
    {
        get => State.CurrentLocationId;
        set => State.CurrentLocationId = value;
    }

    /// <summary>The fixed landing pad this player currently holds on their body (item 38), or -1 if none. Pads
    /// are communal + occupancy is live: a pad counts as taken only while its holder is on the body (not in
    /// space). Set when landing; superseded on the next landing; ignored once the player is in space/elsewhere.</summary>
    public int AssignedPadIndex { get; set; } = -1;

    /// <summary>Environment.TickCount of the last accepted chat line (rate limiting).</summary>
    public int LastChatTick { get; set; }

    /// <summary>Chunks already streamed to this client, to avoid resending.</summary>
    public HashSet<ChunkCoord> SentChunks { get; } = new();

    /// <summary>Short rolling history of the player's recent state (for /bump diagnostics).</summary>
    public List<BumpSample> History { get; } = new();
    public double SinceHistorySample;

    // --- Per-player ship fleet (P4: one ship per player, no crew) ---
    // Each player owns their own ships and one is active (flown + stamped into their world). The server
    // serves a player by pointing its ship cursor at this fleet. Empty until the join sets it up.

    /// <summary>This player's owned ships, keyed by ship id.</summary>
    public Dictionary<string, ShipState> Ships { get; } = new();

    /// <summary>The id of this player's active ship (the one flown + stamped).</summary>
    public string ActiveShipId { get; set; } = string.Empty;

    // Avatar colours (packed 0xRRGGBB) relayed to other players. Sensible defaults until set.
    public int SkinColor { get; set; } = 0xD9AE8C;
    public int TorsoColor { get; set; } = 0x3372CC;
    public int ArmColor { get; set; } = 0x3372CC;
    public int LegColor { get; set; } = 0x40404F;

    /// <summary>Ship hull colour (packed 0xRRGGBB), relayed so other players see this player's ship tinted
    /// (item 32). Default = the steel tint the ship hull used before hull colours existed.</summary>
    public int HullColor { get; set; } = 0xD1D6E0;

    // --- Ship AI companion "VEGA" session bookkeeping (persisted progress lives in State.Milestones) ---

    /// <summary>Blocks mined toward the onboarding "mine" stage (session-scoped; the target is tiny).</summary>
    public int VegaMineCount { get; set; }

    /// <summary>Accumulator for the 1 Hz advisor poll.</summary>
    public double VegaAdvisorAccum { get; set; }

    /// <summary>Uptime gates pacing memory-fragment redemption and the space callouts.</summary>
    public double VegaMemoryReadyAt { get; set; }
    public double VegaThreatReadyAt { get; set; }
    public double VegaEvadeReadyAt { get; set; }

    /// <summary>Uptime of the next LLM banter check (0 = not armed yet; armed on the first poll).</summary>
    public double VegaBanterNextAt { get; set; }

    // --- Periodic vitals sync (HUD bars froze between event-driven sends before) ---
    public double VitalsSyncTimer { get; set; }
    public float LastSentHealth = 100f;
    public float LastSentOxygen = 100f;
    public float LastSentEnergy = 100f;
    public float LastSentHunger = 100f;

    public PlayerSession(int connectionId, PlayerState state)
    {
        ConnectionId = connectionId;
        State = state;
    }
}
