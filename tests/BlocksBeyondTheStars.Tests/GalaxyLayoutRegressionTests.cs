// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Security.Cryptography;
using System.Text;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Pins the exact galaxy LAYOUT (every body's id, kind, planet type, parent, size bias, rings and
/// orbit period — everything except the display <c>Name</c>) for fixed seeds. The universe re-derives
/// from the seed on every start, so any change that shifts the body rng draw sequence would silently
/// regenerate existing players' galaxies with different planets and orphan their visited worlds (#678).
/// The expected hashes were captured BEFORE the naming rework — naming must stay display-only forever.
/// Positions (SystemX/Z) are deliberately EXCLUDED: they go through MathF.Cos/Sin, whose last bits
/// differ between Windows and Linux libm — and they derive from hashes, not from the rng stream this
/// test guards, so they add no signal (only CI flakiness).
/// </summary>
public sealed class GalaxyLayoutRegressionTests
{
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private static string LayoutFingerprint(Galaxy g)
    {
        var sb = new StringBuilder();
        foreach (var b in g.AllBodies())
        {
            sb.Append(b.Id).Append(':').Append(b.Kind).Append(':').Append(b.PlanetType).Append(':')
              .Append(b.ParentId).Append(':')
              .Append(b.SizeBias.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(':')
              .Append(b.RingSeed).Append(':')
              .Append(b.OrbitPeriodDays.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
        }

        return System.Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }

    [Theory]
    [InlineData(42, true, "E9DFC6CBEDA8618ECD1543A78CDB27CA2CF73477A751696267AE7D8FF63A620C")]
    [InlineData(42, false, "EA6CB7A9E992818F78CE9DBD975294E56EF7BF5973B2C36DB162699889860670")]
    [InlineData(7, true, "70B98375DC21D58EB5ADE34D91A998D29351A08604D837631DA01E387EE45965")]
    public void GalaxyLayout_IsByteIdentical_ToPreNamingRework(long seed, bool variance, string expected)
    {
        var desc = new WorldDescription
        {
            StarSystemCount = 40,
            PlanetsPerSystemMin = 2,
            PlanetsPerSystemMax = 6,
            MoonsPerPlanetMin = 0,
            MoonsPerPlanetMax = 2,
            SystemVariance = variance,
            SpaceStations = Frequency.Frequent,
        };
        var galaxy = new UniverseGenerator(seed, desc, _content).Generate();
        Assert.Equal(expected, LayoutFingerprint(galaxy));
    }
}
