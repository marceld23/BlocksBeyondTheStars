// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Text;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Bio;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Localization;
using BlocksBeyondTheStars.Shared.State;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The bio lab panel (#2203–#2206): opens on Interact at a <c>bio_lab</c> block. The sample case stands on the
    /// left on every tab; the right side is one of three tabs — <b>Analyse</b> (what a sample holds), <b>Mix</b>
    /// (active substance + carrier + stabiliser + modifier → a preparation) and <b>Change</b> (a tool or a piece of
    /// gear + a material + a coating). Nothing is decided here: every button sends a <see cref="BioLabIntent"/>, the
    /// server answers with a <see cref="BioLabResult"/> and the changed inventory. What the panel shows before a
    /// click is computed with the same <c>Shared</c> rules the server uses, and only for what the player's research
    /// book already knows — an untried mix reads "reaction unknown".
    /// Modal like <see cref="CrystalDeviceUi"/>: pad-navigable, closes on Esc / pad B / the Close button.
    /// The static helpers name and describe samples, preparations and changed items for the other screens
    /// (inventory, Codex, HUD), so all of them read alike.
    /// </summary>
    public sealed class BioLabUi : MonoBehaviour
    {
        public static BioLabUi Instance { get; private set; }
        public GameBootstrap Game;

        /// <summary>The slot whose options the right pane lists instead of the tab (None = the tab itself).</summary>
        private enum Pick { None, Active, Carrier, Stabiliser, Modifier, Target, Material, Coating }

        /// <summary>One option of a slot: a sample of the case (by seed), a backpack item (by key), or neither = "none".</summary>
        private readonly struct Choice
        {
            public Choice(uint seed, string item, string text)
            {
                Seed = seed;
                Item = item;
                Text = text;
            }

            public uint Seed { get; }
            public string Item { get; }
            public string Text { get; }
        }

        private Canvas _canvas;
        private GameObject _overlay;
        private RectTransform _caseContent;
        private float _caseScroll;
        private bool _open;
        private int _openFrame = -1;
        private int _tab; // 0 analyse, 1 mix, 2 change
        private Pick _pick;
        private int _resultSeen;
        private int _dataSig;
        private string _status = string.Empty;
        private bool _statusOk;

        // #2216: whether a detoxifier stands by (the server's station check, mirrored) — looked up when the panel
        // opens and again on a slow beat, never per frame; a change rebuilds the panel through DataSig.
        private bool _detoxNear;
        private float _detoxCheckedAt;
        private const float DetoxCheckSeconds = 0.5f; // the cadence of the server's own station scan

        // Analyse: the sample of the case the card shows (its item key).
        private string _selKey = string.Empty;

        // Mix: the four slots. A stabiliser is a mineral sample (seed) or a plain backpack material (item key).
        private uint _mixActive, _mixStabSeed, _mixModifier;
        private string _mixCarrier = string.Empty, _mixStabItem = string.Empty;

        // Change: the item (its full key, as it lies in the backpack), the material and the coating.
        private string _chTarget = string.Empty, _chMatItem = string.Empty, _chCoating = string.Empty;
        private uint _chMatSeed;

        private const float W = 1500f, H = 900f;
        private const float CaseX = 32f, CaseW = 430f;
        private const float PaneX = 494f, PaneW = W - PaneX - 32f;
        private const float TopY = 84f;
        private const float ActionY = H - 136f; // the tab's own buttons, above the status line
        private const float PaneBottom = ActionY - 12f;

        private const string DimHex = "#9fb4c8"; // the Codex's dim text colour

        private void Awake() => Instance = this;

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_canvas != null) Destroy(_canvas.gameObject);
        }

        public bool IsOpen => _open;

        /// <summary>#2216: the lab is not used from inside the ship — the server refuses every lab intent while the
        /// player is aboard (in the landed cabin or the floating interior). The prompt, E and the panel follow the
        /// same state, so nothing is offered that would then be refused.</summary>
        public static bool RefusedAboard(GameBootstrap game)
            => game != null && (game.Aboard || game.LoadingPlanetType == "ship_interior");

        public void Open()
        {
            if (Game == null || _open || RefusedAboard(Game)) return;
            EnsureCanvas();
            _open = true;
            _openFrame = Time.frameCount;
            _pick = Pick.None;
            _status = string.Empty;
            _resultSeen = Game.BioLabResultCount; // an answer from before this visit is not shown again
            _detoxNear = DetoxifierInReach();
            _detoxCheckedAt = Time.unscaledTime;
            _dataSig = DataSig();
            _caseScroll = 0f;
            _canvas.gameObject.SetActive(true);
            Build();
            Game.SetMenuOwner(this, true);
        }

        private void Update()
        {
            if (!_open) return;
            if (Time.frameCount != _openFrame && InputMap.Down(InputAction.UiCancel))
            {
                Game?.MarkMenuInputHandled();
                if (_pick != Pick.None)
                {
                    _pick = Pick.None; // back out of the option list first
                    Rebuild();
                }
                else
                {
                    Close();
                }

                return;
            }

            if (Game == null) return;

            if (RefusedAboard(Game))
            {
                Close(); // carried aboard with the panel open (a recall, a teleport): the lab is out of use there
                return;
            }

            if (Time.unscaledTime - _detoxCheckedAt >= DetoxCheckSeconds)
            {
                _detoxCheckedAt = Time.unscaledTime;
                _detoxNear = DetoxifierInReach();
            }

            // The server's answer, and anything that moved under the panel (a sample used up, a tool changed, a
            // blueprint researched, a detoxifier placed beside the lab): show it and rebuild once.
            bool rebuild = false;
            if (Game.BioLabResultCount != _resultSeen)
            {
                _resultSeen = Game.BioLabResultCount;
                ShowResult(Game.LastBioLabResult);
                rebuild = true;
            }

            int sig = DataSig();
            if (sig != _dataSig)
            {
                _dataSig = sig;
                rebuild = true;
            }

            if (rebuild) Rebuild();
        }

        private void Close()
        {
            _open = false;
            if (_overlay != null) Destroy(_overlay);
            _overlay = null;
            _caseContent = null;
            if (_canvas != null) _canvas.gameObject.SetActive(false);
            Game?.SetMenuOwner(this, false);
        }

        private void EnsureCanvas()
        {
            if (_canvas != null) return;
            _canvas = UiKit.CreateCanvas("BioLabUI");
            _canvas.sortingOrder = 59;
            UiNav.Enable(_canvas.gameObject); // pad: the stick walks the lists and slots, A picks, B backs out
            _canvas.gameObject.SetActive(false);
        }

        private void Rebuild()
        {
            if (_caseContent != null) _caseScroll = _caseContent.anchoredPosition.y; // keep the case where it was scrolled to
            if (_overlay != null) Destroy(_overlay);
            Build();
        }

        /// <summary>What the panel shows, as one number: the research book and sample case (their revision), the
        /// backpack, the researched blueprints, the game mode and whether a detoxifier stands by. A change of any
        /// of them rebuilds the open panel.</summary>
        private int DataSig()
        {
            int sig = Game.Bio.Revision * 31 + Game.UnlockedBlueprints.Count;
            unchecked { sig = sig * 31 + (_detoxNear ? 1 : 0) + (FreeMode() ? 2 : 0); }
            if (Game.Personal != null)
            {
                foreach (var s in Game.Personal)
                {
                    unchecked { sig = sig * 31 + s.Slot * 92821 + (s.Item?.GetHashCode() ?? 0) + s.Count * 17; }
                }
            }

            return sig;
        }

        private void ShowResult(BioLabResult result)
        {
            if (result == null) return;
            _status = string.IsNullOrEmpty(result.MessageKey) ? string.Empty : L(result.MessageKey);
            _statusOk = result.Success;
            if (result.Success && result.Knowledge > 0)
            {
                _status += "  +" + result.Knowledge + " " + L("ui.tech.knowledge");
            }

            if (result.Action == BioLabIntent.Mix && (result.Success || result.Failed))
            {
                _status += "  (" + L("ui.bio.stability") + " " + result.Stability + " %)";
                if (result.Washed)
                {
                    _status += "  ·  " + L("ui.bio.washed"); // #2216: the detoxifier washed a toxic sample of this mix
                }
            }

            // A changed or washed item has a new key: keep it in the slot, so the next step works on the same item.
            if (result.Success && (result.Action == BioLabIntent.Change || result.Action == BioLabIntent.WashOff)
                && !string.IsNullOrEmpty(result.ItemKey))
            {
                _chTarget = result.ItemKey;
            }
        }

        private void Send(BioLabIntent intent)
        {
            Game?.Network?.SendBioLab(intent);
            ClientAudio.Instance?.Cue("ui_confirm");
        }

        // ---------------- layout ----------------

        private void Build()
        {
            var (overlay, panel) = UiKit.AddModalOverlay(_canvas.transform, (1920f - W) * 0.5f, (1080f - H) * 0.5f, W, H);
            _overlay = overlay;
            Prune();

            var head = UiKit.AddText(panel, 32f, 22f, 420f, 40f, L("ui.bio.title"), 26, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.AddOutline(head);

            string[] tabs = { "ui.bio.tab.analyse", "ui.bio.tab.mix", "ui.bio.tab.change" };
            for (int i = 0; i < tabs.Length; i++)
            {
                int tab = i;
                var b = UiKit.AddButton(panel, PaneX + i * 232f, 20f, 220f, 48f, L(tabs[i]), () =>
                {
                    _tab = tab;
                    _pick = Pick.None;
                    ClientAudio.Instance?.Cue("ui_click");
                    Rebuild();
                });
                if (_tab == i) Lit(b);
            }

            BuildCase(panel);
            if (_pick != Pick.None)
            {
                BuildPicker(panel);
            }
            else if (_tab == 0)
            {
                BuildAnalyse(panel);
            }
            else if (_tab == 1)
            {
                BuildMix(panel);
            }
            else
            {
                BuildChange(panel);
            }

            if (_status.Length > 0)
            {
                var status = UiKit.AddText(panel, CaseX, H - 76f, W - 2f * CaseX - 244f, 52f, _status, 18,
                    _statusOk ? UiKit.Ok : UiKit.Warn, TextAnchor.MiddleLeft);
                status.horizontalOverflow = HorizontalWrapMode.Wrap;
            }

            UiKit.AddButton(panel, W - 32f - 220f, H - 72f, 220f, 48f, L("ui.action.close"), () =>
            {
                Game?.MarkMenuInputHandled();
                Close();
            });
        }

        /// <summary>Drops what is no longer there: a sample that was used up, a carrier that left the backpack, a
        /// tool that was moved away. The slots then read "none" instead of offering a mix the server would refuse.</summary>
        private void Prune()
        {
            var bio = Game.Bio;
            // The card of a species that was just analysed stays up even when that used the last sample — the result
            // is what the player came for. Anything else that left the case gives way to the first sample.
            if (!InCase(_selKey) && !bio.Analysed(ItemKey.Seed(_selKey)))
            {
                _selKey = string.Empty;
                foreach (var s in bio.Samples)
                {
                    if (s != null && s.Count > 0 && !string.IsNullOrEmpty(s.Item))
                    {
                        _selKey = s.Item;
                        break;
                    }
                }
            }

            if (_mixActive != 0 && bio.SampleCount(_mixActive) < 1) _mixActive = 0;
            // The modifier is a SECOND sample: one of the active species needs two in the case.
            if (_mixModifier != 0 && bio.SampleCount(_mixModifier) < (_mixModifier == _mixActive ? 2 : 1)) _mixModifier = 0;
            if (_mixStabSeed != 0 && bio.SampleCount(_mixStabSeed, true) < 1) _mixStabSeed = 0;
            if (_mixStabItem.Length > 0 && !InBackpack(_mixStabItem, false)) _mixStabItem = string.Empty;
            if (_mixCarrier.Length > 0 && !InBackpack(_mixCarrier, false)) _mixCarrier = string.Empty;

            if (_chTarget.Length > 0 && !InBackpack(_chTarget, true)) _chTarget = string.Empty;
            if (_chMatSeed != 0 && bio.SampleCount(_chMatSeed, true) < 1) _chMatSeed = 0;
            if (_chMatItem.Length > 0 && !InBackpack(_chMatItem, false)) _chMatItem = string.Empty;
            if (_chCoating.Length > 0 && !InBackpack(_chCoating, false)) _chCoating = string.Empty;
        }

        private bool InCase(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            foreach (var s in Game.Bio.Samples)
            {
                if (s != null && s.Item == key && s.Count > 0) return true;
            }

            return false;
        }

        /// <summary>Whether the backpack holds this exact item key; <paramref name="single"/> asks for a stack of
        /// one (the server changes a tool in its slot and looks for exactly that).</summary>
        private bool InBackpack(string key, bool single)
        {
            if (Game.Personal == null) return false;
            foreach (var s in Game.Personal)
            {
                if (s.Item == key && (single ? s.Count == 1 : s.Count > 0)) return true;
            }

            return false;
        }

        // ---------------- the sample case (left, every tab) ----------------

        private void BuildCase(Transform panel)
        {
            UiKit.AddText(panel, CaseX, TopY, CaseW, 28f, L("ui.bio.samples"), 18, UiKit.CyanDim, TextAnchor.MiddleLeft);
            _caseContent = UiKit.ScrollList(panel, CaseX, TopY + 34f, CaseW, H - 92f - (TopY + 34f), 6f);
            bool any = false;
            foreach (var s in Game.Bio.Samples)
            {
                if (s == null || s.Count <= 0 || string.IsNullOrEmpty(s.Item)) continue;
                any = true;
                var stack = s;
                Row(_caseContent, 64f, go => SampleRow(go.transform, stack));
            }

            if (!any)
            {
                Row(_caseContent, 110f, go =>
                {
                    var t = UiKit.AddText(go.transform, 10f, 8f, CaseW - 44f, 96f, L("ui.bio.empty_case"), 16, UiKit.CyanDim, TextAnchor.UpperLeft);
                    t.horizontalOverflow = HorizontalWrapMode.Wrap;
                });
            }

            _caseContent.anchoredPosition = new Vector2(_caseContent.anchoredPosition.x, _caseScroll);
        }

        private void SampleRow(Transform row, NetItemStack stack)
        {
            string key = stack.Item;
            uint seed = ItemKey.Seed(key);
            Game.Bio.Species.TryGetValue(seed, out var species);
            float w = CaseW - 28f;
            var b = UiKit.AddButton(row, 4f, 2f, w, 60f, string.Empty, () => OnSampleClicked(key));
            if (key == _selKey) Lit(b);

            AddSampleIcon(b.transform, 8f, 8f, 44f, key, species);
            var name = UiKit.AddText(b.transform, 62f, 5f, w - 134f, 26f, SampleName(Game, key), 18, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.FitLabel(name, 12, 18);
            var origin = UiKit.AddText(b.transform, 62f, 32f, w - 190f, 22f, species != null ? species.OriginBodyName : string.Empty, 14, UiKit.CyanDim, TextAnchor.MiddleLeft);
            UiKit.FitLabel(origin, 10, 14);
            UiKit.AddText(b.transform, w - 70f, 5f, 62f, 26f, "×" + stack.Count, 18, UiKit.TextCol, TextAnchor.MiddleRight, FontStyle.Bold);
            if (Game.Bio.Analysed(seed))
            {
                var mark = UiKit.AddText(b.transform, w - 126f, 32f, 118f, 22f, L("ui.bio.analysed"), 13, UiKit.Ok, TextAnchor.MiddleRight);
                UiKit.FitLabel(mark, 9, 13);
            }
        }

        /// <summary>A sample shows what it was taken from: a deposit its material, a plant its body block, anything
        /// else the sample item's own icon — and a plain square where there is no art at all.</summary>
        private void AddSampleIcon(Transform parent, float x, float y, float size, string key, NetBioSpecies species)
        {
            string from = species == null ? null
                : !string.IsNullOrEmpty(species.MaterialItem) ? species.MaterialItem
                : !string.IsNullOrEmpty(species.BodyBlock) ? species.BodyBlock
                : null;
            Sprite sprite = from != null ? IconResolver.Resolve(from, Game) : null;
            if (sprite == null) sprite = IconResolver.Resolve(key, Game);
            if (UiKit.AddIconSprite(parent, x, y, size, sprite, Color.white) == null)
            {
                UiKit.AddImage(parent, x + 8f, y + 8f, size - 16f, size - 16f, UiKit.SolidSprite, IsMineral(key) ? UiKit.CyanDim : UiKit.Ok);
            }
        }

        /// <summary>A click in the case picks the sample for the card — and, on the other tabs, puts it where it
        /// belongs: a plant or animal sample is the active substance (or the modifier while that slot's list is
        /// open), a mineral sample the stabiliser or the material.</summary>
        private void OnSampleClicked(string key)
        {
            _selKey = key;
            uint seed = ItemKey.Seed(key);
            bool mineral = IsMineral(key);
            if (_tab == 1)
            {
                if (mineral)
                {
                    _mixStabSeed = seed;
                    _mixStabItem = string.Empty;
                }
                else if (_pick == Pick.Modifier)
                {
                    _mixModifier = seed;
                }
                else
                {
                    _mixActive = seed;
                }
            }
            else if (_tab == 2 && mineral)
            {
                _chMatSeed = seed;
                _chMatItem = string.Empty;
            }

            _pick = Pick.None;
            ClientAudio.Instance?.Cue("ui_click");
            Rebuild();
        }

        // ---------------- Analyse ----------------

        private void BuildAnalyse(Transform panel)
        {
            float y = TopY;
            if (_selKey.Length == 0)
            {
                Para(panel, PaneX, y, PaneW, L("ui.bio.empty_case"), 18, UiKit.CyanDim);
                return;
            }

            uint seed = ItemKey.Seed(_selKey);
            bool mineral = IsMineral(_selKey);
            Game.Bio.Species.TryGetValue(seed, out var species);
            UiKit.AddText(panel, PaneX, y, PaneW, 36f, SampleName(Game, _selKey), 26, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
            y += 40f;
            UiKit.AddText(panel, PaneX, y, PaneW, 26f, SampleOrigin(Game, _selKey), 17, UiKit.CyanDim, TextAnchor.MiddleLeft);
            y += 38f;

            bool analysed = Game.Bio.Analysed(seed);
            var profile = analysed && !mineral ? Game.Bio.ProfileOf(seed) : null;
            var material = analysed && mineral ? MaterialOf(Game, seed) : null;
            if (profile != null)
            {
                y += EffectHeadline(panel, y, EffectLabel(Game, profile.Effect, profile.Level), profile.Effect);
                Para(panel, PaneX, y, PaneW, ProfileText(Game, profile, species), 18, UiKit.TextCol);
            }
            else if (material != null)
            {
                Para(panel, PaneX, y, PaneW, MaterialText(Game, material, "\n"), 18, UiKit.TextCol);
            }
            else
            {
                Para(panel, PaneX, y, PaneW, L("ui.bio.unknown_substance"), 20, UiKit.TextCol);
            }

            float x = PaneX;
            if (!analysed)
            {
                UiKit.AddButton(panel, x, ActionY, 400f, 48f, L("ui.bio.analyse"),
                    () => Send(new BioLabIntent { Action = BioLabIntent.Analyse, Sample = seed, SampleMineral = mineral }));
                x += 412f;
            }

            // A single plant (it has a body block) can be raised into a seedling; a tree's trunk cannot.
            if (!mineral && InCase(_selKey) && species != null && species.Kind == (int)BioKind.Plant && !string.IsNullOrEmpty(species.BodyBlock))
            {
                UiKit.AddButton(panel, x, ActionY, 340f, 48f, L("ui.bio.seedling"),
                    () => Send(new BioLabIntent { Action = BioLabIntent.Seedling, Sample = seed }));
            }
        }

        // ---------------- Mix ----------------

        private void BuildMix(Transform panel)
        {
            float y = TopY;
            y = SlotRow(panel, y, "ui.bio.slot.active", SeedLabel(_mixActive, false), Pick.Active);
            y = SlotRow(panel, y, "ui.bio.slot.carrier", ItemLabel(_mixCarrier), Pick.Carrier);
            y = SlotRow(panel, y, "ui.bio.slot.stabiliser", _mixStabSeed != 0 ? SeedLabel(_mixStabSeed, true) : ItemLabel(_mixStabItem), Pick.Stabiliser);
            y = SlotRow(panel, y, "ui.bio.slot.modifier", SeedLabel(_mixModifier, false), Pick.Modifier);
            if (!Unlocked(BioItems.SynthesisBlueprint))
            {
                // The three extra slots stay usable; the server says so when they are used without the blueprint.
                y += Para(panel, PaneX, y, PaneW, L("ui.bio.locked.synthesis"), 16, UiKit.Warn) + 10f;
            }

            y += 6f;
            UiKit.AddText(panel, PaneX, y, PaneW, 26f, L("ui.bio.result"), 18, UiKit.CyanDim, TextAnchor.MiddleLeft);
            y += 32f;
            if (_mixActive != 0)
            {
                MixPreview(panel, y);
            }

            bool plain = _mixCarrier.Length == 0 && _mixStabSeed == 0 && _mixStabItem.Length == 0 && _mixModifier == 0;
            var mix = UiKit.AddButton(panel, PaneX, ActionY, 340f, 48f, L(plain ? "ui.bio.extract" : "ui.bio.mix"), () => Send(new BioLabIntent
            {
                Action = BioLabIntent.Mix,
                Sample = _mixActive,
                Carrier = _mixCarrier,
                Stabiliser = _mixStabSeed,
                MaterialItem = _mixStabSeed != 0 ? string.Empty : _mixStabItem,
                Modifier = _mixModifier,
            }));
            Enable(mix, _mixActive != 0);
        }

        /// <summary>The result before mixing — only for what the research book knows: a mix that was tried before
        /// (its signature), or the plain extract of an analysed sample. Everything else stays an experiment.
        /// #2216: a toxic sample is washed as part of the mix when a detoxifier stands by. The washed mix is another
        /// experiment than the unwashed one — its own signature, its own result — so the preview asks for the one
        /// the server would run now, and a line under it says which that is.</summary>
        private void MixPreview(Transform panel, float y)
        {
            var form = BioItems.CarrierForm(Game.Content?.GetItem(_mixCarrier)) ?? BioForm.Injector;
            // The signature names a mineral sample by its deposit's material, a plain material by its own key.
            string stabiliserItem = _mixStabSeed != 0
                ? (Game.Bio.Species.TryGetValue(_mixStabSeed, out var deposit) ? deposit.MaterialItem : string.Empty)
                : _mixStabItem;
            bool plain = _mixCarrier.Length == 0 && _mixStabSeed == 0 && _mixStabItem.Length == 0 && _mixModifier == 0;
            bool activeToxic = (Game.Bio.ProfileOf(_mixActive)?.Toxicity ?? 0) > 0;
            bool modifierToxic = _mixModifier != 0 && (Game.Bio.ProfileOf(_mixModifier)?.Toxicity ?? 0) > 0;
            bool toxic = activeToxic || modifierToxic;
            bool washed = WouldWash(toxic);
            bool known = Game.Bio.Knows(Synthesis.Signature(_mixActive, form, _mixStabSeed, stabiliserItem, _mixModifier, washed))
                         || (plain && Game.Bio.Analysed(_mixActive));
            var compound = known ? ComputeMix(Game, _mixActive, form, _mixStabSeed, _mixStabItem, _mixModifier, washed) : null;

            // The wash line belongs to what the player already knows to be toxic: a mix whose result is shown, or a
            // sample that was analysed. An unanalysed sample keeps its secret.
            bool washLine = toxic && (compound != null
                || (activeToxic && Game.Bio.Analysed(_mixActive)) || (modifierToxic && Game.Bio.Analysed(_mixModifier)));
            if (compound == null)
            {
                y += Para(panel, PaneX, y, PaneW, L("ui.bio.reaction_unknown"), 18, UiKit.CyanDim) + 8f;
            }
            else
            {
                string tail = L("ui.bio.stability") + ": " + compound.Stability + " %\n"
                              + L("ui.bio.reaction") + ": " + L("bio.reaction." + compound.Reaction.ToString().ToLowerInvariant());
                if (compound.Failed)
                {
                    y += Para(panel, PaneX, y, PaneW, L("ui.bio.will_fail"), 20, UiKit.Warn) + 8f;
                    y += Para(panel, PaneX, y, PaneW, tail, 18, UiKit.TextCol) + 8f;
                }
                else
                {
                    y += EffectHeadline(panel, y,
                        L("item." + BioItems.PreparationKey(form) + ".name") + " · " + EffectLabel(Game, compound.Effect, compound.Level), compound.Effect);
                    y += Para(panel, PaneX, y, PaneW, CompoundText(Game, compound, false) + "\n" + tail, 18, UiKit.TextCol) + 8f;
                }
            }

            if (washLine)
            {
                Para(panel, PaneX, y, PaneW, L(washed ? "ui.bio.washed" : "ui.bio.unwashed"), 16, washed ? UiKit.Ok : UiKit.Warn);
            }
        }

        /// <summary>#2216: whether the server would wash this mix — a toxic sample in it, a detoxifier standing by, and
        /// one carbon at hand (which a free game mode does not ask for).</summary>
        private bool WouldWash(bool toxic) => toxic && _detoxNear && (FreeMode() || InBackpack("carbon", false));

        /// <summary>
        /// Whether a detoxifier stands by, as the server's station check sees it. Outside a free game mode that is
        /// the server's own answer — the station set the crafting menu gates its recipes on. In a free mode that set
        /// names every station (nothing is gated there) while the wash still asks for a real detoxifier, so the
        /// server's rule is mirrored instead: aboard the ship its detoxifier module, on foot a detoxifier block
        /// within three cells around the feet and two up or down.
        /// </summary>
        private bool DetoxifierInReach()
        {
            const string detoxifier = "detoxifier"; // the station's key, its ship module and its block share the name
            if (Game.StationsKnown && !FreeMode())
            {
                return Game.StationsAvailable.Contains(detoxifier);
            }

            if (Game.Aboard)
            {
                var modules = Game.ShipCombat?.Modules;
                return modules != null && System.Array.IndexOf(modules, detoxifier) >= 0;
            }

            if (Game.World == null || !(Game.Content?.GetBlock(detoxifier) is { } def) || def.NumericId.Value == 0)
            {
                return false;
            }

            ushort id = def.NumericId.Value;
            var feet = Game.PlayerPosition;
            int px = Mathf.FloorToInt(feet.x), py = Mathf.FloorToInt(feet.y), pz = Mathf.FloorToInt(feet.z);
            for (int dx = -3; dx <= 3; dx++)
            {
                for (int dy = -2; dy <= 2; dy++)
                {
                    for (int dz = -3; dz <= 3; dz++)
                    {
                        if (Game.World.GetBlock(px + dx, py + dy, pz + dz).Value == id) return true;
                    }
                }
            }

            return false;
        }

        // ---------------- Change ----------------

        private void BuildChange(Transform panel)
        {
            float y = TopY;
            y = SlotRow(panel, y, "ui.bio.target", ItemLabel(_chTarget), Pick.Target);
            y = SlotRow(panel, y, "ui.bio.material", _chMatSeed != 0 ? SeedLabel(_chMatSeed, true) : ItemLabel(_chMatItem), Pick.Material);
            y = SlotRow(panel, y, "ui.bio.coating", ItemLabel(_chCoating), Pick.Coating);
            if (!Unlocked(BioItems.TuningBlueprint))
            {
                y += Para(panel, PaneX, y, PaneW, L("ui.bio.locked.tuning"), 16, UiKit.Warn) + 10f;
            }

            // What the item already carries (a new change overwrites it; washing takes it off).
            var carried = ItemMods.Of(_chTarget);
            if (!carried.IsEmpty)
            {
                y += 6f;
                UiKit.AddText(panel, PaneX, y, PaneW, 26f, L("ui.bio.changed"), 18, UiKit.CyanDim, TextAnchor.MiddleLeft);
                y += 30f;
                y += Para(panel, PaneX, y, PaneW, ModsText(Game, carried, true, "\n"), 18, UiKit.TextCol) + 10f;
            }

            y += 6f;
            UiKit.AddText(panel, PaneX, y, PaneW, 26f, L("ui.bio.result"), 18, UiKit.CyanDim, TextAnchor.MiddleLeft);
            y += 32f;

            var def = _chTarget.Length > 0 ? Game.Content?.GetItem(_chTarget) : null;
            var material = _chMatSeed != 0 ? MaterialOf(Game, _chMatSeed) : SyntheticMaterial(Game, _chMatItem);
            bool ready = def != null && material != null;
            if (ready)
            {
                // The server's call, with the same arguments: what the tool has decides what a material can change.
                var tool = def.Tool;
                var coating = _chCoating.Length > 0 ? BioItems.CompoundOf(_chCoating) : null;
                var mods = ItemModRules.Compute(tool != null, tool is { CooldownSeconds: > 0f }, tool is { Range: > 0f },
                    tool is { EnergyPerUse: > 0f }, material, coating);
                bool nothing = mods.IsEmpty || mods.ApplyTo(_chTarget) == _chTarget;
                Para(panel, PaneX, y, PaneW, nothing ? L("ui.bio.no_change_preview") : ModsText(Game, mods, true, "\n"), 18,
                    nothing ? UiKit.CyanDim : UiKit.TextCol);
            }

            var change = UiKit.AddButton(panel, PaneX, ActionY, 340f, 48f, L("ui.bio.change"), () => Send(new BioLabIntent
            {
                Action = BioLabIntent.Change,
                TargetItem = _chTarget,
                Stabiliser = _chMatSeed,
                MaterialItem = _chMatSeed != 0 ? string.Empty : _chMatItem,
                CoatingItem = _chCoating,
            }));
            Enable(change, ready);

            // Washing takes off whatever change the key carries — also one this version cannot read (a hand-typed key,
            // a key of a newer version), which shows as no change above. The server's rule (#2216): there is something
            // to wash off whenever taking the change tag away gives another key.
            if (_chTarget.Length > 0 && default(ItemMods).ApplyTo(_chTarget) != _chTarget)
            {
                UiKit.AddButton(panel, PaneX + 352f, ActionY, 300f, 48f, L("ui.bio.wash"),
                    () => Send(new BioLabIntent { Action = BioLabIntent.WashOff, TargetItem = _chTarget }));
            }
        }

        /// <summary>What the lab can change — the server's rule (a drill, a weapon, or a worn piece that protects),
        /// mirrored so the list offers nothing the server would refuse.</summary>
        private static bool Changeable(ItemDefinition def)
            => def != null && def.MaxStack == 1
               && (def.Tool is { Kind: ToolKind.Drill or ToolKind.Weapon }
                   || (!string.IsNullOrEmpty(def.EquipSlot) && (def.ArmorResistance > 0f || def.ThermalInsulation > 0f
                       || def.CorrosionResistance > 0f || def.FallProtection > 0f || def.ClimbGrip > 0f || def.OxygenBonus > 0f)));

        // ---------------- slots and their option lists ----------------

        /// <summary>A labelled slot: the button shows what is in it and opens its option list.</summary>
        private float SlotRow(Transform panel, float y, string labelKey, string value, Pick pick)
        {
            UiKit.AddText(panel, PaneX, y + 6f, 230f, 36f, L(labelKey), 18, UiKit.TextCol, TextAnchor.MiddleLeft);
            UiKit.AddButton(panel, PaneX + 240f, y, 560f, 48f, value, () =>
            {
                _pick = pick;
                ClientAudio.Instance?.Cue("ui_click");
                Rebuild();
            });
            return y + 58f;
        }

        private string SeedLabel(uint seed, bool mineral)
        {
            if (seed == 0) return L("ui.bio.slot.none");
            Game.Bio.Species.TryGetValue(seed, out var species);
            return SpeciesLabel(Game, species, mineral);
        }

        private string ItemLabel(string item) => string.IsNullOrEmpty(item) ? L("ui.bio.slot.none") : ItemName(item);

        private void BuildPicker(Transform panel)
        {
            UiKit.AddText(panel, PaneX, TopY, PaneW, 28f, L(PickTitle(_pick)), 18, UiKit.CyanDim, TextAnchor.MiddleLeft);
            var list = UiKit.ScrollList(panel, PaneX, TopY + 34f, PaneW, PaneBottom - (TopY + 34f), 6f);
            var choices = Choices(_pick, out bool optional);
            if (choices.Count == (optional ? 1 : 0))
            {
                Row(list, 56f, go => UiKit.AddText(go.transform, 12f, 8f, PaneW - 48f, 40f, L("ui.crystal.none"), 17, UiKit.CyanDim, TextAnchor.MiddleLeft));
            }

            foreach (var c in choices)
            {
                var choice = c;
                Row(list, 52f, go =>
                {
                    var b = UiKit.AddButton(go.transform, 4f, 4f, PaneW - 28f, 44f, choice.Text, () => Choose(choice));
                    if (IsCurrent(choice)) Lit(b);
                });
            }

            UiKit.AddButton(panel, PaneX, ActionY, 220f, 48f, L("ui.hotbar_action.back"), () =>
            {
                _pick = Pick.None;
                Rebuild();
            });
        }

        private static string PickTitle(Pick pick) => pick switch
        {
            Pick.Active => "ui.bio.slot.active",
            Pick.Carrier => "ui.bio.slot.carrier",
            Pick.Stabiliser => "ui.bio.slot.stabiliser",
            Pick.Modifier => "ui.bio.slot.modifier",
            Pick.Target => "ui.bio.target",
            Pick.Material => "ui.bio.material",
            _ => "ui.bio.coating",
        };

        /// <summary>The options of a slot. An optional slot starts with "none".</summary>
        private List<Choice> Choices(Pick pick, out bool optional)
        {
            var list = new List<Choice>();
            optional = pick is Pick.Carrier or Pick.Stabiliser or Pick.Modifier or Pick.Coating;
            if (optional)
            {
                list.Add(new Choice(0, string.Empty, L("ui.bio.slot.none")));
            }

            switch (pick)
            {
                case Pick.Active:
                case Pick.Modifier:
                    AddSampleChoices(list, false, pick == Pick.Modifier);
                    break;
                case Pick.Carrier:
                    // "Water → Injector": the carrier decides the form.
                    AddBackpackChoices(list,
                        (s, def) => !ItemKey.HasModifier(s.Item) && BioItems.CarrierForm(def) != null,
                        (s, def) => ItemName(s.Item) + "  →  " + L("item." + BioItems.PreparationKey(BioItems.CarrierForm(def) ?? BioForm.Injector) + ".name"));
                    break;
                case Pick.Stabiliser:
                case Pick.Material:
                    // A mineral sample carries the values of its deposit; a plain material acts with its fixed traits.
                    AddSampleChoices(list, true, false);
                    AddBackpackChoices(list,
                        (s, def) => !ItemKey.HasModifier(s.Item) && def.LabTraits is { Count: > 0 },
                        (s, def) => ItemName(s.Item));
                    break;
                case Pick.Target:
                    AddBackpackChoices(list, (s, def) => s.Count == 1 && Changeable(def), (s, def) => ItemName(s.Item));
                    break;
                case Pick.Coating:
                    AddBackpackChoices(list,
                        (s, def) => BioItems.CompoundOf(s.Item) is { Form: BioForm.Coating },
                        (s, def) => ItemName(s.Item));
                    break;
            }

            return list;
        }

        private void AddSampleChoices(List<Choice> list, bool mineral, bool second)
        {
            var seen = new HashSet<uint>();
            foreach (var s in Game.Bio.Samples)
            {
                if (s == null || s.Count <= 0 || string.IsNullOrEmpty(s.Item) || IsMineral(s.Item) != mineral) continue;
                uint seed = ItemKey.Seed(s.Item);
                if (seed == 0 || !seen.Add(seed)) continue;
                int count = Game.Bio.SampleCount(seed, mineral);
                if (second && seed == _mixActive && count < 2) continue; // the one sample is the active substance already

                string text = SampleName(Game, s.Item) + "  ×" + count;
                if (mineral)
                {
                    if (Game.Bio.Species.TryGetValue(seed, out var deposit) && !string.IsNullOrEmpty(deposit.OriginBodyName))
                    {
                        text += "  ·  " + deposit.OriginBodyName;
                    }
                }
                else if (Game.Bio.Analysed(seed) && Game.Bio.ProfileOf(seed) is { } profile)
                {
                    text += "  ·  " + EffectLabel(Game, profile.Effect, profile.Level);
                }

                list.Add(new Choice(seed, string.Empty, text));
            }
        }

        private void AddBackpackChoices(List<Choice> list, System.Func<NetItemStack, ItemDefinition, bool> fits,
            System.Func<NetItemStack, ItemDefinition, string> text)
        {
            if (Game.Personal == null || Game.Content == null) return;
            var seen = new HashSet<string>();
            foreach (var s in Game.Personal)
            {
                if (s.Count <= 0 || string.IsNullOrEmpty(s.Item) || seen.Contains(s.Item)) continue;
                var def = Game.Content.GetItem(s.Item);
                if (def == null || !fits(s, def)) continue;
                seen.Add(s.Item);
                list.Add(new Choice(0, s.Item, text(s, def)));
            }
        }

        private bool IsCurrent(Choice c) => _pick switch
        {
            Pick.Active => c.Seed == _mixActive,
            Pick.Carrier => c.Item == _mixCarrier,
            Pick.Stabiliser => c.Seed == _mixStabSeed && c.Item == _mixStabItem,
            Pick.Modifier => c.Seed == _mixModifier,
            Pick.Target => c.Item == _chTarget,
            Pick.Material => c.Seed == _chMatSeed && c.Item == _chMatItem,
            Pick.Coating => c.Item == _chCoating,
            _ => false,
        };

        private void Choose(Choice c)
        {
            switch (_pick)
            {
                case Pick.Active: _mixActive = c.Seed; break;
                case Pick.Carrier: _mixCarrier = c.Item; break;
                case Pick.Stabiliser: _mixStabSeed = c.Seed; _mixStabItem = c.Item; break;
                case Pick.Modifier: _mixModifier = c.Seed; break;
                case Pick.Target: _chTarget = c.Item; break;
                case Pick.Material: _chMatSeed = c.Seed; _chMatItem = c.Item; break;
                case Pick.Coating: _chCoating = c.Item; break;
            }

            _pick = Pick.None;
            ClientAudio.Instance?.Cue("ui_click");
            Rebuild();
        }

        // ---------------- small building blocks ----------------

        private static void Row(RectTransform list, float height, System.Action<GameObject> fill)
        {
            var go = new GameObject("Row", typeof(RectTransform));
            go.transform.SetParent(list, false);
            go.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, height);
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = height;
            fill(go);
        }

        /// <summary>A wrapped block of (rich) text, sized to what it needs. Returns its height, so the caller can
        /// put the next thing under it.</summary>
        private static float Para(Transform parent, float x, float y, float w, string text, int size, Color color)
        {
            var t = UiKit.AddText(parent, x, y, w, 40f, text, size, color, TextAnchor.UpperLeft);
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            float h = t.preferredHeight;
            t.rectTransform.sizeDelta = new Vector2(w, h + 6f);
            return h;
        }

        /// <summary>The headline of an effect: its colour as a small square (there are no effect icons) and its
        /// name with the level. Returns the height it takes.</summary>
        private float EffectHeadline(Transform panel, float y, string text, BioEffect effect)
        {
            UiKit.AddImage(panel, PaneX, y + 7f, 20f, 20f, UiKit.SolidSprite, EffectColor(effect));
            UiKit.AddText(panel, PaneX + 32f, y, PaneW - 32f, 34f, text, 22, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
            return 42f;
        }

        private static void Lit(Button b)
        {
            var img = b.GetComponent<Image>();
            if (img != null) img.color = UiKit.Cyan;
        }

        private static void Enable(Button b, bool on)
        {
            b.interactable = on;
            if (!on)
            {
                var img = b.GetComponent<Image>();
                if (img != null) img.color = new Color(0.3f, 0.34f, 0.4f, 0.8f);
            }
        }

        /// <summary>Whether this player plays a free game mode — Sandbox, or their own Creative override. The rules
        /// the server sends are the player's effective ones (the inventory's "All items" page asks the same way).</summary>
        private bool FreeMode() => Game?.Rules != null && Game.Rules.GameMode == "Creative";

        /// <summary>A lab function this player may use — the server's rule: always in a free game mode, otherwise
        /// once its blueprint is researched. The "research …" lines show only where the server would refuse.</summary>
        private bool Unlocked(string blueprint) => FreeMode() || Game.UnlockedBlueprints.Contains(blueprint);

        private static bool IsMineral(string key) => ItemKey.Base(key) == BioItems.MineralSample;

        private string ItemName(string item) => ItemNames.Display(Game.Localizer, item, null, seed => Game.Bio.SpeciesName(seed));

        private string L(string k) => Game?.Localizer?.Get(k) ?? k;

        // ---------------- shared with the inventory, the Codex and the HUD ----------------

        private static string Loc(GameBootstrap game, string key) => game?.Localizer?.Get(key) ?? key;

        private static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);

        private static string Dim(string text) => "<color=" + DimHex + ">" + text + "</color>";

        /// <summary>The colour that stands for an effect wherever it shows: the lab's headline square, the HUD's
        /// effect dot and the tint of a preparation's icon — one colour per effect, so a look at the hotbar tells
        /// two injectors apart.</summary>
        public static Color EffectColor(BioEffect effect) => effect switch
        {
            BioEffect.Speed => new Color(0.35f, 0.85f, 1.00f),
            BioEffect.Jump => new Color(0.55f, 1.00f, 0.55f),
            BioEffect.FeatherFall => new Color(0.85f, 0.95f, 1.00f),
            BioEffect.Grip => new Color(0.80f, 0.62f, 0.40f),
            BioEffect.Shield => new Color(0.40f, 0.60f, 1.00f),
            BioEffect.Regeneration => new Color(0.95f, 0.35f, 0.45f),
            BioEffect.Strength => new Color(1.00f, 0.50f, 0.20f),
            BioEffect.Mining => new Color(0.75f, 0.75f, 0.80f),
            BioEffect.Reflex => new Color(1.00f, 0.90f, 0.30f),
            BioEffect.Breath => new Color(0.60f, 0.90f, 0.95f),
            BioEffect.HeatWard => new Color(1.00f, 0.40f, 0.25f),
            BioEffect.ColdWard => new Color(0.55f, 0.75f, 1.00f),
            BioEffect.ToxinWard => new Color(0.60f, 0.90f, 0.30f),
            BioEffect.Satiety => new Color(0.90f, 0.70f, 0.35f),
            BioEffect.NightSight => new Color(0.70f, 0.50f, 1.00f),
            BioEffect.Perception => new Color(1.00f, 0.55f, 0.85f),
            BioEffect.Stealth => new Color(0.50f, 0.55f, 0.65f),
            BioEffect.Energy => new Color(1.00f, 0.82f, 0.25f),
            BioEffect.Gathering => new Color(0.40f, 0.80f, 0.45f),
            _ => Color.white,
        };

        /// <summary>"Speed III" in the player's language.</summary>
        public static string EffectLabel(GameBootstrap game, BioEffect effect, int level)
            => game?.Localizer != null ? BioItems.EffectLabel(game.Localizer, effect, level) : effect + " " + BioItems.Roman(level);

        /// <summary>The name of a side effect.</summary>
        public static string SideLabel(GameBootstrap game, BioSideEffect side)
            => Loc(game, "bio.side." + side.ToString().ToLowerInvariant());

        /// <summary>What a sample of the case is called: a species by its coined name, a deposit by its material,
        /// a plant without a name by its body block.</summary>
        public static string SampleName(GameBootstrap game, string itemKey)
        {
            if (game == null) return string.Empty;
            game.Bio.Species.TryGetValue(ItemKey.Seed(itemKey), out var species);
            return SpeciesLabel(game, species, IsMineral(itemKey));
        }

        /// <summary>The name of a species of the research book (see <see cref="SampleName"/>); <paramref name="species"/>
        /// may be null for a seed the book does not hold.</summary>
        public static string SpeciesLabel(GameBootstrap game, NetBioSpecies species, bool mineral)
        {
            if (mineral || (species != null && species.Kind == (int)BioKind.Mineral))
            {
                return species != null && !string.IsNullOrEmpty(species.MaterialItem)
                    ? Loc(game, "item." + species.MaterialItem + ".name")
                    : Loc(game, "item." + BioItems.MineralSample + ".name");
            }

            if (species != null && !string.IsNullOrEmpty(species.Name)) return species.Name;
            if (species != null && !string.IsNullOrEmpty(species.BodyBlock)) return Loc(game, "block." + species.BodyBlock + ".name");
            return Loc(game, "item." + BioItems.Sample + ".name");
        }

        /// <summary>"Plant · Found on: Kepler" — what a sample is and where it is from.</summary>
        public static string SampleOrigin(GameBootstrap game, string itemKey)
        {
            if (game == null) return string.Empty;
            game.Bio.Species.TryGetValue(ItemKey.Seed(itemKey), out var species);
            string kind = IsMineral(itemKey) ? "mineral" : species != null && species.Kind == (int)BioKind.Animal ? "animal" : "plant";
            string text = Loc(game, "bio.kind." + kind);
            if (species != null && !string.IsNullOrEmpty(species.OriginBodyName))
            {
                text += "  ·  " + Loc(game, "ui.bio.origin") + ": " + species.OriginBodyName;
            }

            return text;
        }

        /// <summary>The material profile of a deposit of the research book, with the fixed traits from the item data.</summary>
        public static MaterialProfile MaterialOf(GameBootstrap game, uint seed)
            => game == null ? null : game.Bio.MaterialOf(seed, key => game.Content?.GetItem(key)?.LabTraits);

        /// <summary>The profile of a plain material from the backpack (an ingot, an alloy, synthesised ore): its
        /// fixed traits at purity 3. Null for an item without lab traits.</summary>
        public static MaterialProfile SyntheticMaterial(GameBootstrap game, string itemKey)
            => !string.IsNullOrEmpty(itemKey) && game?.Content?.GetItem(itemKey) is { LabTraits: { Count: > 0 } } def
                ? MaterialProfiles.Synthetic(MaterialProfiles.BaseLevels(def.LabTraits))
                : null;

        /// <summary>What a mix makes, computed as the server computes it. Null when the research book lacks a species
        /// the mix needs. <paramref name="washed"/> says whether a detoxifier washed the toxic samples of the mix
        /// (#2216) — the washed and the unwashed mix of the same inputs are two experiments with two results.</summary>
        public static Compound ComputeMix(GameBootstrap game, uint activeSeed, BioForm form, uint stabiliserSeed,
            string stabiliserItem, uint modifierSeed, bool washed = false)
        {
            var active = game?.Bio.ProfileOf(activeSeed);
            if (active == null) return null;

            MaterialProfile stabiliser = null;
            if (stabiliserSeed != 0)
            {
                stabiliser = MaterialOf(game, stabiliserSeed);
                if (stabiliser == null) return null;
            }
            else if (!string.IsNullOrEmpty(stabiliserItem))
            {
                stabiliser = SyntheticMaterial(game, stabiliserItem);
                if (stabiliser == null) return null;
            }

            BioProfile modifier = null;
            if (modifierSeed != 0)
            {
                modifier = game.Bio.ProfileOf(modifierSeed);
                if (modifier == null) return null;
            }

            return Synthesis.Compute(new SynthesisInput
            {
                Active = active,
                ActiveCleaned = washed,
                Form = form,
                Stabiliser = stabiliser,
                Modifier = modifier,
                ModifierCleaned = washed,
            });
        }

        /// <summary>The catch of a substance as rich text — amber, one arrow per level, its name — or "no side effect".</summary>
        public static string SideText(GameBootstrap game, BioSideEffect side, int level)
            => side == BioSideEffect.None || level <= 0
                ? Loc(game, "ui.bio.no_side")
                : "<color=" + Hex(UiKit.Warn) + ">" + new string('▼', Mathf.Clamp(level, 1, 3)) + " " + SideLabel(game, side) + "</color>";

        private static void AppendSide(StringBuilder sb, GameBootstrap game, BioSideEffect side, int level, bool withDesc)
        {
            if (side == BioSideEffect.None || level <= 0)
            {
                sb.Append(SideText(game, side, level)).Append('\n');
                return;
            }

            sb.Append(Loc(game, "ui.bio.side")).Append(": ").Append(SideText(game, side, level)).Append('\n');
            if (withDesc)
            {
                sb.Append(Dim(Loc(game, "bio.side." + side.ToString().ToLowerInvariant() + ".desc"))).Append('\n');
            }
        }

        private static string Seconds(GameBootstrap game, int seconds) => string.Format(Loc(game, "ui.bio.seconds"), seconds);

        /// <summary>What a preparation does besides its main effect, as lines of rich text: a coupled second effect,
        /// the catch, how long it lasts and how it takes the weather. <paramref name="withDesc"/> adds the one-line
        /// explanations — for a reader without the lab at hand.</summary>
        public static string CompoundText(GameBootstrap game, Compound compound, bool withDesc)
        {
            var sb = new StringBuilder();
            if (withDesc)
            {
                sb.Append(Dim(Loc(game, "bio.effect." + compound.Effect.ToString().ToLowerInvariant() + ".desc"))).Append('\n');
            }

            if (compound.Secondary != BioEffect.None && compound.SecondaryLevel > 0)
            {
                sb.Append("+ <b>").Append(EffectLabel(game, compound.Secondary, compound.SecondaryLevel)).Append("</b>\n");
                if (withDesc)
                {
                    sb.Append(Dim(Loc(game, "bio.effect." + compound.Secondary.ToString().ToLowerInvariant() + ".desc"))).Append('\n');
                }
            }

            AppendSide(sb, game, compound.Side, compound.SideLevel, withDesc);
            sb.Append(Loc(game, "ui.bio.duration")).Append(": ").Append(Seconds(game, compound.DurationSeconds)).Append('\n');
            sb.Append(Dim(Loc(game, "bio.thermal." + compound.Thermal.ToString().ToLowerInvariant())));
            return sb.ToString();
        }

        /// <summary>The analysed profile of a species as lines of rich text (everything but the effect headline).</summary>
        public static string ProfileText(GameBootstrap game, BioProfile profile, NetBioSpecies species)
        {
            var sb = new StringBuilder();
            sb.Append(Dim(Loc(game, "bio.effect." + profile.Effect.ToString().ToLowerInvariant() + ".desc"))).Append('\n');
            sb.Append(Loc(game, "ui.bio.substance")).Append(": <b>").Append(profile.Substance).Append("</b>\n");
            AppendSide(sb, game, profile.Side, profile.SideLevel, true);
            if (profile.Toxicity > 0)
            {
                sb.Append("<color=").Append(Hex(UiKit.Warn)).Append('>').Append(Loc(game, "ui.bio.toxicity")).Append(' ')
                    .Append(profile.Toxicity).Append("/3</color>\n");
                sb.Append(Dim(Loc(game, "ui.bio.toxic_hint"))).Append('\n');
            }
            else
            {
                sb.Append(Loc(game, "ui.bio.not_toxic")).Append('\n');
            }

            sb.Append(Loc(game, "ui.bio.group")).Append(": ").Append(Loc(game, "bio.group." + profile.Group)).Append('\n');
            sb.Append(Loc(game, "ui.bio.rarity")).Append(": ").Append(Loc(game, "bio.rarity." + profile.Rarity)).Append('\n');
            sb.Append(Loc(game, "ui.bio.duration")).Append(": ").Append(Seconds(game, profile.DurationSeconds)).Append('\n');
            sb.Append(Dim(Loc(game, "bio.thermal." + profile.Thermal.ToString().ToLowerInvariant())));
            if (species != null && species.ParentA != 0 && species.ParentB != 0 && game != null)
            {
                game.Bio.Species.TryGetValue(species.ParentA, out var a);
                game.Bio.Species.TryGetValue(species.ParentB, out var b);
                sb.Append('\n').Append(Loc(game, "ui.bio.cross_of")).Append(": ")
                    .Append(SpeciesLabel(game, a, false)).Append(" × ").Append(SpeciesLabel(game, b, false));
            }

            return sb.ToString();
        }

        /// <summary>The analysed profile of a deposit: purity, the traits with their levels, the trace trait —
        /// joined by <paramref name="separator"/> (a line break for a card, " · " for a list line).</summary>
        public static string MaterialText(GameBootstrap game, MaterialProfile material, string separator)
        {
            var sb = new StringBuilder();
            sb.Append(Loc(game, "ui.bio.purity")).Append(": ").Append(material.Purity).Append("/5");
            var traits = new StringBuilder();
            for (int i = 1; i < material.Levels.Length; i++)
            {
                if (material.Levels[i] <= 0 || (MatTrait)i == material.Trace) continue;
                if (traits.Length > 0) traits.Append(", ");
                traits.Append(TraitName(game, (MatTrait)i)).Append(' ').Append(material.Levels[i]);
            }

            if (traits.Length > 0)
            {
                sb.Append(separator).Append(Loc(game, "ui.bio.traits")).Append(": ").Append(traits);
            }

            if (material.Trace != MatTrait.None)
            {
                sb.Append(separator).Append(Loc(game, "ui.bio.trace")).Append(": ").Append(TraitName(game, material.Trace));
            }

            return sb.ToString();
        }

        private static string TraitName(GameBootstrap game, MatTrait trait)
            => Loc(game, "bio.trait." + trait.ToString().ToLowerInvariant());

        /// <summary>What the lab changed on a tool or a piece of gear, as rich text: every gain as green arrows (one
        /// per level), the stat and "+n", the drawback as amber arrows and "−n". <paramref name="headers"/> puts the
        /// words "better" / "worse" above them (the lab's card); a list line passes false and " · ".</summary>
        public static string ModsText(GameBootstrap game, ItemMods mods, bool headers, string separator)
        {
            var sb = new StringBuilder();
            bool gains = mods.First != ModStat.None || mods.Second != ModStat.None;
            if (headers && gains)
            {
                sb.Append(Dim(Loc(game, "ui.bio.gain")));
            }

            AppendMod(sb, game, mods.First, mods.FirstLevel, true, separator);
            AppendMod(sb, game, mods.Second, mods.SecondLevel, true, separator);
            if (mods.Drawback != ModStat.None)
            {
                if (headers)
                {
                    if (sb.Length > 0) sb.Append(separator);
                    sb.Append(Dim(Loc(game, "ui.bio.loss")));
                }

                AppendMod(sb, game, mods.Drawback, mods.DrawbackLevel, false, separator);
            }

            return sb.ToString();
        }

        private static void AppendMod(StringBuilder sb, GameBootstrap game, ModStat stat, int level, bool gain, string separator)
        {
            if (stat == ModStat.None || level <= 0) return;
            if (sb.Length > 0) sb.Append(separator);
            sb.Append("<color=").Append(Hex(gain ? UiKit.Ok : UiKit.Warn)).Append('>').Append(gain ? '▲' : '▼', level).Append("</color> ")
                .Append(Loc(game, "bio.stat." + stat.ToString().ToLowerInvariant()))
                .Append(gain ? " +" : " −").Append(level);
        }
    }
}
