// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Story;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The world story's per-player track (#2470, synthesis §4.6/§4.7/§4.10/§4.11): VEGA as its own pack. The arc stays
/// world-shared (<see cref="StoryState"/>, score and beats unchanged); what is per player is when the story reaches them.
/// <list type="bullet">
/// <item><b>Prologue</b> — the cold open of the game: the pack's <c>introCutscene</c> plays once for a player who arrives with
/// no key of the story at all (a newcomer, or everyone on a world just switched from "none" to the story), never on a
/// tutorial restart; ledger <c>story:&lt;id&gt;:cut:prologue</c>. B0 is its speaker page and counts as heard with it. A client
/// without <c>cutscenes1</c> gets the old full-screen pages (Kind 4).</item>
/// <item><b>Start rule</b> (<c>story:&lt;id&gt;:started</c>): the pack's <c>start.after</c> holds (the basic training through),
/// or the tutorial is off for the world or the player, or the player arrived as a veteran / has heard a beat beyond B0, or
/// asked for it ("Start the story now", <see cref="StoryPackActions.StartPack"/>), or stands in the finale system.</item>
/// <item><b>Held content</b> until then (§4.6 sender table): beats and their knowledge, the Guardian reveal, the finale's
/// resolution and epilogue lines, insights, relay insights (to the log silently at the start), the hack and duel broadcasts
/// (players in the finale system excepted) and the live ending. Player-triggered readers and the star map are not held.</item>
/// <item><b>Track start</b> (§5.3 "Spurstart-Folge"): the held beats are heard and their knowledge credited now; when the
/// world is past chapter 1 a recap card ("The story so far", titles only); then only the CURRENT chapter's opening (#2474:
/// its scene shows the beat that starts the chapter; chapter 1's title card is followed by B1's card; a client without
/// cutscenes hears the newest beat as a line); with the finale open the Guardian reveal; on a won world the ending.
/// On a hosted or arcade world a newcomer gets a personal chapter 1 instead (E29: chapter 1's opening, the crew's chapter as
/// a status line in the Story tab, the recap on demand).</item>
/// <item><b>Chapters</b> follow the beats (<see cref="StoryChapters"/>): a player's chapter is derived — <c>max(1, world)</c>
/// once started — and never stored.</item>
/// <item><b>Migration</b> (P1–P9, lazy at the join, marker <c>story:&lt;id&gt;:migrated:v1</c>): a player from before the pack
/// counts the prologue as seen and the scenes of chapters they lived through as seen; an ending watched in an older build
/// counts as watched; the start rule does the rest.</item>
/// </list>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Why a story track started (the log and the admin report).</summary>
    internal static class StoryTrackReasons
    {
        public const string Finale = "finale system";
        public const string Heard = "heard beats";
        public const string Training = "basic training through or off";
        public const string Veteran = "veteran";
        public const string Request = "player request";
        public const string Test = "test";
    }

    /// <summary>The story-call page of the prologue (the call to adventure, #2470 E13) — new in this build, so an older
    /// client gets it as server-composed text.</summary>
    internal const string StoryCallLine = "story.vega.call";

    /// <summary>The old full-screen prologue pages an older client (no <c>cutscenes1</c>) still gets for VEGA, before B0's
    /// page and the call.</summary>
    private static readonly string[] LegacyProloguePages = { "vega.prologue.1", "vega.prologue.2", "vega.prologue.3" };

    private StoryDefinition? _storyStartFor;
    private StoryCondition? _storyStartAfter;
    private bool _storyStartBroken;

    // ---------------------------------------------------------------------------------------------
    // State readers.
    // ---------------------------------------------------------------------------------------------

    /// <summary>Whether the player's track of the active world story has started (<c>story:&lt;id&gt;:started</c>). False in
    /// the "none" sandbox.</summary>
    internal bool StoryTrackStarted(PlayerSession s)
        => _story is not null && s.State.Milestones.Contains(StoryPackKeys.Started(_story.Id));

    /// <summary>The world's chapter index (the last chapter the arc reached), or -1 without a story / chapters.</summary>
    private int WorldChapterIndex()
        => _story is null ? -1 : StoryChapters.WorldChapterIndex(_story, _storyState.BeatsRevealed, _storyState.GuardianSystemRevealed, _storyState.GuardianDefeated);

    /// <summary>The player's chapter index — <c>max(1, world)</c> once their track started — or -1 before.</summary>
    private int TrackChapterIndex(PlayerSession s)
        => _story is null || !StoryTrackStarted(s) ? -1 : StoryChapters.TrackChapterIndex(_story, WorldChapterIndex());

    /// <summary>The active story's chapter at an index, or null.</summary>
    private StoryChapter? ChapterAt(int index)
        => _story is not null && index >= 0 && index < _story.Chapters.Count ? _story.Chapters[index] : null;

    /// <summary>A hosted or arcade world (E29): every newcomer gets a personal chapter 1 there — the crew of a pool world may be
    /// far along after many guests. Hosted = the instance runs behind the WorldHost's join gate; an arcade guest joins with an
    /// install id.</summary>
    private bool PersonalChapterOneFor(PlayerSession s)
        => !string.IsNullOrEmpty(_config.JoinTokenSecret) || !string.IsNullOrEmpty(s.InstallId);

    /// <summary>The Story tab's "The crew of this world is in chapter N" line: before the player's track starts (the world is
    /// past chapter 0), and on a hosted or arcade world while the crew is past chapter 1.</summary>
    private bool ShowCrewChapterFor(PlayerSession s)
    {
        int world = WorldChapterIndex();
        return _story is not null && world >= 1 && (!StoryTrackStarted(s) || (PersonalChapterOneFor(s) && world >= 2));
    }

    /// <summary>The <c>chapter:&lt;world story&gt;:&lt;id&gt;</c> condition atom: the chapter is reached for this player — their
    /// track has started and their (derived) chapter is at or past it.</summary>
    internal bool WorldChapterReachedFor(PlayerSession who, string packId, string chapterId)
    {
        if (_story is null || !string.Equals(_story.Id, packId, StringComparison.OrdinalIgnoreCase) || !StoryTrackStarted(who))
        {
            return false;
        }

        int index = StoryChapters.IndexOf(_story, chapterId);
        return index >= 0 && index <= TrackChapterIndex(who);
    }

    /// <summary>The id of a pack's intro scene — its <c>introCutscene</c>, else the prologue (the ledger key either way).</summary>
    private static string IntroIdOf(StoryDefinition def)
        => string.IsNullOrEmpty(def.IntroCutscene) ? StoryCutsceneIds.Prologue : def.IntroCutscene;

    // ---------------------------------------------------------------------------------------------
    // Join and a story switch.
    // ---------------------------------------------------------------------------------------------

    /// <summary>The story's join hook (before the player packs, so the cold open comes before the first lesson): the one-time
    /// migration, the prologue for a player who arrived without any key of the story (the join snapshot — before this join
    /// wrote anything), then the meter and the catch-up (held while the track has not started). The track itself starts
    /// after the player packs ran (<see cref="TryStartStoryTrack"/>).</summary>
    private void StoryOnJoin(PlayerSession session)
    {
        if (_story is not null && !session.Spectating)
        {
            MigrateStoryTrack(session);
            string prefix = StoryPackKeys.Prefix(_story.Id);
            if (!session.Join.StoryKeys.Any(k => k.StartsWith(prefix, StringComparison.Ordinal)))
            {
                PlayStoryPrologue(session);
            }
        }

        SendStoryStateOnJoin(session);
    }

    /// <summary>An admin switched the world story (#2470): the new story meets every player online now — the migration, the
    /// prologue for whoever holds no key of it (a "none" world switched to VEGA), the start rule, the fresh story state.</summary>
    private void StoryTrackOnStorySwitch()
    {
        if (_story is null)
        {
            return;
        }

        string prefix = StoryPackKeys.Prefix(_story.Id);
        foreach (var s in StoryListeners().ToList())
        {
            bool newcomer = !s.State.Milestones.Any(k => k.StartsWith(prefix, StringComparison.Ordinal));
            MigrateStoryTrack(s);
            if (newcomer)
            {
                PlayStoryPrologue(s);
            }

            TryStartStoryTrack(s);
            SendStoryState(s);
            SendVegaObjective(s);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // The prologue.
    // ---------------------------------------------------------------------------------------------

    /// <summary>The cold open for one player, once (the intro's ledger key, sent or seen): B0 — VEGA's first words, the
    /// prologue's speaker page — is heard with it, so the catch-up never sends it as a second "VEGA wakes" line. A client
    /// that plays cutscenes gets the pack's <c>introCutscene</c>; an older client (or the built-in fallback pack, which has no
    /// cutscenes) gets the full-screen pages (Kind 4). Returns whether it played.</summary>
    private bool PlayStoryPrologue(PlayerSession session)
    {
        var def = _story;
        if (def is null || session.Spectating)
        {
            return false;
        }

        var m = session.State.Milestones;
        string intro = IntroIdOf(def);
        if (m.Contains(StoryPackKeys.CutsceneSeen(def.Id, intro)) || m.Contains(StoryPackKeys.CutsceneSent(def.Id, intro)))
        {
            return false; // once per player — a tutorial restart never brings it back (E17); the Story tab replays it
        }

        if (def.Beats.Count > 0 && _storyState.BeatsRevealed > 0)
        {
            MarkBeatHeard(session, def.Beats[0]);
        }

        bool staged = session.SupportsCutscenes && def.FindCutscene(intro) is not null && PlayBoundCutscene(session, def, intro, def.Id);
        if (!staged)
        {
            m.Add(StoryPackKeys.CutsceneSeen(def.Id, intro));
            SendLegacyPrologue(session, def);
        }

        _repo.SavePlayer(session.State);
        return true;
    }

    /// <summary>The prologue as the full-screen pages an older client renders (Kind 4, #738): the three narration pages,
    /// B0 as VEGA's page and the call to adventure — the call as text composed in the player's language (an older client's
    /// tables do not have the key). Only VEGA has these pages; another world story without its scene starts silently.</summary>
    private void SendLegacyPrologue(PlayerSession session, StoryDefinition def)
    {
        if (!string.Equals(def.Id, StoryRegistry.DefaultStoryId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        foreach (string page in LegacyProloguePages)
        {
            SendVegaLine(session, page, 4);
        }

        if (def.Beats.Count > 0 && !string.IsNullOrEmpty(def.Beats[0].TextKey))
        {
            SendVegaLine(session, def.Beats[0].TextKey, 4);
        }

        SendVegaWithObjective(session, new ShipAiLine { Kind = 4, Text = ComposeLegacyText(session.Locale, StoryCallLine, null) });
    }

    // ---------------------------------------------------------------------------------------------
    // The migration (P1–P9).
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The one-time migration of a player's world-story state from before the story was a pack (#2470, synthesis §4.10;
    /// marker <c>story:&lt;id&gt;:migrated:v1</c>, lazy and idempotent). A player without any key of the story is a newcomer to
    /// it — nothing to migrate, the prologue rule takes them. Everyone else held at least B0 from the old catch-up:
    /// <list type="bullet">
    /// <item>P1–P3: the old prologue pages (or the old veteran path that skipped them) count as the prologue seen — it is
    /// offered in the Story tab, never forced; mid-tutorial players start the track after the basic training, the others
    /// (all stages, a heard beat beyond B0, the veteran path) start it by the start rule at this join;</item>
    /// <item>P2–P4: the scenes of the chapters they lived through — the openings before the world's chapter and the scenes of
    /// heard beats — count as seen (replayable, never auto-played); the current chapter's opening stays owed and plays once
    /// at the track start;</item>
    /// <item>P5: an ending watched in an older build (<c>story:&lt;id&gt;:resolved</c> meant "watched" there) counts as
    /// watched;</item>
    /// <item>P6: a "none" world migrates nothing and writes no marker — a story activated later meets them as newcomers;</item>
    /// <item>P7: VEGA's old memories (<c>vega:mem:N</c>) stay as they are; the "parts handed over" flag never counts;</item>
    /// <item>P8: several <c>story_state</c> rows — chosen in <see cref="SelectActiveStoryRow"/>;</item>
    /// <item>P9: players the old multiplayer bug auto-skipped get the tutorial pack's one refresher offer
    /// (<see cref="MigrateOfficialTutorial"/>) — nothing here, so it is offered once, not twice.</item>
    /// </list>
    /// </summary>
    private void MigrateStoryTrack(PlayerSession session)
    {
        var def = _story;
        if (def is null)
        {
            return; // P6
        }

        var m = session.State.Milestones;
        string marker = StoryPackKeys.Migrated(def.Id);
        if (m.Contains(marker))
        {
            return;
        }

        string prefix = StoryPackKeys.Prefix(def.Id);
        bool oldPlayer = m.Any(k => k.StartsWith(prefix, StringComparison.Ordinal));
        m.Add(marker);
        if (oldPlayer)
        {
            string intro = IntroIdOf(def);
            if (!m.Contains(StoryPackKeys.CutsceneSent(def.Id, intro)))
            {
                m.Add(StoryPackKeys.CutsceneSeen(def.Id, intro));
            }

            int world = WorldChapterIndex();
            for (int i = 0; i < def.Chapters.Count && i < world; i++)
            {
                MarkSceneSeen(session, def, def.Chapters[i].OpenCutscene, def.Chapters[i].Id);
            }

            foreach (var beat in def.Beats)
            {
                if (!string.IsNullOrEmpty(beat.Cutscene) && m.Contains(BeatMilestoneKey(beat)))
                {
                    MarkSceneSeen(session, def, beat.Cutscene, def.Id);
                }
            }

            if (_storyState.GuardianDefeated && m.Contains(ResolutionSeenKey))
            {
                m.Add(ResolutionWatchedKey);
            }
        }

        _repo.SavePlayer(session.State);
    }

    /// <summary>Stamps a pack binding seen without playing it (a template as its instance for <paramref name="owner"/>), unless
    /// it is still owed (<c>:sent</c> — the join catch-up offers that one).</summary>
    private static void MarkSceneSeen(PlayerSession session, StoryDefinition def, string? cutsceneId, string owner)
    {
        if (string.IsNullOrEmpty(cutsceneId) || def.FindCutscene(cutsceneId) is not { } scene)
        {
            return;
        }

        string id = scene.Template ? StoryCutsceneIds.Instance(scene.Id, owner) : scene.Id;
        if (!session.State.Milestones.Contains(StoryPackKeys.CutsceneSent(def.Id, id)))
        {
            session.State.Milestones.Add(StoryPackKeys.CutsceneSeen(def.Id, id));
        }
    }

    // ---------------------------------------------------------------------------------------------
    // The start rule and the track start.
    // ---------------------------------------------------------------------------------------------

    /// <summary>Starts the player's story track when the start rule holds (cheap when started: one lookup). Called at the join
    /// (after the player packs), at 1 Hz, after every player-pack advance (a graduation, a skip, the tutorial switched off),
    /// on arriving in the finale system, when observer mode ends and on a story switch. Returns whether it started.</summary>
    private bool TryStartStoryTrack(PlayerSession s)
    {
        if (_story is null || !s.Joined || s.Spectating || StoryTrackStarted(s))
        {
            return false;
        }

        MigrateStoryTrack(s); // the old state is read before the track writes anything
        string? reason = StoryTrackStartReason(s);
        if (reason is null)
        {
            return false;
        }

        StartStoryTrack(s, reason);
        return true;
    }

    /// <summary>Why the player's track would start now, or null while it waits (synthesis §4.6): in the finale system (never
    /// leave a player in the finale without context); a beat beyond B0 already heard (a player from before the pack); the
    /// pack's <c>start.after</c> holds or no tutorial applies to them; a veteran by the join snapshot who is not in the middle
    /// of the basic training.</summary>
    internal string? StoryTrackStartReason(PlayerSession s)
    {
        if (_story is null)
        {
            return null;
        }

        if (IsGuardianSystemLocation(s.CurrentLocationId))
        {
            return StoryTrackReasons.Finale;
        }

        for (int i = 1; i < _story.Beats.Count; i++)
        {
            if (s.State.Milestones.Contains(StoryPackKeys.Beat(_story.Id, _story.Beats[i].Index)))
            {
                return StoryTrackReasons.Heard; // a dozen lookups — this runs at 1 Hz for every player still waiting
            }
        }

        if (StoryStartConditionHolds(s))
        {
            return StoryTrackReasons.Training;
        }

        return s.Join.Veteran && !TutorialPackRunning(s) ? StoryTrackReasons.Veteran : null;
    }

    /// <summary>The pack's <c>start.after</c> (VEGA: <c>chapter:tutorial:b6</c> — the basic training's last part done or
    /// skipped) holds — or the basic training is through or off for the player whatever tutorial pack the world has: no
    /// tutorial pack still runs its mandatory part for them (none installed, the world's tutorial off, an opt-in world where
    /// they never opted in, opted out, all its mandatory chapters done or skipped — or, for an older client without
    /// <c>packs1</c>, its nine legacy lessons done, #2487). A pack without a start rule starts at once.</summary>
    private bool StoryStartConditionHolds(PlayerSession s)
    {
        CompileStoryStart();
        if (!_storyStartBroken && (_storyStartAfter is null || EvaluateStoryCondition(_storyStartAfter, s)))
        {
            return true;
        }

        foreach (var pack in PlayerPacks)
        {
            if (pack.IsTutorial && StoryPackEnabledFor(s, pack.Id) && !MandatoryChaptersThrough(s, pack))
            {
                return false; // the basic training still runs (or has not started yet) for this player
            }
        }

        return true;
    }

    /// <summary>Parses the active story's <c>start.after</c> once per story (a broken rule is logged, and the track then starts
    /// when no tutorial applies).</summary>
    private void CompileStoryStart()
    {
        if (ReferenceEquals(_storyStartFor, _story))
        {
            return;
        }

        _storyStartFor = _story;
        _storyStartAfter = null;
        _storyStartBroken = false;
        if (_story is null)
        {
            return;
        }

        try
        {
            _storyStartAfter = StoryConditionParser.ParseList(_story.Start?.After, WorldPredicateNames);
        }
        catch (FormatException ex)
        {
            _storyStartBroken = true;
            _log.Warn($"Story '{_story.Id}': start.after does not parse ({ex.Message}) — the track starts once no tutorial applies.");
        }
    }

    /// <summary>"Start the story now" (<see cref="StoryPackActions.StartPack"/> with the world story's id): starts the
    /// sender's track early when the pack allows it (<c>start.earlyOptIn</c>) or the rule holds anyway.</summary>
    private bool StartStoryTrackOnRequest(PlayerSession s)
    {
        if (_story is null || StoryTrackStarted(s) || s.Spectating)
        {
            return false;
        }

        MigrateStoryTrack(s);
        if (!(_story.Start?.EarlyOptIn ?? false) && StoryTrackStartReason(s) is null)
        {
            return false;
        }

        StartStoryTrack(s, StoryTrackReasons.Request);
        return true;
    }

    /// <summary>
    /// The track start (synthesis §4.6 "Beim Spurstart", §5.3 "Spurstart-Folge"):
    /// <list type="number">
    /// <item>every revealed beat the player has not heard is heard now and its knowledge credited now; the story state (the
    /// log, the chapter, the relay insights) follows silently;</item>
    /// <item>"The story so far" (<c>__recap</c>, titles only) when the world is past chapter 1 and the player had not lived
    /// through those chapters (a player from before the pack gets only their current chapter's card);</item>
    /// <item>only the current chapter's opening — chapter 1's for a newcomer on a hosted or arcade world (E29, personal chapter
    /// 1). Its scene shows the beat that starts the chapter (#2474); chapter 1's title card is followed by B1's card when B1
    /// was crossed (<see cref="PlayOpeningBeatAfterTitle"/>). Without a scene for the client (an older build, the built-in
    /// fallback pack) the newest beat speaks as a line instead;</item>
    /// <item>with the finale open the Guardian reveal — the line with the jump generator, held with chapter 7's set piece
    /// (<c>ch7.open</c>, which shows it) when that is queued;</item>
    /// <item>on a won world the ending (deferred from the join), not for a hosted or arcade newcomer's personal chapter 1;</item>
    /// <item>the chip: the story's objective from now on.</item>
    /// </list>
    /// </summary>
    private void StartStoryTrack(PlayerSession s, string reason)
    {
        var def = _story;
        if (def is null || !s.State.Milestones.Add(StoryPackKeys.Started(def.Id)))
        {
            return;
        }

        var heard = new List<StoryBeat>();
        for (int i = 0; i < _storyState.BeatsRevealed && i < def.Beats.Count; i++)
        {
            if (MarkBeatHeard(s, def.Beats[i], sendInventory: false))
            {
                heard.Add(def.Beats[i]);
            }
        }

        if (heard.Any(b => b.KnowledgeReward > 0))
        {
            SendInventory(s); // the held knowledge, credited now
        }

        _repo.SavePlayer(s.State);
        SendStoryState(s);

        int current = TrackChapterIndex(s);
        bool caughtUp = heard.Any(b => b.Index >= 1);
        bool personal = caughtUp && PersonalChapterOneFor(s);
        if (!personal && current >= 2 && heard.Any(b => b.Index >= 1 && StoryChapters.ChapterIndexOfBeat(def, b.Index) < current))
        {
            PlayRecap(s, def.Id, def.Chapters.Skip(1).Take(current - 1).Select(c => c.TitleKey).Where(k => !string.IsNullOrEmpty(k)));
        }

        var chapter = ChapterAt(personal ? StoryChapters.TrackChapterIndex(def, 0) : current);
        if (chapter is null || !PlayChapterOpening(s, chapter))
        {
            // No scene for the client (an older build: stamped seen; the built-in fallback pack: none defined) — the newest
            // beat (personal: chapter 1's) speaks as a line, as before the chapter scenes (#2470).
            var line = personal
                ? heard.FirstOrDefault(b => chapter is not null && b.Index == chapter.FromBeat)
                : heard.LastOrDefault();
            if (line is not null && line.Index > 0 && !string.IsNullOrEmpty(line.TextKey))
            {
                SendVegaLine(s, line.TextKey, 2);
            }
        }
        else
        {
            PlayOpeningBeatAfterTitle(s, def, chapter);
        }

        if (FinaleOpen)
        {
            SpeakGuardianRevealedTo(s);
        }

        if (!personal)
        {
            // The finale achievement too for whoever had heard the arc out before this join (E23: a player from before the
            // pack who missed the win) — a newcomer did not.
            DeliverResolution(s, achievement: FinalBeatKey is { } finalBeat && s.Join.HadStoryKey(finalBeat));
        }

        SendVegaObjective(s);
        _log.Info($"Story track of '{s.State.Name}' started ({reason}) in chapter '{chapter?.Id ?? "-"}' (world '{ChapterAt(WorldChapterIndex())?.Id ?? "-"}').");
    }

    /// <summary>A chapter's opening scene for one player (once, by the ledger) — never the intro (the prologue has its own
    /// rule). Returns whether it was sent or queued for the client.</summary>
    private bool PlayChapterOpening(PlayerSession s, StoryChapter chapter)
    {
        if (_story is null || string.IsNullOrEmpty(chapter.OpenCutscene)
            || string.Equals(chapter.OpenCutscene, _story.IntroCutscene, StringComparison.Ordinal))
        {
            return false;
        }

        return PlayBoundCutscene(s, _story, chapter.OpenCutscene, chapter.Id);
    }

    /// <summary>
    /// #2474, at a story-track start: a chapter opening shows the beat that starts its chapter (<c>replaceLineKeys</c>) —
    /// except chapter 1's card, which carries the title only, because a track may start before the world crossed B1. When
    /// the opening beat was crossed and the opening does not show it, the beat follows: its own beat card (VEGA's <c>b01</c>,
    /// chained to <c>ch1.open</c>, so it goes at once), else its line.
    /// </summary>
    private void PlayOpeningBeatAfterTitle(PlayerSession s, StoryDefinition def, StoryChapter chapter)
    {
        if (chapter.FromBeat < 1 || chapter.FromBeat >= _storyState.BeatsRevealed)
        {
            return; // the chapter's beat lies ahead — it comes live with its card
        }

        var beat = def.Beats.FirstOrDefault(b => b.Index == chapter.FromBeat);
        if (beat is null || string.IsNullOrEmpty(beat.TextKey)
            || (def.FindCutscene(chapter.OpenCutscene) is { } opening && opening.ReplaceLineKeys.Contains(beat.TextKey)))
        {
            return; // the opening shows it
        }

        // As at a live reveal: the card first, then the line — held with the queued card and claimed by a client that plays
        // it; a client without the card reads it in the panel.
        PlayBoundCutscene(s, def, beat.Cutscene, def.Id);
        SendVegaLine(s, beat.TextKey, 2);
    }

    /// <summary>The story-track part of <c>/story</c>: started or waiting, the player's chapter and the world's.</summary>
    private string StoryTrackStatus(PlayerSession who)
        => _story is null
            ? string.Empty
            : $", track of {who.State.Name}={(StoryTrackStarted(who) ? "started" : "waiting")}, chapter={ChapterAt(TrackChapterIndex(who))?.Id ?? "-"}, worldChapter={ChapterAt(WorldChapterIndex())?.Id ?? "-"}{StoryMissionStatus(who)}";

    // ---------------------------------------------------------------------------------------------
    // Test seams.
    // ---------------------------------------------------------------------------------------------

    /// <summary>Test seam: starts the player's story track now (the full start sequence), as if the start rule held.</summary>
    internal void StartStoryTrackForTest(PlayerSession s) => StartStoryTrack(s, StoryTrackReasons.Test);

    /// <summary>Test seam: <see cref="TryStartStoryTrack"/>.</summary>
    internal bool TryStartStoryTrackForTest(PlayerSession s) => TryStartStoryTrack(s);

    /// <summary>Test seam: <see cref="StoryTrackStartReason"/>.</summary>
    internal string? StoryTrackStartReasonForTest(PlayerSession s) => StoryTrackStartReason(s);

    /// <summary>Test seam: the story's join hook (migration, prologue, meter, catch-up).</summary>
    internal void StoryOnJoinForTest(PlayerSession s) => StoryOnJoin(s);

    /// <summary>Test seam: <see cref="MigrateStoryTrack"/>.</summary>
    internal void MigrateStoryTrackForTest(PlayerSession s) => MigrateStoryTrack(s);

    /// <summary>Test seam: the world's chapter id ("" without one).</summary>
    internal string WorldChapterIdForTest => ChapterAt(WorldChapterIndex())?.Id ?? string.Empty;

    /// <summary>Test seam: the player's chapter id ("" before the track starts).</summary>
    internal string TrackChapterIdForTest(PlayerSession s) => ChapterAt(TrackChapterIndex(s))?.Id ?? string.Empty;

    /// <summary>Test seam: wins the finale duel (the resolution lines, the achievement, the cinematic for started players).</summary>
    internal void WinDuelForTest() => WinDuel();

    /// <summary>Test seam: the player ids the finale's live broadcasts reach right now.</summary>
    internal IReadOnlyList<string> FinaleAudienceForTest()
        => _sessions.Values.Where(s => s.Joined && InFinaleAudience(s)).Select(s => s.State.PlayerId).ToList();

    /// <summary>Test seam: speaks a relay-network insight stage once (mirrors a relay completing).</summary>
    internal void SpeakRelayInsightForTest(string stage) => SpeakRelayInsightOnce(stage);
}
