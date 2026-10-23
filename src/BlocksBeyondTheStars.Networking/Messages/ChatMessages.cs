// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Networking.Messages;

/// <summary>A player chat line (client→server). Requires a comm radio; range/rate enforced server-side.</summary>
public sealed class ChatIntent
{
    public string Text { get; set; } = string.Empty;
}

/// <summary>A <c>/bump</c> bug report (client→server): a free-text description plus an optional in-game
/// screenshot (JPG bytes). The server persists a rich JSON snapshot of the reporter's situation and writes
/// the screenshot alongside it. Works without a comm radio; intercepted before the chat gate.</summary>
public sealed class BumpReport
{
    public string Description { get; set; } = string.Empty;

    /// <summary>JPG-encoded screenshot, or empty when the client could not capture one (the server then
    /// falls back to a snapshot without an image).</summary>
    public byte[] Image { get; set; } = System.Array.Empty<byte>();

    /// <summary>The reporting client's build version (<c>Application.version</c>). The server stamps this
    /// onto the forwarded report's <c>gameVersion</c> so the inbox shows the player's build, not the
    /// server's. Empty from older clients (and the text-only <c>/bump</c>), in which case the server falls
    /// back to its own version. MessagePack is contractless, so this extra field is backward compatible.</summary>
    public string ClientVersion { get; set; } = string.Empty;

    /// <summary>The reporter's reply-thread credential (#1359): the same one-way hash of the install secret
    /// the client sends on its direct F1 upload (<c>Shared/Feedback/FeedbackReplyKey</c>). The server forwards
    /// it verbatim so the inbox can pair the two rows of one report and route an answer on either row to the
    /// player; the server never sees the secret itself. Empty from older clients and the text-only
    /// <c>/bump</c>. MessagePack is contractless, so the extra field is backward compatible.</summary>
    public string ReplyKey { get; set; } = string.Empty;
}

/// <summary>A broadcast chat line (server→clients).</summary>
public sealed class ChatMessage
{
    public string Sender { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;

    /// <summary>True for a server-originated NPC radio call (#1158) — the client mirrors these into the
    /// Missions tab's "Recent radio calls" block. Additive contractless field: old clients ignore it.</summary>
    public bool IsNpcCall { get; set; }

    /// <summary>The sender's stable player id (#1209). <see cref="Sender"/> is a DISPLAY name, which is
    /// what a person reads but not what a mute list should key on. Additive and contractless: an older
    /// server simply leaves it empty and the client falls back to matching the name.</summary>
    public string SenderId { get; set; } = string.Empty;
}

/// <summary>One compressed voice frame, used both ways: client→server (the speaker's microphone, ~20 ms of
/// Opus) and server→clients (the server relays the opaque bytes to the speaker's tiered radio audience and
/// stamps <see cref="FromPlayerId"/>). The server never decodes the payload — it is a thin relay. Sent
/// <see cref="Transport.DeliveryMode.Unreliable"/>: a dropped frame is inaudible and beats waiting for a resend.</summary>
public sealed class VoiceFrame
{
    /// <summary>The speaking player's id. Left empty by the client; the server fills it in on relay so the
    /// recipient knows which avatar/voice the frame belongs to (and so a client can't spoof another sender).</summary>
    public string FromPlayerId { get; set; } = string.Empty;

    /// <summary>Opus-encoded mono audio (one frame). Opaque to the server.</summary>
    public byte[] Opus { get; set; } = System.Array.Empty<byte>();

    /// <summary>Monotonic per-speaker frame counter, so the receiver's jitter buffer can order frames and
    /// detect gaps without decoding the payload.</summary>
    public int Sequence { get; set; }
}
