// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.World;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The hand scanner's result panel of <see cref="HudUi"/> (bottom-left, #482): subject, description, threat and
    /// knowledge, sized to the text it shows (#1712).
    /// </summary>
    public sealed partial class HudUi
    {
        // Scan panel (bottom-left), in HUD reference units. Width is CAPPED at 390: the hotbar backplate
        // starts at x 400, so a wider panel clips it. The left column above is VEGA's (speech 396…586,
        // objective chip 594…642 — see VegaPanel), so this panel starts at 650 (#482).
        private const float ScanPanelY = 650f, ScanPanelW = 390f, ScanPanelH = 182f;

        /// <summary>How long a scan readout lingers once the scanner is put away. Was 12 s — long enough to
        /// read the old one-liner, not the fuller readout (#482). While the scanner is still the held item
        /// the panel stays pinned regardless (see <see cref="RefreshScan"/>).</summary>
        private const float ScanHoldSeconds = 20f;

        // Scan / wreck panels.
        private GameObject _scanPanel;
        private TMP_Text _scanSubject, _scanInfo, _scanThreat, _scanKnow;

        private void BuildScan(Transform root)
        {
            // Scan result panel (bottom-left): the scanner's detail readout — subject, description,
            // threat, knowledge, and a highlighted "new discovery" line on a first-time scan. Enlarged in
            // #482 (it carried the densest text in the game at the smallest size). WIDTH IS CAPPED: the
            // hotbar backplate owns x 400…1136, so this panel must not reach x 400.
            _scanPanel = Panel(root, 10, ScanPanelY, ScanPanelW, ScanPanelH).gameObject;
            var scanIcon = UiKit.Icon("item_advanced_scanner") ?? UiKit.Icon("cat_target");
            float scanTextX = 12f;
            if (scanIcon != null)
            {
                UiKit.AddImage(_scanPanel.transform, 10, 8, 26, 26, scanIcon, UiKit.Cyan);
                scanTextX = 42f;
            }

            const float scanTextW = ScanPanelW - 24f;
            _scanSubject = UiText.Add(_scanPanel.transform, scanTextX, 8, ScanPanelW - 12f - scanTextX, 26, string.Empty, 19, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            _scanInfo = UiText.Add(_scanPanel.transform, 12, 40, scanTextW, 78, string.Empty, 17, UiKit.TextCol, TextAnchor.UpperLeft);
            UiText.Wrap(_scanInfo, truncate: true);
            // Truncate, NOT the AddText default Overflow: a long yield list (asteroids report every
            // distinct resource) used to run straight over the threat + knowledge lines (#482).
            // (truncate: see UiText.Wrap above)
            _scanThreat = UiText.Add(_scanPanel.transform, 12, 122, scanTextW, 22, string.Empty, 16, UiKit.TextCol, TextAnchor.MiddleLeft);
            _scanKnow = UiText.Add(_scanPanel.transform, 12, 148, scanTextW, 26, string.Empty, 17, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiText.Style(_scanSubject, UiText.Look.Outline);
            UiText.Style(_scanInfo, UiText.Look.Outline);
            UiText.Style(_scanThreat, UiText.Look.Outline);
            UiText.Style(_scanKnow, UiText.Look.Outline);
        }

        private void RefreshScan(BlocksBeyondTheStars.Shared.Localization.Localizer loc)
        {
            var scan = Game.LastScan;
            // Pinned while the scanner is still in hand (you're actively surveying — the readout is the
            // point), otherwise it lingers ScanHoldSeconds after the scan and fades out (#482).
            // #2247: this panel is the HAND scanner's. In the space view the on-foot hotbar is frozen — its
            // scanner slot used to pin the last surface scan over the whole flight — and the ship scanner's
            // readouts open the card on the right (PlanetOverviewCard), so a ship readout never lands here.
            bool show = scan != null && !Game.SpaceViewActive && !Game.LastScanFromShip
                        && (HoldingScanner() || Time.time - Game.LastScanAt <= ScanHoldSeconds);
            if (_scanPanel.activeSelf != show)
            {
                _scanPanel.SetActive(show);
                if (show) { UiKit.TransitionIn(_scanPanel); }
            }

            if (!show) return;
            _scanSubject.text = $"{loc.Get("ui.scan.title").ToUpperInvariant()}: {ScanReadoutText.Title(Game.Content, loc, scan)}";
            _scanInfo.text = ScanReadoutText.Info(Game.Content, loc, scan) + ScanToolLine(loc, scan) + ScanSentryLine(loc, scan);
            // The threat WORD comes from a locale key now; `scan.Threat` is the legacy English fallback (#484).
            string threat = !string.IsNullOrEmpty(scan.ThreatKey) ? loc.Get(scan.ThreatKey) : scan.Threat;
            _scanThreat.gameObject.SetActive(!string.IsNullOrEmpty(threat) && threat != "—");
            _scanThreat.text = $"{loc.Get("ui.scan.threat")}: {threat}";

            // A first-time discovery shows its knowledge GAIN highlighted; re-scans just show the total.
            if (scan.FirstTime && scan.KnowledgeGained > 0)
            {
                _scanKnow.color = new Color(0.45f, 1f, 0.6f);
                _scanKnow.text = $"{loc.Get("ui.scan.first_time")}  +{scan.KnowledgeGained}  ({loc.Get("ui.scan.knowledge")}: {scan.KnowledgeTotal})";
            }
            else
            {
                _scanKnow.color = UiKit.TextCol;
                _scanKnow.text = $"{loc.Get("ui.scan.knowledge")}: {scan.KnowledgeTotal}";
            }

            ReflowScanPanel();
        }

        /// <summary>
        /// Sizes the scan panel to the text it is actually showing (#1712).
        /// <para>
        /// Every row used to be a constant: the info block was 78 px — three lines at size 17 — with the threat
        /// and knowledge rows nailed to y=122 and y=148 inside a panel of fixed height. The tool-tier line added
        /// in #1686 makes the sentry's block four lines, so the fourth was simply clipped and a player read
        /// "Schießt 14 Blöcke weit · braucht einen Grundstein in" with the rest of the sentence gone.
        /// </para>
        /// The panel grows UPWARD — its bottom edge is what must stay put, because it sits just above the
        /// screen edge and beside the hotbar backplate. A hidden threat row now closes its gap too, which is
        /// the dead space visible in that same report.
        /// </summary>
        private void ReflowScanPanel()
        {
            const float infoTop = 40f, infoMinH = 78f, rowGap = 4f, threatH = 22f, knowH = 26f, padBottom = 8f;
            const float textW = ScanPanelW - 24f;
            // Ceiling, so the panel grows for a wrapped sentence but never for a runaway list: an asteroid
            // reports every distinct resource it holds, and #482 already had that text running over the rows
            // below. Past this the truncation set up in Build takes over again, as it did before.
            const float infoMaxH = 160f;

            // TMP measures the wrapped text itself; a height of 0 asks for "as tall as it needs".
            float infoH = Mathf.Clamp(
                _scanInfo.GetPreferredValues(_scanInfo.text, textW, 0f).y, infoMinH, infoMaxH);
            UiKit.Place(_scanInfo.gameObject, 12f, infoTop, textW, infoH);

            float y = infoTop + infoH + rowGap;
            if (_scanThreat.gameObject.activeSelf)
            {
                UiKit.Place(_scanThreat.gameObject, 12f, y, textW, threatH);
                y += threatH + rowGap;
            }

            UiKit.Place(_scanKnow.gameObject, 12f, y, textW, knowH);
            y += knowH + padBottom;

            // Keep the bottom edge where the design put it: grow up, never down into the hotbar.
            float panelH = Mathf.Max(ScanPanelH, y);
            UiKit.Place(_scanPanel, 10f, ScanPanelY + ScanPanelH - panelH, ScanPanelW, panelH);
        }

        /// <summary>
        /// The tool a scanned BLOCK wants, as a second info line (#1686). Tool-tier gates were invisible until
        /// you swung at them: a Machine Housing refused a Basic Drill with "your current tool cannot mine this
        /// block" and never said what would. Shown on every scan of a gated block, whether or not the tool in
        /// hand already clears it — the readout is a datasheet, not a warning, and a player planning a trip
        /// wants to know what to bring. Blocks anything can break (dirt, sand, flora) get no line, and neither
        /// do creature or asteroid scans, whose subject is not a block key.
        /// </summary>
        private string ScanToolLine(BlocksBeyondTheStars.Shared.Localization.Localizer loc,
            BlocksBeyondTheStars.Networking.Messages.ScanResult scan)
        {
            var content = Game.Content;
            if (content == null
                || content.GetBlock(scan.Subject) is not { } block
                || (block.RequiredTool == BlocksBeyondTheStars.Shared.Definitions.ToolKind.None
                    && block.MinToolTier <= 0))
            {
                return string.Empty;
            }

            // Names a CONCRETE tool rather than an abstract tier: "a drill of tier 2" is not something a
            // player can act on, "Titanium Drill" is. Null when nothing in the content set opens this block
            // (a decorative wall) — then the line would only frustrate, so it stays away.
            var wanted = BlocksBeyondTheStars.Shared.Content.MiningRules.CheapestToolFor(content, block);
            return wanted is null ? string.Empty : $"\n{loc.Get("ui.scan.tool")}: {loc.Get(wanted.NameKey)}";
        }

        /// <summary>The two numbers a scanned SENTRY POST is asked about (#1699): how far it shoots, and how
        /// close to a base core it has to stand to work at all. A player who lined a long perimeter wall with
        /// posts had no way of learning that everything outside the eight-block base zone is decoration — the
        /// item text said "place it inside your own base" and named no distance. Only for this one block, so
        /// the readout stays a datasheet rather than a wall of caveats.</summary>
        private string ScanSentryLine(BlocksBeyondTheStars.Shared.Localization.Localizer loc,
            BlocksBeyondTheStars.Networking.Messages.ScanResult scan)
            => scan.Subject == "sentry_post"
                ? "\n" + loc.Get("ui.scan.sentry")
                    .Replace("{range}", BlocksBeyondTheStars.Shared.World.WorldConstants.SentryRange.ToString())
                    .Replace("{zone}", BlocksBeyondTheStars.Shared.World.WorldConstants.BaseZoneRadius.ToString())
                : string.Empty;
    }
}
