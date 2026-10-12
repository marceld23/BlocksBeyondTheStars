// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;

namespace BlocksBeyondTheStars.Shared.Story;

/// <summary>
/// The lead vocabulary v1 of a world story's chapters (#2475, synthesis §4.8 option O2: "the score is the clock, the chapter
/// leads are the compass"). A lead is one data word in a chapter's <see cref="StoryChapter.Leads"/> list — optionally with an
/// argument after a colon (<c>unique_site:observatory</c>, <c>research:radar_array</c>, <c>module:jump_generator</c>). The
/// server resolves each lead per player (what is feasible here, where it points) only when something relevant changed and
/// shows the first open, feasible one on VEGA's objective chip; the Journal lists the chapter's leads as tasks. A lead never
/// blocks a beat — beats still reveal by score.
/// </summary>
public static class StoryLeadTypes
{
    /// <summary>An unfound net fragment on the player's current world (its signal on the map, as before).</summary>
    public const string Fragment = "fragment";

    /// <summary>One of the galaxy's one-of-a-kind places (<c>unique_site:observatory|shrine|derelict</c>) — feasible only when
    /// the site exists in this galaxy (a world stamped before the sites were added skipped it) and the player can reach it.</summary>
    public const string UniqueSite = "unique_site";

    /// <summary>A rune monument on the player's current world whose runes they have not read yet.</summary>
    public const string Monument = "monument";

    /// <summary>A settlement on the player's current world: finish a job from a board.</summary>
    public const string SettlementHelp = "settlement_help";

    /// <summary>Drive back the Guardian machines — only on a world whose rules field any.</summary>
    public const string Machines = "machines";

    /// <summary>Stand at the heal tank aboard the player's own landed ship.</summary>
    public const string HealTank = "heal_tank";

    /// <summary>Research a blueprint (<c>research:&lt;blueprint&gt;</c>).</summary>
    public const string Research = "research";

    /// <summary>Have a ship module fitted (<c>module:&lt;module&gt;</c>).</summary>
    public const string Module = "module";

    /// <summary>Confront the Guardian core (never muted) — or, without a jump generator, <see cref="FinaleJump"/>.</summary>
    public const string Finale = "finale";

    /// <summary>"Fit a jump generator, then fly to the Core" (never muted).</summary>
    public const string FinaleJump = "finale_jump";

    /// <summary>After the win: link the stars with relays.</summary>
    public const string Relay = "relay";

    /// <summary>The fallback: explore — new systems and settlement jobs help (its text follows the world's rules).</summary>
    public const string Explore = "explore";

    /// <summary>Every lead word of v1.</summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        Fragment, UniqueSite, Monument, SettlementHelp, Machines, HealTank, Research, Module, Finale, FinaleJump, Relay, Explore,
    };

    /// <summary>The words that need an argument after the colon.</summary>
    public static readonly IReadOnlyList<string> WithArgument = new[] { UniqueSite, Research, Module };

    /// <summary>The one-of-a-kind sites a <see cref="UniqueSite"/> lead can name.</summary>
    public static readonly IReadOnlyList<string> UniqueSiteKinds = new[] { "observatory", "shrine", "derelict" };

    /// <summary>What stands in when a chapter's own leads are done or not feasible here (synthesis d2 §3.6, in this order),
    /// before <see cref="Explore"/> — each only when the chapter does not list it itself.</summary>
    public static readonly IReadOnlyList<string> Fallbacks = new[] { Fragment, SettlementHelp };

    /// <summary>The word of a lead type (<c>unique_site</c> of <c>unique_site:observatory</c>).</summary>
    public static string KindOf(string? type)
    {
        if (string.IsNullOrEmpty(type))
        {
            return string.Empty;
        }

        int colon = type!.IndexOf(':');
        return colon < 0 ? type : type.Substring(0, colon);
    }

    /// <summary>The argument of a lead type (<c>observatory</c> of <c>unique_site:observatory</c>), or empty.</summary>
    public static string ArgOf(string? type)
    {
        if (string.IsNullOrEmpty(type))
        {
            return string.Empty;
        }

        int colon = type!.IndexOf(':');
        return colon < 0 ? string.Empty : type.Substring(colon + 1);
    }

    /// <summary>Whether a lead word is one of the finale's — those are never muted (source <c>finale</c>).</summary>
    public static bool IsFinale(string? kind) => kind == Finale || kind == FinaleJump;

    /// <summary>What is wrong with a lead type, or null when it is valid (the validators' rule).</summary>
    public static string? Problem(string? type)
    {
        string kind = KindOf(type);
        string arg = ArgOf(type);
        if (kind.Length == 0)
        {
            return "the lead has no type";
        }

        if (!All.Contains(kind))
        {
            return $"unknown lead type '{kind}' ({string.Join(", ", All)})";
        }

        bool needsArg = WithArgument.Contains(kind);
        if (needsArg && arg.Length == 0)
        {
            return $"the lead type '{kind}' needs an argument ('{kind}:<id>')";
        }

        if (!needsArg && type!.IndexOf(':') >= 0)
        {
            return $"the lead type '{kind}' takes no argument";
        }

        if (kind == UniqueSite && !UniqueSiteKinds.Contains(arg))
        {
            return $"unknown unique site '{arg}' ({string.Join(", ", UniqueSiteKinds)})";
        }

        return null;
    }
}

