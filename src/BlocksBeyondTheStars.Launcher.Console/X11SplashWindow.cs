// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Diagnostics;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace BlocksBeyondTheStars.Launcher;

/// <summary>
/// The Linux loading splash (#3706): a borderless window in the middle of the screen showing the game's poster with
/// "Loading…" and an animated bar (<see cref="SplashRenderer"/>), from the moment the launcher starts until the Unity player's
/// own window is up — the Linux counterpart of the Windows launcher's <c>SplashForm</c>, which until now had no window at
/// all, only a console line. Plain Xlib through P/Invoke (no toolkit to ship): it opens the display, maps an undecorated
/// splash-type window, blits SkiaSharp's pixels with <c>XPutImage</c> about 30 times a second, and watches the window
/// manager's client list (<c>_NET_CLIENT_LIST</c>) for a window whose <c>_NET_WM_PID</c> is the game's. Anything missing —
/// no X server (a pure Wayland session without XWayland), no libX11, an unusual visual — and it quietly does nothing: the
/// console line stays the fallback, and the game starts either way.
/// </summary>
internal static class X11SplashWindow
{
    private const string LibX11 = "libX11.so.6";
    private const int FrameMs = 33;              // ~30 fps for the bar and the window poll
    private const int GraceMs = 350;             // let the player paint its first frame before the splash goes
    private const int SafetyTimeoutMs = 45000;   // the game window never showed (or was never seen): give up
    private const int BlindTimeoutMs = 12000;    // no client list to watch: do not cover the game for long
    private const int ZPixmap = 2;
    private const long ExposureMask = 1L << 15;
    private const int PropModeReplace = 0;
    private const int XaAtom = 4;
    private const int XaCardinal = 6;
    private const int XaWindow = 33;

