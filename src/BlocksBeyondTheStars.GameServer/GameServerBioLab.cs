// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Bio;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.State;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The bio lab block (#2203–#2206) on the server: the facade of <see cref="BioLabSystem"/> (ADR 0019), which owns the
/// lab verbs (analyse, mix, change, wash off, seedling) and the status effects. This partial keeps the members sibling
/// partials, the dispatch table, the tick and the tests call — same names, same signatures, each one line delegating —
/// and the explicit <see cref="IBioLabHost"/> implementation.
/// </summary>
public sealed partial class GameServer : IBioLabHost
{
    private bool BioUnlocked(PlayerState p, string blueprint) => _bioLabSystem.BioUnlocked(p, blueprint);

    private void HandleBioLab(PlayerSession session, BioLabIntent intent) => _bioLabSystem.HandleBioLab(session, intent);

    private bool TryTakePreparation(PlayerSession session, string itemKey) => _bioLabSystem.TryTakePreparation(session, itemKey);

    private void TickBioEffects(PlayerSession session, double dt) => _bioLabSystem.TickBioEffects(session, dt);

    private static float AbsorbWithShield(PlayerState p, float damage) => BioLabSystem.AbsorbWithShield(p, damage);

    private static NetEffect[] DumpEffects(PlayerState p) => BioLabSystem.DumpEffects(p);

    // ---------------- Test seams ----------------

    /// <summary>Test seam: a lab intent as if the client had sent it.</summary>
    public void BioLabForTest(PlayerSession session, BioLabIntent intent) => HandleBioLab(session, intent);

    /// <summary>Test seam: starts an effect on a player directly.</summary>
    public void StartEffectForTest(PlayerSession session, BioEffect effect, int level, float seconds,
        BioSideEffect side = BioSideEffect.None, int sideLevel = 0, BioThermal thermal = BioThermal.Stable)
        => BioLabSystem.StartEffect(session.State, new ActiveEffect { Effect = effect, Level = level, SecondsLeft = seconds, Side = side, SideLevel = sideLevel, Thermal = thermal });

    /// <summary>Test seam: the mix signatures in a player's research book.</summary>
    public IReadOnlyCollection<string> BioReactionsForTest(string playerId) => ResearchOf(playerId).Reactions;

    // IBioLabHost: each member delegates to the server's own member of the same name (never to the interface member,
    // which would recurse).
    void IBioLabHost.ShipAiHintOnce(PlayerSession session, string hintId) => ShipAiHintOnce(session, hintId);

    bool IBioLabHost.IsVisitingShip(string playerId) => IsVisitingShip(playerId);

    bool IBioLabHost.InShipInterior(string playerId) => InShipInterior(playerId);

    ShipState IBioLabHost.ShipOf(PlayerSession session) => ShipOf(session);

    bool IBioLabHost.NearStationBlock(PlayerState player, string blockKey) => NearStationBlock(player, blockKey);

    bool IBioLabHost.StationAvailable(PlayerState player, CraftingStation station) => StationAvailable(player, station);

    float IBioLabHost.AmbientTemperature(PlayerSession session) => AmbientTemperature(session);

    ShipState IBioLabHost.Ship => _ship;

    BioSpeciesEntry? IBioLabHost.BioEntry(uint seed) => BioEntry(seed);

    BioProfile? IBioLabHost.BioProfileOf(uint seed) => BioProfileOf(seed);

    MaterialProfile? IBioLabHost.MaterialProfileOf(uint seed) => MaterialProfileOf(seed);

    BioResearch IBioLabHost.ResearchOf(string playerId) => ResearchOf(playerId);

    void IBioLabHost.SaveResearch(string playerId) => SaveResearch(playerId);

    void IBioLabHost.SendBioSpecies(PlayerSession session, uint seed, bool resend) => SendBioSpecies(session, seed, resend);

    float IBioLabHost.ScanMultiplier(PlayerState p) => ScanMultiplier(p);

    float IBioLabHost.MaxSuitEnergy(PlayerState p) => MaxSuitEnergy(p);
}
