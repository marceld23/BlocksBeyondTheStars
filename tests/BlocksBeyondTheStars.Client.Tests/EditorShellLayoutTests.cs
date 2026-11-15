// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3688 / #3695: the editor shell's geometry (<see cref="EditorShellLayout"/>) in the 1920 × 1080 reference — Back top
/// left, Save rightmost, the actions in a fixed order, nothing overlapping, every control at least 44 px, missing actions
/// leaving no hole — and the status text rule: no file paths (<see cref="EditorStatusText"/>).
/// </summary>
public sealed class EditorShellLayoutTests
{
    public static TheoryData<EditorShellActions> EveryActionSet()
    {
        var data = new TheoryData<EditorShellActions>();
        for (int i = 0; i <= (int)EditorShellActions.All; i++)
        {
            data.Add((EditorShellActions)i);
        }

        return data;
    }

    private static readonly EditorHeaderSlot[] Buttons =
    {
        EditorHeaderSlot.Back, EditorHeaderSlot.Undo, EditorHeaderSlot.Redo, EditorHeaderSlot.Load, EditorHeaderSlot.New,
        EditorHeaderSlot.Submit, EditorHeaderSlot.Save,
    };

    [Theory]
    [MemberData(nameof(EveryActionSet))]
    public void Header_KeepsItsOrder_NothingOverlaps_AndEverythingFits(EditorShellActions actions)
    {
        var layout = EditorShellLayout.Compute(actions);
        var present = layout.Order.Where(s => s != EditorHeaderSlot.SaveSubline).ToList();
        for (int i = 1; i < present.Count; i++)
        {
            var left = layout.Rect(present[i - 1]);
            var right = layout.Rect(present[i]);
            Assert.True(left.Right <= right.X, $"{present[i - 1]} ends before {present[i]} starts ({actions})");
            Assert.True(right.X - left.Right >= EditorShellLayout.Gap, $"{present[i - 1]} and {present[i]} keep a gap ({actions})");
        }

        foreach (var slot in layout.Order)
        {
            Assert.True(layout.Rect(slot).InsideCanvas(EditorShellLayout.CanvasW, EditorShellLayout.HeaderH + 1f, 0f),
                $"{slot} lies in the header ({actions})");
        }

        Assert.Equal(EditorHeaderSlot.Back, layout.Order[0]);
        Assert.Equal(EditorHeaderSlot.Title, layout.Order[1]);
        Assert.True(layout.Rect(EditorHeaderSlot.Title).W >= EditorShellLayout.MinTitleW, $"the title keeps room ({actions})");
    }

    [Theory]
    [MemberData(nameof(EveryActionSet))]
    public void Header_ButtonsAreAtLeastTheTapSize_AndSaveIsRightmost(EditorShellActions actions)
    {
        var layout = EditorShellLayout.Compute(actions);
        foreach (var slot in Buttons.Where(layout.Has))
        {
            var r = layout.Rect(slot);
            Assert.True(r.W >= EditorShellLayout.MinControl && r.H >= EditorShellLayout.MinControl, $"{slot} is at least 44 × 44");
        }

        var save = layout.Rect(EditorHeaderSlot.Save);
        Assert.Equal(EditorShellLayout.CanvasW - EditorShellLayout.Margin, save.Right);
        Assert.Equal(EditorShellLayout.Margin, layout.Rect(EditorHeaderSlot.Back).X);
        var sub = layout.Rect(EditorHeaderSlot.SaveSubline);
        Assert.Equal(save.Right, sub.Right);
        Assert.True(sub.Y >= save.Bottom, "the subline sits under Save");
    }

    [Theory]
    [InlineData(EditorShellActions.None, new[] { EditorHeaderSlot.Back, EditorHeaderSlot.Title, EditorHeaderSlot.Save })]
    [InlineData(EditorShellActions.Undo, new[]
    {
        EditorHeaderSlot.Back, EditorHeaderSlot.Title, EditorHeaderSlot.Undo, EditorHeaderSlot.Redo, EditorHeaderSlot.Save,
    })]
    [InlineData(EditorShellActions.All, new[]
    {
        EditorHeaderSlot.Back, EditorHeaderSlot.Title, EditorHeaderSlot.Undo, EditorHeaderSlot.Redo, EditorHeaderSlot.Load,
        EditorHeaderSlot.New, EditorHeaderSlot.Submit, EditorHeaderSlot.Save,
    })]
    public void Header_ShowsOnlyTheActionsTheEditorHas(EditorShellActions actions, EditorHeaderSlot[] expected)
    {
        var layout = EditorShellLayout.Compute(actions);
        Assert.Equal(expected.Append(EditorHeaderSlot.SaveSubline), layout.Order);
        foreach (var slot in Buttons.Where(s => !expected.Contains(s)))
        {
            Assert.False(layout.Has(slot));
            Assert.Throws<KeyNotFoundException>(() => layout.Rect(slot));
        }
    }

