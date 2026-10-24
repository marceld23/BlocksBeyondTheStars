// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.State;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>Per-connection server-side player session bookkeeping.</summary>
public sealed class PlayerSession
{
    public int ConnectionId { get; }
    public PlayerState State { get; }

    /// <summary>True once the player has completed the join handshake.</summary>
    public bool Joined { get; set; }

    /// <summary>Armed whenever the server authoritatively places this player on a surface (join, travel
    /// landing, respawn). While armed, position reports far away from the placed position are dropped —
    /// the client keeps streaming its pre-snap pose (the scene-default transform near the world origin,
    /// or the pre-respawn spot) until it processes the snap, and trusting those reports overwrote the
    /// fresh spawn and entombed new players in the origin column (#865, the root cause of #834).
    /// Cleared by the first report that lands near the authoritative position.</summary>
    public bool AwaitingSpawnAdopt { get; set; }

    /// <summary>#2723: the uptime until which this player has just been put into a ship interior (their own, or a docked
    /// friend's). Every ship interior shares the same coordinates, so a pose the client sends from the interior it is
    /// leaving lies right beside the new heal tank and passes the spawn gate above; until this time a report from
    /// outside the hull is taken as such a stale pose and dropped (the client stands at the heal tank).</summary>
    public double ShipInteriorSettleUntil { get; set; }

    /// <summary>#2001: the uptime of the last accepted move and the ground covered since the last step pulse — a
    /// walking (not sneaking) player shakes the sand every few blocks.</summary>
    public double StepClockAt { get; set; }
    public float StepDistance { get; set; }

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

    /// <summary>The world id (#1534) this client was last told is current — by <c>JoinAccepted</c> or a
    /// <c>WorldReset</c>. The client drops every chunk stamped with another world's id, so a landing that
    /// skips the reset may only happen while this still names the body being landed on (#2117).</summary>
    public int AnnouncedWorldId { get; set; }

    /// <summary>The fixed landing pad this player currently holds on their body (item 38), or -1 if none. Pads
    /// are communal + occupancy is live: a pad counts as taken only while its holder is on the body (not in
    /// space). Set when landing; superseded on the next landing; ignored once the player is in space/elsewhere.
    /// Mirrors <see cref="State"/>.<c>LandingPadIndex</c> so it is persisted (#848) — the pad is where the ship
    /// is parked, so a reload that forgot it left the ship on the far side of the planet.</summary>
    public int AssignedPadIndex
    {
        get => State.LandingPadIndex;
        set => State.LandingPadIndex = value;
    }

    /// <summary>#2436: the storage crate whose crate screen this player has open, or null — the server re-sends that
    /// crate's contents after every change for as long as it is set. Transient; cleared when the screen closes or the
    /// crate is gone.</summary>
    public string? OpenContainerId { get; set; }

    /// <summary>Fleet admin: the operator of this hosting installation, as opposed to the owner of one world
    /// (issue #487). Granted on join from <see cref="Shared.Configuration.ServerConfig.FleetAdminPlayers"/> and
    /// deliberately <b>never persisted</b> — see that property for why a saved role would leak between worlds.
    /// The only gate for the invisible observer mode.</summary>
    public bool IsFleetAdmin { get; set; }

    /// <summary>Observer mode (issue #487): this player is invisible to everyone — no presence, no avatar, no
    /// nameplate — leaves no footprint in the world (no parked ship, no landing pad, no fauna) and is ignored by
    /// creatures. Session-scoped, never persisted: a crash or reconnect always drops back to normal play, which
    /// is the safe default. Only ever set for <see cref="IsFleetAdmin"/> sessions.</summary>
    public bool Spectating { get; set; }

    /// <summary>Chat rate limit, anti-spam windows and report evidence of this connection (#1208, #1222).</summary>
    public PlayerSessionChat Chat { get; } = new();

    /// <summary>The arcade install id this client joined with (glitch.fun guests), or empty. It is the only
    /// identity such a guest has, so a report is worth nothing to an operator without it.</summary>
    public string InstallId { get; set; } = string.Empty;

    /// <summary>Rate limits and token buckets (appearance, effects, voice, paint, pastes, toasts, message flood).</summary>
    public PlayerSessionThrottles Throttles { get; } = new();

    /// <summary>Where the entombment rescue last moved this player during the CURRENT episode of being sealed
    /// in (#1367), or null when they are out in the open. A rescue whose destination is the same blocked spot
    /// as last time is skipped instead of re-teleporting and re-announcing every second.</summary>
    public Shared.Geometry.Vector3f? EntombedRescueSpot { get; set; }

    /// <summary>What was streamed to this client: chunks, the settled view, the look-ahead, far-terrain tiles.</summary>
    public PlayerSessionStreaming Streaming { get; } = new();

    /// <summary>#2960: the arrival burst of wildlife is due for this player (set by the join and by a landing), and how
    /// long it has waited so far for the ground of the spawn ring to stream in. See <c>GameServerFaunaSeed</c>.</summary>
    public bool FaunaSeedDue { get; set; }
    public double FaunaSeedWaited { get; set; }

