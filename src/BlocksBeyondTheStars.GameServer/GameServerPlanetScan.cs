// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The planet report of the ship scanner (#2140 — Bloody Mary's "Ressourcenscan" — and #2239/#2240). Every ship can
/// scan any body of the current star system: the cockpit's tier 1 gives the overview card (air, temperature, gravity,
/// weather, water, life, machines, terrain, structures, frontier); the Deep scanner (tier 2, the former planet scanner
/// module) adds the ore veins (how common, from which depth, which drill), the world's richness and the extras (oil,
/// data caches, surface outcrops, crater metals); the Quantum scanner (tier 3) adds the rare-tier veins and the
/// secrets. The resource numbers come from <see cref="WorldGeneration.WorldGenerator.SurveyResources"/>, i.e. from the
/// terrain's own rolls; nothing here loads a world.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The Deep scanner module (tier 2 of the ship scanner, key kept from the planet scanner it was).</summary>
    internal const string PlanetScannerModule = "planet_scanner";

    // Abundance bands on a vein's effective density (type rarity × frontier boost × world richness — the value the
    // terrain's ore slot is built with). Type rarities span 0.008..0.1 and a world's richness ~0.7..4, so an average
    // world reads iron "common", titanium "rare".
    private const double PlanetScanCommonDensity = 0.10;
    private const double PlanetScanModerateDensity = 0.05;

    // The world's overall richness band (per-world roll 1.2..2.2 × the world's ore option).
    private const double PlanetScanLeanRichness = 1.45;
    private const double PlanetScanRichRichness = 1.95;

    private void HandlePlanetScan(PlayerSession session, PlanetScanIntent intent)
    {
        if (BuildPlanetScan(session, intent.BodyId, out string reason) is { } result)
        {
            Send(session, result);
            if (result.KnowledgeGained > 0)
            {
                SendInventory(session); // the knowledge total rides the inventory update
            }
        }
        else
        {
            Reject(session, "planetscan", reason);
        }
    }

    /// <summary>The report for <paramref name="bodyId"/> (empty = the body the ship is at), or null with the rejection
    /// token in <paramref name="reason"/>.</summary>
    private PlanetScanResult? BuildPlanetScan(PlayerSession session, string? bodyId, out string reason)
    {
        var p = session.State;
        if (!p.AboardShip && !InSpace(p.PlayerId))
        {
            reason = "@srv.planetscan.aboard";
            return null;
        }

        var scanner = ShipScanner(ShipOf(session)); // #2240: every ship scans; the tier decides how deep

        var here = ResolveLocationHostBody(CurrentBodyLocation(session));
        var body = string.IsNullOrEmpty(bodyId) ? here : _galaxy?.FindBody(bodyId!);
        if (body is null || here is null || body.SystemId != here.SystemId)
        {
            reason = "@srv.planetscan.out_of_range"; // the scanner reaches the bodies of this star system
            return null;
        }

        var planet = string.IsNullOrEmpty(body.PlanetType) ? null : _content.GetPlanet(body.PlanetType!);
        if (planet is null || planet.Void)
        {
            reason = "@srv.planetscan.no_surface"; // a station or a ship world: nothing to survey
            return null;
        }

        bool airlessMoon = body.Kind == CelestialKind.Moon
                           && string.Equals(planet.Atmosphere, "none", System.StringComparison.OrdinalIgnoreCase);
        var survey = _generator.SurveyResources(planet, body.Id, FrontierOreBoostFor(FrontierTierForBody(body.Id)), airlessMoon);
        var (rows, danger) = BuildBodyOverview(body, planet, scanner.Tier);
        bool resources = scanner.Tier >= 2;
        bool secrets = scanner.Tier >= 3;

        // The first overview of a body pays a little knowledge (#2239) — once per player per body.
        int gained = 0;
        if (p.Scanned.Add("body:" + body.Id))
        {
            gained = (int)System.Math.Round(KnowledgeBodyOverview * ScanMultiplier(p));
            p.KnowledgePoints += gained;
        }

        reason = string.Empty;
        return new PlanetScanResult
        {
            BodyId = body.Id,
            BodyName = body.Name,
            PlanetType = planet.Key,
            Tier = (byte)scanner.Tier,
            Rows = rows,
            Danger = danger,
            KnowledgeGained = gained,
            ResourcesLocked = !resources,
            Richness = !resources ? (byte)1
                : survey.Richness < PlanetScanLeanRichness ? (byte)0 : survey.Richness > PlanetScanRichRichness ? (byte)2 : (byte)1,
            Ores = !resources ? System.Array.Empty<NetPlanetOre>() : survey.Veins
                .Where(v => secrets || !v.RareTier) // the rare-tier veins need the Quantum scanner
                .Select(v => new NetPlanetOre
                {
                    Block = v.Block,
                    Abundance = v.Density >= PlanetScanCommonDensity ? (byte)2 : v.Density >= PlanetScanModerateDensity ? (byte)1 : (byte)0,
                    MinDepth = v.MinDepth,
                    RareTier = v.RareTier,
                }).ToArray(),
            OilPockets = resources && survey.OilPockets,
            DataCaches = secrets && survey.DataCaches,
            SurfaceOutcrops = resources && survey.SurfaceOutcrops,
            CraterMetals = resources && survey.CraterMetals,
            GasWorld = survey.GasWorld,
        };
    }

    /// <summary>The location the player's ship is at: the body a flight instance is anchored to ("space:&lt;body&gt;") —
    /// also while the pilot walks the floating interior — otherwise the world the player stands in.</summary>
    private string? CurrentBodyLocation(PlayerSession session)
    {
        string pid = session.State.PlayerId;
        string? instanceId = _playerInstance.TryGetValue(pid, out var flying) ? flying
            : _inShipInterior.TryGetValue(pid, out var interior) ? interior.InstanceId
            : null;
        return instanceId != null && instanceId.StartsWith("space:", System.StringComparison.Ordinal)
            ? instanceId.Substring("space:".Length)
            : session.CurrentLocationId;
    }

    /// <summary>Test seam (#2140): the planet scan a player would get right now, or null with the rejection token.</summary>
    public PlanetScanResult? PlanetScanForTest(string playerId, string bodyId, out string reason)
    {
        reason = "@srv.planetscan.aboard";
        if (FindSessionByPlayerId(playerId) is not { } session)
        {
            return null;
        }

        Serve(session);
        return BuildPlanetScan(session, bodyId, out reason);
    }
}
