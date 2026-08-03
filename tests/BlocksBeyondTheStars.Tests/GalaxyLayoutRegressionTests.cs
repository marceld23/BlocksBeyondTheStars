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
/// Pins the exact galaxy LAYOUT (every body's id, kind, planet type, parent, size, rings, orbit and
/// position — everything except the display <c>Name</c>) for fixed seeds. The universe re-derives from
/// the seed on every start, so any change that shifts the body rng draw sequence would silently regenerate
/// existing players' galaxies with different planets and orphan their visited worlds (#678). The expected
/// hashes were captured BEFORE the naming rework — naming must stay display-only forever.
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
              .Append(b.OrbitPeriodDays.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(':')
              .Append(b.SystemX.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(':')
              .Append(b.SystemZ.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
        }

        return System.Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }

    [Theory]
    [InlineData(42, true, "EA0433FFCD7B339565E626D87173FAB55607AE89A80ED229E232E0BE5954AFF4")]
    [InlineData(42, false, "46E5FCC8E2D1CA76D349082D5E5CEAF84B153753B7789D4F6CA29212E1888486")]
    [InlineData(7, true, "994ECA29E4A977C0E95AC479F4497AB0FD806C7F24B2B474E169F04271CF6ADE")]
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
