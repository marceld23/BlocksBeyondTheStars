// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.Story;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3832 — what a player does with the body became game events, so a playtest task (and a story step) can wait for
/// it: lifting and throwing a block (<c>carry:lifted</c>, <c>throw:any</c>, <c>throw:hit</c>), an emote from the wheel
/// (<c>emote:played</c>), and falling and being brought back in a Hard Survival world (<c>player:fallen</c>,
/// <c>player:revived</c>). Each is raised once for the thing that really happened: a refused lift, a throw inside the
/// gap, an emote inside its pace and a body state of the server's own say nothing.
/// </summary>
public sealed class PlayerActionEventTests : IDisposable
{
    private const int Cx = 300, Cz = 300;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_actions_" + Guid.NewGuid().ToString("N"));
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    public void Dispose() => SoundersStage.Delete(_root);

    private (SvGameServer Server, SqliteWorldRepository Repo, int PadY, RecordingGameEventListener Heard) World(
        string name, DeathPenalty penalty = DeathPenalty.Revive)
    {
        var server = SoundersStage.Started(_root, name, _content, out var repo, c => c.Rules.DeathPenalty = penalty);
        SoundersStage.Pacify(server);
        int padY = SoundersStage.Pad(server, _content, Cx, Cz, 20);
        var heard = new RecordingGameEventListener();
        foreach (string pattern in new[] { "carry:*", "throw:*", "emote:*", "player:*" })
        {
            server.AddGameEventListener(pattern, heard);
        }

        return (server, repo, padY, heard);
    }

    private static PlayerSession Player(SvGameServer server, string name, int padY, float dx)
    {
        var p = SoundersStage.OnFoot(server, name, new Vector3f(Cx + 0.5f + dx, padY + 1, Cz + 0.5f));
        p.State.RespawnPoint = new Vector3f(Cx + 0.5f, padY + 1, Cz + 15.5f); // "the ship"
        p.State.Yaw = 90f;                                                    // facing east (+X)
        return p;
    }

    /// <summary>A block item of medium weight and the block it places (items and blocks are data).</summary>
    private (string Item, string Block) MediumBlock()
    {
        var item = _content.Items.Values
            .Where(i => i.PlacesBlock is { Length: > 0 } b && _content.GetBlock(b) is { } block
                        && BlockThrow.WeightOf(block.Hardness) == BlockWeight.Medium)
            .OrderBy(i => i.Key, StringComparer.Ordinal)
            .First();
        return (item.Key, item.PlacesBlock!);
    }

    [Fact]
    public void LiftingAndThrowing_NameTheBlock_AndAHitNamesWhatWasHit()
    {
        var (item, block) = MediumBlock();
        var (server, repo, padY, heard) = World("throw_events");
        using (repo)
        {
            var p = Player(server, "Kid", padY, 0f);
            string id = p.State.PlayerId;
            p.State.Inventory.SetSlot(0, new ItemStack(item, 4));
            p.State.SelectedHotbarSlot = 0;
            server.TickForTest(0.1);

            // An empty slot lifts nothing.
            server.SelectHotbarForTest(id, 1);
            server.CarryBlockForTest(id, true);
            Assert.Empty(heard.Events);

            server.SelectHotbarForTest(id, 0);
            server.CarryBlockForTest(id, true);
            server.CarryBlockForTest(id, true); // the wish repeated while the block is up: still one lift
            Assert.Equal((id, GameEventKeys.CarryLifted, block, 1), Assert.Single(heard.Events));

            // A throw into the open: thrown, nothing hit.
            server.ThrowBlockForTest(id, 1f, 0.2f, 0f);
            Assert.Equal(block, Assert.Single(heard.Of(GameEventKeys.ThrowAny)).Arg);
            Assert.Empty(heard.Of(GameEventKeys.ThrowHit));

            // Inside the throw gap the next one is refused: lifted again, but not thrown.
            server.CarryBlockForTest(id, true);
            server.ThrowBlockForTest(id, 1f, 0.2f, 0f);
            Assert.Single(heard.Of(GameEventKeys.ThrowAny));

            // A machine in the way.
            server.TickForTest(BlockThrow.ThrowGapSeconds + 0.1);
            server.SpawnPlanetEnemyAtForTest(new Vector3f(Cx + 5.5f, padY + 1, Cz + 0.5f), damagePerSecond: 0f);
            server.CarryBlockForTest(id, true);
            server.ThrowBlockForTest(id, 1f, 0f, 0f);
            Assert.Equal(2, heard.Of(GameEventKeys.ThrowAny).Count());
            Assert.Equal(GameEventKeys.DefeatMachine, Assert.Single(heard.Of(GameEventKeys.ThrowHit)).Arg);
        }
    }

