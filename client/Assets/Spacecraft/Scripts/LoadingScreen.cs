using UnityEngine;

namespace Spacecraft.Client
{
    /// <summary>
    /// Loading screen (`anf_textures.md` §4/§3.1): a brief overlay shown while the in-game
    /// world is set up, then hands off to <see cref="AppShell.LaunchGame"/>. A real progress
    /// bar is added once asset/world load reports progress.
    /// </summary>
    public sealed class LoadingScreen
    {
        private const float MinShowSeconds = 0.6f;

        private readonly AppShell _shell;
        private float _elapsed;

        public LoadingScreen(AppShell shell) => _shell = shell;

        public void Update()
        {
            if (_shell.Phase != ShellPhase.Loading)
            {
                _elapsed = 0f;
                return;
            }

            _elapsed += Time.deltaTime;
            if (_elapsed >= MinShowSeconds)
            {
                _elapsed = 0f;
                _shell.LaunchGame();
            }
        }

        public void Draw()
        {
            GUI.Box(new Rect(0, 0, Screen.width, Screen.height), GUIContent.none);
            GUI.Label(new Rect(Screen.width / 2f - 100, Screen.height / 2f - 12, 200, 24), _shell.L("ui.loading.title"));
        }
    }
}
