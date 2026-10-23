// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #2926 — small client quirks the code-health refactoring found, guarded as source text (the Unity scripts cannot load
/// headless — the <see cref="SeamWiringTests"/> pattern): the HUD's "Board (E)" prompt reads the rig's own board reach,
/// the crosshair ping is a world ray, the browser menu builds its two dialogs after every other overlay, and the notices
/// a portal refresh polls wait for the rules screen and the password/name prompt too. The menu's name write-back after a
/// "name reserved" retry has an EditMode test of its own (<c>MainMenuHierarchyEditModeTests</c>).
/// </summary>
public sealed class ClientQuirksWiringTests
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
    public void TheBoardPrompt_ReadsTheReachTheRigBoardsWith()
    {
        string prompt = Body(ClientTestPaths.ScriptPartsText("HudUi"), "private bool OwnParkedVehicleInBoardReach()");
        Assert.Contains("float boardReach = _playerRig.SpeederBoardRange;", prompt);
        Assert.DoesNotContain("3.2f", prompt); // no copied reach left to drift

        // The rig hands out the very value its on-foot E boards with.
        Assert.Contains("public float SpeederBoardRange => _vehicles.SpeederBoardRange;", Script("PlayerController.cs"));
        Assert.Contains("float bestSq = SpeederBoardRange * SpeederBoardRange;",
            Body(Script("PlayerVehicleDrive.cs"), "internal bool TryBoardNearbySpeeder()"));
    }

    [Fact]
    public void ThePing_IsAWorldRay()
    {
        string ping = Body(ClientTestPaths.PlayerControllerSources(), "private void PingAtCrosshair()");
        Assert.Contains("out var hit, 60f, WorldRayMask, QueryTriggerInteraction.Ignore)", ping);
        Assert.DoesNotContain(", ~0,", ping);
    }

    [Fact]
    public void TheBrowserMenu_BuildsItsTwoDialogsLast()
    {
        string build = Body(Script("UiMainMenu.cs"), "public static GameObject Build(AppShell shell)");
        int participate = build.IndexOf("b.BuildParticipateOverlay();", StringComparison.Ordinal);
        int newWorld = build.IndexOf("b.BuildBrowserNewWorldDialog();", StringComparison.Ordinal);
        int namePrompt = build.IndexOf("b.BuildBrowserNamePrompt();", StringComparison.Ordinal);
        int done = build.IndexOf("return canvas.gameObject;", StringComparison.Ordinal);

        Assert.True(participate >= 0 && newWorld > participate && namePrompt > newWorld && done > namePrompt,
            "the 'New world?' confirmation and the name prompt are built after the participate overlay, the last one before them");
        // …inside a browser-only block of their own: the desktop order (the EditMode golden) stays as it was.
        int guard = build.LastIndexOf("#if UNITY_WEBGL && !UNITY_EDITOR", newWorld, StringComparison.Ordinal);
        Assert.True(guard > participate, "the two dialogs sit behind the browser guard after the participate overlay");
    }

    [Fact]
    public void TheNoticesARefreshPolls_WaitForEveryPortalDialog()
    {
        string overlay = ClientTestPaths.ScriptPartsText("UiOfficialWorlds");
        Assert.Contains("private bool NoPortalDialogOpen() => _portalModal == null && _rulesModal == null && _passwordPrompt == null;", overlay);
        Assert.Contains("if (state.Ok && NoPortalDialogOpen())", Body(overlay, "private async void DoRefresh()"));
    }
}
