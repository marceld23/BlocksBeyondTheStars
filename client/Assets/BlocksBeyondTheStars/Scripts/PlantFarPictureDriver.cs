// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>Ticks the plant far pictures once a frame (<c>PlantBodies.FarPictures.cs</c>). The chunks' plant bodies used
    /// to drive the bake, but on Low no chunk carries bodies while the far picture is the only picture there. Made by
    /// <see cref="PlantBodies.UseWorld"/> on a hidden object that outlives the scene.</summary>
    public sealed class PlantFarPictureDriver : MonoBehaviour
    {
        private void Update() => PlantBodies.TickFarPictureDriver();
    }
}
