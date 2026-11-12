// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>#3518/#3523: a design and a generator entry share a user-content folder — a save never overwrites a file of the
/// other kind under the same key.</summary>
public sealed class UserTemplateFilesTests
{
    private const string Design = """{ "key": "home", "tier": "village", "cells": [ { "x": 0, "y": 0, "z": 0, "id": "stone" } ] }""";
    private const string Entry = """{ "key": "home", "tier": "village", "source": "bandit_camp", "sourceParams": "" }""";

    [Fact]
    public void AFileNamingASource_IsAnEntry_AnyOtherTemplateIsADesign()
    {
        Assert.True(UserTemplateFiles.IsEntry(Entry));
        Assert.False(UserTemplateFiles.IsEntry(Design));
        Assert.False(UserTemplateFiles.IsEntry("""{ "key": "home", "source": "" }""")); // an empty source: the editor's design shape
        Assert.True(UserTemplateFiles.IsEntry("""{ "Source": "monument" }"""));
        Assert.Null(UserTemplateFiles.IsEntry("not json"));
        Assert.Null(UserTemplateFiles.IsEntry(null));
    }

    [Fact]
    public void ASave_IsRefusedOverAFileOfTheOtherKind_Only()
    {
        Assert.True(UserTemplateFiles.HoldsOtherKind(Design, savingEntry: true));  // an entry over a design
        Assert.True(UserTemplateFiles.HoldsOtherKind(Entry, savingEntry: false));  // a design over an entry
        Assert.False(UserTemplateFiles.HoldsOtherKind(Entry, savingEntry: true));  // re-saving the entry
        Assert.False(UserTemplateFiles.HoldsOtherKind(Design, savingEntry: false)); // re-saving the design
        Assert.False(UserTemplateFiles.HoldsOtherKind(null, savingEntry: true));    // nothing there yet
        Assert.False(UserTemplateFiles.HoldsOtherKind("{ broken", savingEntry: false));
    }
}
