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
/// #3354 — the obsidian armour and its disc socket on the server: a disc goes into the socket only while the obsidian chest
/// is worn (and raises <c>disc:inserted</c>), works only while the chest stays on (<see cref="SvGameServer.HasActiveSocketDisc"/>,
/// the Echo code's question), the calm disc makes a Soundrak overlook and leave the wearer alone, and the whole set's quiet
/// steps make a hunter notice its wearer only from half its range.
/// </summary>
public sealed class ObsidianArmourTests
{
    private static readonly Lazy<GameContent> Content = new(() => ContentLoader.LoadFromDirectory(TestPaths.DataDir()));

    private sealed class Recorder : IGameEventListener
    {
        public readonly List<(string Key, string Arg)> Events = new();

        public void OnGameEvent(PlayerSession actor, string key, string arg, int amount) => Events.Add((key, arg));
    }

    private static NpcLifeWorld World(string name) => new(Content.Value, name, padHalf: 8);

    private static int Give(PlayerSession p, string item)
    {
        p.State.Inventory.Add(item, 1, 1);
        for (int i = 0; i < p.State.Inventory.SlotCount; i++)
        {
            if (p.State.Inventory.Slots[i] is { IsEmpty: false } s && s.Item == item)
            {
                return i;
            }
        }

        return -1;
    }

    private static void Wear(NpcLifeWorld w, string item)
        => w.Server.EquipItemForTest(w.Player.State.PlayerId, Give(w.Player, item));

    private static string Worn(PlayerSession p, EquipSlot slot) => p.State.Equipment.Slots[(int)slot]?.Item ?? string.Empty;

    [Fact]
    public void TheSocket_TakesADisc_OnlyWithTheObsidianChest_AndRaisesDiscInserted()
    {
        using var w = World("obsidian_socket");
        var events = new Recorder();
        w.Server.AddGameEventListener(GameEventKeys.DiscInserted, events);

        Wear(w, SoundersIds.DiscHearing); // no chest yet: refused, the disc stays in the pack
        Assert.Equal(string.Empty, Worn(w.Player, EquipSlot.Disc));
        Assert.Contains(w.Transport.To<ActionRejected>(w.Player), r => r.Reason == "@srv.equip.disc_needs_obsidian");

        Wear(w, SoundersIds.ObsidianChest);
        Assert.Equal(SoundersIds.ObsidianChest, Worn(w.Player, EquipSlot.Chest));
        w.Server.EquipItemForTest(w.Player.State.PlayerId, FirstSlotOf(w.Player, SoundersIds.DiscHearing)); // the refused disc, again

        Assert.Equal(SoundersIds.DiscHearing, Worn(w.Player, EquipSlot.Disc));
        Assert.Equal((GameEventKeys.DiscInserted, SoundersIds.DiscHearing), Assert.Single(events.Events));
        Assert.True(SvGameServer.HasActiveSocketDisc(w.Player.State, SoundersIds.DiscHearing));
        Assert.False(SvGameServer.HasActiveSocketDisc(w.Player.State, SoundersIds.DiscCalm));
    }

    [Fact]
    public void ADiscInTheSocket_WorksOnlyWhileTheChestIsWorn()
    {
        using var w = World("obsidian_works");
        Wear(w, SoundersIds.ObsidianChest);
        Wear(w, SoundersIds.DiscLight);
        Assert.True(SvGameServer.HasActiveSocketDisc(w.Player.State, SoundersIds.DiscLight));
        Assert.Equal(GearLook.StyleObsidianGlow, GearLook.ChestStyle(GearLook.Mask(k => EquipmentSystem.Wears(w.Player.State, k))));

        w.Player.State.Equipment.SetSlot((int)EquipSlot.Chest, null); // the chest comes off, the disc stays in the socket
        Assert.Equal(SoundersIds.DiscLight, SvGameServer.SocketDiscOf(w.Player.State));
        Assert.False(SvGameServer.HasActiveSocketDisc(w.Player.State, SoundersIds.DiscLight));
        Assert.False(GearLook.Has(GearLook.Mask(k => EquipmentSystem.Wears(w.Player.State, k)), GearLook.SocketDisc));
    }

    [Fact]
    public void TheCalmDisc_MakesASoundrakOverlookAndLeaveTheWearerAlone()
    {
        using var w = World("obsidian_calm");
        var p = w.Player.State.Position;
        string? id = w.Server.SpawnAuthoredCreature(SoundersIds.Soundrak, new Vector3f(p.X + 5f, w.Feet, p.Z));
        Assert.NotNull(id);
        var soundrak = w.Server.Creatures.First(c => c.Id == id);
        Assert.False(w.Server.PreyOverlooked(soundrak, w.Player, 4.0));
        Assert.False(w.Server.BiteOverlooked(soundrak, w.Player));

        Wear(w, SoundersIds.ObsidianChest);
        Wear(w, SoundersIds.DiscCalm);

        Assert.True(w.Server.PreyOverlooked(soundrak, w.Player, 1.0));
        Assert.True(w.Server.BiteOverlooked(soundrak, w.Player));

        string other = w.Server.SpawnCreatureAtForTest(new Vector3f(p.X - 5f, w.Feet, p.Z),
            w.Server.SpeciesRoster.First(s => !SoundersFauna.IsSoundrak(s)).Id);
        var animal = w.Server.Creatures.First(c => c.Id == other);
        Assert.False(w.Server.BiteOverlooked(animal, w.Player)); // the calm disc is the Soundraks' song only
    }

    [Fact]
    public void TheWholeSet_MakesQuietSteps_HuntersNoticeTheWearerFromHalfTheirRange()
    {
        using var w = World("obsidian_quiet");
        string id = w.Server.SpawnCreatureAtForTest(new Vector3f(NpcLifeWorld.Cx + 5f, w.Feet, NpcLifeWorld.Cz));
        var hunter = w.Server.Creatures.First(c => c.Id == id);
        var sp = w.Server.SpeciesRoster.First(s => s.Id == hunter.SpeciesId);
        float range = 8f + Math.Max(0f, sp.Size - 2f); // the creature tick's aggro rule (#638), a hound's at least its own
        range = HoundRules.IsHound(sp) ? Math.Max(range, HoundRules.AggroRange) : range;
        double far = Math.Pow(range * SvGameServer.QuietStepsFactor + 0.5, 2);
        double near = Math.Pow(range * SvGameServer.QuietStepsFactor - 0.5, 2);

        Wear(w, SoundersIds.ObsidianHelmet);
        Wear(w, SoundersIds.ObsidianChest);
        Assert.False(w.Server.PreyOverlooked(hunter, w.Player, far)); // two pieces are no set

        Wear(w, SoundersIds.ObsidianLegs);
        Assert.True(SvGameServer.WearsObsidianSet(w.Player.State));
        Assert.True(w.Server.PreyOverlooked(hunter, w.Player, far));
        Assert.False(w.Server.PreyOverlooked(hunter, w.Player, near));
        Assert.False(w.Server.PreyOverlooked(null, w.Player, far)); // no hunter, nothing to overlook
    }

    private static int FirstSlotOf(PlayerSession p, string item)
    {
        for (int i = 0; i < p.State.Inventory.SlotCount; i++)
        {
            if (p.State.Inventory.Slots[i] is { IsEmpty: false } s && s.Item == item)
            {
                return i;
            }
        }

        return -1;
    }
}
