// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>The broken walls of a buried ruin of terrain generation 38 (#3516): the shared ruin pass instead of
/// <see cref="Decay"/> and the fallen roofs of <see cref="Collapse"/>.</summary>
internal sealed partial class AncientRuinBuilder
{
    /// <summary>The strength a buried ruin decays with (the masonry default of the ruin pass).</summary>
    internal const double RuinStrength = 0.5;

    /// <summary>
    /// The shared ruin pass (<see cref="StructureRuiner"/>, profile masonry, the floor as the ground row) over the composed city:
    /// crumbled wall tops, bites, shafts through the roofs, smashed glass, dead lights (the switched-off warm strip), a settle
    /// that brings down whatever is left hanging, and rubble of scrap, rusted panels and fallen rock — no flora down here.
    /// What it keeps: the entrance landing (<see cref="AncientRuinGenerator.EntranceClearance"/>), every Ressaw cell, the Ressaw
    /// blocks and everything that touches a pool (a vat's glass, a pool's rim); nothing is piled onto a pool or into a crate
    /// marker's cell. Returns the crate markers (<c>loot</c> / <c>chest</c>) whose cell is free, seated on what is left — a
    /// crate whose floor fell lies on the floor below (a marker inside a block never held a crate and is left out).
    /// </summary>
    public List<SettlementMarker> RuinPass(IReadOnlyList<SettlementMarker> markers)
    {
        var crates = new List<SettlementMarker>();
        var free = new List<Vector3i>();
        foreach (var m in markers)
        {
            var p = m.LocalPos;
            if (m.Type is "loot" or "chest" && In(p.X, p.Y, p.Z) && Get(p.X, p.Y, p.Z) == 0)
            {
                crates.Add(m);
                free.Add(m.LocalPos);
            }
        }

        var grid = VoxelGrid.Over(_w, _h, _l, _blocks, _mods, _shapes);
        grid.KeepWhere((x, y, z, b) => Protected(x, z) || (b != 0 && (b == _ressaw || b == _ressawBlock)) || (_ressaw != 0 && TouchesRessaw(x, y, z)));
        AddPoolSurfaces(free);
        var options = new RuinOptions
        {
            Overgrowth = false,
            RubblePalette = new[] { new RuinWeightedBlock("scrap_metal"), new RuinWeightedBlock("rusted_panel"), new RuinWeightedBlock("deepslate") },
            LightOffBlock = "strip_light_warm_off",
        };
        long seed = StructureDecay.Seed("gds", unchecked((long)_seedMix));
        StructureRuiner.Run(grid, _content, seed, LandmarkRules.ProfileMasonry, RuinStrength, groundRow: 0, free, "stone",
            StructureDecay.Revision, options);
        for (int i = 0; i < crates.Count; i++)
        {
            crates[i] = Drop(crates[i]);
        }

        return crates;
    }

    /// <summary>A crate marker whose floor fell drops to the next block below (its cell and every cell it falls through are
    /// free — the pass writes nothing into a marker's cell or the one above, and what fell is gone). Unlike
    /// <see cref="RuinMarkerRules.Seat(VoxelGrid, List{SettlementMarker}, int)"/> it clears nothing: the composed ruin stays
    /// exactly as the pass left it.</summary>
    private SettlementMarker Drop(SettlementMarker m)
    {
        var p = m.LocalPos;
        int y = p.Y;
        while (y > 0 && Get(p.X, y - 1, p.Z) == 0)
        {
            y--;
        }

        return y == p.Y ? m : new SettlementMarker(m.Type, new Vector3i(p.X, y, p.Z), m.DoorAxis, m.Data);
    }

    /// <summary>The cell over every Ressaw cell: handed to the pass as a marker cell, so no rubble lands on a pool.</summary>
    private void AddPoolSurfaces(List<Vector3i> free)
    {
        if (_ressaw == 0)
        {
            return;
        }

        for (int x = 0; x < _w; x++)
            for (int y = 0; y < _h - 1; y++)
                for (int z = 0; z < _l; z++)
                {
                    if (Get(x, y, z) == _ressaw)
                    {
                        free.Add(new Vector3i(x, y + 1, z));
                    }
                }
    }
}
