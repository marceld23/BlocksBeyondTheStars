// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Right-click gadgets (item 36): the <b>field medkit</b> (heal yourself + nearby allies), the <b>stasis
/// projector</b> (briefly freeze creatures so they can be scanned safely) and the <b>terrain blaster</b>
/// (clear a sphere of terrain — no loot). All are reusable tools gated behind a blueprint, costing suit
/// energy with a short cooldown. The effect is keyed by the item id so one intent drives all three.
/// </summary>
public sealed partial class GameServer
{
    // Uses the existing monotonic _uptime clock (GameServerBump.SampleHistories increments it once per tick).
    private readonly Dictionary<string, double> _gadgetReadyAt = new(); // "playerId|gadget" -> uptime usable again

    // --- balance: field medkit ---
    private const float MedkitHealAmount = 45f; // HP restored to the user + each nearby ally
    private const float MedkitRadius = 6f;       // ally heal radius (blocks)
    private const double MedkitCooldown = 4.0;   // seconds between uses

    // --- balance: stasis projector ---
    private const float StasisRadius = 7f;       // creatures within this of the aim point are frozen
    private const double StasisDuration = 6.0;   // seconds a creature stays in stasis (scan window)
    private const double StasisCooldown = 6.0;

    // --- balance: terrain blaster ---
    private const int BlasterRadius = 3;         // sphere radius (blocks) — a sizeable crater (~120 blocks)
    private const double BlasterCooldown = 3.0;

    // --- balance: fluid pump (#2106) ---
    private const double PumpCooldown = 0.4;     // one cell per pull; a dozen oil covers every lubricant need

    // --- balance: creature translator (taming) ---
    private const double TranslatorCooldown = 1.5; // seconds between decodes (the ritual responses are free)

    // --- balance: terrain scanner (Feature 40) ---
    private const int ScannerRadius = 20;        // pulse radius (blocks) around the player
    private const int ScannerMaxHits = 80;       // nearest hits sent (bounds the message on ore-rich worlds)
    private const float ScannerSeconds = 8f;     // how long the client shows the glow markers
    private const double ScannerCooldown = 10.0;

    // --- balance: the scanner's oil echo (#2372) ---
    internal const int OilEchoRange = 800;       // the nearest pocket within this many blocks (horizontal) answers
    private const int OilEchoCandidates = 3;     // pockets checked for oil left in them before the echo stays silent

    // --- balance: weather scanner (#900) ---
    private const int WeatherForecastEpisodes = 3;  // how far ahead a reading looks
    private const double WeatherScannerCooldown = 15.0;

    // --- balance: energy rope gun (#2317/#2319) ---
    internal const string RopeGunItem = "energy_rope_gun";
    private const double RopeCooldown = 0.6;      // a second shot replaces the first; no spraying
    private const float RopeRangeSlack = 1f;      // the 10 Hz move stream trails the true position (like WithinReach)
    private const float RopeDefaultRange = 24f;   // when the item data carries no range

