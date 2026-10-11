// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Story;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>A consumer of server game events (#2465) — the per-player step runtime of the story packs registers itself
/// for the event keys its active steps listen to. Called synchronously from the gameplay handler that raised the event,
/// on the server thread; it must be cheap and must not throw (a throw is logged and swallowed).</summary>
public interface IGameEventListener
{
    /// <summary>The player <paramref name="actor"/> did <paramref name="key"/> (a <see cref="GameEventKeys"/> key) with
    /// <paramref name="arg"/> (the entity, or empty) <paramref name="amount"/> times.</summary>
    void OnGameEvent(PlayerSession actor, string key, string arg, int amount);
}

/// <summary>
/// The game-event bus (#2465, synthesis §5.5): ONE server entry point, <see cref="RaiseGameEvent"/>, that every
/// gameplay handler calls with a fact <c>(key, arg)</c> about what a player just did. It feeds the player-pack step
/// runtime and nothing else: it persists nothing (in particular nothing in <c>AchievementCounters</c>, which travels
/// whole on every achievement change), and it sits NEXT TO the existing hooks — achievements keep
/// <c>OnAchievement*</c> → <c>Advance</c>, VEGA keeps its <c>ShipAiOn*</c> and <c>RecordStory*</c> calls — never in their
/// place, so their behaviour cannot change by accident.
/// <para>Cost: with no listener registered a raise is one integer compare; otherwise one dictionary lookup on the key
/// (plus a short prefix scan only while a wildcard listener exists) and a call per listener. Observers
/// (<see cref="PlayerSession.Spectating"/>) and the join's own placement (<see cref="PlayerSession.InJoinPlacement"/>)
/// raise nothing.</para>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Listeners per exact event key. Arrays are replaced, never mutated, so a listener may (un)register while
    /// an event is being delivered.</summary>
    private readonly Dictionary<string, IGameEventListener[]> _gameEventListeners = new(StringComparer.Ordinal);

    /// <summary>Wildcard listeners (<c>craft:*</c>, <c>crystal:*</c>, <c>*</c>) as (prefix, listener) pairs.</summary>
    private (string Prefix, IGameEventListener Listener)[] _gameEventPrefixListeners = Array.Empty<(string, IGameEventListener)>();

    /// <summary>All registrations together — zero keeps <see cref="RaiseGameEvent"/> at one compare.</summary>
    private int _gameEventListenerCount;

    /// <summary>The shortest gap between two <c>insight:companion_ward</c> events of one player (the ward is checked
    /// every enemy tick while a machine stands down).</summary>
    internal const double InsightEventCooldownSeconds = 30.0;

    /// <summary>The landing context a call path forces onto the <c>travel:landed</c> it ends in (#1614: a parked ship
    /// flying a map trip lands through the flight landing, but the player chose it on the map → <c>quick</c>).</summary>
    private string? _landingContextOverride;

    /// <summary>Registers <paramref name="listener"/> for <paramref name="keyOrPattern"/>: an exact key
    /// (<c>mine:any</c>) or a prefix ending in <c>*</c> (<c>craft:*</c>, <c>*</c> for everything). Registering the same
    /// pair twice delivers twice — the caller owns its bookkeeping.</summary>
    public void AddGameEventListener(string keyOrPattern, IGameEventListener listener)
    {
        if (string.IsNullOrEmpty(keyOrPattern))
        {
            throw new ArgumentException("An event key or pattern is required.", nameof(keyOrPattern));
        }

        if (listener is null)
        {
            throw new ArgumentNullException(nameof(listener));
        }

        int star = keyOrPattern.IndexOf('*', StringComparison.Ordinal);
        if (star >= 0 && star != keyOrPattern.Length - 1)
        {
            throw new ArgumentException("A wildcard may only end the pattern ('craft:*').", nameof(keyOrPattern));
        }

        if (star >= 0)
        {
            var prefixes = new (string, IGameEventListener)[_gameEventPrefixListeners.Length + 1];
            Array.Copy(_gameEventPrefixListeners, prefixes, _gameEventPrefixListeners.Length);
            prefixes[^1] = (keyOrPattern.Substring(0, star), listener);
            _gameEventPrefixListeners = prefixes;
        }
        else
        {
            _gameEventListeners.TryGetValue(keyOrPattern, out var current);
            current ??= Array.Empty<IGameEventListener>();
            var next = new IGameEventListener[current.Length + 1];
            Array.Copy(current, next, current.Length);
            next[^1] = listener;
            _gameEventListeners[keyOrPattern] = next;
        }

        _gameEventListenerCount++;
    }

    /// <summary>Removes one registration of <paramref name="listener"/> for <paramref name="keyOrPattern"/> (the same
    /// string it was added with). False when there was none.</summary>
    public bool RemoveGameEventListener(string keyOrPattern, IGameEventListener listener)
    {
        if (string.IsNullOrEmpty(keyOrPattern) || listener is null)
        {
            return false;
        }

        if (keyOrPattern[^1] == '*')
        {
            string prefix = keyOrPattern.Substring(0, keyOrPattern.Length - 1);
            int at = Array.FindIndex(_gameEventPrefixListeners, p => p.Prefix == prefix && ReferenceEquals(p.Listener, listener));
            if (at < 0)
            {
                return false;
            }

            var prefixes = new List<(string, IGameEventListener)>(_gameEventPrefixListeners);
            prefixes.RemoveAt(at);
            _gameEventPrefixListeners = prefixes.ToArray();
        }
        else
        {
            if (!_gameEventListeners.TryGetValue(keyOrPattern, out var current))
            {
                return false;
            }

            int at = Array.FindIndex(current, l => ReferenceEquals(l, listener));
            if (at < 0)
            {
                return false;
            }

            if (current.Length == 1)
            {
                _gameEventListeners.Remove(keyOrPattern);
            }
            else
            {
                var next = new List<IGameEventListener>(current);
                next.RemoveAt(at);
                _gameEventListeners[keyOrPattern] = next.ToArray();
            }
        }

        _gameEventListenerCount--;
        return true;
    }

    /// <summary>Whether a raise of <paramref name="key"/> would reach anyone — for a call site whose argument is costly to
    /// build (most are not: the key and the argument are strings the handler already holds).</summary>
    public bool HasGameEventListener(string key)
    {
        if (_gameEventListenerCount == 0 || string.IsNullOrEmpty(key))
        {
            return false;
        }

        if (_gameEventListeners.ContainsKey(key))
        {
            return true;
        }

        foreach (var (prefix, _) in _gameEventPrefixListeners)
        {
            if (key.StartsWith(prefix, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Reports that <paramref name="actor"/> did <paramref name="key"/> (<see cref="GameEventKeys"/>) with
    /// <paramref name="arg"/> <paramref name="amount"/> times. Server-only; persists nothing; a no-op for observers,
    /// during the join's own placement, for a non-positive amount and when nobody listens. Call it NEXT TO the existing
    /// achievement / VEGA / story hook of the site, never instead of one.
    /// </summary>
    private void RaiseGameEvent(PlayerSession? actor, string key, string arg = "", int amount = 1)
    {
        if (_gameEventListenerCount == 0)
        {
            return; // the common case: no step listens to anything
        }

        if (actor is null || !actor.Joined || actor.Spectating || actor.InJoinPlacement || amount <= 0 || string.IsNullOrEmpty(key))
        {
            return;
        }

        arg ??= string.Empty;
        if (_gameEventListeners.TryGetValue(key, out var listeners))
        {
            foreach (var listener in listeners)
            {
                DeliverGameEvent(listener, actor, key, arg, amount);
            }
        }

        var prefixes = _gameEventPrefixListeners;
        foreach (var (prefix, listener) in prefixes)
        {
            if (key.StartsWith(prefix, StringComparison.Ordinal))
            {
                DeliverGameEvent(listener, actor, key, arg, amount);
            }
        }
    }

    /// <summary>One delivery. A listener that throws must not break the gameplay handler that raised the event — the
    /// handler has changed state already and still has messages to send — so the failure is logged and dropped.</summary>
    private void DeliverGameEvent(IGameEventListener listener, PlayerSession actor, string key, string arg, int amount)
    {
        try
        {
            listener.OnGameEvent(actor, key, arg, amount);
        }
        catch (Exception ex)
        {
            _log.Error($"Game event listener {listener.GetType().Name} failed on '{key}' ({arg}) for '{actor.State.Name}': {ex.Message}");
        }
    }

    /// <summary>The <c>travel:landed</c> argument of a landing (synthesis §5.5): an admin move, the map's quick travel or a
    /// landing from flight — unless the call path forced one (<see cref="_landingContextOverride"/>).</summary>
    private string LandingContext(bool quickTravel, bool adminBypass)
        => _landingContextOverride
           ?? (adminBypass ? GameEventKeys.TravelAdmin : quickTravel ? GameEventKeys.TravelQuick : GameEventKeys.TravelFlight);

    /// <summary>The <c>space:enter</c> argument of an entry into flight.</summary>
    private static string SpaceEntryContext(bool skipLaunch, bool hyperjump, bool wormhole)
        => hyperjump ? GameEventKeys.SpaceHyperjump
            : wormhole ? GameEventKeys.SpaceWormhole
            : skipLaunch ? GameEventKeys.SpaceResume
            : GameEventKeys.SpaceLaunch;

    /// <summary>Test seam: raises an event exactly as a gameplay site would (all guards apply).</summary>
    internal void RaiseGameEventForTest(PlayerSession actor, string key, string arg = "", int amount = 1)
        => RaiseGameEvent(actor, key, arg, amount);
}
