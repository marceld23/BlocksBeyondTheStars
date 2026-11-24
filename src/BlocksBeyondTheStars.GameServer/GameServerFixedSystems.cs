// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// #3969: the hand-made star systems — one mechanism for the fixed systems of <c>data/systems/</c> (Sol) and the story
/// packs' systems (#3357, <c>GameServerStoryPacks.Systems.cs</c>, a fixed system with one body). Such a system is built from
/// its definition (<see cref="FixedSystemBuilder"/>) and appended to the galaxy once, at a map spot the save pins in
/// <see cref="WorldMetadata.StampedFeatures"/> (<c>fixedsystem|&lt;id&gt;|&lt;mapX&gt;|&lt;mapY&gt;</c>, a pack's
/// <c>packsystem|…</c>); every start, where the galaxy is rebuilt from the seed, appends it again at that spot. A data
/// system appears by its rule: after the galaxy's N-th growth (a world with <c>GalaxyGrowth</c> only — an older save that
/// already grew that far gets it at its next start), or the first time a player meets its story condition (it is then per
/// player on the star map like a pack's). Like a pack's it never holds a wormhole end and never grows the galaxy.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The <see cref="WorldMetadata.StampedFeatures"/> prefix of an appended data system.</summary>
    internal const string FixedSystemFeaturePrefix = "fixedsystem|";

    /// <summary>How far out an <c>edge</c> system sits at least (map units) — a little inside the Guardian's ring.</summary>
    private const float FixedSystemEdgeDistance = 1450f;

    /// <summary>The salt of a hand-made system's map spot — <c>packsystem:</c> since the pack systems (#3357) placed theirs
    /// with it, kept so their spots never move.</summary>
    private const string FixedSystemSpotSalt = "packsystem:";

    // ---------------------------------------------------------------------------------------------
    // The shared machinery (data and pack systems).
    // ---------------------------------------------------------------------------------------------

    /// <summary>The save's record of an appended hand-made system under <paramref name="prefix"/>, or null.</summary>
    private string? SystemRecord(string prefix, string systemId)
        => _meta.StampedFeatures.FirstOrDefault(f => f.StartsWith(prefix + systemId + "|", StringComparison.Ordinal));

    /// <summary>Pins a system's map spot in the save (replacing an older record) and returns the record.</summary>
    private string PinSystemSpot(string prefix, string systemId, (float X, float Y) spot)
    {
        string record = prefix + systemId + "|"
                        + spot.X.ToString("R", CultureInfo.InvariantCulture) + "|" + spot.Y.ToString("R", CultureInfo.InvariantCulture);
        _meta.StampedFeatures.RemoveAll(f => f.StartsWith(prefix + systemId + "|", StringComparison.Ordinal));
        _meta.StampedFeatures.Add(record);
        _repo.SaveMetadata(_meta);
        return record;
    }

    /// <summary>The map spot a system record pins, or null when it cannot be read.</summary>
    private static (float X, float Y)? RecordedSpot(string record)
    {
        var parts = record.Split('|');
        return parts.Length >= 4
               && float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
               && float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float y)
            ? (x, y)
            : null;
    }

    /// <summary>Builds <paramref name="def"/> at <paramref name="spot"/> and adds it to the galaxy, unless a system of that id is
    /// there already; its bodies' fixed values join the profiles (<see cref="RegisterFixedBodies"/>). Returns the system in the
    /// galaxy (null without one).</summary>
    private StarSystem? AppendFixedSystem(FixedSystemDefinition def, StarSystemOrigin origin, (float X, float Y) spot)
    {
        if (_galaxy is null)
        {
            return null;
        }

        if (_galaxy.Systems.FirstOrDefault(s => s.Id == def.Id) is { } present)
        {
            return present;
        }

        var system = FixedSystemBuilder.Build(def, origin, spot.X, spot.Y, (key, fallback) => LocalizedOr("en", key, fallback));
        _galaxy.Systems.Add(system);
        RegisterFixedBodies(system);
        return system;
    }

    /// <summary>A name key's text in a language — <paramref name="fallback"/> when there is no key or no text for it.</summary>
    private string LocalizedOr(string localeCode, string? nameKey, string fallback)
    {
        if (!string.IsNullOrEmpty(nameKey))
        {
            string text = Localize(localeCode, nameKey!);
            if (!string.IsNullOrEmpty(text) && text != nameKey && !text.StartsWith("[", StringComparison.Ordinal))
            {
                return text;
            }
        }

        return fallback;
    }

    /// <summary>The name of a hand-made system's definition in a language: its name key's text, else its fixed name, else its id.</summary>
    private string FixedSystemName(FixedSystemDefinition def, string localeCode)
        => LocalizedOr(localeCode, def.NameKey, string.IsNullOrEmpty(def.Name) ? def.Id : def.Name);

    /// <summary>
    /// Where a hand-made system goes on the star map (seed-stable; pinned in the save at the first append). <c>edge</c>: out
    /// past every rolled star like the Guardian's, but on a bearing of its own (turned 60°–300° from the Guardian's by the id).
    /// <c>inside</c>: the emptiest of 96 seeded spots inside the stars' box. Only the rolled systems count, so a hand-made
    /// system never moves the Guardian's spot (and the Guardian never moves this one).
    /// </summary>
    internal static (float X, float Y) FixedSystemMapPosition(Galaxy galaxy, string systemId, string placement, long seed)
    {
        var stars = galaxy.Systems.Where(s => s.IsRolled).ToList();
        var home = stars.FirstOrDefault(s => s.Id == "sys0") ?? stars.FirstOrDefault();
        if (home is null)
        {
            return (-FixedSystemEdgeDistance, FixedSystemEdgeDistance);
        }

        uint hash = unchecked((uint)(BlocksBeyondTheStars.WorldGeneration.WorldGenerator.StableHash(FixedSystemSpotSalt + systemId) ^ seed));
        if (string.Equals(placement, FixedSystemPlacements.Inside, StringComparison.Ordinal) && stars.Count > 1)
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
        float distance = MathF.Max(FixedSystemEdgeDistance, farthest + 150f);
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
    // The data systems (data/systems/).
    // ---------------------------------------------------------------------------------------------

    /// <summary>The data systems this server may append: every loaded one whose id no story pack declares too (the pack's wins,
    /// as it always did).</summary>
    private IEnumerable<FixedSystemDefinition> DataFixedSystems()
        => _content.FixedSystems.Where(def => !TryGetPackSystem(def.Id, out _, out _));

    /// <summary>Whether <paramref name="planet"/> is a type only a hand-made system places — a body of a data system (Sol) or the
    /// body of a story pack's system (the Sounders' Soaniri). No rolled galaxy carries such a type, and its system joins saves
    /// of every generation as a newcomer, so the rules written for the type hold on every save, whatever generation the save
    /// was created with (<see cref="StructuresRestricted"/>).</summary>
    private bool IsHandMadeSystemType(Shared.Definitions.PlanetType planet)
        => _content.FixedSystems.Any(def => def.Bodies.Any(b => string.Equals(b.PlanetType, planet.Key, StringComparison.Ordinal)))
           || DeclaredPackSystems().Any(p => string.Equals(p.System.PlanetType, planet.Key, StringComparison.Ordinal));

    /// <summary>Whether the galaxy's growth has reached a data system's <c>afterGrowth</c> (a growing world only).</summary>
    private bool FixedSystemDueByGrowth(FixedSystemDefinition def)
        => def.Appear.AfterGrowth > 0 && _meta.Description.GalaxyGrowth && Math.Max(0, _meta.GalaxyGrownSystems) >= def.Appear.AfterGrowth;

    /// <summary>At start (after the galaxy is built): re-appends every data system the save recorded, at its pinned spot — and
    /// appends one whose growth rule an older save already met.</summary>
    private void EnsureFixedSystemsInGalaxy()
    {
        foreach (var def in DataFixedSystems())
        {
            if (SystemRecord(FixedSystemFeaturePrefix, def.Id) is { } record)
            {
                AppendFixedSystem(def, StarSystemOrigin.Fixed, RecordedSpot(record) ?? FixedSystemMapPosition(_galaxy, def.Id, def.Placement, _meta.Seed));
            }
            else if (FixedSystemDueByGrowth(def))
            {
                RevealFixedSystem(def, broadcast: false);
            }
        }
    }

    /// <summary>After a growth (<see cref="MaybeGrowGalaxy"/>, which then saves and re-sends the star map): every data system
    /// whose growth rule the galaxy now meets joins it.</summary>
    private void RevealFixedSystemsByGrowth()
    {
        foreach (var def in DataFixedSystems())
        {
            if (FixedSystemDueByGrowth(def))
            {
                RevealFixedSystem(def, broadcast: false);
            }
        }
    }

    /// <summary>Appends a data system the first time — pins its map spot in the save and (unless at start) refreshes everyone's
    /// star map. Idempotent. Returns whether the system is in the galaxy now.</summary>
    private bool RevealFixedSystem(FixedSystemDefinition def, bool broadcast = true)
    {
        if (_galaxy is null)
        {
            return false;
        }

        if (_galaxy.Systems.Any(s => s.Id == def.Id))
        {
            return true;
        }

        var spot = FixedSystemMapPosition(_galaxy, def.Id, def.Placement, _meta.Seed);
        PinSystemSpot(FixedSystemFeaturePrefix, def.Id, spot);
        AppendFixedSystem(def, StarSystemOrigin.Fixed, spot);
        _log.Info($"Fixed system '{def.Id}' joined the galaxy.");
        if (broadcast)
        {
            BroadcastStarMap();
        }

        return true;
    }

    /// <summary>The 1 Hz part of the story systems for a player (<see cref="TickPackSystemsFor"/>): a data system with a story
    /// condition the player meets joins the galaxy (the first time anyone does); its id goes to <paramref name="visible"/>.</summary>
    private void TickFixedStorySystemsFor(PlayerSession s, List<string> visible)
    {
        foreach (var def in DataFixedSystems())
        {
            if (def.Appear.VisibleIf.Count > 0 && ConditionTextsHold(def.Appear.VisibleIf, s) && RevealFixedSystem(def))
            {
                visible.Add(def.Id);
            }
        }
    }

    /// <summary>Whether a data system with a story condition is kept off this player's star map and out of their jumps — they
    /// do not meet its condition and are not in it and never were (like a pack's). A growth system is never hidden.</summary>
    private bool FixedSystemHiddenFor(PlayerSession s, string? systemId)
    {
        if (string.IsNullOrEmpty(systemId) || StarSystemIds.HasRolledPrefix(systemId)
            || _content.GetFixedSystem(systemId) is not { Appear.VisibleIf.Count: > 0 } def
            || ConditionTextsHold(def.Appear.VisibleIf, s) || s.State.KnownSystems.Contains(systemId!))
        {
            return false;
        }

        return ResolveLocationHostBody(s.CurrentLocationId)?.SystemId != systemId;
    }

    /// <summary>The star-map name of a system for this player: a hand-made system's in their language.</summary>
    private string StarMapSystemName(PlayerSession s, StarSystem sys)
        => sys.Fixed is { } def ? FixedSystemName(def, s.Locale) : sys.Name;

    /// <summary>The star-map name of a body for this player: a fixed body's name key in their language, else its name.</summary>
    private string StarMapBodyName(PlayerSession s, CelestialBody body)
        => body.Fixed is { NameKey.Length: > 0 } def ? LocalizedOr(s.Locale, def.NameKey, body.Name) : body.Name;

    // ---------------- Test seams ----------------

    /// <summary>Test seam: re-appends the recorded data systems (as a start does).</summary>
    internal void EnsureFixedSystemsForTest() => EnsureFixedSystemsInGalaxy();

    /// <summary>Test seam: the profile the server sizes a galaxy body by.</summary>
    internal BodyProfile BodyProfileForTest(CelestialBody body) => BodyProfileOf(body);

    /// <summary>Test seam: the gravity a world of this location gets.</summary>
    internal float WorldGravityForTest(string locationId, WorldConstants.WorldSizeClass sizeClass) => WorldGravityOf(locationId, sizeClass);
}
