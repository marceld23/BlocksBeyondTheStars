// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Missions;
using BlocksBeyondTheStars.Shared.Story;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Story-pack conditions on the server (#2465): the <see cref="IStoryConditionContext"/> for one player and the
/// registry of <c>world:</c> predicates. The grammar and the pure evaluation live in Shared
/// (<see cref="StoryConditionParser"/>); the step runtime parses a pack's conditions once at load and evaluates them
/// here per player. Every predicate reads the player's own body/system and the save's rules — never the active world
/// cursor, so evaluating one player's condition from inside another player's handler or tick cannot shift it.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The distance <c>world:player_near</c> counts as "near" (blocks, same body).</summary>
    private const float PlayerNearRange = 32f;

    private Dictionary<string, Func<PlayerSession, bool>>? _worldPredicates;

    /// <summary>The <c>world:</c> predicates by name. One line per predicate — a new one is a name in
    /// <see cref="StoryWorldPredicates"/> plus a line here (<c>StoryConditionTests</c> holds the two lists equal).</summary>
    private Dictionary<string, Func<PlayerSession, bool>> WorldPredicates => _worldPredicates ??= new(StringComparer.Ordinal)
    {
        // Place: the player's own body / system.
        [StoryWorldPredicates.StationInSystem] = s => StationContactsForCurrentSystem(s.CurrentLocationId).Any(), // = the dock lesson's skip rule (#2460)
        [StoryWorldPredicates.SettlementReachable] = s => _worlds.Find(s.CurrentLocationId)?.Settlements.Count > 0,
        [StoryWorldPredicates.Airless] = s => !BodyBreathable(s.CurrentLocationId),
        [StoryWorldPredicates.FragmentOnWorld] = s => _worlds.Find(s.CurrentLocationId)?.NetFragments.Count > 0,
        [StoryWorldPredicates.PlayerNear] = PlayerNear,

        // Rules of the save (a per-player mode override counts for "creative").
        [StoryWorldPredicates.Enemies] = _ => PlanetEnemiesActive,
        [StoryWorldPredicates.SpaceEnemies] = _ => Rules.SpaceCombat is SpaceCombatMode.PvE or SpaceCombatMode.Both
                                                   && (Rules.SpaceNpcEnemies != AlienActivity.Off || Rules.AlienUfos != AlienActivity.Off),
        [StoryWorldPredicates.Bandits] = _ => BanditsActive,
        [StoryWorldPredicates.FreeFlight] = _ => Rules.FreeSpaceFlight,
        [StoryWorldPredicates.Hunger] = _ => Rules.HungerConsumption != HungerConsumption.Off,
        [StoryWorldPredicates.Oxygen] = _ => Rules.OxygenConsumption != OxygenConsumption.Off,
        [StoryWorldPredicates.Hazards] = _ => Rules.EnvironmentalHazards != HazardLevel.Off,
        [StoryWorldPredicates.Creative] = s => !Rules.CraftingCostsMaterialsFor(s.State.ModeOverride),
        [StoryWorldPredicates.CreativeFlight] = _ => Rules.CreativeFlight,
        [StoryWorldPredicates.InstantTravel] = _ => Rules.InstantTravel,
        [StoryWorldPredicates.ReturnToShip] = _ => Rules.ReturnToShip,
        [StoryWorldPredicates.StarterTeleporter] = _ => Rules.StarterTeleporter,
        [StoryWorldPredicates.BaseVisitors] = _ => BaseVisitorsActive,
        [StoryWorldPredicates.SpaceStations] = _ => _meta.Description.SpaceStations != Frequency.Off,
        [StoryWorldPredicates.StoryActive] = _ => StoryActive,

        // #2469: the tutorial's feasibility checks (GameServerTutorial.cs) — probes run only when a step activates.
        [StoryWorldPredicates.FoodPlantsNear] = s => NearestBlockOf(s, FoodPlantBlocks) is not null,
        [StoryWorldPredicates.TreesNear] = s => NearestBlockOf(s, TreeBlocks) is not null,
        [StoryWorldPredicates.ShapeBlockCarried] = ShapeBlockCarried,
        [StoryWorldPredicates.ResearchReady] = ResearchReady,
        [StoryWorldPredicates.AllBlueprints] = _ => CreativeGrantsActive,
        [StoryWorldPredicates.NewWorldInSystem] = NewWorldInSystem,

        // #2476: the VEGA story missions' feasibility — what the world can offer, and the unique sites by the lead's own rule.
        [StoryWorldPredicates.FragmentsLeft] = _ => _story is not null && _story.Fragments.Any(f => !_storyState.FoundFragmentKeys.Contains(f.Key)),
        [StoryWorldPredicates.ObservatoryReachable] = s => UniqueSiteFollowable(s, "observatory"),
        [StoryWorldPredicates.DerelictReachable] = s => UniqueSiteFollowable(s, "derelict"),
        [StoryWorldPredicates.Settlements] = _ => _meta.Description.Settlements != Frequency.Off,
        [StoryWorldPredicates.Vaults] = _ => _meta.Description.Vaults != Frequency.Off,
        [StoryWorldPredicates.SpaceWrecks] = _ => _meta.Description.Wrecks != Frequency.Off,

        // #2479: the tutorial's deep dives (C2–C5) — their offers at the moment of need (VEGA's own tip pass, no extra
        // probe) and the feasibility of their steps.
        [StoryWorldPredicates.DataCacheNear] = s => s.VegaTipLive.Contains("data_cache_near"),
        [StoryWorldPredicates.ScannerUnknown] = s => s.VegaTipLive.Contains("scanner_unknown"),
        [StoryWorldPredicates.JumpReady] = s => s.Ships.TryGetValue(s.ActiveShipId, out var ship) && ship.HasModule("jump_generator"),
        [StoryWorldPredicates.ArcadeReady] = s => s.State.UnlockedGames.Count > 0 || _worlds.Find(s.CurrentLocationId)?.DataCubes.Count > 0,
        [StoryWorldPredicates.MonumentUnread] = s => _worlds.Find(s.CurrentLocationId) is { } world && UnreadMonument(s, world) is not null,
        [StoryWorldPredicates.AsteroidMining] = _ => Rules.AsteroidDestruction != AsteroidDestructionMode.Off,
        [StoryWorldPredicates.Docked] = s => InStation(s.State.PlayerId),
        [StoryWorldPredicates.JobActive] = s => s.State.Missions.Any(m => m.Status == MissionStatus.Active
                                                                         && GetMissionDef(m.MissionId) is { } def && MissionAcceptCountsAsTrade(def)),
        [StoryWorldPredicates.ForgeReady] = ForgeReady,
        [StoryWorldPredicates.WormholeKnown] = s => _galaxy is not null
                                                    && _galaxy.Wormholes.Any(w => !IsStoryLockedSystem(w.SystemId) && s.State.KnownSystems.Contains(w.SystemId)),
        [StoryWorldPredicates.DebrisFields] = _ => _meta.Description.DebrisFields != Frequency.Off,

        // #2480: the deep dives "Ship building" and "Base building" (C1, C6, C7, C17) — their offers and their evidence
        // (GameServerTutorialBuilding.cs).
        [StoryWorldPredicates.TierGate] = s => _uptime - s.TierGateRefusedAt <= TierGateOfferWindowSeconds,
        [StoryWorldPredicates.ModuleAffordable] = ModuleAffordable,
        [StoryWorldPredicates.HullDamaged] = HullDamaged,
        [StoryWorldPredicates.KeelLaid] = s => s.Ships.Values.Any(ship => ship.IsCustom),
        [StoryWorldPredicates.ShipBuilt] = s => s.Ships.Values.Any(ship => ship.IsCustom && ship.Commissioned),
        [StoryWorldPredicates.BaseHere] = s => _bases.Any(b => b.Planet == s.CurrentLocationId && OwnOrAllied(b.OwnerId, s.State.PlayerId)),
        [StoryWorldPredicates.BaseCoreReady] = BaseCoreReady,
        [StoryWorldPredicates.NightOutside] = NightOutside,
        [StoryWorldPredicates.StationCoreReady] = StationCoreReady,
        [StoryWorldPredicates.StationOwned] = s => _playerStationCells.Values.Any(st => st.Boardable && st.OwnerId == s.State.PlayerId),

        // #2481: the deep dives "Crystal net" (C8, C14) — the factory course's offer (VEGA's own tip pass), what a world has
        // for it, and the automation lesson's evidence.
        [StoryWorldPredicates.FactoryNear] = s => s.VegaTipLive.Contains("factory_near"),
        [StoryWorldPredicates.FactoryHere] = s => _worlds.Find(s.CurrentLocationId)?.Factories.Count > 0,
        [StoryWorldPredicates.AccessCode] = s => s.State.Inventory.CountOf("access_code") > 0,
        [StoryWorldPredicates.FactoryOwned] = s => _meta.Claims.Any(c => c.OwnerId == s.State.PlayerId),
        [StoryWorldPredicates.MachineCircuit] = s => s.State.AchievementCounters.TryGetValue(AchievementCounters.Crystal(CircuitEvents.MachineJob), out int jobs) && jobs > 0,
        [StoryWorldPredicates.MachineCatchUp] = _ => Rules.MachineCatchUpMinutes > 0,
    };

    /// <summary><c>world:forge_ready</c> (#2479): the player can refine — they crafted a forge before (the achievement
    /// counter), carry one (backpack or the ship's hold), or their ship has the refinery module.</summary>
    private bool ForgeReady(PlayerSession who)
    {
        var p = who.State;
        if ((p.AchievementCounters.TryGetValue(AchievementCounters.Craft("forge"), out int crafted) && crafted > 0) || p.Inventory.Has("forge", 1))
        {
            return true;
        }

        return who.Ships.TryGetValue(who.ActiveShipId, out var ship) && (ship.HasModule("refinery") || ship.Cargo.Has("forge", 1));
    }

    /// <summary>The names the server can evaluate — what the pack loader passes to <see cref="StoryConditionParser"/>.</summary>
    internal IReadOnlyCollection<string> WorldPredicateNames => WorldPredicates.Keys;

    /// <summary>Evaluates a parsed condition for <paramref name="who"/>; a missing condition (null) holds.</summary>
    internal bool EvaluateStoryCondition(StoryCondition? condition, PlayerSession who)
        => StoryConditionParser.Holds(condition, new PlayerConditionContext(this, who));

    /// <summary>Test seam: parses <paramref name="text"/> against the server's predicates and evaluates it for a player.</summary>
    internal bool EvaluateStoryConditionForTest(string text, PlayerSession who)
        => EvaluateStoryCondition(StoryConditionParser.Parse(text, WorldPredicateNames), who);

    /// <summary>Whether a story pack runs for this player in this world (<c>pack:&lt;id&gt;:enabled</c>): the active world
    /// story, or an installed player-scoped pack the player has not switched off (#2466) — a tutorial by the world's
    /// <see cref="TutorialPolicy"/> (<c>Auto</c>: yes; <c>OptIn</c>: once the player started it; <c>Off</c>: no), any
    /// other player pack by its <c>defaultEnabled</c> or once the player started it.</summary>
    internal bool StoryPackEnabledFor(PlayerSession who, string packId)
    {
        if (_story is not null && string.Equals(_story.Id, packId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!_content.Stories.TryGetValue(packId, out var pack)
            || !string.Equals(StoryRegistry.EffectiveScope(pack), StoryRegistry.ScopePlayer, StringComparison.Ordinal)
            || who.State.Milestones.Contains(StoryPackKeys.OptOut(pack.Id)))
        {
            return false;
        }

        bool started = who.State.Milestones.Contains(StoryPackKeys.Started(pack.Id));
        if (string.Equals(pack.Kind, StoryRegistry.KindTutorial, StringComparison.OrdinalIgnoreCase))
        {
            return Rules.TutorialPolicy switch
            {
                TutorialPolicy.Off => false,
                TutorialPolicy.OptIn => started,
                _ => true,
            };
        }

        return pack.DefaultEnabled || started;
    }

    /// <summary>Whether the air of <paramref name="locationId"/> is breathable — the planet type's atmosphere, never on a
    /// player-built station deck (the same rule the weather init applies to the active world).</summary>
    private bool BodyBreathable(string locationId)
    {
        string? planetType = _worlds.Find(locationId)?.PlanetType ?? _galaxy?.FindBody(locationId)?.PlanetType;
        var planet = string.IsNullOrEmpty(planetType) ? null : _content.GetPlanet(planetType!);
        return string.Equals(planet?.Atmosphere, "breathable", StringComparison.OrdinalIgnoreCase)
               && !IsPlayerStationWorld(locationId);
    }

    /// <summary>Another visible, joined player stands within <see cref="PlayerNearRange"/> on the same body.</summary>
    private bool PlayerNear(PlayerSession who)
    {
        foreach (var other in _sessions.Values)
        {
            if (!ReferenceEquals(other, who) && other.Joined && !other.Spectating
                && other.CurrentLocationId == who.CurrentLocationId
                && WrapDistSq(other.State.Position, who.State.Position) <= PlayerNearRange * PlayerNearRange)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>One player's view for the condition evaluator: milestones (<see cref="StoryPackKeys"/>), blueprints,
    /// pack states and the world predicates.</summary>
    private sealed class PlayerConditionContext : IStoryConditionContext
    {
        private readonly GameServer _server;
        private readonly PlayerSession _who;

        public PlayerConditionContext(GameServer server, PlayerSession who)
        {
            _server = server;
            _who = who;
        }

        private HashSet<string> Milestones => _who.State.Milestones;

        // A step's legacy key (vega:stage:*) is migration evidence only (synthesis §5.3): the one-time migration
        // (story:<pack>:migrated:v1) turns it into the step key, which is what counts here.
        public bool IsStepDone(string packId, string stepId)
            => Milestones.Contains(StoryPackKeys.StepDone(packId, stepId))
               || Milestones.Contains(StoryPackKeys.StepSkipped(packId, stepId));

        // A player pack's chapter by its milestones; the WORLD story's chapter (#2470) is "reached" by the arc
        // (fromBeat / fromEvent) — for this player once their story track has started.
        public bool IsChapterDone(string packId, string chapterId)
            => Milestones.Contains(StoryPackKeys.ChapterDone(packId, chapterId))
               || Milestones.Contains(StoryPackKeys.ChapterSkipped(packId, chapterId))
               || _server.WorldChapterReachedFor(_who, packId, chapterId);

        public bool IsPackInState(string packId, StoryPackState state) => state switch
        {
            StoryPackState.Started => Milestones.Contains(StoryPackKeys.Started(packId)),
            StoryPackState.Done => Milestones.Contains(StoryPackKeys.Done(packId)),
            StoryPackState.OptedOut => Milestones.Contains(StoryPackKeys.OptOut(packId)),
            _ => _server.StoryPackEnabledFor(_who, packId),
        };

        public bool HasBlueprint(string blueprintKey) => _who.State.UnlockedBlueprints.Contains(blueprintKey);

        // #2476: the reader writes "lore:<key>" when a lore text opens for this player (GameServerLore).
        public bool HasReadLore(string loreKey) => Milestones.Contains(LoreMilestonePrefix + loreKey);

        public bool IsWorldPredicateTrue(string predicate)
            => _server.WorldPredicates.TryGetValue(predicate, out var test) && test(_who);
    }
}
