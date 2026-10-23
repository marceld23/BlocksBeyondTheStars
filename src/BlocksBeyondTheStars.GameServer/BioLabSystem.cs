// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Bio;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.State;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// What <see cref="BioLabSystem"/> asks of the server beyond <see cref="IServerHost"/> (ADR 0019): where the player
/// stands (ship, cabin, stations), the ship cursor, the bio register and research book of <c>GameServerBio.cs</c>, and
/// the worn gear's scan and battery values. Implemented explicitly by <c>GameServer</c> in <c>GameServerBioLab.cs</c>,
/// each member delegating to the server's own member of the same name. A wide host — it documents how far the lab
/// reaches into the rest of the server (ADR 0019, "wide hosts, shrink later").
/// </summary>
internal interface IBioLabHost : IServerHost
{
    // VEGA
    void ShipAiHintOnce(PlayerSession session, string hintId);

    // Where the player stands
    bool IsVisitingShip(string playerId);

    bool InShipInterior(string playerId);

    ShipState ShipOf(PlayerSession session);

    bool NearStationBlock(PlayerState player, string blockKey);

    bool StationAvailable(PlayerState player, CraftingStation station);

    float AmbientTemperature(PlayerSession session);

    /// <summary>The ship cursor (the server's <c>_ship</c>) — it changes per call, so read it at use time.</summary>
    ShipState Ship { get; }

    // The bio register and the research book (GameServerBio.cs)
    BioSpeciesEntry? BioEntry(uint seed);

    BioProfile? BioProfileOf(uint seed);

    MaterialProfile? MaterialProfileOf(uint seed);

    GameServer.BioResearch ResearchOf(string playerId);

    void SaveResearch(string playerId);

    void SendBioSpecies(PlayerSession session, uint seed, bool resend);

    // Worn gear (GameServerEquipment.cs)
    float ScanMultiplier(PlayerState p);

    float MaxSuitEnergy(PlayerState p);
}

/// <summary>
/// The bio lab block (#2203–#2206): analyser, extractor and synthesis station in one, and the place where a tool or a
/// piece of gear is changed. Everything here is computed from the properties of what goes in (<see cref="Synthesis"/>,
/// <see cref="ItemModRules"/>) — there is no recipe list, and the same inputs always give the same result. The client
/// only asks; samples, results and research are decided here.
/// </summary>
internal sealed class BioLabSystem
{
    /// <summary>Knowledge a first analysis pays: a base, plus more for a rarer species or a purer deposit.</summary>
    private const int KnowledgeAnalysisBase = 2;

    private readonly IBioLabHost _host;
    private readonly GameContent _content;

    /// <summary>Stores the references only; built once in <c>GameServer.ConstructSystems</c> (ADR 0019).</summary>
    internal BioLabSystem(IBioLabHost host, GameContent content)
    {
        _host = host;
        _content = content;
    }

    /// <summary>
    /// Whether a player stands at a bio lab: a lab block of the world within reach — and the player not inside a ship
    /// (#2216). The reach is a box around the player and reads through a hull, so a lab standing against the parked
    /// ship used to answer a player inside it; the wash then asked for the detoxifier the way the cabin does (the
    /// ship's MODULE) and never saw the detoxifier block beside the lab. The lab is used from outside the ship.
    /// "Inside a ship" is the cabin of the temperature the effects feel (see <c>AmbientTemperature</c>) without the
    /// station: aboard the ship, or inside a landed ship's hull — anyone's, a visitor has no aboard flag of their
    /// own. A station deck is no ship: a lab on a station works for whoever stands on that deck.
    /// <para>
    /// The aboard flag only says "in the cabin" where the player's ship stands: in a world without a placed ship
    /// it keeps its default (true) and is never updated, so there it must not count — or the lab would be refused
    /// everywhere.
    /// </para>
    /// <para>
    /// A pilot in space never stands at a lab, in the cockpit or on a spacewalk (#2216): there is no world grid out
    /// there. The launch leaves the on-foot position where it was and the planet stays the pilot's world, so the
    /// reach would be measured from a spot on the ground below — and the spacewalk exemption of the cabin test used
    /// to let exactly that spot decide: a lab beside the launch site answered from orbit, its wash reading the
    /// ship's detoxifier module.
    /// </para>
    /// <para>
    /// #2248: aboard your OWN ship the lab is the ship's <see cref="BioItems.LabModule"/> — the module, not a block,
    /// exactly like the workshop, refinery and detoxifier aboard. It works in the parked ship's cabin and in the
    /// walkable interior while the ship floats in space (never from the pilot seat or a spacewalk). A visitor has no
    /// aboard flag and walks no interior of another player's ship, so only the owner's module counts.
    /// </para>
    /// </summary>
    private bool AtBioLab(PlayerSession session)
    {
        var p = session.State;
        if (ShipLabAboard(session))
        {
            return true;
        }

        if (_host.InSpace(p.PlayerId))
        {
            return false;
        }

        bool cabin = (p.AboardShip && _host.Worlds.Active.LandedFor(p.PlayerId).Placed)
            || _host.ShipInteriorContains(p.Position);
        return !(cabin && !p.InEva) && _host.NearStationBlock(p, BioItems.Lab);
    }

