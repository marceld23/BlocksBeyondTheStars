// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>The pens' animals and their products (#3597) — see GameServerSettlementFarms.cs.</summary>
public sealed partial class GameServer
{
    /// <summary>The species a pen keeps: a passive or skittish land species of the world, not a giant, at most middling
    /// size — a hash of the pen's key picks it. Null when the world has none (or its wildlife is switched off). #3976: from
    /// generation 49 the pick may be a kept Earth pen animal (a cow, a sheep, a chicken) — every pen on a home world of one, half
    /// of them elsewhere (<see cref="BlocksBeyondTheStars.WorldGeneration.KeptAnimals.PoolFor"/>); an older world picks among
    /// its roster exactly as before.</summary>
    private CreatureSpecies? PenSpecies(SettlementFarmArea pen)
    {
        if (_world.Planet?.Void == true || Rules.CreatureAbundance == BlocksBeyondTheStars.Shared.Configuration.AlienActivity.Off)
        {
            return null;
        }

        string key = "pen:species:" + _world.LocationId + ":" + pen.Key;
        var native = _speciesRoster
            .Where(sp => sp.Habitat == CreatureHabitat.Land && sp.Temperament is CreatureTemperament.Passive or CreatureTemperament.Skittish
                         && sp.GiantHeight <= 0f && sp.Size <= 1.6f)
            .ToList();
        var pool = BlocksBeyondTheStars.WorldGeneration.KeptAnimals.PoolFor(key, _meta.Seed, Kept.PenAnimals, native, Kept.PensAtHome);
        return pool.Count == 0 ? null : pool[StableHashing.Index(key, _meta.Seed, pool.Count)];
    }

    /// <summary>What a pen's animals give (#3597): an egg from a winged or two-legged kind, milk from a large one, wool from the
    /// rest. A kept Earth species lays its record's item instead (<see cref="PenProductFor"/>, #3976).</summary>
    internal static string PenProductOf(CreatureSpecies sp)
        => sp.HasWings || sp.Legs <= 2 ? "egg" : sp.Size >= 1.1f ? "milk" : "wool";

    /// <summary>Keeps two or three animals in a pen; true when one was added.</summary>
    private bool SyncPenAnimals(SettlementFarmArea pen)
    {
        if (PenSpecies(pen) is not { } sp)
        {
            return false;
        }

        string owner = PenOwnerPrefix + pen.Key;
        int want = 2 + StableHashing.Index("pen:count:" + pen.Key, _meta.Seed, 2);
        int have = _creatures.Count(c => c.OwnerId == owner);
        for (int i = have; i < want; i++)
        {
            _creatures.Add(new CombatEntity
            {
                Id = NextEntityId(),
                Kind = CombatEntityKind.Creature,
                SpeciesId = sp.Id,
                Hostile = false,
                Hull = sp.MaxHealth,
                HullMax = sp.MaxHealth,
                Position = PenSpot(sp, pen, i),
                DamagePerSecond = 0f,
                SizeScale = PenAnimalScale(sp),
                AnimalSeed = CoatVariants.AnimalSeedOf($"pen:{pen.Key}:{i}"), // #3972: the same animal on every visit
                OwnerId = owner,
                CompanionId = owner,
                HomeSpot = PenCentre(pen),
            });
        }

        return want > have;
    }

    private static Vector3f PenCentre(SettlementFarmArea pen)
        => new(pen.MinX + (pen.W / 2f), pen.FeetY, pen.MinZ + (pen.L / 2f));

    private Vector3f PenSpot(CreatureSpecies sp, SettlementFarmArea pen, int i)
    {
        int x = pen.MinX + 1 + StableHashing.Index($"pen:spot:{pen.Key}:{i}", _meta.Seed, System.Math.Max(1, pen.W - 2));
        int z = pen.MinZ + 1 + StableHashing.Index($"pen:spot:z:{pen.Key}:{i}", _meta.Seed, System.Math.Max(1, pen.L - 2));
        return AdjustHabitatHeight(sp, new Vector3f(x + 0.5f, pen.FeetY, z + 0.5f));
    }

