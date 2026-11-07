// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Sounders;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.Story;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3353 — the Resonance Blaster: its sonic cone pushes and dazes only HOSTILE creatures and machines in front of the player
/// (no damage — it fends off), leaves friendly animals and anything behind alone, and calms a Soundrak; on a monument's rune
/// stone it opens that monument's sound chamber — once, persisted per body — carved as world edits with a relic cache, the
/// first chamber disc for a player with the Sounders story running, <c>chamber:opened</c> and the <c>chamber_open</c> sound.
/// </summary>
public sealed class ResonanceBlasterTests
{
    private static readonly Lazy<GameContent> Content = new(() => ContentLoader.LoadFromDirectory(TestPaths.DataDir()));

    private sealed class Recorder : IGameEventListener
    {
        public readonly List<(string Key, string Arg)> Events = new();

        public void OnGameEvent(PlayerSession actor, string key, string arg, int amount) => Events.Add((key, arg));
    }

    private static NpcLifeWorld World(string name, string planet = "rocky")
        => new(Content.Value, name, padHalf: 10, configure: c => c.StartPlanet = planet);

    private static void Arm(NpcLifeWorld w)
    {
        w.Player.State.Inventory.SetSlot(0, new ItemStack(SoundersIds.ResonanceBlaster, 1));
        w.Player.State.SelectedHotbarSlot = 0;
        w.Player.State.SuitEnergy = 100f;
    }

    private static CreatureSpecies Walker(SvGameServer server)
        => server.SpeciesRoster
            .Where(sp => CreatureMotion.ClassOf(sp) == MotionClass.Walker && !CreatureMotion.IsGiant(sp)
                && !ArachnidRules.Lurks(sp) && !sp.Hostile && !SoundersFauna.IsSoundrak(sp))
            .OrderBy(sp => sp.Size)
            .First();

    private static CombatEntity Animal(NpcLifeWorld w, CreatureSpecies sp, float x, float z, bool hostile)
    {
        string id = w.Server.SpawnCreatureAtForTest(new Vector3f(x, w.Feet, z), sp.Id);
        var c = w.Server.Creatures.First(e => e.Id == id);
        c.HullMax = c.Hull = 200f;
        c.ProvokeTimer = hostile ? 60 : 0; // a provoked animal is hostile until it calms down
        return c;
    }

    /// <summary>The player's eyes look along +X: the aim point four blocks ahead at eye height.</summary>
    private static Vector3f AheadOf(NpcLifeWorld w)
    {
        var p = w.Player.State.Position;
        return new Vector3f(p.X + 4f, p.Y + 1.5f, p.Z);
    }

    [Fact]
    public void ThePush_DazesOnlyTheHostilesInFront_AndHurtsNobody()
    {
        using var w = World("blaster_push", "jungle");
        Arm(w);
        var sp = Walker(w.Server);
        var p = w.Player.State.Position;
        var hostile = Animal(w, sp, p.X + 3f, p.Z, hostile: true);
        var friendly = Animal(w, sp, p.X + 3f, p.Z + 2f, hostile: false);
        var behind = Animal(w, sp, p.X - 3f, p.Z, hostile: true);
        w.Server.SpawnPlanetEnemyAtForTest(new Vector3f(p.X + 4f, w.Feet, p.Z - 1f), damagePerSecond: 0f);
        var machine = w.Server.PlanetEnemies.Last();
        float hostileX = hostile.Position.X;

        w.Server.UseGadgetForTest(w.Player.State.PlayerId, SoundersIds.ResonanceBlaster, AheadOf(w));

        Assert.True(w.Server.IsStaggeredForTest(hostile.Id), "the hostile in front is dazed");
        Assert.True(hostile.Position.X > hostileX, "and pushed away from the player");
        Assert.True(w.Server.IsStaggeredForTest(machine.Id), "a machine in front is dazed too");
        Assert.False(w.Server.IsStaggeredForTest(friendly.Id), "a friendly animal is left alone");
        Assert.False(w.Server.IsStaggeredForTest(behind.Id), "nothing behind the player");
        Assert.Equal(200f, hostile.Hull); // it fends off, it never hurts
        Assert.Equal(100f - Content.Value.GetItem(SoundersIds.ResonanceBlaster)!.Tool!.EnergyPerUse, w.Player.State.SuitEnergy, 3);
        Assert.Contains(w.Transport.Messages().OfType<SoundFx>(), f => f.SoundId == "blaster_pulse_1");

        w.Server.AdvanceUptimeForTest(3.1);
        Assert.False(w.Server.IsStaggeredForTest(hostile.Id), "the daze lasts three seconds");
    }

    [Fact]
    public void ThePush_CalmsASoundrak()
    {
        using var w = World("blaster_soundrak", "jungle");
        Arm(w);
        var p = w.Player.State.Position;
        string? id = w.Server.SpawnAuthoredCreature(SoundersIds.Soundrak, new Vector3f(p.X + 4f, w.Feet, p.Z));
        Assert.NotNull(id);
        var soundrak = w.Server.Creatures.First(c => c.Id == id);
        soundrak.ProvokeTimer = 30;

        w.Server.UseGadgetForTest(w.Player.State.PlayerId, SoundersIds.ResonanceBlaster, AheadOf(w));

        Assert.True(w.Server.IsCalmed(soundrak));
        Assert.Equal(0, soundrak.ProvokeTimer);
    }

