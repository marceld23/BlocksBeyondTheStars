using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Missions;

namespace BlocksBeyondTheStars.Shared.State;

/// <summary>Permission level (technical requirements / `anf_admin_einstellungen.md` §10–11).</summary>
public enum PlayerRole
{
    Player,
    Moderator,
    Admin,
    WorldAdmin,
}

/// <summary>
/// Authoritative per-player state owned by the server. The client only renders a view
/// of this; it never decides these values itself.
/// </summary>
public sealed class PlayerState
{
    public string PlayerId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    public Vector3f Position { get; set; } = Vector3f.Zero;
    public float Yaw { get; set; }
    public float Pitch { get; set; }

    /// <summary>The celestial-body id of the world this player is on (empty until the join places them).
    /// Persisted so a save/load returns the player to the body they were last on, at <see cref="Position"/>
    /// there — not always the home world. The session mirrors this via <c>PlayerSession.CurrentLocationId</c>.</summary>
    public string CurrentLocationId { get; set; } = string.Empty;

    /// <summary>Where the player respawns — the heal-tank in their ship's Medbay.</summary>
    public Vector3f RespawnPoint { get; set; } = Vector3f.Zero;

    public float Health { get; set; } = 100f;
    public float Oxygen { get; set; } = 100f;
    public float SuitEnergy { get; set; } = 100f;

    /// <summary>Satiation 0..100 (survival): drains over time, refilled by eating; at 0 you starve.</summary>
    public float Hunger { get; set; } = 100f;

    /// <summary>The player's personal inventory.</summary>
    public Inventory Inventory { get; set; } = new(24);

    /// <summary>Currently selected hotbar slot index.</summary>
    public int SelectedHotbarSlot { get; set; }

    /// <summary>Blueprint keys the player has unlocked (gates crafting/building).</summary>
    public HashSet<string> UnlockedBlueprints { get; set; } = new();

    /// <summary>Research knowledge earned by scanning new things. A permanent <b>threshold</b> — unlocking a
    /// blueprint needs <c>KnowledgePoints &gt;= KnowledgeCost</c> but never spends it (item 11), and it can be
    /// taught to other players without losing any.</summary>
    public int KnowledgePoints { get; set; }

    /// <summary>Per-recipient cumulative knowledge this player has already taught (receiverId → points given),
    /// so the same knowledge can't be handed back and forth to inflate totals (item 11). Persisted.</summary>
    public Dictionary<string, int> KnowledgeGivenTo { get; set; } = new();

    /// <summary>What each NPC remembers about this player (item 14): NPC key → relationship score + recent
    /// interaction log. Persisted; feeds item 15's dialog backend.</summary>
    public Dictionary<string, NpcRelationship> NpcMemory { get; set; } = new();

    /// <summary>Subjects already scanned (e.g. "creature:sp0", "block:iron_ore") — only new scans pay knowledge.</summary>
    public HashSet<string> Scanned { get; set; } = new();

    /// <summary>Suit ration dispenser: food loaded here is auto-eaten when hunger runs low. Small capacity.</summary>
    public Inventory RationStore { get; set; } = new(RationStoreSlots);

    /// <summary>Number of slots in the ration dispenser.</summary>
    public const int RationStoreSlots = 5;

    /// <summary>True when the player is currently aboard their ship (enables cargo crafting).</summary>
    public bool AboardShip { get; set; } = true;

    /// <summary>True while the player is on an EVA spacewalk — floating outside the ship in a space
    /// instance. The ship bond (<see cref="AboardShip"/>) stays set, but life support does not apply:
    /// the suit runs on its own air, so oxygen drains until the player boards the ship/station again.</summary>
    public bool InEva { get; set; }

    /// <summary>True while the on-foot player has climbed above the planet's atmosphere into space
    /// (item 10): zero-g float, suit oxygen drains (no air to breathe up here), and a space sky. Cleared
    /// when they descend back below the atmosphere line. Not persisted.</summary>
    public bool AboveAtmosphere { get; set; }

    /// <summary>Permission level; the world creator becomes <see cref="PlayerRole.WorldAdmin"/>.</summary>
    public PlayerRole Role { get; set; } = PlayerRole.Player;

    /// <summary>Name verification: SHA-256 hex of the per-install secret the name's first join presented.
    /// Later joins under this name must present the matching token. Empty = unclaimed (legacy save or a
    /// tokenless client) — the next join that brings a token claims the name. Persisted.</summary>
    public string NameTokenHash { get; set; } = string.Empty;

    /// <summary>Stealth field active (from a stealth suit) — creatures/enemies ignore the player. Not persisted.</summary>
    public bool Stealthed { get; set; }

    /// <summary>Jetpack firing (client-driven) — the server drains suit energy while true. Not persisted.</summary>
    public bool Jetpacking { get; set; }

    // Session cheat toggles (admin only, server-authoritative; not persisted).
    public bool GodMode { get; set; }
    public bool Fly { get; set; }
    public bool InstantBuild { get; set; }

    public bool IsAdmin => Role is PlayerRole.Admin or PlayerRole.WorldAdmin;

    /// <summary>Accepted missions and their progress.</summary>
    public List<MissionProgress> Missions { get; set; } = new();

    /// <summary>One-time progression milestones the ship AI (VEGA) has seen this player reach — onboarding
    /// stages ("vega:stage:N"), advisor once-hints ("vega:hint:&lt;key&gt;") and restored memory fragments
    /// ("vega:mem:N"). Server-authoritative, persisted; never removed once set.</summary>
    public HashSet<string> Milestones { get; set; } = new();

    /// <summary>Celestial bodies this player has physically arrived ON (landed via manual flight, hyperjump
    /// or quick-travel). With the Instant Travel world rule OFF, the menu's quick-travel is limited to these
    /// bodies — a never-visited world must be reached by flying there and landing. Server-authoritative,
    /// persisted. The body the player is currently on always counts even if absent here (legacy saves).</summary>
    public HashSet<string> LandedBodies { get; set; } = new();

    /// <summary>Star systems this player has entered — landed on a body there, or warped in on a hyperjump.
    /// A known system reveals its bodies + mini star map on the travel screen; an unknown one is a single
    /// "jump here" entry until visited. Server-authoritative, persisted. The current system always counts.</summary>
    public HashSet<string> KnownSystems { get; set; } = new();
}
