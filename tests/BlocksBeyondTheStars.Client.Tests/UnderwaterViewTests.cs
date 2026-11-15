// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Content;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>#3699: how far a diver sees — the world's air by default, a sea world's own clearer water, the glowing liquids'
/// short reach — and that <c>Sky.Mist</c> takes the reach from <see cref="UnderwaterView"/>.</summary>
public sealed class UnderwaterViewTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir());

    [Theory]
    [InlineData(0f, 23f)]   // the clearest air: the clearest water
    [InlineData(1f, 12.2f)] // the soupiest air
    [InlineData(0.5f, 17.6f)]
    public void WithoutAWaterView_TheAirStandsInForTheWater(float air, float far)
        => Assert.Equal(far, UnderwaterView.FarFor(LiquidLookKind.Water, 0.0, air), 2);

    [Fact]
    public void APlanetsWaterView_WinsOverTheAir_AndIsClamped()
    {
        Assert.Equal(30f, UnderwaterView.FarFor(LiquidLookKind.Water, 30.0, 0.8f));
        Assert.Equal(UnderwaterView.MaxFar, UnderwaterView.FarFor(LiquidLookKind.Water, 500.0, 0.2f));
        Assert.Equal(UnderwaterView.MinFar, UnderwaterView.FarFor(LiquidLookKind.Water, 0.5, 0.2f));
    }

    [Theory]
    [InlineData(LiquidLookKind.Ressaw)]
    [InlineData(LiquidLookKind.RedFluid)]
    public void TheGlowingLiquids_KeepTheirShortView_OnASeaWorldToo(LiquidLookKind look)
        => Assert.Equal(UnderwaterView.GlowingFar, UnderwaterView.FarFor(look, 30.0, 0.2f));

    [Theory]
    [InlineData("ocean")]
    [InlineData("rainbow_sea")]
    public void TheSeaWorlds_SeeThirtyMetres(string planet)
        => Assert.Equal(30.0, Content.GetPlanet(planet)!.WaterView);

    [Fact]
    public void AWorldWithoutAWaterView_KeepsTheOldFormula()
        => Assert.Equal(0.0, Content.GetPlanet("meadowlands")!.WaterView);

    [Fact]
    public void SkyMist_TakesTheReachFromUnderwaterView()
    {
        string mist = File.ReadAllText(Path.Combine(ClientTestPaths.RepoRoot(), "client", "Assets", "BlocksBeyondTheStars", "Scripts", "Sky.Mist.cs"));
        Assert.Contains("float waterFar = UnderwaterView.FarFor(_murkLook, PlanetWaterView(), airDensity);", mist);
        Assert.Contains("UnderwaterReach = waterFar;", mist);
    }
}
