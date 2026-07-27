// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Stamps <b>bandit camps</b> on a body's surface — small hostile outposts (log huts, palisade, fire
/// pit) guarded by bandit NPCs, with a stash as the raid reward. Camps follow the ruins model: their
/// blocks are <b>not protected</b> (players may raze them), so the voxels are stamped <b>once</b>
/// (guarded by <c>FeatureStamped("banditcamps")</c>) and live on as persisted edits — a razed camp
/// stays razed. The camp <i>instances</i> (bounds, markers, guards) are re-derived deterministically
/// from the seed on every load; a camp whose guards were all killed is persisted as cleared and its
/// guards never come back. Deterministic from the world seed.
/// </summary>
public sealed partial class GameServer
{
    private const int BanditCampHardCap = 2;

    private void StampBanditCamps()
    {
        if (!_config.PlaceBanditCamps || Rules.Bandits == AlienActivity.Off)
        {
            // Deliberately NOT marked as stamped: a world first visited with bandits off still gets its
            // camps if the option is turned on later (the placement itself stays seed-deterministic).
            return;
        }

        var planet = _world.Planet;
        if (planet.IsAirless)
        {
            return; // bandits camp where they can breathe (matches the ruins/settlement logic)
        }

        long cSeed = _meta.Seed ^ WorldGenerator.StableHash("banditcamp:" + _world.LocationId); // per body (#478)
        var rng = new System.Random(unchecked((int)(cSeed ^ (cSeed >> 32))));

        // Rarer than ruins: most worlds have none. The rule slider nudges the odds, the seeded roll
        // decides — so a given body either is bandit country or it isn't, forever.
        // #547: in a Pirate Haven system the slider is effectively one step hotter (camps are the
        // archetype's ground presence), and the world's Danger option scales the odds on top
        // (Normal = ×1.0 keeps them exactly as before; Off yields no camps at all).
        var activity = Rules.Bandits;
        if (activity != AlienActivity.Off
            && SystemArchetypeOf(_galaxy.FindBody(_world.LocationId)?.SystemId) == SystemArchetype.PirateHaven)
        {
            activity = activity switch
            {
                AlienActivity.Rare => AlienActivity.Normal,
                AlienActivity.Normal => AlienActivity.Frequent,
                _ => AlienActivity.Extreme,
            };
        }

        double odds = activity switch
        {
            AlienActivity.Rare => 0.75,
            AlienActivity.Normal => 0.60,
            AlienActivity.Frequent => 0.45,
            AlienActivity.Extreme => 0.30,
            _ => 1.0,
        };
        odds = System.Math.Clamp(1.0 - (1.0 - odds) * _meta.Description.Danger.DangerFactor(), 0.05, 1.0);
        double r = rng.NextDouble();
        int count = r < odds ? 0 : r < odds + (1.0 - odds) * 0.8 ? 1 : 2;
        count = System.Math.Min(BanditCampHardCap, count);
        if (count <= 0)
        {
            return;
        }

        var surface = planet.Biomes.Count > 0 ? planet.Biomes[0].SurfaceBlock : planet.SurfaceBlock;

        // Reserve pads, the wreck zone and every settlement (the camps also reserve each other below).
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

        bool blocksAlreadyStamped = FeatureStamped("banditcamps");
        var placed = new List<PlacedSettlement>();
        for (int i = 0; i < count; i++)
        {
            long instSeed = cSeed ^ unchecked((long)(i + 1) * (long)0x9E3779B97F4A7C15);
            var ir = new System.Random(unchecked((int)(instSeed ^ (instSeed >> 32))));
            var structure = BanditCampGenerator.Generate(instSeed, surface, _content);

            if (!TryPlaceSettlement(structure, ir, reserved, wantIsland: false, out var origin, out int groundY, out bool onIsland))
            {
                continue;
            }

            placed.Add(new PlacedSettlement
            {
                Structure = structure,
                Origin = origin,
                GroundY = groundY,
                Tier = "camp",
                Ruined = false,
                OnIsland = onIsland,
                Name = "bandit_camp_" + i,
                Rng = ir,
            });
            reserved.Add((origin.X + structure.Width / 2, origin.Z + structure.Length / 2,
                structure.Width / 2 + 1, structure.Length / 2 + 1));
        }

        if (placed.Count == 0)
        {
            return;
        }

        // Voxels: one-time only (razed camps must stay razed — the ruins rule).
        if (!blocksAlreadyStamped)
        {
            _repo.RunInTransaction(() =>
            {
                foreach (var p in placed)
                {
                    StampSettlementBlocks(p, surface);
                }
            });
            MarkFeatureStamped("banditcamps");
        }

        // Instances + guards: re-derived every load. The stash is idempotent via GeneratedLoot; the
        // guards only spawn while the camp has not been cleared (persisted per camp).
        foreach (var p in placed)
        {
            var camp = new BanditCampInstance
            {
                Min = new Vector3i(p.Origin.X, p.GroundY, p.Origin.Z),
                Max = new Vector3i(p.Origin.X + p.Structure.Width, p.GroundY + p.Structure.Height, p.Origin.Z + p.Structure.Length),
                Center = new Vector3f(p.Origin.X + p.Structure.Width / 2f, p.GroundY + 1, p.Origin.Z + p.Structure.Length / 2f),
                Key = $"{p.Origin.X}:{p.Origin.Z}",
            };
            foreach (var m in p.Structure.Markers)
            {
                var pos = new Vector3f(p.Origin.X + m.LocalPos.X + 0.5f, p.GroundY + m.LocalPos.Y + 0.5f, p.Origin.Z + m.LocalPos.Z + 0.5f);
                camp.Markers.Add((m.Type, pos));
                if (m.Type == "loot")
                {
                    SpawnStructureLoot("bandit_camp", "bandit_stash", pos, p.Rng);
                }
            }

            camp.Cleared = FeatureStamped("banditcamp:" + camp.Key + ":cleared");
            _banditCamps.Add(camp);
            if (!camp.Cleared)
            {
                SpawnBanditCampGuards(camp, p.Rng);
            }
        }

        _log.Info($"Bandit camps on '{_world.LocationId}': {placed.Count} placed, " +
                  $"{_banditCamps.Count(c => c.Cleared)} already cleared.");
    }

