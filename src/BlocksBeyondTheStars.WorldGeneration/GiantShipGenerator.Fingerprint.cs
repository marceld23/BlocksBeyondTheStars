// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Globalization;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>The content fingerprint of a giant ship (#3609).</summary>
public static partial class GiantShipGenerator
{
    /// <summary>
    /// A stable hash of everything that shapes the wreck (16 hex digits): the dimensions, the ground row, the ruin strength,
    /// the style, the ruin zones, the compact body (palette and rows) and every listed cell with its settings — not the name,
    /// weight, pack or planet types. The same file gives the same fingerprint on every platform and build (FNV-1a over the
    /// values, the strengths by their bits, then the SplitMix64 finaliser), so a record can pin it and notice a changed design.
    /// </summary>
    public static string Fingerprint(StructureTemplate t)
    {
        var h = new Fnv();
        h.Add("giantship-fingerprint/1");
        h.Add(t.Width).Add(t.Height).Add(t.Length).Add(t.GroundRow).Add(t.RuinStrength).Add(t.Style);
        foreach (var z in t.RuinZones ?? new System.Collections.Generic.List<RuinZone>())
        {
            if (z is not null)
            {
                h.Add("zone").Add(z.X0).Add(z.Y0).Add(z.Z0).Add(z.X1).Add(z.Y1).Add(z.Z1).Add(z.Profile).Add(z.Strength);
            }
        }

        foreach (var p in t.Palette)
        {
            h.Add("pal").Add(p.Kind).Add(p.Id).Add(p.Tint).Add(p.Glow).Add(p.Shape).Add(p.Keep ? 1 : 0);
        }

        foreach (string row in t.Rows)
        {
            h.Add("row").Add(row);
        }

        foreach (var c in t.Cells)
        {
            h.Add("cell").Add(c.X).Add(c.Y).Add(c.Z).Add(c.Kind).Add(c.Id).Add(c.Tint).Add(c.Glow).Add(c.Shape).Add(c.Keep ? 1 : 0)
                .Add(c.Port).Add(c.Mode).Add(c.Config).Add(c.Label);
        }

        return StableHashing.Mix64(h.Value).ToString("x16", CultureInfo.InvariantCulture);
    }

    /// <summary>Running 64-bit FNV-1a over strings (char by char, each closed by a separator) and integers.</summary>
    private sealed class Fnv
    {
        public ulong Value { get; private set; } = 1469598103934665603UL;

        public Fnv Add(string? s)
        {
            foreach (char c in s ?? string.Empty)
            {
                Mix(c);
            }

            Mix(0xFFFF); // the separator, so "ab"+"c" and "a"+"bc" differ
            return this;
        }

        public Fnv Add(int v) => Add((long)v);

        public Fnv Add(double v) => Add(System.BitConverter.DoubleToInt64Bits(v));

        public Fnv Add(long v)
        {
            for (int i = 0; i < 8; i++)
            {
                Mix((ulong)((v >> (8 * i)) & 0xFF));
            }

            return this;
        }

        private void Mix(ulong b) => Value = unchecked((Value ^ b) * 1099511628211UL);
    }
}
