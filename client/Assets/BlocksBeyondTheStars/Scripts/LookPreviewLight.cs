// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The fixed look light of one editor preview camera (<see cref="LookPreview"/>). Right before the
    /// camera draws (<see cref="RenderPipelineManager.beginCameraRendering"/>, the per-camera pattern <see cref="Sky"/>'s
    /// haze uses) it notes the globals it is about to change and sets the preview's own; right after the camera
    /// (<see cref="RenderPipelineManager.endCameraRendering"/>) it puts every one of them back, so a world camera drawn
    /// later in the same frame sees exactly the values the world's owners set. "As in the world": the models' look light
    /// (<c>_Sc_LookModel</c> 1 with the fixed sun, sky and ground light and the rim) and, with <see cref="Blocks"/>, what
    /// the block shader reads besides — the sun colour and direction, the sky, the look's calm, soft edges, joints and
    /// grain, a world colour for plants, and nothing of the weather, the water, the room or the staged crowns. "Pixels
    /// only": <c>_Sc_LookModel</c> 0, the classic studio light, in the menu and in a world alike. The camera draws no sun
    /// shadows while it shows the look: the shadow map follows the world's real sun, the preview's sun is fixed.
    /// </summary>
    public sealed class LookPreviewLight : MonoBehaviour
    {
        private static readonly int LookId = Shader.PropertyToID("_Sc_Look");
        private static readonly int Look2Id = Shader.PropertyToID("_Sc_Look2");
        private static readonly int Look3Id = Shader.PropertyToID("_Sc_Look3");
        private static readonly int Look4Id = Shader.PropertyToID("_Sc_Look4");
        private static readonly int LookPlantId = Shader.PropertyToID("_Sc_LookPlant");
        private static readonly int LookLeafId = Shader.PropertyToID("_Sc_LookLeaf");
        private static readonly int AmbSkyId = Shader.PropertyToID("_Sc_AmbSky");
        private static readonly int AmbGroundId = Shader.PropertyToID("_Sc_AmbGround");
        private static readonly int LookSunHueId = Shader.PropertyToID("_Sc_LookSunHue");
        private static readonly int LookSunDirId = Shader.PropertyToID("_Sc_LookSunDir");
        private static readonly int LightId = Shader.PropertyToID("_Sc_Light");
        private static readonly int SunDirId = Shader.PropertyToID("_Sc_SunDir");
        private static readonly int SkyId = Shader.PropertyToID("_Sc_Sky");
        private static readonly int FloraTintId = Shader.PropertyToID("_Sc_FloraTint");
        private static readonly int SurfaceId = Shader.PropertyToID("_Sc_Surface");
        private static readonly int UnderwaterId = Shader.PropertyToID("_Sc_Underwater");
        private static readonly int DistSoftId = Shader.PropertyToID("_Sc_DistSoft");
        private static readonly int RoomProbeId = Shader.PropertyToID("_Sc_RoomProbe");
        private static readonly int WindId = Shader.PropertyToID("_Sc_Wind");
        private static readonly int GlowHazeId = Shader.PropertyToID("_Sc_LookGlowHaze");
        private static readonly int LookModelId = Shader.PropertyToID("_Sc_LookModel");
        private static readonly int ModelLightId = Shader.PropertyToID("_Sc_ModelLight");
        private static readonly int IndoorId = Shader.PropertyToID("_Sc_Indoor");

        private static readonly int[] VectorIds =
        {
            LookId, Look2Id, Look3Id, Look4Id, LookPlantId, LookLeafId, AmbSkyId, AmbGroundId, LookSunHueId, LookSunDirId,
            LightId, SunDirId, SkyId, FloraTintId, SurfaceId, UnderwaterId, DistSoftId, RoomProbeId, WindId, GlowHazeId,
        };

        private static readonly int[] FloatIds = { LookModelId, ModelLightId, IndoorId };

        private readonly Vector4[] _vectors = new Vector4[VectorIds.Length];
        private readonly float[] _floats = new float[FloatIds.Length];
        private Camera _camera;
        private bool _saved;
        private bool _shadowsKnown;
        private bool _shadowsBefore;

        /// <summary>True while the camera shows blocks drawn with the block shader (the texture editor's wall, the shape
        /// editors' forms): then the block shader's globals are set too.</summary>
        public bool Blocks;

        /// <summary>For the block shader: the mip of the atlas it draws from that holds one texel per tile (6 for the
        /// world's 64 px tiles; the texture editor's preview atlas tells its own).</summary>
        public float MeanMip = 6f;

        /// <summary>The world colour plants and crowns show where their faces carry none (linear, a = 1 to apply it;
        /// zero = keep the tile's own colour).</summary>
        public Vector4 FloraTint;

        /// <summary>True while the blocks shown are a tile being PAINTED (the texture editor's wall), and painted tiles
        /// stay as painted (<see cref="GameTextures.ShowsPaintedTile"/>): the texture calm is off for this camera, as it
        /// will be for the tile's atlas slot in the world once the painting is saved. The world atlas' calm mask cannot
        /// say so yet — it still describes the tile the slot holds today.</summary>
        public bool PaintedTile;

        private void Awake() => _camera = GetComponent<Camera>();

        private void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += OnBegin;
            RenderPipelineManager.endCameraRendering += OnEnd;
            LookPreview.Changed += ApplyShadows;
            ApplyShadows();
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= OnBegin;
            RenderPipelineManager.endCameraRendering -= OnEnd;
            LookPreview.Changed -= ApplyShadows;
            Restore(); // a camera switched off between its begin and its end must not leave its light behind
        }

        private void OnBegin(ScriptableRenderContext context, Camera camera)
        {
            if (camera != _camera || _camera == null)
            {
                return;
            }

            Save();
            if (LookPreview.AsInWorld)
            {
                PublishModels();
                if (Blocks)
                {
                    PublishBlocks();
                }
            }
            else
            {
                Shader.SetGlobalFloat(LookModelId, 0f); // the classic studio light, wherever the editor was opened
            }
        }

        private void OnEnd(ScriptableRenderContext context, Camera camera)
        {
            if (camera == _camera)
            {
                Restore();
            }
        }

        private void Save()
        {
            for (int i = 0; i < VectorIds.Length; i++)
            {
                _vectors[i] = Shader.GetGlobalVector(VectorIds[i]);
            }

            for (int i = 0; i < FloatIds.Length; i++)
            {
                _floats[i] = Shader.GetGlobalFloat(FloatIds[i]);
            }

            _saved = true;
        }

        private void Restore()
        {
            if (!_saved)
            {
                return;
            }

            _saved = false;
            for (int i = 0; i < VectorIds.Length; i++)
            {
                Shader.SetGlobalVector(VectorIds[i], _vectors[i]);
            }

            for (int i = 0; i < FloatIds.Length; i++)
            {
                Shader.SetGlobalFloat(FloatIds[i], _floats[i]);
            }
        }

        /// <summary>The models' look light (LitColor), built like <c>Sky.PublishLook</c> from the fixed sun and sky.</summary>
        private void PublishModels()
        {
            Color light = ShaderColor.Srgb(LookPreview.SunTint);
            float lum = Mathf.Max(Luminance(light), 1e-4f);
            var hue = new Color(light.r / lum, light.g / lum, light.b / lum);
            Color sky = ShaderColor.Srgb(LookPreview.SkyTint);
            float skyLum = Mathf.Max(Luminance(sky), 1e-4f);
            var skyHue = new Color(sky.r / skyLum, sky.g / skyLum, sky.b / skyLum);
            Color ambSky = Color.Lerp(hue, skyHue, Mathf.Clamp01(GameLook.AmbSat)) * lum;
            Color ambGround = hue * (lum * GameLook.GroundLevel);
            Vector3 sun = LookPreview.SunDirection;

            float calm = Blocks && PaintedTile ? 0f : Mathf.Clamp01(GameLook.Calm);
            Shader.SetGlobalVector(LookId, new Vector4(1f, calm, GameLook.AmbShare, GameLook.SunShare));
            // x = the mean mip of the atlas the blocks draw from; y = 0: crowns as plain leaf cutout (the staged crowns'
            // near clusters live in the world only); z = 0: no flutter; w = the hand's share of the world light.
            Shader.SetGlobalVector(Look2Id, new Vector4(MeanMip, 0f, 0f, Mathf.Clamp01(GameLook.Hand)));
            Shader.SetGlobalVector(Look3Id, new Vector4(
                Mathf.Clamp(GameLook.SoftEdge, 0f, 0.4f), Mathf.Clamp01(GameLook.Joint), Mathf.Clamp(GameLook.Grain, 0f, 2f),
                Mathf.Clamp(GameLook.Rim, 0f, 2f)));
            Shader.SetGlobalVector(AmbSkyId, new Vector4(ambSky.r, ambSky.g, ambSky.b, 1f));
            Shader.SetGlobalVector(AmbGroundId, new Vector4(ambGround.r, ambGround.g, ambGround.b, 1f));
            Shader.SetGlobalVector(LookSunHueId, new Vector4(hue.r, hue.g, hue.b, lum));
            Shader.SetGlobalVector(LookSunDirId, new Vector4(sun.x, sun.y, sun.z, 0f));
            Shader.SetGlobalFloat(LookModelId, 1f);
            Shader.SetGlobalFloat(ModelLightId, 1f);
        }

        /// <summary>What the block shader reads besides: a clear day with no weather, no water, no room and no staged
        /// crowns or plant bodies (the preview draws neither), the world colour for plants.</summary>
        private void PublishBlocks()
        {
            Color light = ShaderColor.Srgb(LookPreview.SunTint);
            Color sky = ShaderColor.Srgb(LookPreview.SkyTint);
            Vector3 sun = LookPreview.SunDirection;
            Shader.SetGlobalVector(LightId, new Vector4(light.r, light.g, light.b, 1f));
            Shader.SetGlobalVector(SunDirId, new Vector4(sun.x, sun.y, sun.z, 0f));
            Shader.SetGlobalVector(SkyId, new Vector4(sky.r, sky.g, sky.b, 1f));
            Shader.SetGlobalVector(FloraTintId, FloraTint);
            Shader.SetGlobalVector(Look4Id, Vector4.zero);
            Shader.SetGlobalVector(LookPlantId, Vector4.zero);
            Shader.SetGlobalVector(LookLeafId, Vector4.zero);
            Shader.SetGlobalVector(SurfaceId, Vector4.zero);
            Shader.SetGlobalVector(UnderwaterId, Vector4.zero);
            Shader.SetGlobalVector(DistSoftId, Vector4.zero);
            Shader.SetGlobalVector(RoomProbeId, Vector4.zero);
            Shader.SetGlobalVector(WindId, Vector4.zero);
            Shader.SetGlobalVector(GlowHazeId, Vector4.zero);
            Shader.SetGlobalFloat(IndoorId, 0f);
        }

        /// <summary>No sun shadows while the preview shows the look (its sun is not the shadow map's); the camera's own
        /// setting back for "pixels only".</summary>
        private void ApplyShadows()
        {
            var data = _camera != null ? _camera.GetUniversalAdditionalCameraData() : null;
            if (data == null)
            {
                return;
            }

            if (!_shadowsKnown)
            {
                _shadowsBefore = data.renderShadows;
                _shadowsKnown = true;
            }

            data.renderShadows = !LookPreview.AsInWorld && _shadowsBefore;
        }

        private static float Luminance(Color linear) => (0.2126f * linear.r) + (0.7152f * linear.g) + (0.0722f * linear.b);
    }
}