    // ---------------- Test hooks ----------------

    /// <summary>Test/util: the bandit camps derived for the active world.</summary>
    public IReadOnlyList<(Vector3f Center, bool Cleared, int GuardMarkers)> BanditCampsForTest()
        => _banditCamps.Select(c => (c.Center, c.Cleared, c.Markers.Count(m => m.Type == "bandit"))).ToList();

    /// <summary>Test/util: materialises a bandit camp instance with live guards at a position, bypassing
    /// the worldgen placement roll (which is seed-dependent). Returns the camp key.</summary>
    public string SpawnBanditCampForTest(Vector3f center, int guards = 2)
    {
        var camp = new BanditCampInstance
        {
            Min = new Vector3i((int)center.X - 8, (int)center.Y - 1, (int)center.Z - 8),
            Max = new Vector3i((int)center.X + 8, (int)center.Y + 6, (int)center.Z + 8),
            Center = center,
            Key = $"{(int)center.X}:{(int)center.Z}",
        };
        for (int i = 0; i < guards; i++)
        {
            camp.Markers.Add(("bandit", new Vector3f(center.X + i * 2f, center.Y, center.Z)));
        }

        _banditCamps.Add(camp);
        SpawnBanditCampGuards(camp, new System.Random(7));
        return camp.Key;
    }

    /// <summary>Test/util: whether the given camp is flagged cleared.</summary>
    public bool BanditCampClearedForTest(string key)
        => _banditCamps.Any(c => c.Key == key && c.Cleared);

    /// <summary>Test/util: whether a one-time feature key is persisted for the active world.</summary>
    public bool FeatureStampedForTest(string feature) => FeatureStamped(feature);
}
