// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using BlocksBeyondTheStars.Client.Portal;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The redesigned Crafting / Tech / Ship screens (UX concept) — a uGUI three-pane UI built in code on
    /// a DPI-independent canvas (UiKit.CreateCanvas: ScaleWithScreenSize @1920×1080, Expand), so it looks
    /// right on a high-DPI 4K monitor and on a normal 1080p screen alike. Replaces the cramped IMGUI lists
    /// with a category sidebar, a searchable / "craftable now"-filterable card list, and a detail pane that
    /// shows ingredients (have/need, pooled from inventory + cargo), required station/blueprint, the benefit,
    /// and a clear reason when an action is blocked. Driven by <see cref="GameMenu"/>; location-bound
    /// (crafting=workshop, tech=lab, ship=ship console) with a hint when you're not at the station.
    /// </summary>
    public sealed partial class CraftingTechShipUI : MonoBehaviour
    {
        public GameBootstrap Game;
        public GameMenu Menu;

        // Values match GameMenu.Tab so the whole in-game menu runs on this one uGUI screen. (Launching into
        // space lives on the Map tab now — there is no separate Space tab.) How the pages are reached — eight main tabs, the
        // grouped ones with sub-tabs (#3671) — is the Groups table in CraftingTechShipUI.Header.cs. Mode.Character is labelled
        // "Character" (#2505; "Settings" before — the key is still ui.tab.settings): it holds the character page, the people
        // you know and the in-world options.
        public enum Mode { Inventory = 0, Crafting = 1, Tech = 2, Ship = 3, Map = 4, Missions = 5, Character = 6, Alliances = 7, Story = 8, Companions = 9, Photos = 10, Achievements = 11 }

        private Canvas _canvas;
        private RectTransform _sidebar, _listContent, _detail, _header;
        private Text _footer, _hint, _feedback;
        private Mode _mode = Mode.Crafting;
        private string _category = "all";
        private string _selected = string.Empty;
        private string _search = string.Empty;

        // Celebration state (craft/unlock juice): the card with this content key pulses until the
        // deadline and a floating label announces the result. Fed by CraftCompleted + the
        // unlocked-blueprints diff in Update.
        private string _celebrateKey;
        private float _celebrateUntil;
        private System.Collections.Generic.HashSet<string> _knownBlueprints;
        private bool _craftHooked;
        private bool _craftableOnly;
        private int _lastDataHash = -1;
        private string _feedbackShown = string.Empty; // the server message the footer currently shows (#1484: refreshed in place, not via the hash)
        private bool _built;
        // The page (mode+category / selection) last rendered into each scroll view. A rebuild that changes
        // the page scrolls back to the top; an in-place refresh (live data, volume/colour cycling) keeps the
        // player's scroll position. Without this, switching from a scrolled-down page (e.g. Character) to a
        // short one (e.g. Space) leaves it scrolled past the top, hiding the first rows (the launch button).
        private string _listPage, _detailPage;

        private const float W = 1920f, H = 1080f;

        // Holo chrome (the HUD's UiHolo panels, not the bitmap sprite) for the three frames, so the menu can
        // boot the way the HUD does: each frame wipes on left→right with a short stagger while its pane's
        // content fades up behind the wipe. Frames in sidebar → list → detail order; a null Shape means the
        // holo shader is unavailable (bitmap fallback) and only the fades play.
        private readonly Image[] _frames = new Image[3];
        private readonly CanvasGroup[] _paneGroups = new CanvasGroup[3];
        private CanvasGroup _headerGroup;

        // --- public control (from GameMenu) ---

        public void ShowMode(Mode mode)
        {
            EnsureBuilt();
            bool changed = _mode != mode || !_canvas.enabled;
            if (!changed)
            {
                return; // already showing this mode; Update() handles live refresh
            }

            _mode = mode;
            _lastSub[GroupOf(mode)] = (int)mode; // #3671: the group opens on this page next time
            CloseSlotPicker(); // #2289: the picker belongs to the Suit page it was opened from
            _avatarPreview?.SetActive(mode == Mode.Character); // only render the live preview on the colour tab
            _shipPreview?.SetActive(false); // re-enabled by the paint detail pane when that category is shown
            // The Inventory tab has no "all" page (its pages are Backpack / Suit / Cargo hold / Samples) — landing on "all"
            // showed an orphaned card list without the grid or the stow button, so it opens on the backpack.
            _category = !string.IsNullOrEmpty(_pendingCategory) ? _pendingCategory
                : mode == Mode.Inventory ? "personal" : "all";
            _pendingCategory = null;
            _selected = string.Empty;
            _search = string.Empty;
            _craftableOnly = false;
            _invFilter = string.Empty;
            _invSort = 0;
            _lastDataHash = -1;
            _listPage = _detailPage = null; // a fresh open / tab switch always scrolls both panes to the top
            _canvas.enabled = true;
            BuildHeader();
            RebuildSidebar();
            RebuildList();
            RebuildDetail();
            PlayReveal(); // the HUD's boot-up feel on open + tab change
        }

        /// <summary>
        /// The menu's take on the HUD's boot reveal (<see cref="HudUi"/> → <see cref="UiHolo.PlayReveal"/>):
        /// the whole screen still fades (backdrop, logo, footer — the old TransitionIn), the header fades
        /// first, then the three holo frames wipe on left→right one after another and each pane's content
        /// fades up once its frame's wipe is past the half-way mark. Played on open AND on every tab change
        /// (ShowMode) — never on the live rebuilds Update triggers, those would flicker. Timings are kept
        /// short enough that LB/RB cycling through the tabs stays snappy; a re-trigger mid-play restarts
        /// cleanly because every tween is keyed to its target. Instant under reduced motion.
        /// </summary>
        private void PlayReveal()
        {
            UiKit.TransitionIn(_canvas.gameObject);
            if (UiKit.ReducedMotion)
            {
                if (_headerGroup != null)
                {
                    _headerGroup.alpha = 1f;
                }

                for (int i = 0; i < _frames.Length; i++)
                {
                    var s = _frames[i] != null ? _frames[i].GetComponent<UiHolo.Shape>() : null;
                    if (s != null)
                    {
                        s.Reveal = 1f;
                    }

                    if (_paneGroups[i] != null)
                    {
                        _paneGroups[i].alpha = 1f;
                    }
                }

                return;
            }

            const float wipe = 0.30f, stagger = 0.07f, lead = 0.04f, contentFade = 0.22f;
            if (_headerGroup != null)
            {
                _headerGroup.alpha = 0f;
                UiTween.Alpha(_headerGroup, 1f, 0.18f, UiTween.Ease.OutQuad);
            }

            for (int i = 0; i < _frames.Length; i++)
            {
                float at = lead + i * stagger;
                var shape = _frames[i] != null ? _frames[i].GetComponent<UiHolo.Shape>() : null;
                if (shape != null)
                {
                    UiTween.Kill(shape);
                    shape.Reveal = 0f;
                    UiTween.To(0f, 1f, wipe, r => { if (shape != null) { shape.Reveal = r; } }, UiTween.Ease.OutCubic, at, null, shape);
                }

                var group = _paneGroups[i];
                if (group != null)
                {
                    group.alpha = 0f;
                    UiTween.Alpha(group, 1f, contentFade, UiTween.Ease.OutQuad, at + wipe * 0.45f);
                }
            }
        }

        private string _pendingCategory; // a category to select when the mode next opens (e.g. "market")

        /// <summary>Requests a category be selected when this panel opens (used to jump straight to the
        /// market when the player talks to a vendor).</summary>
        public void RequestCategory(string category)
        {
            _pendingCategory = category;
            if (_canvas != null && _canvas.enabled && !string.IsNullOrEmpty(category))
            {
                _category = category;
                _pendingCategory = null;
                _selected = string.Empty;
                BuildHeader(); // the header strip follows the page (the Samples filter, #2300)
                RebuildSidebar();
                RebuildList();
                RebuildDetail();
            }
        }

        public void Hide()
        {
            if (_canvas != null)
            {
                // #1804: this screen closes programmatically (GameMenu.CloseForTransition on a hyperjump / transit
                // arrival) while the player may be typing a Funk line or a photo note. A field left focused under a
                // disabled canvas throws in the next caret rebuild — hand focus back before the canvas goes.
                UiKit.ReleaseTextFieldFocus(_canvas.transform);
                UiKit.DropSelectionUnder(_canvas.transform); // #3921: no tab or "Pause menu" button stays selected under the hidden canvas
                _canvas.enabled = false;
            }

            CloseDiscardGearDialog(); // #1484: a half-answered "throw away the helmet lamp?" dies with the menu
            CloseSlotPicker();        // #2289: and so does an open slot picker
            CloseStorySwitchDialog(); // #2506: and the "change the main story?" question
            _avatarPreview?.SetActive(false); // stop rendering the preview camera while the menu is closed
            _shipPreview?.SetActive(false);
        }

        private void Update()
        {
            if (!_built || _canvas == null || !_canvas.enabled || Game == null)
            {
                return;
            }

            if (!_craftHooked && Game.Network != null)
            {
                Game.Network.CraftCompleted += OnCraftResult;
                _craftHooked = true;
            }

            // LB / RB step through the main tabs (#1409, #3671) — the console convention, and the only alternative on
            // a pad is walking the stick back up through a whole list. The two buttons are place / mine in
            // the world, but an open screen freezes player control, so they are free here (the same way B
            // is both crouch and cancel). Not while the on-screen keyboard has the pad, and not while the
            // appearance editor sits on top of the Character tab — LB is its fill modifier. Nor while the suit's slot
            // picker (#2289) or the calendar (#3316) is up: a tab change under the modal would leave it over a page that is gone.
            if (!UiKit.TextFieldFocused() && Menu?.AppearanceEditorOpen != true && Menu?.TextureEditorOpen != true
                && _pickerCanvas == null && !UiFestivalCalendar.IsOpen)
            {
                if (InputMap.PadDown(PadButton.Rb))
                {
                    CycleTab(+1);
                }
                else if (InputMap.PadDown(PadButton.Lb))
                {
                    CycleTab(-1);
                }
            }

            DetectBlueprintUnlocks();

            // Item↔slot signature so a pure quick-bar swap (count + length unchanged) still triggers a rebuild
            // (B58). Manual unchecked loop — LINQ Sum on large hash products would overflow.
            int slotSig = 0;
            if (Game.Personal != null)
            {
                foreach (var s in Game.Personal)
                {
                    unchecked { slotSig = slotSig * 31 + s.Slot * 92821 + (s.Item?.GetHashCode() ?? 0); }
                }
            }

            if (Game.Equipment != null) // #2110: a change of clothes redraws the grid too
            {
                foreach (var s in Game.Equipment)
                {
                    unchecked { slotSig = slotSig * 31 + (s.Slot + 1000) * 92821 + (s.Item?.GetHashCode() ?? 0); }
                }
            }

            // Refresh when the authoritative data the screen shows changes (cheap hash).
            int h = (Game.Personal?.Length ?? 0) * 7 + (Game.Cargo?.Length ?? 0) * 13 + Game.UnlockedBlueprints.Count * 31
                    + (Game.Personal?.Sum(s => s.Count) ?? 0) + (Game.OwnedShips?.Length ?? 0) * 101 + slotSig
                    + (string.IsNullOrEmpty(Game.NearbyStation) ? 0 : Game.NearbyStation.GetHashCode())
                    // #1070: the server-published station gates + the "where is it" answer drive the tab dimming, hint row and reasons.
                    + StationsSig() * 3
                    + (Game.StarMap?.Systems.Length ?? 0) * 211 + (Game.StarMap?.ActiveLocationId?.GetHashCode() ?? 0)
                    + (Game.Missions?.Available.Length ?? 0) * 307 + (Game.Missions?.Active.Length ?? 0) * 401
                    + (Game.KnownNpcs?.Length ?? 0) * 503 // #1118: the roster arrives async
                    // #1484: the space contact list and the last HUD message are NOT hashed any more. Neither is
                    // shown by any tab, yet in flight (contacts drifting in and out, autopilot chatter) they
                    // changed every few seconds and rebuilt the open Inventory tab under the cursor — the item
                    // beneath a half-confirmed "throw away" click silently became a different one. The footer
                    // message refreshes in place instead (see Update).
                    + (Game.InSpace ? 7777 : 0)
                    // Aboard / ship-interior state drives the Map tab dimming + travel-button gating, so a change
                    // (board/leave the ship with the menu open) must rebuild the header + map buttons.
                    + (Game.Aboard ? 8887 : 0) + (Game.LoadingPlanetType?.GetHashCode() ?? 0)
                    + (Game.Visiting ? 9901 : 0) // #2725: a guest's Map tab offers "back to my ship" instead of the helm
                    // Alliances tab: roster + pending changes refresh the lists; the online-player count drives the
                    // "find players" picker. The radio (Funk) feed is deliberately NOT hashed — its log refreshes in
                    // place each frame so an incoming message never rebuilds the pane and steals the input's focus.
                    + (Game.Alliances?.Allies.Length ?? 0) * 601 + (Game.Alliances?.Incoming.Length ?? 0) * 701
                    + (Game.Alliances?.Outgoing.Length ?? 0) * 809 + (Game.StarMap?.Players.Length ?? 0) * 53
                    // Knowledge total + the new-content flags drive the Tech/Story/Arcade menu badges.
                    + Game.Knowledge * 131 + (Game.NewArcadeUnseen ? 1201 : 0) + (Game.NewStoryUnseen ? 1303 : 0)
                    // Companions tab: roster length + present-count + the "new companion" badge flag.
                    + (Game.Companions?.Companions.Length ?? 0) * 907 + (Game.NewCompanionUnseen ? 1409 : 0)
                    + (Game.Companions?.Companions.Count(c => c.Present) ?? 0) * 67
                    // Notes (#1844): count + the server-answer counter only — never the note text, or every
                    // keystroke echoed by the server would rebuild the editor under the player's cursor.
                    + (Game.Notes?.Length ?? 0) * 1511 + Game.NotesVersion * 1523
                    // #2201: the sample case and the research book (a sample taken, a species analysed) — the Samples page.
                    + Game.Bio.Revision * 1613
                    // #2471: the open Journal follows the server's stories, the objective list, the story state and VEGA's notes
                    // (only while it is the page on screen — a progress tick must never rebuild another tab under the cursor).
                    + (_mode == Mode.Story
                        ? Game.JournalVersion * 1709 + Game.VegaLogKeys.Count * 1721 + (Game.Story?.GetHashCode() ?? 0)
                        : 0)
                    // …and the Settings' "Tutorial: on / off" follows a new story list (sent on a state change, never per tick).
                    + (_mode == Mode.Character ? (Game.StoryPackStates?.GetHashCode() ?? 0) + CalendarHash() : 0) // #3316: calendar
                    // #2140: a planet-scanner report arrived, or a module was built / removed (the scan button's gate).
                    + Game.PlanetScanVersion * 1601 + (Game.ShipCombat?.Modules?.Length ?? 0) * 1607
                    // The local custom pixel face + body paintings: applying one in the editor must rebuild the
                    // Character tab so the live preview re-applies it (SetFace/SetBodyPaint run on rebuild).
                    + (Game.FacePixels?.GetHashCode() ?? 0)
                    + (Game.BodyPaintPixels[0]?.GetHashCode() ?? 0) * 3 + (Game.BodyPaintPixels[1]?.GetHashCode() ?? 0) * 5
                    + (Game.BodyPaintPixels[2]?.GetHashCode() ?? 0) * 7 + (Game.BodyPaintPixels[3]?.GetHashCode() ?? 0) * 11
                    // …and the four base colours, which the appearance screen can now change while it is open
                    // (#899) — the Character card shows them as swatches and the preview tints from them.
                    + (Menu?.Settings?.SkinColor.GetHashCode() ?? 0) * 13 + (Menu?.Settings?.TorsoColor.GetHashCode() ?? 0) * 17
                    + (Menu?.Settings?.ArmColor.GetHashCode() ?? 0) * 19 + (Menu?.Settings?.LegColor.GetHashCode() ?? 0) * 23;
            if (h != _lastDataHash)
            {
                _lastDataHash = h;
                BuildHeader();
                RebuildSidebar(); // map systems / sections can arrive async
                RebuildList();
                RebuildDetail();
            }

            // #2289: an open slot picker lists what the backpack and the hold carry and what is worn NOW — rebuilt on an
            // inventory change only, not on every refresh of the page behind it (that would throw the pad to its first row).
            // The hold by slot and item too, not just its length: a row carries its hold slot, and a stack taken while
            // another arrived must not leave a row pointing at a slot that now holds something else.
            int pickerSig = unchecked(slotSig * 17 + (AboardShipNow() ? 1 : 0));
            if (Game.Cargo != null)
            {
                foreach (var s in Game.Cargo)
                {
                    unchecked { pickerSig = pickerSig * 31 + (s.Slot + 2000) * 92821 + (s.Item?.GetHashCode() ?? 0); }
                }
            }

            if (_pickerSlot >= 0 && pickerSig != _pickerSig)
            {
                ShowSlotPicker(_pickerSlot);
            }

            _pickerSig = pickerSig;

            // #1484: the footer echoes the latest server message in place — it used to ride the data hash and
            // rebuilt every tab for a HUD line. A custom footer text (a validation hint) stays until the next
            // server message arrives.
            string latest = Game.LastMessage ?? string.Empty;
            if (_feedback != null && latest != _feedbackShown)
            {
                _feedbackShown = latest;
                _feedback.text = latest;
            }

            // Live radio scrollback: keep the Funk view current without a rebuild (preserves typing focus).
            if (_mode == Mode.Alliances && _category != "funk" && _category != "find")
            {
                // The crew state (#1216) or a duel (#2972) changes under an open view: rebuild when the server pushes a
                // different roster or duel, so the buttons always match reality (AlliancesLiveStamp).
                string stamp = AlliancesLiveStamp();
                if (stamp != _crewStamp)
                {
                    _crewStamp = stamp;
                    RebuildList();
                    RebuildDetail();
                }
            }

            if (_mode == Mode.Alliances && _category == "funk" && _funkLog != null)
            {
                _funkLog.text = ComposeFunkLog();
            }

            // Live "where is it" readout (#1072): distance + arrow follow the player without a rebuild, and the
            // locate request is re-armed every few seconds so the nearest station tracks the player walking.
            if (Time.unscaledTime - _whereRefreshedAt > 0.2f && (_mode == Mode.Crafting || _mode == Mode.Tech || _mode == Mode.Ship))
            {
                _whereRefreshedAt = Time.unscaledTime;
                string missing = MissingStation();
                if (missing != null)
                {
                    RequestLocate(missing);
                    _whereText.text = WhereText(missing);
                }
            }
        }

        /// <summary>Craft success → the crafted item's card pulses + a "+ item" label floats up (the
        /// failure path already reads via the feedback line + error tone).</summary>
        private void OnCraftResult(BlocksBeyondTheStars.Networking.Messages.CraftResult m)
        {
            if (!m.Success || Game?.Content == null
                || !Game.Content.Recipes.TryGetValue(m.RecipeKey ?? string.Empty, out var recipe))
            {
                return;
            }

            var output = recipe.Outputs.FirstOrDefault();
            if (output == null)
            {
                return;
            }

            // The transmuter gets its own matter-synthesis sting over the generic craft feedback.
            if (recipe.Station == BlocksBeyondTheStars.Shared.Definitions.CraftingStation.Transmuter)
            {
                ClientAudio.Instance?.Cue("matter_synth", 0.8f);
            }

            // A factory craft is a heavy machine stamping out the goods — play the press clunk.
            if (recipe.Station == BlocksBeyondTheStars.Shared.Definitions.CraftingStation.Factory)
            {
                ClientAudio.Instance?.Cue("factory_craft", 0.9f);
            }

            _celebrateKey = output.Item;
            _celebrateUntil = Time.unscaledTime + 2.5f;
            SpawnFloatLabel("+ " + ItemName(output.Item), UiKit.Ok);
            _lastDataHash = 0; // force a rebuild so the pulsing card attaches
        }

        /// <summary>Client-side blueprint-unlock detection: the server confirms an unlock only via the
        /// inventory snapshot (no dedicated message), so diff the unlocked set. The first observation
        /// just baselines, and a multi-key jump is a join/admin snapshot — neither fires the fanfare.</summary>
        private void DetectBlueprintUnlocks()
        {
            var cur = Game.UnlockedBlueprints;
            if (cur == null)
            {
                return;
            }

            if (_knownBlueprints == null)
            {
                _knownBlueprints = new System.Collections.Generic.HashSet<string>(cur);
                return;
            }

            string fresh = null;
            int freshCount = 0;
            foreach (var key in cur)
            {
                if (!_knownBlueprints.Contains(key))
                {
                    fresh = key;
                    freshCount++;
                }
            }

            if (freshCount == 0)
            {
                return;
            }

            _knownBlueprints = new System.Collections.Generic.HashSet<string>(cur);
            if (freshCount > 1)
            {
                return;
            }

            _celebrateKey = fresh;
            _celebrateUntil = Time.unscaledTime + 3f;
            ClientAudio.Instance?.Sting("sting_blueprint", "tech_unlock", 0.8f); // the accent, else the plain unlock chime
            SpawnFloatLabel(L($"blueprint.{fresh}.name") + " — " + L("ui.tech.unlocked"), UiKit.Cyan);
            _lastDataHash = 0; // rebuild so the unlocked node pulses + statuses refresh
        }

        /// <summary>A celebration label that rises from the centre of the menu and fades out.</summary>
        private void SpawnFloatLabel(string text, Color color)
        {
            if (_canvas == null)
            {
                return;
            }

            var t = UiKit.AddText(_canvas.transform, W * 0.5f - 400f, H * 0.42f, 800f, 44f, text, 30,
                color, TextAnchor.MiddleCenter, FontStyle.Bold);
            t.raycastTarget = false;
            t.gameObject.AddComponent<FloatLabel>();
        }

        private sealed class FloatLabel : MonoBehaviour
        {
            private const float Life = 1.6f;
            private float _t;
            private Text _text;

            private void Awake() => _text = GetComponent<Text>();

            private void Update()
            {
                _t += Time.unscaledDeltaTime;
                transform.localPosition += Vector3.up * (Time.unscaledDeltaTime * 46f);
                if (_text != null)
                {
                    var c = _text.color;
                    c.a = Mathf.Clamp01(1f - _t / Life);
                    _text.color = c;
                }

                if (_t >= Life)
                {
                    Destroy(gameObject);
                }
            }
        }

        /// <summary>Pulses a card's background toward a celebratory green-cyan until the deadline.</summary>
        private sealed class CardPulse : MonoBehaviour
        {
            public float Until;

            private Image _img;
            private Color _base;

            private void Awake()
            {
                _img = GetComponent<Image>();
                if (_img != null)
                {
                    _base = _img.color;
                }
            }

            private void Update()
            {
                if (_img == null || Time.unscaledTime >= Until)
                {
                    if (_img != null)
                    {
                        _img.color = _base;
                    }

                    Destroy(this);
                    return;
                }

                float k = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 7f);
                _img.color = Color.Lerp(_base, new Color(0.30f, 0.95f, 0.75f, 0.95f), k * 0.6f);
            }
        }

        // --- construction ---

        private void OnDestroy()
        {
            if (_craftHooked && Game?.Network != null)
            {
                Game.Network.CraftCompleted -= OnCraftResult;
            }

            // Top-level canvas — destroy it with the component so the menu doesn't linger after teardown.
            if (_canvas != null)
            {
                Destroy(_canvas.gameObject);
            }

            // The confirms and the suit-slot picker are top-level canvases too. Closing the menu drops them, but the rig
            // teardown does not close the menu (#2936).
            CloseStorySwitchDialog();
            CloseDiscardGearDialog();
            if (_pickerCanvas != null)
            {
                Destroy(_pickerCanvas);
            }
        }

        private void EnsureBuilt()
        {
            if (_built)
            {
                return;
            }

            _canvas = UiKit.CreateCanvas("CraftingTechShipUI");
            _canvas.sortingOrder = 50;
            // The pad drives every tab. This MUST sit on the canvas, not on GameMenu's GameObject:
            // CreateCanvas returns a scene-root object, so a UiNavFocus on the owner sees nothing (#1198).
            UiNav.Enable(_canvas.gameObject);
            UiNav.AddHint(_canvas.gameObject, "ui.pad.tabs", PadButton.Lb, PadButton.Rb); // LB/RB step the main tabs (#1409)
            var root = _canvas.transform;

            // Full-screen backdrop, opaque (#3671): the console hides the game view behind it. It used to be a 60 % dim, so
            // the world and the HUD's own text showed through and fought with the console's text.
            UiKit.AddImage(root, 0, 0, W, H, UiKit.SolidSprite, new Color(0.02f, 0.04f, 0.08f, 0.96f));

            // The in-game menu is framed as the ship's computer, so it carries its own heading
            // ("Ship Interface", localized) instead of the game title.
            UiKit.AddLogo(root, 40, TitleY, 600, TitleH, L("ui.shipmenu.title"), 24);

            _header = new GameObject("Header", typeof(RectTransform)).GetComponent<RectTransform>();
            _header.SetParent(root, false);
            UiKit.Place(_header.gameObject, 0, 0, W, PaneTop);

            // Panels — holo chrome (same shader + colour family as the HUD's panels), see PlayReveal. The list frame and its
            // scroll view take the sidebar's place on the Inventory tab (ApplyPaneLayout).
            _frames[0] = UiHolo.AddPanel(root, 40, PaneTop, 320, PaneH, UiKit.Panel, 12f, 1.5f, 1f);    // sidebar
            _frames[1] = UiHolo.AddPanel(root, 380, PaneTop, 820, PaneH, UiKit.Panel, 12f, 1.5f, 1f);   // list
            _frames[2] = UiHolo.AddPanel(root, 1220, PaneTop, 660, PaneH, UiKit.Panel, 12f, 1.5f, 1f);  // detail

            _sidebar = MakeScroll(root, 50, PaneTop + 12f, 300, PaneH - 24f);
            _listContent = MakeScroll(root, ListX, ListTop, ListW, ListH);
            _detail = MakeScroll(root, 1232, 196, 636, 762); // literal: CraftDetailLayoutTests reads the detail width here

            // One CanvasGroup per scroll VIEW (the content under it is rebuilt per tab; the view survives),
            // so a pane's content can fade up behind its frame's wipe. Header likewise — BuildHeader clears
            // its children, not the header object itself.
            _paneGroups[0] = _sidebar.parent.gameObject.AddComponent<CanvasGroup>();
            _paneGroups[1] = _listContent.parent.gameObject.AddComponent<CanvasGroup>();
            _paneGroups[2] = _detail.parent.gameObject.AddComponent<CanvasGroup>();
            _headerGroup = _header.gameObject.AddComponent<CanvasGroup>();

            // Gate row between the tab bar and the panels (#1071/#1072): "what do I need" (hint), "where is it"
            // (live distance + arrow), a Show-on-compass button and a "craft one" jump. Used to sit INSIDE the
            // list panel at the search box's exact rect, so in Crafting mode the two overlapped. It shares the sub-tab row
            // (#3671): only the single-page tabs (Crafting, Blueprints, Ship, Map) ever fill it, and they have no sub-tabs.
            _hint = UiKit.AddText(root, 40, SubTabY, 1100, SubTabH, string.Empty, 20, new Color(1f, 0.8f, 0.4f), TextAnchor.MiddleLeft, FontStyle.Bold);
            _whereText = UiKit.AddText(root, 1150, SubTabY, 300, SubTabH, string.Empty, 20, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            _whereShow = UiKit.AddButton(root, 1460, SubTabY, 140, SubTabH, L("ui.craft.where_show"), () => MarkLocatedStation(MissingStation()));
            _whereCraft = UiKit.AddButton(root, 1610, SubTabY, 270, SubTabH, L("ui.craft.where_craft"), () =>
            {
                var r = RecipeForStationBlock(MissingStation());
                if (r != null)
                {
                    JumpToRecipe(r.Key);
                }
            });
            _whereShow.gameObject.SetActive(false);
            _whereCraft.gameObject.SetActive(false);
            _footer = UiKit.AddText(root, 40, 980, 1840, 36, string.Empty, 20, UiKit.CyanDim, TextAnchor.MiddleLeft);
            // Server feedback (craft/unlock/build result) — shown here since the HUD toast is hidden while a menu is open.
            _feedback = UiKit.AddText(root, 40, 1018, 1840, 30, string.Empty, 22, UiKit.Ok, TextAnchor.MiddleCenter, FontStyle.Bold);

            // Top-most "visor glass" overlay: the HUD's helmet look (cyan rim glow + faint scanlines), no
            // curvature, click-through — so the menu reads as inside the helmet without displacing its buttons.
            VisorMenuGlass.Add(root);

            _built = true;
        }

        // --- sidebar (categories) ---

        private void RebuildSidebar()
        {
            ClearChildren(_sidebar);
            if (WideList)
            {
                return; // #3671: the Inventory's pages are its sub-tabs (AddInventoryPages) — no sidebar
            }

            var cats = Categories();
            float y = 0f;
            foreach (var (key, label, icon) in cats)
            {
                // A "head:" entry is a non-selectable section heading (e.g. the travel screen's
                // "Current system" / "Hyperspace"). Auto-fit so a long localized heading stays inside the column.
                if (key.StartsWith("head:", System.StringComparison.Ordinal))
                {
                    if (y > 0f)
                    {
                        y += 10f; // a little air above a new section
                    }

                    var h = UiKit.AddText(_sidebar, 10, y, 270, 30, label, 17, UiKit.Cyan, TextAnchor.LowerLeft, FontStyle.Bold);
                    UiKit.FitLabel(h, 11, 17);
                    y += 36f;
                    continue;
                }

                string k = key;
                var b = UiKit.AddButton(_sidebar, 0, y, 290, 52, label, () =>
                {
                    _category = k;
                    _selected = string.Empty;
                    RebuildList();
                    RebuildDetail();
                }, SidebarIconName(icon));
                ApplySidebarItemIcon(b, icon); // #3103: a category shows a real item (the speeder for Travel & Transport)
                if (_category == key)
                {
                    b.GetComponent<Image>().color = UiKit.Cyan;
                }

                y += 58f;
            }

            SetContentHeight(_sidebar, y);
        }

        private List<(string key, string label, string icon)> Categories()
        {
            var list = new List<(string, string, string)> { ("all", L("ui.craft.cat_all"), "cat_all") };
            switch (_mode)
            {
                case Mode.Crafting:
                    // #3103: the shared categories (data/categories.json), grouped under headings — a recipe shows in every
                    // category its output belongs to — then the special pages (dye, shape, market) under "Extras".
                    list.AddRange(CraftCategoryEntries());
                    break;
                case Mode.Tech:
                    list.AddRange(TechCategoryEntries()); // #3104: the same categories, not the raw blueprint field
                    break;
                case Mode.Ship:
                    list.Add(("modules", L("ui.ship.cat_modules"), "cat_modules"));
                    list.AddRange(ModuleKindEntries()); // #3109: the modules by kind, right under "Modules"
                    list.Add(("fleet", L("ui.ship.cat_fleet"), "cat_fleet"));
                    list.Add(("build", L("ui.ship.cat_build"), "cat_buildship"));
                    list.Add(("paint", L("ui.ship.cat_paint"), "cat_suit"));
                    break;
                case Mode.Inventory:
                    list.Clear();
                    list.Add(("personal", L("ui.inventory.backpack"), "cat_inventory"));
                    list.Add(("suit", L("ui.inventory.suit"), "cat_suit")); // the backpack filtered to suit gear + its effects (#1270/#1271)
                    list.Add(("cargo", L("ui.cargo.title"), "cat_cargo"));
                    list.Add(("samples", L("ui.inventory.samples"), "cat_medicine")); // #2201: the sample case of the bio lab
                    if (CatalogAvailable())
                    {
                        list.Add(("catalog", L("ui.inventory.catalog"), "cat_all")); // Sandbox: every item, nothing to craft (#1930)
                    }

                    break;
                case Mode.Missions:
                    list.Clear();
                    list.Add(("available", L("ui.missions.available"), "cat_mission"));
                    list.Add(("active", L("ui.missions.active"), "cat_tech"));
                    list.Add(("create", L("ui.missions.create"), "cat_buildship"));
                    break;
                case Mode.Map:
                    list.Clear();
                    if (Game.StarMap != null && Game.StarMap.Systems.Length > 0)
                    {
                        // Default the selection to the current system if nothing valid is chosen yet, so the
                        // travel screen opens on the reachable in-system targets (and the sidebar highlights it).
                        if (!Game.StarMap.Systems.Any(s => "sys:" + s.Name == _category))
                        {
                            _category = "sys:" + (SelectedSystem()?.Name ?? Game.StarMap.Systems[0].Name);
                        }

                        string curId = CurrentSystemId();
                        var current = Game.StarMap.Systems.FirstOrDefault(s => s.Id == curId);
                        var distant = Game.StarMap.Systems.Where(s => s.Id != curId).ToList();

                        // Current system first, under its own heading — only the in-system targets, no jump.
                        list.Add(("head:current", L("ui.map.current_system"), string.Empty));
                        if (current != null)
                        {
                            list.Add(("sys:" + current.Name, "★ " + current.Shown(), "cat_planet")); // #3133: own name shown, key stays real
                        }

                        // Distant systems under a Hyperspace heading. Unknown ones read as a single
                        // "unexplored" entry (their bodies stay hidden until you hyperjump there); their
                        // NAME shows only once entered — or with a radar array fitted (#1113). The sidebar
                        // key stays the real name either way (it is never displayed).
                        if (distant.Count > 0)
                        {
                            list.Add(("head:hyper", L("ui.map.hyperspace"), string.Empty));
                            foreach (var sys in distant)
                            {
                                string label = Game.KnowsSystem(sys.Id)
                                    ? "★ " + sys.Shown() + (HasClientLane(curId, sys.Id) ? " ⇄" : string.Empty)
                                    : SystemDisplayName(sys) + "  ·  " + L("ui.map.unexplored");
                                list.Add(("sys:" + sys.Name, label, "cat_planet"));
                            }
                        }
                    }

                    break;
                case Mode.Character:
                    list.Clear();
                    list.Add(("appearance", L("ui.settings.character"), "cat_suit"));
                    list.Add(("people", L("ui.character.people"), "cat_mission")); // #1118: who you know
                    break;
                case Mode.Alliances:
                    list.Clear();
                    if (_category != "allies" && _category != "crew" && _category != "find" && _category != "funk")
                    {
                        _category = "allies"; // the tab opens with "all" by default — land on the roster
                    }

                    list.Add(("allies", L("ui.alliance.cat_allies"), "cat_mission"));
                    list.Add(("crew", L("ui.alliance.cat_crew"), "cat_target"));
                    list.Add(("find", L("ui.alliance.cat_find"), "cat_tech"));
                    list.Add(("funk", L("ui.alliance.cat_funk"), "cat_cargo"));
                    break;
                case Mode.Story:
                {
                    // #2471: the Journal — one entry per story the player follows (the tutorial, the VEGA story, side stories),
                    // VEGA's notes and the player's own notes (#1844). A badge (★) marks a story finished by playing, a dot
                    // something that waits (an offered part, a story that may be started).
                    list.Clear();
                    var journal = JournalCategories.Build(Game?.StoryPackStates, Game?.Story?.StoryId, Game?.StoryPack?.NameKey,
                        Game?.Story?.Active ?? false, L, HasLoc);
                    if (!journal.Any(c => c.Id == _category))
                    {
                        _category = JournalCategories.Default(journal, Game?.StoryPackStates, Game?.Objectives); // the tab opens on "all"
                    }

                    foreach (var c in journal)
                    {
                        string label = c.Label + (c.Badge ? " ★" : string.Empty) + (c.Attention ? " •" : string.Empty);
                        list.Add((c.Id, label, c.Id == JournalCategories.NotesId ? "cat_inventory" : "cat_mission"));
                    }

                    break;
                }
                case Mode.Companions:
                    list.Clear();
                    if (_category != "here" && _category != "all")
                    {
                        _category = "here"; // open on the companions present on this world
                    }

                    list.Add(("here", L("ui.companions.cat_here"), "cat_mission"));
                    list.Add(("all", L("ui.companions.cat_all"), "cat_inventory"));
                    break;
                case Mode.Photos:
                    list.Clear();
                    list.Add(("all", L("ui.photos.cat_all"), "cat_inventory"));
                    break;
            }

            return list;
        }

        // --- middle list ---

        private void RebuildList()
        {
            if (!_built)
            {
                return;
            }

            // #1484: remember where the list was scrolled — a rebuild of the SAME page must land exactly there,
            // or the row under the mouse changes between two clicks of the discard confirm.
            Vector2 keepScroll = _listContent != null ? _listContent.anchoredPosition : Vector2.zero;
            ClearChildren(_listContent);
            _selectedCardY = -1f; // AddCard records the selected card's row again (the jumps scroll to it)
            bool production = _mode == Mode.Crafting || _mode == Mode.Tech || _mode == Mode.Ship;
            RefreshGateRow(production);

            float y = 0f;
            switch (_mode)
            {
                case Mode.Crafting: y = BuildCraftingList(); break;
                case Mode.Tech: y = BuildTechList(); break;
                case Mode.Ship: y = BuildShipList(); break;
                case Mode.Inventory: y = BuildInventoryList(); break;
                case Mode.Map: y = BuildMapList(); break;
                case Mode.Missions: y = BuildMissionsList(); break;
                case Mode.Character: y = _category == "people" ? BuildPeopleList() : BuildCharacterList(); break;
                case Mode.Alliances: y = BuildAlliancesList(); break;
                case Mode.Story: y = BuildJournalPage(); break;
                case Mode.Companions: y = BuildCompanionsList(); break;
                case Mode.Photos: y = BuildPhotosList(); break;
                case Mode.Achievements: y = BuildAchievementList(); break;
            }

            SetContentHeight(_listContent, y);
            string listPage = _mode + "|" + _category;
            if (listPage != _listPage)
            {
                _listPage = listPage;
                ScrollToTop(_listContent); // a new page starts at the top, not wherever the last one was scrolled
            }
            else if (_listContent != null)
            {
                // Same page: put the list back where it was (clamped to the new height) — see keepScroll above.
                float viewHeight = (_listContent.parent as RectTransform)?.rect.height ?? 0f;
                float maxY = Mathf.Max(0f, y - viewHeight);
                _listContent.anchoredPosition = new Vector2(keepScroll.x, Mathf.Clamp(keepScroll.y, 0f, maxY));
            }

            _footer.text = production ? L("ui.craft.source") + "   |   " + InReachText() : string.Empty;
            if (_feedback != null)
            {
                _feedbackShown = Game.LastMessage ?? string.Empty;
                _feedback.text = _feedbackShown;
            }
        }

        // --- detail pane ---

        private void RebuildDetail()
        {
            if (!_built)
            {
                return;
            }

            ClearChildren(_detail);

            // A new detail page (different tab / category / selected entry) starts at the top — top-anchored
            // content makes y=0 the top regardless of the size set further down, so it's safe to reset here.
            string detailPage = _mode + "|" + _category + "|" + _selected;
            if (detailPage != _detailPage)
            {
                _detailPage = detailPage;
                ScrollToTop(_detail);
                _discardArmed = string.Empty; // navigating away disarms a half-confirmed discard (#599)
            }

            // Exactly one preview rig may be live at a time, else each rig's camera also picks up the OTHER rig's
            // model and they bleed into each other (B53: the colour tab showed the ship, the paint tab showed both).
            bool showAvatar = _mode == Mode.Character;
            bool showShip = _mode == Mode.Ship && _category == "paint";
            _avatarPreview?.SetActive(showAvatar);
            _shipPreview?.SetActive(showShip);

            if (_mode == Mode.Character)
            {
                SetContentHeight(_detail, BuildCharacterPreview()); // a live, rotating faced-avatar preview (B25)
                return;
            }

            if (_mode == Mode.Ship && _category == "paint")
            {
                SetContentHeight(_detail, BuildShipPreview()); // a live, rotating ship preview (item 32)
                return;
            }

            // The travel screen shows the selected system's animated mini star map even with no body picked.
            if (_mode == Mode.Map)
            {
                SetContentHeight(_detail, DetailMap());
                return;
            }

            // The Alliances detail pane is informational (allies/find) or the radio input (funk) — shown even with
            // nothing selected, so branch before the generic "pick an entry" placeholder.
            if (_mode == Mode.Alliances)
            {
                SetContentHeight(_detail, DetailAlliances());
                return;
            }

            // The Companions list carries its own per-row actions (rename/release), so the detail pane is a short
            // informational blurb rather than the generic "pick an entry" placeholder.
            if (_mode == Mode.Companions)
            {
                UiKit.AddText(_detail, 8, 16, 620, 30, L("ui.companions.title"), 22, UiKit.Cyan, TextAnchor.UpperLeft, FontStyle.Bold);
                var info = UiKit.AddText(_detail, 8, 54, 620, 160, L("ui.companions.empty"), 15, UiKit.CyanDim, TextAnchor.UpperLeft);
                info.horizontalOverflow = HorizontalWrapMode.Wrap;
                // #1210: what a companion does for you (fetch / alert / produce) — the only tab change of the payoff.
                var payoff = UiKit.AddText(_detail, 8, 54 + Mathf.Max(60f, info.preferredHeight + 12f), 620, 160, L("ui.companions.payoff_hint"), 15, UiKit.CyanDim, TextAnchor.UpperLeft);
                payoff.horizontalOverflow = HorizontalWrapMode.Wrap;
                SetContentHeight(_detail, 300);
                return;
            }

            // The Photos gallery: the detail pane is the full-size preview + editable note + delete for the
            // selected photo (or a hint when none is picked yet).
            if (_mode == Mode.Photos)
            {
                SetContentHeight(_detail, BuildPhotosDetail());
                return;
            }

            // Story → Notes (#1844): the detail pane is the note editor (title + body / preview + save/delete),
            // shown with a hint when there is no note yet.
            if (_mode == Mode.Story && _category == "notes")
            {
                SetContentHeight(_detail, BuildNotesDetail());
                return;
            }

            if (string.IsNullOrEmpty(_selected))
            {
                if (_mode == Mode.Inventory)
                {
                    SetContentHeight(_detail, BuildInventoryOverview()); // #3671: what the backpack, hold, suit and case hold
                    return;
                }

                UiKit.AddText(_detail, 8, 20, 620, 30, L("ui.craft.pick"), 22, UiKit.CyanDim, TextAnchor.UpperLeft);
                SetContentHeight(_detail, 60);
                return;
            }

            float y = 0f;
            switch (_mode)
            {
                case Mode.Crafting: y = DetailCrafting(); break;
                case Mode.Tech: y = DetailTech(); break;
                case Mode.Ship: y = DetailShip(); break;
                case Mode.Inventory:
                    y = _selected.StartsWith("cat:", System.StringComparison.Ordinal) ? DetailCatalog()
                        : _selected.StartsWith("smp:", System.StringComparison.Ordinal) ? DetailSample()
                        : DetailInventory();
                    break;
                case Mode.Missions: y = DetailMissions(); break;
            }

            SetContentHeight(_detail, y + 20f);
        }
    }
}