    private void HandleUseGadget(PlayerSession session, UseGadgetIntent intent)
    {
        var p = session.State;
        var item = _content.GetItem(intent.GadgetKey);
        if (item?.Tool is null || item.Tool.Kind != ToolKind.Gadget)
        {
            Reject(session, "gadget", "@srv.gadget.not_usable");
            return;
        }

        if (!p.Inventory.Has(intent.GadgetKey, 1))
        {
            Reject(session, "gadget", "@srv.gadget.missing");
            return;
        }

        string cdKey = p.PlayerId + "|" + intent.GadgetKey;
        if (_gadgetReadyAt.TryGetValue(cdKey, out var readyAt) && _uptime < readyAt)
        {
            return; // still cooling down (the client also rate-limits) — ignore quietly
        }

        if (p.SuitEnergy < item.Tool.EnergyPerUse)
        {
            Reject(session, "gadget", "@no_energy");
            return;
        }

        var target = new Vector3f(intent.X, intent.Y, intent.Z);

        // #2376: a gadget that acts at the aim point acts only within the player's reach. The client aims the 8 m block
        // ray from the camera (or, aimed at nothing, a point 5 m ahead), so a target farther off can only come from a
        // modified client — which used to pump oil out of a far pocket or blast another player's ground. Same bound and
        // slack as mining (WithinReach). The rope gun and the remote control check their own ranges.
        if (ActsAtTarget(intent.GadgetKey)
            && !WithinReach(p, new Vector3i((int)System.Math.Floor(target.X), (int)System.Math.Floor(target.Y), (int)System.Math.Floor(target.Z))))
        {
            Reject(session, "gadget", "@out_of_reach");
            return;
        }

        double cooldown;
        bool happened = true; // false = the use is spent, but nothing came of it to show (a refused vehicle deploy)
        switch (intent.GadgetKey)
        {
            case "field_medkit":
                UseFieldMedkit(session);
                cooldown = MedkitCooldown;
                break;
            case "stasis_projector":
                UseStasisProjector(target);
                cooldown = StasisCooldown;
                break;
            case "terrain_blaster":
                UseTerrainBlaster(session, target);
                cooldown = BlasterCooldown;
                break;
            case "fluid_pump":
                if (!UseFluidPump(session, target))
                {
                    return; // a miss (no liquid, or a protected cell) costs neither energy nor cooldown
                }

                cooldown = PumpCooldown;
                break;
            case "terrain_scanner":
                UseTerrainScanner(session);
                cooldown = ScannerCooldown;
                break;
            case "creature_translator":
                UseCreatureTranslator(session, target);
                cooldown = TranslatorCooldown;
                break;
            case BlocksBeyondTheStars.Shared.Bio.BioItems.Sampler: // #2201: a sample from a living animal, without harm
                if (!UseBioSampler(session, target))
                {
                    return; // nothing in reach, a hostile one, too soon: costs neither energy nor cooldown
                }

                cooldown = TranslatorCooldown;
                break;
            case "weather_scanner":
                SendWeatherForecast(session);
                cooldown = WeatherScannerCooldown;
                break;
            case "speeder":
            case "boat":
                happened = DeployVehicle(session, item.Key); // unfolds a hover speeder / launches a boat ahead (consumes the item, #1215)
                cooldown = SpeederDeployCooldown;
                break;
            case RailRules.LinkerItemKey: // #2113: the linker couples two pylons (first pick, second pick)
                if (!UseRailLinker(session, target))
                {
                    return;
                }

                cooldown = 0.2;
                break;
            case RailRules.CabItemKey: // #2113: the cab appears on the line under the aim
                if (!UseRailCab(session, target))
                {
                    return;
                }

                cooldown = SpeederDeployCooldown;
                break;
            case RemoteControlItem: // #2263: aimed at a signal receiver it pairs, anywhere else it flips the paired receiver
                if (!UseRemoteControl(session, target))
                {
                    return;
                }

                cooldown = 0.3;
                break;
            case "wagon_seats":
            case "wagon_sleeper":
            case "wagon_bar": // #2113: a wagon couples behind the last one
                if (!UseRailWagon(session, item.Key, target))
                {
                    return;
                }

                cooldown = SpeederDeployCooldown;
                break;
            case RopeGunItem: // #2319: the rope sticks where the client says — once the server agrees it can
                if (!UseRopeGun(session, target))
                {
                    return; // too far, nothing to hold, no sight: costs neither energy nor cooldown
                }

                cooldown = RopeCooldown;
                break;
            default:
                Reject(session, "gadget", "@srv.gadget.unknown");
                return;
        }

        p.SuitEnergy = System.Math.Max(0f, p.SuitEnergy - item.Tool.EnergyPerUse);
        _gadgetReadyAt[cdKey] = _uptime + cooldown;
        SendPlayerState(session);
        if (happened)
        {
            BroadcastGadgetOutcome(session, intent.GadgetKey, target); // #2158: the user's client plays the effect on this
        }
    }

