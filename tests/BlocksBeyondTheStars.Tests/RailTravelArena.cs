// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Linq;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The arena the rail-travel tests (#3445–#3447) share: the living-NPC pad (<see cref="NpcLifeWorld"/>, a peaceful world,
/// the sun pinned by day) with a straight monorail line across it — pylons on the pad every 20 blocks from
/// <see cref="WestX"/> to <see cref="EastX"/> along <see cref="LineZ"/> — and two stops three blocks beside the line, 60
/// blocks apart. <see cref="PlaceTrain"/> puts the player's cab and wagons on the line.
/// </summary>
internal sealed class RailTravelArena : IDisposable
{
    public const int LineZ = NpcLifeWorld.Cz + 8;
    public const int WestX = NpcLifeWorld.Cx - 40;
    public const int EastX = NpcLifeWorld.Cx + 40;
    public const int StopWestX = NpcLifeWorld.Cx - 30;
    public const int StopEastX = NpcLifeWorld.Cx + 30;

    public readonly NpcLifeWorld W;

    public RailTravelArena(GameContent content, string name, bool foundBase = false, Action<ServerConfig>? configure = null)
    {
        W = new NpcLifeWorld(content, name, padHalf: 48, configure: c =>
        {
            c.Rules.PlanetEnemies = AlienActivity.Off;
            c.Rules.Bandits = AlienActivity.Off;
            c.Rules.CreatureAbundance = AlienActivity.Off;
            c.PlaceIntercityRail = false;
            configure?.Invoke(c);
        });
        if (foundBase)
        {
            W.FoundBase();
        }

        var inv = W.Player.State.Inventory;
        inv.SetSlot(5, new ItemStack("rail_pylon", 64));
        inv.SetSlot(6, new ItemStack("rail_cab", 2));
        inv.SetSlot(7, new ItemStack("wagon_seats", 4));
        inv.SetSlot(8, new ItemStack("rail_stop", 4));
        for (int x = WestX; x <= EastX; x += 20)
        {
            Pylon(x, LineZ);
        }

        Place(StopWest, "rail_stop");
        Place(StopEast, "rail_stop");
        Home();
    }

    public SvGameServer Server => W.Server;

    public string PlayerId => W.Player.State.PlayerId;

    public Vector3i StopWest => new(StopWestX, W.Feet, LineZ + 3);

    public Vector3i StopEast => new(StopEastX, W.Feet, LineZ + 3);

    public string TrainId { get; private set; } = string.Empty;

    /// <summary>Places a pylon on the pad, standing beside the spot.</summary>
    public void Pylon(int x, int z) => Place(new Vector3i(x, W.Feet, z), "rail_pylon");

    /// <summary>Places a block from the pack, standing three blocks south of the spot.</summary>
    public void Place(Vector3i at, string item)
    {
        W.Player.State.Position = new Vector3f(at.X + 0.5f, W.Feet, at.Z - 2.5f);
        Server.PlaceBlock(PlayerId, at.X, at.Y, at.Z, item);
    }

    /// <summary>Puts the player's cab on the line at <paramref name="cabX"/> and couples the wagons behind it.</summary>
    public string PlaceTrain(int cabX, params string[] wagons)
    {
        Use("rail_cab", new Vector3f(cabX + 0.5f, W.Feet + 1.5f, LineZ + 0.5f));
        TrainId = Server.TrainsForTest().Last().Id;
        for (int i = 0; i < wagons.Length; i++)
        {
            W.TickAt(0.4, 0.5);
            var tail = Server.WagonPoseForTest(TrainId, i + 1)!.Value.Pos;
            Use(wagons[i], tail);
        }

        Home();
        return TrainId;
    }

    /// <summary>Uses a gadget at a spot, standing three blocks south of it.</summary>
    public void Use(string gadget, Vector3f target)
    {
        W.Player.State.Position = new Vector3f(target.X, W.Feet, target.Z - 3f);
        Server.UseGadgetForTest(PlayerId, gadget, target);
    }

