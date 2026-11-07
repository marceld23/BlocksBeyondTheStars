// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// "Use" on a block (#3345, the Sounders story pack): the use key aimed at a block that has a use. The block's key picks the
/// handler — each feature registers its own in <see cref="BuildBlockUseHandlers"/> (one line per block), so the intent stays
/// one generic message. The server checks reach and that the named cell really holds that block; the handler does the rest.
/// Also the shared way to play a melody at a place (<see cref="PlayNoteSequence"/>).
/// </summary>
public sealed partial class GameServer
{
    /// <summary>How far (blocks, eye to cell centre) a player may reach to use a block — the door latch's reach and a bit.</summary>
    internal const float BlockUseReach = 6.5f;

    /// <summary>How far a melody carries (blocks).</summary>
    internal const float NoteSequenceRange = 32f;

    private Dictionary<string, Action<PlayerSession, Vector3i>>? _blockUseHandlers;

    /// <summary>The block key → use handler table, built once. One line per block; keep it sorted by feature.</summary>
    private Dictionary<string, Action<PlayerSession, Vector3i>> BuildBlockUseHandlers()
    {
        var map = new Dictionary<string, Action<PlayerSession, Vector3i>>(StringComparer.Ordinal);

        // #3345 rune stones play their word; puzzle pillars and the Song Spindle register below as they land.
        return map;
    }

    /// <summary>Whether <paramref name="blockKey"/> has a use (the client asks the same through the block's <c>usable</c> flag).</summary>
    internal bool BlockHasUse(string blockKey)
        => (_blockUseHandlers ??= BuildBlockUseHandlers()).ContainsKey(blockKey);

    private void HandleUseBlock(PlayerSession session, UseBlockIntent intent)
    {
        if (session.Spectating || InSpace(session.State.PlayerId))
        {
            return;
        }

        var cell = new Vector3i(intent.X, intent.Y, intent.Z);
        var centre = new Vector3f(cell.X + 0.5f, cell.Y + 0.5f, cell.Z + 0.5f);
        if (WrapDistSq(session.State.Position, centre) > (double)BlockUseReach * BlockUseReach)
        {
            return; // out of reach
        }

        var id = _world.GetBlockIfLoaded(cell);
        if (_content.BlockById(id) is not { } def)
        {
            return;
        }

        _blockUseHandlers ??= BuildBlockUseHandlers();
        if (_blockUseHandlers.TryGetValue(def.Key, out var handler))
        {
            handler(session, cell);
        }
    }

    /// <summary>
    /// Plays a melody at <paramref name="at"/> for every joined player of the active world who is on the ground within
    /// <see cref="NoteSequenceRange"/> — one <see cref="NoteSequence"/> each, the client keeps the beat.
    /// </summary>
    internal void PlayNoteSequence(Vector3f at, IReadOnlyList<int> notes, int instrument, float noteSeconds = 0.32f,
                                   bool withFifth = false, string source = "")
    {
        if (notes.Count == 0)
        {
            return;
        }

        var message = new NoteSequence
        {
            Notes = ToArray(notes),
            Instrument = instrument,
            NoteSeconds = noteSeconds,
            WithFifth = withFifth,
            X = at.X,
            Y = at.Y,
            Z = at.Z,
            Source = source,
        };
        foreach (var s in JoinedInActiveWorld())
        {
            if (!InSpace(s.State.PlayerId) && WrapDistSq(s.State.Position, at) <= (double)NoteSequenceRange * NoteSequenceRange)
            {
                Send(s, message);
            }
        }
    }

    private static int[] ToArray(IReadOnlyList<int> notes)
    {
        var array = new int[notes.Count];
        for (int i = 0; i < array.Length; i++)
        {
            array[i] = notes[i];
        }

        return array;
    }
}
