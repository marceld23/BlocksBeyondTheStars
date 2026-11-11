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
/// <item><b>Cave-ins</b> (below the ground row) — stretches of a tunnel or a cavern fill with rubble.</item>
/// <item><b>Settle</b> — whatever no longer hangs together with the ground comes down.</item>
/// <item><b>Rubble and overgrowth</b> on the ground where something fell.</item>
/// </list>
/// <para><b>What stays:</b> cells the author marked <i>keep</i> are never touched and hold what hangs on them; ladders
/// and shaped pieces (stairs, slabs, furniture) are never eroded, but they fall with the wall that carried them. Rows
/// at and below the ground row are anchored in the rock. A marker's own cell is never filled; when its floor falls,
/// the marker comes down with it.</para>
/// </summary>
internal sealed partial class StructureRuiner
{
    /// <summary>The revision of the pass. Pinned with a placement (#3486): a stamped ruin is re-derived with the revision
    /// it was stamped with, so a later change here never moves a marker of a landmark that already stands.</summary>
    public const int Revision = 1;

    private const long LaneCrumble = 0x11, LaneJitter = 0x12, LaneBite = 0x13, LaneSpeckle = 0x14, LaneSpot = 0x15,
        LaneSpotFall = 0x16, LaneGlass = 0x17, LaneLight = 0x18, LaneCaveIn = 0x19, LaneCaveFill = 0x1A, LaneRubble = 0x1B,
        LaneFlora = 0x1C;

    private readonly VoxelGrid _g;
    private readonly GameContent _content;
    private readonly long _seed;
    private readonly string _profile;
    private readonly double _s;
    private readonly int _ground;
    private readonly int _above; // rows above the ground row
    private readonly BitArray _markerCell;
    private readonly int[] _fallen;         // per column: cells that came down
    private readonly ushort[] _fallenBlock; // per column: what came down (the rubble's material)
    private readonly ushort _rock;
    private readonly ushort _flora;
    private readonly Dictionary<ushort, int> _kind = new(); // block id → 1 glass, 2 light, 3 ladder, 0 other
    private readonly Dictionary<ushort, ushort> _lightOff = new();

    private StructureRuiner(VoxelGrid grid, GameContent content, long seed, string profile, double strength, int groundRow,
        IReadOnlyList<SettlementMarker> markers, string surfaceBlock)
    {
        _g = grid;
        _content = content;
        _seed = seed;
        _profile = profile;
        _s = strength <= 0 ? DefaultStrength(profile) : System.Math.Min(1.0, strength);
        _ground = System.Math.Clamp(groundRow, 0, grid.H - 1);
        _above = System.Math.Max(1, grid.H - 1 - _ground);
        _markerCell = new BitArray(grid.W * grid.H * grid.L);
        _fallen = new int[grid.W * grid.L];
        _fallenBlock = new ushort[grid.W * grid.L];
        ushort B(string key) => content.GetBlock(key)?.NumericId.Value ?? 0;
        _rock = B("stone");
        _flora = B(SettlementGenerator.BiomeFloraKey(surfaceBlock));
        ProtectMarkers(markers);
    }

    /// <summary>The strength a profile decays with when the template names none.</summary>
    public static double DefaultStrength(string profile) => profile == LandmarkRules.ProfileLattice ? 0.4 : 0.5;

    /// <summary>Runs the pass over <paramref name="grid"/> in place.</summary>
    public static void Run(VoxelGrid grid, GameContent content, long seed, string profile, double strength, int groundRow,
        IReadOnlyList<SettlementMarker> markers, string surfaceBlock)
    {
        var r = new StructureRuiner(grid, content, seed, profile, strength, groundRow, markers, surfaceBlock);
        if (profile != LandmarkRules.ProfileLattice)
        {
            r.Crumble();
        }

        r.Erode();
        r.CollapseSpots();
        r.Openings();
        r.CaveIns();
        r.Settle();
        r.Rubble();
        r.Overgrowth();
    }

    // ---------------------------------------------------------------- helpers

    private double U(long lane, int x, int y, int z) => Noise.Value01(_seed + lane, x, y, z);

