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

/// <summary>Operator ban/unban. <paramref name="Days"/> 0 means "until an operator lifts it"; anything
/// greater is a timeout that ends by itself. <paramref name="ReasonCode"/> is one of the canned reasons
/// (chat/griefing/cheating/other) the client can show in the player's language; <paramref name="Reason"/>
/// is the operator's own words and is shown as written.</summary>
public sealed record BanRequest(string AccountId, bool Banned, string? Reason = null, string? ReasonCode = null, int Days = 0);

/// <summary>Acknowledges a player notice; id &lt;= 0 acknowledges all of them.</summary>
public sealed record NoticeAckRequest(long Id = 0);

/// <summary>Rotates the caller's account password. The current password must be presented even though
/// the caller is already signed in — a stolen session must not be enough to take the account over.</summary>
public sealed record ChangePasswordRequest(string OldPassword, string NewPassword);

/// <summary>Self-service password reset with a rescue code ("Rettungscode") — the anonymous flow for a
/// FORGOTTEN password: account name + one unused code + the new password.</summary>
public sealed record RecoverRequest(string Name, string Code, string NewPassword);

/// <summary>Re-issues the caller's rescue codes; the current password is required for the same reason
/// the change-password endpoint requires it (a stolen session must not mint recovery secrets).</summary>
public sealed record RecoveryCodesRequest(string Password);

/// <summary>Operator password reset (scriptable twin of the /admin form): answers a one-time temp
/// password and flags the account to change it at the next sign-in.</summary>
public sealed record AdminResetPasswordRequest(string AccountId);

/// <summary>Owner-only: bar a player from ONE world (#497). Either identifier may be empty as long as one
/// is given; <paramref name="Kick"/> also ends a session already in progress.</summary>
public sealed record WorldBanRequest(string? PlayerName = null, string? AccountId = null, string? Reason = null, bool Kick = true);

/// <summary>Owner-only: end one player's session on this world right now, without a lasting block.</summary>
public sealed record WorldKickRequest(string PlayerName, string? Reason = null);

/// <summary>glitch.fun arcade session grant: the install id Glitch injected into the build's URL, plus
/// an optional preferred display name (the gateway falls back to the Glitch account name).</summary>
public sealed record GlitchSessionRequest(string InstallId, string? PlayerName = null);

/// <summary>glitch.fun heartbeat relay body — mirrors Glitch's install/heartbeat contract so the title
/// token can stay server-side (the client never talks to api.glitch.fun directly).</summary>
public sealed record GlitchHeartbeatRequest(string InstallId, string? SessionId = null, string? Platform = null, string? GameVersion = null);

/// <summary>Browser-singleplayer cloud-save upload (relayed to Glitch Cloud Save slot 0): the gzip'd
/// world snapshot as base64 plus the last cloud version the client synced from (0 = new slot) —
/// Glitch's optimistic concurrency; a stale base version answers 409 with the conflict ids.</summary>
public sealed record GlitchSaveStoreRequest(string InstallId, string Payload, int BaseVersion = 0);

/// <summary>Explicit cloud-save conflict resolution (Glitch's 409 flow): keep_server discards the
/// local state, use_client overwrites the cloud with it.</summary>
public sealed record GlitchSaveResolveRequest(string InstallId, string SaveId, string ConflictId, string Choice);
