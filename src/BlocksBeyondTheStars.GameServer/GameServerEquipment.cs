// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.State;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Suit equipment effects derived from the gear a player <b>carries</b> (no separate equip slots
/// yet): armor damage resistance, extra oxygen capacity, scanner knowledge bonus, and the stealth
/// field. Server-authoritative — these feed the vitals/combat/scan systems. The formula itself lives in
/// <see cref="SuitEquipment"/> (Shared) so the client's Suit tab and HUD show exactly what the server
/// applies (#1270); data-driven via the item definitions (`ArmorResistance`, `OxygenBonus`,
/// `ThermalInsulation`, `ScanKnowledgeMultiplier`).
/// </summary>
public sealed partial class GameServer
{
    private const string StealthItem = "stealth_suit";
    private const float StealthDrainPerSecond = 3f; // suit energy spent while cloaked

    // Since #2206 a worn piece may carry changes from the bio lab in its key, and since #2202 a preparation may add to a
    // stat for a while. Both join the gear formula of their stat and share ITS cap: neither ever lifts a player above
    // what the best gear allows.

    /// <summary>Total physical-damage resistance (0..0.75) from carried armor pieces.</summary>
    private float ArmorResistance(PlayerState p)
        => System.Math.Min(SuitEquipment.MaxArmorResistance,
            SuitEquipment.ArmorResistance(_content.Items.Values, key => Wears(p, key))
            + Shared.Bio.GearMods.Bonus(WornKeys(p), Shared.Bio.ModStat.Armor));

    /// <summary>Maximum suit oxygen — base 100 plus the best carried tank's bonus (tiers do not stack).</summary>
    private float MaxOxygen(PlayerState p)
        => SuitEquipment.MaxOxygen(_content.Items.Values, key => Wears(p, key))
           + Shared.Bio.GearMods.Bonus(WornKeys(p), Shared.Bio.ModStat.Oxygen);

    /// <summary>Best carried thermal insulation 0..0.9 (#669); only the BEST piece counts. A heat ward counts in the heat,
    /// a cold ward in the cold (#2202).</summary>
    private float ThermalInsulation(PlayerState p)
    {
        float temperature = FindSessionByPlayerId(p.PlayerId)?.EffectiveTemperatureC ?? 15f;
        float ward = temperature > Shared.Bio.BioRules.HotAbove ? Shared.Bio.PlayerEffects.ThermalBonus(p.Effects, hot: true)
            : temperature < Shared.Bio.BioRules.ColdBelow ? Shared.Bio.PlayerEffects.ThermalBonus(p.Effects, hot: false)
            : 0f;
        return System.Math.Min(SuitEquipment.MaxThermalInsulation,
            SuitEquipment.ThermalInsulation(_content.Items.Values, key => Wears(p, key))
            + Shared.Bio.GearMods.Bonus(WornKeys(p), Shared.Bio.ModStat.Insulation) + ward);
    }

    /// <summary>Best carried corrosion resistance 0..0.8 (#2026, the suit liners); only the BEST piece counts.</summary>
    private float CorrosionResistance(PlayerState p)
        => System.Math.Min(SuitEquipment.MaxCorrosionResistance,
            SuitEquipment.CorrosionResistance(_content.Items.Values, key => Wears(p, key))
            + Shared.Bio.GearMods.Bonus(WornKeys(p), Shared.Bio.ModStat.Corrosion)
            + Shared.Bio.PlayerEffects.Of(p.Effects, Shared.Bio.BioEffect.ToxinWard));

    /// <summary>Best scanner knowledge multiplier from carried scanners (1 = no bonus).</summary>
    /// <summary>The scanner bonus comes from a TOOL in the pack (the advanced scanner is held, not worn) — or from worn gear.</summary>
    private float ScanMultiplier(PlayerState p)
        => SuitEquipment.ScanMultiplier(_content.Items.Values, key => p.Inventory.Has(key, 1) || Wears(p, key));

    /// <summary>Applies armor resistance to an incoming physical-damage amount. What is left hits the shield cushion of a
    /// preparation first (#2202) — the return value is what reaches the health.</summary>
    private float Mitigate(PlayerState p, float damage) => AbsorbWithShield(p, damage * (1f - ArmorResistance(p)));

