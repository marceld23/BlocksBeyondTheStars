// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Bio;
using BlocksBeyondTheStars.Shared.State;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// What the client knows of the bio lab (#2201–#2206): the sample case, the running status effects and the research
    /// book. Unity-free, so the same class runs in the headless client tests. It only mirrors what the server sent —
    /// the one thing it does by itself is count the effects down between two server updates, and derive profiles with
    /// the same <c>Shared</c> code the server uses (a profile is a pure function of seed and context, so nothing about
    /// it has to travel).
    /// </summary>
    public sealed class BioClientState
    {
        private readonly Dictionary<uint, NetBioSpecies> _species = new Dictionary<uint, NetBioSpecies>();
        private readonly Dictionary<uint, BioProfile?> _profiles = new Dictionary<uint, BioProfile?>();
        private readonly HashSet<string> _reactions = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<string> _changes = new List<string>();
        private readonly List<ActiveEffect> _effects = new List<ActiveEffect>();

        /// <summary>The sample case, by slot.</summary>
        public NetItemStack[] Samples { get; private set; } = Array.Empty<NetItemStack>();

        /// <summary>The effects the player is under, counted down locally.</summary>
        public IReadOnlyList<ActiveEffect> Effects => _effects;

        /// <summary>What is left of the shield cushion.</summary>
        public float Shield { get; private set; }

        /// <summary>Every species the player holds a sample of or has analysed.</summary>
        public IReadOnlyDictionary<uint, NetBioSpecies> Species => _species;

        /// <summary>Signatures of the mixes the player has tried (<see cref="Synthesis.Signature"/>). A mix a detoxifier
        /// washed is its own entry — its signature is the one built with <c>washed: true</c>.</summary>
        public IReadOnlyCollection<string> Reactions => _reactions;

        /// <summary>The tools and gear the player has changed, as "base key|payload".</summary>
        public IReadOnlyList<string> Changes => _changes;

        /// <summary>Goes up whenever anything here changed — a UI that shows it rebuilds when the number moves.</summary>
        public int Revision { get; private set; }

        public void OnInventory(InventoryUpdate m)
        {
            if (m is null || m.SamplesUnchanged)
            {
                return;
            }

            Samples = m.Samples ?? Array.Empty<NetItemStack>();
            Revision++;
        }

        public void OnPlayerState(PlayerStateUpdate m)
        {
            if (m is null)
            {
                return;
            }

            var incoming = m.Effects ?? Array.Empty<NetEffect>();
            bool changed = incoming.Length != _effects.Count || Math.Abs(Shield - m.Shield) > 0.5f;
            for (int i = 0; !changed && i < incoming.Length; i++)
            {
                changed = (int)_effects[i].Effect != incoming[i].Effect || _effects[i].Level != incoming[i].Level;
            }

            _effects.Clear();
            foreach (var e in incoming)
            {
                _effects.Add(new ActiveEffect
                {
                    Effect = (BioEffect)e.Effect,
                    Level = e.Level,
                    SecondsLeft = e.SecondsLeft,
                    Side = (BioSideEffect)e.Side,
                    SideLevel = e.SideLevel,
                    Thermal = (BioThermal)e.Thermal,
                });
            }

            Shield = m.Shield;
            if (changed)
            {
                Revision++;
            }
        }

        public void OnBook(BioBook m)
        {
            if (m is null)
            {
                return;
            }

            if (m.Full)
            {
                _species.Clear();
                _reactions.Clear();
                _changes.Clear();
            }

            foreach (var s in m.Species ?? Array.Empty<NetBioSpecies>())
            {
                _species[s.Seed] = s;
            }

            foreach (string r in m.Reactions ?? Array.Empty<string>())
            {
                _reactions.Add(r);
            }

            foreach (string c in m.Changes ?? Array.Empty<string>())
            {
                if (!_changes.Contains(c))
                {
                    _changes.Add(c);
                }
            }

            _profiles.Clear();
            Revision++;
        }

        /// <summary>Counts the effects down between two server updates, so the HUD's seconds run smoothly. The server
        /// stays the judge of when an effect ends; a heat- or cold-sensitive effect is corrected by its next update.</summary>
        public void Tick(float dt)
        {
            for (int i = _effects.Count - 1; i >= 0; i--)
            {
                _effects[i].SecondsLeft -= dt;
                if (_effects[i].SecondsLeft <= 0f)
                {
                    _effects.RemoveAt(i);
                    Revision++;
                }
            }
        }

        /// <summary>How many samples of a species (or a deposit) lie in the sample case.</summary>
        public int SampleCount(uint seed, bool mineral = false)
        {
            string wanted = mineral ? BioItems.MineralSample : BioItems.Sample;
            int total = 0;
            foreach (var s in Samples)
            {
                if (ItemKey.Seed(s.Item) == seed && ItemKey.Base(s.Item) == wanted)
                {
                    total += s.Count;
                }
            }

            return total;
        }

        /// <summary>The species name of a seed for an item label; null when the book does not know it.</summary>
        public string? SpeciesName(uint seed)
            => _species.TryGetValue(seed, out var s) && !string.IsNullOrEmpty(s.Name) ? s.Name : null;

        public bool Analysed(uint seed) => _species.TryGetValue(seed, out var s) && s.Analysed;

        /// <summary>Whether a mix with this signature has been tried — then the lab may show its result before mixing.
        /// The washed and the unwashed mix of the same inputs are two signatures with two results: ask for the one
        /// whose result is about to be shown.</summary>
        public bool Knows(string signature) => _reactions.Contains(signature);

        /// <summary>The register entry of a species as far as the client knows it (no animal snapshot, no plant genome).</summary>
        public BioSpeciesEntry? Entry(uint seed)
        {
            if (!_species.TryGetValue(seed, out var s))
            {
                return null;
            }

            return new BioSpeciesEntry
            {
                Seed = s.Seed,
                Kind = (BioKind)s.Kind,
                Name = s.Name,
                OriginBodyId = s.OriginBodyId,
                OriginBodyName = s.OriginBodyName,
                Context = s.HasContext
                    ? new BioContext { Kind = (BioKind)s.Kind, Tags = s.Tags, RarityPoints = s.RarityPoints, Toxicity = s.Toxicity, Carrier = (BioCarrier)s.Carrier }
                    : null,
                ParentA = s.ParentA,
                ParentB = s.ParentB,
                DifferentWorlds = s.DifferentWorlds,
                Generation = s.Generation,
                MaterialItem = s.MaterialItem,
            };
        }

        /// <summary>The substance profile of a species — derived here exactly as the server derives it. Null for a
        /// deposit, an unknown seed, or a cross whose parents the book does not hold.</summary>
        public BioProfile? ProfileOf(uint seed)
        {
            if (!_profiles.TryGetValue(seed, out var profile))
            {
                profile = BioSpeciesProfiles.ProfileOf(Entry(seed), Entry);
                _profiles[seed] = profile;
            }

            return profile;
        }

        /// <summary>The material profile of a deposit; <paramref name="traitsOf"/> gives an item's <c>labTraits</c>.</summary>
        public MaterialProfile? MaterialOf(uint seed, Func<string, IReadOnlyDictionary<string, int>?> traitsOf)
            => BioSpeciesProfiles.MaterialOf(Entry(seed), traitsOf);

        // The movement side of the effects — the part of the formulas the client applies (on-foot movement is the client's).

        /// <summary>Walking speed factor; <paramref name="gearWeight"/> is the slow-down of changed gear
        /// (<see cref="GearMods.Bonus"/> with <see cref="ModStat.Weight"/>).</summary>
        public float MoveFactor(float gearWeight = 0f) => PlayerEffects.MoveFactor(_effects, gearWeight);

        public float JumpFactor => PlayerEffects.JumpFactor(_effects);

        /// <summary>Climbing grip a preparation adds to the gear's (the caller caps the sum at the gear cap).</summary>
        public float GripBonus => PlayerEffects.Of(_effects, BioEffect.Grip);

        public float CooldownFactor => PlayerEffects.CooldownFactor(_effects);

        /// <summary>0..1: how much brighter the dark is.</summary>
        public float NightSight => PlayerEffects.Of(_effects, BioEffect.NightSight);

        /// <summary>Blocks within which living things are marked through terrain; 0 = no perception.</summary>
        public float PerceptionRange => PlayerEffects.Of(_effects, BioEffect.Perception);
    }
}
