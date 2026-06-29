// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Build
{
    /// <summary>
    /// Optional Glitch title credentials, injected only for local/deployment builds. The committed values are
    /// empty and disabled so public builds never talk to Glitch accidentally.
    ///
    /// A local or CI build may provide the sibling file <c>GlitchIntegrationSecrets.Generated.cs</c>
    /// (git-ignored). The title token ships inside a WebGL player when enabled, so treat it as a
    /// deploy-scoped credential and never commit or log it.
    /// </summary>
    public static partial class GlitchIntegrationSecrets
    {
        public static bool Enabled
        {
            get
            {
                bool enabled = false;
                ApplyEnabled(ref enabled);
                return enabled;
            }
        }

        public static string TitleId
        {
            get
            {
                string value = string.Empty;
                ApplyTitleId(ref value);
                return value;
            }
        }

        public static string TitleToken
        {
            get
            {
                string value = string.Empty;
                ApplyTitleToken(ref value);
                return value;
            }
        }

        public static string DeveloperTestInstallId
        {
            get
            {
                string value = string.Empty;
                ApplyDeveloperTestInstallId(ref value);
                return value;
            }
        }

        public static string ApiBaseUrl
        {
            get
            {
                string value = string.Empty;
                ApplyApiBaseUrl(ref value);
                return value;
            }
        }

        public static string ServerHost
        {
            get
            {
                string value = string.Empty;
                ApplyServerHost(ref value);
                return value;
            }
        }

        public static string ServerPort
        {
            get
            {
                string value = string.Empty;
                ApplyServerPort(ref value);
                return value;
            }
        }

        public static string ServerPassword
        {
            get
            {
                string value = string.Empty;
                ApplyServerPassword(ref value);
                return value;
            }
        }

        static partial void ApplyEnabled(ref bool enabled);
        static partial void ApplyTitleId(ref string value);
        static partial void ApplyTitleToken(ref string value);
        static partial void ApplyDeveloperTestInstallId(ref string value);
        static partial void ApplyApiBaseUrl(ref string value);
        static partial void ApplyServerHost(ref string value);
        static partial void ApplyServerPort(ref string value);
        static partial void ApplyServerPassword(ref string value);
    }
}
