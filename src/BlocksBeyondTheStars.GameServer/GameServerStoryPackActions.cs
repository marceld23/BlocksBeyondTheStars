// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Story;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The player-pack runtime's actions (#2466): the player's own controls (<see cref="StoryPackIntent"/> — track, opt out,
/// start/skip/replay, offers), the tutorial button's skip/restart, the gates the advisor systems will read, the admin
/// cases (<c>/tutorial</c>, <c>/story</c>) and the test seams. The runtime itself is <c>GameServerStoryPacks.cs</c>.
/// </summary>
public sealed partial class GameServer
{
    // ---------------------------------------------------------------------------------------------
    // Player actions (StoryPackIntent, the tutorial button, admin).
    // ---------------------------------------------------------------------------------------------

    /// <summary>The player's own control of a story (#2466, <see cref="StoryPackIntent"/>; the Journal's full set since #2471).
    /// Unknown packs, words, chapters and steps are ignored, and every action is validated against the player's own state
    /// (a done chapter cannot be skipped, a chapter that never ran cannot be replayed …); each changes only the sender's
    /// ledger. Afterwards the Journal's messages go out at once where they changed (a click wants its answer now).</summary>
    private void HandleStoryPackIntent(PlayerSession s, StoryPackIntent intent)
    {
        string action = intent.Action ?? string.Empty;
        if (action == StoryPackActions.RequestStates)
        {
            PushJournal(s, force: true); // #2471: the Journal opened — the full picture now, live progress included
            return;
        }

        if (action == StoryPackActions.ReplayCutscene)
        {
            ReplayCutsceneOnRequest(s, intent.PackId, intent.CutsceneId); // #2467: any pack, the world story included
            return;
        }

        ApplyStoryPackIntent(s, intent, action);
        PushJournal(s, force: false);
    }

    private void ApplyStoryPackIntent(PlayerSession s, StoryPackIntent intent, string action)
    {
        if (action == StoryPackActions.StartPack && _story is not null
            && string.Equals(_story.Id, intent.PackId, StringComparison.OrdinalIgnoreCase))
        {
            StartStoryTrackOnRequest(s); // #2470: "Start the story now" — the world story's per-player track
            return;
        }

        if (action == StoryPackActions.Untrack)
        {
            s.State.TrackedStoryId = string.Empty;
            SendVegaObjective(s);
            return;
        }

        if (action == StoryPackActions.Track)
        {
            bool worldStory = StoryActive && _story is not null && string.Equals(_story.Id, intent.PackId, StringComparison.OrdinalIgnoreCase);
            if (worldStory || TryGetPlayerPack(intent.PackId, out _))
            {
                s.State.TrackedStoryId = worldStory ? _story!.Id : intent.PackId;
                SendVegaObjective(s);
            }

            return;
        }

        if (_story is not null && string.Equals(_story.Id, intent.PackId, StringComparison.OrdinalIgnoreCase))
        {
            ApplyStoryMissionIntent(s, intent, action); // #2476: the world story's own actions are its story missions' skips
            return;
        }

        if (!TryGetPlayerPack(intent.PackId, out var pack))
        {
            return;
        }

        string chapterId = intent.ChapterId ?? string.Empty;
        switch (action)
        {
            case StoryPackActions.OptOut:
                OptOutOfPack(s, pack);
                break;
            case StoryPackActions.OptIn:
                OptIntoPack(s, pack);
                break;
            case StoryPackActions.StartPack:
                StartPackOnRequest(s, pack);
                break;
            case StoryPackActions.SkipPack:
                SkipPack(s, pack);
                break;
            case StoryPackActions.RestartPack:
                RestartPackOnRequest(s, pack);
                break;
            case StoryPackActions.StartChapter:
                if (pack.ChaptersById.TryGetValue(chapterId, out var start))
                {
                    StartChapter(s, start);
                }

                break;
            case StoryPackActions.DeclineChapter:
                if (pack.ChaptersById.TryGetValue(chapterId, out var decline))
                {
                    DeclineChapter(s, decline);
                }

                break;
            case StoryPackActions.SkipChapter:
                if (pack.ChaptersById.TryGetValue(chapterId, out var skip))
                {
                    SkipChapter(s, skip);
                }

                break;
            case StoryPackActions.RestartChapter:
                if (pack.ChaptersById.TryGetValue(chapterId, out var restart))
                {
                    RestartChapter(s, restart);
                }

                break;
            case StoryPackActions.ReplayChapter:
                if (pack.ChaptersById.TryGetValue(chapterId, out var replay) && ChapterDoneOrSkipped(s, replay) && !ChapterReplaying(s, replay))
                {
                    RestartChapter(s, replay);
                }

                break;
            case StoryPackActions.SkipStep:
                if (pack.StepsById.TryGetValue(intent.StepId ?? string.Empty, out var step))
                {
                    SkipStep(s, step);
                }

                break;
        }
    }

