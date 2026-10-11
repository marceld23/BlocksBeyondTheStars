// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Shared.Localization;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #2466 — the client's half of the player-pack runtime so far: the join announces <c>packs1</c> (a server then sends
/// pack lines instead of the legacy tutorial mode; <c>cutscenes1</c> only with the cutscene director, #2468), the pack controls
/// go out as <see cref="StoryPackIntent"/>, and <see cref="VegaText"/> composes through the one shared implementation
/// (<see cref="LineText"/>) the server uses for older clients.
/// </summary>
[Trait("Suite", "ClientCore")]
public sealed class StoryPackClientTests
{
    private sealed class RecordingClientTransport : IClientTransport
    {
        public event Action? Connected;
        public event Action? Disconnected;
        public event Action<byte[]>? PayloadReceived;

        public List<object?> Sent { get; } = new();

        public void Connect(string host, int port) => Connected?.Invoke();

        public void Send(byte[] payload, DeliveryMode mode) => Sent.Add(NetCodec.Decode(payload));

        public void Poll()
        {
            _ = Disconnected;
            _ = PayloadReceived;
        }

        public void Disconnect()
        {
        }

        public void Dispose()
        {
        }
    }

    [Fact]
    public void TheJoin_AnnouncesPacks1_AndCutscenes1OnlyForAClientThatPlaysThem()
    {
        var transport = new RecordingClientTransport();
        using var client = new NetworkClient(transport) { ContentFingerprint = "palette-a" };
        client.Join("Pilot");

        var join = Assert.IsType<JoinRequest>(Assert.Single(transport.Sent));
        Assert.Contains(ClientFeatures.Packs1, join.Features);
        Assert.DoesNotContain(ClientFeatures.Cutscenes1, join.Features); // a headless client has no director (#2468)

        client.PlaysCutscenes = true; // what the Unity client does when its cutscene director exists
        client.Join("Pilot");
        var director = Assert.IsType<JoinRequest>(transport.Sent.Last());
        Assert.Equal(new[] { ClientFeatures.Packs1, ClientFeatures.Cutscenes1 }, director.Features);
    }

    [Fact]
    public void AHost_CanPlayAnOlderClient()
    {
        var transport = new RecordingClientTransport();
        using var client = new NetworkClient(transport) { ContentFingerprint = "palette-a", Features = Array.Empty<string>() };
        client.Join("Pilot");
        Assert.Empty(Assert.IsType<JoinRequest>(Assert.Single(transport.Sent)).Features);
    }

    [Fact]
    public void ThePackControls_GoOutAsStoryPackIntent()
    {
        var transport = new RecordingClientTransport();
        using var client = new NetworkClient(transport);
        client.SendStoryPack("tutorial", StoryPackActions.SkipStep, "b3", "b3.2");

        var intent = Assert.IsType<StoryPackIntent>(Assert.Single(transport.Sent));
        Assert.Equal("tutorial", intent.PackId);
        Assert.Equal(StoryPackActions.SkipStep, intent.Action);
        Assert.Equal("b3", intent.ChapterId);
        Assert.Equal("b3.2", intent.StepId);
    }

    [Theory]
    [InlineData("Press {key:Interact} at {0}", "the cockpit")]
    [InlineData("The wreck {0} drifts. Map: {key:FlightMap}", null)]
    [InlineData("No slot, no token", "ignored")]
    public void VegaText_ComposesLikeTheSharedImplementation(string text, string? arg)
    {
        static string Glyph(string action) => action == "Interact" ? "E" : "M";
        Assert.Equal(LineText.Compose(text, arg, Glyph), VegaText.Compose(text, arg, Glyph));
        Assert.Equal(LineText.ExpandKeyTokens(text, Glyph), VegaText.ExpandKeyTokens(text, Glyph));
        Assert.Equal(LineText.ArgSeparator, VegaText.ArgSeparator);
    }
}
