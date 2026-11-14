// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Globalization;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// The grid of a kit settlement (#1876), decided once from the kit (Marcel: "the kit shapes the grid too") and pinned
/// in the placement record as a string, so a replay lays the same grid even after the kit changed or vanished.
/// </summary>
public readonly struct SettlementLayoutSpec
{
    public readonly int Cols, Rows, Plot, Building, Storeys;
    public readonly bool ModulesOnly;

    /// <summary>The composer revision the grid was laid with (#1886), pinned with it: 0 = #1876 (six fields), 1 = the
    /// central plaza only where it does not cut into a building. Only a fresh layout takes the current revision.</summary>
    public readonly int Revision;

    public const int CurrentRevision = 1;

    public SettlementLayoutSpec(int cols, int rows, int plot, int building, int storeys, bool modulesOnly, int revision = 0)
    {
        Cols = cols;
        Rows = rows;
        Plot = plot;
        Building = building;
        Storeys = storeys;
        ModulesOnly = modulesOnly;
        Revision = revision;
    }

    /// <summary>Draws the grid a kit allows: cols/rows inside the kit's ranges (the tier's procedural default when
    /// unset), stride, envelope and storeys as the kit says (0 = default).</summary>
    public static SettlementLayoutSpec FromKit(StructureKit kit, string tier, System.Random rng)
    {
        var (baseCols, baseRows, _) = SettlementGenerator.Layout(tier);
        int colsMin = kit.ColsMin > 0 ? kit.ColsMin : baseCols;
        int colsMax = kit.ColsMax > 0 ? kit.ColsMax : baseCols + 1;
        int rowsMin = kit.RowsMin > 0 ? kit.RowsMin : baseRows;
        int rowsMax = kit.RowsMax > 0 ? kit.RowsMax : baseRows + 1;
        int cols = rng.Next(System.Math.Min(colsMin, colsMax), System.Math.Max(colsMin, colsMax) + 1);
        int rows = rng.Next(System.Math.Min(rowsMin, rowsMax), System.Math.Max(rowsMin, rowsMax) + 1);
        int plot = kit.PlotStride > 0 ? System.Math.Clamp(kit.PlotStride, 6, 32) : SettlementGenerator.Plot;
        int building = kit.Building > 0 ? System.Math.Clamp(kit.Building, 4, plot - 2) : System.Math.Min(6, plot - 2);
        return new SettlementLayoutSpec(System.Math.Clamp(cols, 1, 12), System.Math.Clamp(rows, 1, 12), plot, building, kit.Storeys, kit.ModulesOnly, CurrentRevision);
    }

    public string Serialize() => Revision == 0
        ? string.Join(",", Cols, Rows, Plot, Building, Storeys, ModulesOnly ? 1 : 0)
        : string.Join(",", Cols, Rows, Plot, Building, Storeys, ModulesOnly ? 1 : 0, Revision);

    public static bool TryParse(string? text, out SettlementLayoutSpec spec)
    {
        spec = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var parts = text!.Split(',');
        if (parts.Length != 6 && parts.Length != 7)
        {
            return false;
        }

        var n = new int[7];
        for (int i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out n[i]))
            {
                return false;
            }
        }

        spec = new SettlementLayoutSpec(n[0], n[1], n[2], n[3], n[4], n[5] != 0, n[6]);
        return true;
    }
}

