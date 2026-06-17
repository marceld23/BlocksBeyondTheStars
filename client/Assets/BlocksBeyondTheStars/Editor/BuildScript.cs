#if UNITY_EDITOR
using System.IO;
using BlocksBeyondTheStars.Client;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace BlocksBeyondTheStars.Client.EditorTools
{
    /// <summary>
    /// Builds the Windows player (M28). The launcher scene is normally created at runtime, so
    /// this first generates and saves a minimal scene containing a single <see cref="AppShell"/>
    /// GameObject, registers it in the build settings, then builds. Run from the editor menu
    /// (BlocksBeyondTheStars → Build Windows Player) or headless via <c>scripts/build-client.ps1</c>.
    ///
    /// The build automatically includes everything under <c>StreamingAssets/</c> (the synced
    /// data and, if published, the bundled local server), so run sync-client-libs.ps1 and
    /// publish-local-server.ps1 first for a self-contained singleplayer build.
    /// </summary>
    public static class BuildScript
    {
        private const string SceneDir = "Assets/Scenes";
        private const string ScenePath = SceneDir + "/Launcher.unity";

        /// <summary>Shaders the client loads at runtime via Shader.Find — must be force-included or the build strips them.</summary>
        private static readonly string[] RuntimeShaders =
        {
            "BlocksBeyondTheStars/VertexColorOpaque",
            "BlocksBeyondTheStars/BlockAtlas",
            "BlocksBeyondTheStars/LitColor",
            "BlocksBeyondTheStars/SunGlow",
            "BlocksBeyondTheStars/Nebula",
            "BlocksBeyondTheStars/Atmosphere",
            "BlocksBeyondTheStars/Particle",
            "BlocksBeyondTheStars/ParticleAlpha",
            "BlocksBeyondTheStars/VolumetricFog",
            "BlocksBeyondTheStars/Cloud",
            "BlocksBeyondTheStars/PostBloom",
            "BlocksBeyondTheStars/PostComposite",
            "BlocksBeyondTheStars/PostAO",
            "Unlit/Color",
        };

        [MenuItem("BlocksBeyondTheStars/Build Windows Player")]
        public static void BuildWindows()
        {
            EnsureLauncherScene();
            EnsureShadersIncluded();
            EnsureRendererFeatures();
            EnsureAppIcon();

            string outDir = GetArg("-buildOut") ?? "Build/Windows";
            Directory.CreateDirectory(outDir);

            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = Path.Combine(outDir, "BlocksBeyondTheStars.exe"),
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };

            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;
            Debug.Log($"BlocksBeyondTheStars build: {summary.result} → {summary.outputPath} ({summary.totalSize} bytes)");

            if (summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            {
                EditorApplication.Exit(1);
            }
        }

        /// <summary>
        /// Adds the runtime <see cref="Shader.Find"/> shaders to GraphicsSettings' "Always Included
        /// Shaders" so the player build keeps them (otherwise Shader.Find returns null at runtime and
        /// WorldRig.Build throws on <c>new Material(null)</c>, hanging Singleplayer at loading).
        /// </summary>
        public static void EnsureShadersIncluded()
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
            if (assets == null || assets.Length == 0)
            {
                Debug.LogWarning("GraphicsSettings.asset not found; cannot register always-included shaders.");
                return;
            }

            var so = new SerializedObject(assets[0]);
            var list = so.FindProperty("m_AlwaysIncludedShaders");

            foreach (var name in RuntimeShaders)
            {
                var shader = Shader.Find(name);
                if (shader == null)
                {
                    Debug.LogWarning($"Shader '{name}' not found in the editor; skipping always-include.");
                    continue;
                }

                bool present = false;
                for (int i = 0; i < list.arraySize; i++)
                {
                    if (list.GetArrayElementAtIndex(i).objectReferenceValue == shader)
                    {
                        present = true;
                        break;
                    }
                }

                if (present)
                {
                    continue;
                }

                list.InsertArrayElementAtIndex(list.arraySize);
                list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = shader;
                Debug.Log($"Always-included shader added: {name}");
            }

            so.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
        }

        private const string RendererPath = "Assets/Settings/BlocksBeyondTheStarsURP_Renderer.asset";
        private const string FogMatPath = "Assets/Settings/VolumetricFog.mat";

        /// <summary>Temporary isolation switch: false leaves the volumetric-fog full-screen feature on the renderer
        /// but DISABLED, to confirm whether it (interacting with the visor compositor) caused the full-frame blur.
        /// Flip back to true once the interaction is resolved.</summary>
        private const bool EnableVolumetricFog = false;

        /// <summary>
        /// Auto-wires the screen-space look features onto the URP renderer at build time, so no manual Inspector
        /// step is needed (and no fragile hand-edited YAML). Currently: a built-in
        /// <see cref="FullScreenPassRendererFeature"/> running the <c>BlocksBeyondTheStars/VolumetricFog</c>
        /// material (depth-driven fog + sun in-scatter, gated by the <c>_VolFog</c>/<c>_VolFogDensity</c> globals).
        /// Idempotent — skips features already present by name. The renderer-feature map (local-id list) is rebuilt
        /// to match, mirroring URP's own ScriptableRendererData.UpdateMap.
        /// </summary>
        public static void EnsureRendererFeatures()
        {
            var rd = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (rd == null)
            {
                Debug.LogWarning($"URP renderer data not found at {RendererPath}; skipping renderer-feature wiring.");
                return;
            }

            EnsureFullScreenFeature(rd, "VolumetricFog", "BlocksBeyondTheStars/VolumetricFog", FogMatPath,
                FullScreenPassRendererFeature.InjectionPoint.BeforeRenderingPostProcessing,
                ScriptableRenderPassInput.Depth, mat =>
                {
                    // Tuning (re-applied every build so edits here take effect). Density is driven per-world by the
                    // _VolFogDensity global (Sky.cs); these shape the look.
                    mat.SetFloat("_SunScatter", 0.7f);
                    mat.SetFloat("_MaxFog", 0.5f); // lower cap so dusk/night haze tints the distance without darkening it
                    mat.SetColor("_FogTint", Color.white);
                    mat.SetFloat("_FogHeightFalloff", 0f);
                });
        }

        private static void EnsureFullScreenFeature(UniversalRendererData rd, string featureName, string shaderName,
            string matPath, FullScreenPassRendererFeature.InjectionPoint injection, ScriptableRenderPassInput inputs,
            System.Action<Material> tune)
        {
            foreach (var existing in rd.rendererFeatures)
            {
                if (existing != null && existing.name == featureName)
                {
                    // Already wired — refresh the material tuning and the active state (isolation switch).
                    existing.SetActive(EnableVolumetricFog);
                    EditorUtility.SetDirty(rd);
                    var m = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                    if (m != null) { tune(m); EditorUtility.SetDirty(m); }
                    AssetDatabase.SaveAssets();
                    return;
                }
            }

            var shader = Shader.Find(shaderName);
            if (shader == null)
            {
                Debug.LogWarning($"Shader '{shaderName}' not found; skipping renderer feature '{featureName}'.");
                return;
            }

            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                mat = new Material(shader) { name = featureName };
                AssetDatabase.CreateAsset(mat, matPath);
            }

            tune(mat);
            EditorUtility.SetDirty(mat);

            var feature = ScriptableObject.CreateInstance<FullScreenPassRendererFeature>();
            feature.name = featureName;
            feature.passMaterial = mat;
            feature.injectionPoint = injection;
            feature.fetchColorBuffer = true; // the material samples the screen (_BlitTexture)
            feature.requirements = inputs;   // Depth → URP generates + binds the depth texture for the pass
            feature.SetActive(EnableVolumetricFog);
            feature.hideFlags = HideFlags.HideInHierarchy;

            AssetDatabase.AddObjectToAsset(feature, rd);
            rd.rendererFeatures.Add(feature);
            EditorUtility.SetDirty(rd);
            AssetDatabase.SaveAssets(); // persist so the sub-asset gets a stable local file id

            RebuildFeatureMap(rd);
            AssetDatabase.SaveAssets();
            Debug.Log($"Renderer feature added: {featureName}");
        }

        /// <summary>Rebuilds m_RendererFeatureMap to the features' local file ids (mirrors URP's private UpdateMap).</summary>
        private static void RebuildFeatureMap(UniversalRendererData rd)
        {
            var so = new SerializedObject(rd);
            var feats = so.FindProperty("m_RendererFeatures");
            var map = so.FindProperty("m_RendererFeatureMap");
            map.arraySize = feats.arraySize;
            for (int i = 0; i < feats.arraySize; i++)
            {
                var f = feats.GetArrayElementAtIndex(i).objectReferenceValue;
                if (f == null) continue;
                if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(f, out _, out long localId))
                {
                    map.GetArrayElementAtIndex(i).longValue = localId;
                }
            }

            so.ApplyModifiedProperties();
        }

        private const string IconAssetPath = "Assets/BlocksBeyondTheStars/Icon/app_icon.png";

        /// <summary>
        /// Embeds the application icon into BlocksBeyondTheStars.exe and sets the product/company name. The source
        /// PNG (<see cref="IconAssetPath"/>, regenerate via tools/ai-assets/gen_app_icon.py) is loaded as
        /// an imported asset (NOT an in-memory texture — PlayerSettings stores an asset reference, which a
        /// transient Texture2D can't satisfy, and the asset must live outside any Editor folder or the
        /// player build strips it). It's assigned to every standalone icon size so Windows shows it in
        /// Explorer and the taskbar.
        /// </summary>
        public static void EnsureAppIcon()
        {
            PlayerSettings.companyName = "JuMaVe Games";
            // Display title of the game; "BlocksBeyondTheStars" stays the technical codename (exe, namespaces, paths).
            PlayerSettings.productName = "Blocks Beyond the Stars";

            string absPath = Path.Combine(Application.dataPath, "BlocksBeyondTheStars", "Icon", "app_icon.png");
            if (!File.Exists(absPath))
            {
                Debug.LogWarning($"App icon not found at {absPath}; building without a custom .exe icon.");
                return;
            }

            AssetDatabase.ImportAsset(IconAssetPath, ImportAssetOptions.ForceUpdate);
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(IconAssetPath);
            if (tex == null)
            {
                Debug.LogWarning($"App icon asset could not be loaded from {IconAssetPath}; building without a custom .exe icon.");
                return;
            }

            var sizes = PlayerSettings.GetIconSizes(NamedBuildTarget.Standalone, IconKind.Application);
            var icons = new Texture2D[sizes.Length == 0 ? 1 : sizes.Length];
            for (int i = 0; i < icons.Length; i++)
            {
                icons[i] = tex; // Unity rescales the single source to each required size.
            }

            PlayerSettings.SetIcons(NamedBuildTarget.Standalone, icons, IconKind.Application);
            AssetDatabase.SaveAssets();
            Debug.Log($"App icon embedded ({icons.Length} sizes) from {IconAssetPath}.");
        }

        /// <summary>Creates and saves the launcher scene (one AppShell object) if it doesn't exist.</summary>
        public static void EnsureLauncherScene()
        {
            if (File.Exists(ScenePath))
            {
                return;
            }

            if (!Directory.Exists(SceneDir))
            {
                Directory.CreateDirectory(SceneDir);
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var go = new GameObject("AppShell");
            go.AddComponent<AppShell>();
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
        }

        private static string GetArg(string name)
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == name)
                {
                    return args[i + 1];
                }
            }

            return null;
        }
    }
}
#endif
