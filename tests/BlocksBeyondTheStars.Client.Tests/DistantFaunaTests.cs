// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3245 distant fauna: the world's own species call from far away now and then — one call every 20–60 s, never in rain,
/// under a roof, in a vacuum, in space or a cutscene, from a species that fits the hour and the spot, not one already within
/// hearing, never the same twice in a row while another may call, near-silent bodies only rarely, 40–120 blocks out and
/// quieter the farther. Seeded, so every run is the same.
/// </summary>
[Trait("Suite", "ClientCore")]
public sealed class DistantFaunaTests
{
    private const float Step = 0.25f;

    private static NetFaunaSpecies Sp(string id, string habitat = "Land", string activity = "Cathemeral",
        string bodyPlan = "Standard", float size = 1f, int tentacles = 0)
        => new() { SpeciesId = id, Habitat = habitat, Activity = activity, BodyPlan = bodyPlan, Size = size, Tentacles = tentacles };

    private static DistantFaunaConditions Open(float daylight = 0.8f, bool nearWater = false, bool underground = false,
        Func<string, bool>? nearby = null)
        => new(daylight, raining: false, storm: false, roofed: false, vacuum: false, inSpace: false, cutscene: false,
            nearWater, underground, nearby);

    private static DistantFaunaConditions Silenced(string why) => new(0.8f,
        raining: why == "rain", storm: why == "storm", roofed: why == "roof", vacuum: why == "vacuum",
        inSpace: why == "space", cutscene: why == "cutscene", nearWater: false, underground: false);

    /// <summary>Ticks <paramref name="fauna"/> from <paramref name="from"/> for <paramref name="seconds"/> and returns every
    /// call with its time.</summary>
    private static List<(float At, DistantCall Call)> Run(DistantFauna fauna, float seconds, DistantFaunaConditions c, float from = 0f)
    {
        var calls = new List<(float, DistantCall)>();
        for (float t = from; t < from + seconds; t += Step)
        {
            if (fauna.Tick(t, c) is { } call)
            {
                calls.Add((t, call));
            }
        }

        return calls;
    }

    private static DistantFauna With(int seed, params NetFaunaSpecies[] roster)
    {
        var fauna = new DistantFauna(seed);
        fauna.SetRoster(roster);
        return fauna;
    }

    private static int Count(List<(float At, DistantCall Call)> calls, string id) => calls.Count(x => x.Call.Species.SpeciesId == id);

    [Fact]
    public void ACall_ComesEvery20To60Seconds_TheFirstAFullIntervalAfterTheRoster()
    {
        var calls = Run(With(1, Sp("a"), Sp("b"), Sp("c")), 3600f, Open());

        Assert.InRange(calls[0].At, DistantFauna.MinIntervalSeconds, DistantFauna.MaxIntervalSeconds + Step);
        for (int i = 1; i < calls.Count; i++)
        {
            Assert.InRange(calls[i].At - calls[i - 1].At, DistantFauna.MinIntervalSeconds, DistantFauna.MaxIntervalSeconds + Step);
        }

        Assert.InRange(calls.Count, 3600 / 61, 3600 / 20); // every slot calls: three species awake at every hour
    }

    [Fact]
    public void TheSameSeed_GivesTheSameCalls()
    {
        var first = Run(With(7, Sp("a"), Sp("b", size: 2f), Sp("c", size: 0.5f)), 1200f, Open());
        var second = Run(With(7, Sp("a"), Sp("b", size: 2f), Sp("c", size: 0.5f)), 1200f, Open());

        Assert.Equal(
            first.Select(x => (x.At, x.Call.Species.SpeciesId, x.Call.AngleRadians, x.Call.Distance, x.Call.Volume01)),
            second.Select(x => (x.At, x.Call.Species.SpeciesId, x.Call.AngleRadians, x.Call.Distance, x.Call.Volume01)));
    }