    /// <summary>#2248: the player walks their own ship — parked cabin or the interior in space — and it carries the bio
    /// lab module. #2723: a guest in a docked friend's ship walks no interior of their own (their ship floats parked
    /// outside, its walkabout record only remembers where), so their own lab module does not reach them there.</summary>
    private bool ShipLabAboard(PlayerSession session)
    {
        var p = session.State;
        if (p.InEva || _host.InSpace(p.PlayerId) || _host.IsVisitingShip(p.PlayerId) || !_host.ShipOf(session).HasModule(BioItems.LabModule))
        {
            return false;
        }

        return _host.InShipInterior(p.PlayerId) || (p.AboardShip && _host.Worlds.Active.LandedFor(p.PlayerId).Placed);
    }

    /// <summary>A lab function a player may use: always in a creative world, otherwise once its blueprint is researched.</summary>
    internal bool BioUnlocked(PlayerState p, string blueprint)
        => !_host.Rules.CraftingCostsMaterialsFor(p.ModeOverride) || p.UnlockedBlueprints.Contains(blueprint);

    private void LabResult(PlayerSession session, int action, bool success, string messageKey, string itemKey = "",
        int stability = 0, bool failed = false, int knowledge = 0, bool washed = false)
    {
        _host.Send(session, new BioLabResult
        {
            Action = action,
            Success = success,
            MessageKey = messageKey,
            ItemKey = itemKey,
            Stability = stability,
            Failed = failed,
            Knowledge = knowledge,
            Washed = washed,
        });
        if (success)
        {
            _host.RaiseGameEvent(session, Shared.Story.GameEventKeys.BioLabUsed, GameServer.BioLabActionArg(action)); // #2482: the gardener course
        }
    }

    private void LabSound(PlayerSession session, string soundId)
    {
        var at = session.State.Position;
        _host.BroadcastToWorld(new SoundFx { SoundId = soundId, X = at.X, Y = at.Y + 1f, Z = at.Z });
    }

    internal void HandleBioLab(PlayerSession session, BioLabIntent intent)
    {
        var p = session.State;
        if (!AtBioLab(session))
        {
            LabResult(session, intent.Action, false, "srv.bio.need_lab");
            return;
        }

        switch (intent.Action)
        {
            case BioLabIntent.Analyse: LabAnalyse(session, intent); break;
            case BioLabIntent.Mix: LabMix(session, intent); break;
            case BioLabIntent.Change: LabChange(session, intent); break;
            case BioLabIntent.WashOff: LabWashOff(session, intent); break;
            case BioLabIntent.Seedling: LabSeedling(session, intent); break;
            default: LabResult(session, intent.Action, false, "srv.bio.unknown_action"); break;
        }
    }

    // ---------------- Analyse ----------------

