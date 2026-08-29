// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Security.Cryptography;
using System.Text;

namespace BlocksBeyondTheStars.Shared.Feedback
{
    /// <summary>
    /// The "reply key" that lets a game client pull developer answers to its own F1 reports from the
    /// report inbox (issues #1327/#1328). It is derived one-way from a per-install secret — the client's
    /// name-claim token on desktop / play.*, the Glitch install id in the arcade — so the inbox can hand
    /// out replies to exactly the install that sent the report WITHOUT that secret ever being reused as a
    /// credential: whoever learns a reply key can read replies, not claim a player name anywhere.
    /// Shared between the client (which sends it with every report) and the ReportHost (which back-fills
    /// it from the stored <c>playerId</c> of older reports with the same formula, so those become
    /// answerable too). Lowercase hex SHA-256, 64 characters.
    /// </summary>
    public static class FeedbackReplyKey
    {
        /// <summary>Domain separator so the key can never collide with any other hash of the same secret.</summary>
        private const string Prefix = "bbs-reply:";

        /// <summary>Length of a well-formed key (SHA-256 as lowercase hex).</summary>
        public const int Length = 64;

        /// <summary>Derives the reply key for <paramref name="secret"/>; empty when the secret is empty.</summary>
        public static string Derive(string? secret)
        {
            if (string.IsNullOrEmpty(secret))
            {
                return string.Empty;
            }

            using var sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(Prefix + secret));
            var sb = new StringBuilder(hash.Length * 2);
            foreach (byte b in hash)
            {
                sb.Append(b.ToString("x2"));
            }

            return sb.ToString();
        }

        /// <summary>True for a syntactically valid key (64 lowercase hex chars) — the inbox ignores anything else.</summary>
        public static bool IsWellFormed(string? key)
        {
            if (key == null || key.Length != Length)
            {
                return false;
            }

            foreach (char c in key)
            {
                bool hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f');
                if (!hex)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
