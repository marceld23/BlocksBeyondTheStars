// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>One building of a <see cref="SettlementPlan"/>: which module stands where, on which floor row, turned how.</summary>
public sealed class PlanLot
{
    /// <summary>The module's template key ("" for a lot the baker leaves empty).</summary>
    public string Module { get; set; } = string.Empty;

    /// <summary>The module's function (house, market, field, …) — what the lot is for.</summary>
    public string Function { get; set; } = string.Empty;

    /// <summary>The lot's west / north corner in the plan's box.</summary>
    public int X { get; set; }

    public int Z { get; set; }

    /// <summary>The lot's floor row (structure-local; the module's row 0 stands here).</summary>
    public int Y { get; set; }

    /// <summary>Quarter turns the module is turned by (<see cref="TemplateTransform.RotateY"/>), 0..3.</summary>
    public int Turns { get; set; }

    /// <summary>The footprint after turning.</summary>
    public int W { get; set; }

    public int L { get; set; }

    /// <summary>Whether a column lies on the lot.</summary>
    public bool Covers(int x, int z) => x >= X && x < X + W && z >= Z && z < Z + L;
}

/// <summary>
/// A planned settlement (#3584, terrain generation 41): the layout a grown or grown-grid planner made for one site — the
/// buildings (<see cref="Lots"/>) and, per column of the box, what the column is (unclaimed, yard, footpath, lane, main
/// road, plaza, a lot, a green) and its floor row. Pinned in the placement record (<see cref="Serialize"/>), so a replay bakes
/// exactly what was stamped without reading the terrain or growing the village again (<see cref="SettlementPlanBaker"/>).
/// </summary>
public sealed class SettlementPlan
{
    /// <summary>The plan format a fresh plan is written with.</summary>
    public const int CurrentRevision = 1;

    // --- column kinds ---
    public const byte Unclaimed = 0;
    public const byte Yard = 1;
    public const byte Footpath = 2;
    public const byte Lane = 3;
    public const byte MainRoad = 4;
    public const byte Plaza = 5;
    public const byte LotColumn = 6;
    public const byte Green = 7;

    private const string Prefix = "plan";

    public SettlementPlan(int width, int length)
    {
        Width = System.Math.Max(1, width);
        Length = System.Math.Max(1, length);
        Kinds = new byte[Width * Length];
        Heights = new byte[Width * Length];
    }

    public int Revision { get; set; } = CurrentRevision;

    /// <summary><c>grid</c> or <c>grown</c> (<see cref="Shared.Definitions.StructureKit.Layout"/>).</summary>
    public string Layout { get; set; } = string.Empty;

    /// <summary>The village form a grown plan took ("" for a grid).</summary>
    public string Form { get; set; } = string.Empty;

    public int Width { get; }

    public int Length { get; }

    /// <summary>The meeting place's centre column (the plaza, or the middle of a village green) — the festival gathers here.</summary>
    public int PlazaX { get; set; }

    public int PlazaZ { get; set; }

    public List<PlanLot> Lots { get; } = new();

    /// <summary>The world height of floor row 0 when the plan was planned into a site's ground (#3589) — the structure's
    /// ground row; null for a plan on flat ground. Not part of the pinned text (the record keeps the ground row).</summary>
    public int? GroundBase { get; set; }

    /// <summary>The column kinds, <c>x * Length + z</c>.</summary>
    public byte[] Kinds { get; }

    /// <summary>The floor rows, <c>x * Length + z</c>.</summary>
    public byte[] Heights { get; }

    public bool InBox(int x, int z) => x >= 0 && z >= 0 && x < Width && z < Length;

    public byte KindAt(int x, int z) => InBox(x, z) ? Kinds[(x * Length) + z] : Unclaimed;

    public int HeightAt(int x, int z) => InBox(x, z) ? Heights[(x * Length) + z] : 0;

    public void Set(int x, int z, byte kind, int height)
    {
        if (!InBox(x, z))
        {
            return;
        }

        Kinds[(x * Length) + z] = kind;
        Heights[(x * Length) + z] = (byte)System.Math.Clamp(height, 0, 255);
    }

    public void SetKind(int x, int z, byte kind)
    {
        if (InBox(x, z))
        {
            Kinds[(x * Length) + z] = kind;
        }
    }

    public void SetHeight(int x, int z, int height)
    {
        if (InBox(x, z))
        {
            Heights[(x * Length) + z] = (byte)System.Math.Clamp(height, 0, 255);
        }
    }

