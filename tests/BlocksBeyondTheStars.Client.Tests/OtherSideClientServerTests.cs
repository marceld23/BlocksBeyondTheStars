// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Sounders;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The other side on the wire (#3906, #3908): the real client crosses on the real server (the QA verb), learns where it
/// stands and where the gates are, shapes a sound block with the Resonance Blaster — in the tone it last played on the
/// melody bar — sees it in its own world, dissolves it, shapes another, comes back and finds the sound gone.
/// </summary>
public sealed class OtherSideClientServerTests
{
    [Fact]
    public void ARealClient_Crosses_ShapesSound_AndComesBack()
    {
        var content = ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir());
        ushort sound = content.GetBlock(OtherSideRules.SoundBlockKey)!.NumericId.Value;
        using var h = new ClientServerHarness(content, c =>
        {
            c.PlaceStarterShip = false;
            c.Rules = new GameRules { AdminCheats = true, AllowCheatsInSurvival = true };
        }, passObjects: false);
        h.World.SetGlowLookBlocks(new[] { sound });
        h.Client.ShowsOtherSide = true; // announces otherside1 with the join

        var view = new OtherSideView();
        var shaper = new SoundShaper();
        var palette = new SoundPalette();
        var cues = new List<OtherSideCue>();
        var gateLists = new List<OtherSideGates>();
        var said = new List<string>();
        h.Client.OtherSideStateReceived += m => cues.Add(view.Apply(m));
        h.Client.OtherSideGatesReceived += m =>
        {
            gateLists.Add(m);
            view.Gates.Apply(m);
        };
        h.Client.ServerMessageReceived += m => said.Add(m.Text);
        h.Client.ActionRejected += m => said.Add(m.Action + ":" + m.Reason);

        h.Join("Justus"); // the first joiner owns the world
        Assert.NotNull(h.JoinAccepted);
        Assert.True(h.PumpUntil(() => view.Version > 0), "the join never said where the player stands");
        Assert.False(view.Over);
        Assert.Equal(OtherSideCue.None, cues[^1]);
        Assert.Empty(gateLists); // nobody on our side is sent the gates

        var session = h.Server.Sessions.Values.Single(s => s.State.PlayerId == h.JoinAccepted!.PlayerId);
        var p = session.State;
        p.AboardShip = false;
        p.Inventory.SetSlot(0, new ItemStack(SoundersIds.ResonanceBlaster, 1));
        p.SelectedHotbarSlot = 0;

        // ---- across ----
        h.Client.SendAdminCommand("otherside");
        Assert.True(h.PumpUntil(() => view.Over), "the client never crossed: " + string.Join(" / ", said));
        Assert.Equal(OtherSideCue.Cross, cues[^1]);
        Assert.True(view.HasGate);
        Assert.Equal(OtherSideRules.SoundBlockCap, view.SoundBlockCap);
        Assert.True(h.PumpUntil(() => view.Gates.Count > 0), "the gate list never arrived");
        Assert.Contains(Enumerable.Range(0, view.Gates.Count).Select(i => view.Gates[i]), g => g.Entry);
        view.Tick(OtherSideView.BlendSeconds);
        Assert.Equal(1f, view.Look);

        // ---- the palette: the tone played last on the melody bar ----
        h.Client.SendMelodyNote(5, string.Empty, SoundersIds.InstrumentVoice);
        palette.Played(5);

