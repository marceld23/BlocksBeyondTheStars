using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Renders + collides the world's doors (<see cref="DoorList"/>). A doorway opening stays air in the
    /// voxel world; this fills it with a dynamic door whose <see cref="BoxCollider"/> blocks the player while
    /// closed and lifts while open. Two kinds: sci-fi <b>slide</b> doors (two panels swoosh apart) and
    /// <b>hinge</b> doors (a leaf swings ~90°). The server owns open/closed state — slide doors auto-open near
    /// players, hinge doors toggle on E (see <see cref="NearestHinge"/>). Mirrors <see cref="NpcView"/>.
    /// </summary>
    public sealed class DoorView : MonoBehaviour
    {
        public GameBootstrap Game;

        /// <summary>Set so the player controller can find the hinge door it should toggle on E.</summary>
        public static DoorView Instance { get; private set; }

        private const float Height = 2.8f;      // covers the 3-tall doorway, standing on the floor
        private const float Thickness = 0.18f;
        private const float AnimSpeed = 6f;     // how fast a door visually slides/swings toward its state

        private sealed class Door
        {
            public GameObject Go;
            public Transform Pivot;        // rotates the whole door so the wall axis lines up
            public Transform PanelA, PanelB; // slide: the two panels; hinge: PanelA is the leaf
            public BoxCollider Collider;
            public string Kind;
            public Vector3 World;          // canonical world pos (gap centre, floor)
            public float Width;
            public bool Open;
            public float Anim;             // 0 closed → 1 open, eased toward Open
            public Transform Field;        // energy door: the translucent blue field shown in the open doorway
            public Material FieldMat;      // its material (alpha fades in with Anim) — item 35
        }

        private readonly Dictionary<int, Door> _doors = new Dictionary<int, Door>();
        private bool _subscribed;

        private void Awake() => Instance = this;

        private void Update()
        {
            if (!_subscribed && Game?.Network != null)
            {
                Game.Network.DoorsReceived += OnDoors;
                _subscribed = true;
            }

            foreach (var d in _doors.Values)
            {
                d.Go.transform.position = Game != null ? Game.ScenePos(d.World.x, d.World.y, d.World.z) : d.World;

                float target = d.Open ? 1f : 0f;
                d.Anim = Mathf.MoveTowards(d.Anim, target, Time.deltaTime * AnimSpeed);
                Animate(d);

                // The collider blocks passage until the door is mostly open (so you can't slip through a crack).
                if (d.Collider != null)
                {
                    d.Collider.enabled = d.Anim < 0.5f;
                }
            }
        }

        private void Animate(Door d)
        {
            float w = d.Width;
            if (d.Kind == "hinge")
            {
                // Swing the leaf around its jamb edge by up to ~96°.
                d.PanelA.localRotation = Quaternion.Euler(0f, -d.Anim * 96f, 0f);
            }
            else
            {
                // Retract the two panels sideways into the jambs.
                float slide = (w * 0.5f) * d.Anim * 0.92f;
                d.PanelA.localPosition = new Vector3(-w * 0.25f - slide, Height * 0.5f, 0f);
                d.PanelB.localPosition = new Vector3(w * 0.25f + slide, Height * 0.5f, 0f);
            }

            // Energy field: fade it in as the door opens (invisible when closed) with a faint shimmer, so the
            // open doorway shows a passable blue membrane (item 35).
            if (d.FieldMat != null)
            {
                float shimmer = 0.85f + 0.15f * Mathf.Sin(Time.time * 6f);
                float alpha = 0.42f * d.Anim * shimmer;
                d.FieldMat.SetColor(FieldColorId, new Color(0.35f, 0.80f, 1f, alpha));
            }
        }

        private void OnDoors(DoorList m)
        {
            var seen = new HashSet<int>();
            foreach (var nd in m.Doors)
            {
                seen.Add(nd.Id);
                if (!_doors.TryGetValue(nd.Id, out var d))
                {
                    d = Build(nd);
                    _doors[nd.Id] = d;
                }

                if (d.Open != nd.Open)
                {
                    d.Open = nd.Open;
                    PlayDoorSfx(d);
                }
            }

            if (_doors.Count > seen.Count)
            {
                var stale = new List<int>();
                foreach (var id in _doors.Keys)
                {
                    if (!seen.Contains(id))
                    {
                        stale.Add(id);
                    }
                }

                foreach (var id in stale)
                {
                    Destroy(_doors[id].Go);
                    _doors.Remove(id);
                }
            }
        }

        private Door Build(NetDoor nd)
        {
            var go = new GameObject($"Door {nd.Kind} {nd.Id}");
            go.transform.SetParent(transform, true);

            // Build everything in an X-aligned frame (wall runs along local X); rotate 90° for a Z wall.
            var pivot = new GameObject("Pivot").transform;
            pivot.SetParent(go.transform, false);
            pivot.localRotation = nd.AxisX ? Quaternion.identity : Quaternion.Euler(0f, 90f, 0f);

            float w = Mathf.Max(1f, nd.Width);
            bool hinge = nd.Kind == "hinge";
            Color panelCol = hinge ? new Color(0.45f, 0.30f, 0.16f) : new Color(0.62f, 0.69f, 0.78f);
            Color trimCol = hinge ? new Color(0.30f, 0.19f, 0.10f) : new Color(0.30f, 0.85f, 0.95f);

            Transform a, b = null;
            if (hinge)
            {
                // One leaf, pivoting on the left jamb. The pivot sits at the jamb; the leaf extends +X from it.
                a = new GameObject("Leaf").transform;
                a.SetParent(pivot, false);
                a.localPosition = new Vector3(-w * 0.5f, 0f, 0f);
                var leaf = Panel(panelCol, trimCol);
                leaf.transform.SetParent(a, false);
                leaf.transform.localPosition = new Vector3(w * 0.5f, Height * 0.5f, 0f);
                leaf.transform.localScale = new Vector3(w * 0.96f, Height, Thickness);
            }
            else
            {
                a = MakePanel(pivot, panelCol, trimCol, w * 0.5f);
                b = MakePanel(pivot, panelCol, trimCol, w * 0.5f);
            }

            // Frame trim: two jamb posts (sci-fi doors glow) so the opening reads as a real doorway.
            Post(pivot, trimCol, -w * 0.5f);
            Post(pivot, trimCol, w * 0.5f);

            // A solid collider that blocks the player while closed (the player uses a CharacterController).
            var col = go.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, Height * 0.5f, 0f);
            col.size = nd.AxisX ? new Vector3(w, Height, Thickness * 2f) : new Vector3(Thickness * 2f, Height, w);

            // Energy door (item 35): a translucent blue energy field filling the opening, shown only while open
            // (the panels still slide apart). The field is purely visual + passable — no collider — so you walk
            // through it; the door's own collider above handles blocking while closed.
            Transform field = null;
            Material fieldMat = null;
            if (nd.Kind == "energy")
            {
                var fieldGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
                StripCollider(fieldGo);
                fieldGo.transform.SetParent(pivot, false);
                fieldGo.transform.localPosition = new Vector3(0f, Height * 0.5f, 0f);
                fieldGo.transform.localScale = new Vector3(w * 0.98f, Height, 0.05f);
                fieldMat = EnergyFieldMaterial();
                fieldGo.GetComponent<Renderer>().sharedMaterial = fieldMat;
                field = fieldGo.transform;
            }

            return new Door
            {
                Go = go,
                Pivot = pivot,
                PanelA = a,
                PanelB = b,
                Collider = col,
                Kind = nd.Kind,
                World = new Vector3(nd.X, nd.Y, nd.Z),
                Width = w,
                Open = nd.Open,
                Anim = nd.Open ? 1f : 0f,
                Field = field,
                FieldMat = fieldMat,
            };
        }

        private static readonly int FieldColorId = Shader.PropertyToID("_Color");
        private static Shader _fieldShader;

        /// <summary>A translucent, glowing blue energy-field material (item 35). Reuses the always-included
        /// <c>BlocksBeyondTheStars/Cloud</c> alpha-blend shader (no texture → a solid tinted quad), so it can't get
        /// stripped from the build into pink. Alpha is driven per-frame in <see cref="Animate"/>.</summary>
        private static Material EnergyFieldMaterial()
        {
            if (_fieldShader == null)
            {
                _fieldShader = Shader.Find("BlocksBeyondTheStars/Cloud") ?? Shader.Find("Unlit/Transparent");
            }

            var mat = new Material(_fieldShader);
            mat.SetColor(FieldColorId, new Color(0.35f, 0.80f, 1f, 0f)); // alpha set each frame from the open amount
            mat.renderQueue = 3000; // transparent
            return mat;
        }

        private Transform MakePanel(Transform parent, Color body, Color trim, float panelWidth)
        {
            var holder = new GameObject("Panel").transform;
            holder.SetParent(parent, false);
            var panel = Panel(body, trim);
            panel.transform.SetParent(holder, false);
            panel.transform.localScale = new Vector3(panelWidth * 0.98f, Height, Thickness);
            return holder;
        }

        /// <summary>A single panel cube with a thin emissive trim strip (the sci-fi glow / wood edge).</summary>
        private GameObject Panel(Color body, Color trim)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            StripCollider(go);
            Paint(go, body);

            var strip = GameObject.CreatePrimitive(PrimitiveType.Cube);
            StripCollider(strip);
            Paint(strip, trim);
            strip.transform.SetParent(go.transform, false);
            strip.transform.localScale = new Vector3(0.12f, 0.9f, 1.05f); // a vertical light seam down the middle
            return go;
        }

        private void Post(Transform parent, Color trim, float x)
        {
            var post = GameObject.CreatePrimitive(PrimitiveType.Cube);
            StripCollider(post);
            Paint(post, trim);
            post.transform.SetParent(parent, false);
            post.transform.localPosition = new Vector3(x, Height * 0.5f, 0f);
            post.transform.localScale = new Vector3(0.14f, Height + 0.1f, Thickness * 2.2f);
        }

        private static Shader _doorShader;

        private static void Paint(GameObject go, Color c)
        {
            var r = go.GetComponent<Renderer>();
            if (r == null)
            {
                return;
            }

            // Use a project shader (always in the build); the primitives' default Standard material gets
            // stripped from player builds and renders bright pink/magenta.
            if (_doorShader == null)
            {
                _doorShader = Shader.Find("BlocksBeyondTheStars/LitColor") ?? Shader.Find("Unlit/Color");
            }

            r.sharedMaterial = new Material(_doorShader) { color = c };
        }

        private static void StripCollider(GameObject go)
        {
            var c = go.GetComponent<Collider>();
            if (c != null)
            {
                Destroy(c);
            }
        }

        private void PlayDoorSfx(Door d)
        {
            var audio = ClientAudio.Instance;
            if (audio == null)
            {
                return;
            }

            var at = d.Go.transform.position + Vector3.up * 1f;
            string id = d.Kind == "hinge" ? "door_hinge" : (d.Open ? "door_slide_open" : "door_slide_close");
            audio.At(id, at, 1f, 0.85f);
        }

        /// <summary>The nearest hinge door within reach of a point (for the player's E-toggle), or 0 if none.</summary>
        public int NearestHinge(Vector3 worldPos, float reach)
        {
            int best = 0;
            float bestSq = reach * reach;
            foreach (var kv in _doors)
            {
                var d = kv.Value;
                if (d.Kind != "hinge")
                {
                    continue;
                }

                // Compare in SCENE space (seam-aware): the door's stored position is a raw world position, but
                // the caller's position is a scene position — on a longitude-wrapped world they differ by the
                // wrap offset, which made the door read as far away (so E never opened it). (B?)
                Vector3 doorScene = Game != null ? Game.ScenePos(d.World.x, d.World.y, d.World.z) : d.World;
                float sq = (doorScene - worldPos).sqrMagnitude;
                if (sq <= bestSq)
                {
                    bestSq = sq;
                    best = kv.Key;
                }
            }

            return best;
        }

        private void LateUpdate()
        {
            // Show an "E" hint over a hinge door the player can reach.
            var cam = Camera.main;
            if (cam == null || Game == null)
            {
                return;
            }

            int near = NearestHinge(Game.PlayerPosition, 3f);
            if (near != 0 && _doors.TryGetValue(near, out var d))
            {
                string hint = Game.Localizer != null ? Game.Localizer.Get("ui.door.hint") : "E: Door";
                ScreenLabelLayer.Instance.World(cam, d.Go.transform.position + Vector3.up * 2.2f, hint, UiKit.Cyan);
            }
        }

        private void OnDestroy()
        {
            if (_subscribed && Game?.Network != null)
            {
                Game.Network.DoorsReceived -= OnDoors;
            }

            if (Instance == this)
            {
                Instance = null;
            }
        }
    }
}
