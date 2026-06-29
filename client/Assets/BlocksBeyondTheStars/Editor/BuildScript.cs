// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using BlocksBeyondTheStars.Client;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace BlocksBeyondTheStars.Client.EditorTools
{
    /// <summary>
    /// Builds the standalone player (Windows or Linux). The launcher scene is normally created at runtime, so
    /// this first generates and saves a minimal scene containing a single <see cref="AppShell"/>
    /// GameObject, registers it in the build settings, then builds. Run from the editor menu
    /// (BlocksBeyondTheStars → Build Windows/Linux Player) or headless via <c>scripts/build-client.ps1</c> /
    /// <c>scripts/build-client.sh</c>.
    ///
    /// The build automatically includes everything under <c>StreamingAssets/</c> (the synced
    /// data and, if published, the bundled local server), so run sync-client-libs and
    /// publish-local-server first for a self-contained singleplayer build.
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
            "BlocksBeyondTheStars/SunRays",
            "BlocksBeyondTheStars/Nebula",
            "BlocksBeyondTheStars/Atmosphere",
            "BlocksBeyondTheStars/Particle",
            "BlocksBeyondTheStars/ParticleAlpha",
            "BlocksBeyondTheStars/Aurora",
            "BlocksBeyondTheStars/Cloud",
            "BlocksBeyondTheStars/PostBloom",
            "BlocksBeyondTheStars/PostComposite",
            "BlocksBeyondTheStars/PostAO",
            "Unlit/Color",
        };

        [MenuItem("BlocksBeyondTheStars/Build Windows Player")]
        public static void BuildWindows()
            => BuildPlayer(BuildTarget.StandaloneWindows64, "BlocksBeyondTheStars.exe", "Build/Windows");

        /// <summary>Builds the Linux player (StandaloneLinux64). Now that UWB/CEF (Windows-only) is gone the client
        /// builds on Linux too; the remaining piece is the CI/packaging side (a linux-x64 bundled server, no
        /// WinForms launcher, tar/AppImage packaging). Headless: same path with
        /// <c>-buildMethod BlocksBeyondTheStars.Client.EditorTools.BuildScript.BuildLinux -buildOut &lt;dir&gt;</c>.</summary>
        [MenuItem("BlocksBeyondTheStars/Build Linux Player")]
        public static void BuildLinux()
            => BuildPlayer(BuildTarget.StandaloneLinux64, "BlocksBeyondTheStars.x86_64", "Build/Linux");

        /// <summary>Builds the macOS player (StandaloneOSX) as a <c>.app</c> bundle. Because the project uses the
        /// Mono scripting backend (not IL2CPP), GameCI cross-builds this on a Linux runner — no Mac hardware needed.
        /// Experimental: the resulting bundle is unsigned/un-notarized, so macOS Gatekeeper quarantines it (users
        /// run <c>xattr -dr com.apple.quarantine</c> once). Headless: same path with
        /// <c>-buildMethod BlocksBeyondTheStars.Client.EditorTools.BuildScript.BuildMacOS -buildOut &lt;dir&gt;</c>.</summary>
        [MenuItem("BlocksBeyondTheStars/Build macOS Player")]
        public static void BuildMacOS()
            => BuildPlayer(BuildTarget.StandaloneOSX, "BlocksBeyondTheStars.app", "Build/macOS");

        /// <summary>Builds the WebGL player folder for browser deployment. The browser path uses WebSockets
        /// against a hosted authoritative server, so this target keeps browser-client packaging repeatable.</summary>
        [MenuItem("BlocksBeyondTheStars/Build WebGL Player")]
        public static void BuildWebGL()
            => BuildPlayer(BuildTarget.WebGL, string.Empty, "Build/WebGL");

        private static void BuildPlayer(BuildTarget target, string exeName, string defaultOutDir)
        {
            EnsureLauncherScene();
            EnsureShadersIncluded();
            EnsureRendererFeatures();
            EnsureAppIcon();
            if (target == BuildTarget.WebGL)
            {
                ConfigureWebGLPlayer();
                EnsureStreamingAssetsManifest();
            }

            string version = GetArg("-buildVersion");
            if (!string.IsNullOrEmpty(version))
            {
                PlayerSettings.bundleVersion = version;
                Debug.Log($"BlocksBeyondTheStars version set from -buildVersion: {version}");
            }

            // macOS (and iOS) require a non-empty usage description for any Apple "Usage Description" API the
            // player touches, or the StandaloneOSX post-processor fails the build with
            // "Microphone class is used but Microphone Usage Description is empty in Player Settings."
            // Voice chat (BBS_VOICE) uses the Microphone API, so set the Info.plist string here. No-op on
            // Windows/Linux, which don't gate on it. Idempotent; safe to set on every build.
            if (target == BuildTarget.StandaloneOSX)
            {
                PlayerSettings.macOS.microphoneUsageDescription =
                    "Used for in-game voice chat with other players.";
            }

            string outDir = GetArg("-buildOut") ?? defaultOutDir;
            Directory.CreateDirectory(outDir);
            string locationPathName = target == BuildTarget.WebGL ? outDir : Path.Combine(outDir, exeName);

            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = locationPathName,
                target = target,
                options = BuildOptions.None,
            };

            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;
            // Keep the exact "BlocksBeyondTheStars build: <result>" prefix — scripts/build-client.ps1 greps for
            // "build: Succeeded" as the authoritative success marker; the target goes in parens after it.
            Debug.Log($"BlocksBeyondTheStars build: {summary.result} ({target}) → {summary.outputPath} ({summary.totalSize} bytes)");

            if (summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            {
                EditorApplication.Exit(1);
            }

            if (target == BuildTarget.WebGL)
            {
                RemoveAutoFullscreen(outDir);
                StripBundledServerFromWebGLOutput(outDir);
            }

            File.WriteAllText(Path.Combine(outDir, "version.txt"), PlayerSettings.bundleVersion);
        }

        /// <summary>Strips the bundled native game server from a WebGL build's output StreamingAssets. On desktop
        /// the client launches this server for Singleplayer/Host, so a prior desktop build leaves
        /// <c>StreamingAssets/server/</c> in the project — and Unity copies the whole StreamingAssets folder into
        /// every build, WebGL included. A browser can never run a native server, so it is ~70 MB of dead weight
        /// (and a stray platform binary) that must not ride along in the web bundle. Only the build OUTPUT is
        /// touched; the source project is left intact so a later desktop Singleplayer build/run still finds it.</summary>
        private static void StripBundledServerFromWebGLOutput(string outDir)
        {
            string serverDir = Path.Combine(outDir, "StreamingAssets", "server");
            if (!Directory.Exists(serverDir))
            {
                return;
            }

            try
            {
                Directory.Delete(serverDir, recursive: true);
                Debug.Log($"WebGL: stripped the bundled native server from the build output ({serverDir}).");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"WebGL: could not strip the bundled native server at {serverDir}: {ex.Message}");
            }
        }

        /// <summary>Best-effort WebGL production defaults. Reflection keeps this resilient across Unity 6 patch
        /// API shuffles while still applying Brotli + 512 MB heap when those properties are available.</summary>
        public static void ConfigureWebGLPlayer()
        {
            bool fastLocal = string.Equals(Environment.GetEnvironmentVariable("BBS_WEBGL_FAST_LOCAL"), "1", StringComparison.OrdinalIgnoreCase)
                || string.Equals(Environment.GetEnvironmentVariable("BBS_WEBGL_FAST_LOCAL"), "true", StringComparison.OrdinalIgnoreCase);

            EditorUserBuildSettings.development = false;
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.WebGL, ManagedStrippingLevel.Low);
            SetWebGLProperty("memorySize", 512);
            SetWebGLProperty("compressionFormat", fastLocal ? "Disabled" : "Brotli");
            SetWebGLProperty("decompressionFallback", !fastLocal);
            SetWebGLProperty("dataCaching", true);
            if (fastLocal)
            {
                Debug.Log("BBS_WEBGL_FAST_LOCAL enabled: WebGL build compression disabled for local browser verification.");
            }
        }

        private static void EnsureStreamingAssetsManifest()
        {
            string dataRoot = Path.Combine(Application.dataPath, "StreamingAssets", "data");
            if (!Directory.Exists(dataRoot))
            {
                Debug.LogWarning($"StreamingAssets data folder not found at {dataRoot}; WebGL content manifest skipped.");
                return;
            }

            var files = new List<string>();
            foreach (string file in Directory.GetFiles(dataRoot, "*", SearchOption.AllDirectories))
            {
                if (file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(Path.GetFileName(file), "manifest.json", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string relative = file.Substring(dataRoot.Length + 1)
                    .Replace(Path.DirectorySeparatorChar, '/')
                    .Replace(Path.AltDirectorySeparatorChar, '/');
                files.Add(relative);
            }

            files.Sort(StringComparer.Ordinal);

            var json = new StringBuilder();
            json.AppendLine("{");
            json.AppendLine("  \"files\": [");
            for (int i = 0; i < files.Count; i++)
            {
                json.Append("    \"").Append(EscapeJson(files[i])).Append('"');
                if (i < files.Count - 1)
                {
                    json.Append(',');
                }

                json.AppendLine();
            }

            json.AppendLine("  ]");
            json.AppendLine("}");

            string manifestPath = Path.Combine(dataRoot, "manifest.json");
            File.WriteAllText(manifestPath, json.ToString());
            AssetDatabase.ImportAsset("Assets/StreamingAssets/data/manifest.json", ImportAssetOptions.ForceUpdate);
            Debug.Log($"WebGL StreamingAssets manifest written with {files.Count} files.");
        }

        private static void RemoveAutoFullscreen(string outDir)
        {
            string indexPath = Path.Combine(outDir, "index.html");
            if (!File.Exists(indexPath))
            {
                Debug.LogWarning($"WebGL index.html not found at {indexPath}; fullscreen patch skipped.");
                return;
            }

            string[] lines = File.ReadAllLines(indexPath);
            var kept = new List<string>(lines.Length);
            bool changed = false;
            foreach (string line in lines)
            {
                if (line.Trim() == "unityInstance.SetFullscreen(1);")
                {
                    changed = true;
                    continue;
                }

                kept.Add(line);
            }

            if (changed)
            {
                File.WriteAllLines(indexPath, kept);
                Debug.Log("Removed generated WebGL auto-fullscreen call; fullscreen remains available from the button.");
            }
        }

        private static string EscapeJson(string value)
            => value.Replace("\\", "\\\\").Replace("\"", "\\\"");

        private static void SetWebGLProperty(string propertyName, object value)
        {
            Type webGlType = typeof(PlayerSettings).GetNestedType("WebGL", BindingFlags.Public | BindingFlags.NonPublic);
            var property = webGlType?.GetProperty(propertyName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (property == null || !property.CanWrite)
            {
                Debug.Log($"WebGL PlayerSettings.{propertyName} is not available in this Unity version; leaving default.");
                return;
            }

            try
            {
                object converted = value;
                if (property.PropertyType.IsEnum)
                {
                    converted = Enum.Parse(property.PropertyType, value.ToString());
                }
                else if (property.PropertyType != value.GetType())
                {
                    converted = Convert.ChangeType(value, property.PropertyType);
                }

                property.SetValue(null, converted, null);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Could not set WebGL PlayerSettings.{propertyName}: {ex.Message}");
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

        /// <summary>
        /// Cleanup pass: strips the retired <c>VolumetricFog</c> full-screen renderer feature from the URP renderer
        /// (and any null/missing feature entries), keeping SSAO. The volumetric full-screen pass darkened the whole
        /// frame (its colour copy), so its fog role was replaced by the in-shader distance haze in BlockAtlas and
        /// its light-shafts by the additive SunRays billboard. Idempotent (no-op once gone); rebuilds the
        /// renderer-feature map to match the trimmed list. Runs at build so the asset cleans itself.
        /// </summary>
        public static void EnsureRendererFeatures()
        {
            var rd = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (rd == null)
            {
                return;
            }

            bool changed = false;
            for (int i = rd.rendererFeatures.Count - 1; i >= 0; i--)
            {
                var f = rd.rendererFeatures[i];
                if (f == null || f.name == "VolumetricFog")
                {
                    rd.rendererFeatures.RemoveAt(i);
                    if (f != null)
                    {
                        AssetDatabase.RemoveObjectFromAsset(f);
                        UnityEngine.Object.DestroyImmediate(f, true);
                    }

                    changed = true;
                }
            }

            if (!changed)
            {
                return;
            }

            EditorUtility.SetDirty(rd);
            var so = new SerializedObject(rd);
            var feats = so.FindProperty("m_RendererFeatures");
            var map = so.FindProperty("m_RendererFeatureMap");
            map.arraySize = feats.arraySize;
            for (int i = 0; i < feats.arraySize; i++)
            {
                var f = feats.GetArrayElementAtIndex(i).objectReferenceValue;
                if (f != null && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(f, out _, out long localId))
                {
                    map.GetArrayElementAtIndex(i).longValue = localId;
                }
            }

            so.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
            Debug.Log("Removed retired VolumetricFog renderer feature from the URP renderer.");
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
