using UnityEngine;

namespace Spacecraft.Client
{
    /// <summary>
    /// Main menu (`anf_textures.md` §4): the central entry point. MVP entries (singleplayer /
    /// local world, join server, settings, credits, quit), with the client version shown.
    /// Singleplayer starts a local in-process server (same authoritative GameServer).
    /// </summary>
    public sealed class MainMenu
    {
        private readonly AppShell _shell;

        public MainMenu(AppShell shell) => _shell = shell;

        public void Draw()
        {
            float x = Screen.width / 2f - 150, y = Screen.height / 2f - 150;

            GUI.Label(new Rect(x, y - 54, 300, 44), "SPACECRAFT");

            if (GUI.Button(new Rect(x, y, 300, 36), _shell.L("ui.menu.singleplayer")))
            {
                _shell.StartSingleplayer();
            }

            y += 46;
            GUI.Label(new Rect(x, y, 300, 20), $"{_shell.L("ui.menu.join")}:");
            y += 22;
            _shell.Host = GUI.TextField(new Rect(x, y, 196, 28), _shell.Host);
            _shell.Port = GUI.TextField(new Rect(x + 202, y, 98, 28), _shell.Port);
            y += 32;
            if (GUI.Button(new Rect(x, y, 300, 36), _shell.L("ui.menu.join")))
            {
                _shell.StartJoin();
            }

            y += 46;
            if (GUI.Button(new Rect(x, y, 300, 36), _shell.L("ui.menu.settings")))
            {
                _shell.OpenSettings();
            }

            y += 42;
            if (GUI.Button(new Rect(x, y, 300, 36), _shell.L("ui.menu.credits")))
            {
                _shell.GoTo(ShellPhase.Credits);
            }

            y += 42;
            if (GUI.Button(new Rect(x, y, 300, 36), _shell.L("ui.menu.quit")))
            {
                _shell.Quit();
            }

            GUI.Label(new Rect(10, Screen.height - 24, 400, 20), $"v{AppShell.Version}");
        }
    }
}
