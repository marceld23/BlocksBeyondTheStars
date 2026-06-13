using System.Collections.Generic;
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

            // Collection rail (left).
            UiKit.AddPanel(_root, 40, RegionY, 300, RegionH, new Color(0.05f, 0.09f, 0.15f, 0.95f));
            UiKit.AddText(_root, 56, RegionY + 8, 270, 30, L("ui.arcade.collection"), 16, UiKit.CyanDim, TextAnchor.MiddleLeft, FontStyle.Bold);
            _rail = UiKit.Place(new GameObject("Rail", typeof(RectTransform)), 50, RegionY + 44, 280, RegionH - 50);
            _rail.SetParent(_root, false);

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
                string label = (string.IsNullOrEmpty(e.icon) ? "" : e.icon + "  ") + e.Title(Game.German) +
                               (best > 0 ? "   ★" + best : "");
                UiKit.AddButton(_rail, 0, y, 280, 56, label, () => PlayGame(key));
                y += 64f;
            }
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

        private void PlayGame(string key)
        {
            var e = _catalog?.Find(key);
            if (e == null) return;

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
