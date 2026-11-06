// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Diagnostics;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// #1988: what the server is doing while a player stares at the loading screen. Starting a world took 20+ s
/// and printed nothing between "content loaded" and "started on port", so neither a player nor a developer
/// could tell a slow pass from a hung one. Every boot pass now reports itself as
/// <c>[boot] 4/14 landing pads (4120 ms)</c>.
/// <para>Two readers: the desktop launcher already scans the server's stdout for the ready line and turns
/// these into a real progress bar (the bar used to be a 2.5 s animation with no relation to the work), and
/// the browser host gets the same lines through its <see cref="IGameLogger"/> without any stdout at all.
/// The numbers are also the measurement that #1989 needs.</para>
/// <para>Only the initial boot is instrumented. <c>LoadWorld</c> also runs when a player travels to another
/// body; there <see cref="GameServer"/> holds no progress and the passes run unreported.</para>
/// <para>#3260: every pass and step is also announced BEFORE it runs — <c>[boot] &gt; 7/12 landing_pads</c> and
/// <c>[boot]   &gt; settlements</c> — with a stable id (<see cref="StageId"/>), so the loading screen can say what
/// the player is waiting for right now instead of a fixed flavour line. The finish lines keep their exact shape:
/// the bar, the measurements and older clients read those. An older client's parser rejects a start line (the
/// <c>&gt;</c> is not a number), so the two never mix up.</para>
/// </summary>
internal sealed class BootProgress
{
    /// <summary>Passes a planet world runs (see <see cref="GameServer.Start"/> + <c>LoadWorld</c>), the final
    /// "ready" included.</summary>
    public const int PlanetStages = 12;

    /// <summary>A void world (an orbital station) skips terrain, fauna, fluids and the surface structures.</summary>
    public const int VoidStages = 7;

    private readonly IGameLogger _log;
    private readonly Stopwatch _total = Stopwatch.StartNew();
    private int _planned;
    private int _done;

    public BootProgress(IGameLogger log, int planned)
    {
        _log = log;
        _planned = Math.Max(1, planned);
    }

    /// <summary>Corrects the planned pass count once the start body's kind is known (a void world runs fewer).
    /// Called before any pass that the two kinds do not share.</summary>
    public void Replan(int planned) => _planned = Math.Max(_done + 1, planned);

    /// <summary>The stable id a start line names a pass or step by: its name trimmed, lower-cased, spaces as
    /// <c>_</c> ("landing pads" → <c>landing_pads</c>, the indented "  npcs" → <c>npcs</c>). The client maps these
    /// ids onto its loading messages, so renaming a pass means updating its table (BootStageText) too.</summary>
    public static string StageId(string name) => name.Trim().ToLowerInvariant().Replace(' ', '_');

    /// <summary>Runs one pass, times it and reports it — announced before the work, reported after it.</summary>
    public void Run(string name, Action step)
    {
        _log.Info($"[boot] > {_done + 1}/{_planned} {StageId(name)}");
        var watch = Stopwatch.StartNew();
        step();
        _done++;
        _log.Info($"[boot] {_done}/{_planned} {name} ({watch.Elapsed.TotalMilliseconds:F0} ms)");
    }

    /// <summary>Times one step WITHIN a pass and reports it indented, without advancing the counter (#1990):
    /// the "structures" pass runs a dozen stampers, and its total alone never said which of them is slow.
    /// A step under a millisecond stays quiet so the log keeps its shape. Its start line (#3260) is always printed:
    /// how long the step will take is not known before it runs.</summary>
    public void Detail(string name, Action step)
    {
        _log.Info($"[boot]   > {StageId(name)}");
        var watch = Stopwatch.StartNew();
        step();
        double ms = watch.Elapsed.TotalMilliseconds;
        if (ms >= 1.0)
        {
            _log.Info($"[boot]   · {name} ({ms:F0} ms)");
        }
    }

    /// <summary>Announces the final pass (#3260): from here the server only waits for its message formatters and
    /// opens the port, so the loading screen can say "Connecting…" instead of naming the last structure stamped.</summary>
    public void BeginFinish() => _log.Info($"[boot] > {_planned}/{_planned} ready");

    /// <summary>Total boot time, reported once the port is about to open.</summary>
    public void Finish()
        => _log.Info($"[boot] {_planned}/{_planned} ready ({_total.Elapsed.TotalMilliseconds:F0} ms total)");
}