    private void OptOutOfPack(PlayerSession s, CompiledPack pack)
    {
        if (MilestonesOf(s).Add(StoryPackKeys.OptOut(pack.Id)))
        {
            _repo.SavePlayer(s.State);
            SendVegaObjective(s);
        }
    }

    /// <summary>Switches a pack back on. A pack that would not start by itself — an opt-in world's tutorial, a pack off by
    /// default — starts with it (#2471: the Settings' "Tutorial: on" is one switch for the player, whatever the world rule).</summary>
    private void OptIntoPack(PlayerSession s, CompiledPack pack)
    {
        bool changed = MilestonesOf(s).Remove(StoryPackKeys.OptOut(pack.Id));
        if (!PackStarted(s, pack) && !StoryPackEnabledFor(s, pack.Id) && CanStartPackOnRequest(s, pack))
        {
            StartPackOnRequest(s, pack);
            return;
        }

        if (changed)
        {
            _repo.SavePlayer(s.State);
            AdvancePlayer(s);
            SendVegaObjective(s);
        }
    }

    /// <summary>Whether the player may start a pack now: never while the world has the tutorial off, and a pack that starts by
    /// itself later only when it allows an early start.</summary>
    private bool CanStartPackOnRequest(PlayerSession s, CompiledPack pack)
    {
        if (pack.IsTutorial && Rules.TutorialPolicy == TutorialPolicy.Off)
        {
            return false;
        }

        return pack.IsTutorial || !pack.Def.DefaultEnabled || pack.StartAfter is null || pack.Def.Start.EarlyOptIn
               || EvaluateStoryCondition(pack.StartAfter, s);
    }

    /// <summary>Starts a pack on the player's request — an opt-in tutorial, a pack off by default, or an early start the
    /// pack allows. Refused while the world has the tutorial off.</summary>
    private bool StartPackOnRequest(PlayerSession s, CompiledPack pack)
    {
        if (!CanStartPackOnRequest(s, pack))
        {
            return false; // the world has the tutorial off — or the pack starts by itself later and allows no early start
        }

        MilestonesOf(s).Remove(StoryPackKeys.OptOut(pack.Id));
        StartPack(s, pack);
        AdvancePlayer(s);
        SendVegaObjective(s);
        return true;
    }

    /// <summary>The Journal's "Restart" (#2471): the whole pack again from its first mandatory chapter (<see cref="RestartPack"/>,
    /// never the prologue). Refused where the player could not start it (the world's tutorial off).</summary>
    private bool RestartPackOnRequest(PlayerSession s, CompiledPack pack)
    {
        if ((pack.IsTutorial && Rules.TutorialPolicy == TutorialPolicy.Off) || (!PackStarted(s, pack) && !CanStartPackOnRequest(s, pack)))
        {
            return false;
        }

        RestartPack(s, pack);
        return true;
    }

    /// <summary>"Later" to an offered (or available) opt-in chapter: it stays listed and startable, VEGA does not offer it
    /// again. Refused for a mandatory, running or finished chapter.</summary>
    private bool DeclineChapter(PlayerSession s, CompiledChapter ch)
    {
        if (!ch.Optional || ChapterDoneOrSkipped(s, ch) || ChapterRuns(s, ch)
            || !MilestonesOf(s).Add(StoryPackKeys.ChapterDeclined(ch.Pack.Id, ch.Id)))
        {
            return false;
        }

        _repo.SavePlayer(s.State);
        return true;
    }

