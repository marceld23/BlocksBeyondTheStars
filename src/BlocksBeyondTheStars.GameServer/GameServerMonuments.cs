// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Stamps <b>monuments</b> on a body's surface — eroded relics of a vanished civilisation: a half-collapsed
/// arcade of arches, a free-standing gate, a ring of standing stones, an obelisk, a rune altar. They are the
/// smallest structure the world carries and the only one that also appears on <b>airless</b> bodies: a stone
/// circle on a dead moon is exactly the point.
///
/// Monuments follow the ruins model — their blocks are <b>not protected</b> (players may mine them), so the
/// voxels are stamped <b>once</b> (guarded by <c>FeatureStamped("monuments")</c>) and live on as persisted
/// edits; a razed monument stays razed. The instances themselves are re-derived deterministically from the
/// seed on every load, because the scanner needs to know which relic the player is standing in front of
/// (see <see cref="ScanSubject"/>). Deterministic from the world seed.
///
/// Unlike settlements they are NOT seated on a flat foundation plate: each column that carries stone gets its
/// own plinth down to the natural ground, so a circle keeps standing in the landscape instead of on a plaza.
/// </summary>
public sealed partial class GameServer
{
    private const int MonumentHardCap = 3;

    /// <summary>How far from a monument's bounds a scan still counts as a scan OF that monument.</summary>
    private const float MonumentScanReach = 14f;

    /// <summary>Depth cap for the plinth that carries a monument block down to the natural surface.</summary>
    private const int MonumentPlinthDepth = 10;

    private List<MonumentInstance> _monuments => _worlds.Active.Monuments;

