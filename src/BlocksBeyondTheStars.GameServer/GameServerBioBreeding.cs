// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Bio;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.State;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Cloning and crossing from samples (#2207, #2208) and bred plants (#2209).
/// <list type="bullet">
/// <item><b>The clone tank takes samples.</b> A species that is not in this world's roster is registered on the world as
/// a guest from the save's species register — the same snapshot a companion carries — so it moves, renders, is scanned
/// and tamed like any other. Hostile species and the giants are never grown.</item>
/// <item><b>A cross is deterministic.</b> The child's seed is a function of the pair, so the same two parents always
/// give the same child; nothing is re-rolled. A cross is never hostile.</item>
/// <item><b>A bred plant</b> is one block whose look rides on its voxel (colour in the tint, form in the glow channel,
/// see <see cref="FloraForm"/>); which species stands in a cell is remembered per world, so a harvest yields its sample
/// and the regrowth puts the same plant back.</item>
/// </list>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Seconds a harvested bred plant takes to grow back — slower than a wild one (30 s).</summary>
    private const double BredRegrowSeconds = 90.0;

    /// <summary>How far around a tank the habitat rule looks for water or lava.</summary>
    private const int TankHabitatReach = 8;

    private const string TankPartnerKey = "x";
    private const string TankGrowKey = "grow";
    private const string TankSamplePrefix = "g:";

    private ushort _hybridId;

    private Dictionary<Vector3i, uint> _bredPlants => _worlds.Active.BredPlants;

    private static string BioPlantsBlob(string locationId) => "bio:plants:" + locationId;

    // ---------------- Guest species ----------------

    /// <summary>The species a register entry grows on this world: the native roster species when the sample was taken
    /// here, otherwise a guest registered from the snapshot. Null for anything that is no animal.</summary>
    private CreatureSpecies? GuestSpeciesOf(BioSpeciesEntry? entry)
    {
        if (entry?.Creature is not { } snapshot)
        {
            return null;
        }

        if (entry.OriginBodyId == _world.LocationId && !entry.IsCross)
        {
            foreach (var native in _speciesRoster)
            {
                if (BioSeedOfCreature(native) == entry.Seed)
                {
                    return native;
                }
            }
        }

        string id = GuestId(entry.Seed);
        if (!_speciesById.TryGetValue(id, out var sp))
        {
            sp = CloneSpecies(snapshot);
            sp.Id = id;
            // A guest belongs to no biome of this world and to no herd: it is one animal beside its tank.
            sp.BiomeAffinity = -1;
            sp.BiomeSurfaces = Array.Empty<string>();
            sp.BiomeExclusive = false;
            sp.SocialGroupSize = 1;
            _speciesById[id] = sp;
            _locoProfiles[id] = LocomotionController.ForSpecies(sp);
        }

        return sp;
    }

    /// <summary>The register entry a tank choice names: <c>g:&lt;seed&gt;</c> for a sample, or a native species id.</summary>
    private BioSpeciesEntry? TankEntry(string? choice)
    {
        if (string.IsNullOrEmpty(choice))
        {
            return null;
        }

        if (choice!.StartsWith(TankSamplePrefix, StringComparison.Ordinal))
        {
            return uint.TryParse(choice.AsSpan(TankSamplePrefix.Length), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint seed)
                ? BioEntry(seed)
                : null;
        }

        return _speciesById.TryGetValue(choice, out var sp) ? RegisterCreatureSpecies(sp) : null;
    }

    private static string TankChoice(uint seed) => TankSamplePrefix + seed.ToString("x8", CultureInfo.InvariantCulture);

    /// <summary>The samples in a player's case a tank can work with: every animal but a giant, and every plant that is a
    /// single plant. A species already offered as a native of this world is left out.</summary>
    private void AppendSampleChoices(PlayerState p, List<(string SpeciesId, string Name)> result)
    {
        var native = new HashSet<uint>();
        foreach (var (id, _) in result)
        {
            if (_speciesById.TryGetValue(id, out var sp))
            {
                native.Add(BioSeedOfCreature(sp));
            }
        }

        foreach (var stack in p.SampleCase.Slots)
        {
            if (stack is not { IsEmpty: false } || ItemKey.Base(stack.Item) != BioItems.Sample)
            {
                continue;
            }

            var entry = BioEntry(ItemKey.Seed(stack.Item));
            if (entry is null || native.Contains(entry.Seed)
                || (entry.Kind == BioKind.Animal ? entry.Creature is not { IsGiant: false } : entry.Flora is null))
            {
                continue;
            }

            result.Add((TankChoice(entry.Seed), entry.Name));
        }
    }

    private int LivingClonesInWorld() => _creatures.Count(c => c.CloneOf.Length > 0);

    /// <summary>A water animal needs water near the tank, a lava animal lava.</summary>
    private bool HabitatNear(CreatureSpecies sp, Vector3i tank)
    {
        string? fluid = sp.Habitat switch
        {
            CreatureHabitat.Water or CreatureHabitat.Amphibian => "water",
            CreatureHabitat.Lava => "lava",
            _ => null,
        };
        if (fluid is null)
        {
            return true;
        }

        ushort id = _content.GetBlock(fluid)?.NumericId.Value ?? 0;
        for (int dx = -TankHabitatReach; dx <= TankHabitatReach; dx += 2)
        {
            for (int dz = -TankHabitatReach; dz <= TankHabitatReach; dz += 2)
            {
                for (int dy = -6; dy <= 2; dy++)
                {
                    if (_world.GetBlock(new Vector3i(tank.X + dx, tank.Y + dy, tank.Z + dz)).Value == id)
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    // ---------------- The tank, with samples ----------------

    /// <summary>Starts a tank on a sample: a clone of one species, or a cross of two. The price is one sample per parent
    /// from the owner's sample case and matter dust (two for a clone, four for a cross).</summary>
    private void BioTankStart(ServerCrystalCell tank, PlayerSession? by, string choice, string? partner)
    {
        void Refuse(string key)
        {
            if (by is not null)
            {
                Reject(by, "crystal", key);
            }
        }

        var owner = FindSessionByPlayerId(tank.OwnerId);
        var first = TankEntry(choice);
        if (owner is null || first is null)
        {
            SetCrystalBlocked(tank, false);
            return;
        }

        bool cross = !string.IsNullOrEmpty(partner);
        var second = cross ? TankEntry(partner) : null;
        var p = owner.State;
        bool free = !Rules.CraftingCostsMaterialsFor(p.ModeOverride);
        BioSpeciesEntry grown;
        if (!cross)
        {
            if (first.Creature is not { Hostile: false, IsGiant: false })
            {
                Refuse("@srv.crystal.clone_refused"); // never a hostile one, never a giant; a plant is raised in the lab
                return;
            }

            grown = first;
        }
        else
        {
            if (!BioUnlocked(p, BioItems.CrossingBlueprint))
            {
                Refuse("@srv.crystal.cross_locked");
                return;
            }

            if (second is null || second.Seed == first.Seed || second.Kind != first.Kind
                || (first.Kind == BioKind.Animal ? first.Creature is null || second.Creature is null : first.Flora is null || second.Flora is null))
            {
                Refuse("@srv.crystal.cross_kinds");
                return;
            }

            if (first.Creature is { IsGiant: true } || second.Creature is { IsGiant: true })
            {
                Refuse("@srv.crystal.cross_giant");
                return;
            }

            if (Math.Max(first.Generation, second.Generation) + 1 > BioRules.MaxCrossGeneration)
            {
                Refuse("@srv.crystal.cross_generation");
                return;
            }

            if (!free && (SampleCount(p, first) < 1 || SampleCount(p, second) < 1))
            {
                Refuse("@srv.crystal.clone_sample");
                return;
            }

            if (EnsureCross(first, second) is not { } child)
            {
                Refuse("@srv.bio.register_full");
                return;
            }

            grown = child;
        }

        if (grown.Kind == BioKind.Animal)
        {
            if (LivingClonesOf(tank.OwnerId) >= CrystalNetRules.MaxLivingClonesPerOwner)
            {
                Refuse("@srv.crystal.clone_cap");
                return;
            }

            if (LivingClonesInWorld() >= CrystalNetRules.MaxLivingClonesPerWorld)
            {
                Refuse("@srv.crystal.clone_world_cap");
                return;
            }

            if (GuestSpeciesOf(grown) is not { } sp || !HabitatNear(sp, tank.Cell))
            {
                Refuse("@srv.crystal.clone_habitat");
                return;
            }
        }

        if (!free)
        {
            if (SampleCount(p, first) < 1 || (second is not null && SampleCount(p, second) < 1))
            {
                Refuse("@srv.crystal.clone_sample");
                return;
            }

            if (!TakeFromCrateOrPocket(tank, by ?? owner, new[] { new ItemAmount("matter_dust", cross ? 4 : 2) }))
            {
                Refuse("@srv.crystal.clone_dust");
                return;
            }

            TakeSample(p, first);
            if (second is not null)
            {
                TakeSample(p, second);
            }

            SendInventory(owner);
        }

        tank.Config = CrystalConfigWith(CrystalConfigWith(tank.Config, "growing", "1"), TankGrowKey, TankChoice(grown.Seed));
        tank.NextBeat = _uptime + CrystalNetRules.CloneGrowSeconds;
        SaveCrystalCell(tank);
        SetCrystalBlocked(tank, true); // ON while growing
        BroadcastToWorld(new SoundFx { SoundId = "clone_tank_bubble", X = tank.Cell.X + 0.5f, Y = tank.Cell.Y + 1f, Z = tank.Cell.Z + 0.5f, Loop = true, SourceId = tank.Id });
    }

    /// <summary>What a tank that was started on a sample hands out when it is done: the animal (and, for a cross, a
    /// sample of the new species so it can be cloned and crossed again), or two samples of a new plant. Returns false
    /// while the owner is away and the samples cannot be handed over yet.</summary>
    private bool BioTankFinish(ServerCrystalCell tank, string grow)
    {
        var entry = TankEntry(grow);
        if (entry is null)
        {
            return true; // nothing to hand out (a register that no longer knows the seed)
        }

        var owner = FindSessionByPlayerId(tank.OwnerId);
        if (entry.IsCross && owner is null)
        {
            return false;
        }

        if (entry.Kind == BioKind.Animal)
        {
            if (GuestSpeciesOf(entry) is { Hostile: false, IsGiant: false } sp)
            {
                ReleaseClone(tank, sp);
            }

            if (entry.IsCross && owner is not null && GiveSample(owner, entry))
            {
                SendInventory(owner);
            }
        }
        else if (owner is not null)
        {
            GiveSample(owner, entry, 2);
            SendInventory(owner);
        }

        // From now on the tank clones what it made: the next start costs a sample of the new species.
        if (entry.Kind == BioKind.Animal)
        {
            tank.Config = CrystalConfigWith(CrystalConfigWith(tank.Config, "sp", TankChoice(entry.Seed)), TankPartnerKey, string.Empty);
        }

        tank.Config = CrystalConfigWith(tank.Config, TankGrowKey, string.Empty);
        return true;
    }

    /// <summary>The species a tank's <c>sp</c> names — a native one or a guest from a sample.</summary>
    private CreatureSpecies? TankSpecies(string? choice)
    {
        if (string.IsNullOrEmpty(choice))
        {
            return null;
        }

        return choice!.StartsWith(TankSamplePrefix, StringComparison.Ordinal)
            ? GuestSpeciesOf(TankEntry(choice))
            : _speciesById.TryGetValue(choice, out var sp) ? sp : null;
    }

    // ---------------- Crossing ----------------

    /// <summary>The register entry of the cross of two species — made on first need, the same ever after.</summary>
    private BioSpeciesEntry? EnsureCross(BioSpeciesEntry a, BioSpeciesEntry b)
    {
        uint seed = BioHash.CrossSeed(a.Seed, b.Seed);
        if (BioEntry(seed) is { } known)
        {
            return known;
        }

        // Order the parents by seed, so A × B and B × A build the very same child.
        var (first, second) = a.Seed <= b.Seed ? (a, b) : (b, a);
        var entry = new BioSpeciesEntry
        {
            Seed = seed,
            Kind = first.Kind,
            OriginBodyId = _world.LocationId,
            OriginBodyName = ActiveLocationNames().Planet,
            ParentA = first.Seed,
            ParentB = second.Seed,
            DifferentWorlds = first.OriginBodyId != second.OriginBodyId,
            Generation = Math.Max(first.Generation, second.Generation) + 1,
        };

        var rng = new Random(unchecked((int)seed));
        if (first.Kind == BioKind.Animal)
        {
            entry.Name = WorldGeneration.NameGenerator.Creature(rng);
            entry.Creature = CrossCreatures(first.Creature!, second.Creature!, seed, entry.Name);
        }
        else
        {
            entry.Name = WorldGeneration.NameGenerator.Flora(rng);
            entry.Flora = FloraGenome.Cross(first.Flora!, second.Flora!, seed);
        }

        return TryRegister(entry) ? entry : null;
    }

    /// <summary>
    /// The body of a cross. Body plan and habitat come from the same parent with every trait that belongs to that plan,
    /// so a cross is never a body the client cannot build or the server cannot move; the other parent gives colours and
    /// ornaments. Size, speed and health are mixed. A cross is never hostile and inherits no special behaviour.
    /// </summary>
    private static CreatureSpecies CrossCreatures(CreatureSpecies a, CreatureSpecies b, uint seed, string name)
    {
        ulong bits = BioHash.Draw(seed, 30);
        bool Bit(int n) => ((bits >> n) & 1) == 0;

        var plan = Bit(0) ? a : b;
        var other = Bit(0) ? b : a;
        var child = CloneSpecies(plan); // plan, habitat, limbs, gait and yield of one parent
        child.Id = GuestId(seed);
        child.Name = name;
        child.NameKey = "creature.generic.name";
        child.VoiceSeed = unchecked((int)seed);

        // Mixed: a quarter, a half or three quarters of the way from one parent to the other.
        float t = ((bits >> 1) & 3) switch { 0 => 0.25f, 1 => 0.5f, 2 => 0.5f, _ => 0.75f };
        child.Size = Math.Clamp(plan.Size + (other.Size - plan.Size) * t, 0.5f, 2.2f);
        child.Speed = Math.Clamp(plan.Speed + (other.Speed - plan.Speed) * t, 1.5f, 4f);
        child.MaxHealth = 10f + child.Size * 8f;

        // Never hostile: the calmer temperament of the two, and no bite.
        child.Temperament = Calmer(a.Temperament, b.Temperament);
        child.AttackDamage = 0f;
        child.Activity = Bit(3) ? a.Activity : b.Activity;

        // Colours: body from one parent, belly from the other. Ornaments: one bit each.
        child.ColorRgb = Bit(4) ? a.ColorRgb : b.ColorRgb;
        child.BellyRgb = Bit(4) ? b.ColorRgb : a.ColorRgb;
        child.EyeRgb = Bit(5) ? a.EyeRgb : b.EyeRgb;
        child.Eyes = Bit(6) ? a.Eyes : b.Eyes;
        child.Horns = Bit(7) ? a.Horns : b.Horns;
        child.HasCrest = Bit(8) ? a.HasCrest : b.HasCrest;
        child.HasTail = Bit(9) ? a.HasTail : b.HasTail;
        child.EyeStalks = Bit(10) ? a.EyeStalks : b.EyeStalks;
        child.Glows = Bit(11) ? a.Glows : b.Glows;
        if (!string.IsNullOrEmpty(other.Hide) && Bit(12))
        {
            child.Hide = other.Hide;
        }

        // One pair in eight shows a trait neither parent has.
        if (((bits >> 16) & 7) == 0)
        {
            switch ((bits >> 19) % 5)
            {
                case 0: child.Glows = true; break;
                case 1: child.Horns = Math.Min(4, Math.Max(a.Horns, b.Horns) + 1); break;
                case 2: child.HasCrest = true; break;
                case 3: child.ColorRgb = ((child.ColorRgb & 0xFF) << 16) | ((child.ColorRgb >> 8) & 0xFFFF); break;
                default:
                    if (child.BodyPlan is CreatureBodyPlan.Standard or CreatureBodyPlan.Titan)
                    {
                        child.Heads = Math.Min(3, Math.Max(1, child.Heads) + 1);
                    }
                    else
                    {
                        child.Size = Math.Min(2.2f, child.Size * 1.2f);
                    }

                    break;
            }
        }

        // An ordinary animal: one of its kind beside the tank, at home in no biome, with none of a parent's special
        // behaviour (begging, gifts, anger at mining) and none of a giant's measures.
        child.SocialGroupSize = 1;
        child.BiomeAffinity = -1;
        child.BiomeSurfaces = Array.Empty<string>();
        child.BiomeExclusive = false;
        child.AngeredByMining = false;
        child.GiftsWhenCalm = false;
        child.BegsForFood = false;
        child.FavouriteFood = string.Empty;
        child.FeedsToTame = 0;
        child.SwallowsCreatures = false;
        child.HasFins = CreatureMotion.FinsFor(child);
        return child;
    }

    private static CreatureTemperament Calmer(CreatureTemperament a, CreatureTemperament b)
    {
        static int Rank(CreatureTemperament t) => t switch
        {
            CreatureTemperament.Passive => 0,
            CreatureTemperament.Skittish => 1,
            CreatureTemperament.Territorial => 2,
            _ => 3,
        };

        var calm = Rank(a) <= Rank(b) ? a : b;
        return Rank(calm) >= 3 ? CreatureTemperament.Territorial : calm;
    }

    // ---------------- Bred plants ----------------

    private bool IsBredPlant(ushort blockId) => _hybridId != 0 && blockId == _hybridId;

    private void LoadBredPlants()
    {
        var world = _worlds.Active;
        world.BredPlants.Clear();
        if (_repo.LoadNamedBlob(BioPlantsBlob(world.LocationId)) is not { Length: > 0 } json)
        {
            return;
        }

        try
        {
            foreach (var cell in JsonSerializer.Deserialize<List<long[]>>(json) ?? new List<long[]>())
            {
                if (cell is { Length: 4 })
                {
                    world.BredPlants[new Vector3i((int)cell[0], (int)cell[1], (int)cell[2])] = (uint)cell[3];
                }
            }
        }
        catch (JsonException ex)
        {
            _log.Warn($"The bred plants of '{world.LocationId}' could not be read ({ex.Message}).");
        }
    }

    private void SaveBredPlants()
        => _repo.SaveNamedBlob(BioPlantsBlob(_world.LocationId),
            JsonSerializer.Serialize(_bredPlants.Select(kv => new long[] { kv.Key.X, kv.Key.Y, kv.Key.Z, kv.Value }).ToList()));

    // Cells are remembered by their canonical position: on a round world the same cell has many coordinates.
    private Vector3i BredKey(Vector3i pos) => Shared.World.WorldConstants.CanonicalBlock(pos, _world.Circumference);

    private uint BredSeedAt(Vector3i pos) => _bredPlants.TryGetValue(BredKey(pos), out uint seed) ? seed : 0;

    /// <summary>The colour and the packed form a bred plant's voxel carries.</summary>
    private (int Tint, int Glow) BredStamp(uint seed)
        => BioEntry(seed)?.Flora is { } genome ? (genome.TintRgb & 0xFFFFFF, FloraForm.Pack(genome)) : (0, 0);

    /// <summary>The soil of a bred plant: any natural plant ground that is not tainted — or a hydro tray or a flower pot.</summary>
    private bool BredHostValid(Vector3i pos)
    {
        var below = _content.BlockById(_world.GetBlock(new Vector3i(pos.X, pos.Y - 1, pos.Z)));
        if (below is null)
        {
            return false;
        }

        return below.Key is "hydro_tray" or "flower_pot"
            || (below.FloraHost && !below.Key.StartsWith("tainted_", StringComparison.Ordinal));
    }

    /// <summary>The air of a bred plant: the open air of a world whose atmosphere is not corrosive, or the air of a base
    /// or a station.</summary>
    private bool BredAirOk(Vector3i pos)
        => BreathableAirAt(pos)
           || (AtmospherePresent && !ActiveTraits.CorrosiveAir && _world.Planet.AirDamagePerSecond <= 0);

    /// <summary>Why a seedling cannot be planted in a cell, or null when it can.</summary>
    private string? BredPlantRefusal(string seedlingKey, Vector3i pos)
    {
        if (BioEntry(ItemKey.Seed(seedlingKey))?.Flora is null)
        {
            return "@srv.bio.not_plantable";
        }

        if (!_bredPlants.ContainsKey(BredKey(pos)) && _bredPlants.Count >= BioRules.MaxBredPlantsPerWorld)
        {
            PruneBredPlants();
            if (_bredPlants.Count >= BioRules.MaxBredPlantsPerWorld)
            {
                return "@srv.bio.plant_cap";
            }
        }

        if (!BredHostValid(pos))
        {
            return "@srv.bio.plant_soil";
        }

        return BredAirOk(pos) ? null : "@srv.bio.plant_air";
    }

    private void RememberBredPlant(Vector3i pos, uint seed)
    {
        _bredPlants[BredKey(pos)] = seed;
        SaveBredPlants();
    }

    private void ForgetBredPlant(Vector3i pos)
    {
        if (_bredPlants.Remove(BredKey(pos)))
        {
            SaveBredPlants();
        }
    }

    /// <summary>Drops the cells whose plant is gone for good (burnt, flooded, dug away): a loaded cell that neither holds
    /// a bred plant nor waits to regrow one.</summary>
    private void PruneBredPlants()
    {
        var gone = new List<Vector3i>();
        foreach (var pos in _bredPlants.Keys)
        {
            if (!_floraRegrow.ContainsKey(pos) && _world.GetBlockIfLoaded(pos) is var block && !block.IsAir && !IsBredPlant(block.Value))
            {
                gone.Add(pos);
            }
        }

        foreach (var pos in gone)
        {
            _bredPlants.Remove(pos);
        }

        if (gone.Count > 0)
        {
            SaveBredPlants();
        }
    }

    /// <summary>What a harvested bred plant yields: what its body parent's form yields — the poisonous twin for a toxic one.</summary>
    private List<ItemAmount> BredYield(uint seed)
    {
        if (BioEntry(seed)?.Flora is not { } genome || _content.GetBlock(genome.BodyBlock) is not { } body)
        {
            return new List<ItemAmount>();
        }

        return body.Drops.Select(d => new ItemAmount(genome.Toxic ? ToxicCounterpart(d.Item) : d.Item, d.Count)).ToList();
    }

    // ---------------- Test seams ----------------

    /// <summary>Test seam: the cross of two registered species.</summary>
    public BioSpeciesEntry? CrossForTest(uint a, uint b)
        => BioEntry(a) is { } first && BioEntry(b) is { } second ? EnsureCross(first, second) : null;

    /// <summary>Test seam: the body of a cross, without a server.</summary>
    public static CreatureSpecies CrossCreaturesForTest(CreatureSpecies a, CreatureSpecies b, uint seed) => CrossCreatures(a, b, seed, "Cross");

    /// <summary>Test seam: the species seed of the bred plant in a cell (0 for none).</summary>
    public uint BredSeedAtForTest(Vector3i pos) => BredSeedAt(pos);

    /// <summary>Test seam: the tank choice string of a sample.</summary>
    public static string TankChoiceForTest(uint seed) => TankChoice(seed);
}
