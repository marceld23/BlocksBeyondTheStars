// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3919 — a lift-off from a planet's first moon threw the ship far away and left it drifting backwards: the moon's
/// flight scene drew its planet right over the launch column, and the keep-out push became the velocity. The layout rule
/// and the speed clamp are tested in <c>SystemBodyLayoutTests</c> and <c>MovementFeelTests</c>; this guard reads the Unity
/// scripts as text (PR CI never compiles them) and keeps them wired: the flight scene places a moon's planet with
/// <c>SystemBodyLayout.HomeParentPosition</c> and reserves the launch moon's orbit slot before its siblings are laid out,
/// and the settled flight velocity goes through <c>FlightFeel.SettledSpeedScale</c>.
/// </summary>
[Trait("Suite", "ClientCore")]
public sealed class MoonLaunchWiringTests
{
    private static string Script(string name)
        => File.ReadAllText(Path.Combine(ClientTestPaths.RepoRoot(), "client", "Assets", "BlocksBeyondTheStars", "Scripts", name));

    [Fact]
    public void AMoonsFlightScene_PlacesItsPlanetByTheSharedRule_AndItsSiblingsAboveTheLaunchMoon()
    {
        string scene = Script("SpaceView.Scene.cs");
        int ladder = scene.IndexOf("var moonLadder = new Dictionary<int, (float Orbit, float Radius)>();", StringComparison.Ordinal);
        int parent = scene.IndexOf("SystemBodyLayout.HomeParentPosition(pos.x, pos.z, diameter * 0.5f, homeTrueRadius)", StringComparison.Ordinal);
        int slot = scene.IndexOf("moonLadder[planets.Count] = (pos.magnitude, homeTrueRadius);", StringComparison.Ordinal);
        int moons = scene.IndexOf("SystemBodyLayout.NextMoonOrbit(parent.Radius, moonDia * 0.5f, last.Orbit, last.Radius)", StringComparison.Ordinal);
        Assert.True(ladder >= 0 && parent > ladder && slot > parent && moons > slot,
            "the planet of a launch moon is placed and the launch moon's slot reserved before the moons are laid out");
        Assert.Contains("current.Kind == \"Moon\" && !string.IsNullOrEmpty(current.ParentId) ? current.ParentId : null", scene);
        Assert.Contains("SystemBodyLayout.SeparateXZ(", scene); // the relax pass that also holds the launch column clear
    }

    [Fact]
    public void APushOutOfABody_OnlyEverTakesSpeedAway()
    {
        string feel = Script("SpaceView.Feel.cs");
        int settle = feel.IndexOf("_flightVel = (to - from) / dt;", StringComparison.Ordinal);
        int roll = feel.IndexOf("SettleRollSlide(dt);", StringComparison.Ordinal);
        int clamp = feel.IndexOf("_flightVel *= FlightFeel.SettledSpeedScale(_flightVel.magnitude, _thrustVel.magnitude);", StringComparison.Ordinal);
        Assert.True(settle >= 0 && roll > settle && clamp > roll, "the settled velocity is held to the flown speed last");
        Assert.Contains("SettleFlightVelocity(_ship.transform.localPosition, pos);", Script("SpaceView.Flight.cs"));
    }
}
