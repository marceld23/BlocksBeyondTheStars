// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using BlocksBeyondTheStars.Client.Feedback;

namespace BlocksBeyondTheStars.Client.Playtests
{
    /// <summary>
    /// The thing a player was working on in an editor, as the one attachment a playtest report carries when the player
    /// ticks "send my work along": the developers can then load exactly what was on the screen when a step did not
    /// work. It is not a submission — there are no consent boxes because nothing of it is ever put into the game, and
    /// the inbox deletes it after its retention time. Every helper answers null when there is nothing (valid) to send
    /// or the file would be larger than <see cref="MaxBytes"/>; the report then goes out without it.
    /// </summary>
    public static class PlaytestWork
    {
        /// <summary>The largest attachment, as it travels (the inbox's cap for a creation file).</summary>
        public const int MaxBytes = 1_500_000;

        /// <summary>The version of the small document <see cref="OfFields"/> writes.</summary>
        public const int FormatVersion = 1;

        /// <summary>A ship, a station or settlement module, a skin or a HUD frame: the same document a submission of
        /// that kind carries (the export files under one roof), so the same tool unpacks both.</summary>
        public static FeedbackAttachment? OfCreation(string kind, string? key, CreationPayload? payload)
        {
            if (payload == null || !CreationSubmission.IsCreationKind(kind))
            {
                return null;
            }

            byte[]? document = CreationSubmission.Document(kind, CreationSubmission.SafeKey(key, kind), payload);
            return document == null ? null : Json("work-" + kind, document);
        }

        /// <summary>An editor without an export file of its own (a form's voxel code, a tool look): its few facts as
        /// one small document, <c>{"formatVersion":1,"editor":…,"fields":{…}}</c>. Empty values are left out; with no
        /// value at all there is nothing to send.</summary>
        public static FeedbackAttachment? OfFields(string editor, IEnumerable<KeyValuePair<string, string?>> fields)
        {
            if (string.IsNullOrWhiteSpace(editor) || fields == null)
            {
                return null;
            }

            int written = 0;
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                writer.WriteNumber("formatVersion", FormatVersion);
                writer.WriteString("editor", editor);
                writer.WriteStartObject("fields");
                foreach (var field in fields)
                {
                    if (!string.IsNullOrWhiteSpace(field.Key) && !string.IsNullOrEmpty(field.Value))
                    {
                        writer.WriteString(field.Key, field.Value);
                        written++;
                    }
                }

                writer.WriteEndObject();
                writer.WriteEndObject();
            }

            return written == 0 ? null : Json("work-" + FileStem(editor), stream.ToArray());
        }

        /// <summary>A painted picture (a texture's frames as one strip).</summary>
        public static FeedbackAttachment? OfPng(string editor, byte[]? png)
        {
            if (string.IsNullOrWhiteSpace(editor) || png == null || png.Length == 0 || png.Length > MaxBytes)
            {
                return null;
            }

            return new FeedbackAttachment
            {
                FileName = "work-" + FileStem(editor) + ".png",
                MimeType = "image/png",
                Base64 = Convert.ToBase64String(png),
            };
        }

        private static FeedbackAttachment? Json(string stem, byte[] document)
        {
            bool gzip = document.Length > CreationSubmission.GzipThreshold;
            byte[] packed = gzip ? CreationSubmission.Gzip(document) : document;
            if (packed.Length > MaxBytes)
            {
                return null;
            }

            return new FeedbackAttachment
            {
                FileName = stem + (gzip ? ".json.gz" : ".json"),
                MimeType = gzip ? "application/gzip" : "application/json",
                Base64 = Convert.ToBase64String(packed),
            };
        }

        /// <summary>The editor's name as a file-name part: lower-case ASCII letters and digits, nothing else.</summary>
        private static string FileStem(string editor)
        {
            var stem = new System.Text.StringBuilder();
            foreach (char c in editor.Trim().ToLowerInvariant())
            {
                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))
                {
                    stem.Append(c);
                }

                if (stem.Length == 24)
                {
                    break;
                }
            }

            return stem.Length > 0 ? stem.ToString() : "editor";
        }
    }
}