    [Theory]
    [InlineData("rain")]
    [InlineData("storm")]
    [InlineData("roof")]
    [InlineData("vacuum")]
    [InlineData("space")]
    [InlineData("cutscene")]
    public void NothingCalls_InRainUnderARoofInAVacuumInSpaceOrACutscene_AndTheHeldCallComesShortlyAfter(string why)
    {
        var fauna = With(3, Sp("a"), Sp("b"));
        Assert.Empty(Run(fauna, 600f, Silenced(why)));

        // The call that came due meanwhile waited: it comes a short grace after the silence lifts, not on cue.
        var after = Run(fauna, 60f, Open(), from: 600f);
        Assert.NotEmpty(after);
        Assert.InRange(after[0].At, 600f + DistantFauna.MinGraceSeconds, 600f + DistantFauna.MaxGraceSeconds + Step);
    }

    [Fact]
    public void NocturnalSpecies_CallMostlyAtNight_DiurnalOnesByDay()
    {
        var night = Run(With(5, Sp("owl", activity: "Nocturnal"), Sp("lark", activity: "Diurnal")), 20000f, Open(daylight: 0f));
        var day = Run(With(5, Sp("owl", activity: "Nocturnal"), Sp("lark", activity: "Diurnal")), 20000f, Open(daylight: 1f));

        Assert.True(Count(night, "owl") > 3 * Count(night, "lark"), $"night: owl {Count(night, "owl")}, lark {Count(night, "lark")}");
        Assert.True(Count(day, "lark") > 3 * Count(day, "owl"), $"day: lark {Count(day, "lark")}, owl {Count(day, "owl")}");
        Assert.True(Count(night, "lark") > 0, "a sleeper may still call now and then — 'mostly', not 'only'");
    }

    [Fact]
    public void CrepuscularSpecies_CallAtDawnAndDusk()
    {
        var twilight = Run(With(9, Sp("bat", activity: "Crepuscular")), 7200f, Open(daylight: 0.3f));
        var noon = Run(With(9, Sp("bat", activity: "Crepuscular")), 7200f, Open(daylight: 1f));
        var midnight = Run(With(9, Sp("bat", activity: "Crepuscular")), 7200f, Open(daylight: 0f));

        Assert.True(twilight.Count > 3 * noon.Count, $"twilight {twilight.Count}, noon {noon.Count}");
        Assert.True(twilight.Count > 3 * midnight.Count, $"twilight {twilight.Count}, midnight {midnight.Count}");
    }

    [Fact]
    public void WaterAndShoreSpecies_CallOnlyNearWater()
    {
        NetFaunaSpecies[] roster = { Sp("fish", habitat: "Water"), Sp("frog", habitat: "Amphibian"), Sp("deer") };

        var inland = Run(With(2, roster), 3600f, Open());
        Assert.NotEmpty(inland);
        Assert.All(inland, x => Assert.Equal("deer", x.Call.Species.SpeciesId));

        var shore = Run(With(2, roster), 3600f, Open(nearWater: true));
        Assert.All(new[] { "fish", "frog", "deer" }, id => Assert.True(Count(shore, id) > 0, id + " never called by the water"));
    }

    [Fact]
    public void Underground_OnlyCaveSpeciesCall_AndCaveSpeciesOnlyThere()
    {
        NetFaunaSpecies[] roster = { Sp("mole", habitat: "Cave"), Sp("deer"), Sp("crow", habitat: "Air") };

        var cave = Run(With(4, roster), 3600f, Open(underground: true, nearWater: true));
        Assert.NotEmpty(cave);
        Assert.All(cave, x => Assert.Equal("mole", x.Call.Species.SpeciesId));

        var surface = Run(With(4, roster), 3600f, Open());
        Assert.NotEmpty(surface);
        Assert.DoesNotContain(surface, x => x.Call.Species.SpeciesId == "mole");

        Assert.Empty(Run(With(4, Sp("deer"), Sp("crow", habitat: "Air")), 3600f, Open(underground: true))); // no cave life: silence
    }

