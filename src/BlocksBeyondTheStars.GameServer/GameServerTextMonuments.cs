// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The stone text tablets of a planet type (#3310, KuBa-W — Jafar's school-club asteroid): one giant tablet per
/// <see cref="PlanetType.TextMonuments"/> entry, spelled by <see cref="TextMonumentGenerator"/> (huge: 15 × 21 blocks a
/// letter) and stood ~40 blocks in front of its landing pad, the face turned to the pad, so a player who lands sees the
/// whole text at once. The browser's fog closes ~64 blocks around the player and the far view shows builds only as 4×4
/// height blobs, which is why the distance is short and the text is broken into lines of at most five letters; the
/// streaming lifts a tablet's chunk columns to its top (<see cref="TextMonumentTopChunk"/>).
///
/// The seat is a search over the four bearings and a few distances: a dry footprint, little ground spread, and a clear line
/// of sight from eye height at the pad's edge to the foot of the text — what would block the view raises the tablet's
/// plinth instead. Like every monument it is stamped ONCE and recorded in <c>WorldMetadata.StampedFeatures</c>; every later
/// load re-derives the instance from the record. Unlike every other monument it is indestructible (#3311) and ignores the
/// structures slider — the two tablets are the point of the world.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The record prefix ("<c>textmonument@&lt;key&gt;:&lt;fx&gt;:&lt;fz&gt;:&lt;x&gt;:&lt;y&gt;:&lt;z&gt;</c>").</summary>
    private const string TextMonumentRecordPrefix = "textmonument@";

    /// <summary>The distances (pad centre → tablet face) the seat search tries, best first.</summary>
    private static readonly int[] TextMonumentDistances = { 40, 44, 48, 52 };

    /// <summary>The farthest a tablet's far corner may stand from its pad's centre: the browser streams four chunks (64
    /// blocks) around the player, and the far view shows builds only as blobs — the whole text must be inside.</summary>
    public const int TextMonumentViewRadius = 64;

    /// <summary>How far the plinth may lift a tablet over the ground to clear the view; beyond it a seat is ranked last.</summary>
    private const int TextMonumentMaxLift = 18;

    /// <summary>How deep under the tablet's foot its footprint is protected — the plinth and the ground it stands on.</summary>
    private const int TextMonumentFootDepth = 24;

    /// <summary>Eye height over the pad's ground, in blocks (a standing player).</summary>
    private const double TextMonumentEyeHeight = 2.6;

    /// <summary>The lamps in the front step: a warm white.</summary>
    private const int TextMonumentLampGlow = 0xFFE6B0;

    /// <summary>One stamped tablet: its frame in the world (the reader's right <c>(Rx, Rz)</c> and the facing
    /// <c>(Fx, Fz)</c> from the pad to the tablet) and the tablet itself.</summary>
    internal sealed class TextMonumentInstance
    {
        public string Key { get; init; } = string.Empty;
        public int Pad { get; init; }
        public int Fx { get; init; }
        public int Fz { get; init; }
        public Vector3i Anchor { get; init; }
        public TextTablet Tablet { get; init; } = null!;

        public int Rx => Fz;
        public int Rz => -Fx;

        /// <summary>The world cell of tablet cell (<paramref name="x"/>, <paramref name="y"/>, <paramref name="z"/>), X unwrapped.</summary>
        public Vector3i World(int x, int y, int z)
            => new(Anchor.X + (x * Rx) + (z * Fx), Anchor.Y + y, Anchor.Z + (x * Rz) + (z * Fz));
    }

    private List<TextMonumentInstance> _textMonuments => _worlds.Active.TextMonuments;

    private void StampTextMonuments()
    {
        var planet = _world.Planet;
        if (planet.TextMonuments.Count == 0 || _generator.TerrainGeneration < WorldDescription.KubaWGeneration)
        {
            return;
        }

        var records = RecordedTextMonuments();
        foreach (var spec in planet.TextMonuments)
        {
            var tablet = BuildTextTablet(spec);
            if (records.TryGetValue(spec.Key, out var record))
            {
                _textMonuments.Add(new TextMonumentInstance
                {
                    Key = spec.Key,
                    Pad = spec.Pad,
                    Fx = record.Fx,
                    Fz = record.Fz,
                    Anchor = record.Anchor,
                    Tablet = tablet,
                });
                continue;
            }

            if (spec.Pad < 0 || spec.Pad >= _landingPads.Count)
            {
                _log.Warn($"Text monument '{spec.Key}' names pad {spec.Pad}, but '{_world.LocationId}' has {_landingPads.Count}.");
                continue;
            }

            var seat = ChooseTextMonumentSeat(planet, _landingPads[spec.Pad], tablet);
            var instance = new TextMonumentInstance { Key = spec.Key, Pad = spec.Pad, Fx = seat.Fx, Fz = seat.Fz, Anchor = seat.Anchor, Tablet = tablet };
            var standPad = _landingPads[spec.Pad];
            _repo.RunInTransaction(() =>
            {
                StampTextTablet(planet, instance);
                ClearTextMonumentView(planet, instance, standPad);
            });
            MarkFeatureStamped($"{TextMonumentRecordPrefix}{spec.Key}:{seat.Fx}:{seat.Fz}:{seat.Anchor.X}:{seat.Anchor.Y}:{seat.Anchor.Z}");
            _textMonuments.Add(instance);
            _log.Info($"Stamped text monument '{spec.Key}' on '{_world.LocationId}' at {seat.Anchor} facing ({seat.Fx},{seat.Fz}).");
        }
    }

    /// <summary>The tablet a spec spells: basalt slab, concrete letters, the accent run in gold.</summary>
    private TextTablet BuildTextTablet(TextMonumentSpec spec)
    {
        ushort B(string key, string fallback) => (_content.GetBlock(key) ?? _content.GetBlock(fallback))?.NumericId.Value ?? 1;
        int accentLine = spec.AccentFrom.Count >= 2 ? spec.AccentFrom[0] : -1;
        int accentFrom = spec.AccentFrom.Count >= 2 ? spec.AccentFrom[1] : 0;
        return TextMonumentGenerator.Build(spec.Lines, B("basalt", "stone"), B("concrete", "stone"), B("gold_block", "concrete"),
            accentLine, accentFrom);
    }

    /// <summary>This body's recorded tablets: key → the facing and the foot anchor they were stamped with.</summary>
    private Dictionary<string, (int Fx, int Fz, Vector3i Anchor)> RecordedTextMonuments()
    {
        string prefix = _world.LocationId + "|" + TextMonumentRecordPrefix;
        var found = new Dictionary<string, (int Fx, int Fz, Vector3i Anchor)>(System.StringComparer.Ordinal);
        foreach (var f in _meta.StampedFeatures)
        {
            if (!f.StartsWith(prefix, System.StringComparison.Ordinal))
            {
                continue;
            }

            var p = f.Substring(prefix.Length).Split(':');
            if (p.Length != 6 || !int.TryParse(p[1], out int fx) || !int.TryParse(p[2], out int fz) || !int.TryParse(p[3], out int x)
                || !int.TryParse(p[4], out int y) || !int.TryParse(p[5], out int z))
            {
                _log.Warn($"Ignoring malformed text-monument record '{f}'.");
                continue;
            }

            found[p[0]] = (fx, fz, new Vector3i(x, y, z));
        }

        return found;
    }

    /// <summary>The seat of a tablet in front of <paramref name="pad"/>: the best of four bearings × a few distances. A second
    /// tablet at the same pad (#3330, "JuMaVe-Games" beside "KuBa West Trier") never takes a bearing an earlier one uses,
    /// prefers the opposite side and keeps <see cref="TextMonumentGap"/> to every tablet already standing.</summary>
    private (int Fx, int Fz, Vector3i Anchor) ChooseTextMonumentSeat(PlanetType planet, LandingPad pad, TextTablet tablet)
    {
        var bearings = new (int Fx, int Fz)[] { (0, 1), (1, 0), (0, -1), (-1, 0) };
        var taken = _textMonuments.Where(m => m.Pad == pad.Index).Select(m => (m.Fx, m.Fz)).ToList();
        int start = (int)(WorldGenerator.StableHash("textmonument-bearing:" + _world.LocationId + ":" + pad.Index) & 3);
        (int Fx, int Fz, Vector3i Anchor) best = default;
        double bestScore = double.MaxValue;
        for (int b = 0; b < bearings.Length; b++)
        {
            var (fx, fz) = bearings[(start + b) & 3];
            if (taken.Contains((fx, fz)))
            {
                continue;
            }

            // Beside a taken bearing (not opposite it) the two tablets stand at right angles — allowed, but the opposite reads better.
            double side = taken.Any(t => (t.Fx * fx) + (t.Fz * fz) == 0) ? 6.0 : 0.0;
            for (int d = 0; d < TextMonumentDistances.Length; d++)
            {
                if (TextMonumentSeatScore(planet, pad, tablet, fx, fz, TextMonumentDistances[d], out var anchor) is not { } score
                    || CrowdsATablet(anchor, fx, fz, tablet))
                {
                    continue;
                }

                score += (d * 0.5) + side; // the nearer distances read best
                if (score < bestScore)
                {
                    bestScore = score;
                    best = (fx, fz, anchor);
                }
            }
        }

        if (bestScore == double.MaxValue)
        {
            // Every seat left the latitude band — cannot happen with the band the pads keep; stand it north, on the pad's ground.
            best = (0, 1, new Vector3i(pad.CenterX - (tablet.Width / 2), pad.CenterY + 1, pad.CenterZ + TextMonumentDistances[0]));
        }

        return best;
    }

    /// <summary>The gap a tablet keeps to every other tablet, in blocks.</summary>
    private const int TextMonumentGap = 6;

    /// <summary>Whether a tablet seated at <paramref name="anchor"/> facing (<paramref name="fx"/>, <paramref name="fz"/>) would
    /// come within <see cref="TextMonumentGap"/> of a tablet already standing (footprints compared seam-aware).</summary>
    private bool CrowdsATablet(Vector3i anchor, int fx, int fz, TextTablet tablet)
    {
        int circ = _world.Circumference;
        var mine = TabletFootprint(anchor, fz, -fx, fx, fz, tablet.Width, tablet.Depth);
        foreach (var m in _textMonuments)
        {
            var other = TabletFootprint(m.Anchor, m.Rx, m.Rz, m.Fx, m.Fz, m.Tablet.Width, m.Tablet.Depth);
            int shift = WorldConstants.WrapDeltaX(other.MinX - mine.MinX, circ) - (other.MinX - mine.MinX);
            if (other.MinX + shift - TextMonumentGap <= mine.MaxX && mine.MinX <= other.MaxX + shift + TextMonumentGap
                && other.MinZ - TextMonumentGap <= mine.MaxZ && mine.MinZ <= other.MaxZ + TextMonumentGap)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The highest chunk Y a text tablet reaches in chunk column (<paramref name="chunkX"/>, <paramref name="chunkZ"/>),
    /// or <see cref="int.MinValue"/> when no tablet stands in it — the streaming pass lifts the column to it.</summary>
    private int TextMonumentTopChunk(int chunkX, int chunkZ)
    {
        if (_textMonuments.Count == 0)
        {
            return int.MinValue;
        }

        int circ = _world.Circumference;
        int x0 = chunkX * WorldConstants.ChunkSize, z0 = chunkZ * WorldConstants.ChunkSize, last = WorldConstants.ChunkSize - 1;
        int top = int.MinValue;
        foreach (var m in _textMonuments)
        {
            var fp = TabletFootprint(m.Anchor, m.Rx, m.Rz, m.Fx, m.Fz, m.Tablet.Width, m.Tablet.Depth);
            int rel = WorldConstants.WrapDeltaX(x0 - fp.MinX, circ);
            if (rel + last >= 0 && rel <= fp.MaxX - fp.MinX && z0 + last >= fp.MinZ && z0 <= fp.MaxZ)
            {
                top = System.Math.Max(top, WorldConstants.WorldToChunk(m.Anchor.Y + m.Tablet.Height - 1));
            }
        }

        return top;
    }

    /// <summary>Test/util: the top chunk a tablet lifts a column's streaming to (<see cref="int.MinValue"/> = none).</summary>
    internal int TextMonumentTopChunkForTest(int chunkX, int chunkZ) => TextMonumentTopChunk(chunkX, chunkZ);

    /// <summary>A tablet's ground rectangle (X unwrapped) from its anchor and frame.</summary>
    private static (int MinX, int MaxX, int MinZ, int MaxZ) TabletFootprint(Vector3i anchor, int rx, int rz, int fx, int fz, int width, int depth)
    {
        int x2 = anchor.X + ((width - 1) * rx) + ((depth - 1) * fx);
        int z2 = anchor.Z + ((width - 1) * rz) + ((depth - 1) * fz);
        return (System.Math.Min(anchor.X, x2), System.Math.Max(anchor.X, x2), System.Math.Min(anchor.Z, z2), System.Math.Max(anchor.Z, z2));
    }

    /// <summary>A seat's score (lower is better) and its anchor, or null when the footprint would cross the latitude wrap.
    /// The anchor's Y is the tablet's foot: over the highest ground of the footprint, lifted further where the ground
    /// between the pad and the text would block the view.</summary>
    private double? TextMonumentSeatScore(PlanetType planet, LandingPad pad, TextTablet tablet, int fx, int fz, int distance,
        out Vector3i anchor)
    {
        int rx = fz, rz = -fx;
        double farCorner = System.Math.Sqrt(((distance + tablet.Depth) * (distance + tablet.Depth)) + ((tablet.Width / 2.0) * (tablet.Width / 2.0)));
        if (farCorner > TextMonumentViewRadius && distance > TextMonumentDistances.Min())
        {
            anchor = default;
            return null; // the far corner would leave the browser's view — only the nearest distance may (a very wide text)
        }

        int ax = pad.CenterX + (fx * distance) - (rx * (tablet.Width / 2));
        int az = pad.CenterZ + (fz * distance) - (rz * (tablet.Width / 2));
        anchor = new Vector3i(ax, 0, az);
        int circ = _world.Circumference;
        int latLimit = (WorldConstants.LatitudePeriodFor(circ) / 2) - 4;

        int minGround = int.MaxValue, maxGround = int.MinValue, wet = 0, columns = 0;
        for (int x = 0; x < tablet.Width; x++)
        {
            for (int z = 0; z < tablet.Depth; z++)
            {
                int wx = WorldConstants.WrapX(ax + (x * rx) + (z * fx), circ);
                int wz = az + (x * rz) + (z * fz);
                if (System.Math.Abs(wz) > latLimit)
                {
                    return null;
                }

                int h = _generator.SurfaceHeight(planet, wx, wz);
                minGround = System.Math.Min(minGround, h);
                maxGround = System.Math.Max(maxGround, h);
                columns++;
                if (_generator.IsSurfaceWater(planet, wx, wz) || _generator.IsSurfaceLava(planet, wx, wz))
                {
                    wet++;
                }
            }
        }

        int foot = maxGround + 1;
        int lift = TextMonumentViewLift(planet, pad, tablet, ax, az, rx, rz, foot);
        foot += lift;
        anchor = new Vector3i(ax, foot, az);
        double score = (maxGround - minGround) + (lift * 2.0) + (wet * 40.0 / System.Math.Max(1, columns));
        return lift > TextMonumentMaxLift ? score + 1000.0 : score;
    }

    /// <summary>How many blocks the tablet's foot must rise so the lines of sight from eye height at the pad's edge to the
    /// foot of the text (left end, middle, right end) clear the ground in between.</summary>
    private int TextMonumentViewLift(PlanetType planet, LandingPad pad, TextTablet tablet, int ax, int az, int rx, int rz, int foot)
    {
        int circ = _world.Circumference;
        double eyeY = pad.CenterY + TextMonumentEyeHeight;
        double textFoot = foot + TextMonumentGenerator.Border;
        double need = 0;
        foreach (int x in new[] { 0, tablet.Width / 2, tablet.Width - 1 })
        {
            double tx = ax + (x * rx), tz = az + (x * rz);
            double sx = pad.CenterX + ((tx - pad.CenterX) * 0.2), sz = pad.CenterZ + ((tz - pad.CenterZ) * 0.2); // the pad's rim
            int steps = (int)System.Math.Ceiling(System.Math.Sqrt(((tx - sx) * (tx - sx)) + ((tz - sz) * (tz - sz))));
            for (int i = 1; i < steps; i++)
            {
                double t = i / (double)steps;
                int h = _generator.SurfaceHeight(planet, WorldConstants.WrapX((int)System.Math.Round(sx + ((tx - sx) * t)), circ),
                    (int)System.Math.Round(sz + ((tz - sz) * t)));
                double lineY = eyeY + ((textFoot - eyeY) * t);
                if (h + 1 > lineY)
                {
                    need = System.Math.Max(need, eyeY + ((h + 1 - eyeY) / t) - textFoot);
                }
            }
        }

        return (int)System.Math.Ceiling(need);
    }

    /// <summary>Writes a tablet into the world: a plinth from each column's ground up to the foot, then every cell of the
    /// tablet — air cells only where the world holds something, so the edit log stays small. Inside a repo transaction.</summary>
    private void StampTextTablet(PlanetType planet, TextMonumentInstance m)
    {
        var tablet = m.Tablet;
        int circ = _world.Circumference;
        ushort plinth = tablet.Get(0, 0, 1);
        for (int x = 0; x < tablet.Width; x++)
        {
            for (int z = 0; z < tablet.Depth; z++)
            {
                var foot = m.World(x, 0, z);
                int wx = WorldConstants.WrapX(foot.X, circ);
                int ground = _generator.SurfaceHeight(planet, wx, foot.Z);
                for (int y = ground + 1; y < m.Anchor.Y; y++)
                {
                    _world.SetBlock(new Vector3i(wx, y, foot.Z), new BlockId(plinth));
                }

                for (int y = 0; y < tablet.Height; y++)
                {
                    var pos = new Vector3i(wx, m.Anchor.Y + y, foot.Z);
                    ushort b = tablet.Get(x, y, z);
                    if (b != 0)
                    {
                        _world.SetBlock(pos, new BlockId(b), 0, tablet.IsLamp(x, y, z) ? TextMonumentLampGlow : 0, 0);
                    }
                    else if (!_world.GetBlock(pos).IsAir)
                    {
                        _world.SetBlock(pos, BlockId.Air);
                    }
                }
            }
        }
    }

    /// <summary>How high above the ground a plant may stand in front of a tablet: anything taller (giant flowers, bushes, a
    /// boulder) hides the lower lines from a player on the pad, so the whole growth of such a column is cleared.</summary>
    private const int TextMonumentViewClearHeight = 1;

    /// <summary>How far above the ground the view clearing looks for growth.</summary>
    private const int TextMonumentViewClearReach = 14;

    /// <summary>Clears the meadow between the pad's rim and a tablet's face of everything taller than
    /// <see cref="TextMonumentViewClearHeight"/> — the giant flowers of KuBa-W would otherwise cover the bottom lines from a
    /// player standing on the pad. Small flowers stay; the pad's own volume is left alone. Inside the stamp's transaction.</summary>
    private void ClearTextMonumentView(PlanetType planet, TextMonumentInstance m, LandingPad pad)
    {
        int circ = _world.Circumference;
        var face = m.World(m.Tablet.Width / 2, 0, 0);
        int toFace = (WorldConstants.WrapDeltaX(face.X - pad.CenterX, circ) * m.Fx) + ((face.Z - pad.CenterZ) * m.Fz);
        int depth = toFace - pad.Radius - 1;
        for (int lz = -1; lz >= -depth; lz--)
        {
            for (int lx = 0; lx < m.Tablet.Width; lx++)
            {
                var c = m.World(lx, 0, lz);
                int wx = WorldConstants.WrapX(c.X, circ);
                int ground = _generator.SurfaceHeight(planet, wx, c.Z);
                if (_world.GetBlock(new Vector3i(wx, ground + TextMonumentViewClearHeight + 1, c.Z)).IsAir)
                {
                    continue; // nothing taller than a small flower here
                }

                for (int y = ground + 1; y <= ground + TextMonumentViewClearReach; y++)
                {
                    var pos = new Vector3i(wx, y, c.Z);
                    if (!_world.GetBlock(pos).IsAir && !IsOnLandingPad(pos))
                    {
                        _world.SetBlock(pos, BlockId.Air);
                    }
                }
            }
        }
    }

    /// <summary>The tablet whose protected box holds <paramref name="cell"/> — its footprint from
    /// <see cref="TextMonumentFootDepth"/> under the foot up to its top — or null (#3311).</summary>
    private TextMonumentInstance? TextMonumentAt(Vector3i cell)
    {
        if (_textMonuments.Count == 0)
        {
            return null;
        }

        int circ = _world.Circumference;
        foreach (var m in _textMonuments)
        {
            if (cell.Y < m.Anchor.Y - TextMonumentFootDepth || cell.Y >= m.Anchor.Y + m.Tablet.Height)
            {
                continue;
            }

            // Back into the tablet's frame: dx along the reader's right, dz along the facing (both axes unit, X seam-aware).
            int relX = WorldConstants.WrapDeltaX(cell.X - m.Anchor.X, circ);
            int relZ = cell.Z - m.Anchor.Z;
            int lx = (relX * m.Rx) + (relZ * m.Rz);
            int lz = (relX * m.Fx) + (relZ * m.Fz);
            if (lx >= 0 && lx < m.Tablet.Width && lz >= 0 && lz < m.Tablet.Depth)
            {
                return m;
            }
        }

        return null;
    }

    /// <summary>#3311: a cell of a stone text tablet (or the ground under it) — no player mines, burns, blasts or builds there.</summary>
    private bool IsTextMonumentProtected(Vector3i cell) => TextMonumentAt(cell) is not null;

    /// <summary>#3312: the tablet a block scan points at — the aimed cell's tablet, or (an older client sends no cell) the
    /// tablet the scanning player stands at. Only while the player's own world is the active one.</summary>
    private TextMonumentInstance? TextMonumentForScan(PlayerSession session, Vector3i? cell)
    {
        if (_textMonuments.Count == 0 || session.CurrentLocationId != _worlds.Active.LocationId)
        {
            return null;
        }

        if (cell is { } aimed)
        {
            return TextMonumentAt(aimed);
        }

        var pos = session.State.Position;
        int circ = _world.Circumference;
        foreach (var m in _textMonuments)
        {
            var centre = m.World(m.Tablet.Width / 2, m.Tablet.Height / 2, 1);
            float dx = WorldConstants.WrapDeltaX((int)pos.X - centre.X, circ), dz = pos.Z - centre.Z;
            float reach = (m.Tablet.Width / 2f) + MonumentScanReach;
            if ((dx * dx) + (dz * dz) <= reach * reach)
            {
                return m;
            }
        }

        return null;
    }

    // ---------------- Test hooks ----------------

    /// <summary>Test/util: the active world's tablets — key, foot anchor, facing (pad → tablet) and size.</summary>
    public IReadOnlyList<(string Key, Vector3i Anchor, int Fx, int Fz, int Width, int Height, int Depth)> TextMonumentsForTest()
        => _textMonuments.Select(m => (m.Key, m.Anchor, m.Fx, m.Fz, m.Tablet.Width, m.Tablet.Height, m.Tablet.Depth)).ToList();

    /// <summary>Test/util: the world cell (X wrapped) of tablet cell (<paramref name="x"/>, <paramref name="y"/>,
    /// <paramref name="z"/>) of the tablet <paramref name="key"/> — x to the reader's right, y up, z away from the reader.</summary>
    public Vector3i TextMonumentCellForTest(string key, int x, int y, int z)
    {
        var w = _textMonuments.First(m => m.Key == key).World(x, y, z);
        return new Vector3i(WorldConstants.WrapX(w.X, _world.Circumference), w.Y, w.Z);
    }

    /// <summary>Test/util: whether a cell is protected as part of a text tablet.</summary>
    public bool TextMonumentProtectedForTest(Vector3i cell) => IsTextMonumentProtected(cell);
}
