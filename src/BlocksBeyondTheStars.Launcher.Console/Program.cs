using System.Diagnostics;
using Velopack;

namespace BlocksBeyondTheStars.Launcher;

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
            game = Process.Start(new ProcessStartInfo(gameExe)
            {
                WorkingDirectory = dir,
                UseShellExecute = false,
            })!;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to start game: {ex.Message}");
            return 1;
        }

        game.WaitForExit();
        return game.ExitCode;
    }
}
