// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Diagnostics;
using Velopack;

namespace BlocksBeyondTheStars.Launcher;

/// <summary>
/// The Linux launcher (the Velopack main exe of the Linux package): runs the Velopack hooks, starts the Unity player, shows
/// the loading splash window until the player's window is up (<see cref="X11SplashWindow"/>, #3706) and waits for the game.
/// <c>--render-splash &lt;out.png&gt; [w] [h]</c> renders the splash picture to a file instead (a visual check).
/// </summary>
internal static class Program
{
    private const string GameExeName = "BlocksBeyondTheStars.x86_64";

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length >= 2 && (args[0] == "--render-splash" || args[0] == "--render-install-splash"))
        {
            bool showBar = args[0] == "--render-splash";
            int rw = args.Length >= 3 ? int.Parse(args[2]) : 560;
            int rh = args.Length >= 4 ? int.Parse(args[3]) : 320;
            SplashRenderer.RenderSplash(args[1], rw, rh, showBar);
            return 0;
        }

        try
        {
            VelopackApp.Build().Run();
        }
        catch
        {
            // Not an installed build (dev/portable) — no Velopack locator.
        }

        string dir = AppContext.BaseDirectory;
        string gameExe = Path.Combine(dir, GameExeName);
        if (!File.Exists(gameExe))
        {
            Console.Error.WriteLine($"Game executable not found: {gameExe}");
            return 1;
        }

        Console.WriteLine("Loading...");

        Process game;
        try
        {
            var psi = new ProcessStartInfo(gameExe)
            {
                WorkingDirectory = dir,
                UseShellExecute = false,
            };
            GameArguments.Forward(args, psi); // the start options (-dataDir, -hostPort) are the game's
            game = Process.Start(psi)!;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to start game: {ex.Message}");
            return 1;
        }

        // #3706: the poster splash window while the engine starts (an X11 window; without X it does nothing).
        X11SplashWindow.Run(game);
        game.WaitForExit();
        return game.ExitCode;
    }
}
