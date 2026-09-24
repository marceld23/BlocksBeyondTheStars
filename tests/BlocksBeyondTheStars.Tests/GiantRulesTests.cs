// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The giants' pure rules (#1998–#2001): which worlds host a colossus or sandworms, the procedural giants themselves,
/// the colossus body layout the client builds and the server hits, the sandworm's shared path, and the vibration model.
/// </summary>
public sealed class GiantRulesTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    // ---------------- world gates ----------------

    [Theory]
    [InlineData("salt_flats", true)]
    [InlineData("swamp", true)]
    [InlineData("dust_bowl", true)]
    [InlineData("river_lowlands", true)]
    [InlineData("highland", false)]  // mountains
    [InlineData("savanna", false)]   // hills in its pool
    [InlineData("varied", false)]    // no styles: the archetype blend
    [InlineData("desert", false)]    // badlands, labyrinth
    [InlineData("sand_sea", false)]  // mountains and canyons around the sea
    public void VeryFlat_IsPlainsDownsAndDunesWithASmallAmplitude(string key, bool flat)
    {
        Assert.Equal(flat, GiantRules.IsVeryFlat(Content.GetPlanet(key)!));
    }

    [Fact]
    public void Colossus_NeedsGenerationNine_AFlatType_LowGravity_Fauna_AndTheRarityRoll()
    {
        var flat = Content.GetPlanet("salt_flats")!;
        var steep = Content.GetPlanet("highland")!;
        var lifeless = Content.GetPlanet("scrapyard")!; // flat, but no fauna
        // Find a location whose rarity roll hosts one, and one whose does not.
        string? yes = null, no = null;
        for (int i = 0; i < 200 && (yes is null || no is null); i++)
        {
            string id = "sys0-p" + i + "-m0";
            if (GiantRules.RarityRoll(42, id))
            {
                yes ??= id;
            }
            else
            {
                no ??= id;
            }
        }

        Assert.NotNull(yes);
        Assert.NotNull(no);
        Assert.True(GiantRules.HostsColossus(flat, 0.6f, 9, 42, yes!));
        Assert.False(GiantRules.HostsColossus(flat, 0.6f, 9, 42, no!));        // the roll says no
        Assert.False(GiantRules.HostsColossus(flat, 0.6f, 8, 42, yes!));       // an older world never
        Assert.False(GiantRules.HostsColossus(flat, 0.9f, 9, 42, yes!));       // too heavy
        Assert.False(GiantRules.HostsColossus(steep, 0.6f, 9, 42, yes!));      // not flat
        Assert.False(GiantRules.HostsColossus(lifeless, 0.6f, 9, 42, yes!));   // no fauna
    }

    [Fact]
    public void Colossus_IsRare_AboutOneInThreeQualifyingWorlds()
    {
        int hosts = Enumerable.Range(0, 3000).Count(i => GiantRules.RarityRoll(777, "sys" + i / 10 + "-p" + i % 10 + "-m0"));
        Assert.InRange(hosts / 3000.0, 0.28, 0.39);
    }

    [Fact]
    public void Sandworms_LiveOnSandSeaWorldsOfGenerationNineOnly()
    {
        var sea = Content.GetPlanet("sand_sea")!;
        Assert.True(GiantRules.HostsSandworms(sea, 9));
        Assert.False(GiantRules.HostsSandworms(sea, 8));
        Assert.False(GiantRules.HostsSandworms(Content.GetPlanet("desert")!, 9));
        Assert.Equal(1, GiantRules.SandwormCount(4000));
        Assert.Equal(2, GiantRules.SandwormCount(9000));
    }

    // ---------------- the procedural giants ----------------

    [Fact]
    public void Colossus_IsDeterministic_SixtyBlocksAtMost_ToughAndSlowerThanAWalk()
    {
        for (int i = 0; i < 40; i++)
        {
            var a = CreatureGenerator.GenerateColossus(1000 + i, "sys1-p2-m0");
            var b = CreatureGenerator.GenerateColossus(1000 + i, "sys1-p2-m0");
            Assert.Equal(a.Name, b.Name);
            Assert.Equal(a.GiantHeight, b.GiantHeight);
            Assert.Equal(a.Temperament, b.Temperament);
            Assert.Equal(CreatureBodyPlan.Colossus, a.BodyPlan);
            Assert.True(a.IsGiant);
            Assert.InRange(a.GiantHeight, 40f, 60f);
            Assert.InRange(a.MaxHealth, 3000f, 4500f);
            Assert.True(a.Speed < 6f, "a colossus must always be escapable on foot");
            Assert.NotEqual(CreatureTemperament.PackHunter, a.Temperament);
        }

        // Procedural: different worlds roll different giants.
        var looks = Enumerable.Range(0, 40).Select(i => CreatureGenerator.GenerateColossus(i, "w" + i))
            .Select(s => (s.Temperament, s.BackFeature, s.NeckLength, s.ColorRgb)).Distinct().Count();
        Assert.True(looks > 20, $"only {looks} distinct colossi in 40 worlds");
        Assert.True(Enumerable.Range(0, 60).Select(i => CreatureGenerator.GenerateColossus(i, "t").Temperament).Distinct().Count() >= 3);
    }

    [Fact]
    public void Sandworm_IsASandwormEveryTime_WithRolledVariations()
    {
        for (int i = 0; i < 40; i++)
        {
            var w = CreatureGenerator.GenerateSandworm(500 + i, "sys3-p1");
            Assert.Equal(CreatureBodyPlan.Sandworm, w.BodyPlan);
            Assert.Equal(0, w.Legs);
            Assert.InRange(w.GiantHeight, 40f, 60f);
            Assert.InRange(w.Mandibles, 3, 5);
            Assert.InRange(w.WormGirth, 7f, 11f);
            Assert.True(w.WormLength > w.GiantHeight * 2f, "the body must be long enough to rear its full height");
            Assert.InRange(w.Hearing, 0.8f, 1.3f);
            Assert.InRange(w.MaxHealth, 2500f, 4000f);
        }
    }

    // ---------------- the colossus body ----------------

    [Theory]
    [InlineData(40f, 0.8f, 0)]
    [InlineData(52f, 1.0f, 2)]
    [InlineData(60f, 1.3f, 4)]
    public void ColossusBody_TopIsTheSpeciesHeight_AndTheHipsAreMirrored(float height, float legRatio, int neck)
    {
        var body = ColossusBody.For(height, legRatio, neck);
        Assert.Equal(height, body.Top, 3);
        Assert.True(body.LegLength > height * 0.2f);
        var (x0, y0, z0) = body.Hip(0);
        var (x1, y1, z1) = body.Hip(1);
        var (x2, _, z2) = body.Hip(2);
        Assert.Equal(-x0, x1, 4);      // left / right
        Assert.Equal(z0, z1, 4);
        Assert.Equal(-z0, z2, 4);      // front / rear
        Assert.Equal(body.LegLength, y0, 4);
        Assert.True(z0 > 0f, "leg 0 is a FRONT leg");
        Assert.True(x0 < 0f, "leg 0 is a LEFT leg");
    }

    [Fact]
    public void ColossusBody_ToWorld_RunsLocalForwardAlongTheFacing()
    {
        var at = new Vector3f(100f, 50f, 200f);
        var forward = ColossusBody.ToWorld(at, 0f, 0f, 0f, 10f);      // facing +X
        Assert.Equal(110f, forward.X, 3);
        Assert.Equal(200f, forward.Z, 3);
        var right = ColossusBody.ToWorld(at, 0f, 10f, 0f, 0f);        // its right is -Z (the Unity convention)
        Assert.Equal(100f, right.X, 3);
        Assert.Equal(190f, right.Z, 3);
        var north = ColossusBody.ToWorld(at, (float)(System.Math.PI / 2), 0f, 0f, 10f); // facing +Z
        Assert.Equal(100f, north.X, 3);
        Assert.Equal(210f, north.Z, 3);
    }

    [Fact]
    public void ColossusBody_Capsules_CoverLegsTorsoAndEveryHead()
    {
        var body = ColossusBody.For(50f, 1f, 2);
        var caps = body.Capsules(new Vector3f(0f, 0f, 0f), 0f, heads: 3);
        Assert.Equal(4 + 1 + 3, caps.Count);
        // A point on the ground under a hip is inside that leg.
        var (hx, _, hz) = body.Hip(0);
        var foot = ColossusBody.ToWorld(new Vector3f(0f, 0f, 0f), 0f, hx, 1f, hz);
        Assert.True(caps.Take(4).Min(c => c.Distance(foot)) <= 0f);
    }

    // ---------------- the sandworm path ----------------

    [Fact]
    public void Breach_RisesOutOfTheSand_ToItsPeak_AndGoesBackUnder()
    {
        var anchor = new Vector3f(0f, 70f, 0f);
        var path = SandwormPath.Build(WormMove.Breach, anchor, 1f, 0f, peak: 50f, length: 120f, girth: 9f);
        Assert.True(path.Duration > 3f);
        Assert.True(path.HeadAt(0f).Y < anchor.Y, "a breach starts under the sand");
        Assert.False(path.Exposed(0f));
        float top = Enumerable.Range(0, 200).Select(i => path.HeadAt(path.Duration * i / 199f).Y).Max();
        Assert.InRange(top, anchor.Y + 40f, anchor.Y + 55f);
        Assert.Contains(Enumerable.Range(0, 50), i => path.Exposed(path.Duration * i / 49f));
        Assert.False(path.Exposed(path.Duration), "a breach ends with the whole body back under the sand");
        Assert.Equal(-1f, path.StrikeTime);
    }

    [Fact]
    public void Rear_HoldsAtTheTop_ThenTheHeadComesDownOnItsTarget()
    {
        var target = new Vector3f(40f, 70f, -12f);
        var path = SandwormPath.Build(WormMove.Rear, target, 0f, 1f, peak: 55f, length: 130f, girth: 10f, strikeDistance: 24f);
        Assert.InRange(path.StrikeTime, 1f, path.Duration);
        var head = path.HeadAt(path.StrikeTime);
        float dx = head.X - target.X, dy = head.Y - target.Y, dz = head.Z - target.Z;
        Assert.True(System.Math.Sqrt(dx * dx + dy * dy + dz * dz) < 3.0, $"the strike misses its target by {System.Math.Sqrt(dx * dx + dy * dy + dz * dz):0.0}");
        // It towers: at some point the head is at least 40 blocks up.
        Assert.Contains(Enumerable.Range(0, 200), i => path.HeadAt(path.Duration * i / 199f).Y > target.Y + 40f);
        Assert.False(path.Exposed(path.Duration));
    }

    [Fact]
    public void Segments_FollowTheHeadsOwnTrack_EvenlySpaced()
    {
        var path = SandwormPath.Build(WormMove.Breach, new Vector3f(0f, 60f, 0f), 0.6f, 0.8f, 45f, 100f, 8f);
        var segs = new Vector3f[26];
        path.SegmentCenters(path.Duration * 0.5f, segs);
        float spacing = 100f / 25f;
        for (int i = 1; i < segs.Length; i++)
        {
            float dx = segs[i].X - segs[i - 1].X, dy = segs[i].Y - segs[i - 1].Y, dz = segs[i].Z - segs[i - 1].Z;
            float d = (float)System.Math.Sqrt(dx * dx + dy * dy + dz * dz);
            Assert.True(d <= spacing + 0.01f && d >= spacing * 0.6f, $"segment {i} is {d:0.00} from the one ahead (spacing {spacing:0.00})");
        }
    }

    [Fact]
    public void Path_IsTheSameOnServerAndClient_FromTheSameNumbers()
    {
        var a = SandwormPath.Build(WormMove.Rear, new Vector3f(10f, 64f, 20f), 0.3f, -0.95f, 48f, 110f, 9f, 20f);
        var b = SandwormPath.Build(WormMove.Rear, new Vector3f(10f, 64f, 20f), 0.3f, -0.95f, 48f, 110f, 9f, 20f);
        for (float t = 0f; t < a.Duration; t += 0.37f)
        {
            Assert.Equal(a.HeadAt(t), b.HeadAt(t));
        }
    }

    // ---------------- vibration ----------------

    [Fact]
    public void Sneaking_IsQuiet_WalkingIsHeard_AndLouderSourcesCarryFurther()
    {
        Assert.False(GiantRules.WalkShakes(2.4f));  // crouch speed
        Assert.True(GiantRules.WalkShakes(6f));     // walk speed
        Assert.True(GiantRules.Hears(VibrationSource.Step, 1f, 40f));
        Assert.False(GiantRules.Hears(VibrationSource.Step, 1f, 60f));
        Assert.True(GiantRules.Hears(VibrationSource.Thumper, 1f, 250f));
        Assert.True(GiantRules.Reach(VibrationSource.Thumper) > GiantRules.Reach(VibrationSource.Blaster));
        Assert.True(GiantRules.Reach(VibrationSource.Blaster) > GiantRules.Reach(VibrationSource.Step));
        Assert.True(GiantRules.Hears(VibrationSource.Step, 1.3f, 55f), "a keen worm hears further");
    }

    [Fact]
    public void Attention_FadesInTheQuiet()
    {
        Assert.Equal(1.65f, GiantRules.DecayAttention(2f, 1f), 3);
        Assert.Equal(0f, GiantRules.DecayAttention(1f, 10f));
    }
}
