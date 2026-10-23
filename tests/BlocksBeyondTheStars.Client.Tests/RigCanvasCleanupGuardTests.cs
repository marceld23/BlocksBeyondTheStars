// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Text.RegularExpressions;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #2936 — every canvas a rig component builds goes with the rig. <c>UiKit.CreateCanvas</c> (and the diegetic variant
/// on top of it) makes a SCENE-ROOT object, so the rig teardown on a return to the menu (<c>Destroy(_gameRoot)</c>)
/// never takes it along by itself: an open launch question stayed over the main menu with buttons that called into a
/// destroyed GameBootstrap, and every world join left hidden canvases behind (the #1789/#2303/#2925 leak class).
/// <para>The guard walks every component <c>WorldRig.Build</c> adds, and everything those components add in turn
/// (the Tab menu's screens, the trade screen), and checks each canvas they assign from <c>UiKit.Create…Canvas</c>:
/// it lands in a field (a local alone can never be destroyed), and the component's teardown — <c>OnDestroy</c>, or an
/// <c>OnDisable</c> that the teardown runs first, directly or through a method it calls — destroys that field. A
/// <c>UiPanel</c>-derived panel builds its canvas through the base class, whose OnDestroy destroys it; an
/// override must call <c>base.OnDestroy()</c>. The Unity scripts are read as text (the UI wiring guards' pattern) —
/// they cannot be loaded headless. The PlayMode twin, <c>RigCanvasCleanupPlayModeTests</c>, destroys the real
/// components.</para>
/// </summary>
public sealed class RigCanvasCleanupGuardTests
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

    private static readonly Regex ClassDeclaration = new(
        @"^[ \t]*(?:(?:public|internal|private|protected|sealed|static|abstract|partial)\s+)*class\s+(?<name>\w+)(?<head>[^{;]*)\{",
        RegexOptions.Multiline | RegexOptions.ExplicitCapture, RegexTimeout);

    private static readonly Regex PanelBase = new(@"^\s*:\s*UiPanel\b", RegexOptions.None, RegexTimeout);

    private static readonly Regex AddedComponent = new(@"AddComponent<(?<name>\w+)>", RegexOptions.ExplicitCapture, RegexTimeout);

    private static readonly Regex CanvasAssignment = new(
        @"(?<local>\bvar\s+)?\b(?<name>\w+)\s*=\s*UiKit\.Create(?:Diegetic)?Canvas\(",
        RegexOptions.ExplicitCapture, RegexTimeout);

    private static readonly Regex Call = new(@"\b(?<name>\w+)\s*\(", RegexOptions.ExplicitCapture, RegexTimeout);

    private static string ScriptsDir()
        => Path.Combine(ClientTestPaths.RepoRoot(), "client", "Assets", "BlocksBeyondTheStars", "Scripts");

    [Fact]
    public void EveryRigCanvas_IsDestroyedWithItsComponent()
    {
        var files = Directory.GetFiles(ScriptsDir(), "*.cs").OrderBy(f => f, StringComparer.Ordinal).ToList();
        var types = IndexTypes(files);
        var rigTypes = RigTypes(types);

        var failures = new List<string>();
        var checkedTypes = new List<string>();
        foreach (string type in rigTypes.OrderBy(t => t, StringComparer.Ordinal))
        {
            var (bodies, panel) = types[type];
            string text = string.Join("\n", bodies);
            if (panel && text.Contains("override void OnDestroy", StringComparison.Ordinal)
                      && !text.Contains("base.OnDestroy()", StringComparison.Ordinal))
            {
                failures.Add($"{type}: overrides UiPanel.OnDestroy without calling base.OnDestroy() — the panel's canvas outlives the rig");
            }

            var assignments = CanvasAssignment.Matches(text);
            if (assignments.Count == 0)
            {
                continue;
            }

            checkedTypes.Add(type);
            string teardown = TeardownText(text);
            foreach (Match m in assignments)
            {
                string holder = m.Groups["name"].Value;
                if (m.Groups["local"].Success)
                {
                    var stored = Regex.Match(text.Substring(m.Index),
                        $@"\b(?<field>\w+)\s*=\s*{Regex.Escape(holder)}(?:\.gameObject)?\s*;", RegexOptions.ExplicitCapture, RegexTimeout);
                    if (!stored.Success)
                    {
                        failures.Add($"{type}: keeps a canvas only in the local '{holder}', so nothing can destroy it with the rig — keep it in a field");
                        continue;
                    }

                    holder = stored.Groups["field"].Value;
                }

                string esc = Regex.Escape(holder);
                bool destroyed = Regex.IsMatch(teardown, $@"Destroy\(\s*{esc}(?:\.gameObject)?\s*\)", RegexOptions.None, RegexTimeout);
                if (!destroyed)
                {
                    failures.Add($"{type}: the canvas in '{holder}' is a scene root and is not destroyed in OnDestroy — add `if ({holder} != null) Destroy({holder}…);` after the unsubscribes");
                }
            }
        }

        // The walk must actually reach the rig's prompts — an empty list would pass anything.
        foreach (string known in new[] { "LaunchPrompt", "VendorChoicePrompt", "RespawnPrompt", "MaintenanceUi", "DisconnectScreen",
                     "BanditDemandUi", "NpcDialogUi", "DeathFx", "TouchControlsUi", "VendorTradeUI", "HudUi", "SpaceMap", "CraftingTechShipUI" })
        {
            Assert.Contains(known, checkedTypes);
        }

        Assert.True(failures.Count == 0, "Rig canvases that outlive the rig (#2936):\n" + string.Join("\n", failures));
    }

    /// <summary>Class name → the bodies of its declarations (a partial class has one per part) and whether it derives
    /// from <c>UiPanel</c>. Bodies, not files: a file may declare a second class (<c>PadTextEntryBridge</c> lives in
    /// <c>OnScreenKeyboardUi.cs</c>), and what that one adds is not the first one's business.</summary>
    private static Dictionary<string, (List<string> Bodies, bool Panel)> IndexTypes(IEnumerable<string> files)
    {
        var index = new Dictionary<string, (List<string> Bodies, bool Panel)>(StringComparer.Ordinal);
        foreach (string file in files)
        {
            string text = File.ReadAllText(file);
            foreach (Match m in ClassDeclaration.Matches(text))
            {
                string name = m.Groups["name"].Value;
                bool panel = PanelBase.IsMatch(m.Groups["head"].Value);
                if (!index.TryGetValue(name, out var entry))
                {
                    entry = (new List<string>(), false);
                }

                int open = m.Index + m.Length - 1;
                entry.Bodies.Add(text.Substring(open, BlockLength(text, open)));
                index[name] = (entry.Bodies, entry.Panel || panel);
            }
        }

        return index;
    }

    /// <summary>The script types <c>WorldRig.Build</c> adds, and everything they add in turn.</summary>
    private static HashSet<string> RigTypes(Dictionary<string, (List<string> Bodies, bool Panel)> types)
    {
        var rig = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        void Visit(string text)
        {
            foreach (Match m in AddedComponent.Matches(text))
            {
                string name = m.Groups["name"].Value;
                if (types.ContainsKey(name) && rig.Add(name))
                {
                    queue.Enqueue(name);
                }
            }
        }

        Visit(File.ReadAllText(Path.Combine(ScriptsDir(), "WorldRig.cs")));
        Assert.True(rig.Count > 40, $"expected WorldRig.Build to add the whole rig, found {rig.Count} script components");
        while (queue.Count > 0)
        {
            foreach (string body in types[queue.Dequeue()].Bodies)
            {
                Visit(body);
            }
        }

        return rig;
    }

    /// <summary>The bodies of OnDestroy and OnDisable, plus the bodies of the methods they call (one level deep) — the
    /// code that runs when the rig root is destroyed.</summary>
    private static string TeardownText(string text)
    {
        var bodies = new List<string>();
        foreach (string entry in new[] { "OnDestroy", "OnDisable" })
        {
            bodies.AddRange(MethodBodies(text, entry));
        }

        var all = new List<string>(bodies);
        foreach (string body in bodies)
        {
            foreach (Match call in Call.Matches(body))
            {
                string name = call.Groups["name"].Value;
                if (name is "Destroy" or "OnDestroy" or "OnDisable")
                {
                    continue;
                }

                all.AddRange(MethodBodies(text, name));
            }
        }

        return string.Join("\n", all);
    }

    /// <summary>The bodies of every <c>void name(…)</c> declaration in <paramref name="text"/> — a braced block or an
    /// expression body.</summary>
    private static IEnumerable<string> MethodBodies(string text, string name)
    {
        foreach (Match m in Regex.Matches(text, $@"\bvoid\s+{Regex.Escape(name)}\s*\([^)]*\)\s*(?<open>=>|\{{)",
                     RegexOptions.ExplicitCapture, RegexTimeout))
        {
            int start = m.Groups["open"].Index;
            if (m.Groups["open"].Value == "=>")
            {
                int end = text.IndexOf(';', start);
                yield return text.Substring(start, (end < 0 ? text.Length : end) - start);
                continue;
            }

            yield return text.Substring(start, BlockLength(text, start));
        }
    }

    /// <summary>The length of the braced block that opens at <paramref name="open"/>, skipping braces inside line
    /// comments and string and character literals.</summary>
    private static int BlockLength(string text, int open)
    {
        int depth = 0;
        char quote = '\0';
        for (int i = open; i < text.Length; i++)
        {
            char c = text[i];
            if (quote == '\0' && c == '/' && i + 1 < text.Length && text[i + 1] == '/')
            {
                int eol = text.IndexOf('\n', i);
                i = eol < 0 ? text.Length : eol; // a comment's "don't" is no character literal
                continue;
            }

            if (quote != '\0')
            {
                if (c == '\\')
                {
                    i++;
                }
                else if (c == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            if (c is '"' or '\'')
            {
                quote = c;
            }
            else if (c == '{')
            {
                depth++;
            }
            else if (c == '}' && --depth == 0)
            {
                return i - open + 1;
            }
        }

        return text.Length - open;
    }
}