    /// <summary>Shows the splash until the game's window is up, the game exits or the time runs out. Never throws.</summary>
    public static void Run(Process game)
    {
        try
        {
            if (!OperatingSystem.IsLinux() || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")))
            {
                return;
            }

            ShowUntilHandoff(game);
        }
        catch
        {
            // No usable X11 here (no libX11, no display, an odd visual, a broken picture) — the console line is the splash,
            // and the launcher goes on to wait for the game: a splash must never stop a start.
        }
    }

    private static void ShowUntilHandoff(Process game)
    {
        IntPtr display = XOpenDisplay(IntPtr.Zero);
        if (display == IntPtr.Zero)
        {
            return;
        }

        try
        {
            int screen = XDefaultScreen(display);
            int depth = XDefaultDepth(display, screen);
            if (depth != 24 && depth != 32)
            {
                return; // only the common true-colour visuals: 32-bit pixels in BGRA order
            }

            int scale = XDisplayWidth(display, screen) >= 2400 ? 2 : 1;
            int w = 560 * scale, h = 320 * scale;
            IntPtr root = XRootWindow(display, screen);
            IntPtr window = CreateWindow(display, screen, root, w, h);
            try
            {
                Animate(display, screen, root, window, w, h, game);
            }
            finally
            {
                XDestroyWindow(display, window);
                XFlush(display);
            }
        }
        finally
        {
            XCloseDisplay(display);
        }
    }

    private static IntPtr CreateWindow(IntPtr display, int screen, IntPtr root, int w, int h)
    {
        int x = Math.Max(0, (XDisplayWidth(display, screen) - w) / 2);
        int y = Math.Max(0, (XDisplayHeight(display, screen) - h) / 2);
        IntPtr window = XCreateSimpleWindow(display, root, x, y, (uint)w, (uint)h, 0, UIntPtr.Zero, (UIntPtr)0x06090Eu);
        XStoreName(display, window, "Blocks Beyond the Stars");

        // A splash-type window (window managers centre it and leave off the frame) and, for the older ones, Motif's
        // "no decorations" hint.
        IntPtr type = XInternAtom(display, "_NET_WM_WINDOW_TYPE", false);
        IntPtr splash = XInternAtom(display, "_NET_WM_WINDOW_TYPE_SPLASH", false);
        XChangeProperty(display, window, type, (IntPtr)XaAtom, 32, PropModeReplace, new[] { splash }, 1);
        IntPtr motif = XInternAtom(display, "_MOTIF_WM_HINTS", false);
        XChangeProperty(display, window, motif, motif, 32, PropModeReplace, new IntPtr[] { (IntPtr)2, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero }, 5);
        XSelectInput(display, window, (IntPtr)ExposureMask);
        XMapRaised(display, window);
        XMoveWindow(display, window, x, y); // some window managers place a new window themselves
        XFlush(display);
        return window;
    }

    /// <summary>Redraws the splash every frame until the hand-off: the game's window showed (plus a short grace), the game
    /// exited, or a timeout. SkiaSharp draws straight into the XImage's pixel buffer.</summary>
    private static void Animate(IntPtr display, int screen, IntPtr root, IntPtr window, int w, int h, Process game)
    {
        using var poster = SplashRenderer.LoadPoster();
        string loading = SplashLocalization.LoadingText(AppContext.BaseDirectory);
        IntPtr data = Marshal.AllocHGlobal(w * h * 4);
        IntPtr ev = Marshal.AllocHGlobal(256); // an XEvent is 192 bytes on 64-bit
        IntPtr image = IntPtr.Zero;
        try
        {
            image = XCreateImage(display, XDefaultVisual(display, screen), (uint)XDefaultDepth(display, screen), ZPixmap, 0, data,
                (uint)w, (uint)h, 32, w * 4);
            using var surface = SKSurface.Create(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul), data, w * 4);
            if (image == IntPtr.Zero || surface == null)
            {
                return;
            }

            var clock = Stopwatch.StartNew();
            long seenAt = -1;
            float phase = 0f;
            while (true)
            {
                while (XPending(display) > 0)
                {
                    XNextEvent(display, ev); // exposures included: every frame repaints anyway
                }

                phase = (phase + 0.018f) % 1f;
                SplashRenderer.Draw(surface.Canvas, w, h, true, phase, poster, loading);
                surface.Canvas.Flush();
                XPutImage(display, window, XDefaultGC(display, screen), image, 0, 0, 0, 0, (uint)w, (uint)h);
                XFlush(display);

                long now = clock.ElapsedMilliseconds;
                bool canSee = GameWindowShown(display, root, game.Id, out bool shown);
                if (seenAt < 0 && shown)
                {
                    seenAt = now;
                }

                bool timedOut = now >= (canSee ? SafetyTimeoutMs : BlindTimeoutMs);
                if (game.HasExited || (seenAt >= 0 && now - seenAt >= GraceMs) || timedOut)
                {
                    return;
                }

                Thread.Sleep(FrameMs);
            }
        }
        finally
        {
            if (image != IntPtr.Zero)
            {
                XFree(image); // the XImage struct only; its pixel buffer is ours
            }

            Marshal.FreeHGlobal(data);
            Marshal.FreeHGlobal(ev);
        }
    }

    /// <summary>Looks for a window of process <paramref name="pid"/> in the window manager's client list. Returns false when
    /// there is no client list to look at (no EWMH window manager); <paramref name="shown"/> is the answer.</summary>
    private static bool GameWindowShown(IntPtr display, IntPtr root, int pid, out bool shown)
    {
        shown = false;
        IntPtr clientList = XInternAtom(display, "_NET_CLIENT_LIST", false);
        IntPtr pidAtom = XInternAtom(display, "_NET_WM_PID", false);
        if (XGetWindowProperty(display, root, clientList, IntPtr.Zero, (IntPtr)4096, false, (IntPtr)XaWindow,
                out _, out int format, out IntPtr count, out _, out IntPtr list) != 0 || list == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            if (format != 32)
            {
                return false;
            }

            for (int i = 0; i < (int)count && !shown; i++)
            {
                IntPtr win = Marshal.ReadIntPtr(list, i * IntPtr.Size); // format-32 items are C longs
                shown = WindowPid(display, win, pidAtom) == pid;
            }

            return true;
        }
        finally
        {
            XFree(list);
        }
    }