    /// <summary>#2376: the gadgets whose effect lands at the aim point — and so must stay within reach. The self-centred
    /// ones (medkit, the scanners, the vehicle deploys) ignore the target; the rope gun and the remote control check
    /// their own ranges.</summary>
    private static bool ActsAtTarget(string gadgetKey) => gadgetKey switch
    {
        "stasis_projector" or "terrain_blaster" or "fluid_pump" or "creature_translator" => true,
        BlocksBeyondTheStars.Shared.Bio.BioItems.Sampler => true,
        RailRules.LinkerItemKey or RailRules.CabItemKey => true,
        "wagon_seats" or "wagon_sleeper" or "wagon_bar" => true,
        _ => false,
    };

    /// <summary>Heals the user and every other on-foot player within <see cref="MedkitRadius"/> in the same
    /// world (a shared first-aid pulse) — item 36.</summary>
    private void UseFieldMedkit(PlayerSession user)
    {
        var origin = user.State.Position;
        foreach (var s in JoinedInActiveWorld())
        {
            if (InSpace(s.State.PlayerId))
            {
                continue; // piloting in space, not on foot
            }

            if (WrapDistSq(origin, s.State.Position) > MedkitRadius * MedkitRadius)
            {
                continue;
            }

            var t = s.State;
            if (t.Health <= 0f)
            {
                continue; // already down — a medkit can't revive
            }

            float before = t.Health;
            t.Health = System.Math.Min(100f, t.Health + MedkitHealAmount);
            if (t.Health != before)
            {
                SendPlayerState(s);
            }
        }
    }

    /// <summary>Freezes every creature within <see cref="StasisRadius"/> of the aim point for
    /// <see cref="StasisDuration"/> seconds (item 36) — they stop moving + biting so you can scan them safely.</summary>
    private void UseStasisProjector(Vector3f target)
    {
        bool any = false;
        foreach (var c in _creatures)
        {
            if (!c.IsGiant && WrapDistSq(target, c.Position) <= StasisRadius * StasisRadius) // #1998: too big to hold
            {
                c.FrozenTimer = System.Math.Max(c.FrozenTimer, StasisDuration); // never shorten an existing freeze
                any = true;
            }
        }

        if (any)
        {
            BroadcastCreatures(); // push the Frozen flag now so the client tints them immediately
        }
    }

    /// <summary>Test hook: seconds a creature is still frozen (0 = not frozen).</summary>
    public double CreatureFrozenForTest(string creatureId)
        => _creatures.FirstOrDefault(c => c.Id == creatureId)?.FrozenTimer ?? 0;

