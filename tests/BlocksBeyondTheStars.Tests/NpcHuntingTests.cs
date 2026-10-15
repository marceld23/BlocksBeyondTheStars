// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The hunter's work core (#2645): a hunter only ever takes a wild, common, calm land animal in sight, never next to a
/// player or with one in the line of fire; the shot sends the arrow and the bow's sound, the animal breaks into sparkles
/// like a sentry's kill, its loot goes to the hunter's store and nobody is credited; its kin run from the HUNTER, and a
/// skittish animal runs from a hunter coming near.
/// <para>The rocky test world rolls no calm land walker, so each test turns one of its standard-plan walkers into one (the
/// species object is the world's own; nothing outside the test server sees it).</para>
/// </summary>
public sealed class NpcHuntingTests
{
    private const int Cx = NpcLifeWorld.Cx, Cz = NpcLifeWorld.Cz;

    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private static CreatureSpecies Huntable(NpcLifeWorld w, CreatureTemperament temperament = CreatureTemperament.Passive)
    {
        var sp = w.Server.SpeciesRoster.First(s => s.BodyPlan == CreatureBodyPlan.Standard && s.Legs is > 0 and < 6
                                                 && !s.HasGasSac && !s.HasWings && !s.Id.StartsWith("au_", StringComparison.Ordinal));
        sp.Habitat = CreatureHabitat.Land;
        sp.Temperament = temperament;
        sp.Activity = CreatureActivity.Cathemeral; // awake whatever the hour
        sp.Glows = false;
        sp.Heads = 1;
        sp.BegsForFood = false;
        Assert.Equal(MotionClass.Walker, CreatureMotion.ClassOf(sp));
        return sp;
    }

    private static CombatEntity Spawn(NpcLifeWorld w, CreatureSpecies sp, float x, float z)
    {
        string id = w.Server.SpawnCreatureAtForTest(new Vector3f(x, w.Feet, z), sp.Id);
        return w.Server.Creatures.Single(c => c.Id == id);
    }

    private static int Hunter(NpcLifeWorld w, float x, float z) => w.Server.AddNpcForTest(new Vector3f(x, w.Feet, z));

    private static void PlayerAt(NpcLifeWorld w, float x, float z) => w.Player.State.Position = new Vector3f(x, w.Feet, z);

    // ---------------- who may be hunted ----------------

    [Fact]
    public void TheHunter_PicksTheNearestCalmLandAnimal_InSight()
    {
        using var w = new NpcLifeWorld(_content, "hunt_pick");
        var sp = Huntable(w);
        int hunter = Hunter(w, Cx + 0.5f, Cz + 0.5f);
        PlayerAt(w, Cx - 18.5f, Cz + 18.5f);
        var far = Spawn(w, sp, Cx + 12.5f, Cz + 0.5f);
        var near = Spawn(w, sp, Cx + 0.5f, Cz + 8.5f);

        Assert.Equal(near.Id, w.Server.FindNpcHuntTargetForTest(hunter, 20f));
        Assert.Null(w.Server.FindNpcHuntTargetForTest(hunter, 6f)); // nothing within six blocks
        _ = far;
    }

