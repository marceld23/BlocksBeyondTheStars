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
    /// A fresh world starts the default pack; an unknown stored pack also falls back to the default.</summary>
    private void LoadStoryState()
    {
        var stored = _repo.ListStoryStates().FirstOrDefault();

        if (stored is not null && string.Equals(stored.StoryId, StoryRegistry.NoneStoryId, StringComparison.OrdinalIgnoreCase))
        {
            _story = null; // the save chose the sandbox (no story)
            _storyState.StoryId = StoryRegistry.NoneStoryId;
            return;
        }

        if (stored is not null && _content.TryGetStory(stored.StoryId, out var def))
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
        }
        else if (stored is null && string.Equals(Rules.StoryId, StoryRegistry.NoneStoryId, StringComparison.OrdinalIgnoreCase))
        {
            _story = null; // fresh save, world option chose the sandbox
            _storyState.StoryId = StoryRegistry.NoneStoryId;
            return;
        }
        else if (stored is null && !string.IsNullOrEmpty(Rules.StoryId) && _content.TryGetStory(Rules.StoryId, out var chosen))
        {
            _story = chosen; // fresh save, world option chose a specific pack
            _storyState.StoryId = chosen.Id;
        }
        else
        {
            _story = _content.DefaultStory; // fresh save (no choice) or an unknown stored pack → built-in default
            _storyState.StoryId = _story.Id;
        }

        // Mark any already-crossed beats as revealed (reveals B0 on a fresh world; idempotent on a restored
        // one). No players are joined yet, so this only advances the shared state; per-player reveal happens
        // on join via catch-up.
        AdvanceStory();
    }

    private void PersistStoryState()
        => _repo.SaveStoryState(new StoredStoryState
        {
            StoryId = _storyState.StoryId,
            FragmentsFound = _storyState.FragmentsFound,
            MachineKills = _storyState.MachineKills,
            Milestones = _storyState.Milestones,
            BeatsRevealed = _storyState.BeatsRevealed,
            GuardianSystemRevealed = _storyState.GuardianSystemRevealed,
            GuardianDefeated = _storyState.GuardianDefeated,
            FoundFragmentKeys = _storyState.FoundFragmentKeys.ToList(),
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

    /// <summary>Records a story milestone (system mapped / settlement helped / first base or station built).</summary>
    public void RecordStoryMilestone()
    {
        if (!StoryActive)
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

        foreach (var beat in StoryEngine.AdvanceBeats(_story, _storyState, Rules.StoryProgressScale))
        {
            RevealBeatToAll(beat);
        }

        RevealGuardianSystemIfReady(); // arc complete → place the finale system on the map (fires once)

        PersistStoryState();
        BroadcastStoryState();
    }

    private string BeatMilestoneKey(StoryBeat beat) => "story:" + _storyState.StoryId + ":beat:" + beat.Index;

    private void RevealBeatToAll(StoryBeat beat)
    {
        foreach (var session in _sessions.Values.Where(s => s.Joined))
        {
            RevealBeatTo(session, beat);
        }
    }

    /// <summary>Speaks a beat to one player if they haven't heard it (tracked per-player, persisted), granting
    /// its one-time knowledge reward.</summary>
    private void RevealBeatTo(PlayerSession session, StoryBeat beat)
    {
        if (!session.State.Milestones.Add(BeatMilestoneKey(beat)))
        {
            return; // this player already heard this beat
        }

        if (beat.KnowledgeReward > 0)
        {
            session.State.KnowledgePoints += beat.KnowledgeReward;
            SendInventory(session); // carries the new KnowledgePoints to the client
        }

        if (!string.IsNullOrEmpty(beat.TextKey))
        {
            SendVegaLine(session, beat.TextKey, 2); // ShipAiLine kind 2 = memory/story
        }
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

        for (int i = 0; i < _storyState.BeatsRevealed && i < _story.Beats.Count; i++)
        {
            RevealBeatTo(session, _story.Beats[i]);
        }
    }

    private void SendStoryState(PlayerSession session) => Send(session, BuildStoryState());

    private void BroadcastStoryState()
    {
        var msg = BuildStoryState();
        foreach (var session in _sessions.Values.Where(s => s.Joined))
        {
            Send(session, msg);
        }
    }

    private StoryStateMessage BuildStoryState()
    {
        int target = _story is { Beats.Count: > 0 } d ? d.Beats[d.Beats.Count - 1].Threshold : 0;
        return new StoryStateMessage
        {
            StoryId = _storyState.StoryId,
            Active = StoryActive,
            Progress = _story is null ? 0 : StoryEngine.Progress(_story, _storyState, Rules.StoryProgressScale),
            ProgressTarget = target,
            FragmentsFound = _storyState.FragmentsFound,
            MachineKills = _storyState.MachineKills,
            Milestones = _storyState.Milestones,
            BeatsRevealed = _storyState.BeatsRevealed,
            GuardianSystemRevealed = _storyState.GuardianSystemRevealed,
            GuardianDefeated = _storyState.GuardianDefeated,
        };
    }

    // ---------------- Active-story selection (admin) ----------------

    private void HandleStorySelect(PlayerSession session, StorySelectIntent intent)
    {
        if (!session.State.IsAdmin)
        {
            Reject(session, "story", "Only an admin can change the active story.");
            return;
        }

        SetActiveStory(intent.StoryId);
    }

    /// <summary>Switches the save's active story pack (admin / world option). "none" disables the story; an
    /// unknown id is ignored. Switching resets the per-save progress to a fresh state for the chosen pack.</summary>
    public void SetActiveStory(string storyId)
    {
        if (string.Equals(storyId, StoryRegistry.NoneStoryId, StringComparison.OrdinalIgnoreCase))
        {
            _story = null;
            ResetStoryState(StoryRegistry.NoneStoryId);
        }
        else if (_content.TryGetStory(storyId, out var def))
        {
            _story = def;
            ResetStoryState(def.Id);
        }
        else
        {
            return; // unknown pack — leave the active story unchanged
        }

        AdvanceStory();           // persists + broadcasts (and reveals B0 for a real pack)
        if (_story is null)
        {
            PersistStoryState();  // AdvanceStory no-ops when disabled — persist the "none" choice here
            BroadcastStoryState();
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
        ResetFinaleRuntime();
    }

    // ---------------- Finale pacification (P6) ----------------

    /// <summary>Finale win: the Guardian core is shut down — pacify the galaxy. One-way per-save: gates the
    /// pack's planet + space machine spawns off (see <c>PlanetEnemiesActive</c> + the space spawn), despawns
    /// live planet machines on the active world, persists, and broadcasts. The game continues afterwards.</summary>
    private void MarkGuardianDefeated()
    {
        if (!StoryActive || _storyState.GuardianDefeated)
        {
            return;
        }

        _storyState.GuardianDefeated = true;
        _planetEnemies.Clear();   // despawn live planet machines on the active world
        BroadcastPlanetEnemies();
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
    // Two discrete, action-triggered VEGA memories that tie the taming + block-shape features into the arc.
    // Unlike the threshold beats (B0–B12) these are NOT progress-gated and must NOT join the Beats list (that
    // would break the engine's strict beat ordering) — they are spoken directly on their gameplay event, once
    // per player, tracked with the same per-player milestone mechanism the beats use.

    /// <summary>Beats spoken once the Guardian's protective mandate is known (beat B5 "The Guardian"): beats
    /// 0..5 revealed ⇒ BeatsRevealed ≥ 6. Both insights stay silent until then so an early tame or shape can't
    /// pre-empt the Guardian reveal — they defer to the next eligible trigger (the per-player flag is only
    /// consumed once a line is actually spoken). Assumes the default vega_protocol arc shape.</summary>
    private const int GuardianMandateBeatCount = 6;

    /// <summary>True once the shared arc has named the Guardian and its "protect the living worlds" mandate —
    /// the premise both insights build on. Requires an active story (silent in the "none" sandbox).</summary>
    private bool GuardianMandateKnown => StoryActive && _storyState.BeatsRevealed >= GuardianMandateBeatCount;

    private const string CompanionWardInsightKey = "story:insight:companion-ward";
    private const string ShapeAnomalyInsightKey = "story:insight:shape-anomaly";

    /// <summary>VEGA's realisation the first time a present companion makes a Guardian machine stand down: the
    /// network reads a creature-bonded human as part of the biosphere it guards, not as prey. Once-only per
    /// player, gated behind the Guardian reveal. Returns true if it spoke this call. Driven from
    /// <c>TickEnemies</c> (a warded player within hunt range of a machine).</summary>
    private bool RevealCompanionWardInsight(PlayerSession session)
    {
        if (!GuardianMandateKnown || !session.State.Milestones.Add(CompanionWardInsightKey))
        {
            return false; // story too early (flag NOT consumed → may fire later), or already heard
        }

        SendVegaLine(session, "story.vega.insight.companion_ward", 2); // ShipAiLine kind 2 = memory/story
        return true;
    }

    /// <summary>VEGA's recovered memory the first time the player forms a non-cube block: the Service always
    /// built in cubes by design — to the Guardian any deliberate curve or edge is a constructed anomaly, the
    /// mark of a human hand. Narrative only (no mechanic). Once-only per player, gated behind the reveal.
    /// Returns true if it spoke this call. Driven from <c>HandleShapeCraft</c>.</summary>
    private bool RevealShapeAnomalyMemory(PlayerSession session)
    {
        if (!GuardianMandateKnown || !session.State.Milestones.Add(ShapeAnomalyInsightKey))
        {
            return false;
        }

        SendVegaLine(session, "story.vega.insight.shape_anomaly", 2);
        return true;
    }

    // ---------------- P7: world knowledge level (NPC flavour gating) ----------------

    /// <summary>The world's story "knowledge level" 0..4 (0 = nothing learned … 4 = the Guardian core is
    /// known/defeated), derived from how far the shared arc has progressed. Drives which story flavour lines
    /// settlement/station NPCs are allowed to speak (P7).</summary>
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

        double frac = StoryEngine.Progress(_story, _storyState, Rules.StoryProgressScale) / (double)target;
        if (frac >= 1.0) return 4;
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
        double scale = System.Math.Max(0.05, Rules.StoryProgressScale);
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

    /// <summary>Test hook: record a Guardian-machine kill (mirrors the gameplay event).</summary>
    public void RecordStoryMachineKillForTest() => RecordStoryMachineKill();

    /// <summary>Test hook: record a story milestone (mirrors the gameplay event).</summary>
    public void RecordStoryMilestoneForTest() => RecordStoryMilestone();

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
