// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #2613 / #2622 — the Unity half of the wrap-seam fixes and the F1 screenshot, guarded as source text (the Unity scripts
/// cannot load headless — the UI layout guards' pattern): E and the HUD's talk prompt ask ONE seam-aware picker, the crate,
/// base-core and beacon matches go through <c>WorldConstants.SameCell</c>, the crate line / base zone / air dome / beam list
/// measure the short way round, and the F1 dialog only takes the screen over once its screenshot holds the HUD.
/// </summary>
public sealed class SeamWiringTests
{
    private static string Script(string file)
        => File.ReadAllText(Path.Combine(ClientTestPaths.RepoRoot(), "client", "Assets", "BlocksBeyondTheStars", "Scripts", file));

    /// <summary>The body of the method that starts at <paramref name="signature"/> (up to the next method's doc comment).</summary>
    private static string Body(string src, string signature)
    {
        int start = src.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"'{signature}' not found");
        int end = src.IndexOf("/// <summary>", start, StringComparison.Ordinal);
        return end < 0 ? src.Substring(start) : src.Substring(start, end - start);
    }

    [Fact]
    public void TheEKey_AndTheTalkPrompt_AskTheSamePicker()
    {
        string boot = Script("GameBootstrap.cs");
        Assert.Contains("InteractReach.NearestTalkableNpc(Npcs, PlayerPosition.x, PlayerPosition.y, PlayerPosition.z, Circumference)", boot);

        string player = Script("PlayerController.cs");
        string hud = Script("HudUi.cs");
        Assert.Contains("Game.NearestTalkableNpc()", player);
        Assert.Contains("Game.NearestTalkableNpc()", Body(hud, "private string NearestTalkableNpc()"));

        // Neither measures on its own any more — the raw, seam-blind distance is gone from both.
        Assert.DoesNotContain("new Vector3(npcs[i].X, npcs[i].Y, npcs[i].Z)", player);
        Assert.DoesNotContain("new Vector3(npcs[i].X, npcs[i].Y, npcs[i].Z)", hud);
    }

    [Fact]
    public void TheCrate_BaseCore_AndBeacon_InTheCrosshair_MatchAcrossTheSeam()
    {
        string player = Script("PlayerController.cs");
        Assert.Contains("InteractReach.CrateAtCell(Game.Containers, crateHit.x, crateHit.y, crateHit.z, Game.Circumference)", player);
        Assert.DoesNotContain("(int)c.X == crateHit.x", player);

        Assert.Contains("WorldConstants.SameCell(", Body(player, "private bool TryAimOwnedBeacon("));
        Assert.Contains("WorldConstants.SameCell(", Body(player, "private BlocksBeyondTheStars.Networking.Messages.NetBase AimedOwnedBase()"));
        Assert.DoesNotContain("Mathf.FloorToInt(b.X) == hit.x", player);
    }

    [Fact]
    public void TheCrateLine_AndTheLootKeys_AskTheSamePicker()
    {
        Assert.Contains("InteractReach.NearestContainer(", Body(Script("HudUi.cs"), "private string LootText("));
        Assert.Contains("InteractReach.NearestContainer(", Body(Script("PlayerController.cs"), "private string NearestContainerId("));
    }

    [Fact]
    public void TheBaseZone_TheAirDome_AndTheBeamList_MeasureTheShortWay()
    {
        Assert.Contains("InteractReach.InBaseZone(", Body(Script("HudUi.cs"), "private NetBase FindBaseZone()"));
        Assert.Contains("dome.position = Game.ScenePos(b.X, b.Y + 0.5f, b.Z);", Script("BaseShieldView.cs"));
        Assert.Contains("InteractReach.SeamDistanceSquared(", Script("BeamPadUi.cs"));
    }

    [Fact]
    public void TheCutsceneAnchors_AndTheAtHomeCheck_UseTheCopyNearestThePlayer()
    {
        string director = Script("CutsceneDirector.cs");
        Assert.Contains("Game.ScenePos(ship.x, ship.y, ship.z)", director);
        Assert.DoesNotContain("(Game.PlayerPosition - ship).sqrMagnitude", director);
        Assert.Contains("Game.ScenePos(tx, ty, tz)", director);
        Assert.Contains("Game.ScenePos(_duel.AnchorX, _duel.AnchorY, _duel.AnchorZ)", Script("CutsceneDirectorDuel.cs"));
        Assert.Contains("Game.ScenePos(shipAt.x, shipAt.y, shipAt.z)", Script("PrologueCinematic.cs"));
    }

    /// <summary>#2622: the HUD hides itself in its LATE update whenever a screen-level menu owner exists — so the F1 dialog
    /// registers as a HUD-level owner on the hotkey frame (control paused, HUD up) and only becomes a screen-level owner once
    /// the frame is copied. Before, every F1 screenshot since #1858 showed no crosshair, hotbar or vitals.</summary>
    [Fact]
    public void TheF1Screenshot_IsTaken_BeforeTheDialogHidesTheHud()
    {
        string hud = Script("HudUi.cs");
        Assert.Contains("private void LateUpdate()", hud);
        Assert.Contains("bool show = !Game.ScreenMenuOpen", hud);

        string boot = Script("GameBootstrap.cs");
        Assert.Contains("if (!_hudMenuOwners.Contains(owner))", Body(boot, "public bool ScreenMenuOpen"));

        string feedback = Script("FeedbackUi.cs");
        string open = Body(feedback, "public void Open()");
        Assert.Contains("Game?.SetHudMenuOwner(this, true);", open);
        Assert.DoesNotContain("SetMenuOwner(this, true)", open);
        Assert.True(open.IndexOf("SetHudMenuOwner(this, true)", StringComparison.Ordinal)
                    < open.IndexOf("StartCoroutine(OpenRoutine())", StringComparison.Ordinal));

        Assert.Contains("Game.SetMenuOwner(this, true);", Body(feedback, "private void TakeOverScreen()"));
        Assert.Contains("GpuReadback.CaptureScreen(1600, TextureFormat.RGB24, t => shot = t, TakeOverScreen)", feedback);
        Assert.Contains("Game?.SetHudMenuOwner(this, false);", Body(feedback, "private void Close()"));

        // The take-over runs the moment the frame is copied — before the readback, which lasts frames on WebGPU.
        string readback = Script("GpuReadback.cs");
        int copy = readback.IndexOf("ScreenCapture.CaptureScreenshotIntoRenderTexture(full);", StringComparison.Ordinal);
        int captured = readback.IndexOf("captured?.Invoke();", StringComparison.Ordinal);
        int read = readback.IndexOf("yield return ReadInto(source", StringComparison.Ordinal);
        Assert.True(copy > 0 && captured > copy && read > captured, "the capture callback must run between the GPU copy and the readback");
    }
}
