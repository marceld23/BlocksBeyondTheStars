using System.Collections.Generic;
using Spacecraft.Networking.Messages;
using UnityEngine;
using UnityEngine.UI;

namespace Spacecraft.Client
{
    /// <summary>
    /// The HUD companion panel for the ship AI "VEGA": shows her lines with a typewriter effect (queued,
    /// radio blip per line) and a persistent objective chip while the onboarding chain is active, with a
    /// skip button. Lines arrive as locale KEYS (<see cref="ShipAiLine"/>) and are localized here, so the
    /// companion is fully bilingual and offline-safe. Advisor hints (Kind 1) respect the settings mute.
    /// </summary>
    public sealed class VegaPanel : MonoBehaviour
    {
        public GameBootstrap Game;
        public ClientSettings Settings;

        private const float CharsPerSecond = 42f;
        private const float HoldSeconds = 2.6f;       // extra read time after the typewriter finishes
        private const float HoldPerChar = 0.045f;

        private Canvas _canvas;
        private GameObject _speech;
        private Text _speechText;
        private GameObject _chip;
        private Text _chipText;
        private GameObject _skip;

        private readonly Queue<string> _queue = new Queue<string>();
        private string _current = string.Empty;
        private float _shown;     // characters revealed so far
        private float _holdLeft;  // remaining display time once fully revealed

        private string _objectiveKey = string.Empty;
        private int _objProgress, _objTarget;

        private void Start()
        {
            _canvas = UiKit.CreateDiegeticCanvas("VegaPanel");

            // Speech panel: left side above the vitals, out of the crosshair's way.
            _speech = UiKit.AddPanel(_canvas.transform, 24, 600, 600, 150, new Color(0.05f, 0.10f, 0.16f, 0.82f)).gameObject;
            UiKit.AddText(_speech.transform, 14, 6, 300, 28, L("ui.vega.name"), 20, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            _speechText = UiKit.AddText(_speech.transform, 14, 34, 572, 110, string.Empty, 19, UiKit.TextCol, TextAnchor.UpperLeft);
            _speechText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _speechText.verticalOverflow = VerticalWrapMode.Overflow;
            _speech.SetActive(false);

            // Objective chip: small persistent strip below the speech spot.
            _chip = UiKit.AddPanel(_canvas.transform, 24, 760, 600, 44, new Color(0.05f, 0.10f, 0.16f, 0.66f)).gameObject;
            _chipText = UiKit.AddText(_chip.transform, 14, 0, 440, 44, string.Empty, 19, UiKit.Cyan, TextAnchor.MiddleLeft);
            _skip = UiKit.AddButton(_chip.transform, 458, 6, 132, 32, L("ui.vega.skip"), () =>
            {
                Game?.Network?.SendSkipOnboarding();
                _objectiveKey = string.Empty;
                Refresh();
            }).gameObject;
            _chip.SetActive(false);

            if (Game?.Network != null)
            {
                Game.Network.ShipAiLineReceived += OnLine;
            }
        }

        private string L(string key) => Game?.Localizer?.Get(key) ?? key;

        private void OnLine(ShipAiLine m)
        {
            _objectiveKey = m.ObjectiveKey ?? string.Empty;
            _objProgress = m.ObjectiveProgress;
            _objTarget = m.ObjectiveTarget;

            bool muted = m.Kind == 1 && Settings is { VegaHints: false };
            if (!string.IsNullOrEmpty(m.Text) && !muted)
            {
                _queue.Enqueue(m.Text); // LLM-authored line (already in the player's language)
            }
            else if (!string.IsNullOrEmpty(m.LineKey) && !muted)
            {
                string text = L(m.LineKey);
                if (!string.IsNullOrEmpty(m.LineArg) && text.Contains("{0}"))
                {
                    text = string.Format(text, m.LineArg);
                }

                _queue.Enqueue(text);
            }

            Refresh();
        }

        private void Refresh()
        {
            if (_chip == null)
            {
                return;
            }

            bool hasObjective = !string.IsNullOrEmpty(_objectiveKey);
            _chip.SetActive(hasObjective);
            if (hasObjective)
            {
                string counter = _objTarget > 1 ? $"  ({Mathf.Min(_objProgress, _objTarget)}/{_objTarget})" : string.Empty;
                _chipText.text = $"{L("ui.vega.objective")}: {L(_objectiveKey)}{counter}";
            }
        }

        private void Update()
        {
            if (_speechText == null)
            {
                return;
            }

            if (_current.Length == 0 && _queue.Count > 0)
            {
                _current = _queue.Dequeue();
                _shown = 0f;
                _holdLeft = HoldSeconds + _current.Length * HoldPerChar;
                _speech.SetActive(true);
                ClientAudio.Instance?.Cue("ai_blip"); // VEGA's radio chirp
            }

            if (_current.Length == 0)
            {
                if (_speech.activeSelf)
                {
                    _speech.SetActive(false);
                }

                return;
            }

            if (_shown < _current.Length)
            {
                _shown = Mathf.Min(_current.Length, _shown + Time.deltaTime * CharsPerSecond);
                _speechText.text = _current.Substring(0, (int)_shown);
                return;
            }

            _holdLeft -= Time.deltaTime;
            if (_holdLeft <= 0f || _queue.Count > 0) // next line waiting? move on a touch early
            {
                _current = string.Empty;
                _speechText.text = string.Empty;
            }
        }

        private void OnDestroy()
        {
            if (Game?.Network != null)
            {
                Game.Network.ShipAiLineReceived -= OnLine;
            }
        }
    }
}
