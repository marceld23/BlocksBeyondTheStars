// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.State;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>#3314: a companion record survives the save. <c>StateMapper</c> copies every <see cref="TamedCreature"/> field
/// by hand, and it forgot the robot's <see cref="TamedCreature.Kind"/>, <see cref="TamedCreature.Roams"/> and
/// <see cref="TamedCreature.KnockedOut"/> — after a reload the hunter robot was an ordinary pet bound to one world. The
/// reflection guard makes the next new field fail here instead of in a player's save.</summary>
public sealed class TamedCreatureSnapshotTests
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    /// <summary>The save path: snapshot, JSON (as every repository writes it), and back.</summary>
    private static PlayerState SaveAndLoad(PlayerState state)
        => StateMapper.PlayerFromJson(JsonSerializer.Serialize(StateMapper.ToSnapshot(state), Json), state.PlayerId, Json);

    private static PlayerState With(TamedCreature tc) => new() { PlayerId = "ada", Name = "Ada", TamedCreatures = { tc } };

    [Fact]
    public void ARobotCompanion_StaysARoamingRobot_AcrossSaveAndLoad()
    {
        var robot = new TamedCreature
        {
            Id = "robot_1",
            HomeBodyId = "sys0-p1",
            Name = "Robo",
            SpeciesId = "robot_hunter",
            Species = new CreatureSpecies { Id = "robot_hunter", BodyPlan = CreatureBodyPlan.Robot },
            Bond = 100,
            Kind = TamedCreature.RobotKind,
            Roams = true,
            KnockedOut = true,
        };

        var back = Assert.Single(SaveAndLoad(With(robot)).TamedCreatures);

        Assert.Equal(TamedCreature.RobotKind, back.Kind);
        Assert.True(back.Roams);
        Assert.True(back.KnockedOut);
        Assert.Equal(CreatureBodyPlan.Robot, back.Species.BodyPlan);
    }

    [Fact]
    public void AnOldRecordWithoutTheNewFields_LoadsAsATamedAnimal()
    {
        const string json = "{\"Id\":\"ada\",\"Name\":\"Ada\",\"TamedCreatures\":[{\"Id\":\"pet\",\"HomeBodyId\":\"b\",\"Name\":\"Flocke\",\"Bond\":50}]}";

        var pet = Assert.Single(StateMapper.PlayerFromJson(json, "ada", Json).TamedCreatures);

        Assert.Equal(string.Empty, pet.Kind);
        Assert.False(pet.Roams);
        Assert.False(pet.KnockedOut);
        Assert.True(pet.Following); // #3344: an old save keeps every companion following, exactly as before
        Assert.False(pet.HasHomeSpot);
    }

    /// <summary>#3344: the follow choice and the home spot survive save and load — a pet left at home stays home.</summary>
    [Fact]
    public void ThePetsFollowChoiceAndHomeSpot_SurviveSaveAndLoad()
    {
        var pet = new TamedCreature
        {
            Id = "pet",
            HomeBodyId = "sys0-p1",
            Name = "Flocke",
            Following = false,
            HasHomeSpot = true,
            HomeX = 12.5f,
            HomeY = 70f,
            HomeZ = -3.5f,
        };

        var back = Assert.Single(SaveAndLoad(With(pet)).TamedCreatures);

        Assert.False(back.Following);
        Assert.True(back.HasHomeSpot);
        Assert.Equal((12.5f, 70f, -3.5f), (back.HomeX, back.HomeY, back.HomeZ));
    }

    /// <summary>Every public settable property of <see cref="TamedCreature"/> gets a non-default value and must come back
    /// from the snapshot round trip — a field added later without its line in <c>CloneTamed</c> fails here.</summary>
    [Fact]
    public void EveryPublicSettableProperty_SurvivesTheSnapshotRoundTrip()
    {
        var properties = typeof(TamedCreature).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite && p.SetMethod!.IsPublic)
            .ToList();
        var original = new TamedCreature();
        foreach (var property in properties)
        {
            property.SetValue(original, SampleValue(property));
        }

        var restored = Assert.Single(SaveAndLoad(With(original)).TamedCreatures);
        var direct = Assert.Single(StateMapper.FromSnapshot(StateMapper.ToSnapshot(With(original))).TamedCreatures);

        foreach (var property in properties)
        {
            Assert.True(Same(property.GetValue(original), property.GetValue(restored)), $"TamedCreature.{property.Name} is lost on save + load.");
            Assert.True(Same(property.GetValue(original), property.GetValue(direct)), $"TamedCreature.{property.Name} is lost in the snapshot copy.");
        }
    }

    private static object SampleValue(PropertyInfo property)
    {
        var type = property.PropertyType;
        if (type == typeof(string))
        {
            return "sample_" + property.Name;
        }

        if (type == typeof(bool))
        {
            // #3344: the opposite of the default — a flag that defaults to true (Following) must be copied too, and a
            // fixed "true" would read as copied even when CloneTamed forgot it.
            return !(bool)property.GetValue(new TamedCreature())!;
        }

        if (type == typeof(int))
        {
            return 37;
        }

        if (type == typeof(long))
        {
            return 1_234_567_890_123L;
        }

        if (type == typeof(float))
        {
            return 1.25f;
        }

        if (type == typeof(CreatureSpecies))
        {
            return new CreatureSpecies { Id = "sample_species", BodyPlan = CreatureBodyPlan.Reindeer, ColorRgb = 0x8A5A3B };
        }

        throw new InvalidOperationException($"Give TamedCreature.{property.Name} ({type.Name}) a sample value here — and make sure CloneTamed copies it.");
    }

    private static bool Same(object? expected, object? actual)
        => expected is CreatureSpecies a && actual is CreatureSpecies b
            ? a.Id == b.Id && a.BodyPlan == b.BodyPlan && a.ColorRgb == b.ColorRgb
            : Equals(expected, actual);
}
