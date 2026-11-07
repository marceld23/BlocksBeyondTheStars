// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Sounders;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The bodies and records of Soaniri (#3355, #3359, #3366): the body plans are appended (their numbers are persisted), the
/// four authored records load with their designed looks and tempers, a roster holds them only where a planet type lists them,
/// the body rules give the sizes the issues ask for (a Sounder 3–4 blocks, a knee-high Soundi, a ten-metre Soundrak, a giant
/// Quali), every body stays inside the footprint's bounds, the recorded voices are clean, and the light rule knows a lamp.
/// </summary>
public sealed class SoundersCreaturesTests
{
    private static readonly Lazy<GameContent> Shipped = new(() => ContentLoader.LoadFromDirectory(TestPaths.DataDir()));

    private static readonly string[] Keys = { SoundersIds.Soundi, SoundersIds.Soundrak, SoundersIds.Quali, SoundersIds.Soundeling };

    [Fact]
    public void TheBodyPlans_AreAppendedAfterReindeer_WithStableNumbers()
    {
        // Persisted as numbers (companion snapshots, the bio register): only ever appended.
        Assert.Equal(14, (int)CreatureBodyPlan.Reindeer);
        Assert.Equal(15, (int)CreatureBodyPlan.Sounder);
        Assert.Equal(16, (int)CreatureBodyPlan.Soundi);
        Assert.Equal(17, (int)CreatureBodyPlan.Soundrak);
        Assert.Equal(18, (int)CreatureBodyPlan.Soundeling);
        Assert.Equal(19, (int)CreatureBodyPlan.Quali);
        Assert.All(new[] { CreatureBodyPlan.Sounder, CreatureBodyPlan.Soundi, CreatureBodyPlan.Soundrak, CreatureBodyPlan.Soundeling, CreatureBodyPlan.Quali },
            p => Assert.True(SoundersFauna.IsSoundersPlan(p)));
        Assert.False(SoundersFauna.IsSoundersPlan(CreatureBodyPlan.Biped));
    }

    [Fact]
    public void TheFourRecords_LoadWithTheirDesign()
    {
        var a = Shipped.Value.AuthoredCreatures;
        var soundi = a[SoundersIds.Soundi];
        Assert.Equal(CreatureBodyPlan.Soundi, soundi.BodyPlan);
        Assert.Equal(SoundiRules.Fur, soundi.ColorRgb);
        Assert.Equal(SoundiRules.Mint, soundi.BellyRgb);
        Assert.Equal(0, soundi.Eyes); // no eyes at all
        Assert.True(soundi.HasTail);
        Assert.Equal(4, soundi.Legs);
        Assert.Equal(CreatureTemperament.Passive, soundi.Temperament);
        Assert.InRange(soundi.SocialGroupSize, SoundiRules.PackMin, SoundiRules.PackMax);
        Assert.True(soundi.DuelTame && soundi.PackDefender); // the companions pack's duel taming and pack defence

        var soundrak = a[SoundersIds.Soundrak];
        Assert.Equal(CreatureBodyPlan.Soundrak, soundrak.BodyPlan);
        Assert.Equal(CreatureTemperament.Territorial, soundrak.Temperament);
        Assert.Equal(1, soundrak.SocialGroupSize);
        Assert.Equal("leather", soundrak.Hide); // smooth leathery skin — no fur, nothing leafy

        var quali = a[SoundersIds.Quali];
        Assert.Equal(CreatureBodyPlan.Quali, quali.BodyPlan);
        Assert.Equal(CreatureHabitat.Air, quali.Habitat);
        Assert.Equal(CreatureTemperament.Passive, quali.Temperament);
        Assert.Equal(0f, quali.AttackDamage);
        Assert.True(quali.Glows);
        Assert.True(quali.HoverAltitude >= 20f, "a Quali drifts high in the sky");

        var soundeling = a[SoundersIds.Soundeling];
        Assert.Equal(CreatureBodyPlan.Soundeling, soundeling.BodyPlan);
        Assert.Equal(CreatureActivity.Diurnal, soundeling.Activity); // curious by day, curled up at night
        Assert.True(soundeling.Glows);
        Assert.InRange(soundeling.SocialGroupSize, 2, 4);
        Assert.All(Keys, k => Assert.True(a[k].FixedName));
    }

