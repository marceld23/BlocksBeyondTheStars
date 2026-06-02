#if UNITY_EDITOR
using System.IO;
using Spacecraft.Client;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Spacecraft.Client.EditorTools
{
    /// <summary>
    /// Builds the Windows player (M28). The launcher scene is normally created at runtime, so
    /// this first generates and saves a minimal scene containing a single <see cref="AppShell"/>
    /// GameObject, registers it in the build settings, then builds. Run from the editor menu
    /// (Spacecraft → Build Windows Player) or headless via <c>scripts/build-client.ps1</c>.
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
            "Spacecraft/VertexColorOpaque",
            "Spacecraft/BlockAtlas",
            "Spacecraft/LitColor",
            "Spacecraft/SunGlow",
            "Spacecraft/Cloud",
            "Spacecraft/PostBloom",
            "Spacecraft/PostComposite",
            "Spacecraft/PostAO",
            "Unlit/Color",
        };

        [MenuItem("Spacecraft/Build Windows Player")]
        public static void BuildWindows()
        {
            EnsureLauncherScene();
            EnsureShadersIncluded();
            EnsureAppIcon();

            string outDir = GetArg("-spacecraftOut") ?? "Build/Windows";
            Directory.CreateDirectory(outDir);

            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = Path.Combine(outDir, "Spacecraft.exe"),
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };

            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;
            Debug.Log($"Spacecraft build: {summary.result} → {summary.outputPath} ({summary.totalSize} bytes)");

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

        private const string IconAssetPath = "Assets/Spacecraft/Icon/spacecraft_icon.png";

        /// <summary>
        /// Embeds the application icon into Spacecraft.exe and sets the product/company name. The source
        /// PNG (<see cref="IconAssetPath"/>, regenerate via tools/ai-assets/gen_app_icon.py) is loaded as
        /// an imported asset (NOT an in-memory texture — PlayerSettings stores an asset reference, which a
        /// transient Texture2D can't satisfy, and the asset must live outside any Editor folder or the
        /// player build strips it). It's assigned to every standalone icon size so Windows shows it in
        /// Explorer and the taskbar.
        /// </summary>
        public static void EnsureAppIcon()
        {
            PlayerSettings.companyName = "Spacecraft";
            PlayerSettings.productName = "Spacecraft";

            string absPath = Path.Combine(Application.dataPath, "Spacecraft", "Icon", "spacecraft_icon.png");
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
