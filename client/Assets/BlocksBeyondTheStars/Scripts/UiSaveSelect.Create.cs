// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    public static partial class UiSaveSelect
    {
        private sealed partial class WorldsScreen
        {
            // ── New world (the right pane after "+ New world", #3664) ──────────────────────────
            private string _name = RandomWorldName.DefaultName;
            private Text _nameLabel;
            private InputField _nameInput;

            // Mode: Explorer (normal) vs Creative (everything unlocked + a starter set; survival mechanics stay on) vs
            // Sandbox (#662: free crafting, no oxygen/hunger, peaceful, everything unlocked).
            private int _mode; // 0 Explorer · 1 Creative · 2 Sandbox · 3 Hard Survival (#3765)
            private readonly bool[] _optBlueprints = { true };
            private readonly bool[] _optShips = { true };
            private readonly bool[] _optKit = { true };
            private readonly Button[] _modeCards = new Button[4];
            private GameObject _creativePanel, _sandboxHint, _hardHint;

            // Four cards share the row the three used to fill (#3765).
            private const float ModeCardW = 229f, ModeCardH = 124f, ModeCardStep = 239f;
            private readonly WorldCreationOptions _worldOptions = new WorldCreationOptions();
            private GameObject _optionsOverlay;
            private Text _lanHint;

            private void ShowCreate()
            {
                _detail.SetActive(false);
                _empty.SetActive(false);
                _deleteButton.SetActive(false);
                _create.SetActive(true);
                _list.MarkSelected(null);
                _nameInput.ActivateInputField();
            }

            /// <summary>Leaves the New-world form for the world that was selected before (its row marked again) — the form's
            /// Cancel and Esc / pad B (<see cref="CloseTopLayer"/>). A pad whose focus was inside the form lands on Play.</summary>
            private void CancelCreate()
            {
                var world = _list.Find(_selected);
                Select(world);
                UiNav.Prefer(_canvas.gameObject, world != null ? _play : _emptyNew, takeOver: true);
            }

            private void BuildCreate(Transform pane)
            {
                _create = UiKit.AddStretchChild(pane, "NewWorld");
                var c = _create.transform;
                UiKit.AddKicker(c, 32f, 24f, 600f, _shell.L("ui.save.new"));
                _nameLabel = UiKit.AddText(c, 32f, 56f, 760f, 24f, _shell.L("ui.save.name"), 16, UiKit.TextCol, TextAnchor.MiddleLeft);
                BuildNameRow(c);

                UiKit.AddText(c, 32f, 148f, 600f, 24f, _shell.L("ui.save.mode"), 16, UiKit.TextCol, TextAnchor.MiddleLeft);
                string[] titles = { "ui.save.mode_explorer", "ui.save.mode_creative", "ui.save.mode_sandbox", "ui.save.mode_hard" };
                string[] lines = { "ui.save.mode_explorer_line", "ui.save.mode_creative_line", "ui.save.mode_sandbox_line", "ui.save.mode_hard_line" };
                string[] icons = { "btn_singleplayer", "btn_editors", "cat_blocks", "cat_weapons" };
                for (int i = 0; i < _modeCards.Length; i++)
                {
                    int mode = i;
                    _modeCards[i] = AddModeCard(c, 32f + i * ModeCardStep, 176f, _shell.L(titles[i]), _shell.L(lines[i]), icons[i], () =>
                    {
                        _mode = mode;
                        RefreshMode();
                    });
                }

                BuildCreativeOptions(c);
                UiKit.AddTierButton(c, 32f, 470f, 440f, 46f, _shell.L("ui.worldopt.open"), () =>
                {
                    _optionsOverlay ??= UiWorldOptions.Build(_shell, _root, _worldOptions);
                    _optionsOverlay.SetActive(true);
                }, ButtonTier.Secondary, "btn_settings");

                UiKit.AddLinkButton(c, 32f, FooterY + 7f, 260f, 36f, _shell.L("ui.action.cancel"), CancelCreate); // a 44-high hit area
                var create = UiKit.AddTierButton(c, PaneW - 32f - 400f, FooterY - 4f, 400f, 60f, _shell.L("ui.save.create_play"),
                    CreateNamed, ButtonTier.Primary, "btn_continue");
                _list.GateUntilLoaded(create); // the duplicate-name check needs the list (#3259)
                RefreshMode(); // start on Explorer (sub-options hidden)
            }

            // The name is sanitized AS IT IS TYPED, so the box always shows exactly what the save will be called — no silent
            // mangling three layers down. The label doubles as the message line: a warning right above the box is where the
            // eye already is. The dice button next to it is "Random world" (#2522).
            private void BuildNameRow(Transform c)
            {
                _nameInput = UiKit.AddInput(c, 32f, 84f, 730f, 46f, _name, v =>
                {
                    string clean = SanitizeWorldName(v, trimEnd: false); // still typing — keep a trailing space
                    _name = clean;
                    if (clean != v && _nameInput != null)
                    {
                        // Re-entrant by design: the setter fires onChange again with `clean`, and SanitizeWorldName is
                        // idempotent, so the second pass changes nothing and the recursion stops there.
                        _nameInput.text = clean;
                        SayName("ui.save.name_stripped", true);
                    }
                    else
                    {
                        SayName("ui.save.name", false);
                    }
                }, maxLength: 24);
                var dice = UiKit.AddTierButton(c, 778f, 84f, 200f, 46f, _shell.L("ui.save.random_short"), CreateRandom, ButtonTier.Secondary, "btn_dice");
                _list.GateUntilLoaded(dice); // a taken name must not be reused (#3259)
            }

            private void SayName(string key, bool warn)
            {
                _nameLabel.text = _shell.L(key);
                _nameLabel.color = warn ? UiKit.Warn : UiKit.TextCol;
            }

            /// <summary>A mode card: icon + mode name on top, one line of what it means under it (#3664: the three bare mode
            /// buttons neither explained themselves nor showed which one was chosen).</summary>
            private Button AddModeCard(Transform parent, float x, float y, string title, string line, string icon, System.Action pick)
            {
                const float w = ModeCardW, h = ModeCardH;
                var card = UiKit.AddTierButton(parent, x, y, w, h, title, pick, ButtonTier.Secondary, icon);
                foreach (var image in card.GetComponentsInChildren<Image>(true))
                {
                    if (image != card.targetGraphic)
                    {
                        UiKit.Place(image.gameObject, 14f, 12f, 32f, 32f);
                    }
                }

                var label = card.GetComponentInChildren<Text>();
                UiKit.Place(label.gameObject, 56f, 10f, w - 66f, 36f);
                label.resizeTextForBestFit = true; // a long mode name ("Hard Survival" in some languages) shrinks, not clips
                label.resizeTextMinSize = 12;
                label.resizeTextMaxSize = label.fontSize;
                var text = UiKit.AddText(card.transform, 14f, 50f, w - 28f, h - 56f, line, 14, UiKit.CyanDim, TextAnchor.UpperLeft);
                text.horizontalOverflow = HorizontalWrapMode.Wrap;
                text.text = UiKit.Ellipsize(line, w - 28f, h - 56f, 14);
                return card;
            }

            private void RefreshMode()
            {
                for (int i = 0; i < _modeCards.Length; i++)
                {
                    // The chosen mode reads as chosen: the cyan frame tint, the others plain.
                    ((Image)_modeCards[i].targetGraphic).color = i == _mode ? UiKit.Cyan : Color.white;
                }

                _creativePanel.SetActive(_mode == 1);
                _sandboxHint.SetActive(_mode == 2);
                _hardHint.SetActive(_mode == 3);
            }

            // Creative sub-options (a checklist; shown only when Creative is selected) and the Sandbox hint.
            private void BuildCreativeOptions(Transform c)
            {
                var cp = UiKit.AddPanel(c, 32f, 300f, 946f, 160f, new Color(0.05f, 0.10f, 0.16f, 0.55f));
                _creativePanel = cp.gameObject;
                AddToggle(cp.transform, 8f, _shell.L("ui.save.opt_blueprints"), _optBlueprints);
                AddToggle(cp.transform, 50f, _shell.L("ui.save.opt_ships"), _optShips);
                AddToggle(cp.transform, 92f, _shell.L("ui.save.opt_kit"), _optKit);

                // Flight is not a checkbox — it comes with both non-Explorer modes — but it has to be SAID, or it stays as
                // invisible as the /fly command it replaces. #2502: "double-tap {key:Jump}" — the device in hand's jump key.
                string flightHint = HudUi.ExpandKeyTokens(_shell.Localizer, _shell.L("ui.save.creative_flight"));
                UiKit.AddText(cp.transform, 12f, 134f, 920f, 22f, flightHint, 14, UiKit.Cyan, TextAnchor.MiddleLeft);

                // Sandbox: no checklist — everything is on by design; a short hint says what the mode does.
                var sh = UiKit.AddText(c, 32f, 304f, 946f, 150f, _shell.L("ui.save.sandbox_hint") + "\n" + flightHint, 15,
                    UiKit.CyanDim, TextAnchor.UpperLeft);
                sh.horizontalOverflow = HorizontalWrapMode.Wrap;
                _sandboxHint = sh.gameObject;

                // Hard Survival (#3765): no checklist either — a few lines say what the mode sets and how a death works.
                var hh = UiKit.AddText(c, 32f, 304f, 946f, 150f, _shell.L("ui.save.hard_hint"), 15,
                    UiKit.CyanDim, TextAnchor.UpperLeft);
                hh.horizontalOverflow = HorizontalWrapMode.Wrap;
                _hardHint = hh.gameObject;
            }

            private static void AddToggle(Transform parent, float y, string label, bool[] state)
            {
                Button b = null;
                string Fmt() => (state[0] ? "[X]  " : "[  ]  ") + label;
                b = UiKit.AddButton(parent, 12f, y, 600f, 38f, Fmt(), () =>
                {
                    state[0] = !state[0];
                    var t = b.GetComponentInChildren<Text>();
                    if (t != null) t.text = Fmt();
                });
            }

            // Creative honours the checklist; Sandbox forces every grant on (free crafting makes blueprints moot, but
            // owning all ships + the kit keeps testing friction-free). Both non-Explorer modes fly.
            private bool UnlockAll() => _mode == 2 || (_mode == 1 && _optBlueprints[0]);
            private bool AllShips() => _mode == 2 || (_mode == 1 && _optShips[0]);
            private bool Kit() => _mode == 2 || (_mode == 1 && _optKit[0]);
            private bool Flight() => _mode == 1 || _mode == 2;

            /// <summary>The world options the launch uses: the player's own — under Hard Survival with that mode's rules on top.</summary>
            private WorldCreationOptions LaunchOptions() => _mode == 3 ? WorldCreationOptions.HardSurvival(_worldOptions) : _worldOptions;

            // Create validates instead of half-working: an empty name and a name that already exists both say so, right
            // above the box, and nothing is launched.
            private void CreateNamed()
            {
                string world = SanitizeWorldName(_name);
                if (string.IsNullOrEmpty(world))
                {
                    SayName("ui.save.name_empty", true);
                    return;
                }

                if (!_list.Loaded)
                {
                    return; // the button waits for the list (#3259) — its duplicate check needs it
                }

                if (_list.Has(world))
                {
                    SayName("ui.save.name_taken", true);
                    return;
                }

                Launch(world, UnlockAll(), AllShips(), Kit(), _mode == 2, LaunchOptions(), Flight());
            }

            // "Random world" honours a name the player actually typed (#2522): an edited, free name gets a random seed under
            // that name; an empty, untouched or taken one keeps world_<n> (RandomWorldName).
            private void CreateRandom()
            {
                if (!_list.Loaded)
                {
                    return; // waits for the list (#3259): a taken name must not be reused
                }

                var pick = RandomWorldName.Resolve(SanitizeWorldName(_name), RandomWorldName.DefaultName, _list.Names(), new System.Random());
                Launch(pick.Name, UnlockAll(), AllShips(), Kit(), _mode == 2, LaunchOptions(), Flight(), pick.Seed);
            }

            // ── Open to friends (hosting, #3664) — under both views ─────────────────────────────
            private void BuildLan(Transform pane)
            {
                UiKit.AddImage(pane, 32f, LanY - 14f, PaneW - 64f, 1.5f, UiKit.SolidSprite, new Color(0.30f, 0.55f, 0.72f, 0.5f));
                var lanSwitch = UiKit.AddSwitch(pane, 32f, LanY + 6f, _lan, on =>
                {
                    _lan = on;
                    ShowLanOptions(pane, on);
                });
                // The label switches too (a 60×32 pill alone is a small target for a finger), above the hosting options
                // (LanY + 66) so it never covers them.
                UiKit.AddLabelHit(pane, 100f, LanY - 2f, PaneW - 132f, 54f, lanSwitch);
                UiKit.AddIcon(pane, 106f, LanY + 2f, 40f, "btn_host");
                UiKit.AddText(pane, 156f, LanY, 800f, 28f, _shell.L("ui.save.open_to_friends"), 21, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
                _lanHint = UiKit.AddText(pane, 156f, LanY + 28f, 820f, 22f, _shell.L("ui.host.address_hint"), 14, UiKit.CyanDim, TextAnchor.MiddleLeft);

                ShowLanOptions(pane, _lan);
            }

            // The hosting options are built the first time the switch turns on: their join address enumerates every network
            // interface, which "My worlds" must not pay on every open and every delete rebuild when nobody hosts.
            private void ShowLanOptions(Transform pane, bool on)
            {
                if (on && _lanOptions == null)
                {
                    _lanOptions = UiKit.AddStretchChild(pane, "LanOptions");
                    BuildLanOptions(_lanOptions.transform);
                }

                if (_lanOptions != null)
                {
                    _lanOptions.SetActive(on);
                }
            }

            private void BuildLanOptions(Transform o)
            {
                const float y = LanY + 66f;
                UiKit.AddText(o, 32f, y, 200f, 20f, _shell.L("ui.host.max_players"), 14, UiKit.CyanDim, TextAnchor.MiddleLeft);
                Text count = null;
                void StepPlayers(int dir)
                {
                    _maxPlayers = Mathf.Clamp(_maxPlayers + dir, 2, 16);
                    count.text = _maxPlayers.ToString();
                }

                // − count +: the count is the pad's one stop (Left / Right step it), − and + stay mouse and touch targets.
                UiKit.NoPadStop(UiKit.AddTierButton(o, 32f, y + 24f, 44f, 44f, "-", () => StepPlayers(-1), ButtonTier.Quiet));
                UiKit.AddStepperValue(o, 80f, y + 24f, 52f, 44f, StepPlayers);
                count = UiKit.AddText(o, 80f, y + 24f, 52f, 44f, _maxPlayers.ToString(), 22, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
                UiKit.NoPadStop(UiKit.AddTierButton(o, 136f, y + 24f, 44f, 44f, "+", () => StepPlayers(1), ButtonTier.Quiet));

                UiKit.AddText(o, 210f, y, 300f, 20f, _shell.L("ui.host.password"), 14, UiKit.CyanDim, TextAnchor.MiddleLeft);
                UiKit.AddInput(o, 210f, y + 24f, 300f, 44f, _hostPass, v => _hostPass = v);

                // The join address, HERE and not only in the chat once the world is up (#984): the host reads it out (or
                // copies it) while worldgen runs. Resolved on build — the live interface list, never a stale address.
                string joinAddress = AppShell.LanJoinAddress();
                UiKit.AddText(o, 540f, y, 300f, 20f, _shell.L("ui.host.your_address"), 14, UiKit.CyanDim, TextAnchor.MiddleLeft);
                var address = UiKit.AddText(o, 540f, y + 24f, 280f, 44f, joinAddress, 20, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
                UiKit.FitLabelSingleLine(address, 280f, 13, 20);
                UiKit.AddTierButton(o, 826f, y + 24f, 150f, 44f, _shell.L("ui.host.copy"), () =>
                {
                    GUIUtility.systemCopyBuffer = joinAddress;
                    _lanHint.text = _shell.L("ui.host.copied"); // the hint line confirms — a clipboard copy is otherwise invisible
                    _lanHint.color = UiKit.Cyan;
                }, ButtonTier.Quiet, "btn_join");
            }
        }
    }
}
