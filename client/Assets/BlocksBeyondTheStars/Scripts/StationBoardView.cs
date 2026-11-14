// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The departure boards of the intercity stations (#3633): over every stop of a line a public train runs on, a board
    /// hangs under the hall's roof above the track — "Departures", the destination (the other station's map name, the
    /// <c>rail_station</c> point of interest nearest to the other stop) and the next train: "Next train in 1:20", "Train is
    /// here · departs in 12 s" or "Train arriving". The forecast is <see cref="RailEta"/> (Client.Core) from the train's
    /// replicated state, counted down locally between the server's train lists. World-space text like the nameplates
    /// (<see cref="WorldTextMaterial"/>, depth-tested) on a dark plate, turned toward the camera about the vertical; shown
    /// within <see cref="ShowRange"/> blocks, hidden farther away and while a scene hides the HUD. Client only: no wire, no
    /// save, no block. The texts refresh a few times a second and only change when a shown second does.
    /// </summary>
    public sealed class StationBoardView : MonoBehaviour
    {
        public GameBootstrap Game;
        public RailView Rails;

        /// <summary>A board is shown within this many blocks of the camera.</summary>
        public const float ShowRange = 30f;

        /// <summary>A train due in fewer seconds than this reads "Train arriving".</summary>
        public const double ArrivingSeconds = 8.0;

        /// <summary>The board hangs this far over the line's centre at the stop (under the 7-high hall's roof, above the
        /// wagon's 3.7-block top).</summary>
        private const float BoardRise = 4.25f;

        private const float RefreshSeconds = 0.25f;

        private static readonly Color TextColour = new Color(1f, 0.72f, 0.25f, 1f); // a departure board's amber
        private static readonly Color PlateColour = new Color(0.05f, 0.06f, 0.08f, 1f);

        private sealed class Board
        {
            public int LineId;
            public int Stop;
            public GameObject Root;
            public TextMesh Text;
            public Transform Plate;
            public string Shown = string.Empty;
        }

        private readonly List<Board> _boards = new List<Board>();
        private readonly Dictionary<int, RailEta> _etas = new Dictionary<int, RailEta>();
        private NetRailLine[] _builtRails;
        private NetTrain[] _builtTrains;
        private float _nextRefresh;
        private static Shader _plateShader;

        private void Update()
        {
            if (Game == null || Rails == null)
            {
                return;
            }

            if (!ReferenceEquals(_builtRails, Game.Rails) || !ReferenceEquals(_builtTrains, Game.Trains))
            {
                SyncBoards();
            }

            var cam = Camera.main;
            bool hide = cam == null || GameBootstrap.HudHidden;
            bool refresh = Time.time >= _nextRefresh;
            if (refresh)
            {
                _nextRefresh = Time.time + RefreshSeconds;
            }

            foreach (var b in _boards)
            {
                Place(b, cam, hide, refresh);
            }
        }

        /// <summary>One board per stop of every line a public train runs on (rebuilt when the lists change).</summary>
        private void SyncBoards()
        {
            _builtRails = Game.Rails;
            _builtTrains = Game.Trains;
            var wanted = new List<(int Line, int Stop)>();
            foreach (var line in Game.Rails ?? System.Array.Empty<NetRailLine>())
            {
                if (line == null || PublicTrainOn(line.Id) == null || Eta(line) is not { } eta)
                {
                    continue;
                }

                for (int i = 0; i < eta.StopCount; i++)
                {
                    wanted.Add((line.Id, i));
                }
            }

            for (int i = _boards.Count - 1; i >= 0; i--)
            {
                if (!wanted.Contains((_boards[i].LineId, _boards[i].Stop)))
                {
                    Destroy(_boards[i].Root);
                    _boards.RemoveAt(i);
                }
            }

            foreach (var (lineId, stop) in wanted)
            {
                if (!_boards.Exists(b => b.LineId == lineId && b.Stop == stop))
                {
                    _boards.Add(Build(lineId, stop));
                }
            }
        }

        /// <summary>The forecast of a line, rebuilt when the line's spline is (null for a line too short to run on).</summary>
        private RailEta Eta(NetRailLine line)
        {
            var spline = Rails.SplineOf(line.Id);
            if (spline == null || line.StopArcs == null || line.StopArcs.Length == 0)
            {
                _etas.Remove(line.Id);
                return null;
            }

            if (!_etas.TryGetValue(line.Id, out var eta) || !ReferenceEquals(eta.Spline, spline) || eta.StopCount != line.StopArcs.Length)
            {
                eta = new RailEta(spline, line.StopArcs);
                _etas[line.Id] = eta;
            }

            return eta;
        }

        private NetTrain PublicTrainOn(int lineId)
        {
            foreach (var t in Game.Trains ?? System.Array.Empty<NetTrain>())
            {
                if (t != null && t.LineId == lineId && RailRules.IsPublic(t.OwnerId))
                {
                    return t;
                }
            }

            return null;
        }

        private Board Build(int lineId, int stop)
        {
            var root = new GameObject("StationBoard");
            root.transform.SetParent(transform, false);

            var plate = GameObject.CreatePrimitive(PrimitiveType.Quad);
            plate.name = "Plate";
            if (plate.TryGetComponent<Collider>(out var col))
            {
                Destroy(col); // a board is light, not a block: nothing collides with it
            }

            plate.transform.SetParent(root.transform, false);
            plate.transform.localPosition = new Vector3(0f, 0f, 0.02f); // just behind the text
            if (_plateShader == null)
            {
                _plateShader = Shader.Find("BlocksBeyondTheStars/LitColor") ?? Shader.Find("Unlit/Color");
            }

            plate.GetComponent<Renderer>().sharedMaterial = new Material(_plateShader) { color = ShaderColor.Srgb(PlateColour) };

            var textGo = new GameObject("Text");
            textGo.transform.SetParent(root.transform, false);
            var tm = textGo.AddComponent<TextMesh>();
            tm.font = UiKit.Font;
            if (textGo.TryGetComponent<MeshRenderer>(out var mr) && UiKit.Font != null)
            {
                mr.sharedMaterial = WorldTextMaterial.Shared;
            }

            tm.fontSize = 64;
            tm.characterSize = 0.035f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = TextColour;
            root.SetActive(false);
            return new Board { LineId = lineId, Stop = stop, Root = root, Text = tm, Plate = plate.transform };
        }

        /// <summary>Positions one board over its stop, turns it to the camera, hides it out of range and refreshes its text.</summary>
        private void Place(Board b, Camera cam, bool hide, bool refresh)
        {
            var spline = Rails.SplineOf(b.LineId);
            var eta = spline != null && _etas.TryGetValue(b.LineId, out var e) && ReferenceEquals(e.Spline, spline) ? e : null;
            if (hide || eta == null || b.Stop >= eta.StopCount)
            {
                SetActive(b, false);
                return;
            }

            var p = spline.PointAt(eta.StopArc(b.Stop));
            var pos = Game.ScenePos(p.X, p.Y + BoardRise, p.Z);
            var toBoard = pos - cam.transform.position;
            if (toBoard.sqrMagnitude > ShowRange * ShowRange)
            {
                SetActive(b, false);
                return;
            }

            SetActive(b, true);
            b.Root.transform.position = pos;
            toBoard.y = 0f;
            if (toBoard.sqrMagnitude > 1e-4f)
            {
                b.Root.transform.rotation = Quaternion.LookRotation(toBoard.normalized, Vector3.up);
            }

            if (refresh || b.Shown.Length == 0)
            {
                SetText(b, BoardText(eta, b));
            }
        }

        private static void SetActive(Board b, bool on)
        {
            if (b.Root.activeSelf != on)
            {
                b.Root.SetActive(on);
                if (!on)
                {
                    b.Shown = string.Empty; // re-read on the next show
                }
            }
        }

        private void SetText(Board b, string text)
        {
            if (text == b.Shown)
            {
                return;
            }

            b.Shown = text;
            b.Text.text = text;
            float width = 0f;
            int lines = 0;
            foreach (string line in text.Split('\n'))
            {
                width = Mathf.Max(width, LineWidth(b.Text, line));
                lines++;
            }

            float lineHeight = b.Text.fontSize * b.Text.characterSize * 0.1f * b.Text.lineSpacing;
            b.Plate.localScale = new Vector3(Mathf.Max(2.5f, width + 0.5f), Mathf.Max(0.9f, (lines * lineHeight) + 0.3f), 1f);
        }

        /// <summary>A line's width in world units, from the font's own advances (the text mesh's world bounds turn with the
        /// board, so they do not tell the width).</summary>
        private static float LineWidth(TextMesh tm, string line)
        {
            var font = tm.font;
            if (font == null || string.IsNullOrEmpty(line))
            {
                return line == null ? 0f : line.Length * 0.15f;
            }

            font.RequestCharactersInTexture(line, tm.fontSize, tm.fontStyle);
            int advance = 0;
            foreach (char c in line)
            {
                if (font.GetCharacterInfo(c, out var info, tm.fontSize, tm.fontStyle))
                {
                    advance += info.advance;
                }
            }

            return advance * tm.characterSize * 0.1f;
        }

        /// <summary>The three lines: the title, the destination, the next train.</summary>
        private string BoardText(RailEta eta, Board b)
        {
            string dest = Destination(eta, b.Stop);
            string state = StateLine(eta, b);
            return L("ui.train.board_title", "Departures") + "\n→ " + dest + "\n" + state;
        }

        private string StateLine(RailEta eta, Board b)
        {
            var train = PublicTrainOn(b.LineId);
            var reading = train != null ? eta.Read(train, b.Stop) : RailEtaReading.None;
            double age = System.Math.Max(0.0, Time.timeAsDouble - Game.TrainsReceivedAt);
            double secs = reading.Seconds - age;
            switch (reading.State)
            {
                case RailEtaState.Here:
                    return string.Format(L("ui.train.board_here", "Train is here · departs in {0} s"), Mathf.Max(0, Mathf.CeilToInt((float)secs)));
                case RailEtaState.Coming when secs <= ArrivingSeconds:
                    return L("ui.train.board_arriving", "Train arriving");
                case RailEtaState.Coming:
                    int whole = Mathf.CeilToInt((float)secs);
                    return string.Format(L("ui.train.board_next", "Next train in {0}"), $"{whole / 60}:{whole % 60:00}");
                default:
                    return "—";
            }
        }

        /// <summary>The other station's map name: the <c>rail_station</c> point of interest nearest to the line's other stop
        /// (the stop farthest along the line from this one when it has more than two).</summary>
        private string Destination(RailEta eta, int stop)
        {
            int other = -1;
            float farthest = -1f;
            for (int i = 0; i < eta.StopCount; i++)
            {
                float gap = Mathf.Abs(eta.StopArc(i) - eta.StopArc(stop));
                if (i != stop && gap > farthest)
                {
                    farthest = gap;
                    other = i;
                }
            }

            if (other < 0)
            {
                return "—";
            }

            var at = eta.Spline.PointAt(eta.StopArc(other));
            string best = "—";
            double bestSq = double.MaxValue;
            foreach (var poi in Game.PlanetPois ?? System.Array.Empty<NetPoi>())
            {
                if (poi == null || poi.Type != "rail_station")
                {
                    continue;
                }

                double dx = Shared.World.WorldConstants.WrapDeltaX((double)poi.X - at.X, Game.Circumference), dz = poi.Z - at.Z;
                double sq = (dx * dx) + (dz * dz);
                if (sq < bestSq)
                {
                    bestSq = sq;
                    best = poi.Name;
                }
            }

            return best;
        }

        private string L(string key, string fallback)
        {
            var loc = Game != null ? Game.Localizer : null;
            return loc != null && loc.Has(key) ? loc.Get(key) : fallback;
        }
    }
}
