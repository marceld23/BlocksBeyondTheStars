// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using BlocksBeyondTheStars.Client;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.State;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>Held part forms (#3934): barrels, shafts and rings of a held tool get flattened corners, a drill's bit becomes a
/// cone, everything else stays a box ("nicht alles rund"). These pin the rule and — since nobody sees the meshes before the
/// Unity build — their geometry: unit extent, outward winding, closed, 45° in metres, the cone's radii.</summary>
public sealed class HeldPartFormsTests
{
    private static readonly HeldItemShapes.Rgb Tint = new(0.5f, 0.5f, 0.5f);
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir());

    private static HeldItemShapes.Part Box(float w, float h, float d, float z = 0f)
        => new(new Vector3(0f, 0f, z), new Vector3(w, h, d), Tint);

    private static HeldItemShapes.Part Auto(string kind, HeldItemShapes.Part part)
        => HeldPartForms.Automatic(kind, new List<HeldItemShapes.Part> { part }, null)[0];

    // ---------------------------------------------------------------- the rule

    [Theory]
    [InlineData(0.07f, 0.16f, 0.08f)]   // a grip
    [InlineData(0.16f, 0.16f, 0.26f)]   // a housing (1.63 × as long as wide)
    [InlineData(0.13f, 0.012f, 0.12f)]  // a screen (its two wide sides differ by 8 %)
    [InlineData(0.05f, 0.05f, 0.084f)]  // a stub just under 1.7 × as long as wide
    [InlineData(0.012f, 0.012f, 0.78f)] // the bow string: too thin to cut
    [InlineData(0.06f, 0.06f, 0.06f)]   // a cube
    [InlineData(0.05f, 0.04f, 0.3f)]    // a flat bar (its cross sides differ by 20 %)
    [InlineData(0.1f, 0.1f, 0.05f)]     // a thick disc (thin over 0.4 × mid)
    public void GripsHousingsAndScreens_StayBoxes(float w, float h, float d)
    {
        var part = Auto("Gun", Box(w, h, d));
        Assert.Equal(HeldPartForm.Box, part.Form);
    }

    [Theory]
    [InlineData(0.02f, 0.02f, 0.3f, 2)]    // the gauss rails
    [InlineData(0.05f, 0.05f, 0.086f, 2)]  // just over 1.7 × as long as wide
    [InlineData(0.025f, 0.12f, 0.025f, 1)] // an antenna standing up
    [InlineData(0.085f, 0.03f, 0.03f, 0)]  // a bar across
    [InlineData(0.049f, 0.05f, 0.3f, 2)]   // its cross sides 2 % apart
    public void LongSquareParts_AreChamferedBars_AlongTheirLongAxis(float w, float h, float d, int axis)
    {
        var part = Auto("Gun", Box(w, h, d));
        Assert.Equal(HeldPartForm.Chamfer, part.Form);
        Assert.Equal(axis, part.Axis);
        Assert.False(HeldPartForms.IsPlate(part));
    }

    [Theory]
    [InlineData(0.12f, 0.12f, 0.02f, 2)]  // a lens ring
    [InlineData(0.03f, 0.12f, 0.12f, 0)]  // a reel on the side
    [InlineData(0.12f, 0.02f, 0.12f, 1)]  // a bell's rim
    [InlineData(0.1f, 0.1f, 0.038f, 2)]   // just under 0.4 × as thick as wide
    public void FlatSquareParts_AreChamferedPlates_AboutTheirThinAxis(float w, float h, float d, int axis)
    {
        var part = Auto("Gun", Box(w, h, d));
        Assert.Equal(HeldPartForm.Chamfer, part.Form);
        Assert.Equal(axis, part.Axis);
        Assert.True(HeldPartForms.IsPlate(part));
    }

    [Fact]
    public void TheDrillsBit_IsACone_OnlyOnADrillAndOnlyAtTheFront()
    {
        var standard = HeldItemShapes.Parts("Drill", Tint)!;
        Assert.Equal(new[] { HeldPartForm.Box, HeldPartForm.Cone, HeldPartForm.Box }, standard.Select(p => p.Form));
        Assert.Equal(2, standard[1].Axis);
        Assert.Equal(1, standard[1].Tip);

        // The same 9 cm bar on a gun is wider than a barrel: a body, a box. Only the drill's cone may be that wide.
        Assert.Equal(HeldPartForm.Box, Auto("Gun", Box(0.09f, 0.09f, 0.18f, 0.24f)).Form);
        Assert.Equal(HeldPartForm.Chamfer, Auto("Gun", Box(0.08f, 0.08f, 0.14f)).Form); // the widest barrel: 8 cm

        // On a drill a bar behind another part's front is a chamfered shaft.
        var shaft = HeldPartForms.Automatic("Drill", new List<HeldItemShapes.Part> { Box(0.04f, 0.04f, 0.2f, 0.1f), Box(0.1f, 0.1f, 0.05f, 0.2f) }, null);
        Assert.Equal(HeldPartForm.Chamfer, shaft[0].Form);

        // Up to 5 mm may stick out beyond the bit.
        var close = HeldPartForms.Automatic("Drill", new List<HeldItemShapes.Part> { Box(0.04f, 0.04f, 0.2f, 0.1f), Box(0.02f, 0.02f, 0.02f, 0.194f) }, null);
        Assert.Equal(HeldPartForm.Cone, close[0].Form);

        // A bar standing across the front of a drill is no bit.
        Assert.Equal(HeldPartForm.Chamfer, Auto("Drill", Box(0.2f, 0.04f, 0.04f)).Form);
    }

    [Fact]
    public void TheItemModels_GetTheirFormsByTheRule()
    {
        static List<HeldPartForm> Forms(string kind, string item) => HeldItemShapes.Parts(kind, Tint, Content.GetItem(item)!.HeldModel)!.Select(p => p.Form).ToList();

        Assert.Equal(HeldPartForm.Cone, Forms("Drill", "titanium_drill")[1]);
        Assert.Equal(HeldPartForm.Cone, Forms("Drill", "diamond_drill")[4]);  // the crystal tip
        Assert.DoesNotContain(HeldPartForm.Cone, Forms("Drill", "guardian_drill")); // its front is the glowing cube
        Assert.DoesNotContain(HeldPartForm.Cone, Forms("Gun", "laser_pistol"));
        Assert.Equal(HeldPartForm.Chamfer, Forms("Gun", "laser_pistol")[2]); // the barrel
        Assert.Equal(new[] { HeldPartForm.Chamfer, HeldPartForm.Chamfer }, Forms("Gun", "gauss_pistol").Skip(1).Take(2)); // the rails
        Assert.Equal(HeldPartForm.Box, Forms("Gun", "gauss_pistol")[6]);     // the grip
        Assert.All(Forms("Gadget", "blueprint_tool").Take(4), f => Assert.Equal(HeldPartForm.Box, f)); // the screen and its lines
        Assert.Equal(HeldPartForm.Chamfer, Forms("Blade", "wood_spear")[0]);
        Assert.Equal(HeldPartForm.Chamfer, Forms("Gadget", "fluid_pump")[2]); // the 8 cm glass tank
    }

    [Theory]
    [InlineData("Drill", "diamond_drill", 0)]
    [InlineData("Drill", "ressaw_drill", 0)]
    [InlineData("Gun", "gauss_pistol", 0)]
    [InlineData("Gun", "guardian_laser", 0)]
    [InlineData("Gadget", "fluid_pump", 0)]
    [InlineData("Gadget", "stasis_projector", 0)]
    [InlineData("Gadget", "shape_stencil", 4)] // the sheet inside its frame names "box"
    [InlineData("Gadget", "shape_stencil", 6)] // the square mark on it
    [InlineData("Gadget", "rail_cab", 2)]      // the window strip
    public void ToolBodies_AndThePartsTheDataKeepsSquare_StayBoxes(string kind, string item, int index)
    {
        var parts = HeldItemShapes.Parts(kind, Tint, Content.GetItem(item)!.HeldModel)!;
        Assert.Equal(Content.GetItem(item)!.HeldModel!.Count, parts.Count);
        Assert.Equal(HeldPartForm.Box, parts[index].Form);
    }

    [Fact]
    public void EveryFormTheDataNames_IsOneTheClientKnows()
    {
        var named = Content.Items.Values.Where(i => i.HeldModel != null)
            .SelectMany(i => i.HeldModel!.Where(p => p.F != null).Select(p => (i.Key, p.F))).ToList();
        Assert.Equal(3, named.Count); // shape_stencil's sheet and mark, rail_cab's window — "f" binds to HeldModelPart.F
        Assert.All(named, n => Assert.Contains(n.F, new[] { HeldModelPart.FormBox, HeldModelPart.FormChamfer, HeldModelPart.FormCone }));
        Assert.Equal(45, Content.Items.Values.Count(i => i.HeldModel is { Count: > 0 }));
    }

    [Fact]
    public void TheNpcTools_GetFormsToo()
    {
        var axe = HeldItemShapes.NpcToolParts(HeldItemShapes.AxeKind, Tint)!;
        Assert.Equal(HeldPartForm.Chamfer, axe[0].Form); // the handle
        Assert.All(axe.Skip(1), p => Assert.Equal(HeldPartForm.Box, p.Form)); // the iron head

        var bow = HeldItemShapes.NpcToolParts(HeldItemShapes.BowKind, Tint)!;
        Assert.Equal(4, bow.Count(p => p.Form == HeldPartForm.Chamfer)); // the limbs
        Assert.Equal(HeldPartForm.Box, bow[^1].Form); // the string is too thin
    }

    [Fact]
    public void TheWorkingNpcsHoeAndHammer_GetChamferedHandles_AndKeepTheirIronBoxes()
    {
        var hoe = HeldWorkTools.Parts(HeldWorkTools.HoeKind, Tint)!;
        Assert.Equal((HeldPartForm.Chamfer, 2), (hoe[0].Form, hoe[0].Axis)); // the 70 cm shaft
        Assert.Equal(HeldPartForm.Box, hoe[1].Form);                        // the blade (its wide sides differ by 12 %)
        Assert.Equal(HeldMaterials.Wood, hoe[0].Material);

        var hammer = HeldWorkTools.Parts(HeldWorkTools.HammerKind, Tint)!;
        Assert.Equal((HeldPartForm.Chamfer, 2), (hammer[0].Form, hammer[0].Axis)); // the handle
        Assert.Equal(HeldPartForm.Box, hammer[1].Form);                           // the head: a bar wider than 8 cm
        Assert.Equal(Tint.R, hammer[1].Color.R);

        // The cubes are the ones HeldItem.Build drew before (#1869): same centres and sizes.
        Assert.Equal(new Vector3(0.045f, 0.045f, 0.70f), hoe[0].Size);
        Assert.Equal(new Vector3(0f, -0.07f, 0.54f), hoe[1].Position);
        Assert.Equal(new Vector3(0.09f, 0.18f, 0.09f), hammer[1].Size);
        Assert.Null(HeldWorkTools.Parts("Drill", Tint));

        // With the form mode 0 they are boxes again.
        Assert.All(hoe.Concat(hammer), p => Assert.Equal(HeldPartForm.Box, HeldPartForms.Shown(p, 0)));

        string held = System.IO.File.ReadAllText(System.IO.Path.Combine(ClientTestPaths.RepoRoot(), "client", "Assets", "BlocksBeyondTheStars", "Scripts", "HeldItem.cs"));
        Assert.Contains("HeldWorkTools.Parts(kind.ToString(), ToRgb(tint))", held);
    }

    [Fact]
    public void ANamedForm_WinsOverTheRule()
    {
        HeldModelPart M(float[] s, string? f, float z = 0f) => new() { P = new[] { 0f, 0f, z }, S = s, C = "tint", F = f };
        var model = new List<HeldModelPart>
        {
            M(new[] { 0.02f, 0.02f, 0.3f }, "box"),             // a bar told to stay a box
            M(new[] { 0.05f, 0.2f, 0.3f }, "chamfer"),          // thin ≤ 0.6 × mid → a plate about x
            M(new[] { 0.1f, 0.1f, 0.1f }, "chamfer"),           // a cube → a bar along z
            M(new[] { 0.05f, 0.05f, 0.2f }, "cone", -0.1f),     // behind the hand → the tip points back
            M(new[] { 0.05f, 0.05f, 0.2f }, "cylinder"),        // unknown → the rule (a bar)
            M(new[] { 0.07f, 0.16f, 0.08f }, "cone"),           // a grip told to be a cone: along y
        };

        var parts = HeldItemShapes.Parts("Gun", Tint, model)!;
        Assert.Equal(HeldPartForm.Box, parts[0].Form);
        Assert.Equal((HeldPartForm.Chamfer, 0), (parts[1].Form, parts[1].Axis));
        Assert.True(HeldPartForms.IsPlate(parts[1]));
        Assert.Equal((HeldPartForm.Chamfer, 2), (parts[2].Form, parts[2].Axis));
        Assert.Equal((HeldPartForm.Cone, 2, -1), (parts[3].Form, parts[3].Axis, parts[3].Tip));
        Assert.Equal((HeldPartForm.Chamfer, 2), (parts[4].Form, parts[4].Axis));
        Assert.Equal((HeldPartForm.Cone, 1, 1), (parts[5].Form, parts[5].Axis, parts[5].Tip));
        Assert.Equal(6, parts.Count); // every part is still drawn
    }

    [Fact]
    public void APlayersOwnLook_KeepsItsBoxes()
    {
        // A one-voxel-wide bar ten voxels long (0.04 × 0.04 × 0.4 m) and a grip: the rule would chamfer the bar.
        var voxels = new byte[ToolLook.VoxelCount];
        for (int z = 2; z < 12; z++)
        {
            voxels[ToolLook.IndexOf(4, 5, z)] = 3;
        }

        voxels[ToolLook.IndexOf(4, 2, 2)] = 2;
        var look = ToolLook.ToParts(ToolLook.Compose(voxels, 0));
        Assert.Equal(2, look.Count);
        Assert.All(look, p => Assert.Equal(HeldModelPart.FormBox, p.F));
        Assert.True(HeldPartForms.IsBar(new Vector3(look[1].S[0], look[1].S[1], look[1].S[2]), out _) || HeldPartForms.IsBar(new Vector3(look[0].S[0], look[0].S[1], look[0].S[2]), out _));
        Assert.All(HeldItemShapes.Parts("Drill", Tint, look)!, p => Assert.Equal(HeldPartForm.Box, p.Form));
    }

    [Fact]
    public void AMirroredCone_AcrossX_TurnsItsTip()
    {
        var cone = Box(0.2f, 0.05f, 0.05f).WithForm(HeldPartForm.Cone, 0, 1);
        var bar = Box(0.05f, 0.05f, 0.2f).WithForm(HeldPartForm.Chamfer, 2, 1);
        var mirrored = HeldItemShapes.Mirror(new[] { cone, bar });
        Assert.Equal((HeldPartForm.Cone, 0, -1), (mirrored[0].Form, mirrored[0].Axis, mirrored[0].Tip));
        Assert.Equal((HeldPartForm.Chamfer, 2, 1), (mirrored[1].Form, mirrored[1].Axis, mirrored[1].Tip));
        Assert.Equal(1, HeldItemShapes.Mirror(mirrored)[0].Tip);
    }

    [Fact]
    public void TheLabSwitch_PicksWhatIsShown()
    {
        var plate = Auto("Gun", Box(0.12f, 0.12f, 0.02f));
        var bar = Auto("Gun", Box(0.02f, 0.02f, 0.3f));
        var cone = HeldItemShapes.Parts("Drill", Tint)![1];
        Assert.All(new[] { plate, bar, cone }, p => Assert.Equal(HeldPartForm.Box, HeldPartForms.Shown(p, 0)));
        Assert.Equal(new[] { HeldPartForm.Chamfer, HeldPartForm.Chamfer, HeldPartForm.Cone }, new[] { plate, bar, cone }.Select(p => HeldPartForms.Shown(p, 1)));
        Assert.Equal(new[] { HeldPartForm.Box, HeldPartForm.Chamfer, HeldPartForm.Cone }, new[] { plate, bar, cone }.Select(p => HeldPartForms.Shown(p, 2)));
    }

    [Fact]
    public void TheFarStage_MatchesTheCornerGap()
    {
        var bar = Auto("Gun", Box(0.02f, 0.02f, 0.3f));
        var plate = Auto("Gun", Box(0.12f, 0.12f, 0.02f));
        Assert.Equal(0.28f * 2.414f, HeldPartForms.LodShare(bar, HeldPartForm.Chamfer, 0.28f, 0.18f), 2);
        Assert.Equal(0.28f * 2.414f * 6f, HeldPartForms.LodShare(plate, HeldPartForm.Chamfer, 0.28f, 0.18f), 2); // cut on the 12 cm side, thinnest 2 cm
        Assert.Equal(1f, HeldPartForms.LodShare(bar, HeldPartForm.Cone, 0.28f, 0.18f));
        Assert.Equal(0.18f, HeldPartForms.LodShare(bar, HeldPartForm.Box, 0.28f, 0.18f));
    }

    [Fact]
    public void TheClient_DrawsEveryToolPartByItsForm_AndTheLookEditorPreviewKeepsBoxes()
    {
        static string Script(string file)
            => System.IO.File.ReadAllText(System.IO.Path.Combine(ClientTestPaths.RepoRoot(), "client", "Assets", "BlocksBeyondTheStars", "Scripts", file));

        string held = Script("HeldItem.cs");
        Assert.Contains("HeldTextures.ToolPartMesh(part, out float labShare)", held);

        // A part whose mesh is replaced after PartCube / RoundLabPart leaves the far stage, or far away the sweep would put
        // the plain cube back over the ore's block faces or the painted hand.
        Assert.Contains("RoundLod.Assign(go.GetComponent<MeshFilter>(), mesh, null, 0f);", held);
        Assert.Contains("RoundLod.Assign(go.GetComponent<MeshFilter>(), HandMesh(left, hand: box.Piece == HeldHands.Piece.Hand), null, 0f);", held);
        Assert.DoesNotContain(".sharedMesh = mesh;", held);
        Assert.Contains("HeldPartForms.Shown(part, GameLook.ToolFormsMode)", Script("HeldTextures.cs"));
        Assert.Contains("F = HeldModelPart.FormBox", Script("ToolLookEditor.cs"));
        // The forms are the game's look, no longer behind a switch: bars, plates and the drill's cone (mode 1), cut by 0.28.
        string look = Script("GameLook.cs");
        Assert.Contains("public static readonly float ToolForms = 1f;", look);
        Assert.Contains("public static readonly float ToolChamfer = 0.28f;", look);
    }

    // ---------------------------------------------------------------- the meshes

    public static IEnumerable<object[]> Shapes()
    {
        var sizes = new[] { new Vector3(0.02f, 0.02f, 0.3f), new Vector3(0.04f, 0.1f, 0.3f), new Vector3(0.12f, 0.12f, 0.02f), new Vector3(0.3f, 0.05f, 0.07f) };
        foreach (var size in sizes)
        {
            for (int axis = 0; axis < 3; axis++)
            {
                yield return new object[] { size.X, size.Y, size.Z, axis, 1 };
                yield return new object[] { size.X, size.Y, size.Z, axis, -1 };
            }
        }
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void TheChamferMesh_IsAClosedUnitSolid_WoundOutward(float w, float h, float d, int axis, int tip)
    {
        var mesh = HeldFormMeshes.BuildChamfer(new Vector3(w, h, d), axis, tip > 0 ? 0.28f : HeldFormMeshes.RegularCut);
        AssertSolid(mesh, flat: true);
        Assert.Equal(48, mesh.Positions.Length);
        Assert.Equal(28 * 3, mesh.Triangles.Length);
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void TheConeMesh_IsAClosedUnitSolid_WoundOutward(float w, float h, float d, int axis, int tip)
    {
        var mesh = HeldFormMeshes.BuildCone(new Vector3(w, h, d), axis, tip);
        AssertSolid(mesh, flat: false);
        Assert.Equal(50, mesh.Positions.Length);
        Assert.Equal(44 * 3, mesh.Triangles.Length);
    }

    [Fact]
    public void TheChamfer_IsCutAt45Degrees_InMetres()
    {
        var size = new Vector3(0.04f, 0.1f, 0.3f);
        var mesh = HeldFormMeshes.BuildChamfer(size, 2, 0.28f);
        var cuts = new HashSet<(int, int)>();
        for (int i = 0; i < mesh.Normals.Length; i++)
        {
            var n = mesh.Normals[i];
            if (Math.Abs(n.X) < 1e-4f || Math.Abs(n.Y) < 1e-4f)
            {
                continue;
            }

            // Unity turns a mesh normal by the inverse of the scale: that is the normal in metres.
            var metres = Vector3.Normalize(new Vector3(n.X / size.X, n.Y / size.Y, n.Z / size.Z));
            Assert.Equal(Math.Abs(metres.X), Math.Abs(metres.Y), 4);
            Assert.Equal(0f, metres.Z, 5);
            cuts.Add((Math.Sign(n.X), Math.Sign(n.Y)));
        }

        Assert.Equal(4, cuts.Count); // all four long edges

        // The cut is 0.28 of the smaller cross side (4 cm) on each side of the corner: 11.2 mm.
        float cornerX = mesh.Positions.Where(p => Math.Abs(p.Y - 0.5f) < 1e-5f && p.Z > 0.49f).Max(p => p.X) * size.X;
        Assert.Equal((0.02f - (0.28f * 0.04f)), cornerX, 5);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void TheCone_IsTheInscribedEllipse_NarrowingToThreeTenths(int tip)
    {
        var size = new Vector3(0.06f, 0.09f, 0.2f);
        var mesh = HeldFormMeshes.BuildCone(size, 2, tip);
        float baseZ = -0.5f * tip, tipZ = 0.5f * tip;
        var atBase = mesh.Positions.Where(p => Math.Abs(p.Z - baseZ) < 1e-5f).ToList();
        var atTip = mesh.Positions.Where(p => Math.Abs(p.Z - tipZ) < 1e-5f).ToList();
        Assert.NotEmpty(atBase);
        Assert.NotEmpty(atTip);
        Assert.All(atBase, p => Assert.Equal(0.5f, new Vector2(p.X, p.Y).Length(), 4));
        Assert.All(atTip, p => Assert.Equal(0.15f, new Vector2(p.X, p.Y).Length(), 4));

        // In metres the base is the ellipse inscribed in the 6 × 9 cm cross-section.
        Assert.Equal(0.03f, atBase.Max(p => p.X) * size.X, 5);
        Assert.Equal(0.045f, atBase.Max(p => p.Y) * size.Y, 4);
        Assert.Equal(0.009f, atTip.Max(p => p.X) * size.X, 5);
    }

    [Fact]
    public void TheConesTiles_GoAroundAWholeNumberOfTimes()
    {
        Assert.Equal(1, HeldFormMeshes.TilesAround(0.01f, 0.01f));      // a thin bit still shows one whole tile
        Assert.Equal(1, HeldFormMeshes.TilesAround(0.045f, 0.045f));    // the standard bit: 0.18 m around on average
        Assert.Equal(3, HeldFormMeshes.TilesAround(0.15f, 0.15f));
        var mesh = HeldFormMeshes.BuildCone(new Vector3(0.3f, 0.3f, 0.4f), 2, 1);
        Assert.Equal(3f, mesh.Uvs.Max(uv => uv.X), 4);
    }

    [Fact]
    public void EveryFormedPartOfTheContent_BuildsASolidMesh()
    {
        int formed = 0;
        foreach (var item in Content.Items.Values.Where(i => i.HeldModel is { Count: > 0 }))
        {
            foreach (string kind in new[] { "Drill", "Gun" })
            {
                foreach (var part in HeldItemShapes.Parts(kind, Tint, item.HeldModel)!.Where(p => p.Form != HeldPartForm.Box))
                {
                    formed++;
                    AssertSolid(part.Form == HeldPartForm.Cone
                        ? HeldFormMeshes.BuildCone(part.Size, part.Axis, part.Tip)
                        : HeldFormMeshes.BuildChamfer(part.Size, part.Axis, 0.28f), flat: part.Form == HeldPartForm.Chamfer);
                }
            }
        }

        Assert.True(formed > 50, $"only {formed} formed parts in the content");
    }

    private static void AssertSolid(HeldMaterialRules.CubeMesh mesh, bool flat)
    {
        Assert.Equal(mesh.Positions.Length, mesh.Normals.Length);
        Assert.Equal(mesh.Positions.Length, mesh.Uvs.Length);
        Assert.Equal(0, mesh.Triangles.Length % 3);
        Assert.All(mesh.Positions, p => Assert.True(Math.Abs(p.X) <= 0.5f + 1e-5f && Math.Abs(p.Y) <= 0.5f + 1e-5f && Math.Abs(p.Z) <= 0.5f + 1e-5f, $"{p} leaves the unit box"));
        Assert.All(mesh.Normals, n => Assert.Equal(1f, n.Length(), 4));
        Assert.All(mesh.Uvs, uv => Assert.True(float.IsFinite(uv.X) && float.IsFinite(uv.Y) && uv.X >= 0f && uv.Y >= 0f, $"bad uv {uv}"));

        var edges = new Dictionary<(long, long, long, long, long, long), int>();
        for (int t = 0; t < mesh.Triangles.Length; t += 3)
        {
            int[] i = { mesh.Triangles[t], mesh.Triangles[t + 1], mesh.Triangles[t + 2] };
            var a = mesh.Positions[i[0]];
            var cross = Vector3.Cross(mesh.Positions[i[1]] - a, mesh.Positions[i[2]] - a);
            Assert.True(cross.Length() > 1e-7f, $"triangle {t / 3} is degenerate");
            var centre = (a + mesh.Positions[i[1]] + mesh.Positions[i[2]]) / 3f;
            Assert.True(Vector3.Dot(cross, centre) > 0f, $"triangle {t / 3} faces into the solid"); // convex, around the origin
            foreach (int v in i)
            {
                Assert.True(Vector3.Dot(cross, mesh.Normals[v]) > 0f, $"triangle {t / 3} is wound against its normals");
                if (flat)
                {
                    Assert.Equal(1f, Vector3.Dot(Vector3.Normalize(cross), mesh.Normals[v]), 4); // a flat face: one normal
                }
            }

            for (int k = 0; k < 3; k++)
            {
                var key = EdgeKey(mesh.Positions[i[k]], mesh.Positions[i[(k + 1) % 3]]);
                edges[key] = edges.TryGetValue(key, out int n) ? n + 1 : 1;
            }
        }

        Assert.All(edges, e => Assert.Equal(2, e.Value)); // closed: every edge between exactly two triangles
    }

    private static (long, long, long, long, long, long) EdgeKey(Vector3 a, Vector3 b)
    {
        (long, long, long) Q(Vector3 p) => ((long)Math.Round(p.X * 1e5), (long)Math.Round(p.Y * 1e5), (long)Math.Round(p.Z * 1e5));
        var (qa, qb) = (Q(a), Q(b));
        return qa.CompareTo(qb) <= 0 ? (qa.Item1, qa.Item2, qa.Item3, qb.Item1, qb.Item2, qb.Item3) : (qb.Item1, qb.Item2, qb.Item3, qa.Item1, qa.Item2, qa.Item3);
    }
}
