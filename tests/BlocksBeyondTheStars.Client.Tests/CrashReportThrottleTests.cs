// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client.Feedback;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>The crash throttle reports each distinct fault once per session and stops at the per-session cap,
/// so a per-frame exception can't flood the disk or the website.</summary>
public sealed class CrashReportThrottleTests
{
    [Fact]
    public void SameSignature_ReportedOnlyOnce()
    {
        var t = new CrashReportThrottle(maxPerSession: 20);

        Assert.True(t.ShouldReport("NullRef @ Player.Update"));
        Assert.False(t.ShouldReport("NullRef @ Player.Update")); // duplicate
        Assert.False(t.ShouldReport("NullRef @ Player.Update"));
        Assert.Equal(1, t.Reported);
    }

    [Fact]
    public void DistinctSignatures_EachReportedOnce()
    {
        var t = new CrashReportThrottle(maxPerSession: 20);

        Assert.True(t.ShouldReport("A @ x"));
        Assert.True(t.ShouldReport("B @ y"));
        Assert.Equal(2, t.Reported);
    }

    [Fact]
    public void PerSessionCap_IsEnforced()
    {
        var t = new CrashReportThrottle(maxPerSession: 2);

        Assert.True(t.ShouldReport("A"));
        Assert.True(t.ShouldReport("B"));
        Assert.False(t.ShouldReport("C")); // cap reached even for a brand-new signature
        Assert.Equal(2, t.Reported);
    }

    [Fact]
    public void Signature_CollapsesVaryingTail_ToFirstFrame()
    {
        string s1 = CrashReportThrottle.Signature("NullReferenceException", "Player.Update () line 1\nFoo.Bar ()");
        string s2 = CrashReportThrottle.Signature("NullReferenceException", "Player.Update () line 1\nDifferent.Tail ()");
        Assert.Equal(s1, s2); // same message + first frame ⇒ one bucket

        string s3 = CrashReportThrottle.Signature("NullReferenceException", "Other.Method ()");
        Assert.NotEqual(s1, s3);
    }
}
