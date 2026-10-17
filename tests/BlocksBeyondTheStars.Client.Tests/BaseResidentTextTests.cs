// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #2717: the base core screen's "who lives here" text — a title with the count, the bed rule while there is room (#2715),
/// then one line per resident with name, role, what they do, where they are from the core (or "not here right now") and
/// whether they have a bed. Checked against the real English and German tables.
/// </summary>
public sealed class BaseResidentTextTests
{
    private static Func<string, string> Localizer(string code)
    {
        var en = VegaLineBudgetTests.Core("en");
        var table = VegaLineBudgetTests.Core(code);
        return key => table.TryGetValue(key, out var v) ? v : en.TryGetValue(key, out var e) ? e : "[" + key + "]";
    }

    private static BaseResidentList TwoResidents() => new()
    {
        BodyId = "sys0-p1",
        BaseName = "Home",
        Beds = 1,
        Cap = 5,
        People = new[]
        {
            new NetBaseResident
            {
                Name = "Mila", RoleKey = "npc.role.craftsman", ActivityKey = "npc.activity.working", Here = true,
                DirectionKey = "dir.ne", Distance = 12, HasBed = false, Founder = true,
            },
            new NetBaseResident
            {
                Name = "Jonas", RoleKey = "npc.theme.settlers", Here = false, HasBed = true,
            },
        },
    };

    [Fact]
    public void English_ListsEveryone_WithTheBedRule()
    {
        string text = BaseResidentText.Format(TwoResidents(), Localizer("en"));
        Assert.Equal(
            "Who lives here — 2/5\n"
            + "Every bed inside your base brings one more resident — up to 5.\n"
            + "• Mila (first to move in) — Craftsman · working · 12 m to the northeast · no bed yet\n"
            + "• Jonas — Settler · not here right now · has a bed",
            text);
    }

    [Fact]
    public void German_ReadsTheSame_InGerman()
    {
        string text = BaseResidentText.Format(TwoResidents(), Localizer("de"));
        Assert.Contains("Wer hier wohnt — 2/5", text, StringComparison.Ordinal);
        Assert.Contains("Jedes Bett in deiner Basis bringt einen Bewohner mehr — bis zu 5.", text, StringComparison.Ordinal);
        Assert.Contains("• Mila (zuerst eingezogen) — Handwerker · arbeitet · 12 m Richtung Nordosten · noch kein Bett", text, StringComparison.Ordinal);
        Assert.Contains("• Jonas — Siedler · gerade nicht da · hat ein Bett", text, StringComparison.Ordinal);
        Assert.DoesNotContain("[", text, StringComparison.Ordinal); // every key resolved
    }

    [Fact]
    public void AFullBase_DropsTheBedRule_AndSomeoneByTheCore_HasNoDirection()
    {
        var list = TwoResidents();
        list.Cap = 2;
        list.People[0].DirectionKey = string.Empty;
        list.People[0].Distance = 1;
        string text = BaseResidentText.Format(list, Localizer("en"));
        Assert.DoesNotContain("Every bed", text, StringComparison.Ordinal);
        Assert.Contains("· working · right by the base core ·", text, StringComparison.Ordinal);
    }

    [Fact]
    public void NobodyYet_SaysWhatBringsTheFirstSettler()
    {
        string text = BaseResidentText.Format(new BaseResidentList { Cap = 5 }, Localizer("en"));
        Assert.Equal("Who lives here — 0/5\nNobody lives here yet — a bed or a few machines inside your base bring the first settler.", text);
    }
}
