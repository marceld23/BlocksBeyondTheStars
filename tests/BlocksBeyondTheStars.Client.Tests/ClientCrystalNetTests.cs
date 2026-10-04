// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Geometry;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The client's Crystal Net bookkeeping (#2267, #2268): device deltas land on the right cells, and a parked ship's net —
/// sent in ship-local cells, tagged with the ship — shows up in world cells and is operated back in its own frame.
/// </summary>
public sealed class ClientCrystalNetTests
{
    private static NetCrystalDevice Dev(int x, int y, int z, string kind = "Switch", bool output = false)
        => new NetCrystalDevice { Id = x * 100 + z, X = x, Y = y, Z = z, Kind = kind, Output = output };

    [Fact]
    public void ADelta_ChangesAndRemovesDevices_AndTheListIsANewArray()
    {
        var net = new ClientCrystalNet();
        net.OnDevices(new CrystalDeviceList { Devices = new[] { Dev(1, 5, 1), Dev(2, 5, 1) } });
        var before = net.Devices;

        net.OnDelta(new CrystalDeviceDelta { Changed = new[] { Dev(1, 5, 1, output: true) }, Removed = new[] { 2, 5, 1 } });

        Assert.NotSame(before, net.Devices);
        Assert.Single(net.Devices);
        Assert.True(net.DeviceAt(1, 5, 1)!.Output);
        Assert.Null(net.DeviceAt(2, 5, 1));
    }

    [Fact]
    public void AShipsNet_IsShownInWorldCells_AndOperatedInItsFrame()
    {
        var origins = new Dictionary<string, Vector3i>();
        var net = new ClientCrystalNet { FrameOrigin = f => origins.TryGetValue(f, out var o) ? o : null, Circumference = () => 1024 };
        net.OnDevices(new CrystalDeviceList { Devices = new[] { Dev(7, 7, 7) } }); // a world device
        net.OnDevices(new CrystalDeviceList { Frame = "ship:Pilot", Devices = new[] { Dev(2, 1, 3) } });
        net.OnNets(new CrystalNetList { Frame = "ship:Pilot", Nets = new[] { new NetCrystalNet { Id = 1, On = true, Cells = new[] { 2, 1, 3, 2, 1, 4 } } } });

        // The ship is not known yet: only the world shows.
        Assert.Single(net.Devices);
        Assert.Empty(net.Nets);

        origins["ship:Pilot"] = new Vector3i(1022, 60, 10); // parked across the seam of a round world
        net.Recompose();
        Assert.Equal(2, net.Devices.Length);
        var aboard = net.DeviceAt(0, 61, 13); // 1022 + 2 wraps to 0
        Assert.NotNull(aboard);
        Assert.True(net.TryFrameCell(0, 61, 13, out string frame, out var local));
        Assert.Equal("ship:Pilot", frame);
        Assert.Equal(new Vector3i(2, 1, 3), local);
        Assert.Equal(new[] { 0, 61, 13, 0, 61, 14 }, net.Nets[0].Cells);

        // The world device stays in the world's frame.
        Assert.False(net.TryFrameCell(7, 7, 7, out _, out _));

        // The ship leaves (its lists come empty): its devices go, the world's stay.
        net.OnDevices(new CrystalDeviceList { Frame = "ship:Pilot" });
        net.OnNets(new CrystalNetList { Frame = "ship:Pilot" });
        Assert.Single(net.Devices);
        Assert.Empty(net.Nets);
        Assert.False(net.HasFrames);
    }

    [Fact]
    public void ADeltaForAShip_ChangesTheShipsDevice()
    {
        var net = new ClientCrystalNet { FrameOrigin = _ => new Vector3i(100, 50, 100) };
        net.OnDevices(new CrystalDeviceList { Frame = "ship:Pilot", Devices = new[] { Dev(1, 1, 1) } });
        net.OnDelta(new CrystalDeviceDelta { Frame = "ship:Pilot", Changed = new[] { Dev(1, 1, 1, output: true) } });

        Assert.True(net.DeviceAt(101, 51, 101)!.Output);
    }
}