    [Fact]
    public void TheHunter_NeverPicksACompanionAPetAGiantACloneOrABeggingCalledFrozenOrHostileAnimal()
    {
        using var w = new NpcLifeWorld(_content, "hunt_never");
        var sp = Huntable(w);
        int hunter = Hunter(w, Cx + 0.5f, Cz + 0.5f);
        PlayerAt(w, Cx - 18.5f, Cz + 18.5f);

        var cases = new List<(string What, Action<CombatEntity> Make)>
        {
            ("a player's companion", c => c.OwnerId = w.Player.State.PlayerId),
            ("a tamer's pet", c => c.OwnerId = SvGameServer.NpcPetOwnerPrefix + "7"),
            ("a giant", c => c.Giant = new GiantRuntime()),
            ("a clone", c => c.CloneOf = "1,2,3"),
            ("a begging animal", c => c.BegPhase = BegPhase.Beg),
            ("a called animal", c => c.BegPhase = BegPhase.Called),
            ("an animal in stasis", c => c.FrozenTimer = 30),
            ("a hostile animal", c => c.Hostile = true),
        };
        var banked = new List<ItemAmount>();
        for (int i = 0; i < cases.Count; i++)
        {
            double a = i * Math.PI * 2 / cases.Count;
            var c = Spawn(w, sp, Cx + 0.5f + (float)(Math.Cos(a) * 7), Cz + 0.5f + (float)(Math.Sin(a) * 7));
            cases[i].Make(c);
            Assert.False(w.Server.NpcShootCreatureForTest(hunter, c.Id, banked), cases[i].What);
        }

        Assert.Null(w.Server.FindNpcHuntTargetForTest(hunter, 20f));

        // A plain animal further out is the one it takes — the nearer ones were skipped, not missed.
        var plain = Spawn(w, sp, Cx + 0.5f, Cz - 11.5f);
        Assert.Equal(plain.Id, w.Server.FindNpcHuntTargetForTest(hunter, 20f));

        // Not of a huntable kind: a titan, a hunter, a territorial animal, a water species.
        foreach (var (what, make, undo) in new (string, Action, Action)[]
                 {
                     ("a titan", () => sp.BodyPlan = CreatureBodyPlan.Titan, () => sp.BodyPlan = CreatureBodyPlan.Standard),
                     ("an aggressive species", () => sp.Temperament = CreatureTemperament.Aggressive, () => sp.Temperament = CreatureTemperament.Passive),
                     ("a territorial species", () => sp.Temperament = CreatureTemperament.Territorial, () => sp.Temperament = CreatureTemperament.Passive),
                     ("a water species", () => sp.Habitat = CreatureHabitat.Water, () => sp.Habitat = CreatureHabitat.Land),
                 })
        {
            make();
            Assert.True(w.Server.FindNpcHuntTargetForTest(hunter, 20f) is null, what);
            undo();
        }

        Assert.Empty(banked);
    }

    [Fact]
    public void TheHunter_NeverShootsNextToAPlayer_ThroughOne_OrWithoutSight()
    {
        using var w = new NpcLifeWorld(_content, "hunt_safe");
        var sp = Huntable(w);
        int hunter = Hunter(w, Cx + 0.5f, Cz + 0.5f);
        var animal = Spawn(w, sp, Cx + 8.5f, Cz + 0.5f);
        var banked = new List<ItemAmount>();

        PlayerAt(w, Cx + 11.5f, Cz + 0.5f); // three blocks from the animal
        Assert.Null(w.Server.FindNpcHuntTargetForTest(hunter, 20f));
        Assert.False(w.Server.NpcShootCreatureForTest(hunter, animal.Id, banked));

        PlayerAt(w, Cx + 4.5f, Cz + 0.5f); // in the line of fire, between bow and animal
        Assert.Null(w.Server.FindNpcHuntTargetForTest(hunter, 20f));

        PlayerAt(w, Cx + 4.5f, Cz + 6.5f); // well beside the line
        Assert.Equal(animal.Id, w.Server.FindNpcHuntTargetForTest(hunter, 20f));

        // A wall between them: no sight, no shot.
        for (int dz = -2; dz <= 2; dz++)
            for (int dy = 0; dy < 3; dy++)
            {
                w.Set(Cx + 4, w.Feet + dy, Cz + dz, "stone");
            }

        Assert.Null(w.Server.FindNpcHuntTargetForTest(hunter, 20f));
        Assert.False(w.Server.NpcShootCreatureForTest(hunter, animal.Id, banked));
        Assert.Contains(w.Server.Creatures, c => c.Id == animal.Id);
        Assert.Empty(banked);
    }

