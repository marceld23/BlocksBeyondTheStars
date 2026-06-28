// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using BlocksBeyondTheStars.Persistence;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>CrashReportWriter is the durable, endpoint-independent sink: it writes one self-contained JSON
/// report per fault, never throws, and exposes the unsent files as a retry queue.</summary>
public sealed class CrashReportWriterTests : IDisposable
{
    private readonly string _dir;

    public CrashReportWriterTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "bbts_crash_" + Guid.NewGuid().ToString("N"));
    }

    [Fact]
    public void Write_ProducesEndpointShapedReport()
    {
        var writer = new CrashReportWriter(_dir, "Test World", "1.2.3");
        Exception captured;
        try { throw new InvalidOperationException("boom in the tick"); }
        catch (Exception ex) { captured = ex; }

        string? path = writer.Write("tick-fault", "TickCreatures", captured, uptimeSeconds: 42.5);

        Assert.NotNull(path);
        Assert.True(File.Exists(path));

        using var doc = JsonDocument.Parse(File.ReadAllText(path!));
        var root = doc.RootElement;

        // Top level matches the website post_bugreport contract (string fields + reportJson object).
        Assert.Contains("Server crash", root.GetProperty("title").GetString());
        Assert.Contains("TickCreatures", root.GetProperty("title").GetString());
        Assert.Contains("boom in the tick", root.GetProperty("description").GetString());
        Assert.True(root.GetProperty("description").GetString()!.Length <= CrashReportWriter.MaxDescriptionLength);
        Assert.Equal(string.Empty, root.GetProperty("email").GetString());
        Assert.Equal("1.2.3", root.GetProperty("gameVersion").GetString());
        Assert.Equal("server", root.GetProperty("platform").GetString());
        Assert.Equal(JsonValueKind.String, root.GetProperty("clientTimestamp").ValueKind);

        // Crash-native detail rides in reportJson (incl. the full stack, which the description cap excludes).
        var rj = root.GetProperty("reportJson");
        Assert.Equal(CrashReportWriter.SchemaVersion, rj.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("tick-fault", rj.GetProperty("kind").GetString());
        Assert.Equal("server", rj.GetProperty("source").GetString());
        Assert.Equal("TickCreatures", rj.GetProperty("system").GetString());
        Assert.Equal("Test World", rj.GetProperty("world").GetString());
        Assert.Equal("1.2.3", rj.GetProperty("serverVersion").GetString());
        Assert.Equal(42.5, rj.GetProperty("uptimeSeconds").GetDouble());
        Assert.Contains("InvalidOperationException", rj.GetProperty("exceptionType").GetString());
        Assert.Equal("boom in the tick", rj.GetProperty("message").GetString());
        Assert.Contains("boom in the tick", rj.GetProperty("stackTrace").GetString());
    }

    [Fact]
    public void Write_ScrubsPiiFromMessageAndStack()
    {
        var writer = new CrashReportWriter(_dir, "w", "v");
        var ex = new Exception(@"failed reading C:\Users\Alice\AppData\Local\BBS\world.db");

        string? path = writer.Write("unhandled-exception", null, ex);

        using var doc = JsonDocument.Parse(File.ReadAllText(path!));
        var root = doc.RootElement;
        string desc = root.GetProperty("description").GetString()!;
        string rjMessage = root.GetProperty("reportJson").GetProperty("message").GetString()!;

        Assert.DoesNotContain("Alice", desc);             // OS account name gone...
        Assert.DoesNotContain("Alice", rjMessage);
        Assert.Contains(@"C:\Users\<user>", desc);        // ...but the rest of the path is preserved
    }

    [Fact]
    public void Write_CapsDescriptionAtEndpointLimit()
    {
        var writer = new CrashReportWriter(_dir, "w", "v");
        string hugeMessage = new string('x', 20_000);

        string? path = writer.Write("unhandled-exception", null, new Exception(hugeMessage));

        using var doc = JsonDocument.Parse(File.ReadAllText(path!));
        Assert.True(doc.RootElement.GetProperty("description").GetString()!.Length <= CrashReportWriter.MaxDescriptionLength);
    }

    [Fact]
    public void Write_NullException_WritesNothing()
    {
        var writer = new CrashReportWriter(_dir, "w", "v");
        Assert.Null(writer.Write("unhandled-exception", null, null!));
        Assert.Equal(0, writer.CountPending());
    }

    [Fact]
    public void EmptyDirectory_DisablesWriting_NeverThrows()
    {
        var writer = new CrashReportWriter(string.Empty, "w", "v");
        Assert.Null(writer.Write("tick-fault", "TickFluids", new Exception("x")));
        Assert.Empty(writer.ListPending());
    }

    [Fact]
    public void ListPending_TracksWrittenReports()
    {
        var writer = new CrashReportWriter(_dir, "w", "v");
        Assert.Equal(0, writer.CountPending());

        writer.Write("tick-fault", "TickWeather", new Exception("a"));
        writer.Write("unobserved-task", null, new Exception("b"));

        Assert.Equal(2, writer.CountPending());
        foreach (var f in writer.ListPending())
        {
            Assert.StartsWith("crash_", Path.GetFileName(f));
            Assert.EndsWith(".json", f);
        }
    }

    [Fact]
    public void FlushPending_SendsAndRetiresAcceptedReports()
    {
        var writer = new CrashReportWriter(_dir, "w", "v");
        writer.Write("tick-fault", "TickFluids", new Exception("a"));
        writer.Write("tick-fault", "TickNpcs", new Exception("b"));

        var sink = new FakeSink(configured: true, accept: true);
        int sent = writer.FlushPending(sink);

        Assert.Equal(2, sent);
        Assert.Equal(2, sink.Received.Count);
        Assert.Equal(0, writer.CountPending());                     // queue drained
        Assert.True(Directory.Exists(Path.Combine(_dir, "sent")));  // moved, not deleted
        Assert.Equal(2, Directory.GetFiles(Path.Combine(_dir, "sent")).Length);
    }

    [Fact]
    public void FlushPending_StopsOnFirstFailure_LeavesRestQueued()
    {
        var writer = new CrashReportWriter(_dir, "w", "v");
        writer.Write("tick-fault", "TickFluids", new Exception("a"));
        writer.Write("tick-fault", "TickNpcs", new Exception("b"));

        var sink = new FakeSink(configured: true, accept: false); // endpoint down
        int sent = writer.FlushPending(sink);

        Assert.Equal(0, sent);
        Assert.Equal(2, writer.CountPending()); // nothing lost — retried next launch
    }

    [Fact]
    public void FlushPending_UnconfiguredSink_IsNoOp()
    {
        var writer = new CrashReportWriter(_dir, "w", "v");
        writer.Write("tick-fault", "TickFluids", new Exception("a"));

        var sink = new FakeSink(configured: false, accept: true);
        Assert.Equal(0, writer.FlushPending(sink));
        Assert.Empty(sink.Received);
        Assert.Equal(1, writer.CountPending());
    }

    private sealed class FakeSink : ICrashReportSink
    {
        private readonly bool _accept;
        public FakeSink(bool configured, bool accept) { IsConfigured = configured; _accept = accept; }
        public bool IsConfigured { get; }
        public List<string> Received { get; } = new();
        public bool Send(string json)
        {
            if (!_accept) return false;
            Received.Add(json);
            return true;
        }
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dir))
            {
                Directory.Delete(_dir, recursive: true);
            }
        }
        catch
        {
            // best-effort temp cleanup
        }
    }
}