    private void LabAnalyse(PlayerSession session, BioLabIntent intent)
    {
        var p = session.State;
        bool free = !_host.Rules.CraftingCostsMaterialsFor(p.ModeOverride);
        var entry = _host.BioEntry(intent.Sample);
        if (entry is null || (entry.Kind == BioKind.Mineral) != intent.SampleMineral || GameServer.SampleCount(p, entry) < 1)
        {
            LabResult(session, intent.Action, false, "srv.bio.no_sample");
            return;
        }

        var research = _host.ResearchOf(p.PlayerId);
        if (research.Analysed.Contains(entry.Seed))
        {
            LabResult(session, intent.Action, false, "srv.bio.already_analysed");
            return;
        }

        if (research.Analysed.Count >= BioRules.MaxResearchEntries)
        {
            LabResult(session, intent.Action, false, "srv.bio.book_full");
            return;
        }

        if (!free)
        {
            GameServer.TakeSample(p, entry);
        }

        research.Analysed.Add(entry.Seed);
        _host.SaveResearch(p.PlayerId);

        // The first look at a species pays knowledge once, like a first scan: more for a rarer one.
        int value = KnowledgeAnalysisBase + (entry.Kind == BioKind.Mineral
            ? Math.Max(0, (_host.MaterialProfileOf(entry.Seed)?.Purity ?? 3) - 3)
            : 2 * (_host.BioProfileOf(entry.Seed)?.Rarity ?? 0));
        int gained = (int)Math.Round(value * _host.ScanMultiplier(p));
        p.KnowledgePoints += gained;

        _host.SendBioSpecies(session, entry.Seed, resend: true);
        _host.SendInventory(session);
        LabSound(session, "bio_lab_analyse");
        _host.ShipAiHintOnce(session, "first_analysis");
        LabResult(session, intent.Action, true, "srv.bio.analysed", knowledge: gained);
    }

    // ---------------- Mix ----------------

    private void LabMix(PlayerSession session, BioLabIntent intent)
    {
        var p = session.State;
        bool free = !_host.Rules.CraftingCostsMaterialsFor(p.ModeOverride);
        var active = _host.BioEntry(intent.Sample);
        var activeProfile = active is null ? null : _host.BioProfileOf(active.Seed);
        if (active is null || activeProfile is null || GameServer.SampleCount(p, active) < 1)
        {
            LabResult(session, intent.Action, false, "srv.bio.no_sample");
            return;
        }

        // The carrier decides the form; without one the sample is simply extracted into an injector.
        var form = BioForm.Injector;
        ItemDefinition? carrier = null;
        if (!string.IsNullOrEmpty(intent.Carrier))
        {
            carrier = _content.GetItem(intent.Carrier);
            if (BioItems.CarrierForm(carrier) is not { } carrierForm)
            {
                LabResult(session, intent.Action, false, "srv.bio.no_carrier");
                return;
            }

            form = carrierForm;
        }

        var pool = new MaterialPool(_content, p, _host.Ship);
        BioSpeciesEntry? modifier = intent.Modifier == 0 ? null : _host.BioEntry(intent.Modifier);
        var modifierProfile = modifier is null ? null : _host.BioProfileOf(modifier.Seed);
        if (!TryLabMaterial(p, pool, free, intent, out var stabiliserProfile, out var stabiliser, out string stabiliserItem)
            || (intent.Modifier != 0 && (modifier is null || modifierProfile is null
                || GameServer.SampleCount(p, modifier) < (modifier.Seed == active.Seed ? 2 : 1))))
        {
            LabResult(session, intent.Action, false, "srv.bio.no_sample");
            return;
        }

        // Anything beyond a plain extract is the full mixer.
        if ((carrier is not null || stabiliserProfile is not null || modifier is not null) && !BioUnlocked(p, BioItems.SynthesisBlueprint))
        {
            LabResult(session, intent.Action, false, "srv.bio.need_synthesis");
            return;
        }

        var carrierCost = carrier is null ? null : new[] { new ItemAmount(ItemKey.Base(intent.Carrier), 1) };
        if (!free && carrierCost is not null && !pool.Has(carrierCost))
        {
            LabResult(session, intent.Action, false, "srv.bio.no_carrier");
            return;
        }

        // A toxic sample is washed as part of the mix when a detoxifier stands by and carbon is at hand. The wash is part
        // of what goes in (#2216): the washed mix is another experiment than the unwashed one, with its own result and
        // its own entry in the research book — so the same inputs, washed the same way, always give the same result.
        bool toxic = activeProfile.Toxicity > 0 || (modifierProfile?.Toxicity ?? 0) > 0;
        var carbon = new[] { new ItemAmount("carbon", 1) };
        bool washed = toxic && _host.StationAvailable(p, CraftingStation.Detoxifier) && (free || pool.Has(carbon));

        var compound = Synthesis.Compute(new SynthesisInput
        {
            Active = activeProfile,
            ActiveCleaned = washed,
            Form = form,
            Stabiliser = stabiliserProfile,
            Modifier = modifierProfile,
            ModifierCleaned = washed,
        });

        string output = compound.Failed ? string.Empty : BioItems.PreparationItem(compound);
        if (!compound.Failed && !pool.CanFit(new[] { new ItemAmount(output, 1) }))
        {
            LabResult(session, intent.Action, false, "srv.bio.inventory_full");
            return;
        }

        // The inputs are used up whether the mix holds or falls apart — that is the price of an experiment.
        if (!free)
        {
            GameServer.TakeSample(p, active);
            TakeLabMaterial(p, pool, stabiliser, stabiliserItem);
            if (modifier is not null) { GameServer.TakeSample(p, modifier); }
            if (carrierCost is not null) { pool.Remove(carrierCost); }
            if (washed) { pool.Remove(carbon); }
        }

        var research = _host.ResearchOf(p.PlayerId);
        string signature = Synthesis.Signature(active.Seed, form, stabiliser?.Seed ?? 0, stabiliserItem, modifier?.Seed ?? 0, washed);
        if (research.Reactions.Count < BioRules.MaxResearchEntries && research.Reactions.Add(signature))
        {
            _host.SaveResearch(p.PlayerId);
            _host.Send(session, new BioBook { Reactions = new[] { signature } });
        }

        if (compound.Failed)
        {
            _host.SendInventory(session);
            LabSound(session, "bio_lab_fail");
            LabResult(session, intent.Action, false, "srv.bio.mix_failed", stability: compound.Stability, failed: true, washed: washed);
            return;
        }

        pool.Add(output, 1);
        _host.SendInventory(session);
        LabSound(session, "bio_lab_mix");
        LabResult(session, intent.Action, true, "srv.bio.mixed", output, compound.Stability, washed: washed);
    }

