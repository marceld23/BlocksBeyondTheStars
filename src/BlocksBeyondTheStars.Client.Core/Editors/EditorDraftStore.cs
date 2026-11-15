// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>What <see cref="EditorDraftStore.Write"/> did.</summary>
    public enum DraftWriteResult
    {
        /// <summary>The draft is on disk.</summary>
        Written,

        /// <summary>The same text was written last time — nothing to do.</summary>
        Unchanged,

        /// <summary>The work is bigger than <see cref="EditorDraftStore.MaxBytes"/>; the previous draft stays.</summary>
        TooLarge,

        /// <summary>The disk refused (full, read-only, locked). The previous draft stays; the editor keeps working.</summary>
        Failed,
    }

    /// <summary>A draft read back by <see cref="EditorDraftStore.TryLoad"/>.</summary>
    public sealed class EditorDraft
    {
        public EditorDraft(string content, string title, DateTime savedUtc)
        {
            Content = content;
            Title = title;
            SavedUtc = savedUtc;
        }

        /// <summary>The editor's own text for the open work (what <c>IEditorDocument.SaveDraft</c> returned).</summary>
        public string Content { get; }

        /// <summary>The work's name when the draft was written ("My cruiser"), empty when it had none.</summary>
        public string Title { get; }

        /// <summary>When the draft was written (UTC) — "5 min ago" in the prompt.</summary>
        public DateTime SavedUtc { get; }
    }

    /// <summary>
    /// The automatic drafts of the editors (#3689): no work is lost when an editor is left, whatever closed it. One draft
    /// per editor (the key: <c>ship</c>, <c>texture</c>, …) as one file under a root the client passes
    /// (<c>AppPaths.Root/drafts</c> — persistentDataPath or the portable data folder), next to the user content but never
    /// inside it. Unity-free, so the rules are tested headless.
    /// <para>
    /// The caller drives the timing: <see cref="MarkDirty"/> on every change, <see cref="Due"/> each frame, and
    /// <see cref="Write"/> when it is due — after the work rested for <see cref="QuietSeconds"/>, or at the latest
    /// <see cref="MaxWaitSeconds"/> after the first unsaved change, so a child who paints without a pause still gets a draft.
    /// Saving the work for real calls <see cref="Clear"/>.
    /// </para>
    /// <para>
    /// A draft never blocks opening an editor: <see cref="TryLoad"/> answers false for a missing, unreadable, foreign,
    /// truncated or oversized file, and <see cref="SetAside"/> renames one the editor could not restore, so it is not
    /// offered again (and not destroyed — it can still be looked at). Writes are atomic (a temp file, then a swap), so a
    /// crash mid-write leaves the previous draft. Nothing here throws on disk errors.
    /// </para>
    /// <para>
    /// File format: one header line <c>bbs-draft/1 \t &lt;UTC ticks&gt; \t &lt;content length&gt; \t &lt;title&gt;</c>, then the
    /// content exactly as given. The length catches a file that was cut short or edited by hand.
    /// </para>
    /// </summary>
    public sealed class EditorDraftStore
    {
        /// <summary>The default size cap: 8 MB, far above a big village build as text.</summary>
        public const long DefaultMaxBytes = 8L * 1024 * 1024;

        /// <summary>A draft is written after the work rested this long (seconds).</summary>
        public const double DefaultQuietSeconds = 2.0;

        /// <summary>… or at the latest this long after the first unsaved change (seconds).</summary>
        public const double DefaultMaxWaitSeconds = 15.0;

        private const string Magic = "bbs-draft/1";
        private const string Extension = ".draft";

        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        private double _firstDirty = double.NaN;
        private double _lastDirty = double.NaN;
        private int _lastHash;
        private int _lastLength = -1;

        /// <param name="rootDirectory">The drafts folder; created on the first write.</param>
        /// <param name="editorKey">The editor this draft belongs to; letters, digits, <c>-</c> and <c>_</c> are kept, every
        /// other character becomes <c>_</c> (a key never escapes the folder).</param>
        /// <param name="maxBytes">The size cap of one draft (UTF-8 bytes of the content).</param>
        public EditorDraftStore(string rootDirectory, string editorKey, long maxBytes = DefaultMaxBytes)
        {
            if (string.IsNullOrEmpty(rootDirectory))
            {
                throw new ArgumentException("A drafts folder is required.", nameof(rootDirectory));
            }

            RootDirectory = rootDirectory;
            EditorKey = SafeKey(editorKey);
            MaxBytes = Math.Max(1, maxBytes);
            FilePath = Path.Combine(rootDirectory, EditorKey + Extension);
        }

        /// <summary>The drafts folder.</summary>
        public string RootDirectory { get; }

        /// <summary>The editor key as used for the file name.</summary>
        public string EditorKey { get; }

        /// <summary>The draft file — for logs and tests only, never for the player's eyes (no paths in the UI).</summary>
        public string FilePath { get; }

        /// <summary>The size cap of one draft.</summary>
        public long MaxBytes { get; }

        /// <summary>Seconds of rest after the last change before a draft is written.</summary>
        public double QuietSeconds { get; set; } = DefaultQuietSeconds;

        /// <summary>Seconds after the first unsaved change by which a draft is written even without a rest.</summary>
        public double MaxWaitSeconds { get; set; } = DefaultMaxWaitSeconds;

        /// <summary>True when a draft file exists (it may still turn out unreadable — <see cref="TryLoad"/> decides).</summary>
        public bool HasDraft => SafeExists(FilePath);

        /// <summary>True while changes wait for their draft (<see cref="MarkDirty"/> was called since the last write).</summary>
        public bool Pending => !double.IsNaN(_lastDirty);

        /// <summary>A change happened at <paramref name="now"/> (seconds on any steady clock, e.g. Unity's unscaled time).</summary>
        public void MarkDirty(double now)
        {
            if (double.IsNaN(_firstDirty))
            {
                _firstDirty = now;
            }

            _lastDirty = now;
        }

        /// <summary>True when the pending changes should be written now: they rested for <see cref="QuietSeconds"/>, or the
        /// oldest waits for <see cref="MaxWaitSeconds"/>.</summary>
        public bool Due(double now)
            => Pending && (now - _lastDirty >= QuietSeconds || now - _firstDirty >= MaxWaitSeconds);

        /// <summary>Forgets pending changes without writing (the work was saved or thrown away).</summary>
        public void CancelPending()
        {
            _firstDirty = double.NaN;
            _lastDirty = double.NaN;
        }

        /// <summary>
        /// Writes <paramref name="content"/> as the draft, atomically. <paramref name="title"/> is the work's name for the
        /// "Continue your draft" prompt. Clears <see cref="Pending"/> whatever the result — after a refusal the next change
        /// (or the shell's periodic re-check of unsaved work) tries again, not every frame.
        /// </summary>
        public DraftWriteResult Write(string content, string? title, DateTime nowUtc)
        {
            content ??= string.Empty;
            if (Utf8.GetByteCount(content) > MaxBytes)
            {
                CancelPending();
                return DraftWriteResult.TooLarge;
            }

            int hash = Hash(content);
            if (hash == _lastHash && content.Length == _lastLength && HasDraft)
            {
                CancelPending();
                return DraftWriteResult.Unchanged;
            }

            string header = Magic + "\t" + nowUtc.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture) + "\t"
                + content.Length.ToString(CultureInfo.InvariantCulture) + "\t" + OneLine(title) + "\n";
            if (!WriteAtomic(FilePath, header + content))
            {
                CancelPending();
                return DraftWriteResult.Failed;
            }

            _lastHash = hash;
            _lastLength = content.Length;
            CancelPending();
            return DraftWriteResult.Written;
        }

        /// <summary>Reads the draft. False — never an exception — when there is none, or it cannot be read, is not a
        /// draft, was cut short or is over the size cap.</summary>
        public bool TryLoad(out EditorDraft? draft)
        {
            draft = null;
            try
            {
                var info = new FileInfo(FilePath);
                if (!info.Exists || info.Length > MaxBytes + 4096)
                {
                    return false;
                }

                return TryParse(File.ReadAllText(FilePath, Utf8), out draft);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException
                                      || e is NotSupportedException || e is System.Security.SecurityException)
            {
                return false;
            }
        }

        /// <summary>Deletes the draft (the work was saved). Also forgets pending changes.</summary>
        public void Clear()
        {
            CancelPending();
            _lastLength = -1;
            TryDelete(FilePath);
            TryDelete(FilePath + ".tmp");
        }

        /// <summary>Moves a draft the editor could not restore out of the way (<c>&lt;key&gt;.draft.broken</c>, replacing an
        /// older one), so it is not offered again. Returns false when there was nothing to move or the disk refused.</summary>
        public bool SetAside()
        {
            _lastLength = -1;
            string target = FilePath + ".broken";
            try
            {
                if (!File.Exists(FilePath))
                {
                    return false;
                }

                if (File.Exists(target))
                {
                    File.Delete(target);
                }

                File.Move(FilePath, target);
                return true;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is NotSupportedException)
            {
                return false;
            }
        }

        /// <summary>The file-name form of an editor key: letters, digits, '-' and '_' kept, anything else '_'.</summary>
        public static string SafeKey(string? key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return "editor";
            }

            var sb = new StringBuilder(key!.Length);
            foreach (char c in key)
            {
                sb.Append((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-' || c == '_' ? c : '_');
            }

            return sb.ToString();
        }

        /// <summary>Parses a draft file's text (see the class summary for the format).</summary>
        public static bool TryParse(string text, out EditorDraft? draft)
        {
            draft = null;
            int newline = text?.IndexOf('\n') ?? -1;
            if (newline < 0)
            {
                return false;
            }

            string[] header = text!.Substring(0, newline).Split('\t');
            if (header.Length < 4 || header[0] != Magic
                || !long.TryParse(header[1], NumberStyles.None, CultureInfo.InvariantCulture, out long ticks)
                || ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks
                || !int.TryParse(header[2], NumberStyles.None, CultureInfo.InvariantCulture, out int length)
                || text.Length - newline - 1 != length)
            {
                return false;
            }

            string title = string.Join("\t", header, 3, header.Length - 3);
            draft = new EditorDraft(text.Substring(newline + 1), title, new DateTime(ticks, DateTimeKind.Utc));
            return true;
        }

        private static bool WriteAtomic(string path, string text)
        {
            string tmp = path + ".tmp";
            try
            {
                string? dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.WriteAllText(tmp, text, Utf8);
                if (File.Exists(path))
                {
                    try
                    {
                        File.Replace(tmp, path, null);
                    }
                    catch (Exception e) when (e is IOException || e is PlatformNotSupportedException || e is UnauthorizedAccessException)
                    {
                        // Some file systems have no replace: the same result, just not atomic.
                        File.Delete(path);
                        File.Move(tmp, path);
                    }
                }
                else
                {
                    File.Move(tmp, path);
                }

                return true;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is NotSupportedException
                                      || e is ArgumentException || e is System.Security.SecurityException)
            {
                TryDelete(tmp);
                return false;
            }
        }

        private static bool SafeExists(string path)
        {
            try
            {
                return File.Exists(path);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                return false;
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                // A draft that cannot be deleted is overwritten by the next one; nothing to tell the player.
            }
        }

        private static string OneLine(string? title)
        {
            if (string.IsNullOrEmpty(title))
            {
                return string.Empty;
            }

            var sb = new StringBuilder(title!.Length);
            foreach (char c in title)
            {
                sb.Append(char.IsControl(c) ? ' ' : c);
            }

            return sb.ToString().Trim();
        }

        /// <summary>FNV-1a over the UTF-16 code units — stable within a session, which is all "unchanged" needs.</summary>
        private static int Hash(string text)
        {
            unchecked
            {
                uint h = 2166136261;
                foreach (char c in text)
                {
                    h = (h ^ c) * 16777619;
                }

                return (int)h;
            }
        }
    }
}
