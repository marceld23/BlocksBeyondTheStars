// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Networking.Messages;

/// <summary>
/// Server → client: the active story's shared, server-wide progress for this save — the meter shown in the
/// Story Log tab + Map tab, plus the raw counters. Sent on join and whenever the story advances. The
/// narrator beats themselves arrive separately on the existing VEGA <c>ShipAiLine</c> channel; this message
/// carries only the aggregate state. <see cref="Active"/> is false when the story is disabled ("none"
/// sandbox).
/// </summary>
public sealed class StoryStateMessage
{
    /// <summary>The active story pack id (e.g. "vega_protocol"), or "none" when disabled.</summary>
    public string StoryId { get; set; } = string.Empty;

    /// <summary>False when no story is active (the "none" sandbox) — the UI hides the meter/tab.</summary>
    public bool Active { get; set; }

    /// <summary>The current weighted progress score.</summary>
    public int Progress { get; set; }

    /// <summary>The score that opens the finale (the last beat's threshold) — for the "NN %" meter.</summary>
    public int ProgressTarget { get; set; }

    public int FragmentsFound { get; set; }
    public int MachineKills { get; set; }
    public int Milestones { get; set; }

    /// <summary>How many narrator beats of the arc have been revealed so far.</summary>
    public int BeatsRevealed { get; set; }

    public bool GuardianSystemRevealed { get; set; }
    public bool GuardianDefeated { get; set; }

    /// <summary>Keys of the save's found net fragments (#1110) — the client resolves text + category from the
    /// pack definition, so the Story tab's fragment list survives a rejoin. Additive; old clients ignore it.</summary>
    public string[] FoundFragmentKeys { get; set; } = System.Array.Empty<string>();

    /// <summary>THIS player's unlocked personal-memory keys (#1110). Per-session content — the message is
    /// built per receiver. Additive; old clients ignore it.</summary>
    public string[] PlayerMemoryKeys { get; set; } = System.Array.Empty<string>();

    /// <summary>THIS player's found environmental lore keys (#1111) — site + text resolve from the pack's
    /// <c>LoreSites</c> client-side. Additive.</summary>
    public string[] FoundLoreKeys { get; set; } = System.Array.Empty<string>();

    /// <summary>The beat indexes THIS player has heard (#2462, B08), ascending — from their
    /// <c>story:&lt;id&gt;:beat:&lt;n&gt;</c> milestones. The Story tab rebuilds its beat list from these (texts resolve
    /// from the pack), so it survives a rejoin. Additive: an older server leaves it empty and the client keeps the
    /// beats it heard live this session.</summary>
    public int[] HeardBeats { get; set; } = System.Array.Empty<int>();

    /// <summary>The cutscenes THIS player's client has confirmed (#2468, synthesis §5.7), every pack, as
    /// <c>&lt;pack&gt;/&lt;id&gt;</c> (a template as its instance id) — from the <c>story:&lt;pack&gt;:cut:&lt;id&gt;</c>
    /// milestones. The Story tab's "Watch again" list is built from these (and what the client played since). Additive:
    /// an older server leaves it empty.</summary>
    public string[] SeenCutscenes { get; set; } = System.Array.Empty<string>();

    /// <summary>Whether THIS player's story track has started (#2470, <c>story:&lt;id&gt;:started</c>): until then the world
    /// story's beats and lines are held for them (they play the basic training first). Additive: an older server leaves it
    /// false — read it together with <see cref="WorldChapter"/>, which a current server always fills.</summary>
    public bool Started { get; set; }

    /// <summary>THIS player's chapter id (#2470, derived, never stored: <c>max(1, world)</c> once their track started);
    /// empty before the track starts. Additive.</summary>
    public string Chapter { get; set; } = string.Empty;

    /// <summary>The world's chapter id — the last chapter the shared arc has reached (#2470). Filled by every current server
    /// for a story with chapters, so an empty value means an older server. Additive.</summary>
    public string WorldChapter { get; set; } = string.Empty;

