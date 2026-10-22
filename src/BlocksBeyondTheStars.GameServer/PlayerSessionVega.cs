// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>Per-connection VEGA companion bookkeeping of a <see cref="PlayerSession"/>
/// (RAM only, never persisted).</summary>
public sealed class PlayerSessionVega
{
    /// <summary>#2462 (B31): the client has VEGA's hints switched off (<c>SetNpcCallsIntent.VegaHintsMuted</c>, sent at
    /// every join and from the settings). The advisor hints and context tips are then held back instead of being sent
    /// to a muted panel — sending burned their once-flags, so they never came back. Session-scoped, starts "on".</summary>
    public bool VegaHintsMuted { get; set; }

    /// <summary>Accumulator for the 1 Hz advisor poll.</summary>
    public double VegaAdvisorAccum { get; set; }

    /// <summary>Uptime gates pacing memory-fragment redemption and the space callouts.</summary>
    public double VegaMemoryReadyAt { get; set; }

    /// <summary>Set once VEGA has said "no room for the Mk3 parts" this session, so the final memory fragment
    /// waits quietly instead of nagging every tick (#1104).</summary>
    public bool VegaMemoryHoldFullWarned { get; set; }

    /// <summary>#2009: VEGA has pointed out an arachnid to this player (once per session — a sighting, not a nag).</summary>
    public bool ArachnidSighted { get; set; }

    public double VegaThreatReadyAt { get; set; }
    public double VegaEvadeReadyAt { get; set; }

    /// <summary>Uptime of the next LLM banter check (0 = not armed yet; armed on the first poll).</summary>
    public double VegaBanterNextAt { get; set; }

    // --- Context tips (#1077): throttled, repeatable advisor lines. Persisted counters live in
    // State.Milestones ("vega:hint:<id>", "#2", "#done"); everything here is per-connection pacing state. ---

    /// <summary>Uptime before which VEGA says no further context tip (global cadence, shared with banter).</summary>
    public double VegaTipReadyAt { get; set; }

    /// <summary>Per tip id: uptime the condition first became true in the current run (dwell / hysteresis).</summary>
    public Dictionary<string, double> VegaTipSince { get; } = new();

    /// <summary>#2479: the tip ids whose situation held in the last 1 Hz tip pass (before the hints switch, the gates, the
    /// dwell and the cooldowns) — the "moment of need" a tutorial deep dive is offered at (<c>world:data_cache_near</c> …).</summary>
    public IReadOnlyCollection<string> VegaTipLive { get; set; } = System.Array.Empty<string>();

    /// <summary>Per tip id: uptime before which the same tip does not repeat.</summary>
    public Dictionary<string, double> VegaTipCooldownUntil { get; } = new();

    /// <summary>The last context tip fired + when — the "learned" window (a reaction shortly after
    /// the tip retires it for good).</summary>
    public string VegaTipLastId { get; set; } = string.Empty;
    public double VegaTipLastAt { get; set; }

    /// <summary>Things already mentioned this session (settlement names, ore keys, recipe keys, player
    /// names …) so a tip that repeats never names the same thing twice in one sitting.</summary>
    public HashSet<string> VegaTipMentioned { get; } = new();

    /// <summary>The player's <c>PlayerState.LastSeenUtc</c> as it was BEFORE this join overwrote it (#2595): when they
    /// were last online — what VEGA's "welcome back after a long time" reads. "" for a first join or an old save.</summary>
    public string PreviousSeenUtc { get; set; } = string.Empty;

    // --- #2593/#2595/#2597: the world greets back (passing hellos, VEGA's welcome and praise). RAM only. ---

    /// <summary>#2595: VEGA's chatter state was seeded on the first 1 Hz pass after the join (aboard flag, login welcome).</summary>
    public bool VegaChatterSeeded { get; set; }

    /// <summary>#2595: whether the player was aboard the ship on the last 1 Hz pass — boarding is the edge false → true.</summary>
    public bool VegaWasAboard { get; set; }

    /// <summary>#2595: uptime the player last stepped off the ship (NaN = not outside since the join or the last respawn).</summary>
    public double VegaLeftShipAt { get; set; } = double.NaN;

    /// <summary>#2595: uptime before which VEGA does not say "welcome back aboard" again (30 min).</summary>
    public double VegaWelcomeAboardReadyAt { get; set; }

    /// <summary>#2595: uptime VEGA's "welcome back after a long time" is due (0 = none pending).</summary>
    public double VegaWelcomeLoginDueAt { get; set; }

    /// <summary>#2597: achievements unlocked within the coalescing window, waiting for VEGA's praise (keys).</summary>
    public List<string> PraisePending { get; } = new();

    /// <summary>#2597: uptime the pending praise is spoken (the first unlock + 2 s).</summary>
    public double PraiseDueAt { get; set; }

    /// <summary>#2597: uptime before which VEGA praises nothing again (90 s between praise lines).</summary>
    public double VegaPraiseReadyAt { get; set; }

    /// <summary>Uptime of the next (heavier) block-probe around the player; results cached in <see cref="VegaProbe"/>.</summary>
    public double VegaTipProbeAt { get; set; }
    public VegaTipProbe VegaProbe { get; } = new();

    /// <summary>Uptime the terrain scanner was last used (0 = never this session).</summary>
    public double VegaScannerUsedAt { get; set; }

    /// <summary>Blocks broken while holding no drill although one is carried (resets when a drill is held).</summary>
    public int VegaHandMineStreak { get; set; }

    /// <summary>Decaying "is digging right now" score: +1 per broken block, ×0.9 per second.</summary>
    public double VegaMineRecent { get; set; }

    /// <summary>Localized name of the block whose tool-tier gate just turned a swing away (#1686), and the
    /// tool that would break it. Empty until the player runs into a gate; consumed by the <c>tier_gate</c>
    /// context tip on the next cadence slot and cleared once it has been spoken.</summary>
    public string VegaTierGateBlock { get; set; } = string.Empty;

    public string VegaTierGateTool { get; set; } = string.Empty;

    /// <summary>Whether VEGA already explained base scouts (#1224) to this player this session — the toast
    /// repeats per visit, the explanation does not.</summary>
    public bool ScoutsVegaSaid { get; set; }
}
