using System;
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Shared.Story;

/// <summary>
/// One narrator beat in a story pack's ordered arc. Beats reveal strictly in <see cref="Index"/> order, each
/// gated by a <see cref="Threshold"/> on the story-progress score (see <see cref="StoryEngine"/>). The
/// <see cref="TextKey"/> is a locale key the narrator (e.g. VEGA) speaks — bilingual DE+EN like all in-game
/// text. The pack is story-agnostic data: nothing here is VEGA-specific.
/// </summary>
public sealed class StoryBeat
{
    /// <summary>Position in the arc (0-based). Beats are revealed in ascending index order.</summary>
    public int Index { get; set; }

    /// <summary>Short dev-facing working title (not shown to players).</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Locale key spoken by the narrator when this beat reveals (must exist in DE+EN once wired to UI).</summary>
    public string TextKey { get; set; } = string.Empty;

    /// <summary>Progress score at/above which this beat reveals (monotonic across the arc).</summary>
    public int Threshold { get; set; }

    /// <summary>Optional knowledge points granted to the revealing player when this beat first fires.</summary>
    public int KnowledgeReward { get; set; }
}

/// <summary>
/// One findable net fragment of a story pack — a text-only story find (distinct from the knowledge
/// mini-game dataqubes). Placed in the world (structures + scattered on planet surfaces); picking one up
/// reveals its archive <see cref="TextKey"/> and advances the shared story. Deduped by <see cref="Key"/>.
/// </summary>
public sealed class StoryFragment
{
    /// <summary>Unique fragment id (the dedupe key tracked in <c>StoryState.FoundFragmentKeys</c>).</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Lore category: vega | sps | guardian | network | settler | netnode (for the reader/icon).</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>Locale key of the archive text shown on pickup (bilingual DE+EN).</summary>
    public string TextKey { get; set; } = string.Empty;

    /// <summary>Relative draw weight (rarity) when picking which fragment a spot holds.</summary>
    public int Weight { get; set; } = 1;
}

/// <summary>
/// One personal player memory — a fragment of the cloned SPS member's life, unlocked (in order) when the
/// player defeats the Guardian's machines. Per-player + non-contradictory in multiplayer (each player is a
/// different neural imprint). Tracked per-player in <c>PlayerState.Milestones</c> as <c>story:mem:&lt;key&gt;</c>.
/// </summary>
public sealed class StoryMemory
{
    /// <summary>Unique memory id (the per-player unlock key).</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Locale key of the memory text (bilingual DE+EN), shown in the reader on unlock.</summary>
    public string TextKey { get; set; } = string.Empty;
}

/// <summary>
/// One rebuttal the player can offer at a <see cref="CoreArgument"/> node of the finale dialogue duel. The
/// core is defeated <b>by contradiction, not by weapons</b>: exactly one choice per node is <see cref="Correct"/>
/// (it exposes a real flaw in the core's logic and advances the duel); the others are dismissed and the player
/// stays on the node to try again (you cannot lose the duel, only fail to progress). All text is a locale key
/// (bilingual DE+EN).
/// </summary>
public sealed class CoreArgumentChoice
{
    /// <summary>Locale key of the player's rebuttal option shown in the duel panel.</summary>
    public string TextKey { get; set; } = string.Empty;

    /// <summary>True if this rebuttal exposes a real contradiction — it advances the duel toward shutdown.</summary>
    public bool Correct { get; set; }

    /// <summary>Locale key of the core's reaction to this pick: a concession when <see cref="Correct"/>, a
    /// dismissal otherwise.</summary>
    public string ResponseKey { get; set; } = string.Empty;
}

/// <summary>
/// One node of the finale's argument duel (story P6, stage 4). The core states its logic
/// (<see cref="PromptKey"/>); the player answers with one of the <see cref="Choices"/>. The pack's nodes are
/// walked <b>in order</b>; clearing the last one shuts the core down (pacification). Story-agnostic data, like
/// the rest of the pack.
/// </summary>
public sealed class CoreArgument
{
    /// <summary>Stable node id (dev-facing; the duel walks the list in order).</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Locale key of the core's statement/challenge at this node (bilingual DE+EN).</summary>
    public string PromptKey { get; set; } = string.Empty;

    /// <summary>The player's rebuttal options; exactly one should be the contradiction that advances the duel.</summary>
    public List<CoreArgumentChoice> Choices { get; set; } = new();
}