    [Fact]
    public void AnEmoteFromTheWheel_IsAnEvent_ThePaceAndTheBodyStatesAreNot()
    {
        var (server, repo, padY, heard) = World("emote_events");
        using (repo)
        {
            var p = Player(server, "Kid", padY, 0f);
            string id = p.State.PlayerId;

            server.EmoteForTest(id, PlayerEmotes.Dance);
            server.EmoteForTest(id, PlayerEmotes.Wave);  // inside the pace: dropped
            server.EmoteForTest(id, PlayerEmotes.Hurt);  // a body state is the server's to say
            Assert.Equal((id, GameEventKeys.EmotePlayed, "dance", 1), Assert.Single(heard.Events));

            server.TickForTest(SvGameServer.EmoteCooldownSeconds + 0.2);
            server.EmoteForTest(id, PlayerEmotes.Sit);
            server.TickForTest(SvGameServer.EmoteCooldownSeconds + 0.2);
            server.EmoteForTest(id, PlayerEmotes.Stand); // getting up ends an emote, it is none
            Assert.Equal(new[] { "dance", "sit" }, heard.Of(GameEventKeys.EmotePlayed).Select(e => e.Arg));
        }
    }

    [Fact]
    public void EveryEmoteOfTheWheel_HasAWord()
    {
        var words = new HashSet<string>(StringComparer.Ordinal);
        for (byte emote = 1; emote <= PlayerEmotes.LastPlayable; emote++)
        {
            string? word = SvGameServer.EmoteEventArg(emote);
            Assert.False(string.IsNullOrEmpty(word), $"emote {emote} has no event word");
            Assert.True(words.Add(word!), $"the word '{word}' names two emotes");
        }

        Assert.Null(SvGameServer.EmoteEventArg(PlayerEmotes.None));
        Assert.Null(SvGameServer.EmoteEventArg(PlayerEmotes.Stand));
        Assert.Null(SvGameServer.EmoteEventArg(PlayerEmotes.Down));
    }

    [Fact]
    public void InHardSurvival_AFallIsAnEvent_AndTheRestartAtTheShipIsTheRespawn()
    {
        var (server, repo, padY, heard) = World("fall_events");
        using (repo)
        {
            var a = Player(server, "Kid", padY, 0f);
            string id = a.State.PlayerId;

            server.KillPlayerForTest(id);
            Assert.True(server.IsFallenForTest(id));
            Assert.Equal((id, GameEventKeys.PlayerFallen, string.Empty, 1), Assert.Single(heard.Events));

            for (int i = 0; i < 61; i++)
            {
                server.TickForTest(1.0);
            }

            server.RestartFallenForTest(id);
            Assert.False(server.IsFallenForTest(id));
            Assert.Single(heard.Of(GameEventKeys.PlayerRespawned)); // what a playtest step about the restart waits for
            Assert.Single(heard.Of(GameEventKeys.PlayerFallen));
            Assert.Empty(heard.Of(GameEventKeys.PlayerRevived));
        }
    }

    [Fact]
    public void InAnyOtherWorld_ADeathIsARespawn_NotAFall()
    {
        var (server, repo, padY, heard) = World("no_fall_events", DeathPenalty.Light);
        using (repo)
        {
            var a = Player(server, "Kid", padY, 0f);
            server.KillPlayerForTest(a.State.PlayerId);

            Assert.Empty(heard.Of(GameEventKeys.PlayerFallen));
            Assert.Single(heard.Of(GameEventKeys.PlayerRespawned));
        }
    }

    [Fact]
    public void AMedkit_RaisesTheRevival_ForTheOneWhoStoodUp_AndForTheMedic()
    {
        var (server, repo, padY, heard) = World("revive_events");
        using (repo)
        {
            var a = Player(server, "Kid", padY, 0f);
            var b = Player(server, "Friend", padY, 3f);
            server.KillPlayerForTest(a.State.PlayerId);
            server.MedkitPulseForTest(b.State.PlayerId);

            Assert.False(server.IsFallenForTest(a.State.PlayerId));
            var revived = heard.Of(GameEventKeys.PlayerRevived).ToList();
            Assert.Equal(2, revived.Count);
            Assert.Contains(revived, e => e.Player == a.State.PlayerId && e.Arg == GameEventKeys.RevivedFallen);
            Assert.Contains(revived, e => e.Player == b.State.PlayerId && e.Arg == GameEventKeys.RevivedMedic);
            Assert.Empty(heard.Of(GameEventKeys.PlayerRespawned)); // standing up where they fell is no respawn
        }
    }

    [Fact]
    public void TheNewKeys_ArePublished_SoAPlaytestMayWatchThem()
    {
        foreach (string key in new[]
                 {
                     GameEventKeys.ShipRolled, GameEventKeys.ShipBoosted, GameEventKeys.ShipEvaded, GameEventKeys.CarryLifted,
                     GameEventKeys.ThrowAny, GameEventKeys.ThrowHit, GameEventKeys.EmotePlayed, GameEventKeys.PlayerFallen,
                     GameEventKeys.PlayerRevived,
                 })
        {
            Assert.Contains(key, GameEventKeys.All);
        }
    }
}