    [Fact]
    public void ARoster_HoldsThem_OnlyWhereTheTypeListsThem()
    {
        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        foreach (var planet in content.Planets.Values)
        {
            for (long seed = 1; seed <= 3; seed++)
            {
                var roster = CreatureGenerator.GenerateRoster(planet, seed * 7919, WorldDescription.CurrentTerrainGeneration, content.AuthoredCreaturesFor(planet));
                foreach (var sp in roster.Where(s => SoundersFauna.IsSoundersPlan(s.BodyPlan)))
                {
                    Assert.Contains(sp.Id.Substring("au_".Length), planet.AuthoredCreatures); // never rolled, only listed
                }
            }
        }

        // A type that lists them hosts all four.
        var host = content.Planets["meadowlands"];
        host.AuthoredCreatures.AddRange(Keys);
        try
        {
            var roster = CreatureGenerator.GenerateRoster(host, 4242, WorldDescription.CurrentTerrainGeneration, content.AuthoredCreaturesFor(host));
            foreach (string key in Keys)
            {
                var sp = Assert.Single(roster, s => s.Id == SoundersFauna.SpeciesIdOf(key));
                Assert.Equal(content.AuthoredCreatures[key].NamePrefix, sp.Name); // a fixed name on every world
            }
        }
        finally
        {
            host.AuthoredCreatures.RemoveAll(k => Keys.Contains(k));
        }
    }

    [Fact]
    public void TheSoundersWorld_WhenItListsThem_HostsAllFour()
    {
        var content = Shipped.Value;
        if (!content.Planets.TryGetValue(SoundersIds.PlanetType, out var soaniri) || soaniri.AuthoredCreatures.Count == 0)
        {
            return; // the world pack adds the type; until then there is nothing to host them
        }

        var roster = CreatureGenerator.GenerateRoster(soaniri, 77, WorldDescription.CurrentTerrainGeneration, content.AuthoredCreaturesFor(soaniri));
        Assert.All(Keys, k => Assert.Contains(roster, s => s.Id == SoundersFauna.SpeciesIdOf(k)));
    }

    [Fact]
    public void TheSounder_StandsThreeToFourBlocksTall_WithFourArms()
    {
        Assert.InRange(SounderRules.HeightFor(SounderRules.MinSize), 2.9f, 3.1f);
        Assert.InRange(SounderRules.HeightFor(SounderRules.MaxSize), 3.9f, 4.0f);
        Assert.Equal(SounderRules.BaseHeight, SounderRules.HeightFor(0f)); // unset reads as 1
        Assert.Equal(SounderRules.BaseHeight, SounderRules.HeightFor(float.NaN));
        Assert.InRange(SounderRules.LegShare + SounderRules.TorsoShare + SounderRules.NeckShare + SounderRules.HeadShare, 0.999f, 1.001f);
        Assert.True(SounderRules.EarTipFor(1f) > SounderRules.HeightFor(1f));
        Assert.Equal(4, SounderRules.Arms);
        Assert.Equal(4, SounderRules.BodyHeightCells(1f, 2, 8)); // 3.3 blocks → four cells
        Assert.Equal(3, SounderRules.BodyHeightCells(1f, 2, 3));
        for (int seed = -50; seed < 50; seed++)
        {
            Assert.InRange(SounderRules.SizeFor(seed), SounderRules.MinSize, SounderRules.MaxSize);
            var (skin, sash) = SounderRules.LookFor(seed);
            Assert.Contains(skin, SounderRules.SkinTones);
            Assert.Contains(sash, SounderRules.SashTones);
            Assert.Equal(SounderRules.LookFor(seed), SounderRules.LookFor(seed)); // the same NPC, the same colours
        }

        Assert.True(SounderRules.IsSounderLook(SoundersIds.Sounder) && SounderRules.IsSounderLook(SoundersIds.Echo));
        Assert.False(SounderRules.IsSounderLook("pixel") || SounderRules.IsSounderLook(null));
        Assert.True(SounderRules.IsEcho(SoundersIds.Echo) && !SounderRules.IsEcho(SoundersIds.Sounder));
    }

    [Fact]
    public void TheRecordedSizes_GiveAKneeHighSoundi_ATenMetreSoundrak_AndAGiantQuali()
    {
        var a = Shipped.Value.AuthoredCreatures;
        Assert.InRange(SoundiRules.HeightFor(a[SoundersIds.Soundi].Size), 0.6f, 1.05f);         // knee-high (the ears included)
        Assert.True(SoundiRules.EarHeight > SoundiRules.HeadEdge);                                // the ears are taller than the head
        Assert.InRange(SoundrakRules.HeightFor(a[SoundersIds.Soundrak].Size), 9.5f, 10.5f);      // about ten metres
        Assert.InRange(QualiRules.BellRadius(a[SoundersIds.Quali].Size), 4f, 7f);                // a bell about ten blocks across
        Assert.True(a[SoundersIds.Soundeling].Size <= 0.5f);                                     // a small fluff ball
    }

