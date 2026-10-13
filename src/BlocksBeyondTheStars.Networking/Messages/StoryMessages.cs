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

    /// <summary>The world story's narrator lines that are not beats which THIS player has heard (#2471, locale keys): the
    /// Guardian-system reveal, the insights (companion ward, shape anomaly), the resolution line — derived from their once
    /// keys, so the Journal's "VEGA's notes" keep them across a rejoin (they used to live in the session's log only).
    /// Additive: an older server leaves it empty.</summary>
    public string[] NoteKeys { get; set; } = System.Array.Empty<string>();
}

/// <summary>
/// Server → client (#2471, tag 299): the player's current objectives in the chip order of synthesis §5.3 — at most
/// <see cref="MaxItems"/> rows, one per story (pack), the first is the one on the objective chip (still also carried by
/// every <see cref="ShipAiLine"/> for older clients). The Journal's live progress and the optional "Show all objectives"
/// lines read it. Sent to clients with <c>packs1</c> on the join, on a change (at most once a second) and on request.
/// </summary>
public sealed class ObjectiveList
{
    /// <summary>The most rows a list carries (the chip plus two more lines under it).</summary>
    public const int MaxItems = 3;

    /// <summary>The rows, the chip's first; empty = no objective at all.</summary>
    public NetObjective[] Items { get; set; } = System.Array.Empty<NetObjective>();
}

/// <summary>One row of <see cref="ObjectiveList"/> (#2471).</summary>
public sealed class NetObjective
{
    /// <summary>Where it comes from (<see cref="ObjectiveSources"/>): the label and the hints mute follow it.</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>The story (pack) it belongs to — the world story's id for VEGA's lead and the finale.</summary>
    public string PackId { get; set; } = string.Empty;

    /// <summary>The pack step behind the row (empty for the world story's lead) — the Journal matches its progress by it.</summary>
    public string StepId { get; set; } = string.Empty;

    /// <summary>Locale key of the objective text.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>The text's {0} (a display string), or empty.</summary>
    public string Arg { get; set; } = string.Empty;

    /// <summary>Progress / target (a counter shows only when the target is above 1).</summary>
    public int Progress { get; set; }

    public int Target { get; set; }

    /// <summary>The player pinned this story in the Journal (<see cref="StoryPackActions.Track"/>).</summary>
    public bool Pinned { get; set; }
}

/// <summary>
/// Server → client (#2471, tag 300): the Journal's view of every story this player can follow — the player packs (the
/// tutorial, side stories) with their chapters, active steps and switches, plus one short entry for the world story
/// (its arc stays on <see cref="StoryStateMessage"/>; no chapters here). Built per receiver; sent to clients with
/// <c>packs1</c> on the join, after a state change (a step, a chapter, an offer, a switch — coalesced, at most once a
/// second, never for a mere progress tick: <see cref="ObjectiveList"/> carries live progress) and on request
/// (<see cref="StoryPackActions.RequestStates"/>).
/// </summary>
public sealed class StoryPackStates
{
    /// <summary>The stories, in Journal order: tutorial packs, the world story, other packs.</summary>
    public NetStoryPack[] Packs { get; set; } = System.Array.Empty<NetStoryPack>();

    /// <summary>The story pinned to the chip (<c>PlayerState.TrackedStoryId</c>), empty for the automatic order.</summary>
    public string TrackedPackId { get; set; } = string.Empty;

    /// <summary>The world's tutorial rule: <c>Auto</c>, <c>OptIn</c>, <c>Off</c> or <c>Courses</c> (#2498).</summary>
    public string TutorialPolicy { get; set; } = string.Empty;
}

/// <summary>One story of <see cref="StoryPackStates"/> (#2471).</summary>
public sealed class NetStoryPack
{
    public string Id { get; set; } = string.Empty;

    /// <summary><c>tutorial</c>, <c>story</c> or <c>side</c>.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary><c>world</c> (the world story, shared arc) or <c>player</c>.</summary>
    public string Scope { get; set; } = string.Empty;

    /// <summary>Locale keys of the story's name, short name (the chip label) and description.</summary>
    public string NameKey { get; set; } = string.Empty;

    public string ShortNameKey { get; set; } = string.Empty;

    public string DescriptionKey { get; set; } = string.Empty;

