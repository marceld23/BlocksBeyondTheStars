// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using BlocksBeyondTheStars.Persistence;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The backups the server takes by itself (#2223). Until now it never took one: <c>backupIntervalMinutes</c> was a
/// config key nothing read, and the first load after a content update rewrote a save's block ids with no copy to
/// go back to.
/// <list type="bullet">
/// <item><b>Before a block-id remap.</b> When the block set changed since the world was last saved, the save is
/// copied (<c>pre-remap_&lt;UTC time&gt;</c>) before its ids are rewritten. The remap is one transaction, so it
/// cannot leave the save half-converted — the copy is for a result that is consistent but wrong.</item>
/// <item><b>While the server runs.</b> Every <c>BackupIntervalMinutes</c> a copy (<c>auto_&lt;UTC time&gt;</c>) is
/// written on a background thread through a connection of its own, so the tick never waits for it. Only when a
/// player was online since the last one: an idle server would otherwise rotate its good copies away.</item>
/// <item><b>Bounded.</b> Each kind keeps the newest <c>BackupKeepCount</c> copies and deletes the rest; backups
/// made by hand are never touched.</item>
/// </list>
/// The interval runs on real time, not on tick time: a backup is about hours of play on a clock, and a test that
/// simulates a day in a second must not write one.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>How long a shutdown waits for a backup that is still being written. Short on purpose: the world is
    /// already saved by then, and a container stop grants only a few seconds — a copy that does not make it stays
    /// a temp file and is removed by the next rotation.</summary>
    private const double BackupShutdownWaitSeconds = 5.0;

    private readonly Stopwatch _backupClock = Stopwatch.StartNew();
    private double _backupClockOffset; // test seam: seconds added on top of the real clock
    private double _lastBackupAt;
    private bool _playersSinceBackup;
    private Thread? _backupThread;
    private volatile BackupOutcome? _backupOutcome;

    /// <summary>What a backup run did — written by the thread that ran it, reported by the tick thread.</summary>
    private sealed class BackupOutcome
    {
        public string? Path;
        public int Pruned;
        public double ElapsedMs;
        public Exception? Error;
    }

    private double BackupClockSeconds => _backupClock.Elapsed.TotalSeconds + _backupClockOffset;

    /// <summary>
    /// Copies the save before its block ids are rewritten (#2223) — the world-load path calls this right before
    /// the palette is brought up to date, and it only acts when that step would really remap. A failed copy is
    /// logged and the load goes on: a server must not become unstartable because its backup folder is full or
    /// read-only, and the remap itself is atomic.
    /// </summary>
    private void BackupBeforePaletteRemap()
    {
        var palette = _content.BlockPalette();
        if (!_repo.BlockPaletteNeedsRemap(palette))
        {
            return;
        }

        WarnAboutDroppedBlocks(palette);
        if (!_repo.SupportsAutomaticBackups)
        {
            _log.Info("The block set changed since this world was last saved — its stored block ids are remapped.");
            return;
        }

        try
        {
            string path = _repo.CreateBackup(BackupRotation.Label(BackupRotation.PreRemapPrefix, DateTime.UtcNow));
            BackupRotation.Prune(Path.GetDirectoryName(path) ?? string.Empty, BackupRotation.PreRemapPrefix, _config.BackupKeepCount);
            _log.Info($"The block set changed since this world was last saved — backup written to '{path}' before its stored block ids are remapped.");
        }
        catch (Exception ex)
        {
            _log.Warn($"The block set changed since this world was last saved, and the backup before the remap failed ({ex.Message}). " +
                      "The remap runs as one transaction and continues without it.");
        }
    }

    /// <summary>Says which blocks a remap is about to drop: keys the save has on record and this build does not
    /// know become air, for good. That is what a block removed from the game looks like — and what a world last
    /// saved by a build with MORE blocks looks like when its save version was not raised.</summary>
    private void WarnAboutDroppedBlocks(IReadOnlyDictionary<ushort, string> palette)
    {
        var known = new HashSet<string>(palette.Values, StringComparer.Ordinal);
        var dropped = new List<string>();
        foreach (string key in _repo.LoadBlockPalette().Values)
        {
            if (!known.Contains(key))
            {
                dropped.Add(key);
            }
        }

        if (dropped.Count == 0)
        {
            return;
        }

        dropped.Sort(StringComparer.Ordinal);
        const int shown = 12;
        string names = string.Join(", ", dropped.GetRange(0, Math.Min(shown, dropped.Count)));
        _log.Warn($"{dropped.Count} block type(s) of this world are not part of this version of the game and are removed from it " +
                  $"(their cells become air): {names}{(dropped.Count > shown ? ", …" : string.Empty)}.");
    }

    /// <summary>The tick's backup beat: reports a finished copy and starts the next one when it is due.</summary>
    private void TickBackups()
    {
        ReportFinishedBackup();

        if (_config.BackupIntervalMinutes <= 0 || !_repo.SupportsAutomaticBackups)
        {
            return;
        }

        if (!_playersSinceBackup)
        {
            foreach (var session in _sessions.Values)
            {
                if (session.Joined)
                {
                    _playersSinceBackup = true;
                    break;
                }
            }
        }

        double now = BackupClockSeconds;
        if (now - _lastBackupAt < _config.BackupIntervalMinutes * 60.0 || _backupThread is { IsAlive: true })
        {
            return;
        }

        _lastBackupAt = now;
        if (!_playersSinceBackup)
        {
            return; // nobody was here since the last copy: nothing new to keep, and rotating would only push good copies out
        }

        _playersSinceBackup = false;
        string label = BackupRotation.Label(BackupRotation.AutoPrefix, DateTime.UtcNow);
        int keep = _config.BackupKeepCount;
        var thread = new Thread(() => _backupOutcome = RunBackup(label, keep))
        {
            IsBackground = true, // never keeps the process alive; a copy cut short stays a temp file and is pruned
            Name = "world-backup",
        };
        _backupThread = thread;
        thread.Start();
    }

    /// <summary>Writes one rotating backup and prunes the older ones. Touches no server state — safe on any thread.</summary>
    private BackupOutcome RunBackup(string label, int keep)
    {
        var outcome = new BackupOutcome();
        var watch = Stopwatch.StartNew();
        try
        {
            outcome.Path = _repo.CreateBackgroundBackup(label);
            if (outcome.Path is not null)
            {
                outcome.Pruned = BackupRotation.Prune(Path.GetDirectoryName(outcome.Path) ?? string.Empty, BackupRotation.AutoPrefix, keep);
            }
        }
        catch (Exception ex)
        {
            outcome.Error = ex;
        }

        outcome.ElapsedMs = watch.Elapsed.TotalMilliseconds;
        return outcome;
    }

    private void ReportFinishedBackup()
    {
        var outcome = _backupOutcome;
        if (outcome is null)
        {
            return;
        }

        _backupOutcome = null;
        if (outcome.Error is not null)
        {
            _log.Warn($"Backup failed: {outcome.Error.Message}");
        }
        else if (outcome.Path is not null)
        {
            _log.Info($"Backup written to '{outcome.Path}' ({outcome.ElapsedMs:0} ms" +
                      (outcome.Pruned > 0 ? $", {outcome.Pruned} older one(s) removed)." : ")."));
        }
    }

    /// <summary>Shutdown: lets a backup that is still being written finish, then reports it.</summary>
    private void FinishBackups()
    {
        if (_backupThread is { IsAlive: true } running)
        {
            running.Join(TimeSpan.FromSeconds(BackupShutdownWaitSeconds));
        }

        ReportFinishedBackup();
    }

    /// <summary>Test seam (#2223): moves the backup clock forward, as if that much real time had passed.</summary>
    public void AdvanceBackupClockForTest(double seconds) => _backupClockOffset += seconds;

    /// <summary>Test seam (#2223): waits for a backup the tick started and returns its file, or null when none
    /// was started or it failed.</summary>
    public string? WaitForBackupForTest()
    {
        _backupThread?.Join();
        string? path = _backupOutcome is { Error: null } outcome ? outcome.Path : null;
        ReportFinishedBackup();
        return path;
    }

    /// <summary>Test seam (#2223): writes one rotating backup now, on the calling thread, labelled with
    /// <paramref name="utcNow"/> — the rotation without the clock.</summary>
    public string? BackupNowForTest(DateTime utcNow)
        => RunBackup(BackupRotation.Label(BackupRotation.AutoPrefix, utcNow), _config.BackupKeepCount).Path;
}