    private void StampMonuments()
    {
        // Monuments ride the same "structures frequency" world option as settlements and ruins (Off ⇒ none),
        // but they are NOT gated by hospitability or air: whoever raised them is long gone, and a relic on a
        // dead moon is the best version of the feature.
        double factor = _meta.Description.Settlements.StructureFactor();
        if (factor <= 0)
        {
            return;
        }

        long mSeed = _meta.Seed ^ WorldGenerator.StableHash("monument:" + _world.LocationId); // per body (#478)

        // Where a body's monuments stand is decided ONCE and persisted, then only replayed. It cannot be
        // re-rolled per load like a settlement's: the placement gate skips footprints players have built in
        // (#527), so a re-roll after somebody mines a rune would move the instance off its own stones and the
        // scanner would stop recognising the relic the player is standing in.
        var recorded = RecordedMonuments();
        if (recorded.Count > 0)
        {
            RederiveMonuments(recorded, mSeed);
            return;
        }

        if (FeatureStamped("monuments"))
        {
            return; // decided before, and the roll said "none here"
        }

        var rng = new System.Random(unchecked((int)(mSeed ^ (mSeed >> 32))));

        // More common than ruins — the point of the feature is that exploring turns them up. Bigger worlds
        // carry a few more, and the structures slider scales the whole roll.
        var planet = _world.Planet;
        double sizeFactor = System.Math.Clamp(_world.Circumference / SettlementRefCirc, 0.5, 2.0);
        double r = rng.NextDouble() / sizeFactor;
        int count = r < 0.30 ? 0 : r < 0.75 ? 1 : r < 0.95 ? 2 : 3;
        count = System.Math.Min(MonumentHardCap, (int)System.Math.Round(count * System.Math.Clamp(factor, 0.0, 2.0)));

        // Frontier scaling (#1122): full-frontier worlds roll one monument MORE (still under the hard
        // cap). After the rolls, so the rng stream — and every existing world's count — is unchanged;
        // zero-count worlds stay empty (the slider's "none here" verdict is respected out there too).
        if (count > 0 && FrontierTierForBody(_world.LocationId) >= 2)
        {
            count = System.Math.Min(MonumentHardCap, count + 1);
        }

        if (count <= 0)
        {
            // The "none here" roll is a decision too — record it (the guard above documents exactly this
            // case). Without the mark a zero-count world re-rolled on every load; harmless while the roll
            // was deterministic, but a contract violation the #576-#580 galaxy shift finally surfaced.
            MarkFeatureStamped("monuments");
            return;
        }

        var surface = planet.Biomes.Count > 0 ? planet.Biomes[0].SurfaceBlock : planet.SurfaceBlock;

        // Reserve the pads, the wreck zone, every settlement and every bandit camp so a relic lands clear of
        // them (the monuments also reserve each other below).
        var reserved = new List<(int Cx, int Cz, int Hw, int Hl)>();
        foreach (var pad in _landingPads)
        {
            reserved.Add((pad.CenterX, pad.CenterZ, LandingPadRadius + 2, LandingPadRadius + 2));
        }

        int pad0X = _landingPads.Count > 0 ? _landingPads[0].CenterX : 0;
        int pad0Z = _landingPads.Count > 0 ? _landingPads[0].CenterZ : 0;
        reserved.Add((pad0X - 56, pad0Z + 56, 14, 14)); // wreck zone (see GameServerWrecks.StampWreck)
        foreach (var s in _settlements)
        {
            reserved.Add(((s.Min.X + s.Max.X) / 2, (s.Min.Z + s.Max.Z) / 2,
                (s.Max.X - s.Min.X) / 2 + 1, (s.Max.Z - s.Min.Z) / 2 + 1));
        }

        foreach (var camp in _banditCamps)
        {
            reserved.Add(((camp.Min.X + camp.Max.X) / 2, (camp.Min.Z + camp.Max.Z) / 2,
                (camp.Max.X - camp.Min.X) / 2 + 1, (camp.Max.Z - camp.Min.Z) / 2 + 1));
        }

        // Each monument on a body is a different silhouette, so a world never shows the same relic twice.
        var pool = MonumentGenerator.Archetypes.OrderBy(a => WorldGenerator.StableHash(a + ':' + mSeed)).ToList();

        var placed = new List<(PlacedSettlement Placement, string Archetype, int Index)>();
        for (int i = 0; i < count && i < pool.Count; i++)
        {
            string archetype = pool[i];
            var (structure, ir) = BuildMonument(archetype, MonumentSeed(mSeed, i), surface);

            // avoidPlayerEdits (#527): monuments are new in this release, so they stamp into worlds people
            // have already built in — never on top of somebody's base. On a fresh world the guaranteed
            // search takes over (#586) — a relic may even stand in a lava plain; monuments already pin
            // their positions via their own record keys, so no extra placement record is needed.
            Vector3i origin;
            int groundY;
            bool onIsland;
            if (_worlds.Active.VirginAtLoad)
            {
                if (!TryPlaceStructureGuaranteed(structure, RngFor(MonumentSeed(mSeed, i), "search"), reserved,
                        wantIsland: false, SeatPolicy.Monument, avoidPlayerEdits: true,
                        out origin, out groundY, out onIsland, out _))
                {
                    continue;
                }
            }
            else if (!TryPlaceSettlement(structure, ir, reserved, wantIsland: false,
                    out origin, out groundY, out onIsland, avoidPlayerEdits: true))
            {
                continue;
            }

            placed.Add((new PlacedSettlement
            {
                Structure = structure,
                Origin = origin,
                GroundY = groundY,
                Tier = "monument",
                Ruined = true,
                OnIsland = onIsland,
                Name = archetype,
                Rng = ir,
            }, archetype, i));
            reserved.Add((origin.X + structure.Width / 2, origin.Z + structure.Length / 2,
                structure.Width / 2 + 1, structure.Length / 2 + 1));
        }

        // Even a body that ends up with none records the decision, so the roll never runs twice.
        ReportStamp("monument", System.Math.Min(count, pool.Count), placed.Count);
        MarkFeatureStamped("monuments");
        if (placed.Count == 0)
        {
            return;
        }

        // Voxels: written once, here (a mined-out relic must stay mined out — the ruins rule).
        _repo.RunInTransaction(() =>
        {
            foreach (var (p, _, _) in placed)
            {
                StampMonumentBlocks(p);
            }
        });

        foreach (var (p, archetype, index) in placed)
        {
            MarkFeatureStamped($"{MonumentRecordPrefix}{index}:{archetype}:{p.Origin.X}:{p.GroundY}:{p.Origin.Z}");
            RegisterMonument(p, archetype);
        }

        _log.Info($"Stamped {placed.Count} monument(s) on '{_world.LocationId}': " +
                  string.Join(", ", placed.Select(x => x.Archetype)) + ".");
    }

