// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Networking.Messages;

/// <summary>
/// One world texture (#1958): a 64×64 tile — or up to eight animation frames of one — that an admin published for
/// everyone in this save. It overrides the official texture of <see cref="Key"/> on every client of the world.
/// </summary>
public sealed class NetWorldTexture
{
    /// <summary>Registry id inside the save (never reused; a wiped texture keeps its id as a tombstone).</summary>
    public int Id { get; set; }

    /// <summary>The texture key it overrides: a block key or another key the client's texture source knows.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Number of 64×64 frames in <see cref="Data"/> (1 = a still texture).</summary>
    public int Frames { get; set; } = 1;

    /// <summary>Animation speed in frames per second; ignored for a still texture.</summary>
    public int Fps { get; set; }

    /// <summary>The pixels: base64 of the raw-deflated RGBA32 frames (<c>WorldTextureCodec</c>). Empty = the
    /// texture was wiped and the official one shows again.</summary>
    public string Data { get; set; } = string.Empty;

    /// <summary>Display name of the player who published it (credit + moderation).</summary>
    public string Owner { get; set; } = string.Empty;
}

/// <summary>An admin publishes a texture for this world (client → server). Replaces an earlier one for the same
/// key. Only accepted when the world rule allows world textures and the sender is an admin.</summary>
public sealed class PublishWorldTextureIntent
{
    public string Key { get; set; } = string.Empty;

    public int Frames { get; set; } = 1;

    public int Fps { get; set; }

    /// <summary>Base64 of the raw-deflated RGBA32 frames, see <see cref="NetWorldTexture.Data"/>.</summary>
    public string Data { get; set; } = string.Empty;
}

/// <summary>An admin removes this world's texture for a key (client → server); the official texture shows again.</summary>
public sealed class RemoveWorldTextureIntent
{
    public string Key { get; set; } = string.Empty;
}

/// <summary>One world texture was published, replaced or wiped (server → every joined client).</summary>
public sealed class WorldTextureData
{
    public NetWorldTexture Texture { get; set; } = new();
}

/// <summary>
/// A page of the world's textures, streamed to a joining client (server → client). The list is paged and paced
/// over ticks: a texture is some kilobytes, the browser path has neither compression nor binary fields, and a
/// single message must stay far below the 1 MiB cap. The client applies the textures once, when
/// <see cref="Final"/> arrives.
/// </summary>
public sealed class WorldTextureList
{
    public NetWorldTexture[] Textures { get; set; } = System.Array.Empty<NetWorldTexture>();

    /// <summary>Zero-based page number, for diagnostics.</summary>
    public int Page { get; set; }

    /// <summary>True on the last page (also on the single empty page of a world without textures).</summary>
    public bool Final { get; set; }
}