    private int Column(int x, int z) => (x * _g.L) + z;

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
            _lightOff[id] = _content.GetBlock(def.Key + "_off")?.NumericId.Value ?? 0;
        }

        return k;
    }

    /// <summary>Nothing is ever written into a marker's cell or the one above it (a chest, a den's animals and a visitor
    /// need the room). The floor under a marker is NOT kept: when it falls, the marker comes down with it
    /// (<see cref="LandmarkGenerator"/> seats every surviving marker on what is left).</summary>
    private void ProtectMarkers(IReadOnlyList<SettlementMarker> markers)
    {
        foreach (var m in markers)
        {
            var p = m.LocalPos;
            for (int dy = 0; dy <= 1; dy++)
            {
                if (_g.In(p.X, p.Y + dy, p.Z))
                {
                    _markerCell[_g.Index(p.X, p.Y + dy, p.Z)] = true;
                }
            }
        }
    }

    /// <summary>Whether the decay may take the cell at all: never a kept cell, never anything at or below the ground row.</summary>
    private bool Takeable(int x, int y, int z) => y > _ground && !_g.Keep[_g.Index(x, y, z)];

    /// <summary>Whether erosion may bite the cell: takeable, and neither a ladder nor a shaped piece.</summary>
    private bool Erodible(int x, int y, int z, ushort block)
        => Takeable(x, y, z) && _g.ShapeAt(x, y, z) == 0 && KindOf(block) != 3;

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

        _g.Clear(x, y, z);
    }

    // ---------------------------------------------------------------- 1. crumbled patches

    /// <summary>In coherent patches of columns everything above a cut height falls. The patches come from 2-D value
    /// noise, so a tower loses one flank of its top instead of single stones.</summary>
    private void Crumble()
    {
        double scale = System.Math.Clamp(System.Math.Min(_g.W, _g.L) / 6.0, 4.0, 12.0);
        double threshold = 0.72 - (0.25 * _s);
        for (int x = 0; x < _g.W; x++)
        {
            for (int z = 0; z < _g.L; z++)
            {
                double n = Noise.Value3D(_seed + LaneCrumble, x / scale, 0.5, z / scale);
                if (n <= threshold)
                {
                    continue;
                }

                double depth = System.Math.Min(1.0, (n - threshold) / (1.0 - threshold) * (0.9 + (1.2 * _s)));
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
    /// both rising with height. The first course above the ground stays, so the footprint reads.</summary>
    private void Erode()
    {
        bool lattice = _profile == LandmarkRules.ProfileLattice;
        for (int x = 0; x < _g.W; x++)
        {
            for (int y = _ground + 2; y < _g.H; y++)
            {
                double frac = (double)(y - _ground) / _above;
                double biteAt = lattice ? 2.0 : 0.86 - (_s * (0.10 + (0.22 * frac)));
                double speckle = _s * (lattice ? 0.03 : 0.04) * (0.3 + frac);
                for (int z = 0; z < _g.L; z++)
                {
                    ushort b = _g.Blocks[_g.Index(x, y, z)];
                    if (b == 0 || !Erodible(x, y, z, b))
                    {
                        continue;
                    }

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
    /// (lattice — the settle step then brings down the span that hung on it).</summary>
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
                    if (_g.Solid(x, y, z) && Takeable(x, y, z) && U(LaneSpotFall, x, y, z) < 0.85)
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
            for (int y = System.Math.Max(_ground + 2, cy - r); y <= cy + r; y++)
            {
                for (int z = cz - r; z <= cz + r; z++)
                {
                    int d2 = ((x - cx) * (x - cx)) + ((y - cy) * (y - cy)) + ((z - cz) * (z - cz));
                    if (d2 <= r * r && _g.Solid(x, y, z) && Takeable(x, y, z))
                    {
                        Fell(x, y, z);
                    }
                }
            }
        }
    }

    // ---------------------------------------------------------------- 4. openings

    /// <summary>Glass is smashed and lights are dead — everywhere, the cellar and the tunnels included. A light becomes
    /// its switched-off twin (<c>&lt;key&gt;_off</c>) where the content has one, else it is gone.</summary>
    private void Openings()
    {
        double glassGone = 0.3 + (0.5 * _s);
        double lightDead = 0.5 + (0.4 * _s);
        for (int x = 0; x < _g.W; x++)
        {
            for (int y = 0; y < _g.H; y++)
            {
                for (int z = 0; z < _g.L; z++)
                {
                    int i = _g.Index(x, y, z);
                    ushort b = _g.Blocks[i];
                    if (b == 0 || _g.Keep[i])
                    {
                        continue;
                    }

                    int kind = KindOf(b);
                    if (kind == 1 && U(LaneGlass, x, y, z) < glassGone)
                    {
                        _g.Clear(x, y, z);
                    }
                    else if (kind == 2 && U(LaneLight, x, y, z) < lightDead)
                    {
                        ushort off = _lightOff[b];
                        _g.Set(x, y, z, off, shape: off != 0 ? _g.ShapeAt(x, y, z) : 0);
                    }
                }
            }
        }
    }
}