    /// <summary>Whether the Story tab shows the "The crew of this world is in chapter N" status line (#2470, E29): for a
    /// player whose track has not started on a world past chapter 0, and on a hosted or arcade world where every newcomer
    /// gets a personal chapter 1 while the crew is further along. Additive.</summary>
    public bool ShowCrewChapter { get; set; }

    /// <summary>The world story's relay-network insights already spoken in this save (#2470, locale keys such as
    /// <c>vega.relay.first</c>) — sent only to a player whose track has started, so a player who joined the story later
    /// finds them in the log silently, and the log survives a rejoin. Additive.</summary>
    public string[] RelayInsightKeys { get; set; } = System.Array.Empty<string>();
}

/// <summary>Server → client: an environmental lore text just found (#1111) — a rune inscription read with the
/// scanner, a wreck log or ruin note surfaced by looting the site. The client localizes <see cref="TextKey"/>
/// and shows it in the reader panel; <see cref="Site"/> picks the title ("ui.lore.site.&lt;site&gt;").</summary>
public sealed class LoreTextRevealed
{
    /// <summary>The lore entry's stable key (per-player dedupe/bookkeeping).</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Site kind: monument | wreck | vault | data_terminal | ruin | bandit_camp | chest | settlement | factory.</summary>
    public string Site { get; set; } = string.Empty;

    /// <summary>Locale key of the readable text (pack locale tables, merged into the shared localizer).</summary>
    public string TextKey { get; set; } = string.Empty;
}

/// <summary>
/// Client → server (admin only): choose the save's active story pack, or "none" to disable the story
/// (sandbox). Switching resets the per-save story progress to a fresh state for the chosen pack.
/// </summary>
public sealed class StorySelectIntent
{
    public string StoryId { get; set; } = string.Empty;
}

/// <summary>
/// Client → server (#2466, tag 297): the player's own control of a PLAYER story pack (the tutorial, side stories) — from
/// the Journal, the first tutorial card, the Settings and the VEGA panel. <see cref="Action"/> is one of
/// <see cref="StoryPackActions"/>; <see cref="ChapterId"/> / <see cref="StepId"/> name the target where the action needs
/// one. Self-service, never a cheat: it changes only the sender's own ledger. The world story is not switched here
/// (that stays the admin's <see cref="StorySelectIntent"/>) — but <c>startPack</c> with the world story's id starts the
/// sender's own story track early ("Start the story now", #2470). While a singleplayer world is held only
/// <c>track</c>, <c>untrack</c>, <c>optOut</c>, <c>optIn</c>, <c>skipPack</c>, <c>startPack</c> and <c>replayCutscene</c>
/// get through (synthesis §5.7).
/// </summary>
public sealed class StoryPackIntent
{
    /// <summary>The pack (e.g. <c>tutorial</c>).</summary>
    public string PackId { get; set; } = string.Empty;

    /// <summary>What to do (<see cref="StoryPackActions"/>); an unknown word is ignored.</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>The chapter of <c>startChapter</c>, <c>declineChapter</c>, <c>skipChapter</c>, <c>restartChapter</c>.</summary>
    public string ChapterId { get; set; } = string.Empty;

    /// <summary>The step of <c>skipStep</c>.</summary>
    public string StepId { get; set; } = string.Empty;

    /// <summary>The cutscene (instance) id of <c>replayCutscene</c> (#2467). Additive: an older server ignores it.</summary>
    public string CutsceneId { get; set; } = string.Empty;
}

/// <summary>The values of <see cref="StoryPackIntent.Action"/> (#2466).</summary>
public static class StoryPackActions
{
    /// <summary>Pin the pack to the objective chip (<c>PlayerState.TrackedStoryId</c>).</summary>
    public const string Track = "track";

    /// <summary>Back to the automatic chip order.</summary>
    public const string Untrack = "untrack";

