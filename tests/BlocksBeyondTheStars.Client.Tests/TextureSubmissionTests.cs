// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using BlocksBeyondTheStars.Client.Feedback;
using BlocksBeyondTheStars.Shared.Textures;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The "Submit a texture" report (#1965) and the texture-pack note in ordinary reports (#1964). The submit dialog
/// tells the player — often a child — exactly what is sent; these tests hold the builder to that sentence.
/// </summary>
public sealed class TextureSubmissionTests
{
    private static readonly DateTime Now = new DateTime(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc);

    private static TextureSubmissionForm Form(string key = "campfire") => new TextureSubmissionForm
    {
        Key = key,
        Name = "  Blue fire ",
        Nickname = "Screelit",
        Note = "I made the flames blue.\nHope you like it!",
        PaintedMyself = true,
        GrantsUse = true,
        OldEnoughOrParentsAgreed = true,
    };

    private static byte[] Frame(byte alpha)
    {
        var raw = new byte[TextureTiles.BytesPerFrame];
        for (int i = 0; i < raw.Length; i += 4)
        {
            raw[i] = 10;
            raw[i + 3] = alpha;
        }

        return raw;
    }

    private static FeedbackReport? Build(TextureSubmissionForm form, IReadOnlyList<byte[]>? frames = null, int fps = 0, byte[]? png = null)
        => TextureSubmission.Build(form, frames ?? new[] { Frame(255) }, fps, png ?? new byte[] { 0x89, 0x50, 0x4E, 0x47 },
            "2026.9.12", "reply-key-hash", "session-1", "WindowsPlayer", Now);

    [Fact]
    public void TheBody_IsWhatTheInboxReads_AndNothingTheDialogDidNotName()
    {
        var report = Build(Form(), new[] { Frame(255), Frame(255) }, fps: 8)!;
        using var doc = JsonDocument.Parse(FeedbackUploader.Serialize(report, screenshotJpg: null));
        var root = doc.RootElement;

        // the wire contract with ReportIngest (#1966)
        var inner = root.GetProperty("reportJson");
        Assert.Equal("texture-submission", inner.GetProperty("reportType").GetString());
        Assert.False(inner.TryGetProperty("kind", out _)); // a "kind" would file it under crashes
        var texture = inner.GetProperty("texture");
        Assert.Equal("campfire", texture.GetProperty("key").GetString());
        Assert.Equal("Blue fire", texture.GetProperty("name").GetString());
        Assert.Equal("Screelit", texture.GetProperty("nickname").GetString());
        Assert.Equal(2, texture.GetProperty("frames").GetInt32());
        Assert.Equal(8, texture.GetProperty("fps").GetInt32());
        var consent = texture.GetProperty("consent");
        Assert.True(consent.GetProperty("self").GetBoolean());
        Assert.True(consent.GetProperty("grant").GetBoolean());
        Assert.True(consent.GetProperty("age").GetBoolean());
        Assert.Equal(TextureSubmission.ConsentTextVersion, consent.GetProperty("textVersion").GetInt32());

        var attachments = root.GetProperty("attachments").EnumerateArray().ToArray();
        Assert.Equal(new[] { "image/png", "application/octet-stream" }, attachments.Select(a => a.GetProperty("mimeType").GetString()).ToArray());
        Assert.Equal(2 * TextureTiles.BytesPerFrame, Convert.FromBase64String(attachments[1].GetProperty("base64").GetString()!).Length);

        // "No e-mail, no location" — and no machine facts, no screenshot, no install token
        Assert.Equal(string.Empty, root.GetProperty("email").GetString());
        Assert.Equal(string.Empty, root.GetProperty("playerId").GetString());
        Assert.Equal("Screelit", root.GetProperty("playerName").GetString());
        Assert.Equal("reply-key-hash", root.GetProperty("replyKey").GetString());
        Assert.False(root.TryGetProperty("screenshot", out _));
        Assert.Equal(new[] { "reportType", "texture" }, inner.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal("I made the flames blue. Hope you like it!", root.GetProperty("description").GetString());
    }

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public void WithoutAllThreeConsents_NothingIsBuilt(bool self, bool grant, bool age)
    {
        var form = Form();
        form.PaintedMyself = self;
        form.GrantsUse = grant;
        form.OldEnoughOrParentsAgreed = age;

        Assert.False(form.ConsentComplete);
        Assert.Null(Build(form));
    }

    [Fact]
    public void ATextureTheGameCouldNotLoad_IsNotSent()
    {
        Assert.Null(Build(Form("../stone")));
        Assert.Null(Build(Form(), new[] { new byte[12] }));
        Assert.Null(Build(Form(), new[] { Frame(255), Frame(255) }, fps: 5)); // not an offered speed
        Assert.Null(Build(Form(), Enumerable.Range(0, TextureTiles.MaxFrames + 1).Select(_ => Frame(255)).ToArray(), fps: 8));
    }

    [Fact]
    public void NoName_IsAnAnswer_AndTheNoteIsOptional()
    {
        var form = Form();
        form.Nickname = "   ";
        form.Note = string.Empty;
        form.Name = string.Empty;

        var report = Build(form)!;

        Assert.Equal(string.Empty, report.PlayerName);
        Assert.Equal("Texture: campfire", report.Title);
        Assert.True(report.Description.Length >= 3); // the inbox rejects an empty description
    }

    [Fact]
    public void FreeText_IsCapped()
    {
        var form = Form();
        form.Nickname = new string('n', 200);
        form.Note = new string('x', 5000);

        var report = Build(form)!;

        Assert.Equal(TextureSubmission.MaxNicknameLength, report.PlayerName.Length);
        Assert.Equal(TextureSubmission.MaxNoteLength, report.Description.Length);
    }

    [Fact]
    public void AnOpaqueTile_LeavesTheMachineOpaque_ACutoutKeepsItsHoles()
    {
        var block = Build(Form("stone"), new[] { Frame(0) })!;
        var flower = Build(Form("flora_fern"), new[] { Frame(0) })!;

        byte[] blockRaw = Convert.FromBase64String(block.Attachments!.Last().Base64);
        byte[] flowerRaw = Convert.FromBase64String(flower.Attachments!.Last().Base64);

        Assert.Equal(0, TextureTiles.CountSeeThrough(blockRaw));
        Assert.Equal(TextureTiles.Size * TextureTiles.Size, TextureTiles.CountSeeThrough(flowerRaw));
    }

    [Fact]
    public void AnOrdinaryReport_CarriesNoAttachmentsNode()
    {
        string json = FeedbackUploader.Serialize(new FeedbackReport { Description = "The door eats my hat." }, null);

        Assert.DoesNotContain("attachments", json, StringComparison.Ordinal);
    }

    // ---------------- texture pack info in reports (#1964) ----------------

    [Fact]
    public void AReport_NamesTheReplacedTextures_NotThePixels()
    {
        var info = TexturePackReportInfo.Create(true, new[] { "stone", "bed", "" }, true, new[] { "grass" });
        var reportJson = new Dictionary<string, object>();

        info.WriteTo(reportJson);

        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(reportJson));
        var pack = doc.RootElement.GetProperty("texturePack");
        Assert.True(pack.GetProperty("replaced").GetBoolean());
        Assert.Equal(2, pack.GetProperty("localCount").GetInt32());
        Assert.Equal(new[] { "bed", "stone" }, pack.GetProperty("localKeys").EnumerateArray().Select(e => e.GetString()).ToArray());
        Assert.Equal(new[] { "grass" }, pack.GetProperty("worldKeys").EnumerateArray().Select(e => e.GetString()).ToArray());
    }

    [Fact]
    public void ASwitchedOffLayer_DoesNotCountAsReplaced_AndLongListsAreCut()
    {
        var off = TexturePackReportInfo.Create(false, new[] { "stone" }, false, new[] { "grass" });
        var many = TexturePackReportInfo.Create(true, Enumerable.Range(0, 100).Select(i => "k" + i.ToString("000")), true, null);

        Assert.False(off.AnythingReplaced);
        Assert.False(TexturePackReportInfo.None.AnythingReplaced);
        Assert.Equal(100, many.LocalCount);
        Assert.Equal(TexturePackReportInfo.MaxListedKeys, many.LocalKeys.Count);
        Assert.Equal("k000", many.LocalKeys[0]);
    }
}