    /// <summary>The persisted-placement key prefix ("<c>monument@&lt;index&gt;:&lt;archetype&gt;:&lt;x&gt;:&lt;y&gt;:&lt;z&gt;</c>").</summary>
    private const string MonumentRecordPrefix = "monument@";

    private static long MonumentSeed(long worldSeed, int index)
        => worldSeed ^ unchecked((long)(index + 1) * (long)0x9E3779B97F4A7C15);

    /// <summary>Generates a monument together with the rng that produced it — the same call sequence on a
    /// re-derive, so the structure, its cache roll and its loot come out identical.</summary>
    private (SettlementStructure Structure, System.Random Rng) BuildMonument(string archetype, long instSeed, string surface)
    {
        var ir = new System.Random(unchecked((int)(instSeed ^ (instSeed >> 32))));
        bool withCache = ir.NextDouble() < 0.35; // roughly one relic in three hides a cache
        return (MonumentGenerator.Generate(archetype, instSeed, surface, _content, withCache), ir);
    }

    /// <summary>The placements this body recorded when its monuments were first stamped.</summary>
    private List<string> RecordedMonuments()
    {
        string prefix = _world.LocationId + "|" + MonumentRecordPrefix;
        var found = new List<string>();
        foreach (var f in _meta.StampedFeatures)
        {
            if (f.StartsWith(prefix, System.StringComparison.Ordinal))
            {
                found.Add(f.Substring(prefix.Length));
            }
        }

        return found;
    }

    /// <summary>Rebuilds this body's monument instances from their recorded placements: same archetype, same
    /// seed, same origin — so the bounds match the stones that are actually in the ground, whatever the player
    /// has since built nearby.</summary>
    private void RederiveMonuments(List<string> records, long mSeed)
    {
        var planet = _world.Planet;
        string surface = planet.Biomes.Count > 0 ? planet.Biomes[0].SurfaceBlock : planet.SurfaceBlock;

        foreach (string record in records)
        {
            var parts = record.Split(':');
            if (parts.Length != 5
                || !int.TryParse(parts[0], out int index)
                || !int.TryParse(parts[2], out int ox)
                || !int.TryParse(parts[3], out int gy)
                || !int.TryParse(parts[4], out int oz))
            {
                _log.Warn($"Ignoring malformed monument record '{record}' on '{_world.LocationId}'.");
                continue;
            }

            var (structure, ir) = BuildMonument(parts[1], MonumentSeed(mSeed, index), surface);
            RegisterMonument(new PlacedSettlement
            {
                Structure = structure,
                Origin = new Vector3i(ox, gy, oz),
                GroundY = gy,
                Tier = "monument",
                Ruined = true,
                OnIsland = false,
                Name = parts[1],
                Rng = ir,
            }, parts[1]);
        }
    }

    /// <summary>Adds the runtime instance and (idempotently) its relic cache.</summary>
    private void RegisterMonument(PlacedSettlement p, string archetype)
    {
        var s = p.Structure;
        _monuments.Add(new MonumentInstance
        {
            Min = new Vector3i(p.Origin.X, p.GroundY, p.Origin.Z),
            Max = new Vector3i(p.Origin.X + s.Width, p.GroundY + s.Height, p.Origin.Z + s.Length),
            Center = new Vector3f(p.Origin.X + s.Width / 2f, p.GroundY + 1, p.Origin.Z + s.Length / 2f),
            Archetype = archetype,
        });

        foreach (var m in s.Markers)
        {
            if (m.Type != "relic_cache")
            {
                continue;
            }

            // Idempotent via GeneratedLoot, so a looted relic never refills.
            var pos = new Vector3f(p.Origin.X + m.LocalPos.X + 0.5f, p.GroundY + m.LocalPos.Y + 0.5f,
                p.Origin.Z + m.LocalPos.Z + 0.5f);
            SpawnStructureLoot("monument", m.Type, pos, p.Rng);
        }
    }

