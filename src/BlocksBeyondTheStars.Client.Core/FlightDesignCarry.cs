// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;

namespace BlocksBeyondTheStars.Client.Core;

/// <summary>
/// #3409: which voxel designs survive the flight view's in-flight rebuild (#1677). A jump made in flight arrives as one
/// batch — the old flight's SpaceClosed, the new SpaceState and then every design of the new system (stations, the wreck,
/// asteroids, debris) — and the view only notices the instance switch on its next frame. Its teardown used to drop every
/// design, the new system's included: stations fell back to the old placeholder model, the wreck to a brown box and the
/// asteroids vanished while the server still had them. A design remembers the instance it arrived for; the designs of the
/// new instance are carried over the rebuild, everything else goes.
/// </summary>
public static class FlightDesignCarry
{
    /// <summary>The ids in <paramref name="designs"/> whose arrival instance (<paramref name="instanceOf"/>) is
    /// <paramref name="instance"/> — the designs that belong to the arrived flight. None for an empty instance.</summary>
    public static IReadOnlyList<string> ArrivedFor<T>(IReadOnlyDictionary<string, T> designs, Func<T, string> instanceOf, string? instance)
    {
        if (designs is null || instanceOf is null || string.IsNullOrEmpty(instance))
        {
            return Array.Empty<string>();
        }

        return designs.Where(kv => string.Equals(instanceOf(kv.Value), instance, StringComparison.Ordinal))
            .Select(kv => kv.Key)
            .ToList();
    }
}
