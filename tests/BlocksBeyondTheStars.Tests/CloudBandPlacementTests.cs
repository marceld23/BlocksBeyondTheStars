// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>#3272: the visible cloud band is placed against a world's measured relief, so only the land that rises well
/// above the rest reaches into it — never the valleys of a flat or watery world (the first capture run found the band of a
/// swamp lying on its ground: the type's relief and the sea floor in the height sample both pulled it down).</summary>
public class CloudBandPlacementTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    [Theory]
    [InlineData("swamp")]
    [InlineData("river_lowlands")]
    [InlineData("varied")]
    [InlineData("jungle")]
    [InlineData("highland")]
    public void TheBand_ClearsTheSeaAndTheLand(string planetKey)
    {
        const long seed = 424242;
        const string body = "probe";
        var gen = new WorldGenerator(seed, Content);
        var planet = Content.GetPlanet(planetKey)!;
        gen.SetWorldMode(WorldConstants.CircumferenceFor(body, WorldConstants.WorldSizeClass.Planet, 0), cratered: false,
            landingPads: null, body);

        var alt = gen.SurfaceAltitudes(planet);
        int sea = gen.SeaLevel(planet);
        var (baseY, topY) = MistProfiles.CloudBand(seed, body, planet, airless: false, alt);

        Assert.True(topY > baseY + MistProfiles.MinBandThickness, $"{planetKey}: a cloudy world has a band");
        Assert.True(alt.UpperY >= alt.MedianY, $"{planetKey}: the upper tenth lies above the median");
        if (sea != int.MinValue)
        {
            Assert.True(alt.MedianY >= sea, $"{planetKey}: the sea's surface is the ground, not its floor");
            Assert.True(baseY >= sea + MistProfiles.MinBandClearance, $"{planetKey}: band base {baseY} sits on the sea ({sea})");
        }

        Assert.True(baseY >= alt.MedianY + MistProfiles.MinBandClearance, $"{planetKey}: band base {baseY} too close to the ground");
        Assert.True(baseY >= alt.UpperY + 4, $"{planetKey}: more than the upper tenth of the land stands in the band");
    }
}
