// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The push-to-talk key (#2568): it defaulted to V, the camera view's key, so every word spoken flipped the view, and
/// the settings picker offered keys that were mostly bound already. The default is Caps Lock now, a saved V moves there
/// once, and the picker skips every key an input action uses.
/// </summary>
public sealed class PushToTalkKeysTests
{
    // A copy of the stock keyboard bindings of every InputAction (InputMap.DefaultKey, Unity-side) as KeyCode names. The
    // EditMode test InputAbstractionEditModeTests.PushToTalkDefault_IsBoundToNoAction_… checks the real table.
    private static readonly string[] StockBindings =
    {
        "E", "F", "X", "X", "V", "G", "H", "R", "L", "B", "R", "I", "J", "Return", "Mouse2", "F", "L", "P", "M", "B",
        "LeftShift", "F", "R", "U", "O", "T", "K", "N", "M", "C", "Q", "Escape", "Tab", "T", "R", "Mouse1",
    };

    [Fact]
    public void TheDefault_IsCapsLock_AndNoStockActionUsesIt()
    {
        Assert.Equal("CapsLock", PushToTalkKeys.Default);
        Assert.DoesNotContain(PushToTalkKeys.Default, StockBindings);
        Assert.Equal(PushToTalkKeys.Default, PushToTalkKeys.Free(StockBindings)[0]); // first in the picker too
    }

    [Fact]
    public void TheOldDefaultV_MovesToCapsLock_OnlyOnce()
    {
        Assert.Equal("CapsLock", PushToTalkKeys.Migrate("V", migrated: false));
        Assert.Equal("CapsLock", PushToTalkKeys.Migrate("v", migrated: false));
        Assert.Equal("V", PushToTalkKeys.Migrate("V", migrated: true)); // picked again on purpose after the move: kept
    }

    [Fact]
    public void AnyOtherSavedKey_IsKept_AndAnEmptyOneTakesTheDefault()
    {
        Assert.Equal("LeftAlt", PushToTalkKeys.Migrate("LeftAlt", migrated: false));
        Assert.Equal("Mouse3", PushToTalkKeys.Migrate("Mouse3", migrated: true));
        Assert.Equal("CapsLock", PushToTalkKeys.Migrate(string.Empty, migrated: false));
        Assert.Equal("CapsLock", PushToTalkKeys.Migrate(null, migrated: true));
    }

    [Fact]
    public void ThePicker_OffersNoBoundKey()
    {
        var free = PushToTalkKeys.Free(StockBindings);

        Assert.NotEmpty(free);
        foreach (var key in free)
        {
            Assert.DoesNotContain(key, StockBindings);
        }

        // V (view), B (stealth), T (trade / target), G (loot), H (deposit) are bound by default — skipped.
        Assert.DoesNotContain("V", free);
        Assert.DoesNotContain("T", free);
        Assert.Equal(new[] { "CapsLock", "Z", "Y", "LeftAlt", "Mouse3", "Mouse4" }, free);
    }

    [Fact]
    public void AKeyFreedByARebind_JoinsThePicker()
    {
        // The camera view moved from V to Z: V is free now, Z is not.
        var bound = new System.Collections.Generic.List<string>(StockBindings);
        bound[bound.IndexOf("V")] = "Z";

        var free = PushToTalkKeys.Free(bound);

        Assert.Contains("V", free);
        Assert.DoesNotContain("Z", free);
    }

    [Fact]
    public void Next_CyclesTheFreeKeys_AndAnOutdatedPickStepsToTheFirst()
    {
        Assert.Equal("Z", PushToTalkKeys.Next("CapsLock", StockBindings));
        Assert.Equal("CapsLock", PushToTalkKeys.Next("Mouse4", StockBindings)); // wraps around
        Assert.Equal("CapsLock", PushToTalkKeys.Next("V", StockBindings));      // the old default, bound now → first free
        Assert.Equal("CapsLock", PushToTalkKeys.Next("LeftControl", StockBindings)); // an old pick outside the list
        Assert.Equal("Z", PushToTalkKeys.Next("capslock", StockBindings));      // names compare case-insensitively
    }

    [Fact]
    public void WithEveryCandidateBound_TheDefaultIsTheAnswer()
    {
        Assert.Equal(PushToTalkKeys.Default, PushToTalkKeys.Next("Z", PushToTalkKeys.Candidates));
    }

    [Fact]
    public void Labels_NameTheKeysThatReadBadly()
    {
        Assert.Equal("ui.key.caps_lock", PushToTalkKeys.LabelKey("CapsLock"));
        Assert.Equal("ui.key.mouse_4", PushToTalkKeys.LabelKey("Mouse3"));
        Assert.Equal("ui.key.mouse_5", PushToTalkKeys.LabelKey("Mouse4"));
        Assert.Null(PushToTalkKeys.LabelKey("Z")); // a letter is its own label
    }
}
