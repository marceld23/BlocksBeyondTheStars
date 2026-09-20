// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Textures;

namespace BlocksBeyondTheStars.Client.Feedback
{
    /// <summary>What the player filled in and ticked in the "Submit a texture" dialog (#1965).</summary>
    public sealed class TextureSubmissionForm
    {
        /// <summary>The texture's key (<c>campfire</c>, <c>flora_fern</c>, …).</summary>
        public string Key { get; set; } = string.Empty;

        /// <summary>The name the player gave the texture (free text, shown to the developers).</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>The nickname for the credits; empty = "no name".</summary>
        public string Nickname { get; set; } = string.Empty;

        /// <summary>The optional note to the developers.</summary>
        public string Note { get; set; } = string.Empty;

        /// <summary>"I painted this texture myself and did not copy it from anyone."</summary>
        public bool PaintedMyself { get; set; }

        /// <summary>"The developers may use, change and distribute my texture …"</summary>
        public bool GrantsUse { get; set; }

        /// <summary>"I am at least 16 years old – or my parents have allowed it."</summary>
        public bool OldEnoughOrParentsAgreed { get; set; }

        /// <summary>All three boxes are required — the submit button stays off until then.</summary>
        public bool ConsentComplete => PaintedMyself && GrantsUse && OldEnoughOrParentsAgreed;
    }

    /// <summary>
    /// Builds the report a texture submission travels in (#1965). It is an ordinary <see cref="FeedbackReport"/>
    /// — same endpoint, same spam key, same offline spool, same reply thread — marked with
    /// <see cref="ReportType"/> so the inbox files it under its own category (#1966), with the picture and the
    /// raw tile attached.
    ///
    /// The dialog tells the player exactly what is sent; this builder is where that promise is kept: no e-mail,
    /// no location, no machine facts, no screenshot, and the in-game player name is NOT used — only the nickname
    /// the player typed for this purpose (or none).
    /// </summary>
    public static class TextureSubmission
    {
        /// <summary><c>reportJson.reportType</c> — the inbox's <c>ReportIngest.TextureSubmissionType</c>.</summary>
        public const string ReportType = "texture-submission";

        /// <summary>Version of the consent wording the player saw (<c>ui.tex.submit.check_*</c>). Bump it whenever
        /// the meaning of one of the three sentences changes, so it stays provable what was agreed to.</summary>
        public const int ConsentTextVersion = 1;

        public const int MaxNameLength = 60;
        public const int MaxNicknameLength = 24;
        public const int MaxNoteLength = 500;

        /// <summary>
        /// The report, or null when the form is incomplete or the texture is not one the game could load (then
        /// nothing must be sent). <paramref name="frames"/> are raw tiles (<see cref="TextureTiles.BytesPerFrame"/>
        /// each); <paramref name="png"/> is the same picture for human eyes (frames side by side).
        /// </summary>
        public static FeedbackReport? Build(
            TextureSubmissionForm form, IReadOnlyList<byte[]> frames, int fps, byte[]? png,
            string gameVersion, string replyKey, string sessionId, string platform, DateTime utcNow)
        {
            if (form == null || !form.ConsentComplete || !TextureTiles.IsValidKey(form.Key)
                || frames == null || !TextureTiles.IsValidAnimation(frames.Count, fps))
            {
                return null;
            }

            var raw = new byte[frames.Count * TextureTiles.BytesPerFrame];
            for (int i = 0; i < frames.Count; i++)
            {
                if (frames[i] == null || frames[i].Length != TextureTiles.BytesPerFrame)
                {
                    return null;
                }

                Buffer.BlockCopy(frames[i], 0, raw, i * TextureTiles.BytesPerFrame, TextureTiles.BytesPerFrame);
            }

            // What leaves the machine obeys the same alpha rule as what the game shows.
            TextureTiles.EnforceAlpha(raw, TextureTiles.AlphaModeOf(form.Key));

            string name = Clean(form.Name, MaxNameLength);
            string nickname = Clean(form.Nickname, MaxNicknameLength);
            string note = Clean(form.Note, MaxNoteLength);

            var attachments = new List<FeedbackAttachment>
            {
                new FeedbackAttachment { FileName = "texture.bytes", MimeType = "application/octet-stream", Base64 = Convert.ToBase64String(raw) },
            };
            if (png != null && png.Length > 0)
            {
                attachments.Insert(0, new FeedbackAttachment { FileName = "texture.png", MimeType = "image/png", Base64 = Convert.ToBase64String(png) });
            }

            return new FeedbackReport
            {
                Title = "Texture: " + (name.Length > 0 ? name + " (" + form.Key + ")" : form.Key),
                // The inbox requires a description; the note is optional for the player.
                Description = note.Length > 0 ? note : "Texture submission: " + form.Key,
                Email = string.Empty,
                GameVersion = gameVersion ?? string.Empty,
                PlayerId = string.Empty,            // the reply key below is the anonymous id — nothing else
                PlayerName = nickname,
                ReplyKey = replyKey ?? string.Empty,
                SessionId = sessionId ?? string.Empty,
                Platform = platform ?? string.Empty,
                ClientTimestamp = utcNow.ToString("o"),
                ReportJson = new Dictionary<string, object>
                {
                    ["reportType"] = ReportType,
                    ["texture"] = new Dictionary<string, object>
                    {
                        ["key"] = form.Key,
                        ["kind"] = "tile",
                        ["name"] = name,
                        ["nickname"] = nickname,
                        ["frames"] = frames.Count,
                        ["fps"] = frames.Count > 1 ? fps : 0,
                        ["consent"] = new Dictionary<string, object>
                        {
                            ["self"] = form.PaintedMyself,
                            ["grant"] = form.GrantsUse,
                            ["age"] = form.OldEnoughOrParentsAgreed,
                            ["textVersion"] = ConsentTextVersion,
                        },
                    },
                },
                Attachments = attachments,
            };
        }

        /// <summary>One line, trimmed, capped — free text from a child's keyboard.</summary>
        private static string Clean(string? text, int max)
        {
            string value = (text ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
            return value.Length <= max ? value : value.Substring(0, max).TrimEnd();
        }
    }
}
