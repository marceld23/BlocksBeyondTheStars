// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System.Collections.Generic;
using System.Threading;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Which world a report came from (#4080): the server's world name and the world's seed. Once a new world's seed is
    /// random by default (#4078), the name no longer identifies the generated world and the seed alone does not say
    /// which save it is — triage needs both. The F1 bug report writes the keys from the joined game; the client crash
    /// reporter, which builds its report on whatever thread logged the exception, reads <see cref="Current"/> — an
    /// immutable snapshot set on join and cleared when the world is left.
    /// </summary>
    public sealed class WorldReportInfo
    {
        /// <summary>The report key of the world's name.</summary>
        public const string NameKey = "worldName";

        /// <summary>The report key of the world's seed (the key the F1 report always used).</summary>
        public const string SeedKey = "worldSeed";

        private static WorldReportInfo? s_current;

        public WorldReportInfo(string? name, long seed)
        {
            Name = name ?? string.Empty;
            Seed = seed;
        }

        /// <summary>The server's world name — empty from an older server that does not send it.</summary>
        public string Name { get; }

        /// <summary>The world's seed.</summary>
        public long Seed { get; }

        /// <summary>The world the client is in now, or null in the menu. Safe to read from any thread.</summary>
        public static WorldReportInfo? Current
        {
            get => Volatile.Read(ref s_current);
            set => Volatile.Write(ref s_current, value);
        }

        /// <summary>Adds <see cref="NameKey"/> (when the server named the world) and <see cref="SeedKey"/> to a report's
        /// <c>reportJson</c>.</summary>
        public void WriteTo(IDictionary<string, object> reportJson)
        {
            if (Name.Length > 0)
            {
                reportJson[NameKey] = Name;
            }

            reportJson[SeedKey] = Seed;
        }
    }
}