    /// <summary>
    /// The material in the lab's mineral slot: a mineral sample with the origin values of its deposit — or, without one,
    /// a plain material with lab traits from the backpack (an ingot, an alloy, synthesised ore), which has no origin and
    /// acts with exactly its fixed traits. False when one is named but not at hand; no material at all is fine.
    /// </summary>
    private bool TryLabMaterial(PlayerState p, MaterialPool pool, bool free, BioLabIntent intent,
        out MaterialProfile? profile, out BioSpeciesEntry? sample, out string item)
    {
        profile = null;
        sample = null;
        item = string.Empty;
        if (intent.Stabiliser != 0)
        {
            sample = _host.BioEntry(intent.Stabiliser);
            profile = sample is null ? null : _host.MaterialProfileOf(sample.Seed);
            item = sample?.MaterialItem ?? string.Empty;
            return sample is not null && profile is not null && GameServer.SampleCount(p, sample) >= 1;
        }

        if (string.IsNullOrEmpty(intent.MaterialItem))
        {
            return true;
        }

        item = ItemKey.Base(intent.MaterialItem);
        if (_content.GetItem(item) is not { LabTraits.Count: > 0 } def || (!free && !pool.Has(new[] { new ItemAmount(item, 1) })))
        {
            return false;
        }

        profile = MaterialProfiles.Synthetic(MaterialProfiles.BaseLevels(def.LabTraits));
        return true;
    }

    private static void TakeLabMaterial(PlayerState p, MaterialPool pool, BioSpeciesEntry? sample, string item)
    {
        if (sample is not null)
        {
            GameServer.TakeSample(p, sample);
        }
        else if (item.Length > 0)
        {
            pool.Remove(new[] { new ItemAmount(item, 1) });
        }
    }

    // ---------------- Change a tool or a piece of gear ----------------

