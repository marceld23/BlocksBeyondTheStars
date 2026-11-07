// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Sounders;
using BlocksBeyondTheStars.Shared.Story;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The note-key minigame on the server (#3352): decoding a story Song Disc. A story disc the player has not decoded yet,
/// sung by a Song Spindle for a player whose Sounders pack is active, opens the minigame on that player's client
/// (<see cref="StoryMinigameStart"/>, the disc's three words and the letters the player already knows). The client answers
/// with <see cref="StoryMinigameResultIntent"/>; the server accepts it only for the disc it offered and only when the answers
/// spell the disc's phrase — anything else counts as a failed try and the offer goes out again with the new count. After
/// <see cref="SongDiscs.FailuresBeforeSkip"/> failed tries a skip is accepted (VEGA offers it; no dead end). Decoding writes
/// the milestone <c>snd:disc:&lt;disc&gt;</c> (which shows the hidden blueprint the disc carries) and raises
/// <see cref="GameEventKeys.DiscDecoded"/>. The offers and the failure counts live in RAM: a player who leaves simply opens
/// the minigame again at the spindle.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The story disc the server last opened the minigame for, per player id.</summary>
    private readonly Dictionary<string, string> _noteKeyOffers = new(System.StringComparer.Ordinal);

    /// <summary>Failed note-key tries per "player|disc".</summary>
    private readonly Dictionary<string, int> _noteKeyFailures = new(System.StringComparer.Ordinal);

    /// <summary>Opens the note-key minigame for <paramref name="disc"/> when it is a story disc this player still has to
    /// decode and their Sounders pack is active. True when the offer went out.</summary>
    internal bool MaybeOfferNoteKey(PlayerSession session, string disc)
    {
        var p = session.State;
        if (!SongDiscs.IsStoryDisc(disc) || p.Milestones.Contains(SoundersIds.DiscMilestone(disc)) || !SoundersActive(session))
        {
            return false;
        }

        _noteKeyOffers[p.PlayerId] = disc;
        SendNoteKeyStart(session, disc);
        return true;
    }

    private void SendNoteKeyStart(PlayerSession session, string disc)
        => Send(session, new StoryMinigameStart
        {
            Game = SongDiscs.NoteKeyGame,
            Context = disc,
            Words = SongDiscs.WordsOf(disc).ToArray(),
            KnownLetters = KnownSounderLetters(session.State),
            Failures = NoteKeyFailures(session.State.PlayerId, disc),
            Instrument = SoundersIds.InstrumentVoice,
        });

    private int NoteKeyFailures(string playerId, string disc)
        => _noteKeyFailures.TryGetValue(playerId + "|" + disc, out int n) ? n : 0;

    /// <summary>The letters of the note alphabet this player knows: those of every rune word they heard
    /// (<c>snd:word:&lt;WORD&gt;</c>) and of every disc they decoded. The minigame's alphabet table shows them filled in.</summary>
    internal static string KnownSounderLetters(Shared.State.PlayerState p)
    {
        string wordPrefix = SoundersIds.WordMilestone(string.Empty);
        string discPrefix = SoundersIds.DiscMilestone(string.Empty);
        var words = new List<string>();
        foreach (string m in p.Milestones)
        {
            if (m.StartsWith(wordPrefix, System.StringComparison.Ordinal))
            {
                words.Add(m.Substring(wordPrefix.Length));
            }
            else if (m.StartsWith(discPrefix, System.StringComparison.Ordinal))
            {
                words.AddRange(SongDiscs.WordsOf(m.Substring(discPrefix.Length)));
            }
        }

        return SongDiscs.LettersOf(words);
    }

    /// <summary>The client's answer to a note-key run (or a skip after failed tries).</summary>
    private void HandleStoryMinigameResult(PlayerSession session, StoryMinigameResultIntent intent)
    {
        var p = session.State;
        string disc = intent.Context ?? string.Empty;
        if (intent.Game != SongDiscs.NoteKeyGame || !_noteKeyOffers.TryGetValue(p.PlayerId, out string? offered) || offered != disc)
        {
            return; // a run the server did not open
        }

        if (p.Milestones.Contains(SoundersIds.DiscMilestone(disc)))
        {
            _noteKeyOffers.Remove(p.PlayerId);
            return;
        }

        string failKey = p.PlayerId + "|" + disc;
        if (intent.Skip)
        {
            if (NoteKeyFailures(p.PlayerId, disc) >= SongDiscs.FailuresBeforeSkip)
            {
                DecodeDisc(session, disc, skipped: true);
            }

            return; // a skip before the tries are up is ignored — the window keeps its game
        }

        if (intent.Completed && SongDiscs.AnswersMatch(disc, intent.Answers))
        {
            DecodeDisc(session, disc, skipped: false);
            return;
        }

        _noteKeyFailures[failKey] = NoteKeyFailures(p.PlayerId, disc) + 1;
        SendNoteKeyStart(session, disc); // the open window learns the new count (and offers the skip once it may)
    }

    /// <summary>The disc is decoded: its milestone (the hidden blueprint it carries shows up), the story event and a word.</summary>
    private void DecodeDisc(PlayerSession session, string disc, bool skipped)
    {
        var p = session.State;
        _noteKeyOffers.Remove(p.PlayerId);
        _noteKeyFailures.Remove(p.PlayerId + "|" + disc);
        if (!p.Milestones.Add(SoundersIds.DiscMilestone(disc)))
        {
            return;
        }

        Send(session, new ServerMessage { Text = skipped ? "@srv.sounders.disc_skipped" : "@srv.sounders.disc_decoded" });
        RaiseGameEvent(session, GameEventKeys.DiscDecoded, disc);
    }

    /// <summary>Test seam: the result intent as if the client had sent it.</summary>
    internal void StoryMinigameResultForTest(PlayerSession session, StoryMinigameResultIntent intent)
        => HandleStoryMinigameResult(session, intent);
}