    /// <summary>Writes a monument's voxels into the world. Unlike <see cref="StampSettlementBlocks"/> this
    /// lays NO foundation plate: only the columns that actually carry stone are cleared above and filled
    /// below, so the relic sits in the landscape instead of on a paved square. Must run inside a repo
    /// transaction.</summary>
    private void StampMonumentBlocks(PlacedSettlement p)
    {
        var s = p.Structure;
        var planet = _world.Planet;
        int gy = p.GroundY;
        var origin = p.Origin;

        for (int x = 0; x < s.Width; x++)
        {
            for (int z = 0; z < s.Length; z++)
            {
                int top = -1;
                for (int y = s.Height - 1; y >= 0; y--)
                {
                    if (s.Get(x, y, z) != 0)
                    {
                        top = y;
                        break;
                    }
                }

                if (top < 0)
                {
                    continue; // nothing stands in this column — leave the terrain exactly as it is
                }

                int wx = origin.X + x, wz = origin.Z + z;

                // Clear the terrain the relic occupies, then stamp its cells (air cells inside the volume are
                // left as air so an arch's opening is genuinely open).
                for (int y = 0; y <= top; y++)
                {
                    ushort b = s.Get(x, y, z);
                    var pos = new Vector3i(wx, gy + y, wz);
                    if (b == 0)
                    {
                        _world.SetBlock(pos, BlockId.Air);
                        continue;
                    }

                    var (tint, glow) = s.GetModifier(x, y, z);
                    _world.SetBlock(pos, new BlockId(b), tint, glow, s.GetShape(x, y, z));
                }

                // A plinth down to the natural ground so nothing hangs in the air on a slope. Skipped on a sky
                // island, whose deck is the ground.
                if (p.OnIsland || s.Get(x, 0, z) == 0)
                {
                    continue;
                }

                ushort foot = s.Get(x, 0, z);
                int colSurf = _generator.SurfaceHeight(planet, wx, wz);
                int floorY = System.Math.Max(colSurf + 1, gy - MonumentPlinthDepth);
                for (int y = gy - 1; y >= floorY; y--)
                {
                    _world.SetBlock(new Vector3i(wx, y, wz), new BlockId(foot));
                }
            }
        }
    }

    /// <summary>The monument a player standing at <paramref name="pos"/> is at, or null. Used by the scanner
    /// to tell a rune block that belongs to a relic apart from one the player mined and carried home.</summary>
    private MonumentInstance? MonumentNear(Vector3f pos)
    {
        int circ = _world.Circumference;
        MonumentInstance? best = null;
        float bestSq = MonumentScanReach * MonumentScanReach;
        foreach (var m in _monuments)
        {
            // Gap between the player and the monument's box (0 while inside it), longitude-wrap aware.
            float relX = WorldConstants.WrapDeltaX((int)pos.X - m.Min.X, circ);
            float dx = Gap(relX, 0, m.Max.X - m.Min.X);
            float dz = Gap(pos.Z, m.Min.Z, m.Max.Z);
            float dy = Gap(pos.Y, m.Min.Y - 4, m.Max.Y + 4); // a relic is scannable from its foot and its top
            float d = (dx * dx) + (dy * dy) + (dz * dz);
            if (d < bestSq)
            {
                bestSq = d;
                best = m;
            }
        }

        return best;
    }

    /// <summary>Distance from a value to an interval — 0 when it lies inside.</summary>
    private static float Gap(float v, float min, float max)
        => v < min ? min - v : v > max ? v - max : 0f;

    // ---------------- Test hooks ----------------

    /// <summary>Test/util: the monuments derived for the active world (centre + archetype).</summary>
    public IReadOnlyList<(Vector3f Center, string Archetype)> MonumentsForTest()
        => _monuments.Select(m => (m.Center, m.Archetype)).ToList();

    /// <summary>Test/util: whether the placement guard (#527) sees player-built blocks in a footprint.</summary>
    public bool FootprintHasPlayerEditsForTest(int ox, int oz, int gy, int w, int h, int l)
        => FootprintHasPlayerEdits(ox, oz, gy, w, h, l);

    /// <summary>Test/util: materialises a monument instance at a position, bypassing the seeded placement
    /// roll, so the scan path can be exercised deterministically.</summary>
    public void SpawnMonumentForTest(Vector3f center, string archetype)
        => _monuments.Add(new MonumentInstance
        {
            Min = new Vector3i((int)center.X - 4, (int)center.Y - 1, (int)center.Z - 4),
            Max = new Vector3i((int)center.X + 4, (int)center.Y + 6, (int)center.Z + 4),
            Center = center,
            Archetype = archetype,
        });
}
