// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3900/#3901 — the Unity side of the escort cruiser and the generic boss bar is thin, but PR CI never compiles it. This
/// guard reads the client scripts as text (the UI wiring guards' pattern) and keeps the few joints in place that connect
/// them to the tested Client.Core models: the boss status feeds <see cref="BossBarState"/>, the HUD's bar and the music
/// read it, a cruiser entity goes through the boss check, the mega laser branches off before the classic heavy shot
/// (which stays as it was), the boss's warp has its own picture, the Codex has its portrait — and every new sound id is
/// played behind a fallback, so a missing clip is silence or the older cue, never an error.
/// </summary>
public sealed class EscortCruiserWiringTests
{
    private static readonly string[] NewSounds = { "mega_laser_charge", "mega_laser_fire", "cruiser_warp_in", "cruiser_warp_out" };

    private static string ScriptsDir() => Path.Combine(ClientTestPaths.RepoRoot(), "client", "Assets", "BlocksBeyondTheStars", "Scripts");

    private static string Script(string name) => File.ReadAllText(Path.Combine(ScriptsDir(), name));

    [Fact]
    public void TheBossStatus_FeedsTheOneModel_TheBarAndTheMusicReadIt()
    {
        string entities = Script("GameBootstrap.Entities.cs");
        Assert.Contains("public BossBarState BossBar { get; } = new BossBarState();", entities);
        Assert.Contains("Network.BossStatusReceived += m => BossBar.Receive(m, WorldTime);", entities);
        Assert.Contains("Network.GuardianGiantStatusReceived += m => GuardianGiantFight.Receive(m, WorldTime);", entities); // the giant's feed stays
        Assert.Contains("BossBar.Leave(BossSources.Space)", entities);

        string bar = Script("HudUi.BossBar.cs");
        Assert.Contains("Game.BossBar.TryGetBar(Game.GuardianGiantFight, Game.WorldTime, Game.SpaceViewActive || Game.InSpace, out bar)", bar);
        Assert.Contains("loc.Get(nameKey)", bar);
        Assert.DoesNotContain("\"enemy.guardian_giant.name\"", bar); // no boss's name is hard-wired into the bar any more
        Assert.Contains("ShowBossShield(bar.Shield, fillW);", bar);
        Assert.Contains("bool rage = bar.Enraged;", bar);

        string music = ClientTestPaths.ScriptPartsText("ClientMusic");
        Assert.Contains("BossMusicRules.Plays(", music);
        Assert.Contains("spaceBossFighting: game.BossBar.SpaceBossFighting(game.WorldTime)", music);
        Assert.Contains("giantFighting: game.GuardianGiantFight.Fighting(game.WorldTime)", music);
        // The finale's music is asked before the boss fight's, so it wins in the Guardian's system.
        int finale = music.IndexOf("string finale = FinaleContext(game, inSpace);", StringComparison.Ordinal);
        int bossFight = music.IndexOf("if (BossFightPlays(game, inSpace))", StringComparison.Ordinal);
        Assert.True(finale >= 0 && bossFight > finale, "the finale context must be decided before the boss fight's");
    }

    [Fact]
    public void ACruiserEntity_GoesThroughTheBossCheck_AndTheBossHasItsOwnModel()
    {
        string view = ClientTestPaths.ScriptPartsText("SpaceView");
        Assert.Contains("\"Cruiser\" => BuildCruiserEntityModel(_root.transform, e),", view);
        Assert.Contains("EscortCruiserLook.IsEscortCruiser(e.Kind, e.Scale, Game.BossBar.IsSpaceBoss(e.Id, now))", view);
        Assert.Contains("return BuildCruiserModel(parent);", view); // every other cruiser is the plain model
        Assert.Contains("internal static GameObject BuildEscortCruiserModel(Transform parent, float scale = 1f)", view);
        Assert.Contains("new Vector3(EscortCruiserLook.MuzzleX, EscortCruiserLook.MuzzleY, EscortCruiserLook.MuzzleZ)", view);
        Assert.Contains("case \"Cruiser\": return CruiserTargetRadius(e);", view);
        Assert.Contains("\"Cruiser\" => CruiserExplosionScale(m.Id),", view);
        Assert.Contains("if (hostileShip && BossBarSpeaksFor(e.Id))", view); // no small health bar over the boss
        Assert.Contains("UpdateEscortCruisers();", view);
        Assert.Contains("ResetEscortCruisers();", view);

        Assert.Contains("EnemyCatalog.EscortCruiser => SpaceView.BuildEscortCruiserModel(parent),", Script("EnemyPortraitRig.cs"));
    }

