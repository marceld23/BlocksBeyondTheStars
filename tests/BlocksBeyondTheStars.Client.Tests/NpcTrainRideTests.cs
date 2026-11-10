// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Buffers;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using MessagePack;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3445: the client's rules for drawing an NPC passenger — a snapshot with a train frame is aboard (an older server's,
/// without one, is on foot), a changed frame is a snap (boarding, stepping off, another wagon), the passenger's facing turns
/// with its wagon, and the frame fields survive the wire (an older payload without them reads as on foot).
/// </summary>
public sealed class NpcTrainRideTests
{
    [Fact]
    public void AFrame_IsAboard_AndChangingItIsASnap()
    {
        string frame = RailRules.FrameId("train-rocky-1", 1);
        Assert.True(NpcTrainRide.Aboard(frame));
        Assert.False(NpcTrainRide.Aboard(string.Empty));
        Assert.False(NpcTrainRide.Aboard(null));

        Assert.True(NpcTrainRide.FrameChanged(string.Empty, frame));                          // boarded
        Assert.True(NpcTrainRide.FrameChanged(frame, string.Empty));                          // stepped off
        Assert.True(NpcTrainRide.FrameChanged(frame, RailRules.FrameId("train-rocky-1", 2))); // moved wagons
        Assert.False(NpcTrainRide.FrameChanged(frame, frame));
        Assert.False(NpcTrainRide.FrameChanged(null, string.Empty));
    }

    [Fact]
    public void ThePassengersFacing_TurnsWithItsWagon()
    {
        Assert.Equal(90f, NpcTrainRide.DrawnYawDegrees(90f, 0f), 3);                      // toward the cab
        Assert.Equal(180f, NpcTrainRide.DrawnYawDegrees(90f, (float)(Math.PI / 2)), 3);   // across to the wagon's right
        Assert.Equal(270f, NpcTrainRide.DrawnYawDegrees(0f, (float)(-Math.PI / 2)), 3);   // across to its left, wrapped
    }

    [Fact]
    public void TheFrameFields_TravelOnTheWire_AndAnOlderPayloadIsOnFoot()
    {
        var npc = new NetNpc { Id = 7, Name = "Tomas", FrameId = RailRules.FrameId("train-rocky-1", 1), LocalX = -0.9f, LocalZ = 1.6f, LocalYaw = 1.5f, Pose = 1 };
        var back = Assert.IsType<NpcList>(NetCodec.Decode(NetCodec.Encode(new NpcList { Npcs = new[] { npc } }))).Npcs[0];
        Assert.Equal(npc.FrameId, back.FrameId);
        Assert.Equal(-0.9f, back.LocalX, 3);
        Assert.Equal(1.6f, back.LocalZ, 3);
        Assert.Equal(1.5f, back.LocalYaw, 3);

        // An older server's list: its NPC map has no frame keys at all (contractless MessagePack writes every key it knows,
        // so even a default NetNpc carries them) — it reads as on foot, everything else intact.
        var body = new ArrayBufferWriter<byte>();
        var w = new MessagePackWriter(body);
        w.WriteMapHeader(1);
        w.Write(nameof(NpcList.Npcs));
        w.WriteArrayHeader(1);
        w.WriteMapHeader(5);
        w.Write(nameof(NetNpc.Id));
        w.Write(8);
        w.Write(nameof(NetNpc.Name));
        w.Write("Tomas");
        w.Write(nameof(NetNpc.X));
        w.Write(12.5f);
        w.Write(nameof(NetNpc.Z));
        w.Write(-3f);
        w.Write(nameof(NetNpc.Pose));
        w.Write(1);
        w.Flush();
        var old = Assert.IsType<NpcList>(NetCodec.Decode(OlderPayload(new NpcList(), body))).Npcs[0];
        Assert.Equal((8, "Tomas", 12.5f, -3f, (byte)1), (old.Id, old.Name, old.X, old.Z, old.Pose));
        Assert.Equal(string.Empty, old.FrameId);
        Assert.False(NpcTrainRide.Aboard(old.FrameId));
        Assert.Equal((0f, 0f, 0f, 0f), (old.LocalX, old.LocalY, old.LocalZ, old.LocalYaw));
    }

    [Fact]
    public void TheTrainSwitches_ReadTheirDefaults_FromAnOlderPeer()
    {
        // An older client's cab panel sends no Passengers key: −1, nothing changes.
        var body = new ArrayBufferWriter<byte>();
        var w = new MessagePackWriter(body);
        w.WriteMapHeader(2);
        w.Write(nameof(SetTrainIntent.TrainId));
        w.Write("train-rocky-1");
        w.Write(nameof(SetTrainIntent.Speed));
        w.Write(3);
        w.Flush();
        var set = Assert.IsType<SetTrainIntent>(NetCodec.Decode(OlderPayload(new SetTrainIntent(), body)));
        Assert.Equal(("train-rocky-1", 3, -1, -1, -1), (set.TrainId, set.Speed, set.Halt, set.Autopilot, set.Passengers));

        // An older server's train list sends no TakesPassengers key: villagers may ride.
        body = new ArrayBufferWriter<byte>();
        w = new MessagePackWriter(body);
        w.WriteMapHeader(1);
        w.Write(nameof(TrainList.Trains));
        w.WriteArrayHeader(1);
        w.WriteMapHeader(2);
        w.Write(nameof(NetTrain.Id));
        w.Write("train-rocky-1");
        w.Write(nameof(NetTrain.Speed));
        w.Write(2);
        w.Flush();
        var train = Assert.Single(Assert.IsType<TrainList>(NetCodec.Decode(OlderPayload(new TrainList(), body))).Trains);
        Assert.Equal(("train-rocky-1", 2), (train.Id, train.Speed));
        Assert.True(train.TakesPassengers);
    }

    /// <summary>A payload of <paramref name="sample"/>'s message type with a hand-written MessagePack body — the shape an older
    /// peer sends, with only the keys it knew. The frame head (the type tag: one byte, or three for an extended id) is the
    /// codec's own.</summary>
    private static byte[] OlderPayload(object sample, ArrayBufferWriter<byte> body)
    {
        byte[] encoded = NetCodec.Encode(sample);
        int head = encoded[0] == NetCodec.ExtendedTag ? 3 : 1;
        var payload = new byte[head + body.WrittenCount];
        Array.Copy(encoded, payload, head);
        body.WrittenSpan.CopyTo(payload.AsSpan(head));
        return payload;
    }
}
