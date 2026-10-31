// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// Justus' reports of 2026-10-30 on the client: the season look (<see cref="SeasonLook"/>, unit-tested) and the Unity wiring
/// guarded as source text (the scripts cannot load headless — the <see cref="ClientQuirksWiringTests"/> pattern): no TMP
/// closing alpha tag (#3030), the enemy portraits (#3032), the coordinates under the vitals (#3033), the companion name
/// prompt and the hidden-HUD nameplates (#3034), the name tag (#3035), the base focus row (#3036) and the season shaders
/// (#3042).
/// </summary>
public sealed class Reports20261030WiringTests
{
    private static string ScriptsDir() => Path.Combine(ClientTestPaths.RepoRoot(), "client", "Assets", "BlocksBeyondTheStars", "Scripts");

    private static string Script(string file) => File.ReadAllText(Path.Combine(ScriptsDir(), file));

    private static string Shader(string file)
        => File.ReadAllText(Path.Combine(ClientTestPaths.RepoRoot(), "client", "Assets", "BlocksBeyondTheStars", "Shaders", file));

    // ---------------- SeasonLook (#3042) ----------------

    [Fact]
    public void Autumn_TurnsTheLeaves_OverItsFirstThird_AndKeepsThem()
    {
        Assert.Equal(0f, SeasonLook.Target(PlanetSeasons.Autumn, 0f).Autumn, 3);
        Assert.Equal(1f, SeasonLook.Target(PlanetSeasons.Autumn, 0.4f).Autumn, 3);
        Assert.Equal(1f, SeasonLook.Target(PlanetSeasons.Autumn, 1f).Autumn, 3);
        Assert.Equal(0f, SeasonLook.Target(PlanetSeasons.Autumn, 0.5f).Bloom, 3);
    }

    [Fact]
    public void Winter_Pales_SpringMeltsItAndBlooms_ANoSeasonWorldStaysPlain()
    {
        Assert.Equal(1f, SeasonLook.Target(PlanetSeasons.Winter, 0.5f).Winter, 3);
        Assert.Equal(1f, SeasonLook.Target(PlanetSeasons.Spring, 0f).Winter, 3);  // the frost of winter is still there…
        Assert.Equal(0f, SeasonLook.Target(PlanetSeasons.Spring, 0.3f).Winter, 3); // …and melts in the first quarter
        Assert.Equal(1f, SeasonLook.Target(PlanetSeasons.Spring, 0.6f).Bloom, 3);
        Assert.Equal((0f, 0f, 0f), SeasonLook.Target(string.Empty, 0.5f));
    }

    [Fact]
    public void AFrost_LiesOnlyInACold_Winter()
    {
        Assert.True(SeasonLook.Frost(PlanetSeasons.Winter, -2f));
        Assert.False(SeasonLook.Frost(PlanetSeasons.Winter, 10f));
        Assert.False(SeasonLook.Frost(PlanetSeasons.Autumn, -20f));
        Assert.True(SeasonLook.Grade(PlanetSeasons.Winter).Warmth < 0f);
        Assert.True(SeasonLook.Grade(PlanetSeasons.Summer).Warmth > 0f);
    }

    // ---------------- source guards ----------------

    [Fact]
    public void NoScript_ClosesAnAlphaTag_TmpHasNone()
    {
        foreach (string file in Directory.EnumerateFiles(ScriptsDir(), "*.cs", SearchOption.AllDirectories))
        {
            Assert.DoesNotContain("</alpha>", File.ReadAllText(file), StringComparison.Ordinal); // #3030: it prints literally
        }
    }

    [Fact]
    public void TheEnemiesChapter_DrawsEachEnemyBesideItsRenderedPortrait()
    {
        string wiki = Script("WikiUI.cs");
        Assert.Contains("y = RenderEnemies(content, textW, y);", wiki);
        string rows = Script("WikiUI.Enemies.cs");
        Assert.Contains("EnemyPortraitRig.Portrait(transform, enemyKey)", rows);
        string entities = Script("WorldEntities.cs");
        Assert.Contains("hunter: e.Kind == \"AlienMonster\"", entities); // #3032: the hunter has a look of its own
    }

    [Fact]
    public void TheCoordinates_SitUnderTheVitals_AndTheToastMakesRoom()
    {
        string coords = Script("HudUi.Coordinates.cs");
        Assert.Contains("float top = VitalsBottomY + CoordsGap;", coords);
        Assert.Contains("_toastBasePos = new Vector2(", coords);
        Assert.DoesNotContain("W - 210f", coords); // no longer under the clock
    }

    [Fact]
    public void ANewCompanion_AsksForItsName_AndItsPlateHidesWithTheHud()
    {
        string entities = Script("GameBootstrap.Entities.cs");
        Assert.Contains("if (m.Success) OnCompanionTamed(m);", entities);
        Assert.Contains("\"ui.companion.name_prompt\"", entities);

        string view = Script("CreatureView.cs");
        Assert.Contains("if (!owned || GameBootstrap.HudHidden)", view);
        Assert.Contains("UpdateTagRing(e, c.NameTag && !c.Alerting, platePos, alpha);", view);
    }

    [Fact]
    public void TheNameTag_IsUsedBeforeTheGadgetBranch()
    {
        string interact = Script("PlayerController.Interact.cs");
        int tag = interact.IndexOf("if (held == NameTagItem)", StringComparison.Ordinal);
        int gadget = interact.IndexOf("hdef.Tool.Kind == BlocksBeyondTheStars.Shared.Definitions.ToolKind.Gadget", StringComparison.Ordinal);
        Assert.True(tag > 0 && gadget > tag, "the name tag is handled before a gadget would be");
        Assert.Contains("useNameTag: true", Script("PlayerController.NameTag.cs"));
    }

    [Fact]
    public void TheCoreScreen_HasTheFocusRow()
    {
        Assert.Contains("BuildFocusRow(root, cardY - h - 12f);", Script("BeaconLabelUi.Yield.cs"));
        Assert.Contains("SendBaseFocus(_yieldBody", Script("BeaconLabelUi.Focus.cs"));
    }

    [Fact]
    public void TheSeason_ReachesThePlantAndGrassShaders_EveryFrame()
    {
        Assert.Contains("float4 _Sc_Season;", Shader("FloraAccent.hlsl"));
        Assert.Contains("tint = BbtsSeasonTint(tint);", Shader("FloraAccent.hlsl"));
        string scatter = Shader("ScatterLit.shader");
        Assert.Equal(2, scatter.Split("float4 _Sc_Season;").Length - 1); // both passes
        Assert.Contains("Shader.SetGlobalVector(SeasonId, _seasonLook);", Script("Sky.Seasons.cs"));
        Assert.Contains("TickSeasonLook(worldDt);", Script("Sky.cs"));
    }
}
