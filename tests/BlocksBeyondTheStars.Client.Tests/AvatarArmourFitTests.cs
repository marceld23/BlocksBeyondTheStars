// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.State;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3752 (Justus, 2026-11-17: "the worn armour goes through the skin, sticks in you and flickers"): a plate's face in the
/// plane of the body face under it flickers against it. The boxes live in <see cref="AvatarArmourFit"/>; this holds the
/// rule for the chest plate and the leg plates every set draws — and proves the old boxes broke it. The Unity wiring is
/// guarded as source text (the scripts cannot load headless — the <see cref="ClientQuirksWiringTests"/> pattern).
/// </summary>
public sealed class AvatarArmourFitTests
{
    private static string Script(string file)
        => File.ReadAllText(Path.Combine(ClientTestPaths.RepoRoot(), "client", "Assets", "BlocksBeyondTheStars", "Scripts", file));

    /// <summary>The body boxes in the root frame: the torso, both shoulders, both upper legs.</summary>
    private static IEnumerable<(string Name, AvatarBox Box)> Body()
    {
        yield return ("pelvis", AvatarArmourFit.Pelvis);
        yield return ("abdomen", AvatarArmourFit.Abdomen);
        yield return ("chest", AvatarArmourFit.Chest);
        yield return ("right shoulder", AvatarArmourFit.Shoulder);
        yield return ("left shoulder", AvatarArmourFit.Shoulder.From(-2f * AvatarArmourFit.Shoulder.X, 0f));
        yield return ("right upper leg", AvatarArmourFit.UpperLeg.From(AvatarArmourFit.HipX, AvatarArmourFit.HipY));
        yield return ("left upper leg", AvatarArmourFit.UpperLeg.From(-AvatarArmourFit.HipX, AvatarArmourFit.HipY));
    }

    /// <summary>A leg plate in the root frame, the legs hanging straight (the standing pose).</summary>
    private static AvatarBox LegPlate(float side) => AvatarArmourFit.LegPlate.From(side * AvatarArmourFit.HipX, AvatarArmourFit.HipY);

    [Fact]
    public void NoPlateFace_LiesInThePlaneOfABodyFaceItCovers()
    {
        foreach (var (name, box) in Body())
        {
            Assert.False(AvatarArmourFit.ShareAFace(AvatarArmourFit.ChestPlate, box), "chest plate / " + name);
            Assert.False(AvatarArmourFit.ShareAFace(LegPlate(1f), box), "right leg plate / " + name);
            Assert.False(AvatarArmourFit.ShareAFace(LegPlate(-1f), box), "left leg plate / " + name);
        }
    }

    [Fact]
    public void TheLegPlate_StaysBehindTheKneePadsFront_AndTheChestPlatesFrontStaysWhereTheTrimSits()
    {
        // The knee pad hangs on the knee pivot, under the hip; its front must not come to lie in the leg plate's front.
        var pad = AvatarArmourFit.KneePad.From(0f, AvatarArmourFit.KneeDrop);
        Assert.False(AvatarArmourFit.ShareAFace(AvatarArmourFit.LegPlate, pad));
        Assert.True(pad.Front - AvatarArmourFit.LegPlate.Front >= AvatarArmourFit.Clearance);

        // The sets' bands, seams and the disc sit on the chest plate's front (z 0.212–0.22): it must not move.
        Assert.Equal(0.21f, AvatarArmourFit.ChestPlate.Front, 4);
        Assert.True(AvatarArmourFit.Chest.Back - AvatarArmourFit.ChestPlate.Back >= 0.01f, "the plate's back is clearly behind the body's");
        Assert.True(AvatarArmourFit.LegPlate.Front - AvatarArmourFit.Pelvis.Front >= AvatarArmourFit.Clearance);
        Assert.True(AvatarArmourFit.Pelvis.Back - AvatarArmourFit.LegPlate.Back >= AvatarArmourFit.Clearance);
    }