    /// <summary>"I know the game": every mandatory chapter is skipped (deep-dive offers stay on). Grants nothing.</summary>
    private void SkipPack(PlayerSession s, CompiledPack pack)
    {
        StartPack(s, pack, playIntro: false);
        foreach (var ch in pack.Chapters)
        {
            if (!ch.Optional && !ChapterFinished(s, ch))
            {
                SkipChapterSteps(s, ch);
                CompleteChapter(s, ch, skipped: true);
            }
        }

        if (pack.IsTutorial)
        {
            WriteLegacyGraduation(s);
        }

        CheckPackDone(s, pack);
        _repo.SavePlayer(s.State);
        AdvancePlayer(s);
        SendVegaObjective(s);
    }

    private void SkipChapterSteps(PlayerSession s, CompiledChapter ch)
    {
        foreach (var step in ch.Steps)
        {
            if (!StepComplete(s, step))
            {
                MarkStepSkipped(s, step);
            }
        }
    }

    /// <summary>Skips the rest of a running chapter (a replay just ends). Grants nothing; <c>chapter:</c> conditions hold
    /// after. Since #2471 only a chapter that runs now (the Journal offers the skip there): one that is through has nothing
    /// left to skip, one not reached yet is skipped when it comes up, and nothing runs while the pack is switched off.</summary>
    private bool SkipChapter(PlayerSession s, CompiledChapter ch)
    {
        if (!(ch.Def.Skippable ?? true) || !PackRunsFor(s, ch.Pack) || !ChapterRuns(s, ch))
        {
            return false;
        }

        SkipChapterSteps(s, ch);
        CompleteChapter(s, ch, skipped: SkipEndsAsSkipped(s, ch), silent: true); // a skip never graduates
        AdvancePlayer(s);
        SendVegaObjective(s);
        return true;
    }

    /// <summary>Whether skipping this chapter stamps it skipped: always, except in a replay of a chapter that was through
    /// before (the replay just ends, its done key stays) — a restarted running chapter (#2471) was not, so it is skipped.</summary>
    private static bool SkipEndsAsSkipped(PlayerSession s, CompiledChapter ch)
        => !ChapterReplaying(s, ch) || !ChapterDoneOrSkipped(s, ch);

    /// <summary>"I know this": skips one active step (or the next one of a running chapter). Grants nothing.</summary>
    private bool SkipStep(PlayerSession s, CompiledStep step)
    {
        if (!(step.Def.Skippable ?? true) || StepComplete(s, step) || !PackRunsFor(s, step.Pack) || !ChapterRuns(s, step.Chapter))
        {
            return false;
        }

        MarkStepSkipped(s, step);
        AdvancePlayer(s);
        SendVegaObjective(s);
        return true;
    }

    /// <summary>Starts an optional chapter (or a non-optional one that does not start by itself); a chapter that is done
    /// is replayed instead.</summary>
    private bool StartChapter(PlayerSession s, CompiledChapter ch)
    {
        if (ChapterDoneOrSkipped(s, ch))
        {
            return RestartChapter(s, ch);
        }

        if (!PackRunsFor(s, ch.Pack) || (!s.SupportsPacks && ch.Pack.IsTutorial && ch.Optional)
            || !EvaluateStoryCondition(ch.Requires, s))
        {
            return false;
        }

        var m = MilestonesOf(s);
        m.Remove(StoryPackKeys.ChapterDeclined(ch.Pack.Id, ch.Id));
        if (!m.Add(StoryPackKeys.ChapterActive(ch.Pack.Id, ch.Id)))
        {
            return false; // already running
        }

        _repo.SavePlayer(s.State);
        AdvancePlayer(s);
        SendVegaObjective(s);
        return true;
    }

    /// <summary>Replays a done (or skipped) chapter with the evidence switched off: its steps run again, recorded in replay
    /// keys; its done key and step keys stay, so conditions and gates keep holding. No veteran check, no prologue. Since
    /// #2471 a RUNNING chapter (or one already being replayed) starts over the same way from its first step — what the
    /// player had done stays done, a step done for the first time in the replay is stamped done, and the chapter completes
    /// for real at the end of it (<see cref="CompleteChapter"/>).</summary>
    private bool RestartChapter(PlayerSession s, CompiledChapter ch)
    {
        if (!PackRunsFor(s, ch.Pack) || !(ChapterDoneOrSkipped(s, ch) || ChapterRuns(s, ch)))
        {
            return false; // nothing to restart: a chapter that has not been reached yet, or the pack is off
        }

        BeginReplay(s, ch);
        _repo.SavePlayer(s.State);
        AdvancePlayer(s);
        SendVegaObjective(s);
        return true;
    }

