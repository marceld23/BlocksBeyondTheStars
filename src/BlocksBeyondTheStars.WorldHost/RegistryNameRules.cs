// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

namespace BlocksBeyondTheStars.WorldHost;

/// <summary>
/// The registry's name rules: the developer-reserved names and the kid-facing blocked/watch word screen. Shared by
/// <see cref="AccountStore"/> (account names) and <see cref="WorldStore"/> (world names) and exposed through
/// <see cref="HostRegistry"/>, which carries the API docs.
/// </summary>
internal sealed class RegistryNameRules
{
    private readonly WorldHostConfig _config;
    private readonly Shared.Moderation.NameScreen _nameScreen;

    public RegistryNameRules(WorldHostConfig config)
    {
        _config = config;
        _nameScreen = new Shared.Moderation.NameScreen(config.BlockedNameWords, config.WatchNameWords);
    }

    /// <inheritdoc cref="HostRegistry.IsReservedName"/>
    public bool IsReservedName(string? name)
    {
        string normalized = NormalizeName(name);
        return normalized.Length > 0 && _config.ReservedNames.Any(r => NormalizeName(r) == normalized);
    }

    private static string NormalizeName(string? name)
        => new((name ?? string.Empty).ToLowerInvariant().Where(c => c is not (' ' or '-' or '_')).ToArray());

    /// <inheritdoc cref="HostRegistry.IsBlockedName"/>
    public bool IsBlockedName(string? name) => _nameScreen.IsBlocked(name);

    /// <inheritdoc cref="HostRegistry.ScreenName"/>
    public Shared.Moderation.NameScreenResult ScreenName(string? name) => _nameScreen.Screen(name);
}