    [Fact]
    public void TheHunter_NeverTakesTheAnimalAPlayerIsTaming()
    {
        using var w = new NpcLifeWorld(_content, "hunt_taming");
        var sp = Huntable(w);
        int hunter = Hunter(w, Cx + 0.5f, Cz + 0.5f);
        var animal = Spawn(w, sp, Cx + 9.5f, Cz + 0.5f);
        PlayerAt(w, Cx + 9.5f, Cz + 5.5f); // five blocks off: no longer "next to it", but busy taming it
        w.Player.State.Inventory.Add("creature_translator", 1, 1);
        w.Player.State.SuitEnergy = 100f;
        Assert.Equal(animal.Id, w.Server.FindNpcHuntTargetForTest(hunter, 20f));

        w.Server.TameDecodeForTest(w.Player.State.PlayerId);
        Assert.Equal(animal.Id, w.Server.TameTargetForTest(w.Player.State.PlayerId));

        Assert.Null(w.Server.FindNpcHuntTargetForTest(hunter, 20f));
        Assert.False(w.Server.NpcShootCreatureForTest(hunter, animal.Id, new List<ItemAmount>()));
    }

    // ---------------- the shot ----------------

    [Fact]
    public void AShot_RemovesTheAnimalWithTheSparkle_SendsTheArrowAndTheBow_AndBanksItsLoot()
    {
        using var w = new NpcLifeWorld(_content, "hunt_shot");
        var sp = Huntable(w);
        int hunter = Hunter(w, Cx + 0.5f, Cz + 0.5f);
        PlayerAt(w, Cx - 9.5f, Cz + 0.5f); // behind the hunter: sees the arrow, hears the bow
        var animal = Spawn(w, sp, Cx + 10.5f, Cz + 0.5f);
        var loot = animal.Loot.Select(l => (l.Item, l.Count)).ToArray();
        Assert.NotEmpty(loot);
        w.Transport.Sent.Clear();
        var banked = new List<ItemAmount>();

        Assert.True(w.Server.NpcShootCreatureForTest(hunter, animal.Id, banked));

        Assert.DoesNotContain(w.Server.Creatures, c => c.Id == animal.Id);
        Assert.Equal(loot, banked.Select(b => (b.Item, b.Count)).ToArray());
        Assert.Empty(w.Server.DropPackets); // banked, not spilled
        var sent = w.Transport.Sent.Select(s => s.Msg).ToList();
        Assert.Contains(sent, m => m is CreatureDefeated d && d.Id == animal.Id);

        var toPlayer = w.Transport.Sent.Where(s => s.Conn == w.Player.ConnectionId).Select(s => s.Msg).ToList();
        var arrow = Assert.Single(toPlayer.OfType<WorldFx>().Where(f => f.Kind == "arrow"));
        int npcId = hunter;
        Assert.Equal(npcId, arrow.SourceId);
        Assert.Equal(Cx + 0.5f, arrow.X, 3);
        Assert.True(arrow.Y > w.Feet + 1f, "the arrow leaves the bow, not the feet");
        Assert.Equal(Cx + 10.5f, arrow.ToX, 3);
        Assert.Equal(Cz + 0.5f, arrow.ToZ, 3);
        Assert.Contains(toPlayer.OfType<SoundFx>(), s => s.SoundId == "npc_bow_shot");
    }

