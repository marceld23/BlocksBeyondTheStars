// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Argument parsing for the admin slash commands typed in chat. Unity-free so the headless test suite
    /// covers it — the Unity side (<c>ChatUi</c>) only recognises the verb and ships the result — the same
    /// split <see cref="Portal.ReportChatCommand"/> uses for <c>/report</c>.
    ///
    /// <para>The one rule here: <b>a player name is the whole rest of the line</b>. Names may contain
    /// spaces ("mincraft Fan"), so reading the single token after the verb truncated the name and the
    /// server answered "target player not found" — a message that looks like the player does not exist
    /// rather than like the name got cut in half (issue #980).</para>
    /// </summary>
    public static class AdminChatCommand
    {
        /// <summary>
        /// The rest of <paramref name="line"/> after the first <paramref name="skipTokens"/> whitespace-
        /// separated tokens, as ONE argument: <c>"/tpp mincraft Fan"</c> with <c>skipTokens: 1</c> yields
        /// <c>mincraft Fan</c>. Surrounding quotes and the <c>@Name</c> habit from other games are stripped,
        /// so <c>/tpp "@mincraft Fan"</c> resolves too. Returns an empty string when nothing follows — the
        /// caller prints its usage line.
        /// </summary>
        /// <param name="line">The raw typed line, verb included.</param>
        /// <param name="skipTokens">How many leading tokens are not part of the name: 1 for
        /// <c>/tpp &lt;name&gt;</c>, 3 for <c>/give &lt;item&gt; &lt;count&gt; &lt;name&gt;</c>.</param>
        public static string PlayerArgument(string? line, int skipTokens = 1)
        {
            string t = line ?? string.Empty;

            // Walk the raw line token by token instead of re-joining the split parts: the rest has to come
            // out exactly as typed (a name may carry any run of spaces), and the caller already split.
            int i = 0;
            for (int n = 0; n < skipTokens; n++)
            {
                while (i < t.Length && char.IsWhiteSpace(t[i]))
                {
                    i++;
                }

                while (i < t.Length && !char.IsWhiteSpace(t[i]))
                {
                    i++;
                }
            }

            return CleanName(t.Substring(Math.Min(i, t.Length)));
        }

        /// <summary>
        /// Splits <c>/silence &lt;name&gt; [minutes]</c> (#1223). The optional minutes sit at the END, which
        /// collides with the rule above — a name is the whole rest of the line — so the tie is broken the
        /// only way that keeps both usable: a trailing token counts as minutes ONLY when it parses as a
        /// number AND something is left in front of it. <c>/silence Player2</c> therefore silences Player2
        /// for the default span, while <c>/silence mincraft Fan 30</c> silences "mincraft Fan" for 30.
        /// Returns 0 minutes when none was given — the server applies its default.
        /// </summary>
        public static (string Name, int Minutes) NameAndMinutes(string? line)
        {
            string rest = PlayerArgument(line);
            int lastSpace = rest.LastIndexOfAny(new[] { ' ', '\t' });
            if (lastSpace <= 0)
            {
                return (rest, 0);
            }

            string tail = rest.Substring(lastSpace + 1);
            if (!int.TryParse(tail, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out int minutes) || minutes <= 0)
            {
                return (rest, 0);
            }

            return (CleanName(rest.Substring(0, lastSpace)), minutes);
        }

        /// <summary>
        /// Splits <c>/tutorial</c> (#2471) into the server's verb line and the optional player — the server case exists since
        /// #2466 (<c>AdminCommandIntent{Command="tutorial", StringArg, TargetPlayer}</c>):
        /// <c>/tutorial skip [player]</c>, <c>/tutorial restart [player]</c>, <c>/tutorial chapter &lt;id&gt; [player]</c> and
        /// <c>/tutorial policy auto|courses|optin|off</c> (the world's rule — never a player). The player is the whole rest of the line
        /// (names may contain spaces, see <see cref="PlayerArgument"/>). An unknown or missing verb is passed on as it is: the
        /// server answers with its usage line (so the help stays in one place).
        /// </summary>
        public static (string Verb, string Player) Tutorial(string? line)
        {
            string t = line ?? string.Empty;
            string[] p = t.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            string verb = p.Length >= 2 ? p[1].ToLowerInvariant() : string.Empty;
            switch (verb)
            {
                case "skip":
                case "restart":
                    return (verb, PlayerArgument(t, 2));
                case "chapter":
                    return p.Length >= 3 ? ("chapter " + p[2], PlayerArgument(t, 3)) : ("chapter", string.Empty);
                case "policy":
                    return p.Length >= 3 ? ("policy " + p[2].ToLowerInvariant(), string.Empty) : ("policy", string.Empty);
                default:
                    return (verb, string.Empty);
            }
        }

        /// <summary>Trims a typed name down to what the server stores: no surrounding whitespace, no quotes
        /// around it, no leading <c>@</c>. Order matters — <c>"@mincraft Fan"</c> has to lose the quotes
        /// before the <c>@</c> is even reachable.</summary>
        public static string CleanName(string? name)
            => (name ?? string.Empty).Trim().Trim('"').Trim().TrimStart('@').Trim();
    }
}
