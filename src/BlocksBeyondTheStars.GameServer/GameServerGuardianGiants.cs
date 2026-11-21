// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The guardian giants (#3676, #3681): fourteen-block machines lying fallen in the wilderness as ordinary block bodies — the
/// structure stamp lays them and writes their <see cref="GuardianGiantRecord"/>s (<see cref="WorldMetadata.GuardianGiants"/>);
/// this runtime keeps their state. See docs/developer/GUARDIAN_GIANTS.md for the whole picture.
/// <list type="bullet">
/// <item><b>The tick</b> (<see cref="TickGuardianGiants"/>, per loaded world): a scan of the body's records once a second —
/// four times a second while a player on foot is within the stir range of a giant that can wake — and the fight at the full
/// tick rate while one stands. At most one giant per body is awake at a time.</item>
/// <item><b>Found.</b> Any player within <see cref="GuardianGiantRules.FoundRange"/> of a giant (whatever its state) marks it
/// found: its planet-map marker (<see cref="GuardianGiantRules.PoiType"/>) shows from then on, and VEGA names the first giant
/// each player finds.</item>
/// <item><b>Stir and wake</b> — only where planet machines are active (<see cref="PlanetEnemiesActive"/>: Survival, the
/// machines rule not Off; the Remnant era does NOT stop them — the giants are cut off from the Guardian core). A player on foot
/// within the stir range of a dormant giant that can wake makes one visor light flicker (real block swaps) and VEGA warns once
/// per approach; within the wake range — or breaking one of its blocks — every light switches on and it rises a second later.
/// A lit giant (it lay down earlier) rises at once.</item>
/// <item><b>The fight</b> (GameServerGuardianGiantsFight.cs, …Attacks.cs), <b>the end</b> (…Defeat.cs: the corpse, a loot crate
/// per fighter), <b>the body blocks</b> (…Body.cs) and <b>the admin tools</b> (…Admin.cs: <c>/giant guardian</c>,
/// <c>/tp giant</c>).</item>
/// </list>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>How far past the stir range the scan keeps its quick beat (and a warned player must go to be warned again).</summary>
    private const float GuardianGiantStirMargin = 8f;

    /// <summary>The scan's beat: once a second, four times while someone is near a giant that can wake (the flicker).</summary>
    private const double GuardianGiantScanSeconds = 1.0;

    /// <summary>See <see cref="GuardianGiantScanSeconds"/>.</summary>
    private const double GuardianGiantNearScanSeconds = 0.25;

    /// <summary>A dormant giant's lights are on this long before it rises.</summary>
    private const double GuardianGiantWakeDelay = 1.0;

    /// <summary>The share of flicker slots (a quarter second each) in which the stirring eye is lit.</summary>
    private const double GuardianGiantFlickerShare = 0.35;

    /// <summary>The once-per-player flag of VEGA's line about the first giant a player finds.</summary>
    internal const string GuardianGiantFoundMilestone = "guardian_giant:found";

    /// <summary>One loaded world's giant runtime (never persisted — a reload starts it over; the records keep the rest).</summary>
    private sealed class GuardianGiantWorld
    {
        public LoadedWorld? World;
        public double Clock;
        public double Interval = GuardianGiantScanSeconds;
        public GuardianGiantFight? Fight;

        /// <summary>"site|player" — warned of a stirring giant during the current approach.</summary>
        public readonly HashSet<string> Warned = new(System.StringComparer.Ordinal);

        /// <summary>The sites whose stirring eye is lit right now.</summary>
        public readonly HashSet<string> Flickering = new(System.StringComparer.Ordinal);
    }

    private readonly Dictionary<string, GuardianGiantWorld> _guardianGiantWorlds = new(System.StringComparer.Ordinal);
    private readonly List<GuardianGiantRecord> _guardianGiantsHere = new(); // reused per scan
    private readonly List<PlayerSession> _guardianGiantPresent = new();      // reused per scan / fight tick
    private readonly List<PlayerSession> _guardianGiantOnFoot = new();

    /// <summary>Whether a giant may stir and wake here: the planet machines' gate, without the Remnant era's limits.</summary>
    private bool GuardianGiantsMayWake => PlanetEnemiesActive;

    /// <summary>How many awake guardian giants the active world's enemy list holds — none of them takes a machine's slot.</summary>
    private int GuardianGiantsInEnemyList()
    {
        int n = 0;
        foreach (var e in _planetEnemies)
        {
            n += e.Kind == CombatEntityKind.GuardianGiant ? 1 : 0;
        }

        return n;
    }

    /// <summary>Per-world tick step: the fight at full rate while a giant stands; the scan (found, stir, wake, restore) on its
    /// own beat. A body without a giant costs one dictionary walk a second.</summary>
    private void TickGuardianGiants(double dt)
    {
        if (_world.Planet.Void)
        {
            return;
        }

        var gw = GuardianGiantWorldFor(_world.LocationId);
        if (gw.Fight is { } fight)
        {
            TickGuardianGiantFight(gw, fight, dt);
        }

        gw.Clock += dt;
        if (gw.Clock < gw.Interval)
        {
            return;
        }

        gw.Clock = 0;
        ScanGuardianGiants(gw);
    }

    /// <summary>The giant runtime of a body on the active world — started over when the world was reloaded.</summary>
    private GuardianGiantWorld GuardianGiantWorldFor(string locationId)
    {
        if (!_guardianGiantWorlds.TryGetValue(locationId, out var gw) || !ReferenceEquals(gw.World, _worlds.Active))
        {
            gw = new GuardianGiantWorld { World = _worlds.Active, Clock = GuardianGiantScanSeconds }; // the first tick scans
            _guardianGiantWorlds[locationId] = gw;
        }

        return gw;
    }

    /// <summary>The records of the active body (into a reused list).</summary>
    private List<GuardianGiantRecord> GuardianGiantsHere()
    {
        _guardianGiantsHere.Clear();
        foreach (var rec in _meta.GuardianGiants.Values)
        {
            if (rec.LocationId == _world.LocationId)
            {
                _guardianGiantsHere.Add(rec);
            }
        }

        return _guardianGiantsHere;
    }

    /// <summary>Fills the reused lists: every player on this surface (not in space, not observing, alive), and those of them on
    /// foot.</summary>
    private void CollectGuardianGiantPlayers()
    {
        _guardianGiantPresent.Clear();
        _guardianGiantOnFoot.Clear();
        foreach (var s in JoinedInActiveWorld())
        {
            if (s.Watching || s.OtherSide || InSpace(s.State.PlayerId) || s.State.Health <= 0f) // #3905: no giant stirs for a player over there
            {
                continue;
            }

            _guardianGiantPresent.Add(s);
            if (!s.State.AboardShip)
            {
                _guardianGiantOnFoot.Add(s);
            }
        }
    }

    /// <summary>One scan of the body's giants: an awake record without a fight is restored (the server stopped mid-fight),
    /// then found, stir and wake per giant. Quickens the beat while someone is near a giant that can wake.</summary>
    private void ScanGuardianGiants(GuardianGiantWorld gw)
    {
        var here = GuardianGiantsHere();
        gw.Interval = GuardianGiantScanSeconds;
        if (here.Count == 0)
        {
            return;
        }

        CollectGuardianGiantPlayers();
        bool near = false;
        foreach (var rec in here.ToArray())
        {
            if (rec.State == GuardianGiantRecord.Awake && !ReferenceEquals(gw.Fight?.Rec, rec))
            {
                RestoreGuardianGiant(rec);
            }

            MarkGuardianGiantFound(rec, gw);
            near |= StirOrWakeGuardianGiant(gw, rec);
        }

        gw.Interval = near ? GuardianGiantNearScanSeconds : GuardianGiantScanSeconds;
    }

    /// <summary>Where a giant is, flat, for the ranges: the awake one's position, the corpse's centre, else the lying body's.</summary>
    private Vector3f GuardianGiantCentre(GuardianGiantRecord rec, GuardianGiantWorld? gw = null)
    {
        if (gw?.Fight is { Entity: { } e } f && ReferenceEquals(f.Rec, rec))
        {
            return e.Position;
        }

        bool dead = rec.State == GuardianGiantRecord.Defeated;
        int facing = dead ? rec.CorpseFacing : rec.Facing;
        var (cx, cz) = GuardianGiantBody.LyingCentre(facing);
        return dead
            ? new Vector3f(rec.CorpseX + cx, rec.CorpseY, rec.CorpseZ + cz)
            : new Vector3f(rec.X + cx, rec.Y, rec.Z + cz);
    }

    /// <summary>A player within the found range marks the giant found (its map marker shows) and hears VEGA about the first
    /// giant they ever find.</summary>
    private void MarkGuardianGiantFound(GuardianGiantRecord rec, GuardianGiantWorld gw)
    {
        var centre = GuardianGiantCentre(rec, gw);
        const float r = GuardianGiantRules.FoundRange;
        foreach (var s in _guardianGiantPresent)
        {
            if (FlatDistSq(s.State.Position, centre) > r * r)
            {
                continue;
            }

            if (!rec.Found)
            {
                rec.Found = true;
                _repo.SaveMetadata(_meta);
                BroadcastPlanetPois();
            }

            if (s.State.Milestones.Add(GuardianGiantFoundMilestone))
            {
                SendGuardianGiantVega(s, GuardianGiantVegaMoment.Found);
                _repo.SavePlayer(s.State);
            }
        }
    }

    /// <summary>The stir and the wake of a giant that can wake. True while a player on foot is within the stir range plus its
    /// margin (the scan keeps its quick beat).</summary>
    private bool StirOrWakeGuardianGiant(GuardianGiantWorld gw, GuardianGiantRecord rec)
    {
        bool sleeping = rec.State is GuardianGiantRecord.Dormant or GuardianGiantRecord.Lit;
        if (!rec.CanWake || !sleeping || !GuardianGiantsMayWake || gw.Fight is not null)
        {
            EndGuardianGiantFlicker(gw, rec);
            return false;
        }

        var centre = GuardianGiantCentre(rec);
        PlayerSession? nearest = null;
        double best = double.MaxValue;
        foreach (var s in _guardianGiantOnFoot)
        {
            double d = FlatDistSq(s.State.Position, centre);
            if (d < best)
            {
                best = d;
                nearest = s;
            }
        }

        float outer = GuardianGiantRules.StirRange + GuardianGiantStirMargin;
        ForgetGuardianGiantWarnings(gw, rec, centre, outer);
        if (nearest is null || best > outer * outer)
        {
            EndGuardianGiantFlicker(gw, rec);
            return false;
        }

        if (best <= GuardianGiantRules.WakeRange * GuardianGiantRules.WakeRange)
        {
            WakeGuardianGiant(gw, rec);
            return true;
        }

        if (rec.State == GuardianGiantRecord.Dormant && best <= GuardianGiantRules.StirRange * GuardianGiantRules.StirRange)
        {
            FlickerGuardianGiant(gw, rec);
            WarnOfStirringGuardianGiant(gw, rec, centre);
        }
        else
        {
            EndGuardianGiantFlicker(gw, rec);
        }

        return true;
    }

    /// <summary>VEGA's warning to every player on foot within the stir range — once per approach.</summary>
    private void WarnOfStirringGuardianGiant(GuardianGiantWorld gw, GuardianGiantRecord rec, Vector3f centre)
    {
        const float r = GuardianGiantRules.StirRange;
        foreach (var s in _guardianGiantOnFoot)
        {
            if (FlatDistSq(s.State.Position, centre) <= r * r && gw.Warned.Add(rec.SiteId + "|" + s.State.PlayerId))
            {
                SendGuardianGiantVega(s, GuardianGiantVegaMoment.Stir);
            }
        }
    }

    /// <summary>A player who walked out past the stir range and its margin is warned again on the next approach.</summary>
    private void ForgetGuardianGiantWarnings(GuardianGiantWorld gw, GuardianGiantRecord rec, Vector3f centre, float outer)
    {
        if (gw.Warned.Count == 0)
        {
            return;
        }

        foreach (var s in _guardianGiantOnFoot)
        {
            if (FlatDistSq(s.State.Position, centre) > outer * outer)
            {
                gw.Warned.Remove(rec.SiteId + "|" + s.State.PlayerId);
            }
        }
    }

    /// <summary>The stirring eye: one visor light of the dormant body, lit in about a third of the quarter-second slots
    /// (stable per site and slot), swapped only when its state changes — a few block changes a second at most.</summary>
    private void FlickerGuardianGiant(GuardianGiantWorld gw, GuardianGiantRecord rec)
    {
        long slot = (long)System.Math.Floor(_uptime / GuardianGiantNearScanSeconds);
        bool on = StableHashing.Unit(rec.SiteId + ":flicker:" + slot, rec.Seed) < GuardianGiantFlickerShare;
        if (on == gw.Flickering.Contains(rec.SiteId))
        {
            return;
        }

        if (on)
        {
            gw.Flickering.Add(rec.SiteId);
        }
        else
        {
            gw.Flickering.Remove(rec.SiteId);
        }

        SetGuardianGiantStirLight(rec, on);
    }

    /// <summary>Puts the stirring eye out again (nobody near any more, or the giant woke).</summary>
    private void EndGuardianGiantFlicker(GuardianGiantWorld gw, GuardianGiantRecord rec)
    {
        if (gw.Flickering.Remove(rec.SiteId) && rec.State == GuardianGiantRecord.Dormant)
        {
            SetGuardianGiantStirLight(rec, false);
        }
    }

    /// <summary>The giant wakes: a dormant one switches every light on and rises <see cref="GuardianGiantWakeDelay"/> later,
    /// a lit one rises at once. VEGA tells everyone near. Nothing when another giant of this body is already awake.</summary>
    private void WakeGuardianGiant(GuardianGiantWorld gw, GuardianGiantRecord rec)
    {
        if (gw.Fight is not null)
        {
            return;
        }

        gw.Flickering.Remove(rec.SiteId);
        bool dormant = rec.State == GuardianGiantRecord.Dormant;
        if (dormant)
        {
            SetGuardianGiantLights(rec.X, rec.Y, rec.Z, rec.Facing, rec.Seed, lit: true);
        }

        gw.Fight = new GuardianGiantFight(rec) { RiseAt = SnapshotTime + (dormant ? GuardianGiantWakeDelay : 0.0) };
        foreach (var s in GuardianGiantAudience(GuardianGiantCentre(rec)))
        {
            SendGuardianGiantVega(s, GuardianGiantVegaMoment.Rise);
        }

        _log.Info($"The guardian giant '{rec.SiteId}' woke at {rec.X},{rec.Y},{rec.Z}.");
    }

    /// <summary>Every player of this surface within <see cref="GuardianGiantRules.StatusRange"/> of <paramref name="at"/>
    /// (aboard a ship too — it is a sight from the window).</summary>
    private List<PlayerSession> GuardianGiantAudience(Vector3f at)
    {
        var list = new List<PlayerSession>();
        const float r = GuardianGiantRules.StatusRange;
        foreach (var s in JoinedInActiveWorld())
        {
            if (!InSpace(s.State.PlayerId) && FlatDistSq(s.State.Position, at) <= r * r)
            {
                list.Add(s);
            }
        }

        return list;
    }

    /// <summary>Whether anyone of this surface is within <see cref="GuardianGiantRules.StatusRange"/> of <paramref name="at"/>
    /// (the allocation-free form of <see cref="GuardianGiantAudience"/>).</summary>
    private bool AnyoneSeesGuardianGiant(Vector3f at)
    {
        const float r = GuardianGiantRules.StatusRange;
        foreach (var s in JoinedInActiveWorld())
        {
            if (!InSpace(s.State.PlayerId) && FlatDistSq(s.State.Position, at) <= r * r)
            {
                return true;
            }
        }

        return false;
    }

    // ---------------------------------------------------------------- VEGA

    /// <summary>The ONE way a giant line reaches a player: the key from <see cref="GuardianGiantVegaKey"/>, a story line
    /// (kind 2 — never muted with the hints).</summary>
    private void SendGuardianGiantVega(PlayerSession s, GuardianGiantVegaMoment moment)
    {
        string key = GuardianGiantVegaKey(s, moment);
        if (key.Length > 0)
        {
            SendVegaLine(s, key, 2);
        }
    }

    /// <summary>The seam that picks a giant line's locale key: the VEGA story's variant by what this player's story knows
    /// (#3685, <see cref="StoryGuardianGiantVegaKey"/> — puzzled, the Service's builders, the Guardian's hand, the silent
    /// core), falling back to the neutral line (it never names the Guardian) without the VEGA story or for a moment the story
    /// has no variant for.</summary>
    private string GuardianGiantVegaKey(PlayerSession s, GuardianGiantVegaMoment moment)
        => StoryGuardianGiantVegaKey(s, moment, NeutralGuardianGiantVegaKey(moment));

    /// <summary>The neutral giant lines: true in every world, with or without the VEGA story.</summary>
    private static string NeutralGuardianGiantVegaKey(GuardianGiantVegaMoment moment) => moment switch
    {
        GuardianGiantVegaMoment.Found => "vega.giant.found",
        GuardianGiantVegaMoment.Stir => "vega.giant.stir",
        GuardianGiantVegaMoment.Rise => "vega.giant.rise",
        GuardianGiantVegaMoment.Rage => "vega.giant.rage",
        GuardianGiantVegaMoment.Victory => "vega.giant.victory",
        GuardianGiantVegaMoment.LieDown => "vega.giant.lie_down",
        _ => string.Empty,
    };

    // ---------------------------------------------------------------- the map

    /// <summary>The found giants of the active body on the planet map, with their state (lying, awake, defeated) — derived from
    /// the records on every build.</summary>
    private void AppendGuardianGiantPois(PlayerSession session, List<NetPoi> pois)
    {
        if (_world is null || _world.Planet.Void)
        {
            return;
        }

        _guardianGiantWorlds.TryGetValue(_world.LocationId, out var gw);
        foreach (var rec in GuardianGiantsHere().ToArray())
        {
            if (!rec.Found)
            {
                continue;
            }

            var at = GuardianGiantCentre(rec, gw);
            pois.Add(new NetPoi
            {
                Type = GuardianGiantRules.PoiType,
                Name = Localize(session.Locale, "poi.guardian_giant"),
                X = at.X,
                Z = at.Z,
                State = GuardianGiantPoiState(rec),
            });
        }
    }

    /// <summary>The map marker's state of a giant.</summary>
    private static string GuardianGiantPoiState(GuardianGiantRecord rec) => rec.State switch
    {
        GuardianGiantRecord.Awake => GuardianGiantRules.PoiAwake,
        GuardianGiantRecord.Defeated => GuardianGiantRules.PoiDefeated,
        _ => GuardianGiantRules.PoiLying,
    };
}
