// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3920/#3925 — the flight view's engine picture is Unity code that PR CI never compiles. This guard reads the client
/// scripts as text (the UI wiring guards' pattern) and keeps the joints in place: the engine flame, the engine sound and
/// the THR readout follow the thrust the helm really applied (so W behind the F1 dialog or a menu fires nothing), never
/// the raw stick; and the boost's hot look is driven by the tested <c>BoostLook</c>, honours the comfort settings and
/// adds its speed lines to the space dust next to the hyperjump's.
/// </summary>
[Trait("Suite", "ClientCore")]
public sealed class FlightEngineWiringTests
{
    private static string Script(string name)
        => File.ReadAllText(Path.Combine(ClientTestPaths.RepoRoot(), "client", "Assets", "BlocksBeyondTheStars", "Scripts", name));

    [Fact]
    public void TheFlameTheSoundAndTheReadout_ReadTheAppliedThrottle_NotTheRawStick()
    {
        string view = Script("SpaceView.cs");
        Assert.DoesNotContain("Clamp01(CaptureForward())", view);
        Assert.DoesNotContain("MoveYRaw()", view);
        Assert.Contains("float throttle = _phase == Phase.Cruise ? Mathf.Clamp01(HelmThrottle) : 0f;", view);
        Assert.Contains("UpdateEngineFlame(throttle);", view);
        Assert.Contains("UpdateFlightFx(throttle);", view);

        string audio = Script("SpaceView.Audio.cs");
        Assert.DoesNotContain("CaptureForward()", audio);
        Assert.DoesNotContain("MoveYRaw()", audio);
        Assert.Contains("Mathf.Clamp01(Mathf.Abs(HelmThrottle))", audio);
        Assert.Contains("(cruise && BoostBurning)", audio); // the afterburner layer, not the meter's bare state

        string hud = Script("SpaceView.Hud.cs");
        Assert.DoesNotContain("Clamp01(InputMap.MoveY())", hud);
        Assert.Contains("int thr = Mathf.RoundToInt(Mathf.Clamp01(HelmThrottle) * 100f);", hud);
    }

    [Fact]
    public void TheAppliedThrottle_IsSetOnlyByTheCruiseStep_AfterEveryEarlyReturn()
    {
        string flight = Script("SpaceView.Flight.cs");
        int gate = flight.IndexOf("if (!FlightInputAllowed || _visitWait >= 0f)", StringComparison.Ordinal);
        int chooser = flight.IndexOf("return; // hold position while choosing", StringComparison.Ordinal);
        int stick = flight.IndexOf("fwd = CaptureForward();", StringComparison.Ordinal); // the clip recorder's throttle stays
        int autopilot = flight.IndexOf("fwd = 1f;", StringComparison.Ordinal);
        int applied = flight.IndexOf("ApplyHelmThrottle(fwd);", StringComparison.Ordinal);
        int maneuvers = flight.IndexOf("UpdateManeuvers(fwd, strafe);", StringComparison.Ordinal);
        Assert.True(gate >= 0 && chooser > gate && stick > chooser && autopilot > chooser,
            "the helm's gates must come before the stick and the autopilot set the thrust");
        Assert.True(applied > stick && applied > autopilot && maneuvers > applied,
            "the applied throttle is recorded once the thrust is decided, before the manoeuvres fly with it");
        Assert.Equal(applied, flight.LastIndexOf("ApplyHelmThrottle(", StringComparison.Ordinal));

        string fx = Script("SpaceView.EngineFx.cs");
        // Stamped with the frame: any frame the cruise step did not fly (a menu, the chooser, a spacewalk, a landing) is 0.
        Assert.Contains("private float HelmThrottle => _helmThrottleFrame == Time.frameCount ? _helmThrottle : 0f;", fx);
        Assert.Contains("_helmThrottleFrame = Time.frameCount;", fx);
        Assert.Contains("private bool BoostBurning => _boost.Active && _maneuverFrame == Time.frameCount;", fx);
        foreach (string other in new[] { "SpaceView.cs", "SpaceView.Audio.cs", "SpaceView.Hud.cs", "SpaceView.Flight.cs" })
        {
            Assert.DoesNotContain("_helmThrottle =", Script(other)); // only EngineFx writes the field
        }
    }

    [Fact]
    public void TheBoost_LooksHot_ThroughTheTestedModel_AndHonoursTheComfortSettings()
    {
        string maneuvers = Script("SpaceView.Maneuvers.cs");
        Assert.Contains("TickBoostLook(BoostBurning);", maneuvers);
        Assert.Contains("BoostIgnition();", maneuvers);
        Assert.Contains("_boostLook.Reset();", maneuvers);
        Assert.Contains("BoostGaugeColour(BoostCol)", maneuvers);
        Assert.Contains("\"ship_boost\"", maneuvers); // the one-shot sound stays, no new clip
        Assert.DoesNotContain("FxCamera.FovPunch(4f)", maneuvers); // the old hold moved into the boost look

        string fx = Script("SpaceView.EngineFx.cs");
        Assert.Contains("_boostLook.Tick(Time.deltaTime, burning);", fx);
        Assert.Contains("FxCamera.FovPunch(BoostLook.IgnitionFovDegrees);", fx);
        Assert.Contains("FxCamera.FovPunch(BoostLook.BurnFovDegrees);", fx);
        Assert.Contains("FxCamera.AddTrauma(BoostLook.IgnitionTrauma, rumble: false);", fx);
        Assert.Contains("BoostLook.IgnitionBurst(FxKit.Preset == QualityPreset.Potato, FxKit.Density)", fx);
        Assert.Contains("if (!FxKit.ReduceFlashes)", fx);
        Assert.Contains("BoostLook.BarPulse(Time.time, UiKit.ReducedMotion || FxKit.ReduceFlashes)", fx);
        Assert.Contains("BoostLook.ParticleShare(FxKit.Preset == QualityPreset.Potato, FxKit.Density)", fx);
        Assert.Contains("FxSpaceDust.FlightBoost = ", fx);
        Assert.DoesNotContain("FxCamera.Sample", fx); // the camera parts go through FxCamera, which "Camera motion" off flattens

        string dust = Script("FxSpaceDust.cs");
        Assert.Contains("public static float FlightBoost;", dust);
        Assert.Contains("Mathf.Max(warp, Mathf.Clamp01(FlightBoost))", dust);
        Assert.Contains("FlightBoost = 0f;", dust);

        string shipFx = Script("SpaceView.ShipFx.cs");
        Assert.Contains("* FxKit.Density * TrailBoost", shipFx);
        Assert.Contains("FxLights.Set(_engineLight, EngineLightColour,", shipFx);
    }
}
