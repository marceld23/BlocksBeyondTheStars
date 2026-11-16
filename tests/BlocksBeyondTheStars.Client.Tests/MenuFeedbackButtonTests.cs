// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Text.Json;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3715 — the main menu's Feedback button and the one extra line of the form it opens, guarded as source text (the Unity
/// scripts cannot load headless — the UI layout guards' pattern; the menu's own layout is pinned by the EditMode golden
/// <c>MainMenuHierarchyEditModeTests</c>): the button goes through <c>FeedbackUi.OpenFromMenu</c>, that entry opens the
/// shell's form only, and the tip about the in-game key is built for the menu's form and never for the in-world one.
/// </summary>
public sealed class MenuFeedbackButtonTests
{
    private const string TipKey = "ui.feedback.menu_tip";

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
    public void TheMenuButton_OpensTheShellsForm()
    {
        string menu = Script("UiMainMenu.cs");
        Assert.Contains("_shell.L(\"ui.menu.feedback\"), FeedbackUi.OpenFromMenu, ButtonTier.Quiet, \"btn_feedback\")", Body(menu, "public void BuildBottomBar()"));

        // The entry reaches the shell's instance only — never a world's — and not over a developer reply.
        string open = Body(Script("FeedbackUi.cs"), "public static void OpenFromMenu()");
        Assert.Contains("var menu = _shellInstance;", open);
        Assert.Contains("menu._replyOpen", open);
        Assert.Contains("menu.Shell.CurrentBoot != null", open);
        Assert.Contains("!IsShellFeedbackScreen(menu.Shell.Phase)", open);
        Assert.Contains("menu.Open();", open);
    }

    [Fact]
    public void TheInGameKeyTip_IsBuiltForTheMenuFormOnly()
    {
        string feedback = Script("FeedbackUi.cs");
        string build = Body(feedback, "private void EnsureDialog()");

        // One call, behind the shell-mode check; the in-world form's layout keeps its old offsets (tip == 0).
        Assert.Contains("if (ShellMode)\n            {\n                AddMenuTip(panel, m, 62f, innerW);\n            }", build.Replace("\r\n", "\n"));
        Assert.Contains("float tip = ShellMode ? MenuTipH + 8f : 0f;", build);
        Assert.Equal(1, CountOf(feedback, "AddMenuTip(panel"));

        // The tip names the platform's key (F1, F2 in the browser), not a fixed one.
        string tip = Body(feedback, "private void AddMenuTip(");
        Assert.Contains("L(\"" + TipKey + "\").Replace(\"{feedback_key}\", HotkeyName)", tip);
    }

    [Fact]
    public void TheTip_NamesTheKeyThroughThePlaceholder_InEveryLanguage()
    {
        string locales = Path.Combine(ClientTestPaths.DataDir(), "locales");
        foreach (string file in Directory.GetFiles(locales, "*.json"))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            if (!doc.RootElement.TryGetProperty(TipKey, out var value))
            {
                continue; // a community language may trail — English fills the gap
            }

            string text = value.GetString() ?? string.Empty;
            Assert.True(text.Contains("{feedback_key}", StringComparison.Ordinal), $"{Path.GetFileName(file)}: the tip must name the key through {{feedback_key}}");
            Assert.DoesNotContain("F1", text, StringComparison.Ordinal); // the browser's key is F2
        }
    }

    private static int CountOf(string text, string needle)
    {
        int count = 0;
        for (int i = text.IndexOf(needle, StringComparison.Ordinal); i >= 0; i = text.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
