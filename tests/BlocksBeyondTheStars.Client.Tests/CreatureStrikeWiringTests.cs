// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3750 (Justus, 2026-11-17: "the Soundraks do no damage when they attack me"): the Soundrak strikes in chunks like the
/// Schuppenhund, and the client shows a chunk biter's attack — the lunge, the attack call, the red swipe — exactly when a
/// strike lands. The rule is shared (<see cref="CreatureReach.LungesOnHit"/>); the Unity wiring is guarded as source text
/// (the scripts cannot load headless — the <see cref="ClientQuirksWiringTests"/> pattern).
/// </summary>
public sealed class CreatureStrikeWiringTests
{
    private static string Script(string file)
        => File.ReadAllText(Path.Combine(ClientTestPaths.RepoRoot(), "client", "Assets", "BlocksBeyondTheStars", "Scripts", file));

    [Fact]
    public void TheHoundAndTheSoundrak_LungeOnAHit_EveryDrainingHunterOnItsCadence()
    {
        Assert.True(CreatureReach.LungesOnHit(nameof(CreatureBodyPlan.Hound)));
        Assert.True(CreatureReach.LungesOnHit(nameof(CreatureBodyPlan.Soundrak)));
        Assert.False(CreatureReach.LungesOnHit(nameof(CreatureBodyPlan.Soundi)));
        Assert.False(CreatureReach.LungesOnHit(nameof(CreatureBodyPlan.Biped)));
        Assert.False(CreatureReach.LungesOnHit(string.Empty));
        Assert.False(CreatureReach.LungesOnHit(null));
    }

    [Fact]
    public void TheCreatureView_AsksTheSharedRule_NotABodyPlanOfItsOwn()
    {
        string view = Script("CreatureView.cs");
        Assert.Contains("bool chunkBiter = CreatureReach.LungesOnHit(c.BodyPlan);", view);
        Assert.DoesNotContain("chunkBiter = c.BodyPlan ==", view);
    }
}