/// <summary>
/// The rules around a world story's leads (#2475): the effective lead list of a chapter, the chip/Journal text keys and the
/// once-per-save bonus keys. Pure, so the server, the client and the validators share them.
/// </summary>
public static class StoryLeads
{
    /// <summary>The prefix of every lead text key (synthesis §5.3 "Lead-Schlüssel"; the VEGA pack locale). An older client gets
    /// the old <c>story.obj.*</c> keys instead.</summary>
    public const string ObjPrefix = "story.vega.obj.";

    /// <summary>The chip text of a fragment lead on a named world (<c>{0}</c> = the world).</summary>
    public const string FragmentOnKey = ObjPrefix + "fragment";

    /// <summary>The chip text of a fragment lead on a world the galaxy has no name for.</summary>
    public const string FragmentHereKey = ObjPrefix + "fragment_here";

    /// <summary>The fallback's text on a world with machines.</summary>
    public const string ExploreKey = ObjPrefix + "explore";

    /// <summary>The fallback's text on a world without machines (the rules-aware variant).</summary>
    public const string ExplorePeacefulKey = ObjPrefix + "explore_peaceful";

    /// <summary>The finale's text.</summary>
    public const string FinaleKey = ObjPrefix + "finale";

    /// <summary>The finale's text for a player whose ship has no jump generator yet.</summary>
    public const string FinaleJumpKey = ObjPrefix + "finale_jump";

    /// <summary>The once-per-save bonus key of a chapter in the story state's <c>MilestoneKeys</c> — one key per bonus milestone
    /// (<c>lead:&lt;chapter&gt;:1..n</c>, synthesis §4.8), so the existing once-key bookkeeping counts them and an older build
    /// keeps them untouched.</summary>
    public static string BonusKey(string chapterId, int number)
        => "lead:" + chapterId + ":" + number.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>A chapter's effective leads in order: its own list, then the <see cref="StoryLeadTypes.Fallbacks"/> it does not
    /// list itself (their number continues the list, so the per-player keys stay stable for given data). <see cref="StoryLeadTypes.Explore"/>
    /// is not part of it — it is what the chip shows when nothing here is open.</summary>
    public static List<StoryLead> EffectiveLeads(StoryChapter? chapter)
    {
        var leads = new List<StoryLead>();
        if (chapter is null)
        {
            return leads;
        }

        var kinds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var lead in chapter.Leads)
        {
            if (lead is null || StoryLeadTypes.Problem(lead.Type) is not null)
            {
                continue; // the validators report it; the runtime skips it
            }

            leads.Add(lead);
            kinds.Add(StoryLeadTypes.KindOf(lead.Type));
        }

        foreach (string fallback in StoryLeadTypes.Fallbacks)
        {
            if (!kinds.Contains(fallback))
            {
                leads.Add(new StoryLead { Type = fallback });
            }
        }

        return leads;
    }

    /// <summary>The number of declared leads of a chapter that survive validation — the leads after them are fallbacks.</summary>
    public static int DeclaredCount(StoryChapter? chapter)
    {
        int n = 0;
        foreach (var lead in chapter?.Leads ?? new List<StoryLead>())
        {
            if (lead is not null && StoryLeadTypes.Problem(lead.Type) is null)
            {
                n++;
            }
        }

        return n;
    }

    /// <summary>
    /// The chip / Journal text key of a lead: the pack's own <see cref="StoryLead.ObjKey"/> when it sets one, else the built-in
    /// key of its word. The rules-aware variants always win (a pack text cannot know them): a fragment on a world without a name
    /// (<paramref name="hasPlace"/> false), the finale without a jump generator (<paramref name="needsJump"/>).
    /// </summary>
    public static string ObjKey(StoryLead lead, bool hasPlace, bool needsJump)
    {
        string kind = StoryLeadTypes.KindOf(lead.Type);
        string arg = StoryLeadTypes.ArgOf(lead.Type);
        if (kind == StoryLeadTypes.Fragment && !hasPlace)
        {
            return FragmentHereKey;
        }

        if ((kind == StoryLeadTypes.Finale && needsJump) || kind == StoryLeadTypes.FinaleJump)
        {
            return FinaleJumpKey;
        }

        if (!string.IsNullOrEmpty(lead.ObjKey))
        {
            return lead.ObjKey;
        }

        return kind switch
        {
            StoryLeadTypes.Fragment => FragmentOnKey,
            StoryLeadTypes.UniqueSite => ObjPrefix + "site." + arg,
            StoryLeadTypes.Finale => FinaleKey,
            _ => ObjPrefix + kind,
        };
    }

    /// <summary>The fallback's key for the world's rules: with machines or without (<see cref="ExploreKey"/> /
    /// <see cref="ExplorePeacefulKey"/>).</summary>
    public static string ExploreObjKey(bool machinesActive) => machinesActive ? ExploreKey : ExplorePeacefulKey;
}

/// <summary>What else unlocks a player's next personal memory on a world without machines (#2475, synthesis §4.9; the pack's
/// <see cref="StoryDefinition.MemoryTriggers"/>) — there the kill drop can never fire, so the four memories never came.</summary>
public static class StoryMemoryTriggers
{
    /// <summary>Following a chapter lead.</summary>
    public const string Lead = "lead";

    /// <summary>Reading a rune monument the player had not read.</summary>
    public const string Monument = "monument";

    /// <summary>Every trigger word.</summary>
    public static readonly IReadOnlyList<string> All = new[] { Lead, Monument };
}