    /// <summary>Switch the pack off for this player (keeps every key; lines and objectives stop).</summary>
    public const string OptOut = "optOut";

    /// <summary>Switch it back on.</summary>
    public const string OptIn = "optIn";

    /// <summary>Start the pack now (an opt-in tutorial, a pack whose start rule allows an early start) — for the world story
    /// (VEGA) the player's own story track: "Start the story now" in the Story tab (#2470).</summary>
    public const string StartPack = "startPack";

    /// <summary>"I know the game": skip every mandatory chapter (deep-dive offers stay on).</summary>
    public const string SkipPack = "skipPack";

    /// <summary>Start an optional chapter (accepts an offer).</summary>
    public const string StartChapter = "startChapter";

    /// <summary>"Later" to an offered chapter — it stays listed and startable.</summary>
    public const string DeclineChapter = "declineChapter";

    /// <summary>Skip the rest of a chapter.</summary>
    public const string SkipChapter = "skipChapter";

    /// <summary>Replay a chapter (evidence off).</summary>
    public const string RestartChapter = "restartChapter";

    /// <summary>"I know this": skip one active step.</summary>
    public const string SkipStep = "skipStep";

    /// <summary>Watch a cutscene again (#2467, <see cref="StoryPackIntent.CutsceneId"/>; any installed pack, the world story
    /// included): the server answers with <see cref="CutscenePlay"/> <c>Replay = true</c> — only for a scene the player has
    /// already been shown and that is replayable (spoiler guard). Changes no state.</summary>
    public const string ReplayCutscene = "replayCutscene";
}

/// <summary>
/// Client → server (#2469, tag 298): a UI moment a tutorial step teaches — the VEGA line continued, the Tab menu, the
/// Codex or a map opened, a flight-chart waypoint set. <see cref="Id"/> is one of
/// <c>GameEventKeys.UiEvents</c>; the server raises <c>ui:&lt;id&gt;</c> on its event bus, where only an active step that
/// waits for exactly that flag listens. A teaching flag only ever ticks off the sender's own lesson: it never pays,
/// never unlocks, never counts for the story and is never stored (synthesis G1, §3.6). Rate-limited per player; passes a
/// held singleplayer world (the pause menu and its Codex send it). An older server drops the unknown tag.
/// </summary>
public sealed class UiTutorialEvent
{
    /// <summary>The flag id (<c>vega_continue</c>, <c>menu_open</c>, <c>codex_open</c>, <c>map_open</c>,
    /// <c>waypoint_set</c>); an unknown id is ignored.</summary>
    public string Id { get; set; } = string.Empty;
}

/// <summary>
/// Server → client (#2467, tag 295): play a cutscene of a story pack. The client resolves shots and captions from its own
/// copy of the pack's <c>cutscenes.json</c>; only clients that announce <c>cutscenes1</c> get it (the trigger's narrator
/// line goes out to everyone as before). The client confirms with <see cref="CutsceneSeenIntent"/> once the scene played,
/// was skipped, downgraded or cannot be played; a scene without a confirmation is offered again at the next join.
/// </summary>
public sealed class CutscenePlay
{
    /// <summary>The pack that defines the scene.</summary>
    public string PackId { get; set; } = string.Empty;

    /// <summary>The cutscene id — for a template binding the instance id <c>&lt;id&gt;@&lt;owner&gt;</c> (the definition is
    /// the part before <c>@</c>); <c>__recap</c> for the built-in recap card.</summary>
    public string CutsceneId { get; set; } = string.Empty;

    /// <summary>The argument for the captions' <c>{0}</c> (several joined by U+001F) — a chapter title key for a chapter
    /// binding (a client localizes an argument that is a key), a body name, or for <c>__recap</c> the reached chapters'
    /// title keys in chapter order. Empty = none.</summary>
    public string Arg { get; set; } = string.Empty;

