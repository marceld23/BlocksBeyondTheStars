// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Threading;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Shared.Missions;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The shared scripted /npc-lines backend: answers <see cref="GenerateNpcLines"/> per request from a lambda, counts the
/// calls and records every request. With a gate it blocks until the test opens it (to hold requests in flight). Every
/// other kind of AI request answers null.
/// </summary>
public sealed class FakeNpcLines : IAiMissionProvider
{
    private readonly Func<NpcLinesRequest, string[]?> _answer;
    private readonly ManualResetEventSlim? _gate;
    private int _calls;

    public FakeNpcLines(Func<NpcLinesRequest, string[]?> answer, ManualResetEventSlim? gate = null)
    {
        _answer = answer;
        _gate = gate;
    }

    public int Calls => Volatile.Read(ref _calls);

    public List<NpcLinesRequest> Requests { get; } = new();

    public MissionPlan? Generate(string context) => null;

    public string? GenerateNpcLine(NpcLineRequest request) => null;

    public MissionTextResult? GenerateMissionText(MissionTextRequest request) => null;

    public string[]? GenerateNpcLines(NpcLinesRequest request)
    {
        Interlocked.Increment(ref _calls);
        lock (Requests)
        {
            Requests.Add(request);
        }

        _gate?.Wait(TimeSpan.FromSeconds(10));
        return _answer(request);
    }
}