    /// <summary>Moves a companion no player owns — the tamer's pet after its NPC, a pen's animal in its pen; false for a
    /// player's companion.</summary>
    private bool MoveNonPlayerCompanion(CombatEntity c, CreatureSpecies sp, double moveDt)
    {
        if (NpcPetOwner(c) is { } tamer)
        {
            MoveTamerPet(c, sp, tamer.Pos, moveDt);
            return true;
        }

        if (IsPenAnimal(c))
        {
            MovePenAnimal(c, sp, moveDt);
            return true;
        }

        return false;
    }

    /// <summary>A pen's animal ambles about the middle of its pen; one that got out is put back in.</summary>
    private void MovePenAnimal(CombatEntity c, CreatureSpecies sp, double moveDt)
    {
        var pen = _worlds.Active.SettlementPens.FirstOrDefault(p => PenOwnerPrefix + p.Key == c.OwnerId);
        if (pen is null)
        {
            return;
        }

        int x = (int)System.Math.Floor(c.Position.X), z = (int)System.Math.Floor(c.Position.Z);
        if (!pen.Contains(x, z, _world.Circumference))
        {
            c.Position = PenSpot(sp, pen, StableHashing.Index("pen:back:" + c.Id, _meta.Seed, 8));
            return;
        }

        var profile = ProfileFor(c.SpeciesId);
        var res = LocomotionController.FollowStep(c.Loco, profile, c.Position, PenCentre(pen),
            System.Math.Max(1f, (System.Math.Min(pen.W, pen.L) / 2f) - 1f), moveDt, unchecked((uint)StableHashing.Seed32(Hash(c.Id, "wander"))));
        c.Loco = res.State;
        ApplyCreatureStep(c, sp, EffectiveMotion(c, sp), res.Position, res.VertWave, profile, moveDt,
            res.State.Mode == MoveMode.Seek ? MoveMode.Seek : MoveMode.Roam, res.Moving, terrainGates: false);
    }

    /// <summary>Lays one product into the pen's first crate with room, every <see cref="PenProductSeconds"/>, up to the cap.</summary>
    private void LayPenProduct(SettlementFarmArea pen)
    {
        if (double.IsNaN(pen.NextProductAt))
        {
            pen.NextProductAt = _uptime + PenProductSeconds;
            return;
        }

        if (_uptime < pen.NextProductAt || pen.Boxes.Count == 0 || PenSpecies(pen) is not { } sp)
        {
            return;
        }

        pen.NextProductAt = _uptime + PenProductSeconds;
        string product = PenProductFor(sp);
        var boxes = pen.Boxes.Select(ContainerAt).Where(b => b is not null).ToList();
        if (boxes.Sum(b => b!.Items.Where(s => s.Item == product).Sum(s => s.Count)) >= PenProductCap || boxes.Count == 0)
        {
            return;
        }

        var box = boxes[0]!;
        if (box.Items.FirstOrDefault(s => s.Item == product && s.Count < 99) is { } stack)
        {
            stack.Count++;
        }
        else
        {
            box.Items.Add(new ItemStack(product, 1));
        }

        _repo.SaveContainer(box);
        _containerSystem.BroadcastContainers();
    }

    /// <summary>Test seam: the pens of the active world (key, product crates, animals in it).</summary>
    internal IReadOnlyList<(string Key, int Boxes, int Animals)> PensForTest
        => _worlds.Active.SettlementPens.Select(p => (p.Key, p.Boxes.Count, _creatures.Count(c => c.OwnerId == PenOwnerPrefix + p.Key))).ToList();

    /// <summary>Test seam: the fields of the active world (corner, size).</summary>
    internal IReadOnlyList<(int X, int Z, int W, int L)> FieldsForTest
        => _worlds.Active.SettlementFields.Select(f => (f.MinX, f.MinZ, f.W, f.L)).ToList();

    /// <summary>Test seam: lays a pen's product now.</summary>
    internal void LayPenProductsForTest()
    {
        foreach (var pen in _worlds.Active.SettlementPens)
        {
            pen.NextProductAt = _uptime;
            LayPenProduct(pen);
        }
    }
}
