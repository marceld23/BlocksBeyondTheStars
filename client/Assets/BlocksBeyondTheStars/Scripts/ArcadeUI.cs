using System.Collections.Generic;
using BlocksBeyondTheStars.Client.Minigames;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The in-game Arcade screen — the player's personal collection of minigames downloaded from data cubes.
    /// A left rail lists the games they own (with the local personal best); picking one plays it in the shared
    /// <see cref="EmbeddedBrowser"/> on the right, passing the language + best score through the URL. Locked
    /// games aren't listed — you find them out in the world. Highscores are local-only (no leaderboard). On a
    /// build without the UWB browser the collection still shows; the play area shows an "install" placeholder.
    /// </summary>
    public sealed class ArcadeUI : MonoBehaviour
    {
        public GameBootstrap Game;
        public GameMenu Menu;
        public ClientSettings Settings;

        private const float W = 1920f, H = 1080f;
        private const float RegionX = 360f, RegionY = 96f, RegionW = W - RegionX - 40f, RegionH = H - 130f;

        private Canvas _canvas;
        private RectTransform _root;
        private RectTransform _rail;
        private GameObject _placeholder;
        private GameObject _emptyState;
        private MinigameCatalog _catalog;
        private MinigameHostUI _native;
        private bool _built;
        private bool _open;
        private string _currentGame = string.Empty;

        private void EnsureBuilt()
        {
            if (_built) return;
            _catalog = MinigameCatalog.Load();

            _canvas = UiKit.CreateCanvas("ArcadeUI");
            _canvas.sortingOrder = 55;
            _root = (RectTransform)_canvas.transform;

            UiKit.AddImage(_root, 0, 0, W, H, UiKit.SolidSprite, new Color(0.02f, 0.04f, 0.08f, 0.96f));
            UiKit.AddLogo(_root, 40, 18, 520, 44, L("ui.arcade.title"), 26);
            UiKit.AddButton(_root, W - 360, 26, 180, 46, L("ui.action.back_menu"), () => Menu?.CloseBrowser());
            UiKit.AddButton(_root, W - 170, 26, 130, 46, L("ui.action.close"), () => Menu?.CloseFromUi());

            // Collection rail (left) — scrollable, since the unlocked-game list can exceed the panel height.
            UiKit.AddPanel(_root, 40, RegionY, 300, RegionH, new Color(0.05f, 0.09f, 0.15f, 0.95f));
            UiKit.AddText(_root, 56, RegionY + 8, 270, 30, L("ui.arcade.collection"), 16, UiKit.CyanDim, TextAnchor.MiddleLeft, FontStyle.Bold);
            _rail = MakeScroll(_root, 50, RegionY + 44, 290, RegionH - 52);

            // Play region (right).
            UiKit.AddPanel(_root, RegionX, RegionY, RegionW, RegionH, new Color(0.03f, 0.06f, 0.11f, 0.96f));
            _placeholder = BuildText("ArcadePlaceholder", L("ui.browser.unavailable_title"), L("ui.browser.unavailable_body"));
            _emptyState = BuildText("ArcadeEmpty", L("ui.arcade.empty_title"), L("ui.arcade.empty_body"));
            _canvas.enabled = false; // first Show() enables it + runs OnOpen()
            _built = true;
        }

        private GameObject BuildText(string name, string title, string body)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(_root, false);
            UiKit.Place(go, RegionX, RegionY, RegionW, RegionH);
            UiKit.AddText(go.transform, 0, RegionH * 0.5f - 70, RegionW, 60, title, 26, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.AddText(go.transform, 60, RegionH * 0.5f, RegionW - 120, 80, body, 18, UiKit.CyanDim, TextAnchor.MiddleCenter);
            go.SetActive(false);
            return go;
        }

        public void Show()
        {
            EnsureBuilt();
            if (!_canvas.enabled)
            {
                _canvas.enabled = true;
                _currentGame = string.Empty;
                RebuildRail();
                OnOpen();
                UiKit.TransitionIn(_canvas.gameObject);
            }
        }

        public void Hide()
        {
            if (!_built || !_canvas.enabled) return;
            _canvas.enabled = false;
            _native?.Stop();
            if (_open)
            {
                EmbeddedBrowser.Instance?.Park();
                _open = false;
            }
        }

        private List<MinigameCatalog.Entry> Owned()
        {
            var list = new List<MinigameCatalog.Entry>();
            if (_catalog == null || Game == null) return list;
            foreach (var e in _catalog.Games)
            {
                if (Game.UnlockedGames.Contains(e.key)) list.Add(e);
            }

            return list;
        }

        private void RebuildRail()
        {
            for (int i = _rail.childCount - 1; i >= 0; i--) Destroy(_rail.GetChild(i).gameObject);

            var owned = Owned();
            float y = 0f;
            foreach (var e in owned)
            {
                string key = e.key;
                int best = Settings != null ? Settings.GetMinigameBest(key) : 0;
                // Two-line entry: title on top, personal best on its own line below — so long (German) titles
                // and the highscore both fit the 270px frame instead of being crammed onto one shrunk line.
                string title = (string.IsNullOrEmpty(e.icon) ? "" : e.icon + "  ") + e.Title(Game.German);
                string hs = best > 0 ? "★ " + best : (Game.German ? "noch kein Rekord" : "no record yet");
                var btn = UiKit.AddButton(_rail, 0, y, 270, 64, string.Empty, () => PlayGame(key));
                UiKit.AddText(btn.transform, 18, 7, 244, 30, title, 17, UiKit.TextCol, TextAnchor.LowerLeft, FontStyle.Bold);
                UiKit.AddText(btn.transform, 18, 37, 244, 20, hs, 13,
                    best > 0 ? UiKit.Cyan : UiKit.CyanDim, TextAnchor.UpperLeft);
                y += 72f;
            }
            // Size the scroll content so it scrolls when taller than the viewport, and reset to the top.
            float viewportH = _rail.parent is RectTransform vp ? vp.rect.height : 0f;
            _rail.sizeDelta = new Vector2(_rail.sizeDelta.x, Mathf.Max(y + 8f, viewportH));
            _rail.anchoredPosition = new Vector2(_rail.anchoredPosition.x, 0f);
            if (_rail.parent != null && _rail.parent.GetComponent<ScrollRect>() is { } sr) sr.velocity = Vector2.zero;
        }

        /// <summary>Builds a vertical scroll view (viewport mask + a thin cyan scrollbar) and returns its content
        /// RectTransform (top-left anchored, so UiKit.Place lays children out from the top).</summary>
        private static RectTransform MakeScroll(Transform parent, float x, float y, float w, float h)
        {
            var viewGo = new GameObject("Scroll", typeof(RectTransform));
            viewGo.transform.SetParent(parent, false);
            UiKit.Place(viewGo, x, y, w, h);
            var sr = viewGo.AddComponent<ScrollRect>();
            sr.horizontal = false; sr.vertical = true; sr.scrollSensitivity = 32f; sr.movementType = ScrollRect.MovementType.Clamped;
            viewGo.AddComponent<RectMask2D>();

            var content = new GameObject("Content", typeof(RectTransform)).GetComponent<RectTransform>();
            content.SetParent(viewGo.transform, false);
            content.anchorMin = new Vector2(0f, 1f); content.anchorMax = new Vector2(0f, 1f); content.pivot = new Vector2(0f, 1f);
            content.sizeDelta = new Vector2(w, h); content.anchoredPosition = Vector2.zero;
            sr.content = content; sr.viewport = (RectTransform)viewGo.transform;

            // Thin vertical scrollbar on the right edge.
            var sbGo = new GameObject("Scrollbar", typeof(RectTransform), typeof(Image), typeof(Scrollbar));
            var sbRT = (RectTransform)sbGo.transform; sbRT.SetParent(viewGo.transform, false);
            sbRT.anchorMin = new Vector2(1f, 0f); sbRT.anchorMax = new Vector2(1f, 1f); sbRT.pivot = new Vector2(1f, 0.5f);
            sbRT.sizeDelta = new Vector2(7f, 0f); sbRT.anchoredPosition = Vector2.zero;
            sbGo.GetComponent<Image>().color = new Color(0.27f, 0.55f, 0.72f, 0.12f);
            var sb = sbGo.GetComponent<Scrollbar>(); sb.direction = Scrollbar.Direction.BottomToTop;
            var handleGo = new GameObject("Handle", typeof(RectTransform), typeof(Image));
            var hRT = (RectTransform)handleGo.transform; hRT.SetParent(sbGo.transform, false);
            hRT.anchorMin = Vector2.zero; hRT.anchorMax = Vector2.one; hRT.sizeDelta = Vector2.zero; hRT.anchoredPosition = Vector2.zero;
            handleGo.GetComponent<Image>().color = new Color(0.27f, 0.84f, 1f, 0.55f);
            sb.handleRect = hRT; sb.targetGraphic = handleGo.GetComponent<Image>();
            sr.verticalScrollbar = sb; sr.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            return content;
        }

        private void OnOpen()
        {
            var owned = Owned();
            if (owned.Count == 0)
            {
                _placeholder.SetActive(false);
                _emptyState.SetActive(true);
                return;
            }

            _emptyState.SetActive(false);
            PlayGame(owned[0].key); // open the first owned game by default
        }

        /// <summary>Creates (once) the native minigame host that runs the C# games in the play region, wiring its
        /// result handler to the same local-highscore + knowledge path the embedded browser used.</summary>
        private void EnsureNative()
        {
            if (_native != null) return;
            var go = new GameObject("MinigameHost", typeof(RectTransform));
            _native = go.AddComponent<MinigameHostUI>();
            _native.Game = Game;
            _native.OnReport = (k, score, rating, completed) =>
            {
                bool newBest = Settings != null && Settings.RecordMinigameScore(k, score);
                if (newBest) Settings.Save();
                if (completed && newBest) Game?.Network?.SendMinigameResult(k, score, rating, completed);
                RebuildRail(); // refresh the rail's "★ best" line
            };
            _native.Mount(_root, RegionX, RegionY, RegionW, RegionH);
        }

        private void PlayGame(string key)
        {
            var e = _catalog?.Find(key);
            if (e == null) return;

            // Native C# host (no UWB) for every game in the registry — all 20 ship games.
            if (MinigameRegistry.Has(key))
            {
                EnsureNative();
                EmbeddedBrowser.Instance?.Park();
                _placeholder.SetActive(false);
                _emptyState.SetActive(false);
                _native.Play(MinigameRegistry.Create(key), Settings != null ? Settings.GetMinigameBest(key) : 0, Game != null && Game.German);
                _currentGame = key;
                _open = false;
                return;
            }

            var host = Menu != null ? Menu.EnsureBrowserHost() : EmbeddedBrowser.Instance;
            string lang = (Game != null && Game.German) ? "de" : "en";
            int best = Settings != null ? Settings.GetMinigameBest(key) : 0;
            string url = "minigames/" + e.entry + "?lang=" + lang + "&hi=" + best + "&game=" + key;

            if (host != null && host.Available && host.Content != null && host.Content.Running
                && host.MountInto(_root, RegionX, RegionY, RegionW, RegionH))
            {
                host.SetLoadingLabel(L("ui.browser.loading"));
                host.Navigate(url);
                _placeholder.SetActive(false);
                _emptyState.SetActive(false);
                _currentGame = key;
                _open = true;
            }
            else
            {
                _placeholder.SetActive(true);
            }
        }

        private string L(string key) => Game?.Localizer?.Get(key) ?? key;

        private void OnDestroy()
        {
            if (_canvas != null) Destroy(_canvas.gameObject);
        }
    }
}
