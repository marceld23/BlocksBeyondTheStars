// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Shared.Content;

/// <summary>The placement rules of the stamped structure kinds (#3517, <c>data/structure_types.json</c>).</summary>
public sealed partial class GameContent
{
    /// <summary>The placement rules of every stamped structure kind — the C# defaults until <see cref="SetStructureTypes"/>.</summary>
    public StructureTypeRules StructureTypes { get; private set; } = StructureTypeRules.Defaults;

    /// <summary>Replaces the placement rules (null keeps the defaults).</summary>
    public void SetStructureTypes(StructureTypeRules? rules) => StructureTypes = rules ?? StructureTypeRules.Defaults;
}
