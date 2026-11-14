// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Tools;

/// <summary>
/// The builders' check for a giant ship (#3617): <c>giantship &lt;key|file.json&gt; [--data dir] [--check]
/// [--setting intact|planet|space] [--seed N] [--airless] [--turns N] [--out file.json]</c>. <c>--check</c> prints the
/// content's problems with the template and its stats; <c>--out</c> writes the ship — intact, or as the wreck the server
/// derives with that seed — as a compact template file that <c>tools/landmarks/preview.py</c> renders like any template.
/// </summary>
internal static class GiantShipTool
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // Never WhenWritingDefault: a marker at x, y or z = 0 would lose its coordinate and the preview could not read it.
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static int Run(string[] args)
    {
        if (args.Length < 2)
        {
            throw new ArgumentException("Usage: giantship <key|file.json> [--data dir] [--check] [--setting intact|planet|space] [--seed N] [--airless] [--turns N] [--out file.json]", nameof(args));
        }

        var content = ContentLoader.LoadFromDirectory(Option(args, "--data", "data"));
        var template = Load(args[1], content);
        int exit = 0;
        if (args.Contains("--check"))
        {
            exit = Check(template, content);
        }

        if (Option(args, "--out", string.Empty) is { Length: > 0 } outFile)
        {
            string setting = Option(args, "--setting", "intact");
            long seed = long.Parse(Option(args, "--seed", "1"), CultureInfo.InvariantCulture);
            int turns = int.Parse(Option(args, "--turns", "0"), CultureInfo.InvariantCulture);
            var built = GiantShipWreckDump.Build(template, content, setting, seed, turns, atmosphere: !args.Contains("--airless"));
            Write(built, outFile);
            Console.WriteLine($"  wrote {outFile} ({setting}, seed {seed})");
        }

        return exit;
    }

    private static string Option(string[] args, string name, string fallback)
    {
        int at = Array.IndexOf(args, name);
        return at > 0 && at + 1 < args.Length ? args[at + 1] : fallback;
    }

    /// <summary>A key of the content's pool, else a path to a template file.</summary>
    private static StructureTemplate Load(string keyOrPath, GameContent content)
    {
        if (File.Exists(keyOrPath))
        {
            var t = JsonSerializer.Deserialize<StructureTemplate>(File.ReadAllText(keyOrPath), ContentLoader.JsonOptions)
                    ?? throw new ArgumentException($"'{keyOrPath}' holds no template.", nameof(keyOrPath));
            t.GiantShip = true;
            if (string.IsNullOrEmpty(t.Key))
            {
                t.Key = Path.GetFileNameWithoutExtension(keyOrPath);
            }

            return t;
        }

        return content.GetGiantShip(keyOrPath) ?? throw new ArgumentException($"No giant ship '{keyOrPath}' and no such file.", nameof(keyOrPath));
    }

    private static int Check(StructureTemplate t, GameContent content)
    {
        var problems = content.GiantShipProblems(t);
        int blocks = 0, cavity = 0, keep = 0;
        TemplateBody.ForEachPlain(t, c =>
        {
            if (c.Cavity)
            {
                cavity++;
            }
            else
            {
                blocks++;
            }

            if (c.Keep)
            {
                keep++;
            }
        });
        int markers = t.Cells.Count(c => c != null && c.Kind == "marker");
        int chests = t.Cells.Count(c => c != null && c.Kind == "marker" && c.Id == LandmarkRules.ChestMarker);
        int entrances = t.Cells.Count(c => c != null && c.Kind == "marker" && c.Id == GiantShipRules.EntranceMarker);
        Console.WriteLine($"{t.Key}: {t.Width} x {t.Height} x {t.Length}, {blocks} blocks, {cavity} cavity, {keep} keep, "
                          + $"{markers} markers ({chests} chests, {entrances} entrances), groundRow {t.GroundRow}, weight {t.Weight}, style '{t.Style}'");
        if (problems.Count == 0)
        {
            Console.WriteLine("  OK — no problems");
            return 0;
        }

        foreach (var p in problems)
        {
            Console.WriteLine("  PROBLEM: " + p);
        }

        return 2;
    }

    private static void Write(StructureTemplate t, string path)
    {
        if (Path.GetDirectoryName(Path.GetFullPath(path)) is { } dir)
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(path, JsonSerializer.Serialize(t, WriteOptions));
    }
}
