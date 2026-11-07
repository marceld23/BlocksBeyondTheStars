// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Sounders;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.Story;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The Sounders' items in the data and the shared rules (#3352–#3354): every Song Disc sings a phrase of real rune words that
/// round-trips through the note alphabet, the collectible's seeded song, the hidden blueprints and what they unlock, the
/// disc socket as the twelfth suit slot, and the obsidian look in the gear mask — one new bit, the presence never grows.
/// </summary>
public sealed class SoundersItemsDataTests
{
    private static readonly Lazy<GameContent> Content = new(() => ContentLoader.LoadFromDirectory(TestPaths.DataDir()));

    private static Func<string, bool> Wearing(params string[] keys) => key => keys.Contains(key);

    [Fact]
    public void EveryPhraseDisc_SingsThreeRuneWords_ThatRoundTripThroughTheNoteAlphabet()
    {
        foreach (string disc in SongDiscs.All.Where(d => d != SoundersIds.DiscSong))
        {
            var words = SongDiscs.WordsOf(disc);
            Assert.Equal(SongDiscs.RoundsPerDisc, words.Count);
            Assert.All(words, w => Assert.True(SounderLexicon.IsWord(w), $"{disc}: '{w}' is no rune word"));
            int[] notes = SongDiscs.NotesOf(disc);
            Assert.True(SounderAlphabet.IsValidSequence(notes));
            Assert.Equal(SongDiscs.PhraseOf(disc), SounderAlphabet.Decode(notes));
        }

        Assert.Empty(SongDiscs.WordsOf(SoundersIds.DiscSong)); // the collectible sings the spindle's own song
        Assert.Empty(SongDiscs.WordsOf("iron_ore"));
        Assert.Equal(SoundersIds.StoryDiscs, SongDiscs.All.Where(SongDiscs.IsStoryDisc));
    }

    [Fact]
    public void TheStoryOrders_AreTheIssuesOrder()
    {
        Assert.Equal(new[] { SoundersIds.DiscBuilders, SoundersIds.DiscHearing, SoundersIds.DiscHunger }, SongDiscs.LootOrder);
        Assert.Equal(new[] { SoundersIds.DiscGreatSong, SoundersIds.DiscVoices }, SongDiscs.ChamberOrder);
        Assert.Equal(new[] { SoundersIds.DiscHearing, SoundersIds.DiscCalm, SoundersIds.DiscLight }, SongDiscs.SocketDiscs);
    }

    [Fact]
    public void TheCollectibleSong_IsTwoOrThreeRuneWords_StablePerPlace_AndVaries()
    {
        var songs = new HashSet<string>();
        for (int x = 0; x < 60; x++)
        {
            string[] words = SongDiscs.CollectibleWords(4242, x, 64, -x * 3);
            Assert.InRange(words.Length, 2, 3);
            Assert.All(words, w => Assert.True(SounderLexicon.IsWord(w)));
            for (int i = 1; i < words.Length; i++)
            {
                Assert.NotEqual(words[i - 1], words[i]);
            }

            Assert.Equal(words, SongDiscs.CollectibleWords(4242, x, 64, -x * 3));
            songs.Add(string.Join(" ", words));
        }

        Assert.True(songs.Count > 20, "the songs should differ from place to place");
    }

    [Fact]
    public void Answers_MatchTheDiscWordByWord_IgnoringCase()
    {
        Assert.True(SongDiscs.AnswersMatch(SoundersIds.DiscBuilders, new[] { "olu", " TAR", "Soa " }));
        Assert.False(SongDiscs.AnswersMatch(SoundersIds.DiscBuilders, new[] { "OLU", "TAR" }));
        Assert.False(SongDiscs.AnswersMatch(SoundersIds.DiscBuilders, new[] { "OLU", "TAR", "SOO" }));
        Assert.False(SongDiscs.AnswersMatch(SoundersIds.DiscSong, Array.Empty<string>()));
        Assert.False(SongDiscs.AnswersMatch(SoundersIds.DiscBuilders, null));
        Assert.Equal("AOS", SongDiscs.LettersOf(new[] { "SOA" }));
        Assert.Equal("ALMNORSTU", SongDiscs.LettersOf(new[] { "OLU", "TAR", "SOA", "MN" }));
    }

