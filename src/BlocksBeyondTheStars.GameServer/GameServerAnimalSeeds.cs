// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.GameServer;

public sealed partial class CombatEntity
{
    /// <summary>
    /// #3972: this animal's own seed (<see cref="CoatVariants.AnimalSeedOf"/>) — a species with coat variants dresses each
    /// animal by it (<see cref="CoatVariants.Pick"/>, <see cref="CoatVariants.MaskVariant"/>). Assigned where a creature is
    /// created, from a key that names the animal: a wild animal (and every herd member) from its entity id, a pen animal from
    /// its pen and place, a village or keeper's pet from its owner (always the same pet), a companion from its saved
    /// <c>TamedCreature.AnimalSeed</c> (taming copies it there, like the size) or, for one saved before the seed existed, from
    /// its companion id. 0 only on a non-creature entity. For a species without coat variants nothing visible depends on it.
    /// </summary>
    public int AnimalSeed { get; set; }
}
