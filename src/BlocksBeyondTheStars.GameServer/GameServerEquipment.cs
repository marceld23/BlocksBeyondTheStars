// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.State;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The suit's equipment slots (#2110) and the worn gear on the server: the facade of <see cref="EquipmentSystem"/>
/// (ADR 0019), which owns the gear formulas (armour, oxygen, suit energy, insulation, corrosion, fall protection, scan
/// bonus), the stealth field, the jetpack, the glider and the wear/take-off verbs. This partial keeps the members sibling
/// partials, the dispatch table, the tick and the tests call — same names, same signatures, each one line delegating —
/// and the explicit <see cref="IEquipmentHost"/> implementation.
/// </summary>
public sealed partial class GameServer : IEquipmentHost
{
    private float MaxOxygen(PlayerState p) => _equipmentSystem.MaxOxygen(p);

    private float MaxSuitEnergy(PlayerState p) => _equipmentSystem.MaxSuitEnergy(p);

    private float ThermalInsulation(PlayerState p) => _equipmentSystem.ThermalInsulation(p);

    private float CorrosionResistance(PlayerState p) => _equipmentSystem.CorrosionResistance(p);

    private float ScanMultiplier(PlayerState p) => _equipmentSystem.ScanMultiplier(p);

    private float Mitigate(PlayerState p, float damage) => _equipmentSystem.Mitigate(p, damage);

    /// <summary>Toggles the stealth field on/off if the player carries a stealth suit and has energy.</summary>
    public void ToggleStealth(string playerId) => _equipmentSystem.ToggleStealth(playerId);

    private void TickStealth(PlayerSession session, double dt) => _equipmentSystem.TickStealth(session, dt);

    private void HandleToggleStealth(PlayerSession session) => _equipmentSystem.HandleToggleStealth(session);

    private void HandleSetJetpack(PlayerSession session, SetJetpackIntent intent) => _equipmentSystem.HandleSetJetpack(session, intent);

    private void HandleSetGliding(PlayerSession session, SetGlidingIntent intent) => _equipmentSystem.HandleSetGliding(session, intent);

    /// <summary>True while the gear is WORN in one of the suit's slots (<see cref="EquipmentSystem.Wears"/>).</summary>
    private static bool Wears(PlayerState p, string key) => EquipmentSystem.Wears(p, key);

    private float FallProtection(PlayerState p) => _equipmentSystem.FallProtection(p);

    private void EnsureEquipmentInitialised(PlayerState p) => _equipmentSystem.EnsureEquipmentInitialised(p);

    private void HandleEquipItem(PlayerSession session, EquipItemIntent intent) => _equipmentSystem.HandleEquipItem(session, intent);

    private void HandleUnequipItem(PlayerSession session, UnequipItemIntent intent) => _equipmentSystem.HandleUnequipItem(session, intent);

    private void TickJetpack(PlayerSession session, double dt) => _equipmentSystem.TickJetpack(session, dt);

    // ---------------- Test seams ----------------

    /// <summary>Test seam (#2296): the glide intent as if the client had sent it.</summary>
    public void SetGlidingForTest(string playerId, bool active)
    {
        if (FindSessionByPlayerId(playerId) is { } session)
        {
            HandleSetGliding(session, new SetGlidingIntent { Active = active });
        }
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

    // IEquipmentHost: each member delegates to the server's own member of the same name (never to the interface member,
    // which would recurse).
    float IEquipmentHost.AmbientTemperature(PlayerSession session) => AmbientTemperature(session);

    ShipState IEquipmentHost.Ship => _ship;

    float IEquipmentHost.AbsorbWithShield(PlayerState p, float damage) => AbsorbWithShield(p, damage);
}
