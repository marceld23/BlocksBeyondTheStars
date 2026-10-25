// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #2894: the server's intent dispatch became a type-keyed registry (<c>GameServerDispatch.cs</c>) instead of a
/// 155-arm type-pattern <c>switch</c>. These facts pin that no intent lost its handler on the way and that the
/// exact-type lookup still equals the pattern switch (every handled type is a registered, sealed message). The gates
/// in front of the lookup stay covered where they always were:
/// <c>WorldPauseTests.WhileHeld_GameplayIntentsAreDropped_ButTheResumePathStaysLive</c> (pause),
/// <c>AdminObserverTests</c> (observer) and <c>ShipVisitTests.TheGuest_LooksOnly_NoBuildingMiningPaintingStationsOrBioLab</c>
/// (guest).
/// </summary>
public sealed class IntentDispatchRegistryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_dispatch_" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void TheRegistry_HoldsEveryIntentTheSwitchHandled()
    {
        var types = SvGameServer.HandledIntentTypesForTest.ToHashSet();

        Assert.Equal(157, types.Count); // #2972 added DuelIntent
        Assert.Contains(typeof(MoveIntent), types);                    // the switch's first arm
        Assert.Contains(typeof(RequestStoryResolutionIntent), types);  // and its last
        Assert.Contains(typeof(SelectHotbarIntent), types);            // the two arms that had an inline body
        Assert.Contains(typeof(SaveGameIntent), types);
        Assert.DoesNotContain(typeof(JoinRequest), types);             // answered in OnPayload, before Dispatch
        Assert.DoesNotContain(typeof(JoinAccepted), types);            // server → client
    }

    [Fact]
    public void EveryHandledIntent_IsARegisteredSealedMessage()
    {
        // Sealed is what makes the exact-type lookup equal to the former `case X x:` pattern: no subclass can arrive.
        Assert.All(SvGameServer.HandledIntentTypesForTest, t =>
        {
            Assert.True(NetCodec.RegisteredMessageTags.ContainsKey(t), t.Name + " is not registered with NetCodec");
            Assert.True(t.IsSealed, t.Name + " is not sealed");
        });
    }

    [Fact]
    public void TheHotbarSelect_StillClampsAndIgnoresAResync()
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "dispatch"));
        var server = new SvGameServer(
            new ServerConfig
            {
                WorldName = "dispatch",
                Seed = 3,
                AutoSaveIntervalMinutes = 9999,
                PlaceStarterShip = false,
                PlaceSettlements = false,
            },
            ContentLoader.LoadFromDirectory(TestPaths.DataDir()),
            new LoopbackServerTransport(new LoopbackLink()),
            repo);
        server.Start();
        var p = server.AddLocalPlayer("Justus");

        server.HandlePayloadForTest(p.ConnectionId, NetCodec.Encode(new SelectHotbarIntent { Slot = 99 }));
        Assert.Equal(8, p.State.SelectedHotbarSlot); // clamped to the last quick-bar slot

        // A resync still clamps and stores the slot — only the hotbar:select game event is skipped (TutorialPackTests).
        server.HandlePayloadForTest(p.ConnectionId, NetCodec.Encode(new SelectHotbarIntent { Slot = 3, Resync = true }));
        Assert.Equal(3, p.State.SelectedHotbarSlot);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // A locked save file must never fail the test run.
        }
    }
}
