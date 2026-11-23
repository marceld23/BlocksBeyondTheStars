// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Text.Json;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.Shared.Content;

public static partial class ContentLoader
{
    /// <summary>#3969: every fixed star system of a folder — one <see cref="FixedSystemDefinition"/> per <c>*.json</c> file in
    /// ordinal file-name order, the key (and default id) the file name. An unreadable file is reported and skipped; no folder →
    /// none. The checks against the content come after (<see cref="GameContent.SetFixedSystems"/>).</summary>
    public static List<FixedSystemDefinition> LoadFixedSystems(string dir, Action<string>? warn = null)
    {
        var result = new List<FixedSystemDefinition>();
        if (!Directory.Exists(dir))
        {
            return result;
        }

        var files = Directory.GetFiles(dir, "*.json");
        Array.Sort(files, StringComparer.Ordinal);
        foreach (var file in files)
        {
            try
            {
                var def = JsonSerializer.Deserialize<FixedSystemDefinition>(File.ReadAllText(file), JsonOptions);
                if (def is null)
                {
                    warn?.Invoke($"Skipping empty fixed system '{Path.GetFileName(file)}'.");
                    continue;
                }

                def.Key = Path.GetFileNameWithoutExtension(file);
                result.Add(def);
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
            {
                warn?.Invoke($"Skipping unreadable fixed system '{Path.GetFileName(file)}': {ex.Message}");
            }
        }

        return result;
    }
}
