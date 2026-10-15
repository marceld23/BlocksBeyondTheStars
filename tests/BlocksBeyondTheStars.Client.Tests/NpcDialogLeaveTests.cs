// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #2616 — the client half of the NPC conversation's life: closing the dialogue panel while it still offers replies tells
/// the server ("leave" = <see cref="NpcDialogChoiceIntent.Leave"/> on the existing choice intent, with the NPC's id — no
/// new intent, no new codec tag), every reply names its NPC, and the server's own close
/// (<see cref="NpcDialogState.ActionClose"/>) takes the panel down WITHOUT a leave going back. The Unity panel is guarded
/// as source text (the Unity scripts cannot load headless — the UI wiring guards' pattern).
/// </summary>
[Trait("Suite", "ClientCore")]
public sealed class NpcDialogLeaveTests
{
    private static string Script(string file)
        => File.ReadAllText(Path.Combine(ClientTestPaths.RepoRoot(), "client", "Assets", "BlocksBeyondTheStars", "Scripts", file));

    /// <summary>An in-memory client transport that records what the client sends.</summary>
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
        }

        public void Disconnect() => Disconnected?.Invoke();

        public void Dispose()
        {
        }

        public void Deliver(object message) => PayloadReceived?.Invoke(NetCodec.Encode(message));
    }

    [Fact]
    public void TheLeave_AndEveryReply_RideTheChoiceIntent_WithTheNpcsId()
    {
        var transport = new RecordingClientTransport();
        using var client = new NetworkClient(transport);
        client.Connect("server", 0);

        client.SendNpcDialogChoice(1, 42);
        client.SendNpcDialogLeave(42);

        var sent = transport.Sent.OfType<NpcDialogChoiceIntent>().ToList();
        Assert.Equal(2, sent.Count);
        Assert.Equal((1, 42), (sent[0].ChoiceIndex, sent[0].NpcId));
        Assert.Equal((NpcDialogChoiceIntent.Leave, 42), (sent[1].ChoiceIndex, sent[1].NpcId));
        Assert.True(NpcDialogChoiceIntent.Leave < 0, "every released server drops the dialogue on a negative index");
    }

    [Fact]
    public void TheUnityClient_AnnouncesThatItsPanelTakesTheClose()
    {
        using var client = new NetworkClient(new RecordingClientTransport());
        Assert.False(client.ClosesDialogs); // a headless client keeps the list it always had
        client.ClosesDialogs = true;
        Assert.Contains(ClientFeatures.DialogClose1, client.Features);
        client.ClosesDialogs = false;
        Assert.DoesNotContain(ClientFeatures.DialogClose1, client.Features);

        Assert.Contains("Network.ClosesDialogs = true;", Script("GameBootstrap.cs"));
    }

    [Fact]
    public void TheServersClose_ReachesThePanel()
    {
        var transport = new RecordingClientTransport();
        using var client = new NetworkClient(transport);
        client.Connect("server", 0);
        NpcDialogState? got = null;
        client.NpcDialogReceived += m => got = m;

        transport.Deliver(new NpcDialogState { NpcId = 5, Text = "Bye!", End = true, Action = NpcDialogState.ActionClose });
        client.Poll();

        Assert.NotNull(got);
        Assert.Equal(NpcDialogState.ActionClose, got!.Action);
        Assert.Equal(5, got.NpcId);
    }

    [Fact]
    public void ThePanel_SendsTheLeaveOnClose_ButNotOnTheServersClose()
    {
        string src = Script("NpcDialogUi.cs");

        // Close() tells the server — only while the panel still offered replies (a closing line has no conversation left).
        int close = src.IndexOf("private void Close()", StringComparison.Ordinal);
        int hide = src.IndexOf("private void Hide()", StringComparison.Ordinal);
        Assert.True(close > 0 && hide > close, "Close() and Hide() are missing");
        string closeBody = src.Substring(close, hide - close);
        Assert.Contains("SendNpcDialogLeave(_state.NpcId)", closeBody);
        Assert.Contains("_state is { End: false }", closeBody);
        Assert.Contains("Hide();", closeBody);

        // The server's close only hides the panel: no leave, no Close().
        int onDialog = src.IndexOf("private void OnDialog(NpcDialogState m)", StringComparison.Ordinal);
        int closeBranch = src.IndexOf("m.Action == NpcDialogState.ActionClose", onDialog, StringComparison.Ordinal);
        int voice = src.IndexOf("bool opening", onDialog, StringComparison.Ordinal);
        Assert.True(onDialog > 0 && closeBranch > onDialog && voice > closeBranch, "the close branch must come first in OnDialog");
        string closeHandling = src.Substring(closeBranch, voice - closeBranch);
        Assert.Contains("Hide();", closeHandling);
        Assert.DoesNotContain("SendNpcDialogLeave", closeHandling);
        Assert.DoesNotContain("Close();", closeHandling);
        int first = src.IndexOf("SendNpcDialogLeave(", StringComparison.Ordinal);
        Assert.True(first > 0 && src.IndexOf("SendNpcDialogLeave(", first + 1, StringComparison.Ordinal) < 0,
            "the leave is sent from exactly one place — Close()");

        // Every reply names the NPC whose panel shows it.
        Assert.Contains("SendNpcDialogChoice(index, _state.NpcId)", src);
    }
}