        // ---- shape: the use key, the blaster in hand ----
        var aim = Aim(h, p);
        var cell = new Vector3i(aim.CellX, aim.CellY, aim.CellZ);
        Assert.True(h.PumpUntil(() => h.World.TryGetBlock(cell.X, cell.Y, cell.Z, out _), 400), "the chunk at the aim never streamed");
        aim = Aim(h, p); // with the terrain there
        cell = new Vector3i(aim.CellX, aim.CellY, aim.CellZ);
        Assert.Equal(SoundShapeAction.Shape, shaper.Tick(0.016f, view.ShapesSound(blasterInHand: true), useHeld: true, mineHeld: false, aimsAtSoundBlock: false));
        h.Client.SendUseGadget(SoundersIds.ResonanceBlaster, new Vector3f(aim.PointX, aim.PointY, aim.PointZ));
        Assert.True(h.PumpUntil(() => h.World.GetBlock(cell.X, cell.Y, cell.Z).Value == sound),
            "no sound block in the client's world: " + string.Join(" / ", said));
        Assert.Equal(OtherSideRules.GlowOfTone(palette.Tone), h.World.GetGlow(cell.X, cell.Y, cell.Z)); // the palette's tone
        Assert.Equal(5, OtherSideRules.ToneOfGlow(h.World.GetGlow(cell.X, cell.Y, cell.Z)));           // … which a step on it sounds
        Assert.True(h.PumpUntil(() => view.SoundBlocksUsed == 1), "the meter never moved");
        Assert.Equal(0, h.World.LightSourceAt(cell.X, cell.Y, cell.Z)); // it glows, it is no light

        // ---- dissolve: the mine key on the sound block ----
        Pace(h);
        var onSound = Aim(h, p);
        Assert.True(onSound.Surface);
        Assert.Equal((cell.X, cell.Y, cell.Z), (onSound.HitX, onSound.HitY, onSound.HitZ)); // the crosshair now rests on it
        h.Client.SendUseGadget(SoundersIds.ResonanceBlaster, new Vector3f(onSound.PointX, onSound.PointY, onSound.PointZ), alt: true);
        Assert.True(h.PumpUntil(() => h.World.GetBlock(cell.X, cell.Y, cell.Z).Value != sound),
            "the sound block never dissolved: " + string.Join(" / ", said));
        Assert.True(h.PumpUntil(() => view.SoundBlocksUsed == 0));

        // ---- shape it again, then come back: the client's world shows our side ----
        Pace(h);
        h.Client.SendUseGadget(SoundersIds.ResonanceBlaster, new Vector3f(aim.PointX, aim.PointY, aim.PointZ));
        Assert.True(h.PumpUntil(() => h.World.GetBlock(cell.X, cell.Y, cell.Z).Value == sound), "the second block never came");

        h.Client.SendAdminCommand("otherside", stringArg: "off");
        Assert.True(h.PumpUntil(() => !view.Over), "the client never came back: " + string.Join(" / ", said));
        Assert.Equal(OtherSideCue.Return, cues[^1]);
        Assert.False(view.HasGate);
        Assert.True(h.PumpUntil(() => h.World.GetBlock(cell.X, cell.Y, cell.Z).Value != sound, 100), "the sound block stayed in the client's world on our side");
        Assert.True(h.PumpUntil(() => gateLists.Count > 0 && (gateLists[^1].Gates?.Length ?? 0) == 0), "the lights never went out");
        Assert.Equal(0, view.Gates.Count);
        Assert.DoesNotContain(said, t => t.Contains("gadget:")); // no pulse was refused on the way
    }

    /// <summary>The crosshair of a first-person player who looks ahead and a little down, as the client resolves it.</summary>
    private static SoundAimResult Aim(ClientServerHarness h, PlayerState p)
    {
        float eyeY = p.Position.Y + SoundAim.EyeHeight;
        const float len = 1.0440307f; // of (1, −0.3, 0)
        return SoundAim.Resolve((x, y, z) => !h.World.GetBlock(x, y, z).IsAir, p.Position.X, eyeY, p.Position.Z,
            1f / len, -0.3f / len, 0f, p.Position.X, p.Position.Y, p.Position.Z, SoundAim.DefaultReach);
    }

    /// <summary>Lets the blaster's cadence pass on the server (a pulse inside it is dropped quietly).</summary>
    private static void Pace(ClientServerHarness h)
    {
        for (int i = 0; i < 4; i++)
        {
            h.Tick(0.1);
        }
    }
}
