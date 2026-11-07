// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Story;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Pack star systems (#3357, the Sounders' detuned Ornemis) — the generalisation of VEGA's Guardian system: a pack declares
/// <see cref="StoryDefinition.Systems"/>, and such a system is appended to the galaxy the first time any player satisfies its
/// <see cref="StoryPackSystem.VisibleIf"/> (an old save gets it too). The save remembers it in
/// <see cref="WorldMetadata.StampedFeatures"/> (<c>packsystem|&lt;id&gt;|&lt;mapX&gt;|&lt;mapY&gt;</c> — the map spot is pinned at
/// the first append), so it is re-appended at every start, where the galaxy is regenerated from the seed. Per player it is on
/// the star map and a jump target only while it is visible to them (<see cref="PackSystemHiddenFor"/>): the pack active for
/// them and its condition holding — or they are in it or were (a crewmate who flew along). Normal jump rules apply (a jump
/// generator or a lane). Never a wormhole end (<see cref="IsStoryLockedSystem"/>). Also here: the marker providers
/// <c>nearest_monument</c>, <c>pack_site:&lt;id&gt;</c> (<see cref="RegisterPackSite"/>) and <c>story_system:&lt;id&gt;</c> (#3349).
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The <see cref="WorldMetadata.StampedFeatures"/> prefix of an appended pack system.</summary>
    internal const string PackSystemFeaturePrefix = "packsystem|";

    /// <summary>The narrator line of a <c>story_system:&lt;id&gt;</c> step's marker ({0} = the system's name).</summary>
    internal const string StepMarkedSystemLine = "vega.step.marked_system";

    /// <summary>How far out an <c>edge</c> pack system sits at least (map units) — a little inside the Guardian's ring.</summary>
    private const float PackSystemEdgeDistance = 1450f;

    /// <summary>Sites the gameplay code registered for packs (#3349, <c>pack_site:&lt;id&gt;</c>), by site id. RAM only — the code
    /// that places a site registers it again whenever it finds it (a world load).</summary>
    private readonly Dictionary<string, (string PackId, string LocationId, Vector3i Pos)> _packSites = new(StringComparer.Ordinal);

    /// <summary>
    /// #3349: registers a site a pack's gameplay code placed (the Sounders' Choir Tower) so a step's
    /// <c>onStart.reveal: "pack_site:&lt;siteId&gt;"</c> can mark it while the player is on <paramref name="locationId"/>. Call it
    /// when the site is placed and again whenever its world loads (RAM only). A second call for the same site id moves it.
    /// </summary>
    public void RegisterPackSite(string packId, string siteId, string locationId, Vector3i pos)
    {
        if (string.IsNullOrEmpty(siteId) || string.IsNullOrEmpty(locationId))
        {
            return;
        }

        _packSites[siteId] = (packId ?? string.Empty, locationId, pos);
    }

    // ---------------------------------------------------------------------------------------------
    // The declared systems.
    // ---------------------------------------------------------------------------------------------

    /// <summary>Every pack system of an installed player pack, with its pack (the world's switched-off packs included — their
    /// system stays in the galaxy once appended, so nobody is stranded, but nobody new sees it).</summary>
    private IEnumerable<(StoryDefinition Pack, StoryPackSystem System)> DeclaredPackSystems()
    {
        foreach (var def in _content.Stories.Values)
        {
            if (!string.Equals(StoryRegistry.EffectiveScope(def), StoryRegistry.ScopePlayer, StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var system in def.Systems ?? new List<StoryPackSystem>())
            {
                if (IsValidPackSystemId(system?.Id))
                {
                    yield return (def, system!);
                }
            }
        }
    }

    /// <summary>A pack system id may never be a procedural one (<c>sys…</c>) or the Guardian's.</summary>
    private static bool IsValidPackSystemId(string? id)
        => !string.IsNullOrEmpty(id) && id != GuardianFinaleSystemId && !id!.StartsWith("sys", StringComparison.Ordinal)
           && StoryPackValidator.IsKeySafeId(id);

    /// <summary>The pack system with this id, if one is declared.</summary>
    private bool TryGetPackSystem(string? systemId, out StoryDefinition pack, out StoryPackSystem system)
    {
        foreach (var (p, s) in DeclaredPackSystems())
        {
            if (string.Equals(s.Id, systemId, StringComparison.Ordinal))
            {
                pack = p;
                system = s;
                return true;
            }
        }

        pack = null!;
        system = null!;
        return false;
    }

    /// <summary>The save's record of an appended pack system (its feature entry), or null.</summary>
    private string? PackSystemRecord(string systemId)
        => _meta.StampedFeatures.FirstOrDefault(f => f.StartsWith(PackSystemFeaturePrefix + systemId + "|", StringComparison.Ordinal));

    // ---------------------------------------------------------------------------------------------
    // Appending.
    // ---------------------------------------------------------------------------------------------

    /// <summary>At start (after the galaxy is built): re-appends every pack system the save recorded, at its pinned spot.</summary>
    private void EnsurePackSystemsInGalaxy()
    {
        foreach (var (pack, system) in DeclaredPackSystems())
        {
            if (PackSystemRecord(system.Id) is { } record)
            {
                AppendPackSystem(system, record);
            }
        }
    }

    /// <summary>Appends a pack system the first time — records it in the save (with its map spot) and refreshes everyone's
    /// star map (each player's filters it). Idempotent. Returns whether the system is in the galaxy now.</summary>
    private bool RevealPackSystem(StoryDefinition pack, StoryPackSystem system)
    {
        if (_galaxy is null)
        {
            return false;
        }

        if (_galaxy.Systems.Any(s => s.Id == system.Id))
        {
            return true;
        }

        var (x, y) = PackSystemMapPosition(_galaxy, system, _meta.Seed);
        string record = PackSystemFeaturePrefix + system.Id + "|"
                        + x.ToString("R", CultureInfo.InvariantCulture) + "|" + y.ToString("R", CultureInfo.InvariantCulture);
        _meta.StampedFeatures.RemoveAll(f => f.StartsWith(PackSystemFeaturePrefix + system.Id + "|", StringComparison.Ordinal));
        _meta.StampedFeatures.Add(record);
        _repo.SaveMetadata(_meta);
        AppendPackSystem(system, record);
        _log.Info($"Pack system '{system.Id}' of '{pack.Id}' joined the galaxy.");
        BroadcastStarMap();
        return true;
    }

    /// <summary>Adds the system (one landable body) at the record's map spot, unless it is there already.</summary>
    private void AppendPackSystem(StoryPackSystem def, string record)
    {
        if (_galaxy is null || _galaxy.Systems.Any(s => s.Id == def.Id))
        {
            return;
        }

        var parts = record.Split('|');
        var (mapX, mapY) = parts.Length >= 4
                           && float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float px)
                           && float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float py)
            ? (px, py)
            : PackSystemMapPosition(_galaxy, def, _meta.Seed);
        string name = PackSystemName(def, "en");
        var system = new StarSystem { Id = def.Id, Name = name, MapX = mapX, MapY = mapY };
        float angle = (BlocksBeyondTheStars.WorldGeneration.WorldGenerator.StableHash("packsystem-orbit:" + def.Id) % 360) * MathF.PI / 180f;
        system.Bodies.Add(new CelestialBody
        {
            Id = StoryPackSystem.BodyIdOf(def.Id),
            Name = string.IsNullOrEmpty(def.BodyName) ? name : def.BodyName,
            Kind = CelestialKind.Planet,
            PlanetType = _content.GetPlanet(def.PlanetType ?? string.Empty) is not null ? def.PlanetType : GuardianCorePlanetType(),
            SystemId = def.Id,
            SystemX = 940f * MathF.Cos(angle), // a calm second orbit (the generator's planets sit 420 + n·520 out)
            SystemZ = 940f * MathF.Sin(angle),
        });
        _galaxy.Systems.Add(system);
    }

    /// <summary>A pack system's name in a language: its <see cref="StoryPackSystem.NameKey"/>'s text, else the fixed name, else
    /// the id.</summary>
    private string PackSystemName(StoryPackSystem def, string localeCode)
    {
        if (!string.IsNullOrEmpty(def.NameKey))
        {
            string text = Localize(localeCode, def.NameKey);
            if (!string.IsNullOrEmpty(text) && text != def.NameKey && !text.StartsWith("[", StringComparison.Ordinal))
            {
                return text;
            }
        }

        return string.IsNullOrEmpty(def.Name) ? def.Id : def.Name;
    }

    /// <summary>
    /// Where a pack system goes on the star map (seed-stable; pinned in the save at the first append). <c>edge</c>: out past
    /// every procedural star like the Guardian's, but on a bearing of its own (turned 60°–300° from the Guardian's by the id).
    /// <c>inside</c>: the emptiest of 96 seeded spots inside the stars' box. Only the procedural systems count, so a pack
    /// system never moves the Guardian's spot (and the Guardian never moves this one).
    /// </summary>
    internal static (float X, float Y) PackSystemMapPosition(Galaxy galaxy, StoryPackSystem def, long seed)
    {
        var stars = galaxy.Systems.Where(s => BlocksBeyondTheStars.WorldGeneration.WormholePlacer.MayHoldWormhole(s.Id)).ToList();
        var home = stars.FirstOrDefault(s => s.Id == "sys0") ?? stars.FirstOrDefault();
        if (home is null)
        {
            return (-PackSystemEdgeDistance, PackSystemEdgeDistance);
        }

        uint hash = unchecked((uint)(BlocksBeyondTheStars.WorldGeneration.WorldGenerator.StableHash("packsystem:" + def.Id) ^ seed));
        if (string.Equals(def.Placement, StoryPackSystemPlacements.Inside, StringComparison.Ordinal) && stars.Count > 1)
        {
            return EmptiestSpot(galaxy, stars, hash);
        }

        float sumX = 0f, sumY = 0f, farthest = 0f;
        foreach (var s in stars)
        {
            sumX += s.MapX;
            sumY += s.MapY;
            float dx = s.MapX - home.MapX, dy = s.MapY - home.MapY;
            farthest = MathF.Max(farthest, MathF.Sqrt((dx * dx) + (dy * dy)));
        }

        float dirX = home.MapX - (sumX / stars.Count), dirY = home.MapY - (sumY / stars.Count);
        float baseAngle = (MathF.Abs(dirX) + MathF.Abs(dirY)) < 1f ? MathF.PI / 4f : MathF.Atan2(dirY, dirX);
        float turn = (60f + (hash % 241)) * MathF.PI / 180f;
        float distance = MathF.Max(PackSystemEdgeDistance, farthest + 150f);
        return (home.MapX + (MathF.Cos(baseAngle + turn) * distance), home.MapY + (MathF.Sin(baseAngle + turn) * distance));
    }

    /// <summary>The <c>inside</c> placement: of 96 seeded spots in the stars' box, the one farthest from every system.</summary>
    private static (float X, float Y) EmptiestSpot(Galaxy galaxy, List<StarSystem> stars, uint hash)
    {
        float minX = stars.Min(s => s.MapX), maxX = stars.Max(s => s.MapX);
        float minY = stars.Min(s => s.MapY), maxY = stars.Max(s => s.MapY);
        var rng = new Random(unchecked((int)hash));
        (float X, float Y) best = (minX, minY);
        double bestGap = -1;
        for (int i = 0; i < 96; i++)
        {
            float x = minX + ((float)rng.NextDouble() * (maxX - minX));
            float y = minY + ((float)rng.NextDouble() * (maxY - minY));
            double gap = galaxy.Systems.Min(s => ((s.MapX - x) * (double)(s.MapX - x)) + ((s.MapY - y) * (double)(s.MapY - y)));
            if (gap > bestGap)
            {
                bestGap = gap;
                best = (x, y);
            }
        }

        return best;
    }

    // ---------------------------------------------------------------------------------------------
    // Per player.
    // ---------------------------------------------------------------------------------------------

    /// <summary>Whether the player may see and target the pack system: the pack active for them (never in a world that switched
    /// it off) and its <see cref="StoryPackSystem.VisibleIf"/> holding.</summary>
    private bool PackSystemVisibleTo(PlayerSession s, StoryDefinition pack, StoryPackSystem system)
        => IsStoryPackActive(s, pack.Id) && ConditionTextsHold(system.VisibleIf, s);

    /// <summary>
    /// #3357: whether a system is kept off this player's star map and out of their jumps and travel: a pack system they may not
    /// see (<see cref="PackSystemVisibleTo"/>) — unless they are in it now or have been (a crewmate who flew along is never
    /// stranded). Any other system: false.
    /// </summary>
    internal bool PackSystemHiddenFor(PlayerSession s, string? systemId)
    {
        if (string.IsNullOrEmpty(systemId) || systemId!.StartsWith("sys", StringComparison.Ordinal) || systemId == GuardianFinaleSystemId
            || !TryGetPackSystem(systemId, out var pack, out var system))
        {
            return false;
        }

        if (PackSystemVisibleTo(s, pack, system) || s.State.KnownSystems.Contains(systemId))
        {
            return false;
        }

        return ResolveLocationHostBody(s.CurrentLocationId)?.SystemId != systemId;
    }

    /// <summary>#2506 + #3357: the one per-player gate of the star map, the hyperjump and the travel screen — the Guardian's
    /// system while the story is off, a pack system the player may not see.</summary>
    internal bool SystemHiddenFor(PlayerSession s, string? systemId)
        => GuardianSystemHidden(systemId) || PackSystemHiddenFor(s, systemId);

    /// <summary>The 1 Hz part of <see cref="TickPackExtrasFor"/>: a pack system this player may see joins the galaxy (the first
    /// time anyone may), and a change in what they may see re-sends their star map.</summary>
    private void TickPackSystemsFor(PlayerSession s, PackExtrasState state)
    {
        var visible = new List<string>();
        foreach (var (pack, system) in DeclaredPackSystems())
        {
            if (PackSystemVisibleTo(s, pack, system) && RevealPackSystem(pack, system))
            {
                visible.Add(system.Id);
            }
        }

        string now = string.Join(",", visible);
        if (!string.Equals(now, state.PackSystems, StringComparison.Ordinal))
        {
            bool first = state.PackSystems.Length == 0 && visible.Count == 0;
            state.PackSystems = now;
            if (!first)
            {
                SendStarMap(s);
            }
        }
    }

    /// <summary>The star-map name of a system for this player: a pack system's in their language.</summary>
    private string StarMapSystemName(PlayerSession s, StarSystem sys)
        => !sys.Id.StartsWith("sys", StringComparison.Ordinal) && TryGetPackSystem(sys.Id, out _, out var def) ? PackSystemName(def, s.Locale) : sys.Name;

    // ---------------------------------------------------------------------------------------------
    // Marker providers (#3349).
    // ---------------------------------------------------------------------------------------------

    /// <summary>The spot of a pack marker provider for this player on their world (<c>nearest_monument</c>,
    /// <c>pack_site:&lt;id&gt;</c>), or null — <paramref name="handled"/> false for any other provider.</summary>
    private Vector3f? PackRevealTarget(PlayerSession s, string reveal, out bool handled)
    {
        handled = true;
        if (reveal == StoryRevealProviders.NearestMonument)
        {
            if (_worlds.Find(s.CurrentLocationId) is not { } world)
            {
                return null;
            }

            var best = world.Monuments
                .OrderBy(m => WorldConstants.WrapDistanceSquared(m.Center, s.State.Position, world.World.Circumference))
                .FirstOrDefault();
            return best?.Center;
        }

        if (StoryRevealProviders.ArgumentOf(reveal, StoryRevealProviders.PackSite) is { } siteId)
        {
            return _packSites.TryGetValue(siteId, out var site) && site.LocationId == s.CurrentLocationId
                ? new Vector3f(site.Pos.X + 0.5f, site.Pos.Y + 1f, site.Pos.Z + 0.5f)
                : null;
        }

        handled = false;
        return null;
    }

    /// <summary>#3357: the <c>story_system:&lt;id&gt;</c> marker — when the player may see the system it joins the galaxy (if it has
    /// not yet), their star map is re-sent and the narrator names it ("… is on your star map now"). Nothing while it is
    /// hidden for them. Returns whether the marker went up.</summary>
    private bool MarkStorySystem(PlayerSession s, CompiledStep step, string systemId)
    {
        if (!TryGetPackSystem(systemId, out var pack, out var system) || !PackSystemVisibleTo(s, pack, system)
            || !RevealPackSystem(pack, system))
        {
            return false;
        }

        SendStarMap(s);
        SendPackLine(s, step.Pack, StepMarkedSystemLine, PackSystemName(system, s.Locale));
        return true;
    }

    // ---------------- Test seams ----------------

    /// <summary>Test seam: whether a system is in the galaxy now.</summary>
    internal bool GalaxyHasSystemForTest(string systemId) => _galaxy?.Systems.Any(s => s.Id == systemId) ?? false;

    /// <summary>Test seam: how many times the galaxy holds a system (an append must happen once).</summary>
    internal int GalaxySystemCountForTest(string systemId) => _galaxy?.Systems.Count(s => s.Id == systemId) ?? 0;

    /// <summary>Test seam: the star-map system ids this player is sent now.</summary>
    internal IReadOnlyList<string> StarMapSystemIdsForTest(PlayerSession s)
        => _galaxy?.Systems.Where(sys => !SystemHiddenFor(s, sys.Id)).Select(sys => sys.Id).ToList() ?? new List<string>();

    /// <summary>Test seam: re-appends the recorded pack systems (as a start does).</summary>
    internal void EnsurePackSystemsForTest() => EnsurePackSystemsInGalaxy();

    /// <summary>Test seam: drops a system from the galaxy (as a restart regenerating it from the seed would).</summary>
    internal void DropSystemForTest(string systemId) => _galaxy?.Systems.RemoveAll(s => s.Id == systemId);

    /// <summary>Test seam: where a pack marker provider (<c>nearest_monument</c>, <c>pack_site:&lt;id&gt;</c>) points for a player.</summary>
    internal Vector3f? PackRevealTargetForTest(PlayerSession s, string reveal) => PackRevealTarget(s, reveal, out _);
}
