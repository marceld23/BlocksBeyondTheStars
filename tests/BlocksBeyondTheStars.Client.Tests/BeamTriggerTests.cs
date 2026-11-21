// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3898 — the held beam's client half: <see cref="BeamTrigger"/> (a tap stays a tap, a hold becomes a beam, the ticks'
/// pace, every way it stops), the intent on the wire, one beam against the real server, and the Unity wiring as source
/// text (the Unity scripts cannot load headless — the UI wiring guards' pattern).
/// </summary>
[Trait("Suite", "ClientCore")]
public sealed class BeamTriggerTests
{
    private const float Frame = 1f / 60f;
    private const float Tick = 0.25f;

    private static string Script(string file)
        => File.ReadAllText(Path.Combine(ClientTestPaths.RepoRoot(), "client", "Assets", "BlocksBeyondTheStars", "Scripts", file));

    /// <summary>Runs <paramref name="seconds"/> of frames with the same inputs; returns what the trigger asked for.</summary>
    private static (int Started, int Ticks, int Stopped, int Starved) Run(BeamTrigger trigger, float seconds, bool held, bool canBeam = true, bool canPay = true, float frame = Frame)
    {
        int started = 0, ticks = 0, stopped = 0, starved = 0;
        int frames = (int)MathF.Round(seconds / frame);
        for (int i = 0; i < frames; i++)
        {
            var step = trigger.Update(frame, held, canBeam, canPay, Tick);
            started += step.Started ? 1 : 0;
            ticks += step.Tick ? 1 : 0;
            stopped += step.Stopped ? 1 : 0;
            starved += step.Starved ? 1 : 0;
        }

        return (started, ticks, stopped, starved);
    }

    [Fact]
    public void ATap_NeverBecomesABeam_AndNothingHappensOnTheRelease()
    {
        var trigger = new BeamTrigger();
        var press = Run(trigger, 0.15f, held: true); // shorter than the hold
        Assert.Equal(BeamTrigger.Phase.Pressed, trigger.State);
        var release = Run(trigger, 0.5f, held: false);

        Assert.Equal((0, 0, 0, 0), press);
        Assert.Equal((0, 0, 0, 0), release); // no shot, no beam, no stop: the tap's shot went out on the press, elsewhere
        Assert.Equal(BeamTrigger.Phase.Idle, trigger.State);
        Assert.InRange(BeamWeaponRules.HoldSeconds, 0.16f, 0.3f); // longer than a quick press, short enough to feel direct
    }

    [Fact]
    public void AHold_StartsTheBeam_WithItsFirstTick_ThenTicksAtTheWeaponsPace()
    {
        var trigger = new BeamTrigger();
        var before = Run(trigger, BeamWeaponRules.HoldSeconds - (2 * Frame), held: true);
        Assert.Equal((0, 0, 0, 0), before);

        var start = Run(trigger, 4 * Frame, held: true);
        Assert.Equal((1, 1, 0, 0), start); // on, and the first tick with it
        Assert.True(trigger.Beaming);

        var two = Run(trigger, 2f, held: true);
        Assert.Equal(0, two.Started);
        Assert.InRange(two.Ticks, 7, 8); // never more than one per 0.25 s
        Assert.Equal(0, two.Stopped);
    }

    [Fact]
    public void TwoTicks_AreNeverCloserThanTheWeaponsTick_AtAnyFrameRate()
    {
        foreach (float frame in new[] { 1f / 144f, 1f / 60f, 1f / 30f, 1f / 12f })
        {
            var trigger = new BeamTrigger();
            float sinceLast = float.PositiveInfinity;
            int ticks = 0;
            for (float t = 0f; t < 6f; t += frame)
            {
                var step = trigger.Update(frame, held: true, canBeam: true, canPay: true, Tick);
                sinceLast += frame;
                if (step.Tick)
                {
                    Assert.True(ticks == 0 || sinceLast >= Tick - 1e-4f, $"frame {frame}: {sinceLast} s after the last tick");
                    sinceLast = 0f;
                    ticks++;
                }
            }

            Assert.True(ticks >= 10, $"frame {frame}: {ticks} ticks in six seconds");
        }
    }

