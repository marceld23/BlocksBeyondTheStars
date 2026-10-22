// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

namespace BlocksBeyondTheStars.WorldHost;

public sealed record AccountRecord(
    string Id,
    string Name,
    bool IsDeveloper = false,
    bool IsBanned = false,
    string BanReason = "",
    int AcceptedTermsVersion = 0,
    long BannedAtUnix = 0,
    long BannedUntilUnix = 0,
    string BanReasonCode = "",
    bool MustChangePassword = false)
{
    /// <summary>True for a ban that ends by itself (a timeout); false for "until an operator lifts it".</summary>
    public bool BanExpires => IsBanned && BannedUntilUnix > 0;
}

/// <summary>
/// A message waiting for a player: why they were banned, that a ban was lifted, that one of their worlds
/// was deleted. Bans could be re-derived from the account row, but a deleted world leaves nothing behind
/// to derive anything from — the notice IS the record, written at the moment of the action.
/// </summary>
public sealed record NoticeRecord(
    long Id,
    string Kind,
    string Subject,
    string Reason,
    string ReasonCode,
    long UntilUnix,
    long CreatedUnix,
    long SeenUnix)
{
    public const string KindBanned = "banned";
    public const string KindUnbanned = "unbanned";
    public const string KindWorldDeleted = "world_deleted";
}

/// <summary>A player barred from ONE world by its owner (the lever a world owner actually needs — the
/// fleet-wide ban is the operator's). Keyed on the account when there is one; arcade guests have none,
/// so the player name is matched too.</summary>
public sealed record WorldBanRecord(
    long Id,
    string WorldId,
    string AccountId,
    string PlayerName,
    string Reason,
    long CreatedUnix);

/// <summary>Who played on a world under which in-game name — written at the join grant. Exists so the
/// owner's ban UI can offer a pick list (nobody remembers account ids) and so a fleet ban knows which
/// in-game names to kick.</summary>
public sealed record WorldVisitorRecord(
    string WorldId,
    string AccountId,
    string PlayerName,
    long FirstSeenUnix,
    long LastSeenUnix);

public sealed record WorldRecord(
    string Id,
    string OwnerAccountId,
    string DisplayName,
    string JoinSecret,
    int HostPort,
    string Status,
    string ContainerId,
    long CreatedUnix,
    long LastStartedUnix,
    string PasswordHash = "",
    bool IsPublic = false,
    string Channel = WorldChannel.Portal,
    string SaveTooNewImage = "",
    long SaveTooNewUnix = 0)
{
    /// <summary>The public routing label: <c>w-&lt;id&gt;.&lt;BaseDomain&gt;</c> resolves to this world's instance.</summary>
    public string Subdomain => "w-" + Id;

    /// <summary>True when the creator protected this world with a join password (#250).</summary>
    public bool HasPassword => PasswordHash.Length > 0;

    /// <summary>True when <paramref name="serverImage"/> already refused this world's save as written by a newer
    /// build (#2464: the instance exited with code 3, <see cref="SaveTooNewImage"/> names the image). Such a world
    /// needs a newer server; a different image (a fleet update) may open it, so the mark no longer applies.</summary>
    public bool NeedsNewerServerThan(string serverImage)
        => SaveTooNewImage.Length > 0 && string.Equals(SaveTooNewImage, serverImage, StringComparison.Ordinal);
}

/// <summary>Which storefront a world belongs to. Portal worlds ('') follow the Baumhaus rules
/// (player-created, password-gated, listed on the portal); glitch worlds exist ONLY for the
/// glitch.fun arcade — they never appear in the public browser or any account's world list, and are
/// joinable solely through the glitch session gateway's tokens.</summary>
public static class WorldChannel
{
    public const string Portal = "";
    public const string Glitch = "glitch";
}

/// <summary>A glitch.fun visitor seen by the session gateway — the admin UI's ban targets. The
/// install id is Glitch's pseudonymous per-player UUID; no further identity is stored.</summary>
public sealed record GlitchGuestRecord(
    string InstallId,
    string PlayerName,
    long FirstSeenUnix,
    long LastSeenUnix,
    long Sessions);

/// <summary>An install-id ban for the glitch.fun arcade (accounts don't exist on that channel, so
/// bans key on Glitch's install id instead).</summary>
public sealed record GlitchBanRecord(
    string InstallId,
    string PlayerName,
    string Reason,
    long CreatedUnix);

/// <summary>A filed player report awaiting (or after) operator review.</summary>
public sealed record ReportRecord(
    long Id,
    string WorldId,
    string ReporterAccountId,
    string ReportedName,
    string Category,
    string Message,
    string Status,
    long CreatedUnix);

/// <summary>World lifecycle states tracked in the registry.</summary>
public static class WorldStatus
{
    public const string Stopped = "stopped";
    public const string Starting = "starting";
    public const string Running = "running";

    /// <summary>Long-inactive: saves moved to the archive folder, instance claim ended. A join
    /// transparently restores + wakes the world (it just takes a moment longer).</summary>
    public const string Archived = "archived";
}

/// <summary>Registry gauges for the /metrics scrape.</summary>
public sealed record RegistryCounts(
    long Accounts,
    long OpenReports,
    IReadOnlyList<(string Status, long Count)> WorldsByStatus);
