// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3087 / #3088 / #3089 — the Unity half of the interact pick, guarded as source text (the Unity scripts cannot load headless,
/// the wiring guards' pattern): the E key and the HUD's prompt line act on the ONE pick PlayerController makes
/// (<see cref="InteractResolver"/>), the crate line offers "open" only for the crate E opens, an aimed mission board opens the
/// Missions tab, and the quartermaster's "Show me the jobs" does too.
/// </summary>
public sealed class InteractWiringTests
{
    private static string Script(string file)
        => File.ReadAllText(Path.Combine(ClientTestPaths.RepoRoot(), "client", "Assets", "BlocksBeyondTheStars", "Scripts", file));

    /// <summary>The body of the member that starts at <paramref name="signature"/> (up to the next doc comment).</summary>
    private static string Body(string src, string signature)
    {
        int start = src.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"'{signature}' not found");
        int end = src.IndexOf("/// <summary>", start, StringComparison.Ordinal);
        return end < 0 ? src.Substring(start) : src.Substring(start, end - start);
    }

    [Fact]
    public void TheKey_ActsOnThePick_ThePlayerRigPublishes()
    {
        string player = ClientTestPaths.PlayerControllerSources();
        string stations = Body(player, "private void HandleStations()");
        Assert.Contains("ResolveInteraction(handDoor, doorAimed);", stations);
        Assert.Contains("RunInteraction(Game.Interaction);", stations);
        Assert.DoesNotContain("SendTalkToNpc", stations); // no second chain of its own any more

        string resolve = Body(player, "private void ResolveInteraction(");
        Assert.Contains("Game.Interaction = InteractResolver.Resolve(_facts);", resolve);
        Assert.Contains("Game.InteractionFrame = Time.frameCount;", resolve);
        Assert.Contains("case InteractKind.MissionBoard:", Body(player, "private void RunInteraction("));
        Assert.Contains("Menu?.OpenMissionBoard();", Body(player, "private void RunInteraction("));
    }

    [Fact]
    public void TheHudPrompt_NamesThePick_AndTheCrateLineOffersOpenOnlyForTheAimedCrate()
    {
        string hud = ClientTestPaths.ScriptPartsText("HudUi");
        Assert.Contains("_prompt.text = Game.SpaceViewActive ? string.Empty : OnFootPrompt(loc);", hud);

        string onFoot = Body(hud, "private string OnFootPrompt(");
        Assert.Contains("Game.InteractionFrame >= Time.frameCount - 1 ? Game.Interaction : InteractKind.None", onFoot);
        Assert.DoesNotContain("NearestUsableBeam(", hud); // the HUD no longer measures on its own

        string loot = Body(hud, "private string LootText(");
        Assert.Contains("Game.Interaction == InteractKind.Crate", loot);
        Assert.Contains("ui.hud.stash_filter_key", loot);

        Assert.Contains("\"ui.board.prompt\"", Body(hud, "private string InteractPrompt("));
    }

    [Fact]
    public void TheMissionsTab_OpensOnTheJobsOnOffer_FromTheBoardAndFromTheQuartermaster()
    {
        string menu = Body(Script("GameMenu.cs"), "public void OpenMissionBoard()");
        Assert.Contains("_ui.RequestCategory(\"available\");", menu);
        Assert.Contains("OpenAt(Tab.Missions);", menu);

        string dialog = Script("NpcDialogUi.cs");
        Assert.Contains("m.Action == NpcDialogState.ActionMissions", dialog);
        Assert.Contains("Menu?.OpenMissionBoard();", Body(dialog, "private void Close()"));
        Assert.Contains("dialogUi.Menu = menu;", Script("WorldRig.cs"));
    }

    [Fact]
    public void ABoardInTheShip_IsAimedAt_OnlyInTheOwnShip_AndWithinTheServersReach()
    {
        string board = Body(ClientTestPaths.PlayerControllerSources(), "private bool AimedMissionBoardInReach()");
        Assert.Contains("_targetShip.OwnerId == Game.LocalPlayerId && !Game.Visiting", board);
        Assert.Contains("Game.LandedShipBlockAt(", board);
        Assert.Contains("InteractionReach.MissionBoard * InteractionReach.MissionBoard", board);
    }
}
