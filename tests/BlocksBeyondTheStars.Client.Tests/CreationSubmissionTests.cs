// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using BlocksBeyondTheStars.Client.Feedback;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// Ships, station/town modules and avatar skins submitted from the editors (#3262). The submit dialog tells the
/// player — often a child — exactly what is sent; these tests hold the builder to that sentence, to the inbox's wire
/// contract (category from <c>reportType</c>, never a top-level <c>kind</c>) and to the size caps.
/// </summary>
public sealed class CreationSubmissionTests
{
    private static readonly DateTime Now = new DateTime(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc);
    private static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3 };

    private static CreationSubmissionForm Form(string kind = CreationSubmission.KindShip) => new CreationSubmissionForm
    {
        Kind = kind,
        Key = "star_hopper",
        Name = "  Star Hopper ",
        Nickname = "Layex",
        Note = "It has two engines.\nAnd a bed!",
        MadeMyself = true,
        GrantsUse = true,
        OldEnoughOrParentsAgreed = true,
    };

    private static CreationPayload Ship(int cells = 3) => new CreationPayload { CellCount = cells, BlockFingerprint = "00ff00ff00ff00ff", BasedOn = "hammerhead" }
        .Add("ship.json", "{\n  \"key\": \"star_hopper\",\n  \"name\": \"Star Hopper\",\n  \"description\": \"Fast.\"\n}")
        .Add("layout.json", "{ \"width\": 5, \"cells\": [ { \"x\": 0, \"id\": \"steel_wall\" } ] }");

    private static CreationSubmissionResult Build(CreationSubmissionForm form, CreationPayload? payload = null, byte[]? png = null)
        => CreationSubmission.Build(form, payload ?? Ship(), png ?? Png, "2026.11.6", "reply-key-hash", "session-1", "WindowsPlayer", Now);

    private static JsonElement Body(FeedbackReport report)
        => JsonDocument.Parse(FeedbackUploader.Serialize(report, screenshotJpg: null)).RootElement;

    [Fact]
    public void TheBody_IsWhatTheInboxReads_AndNothingTheDialogDidNotName()
    {
        var result = Build(Form());
        Assert.Equal(CreationRefusal.None, result.Refusal);
        var root = Body(result.Report!);

        var inner = root.GetProperty("reportJson");
        Assert.Equal("ship-submission", inner.GetProperty("reportType").GetString());
        Assert.False(inner.TryGetProperty("kind", out _)); // a top-level "kind" would file it under crashes
        Assert.Equal(new[] { "reportType", "ship" }, inner.EnumerateObject().Select(p => p.Name).ToArray());
        var ship = inner.GetProperty("ship");
        Assert.Equal("star_hopper", ship.GetProperty("key").GetString());
        Assert.Equal("Star Hopper", ship.GetProperty("name").GetString());
        Assert.Equal("Layex", ship.GetProperty("nickname").GetString());
        Assert.Equal("It has two engines. And a bed!", ship.GetProperty("note").GetString());
        Assert.Equal("2026.11.6", ship.GetProperty("gameVersion").GetString());
        Assert.Equal("00ff00ff00ff00ff", ship.GetProperty("blockFingerprint").GetString());
        Assert.Equal(CreationSubmission.FormatVersion, ship.GetProperty("formatVersion").GetInt32());
        Assert.Equal(3, ship.GetProperty("cellCount").GetInt32());
        Assert.Equal("hammerhead", ship.GetProperty("basedOn").GetString());
        var consent = ship.GetProperty("consent");
        Assert.True(consent.GetProperty("self").GetBoolean() && consent.GetProperty("grant").GetBoolean() && consent.GetProperty("age").GetBoolean());
        Assert.Equal(CreationSubmission.ConsentTextVersion, consent.GetProperty("textVersion").GetInt32());

        // "No e-mail, no location" — and no machine facts, no install token, not the in-game name
        Assert.Equal(string.Empty, root.GetProperty("email").GetString());
        Assert.Equal(string.Empty, root.GetProperty("playerId").GetString());
        Assert.Equal("Layex", root.GetProperty("playerName").GetString());
        Assert.Equal("reply-key-hash", root.GetProperty("replyKey").GetString());
        Assert.Equal("Ship: Star Hopper (star_hopper)", root.GetProperty("title").GetString());
    }

    [Fact]
    public void TheCreation_TravelsAsOneCompactJsonDocument_AndThePreviewAsThePngScreenshot()
    {
        var root = Body(Build(Form()).Report!);

        var attachments = root.GetProperty("attachments").EnumerateArray().ToArray();
        var payload = Assert.Single(attachments);
        Assert.Equal("application/json", payload.GetProperty("mimeType").GetString());
        string json = Encoding.UTF8.GetString(Convert.FromBase64String(payload.GetProperty("base64").GetString()!));
        Assert.DoesNotContain("\n", json); // the editors' pretty print is not paid for twice
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(1, doc.RootElement.GetProperty("formatVersion").GetInt32());
        Assert.Equal("ship", doc.RootElement.GetProperty("kind").GetString());
        var files = doc.RootElement.GetProperty("files");
        Assert.Equal(new[] { "ship.json", "layout.json" }, files.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal("Fast.", files.GetProperty("ship.json").GetProperty("description").GetString());

        var shot = root.GetProperty("screenshot");
        Assert.Equal("image/png", shot.GetProperty("mimeType").GetString());
        Assert.Equal(Png, Convert.FromBase64String(shot.GetProperty("base64").GetString()!));
    }

    [Fact]
    public void ABigPayload_IsGzipped_AndStillUnpacksToTheSameDocument()
    {
        var big = new StringBuilder("{\"cells\":[");
        for (int i = 0; i < 20_000; i++)
        {
            big.Append(i == 0 ? string.Empty : ",").Append("{\"x\":").Append(i).Append(",\"id\":\"steel_wall\",\"label\":\"\"}");
        }

        var payload = new CreationPayload { CellCount = 20_000 }.Add("structure.json", "{\"key\":\"big\"}").Add("layout.json", big.Append("]}").ToString());
        var result = Build(Form(CreationSubmission.KindStructure), payload);

        var attachment = Assert.Single(result.Report!.Attachments!);
        Assert.Equal("application/gzip", attachment.MimeType);
        Assert.Equal("structure.json.gz", attachment.FileName);
        using var gz = new GZipStream(new MemoryStream(Convert.FromBase64String(attachment.Base64)), CompressionMode.Decompress);
        using var reader = new StreamReader(gz, Encoding.UTF8);
        using var doc = JsonDocument.Parse(reader.ReadToEnd());
        Assert.Equal(20_000, doc.RootElement.GetProperty("files").GetProperty("layout.json").GetProperty("cells").GetArrayLength());
        Assert.True(result.PayloadBytes < CreationSubmission.GzipThreshold);
    }

    [Fact]
    public void AStructureAboveTheCellCap_AndAPayloadAboveTheSizeCap_AreRefused()
    {
        var tooMany = Build(Form(CreationSubmission.KindStructure), new CreationPayload { CellCount = CreationSubmission.MaxCells(CreationSubmission.KindStructure) + 1 }.Add("layout.json", "{}"));
        Assert.Equal(CreationRefusal.TooManyCells, tooMany.Refusal);
        Assert.Null(tooMany.Report);

        // Random text does not compress: 120 KB of it is above the avatar's 100 KB even gzipped.
        var random = new Random(7);
        var noise = new char[CreationSubmission.GzipThreshold + 10];
        for (int i = 0; i < noise.Length; i++)
        {
            noise[i] = (char)random.Next('a', 'z' + 1);
        }

        var tooLarge = Build(Form(CreationSubmission.KindAvatar), new CreationPayload().Add("skin.json", "{\"facePixels\":\"" + new string(noise) + "\"}"));
        Assert.Equal(CreationRefusal.TooLarge, tooLarge.Refusal);
        Assert.True(tooLarge.PayloadBytes > CreationSubmission.MaxPayloadBytes(CreationSubmission.KindAvatar));
        Assert.Null(tooLarge.Report);
        Assert.Equal(0, CreationSubmission.MaxCells(CreationSubmission.KindShip)); // a ship is bounded by its room
    }

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public void WithoutAllThreeConsents_NothingIsBuilt(bool self, bool grant, bool age)
    {
        var form = Form();
        form.MadeMyself = self;
        form.GrantsUse = grant;
        form.OldEnoughOrParentsAgreed = age;

        var result = Build(form);

        Assert.False(form.ConsentComplete);
        Assert.Equal(CreationRefusal.Incomplete, result.Refusal);
        Assert.Null(result.Report);
    }

    [Fact]
    public void NothingValid_IsNotSent_AndATextureIsNotThisBuildersKind()
    {
        Assert.Equal(CreationRefusal.NoContent, Build(Form(), new CreationPayload()).Refusal);
        Assert.Equal(CreationRefusal.NoContent, Build(Form(), new CreationPayload().Add("ship.json", "{ not json")).Refusal);
        Assert.Equal(CreationRefusal.Incomplete, Build(Form(CreationSubmission.KindTexture)).Refusal);
    }

    [Fact]
    public void NoName_NoNote_AndAnOddKey_StillMakeAValidReport()
    {
        var form = Form(CreationSubmission.KindAvatar);
        form.Nickname = "   ";
        form.Note = string.Empty;
        form.Name = string.Empty;
        form.Key = "Мой Скин";

        var root = Body(Build(form, new CreationPayload().Add("skin.json", "{\"name\":\"Мой Скин\"}")).Report!);

        Assert.Equal(string.Empty, root.GetProperty("playerName").GetString());
        Assert.Equal("Avatar skin: avatar", root.GetProperty("title").GetString()); // a folder-safe key, never empty
        Assert.True(root.GetProperty("description").GetString()!.Length >= 3); // the inbox rejects an empty description
        Assert.Equal("avatar-submission", root.GetProperty("reportJson").GetProperty("reportType").GetString());
        Assert.Equal("my_ship_2", CreationSubmission.SafeKey("My Ship-2", CreationSubmission.KindShip));
    }

    [Fact]
    public void Details_RideAlong_ButNeverReplaceTheBuildersOwnFields()
    {
        var payload = Ship();
        payload.Details["structureKind"] = "settlement";
        payload.Details["key"] = "evil";

        var block = Body(Build(Form(CreationSubmission.KindStructure), payload).Report!).GetProperty("reportJson").GetProperty("structure");

        Assert.Equal("settlement", block.GetProperty("structureKind").GetString());
        Assert.Equal("star_hopper", block.GetProperty("key").GetString());
    }

    [Fact]
    public void FreeText_IsCapped_AndAHugePreviewIsDropped_NotTheCreation()
    {
        var form = Form();
        form.Nickname = new string('n', 200);
        form.Note = new string('x', 5000);

        var report = Build(form, png: new byte[CreationSubmission.MaxPreviewBytes + 1]).Report!;

        Assert.Equal(CreationSubmission.MaxNicknameLength, report.PlayerName.Length);
        Assert.Equal(CreationSubmission.MaxNoteLength, report.Description.Length);
        Assert.Null(report.Screenshot);
        Assert.Single(report.Attachments!);
    }

    /// <summary>#3319: a HUD frame from the character editor — its own category, the four tiles + twinkle + size in
    /// <c>hud.json</c> (the dialog promises: the four frame tiles, a picture of the frame, the nickname), the skin's cap.</summary>
    [Fact]
    public void AHudFrame_TravelsAsHudJson_InItsOwnCategory_WithNothingButTheFrame()
    {
        var design = HudFrameDesignTests.Sample();
        string file = HudFrames.HudFrameSubmissionFile.Json(design, 2, "snow_frame", "Snow frame");
        var payload = new CreationPayload { BasedOn = "christmas_lights" }.Add(HudFrames.HudFrameSubmissionFile.FileName, file);
        var form = Form(CreationSubmission.KindHud);
        form.Key = "snow_frame";
        form.Name = "Snow frame";

        var result = Build(form, payload);

        Assert.Equal(CreationRefusal.None, result.Refusal);
        var root = Body(result.Report!);
        var inner = root.GetProperty("reportJson");
        Assert.Equal("hud-submission", inner.GetProperty("reportType").GetString());
        Assert.Equal("christmas_lights", inner.GetProperty("hud").GetProperty("basedOn").GetString());
        Assert.Equal("HUD frame: Snow frame (snow_frame)", root.GetProperty("title").GetString());
        Assert.Equal(string.Empty, root.GetProperty("email").GetString());
        var attachment = Assert.Single(root.GetProperty("attachments").EnumerateArray().ToArray());
        Assert.Equal("hud.json", attachment.GetProperty("fileName").GetString());
        using var doc = JsonDocument.Parse(Convert.FromBase64String(attachment.GetProperty("base64").GetString()!));
        var hud = doc.RootElement.GetProperty("files").GetProperty("hud.json");
        Assert.Equal(new[] { "formatVersion", "key", "name", "size", "corner", "top", "side", "bottom", "twinkle", "twinkleColors" },
            hud.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal(design.Tile(HudFrames.HudFrameTile.Top), hud.GetProperty("top").GetString());
        Assert.Equal(2, hud.GetProperty("size").GetInt32());
        Assert.Equal(new[] { 25, 27 }, hud.GetProperty("twinkle").EnumerateArray().Select(e => e.GetInt32()).ToArray());

        Assert.True(CreationSubmission.IsCreationKind(CreationSubmission.KindHud));
        Assert.Equal(100_000, CreationSubmission.MaxPayloadBytes(CreationSubmission.KindHud));
        Assert.True(result.PayloadBytes < CreationSubmission.MaxPayloadBytes(CreationSubmission.KindHud));
        Assert.Contains("\"size\": 2", HudFrames.HudFrameSubmissionFile.Json(design, 9, "k", "n"), StringComparison.Ordinal); // clamped
    }
}
