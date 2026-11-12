// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Globalization;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration.Sources;

/// <summary>The resolved parameter values of one build (every declared parameter, normalized or at its default).</summary>
internal sealed class SourceParams
{
    private readonly Dictionary<string, string> _values;

    public SourceParams(Dictionary<string, string> values) => _values = values;

    public string Get(string key) => _values.TryGetValue(key, out string? v) ? v : string.Empty;

    public int Int(string key) => int.TryParse(Get(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : 0;

    public bool Bool(string key) => Get(key) == "true";

    /// <summary>An <c>auto</c>/<c>no</c>/<c>yes</c> choice: null for auto (the generator or the seed decides).</summary>
    public bool? Tri(string key) => Get(key) switch
    {
        SourceOptions.Yes => true,
        SourceOptions.No => false,
        _ => null,
    };
}

/// <summary>Option values several sources share.</summary>
internal static class SourceOptions
{
    /// <summary>The generator (its seed, the template) decides.</summary>
    public const string Auto = "auto";

    public const string No = "no";

    public const string Yes = "yes";

    public static readonly string[] Tri = { Auto, No, Yes };

    /// <summary>The keys of <paramref name="kits"/> behind an empty option (no kit: the procedural composer).</summary>
    public static List<string> KitKeys(IEnumerable<StructureKit> kits, bool withNone)
    {
        var keys = new List<string>();
        if (withNone)
        {
            keys.Add(string.Empty);
        }

        foreach (var k in kits)
        {
            keys.Add(k.Key);
        }

        return keys;
    }
}

/// <summary>The shared part of every wrapper: key, kind, label, the parameter resolution and the build entry.</summary>
internal abstract class StructureSourceBase : IStructureSource
{
    private static readonly IReadOnlyList<StructureParam> NoParams = Array.Empty<StructureParam>();

    protected StructureSourceBase(string key, StructureSourceKind kind, bool supportsIntact = false)
    {
        Key = key;
        Kind = kind;
        SupportsIntact = supportsIntact;
    }

    public string Key { get; }

    public StructureSourceKind Kind { get; }

    public string LabelKey => StructureSources.LabelKeyPrefix + Key;

    public bool SupportsIntact { get; }

    public virtual IReadOnlyList<StructureParam> GetParams(GameContent content) => NoParams;

    public BuiltStructure Build(StructureBuildRequest request)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        return BuildCore(request, new SourceParams(StructureSources.Resolve(this, request.Content, request.Params)));
    }

    protected abstract BuiltStructure BuildCore(StructureBuildRequest request, SourceParams p);

    /// <summary>The server's per-instance rng (<c>new Random(seed ^ seed &gt;&gt; 32)</c>).</summary>
    internal static System.Random InstanceRng(long seed) => new(unchecked((int)(seed ^ (seed >> 32))));

    /// <summary>The server's rng lane per instance seed (<c>GameServer.RngFor</c>).</summary>
    internal static System.Random LaneRng(long instSeed, string lane)
    {
        long s = instSeed ^ WorldGenerator.StableHash(lane);
        return new System.Random(unchecked((int)(s ^ (s >> 32))));
    }

    /// <summary>The planet type of a request, or null without one.</summary>
    internal static string? PlanetKey(StructureBuildRequest r) => string.IsNullOrEmpty(r.PlanetType) ? null : r.PlanetType;
}

/// <summary>What the server passes the composers on a fresh instance of a world with default options.</summary>
internal static class DefaultWorld
{
    /// <summary>The settlement/city module pool (<c>SettlementModulesFor</c> with the default packs and the planet type).</summary>
    public static IReadOnlyList<StructureTemplate> SettlementModules(GameContent content, string? planetType)
        => content.SettlementModulesFor(new WorldDescription().EnabledStructurePacks, planetType);

    /// <summary>The per-plot module chance: the default <c>SettlementTemplateUse</c>, 0 without modules.</summary>
    public static double ModuleChance(int moduleCount)
        => moduleCount == 0 ? 0.0 : new WorldDescription().SettlementTemplateUse.Probability();

    /// <summary>The kit module pool: every settlement module, from every pack (a kit names its modules by key).</summary>
    public static List<StructureTemplate> KitModulePool(GameContent content)
    {
        var pool = new List<StructureTemplate>();
        foreach (var t in content.SettlementTemplates)
        {
            if (t.IsModule)
            {
                pool.Add(t);
            }
        }

        return pool;
    }
}

/// <summary>Turns the generators' own result types into a <see cref="BuiltStructure"/> through their public accessors —
/// cell by cell, marker by marker, nothing added or dropped.</summary>
internal static class BuiltConvert
{
    /// <summary>Copies a <c>w × h × l</c> grid into <paramref name="b"/>, its row 0 at row <paramref name="yShift"/>.</summary>
    public static void CopyGrid(BuiltStructure b, int w, int h, int l, int yShift, Func<int, int, int, ushort> get,
        Func<int, int, int, (int Tint, int Glow)>? mod = null, Func<int, int, int, int>? shape = null)
    {
        for (int x = 0; x < w; x++)
        {
            for (int y = 0; y < h; y++)
            {
                for (int z = 0; z < l; z++)
                {
                    var (tint, glow) = mod?.Invoke(x, y, z) ?? (0, 0);
                    b.Set(x, y + yShift, z, get(x, y, z), tint, glow, shape?.Invoke(x, y, z) ?? 0);
                }
            }
        }
    }

    public static void AddMarkers(BuiltStructure b, IEnumerable<SettlementMarker> markers, int dx = 0, int dy = 0, int dz = 0)
    {
        foreach (var m in markers)
        {
            b.Markers.Add(new BuiltMarker(m.Type, m.LocalPos.X + dx, m.LocalPos.Y + dy, m.LocalPos.Z + dz, m.Data, m.DoorAxis));
        }
    }

    /// <summary>A settlement-shaped structure (every surface generator but the station and wreck families).</summary>
    public static BuiltStructure FromSettlement(string key, SettlementStructure s)
    {
        var b = new BuiltStructure(key, s.Width, s.Height, s.Length);
        CopyGrid(b, s.Width, s.Height, s.Length, 0, s.Get, s.GetModifier, s.GetShape);
        AddMarkers(b, s.Markers);
        return b;
    }

    public static BuiltStructure FromStation(string key, StationStructure s)
    {
        var b = new BuiltStructure(key, s.Width, s.Height, s.Length);
        CopyGrid(b, s.Width, s.Height, s.Length, 0, s.Get, s.GetModifier, s.GetShape);
        foreach (var m in s.Markers)
        {
            b.Markers.Add(new BuiltMarker(m.Type, m.LocalPos.X, m.LocalPos.Y, m.LocalPos.Z, m.Data));
        }

        return b;
    }
}
