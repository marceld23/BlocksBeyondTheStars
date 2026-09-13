// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Moderation;
using BlocksBeyondTheStars.Shared.State;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Player notes (#1844): titled free-text pages a player keeps for themselves under the Story tab — a base
/// plan, a shopping list, a diary. Private (only ever sent back to their author), persisted on the player
/// blob, capped at <see cref="NoteMaxPerPlayer"/>. The title goes through the same content screen as a base
/// name (a refused title refuses the save — the note is not worth a "Fort ***"); the body is screened like a
/// chat line, profanity masked rather than refused, because a two-thousand-character page must not vanish
/// over one word. Newlines are the one control character the body keeps.
/// </summary>
public sealed partial class GameServer
{
    private const int NoteMaxPerPlayer = 20;
    private const int NoteTitleMaxLength = 40;
    private const int NoteBodyMaxLength = 2000;

    // ---------------------------------------------------------------------------------------------
    // The one intent envelope.
    // ---------------------------------------------------------------------------------------------

    private void HandleNoteAction(PlayerSession session, NoteActionIntent intent)
    {
        switch (intent.Kind)
        {
            case "set": SetNote(session, intent); break;
            case "remove": RemoveNote(session, intent.Id); break;
            default: break; // unknown verb from a newer client — ignore
        }
    }

    private void SetNote(PlayerSession session, NoteActionIntent intent)
    {
        // The base-name rules for the title: strip + clamp, then the #1221 content screen. A refused title
        // refuses the save; the player is told through the note surface, not the name one.
        string cleanTitle = Clamp(StripControlChars(intent.Title), NoteTitleMaxLength);
        if (ScreenPlayerName(session, cleanTitle, "note", "@srv.note.blocked") is not { } title)
        {
            return;
        }

        if (ScreenNoteBody(session, StripControlCharsKeepNewlines(intent.Body, NoteBodyMaxLength)) is not { } body)
        {
            return;
        }

        var p = session.State;
        var existing = string.IsNullOrEmpty(intent.Id) ? null : p.Notes.FirstOrDefault(n => n.Id == intent.Id);
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (existing is null)
        {
            if (p.Notes.Count >= NoteMaxPerPlayer)
            {
                Reject(session, "note", "@srv.note.full");
                return;
            }

            existing = new PlayerNote
            {
                Id = "nt" + Guid.NewGuid().ToString("N").Substring(0, 12),
                CreatedUtc = now,
            };
            p.Notes.Add(existing);
        }

        existing.Title = title;
        existing.Body = body;
        existing.UpdatedUtc = now;
        _repo.SavePlayer(p);
        SendNotes(session);
    }

    private void RemoveNote(PlayerSession session, string id)
    {
        var p = session.State;
        var note = p.Notes.FirstOrDefault(n => n.Id == id);
        if (note is null)
        {
            return;
        }

        p.Notes.Remove(note);
        _repo.SavePlayer(p);
        SendNotes(session);
    }

    // ---------------------------------------------------------------------------------------------
    // Sanitising + screening.
    // ---------------------------------------------------------------------------------------------

    private static string Clamp(string text, int max) => text.Length <= max ? text : text.Substring(0, max);

    /// <summary>Like <see cref="StripControlChars"/>, but a line break survives: a note is a page, not a chat
    /// line. CR/LF pairs fold to one newline; every other control character becomes a space. Clamped to
    /// <paramref name="max"/> characters, outer whitespace trimmed.</summary>
    internal static string StripControlCharsKeepNewlines(string? text, int max)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(Math.Min(text.Length, max));
        for (int i = 0; i < text.Length && sb.Length < max; i++)
        {
            char c = text[i];
            if (c == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n')
                {
                    continue; // the '\n' that follows is kept
                }

                sb.Append('\n');
            }
            else if (c == '\n' || !char.IsControl(c))
            {
                sb.Append(c);
            }
            else
            {
                sb.Append(' ');
            }
        }

        return sb.ToString().Trim();
    }

    /// <summary>Screens the body line by line with the chat screen: a masked line is stored masked (the
    /// sender is told once per session, as in chat), a blocked line refuses the whole save. Line by line so
    /// the mask never touches the line breaks. Returns null when refused.</summary>
    private string? ScreenNoteBody(PlayerSession session, string body)
    {
        var mode = EffectiveChatMode;
        if (body.Length == 0 || mode == ChatMode.Open)
        {
            return body;
        }

        var lines = body.Split('\n');
        bool masked = false;
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].Length == 0)
            {
                continue;
            }

            var result = ChatContentScreen.Screen(lines[i], mode);
            switch (result.Verdict)
            {
                case ChatVerdict.Block:
                    _log.Info($"Note filter: refused a note from '{session.State.Name ?? "?"}' " +
                              $"({(result.Pii ? "personal data: " : "term: ")}{result.MatchedTerm}).");
                    Reject(session, "note", "@srv.note.blocked");
                    return null;
                case ChatVerdict.Mask:
                    lines[i] = result.Text;
                    masked = true;
                    break;
            }
        }

        if (masked && !session.ChatMaskNoticeSent)
        {
            session.ChatMaskNoticeSent = true;
            Send(session, new ServerMessage { Text = "@srv.chat.masked" });
        }

        return masked ? string.Join("\n", lines) : body;
    }

    // ---------------------------------------------------------------------------------------------
    // Sync.
    // ---------------------------------------------------------------------------------------------

    /// <summary>The player's own notes, newest first. The list is appended in creation order (and the snapshot
    /// keeps that order), so reversing it is exact — sorting by CreatedUtc would tie two notes written in the
    /// same millisecond.</summary>
    private static NoteList NotesFor(PlayerSession session) => new()
    {
        Notes = session.State.Notes
            .AsEnumerable()
            .Reverse()
            .Select(n => new NetNote
            {
                Id = n.Id,
                Title = n.Title,
                Body = n.Body,
                CreatedUtc = n.CreatedUtc,
                UpdatedUtc = n.UpdatedUtc,
            })
            .ToArray(),
    };

    private void SendNotes(PlayerSession session) => Send(session, NotesFor(session));

    // ---------------------------------------------------------------------------------------------
    // Test hooks.
    // ---------------------------------------------------------------------------------------------

    /// <summary>Test/util: create/update a note as a player (mirrors the intent). Returns the note count the
    /// player now has.</summary>
    public int SetNoteForTest(string playerId, string title, string body, string id = "")
    {
        if (FindSessionByPlayerId(playerId) is { } s)
        {
            HandleNoteAction(s, new NoteActionIntent { Kind = "set", Id = id, Title = title, Body = body });
            return s.State.Notes.Count;
        }

        return 0;
    }

    /// <summary>Test/util: remove an own note.</summary>
    public void RemoveNoteForTest(string playerId, string id)
    {
        if (FindSessionByPlayerId(playerId) is { } s)
        {
            HandleNoteAction(s, new NoteActionIntent { Kind = "remove", Id = id });
        }
    }

    /// <summary>Test/util: the given player's notes as the client would receive them (newest first).</summary>
    public IReadOnlyList<(string Id, string Title, string Body)> NotesForTest(string playerId)
        => FindSessionByPlayerId(playerId) is { } s
            ? NotesFor(s).Notes.Select(n => (n.Id, n.Title, n.Body)).ToList()
            : new List<(string, string, string)>();
}