    /// <summary>Toggles the stealth field on/off if the player carries a stealth suit and has energy.</summary>
    public void ToggleStealth(string playerId)
    {
        var session = FindSessionByPlayerId(playerId);
        if (session is null)
        {
            return;
        }

        var p = session.State;
        if (!Wears(p, StealthItem))
        {
            Reject(session, "stealth", "@srv.equip.no_stealth");
            return;
        }

        if (!p.Stealthed && p.SuitEnergy <= 0f)
        {
            Reject(session, "stealth", "@srv.equip.no_energy_cloak");
            return;
        }

        p.Stealthed = !p.Stealthed;
        SendPlayerState(session);
    }

    /// <summary>Drains suit energy while cloaked; drops stealth when the energy runs out.</summary>
    private void TickStealth(PlayerSession session, double dt)
    {
        var p = session.State;
        if (!p.Stealthed)
        {
            return;
        }

        p.SuitEnergy = System.Math.Max(0f, p.SuitEnergy - (float)(dt * StealthDrainPerSecond));
        if (p.SuitEnergy <= 0f)
        {
            p.Stealthed = false;
        }
    }

    private void HandleToggleStealth(PlayerSession session) => ToggleStealth(session.State.PlayerId);

    private const string JetpackItem = "jetpack";
    private const float JetpackDrainPerSecond = 9f; // suit energy spent while thrusting

    /// <summary>Sets the player's jetpack thrust state (client-driven). Rejects if they carry no jetpack
    /// or have no suit energy; the actual upward thrust is applied client-side.</summary>
    private void HandleSetJetpack(PlayerSession session, SetJetpackIntent intent)
    {
        var p = session.State;
        if (!intent.Active)
        {
            p.Jetpacking = false;
            return;
        }

        if (!Wears(p, JetpackItem))
        {
            p.Jetpacking = false;
            Reject(session, "jetpack", "@srv.equip.no_jetpack");
            return;
        }

        if (p.SuitEnergy <= 0f)
        {
            p.Jetpacking = false;
            Reject(session, "jetpack", "@srv.equip.no_energy_jetpack");
            return;
        }

        p.Jetpacking = true;
    }

    /// <summary>Mirrors the client's sit-on-chair pose (#806). Pure cosmetics — no validation beyond
    /// "on foot": movement stays client-authoritative and the flag only feeds the presence broadcast.</summary>
    // ---------------- Equipment slots (#2110) ----------------

