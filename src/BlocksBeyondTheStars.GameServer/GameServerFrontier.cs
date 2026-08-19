// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The frontier (#1122, #1123): distance from home finally MEANS something. Systems tier by their star-map
/// distance from the home system — outer systems generate richer rare-tier ore veins, roll one extra
/// vault/monument, drop a little more structure loot, and (opt-in world rule, never on family/peaceful
/// setups) field tougher machines. And when the world was created with a growing galaxy, hyperjumping into
/// one of the current OUTERMOST systems appends a brand-new system beyond it — deterministically (system N
/// is a pure function of seed + N; only the grown COUNT persists), up to a soft cap.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Map distance from home below which a system counts as home turf (tier 0).</summary>
    private const float FrontierNearDistance = 400f;

    /// <summary>Map distance from home beyond which a system is full frontier (tier 2).</summary>
    private const float FrontierFarDistance = 700f;

    /// <summary>How many of the outermost systems count as "the edge" for galaxy growth.</summary>
    private const int GalaxyEdgeCount = 3;

    /// <summary>Growth stops here: enough galaxy for any playthrough, small enough for the Pi target
    /// (bodies are POCOs — ~600 at the cap — but every star-map send serialises all of them).</summary>
    private const int GalaxyGrowthSoftCap = 48;

    /// <summary>Rare-vein multiplier per frontier tier (#1122) — applied only to tier-2-tool ores, so the
    /// starter iron/copper economy is identical everywhere.</summary>
    private static double FrontierOreBoostFor(int tier) => tier switch
    {
        2 => 1.6,
        1 => 1.25,
        _ => 1.0,
    };

    /// <summary>The frontier tier of a star system: 0 = home turf, 1 = mid, 2 = frontier. Measured as
    /// star-map distance from the HOME system with absolute thresholds, so a system's tier is a pure
    /// function of the seed — it never re-tiers when the galaxy grows, and the home system is always 0.
    /// The finale system sits at the map rim and naturally reads as frontier.</summary>
    private int FrontierTierOf(string? systemId)
    {
        if (string.IsNullOrEmpty(systemId) || _galaxy is null || _galaxy.Systems.Count == 0)
        {
            return 0;
        }

        var home = HomeSystem();
        if (home is null || systemId == home.Id)
        {
            return 0;
        }

        var system = _galaxy.Systems.FirstOrDefault(s => s.Id == systemId);
        if (system is null)
        {
            return 0;
        }

        float dx = system.MapX - home.MapX;
        float dy = system.MapY - home.MapY;
        float dist = (float)Math.Sqrt(dx * dx + dy * dy);
        return dist < FrontierNearDistance ? 0 : dist < FrontierFarDistance ? 1 : 2;
    }

    /// <summary>The frontier tier of the system a BODY lives in; 0 for anything the galaxy doesn't know
    /// (station interiors, ship interiors, tests without a galaxy).</summary>
    private int FrontierTierForBody(string? locationId)
    {
        var body = string.IsNullOrEmpty(locationId) ? null : _galaxy?.FindBody(locationId);
        return body is null ? 0 : FrontierTierOf(body.SystemId);
    }

    /// <summary>Home = the system the start body lives in — in practice "sys0" (the generator's index 0,
    /// the same convention the archetype roller and the fallback station use).</summary>
    private StarSystem? HomeSystem()
        => _galaxy?.Systems.FirstOrDefault(s => s.Id == "sys0") ?? _galaxy?.Systems.FirstOrDefault();

    /// <summary>Whether this system currently ranks among the outermost <see cref="GalaxyEdgeCount"/>
    /// procedural systems by map distance from home — the growth trigger. Recomputed per call: every
    /// growth pushes the rim outward, so the edge naturally moves with it.</summary>
    private bool IsEdgeSystem(string systemId)
    {
        var home = HomeSystem();
        if (home is null || systemId == home.Id)
        {
            return false;
        }

        var outermost = _galaxy!.Systems
            .Where(s => s.Id.StartsWith("sys", StringComparison.Ordinal) && s.Id != home.Id)
            .OrderByDescending(s =>
            {
                float dx = s.MapX - home.MapX;
                float dy = s.MapY - home.MapY;
                return dx * dx + dy * dy;
            })
            .Take(GalaxyEdgeCount);
        return outermost.Any(s => s.Id == systemId);
    }

    /// <summary>Galaxy growth (#1123), called from the two "a system became newly known to this player"
    /// funnels: when the world grows and the player just reached the edge, one brand-new system appears
    /// beyond it. Deterministic and persistence-light: only <c>WorldMetadata.GalaxyGrownSystems</c> is
    /// stored — a restart regenerates the grown systems byte-identically from (seed, N).</summary>
    private void MaybeGrowGalaxy(PlayerSession session, string systemId)
    {
        if (!_meta.Description.GalaxyGrowth || _galaxy is null
            || !systemId.StartsWith("sys", StringComparison.Ordinal) || !IsEdgeSystem(systemId))
        {
            return;
        }

        int procedural = Math.Max(0, _meta.Description.StarSystemCount) + Math.Max(0, _meta.GalaxyGrownSystems);
        if (procedural >= GalaxyGrowthSoftCap)
        {
            Send(session, new ServerMessage { Text = "@srv.galaxy.frontier_quiet" });
            return;
        }

        // Regenerate the whole procedural galaxy one system larger and adopt ONLY the new one: the
        // generator's name registry is claimed in index order, so the first N systems of an (N+1)-run are
        // byte-identical to the live ones (the prefix property) — and a full run is a few milliseconds of
        // pure CPU over POCOs. Appending the fresh object keeps every live reference into _galaxy valid.
        var regrown = new UniverseGenerator(_meta.Seed, _meta.Description, _content).Generate(procedural + 1);
        if (regrown.Systems.Count <= procedural)
        {
            return;
        }

        var grown = regrown.Systems[procedural];
        if (_galaxy.Systems.Any(s => s.Id == grown.Id))
        {
            return; // paranoia: never double-append (a stale metadata count would otherwise duplicate ids)
        }

        _galaxy.Systems.Add(grown);
        _meta.GalaxyGrownSystems = Math.Max(0, _meta.GalaxyGrownSystems) + 1;

        // Pin the new bodies' types right away (#468 freeze), same as BuildGalaxy does at start.
        foreach (var body in grown.Bodies)
        {
            if (!string.IsNullOrEmpty(body.PlanetType))
            {
                _meta.BodyPlanetTypes[body.Id] = body.PlanetType!;
            }
        }

        _repo.SaveMetadata(_meta);
        BroadcastStarMap();
        Send(session, new ServerMessage
        {
            Text = Localize(session.Locale, "srv.galaxy.grown").Replace("{name}", grown.Name),
        });
        _log.Info($"Galaxy grew to {procedural + 1} systems: '{grown.Name}' ({grown.Id}) appeared beyond {systemId}.");
    }

    // ---- test seams ----

    public int FrontierTierForTest(string systemId) => FrontierTierOf(systemId);

    public bool IsEdgeSystemForTest(string systemId) => IsEdgeSystem(systemId);

    /// <summary>Runs the growth funnel as if the session had just made <paramref name="systemId"/> newly
    /// known; returns whether the galaxy actually grew.</summary>
    public bool TryGrowGalaxyForTest(PlayerSession session, string systemId)
    {
        int before = _galaxy?.Systems.Count ?? 0;
        MaybeGrowGalaxy(session, systemId);
        return (_galaxy?.Systems.Count ?? 0) > before;
    }
}