    /// <summary>The app-level heartbeat and this player's pause-menu hold (#964, #973).</summary>
    public PlayerSessionLiveness Liveness { get; } = new();

    /// <summary>The client's requested render distance in chunks (from its JoinRequest), or 0 if it didn't say.
    /// When set, it drives this player's streaming radius (clamped server-side) instead of the host's config —
    /// so the in-game View Distance slider extends the actually-streamed terrain on dedicated servers too, not
    /// only the local fog. 0 = fall back to <see cref="ServerConfig.ViewDistanceChunks"/>.</summary>
    public int ViewDistance { get; set; }

    /// <summary>Short rolling history of the player's recent state (for /bump diagnostics).</summary>
    public List<BumpSample> History { get; } = new();
    public double SinceHistorySample;

    // --- Per-player ship fleet (P4: one ship per player, no crew) ---
    // Each player owns their own ships and one is active (flown + stamped into their world). The server
    // serves a player by pointing its ship cursor at this fleet. Empty until the join sets it up.

    /// <summary>This player's owned ships, keyed by ship id.</summary>
    public Dictionary<string, ShipState> Ships { get; } = new();

    /// <summary>The id of this player's active ship (the one flown + stamped). Mirrors
    /// <see cref="State"/>.<c>ActiveShipId</c> so a reload hands back the ship they were flying (#848).</summary>
    public string ActiveShipId
    {
        get => State.ActiveShipId;
        set => State.ActiveShipId = value;
    }

    // Avatar colours (packed 0xRRGGBB) relayed to other players. Sensible defaults until set.
    public int SkinColor { get; set; } = 0xD9AE8C;
    public int TorsoColor { get; set; } = 0x3372CC;
    public int ArmColor { get; set; } = 0x3372CC;
    public int LegColor { get; set; } = 0x40404F;

    /// <summary>Ship hull colour (packed 0xRRGGBB), relayed so other players see this player's ship tinted
    /// (item 32). Default = the steel tint the ship hull used before hull colours existed.</summary>
    public int HullColor { get; set; } = 0xD1D6E0;

    // --- Ship AI companion "VEGA" session bookkeeping (persisted progress lives in State.Milestones) ---

    /// <summary>#2460 (B01): the player as they arrived — the newcomer-vs-veteran decision and the story keys
    /// they carried — taken once at the very top of the join, before anything in the join pays knowledge or
    /// blueprints. Session-scoped; see <see cref="JoinSnapshot"/>.</summary>
    public JoinSnapshot Join { get; set; } = JoinSnapshot.Empty;

    /// <summary>#2465: true while the join itself places the player (ship setup, the spawn body counted as visited, a
    /// station re-boarded, a flight resumed). Game events (<c>GameServer.RaiseGameEvent</c>) raise nothing until the
    /// join is through — being put somewhere is not something the player did.</summary>
    public bool InJoinPlacement { get; set; }

    /// <summary>#2465: server uptime of the last <c>insight:companion_ward</c> game event — the ward holds on every enemy
    /// tick, so the event is raised at most once per <c>GameServer.InsightEventCooldownSeconds</c>.</summary>
    public double CompanionWardEventAt { get; set; } = double.NegativeInfinity;

    /// <summary>#2466: the <c>JoinRequest.Features</c> words of this client (<c>packs1</c>, <c>cutscenes1</c>). A local
    /// player (<c>AddLocalPlayer</c>) runs in this build and gets <c>packs1</c>. Session-scoped.</summary>
    public HashSet<string> ClientFeatures { get; } = new(System.StringComparer.Ordinal);

    /// <summary>#2466: the client understands story packs (<c>packs1</c>); without it the legacy tutorial mode applies
    /// (old keys, server-composed text, no pack chips).</summary>
    public bool SupportsPacks => ClientFeatures.Contains(Networking.Messages.ClientFeatures.Packs1);

    /// <summary>Story-pack, Journal and cutscene pacing (#2466, #2467, #2471, #2475, #2499).</summary>
    public PlayerSessionStory Story { get; } = new();

    /// <summary>#2467: the client plays data-driven cutscenes (<c>cutscenes1</c>); without it a scene counts as seen at
    /// once and the narrator line alone carries it.</summary>
    public bool SupportsCutscenes => ClientFeatures.Contains(Networking.Messages.ClientFeatures.Cutscenes1);

    /// <summary>#2499: the client draws a VEGA line's buttons (<c>choices1</c>, <c>ShipAiLine.Choices</c>); without it the
    /// first-start question is never asked and a course offer is the plain line.</summary>
    public bool SupportsChoices => ClientFeatures.Contains(Networking.Messages.ClientFeatures.Choices1);

    /// <summary>#2616: the client's dialogue panel takes the server's close and sends the leave (<c>dialogclose1</c>);
    /// without it no close is sent (an older panel would pop open again to show it).</summary>
    public bool SupportsDialogClose => ClientFeatures.Contains(Networking.Messages.ClientFeatures.DialogClose1);

    /// <summary>Tutorial step counters and the moments its courses are offered at (#2469, #2480, #2482).</summary>
    public PlayerSessionTutorial Tutorial { get; } = new();

