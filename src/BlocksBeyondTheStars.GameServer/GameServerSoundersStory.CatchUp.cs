// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Sounders;
using BlocksBeyondTheStars.Shared.Story;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// #3425: a skip never strands a player in the Sounders' Song. A kid skipped chapters 1 and 2 and stood at "decode the first
/// Song Disc" without the spindle's first disc — chapter 2's reward is handed out only when the shrine's echo game is solved,
/// and the note table stayed empty because only decoding fills it. So, once a step (or a whole chapter) of the pack was
/// SKIPPED, the server hands over what playing it would have left with the player — once, guarded by the milestones the
/// normal play writes (a player who played it has them and never gets a second one):
/// <list type="bullet">
/// <item>chapter 2 (the shrine): the Song Spindle (unless one is carried) and disc 1 — <c>snd:shrine_reward</c>;</item>
/// <item>s3.2 / s3.4 / s4.5 (decode a disc): the disc counts as decoded (<c>snd:disc:&lt;disc&gt;</c>) — its letters fill the
/// note table, its Song Archive page opens, the hidden blueprint it carries shows up in research and the story loot moves
/// on to the next disc, exactly as a decode would; s3.4 also hands over the builders and the hearing disc (the hearing disc
/// is the chest socket's disc chapters 4 and 5 need), s4.4 (open a sound chamber) the Great Song disc — a story disc only
/// when the player neither decoded it nor has it at hand (<see cref="NextMissingDisc"/>), and once
/// (<c>snd:catchup:&lt;disc&gt;</c>: a disc sitting in a spindle is not at hand, yet must not come again);</item>
/// <item>s4.2 (research): the Resonance Blaster and obsidian armour blueprints; s4.3 / s4.6 (craft): one Resonance Blaster /
/// obsidian chest piece, unless one is carried (<c>snd:catchup:&lt;item&gt;</c> makes it once);</item>
/// <item>chapter 6: the Song of Return is learned (<c>snd:song_learned</c>, Ornemis on the star map); s8.1: Roll is convinced;
/// s8.2: the Bridge Song counts as sung (the sing duel can start).</item>
/// </list>
/// Only while the story still runs — a chapter of it is ahead — so "Skip all" hands out nothing, and the sing duel's Soundi
/// (the last step) stays the prize of playing it. VEGA says what was handed over (<c>srv.sounders.catch_up_items</c>) or that
/// the gaps were filled (<c>srv.sounders.catch_up</c>). Runs in the 1 Hz story-pack pass (<see cref="TickStoryPacksFor"/>), so
/// an old save that is already stuck heals on its next join as well.
/// <para>And a disc that is no story disc (a socket disc, a collectible) sung on a spindle while a decode step runs says it only
/// sings a tune and names the disc the story waits for (<see cref="HintStoryDisc"/>).</para>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Prefix of the catch-up's own once-milestones (a disc or a crafted piece handed over for a skipped step).</summary>
    internal const string SoundersCatchUpPrefix = "snd:catchup:";

    private enum SoundersCatchUpKind
    {
        /// <summary>Chapter 2's reward: the Song Spindle and disc 1.</summary>
        ShrineReward,

        /// <summary>A story disc the step would have found (unless decoded or at hand).</summary>
        FindDisc,

        /// <summary>A story disc the step would have decoded.</summary>
        DecodeDisc,

        /// <summary>A blueprint the step would have researched.</summary>
        Blueprint,

        /// <summary>An item the step would have crafted (once, unless one is carried).</summary>
        Craft,

        /// <summary>A pack milestone the step would have written.</summary>
        Milestone,
    }

    /// <summary>What each skipped step (a chapter id: the whole chapter, for steps with stand-ins) would have left with the
    /// player, in story order — a disc is found before it is decoded.</summary>
    private static readonly (string Producer, SoundersCatchUpKind Kind, string Key)[] SoundersCatchUps =
    {
        ("s2", SoundersCatchUpKind.ShrineReward, string.Empty),
        ("s3.2", SoundersCatchUpKind.DecodeDisc, SoundersIds.DiscBeginning),
        ("s3.4", SoundersCatchUpKind.FindDisc, SoundersIds.DiscBuilders),
        ("s3.4", SoundersCatchUpKind.FindDisc, SoundersIds.DiscHearing),
        ("s3.4", SoundersCatchUpKind.DecodeDisc, SoundersIds.DiscBuilders),
        ("s3.4", SoundersCatchUpKind.DecodeDisc, SoundersIds.DiscHearing),
        ("s4.2", SoundersCatchUpKind.Blueprint, SoundersIds.BlueprintResonance),
        ("s4.2", SoundersCatchUpKind.Blueprint, SoundersIds.BlueprintObsidian),
        ("s4.3", SoundersCatchUpKind.Craft, SoundersIds.ResonanceBlaster),
        ("s4.4", SoundersCatchUpKind.FindDisc, SoundersIds.DiscGreatSong),
        ("s4.5", SoundersCatchUpKind.DecodeDisc, SoundersIds.DiscGreatSong),
        ("s4.6", SoundersCatchUpKind.Craft, SoundersIds.ObsidianChest),
        ("s6", SoundersCatchUpKind.Milestone, SoundersIds.MilestoneSongLearned),
        ("s8.1", SoundersCatchUpKind.Milestone, SoundersRollConvincedMilestone),
        ("s8.2", SoundersCatchUpKind.Milestone, SoundersBridgeDoneMilestone),
    };

    /// <summary>The catch-up (see the type's summary): hands over, once, what skipped Sounders steps would have given.</summary>
    private void CatchUpSoundersSkips(PlayerSession s)
    {
        if (!s.Joined || s.Spectating || !IsStoryPackActive(s, SoundersIds.PackId) || !SoundersStoryRuns(s))
        {
            return;
        }

        var handed = new List<string>(); // display names of what went into the backpack or the research list
        bool changed = false;
        foreach (var (producer, kind, key) in SoundersCatchUps)
        {
            if (SoundersProducerSkipped(s.State.Milestones, producer))
            {
                changed |= ApplySoundersCatchUp(s, kind, key, handed);
            }
        }

        if (!changed)
        {
            return;
        }

        MarkJournalDirty(s);
        _repo.SavePlayer(s.State);
        SendInventory(s); // the backpack and the research list
        Send(s, new ServerMessage
        {
            Text = handed.Count > 0 ? "@srv.sounders.catch_up_items:" + string.Join(", ", handed) : "@srv.sounders.catch_up",
        });
    }

    /// <summary>Whether a chapter of the Sounders' Song still runs for the player (one is ahead of them).</summary>
    private bool SoundersStoryRuns(PlayerSession s)
    {
        foreach (string chapter in SoundersChapters)
        {
            if (InSoundersChapter(s, chapter))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether <paramref name="producer"/> was passed without being played: a chapter id (s2, s6 — their steps have
    /// stand-ins, so a skipped step alone says nothing) once the chapter is through, its outcome guarding the rest; a step id
    /// once it is skipped and not done.</summary>
    private static bool SoundersProducerSkipped(HashSet<string> milestones, string producer)
        => producer.IndexOf('.') < 0
            ? SoundersChapterThrough(milestones, producer)
            : milestones.Contains(StoryPackKeys.StepSkipped(SoundersIds.PackId, producer))
              && !milestones.Contains(StoryPackKeys.StepDone(SoundersIds.PackId, producer));

    /// <summary>One catch-up entry; true when it changed the player. Names of handed items and blueprints go to
    /// <paramref name="handed"/>.</summary>
    private bool ApplySoundersCatchUp(PlayerSession s, SoundersCatchUpKind kind, string key, List<string> handed)
    {
        var p = s.State;
        switch (kind)
        {
            case SoundersCatchUpKind.ShrineReward:
                if (!p.Milestones.Add(SoundersShrineRewardMilestone))
                {
                    return false;
                }

                if (!CarriesSounderItem(p, SoundersIds.SongSpindle))
                {
                    HandSoundersItem(s, SoundersIds.SongSpindle, handed);
                }

                ApplySoundersCatchUp(s, SoundersCatchUpKind.FindDisc, SoundersIds.DiscBeginning, handed);
                return true;
            case SoundersCatchUpKind.FindDisc:
                // Once (a disc sitting in a spindle is not "at hand" and must not come again every second).
                if (p.Milestones.Contains(SoundersCatchUpPrefix + key) || NextMissingDisc(p, new[] { key }) is null)
                {
                    return false;
                }

                p.Milestones.Add(SoundersCatchUpPrefix + key);
                HandSoundersItem(s, key, handed);
                return true;
            case SoundersCatchUpKind.DecodeDisc:
                return p.Milestones.Add(SoundersIds.DiscMilestone(key));
            case SoundersCatchUpKind.Blueprint:
                if (_content.GetBlueprint(key) is not { } bp || !p.UnlockedBlueprints.Add(key))
                {
                    return false;
                }

                handed.Add(LocalizedName(s.Locale, bp.NameKey, key));
                return true;
            case SoundersCatchUpKind.Craft:
                if (!p.Milestones.Add(SoundersCatchUpPrefix + key))
                {
                    return false;
                }

                if (!CarriesSounderItem(p, key))
                {
                    HandSoundersItem(s, key, handed);
                }

                return true;
            default:
                return p.Milestones.Add(key);
        }
    }

    /// <summary>One Sounders item into the backpack (or the hold, or at the player's feet) and its name into the line.</summary>
    private void HandSoundersItem(PlayerSession s, string itemKey, List<string> handed)
    {
        if (_content.GetItem(itemKey) is not { } item)
        {
            return;
        }

        GiveSoundersItem(s, itemKey, 1);
        handed.Add(LocalizedName(s.Locale, item.NameKey, itemKey));
    }

    // ---------------- A tune instead of the story's disc ----------------

    /// <summary>
    /// A Song Spindle sang <paramref name="disc"/> for this player without opening the note-key minigame: when it is a disc
    /// that is no story disc (a socket disc, a collectible) and a decode step of the Sounders' Song runs, the player learns it
    /// only sings a tune — and which disc the story waits for (<c>srv.sounders.wrong_disc</c> when they carry it,
    /// <c>srv.sounders.wrong_disc_find</c> when it is still to be found).
    /// </summary>
    private void HintStoryDisc(PlayerSession session, string disc)
    {
        if (!SongDiscs.IsDisc(disc) || SongDiscs.IsStoryDisc(disc) || WantedStoryDisc(session) is not { } wanted)
        {
            return;
        }

        string key = CarriesSounderItem(session.State, wanted) ? "@srv.sounders.wrong_disc:" : "@srv.sounders.wrong_disc_find:";
        Send(session, new ServerMessage { Text = key + LocalizedName(session.Locale, _content.GetItem(wanted)?.NameKey, wanted) });
    }

    /// <summary>The story disc a running decode step (<c>disc:decoded</c>) of the player's Sounders pack waits for — one they
    /// carry first — or null when no decode step runs.</summary>
    internal string? WantedStoryDisc(PlayerSession s)
    {
        if (!SoundersActive(s) || SoundersPack is not { } pack)
        {
            return null;
        }

        string? wanted = null;
        foreach (var chapter in pack.Chapters)
        {
            foreach (var step in chapter.Steps)
            {
                if (!string.Equals(step.Event, GameEventKeys.DiscDecoded, StringComparison.Ordinal) || !StepActive(s, step))
                {
                    continue;
                }

                foreach (string disc in step.Args)
                {
                    if (s.State.Milestones.Contains(SoundersIds.DiscMilestone(disc)))
                    {
                        continue;
                    }

                    if (CarriesSounderItem(s.State, disc))
                    {
                        return disc;
                    }

                    wanted ??= disc;
                }
            }
        }

        return wanted;
    }

    /// <summary>Test seam: the catch-up for a player now (it runs in the 1 Hz story-pack pass).</summary>
    internal void CatchUpSoundersSkipsForTest(PlayerSession s) => CatchUpSoundersSkips(s);
}
