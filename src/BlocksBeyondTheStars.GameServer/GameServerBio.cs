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
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The bio lab's foundation (#2200, #2201): the save's species register, each player's research book, and the samples.
/// <list type="bullet">
/// <item><b>Samples, not seeded stacks.</b> A harvest yields exactly what it always did — and one sample that carries
/// the species seed in its key, into the player's sample case. No stack splits, no recipe has to change.</item>
/// <item><b>The register.</b> The first sample of a species registers it for the save: name, origin, context and, for
/// animals, the species snapshot companions already carry. Cloning and crossing read it; a profile is never stored
/// (it is derived from seed and context, see <see cref="BioProfiles"/>).</item>
/// <item><b>Kept out of the autosave.</b> Register and research books live in named blobs written only on change — the
/// player record that is rewritten on every autosave stays as small as it was.</item>
/// </list>
/// </summary>
public sealed partial class GameServer
{
    private const string BioRegisterBlob = "bio:register";

    /// <summary>Seconds a changed register may wait before it is written (it is also written with every save).</summary>
    private const double BioRegisterFlushSeconds = 20.0;

    /// <summary>Seconds before the same living animal gives another sample to the sampler.</summary>
    private const double SamplerCooldownPerAnimal = 300.0;

    /// <summary>Reach of the sampler around its aim point; a giant is reached from much further off.</summary>
    private const float SamplerRange = 6f;
    private const float SamplerGiantRange = 48f;

    private readonly Dictionary<uint, BioSpeciesEntry> _bioRegister = new();
    private bool _bioRegisterDirty;
    private double _bioRegisterDirtySince;
    private readonly Dictionary<string, BioResearch> _bioResearch = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _bioSampledAt = new(StringComparer.Ordinal); // entity id → uptime it may be sampled again

    /// <summary>What one player has found out in the lab. Persisted per player in a named blob.</summary>
    private sealed class BioResearch
    {
        public HashSet<uint> Analysed { get; set; } = new();
        public HashSet<string> Reactions { get; set; } = new(StringComparer.Ordinal);
        public List<string> Changes { get; set; } = new();
    }

    private static string BioResearchBlob(string playerId) => "bio:research:" + playerId;

    // ---------------- Register ----------------

    private void LoadBioRegister()
    {
        _bioRegister.Clear();
        _bioRegisterDirty = false;
        if (_repo.LoadNamedBlob(BioRegisterBlob) is not { Length: > 0 } json)
        {
            return;
        }

        try
        {
            foreach (var entry in JsonSerializer.Deserialize<List<BioSpeciesEntry>>(json) ?? new List<BioSpeciesEntry>())
            {
                if (entry is { Seed: not 0 })
                {
                    _bioRegister[entry.Seed] = entry;
                }
            }
        }
        catch (JsonException ex)
        {
            _log.Warn($"The species register could not be read ({ex.Message}); starting with an empty one.");
        }
    }

    private void MarkBioRegisterDirty()
    {
        if (!_bioRegisterDirty)
        {
            _bioRegisterDirty = true;
            _bioRegisterDirtySince = _uptime;
        }
    }

    /// <summary>Writes the register when it changed — at once with <paramref name="force"/> (a save), otherwise a little
    /// later, so a walk through a new world writes it once, not once per species.</summary>
    private void FlushBioRegister(bool force)
    {
        if (!_bioRegisterDirty || (!force && _uptime - _bioRegisterDirtySince < BioRegisterFlushSeconds))
        {
            return;
        }

        _bioRegisterDirty = false;
        _repo.SaveNamedBlob(BioRegisterBlob, JsonSerializer.Serialize(_bioRegister.Values.ToList()));
    }

    private BioSpeciesEntry? BioEntry(uint seed) => seed != 0 && _bioRegister.TryGetValue(seed, out var e) ? e : null;

    private bool TryRegister(BioSpeciesEntry entry)
    {
        if (_bioRegister.Count >= BioRules.MaxRegisterEntries)
        {
            return false; // a bound for the save; a save this full has sampled five hundred species
        }

        _bioRegister[entry.Seed] = entry;
        MarkBioRegisterDirty();
        return true;
    }

    private long ActiveRosterSeed => WorldGenerator.RosterSeedFor(_meta.Seed, _world.LocationId);

    /// <summary>The name the client keys this world's flora colours by: "system · planet", as it composes it on join.</summary>
    private string ActiveLocationDisplayName()
    {
        var (system, planet) = ActiveLocationNames();
        return string.IsNullOrEmpty(system) ? planet : $"{system} · {planet}";
    }

