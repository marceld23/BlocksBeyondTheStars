// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// #3487: the SHARED RUIN PASS — an intact structure in, a ruin out. Until now every ruin in the game had a decay of
/// its own that nothing else could call (the fallen settlements, the monuments, the buried G.D.S. ruin, the abandoned
/// rail station); this one takes any authored structure.
/// <para>Every choice is a stateless function of the seed, a lane and the cell (hashes and value noise), so the same
/// seed always gives the same ruin, two instances never look alike, and no step moves another. The steps, in order:</para>
/// <list type="number">
/// <item><b>Crumbled patches</b> — in coherent patches of columns everything above a cut height falls (a tower loses
/// one side of its top, a wall its crown).</item>
/// <item><b>Erosion</b> — bites out of walls, more of them the higher up; the base course stays.</item>
/// <item><b>Collapse spots</b> — holes through roofs and floors.</item>
/// <item><b>Openings</b> — glass is smashed, lights are dead.</item>
/// <item><b>Weathering</b> and <b>scorch marks</b> (#3515, only when the options ask for them).</item>
/// <item><b>Cave-ins</b> (below the ground row) — stretches of a tunnel or a cavern fill with rubble.</item>
/// <item><b>Settle</b> — whatever no longer hangs together with the ground comes down.</item>
/// <item><b>Rubble and overgrowth</b> on the ground where something fell.</item>
/// </list>
/// <para><b>What stays:</b> cells the author marked <i>keep</i> are never touched and hold what hangs on them; ladders
/// and shaped pieces (stairs, slabs, furniture) are never eroded, but they fall with the wall that carried them. Rows
/// at and below the ground row are anchored in the rock. A marker's own cell is never filled; when its floor falls,
/// the marker comes down with it.</para>
/// <para><b>Profiles</b> (<see cref="LandmarkRules.AllProfiles"/>): <i>masonry</i> as above; <i>lattice</i> without
/// crumbled patches and erosion bites, its collapse spots are balls cut out of the frame; <i>underground</i> adds the
/// cave-ins; <i>hull</i> (#3515, a wreck) has no ground at all — no crumbled patches, no height grading, no rubble, no
/// flora: erosion bites and collapse balls breach it anywhere, and what settles is what no longer hangs on a kept cell, a
/// protected row or the largest piece that is left.</para>
/// <para><b>Revisions</b>: <see cref="Revision"/> 1 is the pass as it shipped with the landmarks. A placement pins the
/// revision it was stamped with and replays through it, so a later change of the default output becomes revision 2 and
/// never moves a marker of a structure that already stands. <see cref="RuinOptions"/> (#3515) change what a caller
/// builds; a fresh set of options is the revision's own output.</para>
/// </summary>
internal sealed partial class StructureRuiner
{
    /// <summary>The newest revision of the pass. Pinned with a placement (#3486): a stamped ruin is re-derived with the
    /// revision it was stamped with, so a later change here never moves a marker of a landmark that already stands.</summary>
    public const int Revision = 1;

    private const long LaneCrumble = 0x11, LaneJitter = 0x12, LaneBite = 0x13, LaneSpeckle = 0x14, LaneSpot = 0x15,
        LaneSpotFall = 0x16, LaneGlass = 0x17, LaneLight = 0x18, LaneCaveIn = 0x19, LaneCaveFill = 0x1A, LaneRubble = 0x1B,
        LaneFlora = 0x1C;

    /// <summary>#3515: the lanes of what the options add — each step its own, so none of them moves another (and none
    /// moves the default pass, which never draws on them). A weathering rule k draws on <c>LaneWeather + k</c>.</summary>
    private const long LaneRubblePick = 0x1D, LaneSpare = 0x1E, LaneScorch = 0x1F, LaneWeather = 0x100;

    /// <summary>The height fraction a hull erodes with everywhere: it has no top and no bottom.</summary>
    private const double HullFrac = 0.5;

    private readonly VoxelGrid _g;
    private readonly GameContent _content;
    private readonly long _seed;
    private readonly string _profile;
    private readonly bool _hull;
    private readonly double _s;
    private readonly int _ground; // -1 for a hull: no ground
    private readonly int _above;  // rows above the ground row
    private readonly int _floor;  // the lowest row erosion and collapse balls reach
    private readonly Tuning _t;
    private readonly BitArray _markerCell;
    private readonly BitArray? _breach;     // cells the decay took out — only tracked for scorch marks
    private readonly int[] _fallen;         // per column: cells that came down
    private readonly ushort[] _fallenBlock; // per column: what came down (the rubble's material)
    private readonly ushort _rock;
    private readonly Dictionary<ushort, int> _kind = new(); // block id → 1 glass, 2 light, 3 ladder, 0 other
    private readonly Dictionary<ushort, ushort> _lightOff = new();