    [Fact]
    public void TheOldBoxes_BrokeTheRule_ExactlyWhereItFlickered()
    {
        // Up to v2026.11.20: the chest plate ended in the plane of the body's back, the leg plates in the pelvis' planes.
        var oldChestPlate = new AvatarBox(0f, 1.45f, 0.02f, 0.62f, 0.42f, 0.38f);
        var oldLegPlate = new AvatarBox(0f, -0.22f, 0f, 0.28f, 0.46f, 0.28f).From(AvatarArmourFit.HipX, AvatarArmourFit.HipY);
        Assert.True(AvatarArmourFit.ShareAFace(oldChestPlate, AvatarArmourFit.Chest));
        Assert.True(AvatarArmourFit.ShareAFace(oldLegPlate, AvatarArmourFit.Pelvis));

        // And the rule itself: a shared plane only counts where the two faces overlap and look the same way.
        var a = new AvatarBox(0f, 0f, 0f, 1f, 1f, 1f);
        Assert.True(AvatarArmourFit.ShareAFace(a, new AvatarBox(0.2f, 0f, 0f, 1f, 0.5f, 0.5f).From(-0.2f, 0f, 0.25f))); // fronts at 0.5
        Assert.False(AvatarArmourFit.ShareAFace(a, new AvatarBox(0f, 0f, 1f, 1f, 1f, 1f)));   // touching: front against back
        Assert.False(AvatarArmourFit.ShareAFace(a, new AvatarBox(3f, 0f, 0f, 1f, 1f, 1f)));   // same planes, far apart
        Assert.False(AvatarArmourFit.ShareAFace(a, new AvatarBox(0f, 0f, 0f, 0.8f, 0.8f, 0.8f))); // inside, clear all round
    }

    [Fact]
    public void EverySet_DrawsTheSharedPlates_AndTheFigureTheSharedBody()
    {
        string avatar = Script("PlayerAvatar.cs");
        string sets = Script("PlayerAvatar.GearSets.cs");
        string obsidian = Script("PlayerAvatar.Obsidian.cs");

        Assert.Contains("AddCube(\"GearChest\", transform, AvatarArmourFit.ChestPlate, m)", avatar);       // classic + titanium
        Assert.Contains("AddCube(\"GearLegL\", _legL, AvatarArmourFit.LegPlate, m)", avatar);
        Assert.Contains("AddCube(\"GearLegR\", _legR, AvatarArmourFit.LegPlate, m)", avatar);
        Assert.Contains("AddCube(\"SetChest\", transform, AvatarArmourFit.ChestPlate, _setDark)", sets);     // guardian
        Assert.Contains("AddCube(\"SetLegL\", _legL, AvatarArmourFit.LegPlate, _setDark)", sets);
        Assert.Contains("AddCube(\"SetLegR\", _legR, AvatarArmourFit.LegPlate, _setDark)", sets);
        Assert.Contains("RessawPart(\"Chest\", transform, At(AvatarArmourFit.ChestPlate), Size(AvatarArmourFit.ChestPlate)", sets);
        Assert.Contains("RessawPart(\"Thigh\", hip, At(AvatarArmourFit.LegPlate), Size(AvatarArmourFit.LegPlate)", sets);
        Assert.Contains("AddCube(\"ObsChest\", transform, AvatarArmourFit.ChestPlate, _obsidian)", obsidian);
        Assert.Contains("AddCube(\"ObsLegL\", _legL, AvatarArmourFit.LegPlate, _obsidian)", obsidian);
        Assert.Contains("AddCube(\"ObsLegR\", _legR, AvatarArmourFit.LegPlate, _obsidian)", obsidian);

        // No builder keeps a plate box of its own.
        foreach (string source in new[] { avatar, sets, obsidian })
        {
            Assert.DoesNotContain("new Vector3(0.62f, 0.42f, 0.38f)", source);
            Assert.DoesNotContain("new Vector3(0.28f, 0.46f, 0.28f)", source);
        }

        // The body under the plates comes from the same place, so the test above measures the figure that is drawn.
        Assert.Contains("AddCube(\"Pelvis\", transform, AvatarArmourFit.Pelvis, _legs)", avatar);
        Assert.Contains("AddCube(\"Chest\", transform, AvatarArmourFit.Chest, _torso)", avatar);
        Assert.Contains("AddCube(name + \"Upper\", hip, AvatarArmourFit.UpperLeg, _legs)", avatar);
        Assert.Contains("new Vector3(x, AvatarArmourFit.HipY, 0f)", avatar);
    }

