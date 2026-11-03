// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// A landing or launching ship scares the animals off its pad (#3149). A tester pointed out that a flock sleeping over
/// his spaceport would, story-wise, be roasted the moment a ship came down on its jets; the kid-friendly version is that
/// the jets frighten them away. Every wild animal within <see cref="ShipJetScareRadius"/> of the pad — fliers included —
/// is startled, woken and flees from the pad for <see cref="ShipJetScareSeconds"/>, through the same threat/panic path a
/// hunter's shot uses (<c>ApplyThreatIntent</c>): the panic makes a passive grazer run too, where the plain hunter's
/// <c>ScareCreaturesFrom</c> moves only skittish animals. Companions, clones, giants and hostile animals are left alone,
/// and an animal that fights back when attacked does not run (the panic rule). Nobody is hurt.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>How far around the pad the jets scare animals — the panic flee range, so every startled one actually runs.</summary>
    private const float ShipJetScareRadius = CreaturePanicFleeRange;

    /// <summary>How long a scared animal keeps fleeing from the pad (and stays awake).</summary>
    private const double ShipJetScareSeconds = 12.0;

    /// <summary>Startles the wild animals of <paramref name="bodyId"/>'s world (if it is loaded) near a ship touching down at
    /// or lifting off from <paramref name="at"/>.</summary>
    private void ScareAnimalsOffPad(string bodyId, Vector3f at)
    {
        if (_worlds.Find(bodyId) is not { } world || world.Creatures.Count == 0)
        {
            return;
        }

        double radiusSq = (double)ShipJetScareRadius * ShipJetScareRadius;
        int circ = world.World.Circumference;
        foreach (var c in world.Creatures)
        {
            if (c.IsCompanion || c.CompanionId.Length > 0 || c.CloneOf.Length > 0 || c.IsGiant || c.Hostile
                || WorldConstants.WrapDistanceSquared(c.Position, at, circ) > radiusSq)
            {
                continue;
            }

            Startle(c, ShipJetScareSeconds, dropFood: true);
            c.AwakeOverrideTimer = System.Math.Max(c.AwakeOverrideTimer, ShipJetScareSeconds); // the roar wakes a sleeper
            c.ThreatPos = at;
            c.ThreatTimer = System.Math.Max(c.ThreatTimer, ShipJetScareSeconds);
        }
    }

    /// <summary>Test seam (#3149): the jets of a ship at a pad, without a landing around them.</summary>
    internal void ScareAnimalsOffPadForTest(string bodyId, Vector3f at) => ScareAnimalsOffPad(bodyId, at);
}
