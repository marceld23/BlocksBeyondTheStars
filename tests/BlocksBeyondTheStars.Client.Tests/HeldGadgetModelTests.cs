// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Globalization;
using System.Linq;
using System.Numerics;
using BlocksBeyondTheStars.Client;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>#2837: every gadget used to be the same four-cube emitter in another colour (and the remote control's own model
/// was never drawn). Now a gadget draws its <c>heldModel</c> like a drill or a gun does, and each of the 21 gadgets has
/// one; the emitter is left as the fallback of a gadget without a model.</summary>
public sealed class HeldGadgetModelTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir());
    private static readonly HeldItemShapes.Rgb Tint = new(0.6f, 0.85f, 0.9f);

    /// <summary>The gadgets held as a gadget: not the vehicles (held as their hull) nor the rope gun (held as a gun).</summary>
    public static readonly string[] Gadgets =
    {
        "blueprint_tool", "field_medkit", "stasis_projector", "terrain_blaster", "fluid_pump", "terrain_scanner",
        "creature_translator", "weather_scanner", "camera", "paint_tool", "shape_tool", "shape_stencil", "binoculars",
        "thermal_binoculars", "rail_linker", "rail_cab", "wagon_seats", "wagon_sleeper", "wagon_bar", "bio_sampler",
        "remote_control",
    };

    public static TheoryData<string> GadgetKeys()
    {
        var data = new TheoryData<string>();
        foreach (string key in Gadgets)
        {
            data.Add(key);
        }

        return data;
    }

    [Fact]
    public void TheList_IsEveryGadgetTheHandHoldsAsAGadget()
    {
        var inData = Content.Items.Values
            .Where(i => i.Tool?.Kind == ToolKind.Gadget && i.Vehicle == null && !i.Key.EndsWith("_gun", System.StringComparison.Ordinal))
            .Select(i => i.Key).OrderBy(k => k, System.StringComparer.Ordinal);
        Assert.Equal(Gadgets.OrderBy(k => k, System.StringComparer.Ordinal), inData);
    }

    [Fact]
    public void AGadget_IsAShapedKind_LikeTheTools()
    {
        Assert.True(HeldItemShapes.IsShapedKind(HeldItemShapes.GadgetKind)); // so the tool-look editor offers gadgets too
        Assert.True(HeldItemShapes.IsShapedKind("Drill"));
        Assert.False(HeldItemShapes.IsShapedKind("Block"));
    }

    [Theory]
    [MemberData(nameof(GadgetKeys))]
    public void EveryGadget_DrawsItsOwnModel(string key)
    {
        var model = Content.GetItem(key)!.HeldModel;
        Assert.NotNull(model);
        Assert.InRange(model!.Count, 4, 14);
        Assert.True(model.Count <= HeldModelPart.MaxParts);
        Assert.All(model, p => Assert.True(p.IsValid(), $"{key} has a part the game cannot draw"));
        Assert.All(model, p => Assert.True(HeldMaterials.IsKnown(p.M), $"{key}: every gadget part names its material"));

        var parts = HeldItemShapes.Parts(HeldItemShapes.GadgetKind, Tint, model)!;
        Assert.Equal(model.Count, parts.Count); // its own parts, not the emitter
        Assert.Equal(model.Select(p => p.M), parts.Select(p => p.Material));
    }

    [Fact]
    public void TheRemoteControl_ShowsItsModel_NotTheEmitter()
    {
        var parts = HeldItemShapes.Parts(HeldItemShapes.GadgetKind, Tint, Content.GetItem("remote_control")!.HeldModel)!;
        Assert.Contains(parts, p => p.Size == new Vector3(0.07f, 0.03f, 0.14f)); // the flat handset
        Assert.Contains(parts, p => System.Math.Abs(p.Color.R - (0xb9 / 255f)) < 1e-3f); // its purple switch
        Assert.DoesNotContain(parts, p => p.Size == new Vector3(0.16f, 0.13f, 0.20f)); // no emitter body
    }

    [Fact]
    public void AGadgetWithoutAModel_KeepsTheEmitter_ItsTipInTheTint()
    {
        var parts = HeldItemShapes.Parts(HeldItemShapes.GadgetKind, Tint)!;
        Assert.Equal(4, parts.Count);
        var tip = parts.OrderByDescending(p => p.Position.Z).First();
        Assert.Equal(Tint.R, tip.Color.R);
        Assert.Equal(HeldMaterials.Energy, tip.Material);
        Assert.NotEqual(Vector3.Zero, HeldHands.HandlePoint(parts)); // held by its pistol grip, as before
    }

    [Fact]
    public void EveryGadget_LooksDifferent()
    {
        string Signature(string key) => string.Join("|", Content.GetItem(key)!.HeldModel!.Select(p => string.Format(CultureInfo.InvariantCulture,
            "{0:0.###},{1:0.###},{2:0.###}/{3:0.###},{4:0.###},{5:0.###}/{6}", p.P[0], p.P[1], p.P[2], p.S[0], p.S[1], p.S[2], p.C)));
        Assert.Equal(Gadgets.Length, Gadgets.Select(Signature).Distinct().Count());
    }

    [Theory]
    [MemberData(nameof(GadgetKeys))]
    public void NoTwoPartsOfAGadget_ShareAFacePlane(string key)
    {
        // Two faces in one plane that face the same way and overlap z-fight — with two materials, visibly.
        var parts = HeldItemShapes.FromModel(Content.GetItem(key)!.HeldModel, Tint);
        Assert.Empty(SharedFaces(parts));
    }

    [Theory]
    [MemberData(nameof(GadgetKeys))]
    public void EveryGadget_IsHeldInTheFist_AndShowsPastIt(string key)
    {
        // The fist closes round the origin (or a pistol grip): what the eye should see of the gadget — a screen, the
        // lenses, the toy wagon — has to stand above the fist or reach out in front of it, not hide inside it.
        var parts = HeldItemShapes.FromModel(Content.GetItem(key)!.HeldModel, Tint);
        Assert.Equal(HeldGrips.Fist, HeldHands.GripFor(Content.GetItem(key), HeldItemShapes.GadgetKind));
        var hand = HeldHands.For(HeldGrips.Fist, parts, Vector3.Zero, Vector3.Zero);
        var fist = hand.Where(b => b.Piece == HeldHands.Piece.Hand).OrderByDescending(b => b.Size.X).First();
        float fistTop = fist.Position.Y + (fist.Size.Y * 0.5f);
        float fistFront = fist.Position.Z + (fist.Size.Z * 0.5f);
        float itemTop = parts.Max(p => p.Position.Y + (p.Size.Y * 0.5f));
        float itemFront = parts.Max(p => p.Position.Z + (p.Size.Z * 0.5f));
        Assert.True(itemTop > fistTop + 0.04f || itemFront > fistFront + 0.05f, $"{key}: it hides in the fist");
    }

    /// <summary>The same-facing, overlapping, coplanar face pairs of <paramref name="parts"/> ("2/5 +y").</summary>
    internal static List<string> SharedFaces(IReadOnlyList<HeldItemShapes.Part> parts)
    {
        const float Tol = 1e-4f;
        var found = new List<string>();
        for (int i = 0; i < parts.Count; i++)
        {
            for (int j = i + 1; j < parts.Count; j++)
            {
                var aMin = parts[i].Position - (parts[i].Size * 0.5f);
                var aMax = parts[i].Position + (parts[i].Size * 0.5f);
                var bMin = parts[j].Position - (parts[j].Size * 0.5f);
                var bMax = parts[j].Position + (parts[j].Size * 0.5f);
                for (int axis = 0; axis < 3; axis++)
                {
                    bool overlap = true;
                    for (int other = 0; other < 3; other++)
                    {
                        if (other != axis && System.Math.Min(Get(aMax, other), Get(bMax, other)) - System.Math.Max(Get(aMin, other), Get(bMin, other)) <= Tol)
                        {
                            overlap = false;
                        }
                    }

                    if (!overlap)
                    {
                        continue;
                    }

                    string name = axis == 0 ? "x" : axis == 1 ? "y" : "z";
                    if (System.Math.Abs(Get(aMin, axis) - Get(bMin, axis)) <= Tol)
                    {
                        found.Add($"{i}/{j} -{name}");
                    }

                    if (System.Math.Abs(Get(aMax, axis) - Get(bMax, axis)) <= Tol)
                    {
                        found.Add($"{i}/{j} +{name}");
                    }
                }
            }
        }

        return found;
    }

    private static float Get(Vector3 v, int axis) => axis == 0 ? v.X : axis == 1 ? v.Y : v.Z;
}