    [Fact]
    public void Header_GroupsAreSetApart_ButtonsInAGroupSitClose()
    {
        var layout = EditorShellLayout.Compute(EditorShellActions.All);
        Assert.Equal(EditorShellLayout.Gap, layout.Rect(EditorHeaderSlot.Redo).X - layout.Rect(EditorHeaderSlot.Undo).Right);
        Assert.Equal(EditorShellLayout.GroupGap, layout.Rect(EditorHeaderSlot.Load).X - layout.Rect(EditorHeaderSlot.Redo).Right);
        Assert.Equal(EditorShellLayout.Gap, layout.Rect(EditorHeaderSlot.New).X - layout.Rect(EditorHeaderSlot.Load).Right);
        Assert.Equal(EditorShellLayout.GroupGap, layout.Rect(EditorHeaderSlot.Submit).X - layout.Rect(EditorHeaderSlot.New).Right);
        Assert.Equal(EditorShellLayout.GroupGap, layout.Rect(EditorHeaderSlot.Save).X - layout.Rect(EditorHeaderSlot.Submit).Right);
        Assert.True(layout.Rect(EditorHeaderSlot.Title).W >= 480f, "with every action the title still has about 500 px");
    }

    [Fact]
    public void WorkArea_HeaderAndStatusBar_TileTheCanvas()
    {
        var header = EditorShellLayout.HeaderRect;
        var work = EditorShellLayout.WorkRect;
        var status = EditorShellLayout.StatusRect;
        Assert.Equal(header.Bottom, work.Y);
        Assert.Equal(work.Bottom, status.Y);
        Assert.Equal(EditorShellLayout.CanvasH, status.Bottom);
        Assert.False(header.Overlaps(work));
        Assert.False(work.Overlaps(status));
    }

    [Theory]
    [InlineData(false, 320f)]
    [InlineData(true, 420f)]
    [InlineData(true, 5000f)]
    public void SideCards_StayInTheWorkArea_OnTheirSide(bool right, float width)
    {
        var card = EditorShellLayout.SideCard(right, width);
        var work = EditorShellLayout.WorkRect;
        Assert.True(card.X >= work.X + EditorShellLayout.Margin && card.Right <= work.Right - EditorShellLayout.Margin);
        Assert.True(card.Y >= work.Y && card.Bottom <= work.Bottom);
        Assert.Equal(right, card.X > work.W / 2f);
        Assert.True(card.W <= work.W / 2f, "a side card never takes more than half the work area");
    }

    // ---- status text: no file paths ------------------------------------------------------------------------------------

    [Theory]
    [InlineData("Saved 'X' to:\nC:\\Users\\Kid Name\\AppData\\LocalLow\\JuMaVe\\ships\\cruiser.json\nFor everyone?",
        "Saved 'X' to:\ncruiser.json\nFor everyone?")]
    [InlineData("Exported to C:\\Users\\kid\\Documents\\pack. Restart the game.", "Exported to pack. Restart the game.")]
    [InlineData("Saved content bundle to: /home/kid/.config/unity3d/JuMaVe/content/bundle_1", "Saved content bundle to: bundle_1")]
    [InlineData("Open file:///C:/Users/kid/textures/ in the browser", "Open textures in the browser")]
    [InlineData("Read failed: 'D:\\games\\bbs\\ship.json' is locked", "Read failed: 'ship.json' is locked")]
    [InlineData("On the share \\\\server\\kids\\ship.json", "On the share ship.json")]
    [InlineData("~/drafts/ship.draft is gone", "ship.draft is gone")]
    [InlineData("Drive C:\\ only", "Drive … only")]
    public void StatusText_CutsPathsToTheirLastPart(string text, string expected)
        => Assert.Equal(expected, EditorStatusText.WithoutPaths(text));

    [Theory]
    [InlineData("Saved “My cruiser” (12 blocks).")]
    [InlineData("SAVE / EXPORT")]
    [InlineData("Use /tp civ to go there")]
    [InlineData("1/2 of the blocks, and/or more")]
    [InlineData("See https://example.org/docs/editor for help")]
    [InlineData("Ratio 3:4 is fine")]
    [InlineData("")]
    public void StatusText_LeavesTextWithoutPathsAlone(string text)
        => Assert.Equal(text, EditorStatusText.WithoutPaths(text));

    [Fact]
    public void StatusText_NullIsEmpty() => Assert.Equal(string.Empty, EditorStatusText.WithoutPaths(null));
}
