// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.Shared.Localization;

/// <summary>
/// The one way to turn an item key into a player-facing name (#927). A modified stack carries its
/// modifiers INSIDE the key (<c>"snow#t8fd030"</c>, see <see cref="ItemKey"/>), so looking up
/// <c>item.{key}.name</c> with the composite key misses every locale table and renders as the raw
/// bracketed key. This helper strips to the base key first and then names the modifiers as suffixes:
/// base name · glowing/dyed · form · painted. Every UI surface that shows an item name for a
/// possibly-modified key must resolve it through here — three surfaces (the hotbar caption, the
/// pickup feed and the hotbar slot-action panel) each had their own raw lookup and all three showed
/// <c>[item.snow#t8fd030.name]</c> for the same stack.
/// </summary>
public static class ItemNames
{
    /// <summary>
    /// The localized display name for an item key, modifiers included. <paramref name="customFormName"/>
    /// resolves a player-designed form index (<see cref="ShapeCode.IsCustomShape"/>) to its player-given
    /// name — the client passes its <c>CustomShapeRegistry</c> lookup; callers without one (or an id the
    /// save no longer knows) fall back to the generic form suffix, never to a raw key.
    /// </summary>
    public static string Display(Localizer localizer, string itemKey, System.Func<int, string?>? customFormName = null)
    {
        if (localizer is null || string.IsNullOrEmpty(itemKey))
        {
            return string.Empty;
        }

        var (baseKey, tint, glow) = ItemKey.Parse(itemKey);
        string name = localizer.Get($"item.{baseKey}.name");

        if (glow != 0)
        {
            name += " · " + localizer.Get("ui.color.glowing");
        }
        else if (tint != 0)
        {
            name += " · " + localizer.Get("ui.color.dyed");
        }

        int shape = ItemKey.Shape(itemKey);
        if (shape != 0)
        {
            string label = ShapeLabel(localizer, shape, customFormName);
            if (label.Length != 0)
            {
                name += " · " + label;
            }
        }

        if (ItemKey.Design(itemKey) != 0)
        {
            name += " · " + localizer.Get("ui.color.painted");
        }

        return name;
    }

    /// <summary>The localized label of a shape index: the built-in form names for the enum range, the
    /// player-given name for a registered custom form, and the generic "own form" suffix when the custom
    /// id cannot be resolved (wiped id, no registry at hand) — never a raw locale key.</summary>
    public static string ShapeLabel(Localizer localizer, int shape, System.Func<int, string?>? customFormName = null)
    {
        if (localizer is null || shape <= 0)
        {
            return string.Empty;
        }

        if (ShapeCode.IsCustomShape(shape))
        {
            string? custom = customFormName?.Invoke(shape);
            return string.IsNullOrEmpty(custom) ? localizer.Get("ui.shape.custom.section") : custom!;
        }

        string? key = BuiltInShapeKey(shape);
        return key is null ? string.Empty : localizer.Get(key);
    }

    /// <summary>Locale key of a built-in <see cref="BlockShape"/>, or null outside the enum range.
    /// The single source for this mapping — the crafting menu and the hotbar panel used to carry
    /// their own copies of it.</summary>
    public static string? BuiltInShapeKey(int shape) => (BlockShape)shape switch
    {
        BlockShape.Cube => "ui.shape.cube",
        BlockShape.Slab => "ui.shape.slab",
        BlockShape.Pyramid => "ui.shape.pyramid",
        BlockShape.Dome => "ui.shape.dome",
        BlockShape.Sphere => "ui.shape.sphere",
        BlockShape.Ramp => "ui.shape.ramp",
        BlockShape.Stairs => "ui.shape.stairs",
        BlockShape.Cone => "ui.shape.cone",
        BlockShape.Cylinder => "ui.shape.cylinder",
        BlockShape.Panel => "ui.shape.panel",
        BlockShape.Post => "ui.shape.post",
        BlockShape.Beam => "ui.shape.beam",
        BlockShape.LowRamp => "ui.shape.lowramp",
        BlockShape.QuarterCube => "ui.shape.quartercube",
        BlockShape.Table => "ui.shape.table",
        BlockShape.Chair => "ui.shape.chair",
        BlockShape.Fence => "ui.shape.fence",
        BlockShape.Sheet => "ui.shape.sheet",
        BlockShape.Pot => "ui.shape.pot",
        _ => null,
    };
}