    [Fact]
    public void ASpeciesAlreadyWithinHearing_DoesNotCallFromAfar()
    {
        var calls = Run(With(6, Sp("near"), Sp("far")), 3600f, Open(nearby: id => id == "near"));
        Assert.NotEmpty(calls);
        Assert.All(calls, x => Assert.Equal("far", x.Call.Species.SpeciesId));

        Assert.Empty(Run(With(6, Sp("near"), Sp("far")), 3600f, Open(nearby: _ => true)));
    }

    [Fact]
    public void NeverTheSameSpeciesTwiceInARow_WhileAnotherMayCall()
    {
        var calls = Run(With(8, Sp("a"), Sp("b"), Sp("c")), 7200f, Open());
        for (int i = 1; i < calls.Count; i++)
        {
            Assert.NotEqual(calls[i - 1].Call.Species.SpeciesId, calls[i].Call.Species.SpeciesId);
        }

        // A world with a single species: it is the only one that fits, so it calls again.
        Assert.True(Run(With(8, Sp("solo")), 600f, Open()).Count > 5);
    }

    [Fact]
    public void NearSilentBodies_CallOnlyRarely()
    {
        var calls = Run(With(10, Sp("jelly", bodyPlan: "Medusa"), Sp("squid", tentacles: 6), Sp("deer")), 36000f, Open());

        int quiet = Count(calls, "jelly") + Count(calls, "squid");
        Assert.True(quiet > 0, "a near-silent body still calls now and then");
        Assert.True(quiet * 3 < Count(calls, "deer"), $"quiet {quiet}, deer {Count(calls, "deer")}");

        // Alone on a world, a medusa leaves most slots silent.
        var alone = Run(With(10, Sp("jelly", bodyPlan: "Medusa")), 36000f, Open());
        Assert.InRange(alone.Count, 1, 36000 / 60 / 2);
    }

    [Fact]
    public void TheCaller_StandsFarOut_AndTheFartherTheQuieter()
    {
        var calls = Run(With(11, Sp("mouse", size: 0.5f), Sp("bear", size: 2.5f), Sp("dog", size: 1.2f)), 36000f, Open());

        Assert.All(calls, x =>
        {
            Assert.InRange(x.Call.Distance, DistantFauna.MinDistance, DistantFauna.MaxDistance);
            Assert.InRange(x.Call.AngleRadians, 0f, (float)(2 * Math.PI));
            Assert.InRange(x.Call.Volume01, DistantFauna.FarVolume, DistantFauna.NearVolume);
        });

        var byDistance = calls.Select(x => x.Call).OrderBy(c => c.Distance).ToList();
        for (int i = 1; i < byDistance.Count; i++)
        {
            Assert.True(byDistance[i].Volume01 <= byDistance[i - 1].Volume01, "a farther call must not sound louder");
        }

        var mouse = calls.Where(x => x.Call.Species.SpeciesId == "mouse").Select(x => x.Call.Distance).ToList();
        var bear = calls.Where(x => x.Call.Species.SpeciesId == "bear").Select(x => x.Call.Distance).ToList();
        Assert.All(mouse, d => Assert.True(d <= DistantFauna.SmallMaxDistance, $"a mouse heard from {d} blocks"));
        Assert.Contains(bear, d => d > 100f); // a big animal carries to the far end
        Assert.True(bear.Average() > mouse.Average());

        // The bearings go all the way round.
        Assert.Contains(calls, x => x.Call.AngleRadians < Math.PI / 2);
        Assert.Contains(calls, x => x.Call.AngleRadians > 3 * Math.PI / 2);
    }

    [Fact]
    public void AnEmptyRoster_NeverCalls_AndANewRosterStartsOver()
    {
        var fauna = With(12);
        Assert.Empty(Run(fauna, 600f, Open()));

        fauna.SetRoster(new[] { Sp("a"), Sp("b") });
        var calls = Run(fauna, 600f, Open(), from: 1000f);
        Assert.NotEmpty(calls);
        Assert.True(calls[0].At >= 1000f + DistantFauna.MinIntervalSeconds, "the first call waits a full interval after the roster");

        fauna.SetRoster(Array.Empty<NetFaunaSpecies>()); // a station deck: no animals
        Assert.Empty(Run(fauna, 600f, Open(), from: 2000f));
    }
}
