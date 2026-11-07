// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Globalization;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Sounders;
using BlocksBeyondTheStars.Shared.Story;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The Echoes (#3356): the Sounders who sang the Great Song, "on the other side". While chapter 5 runs for a player who
/// wears the hearing disc in the obsidian chest's socket, the next Echo (Elora, then Kamo and Feya, then Tini) appears at a
/// group of three or more rune stones near them — a monument, the shrine, a ruin, or stones the player placed — as a
/// translucent Sounder NPC (look <c>sounder_echo</c>) carrying the Echo's character id, so the pack's dialog
/// <c>snd_echo_&lt;id&gt;</c> answers. A new Echo needs a site 120 blocks from where the others appeared, and Tini a world
/// none of them was met on (the star singer "sings on another world"). The end of an Echo's dialog raises
/// <c>echo:questioned</c>; the Echo fades a few seconds later. Echoes are runtime only — one per player, gone when the
/// player leaves.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>How far around the player the server looks for rune stones (blocks).</summary>
    internal const int EchoRuneScanRadius = 8;

    /// <summary>Rune stones that make a place where an Echo can come through.</summary>
    internal const int EchoRuneGroupSize = 3;

    /// <summary>A new Echo appears at least this far from the places the other Echoes appeared (blocks).</summary>
    internal const float EchoSiteSpacing = 120f;

    /// <summary>The Echo fades when its player is farther than this (blocks).</summary>
    private const float EchoLeaveDistance = 40f;

    /// <summary>Seconds the Echo stays after its dialog ended (the last answer is still being read).</summary>
    private const double EchoFadeSeconds = 8.0;

    /// <summary>Seconds between two rune-stone scans for one player.</summary>
    private const double EchoScanSeconds = 2.0;

    /// <summary>Per-player milestone of the place an Echo appeared: <c>snd:echo_site:&lt;id&gt;:&lt;location&gt;:&lt;x&gt;:&lt;z&gt;</c>.</summary>
    private const string EchoSitePrefix = "snd:echo_site:";

    private sealed class SoundersEcho
    {
        public string EchoId = string.Empty;
        public int NpcId;
        public string LocationId = string.Empty;
        public Vector3f Pos;
        public double FadeAt;
    }

    private readonly Dictionary<string, SoundersEcho> _soundersEchoes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _echoScanAt = new(StringComparer.Ordinal);

    /// <summary>The Echoes for every player of the active world (from <see cref="TickSoundersStory"/>).</summary>
    private void TickSoundersEchoes(PlayerSession s)
    {
        string pid = s.State.PlayerId;
        if (_soundersEchoes.TryGetValue(pid, out var echo))
        {
            bool fade = (echo.FadeAt > 0 && _uptime >= echo.FadeAt)
                        || !string.Equals(echo.LocationId, _world.LocationId, StringComparison.Ordinal)
                        || WrapDistSq(s.State.Position, echo.Pos) > EchoLeaveDistance * EchoLeaveDistance
                        || (echo.FadeAt <= 0 && !SoundersEchoMayStay(s));
            if (fade)
            {
                RemoveSounderNpc(echo.NpcId);
                _soundersEchoes.Remove(pid);
            }

            return;
        }

        if (!SoundersEchoMayStay(s) || NextSoundersEcho(s.State.Milestones) is not { } next
            || (_echoScanAt.TryGetValue(pid, out double at) && _uptime < at))
        {
            return;
        }

        _echoScanAt[pid] = _uptime + EchoScanSeconds;
        if (TryFindRuneGroup(s.State.Position, out var centre) && EchoSiteAllowed(s.State.Milestones, next, centre))
        {
            SpawnSoundersEcho(s, next, centre);
        }
    }

    /// <summary>An Echo can be heard: chapter 5 runs and the hearing disc sits in the worn obsidian chest.</summary>
    private bool SoundersEchoMayStay(PlayerSession s)
        => InSoundersChapter(s, "s5") && HasActiveSocketDisc(s.State, SoundersIds.DiscHearing);

    /// <summary>The next Echo this player has not questioned yet, in the order of the Song of Return's parts.</summary>
    internal static string? NextSoundersEcho(HashSet<string> milestones)
    {
        foreach (string id in SoundersIds.Echoes)
        {
            if (!milestones.Contains(SoundersIds.EchoMilestone(id)))
            {
                return id;
            }
        }

        return null;
    }

    /// <summary>The middle of the rune stones within <see cref="EchoRuneScanRadius"/> of <paramref name="at"/>, when there are
    /// at least <see cref="EchoRuneGroupSize"/> of them.</summary>
    private bool TryFindRuneGroup(Vector3f at, out Vector3f centre)
    {
        centre = at;
        if (_content.GetBlock("rune_stone") is not { } rune)
        {
            return false;
        }

        int cx = (int)Math.Floor(at.X), cy = (int)Math.Floor(at.Y), cz = (int)Math.Floor(at.Z);
        int count = 0;
        double sx = 0, sy = 0, sz = 0;
        for (int dx = -EchoRuneScanRadius; dx <= EchoRuneScanRadius; dx++)
        {
            for (int dy = -EchoRuneScanRadius; dy <= EchoRuneScanRadius; dy++)
            {
                for (int dz = -EchoRuneScanRadius; dz <= EchoRuneScanRadius; dz++)
                {
                    if (_world.GetBlockIfLoaded(new Vector3i(cx + dx, cy + dy, cz + dz)).Value == rune.NumericId.Value)
                    {
                        count++;
                        sx += cx + dx + 0.5;
                        sy += cy + dy;
                        sz += cz + dz + 0.5;
                    }
                }
            }
        }

        if (count < EchoRuneGroupSize)
        {
            return false;
        }

        centre = new Vector3f((float)(sx / count), (float)(sy / count), (float)(sz / count));
        return true;
    }

    /// <summary>Whether <paramref name="echoId"/> may come through at <paramref name="centre"/>: far enough from the places the
    /// other Echoes appeared, and — for Tini — on a world none of them was met on.</summary>
    private bool EchoSiteAllowed(HashSet<string> milestones, string echoId, Vector3f centre)
    {
        foreach (string key in milestones)
        {
            if (!TryParseEchoSite(key, out string id, out string location, out float x, out float z)
                || string.Equals(id, echoId, StringComparison.Ordinal)
                || !string.Equals(location, _world.LocationId, StringComparison.Ordinal))
            {
                continue;
            }

            if (string.Equals(echoId, "tini", StringComparison.Ordinal)
                || WrapDistSq(centre, new Vector3f(x, centre.Y, z)) < EchoSiteSpacing * EchoSiteSpacing)
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryParseEchoSite(string key, out string id, out string location, out float x, out float z)
    {
        id = location = string.Empty;
        x = z = 0f;
        if (!key.StartsWith(EchoSitePrefix, StringComparison.Ordinal))
        {
            return false;
        }

        // snd:echo_site:<id>:<location>:<x>:<z> — the location id may hold colons, so read x and z from the end.
        string rest = key.Substring(EchoSitePrefix.Length);
        int firstColon = rest.IndexOf(':');
        int zColon = rest.LastIndexOf(':');
        int xColon = zColon > 0 ? rest.LastIndexOf(':', zColon - 1) : -1;
        if (firstColon <= 0 || xColon <= firstColon)
        {
            return false;
        }

        id = rest.Substring(0, firstColon);
        location = rest.Substring(firstColon + 1, xColon - firstColon - 1);
        return float.TryParse(rest.Substring(xColon + 1, zColon - xColon - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out x)
               && float.TryParse(rest.Substring(zColon + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out z);
    }

    /// <summary>Puts the Echo between the player and the stones (2.5 blocks from the player, at the player's feet) and
    /// remembers where it appeared; the first one, Elora, gets her scene.</summary>
    private void SpawnSoundersEcho(PlayerSession s, string echoId, Vector3f centre)
    {
        var p = s.State.Position;
        float dx = centre.X - p.X, dz = centre.Z - p.Z;
        float len = MathF.Max(0.001f, MathF.Sqrt((dx * dx) + (dz * dz)));
        float step = MathF.Min(2.5f, len);
        var pos = new Vector3f(p.X + (dx / len * step), p.Y, p.Z + (dz / len * step));

        string name = char.ToUpperInvariant(echoId[0]) + echoId.Substring(1);
        int npc = SpawnSounderNpc(SoundersIds.Echo, pos, name, echoId, leash: 1.2f);
        _soundersEchoes[s.State.PlayerId] = new SoundersEcho { EchoId = echoId, NpcId = npc, LocationId = _world.LocationId, Pos = pos };

        s.State.Milestones.RemoveWhere(k => k.StartsWith(EchoSitePrefix + echoId + ":", StringComparison.Ordinal));
        s.State.Milestones.Add(string.Format(CultureInfo.InvariantCulture, "{0}{1}:{2}:{3:0}:{4:0}",
            EchoSitePrefix, echoId, _world.LocationId, centre.X, centre.Z));
        if (string.Equals(echoId, "elora", StringComparison.Ordinal))
        {
            PlayCutscene(s, SoundersIds.PackId, "snd.first_echo", anchor: pos);
        }
    }

    /// <summary>A Sounders dialog ended: an Echo's turns into <c>echo:questioned</c> (once — the dialog is once per player),
    /// and the Echo fades a moment later.</summary>
    private void OnSoundersDialogDone(PlayerSession s, string dialogKey)
    {
        const string EchoDialogPrefix = "snd_echo_";
        if (string.IsNullOrEmpty(dialogKey) || !dialogKey.StartsWith(EchoDialogPrefix, StringComparison.Ordinal))
        {
            return;
        }

        string echoId = dialogKey.Substring(EchoDialogPrefix.Length);
        if (s.State.Milestones.Contains(SoundersIds.EchoMilestone(echoId)))
        {
            MarkJournalDirty(s);
            RaiseGameEvent(s, GameEventKeys.EchoQuestioned, echoId);
        }

        if (_soundersEchoes.TryGetValue(s.State.PlayerId, out var echo) && string.Equals(echo.EchoId, echoId, StringComparison.Ordinal))
        {
            echo.FadeAt = _uptime + EchoFadeSeconds;
        }
    }

    // --- Test seams --------------------------------------------------------------------------------------------------------

    /// <summary>Test seam: the NPC id of the Echo standing for <paramref name="playerId"/>, or -1.</summary>
    internal int SoundersEchoNpcForTest(string playerId)
        => _soundersEchoes.TryGetValue(playerId, out var e) ? e.NpcId : -1;

    /// <summary>Test seam: the id of the Echo standing for <paramref name="playerId"/>, or null.</summary>
    internal string? SoundersEchoIdForTest(string playerId)
        => _soundersEchoes.TryGetValue(playerId, out var e) ? e.EchoId : null;
}