    private StructureRuiner(VoxelGrid grid, GameContent content, long seed, string profile, double strength, int groundRow,
        IReadOnlyList<Vector3i> markerCells, string surfaceBlock, RuinOptions options)
    {
        _g = grid;
        _content = content;
        _seed = seed;
        _profile = profile;
        _hull = profile == LandmarkRules.ProfileHull;
        _s = strength <= 0 ? DefaultStrength(profile) : System.Math.Min(1.0, strength);
        _ground = _hull ? -1 : System.Math.Clamp(groundRow, 0, grid.H - 1);
        _above = System.Math.Max(1, grid.H - 1 - _ground);
        _floor = _hull ? 0 : _ground + 2;
        _markerCell = new BitArray(grid.W * grid.H * grid.L);
        _fallen = new int[grid.W * grid.L];
        _fallenBlock = new ushort[grid.W * grid.L];
        _rock = content.GetBlock("stone")?.NumericId.Value ?? 0;
        _t = Tuning.From(options, content, surfaceBlock);
        _breach = _t.Scorch is null ? null : new BitArray(grid.W * grid.H * grid.L);
        ProtectMarkers(markerCells);
    }

    /// <summary>The strength a profile decays with when the template names none.</summary>
    public static double DefaultStrength(string profile) => profile == LandmarkRules.ProfileLattice ? 0.4 : 0.5;

    /// <summary>Runs the newest revision with the default options over <paramref name="grid"/> in place.</summary>
    public static void Run(VoxelGrid grid, GameContent content, long seed, string profile, double strength, int groundRow,
        IReadOnlyList<SettlementMarker> markers, string surfaceBlock)
        => Run(grid, content, seed, profile, strength, groundRow, markers, surfaceBlock, Revision, null);

    /// <summary>Runs revision <paramref name="revision"/> with <paramref name="options"/> (null = the defaults) over
    /// <paramref name="grid"/> in place; the markers' cells stay free.</summary>
    public static void Run(VoxelGrid grid, GameContent content, long seed, string profile, double strength, int groundRow,
        IReadOnlyList<SettlementMarker> markers, string surfaceBlock, int revision, RuinOptions? options)
    {
        var cells = new Vector3i[markers.Count];
        for (int i = 0; i < cells.Length; i++)
        {
            cells[i] = markers[i].LocalPos;
        }

        Run(grid, content, seed, profile, strength, groundRow, cells, surfaceBlock, revision, options);
    }

    /// <summary>
    /// Runs revision <paramref name="revision"/> with <paramref name="options"/> (null = the defaults) over
    /// <paramref name="grid"/> in place. Nothing is written into a cell of <paramref name="markerCells"/> or the one above
    /// it. A revision this build does not know (a record of a newer build) runs the newest one it knows.
    /// </summary>
    public static void Run(VoxelGrid grid, GameContent content, long seed, string profile, double strength, int groundRow,
        IReadOnlyList<Vector3i> markerCells, string surfaceBlock, int revision, RuinOptions? options)
    {
        var r = new StructureRuiner(grid, content, seed, profile, strength, groundRow, markerCells, surfaceBlock,
            options ?? new RuinOptions());
        switch (revision)
        {
            default:
                r.RunRevision1();
                break;
        }
    }

    /// <summary>Revision 1 — the pass as the landmarks shipped with it (#3487), plus the option steps of #3515, which
    /// draw nothing unless asked for.</summary>
    private void RunRevision1()
    {
        if (!_hull && _profile != LandmarkRules.ProfileLattice)
        {
            Crumble();
        }

        Erode();
        CollapseSpots();
        Openings();
        Weather();
        ScorchMarks();
        CaveIns();
        Settle();
        if (!_hull)
        {
            Rubble();
            Overgrowth();
        }
    }

    // ---------------------------------------------------------------- helpers

    private double U(long lane, int x, int y, int z) => Noise.Value01(_seed + lane, x, y, z);

    private int Column(int x, int z) => (x * _g.L) + z;

    /// <summary>The strength at a column: the pass's strength times the options' factor there (clamped to 0..1).</summary>
    private double S(int x, int z) => _t.StrengthAt is null ? _s : System.Math.Clamp(_s * _t.StrengthAt(x, z), 0.0, 1.0);

    /// <summary>Whether a spot collapse or a cave-in spares the cell: with the chance a strength factor below 1 leaves.
    /// Never without a strength map (and then nothing is drawn).</summary>
    private bool Spared(int x, int y, int z)
    {
        if (_t.StrengthAt is null)
        {
            return false;
        }

        double f = _t.StrengthAt(x, z);
        return f < 1.0 && U(LaneSpare, x, y, z) >= f;
    }

    /// <summary>Whether the options protect the row (<see cref="RuinOptions.ProtectRowsUpTo"/>).</summary>
    private bool Protected(int y) => y <= _t.ProtectRows;