    /// <summary>Whether the lab can change an item at all: a drill, a weapon, or a piece of worn gear.</summary>
    private static bool BioChangeable(ItemDefinition? def)
        => def is not null && def.MaxStack == 1
           && (def.Tool is { Kind: ToolKind.Drill or ToolKind.Weapon }
               || (!string.IsNullOrEmpty(def.EquipSlot) && (def.ArmorResistance > 0f || def.ThermalInsulation > 0f
                   || def.CorrosionResistance > 0f || def.FallProtection > 0f || def.ClimbGrip > 0f || def.OxygenBonus > 0f)));

    private void LabChange(PlayerSession session, BioLabIntent intent)
    {
        var p = session.State;
        bool free = !_host.Rules.CraftingCostsMaterialsFor(p.ModeOverride);
        if (!BioUnlocked(p, BioItems.TuningBlueprint))
        {
            LabResult(session, intent.Action, false, "srv.bio.need_tuning");
            return;
        }

        var def = _content.GetItem(intent.TargetItem);
        int slot = SlotOf(p.Inventory, intent.TargetItem);
        if (!BioChangeable(def) || slot < 0)
        {
            LabResult(session, intent.Action, false, "srv.bio.no_target");
            return;
        }

        var pool = new MaterialPool(_content, p, _host.Ship);
        if (!TryLabMaterial(p, pool, free, intent, out var materialProfile, out var material, out string materialItem)
            || materialProfile is null)
        {
            // #2249: the material slot — a mineral sample OR an ingot, steel, a gem — not "this sample is not in your case".
            LabResult(session, intent.Action, false, "srv.bio.no_material");
            return;
        }

        Compound? coating = null;
        if (!string.IsNullOrEmpty(intent.CoatingItem))
        {
            coating = BioItems.CompoundOf(intent.CoatingItem);
            if (coating is not { Form: BioForm.Coating } || !p.Inventory.Has(intent.CoatingItem, 1))
            {
                LabResult(session, intent.Action, false, "srv.bio.no_coating");
                return;
            }
        }

        var tool = def!.Tool;
        var mods = ItemModRules.Compute(tool is not null, tool is { CooldownSeconds: > 0f }, tool is { Range: > 0f },
            tool is { EnergyPerUse: > 0f }, materialProfile, coating);
        string changed = mods.ApplyTo(intent.TargetItem);
        if (mods.IsEmpty || changed == intent.TargetItem)
        {
            LabResult(session, intent.Action, false, "srv.bio.no_change");
            _host.ShipAiHintOnce(session, "bio_no_change"); // #2249: what works instead — the first dead end gets a way out
            return;
        }

        if (!free)
        {
            TakeLabMaterial(p, pool, material, materialItem);
            if (coating is not null) { p.Inventory.Remove(intent.CoatingItem, 1); }
        }

        p.Inventory.SetSlot(slot, new ItemStack(changed, 1)); // in place: a tool stays where the player keeps it
        var research = _host.ResearchOf(p.PlayerId);
        string record = ItemKey.Base(changed) + "|" + ItemKey.GetTag(changed, ItemMods.Tag);
        if (research.Changes.Count < BioRules.MaxResearchEntries && !research.Changes.Contains(record))
        {
            research.Changes.Add(record);
            _host.SaveResearch(p.PlayerId);
            _host.Send(session, new BioBook { Changes = new[] { record } });
        }

        _host.SendInventory(session);
        LabSound(session, "bio_lab_mix");
        LabResult(session, intent.Action, true, "srv.bio.changed", changed);
    }

    private void LabWashOff(PlayerSession session, BioLabIntent intent)
    {
        var p = session.State;
        int slot = SlotOf(p.Inventory, intent.TargetItem);

        // There is something to wash off when the key carries a change at all — also one this version cannot read (a
        // hand-typed key, a key of a newer version). Such an item acts as the plain one (ItemMods.Of), and washing is
        // how it becomes the plain one again; asking for readable changes here would leave it stuck with its key.
        string plain = slot < 0 ? string.Empty : default(ItemMods).ApplyTo(intent.TargetItem);
        if (slot < 0 || plain == intent.TargetItem)
        {
            LabResult(session, intent.Action, false, "srv.bio.no_target");
            return;
        }

        p.Inventory.SetSlot(slot, new ItemStack(plain, 1));
        _host.SendInventory(session);
        LabResult(session, intent.Action, true, "srv.bio.washed", plain);
    }