    /// <summary>The seed of an animal species: a guest or a cross carries it in its id, an authored species has the same
    /// on every world, a rolled one folds it from the voice seed its roster gave it.</summary>
    private static uint BioSeedOfCreature(CreatureSpecies sp)
    {
        if (GuestSeed(sp.Id) is { } guest)
        {
            return guest;
        }

        if (sp.Id.StartsWith("au_", StringComparison.Ordinal))
        {
            return BioHash.AuthoredSeed(sp.Id.Substring(3));
        }

        return BioHash.CreatureSeed(sp.VoiceSeed != 0 ? sp.VoiceSeed : StableStringHash(sp.Name + "|" + sp.Id));
    }

    private const string GuestPrefix = "gx";

    private static string GuestId(uint seed) => GuestPrefix + seed.ToString("x8", CultureInfo.InvariantCulture);

    private static uint? GuestSeed(string speciesId)
        => speciesId.Length == 10 && speciesId.StartsWith(GuestPrefix, StringComparison.Ordinal)
           && uint.TryParse(speciesId.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint seed)
            ? seed
            : null;

    /// <summary>The register entry of an animal species of the active world, made on first need.</summary>
    private BioSpeciesEntry? RegisterCreatureSpecies(CreatureSpecies sp)
    {
        uint seed = BioSeedOfCreature(sp);
        if (BioEntry(seed) is { } known)
        {
            return known;
        }

        var (_, planetName) = ActiveLocationNames();
        var entry = new BioSpeciesEntry
        {
            Seed = seed,
            Kind = BioKind.Animal,
            Name = sp.Name,
            OriginBodyId = _world.LocationId,
            OriginBodyName = planetName,
            Context = BioContexts.ForCreature(sp, _world.Planet),
            Creature = CloneSpecies(sp),
        };
        return TryRegister(entry) ? entry : null;
    }

    /// <summary>The register entry of what a plant block of the active world is a sample of — a catalog species, the
    /// world's tree, or null for a block that is no species (a sapling).</summary>
    private BioSpeciesEntry? RegisterFloraSpecies(string blockKey)
    {
        var planet = _world.Planet;
        var (_, planetName) = ActiveLocationNames();

        if (TreeSpeciesForBlock(blockKey) is { } tree)
        {
            uint treeSeed = BioHash.FloraSeed(ActiveRosterSeed, planet.Key, "tree:" + tree.Id);
            return BioEntry(treeSeed) ?? Registered(new BioSpeciesEntry
            {
                Seed = treeSeed,
                Kind = BioKind.Plant,
                Name = tree.Name,
                OriginBodyId = _world.LocationId,
                OriginBodyName = planetName,
                Context = BioContexts.ForTree(tree.Toxic, planet),
            });
        }

        if (FloraCatalog.Find(blockKey) is not { } archetype || _content.GetBlock(blockKey) is not { } block)
        {
            return null;
        }

        bool sameEverywhere = archetype.Authored || archetype.Cultivated;
        uint seed = sameEverywhere ? BioHash.AuthoredSeed(blockKey) : BioHash.FloraSeed(ActiveRosterSeed, planet.Key, blockKey);
        if (BioEntry(seed) is { } known)
        {
            return known;
        }

        var species = FloraSpeciesForBlock(blockKey);
        bool toxic = species?.Toxic ?? false;
        bool onTheme = sameEverywhere || (FloraThemes.Resolve(planet.FloraTheme).Preferred & archetype.Tags) != 0;
        var entry = new BioSpeciesEntry
        {
            Seed = seed,
            Kind = BioKind.Plant,
            Name = species?.Name ?? string.Empty,
            OriginBodyId = _world.LocationId,
            OriginBodyName = planetName,
            Context = BioContexts.ForFlora(archetype, toxic, onTheme, planet, block.Drops.Select(d => d.Item)),
        };

        if (FloraForm.Breedable(blockKey))
        {
            // A clone keeps the look it has at home: its own form, plain, in this world's colour for the species.
            var colour = Shared.World.FloraTints.For(_meta.Seed, ActiveLocationDisplayName(), blockKey);
            entry.Flora = new FloraGenome
            {
                BodyBlock = blockKey,
                CrownBlock = blockKey,
                Layout = FloraForm.LayoutPlain,
                Size = archetype.Height == FloraHeight.Tall ? 2 : 1,
                TintRgb = FloraCatalog.KeepsOwnColour(blockKey) ? 0 : Shared.World.FloraTints.ToRgb24(colour, 1f),
                Glow = archetype.Light >= 0.45f ? 2 : archetype.Light > 0f ? 1 : 0,
                Toxic = toxic,
            };
        }

        return Registered(entry);
    }