    private static Vector3i Monument(NpcLifeWorld w, int dz = 0)
    {
        int cx = NpcLifeWorld.Cx, cz = NpcLifeWorld.Cz + dz;
        w.Server.AddMonumentForTest(new Vector3i(cx - 3, w.Feet, cz - 3), new Vector3i(cx + 3, w.Feet + 5, cz + 3));
        var rune = new Vector3i(cx + 1, w.Feet, cz);
        w.Server.World.SetBlock(rune, w.Block("rune_stone"));
        return rune;
    }

    private static void Resonate(NpcLifeWorld w, Vector3i rune)
        => w.Server.UseGadgetForTest(w.Player.State.PlayerId, SoundersIds.ResonanceBlaster, new Vector3f(rune.X + 0.5f, rune.Y + 0.5f, rune.Z + 0.5f));

    [Fact]
    public void TheRuneStone_OpensTheSoundChamber_OnceAndForGood()
    {
        using var w = World("blaster_chamber");
        var events = new Recorder();
        w.Server.AddGameEventListener(GameEventKeys.ChamberOpened, events);
        Arm(w);
        var rune = Monument(w);
        int cx = NpcLifeWorld.Cx, cz = NpcLifeWorld.Cz, floor = w.Feet - SvGameServer.ChamberDepth;

        Resonate(w, rune);

        var m = w.Server.MonumentInstancesForTest.Last();
        Assert.True(w.Server.ChamberOpened(m));
        Assert.True(w.Server.World.GetBlock(new Vector3i(cx, floor, cz)).IsAir);
        Assert.True(w.Server.World.GetBlock(new Vector3i(cx + 2, floor + 2, cz - 2)).IsAir);
        Assert.Equal("resonance_crystal", Content.Value.BlockById(w.Server.World.GetBlock(new Vector3i(cx - 2, floor, cz - 2)))!.Key);
        string cacheId = $"loot_soundchamber_relic_cache_{cx}_{floor}_{cz}";
        Assert.Contains(w.Server.Containers, c => c.Id == cacheId);
        Assert.Single(events.Events);
        Assert.Contains(w.Transport.Messages().OfType<SoundFx>(), f => f.SoundId == "chamber_open");
        Assert.Contains("@srv.chamber.opened", w.Transport.TextsTo(w.Player));

        // A way up: the stairway along +X reaches the open air above the pad.
        Assert.True(w.Server.World.GetBlock(new Vector3i(cx + 3, floor + 1, cz)).IsAir);

        float energy = w.Player.State.SuitEnergy;
        w.Server.AdvanceUptimeForTest(5);
        Resonate(w, rune);
        Assert.Contains("@srv.chamber.already_open", w.Transport.TextsTo(w.Player));
        Assert.Single(events.Events);
        Assert.Equal(energy, w.Player.State.SuitEnergy); // a refused resonate costs nothing

        w.Restart();
        Monument(w);
        Assert.True(w.Server.ChamberOpened(w.Server.MonumentInstancesForTest.Last()), "the opening survives a reload");
        Assert.True(w.Server.World.GetBlock(new Vector3i(cx, floor, cz)).IsAir, "and so does the carved chamber");
    }

    [Fact]
    public void TheFirstTwoChambers_HoldTheGreatSongAndTheVoices_ForTheOpener()
    {
        using var w = World("blaster_chamber_discs");
        Arm(w);
        w.Player.State.Milestones.Add(StoryPackKeys.Started(SoundersIds.PackId));
        var first = Monument(w);
        Resonate(w, first);
        int cx = NpcLifeWorld.Cx, cz = NpcLifeWorld.Cz, floor = w.Feet - SvGameServer.ChamberDepth;
        var cache = w.Server.Containers.First(c => c.Id == $"loot_soundchamber_relic_cache_{cx}_{floor}_{cz}");
        Assert.Contains(cache.Items, s => s.Item == SoundersIds.DiscGreatSong);
        Assert.Equal(w.Player.State.PlayerId, cache.Owner); // locked to the opener until they took it

        w.Server.AdvanceUptimeForTest(5);
        var second = Monument(w, dz: 30);
        w.Player.State.Position = new Vector3f(NpcLifeWorld.Cx - 1.5f, w.Feet, NpcLifeWorld.Cz + 30 - 1.5f); // walk over
        Resonate(w, second);
        var cache2 = w.Server.Containers.First(c => c.Id == $"loot_soundchamber_relic_cache_{cx}_{floor}_{cz + 30}");
        Assert.Contains(cache2.Items, s => s.Item == SoundersIds.DiscVoices);
        Assert.DoesNotContain(cache2.Items, s => s.Item == SoundersIds.DiscGreatSong);
    }

    [Fact]
    public void ARuneStoneOutsideAMonument_IsNoChamber_ThePulseFendsOffInstead()
    {
        using var w = World("blaster_lone_rune");
        Arm(w);
        var lone = new Vector3i(NpcLifeWorld.Cx + 1, w.Feet, NpcLifeWorld.Cz);
        w.Server.World.SetBlock(lone, w.Block("rune_stone"));

        Resonate(w, lone);

        Assert.Equal(ResonanceOutcome.NotARuneStone, w.Server.TryResonate(w.Player, lone));
        Assert.Contains(w.Transport.Messages().OfType<SoundFx>(), f => f.SoundId == "blaster_pulse_1");
        Assert.DoesNotContain(w.Server.Containers, c => c.Id.StartsWith("loot_soundchamber_", StringComparison.Ordinal));
    }
}
