using System.Collections.Generic;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Content-styled icons (Task 4) resolved by key, shared by the hotbar, the crafting/tech/ship
    /// menu and the space-systems bar so every surface shows the same art for an item / ship module /
    /// blueprint. Resolution order: a generated full-colour PNG (<c>Resources/icons/item_&lt;key&gt;.png</c>),
    /// else the in-game block atlas tile for a material that places/equals a block (a downscaled in-game
    /// texture), else null so the caller keeps its procedural / category fallback. Callers pass their
    /// <see cref="GameBootstrap"/> since content + atlas live on that instance (there is no static facade).
    /// </summary>
    public static class IconResolver
    {
        private static readonly Dictionary<string, Sprite> _sprites = new Dictionary<string, Sprite>();
        private static readonly Dictionary<string, Texture2D> _itemTex = new Dictionary<string, Texture2D>();

        /// <summary>The best icon sprite for an item / ship-module / blueprint key, or null if none.</summary>
        public static Sprite Resolve(string key, GameBootstrap game)
        {
            if (string.IsNullOrEmpty(key))
            {
                return null;
            }

            if (_sprites.TryGetValue(key, out var cached))
            {
                return cached;
            }

            // 1) A generated content icon sits in Resources/icons under an item_ prefix.
            // 2) Otherwise surface the block's atlas tile (materials/blocks reuse their in-game texture).
            var sprite = UiKit.Icon("item_" + key) ?? BlockTileSprite(key, game);
            if (sprite != null)
            {
                _sprites[key] = sprite; // cache only hits — the atlas may not be ready on an early call
            }

            return sprite;
        }

        /// <summary>The raw generated icon texture for the hotbar's <see cref="UnityEngine.UI.RawImage"/>
        /// (which draws a texture + uvRect, not a sprite); null when no PNG exists for the key.</summary>
        public static Texture2D ItemTexture(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return null;
            }

            if (_itemTex.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var tex = Resources.Load<Texture2D>("icons/item_" + key);
            _itemTex[key] = tex;
            return tex;
        }

        /// <summary>Tints toxic consumables (negative consume-health) green so poison reads at a glance;
        /// everything else stays neutral white. Applied as the icon image colour.</summary>
        public static Color Tint(string key, GameBootstrap game)
        {
            var def = game?.Content?.GetItem(key);
            return def != null && def.ConsumeHealth < 0f ? new Color(0.45f, 1f, 0.4f) : Color.white;
        }

        /// <summary>Builds a sprite from the block atlas tile for a material/block key (resolving an
        /// item's PlacesBlock so e.g. a seed shows its plant tile); null when the key isn't a block.</summary>
        private static Sprite BlockTileSprite(string key, GameBootstrap game)
        {
            if (game == null || game.Atlas == null || game.Content == null)
            {
                return null;
            }

            string blockKey = key;
            var item = game.Content.GetItem(key);
            if (item != null && !string.IsNullOrEmpty(item.PlacesBlock))
            {
                blockKey = item.PlacesBlock;
            }

            var block = game.Content.GetBlock(blockKey);
            if (block == null)
            {
                return null;
            }

            var tex = game.Atlas.Texture;
            if (tex == null)
            {
                return null;
            }

            var uv = game.Atlas.TileUv(block.NumericId.Value);
            var px = new Rect(uv.x * tex.width, uv.y * tex.height, uv.width * tex.width, uv.height * tex.height);
            return Sprite.Create(tex, px, new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