    [Fact]
    public void EveryBody_FitsTheFootprintBounds_AndTheUprightOnesReachNoHeadIntoAWall()
    {
        var content = Shipped.Value;
        var host = content.Planets["meadowlands"];
        foreach (string key in Keys)
        {
            var sp = CreatureGenerator.MakeAuthored(content.AuthoredCreatures[key], host, 99);
            for (float scale = 0.7f; scale <= 1.31f; scale += 0.3f)
            {
                float r = CreatureFootprint.Radius(sp, scale);
                Assert.InRange(r, 0.05f, CreatureFootprint.MaxRadius);
            }
        }

        var sounder = new CreatureSpecies { BodyPlan = CreatureBodyPlan.Sounder, Size = 1f };
        Assert.InRange(CreatureFootprint.Radius(sounder, 1f), SounderRules.FootprintRadius(1f) - 0.001f, SounderRules.FootprintRadius(1f) + 0.001f);
        Assert.Equal(0f, CreatureFootprint.HeadReach(sounder, 1f));
        Assert.Equal(0f, CreatureFootprint.HeadReach(new CreatureSpecies { BodyPlan = CreatureBodyPlan.Soundrak, Size = 5.6f }, 1f));
        Assert.Equal(0f, CreatureFootprint.HeadReach(new CreatureSpecies { BodyPlan = CreatureBodyPlan.Quali, Size = 7f }, 1f));
    }

    [Fact]
    public void TheRecordedVoices_AreClean_SingleCalls_FromTheirOwnPools()
    {
        var content = Shipped.Value;
        var host = content.Planets["meadowlands"];
        (string Key, string Prefix, string Descriptor)[] cases =
        {
            (SoundersIds.Soundi, "soundi_call_", "ui.scan.voice.sings"),
            (SoundersIds.Soundrak, "soundrak_scream_", "ui.scan.voice.scream"),
            (SoundersIds.Quali, "quali_song_", "ui.scan.voice.song"),
            (SoundersIds.Soundeling, "soundeling_coo_", "ui.scan.voice.coo"),
        };
        foreach (var (key, prefix, descriptor) in cases)
        {
            for (long seed = 1; seed <= 20; seed++)
            {
                var sp = CreatureGenerator.MakeAuthored(content.AuthoredCreatures[key], host, seed * 31);
                var v = CreatureVoices.Derive(sp.VoiceSeed, VoiceTraits.From(sp));
                Assert.StartsWith(prefix, v.Call, StringComparison.Ordinal);
                Assert.StartsWith(prefix, v.AlarmCall, StringComparison.Ordinal);
                Assert.Equal(VoiceOp.None, v.Op);       // the series' recordings stay as they are
                Assert.Equal(string.Empty, v.LayerCall);
                Assert.Equal(1, v.Pulses);
                Assert.Equal(descriptor, CreatureVoices.DescriptorKey(v));
                Assert.True(CreatureVoices.KeepsNaturalPitch(sp.BodyPlan.ToString()));
            }
        }

        Assert.Equal(14, CreatureVoices.Repertoire("soundi_call_07")!.Count);
        Assert.Equal(3, CreatureVoices.Repertoire("soundrak_scream_2")!.Count);
        Assert.Null(CreatureVoices.Repertoire("creature_call_bark"));
        Assert.All(new[] { "soundi_call_01", "soundi_call_14", "soundrak_scream_3", "quali_song_2", "soundeling_coo_1" },
            c => Assert.Contains(c, CreatureVoices.AllCalls));
        Assert.False(CreatureVoices.KeepsNaturalPitch("Biped"));
    }

    [Fact]
    public void TheSoundIdFamilies_StayWithinTheContract()
    {
        for (int roll = -10; roll < 40; roll++)
        {
            Assert.Matches("^soundrak_scream_[123]$", SoundrakRules.ScreamSound(roll));
            Assert.Matches("^soundeling_coo_[123]$", SoundelingRules.CooSound(roll));
        }

        Assert.InRange(SoundrakRules.ScreamGap(0), SoundrakRules.ScreamCooldownMin, SoundrakRules.ScreamCooldownMin);
        Assert.InRange(SoundrakRules.ScreamGap(1), SoundrakRules.ScreamCooldownMax, SoundrakRules.ScreamCooldownMax);
        Assert.InRange(SoundrakRules.ScreamGap(7), SoundrakRules.ScreamCooldownMax, SoundrakRules.ScreamCooldownMax);
    }

