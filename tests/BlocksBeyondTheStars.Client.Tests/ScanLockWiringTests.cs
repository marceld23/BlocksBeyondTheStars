// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3922 — the ship scanner's frame stuttered round an attacking drone: it sat on the raw server pose (a new one every
/// 0.15 s) while the drone's model glides a quarter of a second behind, and it was drawn in Update, before the camera's
/// hit shake and boost kick moved the picture. The fix is Unity code that PR CI never compiles, so this guard reads the
/// scripts as text (the UI wiring guards' pattern): the frame is drawn in LateUpdate next to the target lock's HUD, and
/// the frame, its label and its effects are anchored through the tested <c>SpaceTargeting.ScanAnchorFor</c> on the
/// drawn model — while the range check and the choice of target stay on the server's positions.
/// </summary>
[Trait("Suite", "ClientCore")]
public sealed class ScanLockWiringTests
{
    private static string Script(string name)
        => File.ReadAllText(Path.Combine(ClientTestPaths.RepoRoot(), "client", "Assets", "BlocksBeyondTheStars", "Scripts", name));

    /// <summary>The text of a method of a client script: from its declaration to its closing brace at member indent.</summary>
    private static string Method(string text, string declaration)
    {
        int start = text.IndexOf(declaration, StringComparison.Ordinal);
        Assert.True(start >= 0, $"'{declaration}' not found");
        int end = text.IndexOf("\n        }\n", start, StringComparison.Ordinal);
        Assert.True(end > start, $"the end of '{declaration}' not found");
        return text.Substring(start, end - start);
    }

    private static int Count(string text, string part)
    {
        int n = 0;
        for (int i = text.IndexOf(part, StringComparison.Ordinal); i >= 0; i = text.IndexOf(part, i + part.Length, StringComparison.Ordinal))
        {
            n++;
        }

        return n;
    }

    [Fact]
    public void TheScanFrame_IsDrawnInLateUpdate_NextToTheTargetLock_AfterTheCameraIsPlaced()
    {
        string late = Method(Script("SpaceView.Hud.cs"), "private void LateUpdate()");
        int scan = late.IndexOf("DrawPendingScanLock();", StringComparison.Ordinal);
        int lockHud = late.IndexOf("DrawTargetHud();", StringComparison.Ordinal);
        Assert.True(scan >= 0 && lockHud > scan, "LateUpdate draws the scan frame, then the target lock's HUD");

        string scanner = Script("SpaceView.Scanner.cs");
        string update = Method(scanner, "private void UpdateScanner(float dt)");
        Assert.DoesNotContain("DrawScanLock(", update); // no longer in Update, before PlaceCamera
        Assert.Contains("_scanDrawFrame = Time.frameCount;", update);

        string pending = Method(scanner, "private void DrawPendingScanLock()");
        Assert.Contains("_scanDrawFrame != Time.frameCount", pending); // only a frame the scanner ran
        Assert.Contains("DrawScanLock(_scanDrawTarget, _scanDrawLook, _scanDrawPing);", pending);
        Assert.Contains("_scanDrawFrame = -1;", Method(scanner, "private void StopScanner()")); // a stop is not undone late

        // One declaration and that one call — no other path draws the frame.
        Assert.Equal(2, Count(ClientTestPaths.ScriptPartsText("SpaceView"), "DrawScanLock("));
    }

    [Fact]
    public void TheFrameItsLabelAndItsEffects_SitOnTheDrawnModel_TheTargetChoiceOnTheServersData()
    {
        string scanner = Script("SpaceView.Scanner.cs");
        string anchor = Method(scanner, "private Vector3 ScanDrawWorld(ScanTarget target)");
        Assert.Contains("SpaceTargeting.ScanAnchorFor(target.IsBody, modelShown, hullShown)", anchor);
        Assert.Contains("_entities.TryGetValue(target.Id, out model) && model != null && model.activeSelf", anchor);
        Assert.Contains("_structs.TryGetValue(target.Id, out hull) && hull != null", anchor);
        Assert.Contains("return model.transform.position;", anchor);
        Assert.Contains("return _root.transform.TransformPoint(hull.Pos);", anchor);

        Assert.Contains("Vector3 world = ScanDrawWorld(target);", Method(scanner, "private void DrawScanLock(ScanTarget target, FxLook look, bool ping)"));
        Assert.Contains("Vector3 center = ScanDrawWorld(target);", Method(scanner, "private void ChargeFx(ScanTarget target, FxLook look, bool ping, float dt)"));
        Assert.Contains("Vector3 center = ScanDrawWorld(target);", Method(scanner, "private void CompleteScan(ScanTarget target, FxLook look, bool ping)"));
        Assert.Contains("var center = ScanDrawWorld(target);", Method(scanner, "private void UpdateScanner(float dt)")); // the hologram box
        Assert.Equal(1, Count(scanner, "TransformPoint(target.Local)")); // only the snapshot fallback is left

        // The range check and the aim still read the server's position.
        string best = Method(scanner, "private ScanTarget BestScanTarget(ShipScannerSpec scanner, string keep)");
        Assert.Contains("var pos = new Vector3(e.X, e.Y, e.Z);", best);
        Assert.Contains("ScanRangeDistance(e, shipPos, dist) > scanner.Range", best);
    }
}
