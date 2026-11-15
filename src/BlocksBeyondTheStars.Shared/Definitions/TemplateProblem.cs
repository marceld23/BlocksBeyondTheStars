// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Globalization;

namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>
/// One thing a structure-template check found wrong, as a code the editors can translate (#3692). The structure editor used
/// to show the checks' English sentences to children inside a German frame ("Nicht gespeichert: it has no size."). A problem
/// now carries a <see cref="Code"/> — its locale key is <see cref="Key"/> (<c>ui.problem.&lt;code&gt;</c>), whose
/// <c>{0}</c>, <c>{1}</c> … are <see cref="Args"/> — and the very <see cref="English"/> sentence the check always produced: the
/// string-returning checks (the content loader's warnings, the tools, the tests) return that sentence unchanged, so the server
/// and the world generation behave exactly as before.
/// </summary>
public sealed class TemplateProblem
{
    /// <summary>The locale key prefix of every problem code.</summary>
    public const string KeyPrefix = "ui.problem.";

    /// <param name="code">The problem's code (<c>port.on_marker</c>); the locale key is <see cref="KeyPrefix"/> + code.</param>
    /// <param name="english">The English sentence, exactly as the check wrote it before codes existed.</param>
    /// <param name="args">The values the localized sentence names (<c>{0}</c> …): coordinates, keys, numbers — never words to
    /// translate.</param>
    public TemplateProblem(string code, string english, params string[] args)
    {
        Code = code ?? throw new ArgumentNullException(nameof(code));
        English = english ?? string.Empty;
        Args = args ?? Array.Empty<string>();
    }

    public string Code { get; }

    /// <summary>The locale key of the localized sentence.</summary>
    public string Key => KeyPrefix + Code;

    /// <summary>The localized sentence's <c>{0}</c>, <c>{1}</c> … values.</summary>
    public IReadOnlyList<string> Args { get; }

    /// <summary>The check's English sentence (logs, tools, tests).</summary>
    public string English { get; }

    public override string ToString() => English;

    /// <summary>The English sentences of <paramref name="problems"/>, in order — what the string-returning checks return.</summary>
    public static List<string> Sentences(IEnumerable<TemplateProblem> problems)
    {
        var list = new List<string>();
        foreach (var p in problems)
        {
            list.Add(p.English);
        }

        return list;
    }

    /// <summary>A number as a problem argument (invariant digits).</summary>
    public static string Num(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>A cell position as a problem argument: <c>x, y, z</c>.</summary>
    public static string At(int x, int y, int z) => string.Format(CultureInfo.InvariantCulture, "{0}, {1}, {2}", x, y, z);
}