    [Fact]
    public void TheCapsAndTheUntouchable_FollowTheBodyPlan()
    {
        Assert.Equal(1, SoundersFauna.MaxWildAtOnce(new CreatureSpecies { BodyPlan = CreatureBodyPlan.Soundrak }));
        Assert.Equal(QualiRules.MaxWildAtOnce, SoundersFauna.MaxWildAtOnce(new CreatureSpecies { BodyPlan = CreatureBodyPlan.Quali }));
        Assert.Equal(int.MaxValue, SoundersFauna.MaxWildAtOnce(new CreatureSpecies { BodyPlan = CreatureBodyPlan.Soundi }));
        Assert.True(SoundersFauna.Untouchable(new CreatureSpecies { BodyPlan = CreatureBodyPlan.Quali }));
        Assert.False(SoundersFauna.Untouchable(new CreatureSpecies { BodyPlan = CreatureBodyPlan.Soundrak }));
        Assert.False(SoundersFauna.Untouchable(null));

        // The giants vary only a third as much as the bell of 0.7–1.3; everyone else keeps its roll.
        var rak = new CreatureSpecies { BodyPlan = CreatureBodyPlan.Soundrak };
        Assert.InRange(SoundersFauna.SizeScaleFor(rak, 1.3f), 1.09f, 1.11f);
        Assert.InRange(SoundersFauna.SizeScaleFor(rak, 0.7f), 0.89f, 0.91f);
        Assert.Equal(1.3f, SoundersFauna.SizeScaleFor(new CreatureSpecies { BodyPlan = CreatureBodyPlan.Soundi }, 1.3f));
    }

    [Fact]
    public void ALamp_IsALight_ForASoundeling_AndFoodOrStoneIsNot()
    {
        var content = Shipped.Value;
        bool Light(string item)
        {
            var def = content.GetItem(item);
            return SoundelingRules.IsLightItem(def, def?.PlacesBlock is { Length: > 0 } key ? content.GetBlock(key) : null);
        }

        Assert.True(Light("torch"));
        Assert.True(Light("lantern"));
        Assert.False(Light("berries"));
        Assert.False(Light("stone"));
        Assert.False(SoundelingRules.IsLightItem(null, null));

        Assert.True(SoundelingRules.TakesLight(night: false, curled: false, carrying: false));
        Assert.False(SoundelingRules.TakesLight(night: true, curled: false, carrying: false));   // by day only
        Assert.False(SoundelingRules.TakesLight(night: false, curled: true, carrying: false));   // not while it hides
        Assert.False(SoundelingRules.TakesLight(night: false, curled: false, carrying: true));   // one light at a time
        Assert.True(SoundelingRules.IsDark(openSky: false, lit: false));
        Assert.False(SoundelingRules.IsDark(openSky: true, lit: false));
        Assert.False(SoundelingRules.IsDark(openSky: false, lit: true));
        Assert.True(SoundelingRules.Lights(3, 9) && !SoundelingRules.Lights(7, 9) && !SoundelingRules.Lights(3, 2));
    }

    [Fact]
    public void EveryNewText_HasAnEnglishAndAGermanLine()
    {
        var en = TestLocales.Load("en");
        var de = TestLocales.Load("de");
        string[] keys =
        {
            "npc.role.sounder", "npc.role.sounder_echo", "npc.sounder.hum",
            "ui.scan.voice.sings", "ui.scan.voice.scream", "ui.scan.voice.song", "ui.scan.voice.coo",
            "ui.scan.body.soundi", "ui.scan.body.soundrak", "ui.scan.body.quali", "ui.scan.behaviour.carries_light",
            "ui.hud.give_light", "vega.hint.soundrak", "vega.hint.soundeling_light", "srv.attack.quali",
            "srv.soundeling.sleeping", "srv.soundeling.busy", "srv.soundeling.no_dark",
        };
        foreach (string key in keys)
        {
            Assert.True(en.ContainsKey(key), "en: " + key);
            Assert.True(de.ContainsKey(key), "de: " + key);
        }
    }
}