    [Fact]
    public void TheCoverPlates_OfArmsWaistAndShins_KeepClearOfTheBody()
    {
        // #3861: the chest piece plates the arms and the waist, the leg piece the shins — each clear of the body under it.
        Assert.False(AvatarArmourFit.ShareAFace(AvatarArmourFit.ArmPlate, AvatarArmourFit.UpperArm), "arm plate / upper arm");
        Assert.False(AvatarArmourFit.ShareAFace(AvatarArmourFit.Bracer, AvatarArmourFit.LowerArm), "bracer / forearm");
        Assert.False(AvatarArmourFit.ShareAFace(AvatarArmourFit.ShinPlate, AvatarArmourFit.LowerLeg), "shin plate / lower leg");
        Assert.False(AvatarArmourFit.ShareAFace(AvatarArmourFit.ShinPlate, AvatarArmourFit.KneePad), "shin plate / knee pad");
        var armPlate = AvatarArmourFit.ArmPlate.From(AvatarArmourFit.ShoulderX, AvatarArmourFit.ShoulderY);
        foreach (var (name, box) in Body())
        {
            Assert.False(AvatarArmourFit.ShareAFace(AvatarArmourFit.WaistPlate, box), "waist plate / " + name);
            Assert.False(AvatarArmourFit.ShareAFace(armPlate, box), "arm plate / " + name);
        }

        // Against the other plates of the same figure: the chest plate above, the leg plates below.
        Assert.False(AvatarArmourFit.ShareAFace(AvatarArmourFit.WaistPlate, AvatarArmourFit.ChestPlate));
        Assert.False(AvatarArmourFit.ShareAFace(AvatarArmourFit.WaistPlate, LegPlate(1f)));
        Assert.False(AvatarArmourFit.ShareAFace(armPlate, AvatarArmourFit.ChestPlate));

        // Each plate really is around its part: wider than it, and covering most of its length.
        Assert.True(AvatarArmourFit.ArmPlate.Width > AvatarArmourFit.UpperArm.Width
            && AvatarArmourFit.ArmPlate.Height >= 0.6f * AvatarArmourFit.UpperArm.Height);
        Assert.True(AvatarArmourFit.Bracer.Width > AvatarArmourFit.LowerArm.Width
            && AvatarArmourFit.Bracer.Height >= 0.5f * AvatarArmourFit.LowerArm.Height);
        Assert.True(AvatarArmourFit.ShinPlate.Width > AvatarArmourFit.LowerLeg.Width
            && AvatarArmourFit.ShinPlate.Height >= 0.8f * AvatarArmourFit.LowerLeg.Height);
        Assert.True(AvatarArmourFit.WaistPlate.Top >= AvatarArmourFit.ChestPlate.Bottom - 0.001f, "no belly gap between the chest plate and the waist plate");
        Assert.True(AvatarArmourFit.WaistPlate.Bottom <= AvatarArmourFit.HipY + 0.001f, "the waist plate reaches down to the hips");
    }

