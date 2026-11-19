// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Numerics;
using BlocksBeyondTheStars.Shared.World;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3824 on the client: a landing trader is drawn diving into its body. The numbers of the dive
/// (<see cref="NpcDescentPath"/>), the scene numbers the server shares with the flight view
/// (<see cref="SystemBodyLayout"/>) and the Unity wiring (as source text — the scripts cannot load headless, the
/// <see cref="ClientQuirksWiringTests"/> pattern). The server's side is <c>TraderDescentTests</c>.
/// </summary>
public sealed class NpcDescentTests
{
    private static string Script(string file)
        => File.ReadAllText(Path.Combine(ClientTestPaths.RepoRoot(), "client", "Assets", "BlocksBeyondTheStars", "Scripts", file));

    [Fact]
    public void TheDive_EndsOnTheSurfaceUnderTheShip()
    {
        var body = new Vector3(SystemBodyLayout.HomeX, SystemBodyLayout.HomeY, SystemBodyLayout.HomeZ);
        const float radius = 80f;
        var from = body + (Vector3.Normalize(new Vector3(0.3f, 1f, -0.2f)) * SystemBodyLayout.LandApproachDistance(radius));

        var to = NpcDescentPath.SurfacePoint(from, body, radius);
        Assert.Equal(radius, Vector3.Distance(to, body), 3);
        Assert.Equal(SystemBodyLayout.LandApproachDistance(radius) - radius, Vector3.Distance(from, to), 3); // straight down
        Assert.Equal(body + (Vector3.UnitY * radius), NpcDescentPath.SurfacePoint(body, body, radius));      // no direction: over the pole
    }

    [Fact]
    public void TheShip_FallsEverFaster_ShrinksAndIsGoneBeforeTheEnd()
    {
        var from = new Vector3(0f, 100f, 0f);
        var to = new Vector3(0f, 0f, 0f);
        Assert.Equal(from, NpcDescentPath.PositionAt(from, to, 0f));
        Assert.Equal(to, NpcDescentPath.PositionAt(from, to, 1f));
        Assert.Equal(to, NpcDescentPath.PositionAt(from, to, 3f)); // clamped
        float early = from.Y - NpcDescentPath.PositionAt(from, to, 0.25f).Y;
        float late = NpcDescentPath.PositionAt(from, to, 0.75f).Y - NpcDescentPath.PositionAt(from, to, 1f).Y;
        Assert.True(late > early * 3f, "the last quarter is much faster than the first");

        Assert.Equal(1f, NpcDescentPath.ScaleAt(0f));
        Assert.Equal(1f, NpcDescentPath.ScaleAt(0.2f), 3);
        Assert.Equal(NpcDescentPath.EndScale, NpcDescentPath.ScaleAt(1f), 3);
        for (float t = 0.05f; t <= 1f; t += 0.05f)
        {
            Assert.True(NpcDescentPath.ScaleAt(t) <= NpcDescentPath.ScaleAt(t - 0.05f) + 1e-4f, "the ship never grows on the way down");
        }

        Assert.InRange(NpcDescentPath.HideAt, 0.9f, 1f);
    }

    [Fact]
    public void TheEntryGlow_BuildsInTheAir_AndDiesInTheClouds()
    {
        Assert.Equal(0f, NpcDescentPath.HeatAt(0f));
        Assert.Equal(0f, NpcDescentPath.HeatAt(0.1f));
        Assert.True(NpcDescentPath.HeatAt(0.6f) > 0.95f, "full glow in the middle of the dive");
        Assert.Equal(0f, NpcDescentPath.HeatAt(NpcDescentPath.HideAt), 3);
        Assert.Equal(0f, NpcDescentPath.HeatAt(1f));
    }

    [Fact]
    public void TheSceneNumbers_AreTheOnesTheFlightViewDraws()
    {
        // The server flies a trader to these; the flight view must draw the body and its landing band with the same.
        string scene = Script("SpaceView.Scene.cs");
        Assert.Contains("new Vector3(SystemBodyLayout.HomeX, SystemBodyLayout.HomeY, SystemBodyLayout.HomeZ)", scene);
        Assert.Contains("OrbitDiameterFor(homeCirc) * SystemBodyLayout.HomeDiameterFactor", scene);
        Assert.Contains("=> SystemBodyLayout.OrbitDiameter(circumference);", scene);
        string view = Script("SpaceView.cs");
        Assert.Contains("KeepOutMargin = SystemBodyLayout.KeepOutMargin;", view);
        Assert.Contains("LandBand = SystemBodyLayout.LandBand;", view);

        Assert.Equal(8f + (8800 / 220f), SystemBodyLayout.OrbitDiameter(8800));
        Assert.Equal(60f + SystemBodyLayout.KeepOutMargin + (SystemBodyLayout.LandBand * 0.5f), SystemBodyLayout.LandApproachDistance(60f));
        Assert.True(SystemBodyLayout.LandApproachDistance(60f) < 60f + SystemBodyLayout.KeepOutMargin + SystemBodyLayout.LandBand,
            "the approach point lies inside the band where a player gets the land prompt");
    }

    [Fact]
    public void TheFlightView_DrawsTheDive_InsteadOfTheWarpFlash()
    {
        string combat = Script("SpaceView.Combat.cs");
        Assert.Contains($"fx.Style == \"{SvGameServer.TraderDescentStyle}\" && BeginNpcDescent(fx)", combat);

        string entities = Script("SpaceView.Entities.cs");
        Assert.Contains("PoseNpcDescent(rp.PlayerId, av, now);", entities);
        Assert.Contains("NpcDescentOver(kv.Key, now)", entities);   // a finished dive drops the avatar without the 2 s grace
        Assert.Contains("_npcDescents.Remove(id);", entities);

        string dive = Script("SpaceView.TraderDescent.cs");
        Assert.Contains("NpcDescentPath.SurfacePoint(", dive);
        Assert.Contains("NpcDescentPath.PositionAt(", dive);
        Assert.Contains("NpcDescentPath.ScaleAt(t)", dive);
        Assert.Contains("NpcDescentPath.HeatAt(t)", dive);
        Assert.Contains("want == Game.StarMap.ActiveLocationId", dive); // the launch body is listed under ""
        Assert.Contains("\"reentry_roar\"", dive);
        Assert.Contains("_npcDescents.Clear();", Script("SpaceView.cs")); // a new flight scene starts without old dives
        Assert.Contains("!av.Root.activeSelf", Script("SpaceView.Hud.cs")); // no name label over a ship in the clouds
    }
}
