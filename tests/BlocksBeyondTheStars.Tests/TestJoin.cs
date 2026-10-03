// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Content;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// What a hand-built <c>JoinRequest</c> must carry to get past the join gate (#2222): the fingerprint of the block
/// palette the server was built with. Every server test loads the shipped content from
/// <see cref="TestPaths.DataDir"/>, so one value serves them all:
/// <code>new JoinRequest { ContentFingerprint = TestJoin.Fingerprint, PlayerName = "Pilot" }</code>
/// A test whose server runs on other content passes that content's <see cref="GameContent.BlockFingerprint"/>.
/// </summary>
public static class TestJoin
{
    private static readonly Lazy<string> Shipped =
        new(() => ContentLoader.LoadFromDirectory(TestPaths.DataDir()).BlockFingerprint);

    /// <summary>The block fingerprint of the shipped content.</summary>
    public static string Fingerprint => Shipped.Value;
}
