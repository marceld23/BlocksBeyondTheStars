// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.IO.Compression;

namespace BlocksBeyondTheStars.Shared.Textures;

/// <summary>
/// How a world texture travels and is stored (#1958): the raw RGBA32 frames, deflated, as base64 text.
/// <para>
/// Raw pixels rather than a PNG on purpose — the dedicated server is headless and has no image library, and with
/// raw pixels it can validate a texture with plain byte checks: an exact length, the alpha rule, the frame count.
/// Text rather than bytes because every registry payload in the save files is text, and because the browser
/// client's JSON path would base64 a byte array anyway. Deflate because that JSON path has no compression of its
/// own and a join may carry a few hundred of these.
/// </para>
/// </summary>
public static class WorldTextureCodec
{
    /// <summary>Most world textures a save may hold.</summary>
    public const int MaxTextures = 256;

    /// <summary>Atlas slots all ANIMATED world textures of a save may use together (each frame is one slot in the
    /// client's dynamic band, which also serves the player's local pack).</summary>
    public const int AnimSlotBudget = 320;

    /// <summary>Upper bound for the encoded text of a texture with <paramref name="frames"/> frames: deflate can
    /// grow incompressible input slightly, base64 adds a third.</summary>
    public static int MaxEncodedLength(int frames)
    {
        long raw = (long)frames * TextureTiles.BytesPerFrame;
        long deflated = raw + (raw / 1000) + 1024;
        return (int)(((deflated + 2) / 3) * 4);
    }

    /// <summary>Encodes frames (each <see cref="TextureTiles.BytesPerFrame"/> bytes) for the wire and the save.</summary>
    public static string Encode(IReadOnlyList<byte[]> frames)
    {
        using var buffer = new MemoryStream();
        using (var deflate = new DeflateStream(buffer, CompressionLevel.Optimal, leaveOpen: true))
        {
            foreach (var frame in frames)
            {
                deflate.Write(frame, 0, frame.Length);
            }
        }

        return Convert.ToBase64String(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    /// <summary>
    /// Decodes untrusted text into exactly <paramref name="frames"/> frames. Fails — never throws — on anything
    /// else: text that is too long, not base64, not deflate, or that inflates to a different size. Inflation stops
    /// at the expected size plus one byte, so a small "bomb" cannot make the server allocate more than a texture.
    /// </summary>
    public static bool TryDecode(string? data, int frames, out byte[][] result)
    {
        result = Array.Empty<byte[]>();
        if (string.IsNullOrEmpty(data) || frames < 1 || frames > TextureTiles.MaxFrames
            || data.Length > MaxEncodedLength(frames))
        {
            return false;
        }

        byte[] packed;
        try
        {
            packed = Convert.FromBase64String(data);
        }
        catch (FormatException)
        {
            return false;
        }

        int expected = frames * TextureTiles.BytesPerFrame;
        var raw = new byte[expected];
        try
        {
            using var source = new MemoryStream(packed, writable: false);
            using var inflate = new DeflateStream(source, CompressionMode.Decompress);
            int total = 0;
            while (total < expected)
            {
                int read = inflate.Read(raw, total, expected - total);
                if (read <= 0)
                {
                    return false; // shorter than announced
                }

                total += read;
            }

            if (inflate.ReadByte() >= 0)
            {
                return false; // longer than announced
            }
        }
        catch (InvalidDataException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }

        var split = new byte[frames][];
        for (int i = 0; i < frames; i++)
        {
            split[i] = new byte[TextureTiles.BytesPerFrame];
            Buffer.BlockCopy(raw, i * TextureTiles.BytesPerFrame, split[i], 0, TextureTiles.BytesPerFrame);
        }

        result = split;
        return true;
    }
}
