using UnityEngine;

namespace Spacecraft.Client
{
    /// <summary>
    /// Splash screen (`anf_textures.md` §3): static logo + tagline + version + build badge,
    /// shown every start, skippable, and also bridging real content-load time. Silent until the
    /// player has set audio. Animation/sound are deferred (see CLIENT_SHELL_AND_ASSETS.md).
    /// </summary>
    public sealed class SplashScreen
    {
        private const float Duration = 2.5f;

        private readonly AppShell _shell;
        private float _elapsed;

        public SplashScreen(AppShell shell) => _shell = shell;

        public void Update()
        {
            if (_shell.Phase != ShellPhase.Splash)
            {
                return;
            }

            _elapsed += Time.deltaTime;
            if (_elapsed >= Duration || Input.anyKeyDown)
            {
                _shell.GoTo(ShellPhase.MainMenu);
            }
        }

        public void Draw()
        {
            GUI.Box(new Rect(0, 0, Screen.width, Screen.height), GUIContent.none);

            float x = Screen.width / 2f - 220, y = Screen.height / 2f - 70;
            GUI.Label(new Rect(x, y, 440, 40), "SPACECRAFT");
            GUI.Label(new Rect(x, y + 38, 440, 24), _shell.L("ui.splash.tagline"));
            GUI.Label(new Rect(x, y + 66, 440, 20), $"{_shell.L("ui.splash.build")}   v{AppShell.Version}");
            GUI.Label(new Rect(x, y + 96, 440, 20), _shell.L("ui.splash.skip"));
        }
    }
}
