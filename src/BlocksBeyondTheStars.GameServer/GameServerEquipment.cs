// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.State;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The suit's equipment slots (#2110) and the effects of the gear a player <b>wears</b> in them — gear in the backpack
/// does nothing: armour damage resistance, extra oxygen and suit energy capacity (tanks, the suit battery #2297), thermal
/// and corrosion protection, fall protection, the stealth field, the jetpack and the glider (#2296). Wearing is a swap
/// between a backpack slot — or, aboard, a slot of the cargo hold (#2289) — and the piece's equipment slot.
/// Server-authoritative — these feed the vitals/combat/scan systems. The formulas live in <see cref="SuitEquipment"/>
/// (Shared) so the client's Suit tab and HUD show exactly what the server applies (#1270); data-driven via the item
/// definitions (<c>equipSlot</c>, <c>armorResistance</c>, <c>oxygenBonus</c>, <c>suitEnergyBonus</c>,
/// <c>thermalInsulation</c>, <c>fallProtection</c>, …).
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

    /// <summary>Maximum suit energy (#2297) — base 100 plus the best worn battery's bonus (tiers do not stack). Every
    /// recharge fills up to it and every change of worn gear clamps to it.</summary>
    private float MaxSuitEnergy(PlayerState p)
        => SuitEquipment.MaxSuitEnergy(_content.Items.Values, key => Wears(p, key));

    /// <summary>Best carried thermal insulation 0..0.9 (#669); only the BEST piece counts. A heat ward counts in the heat,
    /// a cold ward in the cold (#2202) — judged by the air the player is really in (#2218), and looked up only for a
    /// player who has a ward running.</summary>
    private float ThermalInsulation(PlayerState p)
    {
        float heatWard = Shared.Bio.PlayerEffects.ThermalBonus(p.Effects, hot: true);
        float coldWard = Shared.Bio.PlayerEffects.ThermalBonus(p.Effects, hot: false);
        float ward = 0f;
        if ((heatWard > 0f || coldWard > 0f) && FindSessionByPlayerId(p.PlayerId) is { } session)
        {
            float temperature = AmbientTemperature(session);
            ward = temperature > Shared.Bio.BioRules.HotAbove ? heatWard
                : temperature < Shared.Bio.BioRules.ColdBelow ? coldWard
                : 0f;
        }

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

    private const string GliderItem = "glider";

    /// <summary>Sets the player's glide state (#2296, client-driven). Opening the wing needs a WORN glider; it costs no
    /// energy, and the glide itself — falling slowly forward, only where there is air — is the client's movement. The
    /// flag feeds the presence so other players see the wing.</summary>
    private void HandleSetGliding(PlayerSession session, SetGlidingIntent intent)
    {
        var p = session.State;
        if (!intent.Active)
        {
            p.Gliding = false;
            return;
        }

        if (!Wears(p, GliderItem))
        {
            p.Gliding = false;
            Reject(session, "glider", "@srv.equip.no_glider");
            return;
        }

        p.Gliding = true;
    }

    /// <summary>Test seam (#2296): the glide intent as if the client had sent it.</summary>
    public void SetGlidingForTest(string playerId, bool active)
    {
        if (FindSessionByPlayerId(playerId) is { } session)
        {
            HandleSetGliding(session, new SetGlidingIntent { Active = active });
        }
    }

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
    /// the first free of the four module slots (#2293). Aboard, the piece may come straight from the cargo hold (#2289):
    /// the piece worn before then goes into the backpack — past the quick-bar first — and only when the backpack is full
    /// back into the hold slot the new piece left.</summary>
    private void HandleEquipItem(PlayerSession session, EquipItemIntent intent)
    {
        var p = session.State;
        if (intent.FromCargo && !p.AboardShip)
        {
            Reject(session, "equip", "@srv.misc.aboard_for_cargo");
            return;
        }

        var source = intent.FromCargo ? _ship.Cargo : p.Inventory;
        var eq = p.Equipment;
        int from = intent.FromSlot;
        if (from < 0 || from >= source.SlotCount || source.Slots[from] is not { IsEmpty: false } stack)
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
            var own = EquipSlots.Parse(def.EquipSlot)!.Value;
            slot = (int)(EquipSlots.IsModule(own) ? EquipSlots.ModuleSlotFor(eq) : own);
        }

        if (slot < 0 || slot >= eq.SlotCount || !EquipSlots.Accepts((EquipSlot)slot, def.EquipSlot))
        {
            Reject(session, "equip", "@srv.equip.wrong_slot");
            return;
        }

        var worn = eq.Slots[slot];
        eq.SetSlot(slot, stack);
        source.SetSlot(from, null);
        if (worn is { IsEmpty: false })
        {
            // From the backpack the old piece takes the slot the new one left. From the hold it goes to the player — the
            // backpack, past the quick-bar first — and only a full backpack sends it back into the freed hold slot.
            int to = from;
            var target = source;
            if (intent.FromCargo)
            {
                int free = p.Inventory.FirstEmptySlot(HotbarSlots);
                if (free < 0)
                {
                    free = p.Inventory.FirstEmptySlot(0);
                }

                if (free >= 0)
                {
                    to = free;
                    target = p.Inventory;
                }
            }

            target.SetSlot(to, worn);
        }

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

    /// <summary>What a change of worn gear settles at once: the oxygen never exceeds the tank now worn nor the suit energy
    /// the battery now worn (#2297), a cloak, a jetpack or a glide without its gear ends, and the client gets the inventory
    /// (the hold too, aboard), the vitals and (through the presence) the body.</summary>
    private void AfterEquipmentChanged(PlayerSession session)
    {
        var p = session.State;
        p.Oxygen = System.Math.Min(p.Oxygen, MaxOxygen(p));
        p.SuitEnergy = System.Math.Min(p.SuitEnergy, MaxSuitEnergy(p));
        if (p.Stealthed && !Wears(p, StealthItem))
        {
            p.Stealthed = false;
        }

        if (p.Jetpacking && !Wears(p, JetpackItem))
        {
            p.Jetpacking = false;
        }

        if (p.Gliding && !Wears(p, GliderItem))
        {
            p.Gliding = false;
        }

        SendInventory(session);
        SendPlayerState(session);
    }

    /// <summary>Test seams (#2110; <paramref name="fromCargo"/> #2289).</summary>
    public void EquipItemForTest(string playerId, int fromSlot, int slot = -1, bool fromCargo = false)
    {
        if (FindSessionByPlayerId(playerId) is { } session)
        {
            Serve(session); // as the dispatch does: the hold is the sender's ship's
            HandleEquipItem(session, new EquipItemIntent { FromSlot = fromSlot, Slot = slot, FromCargo = fromCargo });
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
