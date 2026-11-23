// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// #3979: one view's wait for the map of the world it is about to build a generator for (the far terrain, the biome
    /// looks). <see cref="Ready"/> before building; while it says no, <see cref="ShouldRetry"/> each frame tells when a
    /// fetch has ended and asking again is worth it. Without a demand (desktop) everything is ready at once.
    /// </summary>
    public sealed class PlanetMapWait
    {
        private int _seenVersion = -1;

        /// <summary>The planet type waited for, or null when nothing waits.</summary>
        public string? Waiting { get; private set; }

        /// <summary>True when a generator of <paramref name="planetTypeKey"/> may be built now; else remembers the wait
        /// (and the demand has started the map's fetch).</summary>
        public bool Ready(PlanetMapDemand? demand, string? planetTypeKey)
        {
            if (demand is null || demand.IsReady(planetTypeKey))
            {
                Waiting = null;
                return true;
            }

            Waiting = planetTypeKey;
            _seenVersion = demand.Version;
            return false;
        }

        /// <summary>True when something waits and a fetch has ended since — the caller then builds again (through
        /// <see cref="Ready"/>).</summary>
        public bool ShouldRetry(PlanetMapDemand? demand) => Waiting != null && demand != null && demand.Version != _seenVersion;

        /// <summary>Forgets the wait (another world, or the view was reset).</summary>
        public void Clear() => Waiting = null;
    }

    /// <summary>
    /// #3979: whether the in-process browser server may tick. It builds worlds inside its tick (a join, a landing, a
    /// warp), so it holds while any map of a body in its galaxy is still on its way — after its start, and again when
    /// the galaxy grows a system with map bodies (the Sol system). The galaxy is looked at only when its system count or
    /// a fetch changed, so the answer costs nothing per frame. A map that failed every attempt no longer holds the
    /// server: a world that needs it then fails to generate loudly instead of freezing the game.
    /// </summary>
    public sealed class PlanetMapTickGate
    {
        private int _systems = -1;
        private int _version = -1;
        private bool _open;

        /// <summary>True when the server may tick now.</summary>
        public bool MayTick(PlanetMapDemand? demand, Galaxy? galaxy)
        {
            if (demand is null || !demand.FetchesOnDemand)
            {
                return true;
            }

            int systems = galaxy?.Systems.Count ?? 0;
            if (systems == _systems && (_open || demand.Version == _version))
            {
                return _open;
            }

            _systems = systems;
            _version = demand.Version;
            _open = demand.Need(PlanetMapDemand.PlanetTypesOf(galaxy)) == 0;
            return _open;
        }
    }
}
