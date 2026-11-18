// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3769 on the client (Justus: startled NPCs "raise their hands and open their mouth wide"; "put a frame around the texts
/// that float above them, like in Zelda"): the hands-up curve starts and ends at rest with the mouth wide in between, the
/// speech bubble's plate follows its text, and the Unity wiring is guarded as source text (the scripts cannot load
/// headless — the <see cref="ClientQuirksWiringTests"/> pattern).
/// </summary>
public sealed class NpcStartleClientTests
{
    private static string Script(string file)
        => File.ReadAllText(Path.Combine(ClientTestPaths.RepoRoot(), "client", "Assets", "BlocksBeyondTheStars", "Scripts", file));

    [Fact]
    public void TheHandsUp_StartAndEndAtRest_GoUpFast_AndTheMouthIsWideInBetween()
    {
        foreach (float c in new[] { 0f, 1f, -0.5f, 1.5f })
        {
            Assert.Equal((0f, 0f), AvatarGestures.HandsUp(c));
        }

        Assert.Equal(1f, AvatarGestures.HandsUp(0.1f).Blend, 3);  // up within a tenth of the gesture
        Assert.Equal(1f, AvatarGestures.HandsUp(0.5f).Blend, 3);  // held
        Assert.True(AvatarGestures.HandsUp(0.99f).Blend < 0.05f); // down again at the end
        Assert.Equal(1f, AvatarGestures.HandsUp(0.1f).Gasp, 3);   // the mouth is open at once …
        Assert.Equal(1f, AvatarGestures.HandsUp(0.55f).Gasp, 3);
        Assert.Equal(0f, AvatarGestures.HandsUp(0.85f).Gasp, 3);  // … and shut before the arms come down
        Assert.True(AvatarGestures.HandsUpLift > 150f, "straight up, higher than the cheer's V");
        Assert.InRange(AvatarGestures.HandsUpSeconds, 1.2f, 2.2f);
        Assert.True(AvatarGestures.GaspMouth >= 3f, "clearly wider than talking");

        // No jump from frame to frame at 60 Hz.
        const float frame = 1f / 60f;
        float last = 0f;
        for (float t = frame; t <= AvatarGestures.HandsUpSeconds; t += frame)
        {
            float now = AvatarGestures.HandsUp(t / AvatarGestures.HandsUpSeconds).Blend;
            Assert.True(Math.Abs(now - last) < 0.35f, $"the arms jump by {now - last:0.00} at {t:0.000} s");
            last = now;
        }
    }

    [Fact]
    public void TheBubblesPlate_FollowsTheText_NeverNarrowerThanItsCorners()
    {
        const float wrap = 360f, line = 22f;
        var small = NpcBubbleFrame.Plate(30f, line, wrap, line);
        Assert.Equal(NpcBubbleFrame.MinWidth, small.Width);
        Assert.Equal(line + (2f * NpcBubbleFrame.PadY), small.Height);

        var one = NpcBubbleFrame.Plate(200f, line, wrap, line);
        Assert.Equal(200f + (2f * NpcBubbleFrame.PadX), one.Width);

        var wrapped = NpcBubbleFrame.Plate(900f, 3f * line, wrap, line); // a long line wraps at the bubble's width
        Assert.Equal(wrap + (2f * NpcBubbleFrame.PadX), wrapped.Width);
        Assert.Equal((3f * line) + (2f * NpcBubbleFrame.PadY), wrapped.Height);

        Assert.Equal(line + (2f * NpcBubbleFrame.PadY), NpcBubbleFrame.Plate(100f, 0f, wrap, line).Height); // never flatter than a line
        Assert.True(NpcBubbleFrame.MinWidth >= 2f * 18f, "the rounded corners (radius 18) must not overlap");
    }

    [Fact]
    public void TheClient_PlaysTheGesture_FramesTheBubble_AndPutsAPlateUnderTheToast()
    {
        Assert.Equal(4, NpcGestures.HandsUp);
        Assert.Contains("case NpcGestures.HandsUp:", Script("NpcView.cs"));
        Assert.Contains("n.Avatar.Startle();", Script("NpcView.Feel.cs"));

        string startle = Script("PlayerAvatar.Startle.cs");
        Assert.Contains("AvatarGestures.HandsUp(c).Blend", startle);
        Assert.Contains("AvatarGestures.GaspMouth", startle);
        Assert.Contains("var (k, bob, up) = ArmsUp();", Script("PlayerAvatar.cs"));
        Assert.Contains("Mathf.Max(GaspFactor(),", Script("PlayerAvatar.Feel.cs"));

        string labels = Script("ScreenLabelLayer.cs");
        Assert.Contains("NpcBubbleFrame.Plate(e.Main.preferredWidth, e.Main.preferredHeight, BubbleWidth, BubbleLineHeight)", labels);
        Assert.Contains("plate.sprite = UiKit.PanelSprite;", labels);
        Assert.Contains("var plate = MakeBubblePlate(rt);", labels); // before the text, so it draws behind it

        string toast = Script("HudUi.Toasts.cs");
        Assert.True(toast.IndexOf("BuildToastPlate(root);", StringComparison.Ordinal) < toast.IndexOf("_toast = UiText.Add(", StringComparison.Ordinal),
            "the plate must be built before the toast's text, or it would cover it");
        Assert.Contains("SyncToastPlate();", toast);
    }
}
