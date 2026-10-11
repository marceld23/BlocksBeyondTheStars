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

    [Fact]
    public void TheUiTeachingFlags_GoOutAsUiTutorialEvent_TheSameOneAtMostTwiceASecond()
    {
        // #2469: tag 298 — the menu, the Codex, a map, a waypoint, VEGA's continue.
        var transport = new RecordingClientTransport();
        using var client = new NetworkClient(transport);
        Assert.True(client.SendUiTutorialEvent(BlocksBeyondTheStars.Shared.Story.GameEventKeys.UiMenuOpenId));
        Assert.False(client.SendUiTutorialEvent(BlocksBeyondTheStars.Shared.Story.GameEventKeys.UiMenuOpenId)); // a menu toggled back and forth
        Assert.True(client.SendUiTutorialEvent(BlocksBeyondTheStars.Shared.Story.GameEventKeys.UiCodexOpenId)); // another flag is its own
        Assert.False(client.SendUiTutorialEvent(string.Empty));

        var sent = transport.Sent.OfType<UiTutorialEvent>().Select(e => e.Id).ToList();
        Assert.Equal(new[] { "menu_open", "codex_open" }, sent);
    }

    [Fact]
    public void ARealClient_IsTaughtByTheTutorialPack_AndItsMenuFlagCompletesTheMenuStep()
    {
        // #2469: a current client joining a world with the basic training gets its first step, and the client's own UI
        // flag (what GameMenu sends when the Tab menu opens) completes the step that waits for it.
        using var h = new ClientServerHarness(BlocksBeyondTheStars.Shared.Content.ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir()));
        var lines = new List<ShipAiLine>();
        h.Client.ShipAiLineReceived += lines.Add;
        h.Join("Kid");
        Assert.NotNull(h.JoinAccepted);
        Assert.True(h.PumpUntil(() => lines.Any(l => l.LineKey == "story.tutorial.b1.walk.start")));
        Assert.Equal("tutorial", lines.Last(l => l.LineKey.Length > 0).PackId);

        foreach (string step in new[] { "b1.1", "b1.2", "b1.4" }) // "I know this" — the Journal's skip
        {
            h.Client.SendStoryPack("tutorial", StoryPackActions.SkipStep, stepId: step);
            h.Tick();
        }

        Assert.True(h.PumpUntil(() => lines.Any(l => l.LineKey == "story.tutorial.b1.menu.start")));
        Assert.True(h.Client.SendUiTutorialEvent(BlocksBeyondTheStars.Shared.Story.GameEventKeys.UiMenuOpenId));
        // The Codex step's line — with VEGA's story on, its premise variant ("I wrote it before the crash").
        Assert.True(h.PumpUntil(() => lines.Any(l => l.LineKey.StartsWith("story.tutorial.b1.codex.start", StringComparison.Ordinal))),
            "lines: " + string.Join(", ", lines.Select(l => l.LineKey + "/" + l.ObjectiveKey)));
        Assert.Contains(lines, l => l.LineKey == "story.tutorial.b1.codex.start_story");
        Assert.Equal("story.tutorial.b1.codex.chip", lines.Last().ObjectiveKey);
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
