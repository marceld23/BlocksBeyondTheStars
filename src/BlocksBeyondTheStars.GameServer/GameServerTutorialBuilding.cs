// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.Story;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The server side of the tutorial's deep dives "Ship building" and "Base building" (#2480, synthesis §3.2/§3.4): C1
/// Workshop and titanium, C6 Ship builder, C7 Homestead, C17 Station builder. The <c>world:</c> predicates their offers and
/// evidence read (registered in <c>GameServerStoryConditions</c>), the "built at home" test behind <c>base:built</c>, and the
/// 1 Hz state checks behind <c>base:sealed</c> and <c>base:walled</c> — which run only while one of this player's active
/// steps waits for them. A base counts when the player owns it or shares it with an ally or a crewmate (E32: a crew lives in
/// one base); the predicates read the player's own body and fleet, never the active world cursor.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>How long after a refused swing <c>world:tier_gate</c> holds — long enough for the 90 s offer gap's next pass
    /// to find it, short enough that a refusal long ago is no "moment of need".</summary>
    private const double TierGateOfferWindowSeconds = 60.0;

    /// <summary>Whether a base of <paramref name="ownerId"/> counts for <paramref name="playerId"/>: their own, or an ally's or a
    /// crewmate's (<see cref="AreAllied"/> answers both).</summary>
    private bool OwnOrAllied(string ownerId, string playerId) => ownerId == playerId || AreAllied(ownerId, playerId);

    /// <summary><c>world:hull_damaged</c>: the player's active ship has lost hull points — a wreck on the pad too (a ship
    /// whose hull was never set up reads as whole).</summary>
    private bool HullDamaged(PlayerSession who)
        => who.Ships.TryGetValue(who.ActiveShipId, out var ship)
           && (ship.Downed || ship.Hull > 0f)
           && ship.Hull < ShipHullMaxFor(ship);

    /// <summary><c>world:module_affordable</c>: a ship module the player could build right now — not on their active ship yet
    /// (nor done better by a fitted higher tier), its blueprint owned, its materials in the backpack or the hold (or crafting
    /// is free here). The free starter modules never count: they are fitted from the start.</summary>
    private bool ModuleAffordable(PlayerSession who)
    {
        if (!who.Ships.TryGetValue(who.ActiveShipId, out var ship))
        {
            return false;
        }

        var p = who.State;
        bool free = !Rules.CraftingCostsMaterialsFor(p.ModeOverride);
        foreach (var module in _content.ShipModules.Values)
        {
            if (module.BuildCost.Count == 0 || ship.HasModule(module.Key)
                || ship.Modules.Any(m => _content.GetShipModule(m)?.Replaces.Contains(module.Key) == true)
                || (!string.IsNullOrEmpty(module.RequiredBlueprint) && !p.UnlockedBlueprints.Contains(module.RequiredBlueprint!)))
            {
                continue;
            }

            if (free || module.BuildCost.All(c => CarriedCount(who, ship, c.Item) >= c.Count))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary><c>world:base_core_ready</c>: the player carries a base core (backpack or the active ship's hold) or has crafted
    /// one before (the achievement counter).</summary>
    private bool BaseCoreReady(PlayerSession who)
        => (who.State.AchievementCounters.TryGetValue(AchievementCounters.Craft(BaseCoreBlock), out int crafted) && crafted > 0)
           || CarriedCount(who, ActiveShipOf(who), BaseCoreBlock) > 0;

    /// <summary><c>world:station_core_ready</c>: the player carries a station core, or could craft one now (its recipe's
    /// materials in the backpack or the hold, or crafting is free here).</summary>
    private bool StationCoreReady(PlayerSession who)
    {
        var ship = ActiveShipOf(who);
        if (CarriedCount(who, ship, StationCoreBlock) > 0)
        {
            return true;
        }

        if (_content.GetRecipe(StationCoreBlock) is not { } recipe)
        {
            return false;
        }

        return !Rules.CraftingCostsMaterialsFor(who.State.ModeOverride)
               || recipe.Inputs.All(i => CarriedCount(who, ship, i.Item) >= i.Count);
    }

    /// <summary><c>world:night_outside</c>: the player is out on foot on a world's surface (not aboard, not on a spacewalk,
    /// not in space or a station) while the local sun is down there — the clock of the player's own world.</summary>
    private bool NightOutside(PlayerSession who)
    {
        var p = who.State;
        if (p.AboardShip || p.InEva || InSpace(p.PlayerId) || InStation(p.PlayerId)
            || _worlds.Find(who.CurrentLocationId) is not { } loaded || loaded.World.Circumference <= 0
            || _content.GetPlanet(loaded.PlanetType)?.Void == true)
        {
            return false;
        }

        double t = ((_dayFraction + p.Position.X / (double)loaded.World.Circumference) % 1.0 + 1.0) % 1.0;
        return t < 0.25 || t > 0.75; // the sky's sunrise/sunset (IsNightAt)
    }

    /// <summary>How many of an item the player carries: backpack, worn gear and the given ship's hold (aboard or not — the
    /// offers ask "do you have it", not "can you take it out right here").</summary>
    private static int CarriedCount(PlayerSession who, ShipState? ship, string item)
        => who.State.Inventory.CountOf(item) + who.State.Equipment.CountOf(item) + (ship?.Cargo.CountOf(item) ?? 0);

    private static ShipState? ActiveShipOf(PlayerSession who) => who.Ships.TryGetValue(who.ActiveShipId, out var ship) ? ship : null;

    /// <summary>The "built at home" test behind <c>base:built</c>: the cell lies in the build zone of a base on the active
    /// world (the placing player's) that the player owns or shares with an ally or a crewmate (E32).</summary>
    private bool InOwnOrAlliedBaseZone(string playerId, Vector3i pos)
    {
        string body = _world.LocationId;
        foreach (var b in _bases)
        {
            if (b.Planet == body && OwnOrAllied(b.OwnerId, playerId) && WithinBaseZone(b.Cell, pos))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>1 Hz from <c>TutorialStateEvents</c> (the player's world is the active cursor there): a base the player owns
    /// or shares holds air in a sealed room on this world (<c>base:sealed</c>), the player stands inside a closed wall ring of
    /// such a base (<c>base:walled</c>). Each check only while one of THIS player's active steps waits for it — the air fill
    /// and the wall fill are cached per base, but nobody else should pay for them.</summary>
    private void TutorialBaseStateEvents(PlayerSession session)
    {
        var p = session.State;
        if (session.CurrentLocationId != _world.LocationId || InSpace(p.PlayerId) || _bases.Count == 0)
        {
            return;
        }

        if (StepWaitsFor(session, GameEventKeys.BaseSealed) && OwnOrAlliedBaseHoldsAir(p.PlayerId))
        {
            RaiseGameEvent(session, GameEventKeys.BaseSealed, _world.LocationId);
        }

        if (StepWaitsFor(session, GameEventKeys.BaseWalled) && StandsInOwnOrAlliedYard(session))
        {
            RaiseGameEvent(session, GameEventKeys.BaseWalled, _world.LocationId);
        }
    }

    /// <summary>Whether one of the player's active steps listens to <paramref name="key"/> (a pattern key too).</summary>
    private bool StepWaitsFor(PlayerSession s, string key)
    {
        if (!HasGameEventListener(key))
        {
            return false;
        }

        foreach (string progressKey in s.State.StepProgress.Keys)
        {
            if (_stepByProgressKey.TryGetValue(progressKey, out var step) && step.Event.Length > 0 && GameEventKeys.Matches(step.Event, key))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>A base on the active world that the player owns or shares has at least one supplied sealed room (the air
    /// model of <c>GameServerBaseAir</c>, refreshed at its own pace).</summary>
    private bool OwnOrAlliedBaseHoldsAir(string playerId)
    {
        string body = _world.LocationId;
        foreach (var b in _bases)
        {
            if (b.Planet == body && OwnOrAllied(b.OwnerId, playerId) && RefreshBaseAir(b, broadcastOnChange: true).Rooms > 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The player's feet stand in a fenced-in part of a base they own or share (the wall model of
    /// <c>GameServerBaseWalls</c>: the outside-in fill at the feet level does not reach them).</summary>
    private bool StandsInOwnOrAlliedYard(PlayerSession session)
    {
        var p = session.State;
        if (p.AboardShip || p.InEva)
        {
            return false;
        }

        int circ = _world.Circumference;
        var feet = WorldConstants.CanonicalBlock(p.Position.ToBlock(), circ);
        foreach (var b in _bases)
        {
            if (b.Planet != _world.LocationId || !OwnOrAllied(b.OwnerId, p.PlayerId) || !WithinBaseWallReach(b, feet))
            {
                continue;
            }

            var level = RefreshBaseWalls(b, feet.Y);
            if (!level.FailOpen && !level.Contains(feet, circ))
            {
                return true;
            }
        }

        return false;
    }
}
