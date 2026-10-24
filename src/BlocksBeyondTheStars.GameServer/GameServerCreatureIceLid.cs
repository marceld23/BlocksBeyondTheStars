// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// #2955: the surface a hovering animal rests its band on over a DEEP or FROZEN water body. A tundra world's open ocean
/// is a three-block ice sheet on fifty cells of water; the band probes followed a water body at most
/// <see cref="FluidColumnScan"/> cells up and never looked through an ice lid, so an air jellyfish over the frozen sea
/// read the middle of the ocean as its surface from under the ice and the seabed from above it — two answers more than
/// 24 blocks apart, which the hover ease snaps to, so it teleported between them every tick ("the jellyfish blink").
/// Here the water is followed to its real top, and a sheet of ice frozen onto it is what the animal comes down onto.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>How far up a water body is followed to its real surface — deeper than any generated sea.</summary>
    private const int DeepFluidScan = 256;

    /// <summary>The thickest cover of ice (and snow drifted onto it) an animal still rests on over the water: the
    /// generator's sheets are at most 4 blocks, a fixed sheet 5, and a snow cover can lie on top.</summary>
    private const int IceLidScan = 16;

    private ushort _creatureIceId, _creatureSnowId;

    /// <summary>The cell an animal comes to rest on over the fluid body that holds <paramref name="fluidY"/>: the cell
    /// above the body's real top (open water, a lily pad, the air) — or, when the body is frozen over, the first open
    /// cell above its sheet of ice. <see cref="int.MinValue"/> when the cell holds no fluid or the body reaches past the
    /// deep scan. With <paramref name="openOnly"/> a body sealed under anything but ice (a cave roof, a floor) has no
    /// such cell either, so the caller's rules for a roofed column stay in charge. Real blocks, no-load reads.</summary>
    private int FluidRestY(int x, int z, int fluidY, bool openOnly)
    {
        if (fluidY == int.MinValue || !IsFluidOrSubmergedPlant(x, fluidY, z))
        {
            return int.MinValue;
        }

        int top = fluidY;
        while (IsFluidOrSubmergedPlant(x, top + 1, z))
        {
            if (++top - fluidY >= DeepFluidScan)
            {
                return int.MinValue; // still water — no surface within reach
            }
        }

        if (TryIceLidRestY(x, z, top, out int onIce))
        {
            return onIce;
        }

        return openOnly && IsCollidingCellIfLoaded(x, top + 1, z, foliagePasses: true) ? int.MinValue : top + 1;
    }

    /// <summary>The first open cell above a sheet of ice frozen onto a water body whose top cell is
    /// <paramref name="waterTop"/> — ice, and snow drifted onto it, nothing else. False when the cell on the water is not
    /// ice, when anything else lies in the cover (a floor, a house over a frozen pond: the roofed-column rules decide
    /// there), or when it is thicker than <see cref="IceLidScan"/>.</summary>
    private bool TryIceLidRestY(int x, int z, int waterTop, out int restY)
    {
        restY = int.MinValue;
        if (_creatureIceId == 0 || _world.GetBlockIfLoaded(new Vector3i(x, waterTop + 1, z)).Value != _creatureIceId)
        {
            return false;
        }

        for (int y = waterTop + 2; y <= waterTop + 1 + IceLidScan; y++)
        {
            ushort cell = _world.GetBlockIfLoaded(new Vector3i(x, y, z)).Value;
            if (cell == _creatureIceId || (cell == _creatureSnowId && _creatureSnowId != 0))
            {
                continue;
            }

            if (IsSupportCell(x, y, z) || IsFluidOrSubmergedPlant(x, y, z))
            {
                return false; // a floor or a roof on the ice, or meltwater on the sheet — not a lid this probe can vouch for
            }

            restY = y;
            return true;
        }

        return false;
    }

    /// <summary>The fluid cell a probe from <paramref name="refY"/> stands in or looks down into: the reference itself
    /// when it is in a fluid, else the first fluid cell below it within <paramref name="maxScan"/>, never past real
    /// ground. <see cref="int.MinValue"/> when there is none.</summary>
    private int FluidCellAtOrBelow(int x, int z, int refY, int maxScan)
    {
        for (int r = 0; r <= maxScan; r++)
        {
            if (IsFluidOrSubmergedPlant(x, refY - r, z))
            {
                return refY - r;
            }

            if (IsSupportCell(x, refY - r, z))
            {
                break;
            }
        }

        return int.MinValue;
    }

    /// <summary>The reference cell an air animal spawned over a column is placed from (#2955): over a natural sea, pond
    /// or river the cell above its water — or above the ice frozen onto it — not the generator surface, which there is
    /// the seabed (the jellyfish started under the ice). Anywhere else <paramref name="surface"/> + 1, as before.</summary>
    private int AirSpawnReferenceY(int x, int z, int surface)
    {
        var planet = _world.Planet;
        if (_generator.TryGetIceSurface(planet, x, z, out int iceTop) && iceTop > surface)
        {
            return iceTop + 1;
        }

        return _generator.TryGetWaterSurface(planet, x, z, out int waterTop, out _) && waterTop > surface
            ? waterTop + 1
            : surface + 1;
    }
}
