// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Api;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>Fail-closed authorization for the /api admin endpoints (issue #411).</summary>
public sealed class AdminAuthTests
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("127.0.0.5")]
    [InlineData("::1")]
    [InlineData("localhost")]
    [InlineData("LOCALHOST")]
    [InlineData(" 127.0.0.1 ")]
    public void IsLoopbackBind_TrueForLoopbackAddresses(string bind)
    {
        Assert.True(AdminAuth.IsLoopbackBind(bind));
    }

    [Theory]
    [InlineData("0.0.0.0")]
    [InlineData("::")]
    [InlineData("192.168.1.5")]
    [InlineData("10.0.0.1")]
    [InlineData("*")]
    [InlineData("+")]
    [InlineData("example.com")]
    [InlineData("")]
    [InlineData(null)]
    public void IsLoopbackBind_FalseForEverythingElse(string? bind)
    {
        Assert.False(AdminAuth.IsLoopbackBind(bind));
    }

    [Fact]
    public void NoPassword_LoopbackBind_Allows()
    {
        Assert.True(AdminAuth.IsAuthorized(configuredPassword: "", providedPassword: "", loopbackBind: true));
    }

    [Fact]
    public void NoPassword_NonLoopbackBind_FailsClosed()
    {
        Assert.False(AdminAuth.IsAuthorized(configuredPassword: "", providedPassword: "", loopbackBind: false));
        Assert.False(AdminAuth.IsAuthorized(configuredPassword: null, providedPassword: "anything", loopbackBind: false));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PasswordSet_CorrectHeader_Allows(bool loopbackBind)
    {
        Assert.True(AdminAuth.IsAuthorized("hunter2", "hunter2", loopbackBind));
    }

    [Theory]
    [InlineData("wrong")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("hunter")]
    [InlineData("hunter22")]
    public void PasswordSet_WrongOrMissingHeader_Rejects(string? provided)
    {
        // Even on a loopback bind a configured password must be enforced.
        Assert.False(AdminAuth.IsAuthorized("hunter2", provided, loopbackBind: true));
        Assert.False(AdminAuth.IsAuthorized("hunter2", provided, loopbackBind: false));
    }
}
