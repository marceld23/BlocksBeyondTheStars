// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>#3521: a space wreck of a generation-38 world is built from a ship layout whose design is pinned per wreck in the
/// save; an older world keeps the box of the design it always drew.</summary>
public sealed class SpaceWreckLayoutTests : IDisposable
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_swrecklayout_" + Guid.NewGuid().ToString("N"));

    private SvGameServer NewServer(string name, int generation, out SqliteWorldRepository repo, IGameLogger? logger = null)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        var config = new ServerConfig { WorldName = name, Seed = 11, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        config.Rules.FreeSpaceFlight = true;
        config.World.Wrecks = Frequency.Frequent;
        config.World.TerrainGeneration = generation;
        var server = new SvGameServer(config, Content, new LoopbackServerTransport(new LoopbackLink()), repo, logger);
        server.Start();
        return server;
    }

    /// <summary>Takes the pilot to a landable body of the first system with a wreck and enters space there.</summary>
    private static CelestialBody EnterSpaceNextToAWreck(SvGameServer server)
    {
        var pilot = server.AddLocalPlayer("Salvager");
        foreach (var sys in server.Galaxy.Systems.OrderBy(s => s.Bodies.Any(b => b.Id == server.ActiveLocationId) ? 0 : 1))
        {
            var wreck = sys.Bodies.FirstOrDefault(b => b.Kind == CelestialKind.Wreck);
            var anchor = sys.Bodies.FirstOrDefault(b =>
                b.Kind is CelestialKind.Planet or CelestialKind.Moon or CelestialKind.AsteroidField && !string.IsNullOrEmpty(b.PlanetType));
            if (wreck is null || anchor is null)
            {
                continue;
            }

            if (pilot.CurrentLocationId != anchor.Id)
            {
                if (!server.Ship.Modules.Contains("jump_generator"))
                {
                    server.Ship.Modules.Add("jump_generator");
                }

                server.Travel(pilot.State.PlayerId, anchor.Id);
            }

            server.EnterSpace("Salvager");
            return wreck;
        }

        throw new Xunit.Sdk.XunitException("the Frequent wreck setting should litter at least one system with a wreck");
    }

    /// <summary>The blocks of a hull as the space structure holds them.</summary>
    private static int Blocks(WreckStructure hull)
    {
        int n = 0;
        for (int x = 0; x < hull.Width; x++)
            for (int y = 0; y < hull.Height; y++)
                for (int z = 0; z < hull.Length; z++)
                {
                    n += hull.Get(x, y, z) != 0 ? 1 : 0;
                }

        return n;
    }

    private static long WreckSeed(SvGameServer server, string bodyId) => server.Metadata.Seed ^ WorldGenerator.StableHash("spacewreck:" + bodyId);

    private static WreckStructure LayoutHull(SvGameServer server, string bodyId, string ship)
    {
        var design = Content.GetShip(ship)!;
        return WreckGenerator.GenerateFromLayout(design, Content.GetShipLayout(design.Layout)!, WreckSeed(server, bodyId), Content, groundRow: -1);
    }

    [Fact]
    public void ASpaceWrecksDesign_IsPinned_AndItsHullIsBuiltFromThatLayout_AcrossRestarts()
    {
        const string name = "pinned";
        string wreckId, pinned;
        var server = NewServer(name, WorldDescription.StructurePipelineGeneration, out var repo);
        using (repo)
        {
            wreckId = EnterSpaceNextToAWreck(server).Id;
            Assert.True(server.SpaceWreckDesignsForTest.TryGetValue(wreckId, out pinned!), "the design is pinned on the first flight");
            Assert.NotNull(Content.GetShipLayout(Content.GetShip(pinned)!.Layout));
            Assert.Equal(Blocks(LayoutHull(server, wreckId, pinned)), server.StructureBlockCountForTest(wreckId));
            server.LeaveSpace("Salvager");
            repo.Flush();
            server.Stop();
        }

        // Another design pinned in the save without a fingerprint (as if the pool had changed under it, pinned before the
        // fingerprint was): the pin wins on the next start, and the layout it stands as is pinned now.
        string other = Content.Ships.Values.Where(s => s.Key != pinned && Content.GetShipLayout(s.Layout) is not null)
            .OrderBy(s => s.Key, StringComparer.Ordinal).First().Key;
        EditMeta(name, meta =>
        {
            Assert.Equal(pinned, meta.SpaceWreckDesigns[wreckId]); // persisted …
            Assert.Equal(Fingerprint(pinned), meta.SpaceWreckLayouts[wreckId]); // … with the layout it was built from
            meta.SpaceWreckDesigns[wreckId] = other;
            meta.SpaceWreckLayouts.Remove(wreckId);
        });

        server = NewServer(name, WorldDescription.StructurePipelineGeneration, out repo);
        using (repo)
        {
            Assert.Equal(wreckId, EnterSpaceNextToAWreck(server).Id);
            Assert.Equal(other, server.SpaceWreckDesignsForTest[wreckId]);
            Assert.Equal(Blocks(LayoutHull(server, wreckId, other)), server.StructureBlockCountForTest(wreckId));
            Assert.Equal(Fingerprint(other), server.Metadata.SpaceWreckLayouts[wreckId]);
            server.LeaveSpace("Salvager");
            repo.Flush();
            server.Stop();
        }

        // The layout changed since it was pinned (another fingerprint): the wreck drifts as the box of its own design, warned.
        EditMeta(name, meta => meta.SpaceWreckLayouts[wreckId] = "0000000000000000");
        var log = new CapturingLogger();
        server = NewServer(name, WorldDescription.StructurePipelineGeneration, out repo, log);
        using (repo)
        {
            Assert.Equal(wreckId, EnterSpaceNextToAWreck(server).Id);
            var box = WreckGenerator.Generate(Content.GetShip(other)!, WreckSeed(server, wreckId), Content);
            Assert.Equal(Blocks(box), server.StructureBlockCountForTest(wreckId));
            Assert.Contains(log.Warnings, w => w.Contains(wreckId, StringComparison.Ordinal) && w.Contains("has changed", StringComparison.Ordinal));
            server.Stop();
        }
    }

    private static string Fingerprint(string ship) => WreckGenerator.LayoutFingerprint(Content.GetShipLayout(Content.GetShip(ship)!.Layout)!);

    private void EditMeta(string name, Action<Shared.State.WorldMetadata> edit)
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        repo.Initialize();
        var meta = repo.LoadMetadata()!;
        edit(meta);
        repo.SaveMetadata(meta);
    }

    [Fact]
    public void OnGeneration37_ASpaceWreck_StaysTheBoxOfTheDrawnDesign_AndPinsNothing()
    {
        var server = NewServer("classic", WorldDescription.LandmarkGeneration, out var repo);
        using (repo)
        {
            var wreck = EnterSpaceNextToAWreck(server);
            Assert.Empty(server.SpaceWreckDesignsForTest);

            long seed = WreckSeed(server, wreck.Id);
            var rng = new Random(unchecked((int)(seed ^ (seed >> 32))));
            var designs = Content.Ships.Values.OrderBy(d => d.Key, StringComparer.Ordinal).ToList();
            var box = WreckGenerator.Generate(designs[rng.Next(designs.Count)], seed, Content);
            Assert.Equal(Blocks(box), server.StructureBlockCountForTest(wreck.Id));
            server.Stop();
        }
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best effort: a lingering SQLite handle on Windows can hold the directory for a moment.
        }
    }
}
