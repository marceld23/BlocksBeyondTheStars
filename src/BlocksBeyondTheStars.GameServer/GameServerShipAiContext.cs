// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>Cached results of the heavier block probe around a player (refreshed every few seconds, not
/// every advisor tick). All fields describe the moment of the probe.</summary>
public sealed class VegaTipProbe
{
    /// <summary>Solid blocks in the column above the head (a cave roof reads high, a hut roof low).</summary>
    public int SolidAbove { get; set; }

    /// <summary>A torch / lantern / campfire / lava / fire within a few blocks — the spot is lit.</summary>
    public bool LightNear { get; set; }

    /// <summary>A placed torch or lantern within the probe box (the "drop a torch" tip stays quiet).</summary>
    public bool TorchNear { get; set; }

    /// <summary>Exposed (air-adjacent) ore blocks in the box, nearest first: block key + squared distance.</summary>
    public List<(string Key, int DistSq)> ExposedOres { get; } = new();

    /// <summary>Position of the nearest data cache / crystal in the box, or null.</summary>
    public Vector3i? DataCache { get; set; }
}

/// <summary>
/// VEGA context tips (#1077–#1082): situational advice that may REPEAT — rarely. Unlike the once-per-save
/// advisor hints (<see cref="ShipAiHintOnce"/>) every tip here has a dwell time (the situation must hold for a
/// few seconds), a per-tip cooldown, a hard repeat cap per save (after which VEGA considers it learned) and
/// a priority; all tips share one cadence per player (one line every couple of minutes at most, quiet right
/// after joining, banter counts against it too). Repeat counters persist as milestones without any schema
/// change: <c>vega:hint:&lt;id&gt;</c> for the first occurrence (exactly what the tips log already lists),
/// <c>vega:hint:&lt;id&gt;#2</c>, <c>#3</c>… afterwards and <c>#done</c> once the player reacted to a tip.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Minimum seconds between two context tips for one player (banter shares this cadence).</summary>
    private const double VegaTipGap = 120.0;

    /// <summary>No context tip in the first minute of a session — the join is busy enough.</summary>
    private const double VegaTipJoinQuiet = 60.0;

    /// <summary>A reaction this many seconds after a tip counts as "learned" and retires the tip for good.</summary>
    private const double VegaTipLearnWindow = 30.0;

    /// <summary>Seconds between the block probes around a player (the only non-trivial cost in here).</summary>
    private const double VegaTipProbeInterval = 10.0;

    /// <summary>Half-size of the block probe box (17³ loaded-chunk reads every probe).</summary>
    private const int VegaTipProbeRadius = 8;

    /// <summary>Column scan above the head; this many solid blocks up there = underground.</summary>
    private const int VegaTipColumnScan = 40;
    private const int VegaTipUndergroundSolid = 6;

    /// <summary>Ore rarity (per planet ore table) at or below which VEGA calls an ore "rare here".</summary>
    private const double VegaTipRareOreRarity = 0.03;

    /// <summary>The kind byte for a REPEATED context tip. The first occurrence of any tip goes out as a Kind-1
    /// advisor line (teaching moment, appended to the tips log); repeats use Kind 5 so the client can drop
    /// them when its speech queue is already busy. Both obey the VegaHints settings mute.</summary>
    private const byte VegaTipRepeatKind = 5;

    private enum VegaTipPriority { Safety = 0, Equipment = 1, Opportunity = 2 }

    private readonly record struct VegaTipSpec(
        string Id, VegaTipPriority Priority, double Dwell, double Cooldown, int MaxRepeats, bool AfterScanStage);

    /// <summary>Every context tip VEGA knows, in tie-break order (earlier wins on equal priority).</summary>
    private static readonly VegaTipSpec[] VegaTipTable =
    {
        // Vitals (#1082): the first occurrence is the old once-hint, later ones repeat with a long cooldown.
        new("o2",             VegaTipPriority.Safety,      0,  900, 4, false),
        new("energy",         VegaTipPriority.Safety,      0,  900, 4, false),
        new("hunger",         VegaTipPriority.Safety,      0,  900, 4, false),
        new("cold",           VegaTipPriority.Safety,      0,  900, 3, false),
        new("heat",           VegaTipPriority.Safety,      0,  900, 3, false),
        new("medkit",         VegaTipPriority.Safety,      3,  600, 3, false),
        new("hull_low",       VegaTipPriority.Safety,      3,  600, 3, false),
        // Equipment (#1078): "you have X but aren't using it".
        new("lamp_off",       VegaTipPriority.Equipment,   8,  600, 3, false),
        new("lamp_missing",   VegaTipPriority.Equipment,  10,  900, 2, false),
        new("torch_underground", VegaTipPriority.Equipment, 15, 900, 2, false),
        new("eat_now",        VegaTipPriority.Equipment,   5,  600, 3, false),
        new("wrong_tool",     VegaTipPriority.Equipment,   0,  600, 3, false),
        new("scanner_idle",   VegaTipPriority.Equipment,   0,  900, 2, true),
        new("speeder_far",    VegaTipPriority.Equipment,  10,  900, 2, true),
        // Materials + progression (#1079).
        new("rare_ore_near",  VegaTipPriority.Opportunity, 3,  600, 3, true),
        new("needed_ore_near", VegaTipPriority.Opportunity, 3, 600, 3, true),
        new("data_cache_near", VegaTipPriority.Opportunity, 3, 900, 2, true),
        new("craftable_now",  VegaTipPriority.Opportunity, 0,  900, 3, true),
        new("blueprint_affordable", VegaTipPriority.Opportunity, 0, 900, 3, true),
        // Places + company (#1080).
        new("settlement_near", VegaTipPriority.Opportunity, 5, 900, 3, true),
        new("ruin_near",      VegaTipPriority.Opportunity, 5,  900, 3, true),
        new("factory_near",   VegaTipPriority.Opportunity, 5,  900, 3, true),
        new("treasure_near",  VegaTipPriority.Opportunity, 5,  900, 3, true),
        new("trader_near",    VegaTipPriority.Opportunity, 5,  900, 3, true),
        new("tameable_near",  VegaTipPriority.Opportunity, 5,  900, 3, true),
        new("player_near",    VegaTipPriority.Opportunity, 5,  900, 3, true),
        // Space (#1081).
        new("asteroid_near",  VegaTipPriority.Opportunity, 5,  900, 3, true),
        new("asteroid_no_tool", VegaTipPriority.Opportunity, 5, 900, 2, true),
        new("station_near",   VegaTipPriority.Opportunity, 5,  900, 2, true),
        new("jump_ready",     VegaTipPriority.Opportunity, 0, 1800, 2, true),
    };

    private static readonly Dictionary<string, VegaTipSpec> VegaTips = VegaTipTable.ToDictionary(t => t.Id);

    /// <summary>Sort key for a cadence slot: priority first, then table order — so ties resolve the same
    /// way regardless of the order the conditions happen to be evaluated in.</summary>
    private static int VegaTipRank(VegaTipSpec s) => (int)s.Priority * 1000 + System.Array.IndexOf(VegaTipTable, s);

    /// <summary>Marker milestone: the player reacted to this tip — never again.</summary>
    private static string VegaTipDoneKey(string id) => "vega:hint:" + id + "#done";

    /// <summary>How often this tip has fired for the save (int.MaxValue once retired).</summary>
    private static int VegaTipCount(PlayerState p, string id)
    {
        if (p.Milestones.Contains(VegaTipDoneKey(id)))
        {
            return int.MaxValue;
        }

        string first = "vega:hint:" + id;
        string prefix = first + "#";
        int n = 0;
        foreach (var m in p.Milestones)
        {
            if (m == first || (m.StartsWith(prefix, System.StringComparison.Ordinal) && m.Length > prefix.Length))
            {
                n++;
            }
        }

        return n;
    }

    /// <summary>The player did the thing a recent tip suggested — retire that tip for this save.</summary>
    private void ShipAiTipLearned(PlayerSession session, string id)
    {
        if (session.VegaTipLastId != id || _uptime - session.VegaTipLastAt > VegaTipLearnWindow)
        {
            return;
        }

        if (session.State.Milestones.Add(VegaTipDoneKey(id)))
        {
            _repo.SavePlayer(session.State);
        }
    }

    /// <summary>Test seam: retire a tip as if the player had reacted right after it.</summary>
    public void VegaTipLearnedForTest(string playerId, string tipId)
    {
        if (FindSessionByPlayerId(playerId) is { } s)
        {
            s.VegaTipLastId = tipId;
            s.VegaTipLastAt = _uptime;
            ShipAiTipLearned(s, tipId);
        }
    }

    /// <summary>Test seam: forgets every context-tip cooldown and the cadence for a player — "enough time
    /// passed" without simulating a quarter hour of ticks (which would trip the silent-session sweep).</summary>
    public void SkipVegaTipCooldownsForTest(string playerId)
    {
        if (FindSessionByPlayerId(playerId) is { } s)
        {
            s.VegaTipCooldownUntil.Clear();
            s.VegaTipReadyAt = 0.0;
        }
    }

    /// <summary>Test seam: the tip ids whose conditions hold for a player right now (before dwell,
    /// cooldown, cap and cadence are applied) — plus the probe's view of the surroundings.</summary>
    public (IReadOnlyList<string> Candidates, int SolidAbove, bool LightNear, bool Aboard) VegaTipCandidatesForTest(string playerId)
    {
        if (FindSessionByPlayerId(playerId) is not { } s)
        {
            return (System.Array.Empty<string>(), 0, false, false);
        }

        var list = new List<(VegaTipSpec Spec, string Arg, string Mention)>();
        CollectVegaTipCandidates(s, list);
        return (list.Select(c => c.Spec.Id).ToList(), s.VegaProbe.SolidAbove, s.VegaProbe.LightNear, s.State.AboardShip);
    }

    /// <summary>Test seam: pacing state of one tip for a player (fired count, cooldown end, cadence end,
    /// dwell start or -1, server uptime).</summary>
    public (int Count, double CooldownUntil, double ReadyAt, double Since, double Uptime) VegaTipStateForTest(string playerId, string tipId)
    {
        if (FindSessionByPlayerId(playerId) is not { } s)
        {
            return (0, 0, 0, -1, _uptime);
        }

        return (VegaTipCount(s.State, tipId),
            s.VegaTipCooldownUntil.TryGetValue(tipId, out var cd) ? cd : 0,
            s.VegaTipReadyAt,
            s.VegaTipSince.TryGetValue(tipId, out var since) ? since : -1,
            _uptime);
    }

    /// <summary>Test seam: the suit-lamp state the server holds for a player.</summary>
    public bool LampOnForTest(string playerId) => FindSessionByPlayerId(playerId)?.LampOn ?? false;

    /// <summary>Test seam: sets the world clock (0..1 day fraction) so night-only tips can be exercised.</summary>
    public void SetDayFractionForTest(double fraction) => _dayFraction = fraction;

    /// <summary>Client → server: the suit lamp was toggled. Only the "you have a lamp but it is off" tip
    /// depends on it — an unknown state stays "off", which is also what a fresh client starts with.</summary>
    private void HandleSetLamp(PlayerSession session, SetLampIntent intent)
    {
        session.LampOn = intent.On;
        if (intent.On)
        {
            ShipAiTipLearned(session, "lamp_off");
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Event hooks feeding the tips.
    // ---------------------------------------------------------------------------------------------

    /// <summary>Every broken block: digging score, the by-hand streak and the rare-ore learned check.</summary>
    private void ShipAiOnBlockBroken(PlayerSession session, string blockKey)
    {
        var p = session.State;
        session.VegaMineRecent += 1.0;

        bool holdsDrill = _content.GetItem(HeldItemKey(p))?.Tool?.Kind == ToolKind.Drill;
        session.VegaHandMineStreak = holdsDrill ? 0 : session.VegaHandMineStreak + 1;

        if (blockKey.EndsWith("_ore", System.StringComparison.Ordinal))
        {
            ShipAiTipLearned(session, "rare_ore_near");
            ShipAiTipLearned(session, "needed_ore_near");
        }
    }

    /// <summary>The terrain scanner was used — the "you carry a scanner" nudge stays quiet for a while.</summary>
    private void ShipAiOnScannerUsed(PlayerSession session) => session.VegaScannerUsedAt = _uptime;

    // ---------------------------------------------------------------------------------------------
    // The 1 Hz evaluation (called from TickShipAi per session).
    // ---------------------------------------------------------------------------------------------

    private void TickVegaContextTips(PlayerSession session)
    {
        var p = session.State;
        session.VegaMineRecent *= 0.9;

        var candidates = new List<(VegaTipSpec Spec, string Arg, string Mention)>();
        CollectVegaTipCandidates(session, candidates);

        // Dwell bookkeeping: remember when each candidate first appeared; forget the ones that went away.
        var seen = new HashSet<string>();
        foreach (var c in candidates)
        {
            seen.Add(c.Spec.Id);
            if (!session.VegaTipSince.ContainsKey(c.Spec.Id))
            {
                session.VegaTipSince[c.Spec.Id] = _uptime;
            }
        }

        foreach (var id in session.VegaTipSince.Keys.ToList())
        {
            if (!seen.Contains(id))
            {
                session.VegaTipSince.Remove(id);
            }
        }

        bool gapOpen = _uptime >= session.VegaTipReadyAt; // cadence: has VEGA been quiet long enough?
        int stage = VegaStageIndex(p);
        (VegaTipSpec Spec, string Arg, string Mention)? best = null;
        foreach (var c in candidates)
        {
            var s = c.Spec;
            if (s.AfterScanStage && stage < 3)
            {
                continue; // opportunity tips wait until mine/craft/eat are done (the "scan" stage)
            }

            if (_uptime - session.VegaTipSince[s.Id] < s.Dwell)
            {
                continue;
            }

            if (session.VegaTipCooldownUntil.TryGetValue(s.Id, out var until) && _uptime < until)
            {
                continue;
            }

            int count = VegaTipCount(p, s.Id);
            if (count >= s.MaxRepeats)
            {
                continue;
            }

            // The FIRST safety hint (low O2, freezing …) is the old teaching moment and never waits for
            // the cadence — everything else queues behind whatever VEGA said last.
            if (!gapOpen && !(s.Priority == VegaTipPriority.Safety && count == 0))
            {
                continue;
            }

            if (best is null || VegaTipRank(s) < VegaTipRank(best.Value.Spec))
            {
                best = c;
            }
        }

        if (best is { } pick)
        {
            FireVegaTip(session, pick.Spec, pick.Arg, pick.Mention);
        }
    }

    private void FireVegaTip(PlayerSession session, VegaTipSpec spec, string arg, string mention)
    {
        var p = session.State;
        int count = VegaTipCount(p, spec.Id);
        p.Milestones.Add(count == 0 ? "vega:hint:" + spec.Id : "vega:hint:" + spec.Id + "#" + (count + 1));
        _repo.SavePlayer(p);

        session.VegaTipReadyAt = _uptime + VegaTipGap;
        session.VegaTipCooldownUntil[spec.Id] = _uptime + spec.Cooldown;
        session.VegaTipSince.Remove(spec.Id);
        session.VegaTipLastId = spec.Id;
        session.VegaTipLastAt = _uptime;
        if (mention.Length > 0)
        {
            session.VegaTipMentioned.Add(mention);
        }

        SendVegaLine(session, "vega.hint." + spec.Id, count == 0 ? (byte)1 : VegaTipRepeatKind, arg);
    }

    // ---------------------------------------------------------------------------------------------
    // Conditions.
    // ---------------------------------------------------------------------------------------------

    private void CollectVegaTipCandidates(PlayerSession session, List<(VegaTipSpec, string, string)> outList)
    {
        var p = session.State;
        void Add(string id, string arg = "", string mention = "")
        {
            if (mention.Length > 0 && session.VegaTipMentioned.Contains(mention))
            {
                return;
            }

            outList.Add((VegaTips[id], arg, mention));
        }

        bool inSpace = InSpace(p.PlayerId);
        bool docked = InStation(p.PlayerId);
        bool onFoot = !p.AboardShip && !p.InEva && !inSpace && !docked && p.InSpeeder.Length == 0
                      && !ShipInteriorContains(p.Position);
        bool onSurface = !p.AboardShip && !inSpace && !docked; // on foot OR driving

        // --- Vitals (#1082) ---
        if (p.Oxygen < 25f)
        {
            Add("o2");
        }

        if (p.SuitEnergy < 15f)
        {
            Add("energy");
        }

        if (p.Hunger < 40f)
        {
            Add("hunger");
        }

        if (p.SuitClimateActive)
        {
            Add(session.EffectiveTemperatureC < ComfortLowC ? "cold" : "heat");
        }

        if (p.Health < 40f && p.Health > 0f && HasHealingItem(p))
        {
            Add("medkit");
        }

        // --- Equipment (#1078) ---
        if (p.Hunger < 40f && HasFoodItem(p))
        {
            Add("eat_now");
        }

        if (onFoot)
        {
            RefreshVegaProbe(session);
            var probe = session.VegaProbe;
            bool night = _dayFraction < 0.15 || _dayFraction > 0.85;
            bool underground = probe.SolidAbove >= VegaTipUndergroundSolid;
            bool dark = (night || underground) && !probe.LightNear;
            bool hasLamp = p.Inventory.Has("suit_lamp", 1);
            if (dark && hasLamp && !session.LampOn)
            {
                Add("lamp_off");
            }
            else if (dark && !hasLamp && RecipeKnownFor(p, "suit_lamp"))
            {
                Add("lamp_missing");
            }

            if (underground && p.Inventory.Has("torch", 1) && !probe.TorchNear)
            {
                Add("torch_underground");
            }

            if (session.VegaHandMineStreak >= 8 && CarriesTool(p, ToolKind.Drill))
            {
                Add("wrong_tool");
            }

            if (session.VegaMineRecent > 4.0 && p.Inventory.Has("terrain_scanner", 1)
                && (session.VegaScannerUsedAt <= 0.0 || _uptime - session.VegaScannerUsedAt > 300.0))
            {
                Add("scanner_idle");
            }

            // --- Materials (#1079) ---
            if (probe.ExposedOres.Count > 0)
            {
                var ores = _world.Planet?.Ores;
                foreach (var (key, _) in probe.ExposedOres)
                {
                    var vein = ores?.FirstOrDefault(o => o.Block == key);
                    if (vein is not null && vein.Rarity <= VegaTipRareOreRarity && !session.VegaTipMentioned.Contains("ore:" + key))
                    {
                        Add("rare_ore_near", ItemDisplayName(session, key), "ore:" + key);
                        break;
                    }
                }

                foreach (var (key, _) in probe.ExposedOres)
                {
                    if (session.VegaTipMentioned.Contains("need:" + key))
                    {
                        continue;
                    }

                    if (MissingOreFor(p, key) is { } target)
                    {
                        Add("needed_ore_near", ItemDisplayName(session, key) + VegaArgSeparator + target, "need:" + key);
                        break;
                    }
                }
            }

            if (probe.DataCache is { } cache)
            {
                Add("data_cache_near", "", "cache:" + cache.X + "," + cache.Y + "," + cache.Z);
            }
        }

        if (onSurface && p.InSpeeder.Length == 0)
        {
            foreach (var sp in p.DeployedSpeeders)
            {
                if (sp.HomeBodyId == _world.LocationId
                    && WrapDistSq(p.Position, new Vector3f(sp.X, sp.Y, sp.Z)) > 150.0 * 150.0)
                {
                    Add("speeder_far");
                    break;
                }
            }
        }

        // --- Progression (#1079) — a recipe/blueprint sweep, so only when a tip could actually go out ---
        if (_uptime >= session.VegaTipReadyAt && VegaStageIndex(p) >= 3)
        {
            if (CraftableNow(session) is { } recipe)
            {
                Add("craftable_now", recipe.Name, "craft:" + recipe.Key);
            }

            if (AffordableBlueprint(session) is { } bp)
            {
                Add("blueprint_affordable", bp.Name, "bp:" + bp.Key);
            }
        }

        // --- Places + company (#1080) ---
        if (onSurface)
        {
            CollectPlaceTips(session, Add);
        }

        // --- Space (#1081) ---
        if (session.Ships.TryGetValue(session.ActiveShipId, out var ship))
        {
            if ((p.AboardShip || inSpace) && ship.HasModule("jump_generator") && p.KnownSystems.Count <= 1)
            {
                Add("jump_ready");
            }

            if (inSpace)
            {
                CollectSpaceTips(session, ship, Add);
            }
        }
    }

    private void CollectPlaceTips(PlayerSession session, System.Action<string, string, string> add)
    {
        var p = session.State;
        const double near2 = 120.0 * 120.0;
        var flat = new Vector3f(p.Position.X, 0f, p.Position.Z);

        foreach (var s in _settlements)
        {
            var centre = new Vector3f((s.Min.X + s.Max.X) * 0.5f, 0f, (s.Min.Z + s.Max.Z) * 0.5f);
            if (WrapDistSq(flat, centre) > near2)
            {
                continue;
            }

            bool inside = p.Position.X >= s.Min.X - 8 && p.Position.X <= s.Max.X + 8
                          && p.Position.Z >= s.Min.Z - 8 && p.Position.Z <= s.Max.Z + 8;
            if (inside)
            {
                if (!s.Ruined)
                {
                    ShipAiTipLearned(session, "settlement_near");
                }

                continue;
            }

            add(s.Ruined ? "ruin_near" : "settlement_near", s.Name, (s.Ruined ? "ruin:" : "settlement:") + s.Name);
        }

        for (int i = 0; i < _vaultEntrances.Count; i++)
        {
            var v = _vaultEntrances[i];
            if (WrapDistSq(flat, new Vector3f(v.X, 0f, v.Z)) <= near2)
            {
                add("ruin_near", string.Format(Localize(session.Locale, "poi.ruin"), (char)('A' + i)), "vault:" + i);
            }
        }

        foreach (var f in _factories)
        {
            if (WrapDistSq(flat, new Vector3f(f.TerminalPos.X, 0f, f.TerminalPos.Z)) <= near2)
            {
                add("factory_near", f.Name, "factory:" + f.Id);
            }
        }

        foreach (var cont in _containers)
        {
            if (cont.Id.StartsWith(ChestContainerIdPrefix, System.StringComparison.Ordinal)
                && _meta.RevealedPois.Contains(ChestRevealKey(cont))
                && WrapDistSq(p.Position, cont.Position) <= 80.0 * 80.0)
            {
                add("treasure_near", "", "chest:" + cont.Id);
            }
        }

        if (_landedTraders.TryGetValue(_world.LocationId, out var lt) && WrapDistSq(p.Position, lt.PilotPos) <= 60.0 * 60.0)
        {
            add("trader_near", "", "trader:" + lt.Id);
        }

        if (p.Inventory.Has("creature_translator", 1))
        {
            foreach (var c in _creatures)
            {
                if (c.Kind != CombatEntityKind.Creature || c.Hostile || c.IsCompanion || c.SpeciesId.Length == 0
                    || p.TamedSpecies.Contains(_world.LocationId + ":" + c.SpeciesId)
                    || WrapDistSq(p.Position, c.Position) > 30.0 * 30.0)
                {
                    continue;
                }

                add("tameable_near", "", "tame:" + c.SpeciesId);
                break;
            }
        }

        foreach (var other in JoinedInActiveWorld())
        {
            if (ReferenceEquals(other, session) || other.State.AboardShip || InSpace(other.State.PlayerId)
                || WrapDistSq(p.Position, other.State.Position) > 60.0 * 60.0)
            {
                continue;
            }

            add("player_near", other.State.Name, "player:" + other.State.PlayerId);
            break;
        }
    }

    private void CollectSpaceTips(PlayerSession session, ShipState ship, System.Action<string, string, string> add)
    {
        var p = session.State;
        if (!_playerInstance.TryGetValue(p.PlayerId, out var instanceId) || !_spaceInstances.TryGetValue(instanceId, out var instance))
        {
            return;
        }

        var pos = instance.PlayerPoses.TryGetValue(p.PlayerId, out var pose) ? pose.Pos : instance.ShipPosition;
        bool asteroidNear = false, stationNear = false;
        foreach (var e in instance.Entities)
        {
            if (e.Kind == CombatEntityKind.Asteroid && !asteroidNear && DistSq(pos, e.Position) <= 80.0 * 80.0)
            {
                asteroidNear = true;
            }
            else if (e.Kind == CombatEntityKind.SpaceStation && !stationNear && DistSq(pos, e.Position) <= 150.0 * 150.0)
            {
                stationNear = true;
            }
        }

        if (asteroidNear)
        {
            add(ShipCanMineAsteroids(ship) ? "asteroid_near" : "asteroid_no_tool", "", "asteroids:" + instanceId);
        }

        if (stationNear && !p.Milestones.Contains(VegaStageKey("dock")))
        {
            add("station_near", "", "station:" + instanceId);
        }

        float max = ShipHullMaxFor(ship);
        if (max > 0f && ship.Hull > 0f && ship.Hull / max < 0.4f)
        {
            add("hull_low", "", "");
        }
    }

    private static double DistSq(Vector3f a, Vector3f b)
    {
        double dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
        return dx * dx + dy * dy + dz * dz;
    }

    /// <summary>Any built module that can break asteroids (a mining tool or a dual laser — weapon_class 0 / 2).</summary>
    private bool ShipCanMineAsteroids(ShipState ship)
    {
        foreach (var key in ship.Modules)
        {
            if (_content.GetShipModule(key) is { } def && def.Stats.ContainsKey("weapon_damage"))
            {
                int cls = (int)def.Stats.GetValueOrDefault("weapon_class", 1);
                if (cls == 0 || cls == 2)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Hull maximum of a ship from its design + modules — the same sum <see cref="RecomputeShipCombatStats"/>
    /// applies to the cursor ship, usable for any session's ship without touching the cursor.</summary>
    private float ShipHullMaxFor(ShipState ship)
    {
        var design = _content.GetShip(ship.ShipType);
        float hull = ship.IsCustom ? CustomShipStatsFor(ship).HullMax : design?.BaseHull ?? BaseHull;
        foreach (var key in ship.Modules)
        {
            if (_content.GetShipModule(key) is { } m)
            {
                hull += (float)m.Stats.GetValueOrDefault("hull", 0);
            }
        }

        return hull;
    }

    // ---------------------------------------------------------------------------------------------
    // Inventory / content helpers.
    // ---------------------------------------------------------------------------------------------

    /// <summary>Separator between several <c>{n}</c> arguments packed into one <c>ShipAiLine.LineArg</c>.</summary>
    private const string VegaArgSeparator = "\u001f";

    private bool HasFoodItem(PlayerState p)
    {
        foreach (var slot in p.Inventory.Slots)
        {
            if (slot is not null && _content.GetItem(slot.Item)?.ConsumeHunger > 0f)
            {
                return true;
            }
        }

        return false;
    }

    private bool HasHealingItem(PlayerState p)
    {
        if (p.Inventory.Has("field_medkit", 1))
        {
            return true;
        }

        foreach (var slot in p.Inventory.Slots)
        {
            if (slot is not null && _content.GetItem(slot.Item)?.ConsumeHealth > 0f)
            {
                return true;
            }
        }

        return false;
    }

    private bool CarriesTool(PlayerState p, ToolKind kind)
    {
        foreach (var slot in p.Inventory.Slots)
        {
            if (slot is not null && _content.GetItem(slot.Item)?.Tool?.Kind == kind)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>A recipe producing <paramref name="itemKey"/> whose blueprint (if any) the player has.</summary>
    private bool RecipeKnownFor(PlayerState p, string itemKey)
    {
        foreach (var r in _content.Recipes.Values)
        {
            if (r.Outputs.Any(o => o.Item == itemKey)
                && (string.IsNullOrEmpty(r.RequiredBlueprint) || p.UnlockedBlueprints.Contains(r.RequiredBlueprint)))
            {
                return true;
            }
        }

        return false;
    }

    private string ItemDisplayName(PlayerSession session, string key)
        => LocalizedName(session.Locale, _content.GetItem(key)?.NameKey ?? _content.GetBlock(key)?.NameKey, key);

    /// <summary>The name of something the player could make or unlock if only they had more of this ore:
    /// a known recipe using it directly (player short of the amount) or an unlockable blueprint whose
    /// unlock cost lists it. Null when the ore is not on any shopping list.</summary>
    private string? MissingOreFor(PlayerState p, string oreKey)
    {
        int have = p.Inventory.CountOf(oreKey);
        foreach (var r in _content.Recipes.Values)
        {
            if (!string.IsNullOrEmpty(r.RequiredBlueprint) && !p.UnlockedBlueprints.Contains(r.RequiredBlueprint))
            {
                continue;
            }

            foreach (var input in r.Inputs)
            {
                if (input.Item == oreKey && have < input.Count && r.Outputs.Count > 0)
                {
                    return ItemDisplayNameFor(p, r.Outputs[0].Item);
                }
            }
        }

        foreach (var bp in _content.Blueprints.Values)
        {
            if (p.UnlockedBlueprints.Contains(bp.Key) || bp.Prerequisites.Any(pr => !p.UnlockedBlueprints.Contains(pr)))
            {
                continue;
            }

            foreach (var cost in bp.UnlockCost)
            {
                if (cost.Item == oreKey && have < cost.Count)
                {
                    return LocalizedName(SessionLocaleOf(p), bp.NameKey, bp.Key);
                }
            }
        }

        return null;
    }

    private string SessionLocaleOf(PlayerState p) => FindSessionByPlayerId(p.PlayerId)?.Locale ?? "en";

    private string ItemDisplayNameFor(PlayerState p, string key)
        => LocalizedName(SessionLocaleOf(p), _content.GetItem(key)?.NameKey ?? _content.GetBlock(key)?.NameKey, key);

    /// <summary>A recipe the player can craft right now (blueprint known, every input in the inventory) whose
    /// output they do not own yet and that VEGA has not mentioned this session.</summary>
    private (string Key, string Name)? CraftableNow(PlayerSession session)
    {
        var p = session.State;
        foreach (var r in _content.Recipes.Values)
        {
            if (r.Inputs.Count == 0 || r.Outputs.Count == 0 || session.VegaTipMentioned.Contains("craft:" + r.Key))
            {
                continue;
            }

            if (!string.IsNullOrEmpty(r.RequiredBlueprint) && !p.UnlockedBlueprints.Contains(r.RequiredBlueprint))
            {
                continue;
            }

            if (p.Inventory.CountOf(r.Outputs[0].Item) > 0)
            {
                continue; // they made (or found) one already — no need to point at the recipe
            }

            bool all = true;
            foreach (var input in r.Inputs)
            {
                if (p.Inventory.CountOf(input.Item) < input.Count)
                {
                    all = false;
                    break;
                }
            }

            if (all)
            {
                return (r.Key, ItemDisplayName(session, r.Outputs[0].Item));
            }
        }

        return null;
    }

    /// <summary>A locked blueprint whose prerequisites are met and whose knowledge + item cost the player
    /// can pay right now, not yet mentioned this session.</summary>
    private (string Key, string Name)? AffordableBlueprint(PlayerSession session)
    {
        var p = session.State;
        foreach (var bp in _content.Blueprints.Values)
        {
            if (p.UnlockedBlueprints.Contains(bp.Key) || session.VegaTipMentioned.Contains("bp:" + bp.Key)
                || bp.KnowledgeCost > p.KnowledgePoints
                || bp.Prerequisites.Any(pr => !p.UnlockedBlueprints.Contains(pr)))
            {
                continue;
            }

            bool all = true;
            foreach (var cost in bp.UnlockCost)
            {
                if (p.Inventory.CountOf(cost.Item) < cost.Count)
                {
                    all = false;
                    break;
                }
            }

            if (all)
            {
                return (bp.Key, LocalizedName(session.Locale, bp.NameKey, bp.Key));
            }
        }

        return null;
    }

    // ---------------------------------------------------------------------------------------------
    // The block probe.
    // ---------------------------------------------------------------------------------------------

    private void RefreshVegaProbe(PlayerSession session)
    {
        if (_uptime < session.VegaTipProbeAt)
        {
            return;
        }

        session.VegaTipProbeAt = _uptime + VegaTipProbeInterval;
        var probe = session.VegaProbe;
        probe.ExposedOres.Clear();
        probe.DataCache = null;
        probe.LightNear = false;
        probe.TorchNear = false;

        var p = session.State.Position;
        var centre = WorldConstants.CanonicalBlock(new Vector3i(
            (int)System.Math.Floor(p.X), (int)System.Math.Floor(p.Y + 1f), (int)System.Math.Floor(p.Z)), _world.Circumference);

        // Column above the head — a real cave roof is many blocks thick, a hut roof or canopy is not.
        int solid = 0;
        for (int y = 2; y <= VegaTipColumnScan; y++)
        {
            if (!_world.GetBlockIfLoaded(new Vector3i(centre.X, centre.Y + y, centre.Z)).IsAir)
            {
                solid++;
            }
        }

        probe.SolidAbove = solid;

        int r = VegaTipProbeRadius;
        int nearestCache = int.MaxValue;
        for (int dx = -r; dx <= r; dx++)
            for (int dy = -r; dy <= r; dy++)
                for (int dz = -r; dz <= r; dz++)
                {
                    var cell = WorldConstants.CanonicalBlock(new Vector3i(centre.X + dx, centre.Y + dy, centre.Z + dz), _world.Circumference);
                    var b = _world.GetBlockIfLoaded(cell);
                    if (b.IsAir)
                    {
                        continue;
                    }

                    string? key = _world.Definition(b)?.Key;
                    if (key is null)
                    {
                        continue;
                    }

                    int distSq = dx * dx + dy * dy + dz * dz;
                    switch (key)
                    {
                        case "torch":
                        case "lantern":
                            probe.TorchNear = true;
                            if (distSq <= 36) { probe.LightNear = true; }
                            break;
                        case "campfire":
                        case "fire":
                        case "lava":
                        case "glowstone":
                            if (distSq <= 36) { probe.LightNear = true; }
                            break;
                        case "data_cache":
                        case "crystal":
                            if (distSq < nearestCache)
                            {
                                nearestCache = distSq;
                                probe.DataCache = cell;
                            }
                            break;
                        default:
                            if (key.EndsWith("_ore", System.StringComparison.Ordinal) && IsExposed(cell))
                            {
                                probe.ExposedOres.Add((key, distSq));
                            }
                            break;
                    }
                }

        probe.ExposedOres.Sort((a, b2) => a.DistSq.CompareTo(b2.DistSq));
    }

    /// <summary>An ore the player can actually see: at least one of its six neighbours is air.</summary>
    private bool IsExposed(Vector3i cell)
        => _world.GetBlockIfLoaded(new Vector3i(cell.X + 1, cell.Y, cell.Z)).IsAir
           || _world.GetBlockIfLoaded(new Vector3i(cell.X - 1, cell.Y, cell.Z)).IsAir
           || _world.GetBlockIfLoaded(new Vector3i(cell.X, cell.Y + 1, cell.Z)).IsAir
           || _world.GetBlockIfLoaded(new Vector3i(cell.X, cell.Y - 1, cell.Z)).IsAir
           || _world.GetBlockIfLoaded(new Vector3i(cell.X, cell.Y, cell.Z + 1)).IsAir
           || _world.GetBlockIfLoaded(new Vector3i(cell.X, cell.Y, cell.Z - 1)).IsAir;

    /// <summary>Test seam: forces the next advisor poll to re-probe the blocks around a player.</summary>
    public void ResetVegaProbeForTest(string playerId)
    {
        if (FindSessionByPlayerId(playerId) is { } s)
        {
            s.VegaTipProbeAt = 0.0;
        }
    }
}