/// <summary>
/// One tag-filtered ambient NPC line of a story pack (P7). Settlement/station NPCs may speak these as their
/// greeting/idle flavour, picked by the world's tags + how far the story has come (a "knowledge level"), so
/// villages and machine-logs react to progress. The LLM backend, when on, may rephrase non-canonically; with
/// AI off these are spoken verbatim.
/// </summary>
public sealed class FlavourLine
{
    /// <summary>Unique id (for dedupe/debug).</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Locale key of the spoken line (bilingual DE+EN).</summary>
    public string TextKey { get; set; } = string.Empty;

    /// <summary>World tags this line fits (e.g. biome / settlement theme); empty = any world.</summary>
    public List<string> WorldTags { get; set; } = new();

    /// <summary>Minimum world knowledge level (0 = none … 4 = the core is known) before this line is eligible.</summary>
    public int MinKnowledge { get; set; }

    /// <summary>Restrict to an NPC role (e.g. "vendor"); empty = any role.</summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>Relative draw weight when several lines are eligible.</summary>
    public int Weight { get; set; } = 1;
}

/// <summary>
/// One mission thread of a story pack (P7): wraps the existing random missions so that turning in a matching
/// mission also yields a story <b>fragment</b> (and the usual milestone), threading the procedural mission flow
/// into the arc without bespoke quests.
/// </summary>
public sealed class MissionThread
{
    /// <summary>Case-insensitive substring matched against the completed mission's type key (<c>NameKey</c>) or
    /// id — e.g. "settlement" matches all settlement missions ("" = any mission).</summary>
    public string MissionIdContains { get; set; } = string.Empty;

    /// <summary>The net-fragment key awarded on turn-in (deduped like any fragment; "" = none).</summary>
    public string FragmentKey { get; set; } = string.Empty;
}

/// <summary>
/// A story pack: identity + pacing config + the ordered beat arc. The story engine consumes this and is
/// completely story-agnostic, so further storylines are added as more packs (see the implementation plan
/// D2–D4). For P0 a pack is code-defined in <see cref="StoryRegistry"/>; a later phase loads it from
/// <c>data/stories/&lt;id&gt;/</c>.
/// </summary>
public sealed class StoryDefinition
{
    /// <summary>Stable pack id (e.g. "vega_protocol"); keys the per-save story state + the selection UI.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Locale key for the pack's display name (shown in the story-selection world option).</summary>
    public string NameKey { get; set; } = string.Empty;

    // Pacing weights: progress = FragmentsFound*FragmentWeight
    //                          + min(MachineKills, KillContributionCap)*KillWeight
    //                          + Milestones*MilestoneWeight   (see StoryEngine).

    /// <summary>Score contribution per net fragment found (the primary story driver).</summary>
    public int FragmentWeight { get; set; } = 3;

    /// <summary>Score contribution per Guardian-machine kill, up to <see cref="KillContributionCap"/>.</summary>
    public int KillWeight { get; set; } = 1;

    /// <summary>Score contribution per milestone (system mapped / settlement helped / first base or station).</summary>
    public int MilestoneWeight { get; set; } = 2;

    /// <summary>Diminishing-returns cap: machine kills beyond this stop adding to progress (anti-grind).</summary>
    public int KillContributionCap { get; set; } = 40;

    /// <summary>The ordered beat arc (ascending <see cref="StoryBeat.Index"/> and <see cref="StoryBeat.Threshold"/>).
    /// A concrete list so it deserializes cleanly from a pack's <c>story.json</c>.</summary>
    public List<StoryBeat> Beats { get; set; } = new();

    /// <summary>The pack's findable net fragments (text-only story finds placed in the world). Empty packs
    /// still work — combat then drives the story alone.</summary>
    public List<StoryFragment> Fragments { get; set; } = new();

    /// <summary>The pack's personal player memories, unlocked in order by defeating machines (per player).</summary>
    public List<StoryMemory> Memories { get; set; } = new();

    /// <summary>The finale dialogue-duel nodes (P6, stage 4), walked in order at the Guardian core. Each node
    /// is one of the core's claims with the player's rebuttals; clearing them all shuts the core down. Empty
    /// when a pack has no scripted finale duel (the finale then falls back to a single concede).</summary>
    public List<CoreArgument> CoreArguments { get; set; } = new();

    /// <summary>Tag-filtered ambient NPC flavour lines (P7), spoken by settlement/station NPCs by world tags +
    /// the story's knowledge level. Empty packs simply add no story flavour to NPCs.</summary>
    public List<FlavourLine> FlavourLines { get; set; } = new();

    /// <summary>Mission threads (P7): turning in a matching random mission also yields a story fragment.</summary>
    public List<MissionThread> MissionThreads { get; set; } = new();
}