    [Fact]
    public void AShot_CreditsNobody()
    {
        using var w = new NpcLifeWorld(_content, "hunt_credit");
        var sp = Huntable(w);
        int hunter = Hunter(w, Cx + 0.5f, Cz + 0.5f);
        PlayerAt(w, Cx - 6.5f, Cz + 2.5f);
        var animal = Spawn(w, sp, Cx + 9.5f, Cz + 0.5f);
        var state = w.Player.State;
        string Snapshot() => JsonSerializer.Serialize(new
        {
            Counters = state.AchievementCounters.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToList(),
            Bag = state.Inventory.Slots.Select(s => s is null ? "" : s.Item + "x" + s.Count).ToList(),
            Samples = state.SampleCase.Slots.Select(s => s is null ? "" : s.Item + "x" + s.Count).ToList(),
            Missions = state.Missions,
            Deeds = w.Server.DeedsForTest.Count,
        });
        string before = Snapshot();

        Assert.True(w.Server.NpcShootCreatureForTest(hunter, animal.Id, new List<ItemAmount>()));

        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public void AShot_OutOfRange_IsRefused()
    {
        using var w = new NpcLifeWorld(_content, "hunt_range");
        var sp = Huntable(w);
        int hunter = Hunter(w, Cx + 0.5f, Cz + 0.5f);
        PlayerAt(w, Cx - 18.5f, Cz + 18.5f);
        var animal = Spawn(w, sp, Cx + 18.5f, Cz + 0.5f);

        Assert.Equal(animal.Id, w.Server.FindNpcHuntTargetForTest(hunter, 24f)); // a target worth walking up to ...
        Assert.False(w.Server.NpcShootCreatureForTest(hunter, animal.Id, new List<ItemAmount>())); // ... but no shot from here
        Assert.Contains(w.Server.Creatures, c => c.Id == animal.Id);
    }

    // ---------------- animals notice hunters ----------------

    [Fact]
    public void AfterAShot_TheKinFleeFromTheHunter_NotFromTheNearestPlayer()
    {
        using var w = new NpcLifeWorld(_content, "hunt_kin");
        var sp = Huntable(w);
        int hunter = Hunter(w, Cx - 9.5f, Cz + 0.5f);
        var shot = Spawn(w, sp, Cx + 0.5f, Cz + 0.5f);
        var kin = Spawn(w, sp, Cx + 2.5f, Cz + 2.5f);
        // The player stands where the classic panic would send the kin running from them — toward the hunter.
        PlayerAt(w, Cx + 16.5f, Cz + 6.5f);
        var hunterPos = new Vector3f(Cx - 9.5f, w.Feet, Cz + 0.5f);
        float before = (float)Math.Sqrt(kin.Position.DistanceSquared(hunterPos));
        float startX = kin.Position.X;

        Assert.True(w.Server.NpcShootCreatureForTest(hunter, shot.Id, new List<ItemAmount>()));
        Assert.True(kin.ThreatTimer > 0 && kin.PanicTimer > 0, "the kin are startled and keep the hunter in mind");

        w.TickAt(0.5, 2.0);

        float after = (float)Math.Sqrt(kin.Position.DistanceSquared(hunterPos));
        Assert.True(after > before + 1f, $"the kin ran from the hunter ({before:0.0} → {after:0.0})");
        Assert.True(kin.Position.X > startX + 0.5f, "away from the hunter, even toward the player");
    }

    [Fact]
    public void ASkittishAnimal_FleesAHunterComingNear_ACalmOneGrazesOn()
    {
        using var w = new NpcLifeWorld(_content, "hunt_skittish");
        var sp = Huntable(w, CreatureTemperament.Skittish);
        PlayerAt(w, Cx - 18.5f, Cz + 18.5f); // far beyond the skittish reflex
        var animal = Spawn(w, sp, Cx + 0.5f, Cz + 0.5f);
        var hunterPos = new Vector3f(Cx - 3.5f, w.Feet, Cz + 0.5f);
        float before = (float)Math.Sqrt(animal.Position.DistanceSquared(hunterPos));

        bool fled = false;
        w.TickAt(0.5, 1.5, every: () =>
        {
            w.Server.ScareCreaturesFromForTest(hunterPos, 8f, 2.0); // the hunter on its round
            fled |= animal.Loco.Mode == MoveMode.Flee;
        });

        Assert.True(fled, "the skittish animal bolts from the hunter");
        Assert.True(Math.Sqrt(animal.Position.DistanceSquared(hunterPos)) > before + 0.5f);

        // A passive one notices the hunter but grazes on.
        sp.Temperament = CreatureTemperament.Passive;
        var calm = Spawn(w, sp, Cx + 0.5f, Cz - 6.5f);
        var near = new Vector3f(Cx - 3.5f, w.Feet, Cz - 6.5f);
        bool calmFled = false;
        w.TickAt(0.5, 1.5, every: () =>
        {
            w.Server.ScareCreaturesFromForTest(near, 8f, 2.0);
            calmFled |= calm.Loco.Mode == MoveMode.Flee;
        });

        Assert.False(calmFled, "a calm animal is not frightened by a hunter walking by");
    }
}
