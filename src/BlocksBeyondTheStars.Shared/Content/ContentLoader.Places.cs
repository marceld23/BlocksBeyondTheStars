// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Text.Json;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Shared.Content;

public static partial class ContentLoader
{
    /// <summary>#3981: every place list of a folder — one <see cref="PlaceListDefinition"/> per <c>*.json</c> file in ordinal
    /// file-name order, its planet type the file name. No folder → none. An unreadable file throws: a body that lost its places
    /// would be a different body. The checks against the content run in <see cref="GameContent.Validate"/>.</summary>
    public static List<PlaceListDefinition> LoadPlaceLists(string dir)
    {
        var result = new List<PlaceListDefinition>();
        if (!Directory.Exists(dir))
        {
            return result;
        }

        var files = Directory.GetFiles(dir, "*.json");
        Array.Sort(files, StringComparer.Ordinal);
        foreach (var file in files)
        {
            var list = JsonSerializer.Deserialize<PlaceListDefinition>(File.ReadAllText(file), JsonOptions)
                       ?? throw new InvalidDataException($"The place list '{Path.GetFileName(file)}' is empty.");
            list.PlanetType = Path.GetFileNameWithoutExtension(file);
            result.Add(list);
        }

        return result;
    }
}