    /// <summary>It runs for this player in this world (world rule, opt-out).</summary>
    public bool Enabled { get; set; }

    /// <summary>Started for this player (the world story: their story track).</summary>
    public bool Started { get; set; }

    /// <summary>Through: every mandatory chapter done or skipped (the world story: the finale won).</summary>
    public bool Done { get; set; }

    /// <summary>The player switched it off.</summary>
    public bool OptedOut { get; set; }

    /// <summary>Pinned to the chip.</summary>
    public bool Tracked { get; set; }

    /// <summary>The player may start it now (<see cref="StoryPackActions.StartPack"/>).</summary>
    public bool Startable { get; set; }

    /// <summary>The world rule keeps it off (a tutorial while the world's tutorial is <c>Off</c>): no switch helps.</summary>
    public bool Blocked { get; set; }

    /// <summary>Its mandatory part still runs (the tutorial's basic training) — "skip all" applies.</summary>
    public bool MandatoryRunning { get; set; }

    /// <summary>The story's badge: finished by playing (not by skipping) — the world story: the finale won.</summary>
    public bool Badge { get; set; }

    /// <summary>The chapters in order (empty for the world story — its arc is on <see cref="StoryStateMessage"/>).</summary>
    public NetStoryChapter[] Chapters { get; set; } = System.Array.Empty<NetStoryChapter>();

    /// <summary>The active do steps of the running chapters (empty for the world story).</summary>
    public NetStoryStep[] Steps { get; set; } = System.Array.Empty<NetStoryStep>();

    /// <summary>The world story only (#2475): the leads of the player's current chapter — the Journal's tasks of the VEGA page
    /// (the chapter's own leads, plus the stand-in the chip shows when none of them is open here). Empty for player packs, before
    /// the player's story track starts, and from an older server. Additive.</summary>
    public NetStoryLead[] Leads { get; set; } = System.Array.Empty<NetStoryLead>();

    /// <summary>The world story only (#2476): the story missions of the player's current chapter that VEGA handed out — running
    /// or done — with their steps. Empty for player packs, before the player's story track starts, for an older client's story
    /// and from an older server. Additive.</summary>
    public NetStoryMission[] Missions { get; set; } = System.Array.Empty<NetStoryMission>();
}

/// <summary>One story mission of the world story's <see cref="NetStoryPack"/> (#2476).</summary>
public sealed class NetStoryMission
{
    /// <summary>The mission id (<see cref="StoryPackActions.SkipChapter"/> skips it by this id).</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>The chapter it belongs to (the player's current chapter).</summary>
    public string ChapterId { get; set; } = string.Empty;

    /// <summary>Locale keys of its title and its one-line summary (may be empty).</summary>
    public string TitleKey { get; set; } = string.Empty;

    public string SummaryKey { get; set; } = string.Empty;

    /// <summary><see cref="StoryChapterStates.Active"/> (running) or <see cref="StoryChapterStates.Done"/>.</summary>
    public string State { get; set; } = string.Empty;

    /// <summary>The player may skip it now (a running mission that allows it).</summary>
    public bool Skippable { get; set; }

    /// <summary>Its steps in order (a stand-in at the place of the step it replaces; skipped steps left out).</summary>
    public NetStoryMissionStep[] Steps { get; set; } = System.Array.Empty<NetStoryMissionStep>();
}

/// <summary>One step of a <see cref="NetStoryMission"/> (#2476).</summary>
public sealed class NetStoryMissionStep
{
    public string Id { get; set; } = string.Empty;

    /// <summary>Locale key of its text and the text's {0} (a display string) — the running step shows what the chip shows (a
    /// lead bridge its placed text), the others their own chip text.</summary>
    public string Key { get; set; } = string.Empty;

    public string Arg { get; set; } = string.Empty;

    /// <summary><see cref="StoryChapterStates.Done"/>, <see cref="StoryChapterStates.Active"/> or
    /// <see cref="StoryChapterStates.Upcoming"/>.</summary>
    public string State { get; set; } = string.Empty;

    /// <summary>Progress / target as of the send (a counter shows only for a target above 1).</summary>
    public int Progress { get; set; }

    public int Target { get; set; }

    /// <summary>It is the step VEGA's chip shows now.</summary>
    public bool OnChip { get; set; }
}

