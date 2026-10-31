// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3046 — quitting the game while a cutscene was on screen threw a NullReferenceException out of
/// <c>CutsceneDirector.OnDestroy</c>: Unity had already destroyed the screen's root-level canvas, and the director's teardown
/// reset the continue button on it. Guarded as source text (the Unity scripts cannot load headless — the
/// <see cref="SeamWiringTests"/> pattern); the behaviour itself is pinned by the EditMode suite's
/// <c>CutsceneScreenTeardownEditModeTests</c>.
/// </summary>
public sealed class CutsceneTeardownWiringTests
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
    public void TheDirectorsTeardown_ResetsTheScreenOnlyThroughResetForClose()
    {
        string teardown = Body(Script("CutsceneDirector.cs"), "private void TearDownScene()");
        Assert.Contains("_screen.ResetForClose()", teardown);
        foreach (string widgetCall in new[] { "_screen.ShowContinue(", "_screen.Render(", "_screen.HideChart(", "_screen.SetHint(", "_screen.SetFlash(" })
        {
            Assert.DoesNotContain(widgetCall, teardown);
        }
    }

    [Fact]
    public void ResetForClose_TouchesNoWidget_OnceTheScreenIsNotAlive()
    {
        string screen = Script("CutsceneScreen.cs");
        Assert.Contains("public bool Alive => !_destroyed && _frame != null && _root != null;", screen);

        string reset = Body(screen, "public bool ResetForClose()");
        int guard = reset.IndexOf("if (!Alive)", StringComparison.Ordinal);
        Assert.True(guard >= 0, "ResetForClose must check Alive");
        Assert.True(guard < reset.IndexOf("ShowContinue(", StringComparison.Ordinal), "the Alive check comes before the first widget");
    }

    [Fact]
    public void AClosingScreen_WhoseCanvasIsGone_IsDroppedNotEased()
    {
        string tick = Body(Script("CutsceneDirector.cs"), "private void TickClosingScreens()");
        Assert.Contains("if (left <= 0f || !screen.Alive)", tick);
        Assert.Contains("if (a <= 0.001f || !Alive)", Body(Script("CutsceneScreen.cs"), "public void SetWorldDim(float alpha)"));
    }
}
