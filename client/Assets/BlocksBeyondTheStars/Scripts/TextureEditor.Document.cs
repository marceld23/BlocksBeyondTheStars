// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using BlocksBeyondTheStars.Client.Feedback;
using BlocksBeyondTheStars.Client.Playtests;
using BlocksBeyondTheStars.Shared.Textures;
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The texture editor as the <see cref="EditorShell"/>'s document (#3688–#3690): the open texture's name, unsaved or
    /// not (the canvas model's undo history answers it, so undoing back to the saved state is "saved" again), undo / redo,
    /// the draft (#3689) and the actions behind the header and "More" — Save (into the local texture pack), Submit,
    /// remove mine, back to the original, export, share codes, the folder, publish for a world. Messages go to the shell's
    /// status bar coloured by severity and without a file path.
    /// <para>
    /// Draft, version 1 (<see cref="CanvasDraftFormat"/>, kind <c>texture</c>): JSON of the open texture's key, its frames
    /// (each the raw 64 × 64 RGBA32 tile, base64), the speed and the frame on show. Restoring opens that texture and puts
    /// the frames back as one undoable change, so the work is unsaved again.
    /// </para>
    /// </summary>
    public sealed partial class TextureEditor : IEditorDocument, IPlaytestWorkSource
    {
        /// <summary>The draft slot and kind (#3689).</summary>
        internal const string DraftKind = "texture";

        /// <summary>The open texture for a playtest report ("send my work along"): its draft — every frame as painted,
        /// the speed and the key. Nothing while no texture is open.</summary>
        public FeedbackAttachment WorkForPlaytest() => _entry == null ? null : PlaytestWork.OfFields(DraftKind, new[]
        {
            new KeyValuePair<string, string>("name", _entry.Key),
            new KeyValuePair<string, string>("draft", SaveDraft()),
        });

        /// <summary>The draft layout this editor writes and reads.</summary>
        internal const int DraftVersion = 1;

        private string _hintKey;
        private PendingDraft _pendingDraft;

        /// <summary>A draft waiting for its texture to be open (an icon's pixels arrive a frame later on WebGPU).</summary>
        private sealed class PendingDraft
        {
            public TextureEntry Entry;
            public byte[][] Frames;
            public int Fps;
            public int Active;
            public int Calm;
        }

        [Serializable]
        private sealed class TextureDraft
        {
            public string key;
            public int fps;
            public int active;
            public string[] frames;

            /// <summary>"Calm when saving" in percent; the frames stay the painting as painted. A draft
            /// written before the field reads 0, and a client without it ignores the field — the draft version stays 1.</summary>
            public int calm;
        }

        [Serializable]
        private sealed class ExportMeta
        {
            public string key;
            public string kind = "tile";
            public int frames;
            public int fps;
            public string author;
        }

        // ---------------------------------------------------------------- IEditorDocument

        public string DocumentName => _entry?.Label ?? string.Empty;

        public bool IsDirty => _model != null && _model.Dirty;

        public bool CanUndo => _model != null && _model.CanUndo;

        public bool CanRedo => _model != null && _model.CanRedo;

        public void Undo()
        {
            _stroking = false;
            _dragShape = false;
            if (_model.Undo())
            {
                AfterChange();
            }
        }

        public void Redo()
        {
            _stroking = false;
            _dragShape = false;
            if (_model.Redo())
            {
                AfterChange();
            }
        }

        /// <summary>The texture editor opens no picker or panel of its own (the submit dialog reads its own Esc): nothing
        /// to close, so Esc / pad B leave the editor where it is.</summary>
        public bool CloseTopLayer() => false;

        public string SaveDraft()
        {
            if (_entry == null || _model == null)
            {
                return null;
            }

            var frames = _model.CopyFrames();
            var data = new TextureDraft { key = _entry.Key, fps = _model.Fps, active = _model.ActiveFrame, frames = new string[frames.Length], calm = _model.SaveCalm };
            for (int i = 0; i < frames.Length; i++)
            {
                data.frames[i] = Convert.ToBase64String(frames[i]);
            }

            return CanvasDraftFormat.Wrap(DraftKind, DraftVersion, JsonUtility.ToJson(data));
        }

        public bool LoadDraft(string draft)
        {
            if (!TryReadDraft(draft, out var pending))
            {
                return false;
            }

            _pendingDraft = pending;
            HighlightEntry(pending.Entry);
            Open(pending.Entry); // puts the frames back once the texture is open (an icon may take a frame)
            return true;
        }

        /// <summary>Reads a draft of this editor: false for anything else — another layout, an unknown texture, broken
        /// frames. Never throws.</summary>
        private bool TryReadDraft(string draft, out PendingDraft pending)
        {
            pending = null;
            if (_entries == null || !CanvasDraftFormat.TryUnwrap(draft, DraftKind, DraftVersion, out _, out string payload))
            {
                return false;
            }

            TextureDraft data;
            try
            {
                data = JsonUtility.FromJson<TextureDraft>(payload);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[TextureEditor] The draft could not be read: {e.Message}");
                return false;
            }

            var entry = data?.key == null ? null : _entries.Find(e => e.Key == data.key);
            var frames = DecodeFrames(data?.frames);
            if (entry == null || frames == null)
            {
                return false;
            }

            pending = new PendingDraft { Entry = entry, Frames = frames, Fps = data.fps, Active = data.active, Calm = data.calm };
            return true;
        }

        private static byte[][] DecodeFrames(string[] encoded)
        {
            if (encoded == null || encoded.Length == 0 || encoded.Length > TextureTiles.MaxFrames)
            {
                return null;
            }

            var frames = new byte[encoded.Length][];
            try
            {
                for (int i = 0; i < encoded.Length; i++)
                {
                    frames[i] = Convert.FromBase64String(encoded[i] ?? string.Empty);
                    if (frames[i].Length != TextureTiles.BytesPerFrame)
                    {
                        return null;
                    }
                }
            }
            catch (FormatException)
            {
                return null;
            }

            return frames;
        }

        /// <summary>Puts a restored draft's frames onto <paramref name="entry"/> once it is open: one undoable change.</summary>
        private void ApplyPendingDraft(TextureEntry entry)
        {
            var pending = _pendingDraft;
            if (pending == null || !ReferenceEquals(pending.Entry, entry))
            {
                return;
            }

            _pendingDraft = null;
            _model.ReplaceAll(pending.Frames, pending.Fps, pending.Calm); // with "calm when saving", one step
            _model.SelectFrame(pending.Active);
            AfterChange();
        }

        // ---------------------------------------------------------------- Save, Submit, Back

        /// <summary>Save (the shell's primary button, Ctrl+S): into the local texture pack — this install only. True when
        /// it was saved (the shell then removes the draft).</summary>
        private bool SaveForMe()
        {
            if (_entry == null)
            {
                return false;
            }

            bool ok;
            if (_entry.IsIcon)
            {
                var icon = new Texture2D(Tile, Tile, TextureFormat.RGBA32, false);
                Upload(icon, _model.Frames[0]);
                ok = TexturePackFolder.SaveIcon(_entry.IconName, icon);
                EditorSceneKit.Discard(icon);
                if (ok)
                {
                    _model.MarkSaved();
                }

                EditorPlaytest.SavedIf(ok, EditorPlaytest.Texture);
                SetStatus(L(ok ? "ui.tex.saved_local" : "ui.tex.save_failed"), ok ? EditorStatusSeverity.Success : EditorStatusSeverity.Error);
                RefreshBadges();
                return ok;
            }

            ok = TexturePackFolder.Save(_entry.Key, SavedFrames(), _model.Fps); // calmed as chosen
            if (ok)
            {
                RememberCalmOriginal(); // the painting as painted, for the next opening
                _model.MarkSaved();
                // The world wins over the local pack (decision 2026-09-20) — say so, or "it did not work" is what
                // the player concludes when nothing changes around them.
                bool hidden = GameTextures.HasWorld(_entry.Key) && GameTextures.ShowWorldTextures;
                EditorPlaytest.Saved(EditorPlaytest.Texture);
                SetStatus(L(hidden ? "ui.tex.saved_local_world_wins" : "ui.tex.saved_local"),
                    hidden ? EditorStatusSeverity.Warning : EditorStatusSeverity.Success);
            }
            else
            {
                SetStatus(L("ui.tex.save_failed"), EditorStatusSeverity.Error);
            }

            RefreshBadges();
            return ok;
        }

        /// <summary>Submit (header): the texture for the game itself (#1965) — its own dialog with the three consents.</summary>
        private void OpenSubmit()
        {
            if (_entry == null)
            {
                return;
            }

            if (_submit == null)
            {
                _submit = gameObject.AddComponent<CreationSubmitDialog>();
                _submit.Localize = L;
                _submit.Settings = Shell != null ? Shell.Settings : null;
                _submit.OnOutcome = (text, color) => SetStatus(text, color == UiKit.Ok ? EditorStatusSeverity.Success : EditorStatusSeverity.Warning);
            }

            _submit.Open(_entry.Key, _entry.Label, SavedFrames(), _model.Fps, _entry.IsIcon);
        }

        /// <summary>Back (header, after the shell kept the draft): the host's close.</summary>
        private void Close()
        {
            if (OnClose != null)
            {
                OnClose();
            }
            else if (Shell != null)
            {
                Shell.CloseTextureEditor();
            }
        }

        // ---------------------------------------------------------------- "More"

        /// <summary>"Back to the original …": asks first — the painting goes (Undo brings it back until the editor closes).</summary>
        private void ConfirmResetToOfficial()
            => _shell.Confirm(L("ui.tex.confirm_reset"), L("ui.tex.reset_yes"), ResetToOfficial);

        private void ResetToOfficial()
        {
            var official = OfficialFrames();
            if (official == null)
            {
                SetStatus(L("ui.tex.no_official"), EditorStatusSeverity.Warning);
                return;
            }

            _model.ReplaceAll(official.Frames, official.Fps, 0); // the original is not calmed again
            AfterChange();
            SetStatus(L("ui.tex.reset_done"), EditorStatusSeverity.Info);
        }

        /// <summary>"Remove mine": deletes your own file of this texture for good — asks first; says so when there is none.</summary>
        private void ConfirmRemoveMine()
        {
            bool has = _entry != null && (_entry.IsIcon ? TexturePackFolder.IconOverride(_entry.IconName) != null : GameTextures.HasLocal(_entry.Key));
            if (!has)
            {
                SetStatus(L("ui.tex.none_local"), EditorStatusSeverity.Warning);
                return;
            }

            _shell.Confirm(L("ui.tex.confirm_remove_local").Replace("{name}", _entry.Label), L("ui.tex.remove_local"), RemoveMine);
        }

        private void RemoveMine()
        {
            if (_entry == null)
            {
                return;
            }

            if (_entry.IsIcon)
            {
                TexturePackFolder.RemoveIcon(_entry.IconName);
                Open(_entry); // back to the shipped icon on the canvas (opening clears the status — say it after)
                SetStatus(L("ui.tex.removed_local"), EditorStatusSeverity.Success);
                return;
            }

            TexturePackFolder.Remove(_entry.Key);
            SetStatus(L("ui.tex.removed_local"), EditorStatusSeverity.Success);
            RefreshBadges();
        }

        /// <summary>Export for the game (desktop only): a bundle for <c>tools/merge_texture.py</c> in the export folder. The
        /// status names the folder by its name, never by its path.</summary>
        private void ExportForGame()
        {
            if (_entry == null)
            {
                return;
            }

            try
            {
                var frames = SavedFrames();
                string dir = Path.Combine(AppPaths.Root, "texture_exports", _entry.Key);
                Directory.CreateDirectory(dir);
                var meta = new ExportMeta
                {
                    kind = _entry.IsIcon ? "icon" : "tile",
                    key = _entry.Key,
                    frames = frames.Length,
                    fps = _model.Fps,
                    author = Shell != null && Shell.Settings != null ? Shell.Settings.PlayerName : string.Empty,
                };
                File.WriteAllText(Path.Combine(dir, "texture.json"), JsonUtility.ToJson(meta, true));
                using (var raw = new FileStream(Path.Combine(dir, "texture.bytes"), FileMode.Create, FileAccess.Write))
                {
                    foreach (var f in frames)
                    {
                        raw.Write(f, 0, f.Length); // back to back, in the layout the game loads — merge_texture.py only copies
                    }
                }

                File.WriteAllBytes(Path.Combine(dir, "texture.png"), TexturePackFolder.EncodeStrip(frames));
                EditorPlaytest.Exported(EditorPlaytest.Texture);
                SetStatus(L("ui.tex.exported"), EditorStatusSeverity.Success);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[TextureEditor] Export of '{_entry.Key}' failed: {e}");
                SetStatus(L("ui.ed.export_failed_hint"), EditorStatusSeverity.Error); // a friendly line; the exception goes to the log (#3263)
            }
        }

        private void CopyCode()
        {
            if (_entry == null)
            {
                return;
            }

            GUIUtility.systemCopyBuffer = ShareCode.EncodeTexture(_entry.Key, SavedFrames(), _model.Fps);
            SetStatus(L("ui.tex.code_copied"), EditorStatusSeverity.Success);
        }

        /// <summary>Paste code (desktop only, after <see cref="EditorShell.ConfirmDiscard"/>): the pixels land on the OPEN
        /// texture as one undoable change.</summary>
        private void PasteCode()
        {
            if (!ShareCode.TryDecodeTexture(GUIUtility.systemCopyBuffer, out string key, out var frames, out int fps))
            {
                SetStatus(L("ui.tex.code_bad"), EditorStatusSeverity.Error);
                return;
            }

            // A code is a picture, not an instruction where to put it. Only a block animates, and the alpha rule of the
            // open key is applied by the model.
            if (frames.Length > 1 && (_entry == null || _entry.Block == null))
            {
                frames = new[] { frames[0] };
                fps = 0;
            }

            _model.ReplaceAll(frames, fps, 0); // a code carries saved (already calmed) pixels
            AfterChange();
            SetStatus(string.Format(L("ui.tex.code_pasted"), key), EditorStatusSeverity.Info);
        }

        private void OpenFolder()
        {
            try
            {
                Directory.CreateDirectory(TexturePackFolder.Root);
                Application.OpenURL("file:///" + TexturePackFolder.Root.Replace('\\', '/'));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[TextureEditor] Could not open the texture folder: {e}");
                SetStatus(L("ui.ed.open_folder_failed"), EditorStatusSeverity.Error);
            }
        }

        /// <summary>Reload from the folder (desktop only, after <see cref="EditorShell.ConfirmDiscard"/>): what is on the
        /// canvas may just have been replaced from the folder, so the texture is opened again.</summary>
        private void ReloadFolder()
        {
            int n = TexturePackFolder.Reload();
            Open(_entry);
            SetStatus(string.Format(L("ui.tex.reloaded"), n), EditorStatusSeverity.Success);
        }

        private void Publish()
        {
            if (_entry == null || WorldHost == null || !WorldHost.CanPublish)
            {
                return;
            }

            var frames = SavedFrames(); // calmed as chosen
            if (frames.Length > 1 && _entry.Block == null)
            {
                frames = new[] { frames[0] }; // only blocks animate
            }

            WorldHost.Publish(_entry.Key, frames, frames.Length > 1 ? _model.Fps : 0);
            RememberCalmOriginal();
            SetStatus(L("ui.tex.publish_sent"), EditorStatusSeverity.Success);
        }

        /// <summary>"Remove from world": the world's own texture goes for everyone — asks first.</summary>
        private void ConfirmUnpublish()
        {
            if (_entry != null && WorldHost != null && WorldHost.CanPublish)
            {
                _shell.Confirm(L("ui.tex.confirm_unpublish").Replace("{name}", _entry.Label), L("ui.tex.unpublish"), Unpublish);
            }
        }

        private void Unpublish()
        {
            if (_entry != null && WorldHost != null && WorldHost.CanPublish)
            {
                WorldHost.Unpublish(_entry.Key);
            }
        }

        // ---------------------------------------------------------------- status and hint

        private void SetStatus(string text, EditorStatusSeverity severity) => _shell?.SetStatus(text, severity);

        /// <summary>The controls line on the right of the status bar, by input device and pad mode.</summary>
        private void SetHint(string key)
        {
            if (_shell != null && key != _hintKey)
            {
                _hintKey = key;
                _shell.SetHint(L(key));
            }
        }
    }
}
