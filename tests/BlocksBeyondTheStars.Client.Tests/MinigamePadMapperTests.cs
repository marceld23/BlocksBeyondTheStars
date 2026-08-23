// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Client.Minigames;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The gamepad → minigame bridge (#1218), driven with fake pad sequences: button edges + releases, the
/// D-pad/stick arrow repeat, the virtual cursor's glide/click/drag math, and the registry sweep that proves
/// every one of the 20 games starts and survives a scripted pad session headless.
/// </summary>
public sealed class MinigamePadMapperTests
{
    private readonly List<(string kind, MinigameAction a)> _actions = new();
    private readonly List<(PointerPhase phase, float x, float y)> _pointer = new();
    private readonly MinigamePadMapper _mapper;

    public MinigamePadMapperTests()
    {
        _mapper = new MinigamePadMapper(
            a => _actions.Add(("press", a)),
            a => _actions.Add(("release", a)),
            (p, x, y) => _pointer.Add((p, x, y)));
    }

    private void Frame(PadFrame f, float dt = 0.016f, bool wantsPointer = false, int w = 640, int h = 440)
        => _mapper.Update(f, dt, wantsPointer, w, h);

    [Fact]
    public void FaceButtons_MapToTheDecidedActions_WithEdgesAndReleases()
    {
        Frame(new PadFrame { A = true });
        Frame(new PadFrame { A = true }); // held — no second press
        Frame(new PadFrame());            // released

        Assert.Equal(new[]
        {
            ("press", MinigameAction.Confirm),
            ("press", MinigameAction.Primary),
            ("release", MinigameAction.Confirm),
            ("release", MinigameAction.Primary),
        }, _actions.ToArray());

        _actions.Clear();
        Frame(new PadFrame { B = true, X = true, Y = true, Start = true, Back = true });
        var pressed = _actions.Where(t => t.kind == "press").Select(t => t.a).ToArray();
        Assert.Equal(new[]
        {
            MinigameAction.Cancel, MinigameAction.Secondary, MinigameAction.Help,
            MinigameAction.Pause, MinigameAction.Restart,
        }, pressed);
    }

    [Fact]
    public void Dpad_PressesOnce_ThenRepeatsAfterTheDelay()
    {
        Frame(new PadFrame { DpadX = 1f });
        Assert.Single(_actions.Where(t => t == ("press", MinigameAction.Right)));

        // Hold just short of the repeat delay: still one press.
        for (int i = 0; i < 20; i++)
        {
            Frame(new PadFrame { DpadX = 1f }, dt: 0.016f);
        }

        Assert.Single(_actions.Where(t => t == ("press", MinigameAction.Right)));

        // Cross the delay + one interval: repeats arrive.
        for (int i = 0; i < 20; i++)
        {
            Frame(new PadFrame { DpadX = 1f }, dt: 0.016f);
        }

        int presses = _actions.Count(t => t == ("press", MinigameAction.Right));
        Assert.True(presses >= 2, $"expected repeats, got {presses} press(es)");

        _actions.Clear();
        Frame(new PadFrame()); // let go → one release, repeat stops
        Assert.Equal(new[] { ("release", MinigameAction.Right) }, _actions.ToArray());
    }

    [Fact]
    public void Stick_DrivesArrows_ForKeyGames_ButNotWhileItIsTheCursor()
    {
        Frame(new PadFrame { StickY = 1f }); // key game: stick up = Up
        Assert.Contains(("press", MinigameAction.Up), _actions);

        _actions.Clear();
        Frame(new PadFrame()); // neutral (releases Up)
        _actions.Clear();

        Frame(new PadFrame { StickY = 1f }, wantsPointer: true); // pointer game: the stick is the cursor now
        Assert.DoesNotContain(("press", MinigameAction.Up), _actions);

        Frame(new PadFrame { DpadY = 1f }, wantsPointer: true); // …but the D-pad still serves the arrows
        Assert.Contains(("press", MinigameAction.Up), _actions);
    }

    [Fact]
    public void Cursor_StartsCentred_Glides_Clamps_AndClicks()
    {
        Frame(new PadFrame(), wantsPointer: true, w: 200, h: 100);
        Assert.True(_mapper.CursorVisible);
        Assert.Equal(100f, _mapper.CursorX);
        Assert.Equal(50f, _mapper.CursorY);

        // Full-right for one second: glides right, clamped inside the canvas.
        for (int i = 0; i < 100; i++)
        {
            Frame(new PadFrame { StickX = 1f }, dt: 0.01f, wantsPointer: true, w: 200, h: 100);
        }

        Assert.True(_mapper.CursorX > 100f);
        Assert.True(_mapper.CursorX <= 199f);
        Assert.Contains(_pointer, p => p.phase == PointerPhase.Move);

        // Stick up moves the reticle UP = smaller canvas Y (canvas row 0 is the top).
        float yBefore = _mapper.CursorY;
        Frame(new PadFrame { StickY = 1f }, dt: 0.05f, wantsPointer: true, w: 200, h: 100);
        Assert.True(_mapper.CursorY < yBefore);

        // A clicks at the reticle: Down on the edge, Up on release, a drag Move in between.
        _pointer.Clear();
        Frame(new PadFrame { A = true }, wantsPointer: true, w: 200, h: 100);
        Frame(new PadFrame { A = true, StickX = -1f }, dt: 0.05f, wantsPointer: true, w: 200, h: 100);
        Frame(new PadFrame(), wantsPointer: true, w: 200, h: 100);

        Assert.Equal(PointerPhase.Down, _pointer.First().phase);
        Assert.Contains(_pointer, p => p.phase == PointerPhase.Move); // the drag
        Assert.Equal(PointerPhase.Up, _pointer.Last().phase);
    }