    [Fact]
    public void LettingGo_StopsTheBeam_Once_AndTheNextPressStartsOver()
    {
        var trigger = new BeamTrigger();
        Run(trigger, 1f, held: true);
        Assert.True(trigger.Beaming);

        var release = Run(trigger, 0.2f, held: false);
        Assert.Equal((0, 0, 1, 0), release);
        Assert.Equal(BeamTrigger.Phase.Idle, trigger.State);

        var again = Run(trigger, 0.5f, held: true);
        Assert.Equal(1, again.Started);
    }

    [Fact]
    public void TheHandChanging_StopsIt_AndAControlAlreadyDown_StartsNothing()
    {
        var trigger = new BeamTrigger();
        Run(trigger, 1f, held: true);
        var switched = Run(trigger, 0.1f, held: true, canBeam: false); // another item, a menu, a carried block
        Assert.Equal(1, switched.Stopped);
        Assert.False(trigger.Beaming);

        // Still held, and the beam weapon is back: this is no new press.
        var back = Run(trigger, 1f, held: true);
        Assert.Equal((0, 0, 0, 0), back);

        // The drill's hold, then a scroll to the laser: nothing either.
        var fresh = new BeamTrigger();
        Run(fresh, 1f, held: true, canBeam: false);
        Assert.Equal((0, 0, 0, 0), Run(fresh, 1f, held: true));
        Run(fresh, 0.1f, held: false);
        Assert.Equal(1, Run(fresh, 0.5f, held: true).Started);
    }

    [Fact]
    public void AtTheReserve_TheBeamStops_SaysSoOnce_AndWaitsForTheRelease()
    {
        var trigger = new BeamTrigger();
        Run(trigger, 1f, held: true);
        var dry = Run(trigger, 1f, held: true, canPay: false);
        Assert.Equal((0, 0, 1, 1), dry); // one stop, one "the beam stops here" — not one per frame
        Assert.Equal(BeamTrigger.Phase.Spent, trigger.State);

        // Energy back while still held: no restart without a new press.
        Assert.Equal((0, 0, 0, 0), Run(trigger, 1f, held: true));
        Run(trigger, 0.1f, held: false);
        Assert.Equal(1, Run(trigger, 0.5f, held: true).Started);

        // A hold that starts without the energy never turns the beam on, and says so once.
        var empty = new BeamTrigger();
        var hold = Run(empty, 1f, held: true, canPay: false);
        Assert.Equal((0, 0, 0, 1), hold);
    }

    [Fact]
    public void TheServersStop_AndALostFrame_EndTheBeam_WithoutARestart()
    {
        var trigger = new BeamTrigger();
        Run(trigger, 1f, held: true);
        Assert.True(trigger.ServerStopped());
        Assert.False(trigger.ServerStopped()); // only a beam that is on can be stopped
        Assert.Equal((0, 0, 0, 0), Run(trigger, 1f, held: true));

        var other = new BeamTrigger();
        Run(other, 1f, held: true);
        Assert.True(other.Cancel());   // a menu opened: the on-foot input did not run
        Assert.False(other.Cancel());
        Assert.Equal((0, 0, 0, 0), Run(other, 1f, held: true)); // still down when the menu closes: no new press
        Run(other, 0.1f, held: false);
        Assert.Equal(1, Run(other, 0.5f, held: true).Started);
    }

    [Fact]
    public void TheIntent_CarriesTheTick_AndTheRelease()
    {
        var transport = new RecordingClientTransport();
        using var client = new NetworkClient(transport);
        client.Connect("server", 0);

        client.SendBeamWeaponTick("enemy-7", new Vector3f(0f, 0.5f, 1f));
        client.SendBeamWeaponTick(null!, new Vector3f(1f, 0f, 0f), hasCell: true, x: 4, y: 65, z: -3);
        client.SendBeamWeaponStop();

        var sent = transport.Sent.OfType<BeamWeaponIntent>().ToList();
        Assert.Equal(3, sent.Count);
        Assert.True(sent[0].Active);
        Assert.Equal(("enemy-7", 0.5f, 1f, false), (sent[0].EntityId, sent[0].DirY, sent[0].DirZ, sent[0].HasCell));
        Assert.Equal((string.Empty, true, 4, 65, -3), (sent[1].EntityId, sent[1].HasCell, sent[1].X, sent[1].Y, sent[1].Z));
        Assert.False(sent[2].Active);
    }

