// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// When the client re-dials a server that has not answered yet, and when it stops. Pure so the policy
    /// can be pinned by an EditMode test; <see cref="GameBootstrap"/> feeds it the timers.
    ///
    /// Two very different situations share the retry loop:
    /// <list type="bullet">
    /// <item><b>Remote host</b> (LAN / official world / typed address): the server is either there or it is not —
    /// a handful of attempts over ~14 s, then give up loudly so a mistyped address does not strand the player
    /// in an empty void (#409).</item>
    /// <item><b>Bundled local server</b> (singleplayer / self-hosting): the process was spawned a moment ago and is
    /// generating the world. A FRESH world took 16 s on a fast machine (2026-09-12 Player.log), more on a
    /// slow one, more still with an antivirus first-scan of the freshly built EXE — while the old budget was
    /// the remote one, so the client gave up in the very second the server reported ready. Here the client
    /// keeps knocking (cheaply, once a second) for as long as the server is still coming up, connects the
    /// moment the launcher relays the server's "started on port" line, and only gives up after a generous
    /// ceiling. A server process that dies is caught by the shell's launch watcher, not by this budget.</item>
    /// </list>
    /// </summary>
    public static class ConnectRetryPolicy
    {
        public enum Verdict { Wait, Retry, GiveUp }

        /// <summary>Remote hosts: attempts after the initial dial, and the pause between them.</summary>
        public const int RemoteRetries = 6;
        public const float RemoteIntervalSeconds = 2f;

        /// <summary>Bundled local server: pause between knocks, and the ceiling on the whole wait.</summary>
        public const float LocalIntervalSeconds = 1f;
        public const float LocalBudgetSeconds = 120f;

        /// <param name="localServer">True when the target is the bundled server this client spawned.</param>
        /// <param name="localReadySignal">True when the local launcher has just relayed the server's
        /// "started on port" line and that signal has not been acted on yet — connect right now.</param>
        /// <param name="sinceLastAttempt">Seconds since the previous dial.</param>
        /// <param name="retries">Re-dials so far (the initial dial is not counted).</param>
        /// <param name="waitedTotal">Seconds since the initial dial.</param>
        public static Verdict Next(bool localServer, bool localReadySignal, float sinceLastAttempt, int retries, float waitedTotal)
        {
            if (!localServer)
            {
                if (sinceLastAttempt < RemoteIntervalSeconds)
                {
                    return Verdict.Wait;
                }

                return retries < RemoteRetries ? Verdict.Retry : Verdict.GiveUp;
            }

            if (waitedTotal >= LocalBudgetSeconds)
            {
                return Verdict.GiveUp;
            }

            if (localReadySignal)
            {
                return Verdict.Retry;
            }

            return sinceLastAttempt >= LocalIntervalSeconds ? Verdict.Retry : Verdict.Wait;
        }
    }
}
