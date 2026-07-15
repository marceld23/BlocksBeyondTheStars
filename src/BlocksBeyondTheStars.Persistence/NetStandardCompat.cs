// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
#if !NET10_0_OR_GREATER
// netstandard2.1 flavor (Unity/WebGL in-process server): System.Threading.Lock is .NET 9+; aliasing it
// to object keeps every `lock (_gate)` site identical — plain monitor locks behave the same.
global using Lock = System.Object;

namespace System.Runtime.CompilerServices
{
    /// <summary>C# init-only setter support on pre-.NET-5 targets (compiler marker type).</summary>
    internal static class IsExternalInit
    {
    }
}
#endif