    /// <summary>Destroys a sphere of mineable, unprotected terrain around the aim point (item 36) — a clearing
    /// blast with <b>no loot</b> (so it can't out-mine a drill). Respects ship / settlement / station / other
    /// players' landing-zone protection and leaves indestructible blocks alone.</summary>
    private void UseTerrainBlaster(PlayerSession session, Vector3f target)
    {
        var center = WorldConstants.CanonicalBlock(new Vector3i(
            (int)System.Math.Floor(target.X), (int)System.Math.Floor(target.Y), (int)System.Math.Floor(target.Z)), _world.Circumference);
        // #2001: a blast is heard far through the sand (read before it carves: the ground under the blast).
        EmitVibration(new Vector3f(center.X + 0.5f, center.Y + 0.5f, center.Z + 0.5f), VibrationSource.Blaster, session.State.PlayerId);

        for (int dx = -BlasterRadius; dx <= BlasterRadius; dx++)
            for (int dy = -BlasterRadius; dy <= BlasterRadius; dy++)
                for (int dz = -BlasterRadius; dz <= BlasterRadius; dz++)
                {
                    if (dx * dx + dy * dy + dz * dz > BlasterRadius * BlasterRadius)
                    {
                        continue; // carve a sphere, not a cube
                    }

                    var p = WorldConstants.CanonicalBlock(new Vector3i(center.X + dx, center.Y + dy, center.Z + dz), _world.Circumference);
                    var b = _world.GetBlock(p);
                    if (b.IsAir || IsShipBlock(p) || IsSettlementBlock(p) || IsStationBlock(p)
                        || IsBaseProtected(p, session.State.PlayerId, session.State.IsAdmin))
                    {
                        continue;
                    }

                    var d = _world.Definition(b);
                    if (d is null || !d.Mineable)
                    {
                        continue; // leave bedrock / indestructible blocks intact
                    }

                    _world.SetBlock(p, BlockId.Air);
                    _miningProgress.Remove(p);
                    BroadcastToWorld(new BlockChanged { X = p.X, Y = p.Y, Z = p.Z, Block = BlockId.AirValue });
                    if (d.Key == "radio_beacon")
                    {
                        RemoveBeaconAt(p); // don't orphan a blasted beacon's label/marker (item 37)
                    }
                    if (d.Key == "base_core")
                    {
                        RemoveBaseAt(p); // don't orphan a blasted base's claim/marker (Grundstein)
                    }
                    if (d.Key == "beam_block")
                    {
                        RemoveBeamAt(p); // don't orphan a blasted beam block's name/marker (teleporter pad)
                    }
                    if (IsFluid(b.Value) || HasFluidNeighbor(p))
                    {
                        OnFluidRemoved(p); // a hole opened in/under water or lava refills
                    }

                    OnSupportRemoved(p); // #1319: sand over the blast crater comes down
                }
    }

    /// <summary>The fluid pump (#2106): pulls ONE cell of liquid into the player's pack — oil (a still deposit: the cell
    /// stays air, the pocket is finite), or water / lava (the automaton refills the cell from its neighbours, exactly as
    /// when a tier-3 drill mines them). Goes through the ordinary break path, so the drop, the fluid wake and the sand
    /// above behave as for any mined block; protected cells (ship, settlement, station, someone else's base) are refused.
    /// Returns false on a miss so the caller charges nothing.</summary>
    private bool UseFluidPump(PlayerSession session, Vector3f target)
    {
        var p = WorldConstants.CanonicalBlock(new Vector3i(
            (int)System.Math.Floor(target.X), (int)System.Math.Floor(target.Y), (int)System.Math.Floor(target.Z)), _world.Circumference);
        var b = _world.GetBlock(p);
        var d = b.IsAir ? null : _world.Definition(b);
        if (d is null || !(IsFluid(b.Value) || d.Liquid) || d.Drops.Count == 0)
        {
            Reject(session, "gadget", "@srv.pump.no_fluid"); // #2112: a liquid that yields nothing (the gas sea) cannot be pumped
            return false;
        }

        if (IsShipBlock(p) || IsSettlementBlock(p) || IsStationBlock(p)
            || IsBaseProtected(p, session.State.PlayerId, session.State.IsAdmin))
        {
            Reject(session, "gadget", "@srv.gadget.not_usable");
            return false;
        }

        var pool = new MaterialPool(_content, session.State, _ship);
        BreakBlockCore(session, session.State.PlayerId, p, d, pool, null);
        SendInventory(session);
        SpillPoolOverflow(session, pool, p); // #853: a full pack leaves the cell's yield on the ground
        return true;
    }

