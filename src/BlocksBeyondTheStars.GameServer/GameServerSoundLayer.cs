// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Sounders;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The sound layer (#3907, epic #3891): the blocks of sound a player shapes on the other side. They are NOT in the world's
/// block grid and never a row of <c>block_edit</c> — they live in a sparse second layer per resident world
/// (<see cref="SoundLayerState"/>, chunk → cells) with a table of their own (<c>sound_edit</c>). So nothing that reads the
/// real world — fluids, fire, falling blocks, creatures, machines, the far view, a real-side player's client — can meet
/// one: sound blocks are rigid by construction.
/// <para>Players over there see the same thing, so a chunk has exactly two variants: the real one and "real + sound blocks"
/// (GameServerSoundLayer.Chunks.cs). A change of a sound cell goes out as an ordinary <see cref="BlockChanged"/> to the
/// players of that world who are over there, and to nobody else.</para>
/// <para>The rules (CONTRACT.md "The other side", 7): a sound block stands only in a cell that is air on the real side,
/// inside the build band, not in a base zone of a player who is no ally, not in a body, not right beside the body of a
/// player who is no ally, and not touching a rune stone (nobody is walled in, no gate is walled off); one player may have
/// <see cref="OtherSideRules.SoundBlockCap"/> standing per world; only the builder, their allies and admins dissolve one
/// — and anybody the one that touches their own body.
/// <b>The real side wins:</b> a real block a PLAYER writes into the cell dissolves the sound block for good
/// (<see cref="OnPlayerBlockSetOverSound"/>). Any other real block — snow, flowing water, falling sand, a regrown plant,
/// fire, a worldgen stamp — only HIDES it: the cell stays in the layer and the save (and in the builder's count), the
/// composed chunk shows the real block, and when the cell is air again the players over there get the sound block back
/// (<see cref="OnRealBlockSetOverSound"/>, <see cref="FlushSoundLayer"/>).</para>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The sound cells of one chunk, by local cell index, and the counter the composed chunk variant is cached by.</summary>
    internal sealed class SoundChunk
    {
        /// <summary>Moves with every cell set or removed in this chunk — never repeats within a world's residency.</summary>
        public long SoundVersion;

        public readonly Dictionary<int, (int Glow, string Owner)> Cells = new();
    }

    /// <summary>A resident world's sound layer: its cells by chunk, the count per builder (what the cap reads) and the
    /// cache of the composed chunk variant. Read from the save on first use, dropped with the world.</summary>
    internal sealed class SoundLayerState
    {
        public bool Loaded;
        public long VersionClock;
        public int Count;
        public readonly Dictionary<ChunkCoord, SoundChunk> Chunks = new();
        public readonly Dictionary<string, int> CountByOwner = new(System.StringComparer.Ordinal);
        public readonly Dictionary<ChunkCoord, (long Version, long SoundVersion, bool Json, byte[] Payload)> Payloads = new();
        public readonly Dictionary<ChunkCoord, (long Version, long SoundVersion, ChunkDataMessage Message)> Messages = new();

        /// <summary>Sound cells whose real cell was written as air since the last flush — the players over there are told
        /// the sound block (again) once the real change has gone out (<see cref="FlushSoundLayer"/>). Empty nearly always.</summary>
        public readonly HashSet<Vector3i> Reappear = new();
    }

    /// <summary>The locale keys of the three refusals (sent as <c>@key</c> tokens, localized by the client).</summary>
    internal const string SoundCapKey = "srv.otherside.sound_cap";
    internal const string SoundNotYoursKey = "srv.otherside.not_yours";
    internal const string SoundBlockedKey = "srv.otherside.sound_blocked";

    /// <summary>The cues of a sound block appearing and dissolving, played at the cell for the players over there.</summary>
    internal const string SoundShapeFx = "sound_shape";
    internal const string SoundDissolveFx = "sound_dissolve";

    private ushort? _soundBlockId;

    /// <summary>The numeric id of <c>sound_block</c> (0 when the content has none — then nothing can be shaped).</summary>
    private ushort SoundBlockId => _soundBlockId ??= _content.GetBlock(SoundersIds.SoundBlock)?.NumericId.Value ?? 0;

    /// <summary>A world's sound layer, read from the save the first time anything asks for it.</summary>
    private SoundLayerState SoundLayerOf(LoadedWorld world)
    {
        var layer = world.SoundLayer;
        if (!layer.Loaded)
        {
            layer.Loaded = true;
            int circumference = world.World.Circumference;
            foreach (var row in _repo.ListSoundCells(world.LocationId))
            {
                AddSoundCell(layer, WorldConstants.CanonicalBlock(new Vector3i(row.X, row.Y, row.Z), circumference), row.Glow, row.Owner);
            }
        }

        return layer;
    }

    private static int LocalIndexOf(Vector3i cell)
    {
        var local = WorldConstants.WorldToLocal(cell);
        return WorldConstants.LocalIndex(local.X, local.Y, local.Z);
    }

    private static Vector3i CellOf(ChunkCoord chunk, int localIndex)
    {
        var origin = WorldConstants.ChunkOrigin(chunk);
        int size = WorldConstants.ChunkSize;
        return new Vector3i(origin.X + (localIndex % size), origin.Y + (localIndex / (size * size)), origin.Z + ((localIndex / size) % size));
    }

    private static bool TryGetSoundCell(SoundLayerState layer, Vector3i canonical, out (int Glow, string Owner) cell)
    {
        cell = default;
        return layer.Count > 0
            && layer.Chunks.TryGetValue(WorldConstants.WorldToChunk(canonical), out var chunk)
            && chunk.Cells.TryGetValue(LocalIndexOf(canonical), out cell);
    }

    /// <summary>Memory only: puts a cell into the layer (replacing one that stood there) and moves the chunk's version.</summary>
    private static void AddSoundCell(SoundLayerState layer, Vector3i canonical, int glow, string owner)
    {
        var coord = WorldConstants.WorldToChunk(canonical);
        if (!layer.Chunks.TryGetValue(coord, out var chunk))
        {
            chunk = new SoundChunk();
            layer.Chunks[coord] = chunk;
        }

        int index = LocalIndexOf(canonical);
        if (chunk.Cells.TryGetValue(index, out var old))
        {
            CountSoundOwner(layer, old.Owner, -1);
            layer.Count--;
        }

        chunk.Cells[index] = (glow, owner);
        chunk.SoundVersion = ++layer.VersionClock;
        CountSoundOwner(layer, owner, +1);
        layer.Count++;
    }

    /// <summary>Memory only: takes a cell out of the layer. A chunk that lost its last cell leaves the layer, and its
    /// composed variant leaves the cache — the chunk is the real one again for everybody.</summary>
    private static bool RemoveSoundCell(SoundLayerState layer, Vector3i canonical, out (int Glow, string Owner) removed)
    {
        removed = default;
        var coord = WorldConstants.WorldToChunk(canonical);
        if (!layer.Chunks.TryGetValue(coord, out var chunk) || !chunk.Cells.Remove(LocalIndexOf(canonical), out removed))
        {
            return false;
        }

        chunk.SoundVersion = ++layer.VersionClock;
        CountSoundOwner(layer, removed.Owner, -1);
        layer.Count--;
        if (chunk.Cells.Count == 0)
        {
            layer.Chunks.Remove(coord);
            layer.Payloads.Remove(coord);
            layer.Messages.Remove(coord);
        }

        return true;
    }

    private static void CountSoundOwner(SoundLayerState layer, string owner, int delta)
    {
        layer.CountByOwner.TryGetValue(owner, out int count);
        count += delta;
        if (count <= 0)
        {
            layer.CountByOwner.Remove(owner);
        }
        else
        {
            layer.CountByOwner[owner] = count;
        }
    }

    /// <summary>Stands a sound block in a cell of <paramref name="world"/> — layer, save and the players over there — with no
    /// rule asked (<see cref="TryShapeSound"/> asks them). A cell that already holds one is re-coloured and changes hands.</summary>
    internal void PutSoundCell(LoadedWorld world, Vector3i cell, int glow, string owner)
    {
        var layer = SoundLayerOf(world);
        cell = WorldConstants.CanonicalBlock(cell, world.World.Circumference);
        TryGetSoundCell(layer, cell, out var before);
        AddSoundCell(layer, cell, glow, owner);
        _repo.SaveSoundCell(world.LocationId, cell, glow, owner);
        SendToOtherSideOf(world.LocationId, new BlockChanged { X = cell.X, Y = cell.Y, Z = cell.Z, Block = SoundBlockId, Glow = glow });
        TellSoundCount(world, owner);
        if (!string.IsNullOrEmpty(before.Owner) && before.Owner != owner)
        {
            TellSoundCount(world, before.Owner);
        }
    }

    /// <summary>Takes a sound block out of <paramref name="world"/> — layer, save and the players over there, who are told
    /// what the cell holds on the real side now (air, or the real block that took it). False when the cell holds none.</summary>
    internal bool DropSoundCell(LoadedWorld world, Vector3i cell)
    {
        var layer = SoundLayerOf(world);
        cell = WorldConstants.CanonicalBlock(cell, world.World.Circumference);
        if (!RemoveSoundCell(layer, cell, out var removed))
        {
            return false;
        }

        _repo.DeleteSoundCell(world.LocationId, cell);
        var real = world.World;
        var (tint, glow) = real.GetModifier(cell);
        SendToOtherSideOf(world.LocationId, new BlockChanged
        {
            X = cell.X,
            Y = cell.Y,
            Z = cell.Z,
            Block = real.GetBlock(cell).Value,
            Tint = tint,
            Glow = glow,
            Shape = real.GetShape(cell),
        });
        SendToOtherSideOf(world.LocationId, new SoundFx { SoundId = SoundDissolveFx, X = cell.X + 0.5f, Y = cell.Y + 0.5f, Z = cell.Z + 0.5f });
        TellSoundCount(world, removed.Owner);
        return true;
    }

    /// <summary>Every real write of every resident world (<see cref="ServerWorld.BlockSet"/>) into a cell that holds a
    /// sound block. A real block there — snow, flowing water, falling sand, a regrown plant, fire, a stamp — HIDES the
    /// sound block: nothing is stored, the cell keeps its place in the layer and the composed chunk leaves it out while
    /// the real cell is not air. Air there (the snow melted, the water ran off — or a tool "cleared" a cell that was air
    /// all along) queues the cell: the caller's own <see cref="BlockChanged"/> of air goes out after this hook, so the
    /// sound block is told afterwards, by <see cref="FlushSoundLayer"/>. One field read for a world without sound blocks.</summary>
    private void OnRealBlockSetOverSound(LoadedWorld world, Vector3i cell)
    {
        var layer = SoundLayerOf(world);
        if (layer.Count == 0 || !TryGetSoundCell(layer, cell, out _))
        {
            return;
        }

        if (world.World.GetBlockIfLoaded(cell).IsAir)
        {
            layer.Reappear.Add(cell);
        }
        else
        {
            layer.Reappear.Remove(cell);
        }
    }

    /// <summary>The real side wins (<see cref="ServerWorld.PlayerBlockSet"/>: a write that names a player): a real block a
    /// player put into a cell that holds a sound block dissolves it for good — layer, save, the dissolve cue. A player
    /// writing air (mining what hid the sound block) dissolves nothing: the sound block shows again.</summary>
    private void OnPlayerBlockSetOverSound(LoadedWorld world, Vector3i cell)
    {
        var layer = SoundLayerOf(world);
        if (layer.Count == 0 || !TryGetSoundCell(layer, cell, out _) || world.World.GetBlockIfLoaded(cell).IsAir)
        {
            return;
        }

        layer.Reappear.Remove(cell);
        DropSoundCell(world, cell);
    }

    /// <summary>Diagnostics: how often <see cref="FlushSoundLayer"/> found something queued (a tick with nothing queued
    /// does not count — and does nothing).</summary>
    public int SoundReappearFlushes { get; private set; }

    /// <summary>The per-world tick step: the sound blocks whose real cell became air since the last flush are told to the
    /// players over there — after the real change went out — and their chunks' sound version moves, so a later full send
    /// is right. One field read when nothing is queued, which is every tick but the rare one.</summary>
    private void FlushSoundLayer()
    {
        var world = _worlds.Active;
        var layer = world.SoundLayer;
        if (layer.Reappear.Count == 0)
        {
            return;
        }

        SoundReappearFlushes++;
        ushort block = SoundBlockId;
        foreach (var cell in layer.Reappear)
        {
            var coord = WorldConstants.WorldToChunk(cell);
            if (!TryGetSoundCell(layer, cell, out var found) || !world.World.IsChunkLoaded(coord) || !world.World.GetBlockIfLoaded(cell).IsAir)
            {
                continue; // dissolved meanwhile, hidden again, or in a chunk nobody holds (its next full send is right)
            }

            layer.Chunks[coord].SoundVersion = ++layer.VersionClock;
            SendToOtherSideOf(world.LocationId, new BlockChanged { X = cell.X, Y = cell.Y, Z = cell.Z, Block = block, Glow = found.Glow });
        }

        layer.Reappear.Clear();
    }

    /// <summary>Tells the other-side core that a builder's count moved (it shows the cap), when they are here to see it.</summary>
    private void TellSoundCount(LoadedWorld world, string owner)
    {
        if (FindSessionByPlayerId(owner) is { Joined: true } session && session.CurrentLocationId == world.LocationId)
        {
            SoundBlockCountChanged(session);
        }
    }

    /// <summary>Implemented by the other-side core if it wants to re-send its state when a builder's count of sound blocks
    /// moved; without an implementation the call compiles away.</summary>
    partial void SoundBlockCountChanged(PlayerSession builder);

    // ---------------------------------------------------------------------------------------------
    // What the other systems ask (the active world).
    // ---------------------------------------------------------------------------------------------

    /// <summary>Whether a cell of the active world holds a sound block.</summary>
    internal bool IsSoundBlock(Vector3i cell)
    {
        var world = _worlds.Active;
        return TryGetSoundCell(SoundLayerOf(world), WorldConstants.CanonicalBlock(cell, world.World.Circumference), out _);
    }

    /// <summary>The glow colour of the sound block in a cell of the active world (its tone: <see cref="OtherSideRules.ToneOfGlow"/>),
    /// or 0 when the cell holds none.</summary>
    internal int SoundGlowAt(Vector3i cell)
    {
        var world = _worlds.Active;
        return TryGetSoundCell(SoundLayerOf(world), WorldConstants.CanonicalBlock(cell, world.World.Circumference), out var found) ? found.Glow : 0;
    }

    /// <summary>How many sound blocks a player has standing in the active world.</summary>
    internal int SoundBlocksOf(string playerId) => SoundBlocksOf(_worlds.Active, playerId);

    private int SoundBlocksOf(LoadedWorld world, string playerId)
        => SoundLayerOf(world).CountByOwner.TryGetValue(playerId, out int count) ? count : 0;

    /// <summary>The sound blocks of the active world within <paramref name="radius"/> blocks of a point (measured to the
    /// cell's middle, the short way round the seams) — e.g. "a sound bridge of N blocks was built here". A fresh list.</summary>
    internal IEnumerable<Vector3i> SoundCellsNear(Vector3f at, int radius)
    {
        var found = new List<Vector3i>();
        var world = _worlds.Active;
        var layer = SoundLayerOf(world);
        if (layer.Count == 0 || radius < 0)
        {
            return found;
        }

        int circumference = world.World.Circumference;
        double reach = (double)radius * radius;
        var centre = at.ToBlock();
        var lo = WorldConstants.WorldToChunk(new Vector3i(centre.X - radius, centre.Y - radius, centre.Z - radius));
        var hi = WorldConstants.WorldToChunk(new Vector3i(centre.X + radius, centre.Y + radius, centre.Z + radius));
        long boxChunks = (long)(hi.X - lo.X + 1) * (hi.Y - lo.Y + 1) * (hi.Z - lo.Z + 1);
        if (boxChunks >= layer.Chunks.Count)
        {
            // A wide question (or a small layer): every chunk of the layer is cheaper than the box — and a box wider than
            // the world would meet a wrapped chunk twice.
            foreach (var kv in layer.Chunks)
            {
                CollectSoundCellsNear(kv.Key, kv.Value, at, reach, circumference, found);
            }

            return found;
        }

        for (int cx = lo.X; cx <= hi.X; cx++)
            for (int cy = lo.Y; cy <= hi.Y; cy++)
                for (int cz = lo.Z; cz <= hi.Z; cz++)
                {
                    var coord = WorldConstants.CanonicalChunk(new ChunkCoord(cx, cy, cz), circumference);
                    if (layer.Chunks.TryGetValue(coord, out var chunk))
                    {
                        CollectSoundCellsNear(coord, chunk, at, reach, circumference, found);
                    }
                }

        return found;
    }

    private static void CollectSoundCellsNear(ChunkCoord coord, SoundChunk chunk, Vector3f at, double reachSquared, int circumference,
        List<Vector3i> into)
    {
        foreach (int index in chunk.Cells.Keys)
        {
            var cell = CellOf(coord, index);
            var middle = new Vector3f(cell.X + 0.5f, cell.Y + 0.5f, cell.Z + 0.5f);
            if (WorldConstants.WrapDistanceSquared(at, middle, circumference) <= reachSquared)
            {
                into.Add(cell);
            }
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Shaping and dissolving, with the rules.
    // ---------------------------------------------------------------------------------------------

    /// <summary>Shapes a sound block of <paramref name="tone"/> (0–7, <see cref="OtherSideRules.ToneGlow"/>) in a cell for a
    /// player who is over there, if the rules allow it. On a refusal <paramref name="refusalKey"/> is the locale key of the
    /// one line to tell (<see cref="SoundCapKey"/> or <see cref="SoundBlockedKey"/>). Sounds <see cref="SoundShapeFx"/> at
    /// the cell in the tone's pitch and raises <c>sound:shaped</c> (argument: the tone) for the builder.</summary>
    internal bool TryShapeSound(PlayerSession s, Vector3i cell, int tone, out string refusalKey)
    {
        var world = _worlds.Active;
        cell = WorldConstants.CanonicalBlock(cell, world.World.Circumference);
        if (SoundShapeRefusal(s, world, cell) is { } refusal)
        {
            refusalKey = refusal;
            return false;
        }

        refusalKey = string.Empty;
        tone = System.Math.Clamp(tone, 0, OtherSideRules.ToneGlow.Length - 1);
        PutSoundCell(world, cell, OtherSideRules.GlowOfTone(tone), s.State.PlayerId);
        BroadcastToOtherSide(new SoundFx
        {
            SoundId = SoundShapeFx,
            X = cell.X + 0.5f,
            Y = cell.Y + 0.5f,
            Z = cell.Z + 0.5f,
            Pitch = OtherSideRules.PitchOfTone(tone),
        });
        RaiseGameEvent(s, Shared.Story.GameEventKeys.SoundShaped, tone.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return true;
    }

    /// <summary>Why a sound block cannot be shaped in a (canonical) cell, or null when it can.</summary>
    private string? SoundShapeRefusal(PlayerSession s, LoadedWorld world, Vector3i cell)
    {
        var p = s.State;
        if (!s.OtherSide || s.CurrentLocationId != world.LocationId || SoundBlockId == 0 || !WithinBuildHeight(cell.Y))
        {
            return SoundBlockedKey; // sound is shaped over there only, and inside the build band
        }

        var layer = SoundLayerOf(world);
        if (TryGetSoundCell(layer, cell, out _) || !world.World.GetBlock(cell).IsAir)
        {
            return SoundBlockedKey; // only where the real side is air and no sound stands yet
        }

        if (IsBaseProtected(cell, p.PlayerId, p.IsAdmin) || SoundCellHoldsABody(s, cell) || SoundCellAtRuneStone(world, cell))
        {
            return SoundBlockedKey; // not in the base of a player who is no ally, not in or around somebody, not on a gate's stones
        }

        return SoundBlocksOf(world, p.PlayerId) >= OtherSideRules.SoundBlockCap ? SoundCapKey : null;
    }

    /// <summary>How far around another player's body no stranger's sound block may stand, and how far around their own body
    /// a player may dissolve anybody's (cells): the body's two cells grown by this are a 3×4×3 box — the shared
    /// <see cref="OtherSideRules.BodyMargin"/>, which the client's bare-hand aim reads too (#3914).</summary>
    internal const int SoundBodyMargin = OtherSideRules.BodyMargin;

    /// <summary>A sound block is solid for the players over there, so nobody may be walled in with them: none goes into
    /// the head cell of a player, nor into the feet cell of anybody but the builder (who may lift themselves on it, as
    /// with a real block) — and none within <see cref="SoundBodyMargin"/> cells of the body of a player who is not the
    /// builder's ally (<see cref="AreAllied"/>), so a stranger cannot close a cage around somebody.</summary>
    private bool SoundCellHoldsABody(PlayerSession builder, Vector3i cell)
    {
        foreach (var s in JoinedInActiveWorld())
        {
            if (!s.OtherSide || s.Spectating)
            {
                continue;
            }

            bool own = ReferenceEquals(s, builder);
            if (InSoundBodyBox(s, cell, 0))
            {
                if (!own || cell.Y != s.State.Position.ToBlock().Y)
                {
                    return true; // in a body (the builder's own feet cell aside)
                }
            }
            else if (!own && InSoundBodyBox(s, cell, SoundBodyMargin) && !AreAllied(s.State.PlayerId, builder.State.PlayerId))
            {
                return true; // right beside a player who is no ally of the builder
            }
        }

        return false;
    }

    /// <summary>Whether a cell lies in a player's body box — the feet cell and the head cell — grown by
    /// <paramref name="margin"/> cells on every side (the short way round the seam).</summary>
    private bool InSoundBodyBox(PlayerSession s, Vector3i cell, int margin)
        => OtherSideRules.InBodyBox(s.State.Position.ToBlock(), cell, margin, _world.Circumference);

    /// <summary>A gate's stones stay free: no sound block in a cell that touches a rune stone (its six neighbours — the
    /// cell above a stone is one of them), so nobody walls the way home in.</summary>
    private bool SoundCellAtRuneStone(LoadedWorld world, Vector3i cell)
    {
        if (_content.GetBlock("rune_stone") is not { } rune)
        {
            return false;
        }

        ushort id = rune.NumericId.Value;
        var real = world.World;
        return real.GetBlock(new Vector3i(cell.X, cell.Y - 1, cell.Z)).Value == id
               || real.GetBlock(new Vector3i(cell.X, cell.Y + 1, cell.Z)).Value == id
               || real.GetBlock(new Vector3i(cell.X - 1, cell.Y, cell.Z)).Value == id
               || real.GetBlock(new Vector3i(cell.X + 1, cell.Y, cell.Z)).Value == id
               || real.GetBlock(new Vector3i(cell.X, cell.Y, cell.Z - 1)).Value == id
               || real.GetBlock(new Vector3i(cell.X, cell.Y, cell.Z + 1)).Value == id;
    }

    /// <summary>Dissolves the sound block in a cell for a player who is over there: the builder, an ally of the builder or
    /// an admin — and ANYBODY when the block stands in or within <see cref="SoundBodyMargin"/> cells of their own body
    /// (whoever built it: nobody stays walled in). On a refusal <paramref name="refusalKey"/> is
    /// <see cref="SoundNotYoursKey"/> (somebody else's) or <see cref="SoundBlockedKey"/> (no sound block there, or not
    /// over there).</summary>
    internal bool TryDissolveSound(PlayerSession s, Vector3i cell, out string refusalKey)
    {
        var world = _worlds.Active;
        cell = WorldConstants.CanonicalBlock(cell, world.World.Circumference);
        var p = s.State;
        if (!s.OtherSide || s.CurrentLocationId != world.LocationId || !TryGetSoundCell(SoundLayerOf(world), cell, out var found))
        {
            refusalKey = SoundBlockedKey;
            return false;
        }

        if (!p.IsAdmin && found.Owner != p.PlayerId && !AreAllied(found.Owner, p.PlayerId)
            && !InSoundBodyBox(s, cell, SoundBodyMargin))
        {
            refusalKey = SoundNotYoursKey;
            return false;
        }

        refusalKey = string.Empty;
        return DropSoundCell(world, cell);
    }
}
