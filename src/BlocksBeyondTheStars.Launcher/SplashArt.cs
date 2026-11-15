// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Reflection;

namespace BlocksBeyondTheStars.Launcher;

/// <summary>
/// The splash background (#3706): the game's own poster — the corvette before the "varied" world with the block logo, taken
/// by the player with <c>-capturePoster</c> — so the launcher, the installer and the menu look like one game. A
/// <c>splash.png</c> next to the launcher wins (a quick local swap); otherwise the poster compiled into the exe
/// (<c>client/Branding/launcher-splash.jpg</c>, see the csproj); otherwise null and the procedural look stands in.
/// The poster already carries the title, so <see cref="SplashForm.PaintSplash"/> draws no text title over it.
/// </summary>
internal static class SplashArt
{
    private const string FileName = "splash.png";
    private const string ResourceName = "splash-art"; // the csproj's LogicalName (a JPEG)

    /// <summary>The background image, or null. The caller owns (disposes) it.</summary>
    internal static Image? Load()
    {
        try
        {
            string png = Path.Combine(AppContext.BaseDirectory, FileName);
            if (File.Exists(png))
            {
                return Copy(File.OpenRead(png));
            }

            var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);
            return stream != null ? Copy(stream) : null;
        }
        catch
        {
            return null; // a broken file must never keep the game from starting — the procedural look stands in
        }
    }

    /// <summary>GDI+ needs the source stream for an image's whole life; a bitmap copy does not.</summary>
    private static Bitmap Copy(Stream stream)
    {
        using (stream)
        using (var image = Image.FromStream(stream))
        {
            return new Bitmap(image);
        }
    }
}
