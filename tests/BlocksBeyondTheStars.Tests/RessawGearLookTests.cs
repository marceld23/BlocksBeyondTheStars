// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Linq;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.State;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3337: the look of the Ressaw diving suit in the one gear mask both sides draw from (<see cref="GearLook"/>) — style 4 in
/// the head, chest and leg slots, and the first set boots, whose style took the mask's fourth slot (bits 25–27). Every piece
/// keeps its slot's generic bit (an older client draws a plain piece), the full suit decodes slot by slot, survives the
/// wire, and a presence wearing it stays a small uncompressed message — the presence gained no field.
/// </summary>
public sealed class RessawGearLookTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private static readonly string[] Suit = { "ressaw_helmet", "ressaw_suit", "ressaw_leggings", "ressaw_boots" };

    private static Func<string, bool> Wearing(params string[] keys) => key => keys.Contains(key);

    [Fact]
    public void TheBootsStyle_IsAFourthSlot_AboveTheLegs_AndTheMaskStaysPositive()
    {
        Assert.Equal(25, GearLook.BootsStyleShift);
        int legs = GearLook.Style(GearLook.LegsStyleShift, GearLook.StyleBits);
        int boots = GearLook.Style(GearLook.BootsStyleShift, GearLook.StyleBits);
        Assert.Equal(0, boots & 0xFFFF); // the old bits, LampLit included, stay untouched
        Assert.Equal(0, boots & legs);
        Assert.Equal(0, boots & GearLook.Style(GearLook.HeadStyleShift, GearLook.StyleBits));
        Assert.Equal(0, boots & GearLook.Style(GearLook.ChestStyleShift, GearLook.StyleBits));
        Assert.True(boots > 0);
        Assert.Equal(4, GearLook.StyleRessaw); // the first reserved style value
    }

    [Theory]
    [InlineData("ressaw_helmet", GearLook.Helmet, GearLook.HeadStyleShift)]
    [InlineData("ressaw_suit", GearLook.Chest, GearLook.ChestStyleShift)]
    [InlineData("ressaw_leggings", GearLook.Legs, GearLook.LegsStyleShift)]
    [InlineData("ressaw_boots", GearLook.Boots, GearLook.BootsStyleShift)]
    public void EachSuitPiece_SetsItsSlotBit_AndTheRessawStyle(string item, int slotBit, int shift)
    {
        int mask = GearLook.Mask(Wearing(item));

        Assert.Equal(slotBit | GearLook.Style(shift, GearLook.StyleRessaw), mask);
        Assert.Equal(GearLook.StyleRessaw, (mask >> shift) & GearLook.StyleBits);
    }

    [Fact]
    public void TheFullSuit_DecodesSlotBySlot_AndClassicBootsKeepStyleZero()
    {
        int full = GearLook.Mask(Wearing(Suit.Append("suit_lamp").ToArray())) | GearLook.LampLit;
        Assert.Equal(GearLook.StyleRessaw, GearLook.HeadStyle(full));
        Assert.Equal(GearLook.StyleRessaw, GearLook.ChestStyle(full));
        Assert.Equal(GearLook.StyleRessaw, GearLook.LegsStyle(full));
        Assert.Equal(GearLook.StyleRessaw, GearLook.BootsStyle(full));
        Assert.True(GearLook.Has(full, GearLook.Helmet | GearLook.Chest | GearLook.Legs | GearLook.Boots | GearLook.Lamp | GearLook.LampLit));
        Assert.False(GearLook.Has(full, GearLook.SpringBoots));

        Assert.Equal(GearLook.StyleClassic, GearLook.BootsStyle(GearLook.Mask(Wearing("boots"))));
        Assert.Equal(GearLook.StyleClassic, GearLook.BootsStyle(GearLook.Mask(Wearing("spring_boots", "guardian_legs"))));
    }

    [Fact]
    public void EverySuitPiece_LooksLikeThePieceOfItsOwnEquipSlot()
    {
        var pieces = Content.Items.Values.Where(i => i.RessawSuit).Select(i => i.Key).OrderBy(k => k, StringComparer.Ordinal);
        Assert.Equal(Suit.OrderBy(k => k, StringComparer.Ordinal), pieces);
        foreach (string key in Suit)
        {
            int slotBit = Content.GetItem(key)!.EquipSlot switch
            {
                "head" => GearLook.Helmet,
                "chest" => GearLook.Chest,
                "legs" => GearLook.Legs,
                "feet" => GearLook.Boots,
                var other => throw new InvalidOperationException($"{key} sits in the unexpected slot '{other}'"),
            };
            Assert.True(GearLook.Has(GearLook.Mask(Wearing(key)), slotBit), $"{key} draws in the wrong slot");
        }
    }

    [Fact]
    public void APresenceInTheFullSuit_SurvivesTheWire_AndStaysUnderTheCompressionThreshold()
    {
        int mask = GearLook.Mask(Wearing(Suit)) | GearLook.Lamp | GearLook.LampLit;
        var presence = new PlayerPresence { PlayerId = "p1", Name = "Alice", X = 1f, Y = 64f, Z = 2f, Gear = mask };

        byte[] encoded = NetCodec.Encode(presence);
        var back = Assert.IsType<PlayerPresence>(NetCodec.Decode(encoded));
        Assert.Equal(mask, back.Gear);
        Assert.True(encoded.Length < NetCodec.CompressionMinLengthBytes, $"{encoded.Length} bytes");
    }
}