    /// <summary>Whether <see cref="AnchorX"/>/<see cref="AnchorY"/>/<see cref="AnchorZ"/> carry the position of the
    /// <c>custom</c> anchor (a server-only place such as the core chamber).</summary>
    public bool HasAnchor { get; set; }

    public float AnchorX { get; set; }
    public float AnchorY { get; set; }
    public float AnchorZ { get; set; }

    /// <summary>A replay the player asked for: no confirmation needed, no state changes, skippable at once.</summary>
    public bool Replay { get; set; }
}

/// <summary>
/// Client → server (#2467, tag 296): the client is through with these cutscenes of one pack — watched, skipped
/// (<see cref="Skipped"/>), downgraded, or not playable by this build (<see cref="Unsupported"/>: an unknown id, version
/// skew). The server stamps each known id seen (<c>story:&lt;pack&gt;:cut:&lt;id&gt;</c>) so it is not offered again;
/// unknown ids are ignored. Passes a held singleplayer world (the director confirms while it holds the world).
/// </summary>
public sealed class CutsceneSeenIntent
{
    /// <summary>The pack of the scenes.</summary>
    public string PackId { get; set; } = string.Empty;

    /// <summary>The (instance) ids, as received in <see cref="CutscenePlay.CutsceneId"/>.</summary>
    public string[] Ids { get; set; } = System.Array.Empty<string>();

    /// <summary>The player skipped (the texts went to the journal).</summary>
    public bool Skipped { get; set; }

    /// <summary>The client could not play the scenes (it showed the narrator lines instead).</summary>
    public bool Unsupported { get; set; }
}

/// <summary>One net fragment scattered on a body's surface for the client to render + let the player pick up
/// (walk up, press E). Text-only story finds — distinct from the knowledge mini-game dataqubes. The archive
/// text is not sent until pickup (see <see cref="NetFragmentRevealed"/>); this carries only position + the
/// lore <see cref="Category"/> for the icon/tint. Server-authoritative placement (deterministic from seed).</summary>
public sealed class NetStoryFragment
{
    public int Id { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }

    /// <summary>Lore category (vega | sps | guardian | network | settler | netnode) for the client icon/tint.</summary>
    public string Category { get; set; } = string.Empty;
}

/// <summary>Full set of net fragments the client should render for its current world (server → client).</summary>
public sealed class NetFragmentList
{
    public NetStoryFragment[] Fragments { get; set; } = System.Array.Empty<NetStoryFragment>();
}

/// <summary>The player picks up a net fragment they're standing at — press E (client → server). The server
/// validates the fragment exists and the player is within reach, then reveals it and advances the story.</summary>
public sealed class NetFragmentFoundIntent
{
    public int FragmentId { get; set; }
}

/// <summary>Server → client: the picked-up fragment's archive text to show in the reader panel. The client
/// localizes <see cref="TextKey"/> (bilingual DE+EN); <see cref="Category"/> tints/labels the entry.</summary>
public sealed class NetFragmentRevealed
{
    public string Category { get; set; } = string.Empty;
    public string TextKey { get; set; } = string.Empty;
}

/// <summary>Server → client: a personal player memory just unlocked (by defeating a Guardian machine). The
/// client localizes <see cref="TextKey"/> (DE+EN) and shows it in the reader. Per-player — each player is a
/// different neural-imprint clone, so memories never contradict across a multiplayer crew.</summary>
public sealed class PlayerMemoryRevealed
{
    public string TextKey { get; set; } = string.Empty;
}

// ---------------- Finale (P6): Guardian system reveal → core hack → argument duel ----------------

/// <summary>Server → client: the Guardian (finale) system has just been placed on the star map — the story is
/// complete enough to confront the core. Fired once, when the gate flips. A jump generator is needed to reach
/// it; the narrator line arrives separately on the VEGA channel.</summary>
public sealed class GuardianSystemRevealed
{
    /// <summary>Optional locale key / id for the map marker label (the client may also just use a fixed label).</summary>
    public string LabelKey { get; set; } = string.Empty;
}