/// <summary>One chapter lead of the world story's <see cref="NetStoryPack"/> (#2475).</summary>
public sealed class NetStoryLead
{
    /// <summary>The chapter it belongs to (the player's current chapter).</summary>
    public string ChapterId { get; set; } = string.Empty;

    /// <summary>Its number in the chapter (1-based; the chapter's own leads first, then the stand-ins).</summary>
    public int Index { get; set; }

    /// <summary>The lead word (<c>fragment</c>, <c>unique_site:observatory</c>, …).</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Locale key of its text (the chip text, <c>story.vega.obj.*</c>) and the text's {0} (a display string).</summary>
    public string Key { get; set; } = string.Empty;

    public string Arg { get; set; } = string.Empty;

    /// <summary>The player followed it.</summary>
    public bool Done { get; set; }

    /// <summary>It is the one on the chip now.</summary>
    public bool Current { get; set; }

    /// <summary>A stand-in (a fallback the chapter does not list itself), shown only while it is current.</summary>
    public bool StandIn { get; set; }
}

/// <summary>One chapter of a <see cref="NetStoryPack"/> (#2471).</summary>
public sealed class NetStoryChapter
{
    public string Id { get; set; } = string.Empty;

    public string TitleKey { get; set; } = string.Empty;

    public string SummaryKey { get; set; } = string.Empty;

    /// <summary>One of <see cref="StoryChapterStates"/>.</summary>
    public string State { get; set; } = string.Empty;

    /// <summary>An opt-in chapter (a deep dive), not part of the mandatory sequence.</summary>
    public bool Optional { get; set; }

    /// <summary>May be skipped (<see cref="StoryPackActions.SkipChapter"/>).</summary>
    public bool Skippable { get; set; }

    /// <summary>For a locked chapter: locale keys naming what it still needs (a chapter title, a blueprint name).</summary>
    public string[] RequiresKeys { get; set; } = System.Array.Empty<string>();

    /// <summary>#2500: nothing left to practise — a finished chapter whose replay would pass every task at once (the blueprints
    /// it researches are known, the jobs it bridges are handed in), or a course not started yet that the player has already done
    /// by other means. The Journal shows no Replay / Start for it and reads "done — nothing left to practise". Additive
    /// (contractless): an older server never sets it, so the Journal keeps its buttons there.</summary>
    public bool NothingLeft { get; set; }
}

/// <summary>One active do step of a <see cref="NetStoryPack"/> (#2471).</summary>
public sealed class NetStoryStep
{
    public string Id { get; set; } = string.Empty;

    public string ChapterId { get; set; } = string.Empty;

    /// <summary>Locale keys of its chip text and its explanation (variants for the world's rules applied).</summary>
    public string ChipKey { get; set; } = string.Empty;

    public string StartKey { get; set; } = string.Empty;

    /// <summary>The Codex chapter or guide article the "More" button opens, or empty.</summary>
    public string MoreGuide { get; set; } = string.Empty;

    /// <summary><c>must</c>, <c>core</c> or <c>opt</c>.</summary>
    public string Tier { get; set; } = string.Empty;

    /// <summary>Progress / target as of the send (live progress rides on <see cref="ObjectiveList"/>).</summary>
    public int Progress { get; set; }

    public int Target { get; set; }

    /// <summary>Its place among the chapter's tasks: "step <see cref="Index"/> of <see cref="Count"/>" (1-based).</summary>
    public int Index { get; set; }

    public int Count { get; set; }

    /// <summary>May be skipped (<see cref="StoryPackActions.SkipStep"/>).</summary>
    public bool Skippable { get; set; }

    /// <summary>An open extra: a CORE step past its hint time that no longer holds the chapter up but still counts.</summary>
    public bool Extra { get; set; }

    /// <summary>The step the pack shows on the chip.</summary>
    public bool OnChip { get; set; }
}

/// <summary>The values of <see cref="NetStoryChapter.State"/> (#2471).</summary>
public static class StoryChapterStates
{
    /// <summary>Completed by playing.</summary>
    public const string Done = "done";

    /// <summary>Skipped ("I know this").</summary>
    public const string Skipped = "skipped";

    /// <summary>Running now.</summary>
    public const string Active = "active";

    /// <summary>A done chapter played again.</summary>
    public const string Replay = "replay";