    /// <summary>The backpack slot that holds exactly one of <paramref name="itemKey"/>, or -1.</summary>
    private static int SlotOf(Inventory inventory, string itemKey)
    {
        if (string.IsNullOrEmpty(itemKey))
        {
            return -1;
        }

        for (int i = 0; i < inventory.SlotCount; i++)
        {
            if (inventory.Slots[i] is { IsEmpty: false, Count: 1 } stack && stack.Item == itemKey)
            {
                return i;
            }
        }

        return -1;
    }

    // ---------------- Seedling ----------------

    private void LabSeedling(PlayerSession session, BioLabIntent intent)
    {
        var p = session.State;
        bool free = !_host.Rules.CraftingCostsMaterialsFor(p.ModeOverride);
        var entry = _host.BioEntry(intent.Sample);
        if (entry is null || entry.Kind != BioKind.Plant || GameServer.SampleCount(p, entry) < 1)
        {
            LabResult(session, intent.Action, false, "srv.bio.no_sample");
            return;
        }

        if (entry.Flora is null)
        {
            LabResult(session, intent.Action, false, "srv.bio.not_plantable"); // a tree's trunk is no single plant
            return;
        }

        string seedling = ItemKey.WithSeed(BioItems.Seedling, entry.Seed);
        var pool = new MaterialPool(_content, p, _host.Ship);
        if (!pool.CanFit(new[] { new ItemAmount(seedling, 1) }))
        {
            LabResult(session, intent.Action, false, "srv.bio.inventory_full");
            return;
        }

        if (!free)
        {
            GameServer.TakeSample(p, entry);
        }

        pool.Add(seedling, 1);
        _host.SendInventory(session);
        LabSound(session, "bio_lab_analyse");
        LabResult(session, intent.Action, true, "srv.bio.seedling", seedling);
    }

    // ---------------- Preparations and status effects (#2202) ----------------

    /// <summary>Takes a preparation: starts its effect. A weaker repeat and a fourth effect are refused and nothing is
    /// used up. Returns false when the key is no preparation item at all (the caller eats it as ordinary food). A
    /// preparation item that carries no compound (#2216: a blank from a cheat, a key this version cannot read) is handled
    /// here too — refused and kept, never eaten as food.</summary>
    internal bool TryTakePreparation(PlayerSession session, string itemKey)
    {
        if (BioItems.FormOf(ItemKey.Base(itemKey)) is null)
        {
            return false;
        }

        if (BioItems.CompoundOf(itemKey) is not { } compound)
        {
            _host.Reject(session, "consume", "@srv.bio.prep_empty");
            return true;
        }

        var p = session.State;
        if (compound.Form == BioForm.Coating)
        {
            _host.Reject(session, "consume", "@srv.bio.coating_not_taken");
            return true;
        }

        if (!p.Inventory.Has(itemKey, 1))
        {
            _host.Reject(session, "consume", "@srv.misc.no_item");
            return true;
        }

        switch (PlayerEffects.Check(p.Effects, compound.Effect, compound.Level))
        {
            case EffectAddResult.Weaker:
                _host.Reject(session, "consume", "@srv.bio.effect_weaker");
                return true;
            case EffectAddResult.Full:
                _host.Reject(session, "consume", "@srv.bio.effect_full");
                return true;
        }

        p.Inventory.Remove(itemKey, 1);
        StartEffect(p, new ActiveEffect
        {
            Effect = compound.Effect,
            Level = compound.Level,
            SecondsLeft = compound.DurationSeconds,
            Side = compound.Side,
            SideLevel = compound.SideLevel,
            Thermal = compound.Thermal,
        });

        // A coupled second effect joins when there is room for it; it carries no catch of its own.
        if (compound.Secondary != BioEffect.None
            && PlayerEffects.Check(p.Effects, compound.Secondary, compound.SecondaryLevel) is EffectAddResult.Applied or EffectAddResult.Refreshed)
        {
            StartEffect(p, new ActiveEffect
            {
                Effect = compound.Secondary,
                Level = compound.SecondaryLevel,
                SecondsLeft = compound.DurationSeconds,
                Thermal = compound.Thermal,
            });
        }

        // A bar also feeds, like any food.
        if (_content.GetItem(itemKey) is { ConsumeHunger: > 0f } food)
        {
            p.Hunger = Math.Min(100f, p.Hunger + food.ConsumeHunger);
        }

        LabSound(session, "bio_effect_start");
        _host.ShipAiHintOnce(session, "first_effect");
        _host.SendInventory(session);
        _host.SendPlayerState(session);
        return true;
    }