/// <summary>Client → server: the player channels the core hack for one tick at the inner core (stage 3,
/// "channel-and-defend"). The server owns the increment (anti-cheat) and replies with
/// <see cref="CoreHackProgress"/>; completing it opens the argument duel.</summary>
public sealed class CoreHackIntent
{
}

/// <summary>Server → client: the core-hack channel progress (0..100). When <see cref="Complete"/> the hack is
/// done and the duel begins (the first <see cref="CoreDialogueMessage"/> follows).</summary>
public sealed class CoreHackProgress
{
    public int Progress { get; set; }
    public bool Complete { get; set; }
}

/// <summary>Server → client: the current state of the finale argument duel (stage 4). Carries the core's
/// statement (<see cref="PromptKey"/>) and the player's rebuttal options (<see cref="ChoiceKeys"/>), plus an
/// optional <see cref="ResponseKey"/> — the core's reaction to the previous pick. All are locale keys the
/// client localizes (bilingual DE+EN). When <see cref="Won"/> the core has been argued into shutdown
/// (pacification) and there are no further choices.</summary>
public sealed class CoreDialogueMessage
{
    /// <summary>Index of the current duel node (for the client's progress display).</summary>
    public int Node { get; set; }

    /// <summary>Locale key of the core's statement/challenge ("" when <see cref="Won"/>).</summary>
    public string PromptKey { get; set; } = string.Empty;

    /// <summary>Locale keys of the player's rebuttal options (empty when <see cref="Won"/>).</summary>
    public string[] ChoiceKeys { get; set; } = System.Array.Empty<string>();

    /// <summary>Locale key of the core's reaction to the previous pick ("" at the opening of the duel).</summary>
    public string ResponseKey { get; set; } = string.Empty;

    /// <summary>True once the last node is cleared — the core powers down (the galaxy is pacified).</summary>
    public bool Won { get; set; }
}

/// <summary>Server → client (#1124): the story is RESOLVED — play the resolution cinematic (resolution
/// line → credits roll → epilogue handover). Sent to everyone online when the finale duel is won, once as
/// join catch-up to anyone who missed it (tracked per player via a milestone), and again on request from
/// the Story tab's "watch the ending" button. All text is locale keys / pack strings — the client
/// localizes.</summary>
public sealed class StoryResolved
{
    /// <summary>Locale key of the story pack's display name (for the cinematic's title card).</summary>
    public string StoryNameKey { get; set; } = string.Empty;

    /// <summary>The epilogue's title — a raw pack string like the beat titles; may be empty. Kept for older clients
    /// since #2461: a current client shows <see cref="EpilogueTitleKey"/> instead.</summary>
    public string EpilogueTitle { get; set; } = string.Empty;

    /// <summary>Locale key of the epilogue title (#2461, B22); empty from an older server or a pack without one —
    /// the client then falls back to <see cref="EpilogueTitle"/>. Additive (contractless): an older client ignores it.</summary>
    public string EpilogueTitleKey { get; set; } = string.Empty;

    /// <summary>Locale key of the epilogue text (the handover to the post-story goal); may be empty.</summary>
    public string EpilogueTextKey { get; set; } = string.Empty;
}

/// <summary>Client → server (#1124): the player wants to watch the ending again (Story tab). The server
/// answers with <see cref="StoryResolved"/> — but only when the story actually IS resolved.</summary>
public sealed class RequestStoryResolutionIntent { }

/// <summary>Client → server: the player offers a rebuttal at the current duel node. The server validates the
/// index against the active node; a correct (contradiction) choice advances the duel, a wrong one is dismissed
/// and the node is re-presented (the duel cannot be lost, only stalled).</summary>
public sealed class CoreDialogueChoiceIntent
{
    public int ChoiceIndex { get; set; }
}
