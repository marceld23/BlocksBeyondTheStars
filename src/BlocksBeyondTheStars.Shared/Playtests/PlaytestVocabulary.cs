// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace BlocksBeyondTheStars.Shared.Playtests;

/// <summary>
/// The events the menu editors report to the playtest tracker. They follow the rule of the server's game events:
/// the key names the verb, the editor rides in the argument (<c>editor:saved</c> + <c>ship</c>), so the keys never
/// grow with the number of editors. The editors run without a server, so these never touch the server's event bus.
/// </summary>
public static class PlaytestEditorEvents
{
    /// <summary>The prefix of every editor event.</summary>
    public const string Prefix = "editor:";

    /// <summary>An editor was opened. Arg: the editor.</summary>
    public const string Opened = Prefix + "opened";

    /// <summary>The work was saved. Arg: the editor.</summary>
    public const string Saved = Prefix + "saved";

    /// <summary>The work was exported. Arg: the editor.</summary>
    public const string Exported = Prefix + "exported";

    /// <summary>The work was submitted to the developers. Arg: the editor.</summary>
    public const string Submitted = Prefix + "submitted";

    /// <summary>A saved work or a template was loaded. Arg: the editor.</summary>
    public const string Loaded = Prefix + "loaded";

    /// <summary>The Ship Designer.</summary>
    public const string Ship = "ship";

    /// <summary>The Avatar Designer (its HUD-frame mode included).</summary>
    public const string Avatar = "avatar";

    /// <summary>The Station editor.</summary>
    public const string Station = "station";

    /// <summary>The Settlement editor.</summary>
    public const string Settlement = "settlement";

    /// <summary>The Texture Editor.</summary>
    public const string Texture = "texture";

    /// <summary>The Form Editor.</summary>
    public const string Form = "form";

    /// <summary>My Tools (the tool looks).</summary>
    public const string ToolLook = "toollook";

    /// <summary>Every event an editor reports.</summary>
    public static readonly IReadOnlyList<string> All = new[] { Opened, Saved, Exported, Submitted, Loaded };

    /// <summary>The editors a task can be about — the seven player editors.</summary>
    public static readonly IReadOnlyList<string> Editors = new[] { Ship, Avatar, Station, Settlement, Texture, Form, ToolLook };

    /// <summary>Whether <paramref name="key"/> is an editor event (by its prefix).</summary>
    public static bool IsEditorEvent(string? key) => key is not null && key.StartsWith(Prefix, StringComparison.Ordinal);

