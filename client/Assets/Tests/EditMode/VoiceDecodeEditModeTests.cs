// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
#if BBS_VOICE
using System;
using BlocksBeyondTheStars.Client;
using Concentus;
using Concentus.Enums;
using NUnit.Framework;

namespace BlocksBeyondTheStars.Client.Tests.EditMode
{
    /// <summary>
    /// #2579: a relayed voice packet the Opus decoder rejects threw out of <c>NetworkClient.Poll</c>, aborted the frame's
    /// whole Update and was filed as a client crash. <see cref="VoiceChat.TryDecode"/> is the guard: a real 20 ms frame
    /// decodes, anything else comes back as "dropped" with the reason — never as an exception.
    /// </summary>
    public sealed class VoiceDecodeEditModeTests
    {
        private const int SampleRate = 48000, FrameSamples = 960;

        private static byte[] EncodeTone()
        {
            var encoder = OpusCodecFactory.CreateEncoder(SampleRate, 1, OpusApplication.OPUS_APPLICATION_VOIP);
            var pcm = new short[FrameSamples];
            for (int i = 0; i < pcm.Length; i++)
            {
                pcm[i] = (short)(Math.Sin(i * 2.0 * Math.PI * 440.0 / SampleRate) * 8000.0);
            }

            var buffer = new byte[4000];
            int bytes = encoder.Encode(pcm, FrameSamples, buffer, buffer.Length);
            Assert.Greater(bytes, 0, "the encoder produced a packet");
            var packet = new byte[bytes];
            Array.Copy(buffer, packet, bytes);
            (encoder as IDisposable)?.Dispose();
            return packet;
        }

        [Test]
        public void ARealVoiceFrame_Decodes()
        {
            var decoder = OpusCodecFactory.CreateDecoder(SampleRate, 1);
            int decoded = VoiceChat.TryDecode(decoder, EncodeTone(), new short[FrameSamples], FrameSamples, out var error);
            Assert.IsNull(error);
            Assert.AreEqual(FrameSamples, decoded, "one 20 ms frame at 48 kHz");
        }

        [Test]
        public void EmptyOversizedAndMalformedPackets_AreDroppedWithoutThrowing()
        {
            var bad = new[]
            {
                Array.Empty<byte>(),
                new byte[VoiceChat.MaxOpusPacketBytes + 1],
                new byte[] { 0xFF, 0x00 },             // code-3 packet that claims zero frames
                new byte[] { 0x03, 0xFF, 0xFF, 0xFF }, // code-3 packet whose frame count and padding run past the end
                new byte[] { 0x0B, 0xF8, 0xF8 },       // code-2 packet with a frame length longer than the packet
            };

            var decoder = OpusCodecFactory.CreateDecoder(SampleRate, 1);
            foreach (var packet in bad)
            {
                int decoded = 0;
                Exception error = null;
                Assert.DoesNotThrow(() => decoded = VoiceChat.TryDecode(decoder, packet, new short[FrameSamples], FrameSamples, out error),
                    $"a {packet.Length}-byte packet must not throw");
                if (error == null)
                {
                    // Opus may legally read some short garbage as a (silent) frame — that is fine, it just never throws.
                    Assert.GreaterOrEqual(decoded, 0);
                    continue;
                }

                Assert.AreEqual(0, decoded, $"a dropped {packet.Length}-byte packet yields no samples");
                decoder = OpusCodecFactory.CreateDecoder(SampleRate, 1); // what VoiceChat does after a failure
            }

            Assert.IsNotNull(TryDecodeError(Array.Empty<byte>()), "an empty packet is reported as dropped");
            Assert.IsNotNull(TryDecodeError(new byte[VoiceChat.MaxOpusPacketBytes + 1]), "an oversized packet is reported as dropped");
        }

        [Test]
        public void ADecoderStillWorksForTheNextRealFrame_AfterAFreshStart()
        {
            var decoder = OpusCodecFactory.CreateDecoder(SampleRate, 1);
            VoiceChat.TryDecode(decoder, new byte[] { 0xFF, 0x00 }, new short[FrameSamples], FrameSamples, out _);
            decoder = OpusCodecFactory.CreateDecoder(SampleRate, 1);
            int decoded = VoiceChat.TryDecode(decoder, EncodeTone(), new short[FrameSamples], FrameSamples, out var error);
            Assert.IsNull(error);
            Assert.AreEqual(FrameSamples, decoded);
        }

        private static Exception TryDecodeError(byte[] packet)
        {
            VoiceChat.TryDecode(OpusCodecFactory.CreateDecoder(SampleRate, 1), packet, new short[FrameSamples], FrameSamples, out var error);
            return error;
        }
    }
}
#endif
