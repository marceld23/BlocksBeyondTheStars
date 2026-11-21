// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Story;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3923/#3924 — swimming at the surface against the REAL server, and the Unity joints PR CI never compiles:
/// <list type="bullet">
/// <item>a body floating at the client's float depth (<see cref="SwimRules.FloatDepth"/>) breathes — the server takes the
/// reported position and checks the breath at the feet + 1.5 — while the old resting point drained the tank;</item>
/// <item>VEGA names the swim keys the first time the player swims, the head above the water;</item>
/// <item>the screen's liquid wash follows the rendering camera (like the underwater haze), the controller floats toward the
/// float depth, the controls line names the swim keys and the figure tilts by <see cref="SwimRules.BodyTilt"/>.</item>
/// </list>
/// </summary>
public sealed class SwimSurfaceTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir());

    private const int Cx = 240, Cz = 240, FloorY = 100;

    /// <summary>The pool's surface: water fills the cells from <see cref="FloorY"/> + 1 up to here (exclusive).</summary>
    private const float Surface = FloorY + 9;

    private static string Script(string name)
        => File.ReadAllText(Path.Combine(ClientTestPaths.RepoRoot(), "client", "Assets", "BlocksBeyondTheStars", "Scripts", name));

    /// <summary>A breathable world (jungle) without a story, past the basic training, with a deep pool far from the spawn.</summary>
    private static (ClientServerHarness Harness, PlayerSession Player) Pool()
    {
        var h = new ClientServerHarness(Content, c =>
        {
            c.WorldName = "swim_surface";
            c.StartPlanet = "jungle";
            c.Rules.StoryId = StoryRegistry.NoneStoryId;
        });
        var p = h.Server.AddLocalPlayer("Swimmer");
        h.Server.SkipTutorialForTest(p); // past the basic training: its steps hold some hints back
        h.Server.TickForTest(0.1);
        p.State.AboardShip = false;

        var water = Content.GetBlock("water")!.NumericId;
        var stone = Content.GetBlock("stone")!.NumericId;
        for (int x = Cx - 3; x <= Cx + 3; x++)
        {
            for (int z = Cz - 3; z <= Cz + 3; z++)
            {
                h.Server.World.SetBlock(new Vector3i(x, FloorY, z), stone);
                for (int y = FloorY + 1; y < (int)Surface; y++)
                {
                    h.Server.World.SetBlock(new Vector3i(x, y, z), water);
                }

                for (int y = (int)Surface; y < (int)Surface + 4; y++)
                {
                    h.Server.World.SetBlock(new Vector3i(x, y, z), BlocksBeyondTheStars.Shared.Primitives.BlockId.Air);
                }
            }
        }

        return (h, p);
    }

    private static void FloatFor(ClientServerHarness h, PlayerSession p, float feetDepth, int ticks)
    {
        var at = new Vector3f(Cx + 0.5f, Surface - feetDepth, Cz + 0.5f);
        for (int i = 0; i < ticks; i++)
        {
            p.State.Position = at; // the client owns on-foot movement: the server takes the reported position
            h.Server.TickForTest(0.5);
        }
    }

    [Fact]
    public void FloatingAtTheFloatDepth_KeepsTheTank_TheOldRestDrainedIt()
    {
        var (h, p) = Pool();
        using (h)
        {
            Assert.True(h.Server.AtmosphereBreathable, "jungle should be breathable for this test");

            p.State.Oxygen = 50f;
            FloatFor(h, p, SwimRules.FloatDepth, ticks: 6);
            Assert.True(p.State.Oxygen > 50f, $"floating with the head above the water must breathe (O2 {p.State.Oxygen})");

            p.State.Oxygen = 80f;
            FloatFor(h, p, 1.58f, ticks: 6); // the reported resting point: the breath point 8 cm under water
            Assert.True(p.State.Oxygen < 80f, $"the old float left the head under water — it drained (O2 {p.State.Oxygen})");
        }
    }

    [Fact]
    public void TheSwimHint_ComesWithTheFirstSwim_HeadAboveTheWater()
    {
        var (h, p) = Pool();
        using (h)
        {
            p.State.Position = new Vector3f(Cx + 0.5f, Surface + 2f, Cz + 0.5f); // dry, above the pool
            h.Server.TickForTest(0.5);
            Assert.DoesNotContain("vega:hint:swim", p.State.Milestones);

            FloatFor(h, p, SwimRules.FloatDepth, ticks: 2);
            Assert.Contains("vega:hint:swim", p.State.Milestones);
        }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    public void TheSwimHint_NamesTheKeysByToken(string code)
    {
        var table = VegaLineBudgetTests.Core(code);
        Assert.Contains("{key:Jump}", table["vega.hint.swim"]);
        Assert.Contains("{key:Crouch}", table["vega.hint.swim"]);
        Assert.Contains("{key:Jump}", table["ui.hud.hint_swim"]);
        Assert.Contains("{key:Crouch}", table["ui.hud.hint_swim"]);
    }

    [Fact]
    public void TheWash_FollowsTheRenderingCamera_NotTheBody()
    {
        string fx = Script("WeatherFx.cs");
        Assert.Contains("public Camera Camera;", fx);
        Assert.Contains("var eye = Camera != null ? Camera.transform.position : Game.PlayerPosition + Vector3.up * 1.6f;", fx);
        Assert.DoesNotContain("Game.PlayerPosition + Vector3.up * 1.5f", fx); // the body's head point washed a dry camera blue
        Assert.Contains("weather.Camera = cam;", Script("WorldRig.cs"));
        Assert.Contains("Game.IsWaterAt(Camera.transform.position)", Script("Sky.Mist.cs")); // the haze asks the same cell
    }

    [Fact]
    public void TheController_FloatsTowardTheFloatDepth_AndTheHudNamesTheSwimKeys()
    {
        string controller = ClientTestPaths.PlayerControllerSources();
        Assert.Contains("LiquidSurfaceAbove(transform.position) - transform.position.y", controller);
        Assert.Contains("dense ? DenseGasSinkSpeed : SwimSinkSpeed, feetDepth);", controller);
        Assert.Contains("Game.SwimmingFrame = Time.frameCount;", controller);

        string hud = Script("HudUi.HintLine.cs");
        Assert.Contains("loc.Get(\"ui.hud.hint_swim\")", hud);
        Assert.Contains("SwimRules.SwimControlsLine(hint, swim)", hud);
        Assert.Contains("new[] { swim, fly, rotate, slot }", hud);
        Assert.Contains("public bool Swimming => SwimmingFrame >= Time.frameCount - 1;", Script("GameBootstrap.PlayerState.cs"));
    }

    [Fact]
    public void TheFigure_TiltsByTheSwimRule_AboutItsChest()
    {
        string swim = Script("PlayerAvatar.SwimLevel.cs");
        Assert.Contains("SwimRules.BodyTilt(_fwdSmooth, vy)", swim);
        Assert.Contains("SwimRules.TreadBlend(_swimTilt)", swim);
        Assert.Contains("SwimRules.CrawlBlend(_swimTilt)", swim);
        Assert.Contains("transform.rotation * Quaternion.Inverse(SwimTilt)", swim); // a dive past 90° keeps its yaw
        Assert.Contains("EaseSwimTilt(dt, step, vy, lowPass);", Script("PlayerAvatar.cs"));
        Assert.Contains("PoseSwim(t, ", Script("PlayerAvatar.Feel.cs"));
        Assert.Contains("r.Go.transform.rotation = yawRot * r.Avatar.SwimTilt;", Script("RemotePlayers.cs")); // remote players tilt too
    }
}
