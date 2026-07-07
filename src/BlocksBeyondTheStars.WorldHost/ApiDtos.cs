// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.WorldHost;

// Request bodies of the WorldHost HTTP API (camelCase on the wire via the web JSON defaults).

/// <summary>Signup body; <paramref name="ClaimCode"/> is only needed (and only checked) when registering
/// a developer-reserved name. <paramref name="AcceptedTermsVersion"/> must carry the CURRENT rules version
/// (the signup UI sends it with its required checkbox) or the signup is refused.</summary>
public sealed record SignupRequest(string Name, string Password, string? ClaimCode = null, int AcceptedTermsVersion = 0);

/// <summary>World creation; <paramref name="Password"/> (optional, #250) protects the world with a join
/// password (4-24 chars, PBKDF2-hashed at rest). Empty/null = open world.</summary>
public sealed record CreateWorldRequest(string Name, string? Password = null);

/// <summary>Join grant request; <paramref name="Password"/> is the world's join password when the world
/// is protected (#250) — omitted on the first try, supplied after the password_required answer.</summary>
public sealed record JoinRequestDto(string PlayerName, string? Password = null);

/// <summary>Owner-only: set/change (4-24 chars) or remove (empty) a world's join password (#250).</summary>
public sealed record WorldPasswordRequest(string? Password);

/// <summary>Owner-only: list (<c>true</c>) or un-list (<c>false</c>) a world in the public browser.
/// Listing requires a join password — public worlds stay password-gated so strangers still need it to join.</summary>
public sealed record WorldVisibilityRequest(bool Public);

/// <summary>Operator maintenance announcement (#249). Kind: 0 = info message, 1 = restart countdown of
/// <paramref name="Seconds"/>, 2 = cancel a scheduled restart. <paramref name="WorldId"/> targets one
/// world; null = the whole fleet.</summary>
public sealed record AnnounceRequest(byte Kind, string? Text = null, int Seconds = -1, string? WorldId = null);

/// <summary>Player report ("Spieler melden"): who misbehaved (in-game name), where, why. Categories:
/// chat, name, griefing, other.</summary>
public sealed record ReportRequest(string ReportedName, string Category, string? Message = null, string? WorldId = null);

public sealed record CloseReportRequest(string Status);

public sealed record BanRequest(string AccountId, bool Banned, string? Reason = null);
