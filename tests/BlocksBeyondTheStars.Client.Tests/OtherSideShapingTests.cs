// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Sounders;
using BlocksBeyondTheStars.Shared.World;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The Resonance Blaster on the other side, client half (#3908): the two keys and their pace, the aim the client sends
/// and the cell it means, the melody bar as the palette, the tones of a walk over sound blocks, the gates the client
/// finds itself on our side, and sound blocks that glow without being lights (#3906).
/// </summary>
[Trait("Suite", "ClientCore")]
public sealed class OtherSideShapingTests
{
    // ---- the trigger ---------------------------------------------------------------------------------------------

    [Fact]
    public void TheUseKey_Shapes_AtTheServersPace_WhileHeld()
    {
        var shaper = new SoundShaper();
        Assert.Equal(SoundShapeAction.Shape, shaper.Tick(0.016f, active: true, useHeld: true, mineHeld: false, aimsAtSoundBlock: false));

        int pulses = 0;
        for (float t = 0f; t < 2f; t += 0.016f)
        {
            if (shaper.Tick(0.016f, true, true, false, false) == SoundShapeAction.Shape)
            {
                pulses++;
            }
        }

        // Two seconds held: about nine pulses, never more than the server's five a second takes.
        Assert.InRange(pulses, 8, 10);
        Assert.True(SoundShaper.Interval >= (float)OtherSideRules.ShapeCooldownSeconds);
    }

    [Fact]
    public void TheMineKey_Dissolves_OnlyASoundBlock()
    {
        var shaper = new SoundShaper();
        Assert.Equal(SoundShapeAction.None, shaper.Tick(0.016f, true, useHeld: false, mineHeld: true, aimsAtSoundBlock: false));
        Assert.Equal(SoundShapeAction.Dissolve, shaper.Tick(0.016f, true, useHeld: false, mineHeld: true, aimsAtSoundBlock: true));
        Assert.Equal(SoundShapeAction.None, shaper.Tick(0.016f, true, false, true, true)); // the pace holds for both keys
        Assert.Equal(SoundShapeAction.Shape, shaper.Tick(SoundShaper.Interval, true, useHeld: true, mineHeld: true, aimsAtSoundBlock: true)); // use wins
    }

    [Fact]
    public void WithoutTheBlasterOverThere_NothingGoesOut_AndARefusalRests()
    {
        var shaper = new SoundShaper();
        Assert.Equal(SoundShapeAction.None, shaper.Tick(0.016f, active: false, useHeld: true, mineHeld: true, aimsAtSoundBlock: true));
        Assert.Equal(SoundShapeAction.Shape, shaper.Tick(0.016f, true, true, false, false));

        shaper.Hold(); // the server refused: it drops every pulse for a second
        Assert.Equal(SoundShapeAction.None, shaper.Tick(0.5f, true, true, false, false));
        Assert.Equal(SoundShapeAction.None, shaper.Tick(0.4f, true, true, false, false));
        Assert.Equal(SoundShapeAction.Shape, shaper.Tick(0.2f, true, true, false, false));

        shaper.Hold();
        shaper.Reset();
        Assert.Equal(SoundShapeAction.Shape, shaper.Tick(0.016f, true, true, false, false));
    }

    // ---- the aim -------------------------------------------------------------------------------------------------

    /// <summary>A floor: every cell below y = 10 is solid.</summary>
    private static bool Floor(int x, int y, int z) => y < 10;

    [Fact]
    public void AimedAtAFace_TheNearCellGetsTheBlock_AndThePointIsTheFacesMiddle()
    {
        // Feet on the floor at (5.5, 10, 5.5), first person: the camera is the eyes. Looking down and ahead.
        var aim = SoundAim.Resolve(Floor, 5.5f, 11.5f, 5.5f, 0.6f, -0.8f, 0f, 5.5f, 10f, 5.5f, 8f);

        Assert.True(aim.Surface);
        Assert.Equal((6, 9, 5), (aim.HitX, aim.HitY, aim.HitZ));      // the floor cell the crosshair rests on
        Assert.Equal((6, 10, 5), (aim.CellX, aim.CellY, aim.CellZ));  // the block grows on top of it
        Assert.Equal(6.5f, aim.PointX, 3);
        Assert.Equal(10f, aim.PointY, 3);                             // the top face's middle
        Assert.Equal(5.5f, aim.PointZ, 3);
    }

    [Fact]
    public void AimedAtASoundBlocksSide_TheBlockGrowsSideways()
    {
        // A sound block hangs at eye height at (8, 11, 5); the player looks straight at its −X side.
        bool Solid(int x, int y, int z) => y < 10 || (x == 8 && y == 11 && z == 5);
        var aim = SoundAim.Resolve(Solid, 5.5f, 11.5f, 5.5f, 1f, 0f, 0f, 5.5f, 10f, 5.5f, 8f);

        Assert.True(aim.Surface);
        Assert.Equal((8, 11, 5), (aim.HitX, aim.HitY, aim.HitZ)); // what a dissolve means
        Assert.Equal((7, 11, 5), (aim.CellX, aim.CellY, aim.CellZ));
        Assert.Equal(8f, aim.PointX, 3);
    }

    [Fact]
    public void WithNothingInReach_ThePointLiesInTheOpenAir_AtTheBlastersReach()
    {
        // On a cliff's edge, looking level over the drop: the bridge leaves the cliff.
        bool Cliff(int x, int y, int z) => y < 10 && x < 6;
        var aim = SoundAim.Resolve(Cliff, 5.5f, 11.5f, 5.5f, 1f, 0f, 0f, 5.5f, 10f, 5.5f, 8f);

        Assert.False(aim.Surface);
        Assert.Equal((13, 11, 5), (aim.CellX, aim.CellY, aim.CellZ));
        Assert.Equal(13.5f, aim.PointX, 3);
    }

    [Fact]
    public void AThirdPersonCamera_NamesTheSameCells_AsTheEyes()
    {
        // The camera stands three blocks behind and one above the eyes and looks at the same floor cell.
        float camX = 2.5f, camY = 12.5f, camZ = 5.5f;
        float dx = 6.5f - camX, dy = 10f - camY, dz = 0f;
        float len = (float)System.Math.Sqrt((dx * dx) + (dy * dy));
        var aim = SoundAim.Resolve(Floor, camX, camY, camZ, dx / len, dy / len, dz, 5.5f, 10f, 5.5f, 8f);

        Assert.True(aim.Surface);
        Assert.Equal((6, 9, 5), (aim.HitX, aim.HitY, aim.HitZ));
        Assert.Equal((6, 10, 5), (aim.CellX, aim.CellY, aim.CellZ));
    }

    [Fact]
    public void AFaceFartherThanTheBlasterReaches_IsNoSurface()
    {
        // The floor lies twenty blocks under a player in the air: the pulse ends in the open, at reach.
        var aim = SoundAim.Resolve(Floor, 5.5f, 31.5f, 5.5f, 0f, -1f, 0f, 5.5f, 30f, 5.5f, 8f);
        Assert.False(aim.Surface);
        Assert.Equal(23, aim.CellY); // eyes at 31.5, eight down
    }

    // ---- the palette ---------------------------------------------------------------------------------------------

    [Fact]
    public void ThePalette_IsTheToneLastPlayed_ZeroAtTheStart()
    {
        var palette = new SoundPalette();
        Assert.Equal(0, palette.Tone);

        Assert.True(palette.Played(5, 1.0));
        Assert.Equal(5, palette.Tone);
        int version = palette.Version;
        palette.Played(5, 1.5);
        Assert.Equal(version, palette.Version);
        palette.Played(-1, 2.0); // leaving the bar is no tone
        palette.Played(8, 2.5);
        Assert.Equal(5, palette.Tone);

        palette.Reset();
        Assert.Equal(0, palette.Tone);

        // Eight brightness steps, each one lighter than the one before — the look over there is greyscale.
        float last = -1f;
        for (int tone = 0; tone < OtherSideRules.ToneGlow.Length; tone++)
        {
            var (r, g, b) = SoundPalette.ColorOf(tone);
            float level = (0.299f * r) + (0.587f * g) + (0.114f * b);
            Assert.True(level > last + 0.05f, $"tone {tone} is not clearly lighter than tone {tone - 1}");
            last = level;
        }
    }

    [Fact]
    public void ThePalette_MarksTheLastToneTheServerTook_NotOneMashedPastItsNoteBudget()
    {
        // The server takes a burst of sixteen notes and ten a second after that; the rest it drops unheard — and the
        // next sound block gets the last tone it TOOK. The palette keeps the same budget on the send times.
        var palette = new SoundPalette();
        double t = 10.0;
        for (int i = 0; i < (int)SoundPalette.NoteBurst; i++)
        {
            Assert.True(palette.Played(i % 8, t), $"note {i} of the burst");
        }

        Assert.Equal(7, palette.Tone); // the sixteenth note: tone 15 % 8
        Assert.False(palette.Played(2, t)); // the seventeenth at the same instant: dropped
        Assert.False(palette.Played(3, t + 0.05)); // half a note refilled: still dropped
        Assert.Equal(7, palette.Tone);

        Assert.True(palette.Played(4, t + 0.12)); // a good tenth of a second at ten a second: one note
        Assert.Equal(4, palette.Tone);
        Assert.False(palette.Played(1, t + 0.12));
        Assert.Equal(4, palette.Tone);

        // The bar's "leave" is no tone, but it takes one of the budget like every note.
        Assert.True(palette.Played(-1, t + 0.25));
        Assert.False(palette.Played(6, t + 0.25));
        Assert.Equal(4, palette.Tone);

        // The budget never holds more than the burst, however long the bar rested.
        t += 60.0;
        int taken = 0;
        for (int i = 0; i < 40; i++)
        {
            taken += palette.Played(i % 8, t) ? 1 : 0;
        }

        Assert.Equal((int)SoundPalette.NoteBurst, taken);

        // A calm melody (five notes a second) is never dropped.
        palette.Reset();
        for (int i = 0; i < 200; i++)
        {
            Assert.True(palette.Played(i % 8, 100.0 + (i * 0.2)));
        }

        // The numbers are the server's (its constants are private; this pins the mirror to the source).
        string server = File.ReadAllText(Path.Combine(ClientTestPaths.RepoRoot(), "src", "BlocksBeyondTheStars.GameServer", "GameServerMelodyPuzzles.cs"));
        Assert.Contains("private const double MelodyNoteBurst = 16;", server);
        Assert.Contains("private const double MelodyNotesPerSecond = 10;", server);
        Assert.Equal((16.0, 10.0), (SoundPalette.NoteBurst, SoundPalette.NotesPerSecond));
    }

    // ---- the gamepad's hands under the open melody bar ---------------------------------------------------------------

    private const float PadFrame = 1f / 60f;

    /// <summary>Runs <paramref name="seconds"/> of frames with the same pad state; returns the frames that asked to shape
    /// and to dissolve.</summary>
    private static (int Shape, int Dissolve) Hold(PalettePadHands hands, float seconds, bool lb = false, bool rb = false, bool lt = false, bool rt = false, bool dpad = false)
    {
        int shape = 0, dissolve = 0;
        int frames = (int)MathF.Round(seconds / PadFrame);
        for (int i = 0; i < frames; i++)
        {
            hands.Update(PadFrame, lb, rb, lt, rt, dpad);
            shape += hands.Shape ? 1 : 0;
            dissolve += hands.Dissolve ? 1 : 0;
        }

        return (shape, dissolve);
    }

    [Fact]
    public void OnAPad_AShoulderAlone_IsTheHand_ATapIsOnePulse_AHoldKeepsPulsing()
    {
        var hands = new PalettePadHands();

        // A tap of the left shoulder: nothing while it is down, one frame's pulse as it is let go.
        Assert.Equal((0, 0), Hold(hands, 0.1f, lb: true));
        Assert.Equal((1, 0), Hold(hands, 0.2f));

        // A tap of the right shoulder dissolves.
        Assert.Equal((0, 0), Hold(hands, 0.1f, rb: true));
        Assert.Equal((0, 1), Hold(hands, 0.2f));

        // A hold: after the grace it asks every frame (the shaper paces the pulses), and the release adds nothing.
        var held = Hold(hands, 1f, lb: true);
        int graceFrames = (int)MathF.Ceiling(PalettePadHands.ChordGrace / PadFrame);
        Assert.InRange(held.Shape, 60 - graceFrames - 2, 60 - graceFrames + 1);
        Assert.Equal((0, 0), Hold(hands, 0.2f));

        // Through the shaper: a second of the left shoulder held alone builds at the building pace.
        var shaper = new SoundShaper();
        int pulses = 0;
        for (int i = 0; i < 120; i++)
        {
            hands.Update(PadFrame, true, false, false, false, false);
            pulses += shaper.Tick(PadFrame, active: true, hands.Shape, hands.Dissolve, aimsAtSoundBlock: false) == SoundShapeAction.Shape ? 1 : 0;
        }

        Assert.InRange(pulses, 7, 9); // two seconds less the grace, one pulse every 0.22 s
    }

    [Fact]
    public void OnAPad_AShoulderWithTheDpad_StaysTheBarsModifier_AndNeverShapes()
    {
        var hands = new PalettePadHands();

        // The chord for tones 5–8: shoulder down, then the d-pad, both let go.
        Assert.Equal((0, 0), Hold(hands, 0.1f, rb: true));
        Assert.Equal((0, 0), Hold(hands, 0.1f, rb: true, dpad: true));
        Assert.Equal((0, 0), Hold(hands, 1f, rb: true)); // still held after the tone: the whole hold is the chord
        Assert.Equal((0, 0), Hold(hands, 0.2f));        // … and its release is no tap

        // Pressed onto an already pushed d-pad: the modifier as well.
        Assert.Equal((0, 0), Hold(hands, 0.1f, dpad: true));
        Assert.Equal((0, 0), Hold(hands, 1f, lb: true, dpad: true));
        Assert.Equal((0, 0), Hold(hands, 0.2f));

        // The d-pad alone (tones 1–4) asks for nothing.
        Assert.Equal((0, 0), Hold(hands, 0.5f, dpad: true));

        // A hold that already shapes turns into the modifier the moment the d-pad is pushed.
        Assert.True(Hold(hands, 0.6f, lb: true).Shape > 0);
        Assert.Equal((0, 0), Hold(hands, 0.5f, lb: true, dpad: true));
        Assert.Equal((0, 0), Hold(hands, 0.5f, lb: true));
        Assert.Equal((0, 0), Hold(hands, 0.2f));

        // The next press alone is the hand again.
        Assert.Equal((0, 0), Hold(hands, 0.1f, lb: true));
        Assert.Equal((1, 0), Hold(hands, 0.2f));
    }

    [Fact]
    public void OnAPad_TheTriggers_AreAlwaysTheHand_AndAButtonHeldFromBefore_StartsNothing()
    {
        var hands = new PalettePadHands();

        // The triggers (they read true only while the player switched them on) are no modifier: at once, d-pad or not.
        Assert.Equal((6, 0), Hold(hands, 0.1f, lt: true));
        Assert.Equal((0, 6), Hold(hands, 0.1f, rt: true, dpad: true));
        Assert.Equal((0, 0), Hold(hands, 0.1f));

        // The bar did not hold the shoulders (closed, a menu): a shoulder still down when it takes them is no press.
        hands.Reset();
        Assert.False(hands.Shape || hands.Dissolve);
        Assert.Equal((0, 0), Hold(hands, 1f, lb: true, rb: true));
        Assert.Equal((0, 0), Hold(hands, 0.2f)); // no tap from the stale hold either
        Assert.True(Hold(hands, 0.6f, lb: true).Shape > 0);
    }

    // ---- walking on sound ----------------------------------------------------------------------------------------

    [Fact]
    public void SteppingOntoASoundBlock_SoundsItsTone_Once()
    {
        var steps = new SoundStepTones();
        int glow3 = OtherSideRules.GlowOfTone(3), glow6 = OtherSideRules.GlowOfTone(6);

        Assert.False(steps.Step(0.016f, grounded: true, 4, 9, 4, soundBlock: false, glow: 0, out _)); // rock
        Assert.True(steps.Step(0.016f, true, 5, 9, 4, soundBlock: true, glow3, out int tone));
        Assert.Equal(3, tone);
        Assert.False(steps.Step(0.016f, true, 5, 9, 4, true, glow3, out _)); // standing on it is silent

        // The next step of a stair, after the rest: the next tone.
        Assert.True(steps.Step(SoundStepTones.Rest, true, 6, 10, 4, true, glow6, out tone));
        Assert.Equal(6, tone);
    }

    [Fact]
    public void ARun_DoesNotMachineGun_AndAJumpLandsAsANewStep()
    {
        var steps = new SoundStepTones();
        int glow = OtherSideRules.GlowOfTone(2);
        Assert.True(steps.Step(0.016f, true, 0, 9, 0, true, glow, out _));
        Assert.False(steps.Step(0.016f, true, 1, 9, 0, true, glow, out _)); // too soon after the last tone
        Assert.True(steps.Step(SoundStepTones.Rest, true, 2, 9, 0, true, glow, out _));

        // In the air nothing sounds; landing on the very same block is a step again.
        Assert.False(steps.Step(0.3f, grounded: false, 2, 9, 0, true, glow, out _));
        Assert.True(steps.Step(0.016f, true, 2, 9, 0, true, glow, out _));

        steps.Reset();
        Assert.True(steps.Step(0.016f, true, 2, 9, 0, true, glow, out _));
    }

    // ---- the gates of our side -----------------------------------------------------------------------------------

    [Fact]
    public void ThreeRuneStonesCloseTogether_AreAGate_ALoneOneIsNot()
    {
        var gates = new List<Vector3f>();
        RuneGateFinder.Group(new[] { new Vector3i(10, 5, 10), new Vector3i(12, 5, 10), new Vector3i(14, 5, 10), new Vector3i(60, 5, 60) }, gates);

        var gate = Assert.Single(gates);
        Assert.Equal(12.5f, gate.X, 3);
        Assert.Equal(5f, gate.Y, 3);
        Assert.Equal(10.5f, gate.Z, 3);

        gates.Clear();
        RuneGateFinder.Group(new[] { new Vector3i(0, 5, 0), new Vector3i(20, 5, 0), new Vector3i(40, 5, 0) }, gates);
        Assert.Empty(gates);
    }

    [Fact]
    public void TheScan_ReadsASlabAFrame_AndPublishesWhenDone()
    {
        var stones = new HashSet<(int, int, int)> { (3, 1, 0), (5, 1, 0), (4, 1, 2), (-30, 1, 0) }; // the last one is out of the box
        bool IsRune(int x, int y, int z) => stones.Contains((x, y, z));
        var finder = new RuneGateFinder();
        Assert.False(finder.Advance(IsRune)); // nothing started

        finder.Begin(0, 0, 0);
        int frames = 0;
        while (!finder.Advance(IsRune))
        {
            frames++;
            Assert.True(frames < 100, "the scan never finished");
        }

        Assert.Equal(2 * RuneGateFinder.ReachXZ, frames); // one slab a frame
        Assert.False(finder.Scanning);
        var gate = Assert.Single(finder.Gates);
        Assert.Equal(4.5f, gate.X, 3);
        int version = finder.Version;

        // The same stones again: nothing new to draw.
        finder.Begin(0, 0, 0);
        Assert.True(finder.Advance(IsRune, slabs: 100));
        Assert.Equal(version, finder.Version);

        // A stone is mined: no gate any more.
        stones.Remove((4, 1, 2));
        finder.Begin(0, 0, 0);
        Assert.True(finder.Advance(IsRune, slabs: 100));
        Assert.Empty(finder.Gates);
        Assert.True(finder.Version > version);

        finder.Clear();
        Assert.False(finder.Scanning);
    }

    // ---- sound blocks glow, they do not light --------------------------------------------------------------------

    [Fact]
    public void ASoundBlock_GlowsAsALook_AndIsNoLightSource()
    {
        const ushort sound = 61, stone = 7;
        int cs = WorldConstants.ChunkSize;
        var world = new ClientWorld();
        world.SetCircumference(512);
        world.SetBlockLightResolver(_ => 0);
        world.SetGlowLookBlocks(new[] { sound });

        // The bulk path: a stored chunk with a sound block and an ordinary glow block.
        var blocks = new ushort[cs * cs * cs];
        int a = WorldConstants.LocalIndex(1, 2, 3), b = WorldConstants.LocalIndex(7, 8, 9);
        blocks[a] = sound;
        blocks[b] = stone;
        world.StoreChunk(new ChunkCoord(0, 0, 0), blocks, modIndex: new[] { a, b }, modTint: new[] { 0, 0 },
            modGlow: new[] { OtherSideRules.GlowOfTone(7), 0x3366FF });
        var lights = world.LightSourcesNear(new ChunkCoord(0, 0, 0), 4);
        var only = Assert.Single(lights);
        Assert.Equal(new Vector3i(7, 8, 9), only.Pos);

        // The live path: a block shaped while the chunk is there.
        Assert.True(world.ApplyBlockChange(4, 4, 4, sound, tint: 0, glow: OtherSideRules.GlowOfTone(3), out _));
        Assert.Single(world.LightSourcesNear(new ChunkCoord(0, 0, 0), 4));

        // Without the registration the glow would be a light — the rule every other block keeps.
        world.SetGlowLookBlocks(null);
        Assert.True(world.ApplyBlockChange(5, 5, 5, sound, tint: 0, glow: OtherSideRules.GlowOfTone(3), out _));
        Assert.Equal(2, world.LightSourcesNear(new ChunkCoord(0, 0, 0), 4).Count);
    }
}