    /// <summary>The register entry of a deposit of the active world: one material on this body.</summary>
    private BioSpeciesEntry? RegisterDeposit(BlockDefinition block, string materialItem)
    {
        uint seed = BioHash.DepositSeed(ActiveRosterSeed, materialItem);
        if (BioEntry(seed) is { } known)
        {
            return known;
        }

        var (_, planetName) = ActiveLocationNames();
        var planet = _world.Planet;
        bool tainted = block.Key.StartsWith("tainted_", StringComparison.Ordinal);
        string veinBlock = tainted ? block.Key.Substring("tainted_".Length) : block.Key;
        return Registered(new BioSpeciesEntry
        {
            Seed = seed,
            Kind = BioKind.Mineral,
            OriginBodyId = _world.LocationId,
            OriginBodyName = planetName,
            Context = BioContexts.ForDeposit(planet, planet.Ores.FirstOrDefault(v => v.Block == block.Key || v.Block == veinBlock), tainted),
            MaterialItem = materialItem,
        });
    }

    private BioSpeciesEntry? Registered(BioSpeciesEntry entry) => TryRegister(entry) ? entry : null;

    /// <summary>The species seed of a plant or tree block of the active world, without registering it (0 for a block that
    /// is no species) — the same seeds <see cref="RegisterFloraSpecies"/> gives.</summary>
    private uint BioSeedOfPlantBlock(string blockKey)
    {
        var planet = _world.Planet;
        if (TreeSpeciesForBlock(blockKey) is { } tree)
        {
            return BioHash.FloraSeed(ActiveRosterSeed, planet.Key, "tree:" + tree.Id);
        }

        if (FloraCatalog.Find(blockKey) is not { } archetype)
        {
            return 0;
        }

        return archetype.Authored || archetype.Cultivated
            ? BioHash.AuthoredSeed(blockKey)
            : BioHash.FloraSeed(ActiveRosterSeed, planet.Key, blockKey);
    }

    /// <summary>The scan line about a species' substance: known once the player has analysed it in a bio lab.</summary>
    private string SubstanceTrait(PlayerState p, uint seed)
        => ResearchOf(p.PlayerId).Analysed.Contains(seed) ? "ui.scan.substance_known" : "ui.scan.substance_unknown";

    private BioProfile? BioProfileOf(uint seed) => BioSpeciesProfiles.ProfileOf(BioEntry(seed), BioEntry);

    private MaterialProfile? MaterialProfileOf(uint seed)
        => BioSpeciesProfiles.MaterialOf(BioEntry(seed), item => _content.GetItem(item)?.LabTraits);

    // ---------------- Research ----------------

    private BioResearch ResearchOf(string playerId)
    {
        if (_bioResearch.TryGetValue(playerId, out var research))
        {
            return research;
        }

        research = new BioResearch();
        if (_repo.LoadNamedBlob(BioResearchBlob(playerId)) is { Length: > 0 } json)
        {
            try
            {
                research = JsonSerializer.Deserialize<BioResearch>(json) ?? research;
                research.Reactions = new HashSet<string>(research.Reactions ?? new HashSet<string>(), StringComparer.Ordinal);
                research.Analysed ??= new HashSet<uint>();
                research.Changes ??= new List<string>();
            }
            catch (JsonException ex)
            {
                _log.Warn($"The research book of '{playerId}' could not be read ({ex.Message}); starting with an empty one.");
                research = new BioResearch();
            }
        }

        _bioResearch[playerId] = research;
        return research;
    }

    private void SaveResearch(string playerId)
    {
        if (_bioResearch.TryGetValue(playerId, out var research))
        {
            _repo.SaveNamedBlob(BioResearchBlob(playerId), JsonSerializer.Serialize(research));
        }
    }

    // ---------------- The research book on the wire ----------------

