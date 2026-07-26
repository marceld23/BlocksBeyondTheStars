// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Networking.Messages;

/// <summary>
/// A bandit hails the player and demands part of their goods (server → client). Shown as a modal
/// choice ("hand it over" / "refuse") with a countdown; the client answers with
/// <see cref="BanditResponseIntent"/>. The server enforces the deadline itself (respawn-choice
/// pattern), so a stuck client simply counts as a refusal. Sent both by lone bandits on foot
/// (<c>Source == "foot"</c>) and by bandit ships in space (<c>Source == "ship"</c>).
/// </summary>
public sealed class BanditDemand
{
    /// <summary>Per-encounter id the response must echo (stale responses are dropped).</summary>
    public int DemandId { get; set; }

    /// <summary>Combat-entity id of the demanding bandit (so the client can point a marker at it).</summary>
    public string BanditId { get; set; } = string.Empty;

    /// <summary>"foot" or "ship" — picks the UI host (HUD panel vs space overlay).</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>Coined bandit name for the panel title.</summary>
    public string BanditName { get; set; } = string.Empty;

    /// <summary>Locale key of the demand line (bandit.line.*), localized client-side.</summary>
    public string LineKey { get; set; } = string.Empty;

    /// <summary>Optional pre-localized line (LLM-authored); when set it wins over <see cref="LineKey"/>.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>What the bandit wants handed over.</summary>
    public NetTradeItem[] Demanded { get; set; } = System.Array.Empty<NetTradeItem>();

    /// <summary>Seconds until the server treats silence as a refusal.</summary>
    public int SecondsRemaining { get; set; }
}

/// <summary>The player's answer to a <see cref="BanditDemand"/> (client → server).</summary>
public sealed class BanditResponseIntent
{
    public int DemandId { get; set; }
    public bool Comply { get; set; }
}

/// <summary>
/// A bandit encounter ended (server → client): closes the demand UI and shows the outcome toast.
/// Outcomes: "paid" (items handed over, bandit leaves), "refused" (fight is on), "expired"
/// (deadline hit → treated as refusal), "fled" (bandit lost interest / target left).
/// </summary>
public sealed class BanditEncounterResult
{
    public int DemandId { get; set; }
    public string Outcome { get; set; } = string.Empty;
}