    /// <summary>Terrain scanner (Feature 40): scans a sphere around the player for valuable blocks (ores,
    /// crystal, data caches) and sends their positions to that player as <see cref="OreScanResult"/> — the
    /// client renders them as through-wall glow markers. Non-destructive; nearest hits win when the world is
    /// richer than the message cap.</summary>
    private void UseTerrainScanner(PlayerSession session)
    {
        ShipAiOnScannerUsed(session); // the "you carry a scanner" nudge (#1078) rests after a real use
        var scan = BuildOreScan(session.State, VegaScannerRadiusBonus(session));
        if (scan.OilFound)
        {
            // #2372: the oil echo's pocket as a ping — on the compass and the map, like a ping of the player's own.
            RaisePingAt(session, new Vector3f(scan.OilX + 0.5f, scan.OilY + 0.5f, scan.OilZ + 0.5f));
        }

        Send(session, scan);
    }

    /// <summary>Test seam: the scan result the terrain scanner would send for a player right now
    /// (including any AI-core radius bonus — VEGA crunches the returns).</summary>
    public OreScanResult OreScanForTest(string playerId)
        => FindSessionByPlayerId(playerId) is { } s ? BuildOreScan(s.State, VegaScannerRadiusBonus(s)) : new OreScanResult();

    private OreScanResult BuildOreScan(BlocksBeyondTheStars.Shared.State.PlayerState state, int radiusBonus = 0)
    {
        // #900: blown grit and ionised air swallow the pulse — the scan reaches noticeably less far in a
        // sandstorm, a gale or an ion storm. Never below a third of its range, so it stays usable.
        int radius = System.Math.Max(
            ScannerRadius / 3,
            (int)System.Math.Round((ScannerRadius + radiusBonus) * WeatherScanFactor()));
        var p = state.Position;
        var centre = WorldConstants.CanonicalBlock(new Vector3i(
            (int)System.Math.Floor(p.X), (int)System.Math.Floor(p.Y), (int)System.Math.Floor(p.Z)), _world.Circumference);

        var hits = new List<(Vector3i Pos, ushort Block, int DistSq)>();
        for (int dx = -radius; dx <= radius; dx++)
            for (int dy = -radius; dy <= radius; dy++)
                for (int dz = -radius; dz <= radius; dz++)
                {
                    int distSq = dx * dx + dy * dy + dz * dz;
                    if (distSq > radius * radius)
                    {
                        continue; // a pulse sphere, not a cube
                    }

                    var cell = WorldConstants.CanonicalBlock(new Vector3i(centre.X + dx, centre.Y + dy, centre.Z + dz), _world.Circumference);
                    var b = _world.GetBlock(cell);
                    if (b.IsAir)
                    {
                        continue;
                    }

                    if (IsValuableBlock(_world.Definition(b)?.Key))
                    {
                        hits.Add((cell, b.Value, distSq));
                    }
                }

        // Nearest finds first; cap so an ore-rich world doesn't flood the message.
        hits.Sort((a, b2) => a.DistSq.CompareTo(b2.DistSq));
        int n = System.Math.Min(hits.Count, ScannerMaxHits);
        var result = new OreScanResult
        {
            X = new int[n],
            Y = new int[n],
            Z = new int[n],
            Block = new ushort[n],
            Seconds = ScannerSeconds,
            Capped = hits.Count > n, // the client's toast then reads "80+" rather than a wrong exact count
        };
        for (int i = 0; i < n; i++)
        {
            result.X[i] = hits[i].Pos.X;
            result.Y[i] = hits[i].Pos.Y;
            result.Z[i] = hits[i].Pos.Z;
            result.Block[i] = hits[i].Block;
        }

        AddOilEcho(result, centre);
        return result;
    }