    private NetBioSpecies ToNetSpecies(BioSpeciesEntry e, BioResearch research) => new()
    {
        Seed = e.Seed,
        Kind = (int)e.Kind,
        Name = e.Name,
        OriginBodyId = e.OriginBodyId,
        OriginBodyName = e.OriginBodyName,
        HasContext = e.Context is not null,
        Tags = e.Context?.Tags ?? 0,
        RarityPoints = e.Context?.RarityPoints ?? 0,
        Toxicity = e.Context?.Toxicity ?? 0,
        Carrier = (int)(e.Context?.Carrier ?? BioCarrier.Fibre),
        ParentA = e.ParentA,
        ParentB = e.ParentB,
        DifferentWorlds = e.DifferentWorlds,
        Generation = e.Generation,
        MaterialItem = e.MaterialItem,
        BodyBlock = e.Flora?.BodyBlock ?? string.Empty,
        CrownBlock = e.Flora?.CrownBlock ?? string.Empty,
        Cloneable = e.Creature is { Hostile: false, IsGiant: false },
        Analysed = research.Analysed.Contains(e.Seed),
    };

    /// <summary>The seeds a player's items carry: the sample case, and the seedlings and samples in the backpack.</summary>
    private static IEnumerable<uint> HeldSeeds(PlayerState p)
    {
        foreach (var inv in new[] { p.SampleCase, p.Inventory })
        {
            foreach (var stack in inv.Slots)
            {
                if (stack is not { IsEmpty: false } || !BioItems.CarriesSpecies(ItemKey.Base(stack.Item)))
                {
                    continue;
                }

                uint seed = ItemKey.Seed(stack.Item);
                if (seed != 0)
                {
                    yield return seed;
                }
            }
        }
    }

    /// <summary>The whole book on join: every species the player holds or has analysed, the parents of the crosses among
    /// them, and the mixes and changes they have tried.</summary>
    private void SendBioBook(PlayerSession session)
    {
        var research = ResearchOf(session.State.PlayerId);
        session.BioBookSeeds.Clear();
        var species = new List<NetBioSpecies>();
        foreach (uint seed in HeldSeeds(session.State).Concat(research.Analysed).ToList())
        {
            AddWithParents(session, research, seed, species);
        }

        Send(session, new BioBook
        {
            Full = true,
            Species = species.ToArray(),
            Reactions = research.Reactions.ToArray(),
            Changes = research.Changes.ToArray(),
        });
    }

    /// <summary>One species (and, for a cross, its parents) the client does not have yet — or has, but its state changed.</summary>
    private void SendBioSpecies(PlayerSession session, uint seed, bool resend = false)
    {
        var research = ResearchOf(session.State.PlayerId);
        if (resend)
        {
            session.BioBookSeeds.Remove(seed);
        }

        var species = new List<NetBioSpecies>();
        AddWithParents(session, research, seed, species);
        if (species.Count > 0)
        {
            Send(session, new BioBook { Species = species.ToArray() });
        }
    }

    private void AddWithParents(PlayerSession session, BioResearch research, uint seed, List<NetBioSpecies> into, int depth = 0)
    {
        if (BioEntry(seed) is not { } entry || depth > BioRules.MaxCrossGeneration || !session.BioBookSeeds.Add(seed))
        {
            return;
        }

        into.Add(ToNetSpecies(entry, research));
        if (entry.IsCross)
        {
            AddWithParents(session, research, entry.ParentA, into, depth + 1);
            AddWithParents(session, research, entry.ParentB, into, depth + 1);
        }
    }

    // ---------------- Samples ----------------

    private static string SampleKey(BioSpeciesEntry entry)
        => ItemKey.WithSeed(entry.Kind == BioKind.Mineral ? BioItems.MineralSample : BioItems.Sample, entry.Seed);

    private static int SampleCount(PlayerState p, BioSpeciesEntry entry) => p.SampleCase.CountOf(SampleKey(entry));

    /// <summary>Puts one sample of a species into the player's sample case. A case that is full (or already holds a full
    /// stack of this species) takes nothing — the harvest itself is never held up by it.</summary>
    private bool GiveSample(PlayerSession session, BioSpeciesEntry? entry, int count = 1)
    {
        if (entry is null)
        {
            return false;
        }

        var p = session.State;
        string key = SampleKey(entry);
        int room = BioRules.SampleStack - p.SampleCase.CountOf(key);
        if (room <= 0)
        {
            return false;
        }

        int wanted = Math.Min(room, count);
        int left = p.SampleCase.Add(key, wanted, BioRules.SampleStack);
        if (left >= wanted)
        {
            ShipAiHintOnce(session, "sample_case_full");
            return false;
        }

        SendBioSpecies(session, entry.Seed);
        ShipAiHintOnce(session, "first_sample");
        return true;
    }

