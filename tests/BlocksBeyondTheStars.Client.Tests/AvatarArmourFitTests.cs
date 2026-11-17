// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
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
    public void TheSuitsChestPanelAndStatusLight_HideUnderAChestPiece()
    {
        // The status light stuck 5 mm out of every chest plate (front 0.215 against 0.21).
        string avatar = Script("PlayerAvatar.cs");
        Assert.Contains("_suitChest.Add(AddCube(\"SuitChestPanel\"", avatar);
        Assert.Contains("_suitChest.Add(AddCube(\"SuitStatusLight\"", avatar);
        Assert.Contains("ShowSuitParts(_suitChest, _visible && !ChestPieceWorn);", avatar);
        Assert.Contains("private bool ChestPieceWorn => GearLook.Has(_gearMask, GearLook.Chest);", Script("PlayerAvatar.ArmourFit.cs"));
    }
}
