// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3149 — a landing or launching ship scares the animals off its pad (the kid-friendly answer to "the jets would roast
/// that flock"): every wild animal within the panic flee range is startled, woken and flees from the pad — a passive
/// grazer too, which a hunter on its round does not move. Companions and animals farther away are left alone. A trader's
/// touchdown with players watching does the same.
/// </summary>
public sealed class ShipJetScareTests
{
    private const int Cx = NpcLifeWorld.Cx, Cz = NpcLifeWorld.Cz;

    private static readonly Lazy<GameContent> Content = new(() => ContentLoader.LoadFromDirectory(TestPaths.DataDir()));

    private static CreatureSpecies Passive(NpcLifeWorld w)
    {
        var sp = w.Server.SpeciesRoster.First(s => s.BodyPlan == CreatureBodyPlan.Standard && s.Legs is > 0 and < 6
                                                 && !s.HasGasSac && !s.HasWings && !s.Id.StartsWith("au_", StringComparison.Ordinal));
        sp.Habitat = CreatureHabitat.Land;
        sp.Temperament = CreatureTemperament.Passive;
        sp.Activity = CreatureActivity.Cathemeral;
        sp.BegsForFood = false;
        return sp;
    }

    private static CombatEntity Spawn(NpcLifeWorld w, CreatureSpecies sp, float x, float z)
    {
        string id = w.Server.SpawnCreatureAtForTest(new Vector3f(x, w.Feet, z), sp.Id);
        return w.Server.Creatures.Single(c => c.Id == id);
    }

    [Fact]
    public void TheJets_SendAPassiveAnimalRunning_ButNotOneFarAway_OrACompanion()
    {
        using var w = new NpcLifeWorld(Content.Value, "jets_passive");
        w.Player.State.Position = new Vector3f(Cx - 20.5f, w.Feet, Cz + 20.5f);
        var sp = Passive(w);
        var near = Spawn(w, sp, Cx + 6.5f, Cz + 0.5f);
        var far = Spawn(w, sp, Cx + 30.5f, Cz + 0.5f);
        var pet = Spawn(w, sp, Cx - 4.5f, Cz + 0.5f);
        pet.OwnerId = w.Player.State.PlayerId;
        var pad = new Vector3f(Cx + 0.5f, w.Feet, Cz + 0.5f);
        float before = (float)Math.Sqrt(near.Position.DistanceSquared(pad));

        w.Server.ScareAnimalsOffPadForTest(w.Server.ActiveLocationId, pad);

        Assert.True(near.PanicTimer > 0 && near.ThreatTimer > 0, "the animal near the pad is startled");
        Assert.Equal(0, far.PanicTimer);
        Assert.Equal(0, pet.PanicTimer);

        bool fled = false;
        w.TickAt(0.5, 2.0, every: () => fled |= near.Loco.Mode == MoveMode.Flee);
        Assert.True(fled, "even a passive grazer runs from the jets");
        Assert.True(Math.Sqrt(near.Position.DistanceSquared(pad)) > before + 0.5f, "away from the pad");
    }

    [Fact]
    public void ATradersTouchdown_ScaresTheAnimalsOnItsPad()
    {
        using var w = new NpcLifeWorld(Content.Value, "jets_trader", configure: c =>
        {
            c.Rules.PlanetEnemies = AlienActivity.Off;
            c.Rules.Bandits = AlienActivity.Off;
            c.Rules.FriendlyVisitors = false;
        });
        w.FoundBase();
        w.SetBaseFocus(BaseFocus.Market);
        w.Server.ScanBaseLifeForTest();
        string body = w.Server.ActiveLocationId;
        Assert.True(w.Server.ForcePlanetTraderForTest());
        var info = w.Server.LandingPadInfoForTest(w.Server.LandedTraderPadForTest(body));

        var sp = Passive(w);
        string id = w.Server.SpawnCreatureAtForTest(new Vector3f(info.X + 5.5f, info.Y + 1, info.Z + 0.5f), sp.Id);
        var animal = w.Server.Creatures.Single(c => c.Id == id);
        Assert.Equal(0, animal.PanicTimer);

        w.Server.AdvanceUptimeForTest(21.0);
        w.Server.TickForTest(0.1); // touchdown, with the player on the body watching

        Assert.True(animal.PanicTimer > 0, "the trader's jets startle the animal beside its pad");
    }
}
