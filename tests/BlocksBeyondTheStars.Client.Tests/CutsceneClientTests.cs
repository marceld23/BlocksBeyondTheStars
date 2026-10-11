// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Story;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #2468 — the cutscene wire seen from the real <see cref="NetworkClient"/> against the real in-process server: a client
/// opts in to <c>cutscenes1</c> (<see cref="NetworkClient.PlaysCutscenes"/>; the Unity client does when its director
/// exists), receives <see cref="CutscenePlay"/> through <see cref="NetworkClient.CutscenePlayReceived"/> (here from the
/// admin preview <c>/cutscene</c>), confirms with <see cref="NetworkClient.SendCutsceneSeen"/> — and the story state then
/// lists the scene for the Story tab's "Watch again" (<see cref="StoryStateMessage.SeenCutscenes"/>), which asks with
/// <see cref="NetworkClient.SendReplayCutscene"/>.
/// </summary>
[Trait("Suite", "ClientCore")]
public sealed class CutsceneClientTests
{
    private static GameContent LoadContent() => ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir());

    [Fact]
    public void PlaysCutscenes_TogglesTheFeatureWord_AndKeepsTheRest()
    {
        using var h = new ClientServerHarness(LoadContent());
        var client = h.Client;
        Assert.False(client.PlaysCutscenes); // a headless client does not announce what it cannot play
        Assert.Equal(new[] { ClientFeatures.Packs1 }, client.Features);

        client.PlaysCutscenes = true;
        client.PlaysCutscenes = true;
        Assert.Equal(new[] { ClientFeatures.Packs1, ClientFeatures.Cutscenes1 }, client.Features);

        client.PlaysCutscenes = false;
        Assert.Equal(new[] { ClientFeatures.Packs1 }, client.Features);
    }

    [Fact]
    public void ADirectorClient_ReceivesTheAdminPreview_ConfirmsIt_AndSeesItListed()
    {
        using var h = new ClientServerHarness(LoadContent());
        h.Client.PlaysCutscenes = true;
        var plays = new List<CutscenePlay>();
        StoryStateMessage? story = null;
        h.Client.CutscenePlayReceived += plays.Add;
        h.Client.StoryStateReceived += m => story = m;
        h.Join("Director");
        Assert.NotNull(h.JoinAccepted);
        string me = h.JoinAccepted!.PlayerId;
        Assert.True(h.Server.Sessions.Values.Single(s => s.State.PlayerId == me).SupportsCutscenes);

        // The world creator is its admin: the preview plays for them alone, as a replay.
        h.Client.SendAdminCommand("cutscene", stringArg: "vega_protocol prologue");
        Assert.True(h.PumpUntil(() => plays.Count > 0));
        var play = Assert.Single(plays);
        Assert.Equal(StoryRegistry.DefaultStoryId, play.PackId);
        Assert.Equal(StoryCutsceneIds.Prologue, play.CutsceneId);
        Assert.True(play.Replay);
        Assert.True(play.HasAnchor);

        // The director's receipt stamps the scene; the next story snapshot lists it for "Watch again".
        Assert.NotNull(story);
        Assert.DoesNotContain("vega_protocol/prologue", story!.SeenCutscenes);
        h.Client.SendCutsceneSeen(StoryRegistry.DefaultStoryId, new[] { StoryCutsceneIds.Prologue }, skipped: true);
        h.Tick();
        var snapshot = h.Server.StoryStateForTest(me);
        Assert.NotNull(snapshot);
        Assert.Contains("vega_protocol/prologue", snapshot!.SeenCutscenes);

        // "Watch again" asks the server, which re-sends the seen scene.
        plays.Clear();
        h.Client.SendReplayCutscene(StoryRegistry.DefaultStoryId, StoryCutsceneIds.Prologue);
        Assert.True(h.PumpUntil(() => plays.Count > 0));
        Assert.True(Assert.Single(plays).Replay);
    }

    [Fact]
    public void AClientWithoutTheDirector_GetsNoScene_AndThePreviewSaysWhy()
    {
        using var h = new ClientServerHarness(LoadContent());
        var plays = new List<CutscenePlay>();
        h.Client.CutscenePlayReceived += plays.Add;
        h.Join("Headless");
        Assert.NotNull(h.JoinAccepted);

        h.Client.SendAdminCommand("cutscene", stringArg: "vega_protocol prologue");
        Assert.True(h.PumpUntil(() => h.Rejections.Count > 0));
        Assert.Contains(h.Rejections, r => r.Reason == "@srv.admin.cutscene_unsupported");
        Assert.Empty(plays);
    }
}
