// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The landing descent (#3140): the camera flies with the ship as it turns belly-first and dives in a long arc at the
    /// destination's SURFACE — the planet growing until it fills the view — instead of shrinking into the distance and
    /// vanishing inside the planet sphere. In an atmosphere a bow shock builds in front of the leading face (an open cap of
    /// the plasma shell, not a bubble), plasma ribbons stream back off its rim like a comet, the heat lights the hull, the
    /// view shakes and the roar swells, coloured by the destination's own sky; the stars fade as the sky turns from black
    /// to the planet's colour, and the ship punches into the cloud layer — the <see cref="TransitionCover"/>, behind which
    /// the surface takes over. An airless body gets no plasma: the retro burn flares toward the ground and its glare is the
    /// cover. The look reads the DESTINATION's atmosphere (#3140: the old sheath read the world the ship came from).
    /// </summary>
    public sealed partial class SpaceView
    {
        private const float DescentSeconds = 3.9f;
        private const float DescentCoverAt = 0.7f;   // the clouds (or the glare) start to close at this fraction

        private Vector3 _descentFrom, _descentCtrl, _descentTo, _descentBody;
        private Vector3 _descentVel = Vector3.down;
        private Vector3 _descentCam;
        private bool _descentAirless, _descentCovered, _descentRoar;
        private Color _descentSky, _descentCloud, _descentPlasma;
        private string _descentName = string.Empty, _descentSub = string.Empty;
        private float _descentHeat;
        private MeshRenderer _shock;
        private static Mesh _shockMesh;

        /// <summary>#3152: how long a descent started on the pad pick waits for the server to close the flight before it is
        /// called off (the server builds the destination world first — seconds, never this long). Below the cover's own
        /// give-up (<c>TransitionGate.GiveUp</c>, 25 s from the cover at ~2.7 s), so the cover never opens on the held dive.</summary>
        private const float DescentAnswerTimeout = 24f;

        private bool _descentAwaitingServer; // #3152: started on the pick; the server has not closed the flight yet
        private int _descentRefusalsSeen;
        private float _descentWait;
        private Quaternion _descentFromRot = Quaternion.identity;

        private TransitionDirector Transitions => Game != null ? Game.Views.Transitions : null;

        /// <summary>
        /// #3152: the pad is picked — the descent starts NOW. It used to wait for the server to close the flight, and since
        /// #3139 the server builds the destination world first, so the ship hung in place for the seconds that takes. The
        /// cover closes at the usual point and the flight view holds behind it until the server answers; a refused landing
        /// (a pad taken or built over meanwhile) or no answer calls it off (<see cref="AbortDescent"/>).
        /// </summary>
        private void BeginEarlyDescent()
        {
            if (_phase != Phase.Cruise || _eva || Game == null || _shipDestroyed || Game.AwaitingRespawnConfirm || !Game.InSpace)
            {
                return; // the flight closing starts the descent as before (or there is nothing left to fly)
            }

            _phase = Phase.Landing;
            _seq = 0f;
            _descentAwaitingServer = true;
            _descentWait = 0f;
            _descentRefusalsSeen = Game.LandRefusals;
            ClientAudio.Instance?.Cue("ship_landing");
            BeginDescent();
        }

        /// <summary>#3152: the server refused the landing (or never answered): the cover opens on the flight again, the ship
        /// is back where the pilot picked the pad, and the controls are theirs. The refusal's reason is already on screen.
        /// A timeout keeps the destination: should the server's close still come, its descent flies at the picked body.</summary>
        private void AbortDescent(bool keepDestination)
        {
            _descentAwaitingServer = false;
            if (_descentCovered)
            {
                Transitions?.CancelArrival();
            }

            _phase = Phase.Cruise;
            _seq = 0f;
            if (!keepDestination)
            {
                _landDestBody = null;
            }

            _descentHeat = 0f;
            if (_ship != null)
            {
                _ship.transform.localPosition = _descentFrom;
                _ship.transform.localRotation = _descentFromRot;
            }

            if (_shock != null)
            {
                _shock.enabled = false;
            }

            if (_engineLight != 0)
            {
                FxLights.Set(_engineLight, Color.black, 0f);
            }

            if (Camera != null)
            {
                Camera.backgroundColor = SpaceBackground;
            }

            Starfield.SkyFade = 1f;
        }

        /// <summary>#3152: while an early descent waits for the server — true once it was called off this frame.</summary>
        private bool EarlyDescentCalledOff()
        {
            if (!Game.InSpace)
            {
                _descentAwaitingServer = false; // the flight closed (also with the destruction prompt up) — nothing to wait for
                return false;
            }

            _descentWait += Time.deltaTime;
            bool refused = Game.LandRefusals != _descentRefusalsSeen;
            if (!refused && _descentWait < DescentAnswerTimeout)
            {
                return false;
            }

            if (!refused)
            {
                Game.ShowMessage(Loc("ui.space.pad_timeout", "No landing data from the server - please try again."));
            }

            AbortDescent(keepDestination: !refused);
            return true;
        }

        /// <summary>Plans the descent: the body (the pad chooser's, else the one ahead of the nose), a point on its surface
        /// a little ahead along the ship's heading, and the arc there; reads the body's atmosphere for the look.</summary>
        private void BeginDescent()
        {
            _descentFrom = _ship != null ? _ship.transform.localPosition : Vector3.zero;
            _descentFromRot = _ship != null ? _ship.transform.localRotation : Quaternion.identity;
            var fwd = _ship != null ? _ship.transform.forward : Vector3.forward;
            ResolveDescentBody(fwd, out var body, out float radius, out string name);
            _descentBody = body;
            var outward = _descentFrom - body;
            outward = outward.sqrMagnitude > 1e-4f ? outward.normalized : Vector3.up;
            var tangent = Vector3.ProjectOnPlane(fwd, outward);
            tangent = tangent.sqrMagnitude > 1e-4f ? tangent.normalized : Vector3.Cross(outward, Vector3.right).normalized;
            var surfaceDir = (outward * 0.85f + tangent * 0.5f).normalized;
            _descentTo = body + surfaceDir * (radius * 1.01f + 1.5f);
            float span = Vector3.Distance(_descentFrom, _descentTo);
            _descentCtrl = _descentFrom + tangent * span * 0.35f + outward * span * 0.08f;
            _descentVel = (_descentTo - _descentFrom).normalized;
            _descentCam = Camera != null ? Camera.transform.localPosition : _descentFrom;
            _descentName = name;
            _descentCovered = false;
            _descentRoar = false;
            _descentHeat = 0f;
            ReadDescentAtmosphere(body);
        }

        private void ResolveDescentBody(Vector3 fwd, out Vector3 body, out float radius, out string name)
        {
            body = new Vector3(0f, -150f, -20f);
            radius = 60f;
            name = string.Empty;
            string want = _landDestBody ?? string.Empty;
            if (Game?.StarMap != null && want == Game.StarMap.ActiveLocationId)
            {
                // The launch body is listed under "" (SpaceView.Scene); the chooser resolves "" to its real id (#956), so the
                // descent never found it and dived at whatever was ahead of the nose — with that body's name and sky.
                want = string.Empty;
            }
            float best = -2f;
            foreach (var b in _landables)
            {
                if (b.Id == want)
                {
                    (body, radius, name) = (b.Pos, b.Radius, b.Name);
                    return;
                }

                var to = b.Pos - _descentFrom;
                float dot = to.sqrMagnitude > 1e-4f ? Vector3.Dot(to.normalized, fwd) : -1f;
                if (dot > best)
                {
                    best = dot;
                    (body, radius, name) = (b.Pos, b.Radius, b.Name);
                }
            }
        }

        /// <summary>The destination's own sky, clouds and air — from the flight scene's body nearest the landing point.</summary>
        private void ReadDescentAtmosphere(Vector3 body)
        {
            OrbitBody look = null;
            float bestSq = float.MaxValue;
            foreach (var ob in _orbitBodies)
            {
                float sq = (ob.Pos - body).sqrMagnitude;
                if (sq < bestSq)
                {
                    bestSq = sq;
                    look = ob;
                }
            }

            var atm = look != null ? look.Atmosphere : default;
            _descentAirless = look != null && !atm.HasAir;
            _descentSky = look != null ? TransitionDirector.Rgb(atm.SkyRgb) : new Color(0.55f, 0.75f, 0.95f);
            _descentCloud = look != null ? TransitionDirector.Rgb(atm.CloudRgb) : Color.white;
            // Hot plasma is orange-white everywhere; the outer glow takes the air's own colour (a green toxic sky glows
            // green-gold, a violet one magenta) — breathable air the classic salmon pink.
            var air = atm.Breathable ? new Color(1f, 0.5f, 0.42f) : Color.Lerp(_descentSky, new Color(1f, 0.45f, 0.2f), 0.45f);
            _descentPlasma = Color.Lerp(new Color(1f, 0.55f, 0.22f), air, 0.55f);
            string type = look?.Planet?.Key;
            var loc = Game.Localizer;
            _descentSub = !string.IsNullOrEmpty(type) && loc != null && loc.Has($"planet.{type}.name") ? loc.Get($"planet.{type}.name") : string.Empty;
        }

        private void UpdateDescent()
        {
            if (_descentAwaitingServer && EarlyDescentCalledOff())
            {
                return;
            }

            float dt = Time.deltaTime;
            _seq += dt;
            if (_descentAwaitingServer)
            {
                _seq = Mathf.Min(_seq, DescentSeconds); // #3152: behind the shut cover until the server has closed the flight
            }

            float t = Mathf.Clamp01(_seq / DescentSeconds);
            float p = t * t * (3f - 2f * t);
            var pos = Bezier(_descentFrom, _descentCtrl, _descentTo, p);
            var vel = 2f * (1f - p) * (_descentCtrl - _descentFrom) + 2f * p * (_descentTo - _descentCtrl);
            if (vel.sqrMagnitude > 1e-4f)
            {
                _descentVel = vel.normalized;
            }

            if (_ship != null)
            {
                _ship.transform.localPosition = pos;
                _ship.transform.localRotation = Quaternion.Slerp(_ship.transform.localRotation, EntryAttitude(pos), dt * 2.5f);
            }

            _descentHeat = _descentAirless ? 0f : Smooth(0.16f, 0.42f, t) * (1f - Smooth(0.74f, 0.93f, t));
            EntryFx(t);
            DescentSky(t);
            if (!_descentCovered && t >= DescentCoverAt)
            {
                _descentCovered = true;
                Transitions?.CoverLanding(_descentName, _descentSub, _descentAirless, _descentSky, _descentCloud, (1f - DescentCoverAt) * DescentSeconds * 0.85f);
            }

            bool shut = Transitions == null || Transitions.CoverShut;
            if (!_descentAwaitingServer && ((_seq >= DescentSeconds && shut) || _seq >= DescentSeconds + 3f))
            {
                Exit();
            }
        }

        /// <summary>Belly into the airflow with the nose pitched up — the way a heat shield meets the air.</summary>
        private Quaternion EntryAttitude(Vector3 pos)
        {
            var outward = (pos - _descentBody).normalized;
            var nose = (_descentVel + outward * 0.9f).normalized;
            var up = -(_descentVel - Vector3.Dot(_descentVel, nose) * nose);
            return up.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(nose, up.normalized) : Quaternion.LookRotation(nose);
        }

        private void EntryFx(float t)
        {
            if (_ship == null)
            {
                return;
            }

            var b = ShipLocalBounds();
            var centre = _ship.transform.TransformPoint(b.center);
            float size = b.extents.magnitude * _ship.transform.lossyScale.x;
            var lead = _root.transform.TransformDirection(_descentVel);
            if (!_descentRoar && t > 0.14f)
            {
                _descentRoar = true;
                ClientAudio.Instance?.Cue(_descentAirless ? "engine_thrust" : "reentry_roar", 0.95f);
            }

            if (_descentAirless)
            {
                RetroBurn(centre, lead, size, t);
                return;
            }

            UpdateShock(centre, lead, size);
            PlasmaRibbons(centre, lead, size);
            FxCamera.AddTrauma(_descentHeat * Time.deltaTime * 0.9f);
            if (_engineLight != 0)
            {
                FxLights.Set(_engineLight, _descentPlasma, 3f * _descentHeat);
            }
        }

        /// <summary>Places and tints the bow shock: an open cap ahead of the leading face, hottest at its front.</summary>
        private void UpdateShock(Vector3 centre, Vector3 lead, float size)
        {
            if (_shock == null)
            {
                _shock = BuildShock();
                if (_shock == null)
                {
                    return;
                }
            }

            _shock.transform.SetPositionAndRotation(centre + lead * size * 0.45f, Quaternion.LookRotation(lead));
            _shock.transform.localScale = new Vector3(size * 1.55f, size * 1.55f, size * 1.15f);
            var block = FxKit.Block;
            block.Clear();
            var hot = Color.Lerp(_descentPlasma, new Color(1f, 0.93f, 0.75f), _descentHeat * 0.5f);
            block.SetColor("_Color", FxKit.Lin(hot) * new Color(1f, 1f, 1f, _descentHeat));
            block.SetFloat("_Intensity", 2.4f);
            _shock.SetPropertyBlock(block);
            _shock.enabled = _descentHeat > 0.01f;
        }

        /// <summary>Plasma ribbons torn off the shock's rim and streaming back, embers, a flicker of glow at the front.</summary>
        private void PlasmaRibbons(Vector3 centre, Vector3 lead, float size)
        {
            if (_descentHeat < 0.05f)
            {
                return;
            }

            var side = Vector3.Cross(lead, Vector3.up);
            side = side.sqrMagnitude > 1e-4f ? side.normalized : Vector3.right;
            var up = Vector3.Cross(side, lead).normalized;
            int n = Mathf.RoundToInt(5f * _descentHeat * FxKit.Density) + 1;
            for (int i = 0; i < n; i++)
            {
                float a = Random.value * Mathf.PI * 2f;
                var rim = centre + lead * size * 0.25f + (side * Mathf.Cos(a) + up * Mathf.Sin(a)) * size * 0.75f;
                var c = Color.Lerp(_descentPlasma, new Color(1f, 0.9f, 0.7f), Random.value * 0.6f);
                FxKit.Emit(FxKit.Kind.Sparks, rim, -lead * Random.Range(28f, 46f) + Random.insideUnitSphere * 3f, Random.Range(0.18f, 0.34f), Random.Range(0.22f, 0.4f), c);
            }

            if (Random.value < 0.5f * _descentHeat)
            {
                FxKit.Emit(FxKit.Kind.Motes, centre + Random.insideUnitSphere * size * 0.5f, -lead * 16f + Random.insideUnitSphere * 2f, Random.Range(0.15f, 0.3f), 0.4f, new Color(1f, 0.7f, 0.4f));
                FxKit.Flash(centre + lead * size * 0.9f, Color.Lerp(_descentPlasma, Color.white, 0.4f), size * 0.8f, 0.1f);
            }
        }

        /// <summary>An airless body: the engines fire toward the ground, their glare growing as the surface nears.</summary>
        private void RetroBurn(Vector3 centre, Vector3 lead, float size, float t)
        {
            float power = Smooth(0.35f, 0.75f, t);
            int n = Mathf.RoundToInt(4f * power * FxKit.Density);
            for (int i = 0; i < n; i++)
            {
                var hot = Color.Lerp(new Color(1f, 0.6f, 0.25f), new Color(1f, 0.95f, 0.8f), Random.value);
                FxKit.Emit(FxKit.Kind.Glow, centre + lead * size * 0.6f + Random.insideUnitSphere * size * 0.3f, lead * Random.Range(14f, 24f), size * Random.Range(0.4f, 0.7f), Random.Range(0.12f, 0.22f), hot);
            }

            FxCamera.AddTrauma(power * Time.deltaTime * 0.5f);
            if (_engineLight != 0)
            {
                FxLights.Set(_engineLight, new Color(1f, 0.7f, 0.4f), 3f * power);
            }
        }

        /// <summary>Black space turns into the destination's sky as the ship drops into its air; the stars fade with it.</summary>
        private void DescentSky(float t)
        {
            if (Camera == null || _descentAirless)
            {
                return;
            }

            float sky = Smooth(0.5f, 0.88f, t);
            Camera.backgroundColor = Color.Lerp(SpaceBackground, _descentSky, sky);
            Starfield.SkyFade = 1f - sky;
        }

        /// <summary>The descent camera: chasing a little behind, above and beside the ship, looking down its path at the
        /// growing planet with the horizon kept level; it pulls in as the clouds come.</summary>
        private void PlaceDescentCamera()
        {
            var sp = _ship.transform.localPosition;
            var outward = (sp - _descentBody).normalized;
            var side = Vector3.Cross(_descentVel, outward);
            side = side.sqrMagnitude > 1e-4f ? side.normalized : Vector3.right;
            float t = Mathf.Clamp01(_seq / DescentSeconds);
            float back = Mathf.Lerp(12f, 8f, t);
            var want = sp - _descentVel * back + outward * Mathf.Lerp(5f, 3f, t) + side * 3.5f;
            _descentCam = Vector3.Lerp(_descentCam, want, 1f - Mathf.Exp(-Time.deltaTime * 3.5f));
            var look = sp + _descentVel * 8f;
            Camera.transform.localPosition = _descentCam;
            var aim = Quaternion.LookRotation(look - _descentCam, outward); // the horizon turns level, never snaps
            Camera.transform.localRotation = Quaternion.Slerp(Camera.transform.localRotation, aim, 1f - Mathf.Exp(-Time.deltaTime * 4f));
            Camera.fieldOfView = _flightFov + 7f * _descentHeat;
        }

        private MeshRenderer BuildShock()
        {
            var mat = FxKit.Cached("BlocksBeyondTheStars/FxShell", "bowshock", m =>
            {
                m.SetFloat("_Plasma", 1f);
                m.SetFloat("_RimPower", 2.2f);
                m.SetFloat("_Hex", 0f);
                m.SetFloat("_Fill", 0.03f);
            });
            if (mat == null || _root == null)
            {
                return null;
            }

            var go = new GameObject("BowShock");
            go.transform.SetParent(_root.transform, true);
            go.AddComponent<MeshFilter>().sharedMesh = ShockMesh();
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return mr;
        }

        /// <summary>The front cap of a unit sphere (z from -0.25 to 1), open at the back — the plasma shell's own
        /// object-space sphere maths still apply, so its lead (+Z) is the hottest.</summary>
        private static Mesh ShockMesh()
        {
            if (_shockMesh != null)
            {
                return _shockMesh;
            }

            const int rings = 10, seg = 32;
            var verts = new Vector3[(rings + 1) * (seg + 1)];
            var tris = new int[rings * seg * 6];
            for (int r = 0; r <= rings; r++)
            {
                float polar = Mathf.Lerp(0f, Mathf.Acos(-0.25f), r / (float)rings);
                for (int s = 0; s <= seg; s++)
                {
                    float az = s / (float)seg * Mathf.PI * 2f;
                    verts[r * (seg + 1) + s] = new Vector3(Mathf.Sin(polar) * Mathf.Cos(az), Mathf.Sin(polar) * Mathf.Sin(az), Mathf.Cos(polar)) * 0.5f;
                }
            }

            int k = 0;
            for (int r = 0; r < rings; r++)
            {
                for (int s = 0; s < seg; s++)
                {
                    int a = r * (seg + 1) + s, b = a + seg + 1;
                    tris[k++] = a; tris[k++] = b; tris[k++] = a + 1;
                    tris[k++] = a + 1; tris[k++] = b; tris[k++] = b + 1;
                }
            }

            _shockMesh = new Mesh { name = "FxBowShock", vertices = verts, triangles = tris };
            _shockMesh.RecalculateNormals();
            _shockMesh.RecalculateBounds();
            return _shockMesh;
        }

        private static Vector3 Bezier(Vector3 a, Vector3 b, Vector3 c, float t)
            => (1f - t) * (1f - t) * a + 2f * (1f - t) * t * b + t * t * c;

        private static float Smooth(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / Mathf.Max(1e-4f, b - a));
            return t * t * (3f - 2f * t);
        }

        /// <summary>The flight view's clear colour — black space with a hint of blue.</summary>
        private static readonly Color SpaceBackground = new Color(0.02f, 0.03f, 0.06f);
    }
}