    private static bool TakeSample(PlayerState p, BioSpeciesEntry entry, int count = 1) => p.SampleCase.Remove(SampleKey(entry), count);

    /// <summary>A block a player broke: a plant or a tree block yields a sample of its species, an ore a sample of its
    /// deposit. <paramref name="bredSeed"/> is the species of a bred plant that stood in the cell (0 for anything else);
    /// <paramref name="naturalDeposit"/> says the cell held what the world made, not a block a player set there.</summary>
    private void BioOnBlockBroken(PlayerSession session, Vector3i pos, BlockDefinition def, ushort blockId, uint bredSeed, bool naturalDeposit)
    {
        if (bredSeed != 0)
        {
            GiveSample(session, BioEntry(bredSeed));
            return;
        }

        if (IsFlora(blockId))
        {
            GiveSample(session, RegisterFloraSpecies(def.Key));
            return;
        }

        if (!naturalDeposit)
        {
            return;
        }

        if (_treeSpeciesByBlock.ContainsKey(blockId))
        {
            GiveSample(session, RegisterFloraSpecies(def.Key));
            return;
        }

        foreach (var drop in def.Drops)
        {
            string material = MaterialOfDrop(drop.Item);
            if (material.Length == 0)
            {
                continue;
            }

            uint seed = BioHash.DepositSeed(ActiveRosterSeed, material);
            var p = session.State;
            // The first block of a deposit always yields a sample; after that one cell in eight, decided by the cell.
            bool first = BioEntry(seed) is not { } known
                || (SampleCount(p, known) == 0 && !ResearchOf(p.PlayerId).Analysed.Contains(seed));
            ulong cell = BioHash.Mix(unchecked(((ulong)(uint)pos.X * 0x9E3779B97F4A7C15UL) ^ ((ulong)(uint)pos.Y << 21) ^ ((ulong)(uint)pos.Z << 42) ^ seed));
            if (first || cell % BioRules.MineralSampleEvery == 0)
            {
                GiveSample(session, RegisterDeposit(def, material));
            }

            return; // one deposit per block
        }
    }

    /// <summary>The material a dropped item is a deposit of: the item itself when it has lab traits, the clean ore of a
    /// tainted one; empty for anything that is no material with traits.</summary>
    private string MaterialOfDrop(string item)
    {
        if (_content.GetItem(item) is { LabTraits.Count: > 0 })
        {
            return item;
        }

        return item.StartsWith("tainted_", StringComparison.Ordinal) && item.Substring("tainted_".Length) is var clean
            && _content.GetItem(clean) is { LabTraits.Count: > 0 }
            ? clean
            : string.Empty;
    }

    /// <summary>An animal a player defeated yields a sample of its species along with its loot.</summary>
    private void BioOnCreatureDefeated(PlayerSession session, CombatEntity target)
    {
        if (_speciesById.TryGetValue(target.SpeciesId, out var sp))
        {
            GiveSample(session, RegisterCreatureSpecies(sp));
        }
    }

    /// <summary>A companion's regular gift comes with a sample of its species — the way to one without harming anything.</summary>
    private void BioOnCompanionGift(PlayerSession owner, CreatureSpecies sp)
    {
        if (GiveSample(owner, RegisterCreatureSpecies(sp)))
        {
            SendInventory(owner);
        }
    }

    /// <summary>The sampler (#2201): takes a sample from a living animal and leaves it unharmed. A hostile animal has to
    /// be held in stasis first; the same animal gives a sample only every few minutes. #2217: a giant needs no stasis —
    /// the stasis projector cannot hold one, and the long reach is the hurdle there.</summary>
    private bool UseBioSampler(PlayerSession session, Vector3f target)
    {
        var p = session.State;
        var creature = NearestLivingCreature(target) ?? NearestLivingCreature(p.Position);
        if (creature is null || !_speciesById.TryGetValue(creature.SpeciesId, out var sp))
        {
            Reject(session, "gadget", "@srv.bio.sampler_none");
            return false;
        }

        if (sp.Hostile && !creature.IsGiant && creature.FrozenTimer <= 0)
        {
            Reject(session, "gadget", "@srv.bio.sampler_hostile");
            return false;
        }

        if (_bioSampledAt.TryGetValue(creature.Id, out double readyAt) && _uptime < readyAt)
        {
            Reject(session, "gadget", "@srv.bio.sampler_wait");
            return false;
        }

        // Two different refusals: a save whose species register is full cannot take a NEW species at all; a sample
        // case without room is the player's to empty.
        if (RegisterCreatureSpecies(sp) is not { } entry)
        {
            Reject(session, "gadget", "@srv.bio.register_full");
            return false;
        }

        if (!GiveSample(session, entry))
        {
            Reject(session, "gadget", "@srv.bio.sample_case_full");
            return false;
        }

        if (_bioSampledAt.Count > 256)
        {
            foreach (string id in _bioSampledAt.Where(kv => kv.Value <= _uptime).Select(kv => kv.Key).ToList())
            {
                _bioSampledAt.Remove(id);
            }
        }

        _bioSampledAt[creature.Id] = _uptime + SamplerCooldownPerAnimal;
        BroadcastToWorld(new SoundFx { SoundId = "bio_sample_take", X = creature.Position.X, Y = creature.Position.Y + 0.5f, Z = creature.Position.Z });
        SendInventory(session);
        return true;
    }

