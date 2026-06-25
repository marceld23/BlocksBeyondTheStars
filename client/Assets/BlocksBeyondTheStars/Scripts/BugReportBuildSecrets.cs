// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Build
{
    /// <summary>
    /// The website feedback API key, injected at release-build time. This committed file holds an EMPTY
    /// key, so local/dev builds never post to production (<see cref="ApiKey"/> stays "" and the feedback
    /// dialog tells the player it isn't configured).
    ///
    /// A CI release step writes the sibling file <c>BugReportBuildSecrets.Generated.cs</c> (git-ignored)
    /// that implements <see cref="ApplyApiKey"/> with the real key from a GitHub Environment secret. Because
    /// that's a partial method with no body here, a build WITHOUT the generated file compiles fine. The key
    /// is only a spam/abuse gate, not a real secret — it ships inside the client and can be extracted, so the
    /// website endpoint must accept feedback only (see docs/developer/PLAYER_FEEDBACK.md).
    /// </summary>
    public static partial class BugReportBuildSecrets
    {
        /// <summary>The configured feedback API key, or "" when this build wasn't given one.</summary>
        public static string ApiKey
        {
            get
            {
                string key = string.Empty;
                ApplyApiKey(ref key);
                return key;
            }
        }

        /// <summary>Implemented by the CI-generated partial in release builds; a no-op (empty key) otherwise.</summary>
        static partial void ApplyApiKey(ref string key);
    }
}