    /// <summary>Whether <paramref name="editor"/> is one of <see cref="Editors"/>.</summary>
    public static bool IsKnownEditor(string? editor)
    {
        foreach (string known in Editors)
        {
            if (string.Equals(known, editor, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// The start commands a prepared playtest world may run. A catalogue is fetched text, and in singleplayer the player
/// is the world's admin — so the list is fixed here and nothing else is ever sent. The names are the server's admin
/// command names (<c>AdminCommandIntent.Command</c>).
/// </summary>
public static class PlaytestWorldCommands
{
    /// <summary>Sets the world's clock. Arg: a clock time (<c>18:30</c>) or a word the server knows.</summary>
    public const string SetTime = "set_time";

    /// <summary>Forces a weather. Arg: the weather's name.</summary>
    public const string SetWeather = "set_weather";

    /// <summary>Moves the player to coordinates on the world (X, Y, Z).</summary>
    public const string TeleportToLocation = "teleport_to_location";

    /// <summary>Moves the player to a named place (<c>village1</c>, <c>pad 1</c>). Arg: the name.</summary>
    public const string TeleportToNamed = "teleport_to_named";

    /// <summary>Hands the player an item. Arg: the item key; Count: the amount.</summary>
    public const string GiveItem = "give_item";

    /// <summary>The whole list.</summary>
    public static readonly IReadOnlyList<string> All = new[] { SetTime, SetWeather, TeleportToLocation, TeleportToNamed, GiveItem };

    /// <summary>The longest text argument.</summary>
    public const int MaxArgLength = 60;

    /// <summary>Whether <paramref name="cmd"/> is on the list.</summary>
    public static bool IsAllowed(string? cmd)
    {
        foreach (string known in All)
        {
            if (string.Equals(known, cmd, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>What is wrong with <paramref name="command"/> (an English sentence for the catalogue's author), or
    /// null when it may run. The client asks this again before it sends anything.</summary>
    public static string? Problem(PlaytestWorldCommand command)
    {
        if (!IsAllowed(command.Cmd))
        {
            return $"command '{command.Cmd}' is not on the list ({string.Join(", ", All)})";
        }

        if (command.Arg.Length > MaxArgLength)
        {
            return $"command '{command.Cmd}': the argument is longer than {MaxArgLength} characters";
        }

        switch (command.Cmd)
        {
            case TeleportToLocation:
                return float.IsFinite(command.X) && float.IsFinite(command.Y) && float.IsFinite(command.Z)
                    ? null
                    : "command 'teleport_to_location' needs finite x, y and z";
            case GiveItem:
                if (!IsPlainKey(command.Arg))
                {
                    return "command 'give_item' needs an item key (a-z, 0-9, _) as its argument";
                }

                return command.Count >= 1 && command.Count <= PlaytestLimits.MaxGiveCount
                    ? null
                    : $"command 'give_item' needs a count from 1 to {PlaytestLimits.MaxGiveCount}";
            default:
                return IsPlainArgument(command.Arg) ? null : $"command '{command.Cmd}' needs a plain argument (letters, digits, space, ':', '_', '-', '.')";
        }
    }

    private static bool IsPlainKey(string text)
    {
        if (text.Length == 0)
        {
            return false;
        }

        foreach (char c in text)
        {
            if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_'))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsPlainArgument(string text)
    {
        if (text.Length == 0)
        {
            return false;
        }

        foreach (char c in text)
        {
            bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')
                      || c == ' ' || c == ':' || c == '_' || c == '-' || c == '.';
            if (!ok)
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>
/// Game versions as the catalogue names them (<c>2026.11.21</c>, CalVer) and the "is this task for my build" test.
/// A build whose version does not parse (a local <c>0.1.0-dev</c> parses, a broken string does not) sees every task.
/// </summary>
public static class PlaytestVersion
{
    /// <summary>Reads <c>major.minor.patch</c>, ignoring a suffix after <c>-</c> or <c>+</c>.</summary>
    public static bool TryParse(string? text, out (int Major, int Minor, int Patch) version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string core = text!.Trim();
        if (core.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            core = core.Substring(1);
        }

        int cut = core.IndexOfAny(new[] { '-', '+' });
        if (cut >= 0)
        {
            core = core.Substring(0, cut);
        }

        string[] parts = core.Split('.');
        if (parts.Length != 3
            || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int major)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int minor)
            || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out int patch))
        {
            return false;
        }

        version = (major, minor, patch);
        return true;
    }

    /// <summary>Whether a build of <paramref name="gameVersion"/> is shown a task with these bounds (each empty =
    /// open). A development build (a version below the first CalVer year, e.g. <c>0.1.0-dev</c>) and a version that
    /// does not parse see everything.</summary>
    public static bool InRange(string? gameVersion, string? minVersion, string? maxVersion)
    {
        if (!TryParse(gameVersion, out var game) || game.Major < 2000)
        {
            return true;
        }

        if (TryParse(minVersion, out var min) && Compare(game, min) < 0)
        {
            return false;
        }

        return !TryParse(maxVersion, out var max) || Compare(game, max) <= 0;
    }

    /// <summary>Orders two versions.</summary>
    public static int Compare((int Major, int Minor, int Patch) a, (int Major, int Minor, int Patch) b)
    {
        int byMajor = a.Major.CompareTo(b.Major);
        if (byMajor != 0)
        {
            return byMajor;
        }

        int byMinor = a.Minor.CompareTo(b.Minor);
        return byMinor != 0 ? byMinor : a.Patch.CompareTo(b.Patch);
    }
}

/// <summary>
/// Cleans a fetched catalogue text before it is kept: the game's text components read rich-text tags, and a
/// catalogue must never be able to style, size or link anything. Tags and links are removed, control characters
/// dropped (a line break stays), and the text is cut to its bound.
/// </summary>
public static class PlaytestTextCleaner
{
    /// <summary>The cleaned text, at most <paramref name="maxLength"/> characters.</summary>
    public static string Clean(string? text, int maxLength)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(Math.Min(text!.Length, maxLength));
        bool inTag = false;
        foreach (char c in text)
        {
            if (inTag)
            {
                inTag = c != '>';
                continue;
            }

            if (c == '<')
            {
                inTag = true;
                continue;
            }

            if (c == '>' || (char.IsControl(c) && c != '\n'))
            {
                continue;
            }

            sb.Append(c);
        }

        string cleaned = RemoveLinks(sb.ToString()).Trim();
        return cleaned.Length <= maxLength ? cleaned : cleaned.Substring(0, maxLength).TrimEnd();
    }

    /// <summary>Whether <paramref name="text"/> holds something <see cref="Clean"/> would remove — a tag bracket or
    /// a link. The validator reports it, so the author sees it instead of a silently shortened text.</summary>
    public static bool HasMarkupOrLink(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        return text!.IndexOf('<') >= 0 || text.IndexOf('>') >= 0 || FindLink(text, 0) >= 0;
    }

    private static string RemoveLinks(string text)
    {
        int at = FindLink(text, 0);
        if (at < 0)
        {
            return text;
        }

        var sb = new StringBuilder(text.Length);
        int from = 0;
        while (at >= 0)
        {
            sb.Append(text, from, at - from);
            int end = at;
            while (end < text.Length && !char.IsWhiteSpace(text[end]))
            {
                end++;
            }

            from = end;
            at = FindLink(text, from);
        }

        sb.Append(text, from, text.Length - from);
        return sb.ToString();
    }

    private static int FindLink(string text, int from)
    {
        int best = -1;
        foreach (string marker in new[] { "http://", "https://", "www." })
        {
            int at = text.IndexOf(marker, from, StringComparison.OrdinalIgnoreCase);
            if (at >= 0 && (best < 0 || at < best))
            {
                best = at;
            }
        }

        return best;
    }
}