    private void BeginReplay(PlayerSession s, CompiledChapter ch)
    {
        var m = MilestonesOf(s);
        m.Add(StoryPackKeys.ChapterReplay(ch.Pack.Id, ch.Id));
        foreach (var step in ch.Steps)
        {
            m.Remove(StoryPackKeys.StepReplay(ch.Pack.Id, step.Id));
            s.State.StepProgress.Remove(step.ProgressKey);
            s.StepEscalation.Remove(step.ProgressKey);
        }

        _stepListenersDirty = true;
        MarkJournalDirty(s);
    }

    /// <summary>The tutorial's "restart": opt back in and replay every mandatory chapter from the first (the replays run in
    /// order). Never touches the world story — no prologue (maintainer decision E17).</summary>
    private void RestartPack(PlayerSession s, CompiledPack pack)
    {
        MilestonesOf(s).Remove(StoryPackKeys.OptOut(pack.Id));
        StartPack(s, pack);
        foreach (var ch in pack.Chapters)
        {
            if (ch.Optional)
            {
                continue;
            }

            if (ChapterDoneOrSkipped(s, ch))
            {
                BeginReplay(s, ch);
            }
            else
            {
                foreach (var step in ch.Steps)
                {
                    if (s.State.StepProgress.ContainsKey(step.ProgressKey))
                    {
                        s.State.StepProgress[step.ProgressKey] = 0; // a running chapter starts its counters over
                    }

                    s.StepEscalation.Remove(step.ProgressKey);
                }
            }
        }

        _repo.SavePlayer(s.State);
        AdvancePlayer(s);
        SendVegaObjective(s);
    }

