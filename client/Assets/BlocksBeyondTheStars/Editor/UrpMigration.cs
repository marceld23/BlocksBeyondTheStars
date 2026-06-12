using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace BlocksBeyondTheStars.Client.EditorTools
{
    /// <summary>
    /// URP migration helpers (menu: <b>BlocksBeyondTheStars → URP</b>). Enables/disables URP unambiguously by assigning the
    /// project's URP <see cref="RenderPipelineAsset"/> to the Graphics default AND every Quality level (so it
    /// applies whatever preset the game picks at runtime), and logs the *effective* pipeline so there's no doubt
    /// whether the flip took. Uses only the base <c>RenderPipelineAsset</c> type, so this script needs no URP
    /// package asmdef reference. Reversible: "Disable" sets everything back to Built-in RP.
    /// </summary>
    public static class UrpMigration
    {
        [MenuItem("BlocksBeyondTheStars/URP/Enable URP")]
        public static void EnableUrp()
        {
            var asset = FindUrpAsset();
            if (asset == null)
            {
                Debug.LogError("[URP] No RenderPipelineAsset found in the project. Create one first: " +
                               "Project window → right-click → Create → Rendering → URP Asset (with Universal Renderer).");
                return;
            }

            SetPipeline(asset);
            Debug.Log($"[URP] ENABLED. Effective pipeline = {PipelineName()} (asset: {AssetDatabase.GetAssetPath(asset)}). " +
                      "Enter Play mode to view. Run 'BlocksBeyondTheStars → URP → Disable URP' to revert to Built-in.");
        }

        [MenuItem("BlocksBeyondTheStars/URP/Disable URP (Built-in)")]
        public static void DisableUrp()
        {
            SetPipeline(null);
            Debug.Log($"[URP] DISABLED. Effective pipeline = {PipelineName()} (Built-in RP).");
        }

        [MenuItem("BlocksBeyondTheStars/URP/Log Active Pipeline")]
        public static void LogActive()
        {
            var def = GraphicsSettings.defaultRenderPipeline;
            Debug.Log($"[URP] Effective pipeline = {PipelineName()}; Graphics default asset = " +
                      $"{(def != null ? def.name : "none (Built-in)")}.");
        }

        private static RenderPipelineAsset FindUrpAsset()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:RenderPipelineAsset"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var a = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(path);
                if (a != null)
                {
                    return a;
                }
            }

            return null;
        }

        /// <summary>Assigns (or clears) the pipeline on the Graphics default + every Quality level, so the flip
        /// holds regardless of which quality preset the game selects at runtime.</summary>
        private static void SetPipeline(RenderPipelineAsset asset)
        {
            GraphicsSettings.defaultRenderPipeline = asset;

            int current = QualitySettings.GetQualityLevel();
            string[] names = QualitySettings.names;
            for (int i = 0; i < names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, applyExpensiveChanges: false);
                QualitySettings.renderPipeline = asset; // per-level override (null → use Graphics default / Built-in)
            }

            QualitySettings.SetQualityLevel(current, applyExpensiveChanges: false);
            AssetDatabase.SaveAssets();
        }

        private static string PipelineName()
        {
            var rp = GraphicsSettings.currentRenderPipeline;
            return rp != null ? rp.GetType().Name : "Built-in RP";
        }
    }
}
