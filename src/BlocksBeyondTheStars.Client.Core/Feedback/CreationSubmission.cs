// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace BlocksBeyondTheStars.Client.Feedback
{
    /// <summary>What the player filled in and ticked in the submit dialog of a ship, a station/town module or an
    /// avatar skin (#3262) — the same form the texture editor uses (<see cref="TextureSubmissionForm"/>).</summary>
    public sealed class CreationSubmissionForm
    {
        /// <summary><see cref="CreationSubmission.KindShip"/>, <see cref="CreationSubmission.KindStructure"/> or
        /// <see cref="CreationSubmission.KindAvatar"/>.</summary>
        public string Kind { get; set; } = CreationSubmission.KindShip;

        /// <summary>The creation's key (<c>my_ship</c>) — the export folder's name.</summary>
        public string Key { get; set; } = string.Empty;

        /// <summary>The name the player gave it (free text, shown to the developers).</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>The nickname for the credits; empty = "no name".</summary>
        public string Nickname { get; set; } = string.Empty;

        /// <summary>The optional note to the developers.</summary>
        public string Note { get; set; } = string.Empty;

        /// <summary>"I made this myself and did not copy it from anyone."</summary>
        public bool MadeMyself { get; set; }

        /// <summary>"The developers may use, change and distribute it …"</summary>
        public bool GrantsUse { get; set; }

        /// <summary>"I am at least 16 years old – or my parents have allowed it."</summary>
        public bool OldEnoughOrParentsAgreed { get; set; }

        /// <summary>All three boxes are required — nothing is sent until then.</summary>
        public bool ConsentComplete => MadeMyself && GrantsUse && OldEnoughOrParentsAgreed;
    }

    /// <summary>The creation itself: the files its local export writes (name → JSON text, e.g. <c>ship.json</c> +
    /// <c>layout.json</c>) plus the facts the reviewer needs before opening them.</summary>
    public sealed class CreationPayload
    {
        /// <summary>The export files, in order. Each value must be one JSON document.</summary>
        public List<KeyValuePair<string, string>> Files { get; } = new List<KeyValuePair<string, string>>();

        /// <summary>Placed cells (ship/structure); 0 for a skin.</summary>
        public int CellCount { get; set; }

        /// <summary>The block palette's fingerprint (<c>GameContent.BlockFingerprint</c>): the reviewer sees at once
        /// whether the design was made with a different block set than the one being merged into.</summary>
        public string BlockFingerprint { get; set; } = string.Empty;

        /// <summary>The key of the shipped design the player loaded as a starting point; empty for a fresh one.</summary>
        public string BasedOn { get; set; } = string.Empty;

        /// <summary>Extra facts for the kind's block (e.g. <c>structureKind</c> = station/settlement, <c>kit</c>).
        /// Never overrides a field the builder writes itself.</summary>
        public Dictionary<string, object> Details { get; } = new Dictionary<string, object>();

        public CreationPayload Add(string fileName, string json)
        {
            Files.Add(new KeyValuePair<string, string>(fileName, json));
            return this;
        }
    }

    /// <summary>Why a submission was not built — each has its own sentence in the dialog.</summary>
    public enum CreationRefusal
    {
        None,

        /// <summary>A consent box is open, or the form is not one this builder knows.</summary>
        Incomplete,

        /// <summary>Nothing to send: no files, or a file that is not JSON.</summary>
        NoContent,

        /// <summary>More cells than <see cref="CreationSubmission.MaxCells"/> allows for the kind.</summary>
        TooManyCells,

        /// <summary>Still above <see cref="CreationSubmission.MaxPayloadBytes"/> after compression.</summary>
        TooLarge,
    }

    /// <summary>The built report, or the reason there is none.</summary>
    public sealed class CreationSubmissionResult
    {
        public FeedbackReport? Report { get; set; }

        public CreationRefusal Refusal { get; set; }

        /// <summary>The size of the payload as it travels (compressed when it was compressed).</summary>
        public int PayloadBytes { get; set; }
    }

    /// <summary>
    /// Builds the report a ship, a station/town module or an avatar skin travels in (#3262) — the generalised texture
    /// submission (#1965): an ordinary <see cref="FeedbackReport"/> (same endpoint, spam key, offline spool and reply
    /// thread) whose <c>reportJson.reportType</c> is <c>&lt;kind&gt;-submission</c>, so the inbox files it under its
    /// own category. The creation rides as ONE attachment — a compact JSON document holding the export files
    /// (<c>{"formatVersion":1,"kind":…,"key":…,"files":{"ship.json":{…},"layout.json":{…}}}</c>), gzipped above
    /// <see cref="GzipThreshold"/> — and the preview picture in the ordinary <c>screenshot</c> field.
    ///
    /// The promise of the dialog holds here as for textures: no e-mail, no location, no machine facts, no install
    /// token and NOT the in-game player name — only the nickname typed for this purpose (or none). The facts travel
    /// in <c>reportJson.&lt;kind&gt;</c>; a top-level <c>reportJson.kind</c> is never written (the inbox would file
    /// the report as a crash).
    /// </summary>
    public static class CreationSubmission
    {
        public const string KindTexture = "texture";
        public const string KindShip = "ship";
        public const string KindStructure = "structure";
        public const string KindAvatar = "avatar";

        /// <summary>Version of the payload document (<c>formatVersion</c>) — what <c>tools/pull_submissions.py</c> reads.</summary>
        public const int FormatVersion = 1;

        /// <summary>Version of the consent wording the player saw (<c>ui.submit.&lt;kind&gt;.check_*</c>). Bump it when
        /// the meaning of one of the sentences changes, so it stays provable what was agreed to.</summary>
        public const int ConsentTextVersion = 1;

        /// <summary>A payload larger than this travels gzipped.</summary>
        public const int GzipThreshold = 300_000;

        /// <summary>The preview PNG is dropped (never the creation) above this size.</summary>
        public const int MaxPreviewBytes = 1_000_000;

        public const int MaxNameLength = TextureSubmission.MaxNameLength;
        public const int MaxNicknameLength = TextureSubmission.MaxNicknameLength;
        public const int MaxNoteLength = TextureSubmission.MaxNoteLength;

        /// <summary>The kinds this builder sends (the texture has its own, <see cref="TextureSubmission"/>).</summary>
        public static bool IsCreationKind(string? kind) => kind == KindShip || kind == KindStructure || kind == KindAvatar;

        /// <summary><c>reportJson.reportType</c> of a kind — the inbox's category is the part before <c>-submission</c>.</summary>
        public static string ReportTypeOf(string kind) => kind + "-submission";

        /// <summary>The largest payload a kind may send, as it travels (after compression). The inbox keeps 2 MB per
        /// ship/structure file and 400 KB otherwise; the request body is capped at 4 MB.</summary>
        public static int MaxPayloadBytes(string kind) => kind == KindAvatar ? 100_000 : 1_500_000;

        /// <summary>The most cells a kind may send; 0 = no cell limit. A ship cannot outgrow its 48×32×48 room; a
        /// station or town module larger than this would make every world that rolls it heavy.</summary>
        public static int MaxCells(string kind) => kind == KindStructure ? 60_000 : 0;

        /// <summary>Builds the report, or names why it cannot be sent. <paramref name="previewPng"/> is optional.</summary>
        public static CreationSubmissionResult Build(
            CreationSubmissionForm form, CreationPayload payload, byte[]? previewPng,
            string gameVersion, string replyKey, string sessionId, string platform, DateTime utcNow)
        {
            if (form == null || payload == null || !form.ConsentComplete || !IsCreationKind(form.Kind))
            {
                return new CreationSubmissionResult { Refusal = CreationRefusal.Incomplete };
            }

            int maxCells = MaxCells(form.Kind);
            if (maxCells > 0 && payload.CellCount > maxCells)
            {
                return new CreationSubmissionResult { Refusal = CreationRefusal.TooManyCells };
            }

            string key = SafeKey(form.Key, form.Kind);
            byte[]? document = Document(form.Kind, key, payload);
            if (document == null)
            {
                return new CreationSubmissionResult { Refusal = CreationRefusal.NoContent };
            }

            bool gzip = document.Length > GzipThreshold;
            byte[] packed = gzip ? Gzip(document) : document;
            if (packed.Length > MaxPayloadBytes(form.Kind))
            {
                return new CreationSubmissionResult { Refusal = CreationRefusal.TooLarge, PayloadBytes = packed.Length };
            }

            var attachment = new FeedbackAttachment
            {
                FileName = form.Kind + (gzip ? ".json.gz" : ".json"),
                MimeType = gzip ? "application/gzip" : "application/json",
                Base64 = Convert.ToBase64String(packed),
            };
            var report = Assemble(form, key, payload, attachment, document.Length, packed.Length, gameVersion);
            Stamp(report, Clean(form.Nickname, MaxNicknameLength), replyKey, sessionId, platform, utcNow);
            AttachPreview(report, previewPng);
            return new CreationSubmissionResult { Report = report, PayloadBytes = packed.Length };
        }

        /// <summary>The key as a folder name: lower-case ASCII letters, digits and <c>_</c>, at most 64 characters — the
        /// pull tool unpacks into <c>&lt;kind&gt;_exports/&lt;key&gt;/</c>. Anything else (an umlaut, a Cyrillic name) is
        /// dropped; an empty result falls back to the kind. The name the player typed travels untouched.</summary>
        public static string SafeKey(string? key, string kind)
        {
            var sb = new StringBuilder();
            foreach (char c in (key ?? string.Empty).Trim().ToLowerInvariant())
            {
                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_')
                {
                    sb.Append(c);
                }
                else if (c == ' ' || c == '-')
                {
                    sb.Append('_');
                }

                if (sb.Length == 64)
                {
                    break;
                }
            }

            string safe = sb.ToString().Trim('_');
            return safe.Length > 0 ? safe : kind;
        }

        /// <summary>The payload document as compact UTF-8 JSON, or null when there is nothing (valid) to send.</summary>
        internal static byte[]? Document(string kind, string key, CreationPayload payload)
        {
            if (payload.Files.Count == 0)
            {
                return null;
            }

            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                writer.WriteNumber("formatVersion", FormatVersion);
                writer.WriteString("kind", kind);
                writer.WriteString("key", key);
                writer.WriteStartObject("files");
                foreach (var file in payload.Files)
                {
                    if (string.IsNullOrWhiteSpace(file.Key) || !TryWriteJson(writer, file.Key, file.Value))
                    {
                        return null;
                    }
                }

                writer.WriteEndObject();
                writer.WriteEndObject();
            }

            return stream.ToArray();
        }

        private static bool TryWriteJson(Utf8JsonWriter writer, string name, string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            try
            {
                using var doc = JsonDocument.Parse(json!);
                writer.WritePropertyName(name);
                doc.RootElement.WriteTo(writer); // re-written compact: the editors save pretty-printed
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        /// <summary>Gzip (optimal) — the inbox and the pull tool both read <c>application/gzip</c>.</summary>
        public static byte[] Gzip(byte[] data)
        {
            using var output = new MemoryStream();
            using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
            {
                gzip.Write(data, 0, data.Length);
            }

            return output.ToArray();
        }

        private static FeedbackReport Assemble(CreationSubmissionForm form, string key, CreationPayload payload,
            FeedbackAttachment attachment, int rawBytes, int packedBytes, string gameVersion)
        {
            string name = Clean(form.Name, MaxNameLength);
            string note = Clean(form.Note, MaxNoteLength);
            var block = new Dictionary<string, object>();
            foreach (var detail in payload.Details)
            {
                block[detail.Key] = detail.Value;
            }

            block["key"] = key;
            block["name"] = name;
            block["nickname"] = Clean(form.Nickname, MaxNicknameLength);
            block["note"] = note;
            block["gameVersion"] = gameVersion ?? string.Empty;
            block["blockFingerprint"] = payload.BlockFingerprint ?? string.Empty;
            block["formatVersion"] = FormatVersion;
            block["cellCount"] = Math.Max(0, payload.CellCount);
            block["basedOn"] = payload.BasedOn ?? string.Empty;
            block["payload"] = new Dictionary<string, object>
            {
                ["fileName"] = attachment.FileName,
                ["mimeType"] = attachment.MimeType,
                ["bytes"] = packedBytes,
                ["rawBytes"] = rawBytes,
            };
            block["consent"] = Consent(form.MadeMyself, form.GrantsUse, form.OldEnoughOrParentsAgreed, ConsentTextVersion);

            string label = Label(form.Kind);
            return new FeedbackReport
            {
                Title = label + ": " + (name.Length > 0 ? name + " (" + key + ")" : key),
                // The inbox requires a description; the note is optional for the player.
                Description = note.Length > 0 ? note : label + " submission: " + key,
                GameVersion = gameVersion ?? string.Empty,
                ReportJson = new Dictionary<string, object>
                {
                    ["reportType"] = ReportTypeOf(form.Kind),
                    [form.Kind] = block,
                },
                Attachments = new List<FeedbackAttachment> { attachment },
            };
        }

        private static string Label(string kind) => kind switch
        {
            KindShip => "Ship",
            KindStructure => "Structure",
            KindAvatar => "Avatar skin",
            _ => "Creation",
        };

        private static void AttachPreview(FeedbackReport report, byte[]? png)
        {
            if (png == null || png.Length == 0 || png.Length > MaxPreviewBytes)
            {
                return; // the creation is what counts; a missing or huge picture never blocks it
            }

            report.ScreenshotFileName = "preview.png";
            report.Screenshot = new FeedbackScreenshot { FileName = "preview.png", MimeType = "image/png", Base64 = Convert.ToBase64String(png) };
        }

        /// <summary>The fields every submission shares, whatever its kind: anonymous, no e-mail, no install token.</summary>
        internal static void Stamp(FeedbackReport report, string nickname, string replyKey, string sessionId, string platform, DateTime utcNow)
        {
            report.Email = string.Empty;
            report.PlayerId = string.Empty; // the reply key is the anonymous id — nothing else
            report.PlayerName = nickname;
            report.ReplyKey = replyKey ?? string.Empty;
            report.SessionId = sessionId ?? string.Empty;
            report.Platform = platform ?? string.Empty;
            report.ClientTimestamp = utcNow.ToString("o");
        }

        /// <summary>The consent block as stored with every submission.</summary>
        internal static Dictionary<string, object> Consent(bool self, bool grant, bool age, int textVersion) => new Dictionary<string, object>
        {
            ["self"] = self,
            ["grant"] = grant,
            ["age"] = age,
            ["textVersion"] = textVersion,
        };

        /// <summary>One line, trimmed, capped — free text from a child's keyboard.</summary>
        internal static string Clean(string? text, int max)
        {
            string value = (text ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
            return value.Length <= max ? value : value.Substring(0, max).TrimEnd();
        }
    }
}
