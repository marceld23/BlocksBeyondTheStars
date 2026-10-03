// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.State;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Compatibility between builds (#2222, #2223). Numeric block ids follow the sorted block keys, so every build
/// that adds a block shifts them. Two things must then never meet a build with another block set:
/// <list type="bullet">
/// <item><b>A client.</b> Chunks travel as raw ids — the join compares the content fingerprint of both sides
/// (<see cref="Shared.Content.GameContent.BlockFingerprint"/>) and refuses a mismatch, instead of letting the
/// client draw every block as its neighbour.</item>
/// <item><b>A save from a newer build.</b> The first load remaps stored ids by key and maps keys this build
/// does not know to air, for good — a world with a higher save version than this build writes is refused and
/// left untouched.</item>
/// </list>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The exit code of the server host when the world was saved by a newer build (#2223) — so a launcher
    /// can tell "update the game" apart from "the server could not start".</summary>
    public const int SaveTooNewExitCode = 3;

    /// <summary>What the log MESSAGE of that refusal starts with — the same signal for a launcher that reads the
    /// server's output instead of its exit code, and for a host that runs the server in its own process. The
    /// message, not the line: the console logger puts the time and the level in front
    /// (<c>… [ERROR] [fatal] save-too-new: …</c>), so a reader looks for the marker inside the line, not at its start.</summary>
    public const string SaveTooNewMarker = "[fatal] save-too-new";

    /// <summary>
    /// Starts the server for a host process and names the one start failure a host must tell apart (#2223):
    /// 0 when the server is up, <see cref="SaveTooNewExitCode"/> when the world was saved by a newer build — it
    /// was refused, left untouched and logged with <see cref="SaveTooNewMarker"/>. Every other failure is thrown
    /// as before.
    /// </summary>
    public int StartForHost()
    {
        try
        {
            Start();
            return 0;
        }
        catch (SaveVersionTooNewException)
        {
            return SaveTooNewExitCode;
        }
    }

    /// <summary>The join refusals logged for a mismatching fingerprint are bounded: one line per distinct
    /// fingerprint, and no more than this many distinct ones — a flood of joins cannot fill the log.</summary>
    private const int MaxLoggedContentMismatches = 16;

    private readonly HashSet<string> _loggedContentMismatches = new(StringComparer.Ordinal);

    /// <summary>
    /// Refuses to open a world a newer build wrote (#2223). Runs right after the save is opened and BEFORE the
    /// block palette is brought up to date — that step is the one that would rewrite the save. Older saves (a
    /// lower or missing version) pass and are stamped with <see cref="WorldMetadata.CurrentSaveVersion"/> once
    /// the world is loaded. The refusal is logged here and nowhere else, behind <see cref="SaveTooNewMarker"/>.
    /// </summary>
    private void EnsureSaveVersionSupported()
    {
        var stored = _repo.LoadMetadata();
        if (stored is null || stored.SaveVersion <= WorldMetadata.CurrentSaveVersion)
        {
            return;
        }

        var refusal = new SaveVersionTooNewException(stored.WorldName, stored.SaveVersion, WorldMetadata.CurrentSaveVersion);
        _log.Error($"{SaveTooNewMarker}: {refusal.Message}");
        throw refusal;
    }

    /// <summary>
    /// The content half of the join gate (#2222): true — and the refusal already sent — when the client's block
    /// palette is not this server's, or the client named none. Same channel as the protocol-version refusal,
    /// with a reason the client shows in the player's language ("please update").
    /// </summary>
    private bool RefuseContentMismatch(int connectionId, JoinRequest join)
    {
        string theirs = join.ContentFingerprint ?? string.Empty;
        if (theirs.Length > 0 && string.Equals(theirs, _content.BlockFingerprint, StringComparison.Ordinal))
        {
            return false;
        }

        // The fingerprint is client text: stripped and capped before it reaches the log.
        string shown = StripControlChars(theirs.Length > 32 ? theirs.Substring(0, 32) : theirs);
        if (_loggedContentMismatches.Count < MaxLoggedContentMismatches && _loggedContentMismatches.Add(shown))
        {
            _log.Warn($"Join refused: the client's content fingerprint '{(shown.Length > 0 ? shown : "(none)")}' is not this " +
                      $"server's '{_content.BlockFingerprint}' — client and server run different game versions.");
        }

        SendTo(connectionId, new JoinRejected { Reason = Protocol.ContentMismatchReason });
        return true;
    }
}
