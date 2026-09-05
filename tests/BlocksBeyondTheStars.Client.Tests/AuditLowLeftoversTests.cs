// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using BlocksBeyondTheStars.Networking;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The testable pieces of the 2026-08-29 low-priority client leftovers (#1368): the repair panel's
/// singular/plural pair exists in both mandatory locales, every player-name field caps at the server's
/// join limit, and the Texts that show developer/player text render it verbatim (no rich-text tags).
/// The Unity sources are read as text (the UI layout guards' pattern) — they cannot be loaded headless.
/// </summary>
public sealed class AuditLowLeftoversTests
{
    private static string Scripts(string file)
        => Path.Combine(ClientTestPaths.RepoRoot(), "client", "Assets", "BlocksBeyondTheStars", "Scripts", file);

    private static JsonElement Locale(string code)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(ClientTestPaths.DataDir(), "locales", code + ".json")));
        return doc.RootElement.Clone();
    }

    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    public void RepairPanel_HasASingularAndAPluralBreachLine(string code)
    {
        var loc = Locale(code);
        Assert.True(loc.TryGetProperty("ui.shiprepair.cells_missing", out var many), $"{code}: plural key missing");
        Assert.True(loc.TryGetProperty("ui.shiprepair.cells_missing_one", out var one), $"{code}: singular key missing");

        // The plural takes the count; the singular is a fixed line (no "1 Hüllenzellen fehlen").
        Assert.Contains("{0}", many.GetString());
        Assert.DoesNotContain("{0}", one.GetString());
        Assert.NotEqual(string.Format(many.GetString()!, 1), one.GetString());
    }

    [Fact]
    public void PlayerNameFields_CapAtTheServersJoinLimit()
    {
        // The server truncates a join name to Protocol.MaxPlayerNameLength; every menu/prompt field that
        // edits the player's name must cap at the same, or the stored PlayerName differs from the real id.
        Assert.Equal(24, Protocol.MaxPlayerNameLength);

        string src = File.ReadAllText(Scripts("UiMainMenu.cs"));
        var nameFields = Regex.Matches(src, @"AddInput\([^;]*?\b(?:webName|natName|name|nm)\[0\] = v", RegexOptions.ExplicitCapture, System.TimeSpan.FromSeconds(5));
        Assert.True(nameFields.Count >= 5, $"expected the name fields (menu, prompt, connect dialog, reserved-name prompt), found {nameFields.Count}");
        foreach (Match m in nameFields)
        {
            // The cap must sit in the same AddInput call — i.e. before the next UiKit builder call.
            int end = src.IndexOf("UiKit.Add", m.Index + 8, System.StringComparison.Ordinal);
            string call = src.Substring(m.Index, (end < 0 ? src.Length : end) - m.Index);
            Assert.Contains("maxLength: Protocol.MaxPlayerNameLength", call);
        }
    }

    [Fact]
    public void DeveloperAndPlayerText_IsShownVerbatim()
    {
        string feedback = File.ReadAllText(Scripts("FeedbackUi.cs"));
        Assert.Contains("_replyTitle.supportRichText = false", feedback);
        Assert.Contains("t.supportRichText = false", feedback); // every thread-body chunk
        Assert.DoesNotContain("Shorten(sb.ToString()", feedback); // the old 1400-character cut is gone
        Assert.Contains("UiTextChunks.Split(", feedback); // unbounded thread text goes through the splitter

        string hud = File.ReadAllText(Scripts("HudUi.cs"));
        Assert.Contains("_toast.richText = false", hud); // TMP label since the HUD look pass (#1623): richText is the supportRichText twin
    }

    [Fact]
    public void FeedbackDialog_BailsOutWhenClosedBeforeItsFrameEnds()
    {
        // Hotkey + Esc in one frame: Close() runs before OpenRoutine resumes — the routine must not build/hold.
        string feedback = File.ReadAllText(Scripts("FeedbackUi.cs"));
        int wait = feedback.IndexOf("yield return new WaitForEndOfFrame();", System.StringComparison.Ordinal);
        int bail = feedback.IndexOf("if (!_open)", wait, System.StringComparison.Ordinal);
        int capture = feedback.IndexOf("_shotJpg = TryCaptureJpg();", wait, System.StringComparison.Ordinal);
        Assert.True(wait >= 0 && bail > wait && bail < capture, "OpenRoutine must check _open right after WaitForEndOfFrame, before capturing/building");
    }
}