    [Fact]
    public void TheItems_AreInTheData_WithTheirSlotsAndTheBlasterAsAGadget()
    {
        var c = Content.Value;
        foreach (string disc in SongDiscs.All)
        {
            var item = c.GetItem(disc);
            Assert.NotNull(item);
            Assert.Equal(SongDiscs.IsSocketDisc(disc) ? EquipSlots.Disc : null, item!.EquipSlot);
            Assert.Equal(disc == SoundersIds.DiscSong ? 16 : 1, item.MaxStack);
        }

        var blaster = c.GetItem(SoundersIds.ResonanceBlaster)!;
        Assert.Equal(ToolKind.Gadget, blaster.Tool!.Kind);
        Assert.True(blaster.Tool.Knockback > 0f);
        Assert.Equal(3f, blaster.Tool.StaggerSeconds);
        Assert.Equal(0f, blaster.Tool.Damage); // it fends off, it never hurts
        Assert.NotNull(blaster.HeldModel);

        Assert.Equal(EquipSlots.Head, c.GetItem(SoundersIds.ObsidianHelmet)!.EquipSlot);
        Assert.Equal(EquipSlots.Chest, c.GetItem(SoundersIds.ObsidianChest)!.EquipSlot);
        Assert.Equal(EquipSlots.Legs, c.GetItem(SoundersIds.ObsidianLegs)!.EquipSlot);
        Assert.Equal(c.GetItem("titan_chest")!.ArmorResistance, c.GetItem(SoundersIds.ObsidianChest)!.ArmorResistance);

        var spindle = c.GetBlock(SoundersIds.SongSpindle)!;
        Assert.Equal(SoundersIds.SongSpindle, c.GetItem(SoundersIds.SongSpindle)!.PlacesBlock);
        Assert.Contains(spindle.Drops, d => d.Item == SoundersIds.SongSpindle);
    }

    [Theory]
    [InlineData(SoundersIds.BlueprintSpindle, SoundersIds.DiscBeginning, SoundersIds.SongSpindle)]
    [InlineData(SoundersIds.BlueprintResonance, SoundersIds.DiscBuilders, SoundersIds.ResonanceBlaster)]
    [InlineData(SoundersIds.BlueprintObsidian, SoundersIds.DiscHearing, SoundersIds.ObsidianChest)]
    [InlineData(SoundersIds.BlueprintVoice, SoundersIds.DiscVoices, null)]
    public void EachHiddenBlueprint_ShowsWithItsDisc_AndCostsTheSoundersStones(string blueprint, string disc, string? makes)
    {
        var c = Content.Value;
        var bp = c.GetBlueprint(blueprint)!;
        Assert.Equal("milestone:" + SoundersIds.DiscMilestone(disc), bp.HiddenUntil);
        Assert.True(StoryConditionParser.TryParse(bp.HiddenUntil, out _, out string error), error);
        Assert.Contains(bp.UnlockCost, i => i.Item == "obsidian");
        Assert.Contains(bp.UnlockCost, i => i.Item == "crystal");
        Assert.Contains(bp.UnlockCost, i => i.Item == "rune_stone");
        var recipes = c.Recipes.Values.Where(r => r.RequiredBlueprint == blueprint).ToList();
        if (makes is null)
        {
            Assert.Empty(recipes); // the voices are instruments, not an item
            Assert.NotEmpty(bp.Features);
        }
        else
        {
            Assert.Contains(recipes, r => r.Outputs.Any(o => o.Item == makes));
        }
    }