/// <summary>The grid of a kit city (#1876): districts per side, district size, street width, height, and an optional
/// district map — pinned like the settlement grid.</summary>
public readonly struct CityLayoutSpec
{
    public readonly int Grid, DistrictSize, Street, Height;
    public readonly IReadOnlyList<string> RoleMap;

    /// <summary>#3599: the city that grew — every other district row shifted by half a district, the wall along the outline of
    /// the districts, a suburb grown outside the gates from <see cref="SuburbKit"/>. False for every city before it.</summary>
    public readonly bool Grown;

    /// <summary>#3599: the settlement kit a grown city's suburb grows from ("" = none).</summary>
    public readonly string SuburbKit;

    /// <summary>How far outside the wall a grown city's suburb reaches.</summary>
    public const int SuburbDepth = 36;

    public CityLayoutSpec(int grid, int districtSize, int street, int height, IReadOnlyList<string>? roleMap, bool grown = false, string suburbKit = "")
    {
        Grid = grid;
        DistrictSize = districtSize;
        Street = street;
        Height = height;
        RoleMap = roleMap ?? System.Array.Empty<string>();
        Grown = grown;
        SuburbKit = suburbKit ?? string.Empty;
    }

    public static CityLayoutSpec Default => new(CityGenerator.Modules, CityGenerator.ModuleSize, CityGenerator.Street, CityGenerator.Height, null);

    public static CityLayoutSpec FromKit(StructureKit kit)
    {
        int grid = kit.Grid > 0 ? System.Math.Clamp(kit.Grid | 1, 3, 9) : CityGenerator.Modules; // odd, so a centre exists
        int size = kit.DistrictSize > 0 ? System.Math.Clamp(kit.DistrictSize, 16, 48) : CityGenerator.ModuleSize;
        int street = kit.Street > 0 ? System.Math.Clamp(kit.Street, 2, 8) : CityGenerator.Street;
        int height = kit.Height > 0 ? System.Math.Clamp(kit.Height, 12, 40) : CityGenerator.Height;
        return new CityLayoutSpec(grid, size, street, height, kit.RoleMap.Count > 0 ? new List<string>(kit.RoleMap) : null,
            kit.Grown, kit.SuburbKit);
    }

    /// <summary>The districts and their streets (the core a classic city is).</summary>
    public int Core => Grid * DistrictSize + (Grid + 1) * Street;

    /// <summary>Half a district and its street: how far a grown city shifts every other row.</summary>
    public int Shift => (DistrictSize + Street) / 2;

    /// <summary>A grown city's margin round its core on every side: the shift and the suburb (0 for a classic city).</summary>
    public int Margin => Grown ? Shift + SuburbDepth : 0;

    public int Footprint => Core + (2 * Margin);

    /// <summary>The west / north corner of district (<paramref name="gx"/>, <paramref name="gz"/>): the classic grid, or — for a
    /// grown city — inside its margin, every row whose distance from the middle row is odd shifted east by <see cref="Shift"/>.</summary>
    public (int X, int Z) DistrictOrigin(int gx, int gz)
    {
        var (x, z) = CityGenerator.ModuleOriginFor(gx, gz, DistrictSize, Street);
        return Grown ? (x + Margin + RowShift(gz), z + Margin) : (x, z);
    }

    /// <summary>How far a row is shifted east (#3599).</summary>
    public int RowShift(int gz) => Grown && ((gz - (Grid / 2)) & 1) != 0 ? Shift : 0;

    public string Serialize() => Grown
        ? string.Join(",", Grid, DistrictSize, Street, Height, string.Join("|", RoleMap), "grown:" + SuburbKit)
        : string.Join(",", Grid, DistrictSize, Street, Height, string.Join("|", RoleMap));

    public static bool TryParse(string? text, out CityLayoutSpec spec)
    {
        spec = Default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var parts = text!.Split(',');
        bool grown = parts.Length == 6 && parts[5].StartsWith("grown:", System.StringComparison.Ordinal);
        if (parts.Length != 5 && !grown)
        {
            return false;
        }

        var n = new int[4];
        for (int i = 0; i < 4; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out n[i]))
            {
                return false;
            }
        }

        var map = parts[4].Length == 0 ? null : new List<string>(parts[4].Split('|'));
        spec = new CityLayoutSpec(n[0], n[1], n[2], n[3], map, grown, grown ? parts[5].Substring("grown:".Length) : string.Empty);
        return true;
    }
}