    /// <summary>A road a person walks on: a footpath, a lane, the main road or the plaza (#3590's "walkable").</summary>
    public static bool IsWalkable(byte kind) => kind is Footpath or Lane or MainRoad or Plaza;

    /// <summary>A road the planners grow buildings along: a lane, the main road or the plaza.</summary>
    public static bool IsRoad(byte kind) => kind is Lane or MainRoad or Plaza;

    /// <summary>The highest floor row of any column.</summary>
    public int MaxHeight()
    {
        int max = 0;
        for (int i = 0; i < Heights.Length; i++)
        {
            if (Kinds[i] != Unclaimed && Heights[i] > max)
            {
                max = Heights[i];
            }
        }

        return max;
    }

    /// <summary>The plan's text form: <c>plan|rev|layout|form|w|l|px|pz|lots|columns</c> — a lot is
    /// <c>module,function,x,z,y,turns,w,l</c> (lots joined by <c>;</c>), the columns are the kinds then the heights, deflated
    /// and base64-encoded.</summary>
    public string Serialize()
    {
        var lots = new StringBuilder();
        foreach (var lot in Lots)
        {
            if (lots.Length > 0)
            {
                lots.Append(';');
            }

            lots.Append(lot.Module).Append(',').Append(lot.Function);
            foreach (int n in new[] { lot.X, lot.Z, lot.Y, lot.Turns, lot.W, lot.L })
            {
                lots.Append(',').Append(n.ToString(CultureInfo.InvariantCulture));
            }
        }

        return string.Join("|", Prefix, Revision.ToString(CultureInfo.InvariantCulture), Layout, Form,
            Width.ToString(CultureInfo.InvariantCulture), Length.ToString(CultureInfo.InvariantCulture),
            PlazaX.ToString(CultureInfo.InvariantCulture), PlazaZ.ToString(CultureInfo.InvariantCulture), lots.ToString(), PackColumns());
    }

    /// <summary>Reads <see cref="Serialize"/>'s text; false (and no plan) for anything malformed.</summary>
    public static bool TryParse(string? text, out SettlementPlan plan)
    {
        plan = null!;
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        var parts = text!.Split('|');
        if (parts.Length != 10 || parts[0] != Prefix || !TryInt(parts[1], out int rev) || !TryInt(parts[4], out int w)
            || !TryInt(parts[5], out int l) || w <= 0 || l <= 0 || !TryInt(parts[6], out int px) || !TryInt(parts[7], out int pz))
        {
            return false;
        }

        var parsed = new SettlementPlan(w, l) { Revision = rev, Layout = parts[2], Form = parts[3], PlazaX = px, PlazaZ = pz };
        if (!ReadLots(parts[8], parsed.Lots) || !parsed.UnpackColumns(parts[9]))
        {
            return false;
        }

        plan = parsed;
        return true;
    }

    private static bool ReadLots(string text, List<PlanLot> lots)
    {
        if (text.Length == 0)
        {
            return true;
        }

        foreach (var entry in text.Split(';'))
        {
            var f = entry.Split(',');
            if (f.Length != 8)
            {
                return false;
            }

            var n = new int[6];
            for (int i = 0; i < 6; i++)
            {
                if (!TryInt(f[i + 2], out n[i]))
                {
                    return false;
                }
            }

            lots.Add(new PlanLot { Module = f[0], Function = f[1], X = n[0], Z = n[1], Y = n[2], Turns = n[3] & 3, W = n[4], L = n[5] });
        }

        return true;
    }

    private string PackColumns()
    {
        using var packed = new MemoryStream();
        using (var deflate = new DeflateStream(packed, CompressionLevel.Optimal, leaveOpen: true))
        {
            deflate.Write(Kinds, 0, Kinds.Length);
            deflate.Write(Heights, 0, Heights.Length);
        }

        return System.Convert.ToBase64String(packed.ToArray());
    }

    private bool UnpackColumns(string text)
    {
        try
        {
            using var source = new MemoryStream(System.Convert.FromBase64String(text));
            using var inflate = new DeflateStream(source, CompressionMode.Decompress);
            return ReadFully(inflate, Kinds) && ReadFully(inflate, Heights);
        }
        catch (System.FormatException)
        {
            return false;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    private static bool ReadFully(Stream s, byte[] target)
    {
        int read = 0;
        while (read < target.Length)
        {
            int n = s.Read(target, read, target.Length - read);
            if (n <= 0)
            {
                return false;
            }

            read += n;
        }

        return true;
    }

    private static bool TryInt(string s, out int value)
        => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
}
