using System;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Owns the single in-game embedded web browser used by the Wiki and Arcade menu screens, plus the
    /// <see cref="LocalContentServer"/> that serves their bundled HTML/JS over loopback. One shared browser
    /// surface is reparented into whichever screen is active (<see cref="MountInto"/>) so only one CEF process
    /// ever runs; <see cref="Navigate"/> switches pages and <see cref="Park"/> blanks + hides it on close.
    ///
    /// The browser is UnityWebBrowser (UWB), wrapped behind the <c>BBS_UWB</c> scripting define so the project
    /// compiles and runs before the package is installed — then <see cref="Available"/> is false and the
    /// screens show a "browser not installed" placeholder while collection/highscores/downloads keep working.
    /// Enable it per <c>docs/MINIGAMES_AND_WIKI.md</c> (VoltUPR registry + UWB packages + the BBS_UWB define).
    /// The <c>#if BBS_UWB</c> block below is the single integration point to validate against the UWB version.
    /// </summary>
    public sealed class EmbeddedBrowser : MonoBehaviour
    {
        public static EmbeddedBrowser Instance { get; private set; }

        public LocalContentServer Content { get; } = new LocalContentServer();

        private Action<string, int, int, bool> _onResult;
#if BBS_UWB
        private RectTransform _surface;     // the shared RawImage the browser renders into (UWB only)
        private Image _cover;               // opaque themed overlay masking CEF's white→black startup flash
        private Text _coverText;            // "Loading…" label on the cover
        private string _loadingLabel = "…";
        private string _desiredUrl;         // the page to show; loaded only once the engine signals ready
        private bool _issued;               // has LoadUrl been issued for _desiredUrl yet?
        private float _issueTime;           // unscaled time the load was issued (grace before lifting the cover)
        private volatile bool _loadedFlag;  // set by OnLoadFinish (may be off-thread) → read in Update
        private float _navStart;            // unscaled time of the last Navigate (for the cover timeout)
        private RawImage _rawImage;         // the surface's RawImage (we rebind the live CEF texture each frame)
#endif

        /// <summary>True when a real browser backend is compiled in (the UWB package + BBS_UWB define).</summary>
        public bool Available
        {
#if BBS_UWB
            get => true;
#else
            get => false;
#endif
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            try { Content.Start(); }
            catch (Exception e) { Debug.LogWarning($"[EmbeddedBrowser] content server failed to start: {e.Message}"); }
        }

        /// <summary>Loopback URL for a path under StreamingAssets (e.g. <c>"wiki/index.html?lang=de"</c>).</summary>
        public string Url(string relativePath) => Content.Url(relativePath);

        /// <summary>Sets the handler invoked when a minigame reports a finished run through the JS bridge
        /// (<c>uwb.ExecuteJsMethod("reportResult", gameKey, score, rating, completed)</c>).</summary>
        public void SetResultHandler(Action<string, int, int, bool> handler) => _onResult = handler;

        internal void OnReportResult(string gameKey, int score, int rating, bool completed)
            => _onResult?.Invoke(gameKey, score, rating, completed);

        /// <summary>Sets the localized label shown on the loading cover that masks the browser's startup flash.</summary>
        public void SetLoadingLabel(string label)
        {
#if BBS_UWB
            _loadingLabel = string.IsNullOrEmpty(label) ? "…" : label;
            if (_coverText != null) _coverText.text = _loadingLabel;
#endif
        }

        /// <summary>Reparents the shared browser surface into a menu screen's canvas at the given 1920×1080-space
        /// rect and shows it. Returns false on a build without UWB (the caller then shows a placeholder).</summary>
        public bool MountInto(RectTransform parent, float x, float y, float w, float h)
        {
#if BBS_UWB
            EnsureSurface();
            _surface.SetParent(parent, false);
            UiKit.Place(_surface.gameObject, x, y, w, h);
            _surface.gameObject.SetActive(true);
            return true;
#else
            return false;
#endif
        }

        /// <summary>Requests a StreamingAssets-relative page (no-op without UWB). The actual LoadUrl is deferred
        /// to <see cref="Update"/> until the engine signals ready — issuing before that is silently dropped,
        /// which made the first open stay blank until reopened. A loading cover masks the startup until ready.</summary>
        public void Navigate(string relativePath)
        {
#if BBS_UWB
            _desiredUrl = Url(relativePath);
            _issued = false;
            _loadedFlag = false;
            _navStart = Time.unscaledTime;
            ShowCover(true);
#endif
        }

        /// <summary>Blanks + hides the surface when leaving a browser screen, so no game keeps running.</summary>
        public void Park()
        {
#if BBS_UWB
            if (_surface != null)
            {
                _desiredUrl = null;
                _issued = false;
                ShowCover(false);
                UwbLoad("about:blank");
                _surface.gameObject.SetActive(false);
            }
#endif
        }

        private void OnDestroy()
        {
            try { Content.Dispose(); } catch { }
#if BBS_UWB
            UwbDispose();
#endif
            if (Instance == this) Instance = null;
        }

#if BBS_UWB
        // ---------------------------------------------------------------------------------------------------
        // UnityWebBrowser (UWB) backend — compiled only with the BBS_UWB define. API targets UWB v2.x:
        //   WebBrowserUIBasic (requires a RawImage on the same GameObject) renders CEF into the RawImage and
        //   forwards mouse/keyboard input; its `browserClient` exposes LoadUrl / RegisterJsMethod / Dispose.
        // This is the one place to re-check against the exact installed UWB version (names/lifecycle).
        // ---------------------------------------------------------------------------------------------------
        private VoltstroStudios.UnityWebBrowser.Core.WebBrowserClient _client;

        private void EnsureSurface()
        {
            if (_surface != null) return;

            // Create the GameObject INACTIVE and fully configure the UWB component before activating it, so the
            // component's OnEnable/Init() runs only after engine/communication/input are assigned (avoids a NRE).
            var go = new GameObject("BrowserSurface");
            go.SetActive(false);
            go.transform.SetParent(transform, false);
            _surface = go.AddComponent<RectTransform>();
            _rawImage = go.AddComponent<RawImage>(); // WebBrowserUIBasic renders CEF into this RawImage

            var ui = go.AddComponent<VoltstroStudios.UnityWebBrowser.WebBrowserUIBasic>();
            _client = ui.browserClient; // auto-initialised by the manager (= new())
            if (_client != null)
            {
                // Assign the config ScriptableObjects the UWB packages ship in their Resources folders, so a
                // code-created component works without Inspector wiring. Legacy input system → "Old Input Handler".
                _client.engine = Resources.Load<VoltstroStudios.UnityWebBrowser.Core.Engines.Engine>("Cef Engine Configuration");
                _client.communicationLayer = Resources.Load<VoltstroStudios.UnityWebBrowser.Communication.CommunicationLayer>("TCP Communication Layer");
                ui.inputHandler = Resources.Load<VoltstroStudios.UnityWebBrowser.Input.WebBrowserInputHandler>("Old Input Handler");
                _client.initialUrl = "about:blank";
                _client.windowlessFrameRate = 60; // smoother real-time minigames (default 30 looks choppy)
                _client.jsMethodManager.jsMethodsEnable = true;
                _client.RegisterJsMethod<string, int, int, bool>("reportResult",
                    (gameKey, score, rating, completed) => OnReportResult(gameKey, score, rating, completed));
                _client.OnLoadFinish += url => { _loadedFlag = true; }; // page painted → drop the loading cover

                if (_client.engine == null)
                {
                    Debug.LogWarning("[EmbeddedBrowser] 'Cef Engine Configuration' not found in Resources — is the UWB CEF engine package installed?");
                }
            }

            BuildCover();
            // Left inactive; MountInto() reparents + activates it, which runs Init() with everything assigned.
        }

        /// <summary>Builds the opaque loading cover (a child of the surface, so it draws on top of the browser
        /// texture) that hides CEF's white→black startup until the page has actually loaded.</summary>
        private void BuildCover()
        {
            var go = new GameObject("BrowserCover", typeof(RectTransform), typeof(Image));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(_surface, false);
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            _cover = go.GetComponent<Image>();
            _cover.color = new Color(0.02f, 0.04f, 0.08f, 1f);
            _cover.raycastTarget = true; // swallow clicks while the page is loading

            _coverText = UiKit.AddText(rt, 0, 0, 800, 80, _loadingLabel, 26, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
            var trt = _coverText.rectTransform;
            trt.anchorMin = trt.anchorMax = trt.pivot = new Vector2(0.5f, 0.5f);
            trt.anchoredPosition = Vector2.zero;
            trt.sizeDelta = new Vector2(800, 80);

            go.SetActive(false);
        }

        private void ShowCover(bool show)
        {
            if (_cover != null) _cover.gameObject.SetActive(show);
        }

        private void Update()
        {
            if (_client == null || _surface == null || !_surface.gameObject.activeSelf) return;

            // IsConnected (the IPC link is up) — NOT ReadySignalReceived, which flips earlier, before LoadUrl
            // will work ("UWB is not currently connected!"). Issuing on the wrong signal was why the first open
            // failed and only a re-open (which re-issues once truly connected) showed content.
            bool connected = _client.IsConnected;

            // Issue the page load only once truly connected, and only mark it issued if LoadUrl succeeded
            // (so a transient failure retries next frame instead of sticking forever).
            if (connected && !_issued && _desiredUrl != null)
            {
                _loadedFlag = false;
                if (UwbLoad(_desiredUrl))
                {
                    _issued = true;
                    _issueTime = Time.unscaledTime;
                }
            }

            // Bind the live CEF texture to our RawImage as soon as it exists. UWB binds it once in OnStart
            // (before the engine has produced a frame), so without this the first open stays blank until the
            // screen is toggled — exactly the symptom. Rebinding here makes it show on the first open.
            if (connected && _rawImage != null && _client.BrowserTexture != null && _rawImage.texture != _client.BrowserTexture)
            {
                _rawImage.texture = _client.BrowserTexture;
                _rawImage.uvRect = new Rect(0f, 0f, 1f, -1f); // UWB renders with V flipped
            }

            // Lift the cover once the page is up — on the load-finished signal, or a short grace after the load
            // was issued and a texture exists (in case OnLoadFinish is missed), or a hard safety timeout.
            if (_cover != null && _cover.gameObject.activeSelf)
            {
                bool painted = _issued && _client.BrowserTexture != null
                               && (_loadedFlag || Time.unscaledTime - _issueTime > 1.5f);
                bool timedOut = Time.unscaledTime - _navStart > 15f;
                if (painted || timedOut)
                {
                    ShowCover(false);
                }
                else if (_coverText != null)
                {
                    var c = _coverText.color;
                    c.a = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 2.2f));
                    _coverText.color = c;
                }
            }
        }

        private bool UwbLoad(string url)
        {
            try { _client?.LoadUrl(url); return true; }
            catch (Exception e) { Debug.LogWarning($"[EmbeddedBrowser] LoadUrl failed: {e.Message}"); return false; }
        }

        private void UwbDispose()
        {
            try { _client?.Dispose(); } catch { }
            _client = null;
        }
#endif
    }
}
