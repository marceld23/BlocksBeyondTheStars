// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
// Polyfill so that `init`-only setters and `record` types compile on netstandard2.1.
// This type is required by the compiler but does not exist in the netstandard2.1 BCL.
// It is linked into the other netstandard2.1 projects (WorldGeneration, Networking).

namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit
    {
    }
}
