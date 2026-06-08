using Spacecraft.Networking.Messages;
using UnityEngine;
using UnityEngine.UI;

namespace Spacecraft.Client
{
    /// <summary>
    /// Full-screen death feedback. When the player dies on a planet the screen flashes red and a death sound
    /// plays before they respawn; when the ship is destroyed in space the screen flashes with an explosion
    /// glare (its boom is played by <see cref="ClientAudio"/>). Purely cosmetic — driven by the server's
    /// respawn / space-closed events. The flash fades itself out.
    /// </summary>
    public sealed class DeathFx : MonoBehaviour
    {
        public GameBootstrap Game;

        private Image _flash;
        private Color _from;
        private float _t;
        private float _dur = 1f;
        private bool _subscribed;

        private void Update()
        {
            if (!_subscribed && Game?.Network != null)
            {
                Game.Network.RespawnNoticeReceived += OnRespawn;
                Game.Network.SpaceClosed += OnSpaceClosed;
                _subscribed = true;
            }

            if (_flash != null && _t < _dur)
            {
                _t += Time.deltaTime;
                float k = Mathf.Clamp01(1f - _t / _dur);
                var c = _from;
                c.a = _from.a * k * k; // ease-out fade
                _flash.color = _t >= _dur ? new Color(0f, 0f, 0f, 0f) : c;
            }
        }

        private void EnsureUi()
        {
            if (_flash != null)
            {
                return;
            }

            var canvas = UiKit.CreateCanvas("DeathFx");
            canvas.sortingOrder = 80; // over the HUD
            _flash = UiKit.AddImage(canvas.transform, 0, 0, 1920, 1080, UiKit.SolidSprite, new Color(0f, 0f, 0f, 0f));
            _flash.raycastTarget = false; // never swallow input — it's a brief overlay
        }

        private void Flash(Color color, float duration)
        {
            EnsureUi();
            _from = color;
            _dur = Mathf.Max(0.1f, duration);
            _t = 0f;
            _flash.color = color;
        }

        private void OnRespawn(RespawnNotice m)
        {
            if (!m.Died)
            {
                return; // a non-death relocation (e.g. the void-fall rescue) — no death feedback
            }

            Flash(new Color(0.70f, 0.05f, 0.05f, 0.78f), 0.9f); // a red death wash
            ClientAudio.Instance?.Cue("player_death");
        }

        private void OnSpaceClosed(SpaceClosed m)
        {
            if (!m.ShipDisabled)
            {
                return; // a normal return from space, not a destruction
            }

            Flash(new Color(1f, 0.55f, 0.15f, 0.85f), 0.8f); // an explosion glare …
            ClientAudio.Instance?.Cue("ship_destroyed");      // … with the boom (B49: was silent)
        }

        private void OnDestroy()
        {
            if (_subscribed && Game?.Network != null)
            {
                Game.Network.RespawnNoticeReceived -= OnRespawn;
                Game.Network.SpaceClosed -= OnSpaceClosed;
            }
        }
    }
}
