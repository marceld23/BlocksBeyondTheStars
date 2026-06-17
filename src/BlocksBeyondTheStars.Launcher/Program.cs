using System.Diagnostics;
using System.Drawing.Imaging;
using Velopack;

namespace BlocksBeyondTheStars.Launcher;

/// <summary>
/// A tiny front-end launcher that shows a graphical loading splash the instant it starts, so the player
/// sees feedback during the black gap before the Unity engine creates its window (which Unity itself cannot
/// draw over — see docs / the startup-delay analysis). It starts the real Unity player and the splash hands
/// off (closes) as soon as that window appears.
///
/// It is also the Velopack <c>--mainExe</c>, so it must run the Velopack lifecycle hooks first; on a normal
/// launch that is a fast no-op and we proceed to show the splash and start the game.
/// </summary>
internal static class Program
{
    /// <summary>The Unity player this launcher fronts. It sits in the same folder as the launcher (both in
    /// the Velopack <c>current/</c> dir when installed, or side by side in a dev/portable build).</summary>
    private const string GameExeName = "BlocksBeyondTheStars.exe";

    [STAThread]
    private static int Main(string[] args)
    {
        // Dev-only: render the splash to a PNG for a visual check, then exit. Never used in normal operation.
        //   BlocksBeyondTheStars.Launcher.exe --render-splash <out.png> [width] [height]
        if (args.Length >= 2 && args[0] == "--render-splash")
        {
            int rw = args.Length >= 3 ? int.Parse(args[2]) : 560;
            int rh = args.Length >= 4 ? int.Parse(args[3]) : 320;
            using var bmp = new Bitmap(rw, rh);
            using (var g = Graphics.FromImage(bmp))
            {
                SplashForm.PaintSplash(g, rw, rh, 0.55f, SplashLocalization.LoadingText(AppContext.BaseDirectory), null);
            }

            bmp.Save(args[1], ImageFormat.Png);
            return 0;
        }

        // Velopack install/update/uninstall/firstrun hooks. On a normal user launch this returns immediately;
        // for a hook command it performs the action and exits the process before we reach the splash.
        try
        {
            VelopackApp.Build().Run();
        }
        catch
        {
            // Not an installed build (dev/portable) — there is no Velopack locator. Continue unmanaged.
        }

        string dir = AppContext.BaseDirectory;
        string gameExe = Path.Combine(dir, GameExeName);
        if (!File.Exists(gameExe))
        {
            // Nothing to front — don't show a splash that would never hand off.
            return 1;
        }

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        Process game;
        try
        {
            game = Process.Start(new ProcessStartInfo(gameExe)
            {
                WorkingDirectory = dir,
                UseShellExecute = false,
            })!;
        }
        catch
        {
            // Could not start the player — let the OS surface the error rather than hang on a splash.
            return 1;
        }

        using var splash = new SplashForm(game);
        Application.Run(splash);
        return 0;
    }
}