    private static long WindowPid(IntPtr display, IntPtr window, IntPtr pidAtom)
    {
        if (XGetWindowProperty(display, window, pidAtom, IntPtr.Zero, (IntPtr)1, false, (IntPtr)XaCardinal,
                out _, out int format, out IntPtr count, out _, out IntPtr prop) != 0 || prop == IntPtr.Zero)
        {
            return -1;
        }

        try
        {
            return format == 32 && (long)count >= 1 ? Marshal.ReadIntPtr(prop).ToInt64() : -1;
        }
        finally
        {
            XFree(prop);
        }
    }

    [DllImport(LibX11)] private static extern IntPtr XOpenDisplay(IntPtr name);
    [DllImport(LibX11)] private static extern int XCloseDisplay(IntPtr display);
    [DllImport(LibX11)] private static extern int XDefaultScreen(IntPtr display);
    [DllImport(LibX11)] private static extern int XDefaultDepth(IntPtr display, int screen);
    [DllImport(LibX11)] private static extern IntPtr XDefaultVisual(IntPtr display, int screen);
    [DllImport(LibX11)] private static extern IntPtr XDefaultGC(IntPtr display, int screen);
    [DllImport(LibX11)] private static extern IntPtr XRootWindow(IntPtr display, int screen);
    [DllImport(LibX11)] private static extern int XDisplayWidth(IntPtr display, int screen);
    [DllImport(LibX11)] private static extern int XDisplayHeight(IntPtr display, int screen);

    [DllImport(LibX11)]
    private static extern IntPtr XCreateSimpleWindow(IntPtr display, IntPtr parent, int x, int y, uint width, uint height,
        uint borderWidth, UIntPtr border, UIntPtr background);

    [DllImport(LibX11)] private static extern int XDestroyWindow(IntPtr display, IntPtr window);
    [DllImport(LibX11, CharSet = CharSet.Ansi)] private static extern int XStoreName(IntPtr display, IntPtr window, string name);
    [DllImport(LibX11, CharSet = CharSet.Ansi)] private static extern IntPtr XInternAtom(IntPtr display, string name, bool onlyIfExists);

    [DllImport(LibX11)]
    private static extern int XChangeProperty(IntPtr display, IntPtr window, IntPtr property, IntPtr type, int format, int mode,
        IntPtr[] data, int count);

    [DllImport(LibX11)] private static extern int XSelectInput(IntPtr display, IntPtr window, IntPtr mask);
    [DllImport(LibX11)] private static extern int XMapRaised(IntPtr display, IntPtr window);
    [DllImport(LibX11)] private static extern int XMoveWindow(IntPtr display, IntPtr window, int x, int y);
    [DllImport(LibX11)] private static extern int XFlush(IntPtr display);
    [DllImport(LibX11)] private static extern int XPending(IntPtr display);
    [DllImport(LibX11)] private static extern int XNextEvent(IntPtr display, IntPtr eventReturn);

    [DllImport(LibX11)]
    private static extern IntPtr XCreateImage(IntPtr display, IntPtr visual, uint depth, int format, int offset, IntPtr data,
        uint width, uint height, int bitmapPad, int bytesPerLine);

    [DllImport(LibX11)]
    private static extern int XPutImage(IntPtr display, IntPtr drawable, IntPtr gc, IntPtr image, int srcX, int srcY, int destX,
        int destY, uint width, uint height);

    [DllImport(LibX11)]
    private static extern int XGetWindowProperty(IntPtr display, IntPtr window, IntPtr property, IntPtr longOffset, IntPtr longLength,
        bool delete, IntPtr reqType, out IntPtr actualType, out int actualFormat, out IntPtr itemCount, out IntPtr bytesAfter,
        out IntPtr prop);

    [DllImport(LibX11)] private static extern int XFree(IntPtr data);
}
