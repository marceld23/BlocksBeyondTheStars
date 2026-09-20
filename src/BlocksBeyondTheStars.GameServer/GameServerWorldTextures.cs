// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Textures;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// World textures (#1958): an admin publishes a 64×64 texture — or an animation of up to eight frames — for a
/// texture key, and every client of the save shows it instead of the official one. The registry is save-global
/// (like paint designs and player forms) and keyed by the texture key: publishing again replaces, removing brings
/// the official texture back. Nothing in the world references a world texture, so there are no ids and no
/// tombstones.
/// <para>
/// Only admins may publish, and only while the world rule allows it: an override of <c>stone</c> changes the
/// world for everybody, which makes it a griefing tool in any other hands. The payload is player-made imagery that
/// is persisted and rebroadcast, so it is validated like one: a legal key, a legal frame count and speed, text
/// that inflates to EXACTLY the announced size (with a hard stop — no decompression bomb), and the shared alpha
/// rule forced onto the pixels (a see-through "stone" would be an x-ray, and the transparent shader reads a
/// see-through tile as water). The server needs no image library for any of that.
/// </para>
/// <para>
/// A joining client gets the list in pages, one page per tick: a texture is some kilobytes, the browser path has
/// neither compression nor binary fields, a single message must stay far below the 1 MiB cap, and the join burst
/// is already forty messages in one tick.
/// </para>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Encoded characters per list page. With at most eight frames a texture stays under ~180 KB of
    /// text, so a page is one big animation or many small stills — always far below the 1 MiB message cap, also
    /// on the uncompressed JSON path.</summary>
    internal const int WorldTexturePageChars = 200_000;

    private readonly Dictionary<string, StoredWorldTexture> _worldTextures = new(System.StringComparer.Ordinal);

    /// <summary>Restores the registry at server start. Idempotent. Rows that no longer decode (a hand-edited
    /// save) are dropped with a warning rather than sent to every client.</summary>
    private void LoadWorldTextures()
    {
        _worldTextures.Clear();
        foreach (var texture in _repo.ListWorldTextures())
        {
            if (!TextureTiles.IsValidKey(texture.Key)
                || !TextureTiles.IsValidAnimation(texture.Frames, texture.Fps)
                || !WorldTextureCodec.TryDecode(texture.Data, texture.Frames, out _))
            {
                _log.Warn($"World texture '{texture.Key}' in the save is malformed — ignored.");
                continue;
            }

            _worldTextures[texture.Key] = texture;
        }
    }

    /// <summary>True when world textures are switched on for this world (the rule; who may publish is a second
    /// question, answered in <see cref="MayPublishWorldTextures"/>).</summary>
    private bool WorldTexturesEnabled => Rules.WorldTextures;

    private static bool MayPublishWorldTextures(PlayerSession session)
        => session.State.IsAdmin || session.IsFleetAdmin;

    /// <summary>The keys a world may override: every block, and the families of non-block textures the client
    /// loads by key (creature hides, avatar fabrics, micro fauna, prop and tool tiles, the two loose ones). The
    /// server cannot list the client's resources, so families are matched by prefix — an unknown key inside a
    /// family is simply never asked for by any client.</summary>
    private bool IsPublishableTextureKey(string key)
    {
        if (!TextureTiles.IsValidKey(key))
        {
            return false;
        }

        if (_content.GetBlock(key) != null)
        {
            return true;
        }

        return key.StartsWith("creature_", System.StringComparison.Ordinal)
            || key.StartsWith("microfauna_", System.StringComparison.Ordinal)
            || key.StartsWith("avatar_", System.StringComparison.Ordinal)
            || key.StartsWith("prop_", System.StringComparison.Ordinal)
            || key.StartsWith("tool_", System.StringComparison.Ordinal)
            || key is "enemy_robot" or "asteroid_rock";
    }

    private void HandlePublishWorldTexture(PlayerSession session, PublishWorldTextureIntent intent)
    {
        if (!WorldTexturesEnabled)
        {
            Reject(session, "worldtexture", "@srv.worldtex.off");
            return;
        }

        if (!MayPublishWorldTextures(session))
        {
            Reject(session, "worldtexture", "@srv.worldtex.admin_only");
            return;
        }

        string key = intent.Key ?? string.Empty;
        if (!IsPublishableTextureKey(key))
        {
            Reject(session, "worldtexture", "@srv.worldtex.bad_key");
            return;
        }

        // Only a block tile lives in the atlas, where the shader can walk a frame strip; everything else is a
        // material of its own and shows one frame.
        bool isBlock = _content.GetBlock(key) != null;
        int frames = intent.Frames;
        if (!TextureTiles.IsValidAnimation(frames, intent.Fps) || (frames > 1 && !isBlock))
        {
            Reject(session, "worldtexture", "@srv.worldtex.bad_animation");
            return;
        }

        if (_uptime < session.NextWorldTextureAt)
        {
            Reject(session, "worldtexture", "@srv.worldtex.too_fast");
            return;
        }

        if (!WorldTextureCodec.TryDecode(intent.Data, frames, out var pixels))
        {
            Reject(session, "worldtexture", "@srv.worldtex.bad_data");
            return;
        }

        // The alpha rule is FORCED, not checked: whatever the client sent, a block tile leaves here opaque.
        var mode = TextureTiles.AlphaModeOf(key);
        int changed = 0;
        foreach (var frame in pixels)
        {
            changed += TextureTiles.EnforceAlpha(frame, mode);
        }

        string data = changed == 0 ? intent.Data! : WorldTextureCodec.Encode(pixels);

        _worldTextures.TryGetValue(key, out var existing);
        if (existing != null && existing.Frames == frames && existing.Fps == intent.Fps && existing.Data == data)
        {
            return; // already exactly this — nothing to store, nothing to tell anyone
        }

        if (existing == null && _worldTextures.Count >= WorldTextureCodec.MaxTextures)
        {
            Reject(session, "worldtexture", "@srv.worldtex.limit");
            return;
        }

        int animSlots = _worldTextures.Values.Where(t => t.Frames > 1 && t.Key != key).Sum(t => t.Frames)
                        + (frames > 1 ? frames : 0);
        if (animSlots > WorldTextureCodec.AnimSlotBudget)
        {
            Reject(session, "worldtexture", "@srv.worldtex.anim_limit");
            return;
        }

        session.NextWorldTextureAt = _uptime + 2.0; // a disk write + a broadcast to everyone, like a paint
        var stored = new StoredWorldTexture
        {
            Key = key,
            Frames = frames,
            Fps = frames > 1 ? intent.Fps : 0,
            Data = data,
            OwnerId = session.State.PlayerId,
            OwnerName = session.State.Name,
            CreatedUnix = System.DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        };
        _worldTextures[key] = stored;
        _repo.SaveWorldTexture(stored);
        BroadcastWorldTexture(ToNet(stored));
        _log.Info($"World texture '{key}' published by '{session.State.Name}' ({frames} frame(s)).");
        Send(session, new ServerMessage { Text = "@srv.worldtex.published:" + key });
    }

    private void HandleRemoveWorldTexture(PlayerSession session, RemoveWorldTextureIntent intent)
    {
        if (!MayPublishWorldTextures(session))
        {
            Reject(session, "worldtexture", "@srv.worldtex.admin_only");
            return;
        }

        string key = intent.Key ?? string.Empty;
        if (!RemoveWorldTexture(key))
        {
            Reject(session, "worldtexture", "@srv.worldtex.none");
            return;
        }

        _log.Info($"World texture '{key}' removed by '{session.State.Name}'.");
        Send(session, new ServerMessage { Text = "@srv.worldtex.removed:" + key });
    }

    /// <summary>Deletes one world texture and tells every client (empty data = "the official one again").</summary>
    private bool RemoveWorldTexture(string key)
    {
        if (!_worldTextures.Remove(key))
        {
            return false;
        }

        _repo.DeleteWorldTexture(key);
        BroadcastWorldTexture(new NetWorldTexture { Key = key, Data = string.Empty });
        return true;
    }

    private void BroadcastWorldTexture(NetWorldTexture texture)
    {
        var msg = new WorldTextureData { Texture = texture };
        foreach (var viewer in _sessions.Values)
        {
            if (viewer.Joined)
            {
                Send(viewer, msg);
            }
        }
    }

    private static NetWorldTexture ToNet(StoredWorldTexture t)
        => new() { Key = t.Key, Frames = t.Frames, Fps = t.Fps, Data = t.Data, Owner = t.OwnerName };

    /// <summary>Queues the world's textures for a client — a join, or the world rule was switched. The pages go
    /// out from the tick, one per session per tick. A world without textures (or with the rule off) still gets
    /// one empty final page, so the client knows the list is complete and clears what it had.</summary>
    private void QueueWorldTextures(PlayerSession session)
    {
        session.WorldTexturePages.Clear();
        var pages = BuildWorldTexturePages(
            WorldTexturesEnabled
                ? _worldTextures.Values.OrderBy(t => t.Key, System.StringComparer.Ordinal).Select(ToNet).ToList()
                : new List<NetWorldTexture>());
        foreach (var page in pages)
        {
            session.WorldTexturePages.Enqueue(page);
        }
    }

    /// <summary>Splits a texture list into pages of at most <see cref="WorldTexturePageChars"/> encoded characters
    /// (a single oversized texture still gets a page of its own). Always at least one page; the first carries
    /// <c>Reset</c>, the last <c>Final</c>.</summary>
    internal static List<WorldTextureList> BuildWorldTexturePages(IReadOnlyList<NetWorldTexture> textures)
    {
        var pages = new List<WorldTextureList>();
        var current = new List<NetWorldTexture>();
        int chars = 0;
        foreach (var texture in textures)
        {
            if (current.Count > 0 && chars + texture.Data.Length > WorldTexturePageChars)
            {
                pages.Add(new WorldTextureList { Textures = current.ToArray(), Page = pages.Count });
                current = new List<NetWorldTexture>();
                chars = 0;
            }

            current.Add(texture);
            chars += texture.Data.Length;
        }

        pages.Add(new WorldTextureList { Textures = current.ToArray(), Page = pages.Count });
        pages[0].Reset = true;
        pages[pages.Count - 1].Final = true;
        return pages;
    }

    /// <summary>Tick: sends each session its next queued page. One page per tick keeps a join from turning into a
    /// burst of large messages on top of the forty the join already sends.</summary>
    private void StreamWorldTextures()
    {
        foreach (var session in _sessions.Values)
        {
            if (session.Joined && session.WorldTexturePages.Count > 0)
            {
                Send(session, session.WorldTexturePages.Dequeue());
            }
        }
    }

    /// <summary>The world rule was switched: every client gets the full list again (empty when switched off).</summary>
    private void ResendWorldTexturesToAll()
    {
        foreach (var session in _sessions.Values)
        {
            if (session.Joined)
            {
                QueueWorldTextures(session);
            }
        }
    }

    /// <summary>Chat command <c>/reporttexture &lt;key&gt;</c> — any player may flag a world texture. There is no
    /// table for it: on a self-hosted world the admin who would read the row is the one who published the
    /// texture. The report goes to the server log, the operator notification and — on hosted worlds — the
    /// maintainers' inbox.</summary>
    private void HandleWorldTextureReport(PlayerSession session, string? arg)
    {
        string key = (arg ?? string.Empty).Trim();
        if (!_worldTextures.TryGetValue(key, out var texture))
        {
            Send(session, new ServerMessage { Text = "@srv.worldtex.report_none" });
            return;
        }

        var p = session.State;
        _log.Warn($"WORLD TEXTURE REPORT: '{p.Name}' ({p.PlayerId}) reported texture '{key}' " +
                  $"published by '{texture.OwnerName}' ({texture.OwnerId}).");
        ForwardContentReport(
            "texture", session, 0, texture.OwnerId, texture.OwnerName + " / " + key,
            _worlds.Active.LocationId, (int)p.Position.X, (int)p.Position.Y, (int)p.Position.Z);
        Send(session, new ServerMessage { Text = "@srv.worldtex.report_sent" });
    }

    /// <summary>Admin command <c>/texturewipe &lt;key|Player|all&gt;</c>: removes one texture, every texture a player
    /// published, or all of them.</summary>
    private void AdminWorldTextureWipe(PlayerSession session, string? arg)
    {
        string target = (arg ?? string.Empty).Trim();
        if (target.Length == 0)
        {
            Reject(session, "admin", "@srv.admin.usage_texturewipe");
            return;
        }

        List<string> keys;
        if (target.Equals("all", System.StringComparison.OrdinalIgnoreCase))
        {
            keys = _worldTextures.Keys.ToList();
        }
        else if (_worldTextures.ContainsKey(target))
        {
            keys = new List<string> { target };
        }
        else
        {
            string ownerId = _sessions.Values.FirstOrDefault(s =>
                    s.Joined && string.Equals(s.State.Name, target, System.StringComparison.OrdinalIgnoreCase))
                ?.State.PlayerId ?? target;
            keys = _worldTextures.Values
                .Where(t => t.OwnerId == ownerId
                            || string.Equals(t.OwnerName, target, System.StringComparison.OrdinalIgnoreCase))
                .Select(t => t.Key).ToList();
        }

        if (keys.Count == 0)
        {
            Send(session, new ServerMessage { Text = "@srv.admin.texture_none:" + target });
            return;
        }

        foreach (string key in keys)
        {
            RemoveWorldTexture(key);
        }

        Send(session, new ServerMessage { Text = "@srv.admin.texture_wiped:" + keys.Count });
        CheatLog(session.State, $"wiped {keys.Count} world texture(s) ({target})");
    }
}