    /// <summary>The scanner's oil echo (#2372): the pulse's sphere reaches 20 blocks, the pockets lie ~75 deep and ~600
    /// apart, so the sphere alone practically never shows one. The echo asks the generator's pocket function for the
    /// pockets within <see cref="OilEchoRange"/> blocks (a handful of hotspot cells, no block loop) and reports the nearest
    /// one a pump has not emptied yet — its top oil cell, for the toast, the marker and the ping. Only on a world that
    /// holds oil pockets at all; elsewhere the result says nothing about oil.</summary>
    private void AddOilEcho(OreScanResult result, Vector3i centre)
    {
        var planet = _world.Planet;
        if (planet is null || !_generator.CarriesOilPockets(planet))
        {
            return;
        }

        result.OilEcho = true;
        result.OilEchoRange = OilEchoRange;
        int asked = 0;
        foreach (var site in _generator.FindOilPocketsNear(planet, centre.X, centre.Z, OilEchoRange))
        {
            if (asked++ >= OilEchoCandidates)
            {
                break;
            }

            if (TryFindPocketOil(site, out var cell))
            {
                result.OilFound = true;
                result.OilX = cell.X;
                result.OilY = cell.Y;
                result.OilZ = cell.Z;
                return;
            }
        }
    }

    /// <summary>Whether a pump has left oil in the pocket: the highest oil cell of its centre column, or of four columns
    /// three blocks round it (their spans lie inside the centre's). Reads the live world, so a pocket pumped dry at its
    /// heart stays silent instead of luring the player to an empty shell.</summary>
    private bool TryFindPocketOil(BlocksBeyondTheStars.WorldGeneration.OilPocketSite site, out Vector3i cell)
    {
        cell = default;
        var oilId = _content.GetBlock("oil")?.NumericId;
        if (oilId is null)
        {
            return false;
        }

        foreach (var (ox, oz) in PocketProbeColumns)
        {
            for (int y = site.OilHi; y >= site.OilLo; y--)
            {
                var at = WorldConstants.CanonicalBlock(new Vector3i(site.X + ox, y, site.Z + oz), _world.Circumference);
                if (_world.GetBlock(at) == oilId.Value)
                {
                    cell = at;
                    return true;
                }
            }
        }

        return false;
    }

    private static readonly (int X, int Z)[] PocketProbeColumns = { (0, 0), (3, 0), (-3, 0), (0, 3), (0, -3) };

    /// <summary>What the scanner counts as "valuable": every ore vein block, crystal, data caches — and oil (#2106), or
    /// nobody would ever find a pocket forty blocks down.</summary>
    private static bool IsValuableBlock(string? key)
        => key != null
           && (key.EndsWith("_ore", System.StringComparison.Ordinal) || key is "crystal" or "data_cache" or "oil");

    /// <summary>Test hook: how many seconds until the gadget is usable again for this player (0 = ready).</summary>
    public double GadgetCooldownForTest(string playerId, string gadgetKey)
        => _gadgetReadyAt.TryGetValue(playerId + "|" + gadgetKey, out var at) ? System.Math.Max(0, at - _uptime) : 0;

    /// <summary>Test hook: run a gadget use as if the intent arrived.</summary>
    public void UseGadgetForTest(string playerId, string gadgetKey, Vector3f target)
    {
        if (FindSessionByPlayerId(playerId) is { } s)
        {
            HandleUseGadget(s, new UseGadgetIntent { GadgetKey = gadgetKey, X = target.X, Y = target.Y, Z = target.Z });
        }
    }

    // ---------------- The energy rope gun (#2317/#2319) ----------------