    [Fact]
    public void ARealClientsBeam_HurtsAMachine_AndDrainsTheSuit_OnTheRealServer()
    {
        using var h = new ClientServerHarness(ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir()));
        h.Join("Beamer");
        Assert.NotNull(h.JoinAccepted);
        var session = h.Server.Sessions.Values.Single(s => s.State.PlayerId == h.JoinAccepted!.PlayerId);
        var p = session.State;
        p.AboardShip = false;
        p.Position = new Vector3f(100.5f, 300f, 100.5f); // open sky: nothing blocks the line
        p.SuitEnergy = 100f;
        p.Inventory.SetSlot(0, new ItemStack("guardian_laser", 1));
        p.SelectedHotbarSlot = 0;
        h.Server.SpawnPlanetEnemyAtForTest(new Vector3f(108.5f, 300f, 100.5f), damagePerSecond: 0f);
        var machine = h.Server.PlanetEnemies[^1];
        machine.Hull = 500f;
        machine.HullMax = 500f;

        h.Client.SendBeamWeaponTick(machine.Id, new Vector3f(1f, 0f, 0f));
        h.Server.Tick(0.01); // the intent is handled first; the machine has not moved yet

        Assert.Equal(500f - 13.75f, machine.Hull, 2);
        Assert.True(p.SuitEnergy < 100f);
        Assert.True(h.Server.HeldBeamActiveForTest(p.PlayerId));

        h.Client.SendBeamWeaponStop();
        h.Server.Tick(0.01);
        Assert.False(h.Server.HeldBeamActiveForTest(p.PlayerId));
    }

    [Fact]
    public void TheUnityClient_AnnouncesTheOtherSide_AHeadlessOneDoesNot()
    {
        using var client = new NetworkClient(new RecordingClientTransport());
        Assert.False(client.ShowsOtherSide);
        Assert.DoesNotContain(ClientFeatures.OtherSide1, client.Features);
        client.ShowsOtherSide = true;
        Assert.Contains(ClientFeatures.OtherSide1, client.Features);
        Assert.Contains(ClientFeatures.Packs1, client.Features); // the rest of the list is kept
        client.ShowsOtherSide = false;
        Assert.DoesNotContain(ClientFeatures.OtherSide1, client.Features);

        Assert.Contains("Network.ShowsOtherSide = true;", Script("GameBootstrap.cs"));
    }

    [Fact]
    public void TheUnitySide_IsWired_AndNeverFiresTheShotItself()
    {
        string beam = Script("PlayerController.BeamWeapon.cs");
        Assert.Contains("InputMap.Held(InputAction.PrimaryFire) || InputMap.PrimaryHeld()", beam); // the fire key, the pad, touch — and the left click
        Assert.Contains("_beamTrigger.Update(", beam);
        Assert.Contains("SendBeamWeaponTick(", beam);
        Assert.Contains("SendBeamWeaponStop()", beam);
        Assert.DoesNotContain("AttackNearestEnemy", beam); // the tap's shot stays where it always was
        Assert.DoesNotContain("SendAttackEntity", beam);

        string controller = Script("PlayerController.cs");
        Assert.Contains("HandleBeamWeapon();", controller);
        Assert.Contains("LateBeamWeapon();", controller);

        // The others' beams and the server's stop of the own one hang on the world rig.
        Assert.Contains("AddComponent<BeamWeaponView>()", Script("WorldRig.cs"));
        string view = Script("BeamWeaponView.cs");
        Assert.Contains("FxActionKinds.WeaponBeam", view);
        Assert.Contains("OnBeamStoppedByServer()", view);

        // The loop and the end sound are looked up by the shared ids, and a missing clip is silence.
        string line = Script("HeldBeamLine.cs");
        Assert.Contains("BeamWeaponRules.LoopCue", line);
        Assert.Contains("BeamWeaponRules.EndCue", line);
        Assert.Equal(("guardian_beam_loop", "guardian_beam_end"), (BeamWeaponRules.LoopCue, BeamWeaponRules.EndCue));
    }
}
