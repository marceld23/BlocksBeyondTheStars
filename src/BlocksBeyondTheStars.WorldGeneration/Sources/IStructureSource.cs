// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Content;

namespace BlocksBeyondTheStars.WorldGeneration.Sources;

/// <summary>
/// One kind of built structure as a source (#3512): a key, where it stands, the parameters it takes and a build that
/// runs the very generator the server runs — with the same arguments the server passes for a fresh instance of a
/// default world — and hands back its voxels, modifiers and markers unchanged as a <see cref="BuiltStructure"/>.
/// The structure editor lists every source (<see cref="StructureSources.All"/>) and previews or bakes it; the server's
/// placement goes through the same builds.
/// </summary>
public interface IStructureSource
{
    /// <summary>The stable key (lower case, snake case), e.g. <c>monument</c>.</summary>
    string Key { get; }

    StructureSourceKind Kind { get; }

    /// <summary>The locale key of the source's display name: <c>ui.source.&lt;key&gt;</c>.</summary>
    string LabelKey { get; }

    /// <summary>Whether the source has an intact form besides the one that stands in a world
    /// (<see cref="StructureBuildRequest.AsInWorld"/> = false shows it).</summary>
    bool SupportsIntact { get; }

    /// <summary>The parameters, in the order the editor shows them. A choice whose options come from the content (kits,
    /// ships, professions, landmark templates) lists what <paramref name="content"/> holds.</summary>
    IReadOnlyList<StructureParam> GetParams(GameContent content);

    /// <summary>Builds the structure. Deterministic: the same request always gives the same structure.</summary>
    BuiltStructure Build(StructureBuildRequest request);
}
