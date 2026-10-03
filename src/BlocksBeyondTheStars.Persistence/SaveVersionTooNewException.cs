// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Persistence;

/// <summary>
/// The world was written by a newer build than this one (#2223): its stored
/// <see cref="Shared.State.WorldMetadata.SaveVersion"/> is higher than
/// <see cref="Shared.State.WorldMetadata.CurrentSaveVersion"/>. The server refuses to open it and leaves it
/// untouched — opening it would rewrite it for good (block keys this build does not know become air on the
/// first load). Its own type so a host can tell "update the game" apart from a corrupted save or a server that
/// could not start: the console host exits with its own code, the in-process (browser) host can catch it.
/// </summary>
public sealed class SaveVersionTooNewException : Exception
{
    /// <summary>The save version stored in the world.</summary>
    public int SaveVersion { get; }

    /// <summary>The highest save version this build opens.</summary>
    public int SupportedVersion { get; }

    public SaveVersionTooNewException()
        : this("The world was saved by a newer version of the game.")
    {
    }

    public SaveVersionTooNewException(string message)
        : base(message)
    {
    }

    public SaveVersionTooNewException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public SaveVersionTooNewException(string worldName, int saveVersion, int supportedVersion)
        : base($"World '{worldName}' was saved by a newer version of the game (save version {saveVersion}; this " +
               $"version opens up to {supportedVersion}). It was left untouched — update the game to open it.")
    {
        SaveVersion = saveVersion;
        SupportedVersion = supportedVersion;
    }
}
