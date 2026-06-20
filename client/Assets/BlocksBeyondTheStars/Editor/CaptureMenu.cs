#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;

namespace BlocksBeyondTheStars.Client.EditorTools
{
    /// <summary>
    /// Editor convenience for running the <see cref="BlocksBeyondTheStars.Client.ScreenshotDirector"/> from
    /// the menu (a quick way to eyeball the capture sequence without a full player build). It sets a one-shot
    /// EditorPrefs trigger + language, opens the launcher scene and enters play mode; the director self-installs,
    /// runs, then leaves play mode (it does NOT close the editor for menu runs). Shots land in
    /// <c>&lt;repo&gt;/marketing/screenshots/&lt;lang&gt;/</c>.
    ///
    /// NOTE: for exact 1920x1080 output set the Game view to a 1920x1080 fixed resolution first — the editor
    /// captures the Game view at its current size. The canonical path for final assets is the built player via
    /// <c>scripts/capture-screenshots.ps1</c> (exact resolution + the bundled server).
    /// </summary>
    public static class CaptureMenu
    {
        [MenuItem("BlocksBeyondTheStars/Capture Screenshots/Run (German)")]
        public static void RunDe() => Run("de");

        [MenuItem("BlocksBeyondTheStars/Capture Screenshots/Run (English)")]
        public static void RunEn() => Run("en");

        private static void Run(string lang)
        {
            EditorPrefs.SetBool("bbs_capture", true);
            EditorPrefs.SetString("bbs_capture_lang", lang);

            BuildScript.EnsureLauncherScene();
            EditorSceneManager.OpenScene("Assets/Scenes/Launcher.unity");
            EditorApplication.EnterPlaymode();
        }
    }
}
#endif
