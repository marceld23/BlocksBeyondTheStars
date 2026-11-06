// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Text.RegularExpressions;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// Keeps the catalogue complete: every locale key the editors' scripts name must be catalogued with its box — or stand in
/// <see cref="NotLaidOut"/> with the reason it has none — so a new editor label cannot slip past the fit test.
/// </summary>
public sealed partial class EditorTextFitTests
{
    /// <summary>The editors' scripts (and their partial files).</summary>
    private static readonly string[] EditorScripts =
    {
        "UiEditors", "TextureEditor", "StructureEditor", "ShipEditor", "AvatarEditor", "FaceEditor", "FormEditor", "ToolLookEditor",
        "LayerCanvasEditor", "ShapeEditor", "MaterialEditor", "ContentEditor", "EditorDevicePanel", "EditorFormPicker",
        "EditorLoadPicker", "EditorPaletteKit", "KitEditorPanel", "CreationSubmitDialog", "EditorSceneKit",
    };

    /// <summary>Keys the editors name that are not drawn in a box of their own — with the reason.</summary>
    private static readonly Dictionary<string, string> NotLaidOut = new(StringComparer.Ordinal)
    {
        ["ui.ed.form_auto"] = "the form picker shows it only where it fits two lines, else ui.ed.form_auto_short (EditorFormPicker.AutoCaption)",
        ["ui.shape.custom.multi_in_menu"] = "a HUD message (Game.ShowMessage), not a text of the editor's own screen",
    };

    private static readonly Regex KeyLiteral = new("\"(?<key>ui\\.[a-z0-9_.]+[a-z0-9_])\"", RegexOptions.Compiled | RegexOptions.ExplicitCapture,
        TimeSpan.FromSeconds(1));

    [Fact]
    public void EveryKeyTheEditorsName_IsCatalogued()
    {
        var catalogued = new HashSet<string>(Expand(AllSites().SelectMany(s => s.Keys)), StringComparer.Ordinal);
        var english = English;
        var missing = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string script in EditorScripts)
        {
            foreach (Match m in KeyLiteral.Matches(ClientTestPaths.ScriptPartsText(script)))
            {
                string key = m.Groups["key"].Value;
                if (english.ContainsKey(key) && !catalogued.Contains(key) && !NotLaidOut.ContainsKey(key))
                {
                    missing.Add($"{script}: {key}");
                }
            }
        }

        Assert.True(missing.Count == 0,
            "editor keys without a catalogued box — add their place to EditorTextFitTests (or to NotLaidOut with the reason):\n"
            + string.Join("\n", missing));
    }
}