    /// <summary>1 = glass, 2 = light, 3 = ladder, 0 = anything else — by the block's key and category, cached per id.</summary>
    private int KindOf(ushort id)
    {
        if (_kind.TryGetValue(id, out int k))
        {
            return k;
        }

        var def = _content.BlockById(new Shared.Primitives.BlockId(id));
        k = def is null ? 0
            : def.Key.Contains("glass", System.StringComparison.Ordinal) ? 1
            : def.Category == "light" ? 2
            : def.Key == "ladder" ? 3
            : 0;
        _kind[id] = k;
        if (k == 2 && def is not null)
        {
            _lightOff[id] = _t.LightOff != 0 ? _t.LightOff : _content.GetBlock(def.Key + "_off")?.NumericId.Value ?? 0;
        }

        return k;
    }

    /// <summary>Nothing is ever written into a marker's cell or the one above it (a chest, a den's animals and a visitor
    /// need the room). The floor under a marker is NOT kept: when it falls, the marker comes down with it
    /// (<see cref="RuinMarkerRules.Seat(VoxelGrid, List{SettlementMarker}, int)"/> seats every surviving marker on what
    /// is left).</summary>
    private void ProtectMarkers(IReadOnlyList<Vector3i> cells)
    {
        foreach (var p in cells)
        {
            for (int dy = 0; dy <= 1; dy++)
            {
                if (_g.In(p.X, p.Y + dy, p.Z))
                {
                    _markerCell[_g.Index(p.X, p.Y + dy, p.Z)] = true;
                }
            }
        }
    }

    /// <summary>Whether the decay may take the cell at all: never a kept cell, never anything at or below the ground row
    /// or in a protected row.</summary>
    private bool Takeable(int x, int y, int z) => y > _ground && !Protected(y) && !_g.Keep[_g.Index(x, y, z)];

    /// <summary>Whether erosion may bite the cell: takeable, and neither a ladder nor (unless the options say so) a
    /// shaped piece.</summary>
    private bool Erodible(int x, int y, int z, ushort block)
        => Takeable(x, y, z) && (_t.ErodeShaped || _g.ShapeAt(x, y, z) == 0) && KindOf(block) != 3;

    /// <summary>Removes a cell and remembers that it fell in its column.</summary>
    private void Fell(int x, int y, int z)
    {
        ushort b = _g.Get(x, y, z);
        if (b == 0)
        {
            return;
        }

        int c = Column(x, z);
        _fallen[c]++;
        if (KindOf(b) == 0 && _g.ShapeAt(x, y, z) == 0)
        {
            _fallenBlock[c] = b;
        }

        if (_breach is not null)
        {
            _breach[_g.Index(x, y, z)] = true;
        }

        _g.Clear(x, y, z);
    }

    // ---------------------------------------------------------------- 1. crumbled patches

    /// <summary>In coherent patches of columns everything above a cut height falls. The patches come from 2-D value
    /// noise, so a tower loses one flank of its top instead of single stones.</summary>
    private void Crumble()
    {
        double scale = System.Math.Clamp(System.Math.Min(_g.W, _g.L) / 6.0, 4.0, 12.0);
        for (int x = 0; x < _g.W; x++)
        {
            for (int z = 0; z < _g.L; z++)
            {
                double s = S(x, z);
                double threshold = 0.72 - (0.25 * s);
                double n = Noise.Value3D(_seed + LaneCrumble, x / scale, 0.5, z / scale);
                if (n <= threshold)
                {
                    continue;
                }

                double depth = System.Math.Min(1.0, (n - threshold) / (1.0 - threshold) * (0.9 + (1.2 * s)));
                int jitter = (int)(U(LaneJitter, x, 0, z) * 3) - 1;
                int cut = _ground + 2 + (int)(_above * System.Math.Clamp(1.0 - depth, 0.12, 1.0)) + jitter;
                for (int y = System.Math.Max(cut, _ground + 2); y < _g.H; y++)
                {
                    if (_g.Solid(x, y, z) && Takeable(x, y, z))
                    {
                        Fell(x, y, z);
                    }
                }
            }
        }
    }

    // ---------------------------------------------------------------- 2. erosion

