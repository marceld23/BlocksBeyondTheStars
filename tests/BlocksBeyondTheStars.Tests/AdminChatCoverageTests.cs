// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3391: the admin commands stay reachable and documented. <c>/festival</c> and <c>/shapewipe</c> sat in the server's
/// command table for weeks without a chat case — typed in chat they went out as an ordinary chat line — and half of the
/// working commands were missing from <c>/help admin</c>. Two source guards close that gap:
/// <list type="bullet">
/// <item>every verb of <c>AdminCommandTable</c> is sent by the chat (<c>SendAdminCommand("verb"</c> in <c>ChatUi.cs</c>);</item>
/// <item>every command word of <c>ChatUi.TryAdminCommand</c> (<c>case "/word"</c>) appears in an English help line
/// (<c>ui.admin.help_*</c>, <c>ui.chat.help_*</c>) — except the few aliases below, which reach the same verb as a word the
/// help already names.</item>
/// </list>
/// </summary>
public sealed class AdminChatCoverageTests
{
    /// <summary>Second spellings of a documented command — they reach the same verb as the word the help names.</summary>
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.Ordinal)
    {
        ["/admin"] = "/help admin",
        ["/ai_mission"] = "/ai",
        ["/observe"] = "/spectate",
        ["/modus"] = "/gamemode",
        ["/schedule_restart"] = "/restart",
        ["/cancel_restart"] = "/cancelrestart",
    };

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

    private static IEnumerable<string> Captures(string text, string pattern)
        => Regex.Matches(text, pattern, RegexOptions.None, RegexTimeout).Select(m => m.Groups[1].Value);

    private static string ChatUiSource()
    {
        string path = Path.Combine(TestPaths.RepoRoot(), "client", "Assets", "BlocksBeyondTheStars", "Scripts", "ChatUi.cs");
        Assert.True(File.Exists(path), $"ChatUi.cs not found at {path} — did the chat move? Point this guard at its new home.");
        return File.ReadAllText(path);
    }

    /// <summary>The body of <c>TryAdminCommand</c> — the chat's command switch.</summary>
    private static string CommandSwitch()
    {
        string source = ChatUiSource();
        int start = source.IndexOf("private bool TryAdminCommand(", StringComparison.Ordinal);
        int end = source.IndexOf("private void AdminHelp(", StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "ChatUi.TryAdminCommand / AdminHelp not found — update this guard.");
        return source.Substring(start, end - start);
    }

    private static List<string> HelpLines()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(TestPaths.DataDir(), "locales", "en.json")),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        return doc.RootElement.EnumerateObject()
            .Where(p => p.Name.StartsWith("ui.admin.help_", StringComparison.Ordinal) || p.Name.StartsWith("ui.chat.help_", StringComparison.Ordinal))
            .Select(p => p.Value.GetString() ?? string.Empty)
            .ToList();
    }

    [Fact]
    public void EveryServerAdminVerb_IsSentByTheChat()
    {
        string source = ChatUiSource();
        var sent = new HashSet<string>(Captures(source, "SendAdminCommand\\(\"([a-z_]+)\""), StringComparer.Ordinal);

        var missing = SvGameServer.AdminCommandTableForTest().Select(kv => kv.Key).Where(v => !sent.Contains(v)).OrderBy(v => v, StringComparer.Ordinal).ToList();

        Assert.True(missing.Count == 0,
            "These server admin verbs cannot be typed in chat (no SendAdminCommand(\"verb\") in ChatUi.TryAdminCommand): "
            + string.Join(", ", missing) + ". Add a case for each, and a help line (ui.admin.help_*).");
    }

    [Fact]
    public void EveryChatCommand_IsInAHelpLine()
    {
        var words = Captures(CommandSwitch(), "case \"(/[a-z_]+)\"").Distinct(StringComparer.Ordinal).ToList();
        Assert.Contains("/festival", words); // the guard reads the switch it means to read
        var help = HelpLines();

        var missing = words
            .Where(w => !Aliases.ContainsKey(w))
            .Where(w => !help.Any(line => Regex.IsMatch(line, "(?<![\\w/])" + Regex.Escape(w) + "(?![\\w])", RegexOptions.None, RegexTimeout)))
            .ToList();

        Assert.True(missing.Count == 0,
            "These chat commands are in no help line (data/locales/en.json ui.admin.help_* / ui.chat.help_*): "
            + string.Join(", ", missing) + ". Name them in a help line (de + en, then the community languages) — or, for a "
            + "second spelling of a documented command, add it to Aliases here.");
    }

    [Fact]
    public void TheAliases_PointAtDocumentedCommands_AndStillExist()
    {
        var words = Captures(CommandSwitch(), "case \"(/[a-z_]+)\"").ToHashSet(StringComparer.Ordinal);
        var help = HelpLines();
        foreach (var (alias, documented) in Aliases)
        {
            Assert.True(words.Contains(alias), $"The alias {alias} is no chat command any more — drop it from Aliases.");
            Assert.True(help.Any(line => line.Contains(documented, StringComparison.Ordinal)),
                $"The alias {alias} stands for {documented}, which no help line names.");
        }
    }
}