    private CombatEntity? NearestLivingCreature(Vector3f at)
    {
        CombatEntity? best = null;
        double bestSq = double.MaxValue;
        foreach (var c in _creatures)
        {
            float range = c.IsGiant ? SamplerGiantRange : SamplerRange;
            double d = WrapDistSq(at, c.Position);
            if (d <= range * range && d < bestSq)
            {
                bestSq = d;
                best = c;
            }
        }

        return best;
    }

    // ---------------- Test seams ----------------

    /// <summary>Test seam: the register entry for a seed.</summary>
    public BioSpeciesEntry? BioEntryForTest(uint seed) => BioEntry(seed);

    /// <summary>Test seam: the profile the server derives for a seed.</summary>
    public BioProfile? BioProfileForTest(uint seed) => BioProfileOf(seed);

    /// <summary>Test seam: the seed of a roster species of the active world.</summary>
    public uint BioSeedForTest(CreatureSpecies sp) => BioSeedOfCreature(sp);

    /// <summary>Test seam: registers a roster species and puts samples of it into a player's case.</summary>
    public uint GiveCreatureSampleForTest(PlayerSession session, CreatureSpecies sp, int count = 1)
    {
        var entry = RegisterCreatureSpecies(sp);
        GiveSample(session, entry, count);
        return entry?.Seed ?? 0;
    }

    /// <summary>Test seam: registers a plant block's species and puts samples of it into a player's case.</summary>
    public uint GiveFloraSampleForTest(PlayerSession session, string blockKey, int count = 1)
    {
        var entry = RegisterFloraSpecies(blockKey);
        GiveSample(session, entry, count);
        return entry?.Seed ?? 0;
    }

    /// <summary>Test seam: registers a deposit of the active world and puts samples of it into a player's case.</summary>
    public uint GiveMineralSampleForTest(PlayerSession session, string blockKey, string materialItem, int count = 1)
    {
        var entry = _content.GetBlock(blockKey) is { } block ? RegisterDeposit(block, materialItem) : null;
        GiveSample(session, entry, count);
        return entry?.Seed ?? 0;
    }

    /// <summary>Test seam: registers an animal species as if it had been sampled on another body, and returns its seed —
    /// the way a test gets "a sample from another world" without travelling.</summary>
    public uint RegisterForeignCreatureForTest(CreatureSpecies sp, string originBodyId)
    {
        uint seed = BioHash.Fold(BioSeedOfCreature(sp) ^ BioHash.OfString(originBodyId));
        TryRegister(new BioSpeciesEntry
        {
            Seed = seed,
            Kind = BioKind.Animal,
            Name = sp.Name,
            OriginBodyId = originBodyId,
            OriginBodyName = originBodyId,
            Context = BioContexts.ForCreature(sp, _world.Planet),
            Creature = CloneSpecies(sp),
        });
        return seed;
    }

    /// <summary>Test seam: puts samples of a registered species into a player's case.</summary>
    public bool GiveSampleForTest(PlayerSession session, uint seed, int count = 1) => GiveSample(session, BioEntry(seed), count);

    /// <summary>Test seam: the values of the tool a player holds — with what the lab changed on it.</summary>
    public ToolProperties ActiveToolForTest(PlayerSession session) => ActiveTool(session.State);

    /// <summary>Test seam: whether a player has analysed a species.</summary>
    public bool BioAnalysedForTest(string playerId, uint seed) => ResearchOf(playerId).Analysed.Contains(seed);

    /// <summary>Test seam: uses the sampler at a point.</summary>
    public bool UseBioSamplerForTest(PlayerSession session, Vector3f target) => UseBioSampler(session, target);
}
