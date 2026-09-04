// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Runtime.InteropServices;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// #1536: raises the Windows system timer resolution to 1 ms while the run loop sleeps between ticks — the
/// default 15.6 ms quantum turned the 66.7 ms tick period into alternating 62.5 / 78 ms sleeps. A no-op on
/// other platforms (their sleep is already millisecond-accurate) and in the Unity/WebGL library flavour.
/// Restored on dispose so a bundled singleplayer server that stops leaves the system as it found it.
/// </summary>
internal sealed class HighResolutionTimerScope : IDisposable
{
    private const uint PeriodMs = 1;
    private readonly bool _raised;

    private HighResolutionTimerScope(bool raised) => _raised = raised;

    public static HighResolutionTimerScope Enter()
    {
#if NET
        if (OperatingSystem.IsWindows())
        {
            try
            {
                return new HighResolutionTimerScope(TimeBeginPeriod(PeriodMs) == 0);
            }
            catch (Exception)
            {
                // winmm missing (Nano Server / Wine without it) — the loop simply keeps the coarse timer
            }
        }
#endif
        return new HighResolutionTimerScope(false);
    }

    public void Dispose()
    {
#if NET
        if (_raised)
        {
            try
            {
                TimeEndPeriod(PeriodMs);
            }
            catch (Exception)
            {
                // best effort
            }
        }
#endif
    }

#if NET
    [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    private static extern uint TimeBeginPeriod(uint periodMs);

    [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
    private static extern uint TimeEndPeriod(uint periodMs);
#endif
}