    /// <summary>Another stop beside the line at <paramref name="x"/> (three blocks off it, like the two of the arena).</summary>
    public Vector3i AddStop(int x)
    {
        var stop = new Vector3i(x, W.Feet, LineZ + 3);
        Place(stop, "rail_stop");
        Home();
        return stop;
    }

    /// <summary>A small town round the stop at <paramref name="stopX"/> (its box holds the stop) with
    /// <paramref name="villagers"/> plain villagers standing five blocks behind it. Returns their NPC ids.</summary>
    public int[] Town(string name, int stopX, int villagers = 1)
    {
        Server.RegisterTestSettlementForTest(name, new Vector3i(stopX - 4, W.PadY, LineZ + 2), new Vector3i(stopX + 4, W.PadY + 8, LineZ + 10));
        var ids = new int[villagers];
        for (int i = 0; i < villagers; i++)
        {
            ids[i] = Server.SpawnVillagerForTest(name, new Vector3f(stopX - 1.5f + (i * 1.5f), W.Feet, LineZ + 8.5f));
        }

        return ids;
    }

    /// <summary>
    /// A far town (#3447): the line runs on east past the pad to <paramref name="x"/> + 10 (a corridor cleared through
    /// whatever terrain stands there, a pylon every 20 blocks), a stone pad round a new stop at <paramref name="x"/>, and a
    /// small town with one villager there. Returns the villagers' NPC ids.
    /// </summary>
    public int[] FarTown(string name, int x)
    {
        var air = BlocksBeyondTheStars.Shared.Primitives.BlockId.Air;
        var stone = W.Block("stone");
        for (int cx = EastX + 1; cx <= x + 10; cx++)
        {
            bool pad = cx >= x - 7 && cx <= x + 7;
            for (int cz = LineZ - 3; cz <= LineZ + 12; cz++)
            {
                if (!pad && cz > LineZ + 3)
                {
                    continue;
                }

                if (pad)
                {
                    Server.World.SetBlock(new Vector3i(cx, W.PadY, cz), stone);
                }

                for (int y = W.PadY + 1; y <= W.PadY + 7; y++)
                {
                    var cell = new Vector3i(cx, y, cz);
                    if (!Server.World.GetBlock(cell).IsAir)
                    {
                        Server.World.SetBlock(cell, air);
                    }
                }
            }
        }

        W.Player.State.Fly = true;
        for (int px = EastX + 20; px <= x + 10; px += 20)
        {
            Pylon(px, LineZ);
        }

        W.Player.State.Fly = false;
        AddStop(x);
        return Town(name, x);
    }

    /// <summary>The player far off on the pad's level (every NPC of the arena out of reach), seeing only one chunk round.</summary>
    public void FarAway()
    {
        W.Player.ViewDistance = 1;
        W.Player.State.Position = new Vector3f(NpcLifeWorld.Cx + 260f, W.PadY + 40, NpcLifeWorld.Cz);
    }

    /// <summary>The player back on the pad by the base core.</summary>
    public void Home()
    {
        W.Player.ViewDistance = 0;
        W.Player.State.Position = new Vector3f(NpcLifeWorld.Cx - 1.5f, W.Feet, NpcLifeWorld.Cz - 1.5f);
    }

    /// <summary>The train's halt: whether it stands, how long it still waits, the stop it waits at.</summary>
    public (bool Halted, double Remaining, Vector3i? Stop) Halt => Server.TrainHaltForTest(TrainId);

    /// <summary>Ticks (the sun pinned by day) until the train halts at a stop — <paramref name="other"/>: a stop other than
    /// the one it waits at now. Returns the stop, or null.</summary>
    public Vector3i? TickUntilHalted(double seconds = 120, Vector3i? other = null)
    {
        W.TickAt(0.4, seconds, until: () => Halt.Stop is { } s && s != other);
        return Halt.Stop;
    }

    public void Dispose() => W.Dispose();
}