    /// <summary>
    /// The rope's shot: <paramref name="target"/> is the point on a block face the client's aim ray struck. Accepted
    /// when the player may hold a rope here at all, the point lies within the tool's range (plus the move-stream
    /// slack) of the eyes, a solid block sits just behind the face, and nothing solid blocks the line of sight from the
    /// eyes — gadgets used to check neither distance nor sight, the rope does both. The anchor is stored canonical
    /// (wrapped) and rides the presence to other players; the pull is the shooter's own movement. False refuses with
    /// a reason and leaves energy and cooldown untouched.
    /// </summary>
    private bool UseRopeGun(PlayerSession session, Vector3f target)
    {
        var p = session.State;
        if (!RopeAllowed(p))
        {
            Reject(session, "gadget", "@srv.rope.no_hold");
            return false;
        }

        if (!float.IsFinite(target.X) || !float.IsFinite(target.Y) || !float.IsFinite(target.Z))
        {
            Reject(session, "gadget", "@srv.rope.no_hold");
            return false;
        }

        float range = _content.GetItem(RopeGunItem)?.Tool?.Range ?? 0f;
        if (range <= 0f)
        {
            range = RopeDefaultRange;
        }

        var eye = new Vector3f(p.Position.X, p.Position.Y + SightEyeHeight, p.Position.Z);
        var near = Unwrapped(eye, target); // the anchor in the eye's frame, the short way round a seam
        var span = near - eye;
        float dist = (float)System.Math.Sqrt(span.DistanceSquared(Vector3f.Zero));
        if (dist > range + RopeRangeSlack || dist < 0.5f)
        {
            Reject(session, "gadget", "@srv.rope.too_far");
            return false;
        }

        // The cell just behind the struck face: nudge the point a little further along the ray.
        float nx = span.X / dist, ny = span.Y / dist, nz = span.Z / dist;
        var cell = new Vector3i(
            (int)System.Math.Floor(near.X + (nx * 0.05f)),
            (int)System.Math.Floor(near.Y + (ny * 0.05f)),
            (int)System.Math.Floor(near.Z + (nz * 0.05f)));
        if (!IsSolidBlock(_world.GetBlock(cell)))
        {
            Reject(session, "gadget", "@srv.rope.no_hold");
            return false;
        }

        if (!HasLineOfSight(p.Position, target, SightEyeHeight, 0f, skipToCell: true))
        {
            Reject(session, "gadget", "@srv.rope.too_far");
            return false;
        }

        bool onSurface = !InStation(p.PlayerId) && !InSpace(p.PlayerId);
        float ax = onSurface ? (float)WorldConstants.WrapX((double)target.X, _world.Circumference) : target.X;
        float az = onSurface ? (float)WorldConstants.WrapZ((double)target.Z, _world.Circumference) : target.Z;
        p.RopeAnchor = new Vector3f(ax, target.Y, az);
        ShipAiHintOnce(session, "rope_gun"); // the first rope that holds: how to reel in, hang and let go
        return true;
    }

    /// <summary>The rope was let go (#2319): forget the anchor the presence shows to others.</summary>
    private void HandleReleaseRope(PlayerSession session) => session.State.RopeAnchor = null;

    /// <summary>Where a rope can hold at all: on foot on a body — not aboard, on a spacewalk, in the ship's interior,
    /// seated, on a train, driving a speeder, above the atmosphere or in a flight instance.</summary>
    private bool RopeAllowed(PlayerState p)
        => !p.AboardShip && !p.InEva && !p.Seated && p.InTrain.Length == 0 && string.IsNullOrEmpty(p.InSpeeder)
           && !p.AboveAtmosphere && !InSpace(p.PlayerId) && !InShipInterior(p.PlayerId);

    /// <summary>True while the selected hotbar slot holds the rope gun.</summary>
    private static bool HoldsRopeGun(PlayerState p)
        => p.SelectedHotbarSlot >= 0 && p.SelectedHotbarSlot < p.Inventory.Slots.Count
           && p.Inventory.Slots[p.SelectedHotbarSlot]?.Item == RopeGunItem;

    /// <summary>Drops the rope when the player can no longer hold it: the gun left the hand, or the player boarded,
    /// sat down, rode off, or rose above the air. Called on every hotbar change and every move report.</summary>
    private void ClearRopeIfNotHeld(PlayerState p)
    {
        if (p.RopeAnchor.HasValue && (!RopeAllowed(p) || !HoldsRopeGun(p)))
        {
            p.RopeAnchor = null;
        }
    }

    /// <summary>Test hook (#2319): the release intent as if the client had sent it.</summary>
    public void ReleaseRopeForTest(string playerId)
    {
        if (FindSessionByPlayerId(playerId) is { } s)
        {
            HandleReleaseRope(s);
        }
    }
}
