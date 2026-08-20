// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Story;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Environmental lore texts (#1111): rune inscriptions, wreck logs, ruin notes and vault plaques — readable
/// texts the world itself carries, data-driven per site kind from the active story pack's
/// <c>lore_sites.json</c>. A text is revealed when a player scans a monument or loots a lore-bearing site,
/// gated by the world's knowledge level (no spoilers ahead of the story), deduped and persisted per player
/// (<c>PlayerState.Milestones</c> "lore:&lt;key&gt;") so the Codex can list what THIS player has read.
/// </summary>
public sealed partial class GameServer
{
    private readonly System.Random _loreRng = new(0x10FE);

    /// <summary>Reveals one still-unread lore text of a site kind to a player, or does nothing when the pack
    /// has no (eligible) text left for them there. Weighted pick, knowledge-gated, once per player per text.</summary>
    private void TryRevealLoreText(PlayerSession session, string site)
    {
        if (!StoryActive || _story is null || _story.LoreSites.Count == 0 || string.IsNullOrEmpty(site))
        {
            return;
        }

        int knowledge = WorldKnowledgeLevel();
        var p = session.State;
        var eligible = new List<LoreSite>();
        int totalWeight = 0;
        foreach (var l in _story.LoreSites)
        {
            if (!string.Equals(l.Site, site, System.StringComparison.OrdinalIgnoreCase)
                || l.MinKnowledge > knowledge
                || string.IsNullOrEmpty(l.Key)
                || p.Milestones.Contains(LoreMilestonePrefix + l.Key))
            {
                continue;
            }

            eligible.Add(l);
            totalWeight += System.Math.Max(1, l.Weight);
        }

        if (eligible.Count == 0)
        {
            return; // this player has read everything the site (currently) has to say
        }

        int roll = _loreRng.Next(totalWeight);
        var pick = eligible[eligible.Count - 1];
        foreach (var l in eligible)
        {
            roll -= System.Math.Max(1, l.Weight);
            if (roll < 0)
            {
                pick = l;
                break;
            }
        }

        p.Milestones.Add(LoreMilestonePrefix + pick.Key);
        _repo.SavePlayer(p);
        Send(session, new LoreTextRevealed { Key = pick.Key, Site = pick.Site, TextKey = pick.TextKey });
    }

    /// <summary>The lore-site kind of a structure-loot container, derived from its id
    /// ("loot_&lt;structureKind&gt;_&lt;markerType&gt;_x_y_z", see <see cref="SpawnStructureLoot"/>) — or empty
    /// for player-made and non-lore containers. A data terminal reads as its own site regardless of whether
    /// it sits in a wreck or a vault (it is the text-bearing furniture).</summary>
    internal static string LoreSiteOfContainer(string containerId)
    {
        if (!containerId.StartsWith("loot_", System.StringComparison.Ordinal))
        {
            return string.Empty;
        }

        string rest = containerId.Substring("loot_".Length);

        // The one-of-a-kind sites (#1129) OWN their lore voice — checked before the generic terminal
        // sniff so the observatory's survey terminal speaks as the observatory, not as "a terminal".
        foreach (var kind in new[] { "alien_shrine", "observatory", "derelict" })
        {
            if (rest.StartsWith(kind + "_", System.StringComparison.Ordinal))
            {
                return kind;
            }
        }

        if (containerId.Contains("_data_terminal_", System.StringComparison.Ordinal))
        {
            return "data_terminal";
        }

        foreach (var kind in new[] { "bandit_camp", "settlement", "monument", "factory", "chest", "wreck", "vault", "ruin" })
        {
            if (rest.StartsWith(kind + "_", System.StringComparison.Ordinal))
            {
                return kind;
            }
        }

        return string.Empty;
    }

    // ---------------- Test hooks ----------------

    /// <summary>Test hook: run the lore reveal for a player + site exactly like a scan/loot would.</summary>
    public void RevealLoreForTest(string playerId, string site)
    {
        if (FindSessionByPlayerId(playerId) is { } session)
        {
            TryRevealLoreText(session, site);
        }
    }

    /// <summary>Test hook: the lore keys this player has read ("lore:" milestones, prefix stripped).</summary>
    public IReadOnlyList<string> FoundLoreForTest(string playerId)
        => FindSessionByPlayerId(playerId) is { } s
            ? s.State.Milestones.Where(m => m.StartsWith(LoreMilestonePrefix, System.StringComparison.Ordinal))
                .Select(m => m.Substring(LoreMilestonePrefix.Length)).OrderBy(m => m, System.StringComparer.Ordinal).ToList()
            : new List<string>();
}
