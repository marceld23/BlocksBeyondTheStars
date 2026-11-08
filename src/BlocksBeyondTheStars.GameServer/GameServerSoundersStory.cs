// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Shared.Sounders;
using BlocksBeyondTheStars.Shared.Story;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The Sounders' Song (#3368) — the story glue between the pack's data (<c>data/stories/sounders/</c>) and the gameplay the
/// pack's chapters wait for. The steps themselves are data; this code only does what data cannot: it starts the right
/// melody puzzle when a player strikes a melody site (the shrine's echo game in chapter 2, the Song of Return in chapter 6,
/// the Bridge Song at the Choir Tower in chapter 8), hands out the shrine's reward, writes <c>snd:song_learned</c> (which
/// shows Ornemis on that player's star map), lets the Echoes appear and turns the end of their dialogs into
/// <c>echo:questioned</c> (<c>GameServerSoundersStory.Echoes.cs</c>), and stages the finale and the sing duel for the story
/// Soundi (<c>GameServerSoundersStory.Finale.cs</c>). Everything is per player and only while the pack runs for them.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The shrine (or ring) gave its reward — the Song Spindle and disc 1 — to this player.</summary>
    internal const string SoundersShrineRewardMilestone = "snd:shrine_reward";

    /// <summary>Roll was convinced (the good last answer of <c>snd_roll</c> writes it as a dialog consequence).</summary>
    internal const string SoundersRollConvincedMilestone = "snd:roll_convinced";

    /// <summary>The Bridge Song was played with the Young Voices.</summary>
    internal const string SoundersBridgeDoneMilestone = "snd:bridge_done";

    /// <summary>The sing duel was won — the story Soundi travels with the player.</summary>
    internal const string SoundersSoundiWonMilestone = "snd:soundi";

    /// <summary>The pack's chapters in order (the ids of <c>story.json</c>).</summary>
    private static readonly string[] SoundersChapters = { "s1", "s2", "s3", "s4", "s5", "s6", "s7", "s8" };

    /// <summary>The shrine's echo game (chapter 2): C E G E C' — three rounds that grow (2, 4, then all 5 tones).</summary>
    private static readonly int[] ShrineEchoTune = { 0, 2, 4, 2, 7 };

    /// <summary>The Soundi's three calls in the sing duel — short, then longer, the last with a little turn.</summary>
    private static readonly int[][] SingDuelCalls =
    {
        new[] { 4, 2, 0 },
        new[] { 0, 2, 4, 7 },
        new[] { 7, 4, 5, 4, 2 },
    };

    private SoundersStoryListener? _soundersListener;

    /// <summary>Hooks the story into the melody puzzles and the event bus — once, from the constructor.</summary>
    private void WireSoundersStory()
    {
        RegisterMelodySiteStarter(SoundersSiteStarter);
        RegisterMelodySolvedHandler(SoundersIds.PuzzleShrineEcho, OnShrineEchoSolved);
        RegisterMelodySolvedHandler(SoundersIds.PuzzleSongOfReturn, OnSongOfReturnSolved);
        RegisterMelodySolvedHandler(SoundersIds.PuzzleBridgeSong, OnBridgeSongSolved);
        RegisterMelodySolvedHandler(SoundersIds.PuzzleSingDuel, OnSingDuelSolved);
        _soundersListener = new SoundersStoryListener(this);
        AddGameEventListener(GameEventKeys.DialogDone, _soundersListener);
    }

    /// <summary>Whether chapter <paramref name="chapterId"/> of the Sounders pack runs for <paramref name="s"/>: the pack is
    /// active, the chapter before it is done or skipped, and this one is not.</summary>
    internal bool InSoundersChapter(PlayerSession s, string chapterId)
    {
        int index = Array.IndexOf(SoundersChapters, chapterId);
        if (index < 0 || !IsStoryPackActive(s, SoundersIds.PackId))
        {
            return false;
        }

        var milestones = s.State.Milestones;
        bool previousDone = index == 0 || SoundersChapterThrough(milestones, SoundersChapters[index - 1]);
        return previousDone && !SoundersChapterThrough(milestones, chapterId);
    }

    private static bool SoundersChapterThrough(System.Collections.Generic.HashSet<string> milestones, string chapterId)
        => milestones.Contains(StoryPackKeys.ChapterDone(SoundersIds.PackId, chapterId))
           || milestones.Contains(StoryPackKeys.ChapterSkipped(SoundersIds.PackId, chapterId));

    /// <summary>Sets a pack milestone; true when it is new (the Journal shows the archive's texts from it).</summary>
    private bool SetSoundersMilestone(PlayerSession s, string key)
    {
        if (!s.State.Milestones.Add(key))
        {
            return false;
        }

        MarkJournalDirty(s);
        _repo.SavePlayer(s.State);
        return true;
    }

    /// <summary>
    /// The melody site starter (<see cref="RegisterMelodySiteStarter"/>): a strike on the shrine or a rune ring starts the
    /// echo game while chapter 2 runs and the Song of Return while chapter 6 runs; a strike on a Choir Tower pipe starts the
    /// Bridge Song in chapter 8 once Roll was convinced. Anything else passes (the stone sings its rune word).
    /// </summary>
    private MelodyPuzzleSpec? SoundersSiteStarter(PlayerSession s, MelodySite site)
    {
        bool shrineLike = string.Equals(site.Kind, MelodySiteKindShrine, StringComparison.Ordinal)
                          || string.Equals(site.Kind, MelodySiteKindRing, StringComparison.Ordinal);
        if (shrineLike && InSoundersChapter(s, "s2") && !s.State.Milestones.Contains(SoundersShrineRewardMilestone))
        {
            return new MelodyPuzzleSpec
            {
                Id = SoundersIds.PuzzleShrineEcho,
                Mode = MelodyPuzzleMode.Repeat,
                Target = ShrineEchoTune,
                Rounds = 3,
                Instrument = SoundersIds.InstrumentChoir,
            };
        }

        if (shrineLike && InSoundersChapter(s, "s6") && !s.State.Milestones.Contains(SoundersIds.MilestoneSongLearned))
        {
            return new MelodyPuzzleSpec
            {
                Id = SoundersIds.PuzzleSongOfReturn,
                Mode = MelodyPuzzleMode.Compose,
                Target = SounderAlphabet.Encode(SoundersIds.SongOfReturnWord),
                Word = SoundersIds.SongOfReturnWord, // #3426: the bar shows every letter's two notes
                IdleSeconds = 30f,
            };
        }

        if (string.Equals(site.Id, ChoirTowerSiteKey, StringComparison.Ordinal) && InSoundersChapter(s, "s8")
            && s.State.Milestones.Contains(SoundersRollConvincedMilestone)
            && !s.State.Milestones.Contains(SoundersBridgeDoneMilestone))
        {
            return new MelodyPuzzleSpec
            {
                Id = SoundersIds.PuzzleBridgeSong,
                Mode = MelodyPuzzleMode.Harmony,
                Target = SounderAlphabet.Encode(SoundersIds.SongOfReturnWord),
                Word = SoundersIds.SongOfReturnWord, // #3426: the bar shows each letter's counter-voice notes
                Instrument = SoundersIds.InstrumentChoir,
                IdleSeconds = 30f,
            };
        }

        return null;
    }

    /// <summary>Chapter 2's reward: the Song Spindle and the first disc, once per player.</summary>
    private void OnShrineEchoSolved(PlayerSession s)
    {
        if (!IsStoryPackActive(s, SoundersIds.PackId) || !SetSoundersMilestone(s, SoundersShrineRewardMilestone))
        {
            return;
        }

        GiveSoundersItem(s, SoundersIds.SongSpindle, 1);
        GiveSoundersItem(s, SoundersIds.DiscBeginning, 1);
    }

    /// <summary>Chapter 6: the ship is "tuned" — the pack system's <c>visibleIf</c> holds from now on and the next pack
    /// tick appends Ornemis and shows it on this player's star map.</summary>
    private void OnSongOfReturnSolved(PlayerSession s)
    {
        if (IsStoryPackActive(s, SoundersIds.PackId))
        {
            SetSoundersMilestone(s, SoundersIds.MilestoneSongLearned);
        }
    }

    /// <summary>The event bus listener: the end of a Sounders dialog.</summary>
    private sealed class SoundersStoryListener : IGameEventListener
    {
        private readonly GameServer _server;

        public SoundersStoryListener(GameServer server) => _server = server;

        public void OnGameEvent(PlayerSession actor, string key, string arg, int amount)
        {
            if (string.Equals(key, GameEventKeys.DialogDone, StringComparison.Ordinal))
            {
                _server.OnSoundersDialogDone(actor, arg);
            }
        }
    }

    // --- Test seams --------------------------------------------------------------------------------------------------------

    /// <summary>Test seam: the starter's answer for a site.</summary>
    internal MelodyPuzzleSpec? SoundersStarterForTest(PlayerSession s, MelodySite site) => SoundersSiteStarter(s, site);
}
