// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace BlocksBeyondTheStars.Client.Feedback
{
    /// <summary>
    /// The JSON payload the in-game "Spieler Feedback" (player feedback) dialog POSTs to the website API
    /// (a Wix/Velo HTTP function). One report covers both bug reports and feature requests — there is no
    /// type distinction; the player just writes a title and a description.
    ///
    /// Property names are serialized as camelCase (see <see cref="FeedbackUploader"/>) so they match the
    /// field names the Wix backend expects: <c>title</c>, <c>description</c>, <c>gameVersion</c>, …
    /// Lives in the Unity-free <c>Client.Core</c> assembly so the exact same builder + uploader runs inside
    /// the Unity player and inside the headless tests (which post it at a local <see cref="System.Net.HttpListener"/>).
    /// </summary>
    public sealed class FeedbackReport
    {
        /// <summary>Short title the player typed (also used as the CMS item title).</summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>The player's free-text description (required; the server rejects empty/too-short text).</summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>Optional contact e-mail. Empty when the player chose not to leave one.</summary>
        public string Email { get; set; } = string.Empty;

        /// <summary>The running game version (<c>Application.version</c>).</summary>
        public string GameVersion { get; set; } = string.Empty;

        /// <summary>Build number / commit, when the build injected one.</summary>
        public string BuildNumber { get; set; } = string.Empty;

        /// <summary>Anonymous per-install id (the client's PlayerToken) — never the OS user or real name.</summary>
        public string PlayerId { get; set; } = string.Empty;

        /// <summary>The in-game player name (shown to other players; not personal data on its own).</summary>
        public string PlayerName { get; set; } = string.Empty;

        /// <summary>A random id for this play session, to group several reports from one sitting.</summary>
        public string SessionId { get; set; } = string.Empty;

        /// <summary>Platform string (e.g. <c>WindowsPlayer</c>).</summary>
        public string Platform { get; set; } = string.Empty;

        /// <summary>ISO-8601 timestamp from the client at the moment of sending.</summary>
        public string ClientTimestamp { get; set; } = string.Empty;

        /// <summary>Free-form diagnostic snapshot the client can see (scene/location, vitals, world seed, …).
        /// Kept deliberately small and client-side; the rich server snapshot is produced separately by the
        /// existing <c>/bump</c> path when the player is on their own/singleplayer server.</summary>
        public Dictionary<string, object> ReportJson { get; set; } = new Dictionary<string, object>();

        /// <summary>The attached screenshot, or null when none was captured / it was too large.</summary>
        public FeedbackScreenshot? Screenshot { get; set; }

        /// <summary>The file name to suggest for the screenshot upload — not serialized into the payload root
        /// (it travels inside <see cref="Screenshot"/>).</summary>
        [JsonIgnore]
        public string ScreenshotFileName { get; set; } = "feedback.jpg";
    }

    /// <summary>The screenshot attachment: base64-encoded JPG bytes plus its file name and mime type.</summary>
    public sealed class FeedbackScreenshot
    {
        public string FileName { get; set; } = "feedback.jpg";
        public string MimeType { get; set; } = "image/jpeg";
        public string Base64 { get; set; } = string.Empty;
    }
}
