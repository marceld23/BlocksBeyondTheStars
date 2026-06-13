using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The client-side catalogue of bundled arcade minigames, loaded from
    /// <c>StreamingAssets/minigames/catalog.json</c>. It is the single source of truth for which games exist,
    /// their bilingual titles/descriptions, and — crucially — how a data cube's opaque <see cref="long"/> seed
    /// maps to a concrete game (<see cref="GameForSeed"/>). The server never reads this; it only places cubes
    /// with seeds, so the same build resolves every cube to the same game on every client.
    /// </summary>
    public sealed class MinigameCatalog
    {
        [Serializable] public sealed class Loc { public string en = ""; public string de = ""; }

        [Serializable]
        public sealed class Entry
        {
            public string key = "";
            public string entry = "";   // path under minigames/, e.g. "snake/index.html"
            public string icon = "";
            public Loc title = new Loc();
            public Loc desc = new Loc();

            public string Title(bool german) => Pick(title, german);
            public string Desc(bool german) => Pick(desc, german);
            private static string Pick(Loc l, bool german)
            {
                if (l == null) return "";
                return german ? (string.IsNullOrEmpty(l.de) ? l.en : l.de) : (string.IsNullOrEmpty(l.en) ? l.de : l.en);
            }
        }

        [Serializable] private sealed class File_ { public Entry[] games; }

        public List<Entry> Games { get; } = new List<Entry>();

        public static MinigameCatalog Instance { get; private set; }

        /// <summary>Loads (once) the catalogue from StreamingAssets. Returns the cached instance on repeat calls.</summary>
        public static MinigameCatalog Load()
        {
            if (Instance != null)
            {
                return Instance;
            }

            var cat = new MinigameCatalog();
            try
            {
                string path = Path.Combine(Application.streamingAssetsPath, "minigames", "catalog.json");
                if (File.Exists(path))
                {
                    var parsed = JsonUtility.FromJson<File_>(File.ReadAllText(path));
                    if (parsed?.games != null)
                    {
                        foreach (var g in parsed.games)
                        {
                            if (g != null && !string.IsNullOrEmpty(g.key)) cat.Games.Add(g);
                        }
                    }
                }
                else
                {
                    Debug.LogWarning($"[MinigameCatalog] catalog.json not found at {path}");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[MinigameCatalog] failed to load: {e.Message}");
            }

            Instance = cat;
            return cat;
        }

        /// <summary>Which game a data cube with this seed holds, or null if the catalogue is empty. Deterministic
        /// across clients of the same build (same catalogue order → same mapping).</summary>
        public Entry GameForSeed(long seed)
        {
            if (Games.Count == 0) return null;
            int idx = (int)(((seed % Games.Count) + Games.Count) % Games.Count);
            return Games[idx];
        }

        public Entry Find(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            for (int i = 0; i < Games.Count; i++)
            {
                if (Games[i].key == key) return Games[i];
            }

            return null;
        }
    }
}