    [Fact]
    public void TheDiscSocket_IsTheTwelfthSlot_AndTakesOnlyDiscs()
    {
        Assert.Equal(12, EquipSlots.Count);
        Assert.Equal(EquipSlot.Disc, (EquipSlot)11);
        Assert.Equal(EquipSlot.Disc, EquipSlots.Parse("disc"));
        Assert.Equal("ui.equip.slot.disc", EquipSlots.LabelKey(EquipSlot.Disc));
        Assert.True(EquipSlots.Accepts(EquipSlot.Disc, "disc"));
        Assert.False(EquipSlots.Accepts(EquipSlot.Chest, "disc"));
        Assert.False(EquipSlots.Accepts(EquipSlot.Disc, "module"));
        Assert.False(EquipSlots.IsModule(EquipSlot.Disc));
    }

    [Fact]
    public void TheObsidianSet_DrawsStyleFour_TheSocketDiscIsOneBit_AndTheLightDiscGlows()
    {
        int set = GearLook.Mask(Wearing(SoundersIds.ObsidianHelmet, SoundersIds.ObsidianChest, SoundersIds.ObsidianLegs));
        Assert.Equal(GearLook.StyleObsidian, GearLook.HeadStyle(set));
        Assert.Equal(GearLook.StyleObsidian, GearLook.ChestStyle(set));
        Assert.Equal(GearLook.StyleObsidian, GearLook.LegsStyle(set));
        Assert.True(GearLook.Has(set, GearLook.Helmet | GearLook.Chest | GearLook.Legs)); // an older client draws plain plates
        Assert.False(GearLook.Has(set, GearLook.SocketDisc));

        int hearing = GearLook.Mask(Wearing(SoundersIds.ObsidianChest, SoundersIds.DiscHearing));
        Assert.True(GearLook.Has(hearing, GearLook.SocketDisc));
        Assert.Equal(GearLook.StyleObsidian, GearLook.ChestStyle(hearing));

        int light = GearLook.Mask(Wearing(SoundersIds.ObsidianChest, SoundersIds.DiscLight));
        Assert.Equal(GearLook.StyleObsidianGlow, GearLook.ChestStyle(light));
        Assert.True(GearLook.Has(light, GearLook.SocketDisc));

        Assert.Equal(0, GearLook.Mask(Wearing(SoundersIds.DiscLight))); // a disc without its chest shows nothing
        Assert.Equal(1 << 28, GearLook.SocketDisc);
        Assert.Equal(0, GearLook.SocketDisc & ((1 << 28) - 1)); // above every older bit and style (the boots' style ends at 27)
        Assert.True(light > 0);
    }

    [Fact]
    public void TheMinimalPresence_StaysAsSmallAsBefore_AndTheMaskSurvivesTheWire()
    {
        Assert.Equal(0, GearLook.Mask(Wearing())); // nothing worn: Gear 0, the minimal presence unchanged
        int full = GearLook.Mask(Wearing(SoundersIds.ObsidianHelmet, SoundersIds.ObsidianChest, SoundersIds.ObsidianLegs, SoundersIds.DiscLight));
        var back = Assert.IsType<PlayerPresence>(NetCodec.Decode(NetCodec.Encode(new PlayerPresence { PlayerId = "a", Gear = full })));
        Assert.Equal(full, back.Gear); // the look rides on the existing Gear field — the presence gains no field (#3234)
    }

    [Fact]
    public void TheNoteKeyMinigame_IsAppendedToTheCatalogue()
    {
        string catalog = System.IO.File.ReadAllText(System.IO.Path.Combine(TestPaths.DataDir(), "minigames", "catalog.json"));
        using var doc = System.Text.Json.JsonDocument.Parse(catalog);
        var keys = doc.RootElement.GetProperty("games").EnumerateArray().Select(g => g.GetProperty("key").GetString()).ToList();
        Assert.Equal(SongDiscs.NoteKeyGame, keys[^1]);
        Assert.Equal("void_solitaire", keys[^2]); // appended — the cube → game order of the old games is kept
    }
}