    internal static void StartEffect(PlayerState p, ActiveEffect effect)
    {
        PlayerEffects.Add(p.Effects, effect);
        if (effect.Effect == BioEffect.Shield)
        {
            p.Shield = BioRules.Magnitude(BioEffect.Shield, effect.Level);
        }
    }

    /// <summary>One tick of a player's effects: count down (faster for a heat- or cold-sensitive one in the wrong
    /// weather), heal and recharge. Tells the client when an effect ended, and every couple of seconds while one runs out
    /// faster than the client counts. Runs before the god-mode exit of the vitals loop and whatever the hazard rules
    /// say, so the weather it reads is its own (<see cref="IBioLabHost.AmbientTemperature"/>, #2218) — and only looked up when a
    /// running effect is thermal at all.</summary>
    internal void TickBioEffects(PlayerSession session, double dt)
    {
        var p = session.State;
        if (p.Effects.Count == 0)
        {
            return;
        }

        float regen = PlayerEffects.Of(p.Effects, BioEffect.Regeneration);
        if (regen > 0f && p.Health > 0f)
        {
            p.Health = Math.Min(100f, p.Health + (float)(dt * regen));
        }

        float energy = PlayerEffects.Of(p.Effects, BioEffect.Energy);
        if (energy > 0f)
        {
            p.SuitEnergy = Math.Min(_host.MaxSuitEnergy(p), p.SuitEnergy + (float)(dt * energy)); // #2297: up to the worn battery
        }

        bool thermal = PlayerEffects.AnyThermal(p.Effects);
        float temperature = thermal ? _host.AmbientTemperature(session) : GameServer.CabinComfortC;
        bool stressed = thermal && PlayerEffects.AnyStressed(p.Effects, temperature);
        if (PlayerEffects.Tick(p.Effects, (float)dt, temperature))
        {
            if (PlayerEffects.Of(p.Effects, BioEffect.Shield) <= 0f)
            {
                p.Shield = 0f; // the cushion goes with its effect
            }

            LabSound(session, "bio_effect_end");
            _host.SendPlayerState(session);
            session.Hazards.EffectSyncIn = EffectSyncSeconds;
        }
        else if (stressed)
        {
            // The client counts an effect down at plain speed between two updates. One that runs out faster in this
            // weather drifts on its HUD — and where no vital moves (Sandbox, god mode, hazards off) no update would
            // ever correct it, so the effect would vanish with half its time still showing. Tell the client the true
            // time now and then while that lasts (#2218); nobody else gets an extra message.
            session.Hazards.EffectSyncIn -= dt;
            if (session.Hazards.EffectSyncIn <= 0)
            {
                session.Hazards.EffectSyncIn = EffectSyncSeconds;
                _host.SendPlayerState(session);
            }
        }
    }

    /// <summary>Seconds between two corrections of the client's effect countdown while an effect runs out faster in the
    /// heat or the cold — the HUD is never more than this far off.</summary>
    private const double EffectSyncSeconds = 2.0;

    /// <summary>The shield cushion takes a hit before the health does. Returns what is left for the health.</summary>
    internal static float AbsorbWithShield(PlayerState p, float damage)
    {
        if (p.Shield <= 0f || damage <= 0f)
        {
            return damage;
        }

        float absorbed = Math.Min(p.Shield, damage);
        p.Shield -= absorbed;
        return damage - absorbed;
    }

    internal static NetEffect[] DumpEffects(PlayerState p)
    {
        if (p.Effects.Count == 0)
        {
            return Array.Empty<NetEffect>();
        }

        return p.Effects.Select(e => new NetEffect
        {
            Effect = (int)e.Effect,
            Level = e.Level,
            SecondsLeft = e.SecondsLeft,
            Side = (int)e.Side,
            SideLevel = e.SideLevel,
            Thermal = (int)e.Thermal,
        }).ToArray();
    }
}