    /// <summary>An opt-in chapter VEGA offered (Start / Later).</summary>
    public const string Offered = "offered";

    /// <summary>An offered chapter the player put off ("Later") — still startable.</summary>
    public const string Declined = "declined";

    /// <summary>An opt-in chapter whose requirements hold — startable.</summary>
    public const string Available = "available";

    /// <summary>A mandatory chapter still ahead in the sequence.</summary>
    public const string Upcoming = "upcoming";

    /// <summary>An opt-in chapter whose requirements do not hold yet (<see cref="NetStoryChapter.RequiresKeys"/>).</summary>
    public const string Locked = "locked";
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
/// (sandbox). Since #2506 the switch keeps progress: a pack the save already ran comes back where it stopped (its saved
/// row), a pack it never ran starts fresh, and re-selecting the running story changes nothing. An older server reset the
/// chosen pack instead — a current client offers the switch only when <see cref="ServerRules.MainStory"/> is filled.
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
/// sender's own story track early ("Start the story now", #2470) and <c>track</c> pins it. Every action gets through
/// while a singleplayer world is held (#2471: the Journal may be opened from the pause menu); each is validated on the
/// server and an unknown word, pack, chapter or step is ignored.
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

    /// <summary>"Later" to an offered chapter (the Journal's decline of an offer) — it stays listed and startable.</summary>
    public const string DeclineChapter = "declineChapter";

    /// <summary>Skip the rest of a chapter.</summary>
    public const string SkipChapter = "skipChapter";

    /// <summary>Start a chapter over: a running one counts its steps from zero again (#2471), a done or skipped one is
    /// replayed (evidence off).</summary>
    public const string RestartChapter = "restartChapter";

    /// <summary>Play a done or skipped chapter again (#2471, the Journal's per-chapter "Replay"; evidence off, keys stay).</summary>
    public const string ReplayChapter = "replayChapter";

    /// <summary>Run the whole pack again from its first mandatory chapter (#2471, the Journal's "Restart"; never the prologue).</summary>
    public const string RestartPack = "restartPack";

    /// <summary>"I know this": skip one active step.</summary>
    public const string SkipStep = "skipStep";

    /// <summary>The Journal opened (#2471): the server answers with <see cref="StoryPackStates"/> and
    /// <see cref="ObjectiveList"/> at once. <see cref="StoryPackIntent.PackId"/> is ignored. Changes nothing.</summary>
    public const string RequestStates = "requestStates";

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

    /// <summary>#2478: how many nodes the duel has ("argument 2 of 4" in the dialogue); 0 from an older server. Additive
    /// (contractless): an older client ignores it.</summary>
    public int NodeCount { get; set; }

    /// <summary>#2478: the core column's position on the core world (its floor cell's centre) — where the client's duel
    /// dialogue frames its camera. False from an older server (the dialogue then keeps the player's view).</summary>
    public bool HasAnchor { get; set; }

    public float AnchorX { get; set; }
    public float AnchorY { get; set; }
    public float AnchorZ { get; set; }
}

/// <summary>Server → client (#1124): the story is RESOLVED — play the resolution cinematic (resolution
/// line → credits roll → epilogue handover). Sent to everyone online when the finale duel is won, once as
/// join catch-up to anyone who missed it (tracked per player via a milestone), and again on request from
/// the Story tab's "watch the ending" button. All text is locale keys / pack strings — the client
/// localizes. #2478: only to a client that does not play cutscenes (or a pack without its ending scene) — the others get
/// the stand-down and the ending as cutscenes, never both.</summary>
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
/// answers with <see cref="StoryResolved"/> — but only when the story actually IS resolved. #2478: a client that plays
/// cutscenes gets the pack's ending scene as a replay (<c>CutscenePlay</c> with <c>Replay</c>) instead.</summary>
public sealed class RequestStoryResolutionIntent { }

/// <summary>Client → server: the player offers a rebuttal at the current duel node. The server validates the
/// index against the active node; a correct (contradiction) choice advances the duel, a wrong one is dismissed
/// and the node is re-presented (the duel cannot be lost, only stalled). #2478 (B50): only a player standing on the core
/// world answers — a pick from anywhere else is dropped. Served while a singleplayer world is held (the dialogue holds it).</summary>
public sealed class CoreDialogueChoiceIntent
{
    public int ChoiceIndex { get; set; }
}
