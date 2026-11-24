// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.Shared.Content;

/// <summary>#3978: the planet maps of the content (<c>data/maps/&lt;key&gt;.bbmap</c>). A map is a few megabytes and only
/// the worlds of a map type need it, so the content load never reads one: the first <see cref="GetPlanetMap"/> of a key
/// reads and decodes it, and the decoded map is kept for every later call.</summary>
public sealed partial class GameContent
{
    private readonly object _planetMapLock = new();
    private readonly Dictionary<string, PlanetMap> _planetMaps = new(StringComparer.Ordinal);
    private Func<string, byte[]?> _planetMapSource = _ => null;

    /// <summary>Where the maps come from: the bytes of the <c>.bbmap</c> file of a key, or null when there is none (yet).
    /// The directory loader reads <c>data/maps</c>; a client that fetches maps on demand (the browser) plugs its own
    /// source in. Replacing the source forgets every map decoded so far.</summary>
    public void SetPlanetMapSource(Func<string, byte[]?> source)
    {
        lock (_planetMapLock)
        {
            _planetMapSource = source ?? throw new ArgumentNullException(nameof(source));
            _planetMaps.Clear();
        }
    }

    /// <summary>Hands the content the bytes of one map directly (a map fetched on demand, a test's own map): decoded now
    /// (<see cref="InvalidDataException"/> when damaged) and kept under <paramref name="key"/> — the source is not asked
    /// for it again.</summary>
    public PlanetMap RegisterPlanetMap(string key, byte[] bytes)
    {
        if (!IsPlanetMapKey(key))
        {
            throw new ArgumentException($"'{key}' is not a planet map key (lower-case letters, digits and '_').", nameof(key));
        }

        var map = PlanetMapCodec.Decode(bytes);
        lock (_planetMapLock)
        {
            _planetMaps[key] = map;
        }

        return map;
    }

    /// <summary>The planet map of <paramref name="key"/>, read and decoded on the first call (null when the source has
    /// none — a miss is not remembered, so a map fetched later is found). Throws <see cref="InvalidDataException"/> for
    /// a damaged file: a world must never be built from half a map.</summary>
    public PlanetMap? GetPlanetMap(string? key)
    {
        if (!IsPlanetMapKey(key))
        {
            return null;
        }

        lock (_planetMapLock)
        {
            if (_planetMaps.TryGetValue(key!, out var cached))
            {
                return cached;
            }

            byte[]? bytes = _planetMapSource(key!);
            if (bytes is null)
            {
                return null;
            }

            var map = PlanetMapCodec.Decode(bytes);
            _planetMaps[key!] = map;
            return map;
        }
    }

    /// <summary>Whether <paramref name="key"/> can name a map: lower-case letters, digits and underscores only (a key
    /// becomes a file name, so it never carries a path).</summary>
    public static bool IsPlanetMapKey(string? key)
    {
        if (string.IsNullOrEmpty(key) || key!.Length > 64)
        {
            return false;
        }

        foreach (char ch in key)
        {
            if (!(ch is >= 'a' and <= 'z' || ch is >= '0' and <= '9' || ch == '_'))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The map fields of a planet type that are out of range, one sentence each (content validation). Reads no
    /// map: whether the file exists is the loader's warning, not a failed load.</summary>
    internal static IEnumerable<string> PlanetMapProblems(PlanetType planet)
    {
        if (!planet.HasTerrainMap)
        {
            yield break;
        }

        if (!IsPlanetMapKey(planet.TerrainMap))
        {
            yield return $"Planet '{planet.Key}' names terrain map '{planet.TerrainMap}' — a map key is lower-case letters, digits and '_'.";
        }

        if (planet.MapLowlandMetresPerBlock < 1 || planet.MapHighlandMetresPerBlock < 1 || planet.MapOceanMetresPerBlock < 1
            || planet.MapKneeMetres < 0 || planet.MapOceanMaxDepth < 0 || planet.MapDetailAmplitude < 0 || planet.MapDetailAmplitude > 16
            || planet.MapSeaLevelY < -1000 || planet.MapSeaLevelY > 280)
        {
            yield return $"Planet '{planet.Key}' has a terrain-map height curve field out of range.";
        }
    }
}
