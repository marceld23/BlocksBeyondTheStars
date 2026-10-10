// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Threading.Tasks;
using BlocksBeyondTheStars.Shared.Content;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Harsh worlds skew to ruins: across 60 ice and 60 jungle worlds, the ice ones hold a larger share of ruined
/// settlements. The slowest sweep of the settlement overhaul (120 world boots), in a class of its own and run on two
/// workers, each taking half of the seeds of both planets (#2456).
/// </summary>
public sealed class SettlementRuinSkewTests : IDisposable
{
    private readonly SettlementTestWorlds _worlds = new();

    [Fact]
    [Trait("Category", "Slow")]
    public void HarshWorlds_SkewToRuins_MoreThanLushWorlds()
    {
        Counts low = default, high = default;
        Parallel.Invoke(() => low = CountSeeds(1, 30), () => high = CountSeeds(31, 60));
        var ice = (Inhabited: low.IceInhabited + high.IceInhabited, Ruined: low.IceRuined + high.IceRuined);
        var jungle = (Inhabited: low.JungleInhabited + high.JungleInhabited, Ruined: low.JungleRuined + high.JungleRuined);

        double iceRuinFrac = ice.Ruined / (double)Math.Max(1, ice.Inhabited + ice.Ruined);
        double jungRuinFrac = jungle.Ruined / (double)Math.Max(1, jungle.Inhabited + jungle.Ruined);

        Assert.True(ice.Ruined + ice.Inhabited > 0 && jungle.Ruined + jungle.Inhabited > 0, "Both planets should produce settlements.");
        Assert.True(iceRuinFrac > jungRuinFrac,
            $"A harsh world should have a higher ruin fraction (ice {iceRuinFrac:F2} vs jungle {jungRuinFrac:F2}).");
    }

    private Counts CountSeeds(long first, long last)
    {
        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir()); // one per worker: nothing shared between the halves
        var c = default(Counts);
        for (long seed = first; seed <= last; seed++)
        {
            (int inhab, int ruin) = Kinds(content, "ice", seed);
            c.IceInhabited += inhab;
            c.IceRuined += ruin;
            (inhab, ruin) = Kinds(content, "jungle", seed);
            c.JungleInhabited += inhab;
            c.JungleRuined += ruin;
        }

        return c;
    }

    private (int Inhabited, int Ruined) Kinds(GameContent content, string planet, long seed)
    {
        var server = _worlds.Start(content, planet, seed, out var repo);
        using (repo)
        {
            return (server.InhabitedSettlementCount, server.SettlementCount - server.InhabitedSettlementCount);
        }
    }

    private struct Counts
    {
        public int IceInhabited;
        public int IceRuined;
        public int JungleInhabited;
        public int JungleRuined;
    }

    public void Dispose() => _worlds.Dispose();
}
