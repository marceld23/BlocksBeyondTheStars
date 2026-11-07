// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.IO;
using System.Text;
using System.Text.Json;

namespace BlocksBeyondTheStars.Client.HudFrames
{
    /// <summary>
    /// The file a submitted HUD frame travels as (#3319, <c>hud.json</c>): the four tiles, the twinkle and the size the
    /// player chose — the shape of one entry of <c>data/hud_frames.json</c>, so <c>tools/gen_hud_frames.py --from-hud</c>
    /// adopts it in one step. Nothing else: no name of the player (the nickname rides in the report, typed for it).
    /// </summary>
    public static class HudFrameSubmissionFile
    {
        public const string FileName = "hud.json";

        /// <summary>Version of this file's shape.</summary>
        public const int FormatVersion = 1;

        /// <summary>The file for <paramref name="design"/> (indented, as an export would be).</summary>
        public static string Json(HudFrameDesign design, int sizeLevel, string key, string name)
        {
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
            {
                writer.WriteStartObject();
                writer.WriteNumber("formatVersion", FormatVersion);
                writer.WriteString("key", key ?? string.Empty);
                writer.WriteString("name", name ?? string.Empty);
                writer.WriteNumber("size", sizeLevel < HudFrameLayout.SizeSmall ? HudFrameLayout.SizeSmall
                    : sizeLevel > HudFrameLayout.SizeLarge ? HudFrameLayout.SizeLarge : sizeLevel);
                writer.WriteString("corner", design.Tile(HudFrameTile.Corner));
                writer.WriteString("top", design.Tile(HudFrameTile.Top));
                writer.WriteString("side", design.Tile(HudFrameTile.Side));
                writer.WriteString("bottom", design.Tile(HudFrameTile.Bottom));
                writer.WriteStartArray("twinkle");
                foreach (int index in design.Twinkle)
                {
                    writer.WriteNumberValue(index);
                }

                writer.WriteEndArray();
                writer.WriteStartArray("twinkleColors");
                foreach (string color in design.TwinkleColors)
                {
                    writer.WriteStringValue(color);
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            return Encoding.UTF8.GetString(stream.ToArray());
        }
    }
}
