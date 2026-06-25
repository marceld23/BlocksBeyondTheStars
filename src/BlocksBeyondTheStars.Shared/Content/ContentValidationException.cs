// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Shared.Content;

/// <summary>Thrown when loaded content definitions fail cross-reference validation.</summary>
public sealed class ContentValidationException : Exception
{
    public IReadOnlyList<string> Problems { get; }

    public ContentValidationException(IReadOnlyList<string> problems)
        : base("Content validation failed:" + Environment.NewLine + string.Join(Environment.NewLine, problems))
    {
        Problems = problems;
    }
}