    [Fact]
    public void TheMegaLaser_BranchesOffFirst_AndTheClassicHeavyShotStaysAsItWas()
    {
        string maneuvers = Script("SpaceView.Maneuvers.cs");
        int branch = maneuvers.IndexOf("MegaLaserAim.IsMegaLaser(m.Style)", StringComparison.Ordinal);
        int classic = maneuvers.IndexOf("ShowHeavyCharge(m.EntityId, from.position, Mathf.Clamp(m.Seconds, 0.2f, 6f), atMe);", StringComparison.Ordinal);
        Assert.True(branch >= 0 && classic > branch, "the mega laser must branch off before the classic heavy shot's code");
        Assert.Contains("SpaceCombatFx.HeavyBeam(from.position, end, HeavyCol);", maneuvers);
        Assert.Contains("loc.Get(\"ui.space.heavy_warn\")", maneuvers);

        string fx = Script("SpaceCombatFx.cs");
        Assert.Contains("public static void HeavyCharge(Func<Vector3?> at, float seconds, Color color)", fx); // untouched
        Assert.Contains("FxKit.Beam(from, to, color, 1.1f, 0.45f, mat);", fx);                              // the classic beam
        Assert.Contains("public static void MegaLaserCharge(Func<Vector3?> muzzle, Func<Vector3?> aim, float seconds, Color color, bool atMe)", fx);
        Assert.Contains("MegaLaserAim.Alpha(t, seconds, atMe, still), MegaLaserAim.Width(t, atMe)", fx);

        string mega = Script("SpaceView.MegaLaser.cs");
        Assert.Contains("loc.Get(\"ui.space.mega_laser_warn\")", mega);
        Assert.Contains("MegaLaserAim.Countdown(_megaWarnUntil - Game.WorldTime)", mega);
        Assert.Contains("SpaceCombatFx.MegaLaserBeam(from, to, MegaCol, onRock);", mega);
        Assert.Contains("FxCamera.AddTrauma(", mega); // the jolt goes through FxCamera, which honours the comfort settings

        var content = ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir());
        Assert.True(MegaLaserAim.IsMegaLaser(content.Enemy(EnemyCatalog.EscortCruiser).Tier(EnemyTiers.Normal).HeavyShotStyle),
            "the boss's heavy shot is the style the client draws as the mega laser");
    }

    [Fact]
    public void TheBosssWarp_HasItsOwnPicture()
    {
        string combat = Script("SpaceView.Combat.cs");
        Assert.Contains("fx.Style == BlocksBeyondTheStars.Shared.Definitions.EscortCruiserRules.WarpStyle", combat);
        Assert.Contains("EscortCruiserWarp(at, fx.Arriving);", combat);
        Assert.Contains("SpaceFx.WarpZip(at, dir.sqrMagnitude > 1e-4f ? dir : Vector3.right, fx.Arriving);", combat); // every other ship's, unchanged
        Assert.Contains("public static void WarpZip(Vector3 at, Vector3 dir, bool arriving, float size = 1f)", Script("SpaceFx.cs"));
    }

    [Fact]
    public void EveryNewSound_IsPlayedBehindAFallback_AndABundledClipHasItsMeta()
    {
        string view = ClientTestPaths.ScriptPartsText("SpaceView");
        string audio = Path.Combine(ClientTestPaths.RepoRoot(), "client", "Assets", "Resources", "audio");
        foreach (string id in NewSounds)
        {
            Assert.Contains($"audio.Has(\"{id}\")", view); // asked for before it is played: a missing clip takes the fallback
            Assert.Equal(AudioRange.Huge, AudioMixRules.RangeFor(id));
            string clip = Path.Combine(audio, id + ".mp3");
            if (File.Exists(clip))
            {
                Assert.True(File.Exists(clip + ".meta"), $"{id}.mp3 is bundled without its .meta");
            }
        }

        // The fallbacks are clips the game ships.
        foreach (string fallback in new[] { "heavy_shot_charge", "heavy_shot_fire", "raider_warp_in" })
        {
            Assert.Contains($"\"{fallback}\"", view);
            Assert.True(File.Exists(Path.Combine(audio, fallback + ".mp3")), $"{fallback}.mp3 is not bundled");
        }
    }
}