    /// <summary>Bites out of the walls (3-D value noise, so a hole is a hole and not a speckle) plus a light speckle,
    /// both rising with height. The first course above the ground stays, so the footprint reads. A hull erodes alike
    /// at every height.</summary>
    private void Erode()
    {
        bool lattice = _profile == LandmarkRules.ProfileLattice;
        for (int x = 0; x < _g.W; x++)
        {
            for (int y = _floor; y < _g.H; y++)
            {
                double frac = _hull ? HullFrac : (double)(y - _ground) / _above;
                for (int z = 0; z < _g.L; z++)
                {
                    ushort b = _g.Blocks[_g.Index(x, y, z)];
                    if (b == 0 || !Erodible(x, y, z, b))
                    {
                        continue;
                    }

                    double s = S(x, z);
                    double biteAt = lattice ? 2.0 : 0.86 - (s * (0.10 + (0.22 * frac)));
                    double speckle = s * (lattice ? 0.03 : 0.04) * (0.3 + frac);
                    if (Noise.Value3D(_seed + LaneBite, x / 4.0, y / 4.0, z / 4.0) > biteAt || U(LaneSpeckle, x, y, z) < speckle)
                    {
                        Fell(x, y, z);
                    }
                }
            }
        }
    }

    // ---------------------------------------------------------------- 3. collapse spots

    /// <summary>A few holes: a ragged vertical shaft through roofs and floors (masonry) or a ball cut out of the frame
    /// (lattice — the settle step then brings down the span that hung on it; a hull — a breach at any height).</summary>
    private void CollapseSpots()
    {
        bool lattice = _profile == LandmarkRules.ProfileLattice;
        double area = _g.W * (double)_g.L / 4096.0;
        int spots = System.Math.Clamp((int)System.Math.Round(1 + ((1 + (5 * _s)) * area * (lattice ? 2 : 1))), 1, 14);
        int maxR = System.Math.Clamp(System.Math.Min(_g.W, _g.L) / 12, 1, 6);
        for (int i = 0; i < spots; i++)
        {
            int r = 2 + (int)(U(LaneSpot, i, 1, 0) * (maxR + 1));
            int cx = (int)(U(LaneSpot, i, 2, 0) * _g.W);
            int cz = (int)(U(LaneSpot, i, 3, 0) * _g.L);
            if (_hull)
            {
                Ball(cx, (int)(U(LaneSpot, i, 4, 0) * _g.H), cz, r);
                continue;
            }

            int y0 = _ground + 2 + (int)(U(LaneSpot, i, 4, 0) * _above * (lattice ? 0.9 : 0.6));
            if (lattice)
            {
                Ball(cx, y0, cz, r + 1);
            }
            else
            {
                Shaft(cx, y0, cz, r);
            }
        }
    }

    private void Shaft(int cx, int y0, int cz, int r)
    {
        for (int x = cx - r; x <= cx + r; x++)
        {
            for (int z = cz - r; z <= cz + r; z++)
            {
                if (!_g.In(x, 0, z) || ((x - cx) * (x - cx)) + ((z - cz) * (z - cz)) > r * r)
                {
                    continue;
                }

                for (int y = y0; y < _g.H; y++)
                {
                    if (_g.Solid(x, y, z) && Takeable(x, y, z) && U(LaneSpotFall, x, y, z) < 0.85 && !Spared(x, y, z))
                    {
                        Fell(x, y, z);
                    }
                }
            }
        }
    }

    private void Ball(int cx, int cy, int cz, int r)
    {
        for (int x = cx - r; x <= cx + r; x++)
        {
            for (int y = System.Math.Max(_floor, cy - r); y <= cy + r; y++)
            {
                for (int z = cz - r; z <= cz + r; z++)
                {
                    int d2 = ((x - cx) * (x - cx)) + ((y - cy) * (y - cy)) + ((z - cz) * (z - cz));
                    if (d2 <= r * r && _g.Solid(x, y, z) && Takeable(x, y, z) && !Spared(x, y, z))
                    {
                        Fell(x, y, z);
                    }
                }
            }
        }
    }

    // ---------------------------------------------------------------- 4. openings

    /// <summary>Glass is smashed and lights are dead — everywhere, the cellar and the tunnels included (not in a kept
    /// cell or a protected row). A light becomes its switched-off twin (<c>&lt;key&gt;_off</c>) where the content has
    /// one, else it is gone — or the options' fixed switched-off light.</summary>
    private void Openings()
    {
        for (int x = 0; x < _g.W; x++)
        {
            for (int y = 0; y < _g.H; y++)
            {
                if (Protected(y))
                {
                    continue;
                }

                for (int z = 0; z < _g.L; z++)
                {
                    int i = _g.Index(x, y, z);
                    ushort b = _g.Blocks[i];
                    if (b == 0 || _g.Keep[i])
                    {
                        continue;
                    }

                    int kind = KindOf(b);
                    if (kind == 1 && U(LaneGlass, x, y, z) < 0.3 + (0.5 * S(x, z)))
                    {
                        _g.Clear(x, y, z);
                    }
                    else if (kind == 2 && U(LaneLight, x, y, z) < 0.5 + (0.4 * S(x, z)))
                    {
                        ushort off = _lightOff[b];
                        _g.Set(x, y, z, off, shape: off != 0 ? _g.ShapeAt(x, y, z) : 0);
                    }
                }
            }
        }
    }
}
