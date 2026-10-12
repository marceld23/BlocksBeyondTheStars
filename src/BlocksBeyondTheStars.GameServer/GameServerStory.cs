// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Story;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The story engine wiring (implementation plan P0). A server-wide, per-save story state driven by the
/// active <see cref="StoryDefinition"/> pack — the engine itself is story-agnostic (see
/// <see cref="StoryEngine"/>/<see cref="StoryRegistry"/>), so further storylines are added as packs, not
/// engine code. Gameplay events feed the counters (<see cref="RecordStoryFragment"/> /
/// <see cref="RecordStoryMachineKill"/> / <see cref="RecordStoryMilestone"/>); crossing a beat threshold
/// reveals the next narrator beat to every player who hasn't heard it (spoken via the existing VEGA
/// <c>ShipAiLine</c> channel, tracked per-player in <c>PlayerState.Milestones</c> as
/// <c>story:&lt;id&gt;:beat:N</c> so multiplayer latecomers catch up without spoilers). The aggregate state
/// is persisted server-wide (mirrors the alliance graph) and restored at start. The active story is
/// selectable by an admin, with a "none" sandbox option.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The active story pack, or null when the story is disabled ("none" sandbox).</summary>
    private StoryDefinition? _story;

    /// <summary>Server-wide, per-save runtime story state for the active pack.</summary>
    private readonly StoryState _storyState = new();

    /// <summary>True while a story pack is active.</summary>
    private bool StoryActive => _story is not null;

    /// <summary>Test/inspection snapshot of the story progress.</summary>
    public (string StoryId, int Fragments, int Kills, int Milestones, int BeatsRevealed, bool Defeated) StorySnapshot
        => (_storyState.StoryId, _storyState.FragmentsFound, _storyState.MachineKills,
            _storyState.Milestones, _storyState.BeatsRevealed, _storyState.GuardianDefeated);

    /// <summary>Restores the persisted story state at server start (server-wide, like the alliance graph).
    /// A fresh world starts the default pack; a save whose only row names a pack this build does not have also
    /// starts the default — but never over a real row of an installed pack (#2460, B07). The galaxy is built
    /// before this runs, so a revealed Guardian system is re-appended here (#2460, B06).</summary>
    private void LoadStoryState()
    {
        var stored = SelectActiveStoryRow(_repo.ListStoryStates(), Rules.StoryId, _content.DefaultStory.Id, IsUsableStoryRow);
        if (stored is not null && !IsUsableStoryRow(stored.StoryId))
        {
            _log.Warn($"Story pack '{stored.StoryId}' of this save is not installed (or cannot be a world story) — starting the default story; the stored row is kept untouched.");
        }

        LoadStoryStateFrom(stored);

        // The galaxy is regenerated from the seed before the story loads (BootStage "galaxy" runs first), so the
        // check at the end of BuildGalaxy always saw an unloaded state and a revealed-but-unwon save lost its
        // finale system from the star map on every restart (B06). Re-append it now that the flags are known.
        if (_storyState.GuardianSystemRevealed)
        {
            EnsureGuardianSystemInGalaxy();
        }

        SyncLeadListener(); // #2475: the chapter leads listen on the bus while the story has any
    }

    /// <summary>Whether a stored row names something this build can run: an installed world-capable pack (#2465: never a
    /// beat-less or player pack, <see cref="Shared.Content.GameContent.TryGetWorldStory"/>) or the "none" sandbox.</summary>
    private bool IsUsableStoryRow(string storyId)
        => string.Equals(storyId, StoryRegistry.NoneStoryId, StringComparison.OrdinalIgnoreCase) || _content.TryGetWorldStory(storyId, out _);

    /// <summary>#2460 (B05/B07): which persisted <c>story_state</c> row is the save's active one. A save holds one
    /// row per pack it ever ran (an admin switch writes a new row and leaves the old one), and the database lists
    /// them in no defined order — the first row used to win, so a restart could bring a switched-away pack back
    /// with all its progress. In order:
    /// <list type="number">
    /// <item>rows a current build can run beat a row of an uninstalled pack — that one must never cost real
    /// progress;</item>
    /// <item>the row carrying the active marker (<see cref="StoredStoryState.Active"/>);</item>
    /// <item>the most recently written row (<see cref="StoredStoryState.UpdatedUtc"/>);</item>
    /// <item>older saves without either: the row of the world's fresh-save choice, else the "none" row (only an
    /// explicit admin choice ever writes it), else the default pack's row, else the lowest id.</item>
    /// </list>
    /// Null when the save has no row yet (a fresh world).</summary>
    internal static StoredStoryState? SelectActiveStoryRow(IReadOnlyList<StoredStoryState> rows, string rulesStoryId,
        string defaultStoryId, Func<string, bool> isUsable)
    {
        if (rows.Count == 0)
        {
            return null;
        }

        var usable = rows.Where(r => isUsable(r.StoryId)).ToList();
        var pool = usable.Count > 0 ? usable : rows.ToList();

        var marked = pool.Where(r => r.Active).ToList();
        if (marked.Count > 0)
        {
            pool = marked;
        }

        var stamped = pool.Where(r => !string.IsNullOrEmpty(r.UpdatedUtc)).ToList();
        if (stamped.Count > 0)
        {
            return stamped
                .OrderByDescending(r => r.UpdatedUtc, StringComparer.Ordinal)
                .ThenBy(r => r.StoryId, StringComparer.Ordinal)
                .First();
        }

        static bool Is(StoredStoryState r, string id) => !string.IsNullOrEmpty(id) && string.Equals(r.StoryId, id, StringComparison.OrdinalIgnoreCase);
        return pool.FirstOrDefault(r => Is(r, rulesStoryId))
            ?? pool.FirstOrDefault(r => Is(r, StoryRegistry.NoneStoryId))
            ?? pool.FirstOrDefault(r => Is(r, defaultStoryId))
            ?? pool.OrderBy(r => r.StoryId, StringComparer.Ordinal).First();
    }

    /// <summary>Applies the chosen row (or the fresh-save choice when there is none) to the runtime story state.</summary>
    private void LoadStoryStateFrom(StoredStoryState? stored)
    {
        if (stored is not null && string.Equals(stored.StoryId, StoryRegistry.NoneStoryId, StringComparison.OrdinalIgnoreCase))
        {
            _story = null; // the save chose the sandbox (no story)
            _storyState.StoryId = StoryRegistry.NoneStoryId;
            return;
        }

        if (stored is not null && _content.TryGetWorldStory(stored.StoryId, out var def))
        {
            _story = def;
            _storyState.StoryId = stored.StoryId;
            _storyState.FragmentsFound = stored.FragmentsFound;
            _storyState.MachineKills = stored.MachineKills;
            _storyState.Milestones = stored.Milestones;
            _storyState.BeatsRevealed = stored.BeatsRevealed;
            _storyState.GuardianSystemRevealed = stored.GuardianSystemRevealed;
            _storyState.GuardianDefeated = stored.GuardianDefeated;
            _storyState.FoundFragmentKeys = new HashSet<string>(stored.FoundFragmentKeys ?? new List<string>(), StringComparer.Ordinal);
            _storyState.MilestoneKeys = new HashSet<string>(stored.MilestoneKeys ?? new List<string>(), StringComparer.Ordinal);
            _storyState.BodiesWithoutFragment = stored.BodiesWithoutFragment;
        }
        else if (stored is null && string.Equals(Rules.StoryId, StoryRegistry.NoneStoryId, StringComparison.OrdinalIgnoreCase))
        {
            _story = null; // fresh save, world option chose the sandbox
            _storyState.StoryId = StoryRegistry.NoneStoryId;
            return;
        }
        else if (stored is null && !string.IsNullOrEmpty(Rules.StoryId) && _content.TryGetWorldStory(Rules.StoryId, out var chosen))
        {
            _story = chosen; // fresh save, world option chose a specific pack
            _storyState.StoryId = chosen.Id;
        }
        else
        {
            // Fresh save (no choice), or the save holds nothing but rows of packs this build lacks → the built-in
            // default. SelectActiveStoryRow already preferred any real row, so this never writes zeros over one.
            _story = _content.DefaultStory;
            _storyState.StoryId = _story.Id;
        }

        // Mark any already-crossed beats as revealed (reveals B0 on a fresh world; idempotent on a restored
        // one). No players are joined yet, so this only advances the shared state; per-player reveal happens
        // on join via catch-up.
        AdvanceStory();
    }

    /// <summary>Writes the active pack's row — stamped and marked active (#2460, B05), so a restart picks it back
    /// up even when the save holds rows of other packs.</summary>
    private void PersistStoryState()
        => _repo.SaveStoryState(new StoredStoryState
        {
            Active = true,
            UpdatedUtc = UtcNowIso(),
            StoryId = _storyState.StoryId,
            FragmentsFound = _storyState.FragmentsFound,
            MachineKills = _storyState.MachineKills,
            Milestones = _storyState.Milestones,
            BeatsRevealed = _storyState.BeatsRevealed,
            GuardianSystemRevealed = _storyState.GuardianSystemRevealed,
            GuardianDefeated = _storyState.GuardianDefeated,
            FoundFragmentKeys = _storyState.FoundFragmentKeys.ToList(),
            MilestoneKeys = _storyState.MilestoneKeys.ToList(),
            BodiesWithoutFragment = _storyState.BodiesWithoutFragment,
        });

    // ---------------- Event hooks (called from gameplay) ----------------

    /// <summary>Records a net fragment found (text-only story find). Deduped by key, then advances the arc.</summary>
    public void RecordStoryFragment(string fragmentKey)
    {
        if (!StoryActive)
        {
            return;
        }

        if (!string.IsNullOrEmpty(fragmentKey) && !_storyState.FoundFragmentKeys.Add(fragmentKey))
        {
            return; // already found — never count the same fragment twice
        }

        _storyState.FragmentsFound++;
        _storyState.BodiesWithoutFragment = 0; // any find breaks the drought (#1109)
        AdvanceStory();
    }

    /// <summary>Records a Guardian-machine kill (space UFO / ground robot / scan-drone). Advances the arc; the
    /// contribution is capped in <see cref="StoryEngine"/> so combat can't be farmed past the fragments.</summary>
    public void RecordStoryMachineKill()
    {
        if (!StoryActive)
        {
            return;
        }

        _storyState.MachineKills++;
        AdvanceStory();
    }

    /// <summary>Records a repeatable story milestone: a star system newly mapped, a settlement helped (mission
    /// turned in). The once-per-save firsts go through <see cref="RecordStoryMilestone(string)"/>.</summary>
    public void RecordStoryMilestone()
    {
        if (!StoryActive)
        {
            return;
        }

        _storyState.Milestones++;
        AdvanceStory();
    }

    /// <summary>Records a once-per-save story milestone (#1105): the first base founded, the first station or
    /// self-built ship commissioned, the first companion tamed, the first hyperjump, the first vault opened,
    /// the first rune read on a world (<c>monument:&lt;body&gt;</c>). The key is remembered in
    /// <see cref="StoryState.MilestoneKeys"/> so a first can never be farmed by repeating it; a duplicate key
    /// is a no-op.</summary>
    public void RecordStoryMilestone(string onceKey)
    {
        if (!StoryActive || string.IsNullOrEmpty(onceKey) || !_storyState.MilestoneKeys.Add(onceKey))
        {
            return;
        }

        _storyState.Milestones++;
        AdvanceStory();
    }

    /// <summary>Reveals any beats the current progress newly crossed (to every online player who hasn't heard
    /// them), persists the state and broadcasts the updated meter.</summary>
    private void AdvanceStory()
    {
        if (_story is null)
        {
            return;
        }

        foreach (var beat in StoryEngine.AdvanceBeats(_story, _storyState, StoryScale)) // #2475: the one scale wrapper
        {
            RevealBeatToAll(beat);
        }

        RevealGuardianSystemIfReady(); // arc complete → place the finale system on the map (fires once)

        PersistStoryState();
        BroadcastStoryState();
    }

    private string BeatMilestoneKey(StoryBeat beat) => StoryPackKeys.Beat(_storyState.StoryId, beat.Index);

    private void RevealBeatToAll(StoryBeat beat)
    {
        foreach (var session in StoryListeners().ToList())
        {
            RevealBeatTo(session, beat);
        }
    }

    /// <summary>The joined players the story speaks to. An invisible fleet-admin observer is staff, not a pilot
    /// (#2460, N1): no beats, story lines or finale rewards while observing — the beats catch up when observer
    /// mode ends (<see cref="CatchUpStoryLines"/>) or on the next join.</summary>
    private IEnumerable<PlayerSession> StoryListeners() => _sessions.Values.Where(s => s.Joined && !s.Spectating);

    /// <summary>Speaks a beat to one player if they haven't heard it (tracked per-player, persisted), granting
    /// its one-time knowledge reward. Held while the player's story track has not started (#2470, synthesis §4.6): no key,
    /// no knowledge, no line — the track start hears it and credits its knowledge then. A beat that opens a chapter plays
    /// the chapter's opening scene first when <paramref name="stage"/> (the join catch-up of several beats stages only the
    /// current chapter), then the beat's own scene (#2474: the beat cards), then the line — which the scene shows itself
    /// (<c>replaceLineKeys</c>): the client keeps it out of VEGA's panel, the log still has it.</summary>
    private void RevealBeatTo(PlayerSession session, StoryBeat beat, bool stage = true)
    {
        if (!StoryTrackStarted(session) || !MarkBeatHeard(session, beat))
        {
            return; // held until the track starts — or this player already heard this beat
        }

        if (stage && _story is not null)
        {
            if (beat.Index > 0 && StoryChapters.OpenedByBeat(_story, beat.Index) is { } chapter)
            {
                PlayChapterOpening(session, chapter);
            }

            if (!string.IsNullOrEmpty(beat.Cutscene))
            {
                PlayBoundCutscene(session, _story, beat.Cutscene, _story.Id);
            }
        }

        if (!string.IsNullOrEmpty(beat.TextKey))
        {
            SendVegaLine(session, beat.TextKey, 2); // ShipAiLine kind 2 = memory/story
        }
    }

    /// <summary>Marks a beat heard for a player and credits its knowledge (once; the key is the per-player ledger). Returns
    /// whether it was new. <paramref name="sendInventory"/> = false lets a caller that marks several beats send the new
    /// knowledge once.</summary>
    private bool MarkBeatHeard(PlayerSession session, StoryBeat beat, bool sendInventory = true)
    {
        if (!session.State.Milestones.Add(BeatMilestoneKey(beat)))
        {
            return false;
        }

        if (beat.KnowledgeReward > 0)
        {
            session.State.KnowledgePoints += beat.KnowledgeReward;
            if (sendInventory)
            {
                SendInventory(session); // carries the new KnowledgePoints to the client
            }
        }

        return true;
    }

    /// <summary>On join: send the story meter and catch the player up on beats already revealed world-wide
    /// that they personally haven't heard (so latecomers are current without being spoiled beyond the shared
    /// progress).</summary>
    private void SendStoryStateOnJoin(PlayerSession session)
    {
        SendStoryState(session);

        if (_story is null)
        {
            return;
        }

        CatchUpStoryLines(session);
        SendStoryResolvedOnJoinIfMissed(session); // #1124: the ending, for whoever heard the arc out but missed the win
        SendDuelNodeTo(session); // #2478: a player who relogged mid-duel sees its current node again
    }

    /// <summary>Catches a player up on the world's story lines they have not heard: every revealed beat and, while
    /// the finale is open, the Guardian-system reveal (#2460, B10 — the only line that names the jump generator,
    /// which latecomers never got). Runs on join and when observer mode ends. Only for a player whose story track has
    /// started (#2470): before that everything is held, and the track start delivers it as one recap. Of the chapters the
    /// world crossed meanwhile only the CURRENT chapter's opening plays (synthesis §5.3), not one scene per chapter. Since
    /// #2471 (B09) a client with the Journal that missed <see cref="CatchUpCollapseThreshold"/> or more beats gets them
    /// silently in its Journal (heard, knowledge credited, the story state re-sent) and ONE line saying so
    /// (<see cref="StoryNarratorLines.JournalNews"/>) instead of a flood of lines; a single missed beat still speaks (#2474:
    /// with its beat card). #2474: the current chapter's opening is committed before any line (a line it shows waits with
    /// it), and a client that plays cutscenes whose missed beats crossed into a later chapter gets "The story so far" first
    /// (<see cref="PlayCatchUpRecap"/>) — the Journal line stays: it names the count and lights the Journal's badge.</summary>
    private void CatchUpStoryLines(PlayerSession session)
    {
        if (_story is null || session.Spectating || !StoryTrackStarted(session))
        {
            return;
        }

        var missed = new List<StoryBeat>();
        for (int i = 0; i < _storyState.BeatsRevealed && i < _story.Beats.Count; i++)
        {
            if (!session.State.Milestones.Contains(BeatMilestoneKey(_story.Beats[i])))
            {
                missed.Add(_story.Beats[i]);
            }
        }

        bool caughtUp = missed.Count > 0;
        bool collapse = session.SupportsPacks && missed.Count >= CatchUpCollapseThreshold;
        var current = caughtUp ? ChapterAt(TrackChapterIndex(session)) : null;

        // #2474: the scenes go first, so a line a queued scene shows (its replaceLineKeys) waits with it and the client never
        // reads it twice. When the missed beats crossed into a later chapter, "the story so far" (titles only) leads into the
        // current chapter's opening; a beat card plays for a single missed beat.
        if (collapse && current is not null)
        {
            PlayCatchUpRecap(session, missed);
        }

        if (current is not null)
        {
            PlayChapterOpening(session, current);
        }

        if (collapse)
        {
            foreach (var beat in missed)
            {
                MarkBeatHeard(session, beat, sendInventory: false);
            }

            if (missed.Any(b => b.KnowledgeReward > 0))
            {
                SendInventory(session); // the beats' knowledge, credited at once
            }

            _repo.SavePlayer(session.State);
            SendStoryState(session); // the Journal's chapter list now holds the missed beats
            SendVegaLine(session, StoryNarratorLines.JournalNews, 3, missed.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        else
        {
            foreach (var beat in missed)
            {
                RevealBeatTo(session, beat, stage: missed.Count == 1); // one missed beat: its card (its opening already played)
            }
        }

        if (_storyState.GuardianSystemRevealed && !_storyState.GuardianDefeated)
        {
            SpeakGuardianRevealedTo(session);
        }
    }

    /// <summary>#2474 (B09): a client that plays cutscenes and missed beats that crossed into a later chapter gets "The story
    /// so far" — the titles of the chapters before its current one, as at a track start — which leads straight into the
    /// current chapter's opening. Missed beats within the chapter the player was in bring no recap.</summary>
    private void PlayCatchUpRecap(PlayerSession session, IReadOnlyCollection<StoryBeat> missed)
    {
        var def = _story;
        if (def is null || !session.SupportsCutscenes)
        {
            return;
        }

        int heardUpTo = 0;
        for (int i = 0; i < _storyState.BeatsRevealed && i < def.Beats.Count; i++)
        {
            if (!missed.Contains(def.Beats[i]))
            {
                heardUpTo = Math.Max(heardUpTo, def.Beats[i].Index);
            }
        }

        int before = StoryChapters.TrackChapterIndex(def, StoryChapters.ChapterIndexOfBeat(def, heardUpTo));
        int current = TrackChapterIndex(session);
        if (current < 2 || current <= before)
        {
            return;
        }

        PlayRecap(session, def.Id, def.Chapters.Skip(1).Take(current - 1).Select(c => c.TitleKey).Where(k => !string.IsNullOrEmpty(k)));
    }

    private void SendStoryState(PlayerSession session) => Send(session, BuildStoryState(session));

    private void BroadcastStoryState()
    {
        foreach (var session in _sessions.Values.Where(s => s.Joined))
        {
            Send(session, BuildStoryState(session)); // per receiver: memory/lore keys are per player (#1110)
            SendVegaObjective(session);              // the chip's story objective follows the arc (#1110)
        }
    }

    /// <summary>Milestone-key prefixes of the per-player story surfaces snapshotted to the client (#1110/#1111).</summary>
    private const string MemoryMilestonePrefix = "story:mem:";
    private const string LoreMilestonePrefix = "lore:";

    private StoryStateMessage BuildStoryState(PlayerSession session)
    {
        int target = _story is { Beats.Count: > 0 } d ? d.Beats[d.Beats.Count - 1].Threshold : 0;
        var mine = session.State.Milestones;
        return new StoryStateMessage
        {
            StoryId = _storyState.StoryId,
            Active = StoryActive,
            Progress = StoryProgressShown(), // #2475: the one scale wrapper, clamped for the meter
            ProgressTarget = target,
            FragmentsFound = _storyState.FragmentsFound,
            MachineKills = _storyState.MachineKills,
            Milestones = _storyState.Milestones,
            BeatsRevealed = _storyState.BeatsRevealed,
            GuardianSystemRevealed = _storyState.GuardianSystemRevealed,
            GuardianDefeated = _storyState.GuardianDefeated,
            // Re-readable logs (#1110/#1111): the save's found fragments + this player's memories/lore, so the
            // Story tab and the Codex survive a rejoin instead of resetting to the session's live events.
            FoundFragmentKeys = _storyState.FoundFragmentKeys.ToArray(),
            PlayerMemoryKeys = mine.Where(m => m.StartsWith(MemoryMilestonePrefix, StringComparison.Ordinal))
                .Select(m => m.Substring(MemoryMilestonePrefix.Length)).ToArray(),
            FoundLoreKeys = mine.Where(m => m.StartsWith(LoreMilestonePrefix, StringComparison.Ordinal))
                .Select(m => m.Substring(LoreMilestonePrefix.Length)).ToArray(),
            HeardBeats = HeardBeats(session), // #2462 (B08): the Story tab's beat list survives a rejoin
            SeenCutscenes = SeenCutscenes(session), // #2468: the Story tab's "Watch again" list
            // #2470: the player's story track — started, their (derived) chapter, the world's chapter, the crew status
            // line, and the relay insights the save has spoken (the log of a player who joined the story later).
            Started = StoryTrackStarted(session),
            Chapter = ChapterAt(TrackChapterIndex(session))?.Id ?? string.Empty,
            WorldChapter = ChapterAt(WorldChapterIndex())?.Id ?? string.Empty,
            ShowCrewChapter = ShowCrewChapterFor(session),
            RelayInsightKeys = StoryTrackStarted(session) ? RelayInsightKeysSpoken() : System.Array.Empty<string>(),
            NoteKeys = StoryTrackStarted(session) ? StoryNoteKeys(session) : System.Array.Empty<string>(), // #2471: the Journal's notes
        };
    }

    /// <summary>The cutscenes this player's client confirmed (#2468), every pack, as <c>pack/id</c> (sorted) — from the
    /// <c>story:&lt;pack&gt;:cut:&lt;id&gt;</c> keys (<see cref="StoryPackKeys.TryParseCutsceneSeen"/>; the <c>:sent</c>
    /// form is an owed scene, not a seen one). The client orders the list by the packs' own definitions.</summary>
    private static string[] SeenCutscenes(PlayerSession session)
    {
        var seen = new List<string>();
        foreach (var m in session.State.Milestones)
        {
            if (StoryPackKeys.TryParseCutsceneSeen(m, out string packId, out string id))
            {
                seen.Add(packId + StoryCutsceneIds.PackSeparator + id);
            }
        }

        seen.Sort(StringComparer.Ordinal);
        return seen.ToArray();
    }

    /// <summary>The beat indexes this player has heard of the active story (#2462, B08), ascending — read from their
    /// <c>story:&lt;id&gt;:beat:&lt;n&gt;</c> milestones (<see cref="BeatMilestoneKey"/>). Other <c>story:</c> keys of the same
    /// pack (<c>:finale:revealed</c>, <c>:resolved</c>, …) do not parse as a number and are skipped.</summary>
    private int[] HeardBeats(PlayerSession session)
    {
        if (_story is null)
        {
            return System.Array.Empty<int>();
        }

        string prefix = "story:" + _storyState.StoryId + ":beat:";
        var heard = new List<int>();
        foreach (var m in session.State.Milestones)
        {
            if (m.StartsWith(prefix, StringComparison.Ordinal)
                && int.TryParse(m.Substring(prefix.Length), System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out int index))
            {
                heard.Add(index);
            }
        }

        heard.Sort();
        return heard.ToArray();
    }

    // ---------------- Active-story selection (admin) ----------------

    private void HandleStorySelect(PlayerSession session, StorySelectIntent intent)
    {
        if (!session.State.IsAdmin)
        {
            Reject(session, "story", "@srv.admin.story_admin_only");
            return;
        }

        SetActiveStory(intent.StoryId);
    }

    /// <summary>Switches the save's active story pack (admin / world option). "none" disables the story; an
    /// unknown id — or a pack that cannot be the world story, like the beat-less tutorial (#2465) — is ignored.
    /// Switching resets the per-save progress to a fresh state for the chosen pack.</summary>
    public void SetActiveStory(string storyId)
    {
        string previous = _storyState.StoryId;
        if (string.Equals(storyId, StoryRegistry.NoneStoryId, StringComparison.OrdinalIgnoreCase))
        {
            _story = null;
            ResetStoryState(StoryRegistry.NoneStoryId);
        }
        else if (_content.TryGetWorldStory(storyId, out var def))
        {
            _story = def;
            ResetStoryState(def.Id);
        }
        else
        {
            return; // unknown pack — leave the active story unchanged
        }

        SyncLeadListener();       // #2475: the new story's chapter leads (or none)
        AdvanceStory();           // persists + broadcasts (and reveals B0 for a real pack)
        if (_story is null)
        {
            PersistStoryState();  // AdvanceStory no-ops when disabled — persist the "none" choice here
            BroadcastStoryState();
        }

        DeactivateStoryRow(previous);
        StoryTrackOnStorySwitch(); // #2470: the new story meets the players online now (a "none" world switched to VEGA)
    }

    /// <summary>#2460 (B05): clears the active marker on the row an admin switch left behind, so a restart can
    /// never bring the old pack back. The row itself (and its progress) stays, as before.</summary>
    private void DeactivateStoryRow(string storyId)
    {
        if (string.IsNullOrEmpty(storyId) || string.Equals(storyId, _storyState.StoryId, StringComparison.OrdinalIgnoreCase))
        {
            return; // re-selecting the active pack resets it in place — its row stays the active one
        }

        foreach (var row in _repo.ListStoryStates())
        {
            if (row.Active && string.Equals(row.StoryId, storyId, StringComparison.OrdinalIgnoreCase))
            {
                row.Active = false;
                _repo.SaveStoryState(row);
            }
        }
    }

    private void ResetStoryState(string storyId)
    {
        _storyState.StoryId = storyId;
        _storyState.FragmentsFound = 0;
        _storyState.MachineKills = 0;
        _storyState.Milestones = 0;
        _storyState.BeatsRevealed = 0;
        _storyState.GuardianSystemRevealed = false;
        _storyState.GuardianDefeated = false;
        _storyState.FoundFragmentKeys.Clear();
        _storyState.MilestoneKeys.Clear(); // a new pack starts its once-per-save firsts fresh
        _storyState.BodiesWithoutFragment = 0;
        ResetFinaleRuntime();
    }

    // ---------------- Finale pacification (P6) ----------------

    /// <summary>Finale win: the Guardian core is shut down — pacify the galaxy. One-way per-save: despawns the
    /// live planet machines on every loaded world (the dramatic beat — "across every world the machines stand down"; #2478,
    /// B51: it used to clear only the active world cursor, so the machines of a friend's world kept hunting until the
    /// Remnant rules thinned them out), persists, and broadcasts. The game continues
    /// afterwards under the Remnant Protocol (#1206, see <c>RemnantEra</c>): the flag no longer switches every
    /// spawn off — the planet cap halves, refills slow down, only scan-drones remain, space waves keep only
    /// pirate havens / the frontier-danger tier, bandit ships keep only their havens.</summary>
    private void MarkGuardianDefeated()
    {
        if (!StoryActive || _storyState.GuardianDefeated)
        {
            return;
        }

        _storyState.GuardianDefeated = true;
        foreach (var world in _worlds.Loaded)
        {
            if (world.PlanetEnemies.Count > 0)
            {
                world.PlanetEnemies.Clear(); // despawn the live planet machines of this world
                world.EnemyListDirty = true; // its players get the empty list at the end of its next tick (#1530)
            }
        }

        BroadcastPlanetEnemies(); // the active world's list goes out either way (as before)
        PersistStoryState();
        BroadcastStoryState();
    }

    // ---------------- Player memories (P4: personal, unlocked by defeating machines) ----------------

    private const double PlayerMemoryDropChance = 0.34; // chance per machine kill to release the next memory
    private readonly System.Random _memoryRng = new(9173);

    private string MemoryMilestoneKey(StoryMemory mem) => "story:mem:" + mem.Key;

    /// <summary>On a machine kill, a chance to release a damaged memory remnant → the killer's next unfound
    /// personal memory (in order). Per-player; non-contradictory in MP (each player is a different imprint).</summary>
    private void TryDropPlayerMemory(PlayerSession session)
    {
        if (!StoryActive || _story is null || _story.Memories.Count == 0)
        {
            return;
        }

        if (_memoryRng.NextDouble() < PlayerMemoryDropChance)
        {
            UnlockNextPlayerMemory(session);
        }
    }

    /// <summary>Unlocks the player's next unfound memory (in pack order), if any. False once all are unlocked.</summary>
    private bool UnlockNextPlayerMemory(PlayerSession session)
    {
        if (_story is null)
        {
            return false;
        }

        foreach (var mem in _story.Memories)
        {
            if (session.State.Milestones.Add(MemoryMilestoneKey(mem)))
            {
                if (!string.IsNullOrEmpty(mem.TextKey))
                {
                    Send(session, new PlayerMemoryRevealed { TextKey = mem.TextKey });
                }

                return true;
            }
        }

        return false; // all memories already unlocked for this player
    }

    /// <summary>How many personal memories a player has unlocked (for the Story Log + tests).</summary>
    public int PlayerMemoryCount(string playerId)
    {
        var session = FindSessionByPlayerId(playerId);
        if (session is null || _story is null)
        {
            return 0;
        }

        return _story.Memories.Count(m => session.State.Milestones.Contains(MemoryMilestoneKey(m)));
    }

    // ---------------- Event-driven story insights (taming ward + non-cube shapes) ----------------
    //
    // Discrete, action-triggered VEGA memories that tie the taming + block-shape features into the arc. Unlike the
    // threshold beats (B0–B12) these are NOT progress-ordered and must NOT join the Beats list (that would break the
    // engine's strict beat ordering) — they are spoken directly on their gameplay event, once per player, tracked with the
    // same per-player milestone mechanism the beats use. Since #2470 they are pack data (StoryDefinition.Insights: event,
    // the beat that must be revealed first, the line, the once-key) — the hard-coded "six beats" check is gone.

    /// <summary>The insight event names of <see cref="StoryInsight.Event"/> the server raises.</summary>
    internal const string CompanionWardInsightEvent = "companion_ward";
    internal const string ShapeAnomalyInsightEvent = "shape_noncube";

    /// <summary>Speaks the active story's insight for <paramref name="insightEvent"/> to one player, once — only after the
    /// arc revealed the insight's <see cref="StoryInsight.MinBeat"/> (an early tame or shape must not pre-empt the
    /// Guardian's reveal) and only once the player's story track has started (#2470: held, the once-key is not written,
    /// so the insight fires at the next occasion). A pack without the insight (or the "none" sandbox) says nothing. Returns
    /// whether it spoke.</summary>
    private bool RevealStoryInsight(PlayerSession session, string insightEvent)
    {
        var insight = _story?.Insights.FirstOrDefault(i => string.Equals(i.Event, insightEvent, StringComparison.Ordinal));
        if (insight is null || string.IsNullOrEmpty(insight.TextKey) || _storyState.BeatsRevealed <= insight.MinBeat
            || !StoryTrackStarted(session))
        {
            return false; // no such insight, story too early or held (flag NOT consumed → may fire later)
        }

        string onceKey = string.IsNullOrEmpty(insight.OnceKey) ? "story:insight:" + insight.Event : insight.OnceKey;
        if (!session.State.Milestones.Add(onceKey))
        {
            return false; // already heard
        }

        SendVegaLine(session, insight.TextKey, 2); // ShipAiLine kind 2 = memory/story
        return true;
    }

    /// <summary>VEGA's realisation the first time a present companion makes a Guardian machine stand down: the
    /// network reads a creature-bonded human as part of the biosphere it guards, not as prey (insight
    /// <see cref="CompanionWardInsightEvent"/>). Returns true if it spoke this call. Driven from <c>TickEnemies</c> (a
    /// warded player within hunt range of a machine).</summary>
    private bool RevealCompanionWardInsight(PlayerSession session) => RevealStoryInsight(session, CompanionWardInsightEvent);

    /// <summary>VEGA's recovered memory the first time the player forms a non-cube block: the Service always
    /// built in cubes by design — to the Guardian any deliberate curve or edge is a constructed anomaly, the
    /// mark of a human hand (insight <see cref="ShapeAnomalyInsightEvent"/>). Narrative only (no mechanic). Returns true if
    /// it spoke this call. Driven from <c>HandleShapeCraft</c>.</summary>
    private bool RevealShapeAnomalyMemory(PlayerSession session) => RevealStoryInsight(session, ShapeAnomalyInsightEvent);

    // ---------------- P7: world knowledge level (NPC flavour gating) ----------------

    /// <summary>The world's story "knowledge level" 0..4 (0 = nothing learned … 3 = the arc is nearly or fully
    /// told, 4 = the Guardian core is shut down), derived from how far the shared arc has progressed. Drives which
    /// story flavour lines settlement/station NPCs are allowed to speak (P7). Level 4 is the WIN alone (#2460, B44):
    /// its lines say "the machines went still … thank you", which read wrong while the machines still hunted
    /// between the last beat and the duel.</summary>
    public int WorldKnowledgeLevel()
    {
        if (_story is null || _story.Beats.Count == 0)
        {
            return 0;
        }

        if (_storyState.GuardianDefeated)
        {
            return 4;
        }

        int target = _story.Beats[_story.Beats.Count - 1].Threshold;
        if (target <= 0)
        {
            return 0;
        }

        double frac = StoryProgressShown() / (double)target; // #2475: the one scale wrapper, clamped (never drops back)
        if (frac >= 0.66) return 3;
        if (frac >= 0.33) return 2;
        if (frac > 0.0) return 1;
        return 0;
    }

    // ---------------- Admin QA (P8 telemetry) ----------------

    /// <summary>Admin QA: fast-forward the active story by adding <paramref name="steps"/> milestones (each
    /// advances the arc, revealing any crossed beats). Returns the new beats-revealed count.</summary>
    public int AdminAdvanceStory(int steps)
    {
        if (!StoryActive)
        {
            return _storyState.BeatsRevealed;
        }

        _storyState.Milestones += System.Math.Max(1, steps);
        AdvanceStory();
        return _storyState.BeatsRevealed;
    }

    /// <summary>Admin QA: drive the arc to completion so the Guardian finale system reveals — jumps testers
    /// straight to the finale. No-op when no story is active.</summary>
    public void AdminRevealFinale()
    {
        if (_story is null || _storyState.GuardianSystemRevealed)
        {
            return;
        }

        int target = _story.Beats.Count > 0 ? _story.Beats[_story.Beats.Count - 1].Threshold : 0;
        double scale = System.Math.Max(0.05, StoryScale); // #2475: the one scale wrapper (a peaceful world needs fewer)
        int needed = (int)System.Math.Ceiling(target / (double)System.Math.Max(1, _story.MilestoneWeight) / scale) + 2;
        _storyState.Milestones += needed;
        AdvanceStory(); // reveals every crossed beat + the finale, persists + broadcasts once
    }

    /// <summary>Admin QA: reveal every story fragment and personal memory to one player so the full lore reads
    /// in the Story tab (mirrors having found them all). Each fragment is also recorded against the shared arc
    /// (deduped), so this completes the readable beats too. Returns the number of fragment archive texts sent.</summary>
    public int AdminRevealAllLore(PlayerSession session)
    {
        if (_story is null)
        {
            return 0;
        }

        int sent = 0;
        foreach (var frag in _story.Fragments)
        {
            Send(session, new NetFragmentRevealed { Category = frag.Category, TextKey = frag.TextKey });
            RecordStoryFragment(frag.Key); // dedupes + counts toward the arc + advances/reveals beats
            sent++;
        }

        while (UnlockNextPlayerMemory(session))
        {
            // unlock every remaining personal memory for this player (each call reveals the next one)
        }

        return sent;
    }

    // ---------------- Test hooks ----------------

    /// <summary>Test hook: record a net fragment find (mirrors the gameplay event).</summary>
    public void RecordStoryFragmentForTest(string fragmentKey) => RecordStoryFragment(fragmentKey);

    /// <summary>Test seam: the story-state snapshot exactly as it would be sent to this player (#1110).</summary>
    public StoryStateMessage? StoryStateForTest(string playerId)
        => FindSessionByPlayerId(playerId) is { } session ? BuildStoryState(session) : null;

    /// <summary>Test seam: place a structure fragment at a marker position (mirrors <c>SpawnStructureLoot</c>).</summary>
    public void PlaceStructureFragmentForTest(string markerType, float x, float y, float z)
        => TryPlaceStructureFragment(markerType, new Shared.Geometry.Vector3f(x, y, z));

    /// <summary>Test hook: record a Guardian-machine kill (mirrors the gameplay event).</summary>
    public void RecordStoryMachineKillForTest() => RecordStoryMachineKill();

    /// <summary>Test hook: record a story milestone (mirrors the gameplay event).</summary>
    public void RecordStoryMilestoneForTest() => RecordStoryMilestone();

    /// <summary>Test hook: record a once-per-save milestone (mirrors the gameplay firsts, #1105).</summary>
    public void RecordStoryMilestoneForTest(string onceKey) => RecordStoryMilestone(onceKey);

    /// <summary>Test hook: switch the active story pack (mirrors the admin intent).</summary>
    public void SetActiveStoryForTest(string storyId) => SetActiveStory(storyId);

    /// <summary>Test hook: unconditionally unlock a player's next personal memory (mirrors a lucky drop).</summary>
    public bool GrantNextPlayerMemoryForTest(string playerId)
    {
        var session = FindSessionByPlayerId(playerId);
        return session is not null && UnlockNextPlayerMemory(session);
    }

    /// <summary>Test hook: win the finale (pacify the galaxy — stops all machine spawns).</summary>
    public void MarkGuardianDefeatedForTest() => MarkGuardianDefeated();

    /// <summary>Test hook: attempt the companion-ward insight for a player (mirrors a machine standing down in
    /// a companion's presence); returns true if VEGA spoke it this call.</summary>
    public bool RevealCompanionWardInsightForTest(string playerId)
        => FindSessionByPlayerId(playerId) is { } s && RevealCompanionWardInsight(s);

    /// <summary>Test hook: attempt the non-cube-shape memory for a player (mirrors forming a shape); returns
    /// true if VEGA spoke it this call.</summary>
    public bool RevealShapeAnomalyMemoryForTest(string playerId)
        => FindSessionByPlayerId(playerId) is { } s && RevealShapeAnomalyMemory(s);

    /// <summary>Test hook: whether a player carries a given per-player story milestone flag.</summary>
    public bool HasStoryMilestoneForTest(string playerId, string key)
        => FindSessionByPlayerId(playerId)?.State.Milestones.Contains(key) ?? false;
}
