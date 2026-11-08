// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Sounders;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The Song Spindle (#3352, the Sounders story pack): a placed block that holds one Song Disc and sings it. The use key
/// (<see cref="UseBlockIntent"/>, registered in <see cref="BuildBlockUseHandlers"/>) does what the hand says:
/// <list type="bullet">
/// <item>a disc in the hand goes in (a disc already in it comes back to the player) and the spindle sings it;</item>
/// <item>an empty hand (or any other item) plays the disc again; crouching takes it out;</item>
/// <item>an empty spindle says it is waiting for a disc.</item>
/// </list>
/// The disc in a spindle is a one-slot container record (<see cref="SpindleContainerKind"/>) in the world's container store —
/// persisted with the world like a crate, but never sent to the clients (they have nothing to loot there). Mining or blasting
/// the spindle drops its disc on the ground (<see cref="OnSongSpindleRemoved"/>).
/// <para>Singing is a <see cref="NoteSequence"/> of the disc's phrase in the note alphabet with the Sounder voice
/// (<see cref="SongDiscs.NotesOf"/>; the collectible disc sings a seeded song of the spindle's place) and the
/// <c>spindle_spin</c> sound. A story disc the player has not decoded yet opens the note-key minigame for a player whose
/// Sounders pack is active (<see cref="MaybeOfferNoteKey"/>, the result in <see cref="HandleStoryMinigameResult"/>); a disc
/// that is no story disc, sung while a decode step runs, names the disc the story waits for (#3425, <see cref="HintStoryDisc"/>).</para>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The container kind of a Song Spindle's disc record — never broadcast (<see cref="IsHiddenContainer"/>).</summary>
    internal const string SpindleContainerKind = "song_spindle";

    /// <summary>Seconds per note of a disc's song.</summary>
    internal const float SpindleNoteSeconds = 0.3f;

    /// <summary>A spindle sings once at a time: a play while it still sings is ignored.</summary>
    private readonly Dictionary<string, double> _spindleBusyUntil = new(System.StringComparer.Ordinal);

    /// <summary>Whether a container record is one the clients never see (a Song Spindle's disc).</summary>
    internal static bool IsHiddenContainer(StoredContainer c) => c.Kind == SpindleContainerKind;

    /// <summary>The use key on a Song Spindle (registered in <see cref="BuildBlockUseHandlers"/>).</summary>
    internal void UseSongSpindle(PlayerSession session, Vector3i cell)
    {
        var p = session.State;
        cell = WorldConstants.CanonicalBlock(cell, _world.Circumference);
        string held = ItemKey.Base(HeldItemKey(p));
        var record = SpindleRecordAt(cell);
        if (SongDiscs.IsDisc(held))
        {
            InsertSpindleDisc(session, cell, record, held);
            return;
        }

        string disc = SpindleDiscOf(record);
        if (disc.Length == 0)
        {
            Send(session, new ServerMessage { Text = "@srv.spindle.empty" });
            return;
        }

        if (p.Crouching)
        {
            EjectSpindleDisc(session, cell, record!);
            return;
        }

        PlaySpindle(cell, disc);
        OfferNoteKeyOrHint(session, disc);
    }

    /// <summary>A story disc still to decode opens the note-key minigame; any other disc, sung while a decode step runs, gets
    /// the hint which disc the story waits for (#3425).</summary>
    private void OfferNoteKeyOrHint(PlayerSession session, string disc)
    {
        if (!MaybeOfferNoteKey(session, disc))
        {
            HintStoryDisc(session, disc);
        }
    }

    /// <summary>The disc in the spindle at <paramref name="cell"/>, or "" (test seam and the story's view).</summary>
    internal string SpindleDiscAt(Vector3i cell)
        => SpindleDiscOf(SpindleRecordAt(WorldConstants.CanonicalBlock(cell, _world.Circumference)));

    private StoredContainer? SpindleRecordAt(Vector3i cell)
        => _containers.FirstOrDefault(c => c.Kind == SpindleContainerKind && c.Position.Equals(cell));

    private static string SpindleDiscOf(StoredContainer? record)
        => record?.Items.FirstOrDefault(s => !s.IsEmpty)?.Item ?? string.Empty;

    private string SpindleId(Vector3i cell) => $"spindle_{_world.LocationId}_{cell.X}_{cell.Y}_{cell.Z}";

    /// <summary>Puts the held disc into the spindle (the disc that was in it comes back) and lets it sing.</summary>
    private void InsertSpindleDisc(PlayerSession session, Vector3i cell, StoredContainer? record, string disc)
    {
        var p = session.State;
        if (!p.Inventory.Remove(disc, 1))
        {
            return;
        }

        string previous = SpindleDiscOf(record);
        if (record is null)
        {
            record = new StoredContainer
            {
                Id = SpindleId(cell),
                Planet = _world.LocationId,
                Kind = SpindleContainerKind,
                Position = cell,
            };
            _containers.Add(record);
        }

        record.Items = new List<ItemStack> { new(disc, 1) };
        _repo.SaveContainer(record);
        if (previous.Length > 0)
        {
            GiveBackDisc(session, previous, cell);
        }

        SendInventory(session);
        BroadcastToWorld(new SoundFx { SoundId = "disc_insert", X = cell.X + 0.5f, Y = cell.Y + 0.8f, Z = cell.Z + 0.5f });
        _spindleBusyUntil.Remove(SpindleId(cell)); // a new disc always starts singing
        PlaySpindle(cell, disc);
        OfferNoteKeyOrHint(session, disc);
    }

    /// <summary>Crouch + use: the disc comes back to the player and the spindle record goes.</summary>
    private void EjectSpindleDisc(PlayerSession session, Vector3i cell, StoredContainer record)
    {
        string disc = SpindleDiscOf(record);
        _containers.Remove(record);
        _repo.DeleteContainer(record.Id);
        _spindleBusyUntil.Remove(record.Id);
        GiveBackDisc(session, disc, cell);
        SendInventory(session);
    }

    /// <summary>A disc back into the player's pack — or onto the ground at the spindle when the pack is full.</summary>
    private void GiveBackDisc(PlayerSession session, string disc, Vector3i cell)
    {
        int left = session.State.Inventory.Add(disc, 1, _content.MaxStackOf(disc));
        if (left > 0)
        {
            SpillToGround(cell, disc, left);
            NotifyDropped(session);
        }
    }

    /// <summary>The spindle sings its disc for everyone near it (once at a time).</summary>
    private void PlaySpindle(Vector3i cell, string disc)
    {
        string id = SpindleId(cell);
        if (_spindleBusyUntil.TryGetValue(id, out double until) && _uptime < until)
        {
            return;
        }

        int[] notes = SpindleNotes(cell, disc);
        if (notes.Length == 0)
        {
            return;
        }

        _spindleBusyUntil[id] = _uptime + (notes.Length * SpindleNoteSeconds);
        var at = new Vector3f(cell.X + 0.5f, cell.Y + 0.8f, cell.Z + 0.5f);
        BroadcastToWorld(new SoundFx { SoundId = "spindle_spin", X = at.X, Y = at.Y, Z = at.Z });
        PlayNoteSequence(at, notes, SoundersIds.InstrumentVoice, SpindleNoteSeconds, withFifth: false, source: SoundersIds.SongSpindle);
    }

    /// <summary>What a disc sings on the spindle at <paramref name="cell"/>: its phrase, or for the collectible disc the
    /// seeded song of this place.</summary>
    internal int[] SpindleNotes(Vector3i cell, string disc)
        => disc == SoundersIds.DiscSong
            ? SounderAlphabet.Encode(string.Join(" ", SongDiscs.CollectibleWords(_meta.Seed, cell.X, cell.Y, cell.Z)))
            : SongDiscs.NotesOf(disc);

    /// <summary>The spindle block went (mined, blasted): its disc lands on the ground at the cell — nothing the player put
    /// in is ever destroyed. Called from <see cref="ForgetBlockRegistrations"/>.</summary>
    private void OnSongSpindleRemoved(Vector3i pos)
    {
        var cell = WorldConstants.CanonicalBlock(pos, _world.Circumference);
        if (SpindleRecordAt(cell) is not { } record)
        {
            return;
        }

        string disc = SpindleDiscOf(record);
        _containers.Remove(record);
        _repo.DeleteContainer(record.Id);
        _spindleBusyUntil.Remove(record.Id);
        if (disc.Length > 0)
        {
            SpillToGround(cell, disc, 1);
        }
    }

    // ---------------- Handing out Sounders items ----------------

    /// <summary>
    /// #3352: whether the Sounders story pack runs for this player (started, installed, not switched off) — the gate of the
    /// story loot and the note-key minigame.
    /// </summary>
    internal bool SoundersActive(PlayerSession session) => IsStoryPackActive(session, SoundersIds.PackId);

    /// <summary>
    /// #3352: hands <paramref name="count"/> of a Sounders item to the player — the backpack first, aboard the hold, the rest
    /// on the ground at their feet (never lost). A story disc is recorded as handed out (<see cref="SongDiscs.GotMilestone"/>),
    /// so the story loot never gives it twice. The story (the Singing Shrine's reward: disc 1 and the first Song Spindle) calls
    /// this.
    /// </summary>
    internal void GiveSoundersItem(PlayerSession session, string itemKey, int count)
    {
        if (count <= 0 || _content.GetItem(itemKey) is null)
        {
            return;
        }

        var pool = new MaterialPool(_content, session.State, _ship);
        pool.Add(itemKey, count);
        if (SongDiscs.IsStoryDisc(itemKey))
        {
            session.State.Milestones.Add(SongDiscs.GotMilestone(itemKey));
        }

        SendInventory(session);
        SpillPoolOverflow(session, pool, session.State.Position.ToBlock());
    }
}