    /// <summary>The tutorial packs' answer to the old Settings button (<see cref="SkipOnboardingIntent"/> — still sent by an
    /// older client; a current one has "Tutorial: on/off" and the Journal, #2471): skip = skip the pack, restart = replay its
    /// mandatory chapters (refused while the world has the tutorial off).</summary>
    private void SkipOrRestartTutorialPacks(PlayerSession s, bool restart)
    {
        foreach (var pack in PlayerPacks)
        {
            if (!pack.IsTutorial || (!restart && !StoryPackEnabledFor(s, pack.Id)))
            {
                continue;
            }

            if (restart)
            {
                if (Rules.TutorialPolicy != TutorialPolicy.Off)
                {
                    RestartPack(s, pack);
                }
            }
            else
            {
                SkipPack(s, pack);
            }
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Gates (read by the opportunity tips, VEGA's banter and the station_near tip, #2469).
    // ---------------------------------------------------------------------------------------------

    /// <summary>What a tutorial pack says about a gate (<see cref="StoryGateNames"/>) for this player: null = no tutorial
    /// pack has a say (no pack installed, or the gate unset — the caller keeps today's rule); true when the tutorial is off
    /// for the world or the player (synthesis §5.3: "Tutorial aus" opens every gate); else the gate's condition. Readers:
    /// <c>OpportunityTipsOpen</c> (the opportunity and progression tips), <c>TickVegaBanterFor</c> and the
    /// <c>station_near</c> tip.</summary>
    internal bool? TutorialGate(PlayerSession s, string gate)
    {
        bool anyTutorial = false;
        foreach (var pack in PlayerPacks)
        {
            if (!pack.IsTutorial)
            {
                continue;
            }

            anyTutorial = true;
            if (!StoryPackEnabledFor(s, pack.Id))
            {
                continue;
            }

            return pack.Gates.TryGetValue(gate, out var condition) ? EvaluateStoryCondition(condition, s) : null;
        }

        return anyTutorial ? true : null;
    }

    // ---------------------------------------------------------------------------------------------
    // Admin (/tutorial, /story).
    // ---------------------------------------------------------------------------------------------

    /// <summary>The server side of <c>/tutorial</c> (#2466): <c>skip|restart|chapter &lt;id&gt;</c> for the caller or the named
    /// player, <c>policy auto|optin|off</c> for the world. World management, not a cheat — the role is the gate. The verb
    /// line rides in <see cref="AdminCommandIntent.StringArg"/>, the player in <see cref="AdminCommandIntent.TargetPlayer"/>.</summary>
    private void HandleTutorialAdmin(PlayerSession session, AdminCommandIntent cmd)
    {
        string[] words = (cmd.StringArg ?? string.Empty).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        string verb = words.Length > 0 ? words[0].ToLowerInvariant() : string.Empty;
        if (verb == "policy")
        {
            if (words.Length < 2 || !ServerConfig.TryParseTutorialPolicy(words[1], out var policy))
            {
                Reject(session, "admin", "@srv.admin.tutorial_usage");
                return;
            }

            SetTutorialPolicy(policy);
            Send(session, new ServerMessage { Text = "@srv.admin.tutorial_policy:" + policy });
            CheatLog(session.State, $"set the tutorial policy to {policy}");
            return;
        }

        if (verb is not ("skip" or "restart" or "chapter") || (verb == "chapter" && words.Length < 2))
        {
            Reject(session, "admin", "@srv.admin.tutorial_usage");
            return;
        }

        var target = string.IsNullOrWhiteSpace(cmd.TargetPlayer) ? session : FindSessionByName(cmd.TargetPlayer);
        if (target is null)
        {
            Reject(session, "admin", "@srv.admin.tutorial_no_player:" + cmd.TargetPlayer);
            return;
        }

        var pack = PlayerPacks.FirstOrDefault(p => p.IsTutorial);
        if (pack is null)
        {
            Reject(session, "admin", "@srv.admin.tutorial_none"); // #2469: the tutorial IS the pack — no pack, no tutorial
            return;
        }

        switch (verb)
        {
            case "skip":
                SkipPack(target, pack);
                Send(session, new ServerMessage { Text = "@srv.admin.tutorial_skipped:" + target.State.Name });
                break;
            case "restart":
                RestartPack(target, pack);
                Send(session, new ServerMessage { Text = "@srv.admin.tutorial_restarted:" + target.State.Name });
                break;
            default:
                if (!pack.ChaptersById.TryGetValue(words[1], out var chapter))
                {
                    Reject(session, "admin", "@srv.admin.tutorial_no_chapter:" + words[1]);
                    return;
                }

                MilestonesOf(target).Remove(StoryPackKeys.OptOut(pack.Id));
                StartPack(target, pack);
                if (!JumpToChapter(target, chapter))
                {
                    Reject(session, "admin", "@srv.admin.tutorial_chapter_refused:" + chapter.Id);
                    return;
                }

                Send(session, new ServerMessage { Text = "@srv.admin.tutorial_chapter:" + target.State.Name + " · " + chapter.Id });
                break;
        }

        CheatLog(session.State, $"{verb} the tutorial of {target.State.Name}");
    }

    /// <summary>QA (<c>/tutorial chapter &lt;id&gt;</c>): makes a chapter run for the player now — a done one is replayed, an
    /// optional one started, a later mandatory one reached by skipping the mandatory chapters before it. True when the
    /// chapter runs afterwards.</summary>
    private bool JumpToChapter(PlayerSession s, CompiledChapter ch)
    {
        if (ChapterRuns(s, ch))
        {
            return true;
        }

        if (ChapterDoneOrSkipped(s, ch))
        {
            return RestartChapter(s, ch);
        }

        if (ch.Optional)
        {
            return StartChapter(s, ch);
        }

        foreach (var previous in ch.Pack.Chapters.TakeWhile(c => c != ch))
        {
            if (!previous.Optional && !ChapterFinished(s, previous))
            {
                SkipChapterSteps(s, previous);
                CompleteChapter(s, previous, skipped: SkipEndsAsSkipped(s, previous), silent: true);
            }
        }

        if (!ch.AutoStart)
        {
            MilestonesOf(s).Add(StoryPackKeys.ChapterActive(ch.Pack.Id, ch.Id));
        }

        _repo.SavePlayer(s.State);
        AdvancePlayer(s);
        SendVegaObjective(s);
        return ChapterRuns(s, ch);
    }

    /// <summary>Sets the world's tutorial rule live: persisted into the save's rules and re-sent to everyone.</summary>
    private void SetTutorialPolicy(TutorialPolicy policy)
    {
        Rules.TutorialPolicy = policy;
        _meta.RulesOverride = Rules.Clone();
        _repo.SaveMetadata(_meta);
        foreach (var s in _sessions.Values)
        {
            if (s.Joined)
            {
                SendRules(s);
                AdvancePlayer(s);
                SendVegaObjective(s);
            }
        }
    }

    /// <summary><c>/story</c> (#2466): the world story's counters as before, then the world's tutorial rule and the player
    /// packs of the caller (or of the named player). A QA report in English like the other admin reports.</summary>
    private void AdminStoryStatus(PlayerSession session, string? targetPlayer)
    {
        var who = string.IsNullOrWhiteSpace(targetPlayer) ? session : FindSessionByName(targetPlayer) ?? session;
        var snap = StorySnapshot;
        Send(session, new ServerMessage
        {
            Text = $"Story '{snap.StoryId}': fragments={snap.Fragments}, kills={snap.Kills}, " +
                   $"milestones={snap.Milestones}, beats={snap.BeatsRevealed}/{(_story?.Beats.Count ?? 0)}, " +
                   $"finaleRevealed={_storyState.GuardianSystemRevealed}, defeated={snap.Defeated}" + StoryTrackStatus(who),
        });
        Send(session, new ServerMessage { Text = StoryPackStatus(who) });
    }

    /// <summary>The player packs part of <c>/story</c>: the world's tutorial rule and one entry per pack for
    /// <paramref name="who"/> (state, running chapter, chip step with its counter).</summary>
    private string StoryPackStatus(PlayerSession who)
    {
        if (PlayerPacks.Count == 0)
        {
            return $"tutorial policy={Rules.TutorialPolicy}; player packs: none";
        }

        var parts = new List<string>();
        foreach (var pack in PlayerPacks)
        {
            string state = PackOptedOut(who, pack) ? "opted out"
                : !StoryPackEnabledFor(who, pack.Id) ? "off"
                : PackDone(who, pack) ? "done"
                : PackStarted(who, pack) ? "started"
                : "not started";
            string running = string.Join(",", pack.Chapters.Where(c => ChapterRuns(who, c)).Select(c => c.Id));
            string chip = ChipStepOf(who, pack) is { } step
                ? $" step {step.Id} {who.State.StepProgress.GetValueOrDefault(step.ProgressKey)}/{step.Required}"
                : string.Empty;
            parts.Add($"{pack.Id} ({pack.Def.Kind}, {state}{(running.Length > 0 ? ", chapters " + running : string.Empty)}{chip})");
        }

        return $"tutorial policy={Rules.TutorialPolicy}; player packs for {who.State.Name}: {string.Join("; ", parts)}";
    }

    // ---------------------------------------------------------------------------------------------
    // Test seams.
    // ---------------------------------------------------------------------------------------------

    /// <summary>Test seam: runs the 1 Hz story-pack tick <paramref name="seconds"/> times for a player.</summary>
    internal void TickStoryPacksForTest(PlayerSession s, int seconds = 1)
    {
        for (int i = 0; i < seconds; i++)
        {
            TickStoryPacksFor(s);
        }
    }

    /// <summary>Test seam: the join hook of the story packs.</summary>
    internal void StoryPacksOnJoinForTest(PlayerSession s) => StoryPacksOnJoin(s);

    /// <summary>Test seam: the id of the step on the chip for a pack, or null.</summary>
    internal string? ChipStepForTest(PlayerSession s, string packId)
        => TryGetPlayerPack(packId, out var pack) ? ChipStepOf(s, pack)?.Id : null;

    /// <summary>Test seam: the player-pack status text of <c>/story</c>.</summary>
    internal string StoryPackStatusForTest(PlayerSession s) => StoryPackStatus(s);

    /// <summary>Test seam: the world's tutorial rule in force.</summary>
    internal TutorialPolicy TutorialPolicyForTest => Rules.TutorialPolicy;

    /// <summary>Test seam: <see cref="TutorialGate"/>.</summary>
    internal bool? TutorialGateForTest(PlayerSession s, string gate) => TutorialGate(s, gate);

    /// <summary>Test seam: <see cref="ComposeLegacyText"/>.</summary>
    internal string ComposeLegacyTextForTest(string localeCode, string key, string? arg = null) => ComposeLegacyText(localeCode, key, arg);
}