    /// <summary>True while the gear is WORN in one of the suit's slots — the only place gear works since #2110.</summary>
    /// A piece the bio lab changed (#2206) carries its changes in its key and is still the same piece: the base key decides.
    private static bool Wears(PlayerState p, string key)
    {
        foreach (var stack in p.Equipment.Slots)
        {
            if (stack is { IsEmpty: false } && (stack.Item == key || ItemKey.Base(stack.Item) == key))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Total fall protection (0..0.75) of the worn gear — the boots; a changed piece and a feather-fall
    /// preparation add to it under the same cap.</summary>
    private float FallProtection(PlayerState p)
        => System.Math.Min(SuitEquipment.MaxFallProtection,
            SuitEquipment.FallProtection(_content.Items.Values, key => Wears(p, key))
            + Shared.Bio.GearMods.Bonus(WornKeys(p), Shared.Bio.ModStat.Fall)
            + Shared.Bio.PlayerEffects.Of(p.Effects, Shared.Bio.BioEffect.FeatherFall));

    /// <summary>The one-time migration of a save written before the slots existed: the best wearable piece of every kind
    /// moves from the backpack into its slot, so nobody loses an active effect on the day the slots arrive.</summary>
    private void EnsureEquipmentInitialised(PlayerState p)
    {
        if (p.EquipmentInitialised)
        {
            return;
        }

        int moved = SuitEquipment.MigrateIntoSlots(p.Inventory, p.Equipment, key => _content.GetItem(key));
        p.EquipmentInitialised = true;
        if (moved > 0)
        {
            _log.Info($"Equipment slots (#2110): moved {moved} worn piece(s) of '{p.Name}' from the backpack into the slots.");
        }
    }

    /// <summary>Wears the gear in a backpack slot: a straight swap with whatever the equipment slot held (gear stacks to
    /// one, so the backpack slot is free the moment the piece leaves it). −1 picks the item's own slot, and for a module
    /// the first free module slot.</summary>
    private void HandleEquipItem(PlayerSession session, EquipItemIntent intent)
    {
        var p = session.State;
        var inv = p.Inventory;
        var eq = p.Equipment;
        int from = intent.FromSlot;
        if (from < 0 || from >= inv.SlotCount || inv.Slots[from] is not { IsEmpty: false } stack)
        {
            return;
        }

        var def = _content.GetItem(ItemKey.Base(stack.Item));
        if (def?.EquipSlot is null || stack.Count != 1)
        {
            Reject(session, "equip", "@srv.equip.not_wearable");
            return;
        }

        int slot = intent.Slot;
        if (slot < 0)
        {
            slot = (int)EquipSlots.Parse(def.EquipSlot)!.Value;
            if (EquipSlots.IsModule((EquipSlot)slot) && eq.Slots[slot] is { IsEmpty: false } && eq.Slots[(int)EquipSlot.Module2] is null)
            {
                slot = (int)EquipSlot.Module2;
            }
        }

        if (slot < 0 || slot >= eq.SlotCount || !EquipSlots.Accepts((EquipSlot)slot, def.EquipSlot))
        {
            Reject(session, "equip", "@srv.equip.wrong_slot");
            return;
        }

        var worn = eq.Slots[slot];
        eq.SetSlot(slot, stack);
        inv.SetSlot(from, worn);
        AfterEquipmentChanged(session);
    }

    /// <summary>Takes gear off into a backpack slot (−1 = the first free slot past the quick-bar, then any); a target slot
    /// holding gear that fits the same equipment slot swaps.</summary>
    private void HandleUnequipItem(PlayerSession session, UnequipItemIntent intent)
    {
        var p = session.State;
        var inv = p.Inventory;
        var eq = p.Equipment;
        int slot = intent.Slot;
        if (slot < 0 || slot >= eq.SlotCount || eq.Slots[slot] is not { IsEmpty: false } worn)
        {
            return;
        }

        int to = intent.ToSlot;
        if (to < 0)
        {
            to = inv.FirstEmptySlot(HotbarSlots);
            if (to < 0)
            {
                to = inv.FirstEmptySlot(0);
            }

            if (to < 0)
            {
                Reject(session, "equip", "@srv.equip.no_room");
                return;
            }
        }
        else if (to >= inv.SlotCount)
        {
            return;
        }

        var target = inv.Slots[to];
        if (target is { IsEmpty: false })
        {
            var def = _content.GetItem(ItemKey.Base(target.Item));
            if (def?.EquipSlot is null || target.Count != 1 || !EquipSlots.Accepts((EquipSlot)slot, def.EquipSlot))
            {
                Reject(session, "equip", "@srv.equip.no_room");
                return;
            }
        }

        eq.SetSlot(slot, target);
        inv.SetSlot(to, worn);
        AfterEquipmentChanged(session);
    }

    /// <summary>What a change of worn gear settles at once: the oxygen never exceeds the tank now worn, a cloak or a
    /// jetpack without its gear ends, and the client gets the inventory, the vitals and (through the presence) the body.</summary>
    private void AfterEquipmentChanged(PlayerSession session)
    {
        var p = session.State;
        p.Oxygen = System.Math.Min(p.Oxygen, MaxOxygen(p));
        if (p.Stealthed && !Wears(p, StealthItem))
        {
            p.Stealthed = false;
        }

        if (p.Jetpacking && !Wears(p, JetpackItem))
        {
            p.Jetpacking = false;
        }

        SendInventory(session);
        SendPlayerState(session);
    }

    /// <summary>Test seams (#2110).</summary>
    public void EquipItemForTest(string playerId, int fromSlot, int slot = -1)
    {
        if (FindSessionByPlayerId(playerId) is { } session)
        {
            HandleEquipItem(session, new EquipItemIntent { FromSlot = fromSlot, Slot = slot });
        }
    }

    public void UnequipItemForTest(string playerId, int slot, int toSlot = -1)
    {
        if (FindSessionByPlayerId(playerId) is { } session)
        {
            HandleUnequipItem(session, new UnequipItemIntent { Slot = slot, ToSlot = toSlot });
        }
    }

    /// <summary>Drains suit energy while the jetpack fires; cuts thrust when the energy runs out.</summary>
    private void TickJetpack(PlayerSession session, double dt)
    {
        var p = session.State;
        if (!p.Jetpacking)
        {
            return;
        }

        p.SuitEnergy = System.Math.Max(0f, p.SuitEnergy - (float)(dt * JetpackDrainPerSecond));
        if (p.SuitEnergy <= 0f)
        {
            p.Jetpacking = false;
            SendPlayerState(session); // tell the client its tank is empty so it stops thrusting
        }
    }
}
