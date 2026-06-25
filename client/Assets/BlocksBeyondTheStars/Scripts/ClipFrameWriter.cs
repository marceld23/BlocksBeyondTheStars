// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using Unity.Collections;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The reusable capture core behind <see cref="ClipDirector"/>: writes one PNG per rendered frame plus a
    /// single WAV of the game audio, frame-synced. It does NOT drive the scene or own the timing — the caller
    /// fixes <see cref="Time.captureFramerate"/> and calls <see cref="CaptureFrame"/> once per rendered frame
    /// (after <c>WaitForEndOfFrame</c>), then <see cref="FinishAudio"/> at the end. An external FFmpeg step
    /// (capture-clips.ps1) muxes <c>frame_%05d.png</c> + <c>audio.wav</c> into an MP4.
    ///
    /// Two video modes, chosen per clip:
    /// <list type="bullet">
    ///   <item><b>HUD-free</b> (<paramref name="hudFreeSource"/> set) — renders a throwaway clone of the view
    ///   camera into a render texture, exactly like <see cref="CameraTool"/>: no visor composite, no HUD canvas.</item>
    ///   <item><b>With HUD</b> (source null) — the full composited frame via
    ///   <see cref="ScreenCapture.CaptureScreenshotAsTexture()"/>, like <see cref="ScreenshotDirector"/>.</item>
    /// </list>
    ///
    /// Audio is captured with <see cref="AudioRenderer"/>: while <see cref="Time.captureFramerate"/> is set,
    /// Unity advances the audio DSP by exactly one capture-frame per rendered frame, so reading one frame of
    /// samples per <see cref="CaptureFrame"/> stays in sync even when the offline render runs slower than
    /// real time. This needs a real audio device — it produces silence under <c>-batchmode -nographics</c>.
    /// </summary>
    public sealed class ClipFrameWriter : IDisposable
    {
        private readonly int _width;
        private readonly int _height;
        private readonly string _framesDir;

        // HUD-free path (null => capture the full composited frame incl. HUD via ScreenCapture).
        private readonly Camera _hudFreeSource;
        private RenderTexture _rt;
        private GameObject _camGo;
        private Camera _cam;
        private Texture2D _readback;

        // Audio
        private readonly int _channels;
        private readonly int _sampleRate;
        private readonly List<float> _samples = new List<float>(1 << 20);
        private bool _audioStarted;

        private int _frameIndex;

        public int FrameCount => _frameIndex;

        public ClipFrameWriter(string framesDir, int width, int height, Camera hudFreeSource)
        {
            _framesDir = framesDir;
            _width = Mathf.Max(2, width);
            _height = Mathf.Max(2, height);
            _hudFreeSource = hudFreeSource;
            Directory.CreateDirectory(_framesDir);

            // Clear any frames left from an earlier run so a shorter clip can't inherit stale trailing frames
            // (which would corrupt the FFmpeg sequence mux).
            foreach (var old in Directory.GetFiles(_framesDir, "frame_*.png"))
            {
                try { File.Delete(old); }
                catch { /* best effort */ }
            }

            _channels = ChannelCount(AudioSettings.speakerMode);
            _sampleRate = AudioSettings.outputSampleRate;

            if (_hudFreeSource != null)
            {
                _rt = new RenderTexture(_width, _height, 24, RenderTextureFormat.ARGB32) { name = "ClipRT" };
                _rt.Create();

                _camGo = new GameObject("ClipCamera");
                _cam = _camGo.AddComponent<Camera>();
                _cam.enabled = false;             // we drive it manually with Render() each frame
                _cam.targetTexture = _rt;
            }
        }

        /// <summary>Begin capturing the game audio. Call once, before the first <see cref="CaptureFrame"/>.</summary>
        public void StartAudio()
        {
            AudioRenderer.Start();
            _audioStarted = true;
        }

        /// <summary>Write one video frame (PNG) and pull one frame of audio. Call once per rendered frame,
        /// after <c>yield return new WaitForEndOfFrame()</c> so the pipeline has finished the frame.</summary>
        public void CaptureFrame() => CaptureFrame(false, default, default);

        /// <summary>As <see cref="CaptureFrame()"/>, but for the HUD-free path the clone camera is placed at an
        /// explicit pose (a cinematic camera move) instead of mirroring the live view camera. Ignored in HUD mode.</summary>
        public void CaptureFrame(bool overridePose, Vector3 pos, Quaternion rot)
        {
            CaptureVideoFrame(overridePose, pos, rot);
            CaptureAudioFrame();
        }

        private void CaptureVideoFrame(bool overridePose, Vector3 overridePos, Quaternion overrideRot)
        {
            string path = Path.Combine(_framesDir, $"frame_{_frameIndex + 1:D5}.png");
            try
            {
                byte[] png;
                if (_hudFreeSource != null)
                {
                    // Re-sync the clone to the live view camera, render it into the RT, read it back. A cinematic
                    // camera move (overridePose) keeps the source's lens settings but places the clone elsewhere.
                    _cam.CopyFrom(_hudFreeSource); // FOV, clip planes, HUD-excluded culling mask
                    _cam.enabled = false;
                    _cam.targetTexture = _rt;
                    if (overridePose)
                    {
                        _cam.transform.SetPositionAndRotation(overridePos, overrideRot);
                    }
                    else
                    {
                        _cam.transform.SetPositionAndRotation(
                            _hudFreeSource.transform.position, _hudFreeSource.transform.rotation);
                    }

                    _cam.Render();

                    var prevActive = RenderTexture.active;
                    try
                    {
                        RenderTexture.active = _rt;
                        if (_readback == null)
                        {
                            _readback = new Texture2D(_width, _height, TextureFormat.RGB24, mipChain: false);
                        }

                        _readback.ReadPixels(new Rect(0, 0, _width, _height), 0, 0);
                        _readback.Apply(false);
                        png = _readback.EncodeToPNG();
                    }
                    finally
                    {
                        RenderTexture.active = prevActive;
                    }
                }
                else
                {
                    Texture2D tex = null;
                    try
                    {
                        tex = ScreenCapture.CaptureScreenshotAsTexture(); // full frame incl. overlay HUD
                        png = tex.EncodeToPNG();
                    }
                    finally
                    {
                        if (tex != null)
                        {
                            UnityEngine.Object.Destroy(tex);
                        }
                    }
                }

                File.WriteAllBytes(path, png);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Clip] frame {_frameIndex + 1} failed: {e.Message}");
            }

            _frameIndex++;
        }

        private void CaptureAudioFrame()
        {
            if (!_audioStarted)
            {
                return;
            }

            int perChannel = AudioRenderer.GetSampleCountForCaptureFrame();
            int total = perChannel * _channels;
            if (total <= 0)
            {
                return;
            }

            var buffer = new NativeArray<float>(total, Allocator.Temp);
            try
            {
                AudioRenderer.Render(buffer);
                for (int i = 0; i < total; i++)
                {
                    _samples.Add(buffer[i]);
                }
            }
            finally
            {
                buffer.Dispose();
            }
        }

        /// <summary>Stop audio capture and write the accumulated samples as a 16-bit PCM WAV. Call once when
        /// the clip is done. Returns the WAV path (or null if no audio was captured).</summary>
        public string FinishAudio(string wavPath)
        {
            if (_audioStarted)
            {
                AudioRenderer.Stop();
                _audioStarted = false;
            }

            if (_samples.Count == 0)
            {
                return null;
            }

            try
            {
                WriteWav16(wavPath, _samples, _channels, _sampleRate);
                return wavPath;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Clip] WAV write failed: {e.Message}");
                return null;
            }
        }

        private static void WriteWav16(string path, List<float> samples, int channels, int sampleRate)
        {
            int dataBytes = samples.Count * 2; // 16-bit
            int byteRate = sampleRate * channels * 2;
            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
            using var w = new BinaryWriter(fs);

            // RIFF header
            w.Write(new[] { 'R', 'I', 'F', 'F' });
            w.Write(36 + dataBytes);
            w.Write(new[] { 'W', 'A', 'V', 'E' });
            // fmt chunk
            w.Write(new[] { 'f', 'm', 't', ' ' });
            w.Write(16);                       // PCM fmt chunk size
            w.Write((short)1);                 // PCM
            w.Write((short)channels);
            w.Write(sampleRate);
            w.Write(byteRate);
            w.Write((short)(channels * 2));    // block align
            w.Write((short)16);                // bits per sample
            // data chunk
            w.Write(new[] { 'd', 'a', 't', 'a' });
            w.Write(dataBytes);
            for (int i = 0; i < samples.Count; i++)
            {
                short s = (short)(Mathf.Clamp(samples[i], -1f, 1f) * short.MaxValue);
                w.Write(s);
            }
        }

        private static int ChannelCount(AudioSpeakerMode mode)
        {
            switch (mode)
            {
                case AudioSpeakerMode.Mono: return 1;
                case AudioSpeakerMode.Stereo: return 2;
                case AudioSpeakerMode.Quad: return 4;
                case AudioSpeakerMode.Surround: return 5;
                case AudioSpeakerMode.Mode5point1: return 6;
                case AudioSpeakerMode.Mode7point1: return 8;
                case AudioSpeakerMode.Prologic: return 2;
                default: return 2;
            }
        }

        public void Dispose()
        {
            if (_audioStarted)
            {
                AudioRenderer.Stop();
                _audioStarted = false;
            }

            if (_cam != null)
            {
                _cam.targetTexture = null;
            }

            if (_readback != null)
            {
                UnityEngine.Object.Destroy(_readback);
                _readback = null;
            }

            if (_camGo != null)
            {
                UnityEngine.Object.Destroy(_camGo);
                _camGo = null;
                _cam = null;
            }

            if (_rt != null)
            {
                _rt.Release();
                UnityEngine.Object.Destroy(_rt);
                _rt = null;
            }
        }
    }
}
