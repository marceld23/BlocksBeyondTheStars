using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>A point of interest inside a wreck (loot cache, recoverable module, lore terminal).</summary>
public readonly struct WreckMarker
{
    public readonly string Type;       // loot / module / data_terminal
    public readonly Vector3i LocalPos;

    public WreckMarker(string type, Vector3i localPos)
    {
        Type = type;
        LocalPos = localPos;
    }
}

/// <summary>
/// A crashed/abandoned ship wreck: the hull of a known <see cref="ShipDefinition"/> built from
/// blocks, then run through a <b>decay pass</b> that punches breaches and removes a fraction of the
/// hull. The original intact hull is kept as a <b>repair mask</b> so the wreck can later be restored
/// block-by-block into a flyable ship. No live crew — only loot caches, recoverable modules and the
/// odd data/lore terminal.
/// </summary>
public sealed class WreckStructure
{
    public int Width { get; }
    public int Height { get; }
    public int Length { get; }
    public string ShipType { get; }
    public string Origin { get; }     // "human" | "alien"

    private readonly ushort[] _blocks;     // current (decayed) blocks
    private readonly ushort[] _intact;     // the full hull (repair target)
    public IReadOnlyList<WreckMarker> Markers { get; }

    internal WreckStructure(int w, int h, int l, string shipType, string origin,
        ushort[] blocks, ushort[] intact, IReadOnlyList<WreckMarker> markers)
    {
        Width = w;
        Height = h;
        Length = l;
        ShipType = shipType;
        Origin = origin;
        _blocks = blocks;
        _intact = intact;
        Markers = markers;
    }

    public ushort Get(int x, int y, int z) => _blocks[(x * Height + y) * Length + z];

    /// <summary>The block this cell should hold in a fully repaired hull (0 = interior/air).</summary>
    public ushort IntactAt(int x, int y, int z) => _intact[(x * Height + y) * Length + z];

    /// <summary>True if a hull block is missing here (a breach the player must rebuild to repair).</summary>
    public bool IsBreach(int x, int y, int z)
    {
        int i = (x * Height + y) * Length + z;
        return _intact[i] != 0 && _blocks[i] == 0;
    }

    /// <summary>Count of hull blocks still missing (repair progress = 1 − Breaches/IntactHull).</summary>
    public int BreachCount()
    {
        int n = 0;
        for (int i = 0; i < _blocks.Length; i++)
        {
            if (_intact[i] != 0 && _blocks[i] == 0) n++;
        }

        return n;
    }

    public int IntactHullCount()
    {
        int n = 0;
        foreach (var b in _intact)
        {
            if (b != 0) n++;
        }

        return n;
    }

    public bool InBounds(int x, int y, int z)
        => x >= 0 && y >= 0 && z >= 0 && x < Width && y < Height && z < Length;
}

/// <summary>
/// Builds a <see cref="WreckStructure"/> deterministically from a ship design + seed. Stamps the
/// design's hollow hull (iron_wall shell + a glass viewport), records it as the intact repair mask,
/// then decays it: random breaches in the shell + scorch (carbon) flecks. Drops loot/module/lore
/// markers inside. Human wrecks use iron; alien wrecks swap to a crystal-toned hull.
/// </summary>
public static class WreckGenerator
{
    public static WreckStructure Generate(ShipDefinition design, long seed, GameContent content)
    {
        var rng = new System.Random(unchecked((int)(seed ^ (seed >> 32)) ^ (int)WorldGenerator.StableHash(design.Key)));
        string origin = rng.NextDouble() < 0.4 ? "alien" : "human";

        int halfX = System.Math.Max(2, design.InteriorWidth / 2);
        int halfZ = System.Math.Max(2, design.InteriorLength / 2);
        int height = System.Math.Max(3, design.Height);

        int w = halfX * 2 + 1;
        int l = halfZ * 2 + 1;
        int h = height + 1;

        ushort hull = content.GetBlock(origin == "alien" ? "crystal" : "iron_wall")?.NumericId.Value
                      ?? content.GetBlock("iron_wall")?.NumericId.Value ?? 0;
        ushort glass = content.GetBlock("glass")?.NumericId.Value ?? 0;
        ushort scorch = content.GetBlock("carbon")?.NumericId.Value ?? 0;

        var intact = new ushort[w * h * l];

        // 1) Intact hull: a hollow shell with a viewport band (the repair target).
        for (int x = 0; x < w; x++)
        for (int y = 0; y < h; y++)
        for (int z = 0; z < l; z++)
        {
            bool shell = x == 0 || x == w - 1 || y == 0 || y == h - 1 || z == 0 || z == l - 1;
            if (!shell)
            {
                continue;
            }

            bool sideWall = x == 0 || x == w - 1 || z == 0 || z == l - 1;
            bool viewport = sideWall && y == 2 && x > 0 && x < w - 1 && z > 0 && z < l - 1;
            intact[(x * h + y) * l + z] = viewport ? glass : hull;
        }

        // 2) Decay: copy the intact hull, then punch breaches + scorch.
        var blocks = (ushort[])intact.Clone();
        for (int x = 0; x < w; x++)
        for (int y = 0; y < h; y++)
        for (int z = 0; z < l; z++)
        {
            int i = (x * h + y) * l + z;
            if (intact[i] == 0)
            {
                continue;
            }

            double r = rng.NextDouble();
            if (r < 0.30)
            {
                blocks[i] = 0;               // breach
            }
            else if (r < 0.38 && scorch != 0)
            {
                blocks[i] = scorch;          // scorched plating
            }
        }

        // Always tear a big gash in one wall (the crash impact) so the wreck is open to enter.
        int gx = w / 2;
        for (int y = 1; y <= System.Math.Min(3, h - 2); y++)
        for (int dx = -1; dx <= 1; dx++)
        {
            int x = gx + dx;
            if (x > 0 && x < w - 1)
            {
                blocks[(x * h + y) * l + 0] = 0;
            }
        }

        // 3) Markers inside: loot caches, a recoverable module, and (sometimes) a data terminal.
        var markers = new List<WreckMarker>();
        int cx = w / 2, cz = l / 2;
        markers.Add(new WreckMarker("loot", new Vector3i(1, 1, 1)));
        markers.Add(new WreckMarker("loot", new Vector3i(w - 2, 1, l - 2)));
        markers.Add(new WreckMarker("module", new Vector3i(cx, 1, cz)));
        if (rng.NextDouble() < 0.5)
        {
            markers.Add(new WreckMarker("data_terminal", new Vector3i(cx, 1, 1)));
        }

        return new WreckStructure(w, h, l, design.Key, origin, blocks, intact, markers);
    }
}