    [Fact]
    public void EveryArmourSet_DrawsTheCover_AndItsOwnOutline()
    {
        string avatar = Script("PlayerAvatar.cs");
        string sets = Script("PlayerAvatar.GearSets.cs");
        string obsidian = Script("PlayerAvatar.Obsidian.cs");
        string cover = Script("PlayerAvatar.ArmourCover.cs");
        Assert.Contains("AddArmourArms(m, m);", avatar);              // classic + titanium chest
        Assert.Contains("AddArmourShins(m);", avatar);                // classic + titanium legs
        Assert.Contains("AddArmourArms(_setDark, _setTrim);", sets);   // guardian
        Assert.Contains("AddArmourShins(_setDark);", sets);
        Assert.Contains("AddArmourArms(_ressawMilk, _ressawPurple);", sets);
        Assert.Contains("RessawPart(\"Shin\", knee, At(AvatarArmourFit.ShinPlate), Size(AvatarArmourFit.ShinPlate)", sets);
        Assert.Contains("AddArmourArms(_obsidian, _obsidian);", obsidian);
        Assert.Contains("AddArmourShins(_obsidian);", obsidian);
        Assert.Contains("AddCube(\"CoverArm\", shoulder, AvatarArmourFit.ArmPlate, plate)", cover);
        Assert.Contains("AddCube(\"CoverBracer\", elbow, AvatarArmourFit.Bracer, trim)", cover);
        Assert.Contains("AddCube(\"CoverWaist\", transform, AvatarArmourFit.WaistPlate, plate)", cover);

        // The outlines: titanium brim + shoulder tier, guardian cheeks/fins + stepped shoulders, obsidian domes.
        Assert.Contains("AddTitanHelmetShape(m);", avatar);
        Assert.Contains("AddTitanShoulderTier(m);", avatar);
        Assert.Contains("AddGuardianHelmetShape();", sets);
        Assert.Contains("AddGuardianShoulderSpikes();", sets);
        Assert.Contains("AddObsidianShoulderDomes();", obsidian);

        // The figure's arms and shins are the boxes the plates are measured against.
        Assert.Contains("new Vector3(x, AvatarArmourFit.ShoulderY, 0f)", avatar);
        Assert.Contains("new Vector3(0f, AvatarArmourFit.ElbowDrop, 0f)", avatar);
        Assert.Contains("AddCube(name + \"Lower\", knee, AvatarArmourFit.LowerLeg, _legs)", avatar);
        Assert.Contains("ArmUpperSize = new Vector3(0.16f, 0.42f, 0.16f)", avatar);
        Assert.Contains("ArmLowerSize = new Vector3(0.15f, 0.42f, 0.15f)", avatar);
    }

    [Fact]
    public void TheSetBoots_HaveTheirOwnLook()
    {
        // #3862: guardian and obsidian boots draw their set's style, the titanium boots carry a bit of their own.
        Assert.Contains("if (BuildArmourBoots(style))", Script("PlayerAvatar.GearSets.cs"));
        Assert.Contains("bool titan = GearLook.Has(mask, GearLook.TitanBoots);", Script("PlayerAvatar.ArmourCover.cs"));
        Assert.Contains("BuildClassicBoots(mask);", Script("PlayerAvatar.cs"));
        int titanBoots = GearLook.Mask(k => k == "titan_boots");
        Assert.True(GearLook.Has(titanBoots, GearLook.Boots | GearLook.TitanBoots) && titanBoots > 0, "a positive mask (bit 30, not the sign)");
        Assert.Equal(GearLook.StyleGuardian, GearLook.BootsStyle(GearLook.Mask(k => k == "guardian_boots")));
        Assert.Equal(GearLook.StyleObsidian, GearLook.BootsStyle(GearLook.Mask(k => k == "obsidian_boots")));
    }

    [Fact]
    public void TheSuitsChestPanelAndStatusLight_HideUnderAChestPiece()
    {
        // The status light stuck 5 mm out of every chest plate (front 0.215 against 0.21).
        string avatar = Script("PlayerAvatar.cs");
        Assert.Contains("_suitChest.Add(AddCube(\"SuitChestPanel\"", avatar);
        Assert.Contains("_suitChest.Add(AddCube(\"SuitStatusLight\"", avatar);
        Assert.Contains("ShowSuitParts(_suitChest, _visible && !ChestPieceWorn);", avatar);

        // A piece that covers the chest — not a mask nobody set yet (-1 reads as every bit: the preview figure, the skin
        // picture and a spacewalk suit lost their panel), and not the flower bracelets, which sit in the chest slot but
        // only band the forearms.
        Assert.Contains(
            "=> _gearMask >= 0 && GearLook.Has(_gearMask, GearLook.Chest) && GearLook.ChestStyle(_gearMask) != GearLook.StyleFlower;",
            Script("PlayerAvatar.ArmourFit.cs"));
        Assert.True(GearLook.Has(-1, GearLook.Chest)); // why the sign is asked first
        int bracelets = GearLook.Chest | (GearLook.StyleFlower << GearLook.ChestStyleShift);
        Assert.Equal(GearLook.StyleFlower, GearLook.ChestStyle(bracelets));
    }
}
