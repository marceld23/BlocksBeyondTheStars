// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Globalization;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Turns a template check's <see cref="TemplateProblem"/> into the player's language (#3692): the problem's locale key
    /// (<c>ui.problem.&lt;code&gt;</c>) with its arguments, else — a key the table does not have yet, a translation with a
    /// broken placeholder — the check's English sentence, so a message is never lost. Unity-free so the headless suite pins
    /// that every code the checks can raise has a key.
    /// </summary>
    public static class TemplateProblemText
    {
        /// <summary>The problem in the language <paramref name="localize"/> reads (a lookup that returns the key itself, or
        /// nothing, when it has no text).</summary>
        public static string Format(TemplateProblem? problem, Func<string, string>? localize)
        {
            if (problem is null)
            {
                return string.Empty;
            }

            string? pattern = localize?.Invoke(problem.Key);
            if (string.IsNullOrEmpty(pattern) || pattern == problem.Key)
            {
                return problem.English;
            }

            var args = new object[problem.Args.Count];
            for (int i = 0; i < args.Length; i++)
            {
                args[i] = problem.Args[i] ?? string.Empty;
            }

            try
            {
                return string.Format(CultureInfo.InvariantCulture, pattern, args);
            }
            catch (FormatException)
            {
                return problem.English; // a translation with a broken placeholder still says what is wrong
            }
        }

        /// <summary>The first of <paramref name="problems"/> in the player's language, or empty when there is none.</summary>
        public static string First(IReadOnlyList<TemplateProblem>? problems, Func<string, string>? localize)
            => problems is null || problems.Count == 0 ? string.Empty : Format(problems[0], localize);
    }
}
