// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.WorldHost;

/// <summary>
/// The central browser-play page (HOSTED_WORLDS.md "browser play"): the portal serves the Unity WebGL
/// build at <c>/play</c>, and the My-Worlds Play button deep-links into it with the world's wss URL +
/// join token in the query string. Mirrors the per-instance Api's /play serving (slashless redirect,
/// cache-bust stamping, .br/.gz encodings) — kept as pure helpers here so the policy is unit-testable.
/// </summary>
public static class PlayPage
{
    /// <summary>Reads the Unity index.html and stamps its <c>buildStamp</c> placeholder with the newest
    /// Build/ file timestamp, so every deployed build gets unique <c>?v=…</c> asset URLs (stale mixed
    /// wasm/data pairs crash the engine). Null when no build is installed.</summary>
    public static string? StampIndexHtml(string webglDir)
    {
        string indexPath = Path.Combine(webglDir, "index.html");
        if (!File.Exists(indexPath))
        {
            return null;
        }

        string html = File.ReadAllText(indexPath);
        string buildDir = Path.Combine(webglDir, "Build");
        if (Directory.Exists(buildDir) && Directory.EnumerateFiles(buildDir).Any())
        {
            long stamp = Directory.EnumerateFiles(buildDir).Max(f => File.GetLastWriteTimeUtc(f).Ticks);
            html = html.Replace("var buildStamp = \"\";", $"var buildStamp = \"?v={stamp}\";");
        }

        return html;
    }

    /// <summary>Friendly localized page when no WebGL build is installed (volume not mounted yet), so
    /// /play never 404s blankly on a fresh deployment.</summary>
    public static string NotInstalledHtml(string lang)
    {
        var t = PortalLocales.For(lang);
        return $"<!DOCTYPE html><html lang='{t.Lang}'><meta charset='utf-8'><title>Blocks Beyond the Stars</title>"
            + "<body style='font-family:system-ui;background:#070a12;color:#dfe9f7;padding:40px;line-height:1.5'>"
            + $"<h2>{t.T("play.notInstalled.title")}</h2>"
            + $"<p>{t.T("play.notInstalled.text")}</p>"
            + $"<p><a style='color:#5fd7ff' href='/{t.Query}'>{t.T("play.notInstalled.back")}</a></p>"
            + "</body></html>";
    }

    /// <summary>Content-Encoding + decoded Content-Type for Unity's precompressed build files, so the
    /// browser inflates them natively instead of falling back to Unity's slow JS decompressor.
    /// Nulls = leave the static-file defaults alone.</summary>
    public static (string? Encoding, string? ContentType) EncodingFor(string fileName)
    {
        if (fileName.EndsWith(".br", StringComparison.OrdinalIgnoreCase))
        {
            string contentType = fileName.EndsWith(".wasm.br", StringComparison.OrdinalIgnoreCase)
                ? "application/wasm"
                : fileName.EndsWith(".js.br", StringComparison.OrdinalIgnoreCase)
                    ? "application/javascript"
                    : "application/octet-stream";
            return ("br", contentType);
        }

        return fileName.EndsWith(".gz", StringComparison.OrdinalIgnoreCase) ? ("gzip", null) : (null, null);
    }

    /// <summary>Cache policy: only requests carrying the per-build <c>?v=</c> stamp may cache long-term
    /// (their URL changes with every build); everything else — index.html above all — must revalidate.
    /// Unity's build file names are stable, NOT content-addressed, and a blanket "immutable" once
    /// poisoned browser caches across rebuilds (mixed old/new wasm+data = engine stack overflow).</summary>
    public static string CacheControlFor(string fileName, bool hasVersionQuery)
        => !string.Equals(fileName, "index.html", StringComparison.OrdinalIgnoreCase) && hasVersionQuery
            ? "public, max-age=31536000, immutable"
            : "no-cache";
}
