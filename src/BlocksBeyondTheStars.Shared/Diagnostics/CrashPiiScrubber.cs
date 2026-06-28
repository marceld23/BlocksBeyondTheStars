// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Text.RegularExpressions;

namespace BlocksBeyondTheStars.Shared.Diagnostics
{
    /// <summary>
    /// Removes the personal data that most commonly leaks into a crash report's free text (exception messages
    /// and stack traces) BEFORE it is written to disk or uploaded — so neither the local file nor the website
    /// carries it. The two reliable, high-signal leaks are redacted: the OS user name embedded in home-folder
    /// paths (<c>C:\Users\Alice\…</c>, <c>/home/alice/…</c>, <c>/Users/alice/…</c>) and e-mail addresses. The
    /// rest of a path is kept (it is useful for debugging and not personal), and only the user segment is
    /// replaced. Shared by the server (<c>CrashReportWriter</c>) and the client (<c>CrashReporter</c>) so the
    /// scrubbing is identical on both. Deterministic + dependency-free, so it is unit-tested directly.
    ///
    /// Note: the in-game player name is carried deliberately as its own report field and is not treated as PII
    /// here; this scrubs the OS account name, which the player never chose to share.
    /// </summary>
    public static class CrashPiiScrubber
    {
        // A short bound so a pathological input can never hang the log/crash path (caller also guards).
        private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(1);

        // ExplicitCapture: only the named "p" (prefix) group captures — the account segment matches but is not
        // captured, which is exactly what we want (it gets replaced, not referenced).

        // C:\Users\<name>\…  — keep the drive + "Users\", redact just the account segment.
        private static readonly Regex WindowsHome = new Regex(
            @"(?<p>[A-Za-z]:\\Users\\)[^\\/\r\n""'<>:|]+",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, MatchTimeout);

        // /home/<name>/… (Linux) and /Users/<name>/… (macOS).
        private static readonly Regex UnixHome = new Regex(
            @"(?<p>/(?:home|Users)/)[^/\r\n""'<>:|]+",
            RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, MatchTimeout);

        private static readonly Regex Email = new Regex(
            @"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}",
            RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, MatchTimeout);

        /// <summary>Returns <paramref name="text"/> with home-folder user names and e-mail addresses redacted.
        /// Null/empty input is returned unchanged. Never throws — on the (pathological) regex-timeout it returns
        /// a fully-redacted marker rather than risk leaking the unscrubbed text.</summary>
        public static string Scrub(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }

            try
            {
                text = WindowsHome.Replace(text, "${p}<user>");
                text = UnixHome.Replace(text, "${p}<user>");
                text = Email.Replace(text, "<email>");
                return text;
            }
            catch (RegexMatchTimeoutException)
            {
                return "<redacted: scrub timeout>";
            }
        }
    }
}