    [Fact]
    public void LeavingPointerMode_LiftsAHeldPointer()
    {
        Frame(new PadFrame { A = true }, wantsPointer: true);
        Assert.True(_mapper.CursorPressed);

        Frame(new PadFrame { A = true }, wantsPointer: false); // round ended / key game took over
        Assert.False(_mapper.CursorPressed);
        Assert.False(_mapper.CursorVisible);
        Assert.Equal(PointerPhase.Up, _pointer.Last().phase);
    }

    [Fact]
    public void Reset_ReleasesEverythingHeld()
    {
        Frame(new PadFrame { A = true, DpadX = 1f }, wantsPointer: true);
        _actions.Clear();

        _mapper.Reset();

        Assert.Contains(("release", MinigameAction.Confirm), _actions);
        Assert.Contains(("release", MinigameAction.Primary), _actions);
        Assert.Equal(PointerPhase.Up, _pointer.Last().phase);
        Assert.False(_mapper.CursorVisible);
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    // The registry sweep: every one of the 20 games, driven end-to-end through host + mapper with a
    // scripted "mash everything" pad session. Headless bots can't WIN 20 different games, so the sweep
    // asserts the part a machine can prove: every game creates, draws a canvas, takes 30 simulated
    // seconds of pad input (arrows, buttons, cursor sweeps + clicks) without throwing, and the shell's
    // pad verbs (Start pauses, Back restarts) keep working mid-session. Reaching each result screen by
    // actually PLAYING is the #1227 on-device protocol's job.
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void EveryRegisteredGame_SurvivesAScriptedPadSession()
    {
        foreach (string key in MinigameRegistry.Keys)
        {
            var game = MinigameRegistry.Create(key);
            Assert.NotNull(game);
            var host = new MinigameHost(game!, seed: 42);
            var mapper = new MinigamePadMapper(host.Press, host.Release, host.Pointer);

            host.StartGame();
            Assert.Equal(MinigameState.Playing, host.State);
            Assert.NotNull(host.Api.Surface); // every game creates its canvas in Create

            int w = host.Api.Surface!.Width;
            int h = host.Api.Surface.Height;
            var script = new[]
            {
                new PadFrame { StickX = 1f },
                new PadFrame { StickX = 1f, A = true },
                new PadFrame { StickY = -1f },
                new PadFrame { DpadX = 1f },
                new PadFrame { A = true },
                new PadFrame(),
                new PadFrame { DpadY = 1f, X = true },
                new PadFrame { StickX = -1f, A = true },
                new PadFrame { B = true },
                new PadFrame(),
            };

            // ~30 simulated seconds of mashing. A game may legitimately Fail/Complete under this input —
            // then the shell sits on Result and the rest of the frames are no-ops; what must NEVER happen
            // is an exception out of the game's handlers.
            for (int i = 0; i < 600; i++)
            {
                var f = script[i % script.Length];
                mapper.Update(f, 0.05f, host.WantsPointer, w, h);
                host.Tick(0.05f);
            }

            // The pad's shell verbs still work regardless of where the session ended up.
            if (host.State == MinigameState.Playing)
            {
                mapper.Update(new PadFrame { Start = true }, 0.016f, host.WantsPointer, w, h);
                Assert.Equal(MinigameState.Paused, host.State);
                mapper.Update(new PadFrame(), 0.016f, host.WantsPointer, w, h);
                mapper.Update(new PadFrame { Start = true }, 0.016f, host.WantsPointer, w, h);
                Assert.Equal(MinigameState.Playing, host.State);
            }

            mapper.Update(new PadFrame(), 0.016f, host.WantsPointer, w, h);
            if (host.State == MinigameState.Playing)
            {
                mapper.Update(new PadFrame { Back = true }, 0.016f, host.WantsPointer, w, h);
                Assert.Equal(MinigameState.Playing, host.State); // Restart = a fresh round, still playing
            }
        }
    }

    [Fact]
    public void PointerGames_AdvertiseThePointer_SoTheCursorAppears()
    {
        // Registering a pointer callback in Create is what switches the host UI's virtual cursor on — so the
        // issue's nine pointer-ONLY games must advertise it, and every game must be reachable one way or the
        // other: pointer, or at least one bound action.
        var pointerOnly = new[]
        {
            "blueprint_scramble", "circuit_weaver", "oxygen_loop", "glyph_decoder", "laser_grid",
            "nanobot_repair", "orbit_slingshot", "planet_scanner", "star_memory", "void_solitaire",
        };

        foreach (string key in MinigameRegistry.Keys)
        {
            var host = new MinigameHost(MinigameRegistry.Create(key)!, seed: 7);
            host.StartGame();

            if (pointerOnly.Contains(key))
            {
                Assert.True(host.WantsPointer, key + " is pointer-only but registered no pointer callback");
            }

            // Note: "no pointer AND no Bind" does NOT mean unplayable — docking_sim steers purely through
            // api.Held(), which the pad reaches via the mapper's press/release latching. So the only hard
            // invariant here is the pointer advertisement above; playability itself is the sweep test's job.
        }
    }
}
