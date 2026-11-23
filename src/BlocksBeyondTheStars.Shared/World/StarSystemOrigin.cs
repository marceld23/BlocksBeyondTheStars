// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Globalization;

namespace BlocksBeyondTheStars.Shared.World;

/// <summary>
/// Where a star system comes from (#3969). Set where the system is created and, like the rest of the galaxy, never
/// persisted: every start rebuilds the galaxy from the seed and re-appends the hand-made systems.
/// </summary>
public enum StarSystemOrigin
{
    /// <summary>Rolled by <c>UniverseGenerator</c> from the seed and its index (<c>sys{i}</c>) — also a grown system.</summary>
    Rolled,

    /// <summary>A fixed system from <c>data/systems/&lt;key&gt;.json</c> (Sol).</summary>
    Fixed,

    /// <summary>A story pack's system (#3357, the Sounders' Ornemis) — a fixed system with one body.</summary>
    Pack,

    /// <summary>The story's Guardian finale system.</summary>
    Finale,
}

/// <summary>
/// The id namespace of the rolled systems (#3969): <c>UniverseGenerator</c> names system i <c>sys{i}</c>, and every id
/// beginning with <c>sys</c> is reserved for it — a pack or a data file may never use one. The one place that reads a
/// system id as "rolled, index i"; code holding the <see cref="StarSystem"/> asks <see cref="StarSystem.IsRolled"/>.
/// </summary>
public static class StarSystemIds
{
    /// <summary>The prefix of every rolled system id (and of its bodies' ids).</summary>
    public const string RolledPrefix = "sys";

    /// <summary>The id of rolled system <paramref name="index"/>.</summary>
    public static string Rolled(int index) => RolledPrefix + index.ToString(CultureInfo.InvariantCulture);

    /// <summary>Whether <paramref name="id"/> lies in the rolled namespace at all (<c>sys…</c>) — the reservation check of a
    /// hand-made system's id.</summary>
    public static bool HasRolledPrefix(string? id)
        => id is not null && id.StartsWith(RolledPrefix, System.StringComparison.Ordinal);

    /// <summary>The index <c>i</c> of a rolled system id <c>sys{i}</c>, or -1 for any other id.</summary>
    public static int RolledIndex(string? systemId)
    {
        if (systemId is null || systemId.Length <= RolledPrefix.Length || !HasRolledPrefix(systemId))
        {
            return -1;
        }

        return int.TryParse(systemId.Substring(RolledPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out int i) ? i : -1;
    }

    /// <summary>Whether <paramref name="systemId"/> names a rolled system (<c>sys{i}</c>).</summary>
    public static bool IsRolled(string? systemId) => RolledIndex(systemId) >= 0;

    /// <summary>Whether <paramref name="systemId"/> names one of the systems a world starts with (index below
    /// <paramref name="startCount"/>) — not a grown one, not a hand-made one.</summary>
    public static bool IsStartRolled(string? systemId, int startCount)
    {
        int index = RolledIndex(systemId);
        return index >= 0 && index < startCount;
    }
}
