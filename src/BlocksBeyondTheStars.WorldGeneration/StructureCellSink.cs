// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Primitives;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// Where a structure builder (#3522) puts what it builds: the structures the server used to write straight into the world —
/// the buried vaults and the crystal-vault puzzle (<see cref="VaultBuilder"/>), the unique sites
/// (<see cref="UniqueSiteBuilder"/>), Soaniri's village, ruin city and Choir Tower (<see cref="SoaniriBuilder"/>), the
/// Guardian-core chamber (<see cref="GuardianCoreBuilder"/>) and a hunter shuttle's camp (<see cref="HunterCampBuilder"/>)
/// — emit their cells here, one by one, in the order they are written. The server's sink writes each cell into the world
/// (the very <c>SetBlock</c> the writer made before the move, so the world stays byte-identical); the structure editor's sink
/// collects them on flat ground into a <c>BuiltStructure</c>.
/// <para>Coordinates are the builder's world coordinates as it computes them (an anchor plus offsets, never wrapped):
/// the sink maps them onto its world (wrapping the seam) or its grid. A builder that decides on what a cell holds (clear only
/// what is not air, turn moss into scree) asks <see cref="Get"/>, which sees the builder's own earlier writes.</para>
/// </summary>
public interface IStructureCellSink
{
    /// <summary>Writes one cell: the block (air carves), its dye and glow (0xRRGGBB, 0 = none) and its packed shape
    /// (0 = a plain cube).</summary>
    void Set(int x, int y, int z, BlockId block, int tint, int glow, int shape);

    /// <summary>The block a cell holds now (the builder's earlier writes included).</summary>
    BlockId Get(int x, int y, int z);

    /// <summary>
    /// What stands at a cell besides its block: a loot spot (<c>loot</c>, <c>data_terminal</c>, <c>relic_cache</c>), a
    /// world-circuit cell (<c>Shared.Definitions.TemplateDevices.Marker</c> with its device data) or a robot dock
    /// (<see cref="HunterCampBuilder.DockMarker"/>), in the order the builder reaches it. A server sink acts on the markers its
    /// writer acted on at that point (and documents which); the rest are for the editor.
    /// </summary>
    void Mark(string type, int x, int y, int z, string data);
}

/// <summary>Shorthands of <see cref="IStructureCellSink"/>.</summary>
public static class StructureCellSinkExtensions
{
    /// <summary>Writes a plain cell: no dye, no glow, a plain cube.</summary>
    public static void Put(this IStructureCellSink sink, int x, int y, int z, BlockId block) => sink.Set(x, y, z, block, 0, 0, 0);

    /// <summary>A marker without data.</summary>
    public static void Mark(this IStructureCellSink sink, string type, int x, int y, int z) => sink.Mark(type, x, y, z, string.Empty);
}