    /// <summary>VEGA's pacing: advisor, context tips, chatter, welcome back and praise.</summary>
    public PlayerSessionVega Vega { get; } = new();

    /// <summary>#2082: favourite meals this player has thrown toward the next feed-tame, per herd ("location:species"). Transient,
    /// like the wild animals it counts for.</summary>
    public Dictionary<string, int> FavouriteMeals { get; } = new();

    /// <summary>The suit lamp is switched on (client-reported via <c>SetLampIntent</c>; the server only sees
    /// whether the lamp is CARRIED otherwise). Session-scoped — a rejoin starts with the lamp off, like the client.</summary>
    public bool LampOn { get; set; }

    /// <summary>Zero-g construction mode on the boarded player-built station (#1842): the suit floats everywhere
    /// on the station, not only beyond its gravity volume. Per player, session-only and deliberately never
    /// persisted — it is cleared on leaving the station, on any world change and on disconnect, so a rejoin
    /// always lands walking. Any boarder of a player station may set it for themselves.</summary>
    public bool StationZeroG { get; set; }

    /// <summary>Server uptime (seconds) at which <see cref="StationZeroG"/> was last switched OFF, or negative
    /// infinity. A fall reported within the grace window after that is not a fall: the player was hovering
    /// in zero-g when the gravity came back, and the drop to the deck is the mode's doing, not theirs.</summary>
    public double StationZeroGOffAt { get; set; } = double.NegativeInfinity;

    /// <summary>NPC radio calls, dialogue rewards, the open conversation, wishes and passing greetings.</summary>
    public PlayerSessionNpc Npc { get; } = new();

    /// <summary>Uptime before which the player's companions do not growl at them again (#1210).</summary>
    public double NextCompanionAlertAt { get; set; }

    /// <summary>The pending deferred death-respawn choice (#462).</summary>
    public PlayerSessionRespawn Respawn { get; } = new();

    /// <summary>The pending automatic landed-ship transit (#1614).</summary>
    public PlayerSessionTransit Transit { get; } = new();

    /// <summary>The pending bandit hold-up and the ambush and briefing pacing.</summary>
    public PlayerSessionBandit Bandit { get; } = new();

    /// <summary>Hazard scans: heal tank and bed nearby, temperature, exposure, toxic water, Valuma's mood.</summary>
    public PlayerSessionHazards Hazards { get; } = new();

    /// <summary>#2264: server uptime until which a fall does not hurt — a phase block, a trapdoor or a bridge deck opened
    /// under this player (kid rule: a moving block never hurts).</summary>
    public double MovingFallGraceUntil { get; set; }

    /// <summary>#2276: set when the player sinks back below a planet's atmosphere line, cleared at the first landing
    /// (a reported fall is swallowed, or the feet rest on ground for a moment). Full gravity returns at the line, so the
    /// way back down from above it must never end in a fatal fall — the suit brakes the descent.</summary>
    public bool ReentryFallGrace { get; set; }

    /// <summary>#2276: how long the player has stood on ground since <see cref="ReentryFallGrace"/> was set — the grace
    /// ends after a short settle, so the fall report that rides in with the landing still finds it.</summary>
    public double ReentryGroundedSeconds { get; set; }

    // --- "Back to my ship" gates (#2286) ---

    /// <summary>Gate clock (server uptime plus the time the world stood held, #2558) of the last blow this player was part
    /// of on foot — a hit they landed on a creature, a machine or a bandit, or damage one of those did to them. "In a
    /// fight" for <c>ReturnToShipRules.CombatGraceSeconds</c> after it. Negative infinity = never.</summary>
    public double LastCombatAt { get; set; } = double.NegativeInfinity;

    /// <summary>Gate clock (as <see cref="LastCombatAt"/>) of the last position report that advanced it (several reports
    /// within one tick share it; the first one measures the speed). 0 = none yet.</summary>
    public double LastMoveAt { get; set; }

    /// <summary>Vertical speed (blocks per second, + up) between the last two position reports in different ticks — the
    /// server's reading of whether this player is falling right now (the client owns on-foot movement and reports only
    /// positions). Reset when the player is set down somewhere by the server.</summary>
    public float VerticalSpeed { get; set; }

    // --- Station affordances (#1070/#1072) ---

    /// <summary>What this client was last told: stations in reach, vitals, last blow, presence, blueprints, samples.</summary>
    public PlayerSessionSync Sync { get; } = new();

    /// <summary>Server uptime of the last answered LocateStationIntent (rate limit).</summary>
    public double LastStationLocateAt { get; set; } = -1;

    /// <summary>Server uptime when this player was last told that a flowing quench hardens only the surface
    /// of a lava body (#1727). A flood over a trench crusts hundreds of cells in a few ticks — one sentence
    /// per episode, not one per block.</summary>
    public double LastFlowQuenchTold { get; set; } = double.NegativeInfinity;

    public PlayerSession(int connectionId, PlayerState state)
    {
        ConnectionId = connectionId;
        State = state;
    }
}
